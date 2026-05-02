using Avalonia.Media;
using Cereal.App.Models;

namespace Cereal.App.ViewModels;

public class ThemeSwatchViewModel
{
    public AppTheme Theme { get; }
    public bool IsActive { get; }

    public IBrush VoidBrush    { get; }
    public IBrush AccentBrush  { get; }
    public IBrush SurfaceBrush { get; }
    public IBrush TextBrush    { get; }
    public IBrush BorderBrush  { get; }

    public string Id    => Theme.Id;
    public string Label => Theme.Label;

    public ThemeSwatchViewModel(AppTheme theme, string activeId)
    {
        Theme    = theme;
        IsActive = theme.Id == activeId;

        VoidBrush    = ParseBrush(theme.Void);
        AccentBrush  = ParseBrush(theme.Accent);
        SurfaceBrush = ParseBrush(theme.Card);
        TextBrush    = ParseBrush(theme.Text);
        BorderBrush  = IsActive
            ? ParseBrush(theme.Accent)
            : new SolidColorBrush(Colors.White, 0.1);
    }

    private static SolidColorBrush ParseBrush(string hex) =>
        Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : new SolidColorBrush(Colors.Gray);
}
