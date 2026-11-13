using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Api.Infrastructure;
using Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseAppPipeline(this WebApplication app)
    {
        var trustForwardedHeaders = string.Equals(
            Environment.GetEnvironmentVariable("TRUST_FORWARDED_HEADERS")
                ?? app.Configuration["Proxy:TrustForwardedHeaders"]
                ?? Environment.GetEnvironmentVariable("RENDER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (trustForwardedHeaders)
        {
            var expectedFrontDoorId = (Environment.GetEnvironmentVariable("AZURE_FRONT_DOOR_ID")
                ?? app.Configuration["Proxy:AzureFrontDoorId"] ?? "").Trim();
            if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(expectedFrontDoorId))
                throw new InvalidOperationException(
                    "AZURE_FRONT_DOOR_ID is required when forwarded headers are trusted in Production.");

            if (!string.IsNullOrWhiteSpace(expectedFrontDoorId))
            {
                app.Use(async (context, next) =>
                {
                    if (!string.Equals(
                            context.Request.Headers["X-Azure-FDID"].FirstOrDefault(),
                            expectedFrontDoorId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                    await next();
                });
            }

            var forwardedHeaders = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = 1
            };
            // Render sets RENDER=true and exposes the service port only through its managed
            // reverse proxy. Other platforms must opt in explicitly after verifying the same.
            forwardedHeaders.KnownIPNetworks.Clear();
            forwardedHeaders.KnownProxies.Clear();
            app.UseForwardedHeaders(forwardedHeaders);
        }

        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1", out var remaining))
            {
                context.Request.Path = remaining;
                context.Response.Headers["X-API-Version"] = "v1";
            }
            else if (context.Request.Path.StartsWithSegments("/api", out remaining) && remaining.StartsWithSegments("/v1", out var apiV1Remaining))
            {
                context.Request.Path = apiV1Remaining;
                context.Response.Headers["X-API-Version"] = "v1";
            }

            return next();
        });

        app.UseMiddleware<RequestTelemetryMiddleware>();

        app.UseExceptionHandler(handlerApp =>
        {
            handlerApp.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerPathFeature>();
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalException");
                logger.LogError(feature?.Error, "Unhandled exception for {Path}", feature?.Path ?? context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = app.Environment.IsDevelopment() ? feature?.Error.Message : null,
                    Instance = context.Request.Path
                };

                await context.Response.WriteAsJsonAsync(problem);
            });
        });

        app.Use(async (context, next) =>
        {
            if (!context.Response.Headers.ContainsKey("X-Request-Id"))
            {
                context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
            }

            await next();
        });

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.User.Identity?.IsAuthenticated == true
                    || context.Request.Path.StartsWithSegments("/bff")
                    || context.Request.Path.StartsWithSegments("/signin-auth0")
                    || context.Request.Path.StartsWithSegments("/signout-callback-auth0"))
                {
                    context.Response.Headers.CacheControl = "no-store, max-age=0";
                    context.Response.Headers.Pragma = "no-cache";
                    context.Response.Headers.Vary = "Cookie";
                }
                return Task.CompletedTask;
            });
            await next();
        });

        app.UseCors("FrontendDev");
        app.MapHealthChecks("/healthz", HealthResponseWriter.LivenessOptions)
            .AllowAnonymous()
            .DisableRateLimiting();
        app.MapHealthChecks("/health/live", HealthResponseWriter.LivenessOptions)
            .AllowAnonymous()
            .DisableRateLimiting();
        app.MapHealthChecks("/health/ready", HealthResponseWriter.ReadinessOptions)
            .AllowAnonymous()
            .DisableRateLimiting();
        if (app.Environment.IsProduction())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // Serve only recognized React document navigations before endpoint
        // authorization. This resolves intentional route overlaps such as
        // /cases and /admin/sessions without turning unknown API paths or Auth0
        // callbacks into successful index.html responses.
        app.UseCombinedSpaNavigationFallback(app.Environment);

        app.UseAuthentication();
        app.UseMiddleware<CasePilotAuthenticationBridgeMiddleware>();
        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint();
            var policyName = endpoint is null ? "global" : endpoint.DisplayName ?? "endpoint";
            var partition = context.User?.FindFirst("sub")?.Value is { Length: > 0 } value
                ? $"user:{value}"
                : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RateLimiterDiagnostics");

            logger.LogInformation(
                "Rate limiter pre-check path={Path} method={Method} limiter={Limiter} partition={Partition} userAuthenticated={UserAuthenticated}",
                context.Request.Path.Value ?? "<unknown>",
                context.Request.Method,
                policyName,
                partition,
                context.User?.Identity?.IsAuthenticated == true);

            await next();
        });
        app.UseRedisRateLimiter();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }
}
