using System.Text;
using System.Text.Json;
using Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IngestionApi.Tests;

[Collection(nameof(IndexingServiceCollection))]
public sealed class IndexingServiceTests : IDisposable
{
    private readonly string _pdfPath = Path.Combine(
        Path.GetTempPath(),
        $"casepilot-indexing-{Guid.NewGuid():N}.pdf");
    private readonly string? _originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    [Fact]
    public async Task ExistingIndexReturnsCachedSummaryWithoutRetrievingPdf()
    {
        var uploadId = Guid.NewGuid();
        var storage = new IndexingDocumentStorage
        {
            IndexJson = JsonSerializer.Serialize(new[]
            {
                new SerializableChunk(1, "First", [1f, 0f]),
                new SerializableChunk(2, "Second", [0f, 1f]),
                new SerializableChunk(2, "Third", [0.5f, 0.5f])
            })
        };
        var service = CreateService(storage);

        var result = await service.BuildAsync(uploadId);

        Assert.True(result.Cached);
        Assert.Equal(uploadId, result.UploadId);
        Assert.Equal(3, result.Chunks);
        Assert.Equal(2, result.PagesIndexed);
        Assert.Equal(0, storage.GetPdfPathCalls);
    }

    [Fact]
    public async Task MissingIndexRetrievesPdfAndBuildsIndex()
    {
        CreateBlankPdf();
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        var storage = new IndexingDocumentStorage { PdfPath = _pdfPath };
        var service = CreateService(storage);

        var result = await service.BuildAsync(Guid.NewGuid());

        Assert.False(result.Cached);
        Assert.Equal(0, result.Chunks);
        Assert.Equal(0, result.PagesIndexed);
        Assert.Equal(1, storage.GetPdfPathCalls);
        Assert.Equal(DocumentArtifactSuffixes.Index, storage.LastWrittenSuffix);
    }

    [Fact]
    public async Task MissingIndexAndOriginalPdfPreservesFileNotFoundError()
    {
        var uploadId = Guid.NewGuid();
        var storage = new IndexingDocumentStorage();
        var service = CreateService(storage);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => service.BuildAsync(uploadId));

        Assert.Contains(uploadId.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Equal(1, storage.GetPdfPathCalls);
    }

    [Fact]
    public async Task BuildCanBeRetriedAfterOriginalPdfBecomesAvailable()
    {
        CreateBlankPdf();
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        var uploadId = Guid.NewGuid();
        var storage = new IndexingDocumentStorage();
        var service = CreateService(storage);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.BuildAsync(uploadId));
        storage.PdfPath = _pdfPath;

        var result = await service.BuildAsync(uploadId);

        Assert.False(result.Cached);
        Assert.Equal(2, storage.GetPdfPathCalls);
        Assert.Equal(DocumentArtifactSuffixes.Index, storage.LastWrittenSuffix);
    }

    [Fact]
    public async Task CancellationStopsBuildBeforeStorageAccess()
    {
        var storage = new IndexingDocumentStorage();
        var service = CreateService(storage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.BuildAsync(Guid.NewGuid(), cancellation.Token));

        Assert.Equal(0, storage.ReadTextCalls);
        Assert.Equal(0, storage.GetPdfPathCalls);
    }

    private static IndexingService CreateService(IDocumentStorage storage)
    {
        var database = new DatabaseOptions("sqlite", "Data Source=:memory:", null);
        return new IndexingService(
            storage,
            new IndexJobStore(database),
            NullLogger<IndexingService>.Instance);
    }

    private void CreateBlankPdf()
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };

        AddObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        AddObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        AddObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>");
        AddObject(4, "<< /Length 0 >>\nstream\n\nendstream");

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            pdf.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", offset);
        pdf.Append("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        pdf.Append(xrefOffset);
        pdf.Append("\n%%EOF\n");

        File.WriteAllText(_pdfPath, pdf.ToString(), Encoding.ASCII);

        void AddObject(int number, string body)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(number).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _originalApiKey);
        if (File.Exists(_pdfPath))
            File.Delete(_pdfPath);
    }
}

[CollectionDefinition(nameof(IndexingServiceCollection), DisableParallelization = true)]
public sealed class IndexingServiceCollection;

internal sealed class IndexingDocumentStorage : IDocumentStorage
{
    public string? IndexJson { get; set; }
    public string? PdfPath { get; set; }
    public int ReadTextCalls { get; private set; }
    public int GetPdfPathCalls { get; private set; }
    public string? LastWrittenSuffix { get; private set; }

    public Task<string?> GetPdfPathAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetPdfPathCalls++;
        return Task.FromResult(PdfPath);
    }

    public Task<string?> ReadTextAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadTextCalls++;
        return Task.FromResult(suffix == DocumentArtifactSuffixes.Index ? IndexJson : null);
    }

    public Task WriteJsonAsync(Guid uploadId, string suffix, object value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastWrittenSuffix = suffix;
        return Task.CompletedTask;
    }

    public Task<string> SavePdfAsync(Guid uploadId, Stream pdfStream, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> PdfExistsAsync(Guid uploadId, CancellationToken cancellationToken = default) =>
        Task.FromResult(PdfPath is not null);

    public Task WriteTextAsync(Guid uploadId, string suffix, string content, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<bool> ExistsAsync(Guid uploadId, string suffix, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public async IAsyncEnumerable<StoredSummaryFile> EnumerateSummariesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task DeleteArtifactsAsync(Guid uploadId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
