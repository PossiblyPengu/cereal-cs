using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
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
            new SolidColorBrush(Color.FromArgb(0x14, 0xff, 0xff, 0xff)),
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
            catch { return selected; }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
