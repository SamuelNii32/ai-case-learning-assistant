using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Api.Infrastructure;

public sealed record UploadRateLimitSettings(int PermitLimit, TimeSpan Window)
{
    public static UploadRateLimitSettings Default { get; } = new(8, TimeSpan.FromMinutes(10));

    public static UploadRateLimitSettings Load(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() ||
            !configuration.GetValue<bool>("RateLimiting:Upload:DevelopmentOverride:Enabled"))
        {
            return Default;
        }

        var permitLimit = configuration.GetValue<int?>(
            "RateLimiting:Upload:DevelopmentOverride:PermitLimit") ?? Default.PermitLimit;
        var windowSeconds = configuration.GetValue<int?>(
            "RateLimiting:Upload:DevelopmentOverride:WindowSeconds") ?? (int)Default.Window.TotalSeconds;

        if (permitLimit <= 0)
            throw new InvalidOperationException("The development upload rate-limit PermitLimit must be greater than zero.");
        if (windowSeconds <= 0)
            throw new InvalidOperationException("The development upload rate-limit WindowSeconds must be greater than zero.");

        return new UploadRateLimitSettings(permitLimit, TimeSpan.FromSeconds(windowSeconds));
    }
}

public sealed record GlobalRateLimitSettings(int PermitLimit, TimeSpan Window)
{
    public static GlobalRateLimitSettings Default { get; } = new(180, TimeSpan.FromMinutes(1));

    public static GlobalRateLimitSettings Load(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() ||
            !configuration.GetValue<bool>("RateLimiting:Global:DevelopmentOverride:Enabled"))
        {
            return Default;
        }

        var permitLimit = configuration.GetValue<int?>(
            "RateLimiting:Global:DevelopmentOverride:PermitLimit") ?? Default.PermitLimit;
        var windowSeconds = configuration.GetValue<int?>(
            "RateLimiting:Global:DevelopmentOverride:WindowSeconds") ?? (int)Default.Window.TotalSeconds;

        if (permitLimit <= 0)
            throw new InvalidOperationException("The development global rate-limit PermitLimit must be greater than zero.");
        if (windowSeconds <= 0)
            throw new InvalidOperationException("The development global rate-limit WindowSeconds must be greater than zero.");

        return new GlobalRateLimitSettings(permitLimit, TimeSpan.FromSeconds(windowSeconds));
    }
}
