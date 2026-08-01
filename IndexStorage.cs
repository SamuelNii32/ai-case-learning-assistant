using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

public record SerializableChunk(int Page, string Preview, float[] Vec);

public static class InMemoryStore
{
    public static readonly Dictionary<string, List<IndexedChunk>> VectorIndex = new();
}

public static class IndexPersistence
{
    public static bool TryLoad(Guid uploadId, IHostEnvironment env, out List<IndexedChunk> list)
    {
        var id = uploadId.ToString();
        var uploadsRoot = Path.Combine(env.ContentRootPath, "uploads");
        var indexPath = Path.Combine(uploadsRoot, $"{id}.index.json");
        list = null!;

        if (!File.Exists(indexPath)) return false;

        var json = File.ReadAllText(indexPath);
        var rows = System.Text.Json.JsonSerializer.Deserialize<SerializableChunk[]>(json);
        if (rows is null || rows.Length == 0) return false;

        list = rows.Select(r => new IndexedChunk(
            Page: r.Page,
            Vec: new ReadOnlyMemory<float>(r.Vec),
            Preview: r.Preview
        )).ToList();

        InMemoryStore.VectorIndex[id] = list;
        return true;
    }
}
