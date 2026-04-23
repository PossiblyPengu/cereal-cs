using Serilog;
using Velopack;
using Velopack.Sources;

namespace Cereal.App.Services;

public sealed class UpdateAvailableArgs : EventArgs
{
    public string CurrentVersion { get; init; } = "";
    public string NewVersion { get; init; } = "";
}

public sealed class UpdateService
{
    private const string GitHubRepo = "PossiblyPengu/cereal-cs";
    private UpdateManager? _mgr;
    private UpdateInfo? _pendingUpdate;

    public event EventHandler<UpdateAvailableArgs>? UpdateAvailable;
    public event EventHandler? UpdateReady;

    public bool IsUpdateReady => _pendingUpdate is not null;

    // ─── Check ────────────────────────────────────────────────────────────────

    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            _mgr ??= new UpdateManager(new GithubSource($"https://github.com/{GitHubRepo}", null, false));
            var info = await _mgr.CheckForUpdatesAsync();
            if (info is null) return;

            _pendingUpdate = info;
            var newVer = info.TargetFullRelease?.Version?.ToString() ?? "unknown";
            var curVer = _mgr.CurrentVersion?.ToString() ?? "unknown";
            Log.Information("[update] New version available: {Version}", newVer);
            UpdateAvailable?.Invoke(this, new UpdateAvailableArgs
            {
                CurrentVersion = curVer,
                NewVersion     = newVer,
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[update] Check failed (may be running outside Velopack install)");
        }
    }

    // ─── Download + install ───────────────────────────────────────────────────

    public async Task DownloadAndInstallAsync(CancellationToken ct = default)
    {
        if (_mgr is null || _pendingUpdate is null) return;
        try
        {
            await _mgr.DownloadUpdatesAsync(_pendingUpdate);
            Log.Information("[update] Download complete — will restart to apply");
            UpdateReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[update] Download failed");
        }
    }

    public void ApplyAndRestart()
    {
        if (_mgr is null || _pendingUpdate is null) return;
        _mgr.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
