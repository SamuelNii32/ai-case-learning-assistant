using System.Text.Json.Serialization;
using Api.Infrastructure;
using iText.Kernel.Pdf;

namespace Api.Services;

public interface IUploadProcessingService
{
    Task<UploadProcessingResult> ProcessAsync(
        string ownerId,
        IFormFile file,
        CancellationToken cancellationToken = default);
}

public enum UploadRejectionReason
{
    FileTooLarge,
    UnsupportedMediaType,
    InvalidPdf,
    TooManyPages
}

public sealed record UploadProcessingResult(
    bool Succeeded,
    Guid? UploadId = null,
    UploadRejectionReason? RejectionReason = null,
    string? Error = null,
    long? MaxBytes = null,
    int? Pages = null,
    int? MaxPages = null)
{
    public static UploadProcessingResult Created(Guid uploadId) => new(true, UploadId: uploadId);

    public static UploadProcessingResult Rejected(
        UploadRejectionReason reason,
        string error,
        long? maxBytes = null,
        int? pages = null,
        int? maxPages = null) =>
        new(false, RejectionReason: reason, Error: error, MaxBytes: maxBytes, Pages: pages, MaxPages: maxPages);
}

public sealed record UploadProcessingOptions(long MaxUploadBytes, int MaxUploadPages)
{
    public static UploadProcessingOptions Load(IConfiguration configuration)
    {
        return new UploadProcessingOptions(
            ReadLong(configuration, "MAX_UPLOAD_BYTES", "Upload:MaxBytes", 25L * 1024L * 1024L),
            ReadInt(configuration, "MAX_UPLOAD_PAGES", "Upload:MaxPages", 100));
    }

    private static long ReadLong(IConfiguration configuration, string environmentName, string configurationKey, long fallback)
    {
        var raw = Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationKey];
        return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static int ReadInt(IConfiguration configuration, string environmentName, string configurationKey, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationKey];
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}

public interface IUploadPdfAnalyzer
{
    UploadPdfAnalysis Analyze(Guid uploadId, string stagedPdfPath);
}

public sealed record UploadPdfAnalysis(
    int Pages,
    int Images,
    LayoutManifest? Layout,
    AnalysisOutcome ImageAnalysis,
    AnalysisOutcome LayoutAnalysis);

public sealed record AnalysisOutcome(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("warning")] string? Warning = null)
{
    public static AnalysisOutcome Succeeded() => new("succeeded");
    public static AnalysisOutcome Failed(string warning) => new("failed", warning);
}

public sealed class InvalidPdfUploadException : Exception
{
    public InvalidPdfUploadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class UploadPdfAnalyzer(ILogger<UploadPdfAnalyzer> logger) : IUploadPdfAnalyzer
{
    public UploadPdfAnalysis Analyze(Guid uploadId, string stagedPdfPath)
    {
        int pages;
        try
        {
            using var document = new PdfDocument(new PdfReader(stagedPdfPath));
            pages = document.GetNumberOfPages();
            if (pages <= 0)
            {
                throw new InvalidPdfUploadException("The PDF contains no pages.");
            }
        }
        catch (InvalidPdfUploadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidPdfUploadException("The uploaded file could not be parsed as a PDF.", ex);
        }

        var images = 0;
        var imageAnalysis = AnalysisOutcome.Succeeded();
        try
        {
            images = PdfImageUtils.CountRasterImagesExact(stagedPdfPath);
        }
        catch (Exception ex)
        {
            const string warning = "Raster image analysis failed; the image count is not confirmed.";
            imageAnalysis = AnalysisOutcome.Failed(warning);
            logger.LogWarning(ex, "Could not count raster images in staged upload {UploadId}", uploadId);
        }

        LayoutManifest? layout = null;
        var layoutAnalysis = AnalysisOutcome.Succeeded();
        try
        {
            layout = DocumentLayoutAnalyzer.Analyze(uploadId, stagedPdfPath);
        }
        catch (Exception ex)
        {
            const string warning = "Layout analysis failed; figure and table counts are not confirmed.";
            layoutAnalysis = AnalysisOutcome.Failed(warning);
            logger.LogWarning(ex, "Could not analyze layout in staged upload {UploadId}", uploadId);
        }

        return new UploadPdfAnalysis(pages, images, layout, imageAnalysis, layoutAnalysis);
    }
}

public sealed class UploadProcessingService(
    IDocumentStorage storage,
    IUploadRepository uploads,
    IUploadPdfAnalyzer analyzer,
    UploadProcessingOptions options,
    ILogger<UploadProcessingService> logger) : IUploadProcessingService
{
    private const string SummarySuffix = DocumentArtifactSuffixes.Summary;

    public async Task<UploadProcessingResult> ProcessAsync(
        string ownerId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Length > options.MaxUploadBytes)
        {
            return UploadProcessingResult.Rejected(
                UploadRejectionReason.FileTooLarge,
                "File is too large.",
                maxBytes: options.MaxUploadBytes);
        }

        var looksLikePdf = string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!looksLikePdf)
        {
            return UploadProcessingResult.Rejected(
                UploadRejectionReason.UnsupportedMediaType,
                "Only PDF files are supported.");
        }

