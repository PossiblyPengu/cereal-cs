using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Cereal.App.Services;

namespace Cereal.App.ViewModels.Library;

/// <summary>
/// Converts a local cover file path into a cached, downscaled Bitmap.
/// Uses <see cref="ImageCache.Instance"/> so every card that shows the same
/// cover shares the same <see cref="Bitmap"/> object in memory.
/// </summary>
public sealed class CoverThumbConverter : IValueConverter
{
    public static readonly CoverThumbConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ImageCache.Instance.Get(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
