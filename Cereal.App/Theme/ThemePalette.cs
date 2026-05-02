using Avalonia.Controls;
using Avalonia.Media;
using Cereal.App.Models;

namespace Cereal.App.Theme;

/// <summary>Applies a full <see cref="AppTheme"/> + optional accent override to <see cref="Application"/> resources.</summary>
public static class ThemePalette
{
    public static void Apply(IResourceDictionary res, AppTheme theme, string? accentOverride)
    {
        void SetColor(string key, string hex)
        {
            if (Color.TryParse(hex, out var c))
                res[key] = c;
        }

        SetColor(ThemeResourceKeys.Void,    theme.Void);
        SetColor(ThemeResourceKeys.Surface, theme.Surface);
        SetColor(ThemeResourceKeys.Card,    theme.Card);
        SetColor(ThemeResourceKeys.CardUp,  theme.CardUp);
        SetColor(ThemeResourceKeys.Text,    theme.Text);
        SetColor(ThemeResourceKeys.Text2,   theme.Text2);
        SetColor(ThemeResourceKeys.Text3,   theme.Text3);
        SetColor(ThemeResourceKeys.Text4,   theme.Text4);
        SetColor(ThemeResourceKeys.BodyBg,  theme.BodyBg);

        res[ThemeResourceKeys.Glass]       = theme.Glass;
        res[ThemeResourceKeys.GlassBorder] = theme.GlassBorder;
        res[ThemeResourceKeys.Glow]        = theme.Glow;

        var accentHex = !string.IsNullOrWhiteSpace(accentOverride) ? accentOverride! : theme.Accent;
        if (!Color.TryParse(accentHex, out var accent))
            Color.TryParse(theme.Accent, out accent);

        SetColor(ThemeResourceKeys.Accent, accentHex);

        // Derived accent states (primary buttons, focus rings)
        SetColor(ThemeResourceKeys.AccentHover,   ToHex(ThemeColorMath.Lighten(accent, 0.14)));
        SetColor(ThemeResourceKeys.AccentPressed,  ToHex(ThemeColorMath.Darken(accent, 0.12)));
        SetColor(ThemeResourceKeys.OnAccent,      ToHex(ThemeColorMath.PickOnAccent(accent)));
        var focusC = ThemeColorMath.WithAlpha(accent, 100);
        SetColor(ThemeResourceKeys.FocusRing, ToHex(focusC));
        // Avalonia parses BoxShadows from a string (see default styles); color must be #AARRGGBB
        // 1px outline to match legacy control chrome
        res[ThemeResourceKeys.FocusRingBoxShadow] = $"0 0 0 1 {ToHex(focusC)}";
        res[ThemeResourceKeys.FocusRingBoxShadowInset] = $"inset 0 0 0 1 {ToHex(focusC)}";
        SetColor(ThemeResourceKeys.AccentSoft, ToHex(ThemeColorMath.WithAlpha(accent, 31)));

        if (Color.TryParse(theme.Text3, out var t3))
        {
            SetColor(ThemeResourceKeys.ScrollThumb, ToHex(ThemeColorMath.WithAlpha(t3, 77)));
            SetColor(ThemeResourceKeys.ScrollThumbHover, ToHex(ThemeColorMath.WithAlpha(t3, 120)));
        }
    }

    private static string ToHex(Color c) => $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";
}
