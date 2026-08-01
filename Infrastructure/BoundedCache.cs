using System;
using System.Collections.Generic;

namespace Api.Infrastructure;

/// <summary>
/// A small, process-local acceleration cache with an exact size bound and sliding expiration.
/// Values stored here must never be the application's source of truth.
/// </summary>
public sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private sealed class Entry(TValue value, long size, DateTimeOffset lastAccess)
    {
        public TValue Value { get; } = value;
        public long Size { get; } = size;
        public DateTimeOffset LastAccess { get; set; } = lastAccess;
    }

    private readonly object _gate = new();
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly long _maxSize;
    private readonly TimeSpan _slidingExpiration;
    private readonly Func<TValue, long> _sizeCalculator;
    private long _currentSize;

    public BoundedCache(long maxSize, TimeSpan slidingExpiration, Func<TValue, long>? sizeCalculator = null)
    {
        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize));
        if (slidingExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(slidingExpiration));

        _maxSize = maxSize;
        _slidingExpiration = slidingExpiration;
        _sizeCalculator = sizeCalculator ?? (_ => 1);
    }

    public TValue this[TKey key]
    {
        set => Set(key, value);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                value = default!;
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - entry.LastAccess >= _slidingExpiration)
            {
                RemoveCore(key, entry);
                value = default!;
                return false;
            }

            entry.LastAccess = now;
            value = entry.Value;
            return true;
        }
    }

    public bool TryRemove(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                value = default!;
                return false;
            }

            value = entry.Value;
            RemoveCore(key, entry);
            return true;
        }
    }

    private void Set(TKey key, TValue value)
    {
        var size = Math.Max(1, _sizeCalculator(value));

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
                RemoveCore(key, existing);

            // Oversized values remain available from the durable backing store.
            if (size > _maxSize)
                return;

            var now = DateTimeOffset.UtcNow;
            RemoveExpiredCore(now);
            _entries[key] = new Entry(value, size, now);
            _currentSize += size;

            while (_currentSize > _maxSize && _entries.Count > 0)
            {
                var oldest = FindOldestEntry();
                RemoveCore(oldest.Key, oldest.Value);
            }
        }
    }

    private void RemoveExpiredCore(DateTimeOffset now)
    {
        foreach (var pair in _entries.ToArray())
        {
            if (now - pair.Value.LastAccess >= _slidingExpiration)
                RemoveCore(pair.Key, pair.Value);
        }
    }

    private KeyValuePair<TKey, Entry> FindOldestEntry()
    {
        using var iterator = _entries.GetEnumerator();
        iterator.MoveNext();
        var oldest = iterator.Current;

        while (iterator.MoveNext())
        {
            if (iterator.Current.Value.LastAccess < oldest.Value.LastAccess)
                oldest = iterator.Current;
        }

        return oldest;
    }

    private void RemoveCore(TKey key, Entry entry)
    {
        if (_entries.Remove(key))
            _currentSize -= entry.Size;
    }
}
