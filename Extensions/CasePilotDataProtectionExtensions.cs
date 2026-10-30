using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace Api.Extensions;

public sealed record CasePilotDataProtectionSettings(
    string FileSystemPath,
    Uri? BlobUri,
    Uri? KeyVaultKeyIdentifier,
    string ManagedIdentityClientId)
{
    public bool UsesAzure => BlobUri is not null && KeyVaultKeyIdentifier is not null;

    public static CasePilotDataProtectionSettings Load(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool productionAuthEnabled,
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??= Environment.GetEnvironmentVariable;
        string Read(string variable, string key) =>
            (environmentReader(variable) ?? configuration[key] ?? "").Trim();

        var filePath = Read("DATA_PROTECTION_KEYS_PATH", "DataProtection:KeysPath");
        var blobText = Read("DATA_PROTECTION_BLOB_URI", "DataProtection:BlobUri");
        var keyText = Read("DATA_PROTECTION_KEY_VAULT_KEY_ID", "DataProtection:KeyVaultKeyIdentifier");
        var clientId = Read("AZURE_CLIENT_ID", "DataProtection:ManagedIdentityClientId");

        if (string.IsNullOrWhiteSpace(blobText) != string.IsNullOrWhiteSpace(keyText))
        {
            throw new InvalidOperationException(
                "DATA_PROTECTION_BLOB_URI and DATA_PROTECTION_KEY_VAULT_KEY_ID must be configured together.");
        }

        Uri? blobUri = null;
        Uri? keyUri = null;
        if (!string.IsNullOrWhiteSpace(blobText))
        {
            blobUri = RequireHttpsUri(blobText, "DATA_PROTECTION_BLOB_URI");
            if (!string.IsNullOrEmpty(blobUri.Query))
                throw new InvalidOperationException("DATA_PROTECTION_BLOB_URI must not contain a SAS token; use managed identity.");

            keyUri = RequireHttpsUri(keyText, "DATA_PROTECTION_KEY_VAULT_KEY_ID");
            var segments = keyUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2 || !string.Equals(segments[0], "keys", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "DATA_PROTECTION_KEY_VAULT_KEY_ID must be a versionless Key Vault key URI ending in /keys/{name}.");
        }

        if (environment.IsProduction() && productionAuthEnabled && (blobUri is null || keyUri is null))
        {
            throw new InvalidOperationException(
                "Production Auth0 requires Azure Blob and Key Vault Data Protection configuration.");
        }

        return new CasePilotDataProtectionSettings(filePath, blobUri, keyUri, clientId);
    }

    private static Uri RequireHttpsUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{name} must be an absolute HTTPS URI.");
        return uri;
    }
}

public static class CasePilotDataProtectionExtensions
{
    public static IServiceCollection AddCasePilotDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool productionAuthEnabled)
    {
        var settings = CasePilotDataProtectionSettings.Load(
            configuration, environment, productionAuthEnabled);
        services.AddSingleton(settings);

        var builder = services.AddDataProtection().SetApplicationName("CasePilot");
        if (settings.UsesAzure)
        {
            var credentialOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(settings.ManagedIdentityClientId))
                credentialOptions.ManagedIdentityClientId = settings.ManagedIdentityClientId;
            TokenCredential credential = new DefaultAzureCredential(credentialOptions);
            builder
                .PersistKeysToAzureBlobStorage(settings.BlobUri!, credential)
                .ProtectKeysWithAzureKeyVault(settings.KeyVaultKeyIdentifier!, credential);
        }
        else if (!string.IsNullOrWhiteSpace(settings.FileSystemPath))
        {
            Directory.CreateDirectory(settings.FileSystemPath);
            builder.PersistKeysToFileSystem(new DirectoryInfo(settings.FileSystemPath));
        }

        return services;
    }
}
