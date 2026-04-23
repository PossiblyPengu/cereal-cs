using System.Runtime.InteropServices;
using System.Text.Json;
using Cereal.App.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Serilog;

namespace Cereal.App.Services.Providers;

// ─── Battle.net ───────────────────────────────────────────────────────────────

public class BattleNetProvider : IProvider
{
    public string PlatformId => "battlenet";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var dir = Path.Combine(
                Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData",
                "Battle.net", "Agent", "data", "cache");
            if (!Directory.Exists(dir)) return Task.FromResult(new DetectResult(games));

            foreach (var file in Directory.GetFiles(dir, "*.catalog", SearchOption.AllDirectories))
            {
                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
                    var name = doc.TryGetProperty("product_name", out var n) ? n.GetString() : null;
                    if (name is null) continue;
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "battlenet",
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[battlenet] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── EA App ───────────────────────────────────────────────────────────────────

public class EaProvider : IProvider
{
    public string PlatformId => "ea";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(new DetectResult(games));

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Electronic Arts");
            if (key is null) return Task.FromResult(new DetectResult(games));

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    var install = sub?.GetValue("Install Dir") as string
                               ?? sub?.GetValue("InstallDir") as string;
                    if (install is null) continue;
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = subName,
                        Platform = "ea",
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ea] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── Ubisoft Connect ──────────────────────────────────────────────────────────

public class UbisoftProvider : IProvider
{
    public string PlatformId => "ubisoft";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(new DetectResult(games));

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (key is null) return Task.FromResult(new DetectResult(games));

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    var installDir = sub?.GetValue("InstallDir") as string;
                    if (installDir is null) continue;
                    var name = Path.GetFileName(installDir.TrimEnd(Path.DirectorySeparatorChar));
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "ubisoft",
                        PlatformId = subName,
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ubisoft] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── itch.io ─────────────────────────────────────────────────────────────────

public class ItchioProvider(DatabaseService db) : IImportProvider
{
    public string PlatformId => "itchio";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dbPath = Path.Combine(appData, "itch", "db", "butler.db");
            if (!File.Exists(dbPath)) return Task.FromResult(new DetectResult(games));

            // butler.db is a SQLite database. Schema relevant tables:
            //   caves(id, game_id, install_folder_name, last_touched_at)
            //   games(id, title, cover_url, still_cover_url, type, classification)
            var connStr = $"Data Source={dbPath};Mode=ReadOnly;";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT c.id, g.title, g.cover_url, c.install_folder_name, c.last_touched_at
                FROM caves c
                JOIN games g ON c.game_id = g.id
                WHERE g.classification = 'game'
                ORDER BY g.title";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var title = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (string.IsNullOrEmpty(title)) continue;

                games.Add(new Game
                {
                    Id = Guid.NewGuid().ToString("N")[..12],
                    Name = title,
                    Platform = "itchio",
                    PlatformId = reader.IsDBNull(0) ? null : reader.GetString(0),
                    CoverUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                    LastPlayed = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Installed = true,
                    AddedAt = DateTime.UtcNow.ToString("o"),
                });
            }

            Log.Information("[itchio] Found {Count} installed games in butler.db", games.Count);
        }
        catch (SqliteException ex)
        {
            Log.Debug(ex, "[itchio] Could not read butler.db (schema may differ)");
        }
        catch (Exception ex) { Log.Debug(ex, "[itchio] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }

    // ─── Cloud import via itch.io Web API ───────────────────────────────────
    // Paginates /profile/owned-keys with a Bearer token (the user's API key
    // from https://itch.io/user/settings/api-keys). Merges with any local
    // butler.db detection so owned-but-uninstalled games also appear.
    public async Task<ImportResult> ImportLibrary(ImportContext ctx)
    {
        var local = (await DetectInstalled()).Games;
        var all = new List<Game>(local);

        if (string.IsNullOrWhiteSpace(ctx.ApiKey))
        {
            // No key: import local only (matches source's fallback path).
            return MergeIntoDb(all, ctx, apiCount: 0);
        }

        try
        {
            for (var page = 1; page <= 20; page++)
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.itch.io/profile/owned-keys?page={page}");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.ApiKey);
                using var resp = await ctx.Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) break;
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("owned_keys", out var keys) ||
                    keys.ValueKind != JsonValueKind.Array ||
                    keys.GetArrayLength() == 0)
                    break;

                foreach (var key in keys.EnumerateArray())
                {
                    if (!key.TryGetProperty("game", out var g)) continue;
                    var title = g.TryGetProperty("title", out var t) ? t.GetString() : null;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var canon = ProviderUtils.Canonicalize(title);
                    if (all.Any(x => ProviderUtils.Canonicalize(x.Name) == canon)) continue;

                    all.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = title,
                        Platform = "itchio",
                        PlatformId = g.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                            ? id.GetInt64().ToString()
                            : null,
                        CoverUrl = g.TryGetProperty("cover_url", out var cu) ? cu.GetString() : null,
                        Website = g.TryGetProperty("url", out var u) ? u.GetString() : null,
                        Installed = false,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }

                ctx.Notify?.Invoke(new ImportProgress
                {
                    Status = "running",
                    Processed = all.Count,
                    Provider = "itchio",
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[itchio] API pagination failed, continuing with local only");
        }

        return MergeIntoDb(all, ctx, apiCount: all.Count - local.Count);
    }

    private ImportResult MergeIntoDb(List<Game> all, ImportContext ctx, int apiCount)
    {
        var imported = new List<string>();
        var updated  = new List<string>();
        foreach (var g in all)
        {
            var existing = ProviderUtils.FindExisting(db, "itchio", g.PlatformId ?? "", g.Name);
            if (existing is not null)
            {
                var changed = false;
                if (string.IsNullOrEmpty(existing.PlatformId) && !string.IsNullOrEmpty(g.PlatformId))
                    { existing.PlatformId = g.PlatformId; changed = true; }
                if (string.IsNullOrEmpty(existing.CoverUrl) && !string.IsNullOrEmpty(g.CoverUrl))
                    { existing.CoverUrl = g.CoverUrl; changed = true; }
                if (string.IsNullOrEmpty(existing.Website) && !string.IsNullOrEmpty(g.Website))
                    { existing.Website = g.Website; changed = true; }
                if (g.Installed == true && existing.Installed != true)
                    { existing.Installed = true; changed = true; }
                if (changed) updated.Add(existing.Name);
            }
            else
            {
                db.Db.Games.Add(g);
                imported.Add(g.Name);
            }
        }

        db.Save();
        ctx.Notify?.Invoke(new ImportProgress { Status = "done", Processed = all.Count, Provider = "itchio" });
        Log.Information("[itchio] Import complete: {I} new, {U} updated (api={Api})",
            imported.Count, updated.Count, apiCount);
        return new ImportResult(imported, updated, all.Count);
    }
}

// ─── Xbox ────────────────────────────────────────────────────────────────────

public class XboxProvider(DatabaseService db) : IImportProvider
{
    public string PlatformId => "xbox";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var xboxGamesDir = @"C:\XboxGames";
            if (Directory.Exists(xboxGamesDir))
            {
                foreach (var dir in Directory.GetDirectories(xboxGamesDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName == "Content") continue;
                    var name = System.Text.RegularExpressions.Regex.Replace(dirName, @"([A-Z])", " $1").Trim();
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "xbox",
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[xbox] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }

    // ─── Title Hub import ─────────────────────────────────────────────────
    // Port of electron/providers/xbox.js: hits titlehub.xboxlive.com with
    // `XBL3.0 x=<userHash>;<xstsToken>` to pull owned + Game Pass titles and
    // their playtime/last-played metadata.
    public async Task<ImportResult> ImportLibrary(ImportContext ctx)
    {
        var acct = db.Db.Accounts.GetValueOrDefault("xbox");
        if (acct is null || string.IsNullOrEmpty(acct.AccountId))
            return new ImportResult([], [], 0, "Xbox account not connected");

        var xuid = acct.AccountId!;
        var userHash = acct.Extra?.GetValueOrDefault("userHash")?.ToString();
        var xsts     = acct.Extra?.GetValueOrDefault("xstsToken")?.ToString();
        if (string.IsNullOrEmpty(userHash) || string.IsNullOrEmpty(xsts))
            return new ImportResult([], [], 0, "Xbox account not connected (missing XSTS token)");

        try
        {
            var url = $"https://titlehub.xboxlive.com/users/xuid({xuid})/titles/titlehistory/decoration/GamePass,Achievement,Image";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", $"XBL3.0 x={userHash};{xsts}");
            req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "2");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
            using var resp = await ctx.Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return new ImportResult([], [], 0, $"Title Hub returned {(int)resp.StatusCode}");

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("titles", out var titles) ||
                titles.ValueKind != JsonValueKind.Array)
                return new ImportResult([], [], 0, "Unexpected Title Hub response");

            var imported = new List<string>();
            var updated  = new List<string>();
            var idx = 0;
            var gameCount = 0;

            foreach (var t in titles.EnumerateArray())
            {
                idx++;
                var type = t.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                if (type is "App" or "WebApp") continue;
                gameCount++;

                var titleIdStr = t.TryGetProperty("titleId", out var ti) &&
                                 ti.ValueKind == JsonValueKind.Number
                    ? ti.GetInt64().ToString()
                    : (t.TryGetProperty("titleId", out var ti2) ? ti2.GetString() : null);
                if (string.IsNullOrEmpty(titleIdStr)) continue;

                var name = t.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) name = "Unknown";

                var img = t.TryGetProperty("displayImage", out var di) ? di.GetString() : null;
                if (string.IsNullOrEmpty(img) &&
                    t.TryGetProperty("images", out var imgs) &&
                    imgs.ValueKind == JsonValueKind.Array &&
                    imgs.GetArrayLength() > 0)
                {
                    var first = imgs[0];
                    img = first.TryGetProperty("url", out var iu) ? iu.GetString() : null;
                }

                string? lastPlayed = null;
                int minutesPlayed = 0;
                if (t.TryGetProperty("titleHistory", out var th))
                {
                    lastPlayed = th.TryGetProperty("lastTimePlayed", out var lp) ? lp.GetString() : null;
                    if (th.TryGetProperty("totalMinutesPlayed", out var mp) &&
                        mp.ValueKind == JsonValueKind.Number)
                        minutesPlayed = mp.GetInt32();
                }

                var existing = ProviderUtils.FindExisting(db, "xbox", titleIdStr, name!);
                if (existing is not null)
                {
                    var changed = false;
                    if (string.IsNullOrEmpty(existing.PlatformId)) { existing.PlatformId = titleIdStr; changed = true; }
                    if (minutesPlayed > (existing.PlaytimeMinutes ?? 0))
                        { existing.PlaytimeMinutes = minutesPlayed; changed = true; }
                    if (string.IsNullOrEmpty(existing.CoverUrl) && !string.IsNullOrEmpty(img))
                        { existing.CoverUrl = img; changed = true; }
                    if (!string.IsNullOrEmpty(lastPlayed) &&
                        (string.IsNullOrEmpty(existing.LastPlayed) ||
                         DateTimeOffset.TryParse(lastPlayed, out var lpDt) &&
                         DateTimeOffset.TryParse(existing.LastPlayed, out var exDt) &&
                         lpDt > exDt))
                        { existing.LastPlayed = lastPlayed; changed = true; }
                    if (changed) updated.Add(existing.Name);
                }
                else
                {
                    var game = ProviderUtils.MakeGameEntry("xbox", "xbox", name!, titleIdStr,
                        coverUrl: img, playtimeMinutes: minutesPlayed);
                    if (!string.IsNullOrEmpty(lastPlayed)) game.LastPlayed = lastPlayed;
                    db.Db.Games.Add(game);
                    imported.Add(name!);
                }

                if (idx % 20 == 0)
                    ctx.Notify?.Invoke(new ImportProgress { Status = "running", Processed = idx, Provider = "xbox" });
            }

            db.Save();
            ctx.Notify?.Invoke(new ImportProgress { Status = "done", Processed = gameCount, Provider = "xbox" });
            Log.Information("[xbox] Title Hub import: {I} new, {U} updated, {T} total games",
                imported.Count, updated.Count, gameCount);
            return new ImportResult(imported, updated, gameCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[xbox] ImportLibrary failed");
            return new ImportResult([], [], 0, "Xbox import failed: " + ex.Message);
        }
    }
}
