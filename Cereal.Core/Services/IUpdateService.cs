namespace Cereal.Core.Services;

public interface IUpdateService
{
    /// <summary>Check GitHub for a newer release.  Returns null if already up-to-date.</summary>
    Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default);

    /// <summary>Download the latest release.  Progress reported via <paramref name="progress"/>.</summary>
    Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>Apply the downloaded update and restart the application.</summary>
    void ApplyAndRestart();
}

/// <summary>Metadata for an available update (version string + release notes).</summary>
public sealed record AppUpdateInfo(string CurrentVersion, string NewVersion, string ReleaseNotes);
