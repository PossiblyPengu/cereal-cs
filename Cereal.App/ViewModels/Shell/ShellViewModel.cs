using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using CoreSettings = Cereal.Core.Models.Settings;
using Cereal.Core.Services;

namespace Cereal.App.ViewModels;

/// <summary>
/// Owns the top-level window state:
/// title-bar text, toolbar/nav position, tray visibility, window bounds persistence.
/// Receives <see cref="SettingsChangedMessage"/> and updates derived geometry so no
/// view needs to call App.Services.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject,
    IRecipient<SettingsChangedMessage>
{
    private readonly ISettingsService _settings;

    public ShellViewModel(ISettingsService settings, IMessenger messenger)
    {
        _settings = settings;
        messenger.Register(this);
        ApplySettings(_settings.Current);
    }

    // ── Window chrome ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _title = "Cereal";

    // ── Toolbar / nav position ─────────────────────────────────────────────────

    [ObservableProperty] private string _toolbarPosition = "top";
    [ObservableProperty] private string _navPosition = "top";

    // Derived geometry — consumed by MainWindow AXAML bindings
    public bool ToolbarIsTop    => ToolbarPosition == "top";
    public bool ToolbarIsBottom => ToolbarPosition == "bottom";
    public bool ToolbarIsLeft   => ToolbarPosition == "left";
    public bool ToolbarIsRight  => ToolbarPosition == "right";

    partial void OnToolbarPositionChanged(string value)
    {
        OnPropertyChanged(nameof(ToolbarIsTop));
        OnPropertyChanged(nameof(ToolbarIsBottom));
        OnPropertyChanged(nameof(ToolbarIsLeft));
        OnPropertyChanged(nameof(ToolbarIsRight));
    }

    // ── Tray ───────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _trayVisible;

    // ── Window bounds ─────────────────────────────────────────────────────────

    [ObservableProperty] private int _windowWidth = 1280;
    [ObservableProperty] private int _windowHeight = 800;
    [ObservableProperty] private int? _windowX;
    [ObservableProperty] private int? _windowY;
    [ObservableProperty] private bool _windowMaximized;

    public async Task SaveWindowBoundsAsync(int x, int y, int w, int h, bool maximized)
    {
        if (!_settings.Current.RememberWindowBounds) return;
        var updated = _settings.Current with
        {
            WindowX = x, WindowY = y,
            WindowWidth = w, WindowHeight = h,
            WindowMaximized = maximized,
        };
        await _settings.SaveAsync(updated);
    }

    // ── Update banner ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool _updateBannerVisible;
    [ObservableProperty] private string _updateBannerVersion = "";
    [ObservableProperty] private int _updateDownloadProgress;

    [RelayCommand]
    private void DismissUpdateBanner() => UpdateBannerVisible = false;

    // ── IRecipient<SettingsChangedMessage> ────────────────────────────────────

    public void Receive(SettingsChangedMessage msg) => ApplySettings(msg.Settings);

    private void ApplySettings(CoreSettings s)
    {
        ToolbarPosition = s.ToolbarPosition;
        NavPosition     = s.NavPosition;
        TrayVisible     = s.CloseToTray || s.MinimizeToTray;
        WindowWidth     = s.WindowWidth;
        WindowHeight    = s.WindowHeight;
        WindowX         = s.WindowX;
        WindowY         = s.WindowY;
        WindowMaximized = s.WindowMaximized;
    }
}
