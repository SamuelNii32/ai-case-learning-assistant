using System.Data.Common;
using System.Text.Json;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Hosting;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Api.Infrastructure;

public sealed record IndexBuildSample(int Page, string Preview);

public sealed record IndexBuildSummary(
    Guid UploadId,
    int Chunks,
    int PagesIndexed,
    IReadOnlyList<IndexBuildSample> Sample,
    bool Cached = false
);

public sealed record IndexJobRecord(
    Guid UploadId,
    string Status,
    string? RequestedBy,
    string CreatedAt,
    string? StartedAt,
    string? CompletedAt,
    int Attempts,
    string? LastError,
    string? ResultJson,
    string? WorkerId,
    string? UpdatedAt,
    string? LastHeartbeatAt
);

public sealed class IndexJobStore
{
    private readonly DatabaseOptions _dbOptions;

    public IndexJobStore(DatabaseOptions dbOptions)
    {
        _dbOptions = dbOptions;
    }

    public async Task<IndexJobRecord?> GetAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT UploadId, Status, RequestedBy, CreatedAt, StartedAt, CompletedAt, Attempts,
       LastError, ResultJson, WorkerId, UpdatedAt, LastHeartbeatAt
FROM IndexJobs
WHERE UploadId = @uploadId
LIMIT 1;";
        cmd.AddWithValue("@uploadId", uploadId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadRecord(reader);
    }

    public async Task<IndexJobRecord> EnqueueAsync(Guid uploadId, string? requestedBy, CancellationToken cancellationToken = default)
    {
        var now = UtcNowString();
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
INSERT INTO IndexJobs (UploadId, Status, RequestedBy, CreatedAt, StartedAt, CompletedAt, Attempts, LastError, ResultJson, WorkerId, UpdatedAt, LastHeartbeatAt)
VALUES (@uploadId, 'queued', @requestedBy, @createdAt, NULL, NULL, 0, NULL, NULL, NULL, @updatedAt, NULL)
ON CONFLICT (UploadId) DO NOTHING;";
            insert.AddWithValue("@uploadId", uploadId.ToString());
            insert.AddWithValue("@requestedBy", (object?)requestedBy ?? DBNull.Value);
            insert.AddWithValue("@createdAt", now);
            insert.AddWithValue("@updatedAt", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var requeue = conn.CreateCommand())
        {
            requeue.CommandText = @"
UPDATE IndexJobs
SET Status = 'queued',
    RequestedBy = COALESCE(@requestedBy, RequestedBy),
    CompletedAt = NULL,
    LastError = NULL,
    ResultJson = NULL,
    WorkerId = NULL,
    StartedAt = NULL,
    UpdatedAt = @updatedAt,
    LastHeartbeatAt = NULL
WHERE UploadId = @uploadId
  AND Status NOT IN ('queued', 'running');";
            requeue.AddWithValue("@uploadId", uploadId.ToString());
            requeue.AddWithValue("@requestedBy", (object?)requestedBy ?? DBNull.Value);
            requeue.AddWithValue("@updatedAt", now);
            await requeue.ExecuteNonQueryAsync(cancellationToken);
        }

        return (await GetAsync(uploadId, cancellationToken))!;
    }

