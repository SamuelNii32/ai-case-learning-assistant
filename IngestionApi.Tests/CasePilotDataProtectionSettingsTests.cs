using Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IngestionApi.Tests;

public sealed class CasePilotDataProtectionSettingsTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    [Fact]
    public void Production_auth_requires_shared_azure_key_ring()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CasePilotDataProtectionSettings.Load(
                Configuration(), Environment(Environments.Production), true, NoEnvironment));

        Assert.Contains("Production Auth0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_key_ring_requires_blob_and_key_vault_together()
    {
        var values = new Dictionary<string, string?>
        {
            ["DataProtection:BlobUri"] = "https://storage.example/container/keys.xml"
        };

        Assert.Throws<InvalidOperationException>(() =>
            CasePilotDataProtectionSettings.Load(
                Configuration(values), Environment(Environments.Development), false, NoEnvironment));
    }

    [Fact]
    public void Azure_key_ring_rejects_sas_and_versioned_key_identifiers()
    {
        var sas = new Dictionary<string, string?>
        {
            ["DataProtection:BlobUri"] = "https://storage.example/container/keys.xml?sig=secret",
            ["DataProtection:KeyVaultKeyIdentifier"] = "https://vault.vault.azure.net/keys/casepilot"
        };
        Assert.Throws<InvalidOperationException>(() =>
            CasePilotDataProtectionSettings.Load(
                Configuration(sas), Environment(Environments.Development), false, NoEnvironment));

        var versioned = new Dictionary<string, string?>
        {
            ["DataProtection:BlobUri"] = "https://storage.example/container/keys.xml",
            ["DataProtection:KeyVaultKeyIdentifier"] = "https://vault.vault.azure.net/keys/casepilot/version"
        };
        Assert.Throws<InvalidOperationException>(() =>
            CasePilotDataProtectionSettings.Load(
                Configuration(versioned), Environment(Environments.Development), false, NoEnvironment));
    }

    [Fact]
    public void Production_auth_accepts_managed_identity_azure_configuration()
    {
        var values = new Dictionary<string, string?>
        {
            ["DataProtection:BlobUri"] = "https://storage.example/container/keys.xml",
            ["DataProtection:KeyVaultKeyIdentifier"] = "https://vault.vault.azure.net/keys/casepilot",
            ["DataProtection:ManagedIdentityClientId"] = "managed-identity-client-id"
        };

        var settings = CasePilotDataProtectionSettings.Load(
            Configuration(values), Environment(Environments.Production), true, NoEnvironment);

        Assert.True(settings.UsesAzure);
        Assert.Equal("managed-identity-client-id", settings.ManagedIdentityClientId);
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment
    {
        EnvironmentName = name
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CasePilot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
