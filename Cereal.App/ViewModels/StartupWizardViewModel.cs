using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using System.Globalization;

namespace Cereal.App.ViewModels;

// ─── StartupWizard ViewModel ─────────────────────────────────────────────────
// Mirrors the 7-step wizard from src/components/StartupWizard.tsx.
// Steps: Welcome → Appearance → Performance → Accounts → Behavior
//        → PlayStation → Done.
public partial class StartupWizardViewModel : ObservableObject
{
    public const int TotalSteps = 7;
    public static readonly AppTheme[] ThemeOptions = AppThemes.All;
    public const string DefaultAccent = "#d4a853";

    [ObservableProperty] private int _step = 0;

    // Appearance
    [ObservableProperty] private string _defaultView = "orbit";
    [ObservableProperty] private string _theme = "midnight";
    [ObservableProperty] private string _accentColor = DefaultAccent;

    // Performance / layout
    [ObservableProperty] private string _starDensity = "normal";  // low | normal | high
    [ObservableProperty] private string _uiScale = "100%";        // 90% | 100% | 110% | 125%
    [ObservableProperty] private bool _showAnimations = true;
    [ObservableProperty] private string _toolbarPosition = "top"; // top | bottom | left | right
    [ObservableProperty] private string _hardwareTier = "Balanced";
    [ObservableProperty] private string _performanceRecommendation = "Balanced defaults recommended";

    // Behavior
    [ObservableProperty] private bool _minimizeOnLaunch;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _discordPresence = false;
    [ObservableProperty] private bool _autoSyncPlaytime = false;

    // PlayStation
    [ObservableProperty] private bool _chiakiReady;

    // Final: artwork
    [ObservableProperty] private string _steamGridDbKey = string.Empty;

    public bool CanGoBack => Step > 0;
    public bool CanSkip => Step > 0 && Step < TotalSteps - 1;
    public string NextLabel => Step >= TotalSteps - 1 ? "Launch Cereal" : "Next";
    public string AppearanceSummary => $"{Theme} theme, {AccentColor}, default {DefaultView}";
    public string PerformanceSummary => $"{StarDensity} stars, {UiScale} UI, toolbar {ToolbarPosition}, animations {(ShowAnimations ? "on" : "off")}";
    [ObservableProperty] private string _accountsSummary = "No connected platforms yet";
    public string BehaviorSummary => $"Minimize on launch {(MinimizeOnLaunch ? "on" : "off")}, close to tray {(CloseToTray ? "on" : "off")}, Discord {(DiscordPresence ? "on" : "off")}, auto-sync playtime {(AutoSyncPlaytime ? "on" : "off")}";
    public string PlayStationSummary => ChiakiReady ? "chiaki-ng installed" : "chiaki-ng not detected";
    public string ArtworkSummary => string.IsNullOrWhiteSpace(SteamGridDbKey) ? "SteamGridDB key not set" : "SteamGridDB key configured";

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(NextLabel));
        for (int i = 0; i < TotalSteps; i++)
            OnPropertyChanged($"DotBrush{i}");
    }

    public IEnumerable<ThemeSwatchViewModel> ThemeSwatches =>
        ThemeOptions.Select(t => new ThemeSwatchViewModel(t, Theme));

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

    [RelayCommand]
    private void Skip()
    {
        if (CanSkip) Step++;
    }

    [RelayCommand]
    private void SelectTheme(string? themeId)
    {
        if (!string.IsNullOrWhiteSpace(themeId))
            Theme = themeId;
    }

    [RelayCommand]
    private void ResetAccentColor() => AccentColor = DefaultAccent;

    [RelayCommand]
    private void ApplyRecommendedPerformance()
    {
        switch (HardwareTier)
        {
            case "High":
                StarDensity = "high";
                UiScale = "100%";
                ShowAnimations = true;
                break;
            case "Low":
                StarDensity = "low";
                UiScale = "110%";
                ShowAnimations = false;
                break;
            default:
                StarDensity = "normal";
                UiScale = "100%";
                ShowAnimations = true;
                break;
        }
    }

    partial void OnThemeChanged(string value) => OnPropertyChanged(nameof(ThemeSwatches));
    partial void OnDefaultViewChanged(string value) => OnPropertyChanged(nameof(AppearanceSummary));
    partial void OnAccentColorChanged(string value) => OnPropertyChanged(nameof(AppearanceSummary));
    partial void OnStarDensityChanged(string value) => OnPropertyChanged(nameof(PerformanceSummary));
    partial void OnUiScaleChanged(string value) => OnPropertyChanged(nameof(PerformanceSummary));
    partial void OnShowAnimationsChanged(bool value) => OnPropertyChanged(nameof(PerformanceSummary));
    partial void OnToolbarPositionChanged(string value) => OnPropertyChanged(nameof(PerformanceSummary));
    partial void OnMinimizeOnLaunchChanged(bool value) => OnPropertyChanged(nameof(BehaviorSummary));
    partial void OnCloseToTrayChanged(bool value) => OnPropertyChanged(nameof(BehaviorSummary));
    partial void OnDiscordPresenceChanged(bool value) => OnPropertyChanged(nameof(BehaviorSummary));
    partial void OnAutoSyncPlaytimeChanged(bool value) => OnPropertyChanged(nameof(BehaviorSummary));
    partial void OnChiakiReadyChanged(bool value) => OnPropertyChanged(nameof(PlayStationSummary));
    partial void OnSteamGridDbKeyChanged(string value) => OnPropertyChanged(nameof(ArtworkSummary));

    public event EventHandler<WizardResult>? Completed;

    private void Finish() =>
        Completed?.Invoke(this, new WizardResult(
            DefaultView, Theme, AccentColor, SteamGridDbKey, StarDensity, UiScale, ShowAnimations,
            ToolbarPosition, MinimizeOnLaunch, CloseToTray, DiscordPresence, AutoSyncPlaytime));
}

public sealed record WizardResult(
    string DefaultView,
    string Theme,
    string AccentColor,
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
