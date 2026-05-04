using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.App.ViewModels.Settings;

/// <summary>
/// About / update section of Settings.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updates;

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    [ObservableProperty] private bool    _hasUpdate;
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private double  _downloadProgress;
    [ObservableProperty] private bool    _isChecking;
    [ObservableProperty] private bool    _isDownloading;
    [ObservableProperty] private string? _statusMessage;

    public AboutViewModel(IUpdateService updates)
    {
        _updates = updates;
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsChecking = true;
        StatusMessage = "Checking for updates…";
        try
        {
            var info = await _updates.CheckAsync();
            if (info is not null)
            {
                HasUpdate     = true;
                UpdateVersion = info.NewVersion;
                StatusMessage = $"Update {info.NewVersion} available.";
            }
            else
            {
                HasUpdate     = false;
                StatusMessage = "You're up to date.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Check failed: {ex.Message}";
            Log.Warning(ex, "[About] CheckForUpdate failed");
        }
        finally { IsChecking = false; }
    }

    [RelayCommand]
    private async Task DownloadAndApplyAsync()
    {
        IsDownloading = true;
        StatusMessage = "Downloading update…";
        try
        {
            await _updates.DownloadAsync(new Progress<int>(p => DownloadProgress = p));
            StatusMessage = "Restarting to apply update…";
            _updates.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
            Log.Warning(ex, "[About] DownloadAndApply failed");
        }
        finally { IsDownloading = false; }
    }
}
