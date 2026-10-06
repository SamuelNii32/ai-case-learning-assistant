using Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IngestionApi.Tests;

public sealed class UploadRateLimitSettingsTests
{
    [Fact]
    public void Load_UsesProductionDefaultsEvenWhenOverrideIsConfigured()
    {
        var configuration = CreateConfiguration(enabled: true, permitLimit: 500, windowSeconds: 30);

        var settings = UploadRateLimitSettings.Load(configuration, new TestHostEnvironment("Production"));

        Assert.Equal(8, settings.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.Window);
    }

    [Fact]
    public void Load_UsesDefaultsInDevelopmentWhenOverrideIsNotEnabled()
    {
        var configuration = CreateConfiguration(enabled: false, permitLimit: 500, windowSeconds: 30);

        var settings = UploadRateLimitSettings.Load(configuration, new TestHostEnvironment("Development"));

        Assert.Equal(8, settings.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.Window);
    }

    [Fact]
    public void Load_AppliesEnabledDevelopmentOverride()
    {
        var configuration = CreateConfiguration(enabled: true, permitLimit: 500, windowSeconds: 30);

        var settings = UploadRateLimitSettings.Load(configuration, new TestHostEnvironment("Development"));

        Assert.Equal(500, settings.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.Window);
    }

    [Fact]
    public void GlobalLoad_UsesProductionDefaultsEvenWhenOverrideIsConfigured()
    {
        var configuration = CreateGlobalConfiguration(enabled: true, permitLimit: 500, windowSeconds: 30);

        var settings = GlobalRateLimitSettings.Load(configuration, new TestHostEnvironment("Production"));

        Assert.Equal(180, settings.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.Window);
    }

    [Fact]
    public void GlobalLoad_UsesDefaultsInDevelopmentWhenOverrideIsNotEnabled()
    {
        var configuration = CreateGlobalConfiguration(enabled: false, permitLimit: 500, windowSeconds: 30);

        var settings = GlobalRateLimitSettings.Load(configuration, new TestHostEnvironment("Development"));

        Assert.Equal(180, settings.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.Window);
    }

    [Fact]
    public void GlobalLoad_AppliesEnabledDevelopmentOverride()
    {
        var configuration = CreateGlobalConfiguration(enabled: true, permitLimit: 500, windowSeconds: 30);

        var settings = GlobalRateLimitSettings.Load(configuration, new TestHostEnvironment("Development"));

        Assert.Equal(500, settings.PermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.Window);
    }

    private static IConfiguration CreateConfiguration(bool enabled, int permitLimit, int windowSeconds) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Upload:DevelopmentOverride:Enabled"] = enabled.ToString(),
                ["RateLimiting:Upload:DevelopmentOverride:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:Upload:DevelopmentOverride:WindowSeconds"] = windowSeconds.ToString()
            })
            .Build();

    private static IConfiguration CreateGlobalConfiguration(bool enabled, int permitLimit, int windowSeconds) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Global:DevelopmentOverride:Enabled"] = enabled.ToString(),
                ["RateLimiting:Global:DevelopmentOverride:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:Global:DevelopmentOverride:WindowSeconds"] = windowSeconds.ToString()
            })
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "IngestionApi.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
