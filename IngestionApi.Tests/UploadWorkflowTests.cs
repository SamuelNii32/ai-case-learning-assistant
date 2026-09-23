using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Api.Endpoints;
using Api.Infrastructure;
using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IngestionApi.Tests;

public sealed class UploadEndpointTests
{
    [Fact]
    public async Task ValidPdfReturnsCreatedWithUploadIdAndLocation()
    {
        var storage = new FakeDocumentStorage();
        var processor = new UploadProcessingService(
            storage,
            new FakeUploadRepository(),
            new UploadPdfAnalyzer(NullLogger<UploadPdfAnalyzer>.Instance),
            new UploadProcessingOptions(1024 * 1024, 10),
            NullLogger<UploadProcessingService>.Instance);
        var context = CreateContext(FormWith(ValidPdfFile("file", "case.pdf")));

        var result = await UploadEndpoints.HandlePostUploadAsync(context.Request, context, processor);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        var uploadId = body.RootElement.GetProperty("uploadId").GetGuid();
        Assert.NotEqual(Guid.Empty, uploadId);
        Assert.Equal($"/uploads/{uploadId}/summary", context.Response.Headers.Location);
        Assert.Equal(1, storage.SaveCount);
    }

    [Fact]
    public async Task MissingFileFieldReturnsBadRequest()
    {
        var processor = new FakeUploadProcessingService(UploadProcessingResult.Created(Guid.NewGuid()));
        var context = CreateContext(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));

        var result = await UploadEndpoints.HandlePostUploadAsync(context.Request, context, processor);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, processor.CallCount);
    }

    [Fact]
    public async Task IncorrectlyNamedMultipartFieldReturnsBadRequest()
    {
        var processor = new FakeUploadProcessingService(UploadProcessingResult.Created(Guid.NewGuid()));
        var context = CreateContext(FormWith(File("document", "case.pdf", "%PDF-valid")));

        var result = await UploadEndpoints.HandlePostUploadAsync(context.Request, context, processor);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, processor.CallCount);
    }

    [Fact]
    public async Task EmptyFileReturnsBadRequest()
    {
        var processor = new FakeUploadProcessingService(UploadProcessingResult.Created(Guid.NewGuid()));
        var context = CreateContext(FormWith(File("file", "case.pdf", string.Empty)));

        var result = await UploadEndpoints.HandlePostUploadAsync(context.Request, context, processor);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, processor.CallCount);
    }

    [Fact]
    public async Task OversizedFileReturnsBadRequest()
    {
        var storage = new FakeDocumentStorage();
        var processor = new UploadProcessingService(
            storage,
            new FakeUploadRepository(),
            new FakeUploadPdfAnalyzer(new UploadPdfAnalysis(
                1,
                0,
                new LayoutManifest(Guid.NewGuid(), DateTime.UtcNow, [], [], []),
                AnalysisOutcome.Succeeded(),
                AnalysisOutcome.Succeeded())),
            new UploadProcessingOptions(3, 10),
            NullLogger<UploadProcessingService>.Instance);
        var context = CreateContext(FormWith(File("file", "case.pdf", "1234")));

        var result = await UploadEndpoints.HandlePostUploadAsync(context.Request, context, processor);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task RenamedNonPdfReturnsControlledBadRequest()
    {
        var storage = new FakeDocumentStorage();
        var processor = new UploadProcessingService(
            storage,
            new FakeUploadRepository(),
            new UploadPdfAnalyzer(NullLogger<UploadPdfAnalyzer>.Instance),
            new UploadProcessingOptions(1024, 10),
            NullLogger<UploadProcessingService>.Instance);
        var context = CreateContext(FormWith(File("file", "renamed.pdf", "not really a PDF")));

        var result = await UploadEndpoints.HandlePostUploadAsync(context.Request, context, processor);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, storage.SaveCount);
    }

    private static DefaultHttpContext CreateContext(IFormCollection form)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Items["userId"] = "user-1";
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = form;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static FormCollection FormWith(IFormFile file) =>
        new(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), new FormFileCollection { file });

    private static IFormFile File(string fieldName, string fileName, string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        return File(fieldName, fileName, bytes);
    }

    private static IFormFile ValidPdfFile(string fieldName, string fileName)
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        void AddObject(string value)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(value);
        }

        AddObject("1 0 obj\n<</Type /Catalog /Pages 2 0 R>>\nendobj\n");
        AddObject("2 0 obj\n<</Type /Pages /Kids [3 0 R] /Count 1>>\nendobj\n");
        AddObject("3 0 obj\n<</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources <<>> /Contents 4 0 R>>\nendobj\n");
        AddObject("4 0 obj\n<</Length 0>>\nstream\n\nendstream\nendobj\n");
        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }
        pdf.Append("trailer\n<</Size 5 /Root 1 0 R>>\nstartxref\n")
            .Append(xrefOffset)
            .Append("\n%%EOF\n");

        return File(fieldName, fileName, Encoding.ASCII.GetBytes(pdf.ToString()));
    }

    private static IFormFile File(string fieldName, string fileName, byte[] bytes)
    {
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, fieldName, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }
}

