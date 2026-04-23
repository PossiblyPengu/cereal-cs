// ─── Playtime Sync ───────────────────────────────────────────────────────────
// Port of electron/modules/metadata/detectionIpc.js `playtime:sync`.
// Reads Steam's per-user localconfig.vdf files for `playtime_forever` values
// and updates the matching games in our DB. This is the only local source
// available without SQLite dependencies (Epic/GOG don't expose playtime locally).

using System.Text.RegularExpressions;
using Cereal.App.Models;
using Cereal.App.Services.Providers;
using Serilog;

namespace Cereal.App.Services;

public sealed class PlaytimeSyncResult
{
    public int UpdatedCount { get; init; }
    public List<PlaytimeUpdate> Updates { get; init; } = [];
    public string? Error { get; init; }
}

public sealed record PlaytimeUpdate(string Id, string Name, int Minutes, string Source);

public sealed partial class PlaytimeSyncService
{
    private readonly DatabaseService _db;

    public PlaytimeSyncService(DatabaseService db) => _db = db;

    // Parse `playtime_forever` values from localconfig.vdf for every Steam user
    // folder found under <steam-root>/userdata.
    public Task<PlaytimeSyncResult> SyncAsync() => Task.Run(() =>
    {
        var updates = new List<PlaytimeUpdate>();
        try
        {
            var steamRoot = SteamProvider.FindSteamRoot();
            if (steamRoot is null)
                return new PlaytimeSyncResult { Error = "Steam installation not found." };

            var userdataDir = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdataDir))
                return new PlaytimeSyncResult { Error = "Steam userdata directory missing — sign in to Steam at least once." };

            foreach (var userDir in Directory.EnumerateDirectories(userdataDir))
            {
                var localConfig = Path.Combine(userDir, "config", "localconfig.vdf");
                if (!File.Exists(localConfig)) continue;

                string vdf;
                try { vdf = File.ReadAllText(localConfig); }
                catch (Exception ex) { Log.Warning(ex, "[playtime] failed reading {Path}", localConfig); continue; }

                var playtime = new Dictionary<string, int>();
                foreach (Match m in PlaytimeBlock().Matches(vdf))
                {
                    var appId = m.Groups[1].Value;
                    if (!int.TryParse(m.Groups[2].Value, out var minutes) || minutes <= 0) continue;
                    if (!playtime.TryGetValue(appId, out var prev) || minutes > prev)
                        playtime[appId] = minutes;
                }

                foreach (var (appId, minutes) in playtime)
                {
                    var game = _db.Db.Games.FirstOrDefault(g => g.Platform == "steam" && g.PlatformId == appId);
                    if (game is null) continue;
                    if (minutes > (game.PlaytimeMinutes ?? 0))
                    {
                        game.PlaytimeMinutes = minutes;
                        updates.Add(new PlaytimeUpdate(game.Id, game.Name ?? appId, minutes, "steam"));
                    }
                }
            }

            if (updates.Count > 0)
                _db.Save();

            return new PlaytimeSyncResult { UpdatedCount = updates.Count, Updates = updates };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[playtime] sync failed");
            return new PlaytimeSyncResult { Error = ex.Message };
        }
    });

    // Matches an appid → playtime_forever pair inside localconfig.vdf.
    // Example snippet:
    //   "220" { "LastPlayed" "1700000000" "Playtime" "..." "playtime_forever" "42" ... }
    [GeneratedRegex("""
        "(\d+)"\s*\{[^}]*?"playtime_forever"\s+"(\d+)"[^}]*?\}
        """, RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex PlaytimeBlock();
}
