using Avalonia;
using Cereal.App.Models;
using Cereal.App.Theme;

namespace Cereal.App.Services;

/// <summary>Applies the saved <see cref="Models.Settings.Theme"/> + <see cref="Models.Settings.AccentColor"/> to <see cref="Application"/> resources via <see cref="ThemePalette"/>.</summary>
public class ThemeService
{
    private readonly SettingsService _settings;

    public ThemeService(SettingsService settings) => _settings = settings;

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
        ThemePalette.Apply(Application.Current.Resources, theme, accentOverride);
    }
}
