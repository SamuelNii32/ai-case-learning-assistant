using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using Api.Infrastructure;

public record SerializableChunk(int Page, string Preview, float[] Vec);

public static class InMemoryStore
{
    public static readonly ConcurrentDictionary<string, List<IndexedChunk>> VectorIndex = new();
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
