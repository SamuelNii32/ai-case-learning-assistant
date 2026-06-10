using Microsoft.Extensions.Configuration;

namespace Api.Extensions;

public sealed record AuthSettings(string JwtSecret, string JwtIssuer, string JwtAudience)
{
    public static AuthSettings Load(IConfiguration configuration)
    {
        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var isProduction = string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase);

        var secret =
            Environment.GetEnvironmentVariable("JWT_SECRET") ??
            configuration["Jwt:Secret"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            if (isProduction)
            {
                throw new InvalidOperationException("JWT_SECRET must be configured in production.");
            }

            secret = "dev_only_change_me_32_chars_minimum_secret";
        }

        if (secret.Length < 32)
        {
            throw new InvalidOperationException("JWT_SECRET must be at least 32 characters.");
        }

        var issuer =
            Environment.GetEnvironmentVariable("JWT_ISSUER") ??
            configuration["Jwt:Issuer"] ??
            "IngestionApi";

        var audience =
            Environment.GetEnvironmentVariable("JWT_AUDIENCE") ??
            configuration["Jwt:Audience"] ??
            "IngestionClient";

        return new AuthSettings(secret, issuer, audience);
    }
}
