using Velopack;
using Velopack.Sources;
using VeloUpdateInfo = Velopack.UpdateInfo;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Wraps Velopack's update flow.
/// Check → download → apply-and-restart, with progress reporting.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string GitHubRepo = "PossiblyPengu/cereal-cs";
    private UpdateManager? _mgr;
    private VeloUpdateInfo? _pending;

    public UpdateService()
    {
        try
        {
            _mgr = new UpdateManager(
                new GithubSource($"https://github.com/{GitHubRepo}", null, false));
        }
        catch (Exception ex)
        {
            Log.Information(ex, "[update] Velopack UpdateManager unavailable — updates disabled");
        }
    }

    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (_mgr is null) return null;
        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info is null) return null;
            _pending = info;
            return new AppUpdateInfo(
                _mgr.CurrentVersion?.ToString() ?? "?",
                info.TargetFullRelease?.Version?.ToString() ?? "?",
                info.TargetFullRelease?.NotesMarkdown ?? "");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[update] CheckForUpdates failed (may be outside Velopack install)");
            return null;
        }
    }

    public async Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_mgr is null || _pending is null)
            throw new InvalidOperationException("No pending update — call CheckAsync first");
        await _mgr.DownloadUpdatesAsync(_pending, p => progress?.Report(p));
    }

    public void ApplyAndRestart()
    {
        if (_mgr is null || _pending is null) return;
        _mgr.ApplyUpdatesAndRestart(_pending);
    }
}
