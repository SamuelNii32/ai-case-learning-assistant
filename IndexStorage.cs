using System;
using System.Linq;
using System.Text.Json;
using Api.Infrastructure;

public record SerializableChunk(int Page, string Preview, float[] Vec);

public static class InMemoryStore
{
    private const long DefaultMaxBytes = 256L * 1024L * 1024L;

    public static readonly BoundedCache<string, List<IndexedChunk>> VectorIndex = new(
        maxSize: ReadPositiveLong("VECTOR_INDEX_CACHE_MAX_BYTES", DefaultMaxBytes),
        slidingExpiration: TimeSpan.FromMinutes(ReadPositiveLong("VECTOR_INDEX_CACHE_SLIDING_MINUTES", 30)),
        sizeCalculator: EstimateSize);

    private static long EstimateSize(List<IndexedChunk> chunks)
    {
        return chunks.Sum(chunk =>
            64L +
            (long)chunk.Vec.Length * sizeof(float) +
            (long)(chunk.Preview?.Length ?? 0) * sizeof(char));
    }

    private static long ReadPositiveLong(string name, long fallback)
    {
        return long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;
    }
}

public static class IndexPersistence
{
    public static async Task<List<IndexedChunk>?> TryLoadAsync(
        Guid uploadId,
        IDocumentStorage storage,
        CancellationToken cancellationToken = default)
    {
        var id = uploadId.ToString();
        if (InMemoryStore.VectorIndex.TryGetValue(id, out var cached) && cached.Count > 0)
            return cached;

        var json = await storage.ReadTextAsync(uploadId, ".index.json", cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var rows = JsonSerializer.Deserialize<SerializableChunk[]>(json);
        if (rows is null || rows.Length == 0)
            return null;

        var list = rows.Select(r => new IndexedChunk(
            Page: r.Page,
            Vec: new ReadOnlyMemory<float>(r.Vec),
            Preview: r.Preview
        )).ToList();

        InMemoryStore.VectorIndex[id] = list;
        return list;
    }
}