        var uploadId = Guid.NewGuid();
        var stagingRoot = Path.Combine(Path.GetTempPath(), "casepilot-upload-staging");
        var stagingPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}.pdf");
        var durableWriteStarted = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);
            await using (var stagedOutput = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await file.CopyToAsync(stagedOutput, cancellationToken);
            }

            UploadPdfAnalysis analysis;
            try
            {
                analysis = analyzer.Analyze(uploadId, stagingPath);
            }
            catch (InvalidPdfUploadException ex)
            {
                logger.LogInformation(ex, "Rejected invalid PDF upload {UploadId}", uploadId);
                return UploadProcessingResult.Rejected(
                    UploadRejectionReason.InvalidPdf,
                    "The uploaded file is not a valid PDF.");
            }

            if (analysis.Pages > options.MaxUploadPages)
            {
                return UploadProcessingResult.Rejected(
                    UploadRejectionReason.TooManyPages,
                    "PDF has too many pages.",
                    pages: analysis.Pages,
                    maxPages: options.MaxUploadPages);
            }

            var uploadedAt = DateTime.UtcNow;
            var originalFileName = Path.GetFileName(file.FileName) ?? string.Empty;
            string durableFilePath;

            await using (var stagedInput = File.OpenRead(stagingPath))
            {
                // Cleanup is attempted even if a provider fails after a partial durable write.
                durableWriteStarted = true;
                durableFilePath = await storage.SavePdfAsync(uploadId, stagedInput, cancellationToken);
            }

            if (analysis.Layout is not null)
            {
                await storage.WriteJsonAsync(
                    uploadId,
                    DocumentArtifactSuffixes.Layout,
                    analysis.Layout,
                    cancellationToken);
            }

            var fileSizeBytes = file.Length;
            var tableCount = analysis.Layout is null
                ? 0
                : analysis.Layout.Captions.Count(c => c.Kind.Equals("table", StringComparison.OrdinalIgnoreCase))
                  + analysis.Layout.Tables.Count;
            var figureCount = analysis.Layout?.Captions.Count(
                c => c.Kind.Equals("figure", StringComparison.OrdinalIgnoreCase)) ?? 0;
            var warnings = new[] { analysis.ImageAnalysis.Warning, analysis.LayoutAnalysis.Warning }
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Cast<string>()
                .ToArray();

            var summary = new UploadSummary(
                uploadId,
                originalFileName,
                fileSizeBytes,
                Math.Round(fileSizeBytes / (1024.0 * 1024.0), 2),
                analysis.Pages,
                new UploadCounts(analysis.Images, figureCount, tableCount),
                new UploadAnalysisStatus(analysis.ImageAnalysis, analysis.LayoutAnalysis),
                warnings,
                uploadedAt.ToString("o"),
                DateTime.UtcNow.ToString("o"));

            await storage.WriteJsonAsync(uploadId, SummarySuffix, summary, cancellationToken);
            await uploads.CreateAsync(
                new UploadMetadata(uploadId, ownerId, durableFilePath, originalFileName, uploadedAt),
                cancellationToken);

            return UploadProcessingResult.Created(uploadId);
        }
        catch (Exception originalException) when (durableWriteStarted)
        {
            logger.LogError(originalException, "Upload processing failed after durable storage started for {UploadId}", uploadId);
            try
            {
                await storage.DeleteArtifactsAsync(uploadId, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError(
                    cleanupException,
                    "Artifact cleanup also failed for upload {UploadId}; original failure was {OriginalExceptionType}: {OriginalExceptionMessage}",
                    uploadId,
                    originalException.GetType().Name,
                    originalException.Message);
            }

            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(cleanupException, "Could not delete upload staging file {StagingPath}", stagingPath);
            }
        }
    }
}

public sealed record UploadSummary(
    [property: JsonPropertyName("uploadId")] Guid UploadId,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("fileSizeMB")] double FileSizeMb,
    [property: JsonPropertyName("pages")] int Pages,
    [property: JsonPropertyName("counts")] UploadCounts Counts,
    [property: JsonPropertyName("analysis")] UploadAnalysisStatus Analysis,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("uploadedAt")] string UploadedAt,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt);

public sealed record UploadCounts(
    [property: JsonPropertyName("images")] int Images,
    [property: JsonPropertyName("figures")] int Figures,
    [property: JsonPropertyName("tables")] int Tables);

public sealed record UploadAnalysisStatus(
    [property: JsonPropertyName("images")] AnalysisOutcome Images,
    [property: JsonPropertyName("layout")] AnalysisOutcome Layout);
