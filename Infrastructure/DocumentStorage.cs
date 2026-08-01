using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Api.Infrastructure;

public sealed record StoredSummaryFile(string UploadId, string Json, DateTime LastModifiedUtc);

public static class DocumentArtifactSuffixes
{
    public const string Summary = ".summary.json";
    public const string Index = ".index.json";
    public const string Layout = ".layout.json";
    public const string DocumentType = ".doctype.json";

    public static readonly string[] All = { ".pdf", Summary, Index, Layout, DocumentType };

    public static void EnsureSupported(string suffix)
    {
        if (!All.Contains(suffix, StringComparer.Ordinal))
            throw new ArgumentException($"Unsupported document artifact suffix '{suffix}'.", nameof(suffix));
    }
}

public interface IDocumentStorage
{
    Task<string> SavePdfAsync(Guid uploadId, IFormFile file, CancellationToken cancellationToken = default);
    Task<string?> GetPdfPathAsync(Guid uploadId, CancellationToken cancellationToken = default);
    Task<bool> PdfExistsAsync(Guid uploadId, CancellationToken cancellationToken = default);
    Task WriteJsonAsync(Guid uploadId, string suffix, object value, CancellationToken cancellationToken = default);
    Task WriteTextAsync(Guid uploadId, string suffix, string content, CancellationToken cancellationToken = default);
    Task<string?> ReadTextAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default);
    IAsyncEnumerable<StoredSummaryFile> EnumerateSummariesAsync(CancellationToken cancellationToken = default);
    Task DeleteArtifactsAsync(Guid uploadId, CancellationToken cancellationToken = default);
}

public sealed class LocalDocumentStorage : IDocumentStorage
{
    private readonly string _uploadsRoot;

    public LocalDocumentStorage(IHostEnvironment env)
    {
        _uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(_uploadsRoot);
    }

    public async Task<string> SavePdfAsync(Guid uploadId, IFormFile file, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_uploadsRoot);
        var path = GetPath(uploadId, ".pdf");

        await using var outStream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await file.CopyToAsync(outStream, cancellationToken);
        return path;
    }

    public Task<string?> GetPdfPathAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(uploadId, ".pdf");
        return Task.FromResult(File.Exists(path) ? path : null);
    }

    public Task<bool> PdfExistsAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(GetPath(uploadId, ".pdf")));
    }

    public async Task WriteJsonAsync(Guid uploadId, string suffix, object value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value);
        await WriteTextAsync(uploadId, suffix, json, cancellationToken);
    }

    public async Task WriteTextAsync(Guid uploadId, string suffix, string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_uploadsRoot);
        await File.WriteAllTextAsync(GetPath(uploadId, suffix), content, cancellationToken);
    }

    public async Task<string?> ReadTextAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default)
    {
        var path = GetPath(uploadId, suffix);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : null;
    }

    public Task<bool> ExistsAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(GetPath(uploadId, suffix)));
    }

    public async IAsyncEnumerable<StoredSummaryFile> EnumerateSummariesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_uploadsRoot);

        foreach (var path in Directory.EnumerateFiles(_uploadsRoot, "*.summary.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uploadId = Path.GetFileName(path).Replace(".summary.json", "", StringComparison.OrdinalIgnoreCase);
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            yield return new StoredSummaryFile(uploadId, json, File.GetLastWriteTimeUtc(path));
        }
    }

    public Task DeleteArtifactsAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        foreach (var suffix in DocumentArtifactSuffixes.All)
        {
            var path = GetPath(uploadId, suffix);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup; DB ownership deletion is handled separately.
            }
        }

        return Task.CompletedTask;
    }

    private string GetPath(Guid uploadId, string suffix)
    {
        DocumentArtifactSuffixes.EnsureSupported(suffix);
        return Path.Combine(_uploadsRoot, $"{uploadId}{suffix}");
    }
}

public sealed class AzureBlobDocumentStorage : IDocumentStorage
{
    private readonly BlobContainerClient _container;
    private readonly string _cacheRoot;