public sealed class UploadProcessingServiceTests
{
    [Fact]
    public async Task OversizedFileIsRejectedBeforeStorage()
    {
        var storage = new FakeDocumentStorage();
        var service = CreateService(storage, new FakeUploadRepository(), SuccessfulAnalysis(), maxBytes: 3);

        var result = await service.ProcessAsync("user-1", File("case.pdf", "1234"));

        Assert.False(result.Succeeded);
        Assert.Equal(UploadRejectionReason.FileTooLarge, result.RejectionReason);
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task RenamedNonPdfIsControlledInvalidPdfRejection()
    {
        var storage = new FakeDocumentStorage();
        var analyzer = new UploadPdfAnalyzer(NullLogger<UploadPdfAnalyzer>.Instance);
        var service = CreateService(storage, new FakeUploadRepository(), analyzer);

        var result = await service.ProcessAsync("user-1", File("renamed.pdf", "this is not a PDF"));

        Assert.False(result.Succeeded);
        Assert.Equal(UploadRejectionReason.InvalidPdf, result.RejectionReason);
        Assert.Equal(0, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
    }

    [Fact]
    public async Task TooManyPagesIsRejectedBeforeDurableStorage()
    {
        var storage = new FakeDocumentStorage();
        var analysis = SuccessfulAnalysis(pages: 11);
        var service = CreateService(storage, new FakeUploadRepository(), analysis, maxPages: 10);

        var result = await service.ProcessAsync("user-1", File("case.pdf", "%PDF-staged"));

        Assert.Equal(UploadRejectionReason.TooManyPages, result.RejectionReason);
        Assert.Equal(0, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
        Assert.NotNull(analysis.LastStagedPath);
        Assert.False(System.IO.File.Exists(analysis.LastStagedPath));
    }

    [Fact]
    public async Task DatabaseInsertionFailureDeletesDurableArtifactsWithUsableToken()
    {
        var original = new InvalidOperationException("database insert failed");
        var repository = new FakeUploadRepository { CreateException = original };
        var storage = new FakeDocumentStorage();
        var service = CreateService(storage, repository, SuccessfulAnalysis());
        using var requestCancellation = new CancellationTokenSource();
        repository.OnCreate = requestCancellation.Cancel;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync("user-1", File("case.pdf", "%PDF-staged"), requestCancellation.Token));

        Assert.Same(original, thrown);
        Assert.Equal(1, storage.DeleteCount);
        Assert.False(storage.DeleteCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CleanupFailurePreservesOriginalDatabaseException()
    {
        var original = new InvalidOperationException("database insert failed");
        var repository = new FakeUploadRepository { CreateException = original };
        var storage = new FakeDocumentStorage { DeleteException = new IOException("cleanup failed") };
        var service = CreateService(storage, repository, SuccessfulAnalysis());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync("user-1", File("case.pdf", "%PDF-staged")));

        Assert.Same(original, thrown);
        Assert.Equal(1, storage.DeleteCount);
    }

    [Fact]
    public async Task AnalysisFailureIsDistinguishableFromConfirmedZeroCounts()
    {
        var failedStorage = new FakeDocumentStorage();
        var failedAnalysis = new FakeUploadPdfAnalyzer(new UploadPdfAnalysis(
            1,
            0,
            null,
            AnalysisOutcome.Failed("image analysis failed"),
            AnalysisOutcome.Failed("layout analysis failed")));
        var failedService = CreateService(failedStorage, new FakeUploadRepository(), failedAnalysis);

        var failedResult = await failedService.ProcessAsync("user-1", File("case.pdf", "%PDF-staged"));

        Assert.True(failedResult.Succeeded);
        var failedSummary = Assert.IsType<UploadSummary>(failedStorage.WrittenValues[DocumentArtifactSuffixes.Summary]);
        Assert.Equal(0, failedSummary.Counts.Images);
        Assert.Equal("failed", failedSummary.Analysis.Images.Status);
        Assert.Equal("failed", failedSummary.Analysis.Layout.Status);
        Assert.Equal(2, failedSummary.Warnings.Count);

        var zeroStorage = new FakeDocumentStorage();
        var zeroService = CreateService(zeroStorage, new FakeUploadRepository(), SuccessfulAnalysis());
        await zeroService.ProcessAsync("user-1", File("case.pdf", "%PDF-staged"));
        var zeroSummary = Assert.IsType<UploadSummary>(zeroStorage.WrittenValues[DocumentArtifactSuffixes.Summary]);

        Assert.Equal(0, zeroSummary.Counts.Images);
        Assert.Equal("succeeded", zeroSummary.Analysis.Images.Status);
        Assert.Equal("succeeded", zeroSummary.Analysis.Layout.Status);
        Assert.Empty(zeroSummary.Warnings);
    }

    private static UploadProcessingService CreateService(
        FakeDocumentStorage storage,
        FakeUploadRepository repository,
        IUploadPdfAnalyzer analyzer,
        long maxBytes = 1024,
        int maxPages = 100) =>
        new(
            storage,
            repository,
            analyzer,
            new UploadProcessingOptions(maxBytes, maxPages),
            NullLogger<UploadProcessingService>.Instance);

    private static FakeUploadPdfAnalyzer SuccessfulAnalysis(int pages = 1) =>
        new(new UploadPdfAnalysis(
            pages,
            0,
            new LayoutManifest(Guid.NewGuid(), DateTime.UtcNow, [], [], []),
            AnalysisOutcome.Succeeded(),
            AnalysisOutcome.Succeeded()));

    private static IFormFile File(string fileName, string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }
}

internal sealed class FakeUploadProcessingService(UploadProcessingResult result) : IUploadProcessingService
{
    public int CallCount { get; private set; }

    public Task<UploadProcessingResult> ProcessAsync(string ownerId, IFormFile file, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(result);
    }
}

internal sealed class FakeUploadPdfAnalyzer(UploadPdfAnalysis result) : IUploadPdfAnalyzer
{
    public string? LastStagedPath { get; private set; }

    public UploadPdfAnalysis Analyze(Guid uploadId, string stagedPdfPath)
    {
        LastStagedPath = stagedPdfPath;
        return result;
    }
}

internal sealed class FakeDocumentStorage : IDocumentStorage
{
    public int SaveCount { get; private set; }
    public int DeleteCount { get; private set; }
    public CancellationToken DeleteCancellationToken { get; private set; }
    public Exception? DeleteException { get; init; }
    public Dictionary<string, object> WrittenValues { get; } = new(StringComparer.Ordinal);

    public async Task<string> SavePdfAsync(Guid uploadId, IFormFile file, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        await using var sink = new MemoryStream();
        await file.CopyToAsync(sink, cancellationToken);
        return Path.Combine(Path.GetTempPath(), "fake-document-cache", $"{uploadId}.pdf");
    }

    public Task<string?> GetPdfPathAsync(Guid uploadId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> PdfExistsAsync(Guid uploadId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task WriteJsonAsync(Guid uploadId, string suffix, object value, CancellationToken cancellationToken = default)
    {
        WrittenValues[suffix] = value;
        return Task.CompletedTask;
    }

    public Task WriteTextAsync(Guid uploadId, string suffix, string content, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<string?> ReadTextAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> ExistsAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public async IAsyncEnumerable<StoredSummaryFile> EnumerateSummariesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task DeleteArtifactsAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        DeleteCount++;
        DeleteCancellationToken = cancellationToken;
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeUploadRepository : IUploadRepository
{
    public Exception? CreateException { get; init; }
    public Action? OnCreate { get; set; }

    public Task CreateAsync(UploadMetadata upload, CancellationToken cancellationToken = default)
    {
        OnCreate?.Invoke();
        if (CreateException is not null)
        {
            throw CreateException;
        }

        return Task.CompletedTask;
    }

    public Task<bool> CanAccessAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> CanAccessClassAssignmentAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<string?> FindAccessibleClassIdAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<HashSet<string>> GetOwnedUploadIdsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new HashSet<string>());
    public Task<List<UploadListRecord>> ListMineAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<UploadListRecord>());
    public Task<List<UploadListRecord>> ListAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<UploadListRecord>());
    public Task<string?> GetDisplayNameAsync(Guid uploadId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<List<string>?> DeleteOwnedAsync(Guid uploadId, string userId, CancellationToken cancellationToken = default) => Task.FromResult<List<string>?>(null);
}
