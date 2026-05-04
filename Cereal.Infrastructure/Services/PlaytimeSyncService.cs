using System.Text.RegularExpressions;
using Cereal.Core.Services;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Reads Steam's per-user localconfig.vdf files to sync playtime_forever values
/// back into the database.  Runs at startup and on a periodic timer (every 30 min).
/// </summary>
public sealed partial class PlaytimeSyncService : IDisposable
{
    private readonly IGameService _games;
    private readonly Timer _timer;

    public PlaytimeSyncService(IGameService games)
    {
        _games = games;
        _timer = new Timer(_ => _ = SyncAsync(), null,
            TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(30));
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        try
        {
            var steamRoot = FindSteamRoot();
            if (steamRoot is null) return;

            var userdataDir = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdataDir)) return;

            var playtime = new Dictionary<string, int>();

            foreach (var userDir in Directory.EnumerateDirectories(userdataDir))
            {
                var cfg = Path.Combine(userDir, "config", "localconfig.vdf");
                if (!File.Exists(cfg)) continue;

                string vdf;
                try { vdf = await File.ReadAllTextAsync(cfg, ct); }
                catch { continue; }

                foreach (Match m in PlaytimeBlock().Matches(vdf))
                {
                    var appId = m.Groups[1].Value;
                    if (!int.TryParse(m.Groups[2].Value, out var mins) || mins <= 0) continue;
                    if (!playtime.TryGetValue(appId, out var prev) || mins > prev)
                        playtime[appId] = mins;
                }
            }

            var allGames = await _games.GetAllAsync(ct);
            var steamGames = allGames.Where(g => g.Platform == "steam" && !string.IsNullOrEmpty(g.PlatformId));
            int updated = 0;

            foreach (var game in steamGames)
            {
                if (!playtime.TryGetValue(game.PlatformId!, out var mins)) continue;
                if (mins <= game.PlaytimeMinutes) continue;

                await _games.AddPlaytimeAsync(game.Id, mins - game.PlaytimeMinutes,
                    DateTimeOffset.UtcNow, ct);
                updated++;
            }

            if (updated > 0)
                Log.Information("[playtime-sync] Updated {Count} Steam playtime entries", updated);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[playtime-sync] Sync failed");
        }
    }

    public void Dispose() => _timer.Dispose();

    // ── Steam root detection (shared with SteamProvider) ─────────────────────

    internal static string? FindSteamRoot()
    {
        // Windows: check registry first
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string p && Directory.Exists(p))
                    return p;
            }
            catch { /* fall through */ }
        }

        // Common default paths
        string[] defaults = OperatingSystem.IsWindows()
            ? [@"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam"]
            : [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam")];

        return defaults.FirstOrDefault(Directory.Exists);
    }

    [GeneratedRegex(@"""(\d+)""[^}]+?""playtime_forever""\s+""(\d+)""", RegexOptions.Singleline)]
    private static partial Regex PlaytimeBlock();
}
