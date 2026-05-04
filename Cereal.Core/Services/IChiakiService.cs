namespace Cereal.Core.Services;

/// <summary>
/// PlayStation Remote Play session via Chiaki.
/// </summary>
public interface IChiakiService
{
    /// <summary>True if Chiaki is installed and the path is configured.</summary>
    bool IsAvailable { get; }

    /// <summary>Launch a PS Remote Play session for the given game.</summary>
    Task LaunchRemoteAsync(string gameId, string host, string? psnAccountId = null,
        CancellationToken ct = default);

    /// <summary>Discover PlayStation consoles on the local network.</summary>
    Task<IReadOnlyList<DiscoveredConsoleInfo>> DiscoverAsync(CancellationToken ct = default);
}

/// <summary>Minimal info returned by console discovery.</summary>
public sealed record DiscoveredConsoleInfo(
    string Host,
    string State,
    string? Name,
    string? Type,
    string? FirmwareVersion);
