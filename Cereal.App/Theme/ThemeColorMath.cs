using Avalonia.Media;

namespace Cereal.App.Theme;

/// <summary>RGB utilities for runtime palette derivation (hover/press/focus from accent).</summary>
internal static class ThemeColorMath
{
    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Mix <paramref name="c"/> toward white by <paramref name="amount"/> in 0..1.</summary>
    public static Color Lighten(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            c.A,
            (byte)(c.R + (255 - c.R) * amount),
            (byte)(c.G + (255 - c.G) * amount),
            (byte)(c.B + (255 - c.B) * amount));
    }

    public static Color Darken(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            c.A,
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
    }

    /// <summary>Perceived luminance 0..1 (sRGB).</summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte u) => u <= 10 ? u / 12.92 : Math.Pow((u + 0.055) / 1.055, 2.4);
        var r = Lin(c.R);
        var g = Lin(c.G);
        var b = Lin(c.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>High-contrast text on top of an accent fill.</summary>
    public static Color PickOnAccent(Color accent) =>
        Luminance(accent) > 0.55
            ? Color.Parse("#0a0a0a")
            : Color.Parse("#f2f0eb");
}
