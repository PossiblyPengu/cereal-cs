using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;

namespace Cereal.App.ViewModels;

// ─── StartupWizard ViewModel ─────────────────────────────────────────────────
// Mirrors the 7-step wizard from src/components/StartupWizard.tsx.
// Steps: Welcome → Appearance → Performance → Accounts → Behavior → PlayStation
//        → Artwork (SteamGridDB) → Done.
public partial class StartupWizardViewModel : ObservableObject
{
    public const int TotalSteps = 8;

    [ObservableProperty] private int _step = 0;

    // Appearance
    [ObservableProperty] private string _defaultView = "cards";

    // Performance / layout
    [ObservableProperty] private string _starDensity = "normal";  // low | normal | high
    [ObservableProperty] private string _uiScale = "100%";        // 90% | 100% | 110% | 125%
    [ObservableProperty] private bool _showAnimations = true;
    [ObservableProperty] private string _toolbarPosition = "top"; // top | bottom | left | right

    // Behavior
    [ObservableProperty] private bool _minimizeOnLaunch;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _discordPresence = true;
    [ObservableProperty] private bool _autoSyncPlaytime = true;

    // Final: artwork
    [ObservableProperty] private string _steamGridDbKey = string.Empty;

    public bool CanGoBack => Step > 0;
    public string NextLabel => Step >= TotalSteps - 1 ? "Get started" : "Next";

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(NextLabel));
        for (int i = 0; i < TotalSteps; i++)
            OnPropertyChanged($"DotBrush{i}");
    }

    // Dot brushes — one per step. Accessed dynamically via indexer.
    private IBrush DotFor(int i) =>
        i == Step
            ? new SolidColorBrush(Color.Parse("#d4a853"))
            : new SolidColorBrush(Colors.White, 0.15);

    public IBrush DotBrush0 => DotFor(0);
    public IBrush DotBrush1 => DotFor(1);
    public IBrush DotBrush2 => DotFor(2);
    public IBrush DotBrush3 => DotFor(3);
    public IBrush DotBrush4 => DotFor(4);
    public IBrush DotBrush5 => DotFor(5);
    public IBrush DotBrush6 => DotFor(6);
    public IBrush DotBrush7 => DotFor(7);

    [RelayCommand]
    private void Next()
    {
        if (Step < TotalSteps - 1) Step++;
        else Finish();
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0) Step--;
    }

    public event EventHandler<WizardResult>? Completed;

    private void Finish() =>
        Completed?.Invoke(this, new WizardResult(
            DefaultView, SteamGridDbKey, StarDensity, UiScale, ShowAnimations,
            ToolbarPosition, MinimizeOnLaunch, CloseToTray, DiscordPresence, AutoSyncPlaytime));
}

public sealed record WizardResult(
    string DefaultView,
    string SteamGridDbKey,
    string StarDensity,
    string UiScale,
    bool ShowAnimations,
    string ToolbarPosition,
    bool MinimizeOnLaunch,
    bool CloseToTray,
    bool DiscordPresence,
    bool AutoSyncPlaytime);

// Converter used by the wizard AXAML for step visibility and selectable card highlights.
public sealed class WizardStepConverter : IValueConverter
{
    public static WizardStepConverter IsStep { get; } = new(mode: "step");
    public static WizardStepConverter ViewCard { get; } = new(mode: "card");

    private readonly string _mode;
    private WizardStepConverter(string mode) => _mode = mode;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (_mode == "step")
        {
            // Supports both int (current wizard step) and string (current chip selection).
            if (value is int step)
            {
                var target = parameter is string p && int.TryParse(p, out var n) ? n : -1;
                return step == target;
            }
            if (value is string s)
                return string.Equals(s, parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
            return false;
        }
        if (_mode == "card")
        {
            var view = value?.ToString() ?? "";
            var param = parameter?.ToString() ?? "";
            return string.Equals(view, param, StringComparison.OrdinalIgnoreCase)
                ? (object)new SolidColorBrush(Colors.White, 0.1)
                : new SolidColorBrush(Colors.White, 0.04);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
