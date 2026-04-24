using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Cereal.App.ViewModels;

/// <summary>
/// Converts a cover file path into a cached, downscaled bitmap for card-grid usage.
/// This keeps decode cost lower when many cards are realized at once.
/// </summary>
public sealed class CoverThumbConverter : IValueConverter
{
    private sealed class CacheEntry
    {
        public required long LastWriteTicks { get; init; }
        public required Bitmap Image { get; init; }
    }

    public static CoverThumbConverter Instance { get; } = new();
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const int ThumbDecodeWidth = 300; // 2x target card width (150) for crisp scaling

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(path).Ticks;
            if (Cache.TryGetValue(path, out var existing) && existing.LastWriteTicks == lastWrite)
                return existing.Image;

            using var fs = File.OpenRead(path);
            var bmp = Bitmap.DecodeToWidth(fs, ThumbDecodeWidth);
            Cache[path] = new CacheEntry { LastWriteTicks = lastWrite, Image = bmp };
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
