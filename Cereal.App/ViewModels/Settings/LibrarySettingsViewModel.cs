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
/// Library-related settings (scan paths, SteamGridDB key, startup behaviour).
/// </summary>
public sealed partial class LibrarySettingsViewModel : ObservableObject,
    IRecipient<SettingsChangedMessage>
{
    private readonly ISettingsService _settings;
    private readonly IGameService _games;
    private readonly IMessenger _messenger;

    [ObservableProperty] private string? _steamGridDbKey;
    [ObservableProperty] private bool    _launchOnStartup;
    [ObservableProperty] private bool    _autoSyncPlaytime;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private int _totalGames;
    [ObservableProperty] private int _installedGames;

    public LibrarySettingsViewModel(
        ISettingsService settings,
        IGameService games,
        IMessenger messenger)
    {
        _settings  = settings;
        _games     = games;
        _messenger = messenger;
        messenger.Register(this);
        _ = LoadAsync();
    }

    public void Receive(SettingsChangedMessage msg) => ApplySettings(msg.Settings);

    partial void OnSteamGridDbKeyChanged(string? value)  => _ = SaveAsync();
    partial void OnLaunchOnStartupChanged(bool value)    => _ = SaveAsync();
    partial void OnAutoSyncPlaytimeChanged(bool value)   => _ = SaveAsync();

    private async Task LoadAsync()
    {
        var s = await _settings.LoadAsync();
        ApplySettings(s);
        await RefreshStatsAsync();
    }

    private void ApplySettings(CoreSettings s)
    {
        SteamGridDbKey  = s.SteamGridDbKey;
        LaunchOnStartup = s.LaunchOnStartup;
        AutoSyncPlaytime = s.AutoSyncPlaytime;
    }

    private async Task SaveAsync()
    {
        try
        {
            var s = _settings.Current with
            {
                SteamGridDbKey  = SteamGridDbKey,
                LaunchOnStartup = LaunchOnStartup,
                AutoSyncPlaytime = AutoSyncPlaytime,
            };
            await _settings.SaveAsync(s);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LibrarySettings] Save failed");
        }
    }

    [RelayCommand]
    private async Task ClearLibraryAsync()
    {
        try
        {
            await _games.ClearLibraryAsync();
            StatusMessage = "Library cleared.";
            await RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            Log.Warning(ex, "[LibrarySettings] ClearLibrary failed");
        }
    }

    [RelayCommand]
    private async Task RefreshStatsAsync()
    {
        TotalGames     = await _games.CountAsync();
        InstalledGames = await _games.CountInstalledAsync();
    }
}
