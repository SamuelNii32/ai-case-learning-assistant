using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;

namespace Api.Endpoints;

public static class DebugEndpoints
{
    public static IEndpointRouteBuilder MapDebugEndpoints(
        this IEndpointRouteBuilder app,
        string connString)
    {
        app.MapGet("/ping", () => Results.Ok("pong"));

        // GET /api/llm/ping — round-trip to model
        app.MapGet("/api/llm/ping", async (OpenAI.Chat.ChatClient chat) =>
        {
            // Some installs return ClientResult<ChatCompletion>; take .Value to get ChatCompletion
            var result = await chat.CompleteChatAsync("Reply exactly: hello from CasePilot Q&A");
            var completion = result.Value;               // <-- the key fix
            var text = completion.Content.Count > 0
                ? completion.Content[0].Text ?? ""
                : "";

            return Results.Json(new { ok = text.Contains("hello from CasePilot Q&A"), reply = text });
        });


        // GET /api/embeddings/ping — sanity check: returns vector length
        app.MapGet("/api/embeddings/ping", () =>
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

            var client = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);

            var result = client.GenerateEmbedding("hello world");  // wrapper + value
            var dims = result.Value.ToFloats().Length;            // <-- unwrap, then ToFloats()

            return Results.Json(new { dims });
        });




        app.MapGet("/me", async (HttpContext ctx) =>
        {
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            bool tokenIsSuper =
                ctx.Items.TryGetValue("isSuperUser", out var isSuperObj) &&
                isSuperObj is bool b && b;

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT Email,
               IFNULL(FullName, ''),
               IFNULL(IsSuperUser, 0)
        FROM Users
        WHERE Id = $id
        LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", userId);

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                // Token might be valid but user row missing; treat as unauthorized
                return Results.Unauthorized();
            }

            var email = r.GetString(0);
            var fullName = r.GetString(1);
            var dbIsSuper = r.GetInt32(2) != 0;


            var isSuperUser = dbIsSuper || tokenIsSuper;


            var role = isSuperUser ? "instructor" : "student";

            return Results.Ok(new
            {
                userId,
                email,
                fullName,
                role
            });
        });


        app.MapGet("/debug/claims", (HttpContext ctx) =>
        {
            var claims = ctx.User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Results.Ok(new
            {
                isAuthenticated = ctx.User?.Identity?.IsAuthenticated ?? false,
                name = ctx.User?.Identity?.Name,
                claims
            });
        });

        app.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> sources) =>
        {
            var routes = sources
                .SelectMany(s => s.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(e => $"{string.Join(", ", e.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods ?? new[] { "ANY" })} {e.RoutePattern.RawText}")
                .Distinct()
                .OrderBy(x => x);

            return Results.Ok(routes);
        }).AllowAnonymous();


        app.MapGet("/debug/db-sanity", () =>
        {
            var result = new List<object>();

            // A) Connection using connString (startup DB)
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                conn.Open();

                string? path = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA database_list;";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        // columns: seq, name, file
                        var name = reader.GetString(1);
                        if (name == "main")
                        {
                            path = reader.GetString(2);
                            break;
                        }
                    }
                }

                int uploads = -1;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Uploads;";
                    uploads = Convert.ToInt32(cmd.ExecuteScalar());
                }

                result.Add(new { kind = "connString", path, uploads });
            }
            catch (Exception ex)
            {
                result.Add(new { kind = "connString", error = ex.Message });
            }

            // B) Connection using literal "ingestion.db"
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                conn.Open();

                string? path = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA database_list;";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var name = reader.GetString(1);
                        if (name == "main")
                        {
                            path = reader.GetString(2);
                            break;
                        }
                    }
                }

                int uploads = -1;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM Uploads;";
                    uploads = Convert.ToInt32(cmd.ExecuteScalar());
                }

                result.Add(new { kind = "literal", path, uploads });
            }
            catch (Exception ex)
            {
                result.Add(new { kind = "literal", error = ex.Message });
            }

            return Results.Json(result);
        });


        app.MapGet("/debug/uploads", async (HttpContext ctx) =>
        {
            using var conn = new SqliteConnection(connString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT UploadId, UserId, Name, CreatedAt
        FROM Uploads;
    ";

            var list = new List<object>();

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    list.Add(new
                    {
                        uploadId = reader.IsDBNull(0) ? null : reader.GetString(0),
                        userId = reader.IsDBNull(1) ? null : reader.GetString(1),
                        name = reader.IsDBNull(2) ? null : reader.GetString(2),
                        createdAt = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
                }
            }

            return Results.Ok(list);
        });


        // ======================
        // TEMP DEBUG ENDPOINT
        // ======================
        app.MapGet("/debug/sessions", async () =>
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, UserId, UploadId, ClassId, CreatedAt FROM Sessions";

            var list = new List<object>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    list.Add(new
                    {
                        sessionId = reader.GetString(0),
                        userId = reader.GetString(1),
                        uploadId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        classId = reader.IsDBNull(3) ? null : reader.GetString(3),
                        createdAt = reader.GetString(4)
                    });
                }
            }

            return Results.Ok(list);
        });

        return app;
    }
}