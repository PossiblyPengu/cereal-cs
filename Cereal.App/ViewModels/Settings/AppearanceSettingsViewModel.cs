using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using CoreSettings = Cereal.Core.Models.Settings;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.App.ViewModels.Settings;

/// <summary>
/// Appearance &amp; theme settings section.
/// </summary>
public sealed partial class AppearanceSettingsViewModel : ObservableObject,
    IRecipient<SettingsChangedMessage>
{
    private readonly ISettingsService _settings;
    private readonly IMessenger _messenger;

    public ObservableCollection<AppTheme> BuiltInThemes { get; } =
        new(AppThemes.All);

    [ObservableProperty] private AppTheme _selectedTheme = AppThemes.All[0];
    [ObservableProperty] private string _toolbarPosition = "top";
    [ObservableProperty] private string _navPosition     = "top";
    [ObservableProperty] private bool   _closeToTray;
    [ObservableProperty] private bool   _minimizeToTray;
    [ObservableProperty] private bool   _minimizeOnLaunch;
    [ObservableProperty] private bool   _rememberWindowBounds;

    public AppearanceSettingsViewModel(ISettingsService settings, IMessenger messenger)
    {
        _settings = settings;
        _messenger = messenger;
        messenger.Register(this);
        _ = LoadAsync();
    }

    public void Receive(SettingsChangedMessage msg) => ApplySettings(msg.Settings);

    partial void OnSelectedThemeChanged(AppTheme value)   => _ = SaveAsync();
    partial void OnToolbarPositionChanged(string value)   => _ = SaveAsync();
    partial void OnNavPositionChanged(string value)       => _ = SaveAsync();
    partial void OnCloseToTrayChanged(bool value)         => _ = SaveAsync();
    partial void OnMinimizeToTrayChanged(bool value)      => _ = SaveAsync();
    partial void OnMinimizeOnLaunchChanged(bool value)    => _ = SaveAsync();
    partial void OnRememberWindowBoundsChanged(bool value) => _ = SaveAsync();

    private async Task LoadAsync()
    {
        var s = await _settings.LoadAsync();
        ApplySettings(s);
    }

    private void ApplySettings(CoreSettings s)
    {
        SelectedTheme      = AppThemes.All.FirstOrDefault(t => t.Id == s.Theme) ?? AppThemes.All[0];
        ToolbarPosition    = s.ToolbarPosition ?? "top";
        NavPosition        = s.NavPosition     ?? "top";
        CloseToTray        = s.CloseToTray;
        MinimizeToTray     = s.MinimizeToTray;
        MinimizeOnLaunch   = s.MinimizeOnLaunch;
        RememberWindowBounds = s.RememberWindowBounds;
    }

    private async Task SaveAsync()
    {
        try
        {
            var s = _settings.Current with
            {
                Theme              = SelectedTheme.Id,
                ToolbarPosition    = ToolbarPosition,
                NavPosition        = NavPosition,
                CloseToTray        = CloseToTray,
                MinimizeToTray     = MinimizeToTray,
                MinimizeOnLaunch   = MinimizeOnLaunch,
                RememberWindowBounds = RememberWindowBounds,
            };
            await _settings.SaveAsync(s);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AppearanceSettings] Save failed");
        }
    }
}
