using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace Cereal.App.Services;

/// <summary>
/// Thread-safe LRU bitmap cache bounded to <see cref="Capacity"/> entries.
/// Older entries are disposed and evicted when the limit is exceeded.
/// </summary>
public sealed class ImageCache : IDisposable
{
    public static readonly ImageCache Instance = new(200);

    private sealed class Entry
    {
        public required Bitmap Bitmap { get; init; }
        public required long LastWriteTicks { get; init; }
        public long LastAccess;
    }

    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, Entry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    // Thumb decode width: 2× target card width (150px) for HiDPI crispness
    private const int ThumbDecodeWidth = 300;

    public ImageCache(int capacity) => _capacity = capacity;

    /// <summary>
    /// Returns a decoded bitmap for <paramref name="path"/>, loading and caching on first access.
    /// Returns null if the file does not exist or cannot be decoded.
    /// </summary>
    public Bitmap? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        long lastWrite;
        try { lastWrite = File.GetLastWriteTimeUtc(path).Ticks; }
        catch { return null; }

        if (_cache.TryGetValue(path, out var entry))
        {
            // Invalidate if file has changed
            if (entry.LastWriteTicks == lastWrite)
            {
                Interlocked.Exchange(ref entry.LastAccess, Environment.TickCount64);
                return entry.Bitmap;
            }

            // File changed — remove stale entry
            if (_cache.TryRemove(path, out var stale))
                DisposeSafe(stale.Bitmap);
        }

        try
        {
            using var fs = File.OpenRead(path);
            var bmp = Bitmap.DecodeToWidth(fs, ThumbDecodeWidth);
            var newEntry = new Entry
            {
                Bitmap = bmp,
                LastWriteTicks = lastWrite,
                LastAccess = Environment.TickCount64,
            };
            _cache[path] = newEntry;
            EvictIfOverCapacity();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Remove and dispose the cached entry for <paramref name="path"/>.</summary>
    public void Invalidate(string path)
    {
        if (_cache.TryRemove(path, out var e))
            DisposeSafe(e.Bitmap);
    }

    /// <summary>Remove all cached bitmaps.</summary>
    public void Clear()
    {
        var keys = _cache.Keys.ToList();
        foreach (var k in keys)
            if (_cache.TryRemove(k, out var e))
                DisposeSafe(e.Bitmap);
    }

    public void Dispose() => Clear();

    // ── Eviction ──────────────────────────────────────────────────────────────

    private void EvictIfOverCapacity()
    {
        if (_cache.Count <= _capacity) return;

        // Remove the least recently accessed entry
        var lru = _cache.MinBy(kv => Interlocked.Read(ref kv.Value.LastAccess));
        if (_cache.TryRemove(lru.Key, out var evicted))
            DisposeSafe(evicted.Bitmap);
    }

    private static void DisposeSafe(Bitmap? bmp)
    {
        try { bmp?.Dispose(); }
        catch { /* Avalonia may have already released it */ }
    }
}
