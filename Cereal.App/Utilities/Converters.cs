using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cereal.App
{
    // String converters
    public static class StringConverters
    {
        public static IValueConverter IsNotNullOrEmpty { get; } = new StringNotNullOrEmptyConverter();
    }

    internal sealed class StringNotNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var s = value?.ToString() ?? string.Empty;
            if (parameter is string p && !string.IsNullOrEmpty(p))
                return string.Equals(s, p, StringComparison.OrdinalIgnoreCase);
            return !string.IsNullOrEmpty(s);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    // Object converters
    public static class ObjectConverters
    {
        public static IValueConverter IsNull { get; } = new ObjectIsNullConverter();
        public static IValueConverter IsNotNull { get; } = new InverseConverter(new ObjectIsNullConverter());
    }

    internal sealed class ObjectIsNullConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    internal sealed class InverseConverter : IValueConverter
    {
        private readonly IValueConverter _inner;
        public InverseConverter(IValueConverter inner) => _inner = inner;
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var r = _inner.Convert(value, typeof(object), parameter, culture);
            return r is bool b ? !b : r;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    // Bool -> object converter (returns one of two comma-separated parameters)
    public static class BoolConverters
    {
        public static IValueConverter ToObject { get; } = new BoolToObjectConverter();
        public static IValueConverter IsSearchHighlighted { get; } = new BoolToBrushConverter(
            new SolidColorBrush(Color.FromArgb(0x08, 0xff, 0xff, 0xff)),
            Brushes.Transparent);
    }

    internal sealed class BoolToBrushConverter : IValueConverter
    {
        private readonly IBrush _trueBrush;
        private readonly IBrush _falseBrush;
        public BoolToBrushConverter(IBrush trueBrush, IBrush falseBrush)
        {
            _trueBrush = trueBrush;
            _falseBrush = falseBrush;
        }
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? _trueBrush : _falseBrush;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>index.css .search-plat-chip — MultiBinding: [0]=SearchPlatformFilter, [1]=chip key (__all or platform id).</summary>
    public static class SearchChipConverters
    {
        public static IMultiValueConverter Appearance { get; } = new SearchPlatformChipAppearanceConverter();
    }

    internal sealed class SearchPlatformChipAppearanceConverter : IMultiValueConverter
    {
        private static readonly IBrush AccentSoft = new SolidColorBrush(Color.Parse("#1FD4A853"));
        private static readonly IBrush AccentBorder = new SolidColorBrush(Color.Parse("#4dd4a853"));
        private static readonly IBrush AccentFg = new SolidColorBrush(Color.Parse("#d4a853"));
        private static readonly IBrush MutedFg = new SolidColorBrush(Color.Parse("#3d3a35"));

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var part = parameter as string ?? "bg";
            if (values.Count < 2)
                return part == "fg" ? MutedFg : Brushes.Transparent;

            var filter = values[0] as string;
            var key = values[1]?.ToString();
            if (string.IsNullOrEmpty(key))
                return part == "fg" ? MutedFg : Brushes.Transparent;

            var active = key == "__all"
                ? string.IsNullOrEmpty(filter)
                : string.Equals(filter, key, StringComparison.Ordinal);

            if (!active)
            {
                return part switch
                {
                    "fg" => MutedFg,
                    "border" => Brushes.Transparent,
                    _ => Brushes.Transparent,
                };
            }

            return part switch
            {
                "fg" => AccentFg,
                "border" => AccentBorder,
                _ => AccentSoft,
            };
        }
    }

    // ─── Shortcuts used in XAML ──────────────────────────────────────────────
    public static class Converters
    {
        public static IValueConverter StringIsNotEmpty { get; } = StringConverters.IsNotNullOrEmpty;
    }

    internal sealed class BoolToObjectConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var parts = (parameter as string)?.Split(',') ?? Array.Empty<string>();
            var t = value is bool b && b;
            var selected = t ? (parts.Length > 0 ? parts[0] : string.Empty) : (parts.Length > 1 ? parts[1] : string.Empty);
            // Try to coerce to targetType if it's not string
            if (targetType == typeof(object) || targetType == typeof(string)) return selected;
            try
            {
                return System.Convert.ChangeType(selected, targetType, culture);
            }
            catch (Exception)
            {
                return selected;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Title-bar tab pills — active uses ThemeSurface tint (same family as nav glass / panels).</summary>
    public static class TabConverters
    {
        public static IValueConverter PanelPillBackground { get; } = new BoolToBrushConverter(
            new SolidColorBrush(Color.Parse("#180d0d16")),
            Brushes.Transparent);
        public static IValueConverter PanelPillBorder { get; } = new BoolToBrushConverter(
            new SolidColorBrush(Color.Parse("#30d4a853")),
            Brushes.Transparent);
        public static IValueConverter StreamPillBackground { get; } = new BoolToBrushConverter(
            new SolidColorBrush(Color.Parse("#1a0d0d16")),
            new SolidColorBrush(Color.Parse("#080d0d16")));
    }
}
