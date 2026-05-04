using System.Diagnostics;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using Cereal.Core.Services;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Platform-aware game launcher.  Builds the appropriate URI / executable invocation
/// for each platform, records session start for playtime tracking, and raises
/// window-minimize requests via the messaging bus.
/// </summary>
public sealed class LaunchService(
    IGameService games,
    ISettingsService settings,
    IMessenger messenger) : ILaunchService
{
    // gameId → session start time (+ optional Process for native exes)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset Start, Process? Proc)> _sessions = [];

    public async Task LaunchAsync(Game game, CancellationToken ct = default)
    {
        var platform = game.Platform.ToLowerInvariant();

        // Xbox Cloud — delegate to the XcloudService integration (Phase G)
        if (platform == "xbox" && !string.IsNullOrEmpty(game.StreamUrl))
        {
            messenger.Send(new NavigateToPanelMessage("xcloud", game));
            RecordSession(game.Id, null);
            return;
        }

        // Custom native executable
        if (platform == "custom" && !string.IsNullOrEmpty(game.ExePath))
        {
            await LaunchExeAsync(game, ct);
            return;
        }

        // Platform URI scheme
        var uris = BuildUris(game);
        foreach (var uri in uris)
        {
            if (await TryOpenUriAsync(uri, ct))
            {
                RecordSession(game.Id, null);
                MaybeMinimize();
                return;
            }
        }

        Log.Warning("[launch] All URIs failed for {Name} ({Platform})", game.Name, game.Platform);
    }

    public async Task StopTrackingAsync(string gameId, CancellationToken ct = default)
    {
        if (!_sessions.TryRemove(gameId, out var s)) return;

        var elapsed = (int)(DateTimeOffset.UtcNow - s.Start).TotalMinutes;
        if (elapsed <= 0) return;

        await games.AddPlaytimeAsync(gameId, elapsed, DateTimeOffset.UtcNow, ct);
        Log.Information("[launch] Recorded {Minutes}m playtime for {Id}", elapsed, gameId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RecordSession(string gameId, Process? proc) =>
        _sessions[gameId] = (DateTimeOffset.UtcNow, proc);

    private void MaybeMinimize()
    {
        if (settings.Current.MinimizeOnLaunch)
            messenger.Send(new MinimizeWindowMessage());
    }

    private async Task LaunchExeAsync(Game game, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(game.ExePath!)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(game.ExePath) ?? "",
            };
            var proc = Process.Start(psi);
            RecordSession(game.Id, proc);
            MaybeMinimize();

            if (proc is not null)
            {
                await proc.WaitForExitAsync(ct);
                await StopTrackingAsync(game.Id, ct);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[launch] Failed to start exe: {Path}", game.ExePath);
        }
    }

    private static async Task<bool> TryOpenUriAsync(string uri, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(uri) { UseShellExecute = true };
            Process.Start(psi);
            await Task.Delay(500, ct); // brief wait for the OS to swallow the URI
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[launch] URI failed: {Uri}", uri);
            return false;
        }
    }

    private static IEnumerable<string> BuildUris(Game game)
    {
        var id = game.PlatformId ?? "";
        return game.Platform.ToLowerInvariant() switch
        {
            "steam"     => [$"steam://rungameid/{id}"],
            "epic"      => [$"com.epicgames.launcher://apps/{game.EpicAppName ?? id}?action=launch&silent=true"],
            "gog"       => [$"goggalaxy://rungame/{id}"],
            "ea"        => [$"origin://launchgame/{game.EaOfferId ?? id}",
                            $"ea://launch/{game.EaOfferId ?? id}/1"],
            "ubisoft"   => [$"uplay://launch/{game.UbisoftGameId ?? id}/0"],
            "battlenet" => [$"battlenet://{id}"],
            "itchio"    => [game.StoreUrl ?? $"https://itch.io/app"],
            "xbox"      => [$"ms-xbl-{id}://"],
            _           => [],
        };
    }
}
