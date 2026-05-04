namespace Cereal.Core.Services;

/// <summary>
/// System Media Transport Controls (SMTC) / Windows media key integration.
/// On non-Windows platforms all methods are no-ops.
/// </summary>
public interface ISmtcService
{
    /// <summary>Send a media key (e.g. play/pause, next, previous).</summary>
    void SendMediaKey(MediaKey key);

    /// <summary>Query the current SMTC session info.  Returns null if unavailable.</summary>
    Task<SmtcSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default);
}

/// <summary>Common Windows media key codes.</summary>
public enum MediaKey { PlayPause, Stop, Next, Previous, VolumeUp, VolumeDown, Mute }

/// <summary>Snapshot of the current SMTC session.</summary>
public sealed record SmtcSessionInfo(
    string? Title,
    string? Artist,
    string? AlbumTitle,
    string? ThumbnailUrl,
    string PlaybackStatus);