    public AzureBlobDocumentStorage(IConfiguration configuration, IHostEnvironment env)
    {
        var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? configuration["AzureBlobStorage:ConnectionString"];
        var containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER")
            ?? configuration["AzureBlobStorage:Container"]
            ?? "documents";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING is required when DOCUMENT_STORAGE_PROVIDER=azureblob.");
        }

        _container = new BlobContainerClient(connectionString, containerName);
        _cacheRoot = Path.Combine(Path.GetTempPath(), "casepilot-document-cache", env.EnvironmentName);
        Directory.CreateDirectory(_cacheRoot);
    }

    public async Task<string> SavePdfAsync(Guid uploadId, IFormFile file, CancellationToken cancellationToken = default)
    {
        var localPath = GetCachePath(uploadId, ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using (var output = File.Open(localPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await file.CopyToAsync(output, cancellationToken);
        }

        await EnsureContainerAsync(cancellationToken);
        var blob = _container.GetBlobClient(GetBlobName(uploadId, ".pdf"));
        await using var input = File.OpenRead(localPath);
        await blob.UploadAsync(input, overwrite: true, cancellationToken);
        return localPath;
    }

    public async Task<string?> GetPdfPathAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(GetBlobName(uploadId, ".pdf"));
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var localPath = GetCachePath(uploadId, ".pdf");
        if (File.Exists(localPath))
        {
            return localPath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await blob.DownloadToAsync(localPath, cancellationToken);
        return localPath;
    }

    public async Task<bool> PdfExistsAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        return await _container.GetBlobClient(GetBlobName(uploadId, ".pdf")).ExistsAsync(cancellationToken);
    }

    public async Task WriteJsonAsync(Guid uploadId, string suffix, object value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value);
        await WriteTextAsync(uploadId, suffix, json, cancellationToken);
    }

    public async Task WriteTextAsync(Guid uploadId, string suffix, string content, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        var blob = _container.GetBlobClient(GetBlobName(uploadId, suffix));
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken);

        var localPath = GetCachePath(uploadId, suffix);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, content, cancellationToken);
    }

    public async Task<string?> ReadTextAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(GetBlobName(uploadId, suffix));
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var result = await blob.DownloadContentAsync(cancellationToken);
        return result.Value.Content.ToString();
    }

    public async Task<bool> ExistsAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default)
    {
        return await _container.GetBlobClient(GetBlobName(uploadId, suffix)).ExistsAsync(cancellationToken);
    }

    public async IAsyncEnumerable<StoredSummaryFile> EnumerateSummariesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _container.GetBlobsAsync(
            BlobTraits.None,
            BlobStates.None,
            prefix: "uploads/",
            cancellationToken: cancellationToken))
        {
            if (!item.Name.EndsWith(".summary.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var uploadId = Path.GetFileName(item.Name).Replace(".summary.json", "", StringComparison.OrdinalIgnoreCase);
            var blob = _container.GetBlobClient(item.Name);
            var result = await blob.DownloadContentAsync(cancellationToken);
            yield return new StoredSummaryFile(
                uploadId,
                result.Value.Content.ToString(),
                item.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow);
        }
    }

    public async Task DeleteArtifactsAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        foreach (var suffix in DocumentArtifactSuffixes.All)
        {
            await _container
                .GetBlobClient(GetBlobName(uploadId, suffix))
                .DeleteIfExistsAsync(cancellationToken: cancellationToken);

            var localPath = GetCachePath(uploadId, suffix);
            try
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }
            }
            catch
            {
                // Best-effort cache cleanup.
            }
        }
    }

    private static string GetBlobName(Guid uploadId, string suffix)
    {
        DocumentArtifactSuffixes.EnsureSupported(suffix);
        return $"uploads/{uploadId}{suffix}";
    }

    private string GetCachePath(Guid uploadId, string suffix)
    {
        return Path.Combine(_cacheRoot, $"{uploadId}{suffix}");
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}