    public async Task<IndexJobRecord?> TryClaimNextAsync(string workerId, TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(staleAfter).ToString("O");
        var now = UtcNowString();

        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (_dbOptions.Provider is "postgres" or "postgresql")
        {
            await using var postgresClaim = conn.CreateCommand();
            postgresClaim.CommandText = @"
WITH candidate AS (
    SELECT UploadId
    FROM IndexJobs
    WHERE Status = 'queued'
       OR (Status = 'running' AND COALESCE(LastHeartbeatAt, StartedAt, CreatedAt) <= @cutoff)
    ORDER BY CreatedAt ASC
    FOR UPDATE SKIP LOCKED
    LIMIT 1
)
UPDATE IndexJobs AS job
SET Status = 'running',
    WorkerId = @workerId,
    StartedAt = COALESCE(job.StartedAt, @now),
    UpdatedAt = @now,
    LastHeartbeatAt = @now,
    Attempts = job.Attempts + 1,
    LastError = NULL
FROM candidate
WHERE job.UploadId = candidate.UploadId
RETURNING job.UploadId, job.Status, job.RequestedBy, job.CreatedAt, job.StartedAt,
          job.CompletedAt, job.Attempts, job.LastError, job.ResultJson, job.WorkerId,
          job.UpdatedAt, job.LastHeartbeatAt;";
            postgresClaim.AddWithValue("@workerId", workerId);
            postgresClaim.AddWithValue("@now", now);
            postgresClaim.AddWithValue("@cutoff", cutoff);

            await using var claimedReader = await postgresClaim.ExecuteReaderAsync(cancellationToken);
            return await claimedReader.ReadAsync(cancellationToken) ? ReadRecord(claimedReader) : null;
        }

        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        await using var select = conn.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = @"
SELECT UploadId, Status, RequestedBy, CreatedAt, StartedAt, CompletedAt, Attempts,
       LastError, ResultJson, WorkerId, UpdatedAt, LastHeartbeatAt
FROM IndexJobs
WHERE Status = 'queued'
   OR (Status = 'running' AND COALESCE(LastHeartbeatAt, StartedAt, CreatedAt) <= @cutoff)
ORDER BY CreatedAt ASC
LIMIT 1;";
        select.AddWithValue("@cutoff", cutoff);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var candidate = ReadRecord(reader);

        await reader.DisposeAsync();

        await using var claim = conn.CreateCommand();
        claim.Transaction = transaction;
        claim.CommandText = @"
UPDATE IndexJobs
SET Status = 'running',
    WorkerId = @workerId,
    StartedAt = COALESCE(StartedAt, @now),
    UpdatedAt = @now,
    LastHeartbeatAt = @now,
    Attempts = Attempts + 1,
    LastError = NULL
WHERE UploadId = @uploadId
  AND (Status = 'queued' OR (Status = 'running' AND COALESCE(LastHeartbeatAt, StartedAt, CreatedAt) <= @cutoff));";
        claim.AddWithValue("@workerId", workerId);
        claim.AddWithValue("@now", now);
        claim.AddWithValue("@uploadId", candidate.UploadId.ToString());
        claim.AddWithValue("@cutoff", cutoff);

        var affected = await claim.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(candidate.UploadId, cancellationToken);
    }

