using Cereal.Core.Models;

namespace Cereal.Core.Services;

public interface ICoverService
{
    /// <summary>
    /// Enqueue a cover download for a game.
    /// Progress is reported by firing <see cref="Messaging.LibraryMessages.GameCoverUpdatedMessage"/>.
    /// </summary>
    void EnqueueDownload(string gameId, string? coverUrl, string? headerUrl);

    /// <summary>Fetch cover candidates from SteamGridDB for the given game name.</summary>
    Task<IReadOnlyList<CoverCandidate>> SearchAsync(string gameName, CancellationToken ct = default);

    /// <summary>Download a specific cover URL and store it locally for a game.</summary>
    Task<string?> DownloadAndSaveAsync(string gameId, string url, CoverType type,
        CancellationToken ct = default);
}

public enum CoverType { Cover, Header }

public sealed record CoverCandidate(string Url, string ThumbnailUrl, int Width, int Height, string? Style);
