using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

public static class WebApplicationExtensions
{
    public static WebApplication UseAppPipeline(this WebApplication app)
    {
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

        app.UseCors("FrontendDev");
        app.MapHealthChecks("/healthz").AllowAnonymous();
        if (app.Environment.IsProduction())
        {
            app.UseHttpsRedirection();
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
