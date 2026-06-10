using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app,
        string connString,
        string jwtSecret,
        string jwtIssuer,
        string jwtAudience)
    {
        // endpoints will go here

        // --- Auth: signup (create user) ---
        app.MapPost("/auth/signup", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();

            string email = "", password = "", fullName = "", instructorInviteCode = "";

            try
            {
                var obj = System.Text.Json.JsonDocument.Parse(body).RootElement;
                if (obj.TryGetProperty("email", out var e))
                    email = (e.GetString() ?? "").Trim().ToLowerInvariant();

                if (obj.TryGetProperty("password", out var p))
                    password = p.GetString() ?? "";

                if (obj.TryGetProperty("fullName", out var n))
                    fullName = (n.GetString() ?? "").Trim();

                if (obj.TryGetProperty("instructorInviteCode", out var invite))
                    instructorInviteCode = (invite.GetString() ?? "").Trim();
            }
            catch
            {
                // bad JSON → will fail validation below
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return Results.BadRequest(new { error = "email and password required" });

            if (!IsValidEmail(email))
                return Results.BadRequest(new { error = "Enter a valid email address" });

            if (password.Length < 8)
                return Results.BadRequest(new { error = "password must be at least 8 characters" });

            var userId = Guid.NewGuid().ToString("N");
            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            var isInstructor = IsValidInstructorInviteCode(instructorInviteCode);

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
            await conn.OpenAsync();

            // Enforce unique email
            var check = conn.CreateCommand();
            check.CommandText = "SELECT 1 FROM Users WHERE Email = $e LIMIT 1";
            check.Parameters.AddWithValue("$e", email);
            var exists = (await check.ExecuteScalarAsync()) != null;
            if (exists)
                return Results.Conflict(new { error = "email already exists" });

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        INSERT INTO Users (Id, Email, PasswordHash, FullName, CreatedAt, IsSuperUser)
        VALUES ($id,$e,$h,$n,$t,$su)";
            cmd.Parameters.AddWithValue("$id", userId);
            cmd.Parameters.AddWithValue("$e", email);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$n", string.IsNullOrWhiteSpace(fullName) ? DBNull.Value : fullName);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$su", isInstructor ? 1 : 0);

            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new { userId, email, fullName, role = isInstructor ? "instructor" : "student" });

        });


        app.MapPost("/auth/login", async (HttpContext ctx) =>
        {
            // ⭐ Allow body to be read multiple times
            ctx.Request.EnableBuffering();
            ctx.Request.Body.Position = 0;

            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            // ⭐ Reset again so downstream can read body if needed
            ctx.Request.Body.Position = 0;

            string email = "", password = "";
            try
            {
                var obj = System.Text.Json.JsonDocument.Parse(body).RootElement;
                if (obj.TryGetProperty("email", out var e)) email = (e.GetString() ?? "").Trim().ToLowerInvariant();
                if (obj.TryGetProperty("password", out var p)) password = p.GetString() ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return Results.BadRequest(new { error = "email and password required" });

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
            await conn.OpenAsync();

            string? userId = null, hash = null, fullName = null;
            bool isSuperUser = false;
            int rawIsSuperUser = 0;

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, PasswordHash, IFNULL(FullName,''), IFNULL(IsSuperUser,0) FROM Users WHERE Email = $e LIMIT 1";
            cmd.Parameters.AddWithValue("$e", email);
            using (var r = await cmd.ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    userId = r.GetString(0);
                    hash = r.GetString(1);
                    fullName = r.GetString(2);
                    rawIsSuperUser = r.GetInt32(3);
                    isSuperUser = rawIsSuperUser != 0;
                }
            }
            if (userId is null || hash is null || !BCrypt.Net.BCrypt.Verify(password, hash))
                return Results.Unauthorized();

            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSecret));

            var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key,
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
            );

            var claims = new[]
        {
    new System.Security.Claims.Claim("sub", userId),
    new System.Security.Claims.Claim("email", email),
    new System.Security.Claims.Claim("isSuperUser", isSuperUser ? "true" : "false"),
    new System.Security.Claims.Claim("role", isSuperUser ? "instructor" : "student"),
};

            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: creds
            );

            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .WriteToken(token);

            return Results.Ok(new { token = jwt, userId, email, fullName, isSuperUser });
        });

        return app;
    }

    private static bool IsValidInstructorInviteCode(string inviteCode)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return false;
        }

        var configured = Environment.GetEnvironmentVariable("INSTRUCTOR_INVITE_CODES") ?? "";
        var validCodes = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return validCodes.Any(code => string.Equals(code, inviteCode, StringComparison.Ordinal));
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return false;
        }

        try
        {
            var parsed = new System.Net.Mail.MailAddress(email);
            if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var domain = parsed.Host;
            return domain.Contains('.') &&
                   !domain.StartsWith(".", StringComparison.Ordinal) &&
                   !domain.EndsWith(".", StringComparison.Ordinal) &&
                   domain.Split('.', StringSplitOptions.RemoveEmptyEntries).All(part => part.Length > 0);
        }
        catch
        {
            return false;
        }
    }
}
