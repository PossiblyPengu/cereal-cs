using Avalonia;
using Avalonia.Media;
using Cereal.App.Models;

namespace Cereal.App.Services;

public class ThemeService
{
    private readonly SettingsService _settings;

    public ThemeService(SettingsService settings)
    {
        _settings = settings;
    }

    public void ApplyCurrent()
    {
        var s = _settings.Get();
        Apply(s.Theme, s.AccentColor);
    }

    public void Apply(string themeId, string? accentOverride = null)
    {
        var theme = AppThemes.Find(themeId) ?? AppThemes.All[0];
        Apply(theme, accentOverride);
    }

    public static void Apply(AppTheme theme, string? accentOverride = null)
    {
        if (Application.Current is null) return;
        var res = Application.Current.Resources;

        SetColor(res, "ThemeVoidColor",        theme.Void);
        SetColor(res, "ThemeSurfaceColor",     theme.Surface);
        SetColor(res, "ThemeCardColor",        theme.Card);
        SetColor(res, "ThemeCardUpColor",      theme.CardUp);
        SetColor(res, "ThemeTextColor",        theme.Text);
        SetColor(res, "ThemeText2Color",       theme.Text2);
        SetColor(res, "ThemeText3Color",       theme.Text3);
        SetColor(res, "ThemeText4Color",       theme.Text4);
        SetColor(res, "ThemeBodyBgColor",      theme.BodyBg);

        // Custom accent overrides the theme accent when set
        var accent = !string.IsNullOrWhiteSpace(accentOverride) ? accentOverride : theme.Accent;
        SetColor(res, "ThemeAccentColor", accent);

        // RGBA tokens stored as strings (used where alpha blending is needed)
        res["ThemeGlass"]        = theme.Glass;
        res["ThemeGlassBorder"]  = theme.GlassBorder;
        res["ThemeGlow"]         = theme.Glow;
    }

    private static void SetColor(Avalonia.Controls.IResourceDictionary res, string key, string hex)
    {
        if (Color.TryParse(hex, out var c))
            res[key] = c;
    }
}
