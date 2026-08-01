namespace Api.Infrastructure;

public sealed record DocumentStorageMigrationResult(int PdfsCopied, int JsonArtifactsCopied, int FilesSkipped);

public static class DocumentStorageMigrator
{
    private static readonly string[] JsonSuffixes =
    {
        DocumentArtifactSuffixes.Summary,
        DocumentArtifactSuffixes.Index,
        DocumentArtifactSuffixes.Layout,
        DocumentArtifactSuffixes.DocumentType
    };

    public static async Task<DocumentStorageMigrationResult> MigrateLocalToAzureAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var uploadsRoot = Path.Combine(environment.ContentRootPath, "uploads");
        if (!Directory.Exists(uploadsRoot))
            return new DocumentStorageMigrationResult(0, 0, 0);

        var azureStorage = new AzureBlobDocumentStorage(configuration, environment);
        var pdfsCopied = 0;
        var jsonArtifactsCopied = 0;
        var filesSkipped = 0;

        foreach (var path in Directory.EnumerateFiles(uploadsRoot, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var uploadId))
            {
                filesSkipped++;
                continue;
            }

            await using var stream = File.OpenRead(path);
            var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(path));
            await azureStorage.SavePdfAsync(uploadId, formFile, cancellationToken);
            pdfsCopied++;
        }

        foreach (var path in Directory.EnumerateFiles(uploadsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveJsonArtifact(path, out var uploadId, out var suffix))
            {
                filesSkipped++;
                continue;
            }

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            await azureStorage.WriteTextAsync(uploadId, suffix, content, cancellationToken);
            jsonArtifactsCopied++;
        }

        return new DocumentStorageMigrationResult(pdfsCopied, jsonArtifactsCopied, filesSkipped);
    }

    private static bool TryResolveJsonArtifact(string path, out Guid uploadId, out string suffix)
    {
        var fileName = Path.GetFileName(path);
        foreach (var candidateSuffix in JsonSuffixes)
        {
            if (!fileName.EndsWith(candidateSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            var idText = fileName[..^candidateSuffix.Length];
            if (Guid.TryParse(idText, out uploadId))
            {
                suffix = candidateSuffix;
                return true;
            }
        }

        const string legacyPrefix = "docclass-";
        const string jsonExtension = ".json";
        if (fileName.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(jsonExtension, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(fileName[legacyPrefix.Length..^jsonExtension.Length], out uploadId))
        {
            suffix = DocumentArtifactSuffixes.DocumentType;
            return true;
        }

        uploadId = Guid.Empty;
        suffix = string.Empty;
        return false;
    }
}