    public async Task MarkHeartbeatAsync(Guid uploadId, string workerId, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE IndexJobs
SET LastHeartbeatAt = @now,
    UpdatedAt = @now
WHERE UploadId = @uploadId
  AND WorkerId = @workerId
  AND Status = 'running';";
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@workerId", workerId);
        cmd.AddWithValue("@now", UtcNowString());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(Guid uploadId, IndexBuildSummary summary, string? workerId = null, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE IndexJobs
SET Status = 'completed',
    CompletedAt = @now,
    UpdatedAt = @now,
    LastHeartbeatAt = @now,
    LastError = NULL,
    ResultJson = @resultJson,
    WorkerId = NULL
WHERE UploadId = @uploadId" + (workerId is null ? ";" : " AND WorkerId = @workerId AND Status = 'running';");
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@now", UtcNowString());
        cmd.AddWithValue("@resultJson", JsonSerializer.Serialize(summary));
        if (workerId is not null)
            cmd.AddWithValue("@workerId", workerId);
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0 && workerId is not null)
            return;

        if (affected == 0)
        {
            await using var insert = conn.CreateCommand();
            insert.CommandText = @"
INSERT INTO IndexJobs (UploadId, Status, RequestedBy, CreatedAt, StartedAt, CompletedAt, Attempts, LastError, ResultJson, WorkerId, UpdatedAt, LastHeartbeatAt)
VALUES (@uploadId, 'completed', NULL, @now, @now, @now, 1, NULL, @resultJson, NULL, @now, @now);";
            insert.AddWithValue("@uploadId", uploadId.ToString());
            insert.AddWithValue("@now", UtcNowString());
            insert.AddWithValue("@resultJson", JsonSerializer.Serialize(summary));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task MarkFailedAsync(Guid uploadId, Exception ex, string? workerId = null, CancellationToken cancellationToken = default)
    {
        await using var conn = _dbOptions.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE IndexJobs
SET Status = 'failed',
    CompletedAt = @now,
    UpdatedAt = @now,
    LastHeartbeatAt = @now,
    LastError = @lastError,
    WorkerId = NULL
WHERE UploadId = @uploadId" + (workerId is null ? ";" : " AND WorkerId = @workerId AND Status = 'running';");
        cmd.AddWithValue("@uploadId", uploadId.ToString());
        cmd.AddWithValue("@now", UtcNowString());
        cmd.AddWithValue("@lastError", $"{ex.GetType().Name}: {ex.Message}");
        if (workerId is not null)
            cmd.AddWithValue("@workerId", workerId);
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0 && workerId is not null)
            return;

        if (affected == 0)
        {
            await using var insert = conn.CreateCommand();
            insert.CommandText = @"
INSERT INTO IndexJobs (UploadId, Status, RequestedBy, CreatedAt, StartedAt, CompletedAt, Attempts, LastError, ResultJson, WorkerId, UpdatedAt, LastHeartbeatAt)
VALUES (@uploadId, 'failed', NULL, @now, @now, @now, 1, @lastError, NULL, NULL, @now, @now);";
            insert.AddWithValue("@uploadId", uploadId.ToString());
            insert.AddWithValue("@now", UtcNowString());
            insert.AddWithValue("@lastError", $"{ex.GetType().Name}: {ex.Message}");
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IndexJobRecord ReadRecord(DbDataReader reader)
    {
        string? ReadString(string column)
        {
            var value = reader[column];
            return value is DBNull ? null : value?.ToString();
        }

        return new IndexJobRecord(
            UploadId: Guid.Parse(reader["UploadId"].ToString() ?? Guid.Empty.ToString()),
            Status: reader["Status"]?.ToString() ?? "queued",
            RequestedBy: ReadString("RequestedBy"),
            CreatedAt: reader["CreatedAt"]?.ToString() ?? "",
            StartedAt: ReadString("StartedAt"),
            CompletedAt: ReadString("CompletedAt"),
            Attempts: int.TryParse(reader["Attempts"]?.ToString(), out var attempts) ? attempts : 0,
            LastError: ReadString("LastError"),
            ResultJson: ReadString("ResultJson"),
            WorkerId: ReadString("WorkerId"),
            UpdatedAt: ReadString("UpdatedAt"),
            LastHeartbeatAt: ReadString("LastHeartbeatAt")
        );
    }

    private static string UtcNowString() => DateTimeOffset.UtcNow.ToString("O");
}

public sealed class IndexingService
{
    private readonly IDocumentStorage _storage;
    private readonly IndexJobStore _jobStore;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        IDocumentStorage storage,
        IndexJobStore jobStore,
        ILogger<IndexingService> logger)
    {
        _storage = storage;
        _jobStore = jobStore;
        _logger = logger;
    }

    public async Task<IndexBuildSummary> BuildAsync(Guid uploadId, CancellationToken cancellationToken = default, string? workerId = null)
    {
        var pdfPath = await _storage.GetPdfPathAsync(uploadId, cancellationToken);
        if (pdfPath is null)
            throw new FileNotFoundException($"PDF not found for upload {uploadId}.");

        var existingChunks = await IndexPersistence.TryLoadAsync(uploadId, _storage, cancellationToken);
        if (existingChunks is { Count: > 0 })
        {
            return new IndexBuildSummary(
                uploadId,
                existingChunks.Count,
                existingChunks.Select(x => x.Page).Distinct().Count(),
                existingChunks.Take(3).Select(x => new IndexBuildSample(x.Page, x.Preview)).ToList(),
                Cached: true
            );
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

        var emb = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey);
        var chunks = new List<IndexedChunk>();
        var pendingChunks = new List<(int Page, string Text)>();
        var pagesIndexed = 0;

        using (var pdf = PdfPigDoc.Open(pdfPath))
        {
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = (page.Text ?? "").Trim();
                var text = TextNormalization.Clean(raw);

                if (string.IsNullOrWhiteSpace(text)) continue;
                pagesIndexed++;

                foreach (var chunk in TextChunking.ChunkBySentences(text, 1200, 200))
                {
                    pendingChunks.Add((page.Number, chunk));
                }
            }
        }

        const int embeddingBatchSize = 64;
        for (var i = 0; i < pendingChunks.Count; i += embeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(workerId))
                await _jobStore.MarkHeartbeatAsync(uploadId, workerId, cancellationToken);

            var batch = pendingChunks.Skip(i).Take(embeddingBatchSize).ToList();
            var embeddings = await emb.GenerateEmbeddingsAsync(
                batch.Select(x => x.Text),
                cancellationToken: cancellationToken);

            var vectors = embeddings.Value.ToList();
            for (var j = 0; j < batch.Count; j++)
            {
                var vec = vectors[j].ToFloats();
                chunks.Add(new IndexedChunk(batch[j].Page, vec, batch[j].Text));
            }
        }

        InMemoryStore.VectorIndex[uploadId.ToString()] = chunks;

        try
        {
            var cls = DocTypeClassifier.Evaluate(chunks);
            await DocTypePersistence.SaveAsync(uploadId, _storage, cls, cancellationToken);
            _logger.LogInformation("[CLASSIFY] {UploadId} -> {DocType} (conf {Confidence:0.00}) :: {Reason}",
                uploadId, cls.DocType, cls.Confidence, cls.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CLASSIFY ERROR] {UploadId}", uploadId);
        }

        var serializable = chunks.Select(c => new SerializableChunk(c.Page, c.Preview, c.Vec.ToArray())).ToArray();
        await _storage.WriteJsonAsync(
            uploadId,
            DocumentArtifactSuffixes.Index,
            serializable,
            cancellationToken);

        return new IndexBuildSummary(
            uploadId,
            chunks.Count,
            pagesIndexed,
            chunks.Take(3).Select(x => new IndexBuildSample(x.Page, x.Preview)).ToList()
        );
    }
}

