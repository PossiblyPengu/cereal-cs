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

    public void ApplyCurrent() => Apply(_settings.Get().Theme);

    public void Apply(string themeId)
    {
        var theme = AppThemes.Find(themeId) ?? AppThemes.All[0];
        Apply(theme);
    }

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is null) return;
        var res = Application.Current.Resources;

        SetColor(res, "ThemeVoidColor",    theme.Void);
        SetColor(res, "ThemeSurfaceColor", theme.Surface);
        SetColor(res, "ThemeCardColor",    theme.Card);
        SetColor(res, "ThemeAccentColor",  theme.Accent);
        SetColor(res, "ThemeTextColor",    theme.Text);
        SetColor(res, "ThemeText2Color",   theme.Text2);
    }

    private static void SetColor(Avalonia.Controls.IResourceDictionary res, string key, string hex)
    {
        if (Color.TryParse(hex, out var c))
            res[key] = c;
    }
}
