using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure;

public static class LoadTestTokenGenerator
{
    private const int MaximumTokenCount = 5_000;
    private const int MaximumLifetimeMinutes = 240;

    public static async Task GenerateAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ALLOW_LOAD_TEST_TOKEN_GENERATION"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Load-test token generation is disabled. Set ALLOW_LOAD_TEST_TOKEN_GENERATION=true only in an approved test environment.");
        }

        var count = ReadBoundedInt("LOAD_TEST_TOKEN_COUNT", defaultValue: 1_000, MaximumTokenCount);
        var lifetimeMinutes = ReadBoundedInt(
            "LOAD_TEST_TOKEN_LIFETIME_MINUTES",
            defaultValue: 120,
            MaximumLifetimeMinutes);
        var outputPath = Environment.GetEnvironmentVariable("LOAD_TEST_TOKEN_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("LOAD_TEST_TOKEN_OUTPUT must name a new JSON file.");

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutputPath))
            throw new InvalidOperationException($"Refusing to overwrite existing token file: {fullOutputPath}");

        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var settings = AuthSettings.Load(configuration);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSecret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(lifetimeMinutes);
        var runId = Guid.NewGuid().ToString("N")[..12];
        var handler = new JwtSecurityTokenHandler();
        var tokens = new string[count];

        for (var index = 0; index < count; index++)
        {
            var subject = $"loadtest-{runId}-{index + 1:D5}";
            var token = new JwtSecurityToken(
                issuer: settings.JwtIssuer,
                audience: settings.JwtAudience,
                claims:
                [
                    new Claim("sub", subject),
                    new Claim("email", $"{subject}@invalid.example"),
                    new Claim("role", "student"),
                    new Claim("isSuperUser", "false"),
                    new Claim("load_test", "true")
                ],
                notBefore: now,
                expires: expiresAt,
                signingCredentials: credentials);
            tokens[index] = handler.WriteToken(token);
        }

        await using (var stream = new FileStream(
                         fullOutputPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 64 * 1024,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new LoadTestTokenFile(
                    GeneratedAtUtc: now,
                    ExpiresAtUtc: expiresAt,
                    Count: count,
                    Tokens: tokens),
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                fullOutputPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Console.WriteLine(
            $"[LOAD TEST] Generated {count} unique tokens at {fullOutputPath}; they expire at {expiresAt:O}.");
    }

    private static int ReadBoundedInt(string variableName, int defaultValue, int maximum)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        if (!int.TryParse(raw, out var value) || value <= 0 || value > maximum)
            throw new InvalidOperationException($"{variableName} must be between 1 and {maximum}.");
        return value;
    }

    private sealed record LoadTestTokenFile(
        DateTime GeneratedAtUtc,
        DateTime ExpiresAtUtc,
        int Count,
        IReadOnlyList<string> Tokens);
}