public sealed class IndexJobWorkerHostedService : BackgroundService
{
    private readonly IndexJobStore _jobStore;
    private readonly IndexingService _indexingService;
    private readonly ILogger<IndexJobWorkerHostedService> _logger;
    private readonly string _workerId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";

    public IndexJobWorkerHostedService(
        IndexJobStore jobStore,
        IndexingService indexingService,
        ILogger<IndexJobWorkerHostedService> logger)
    {
        _jobStore = jobStore;
        _indexingService = indexingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleDelay = TimeSpan.FromSeconds(2);
        var staleAfter = TimeSpan.FromMinutes(30);

        _logger.LogInformation("Index worker started as {WorkerId}.", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            IndexJobRecord? job = null;

            try
            {
                job = await _jobStore.TryClaimNextAsync(_workerId, staleAfter, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to claim index job.");
                await Task.Delay(idleDelay, stoppingToken);
                continue;
            }

            if (job is null)
            {
                await Task.Delay(idleDelay, stoppingToken);
                continue;
            }

            try
            {
                _logger.LogInformation("Indexing upload {UploadId} (attempt {Attempt}).", job.UploadId, job.Attempts + 1);
                var result = await _indexingService.BuildAsync(job.UploadId, stoppingToken, _workerId);
                await _jobStore.MarkCompletedAsync(job.UploadId, result, _workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Index job failed for upload {UploadId}.", job.UploadId);
                try
                {
                    await _jobStore.MarkFailedAsync(job.UploadId, ex, _workerId, stoppingToken);
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Failed to persist index job failure for upload {UploadId}.", job.UploadId);
                }
            }
        }

        _logger.LogInformation("Index worker stopped.");
    }
}
