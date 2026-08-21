using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Api.Extensions;
using Api.Infrastructure;

namespace Api.Endpoints;

public static class DebugEndpoints
{
    public static IEndpointRouteBuilder MapDebugEndpoints(
        this IEndpointRouteBuilder app,
        DatabaseOptions databaseOptions,
        IUploadRepository uploadsRepository,
        ISessionRepository sessionsRepository)
    {
        app.MapGet("/ping", () => Results.Ok("pong"));

        // GET /api/llm/ping — round-trip to model
        app.MapGet("/api/llm/ping", async (HttpContext ctx, OpenAI.Chat.ChatClient chat) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            // Some installs return ClientResult<ChatCompletion>; take .Value to get ChatCompletion
            var result = await chat.CompleteChatAsync("Reply exactly: hello from CasePilot Q&A");
            var completion = result.Value;               // <-- the key fix
            var text = completion.Content.Count > 0
                ? completion.Content[0].Text ?? ""
                : "";

            var usingOpenRouter = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
            return Results.Json(new
            {
                ok = text.Contains("hello from CasePilot Q&A"),
                reply = text,
                provider = usingOpenRouter ? "openrouter" : "openai",
                model = Environment.GetEnvironmentVariable("OPENAI_ANSWER_MODEL") ?? "gpt-5.1",
                endpoint = usingOpenRouter
                    ? Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1"
                    : "https://api.openai.com/v1"
            });
        });

        app.MapGet("/api/llm/models", async (HttpContext ctx) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;
            var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) return Results.Json(new { provider = "openai", models = Array.Empty<object>() });

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models?output_modalities=text");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var client = new HttpClient();
            using var response = await client.SendAsync(request, ctx.RequestAborted);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ctx.RequestAborted), cancellationToken: ctx.RequestAborted);
            var models = json.RootElement.GetProperty("data").EnumerateArray()
                .Select(item => new { id = item.GetProperty("id").GetString(), name = item.TryGetProperty("name", out var n) ? n.GetString() : null })
                .Where(item => !string.IsNullOrWhiteSpace(item.id))
                .OrderBy(item => item.name ?? item.id)
                .ToArray();
            return Results.Json(new { provider = "openrouter", models });
        });


        // GET /api/embeddings/ping — sanity check: returns vector length
        app.MapGet("/api/embeddings/ping", (HttpContext ctx) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

            var client = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);

            var result = client.GenerateEmbedding("hello world");  // wrapper + value
            var dims = result.Value.ToFloats().Length;            // <-- unwrap, then ToFloats()

            return Results.Json(new { dims });
        });




        app.MapGet("/me", async (HttpContext ctx, IUserRepository users) =>
        {
            var userId = ctx.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            bool tokenIsSuper = ctx.IsCurrentUserSuperUser();

            var profile = await users.GetProfileByIdAsync(userId, ctx.RequestAborted);
            if (profile is null)
            {
                // Token might be valid but user row missing; treat as unauthorized
                return Results.Unauthorized();
            }

            var isSuperUser = profile.IsSuperUser || tokenIsSuper;


            var role = isSuperUser ? "instructor" : "student";

            return Results.Ok(new
            {
                userId,
                email = profile.Email,
                fullName = profile.FullName,
                role
            });
        }).RequireAuthorization();


        app.MapGet("/debug/claims", (HttpContext ctx) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            var claims = ctx.User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Results.Ok(new
            {
                isAuthenticated = ctx.User?.Identity?.IsAuthenticated ?? false,
                name = ctx.User?.Identity?.Name,
                claims
            });
        });

        app.MapGet("/debug/routes", (HttpContext ctx, IEnumerable<EndpointDataSource> sources) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            var routes = sources
                .SelectMany(s => s.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(e => $"{string.Join(", ", e.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods ?? new[] { "ANY" })} {e.RoutePattern.RawText}")
                .Distinct()
                .OrderBy(x => x);

            return Results.Ok(routes);
        });


        app.MapGet("/debug/db-sanity", (HttpContext ctx) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            var result = new List<object>();

            try
            {
                using var conn = databaseOptions.CreateConnection();
                conn.Open();

                int uploads = -1;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Uploads;";
                    uploads = Convert.ToInt32(cmd.ExecuteScalar());
                }

                result.Add(new { kind = "configured", provider = databaseOptions.Provider, path = databaseOptions.LocalPath, uploads });
            }
            catch (Exception ex)
            {
                result.Add(new { kind = "configured", error = ex.Message });
            }

            return Results.Json(result);
        });


        app.MapGet("/debug/uploads", async (HttpContext ctx) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            var list = await uploadsRepository.ListAllAsync(ctx.RequestAborted);
            return Results.Ok(list.Select(row => new
            {
                uploadId = row.UploadId.ToString(),
                userId = row.UserId,
                name = row.Name,
                createdAt = row.CreatedAt
            }));
        });


        // ======================
        // TEMP DEBUG ENDPOINT
        // ======================
        app.MapGet("/debug/sessions", async (HttpContext ctx) =>
        {
            var deny = RequireDebugAccess(ctx);
            if (deny is not null) return deny;

            var list = await sessionsRepository.ListAllSessionsAsync(ctx.RequestAborted);
            return Results.Ok(list);
        });

        return app;
    }

    private static IResult? RequireDebugAccess(HttpContext ctx)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (!string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("ENABLE_DEBUG_ENDPOINTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!enabled)
        {
            return Results.NotFound();
        }

        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return ctx.IsCurrentUserSuperUser()
            ? null
            : Results.Forbid();
    }
}
