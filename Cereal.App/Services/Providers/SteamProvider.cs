using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Providers;

public partial class SteamProvider(DatabaseService db) : IImportProvider
{
    public string PlatformId => "steam";

    private static string CoverUrl(string appId) =>
        $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";

    private static string HeaderUrl(string appId) =>
        $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";

    // ── Local detection ───────────────────────────────────────────────────────

    public Task<DetectResult> DetectInstalled() =>
        Task.FromResult(DetectLocal());

    private DetectResult DetectLocal()
    {
        var root = FindSteamRoot();
        if (root is null) return new DetectResult([], "Steam not found");

        var libFolders = new List<string> { Path.Combine(root, "steamapps") };
        var vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");

        if (File.Exists(vdfPath))
        {
            var content = File.ReadAllText(vdfPath);
            foreach (Match m in VdfPath().Matches(content))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                var appsDir = Path.Combine(p, "steamapps");
                if (Directory.Exists(appsDir) && !libFolders.Contains(appsDir))
                    libFolders.Add(appsDir);
            }
        }

        var games = new List<Game>();
        foreach (var libFolder in libFolders)
        {
            if (!Directory.Exists(libFolder)) continue;
            foreach (var acf in Directory.GetFiles(libFolder, "*.acf"))
            {
                try
                {
                    var content = File.ReadAllText(acf);
                    var appId = VdfAppId().Match(content).Groups[1].Value;
                    var name = VdfName().Match(content).Groups[1].Value;
                    var installDir = VdfInstallDir().Match(content).Groups[1].Value;
                    var playtime = VdfPlaytime().Match(content).Groups[1].Value;

                    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name)) continue;

                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "steam",
                        PlatformId = appId,
                        CoverUrl = CoverUrl(appId),
                        HeaderUrl = HeaderUrl(appId),
                        PlaytimeMinutes = int.TryParse(playtime, out var pt) ? pt : null,
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch (Exception ex) { Log.Debug(ex, "[steam] Skipping bad ACF: {File}", acf); }
            }
        }

        return new DetectResult(games);
    }

    // ── Library import ────────────────────────────────────────────────────────

    public async Task<ImportResult> ImportLibrary(ImportContext ctx)
    {
        var acct = db.Db.Accounts.GetValueOrDefault("steam");
        if (acct is null) return new ImportResult([], [], 0, "Steam account not connected");

        var steamId = acct.AccountId ?? acct.Extra?.GetValueOrDefault("steamId")?.ToString();
        if (string.IsNullOrEmpty(steamId)) return new ImportResult([], [], 0, "Steam account not connected");

        List<SteamGame>? games = null;
        var source = "unknown";
        var failures = new List<string>();

        if (!string.IsNullOrEmpty(ctx.ApiKey))
        {
            ctx.Notify?.Invoke(new ImportProgress { Status = "running", Name = "Fetching via API key…" });
            (games, var err) = await FetchViaApi(steamId, ctx.ApiKey, ctx.Http);
            if (games is not null) source = "api";
            else failures.Add("api: " + err);
        }

        if (games is null)
        {
            ctx.Notify?.Invoke(new ImportProgress { Status = "running", Name = "Trying storefront page…" });
            (games, var err) = await FetchViaStorefront(steamId, ctx.Http);
            if (games is not null) source = "storefront";
            else failures.Add("storefront: " + err);
        }

        if (games is null)
        {
            ctx.Notify?.Invoke(new ImportProgress { Status = "running", Name = "Trying public XML feed…" });
            (games, var err) = await FetchViaXml(steamId, ctx.Http);
            if (games is not null) source = "xml";
            else failures.Add("xml: " + err);
        }

        if (games is null)
        {
            ctx.Notify?.Invoke(new ImportProgress { Status = "running", Name = "Scanning local library…" });
            var local = DetectLocal();
            if (local.Games.Count > 0)
            {
                games = local.Games.Select(g => new SteamGame(g.PlatformId!, g.Name,
                    g.PlaytimeMinutes ?? 0, g.CoverUrl, g.HeaderUrl)).ToList();
                source = "local";
            }
            else failures.Add("local: Steam not installed");
        }

        if (games is null)
        {
            var f = string.Join("; ", failures);
            if (f.Contains("profile-private", StringComparison.OrdinalIgnoreCase))
                return new ImportResult([], [], 0, "Steam profile is private. Add an API key or set game details to public.");
            if (f.Contains("not-logged-in", StringComparison.OrdinalIgnoreCase))
                return new ImportResult([], [], 0, "Steam session expired. Re-authenticate and retry.");
            return new ImportResult([], [], 0, $"Could not fetch Steam library. ({f})");
        }

        ctx.Notify?.Invoke(new ImportProgress { Status = "running", Name = $"Processing {games.Count} games (via {source})…", Total = games.Count });

        var imported = new List<string>();
        var updated = new List<string>();
        var index = ProviderUtils.GameImportIndex.FromGames(db.Db.Games);

        for (var i = 0; i < games.Count; i++)
        {
            var g = games[i];
            var existing = index.Find("steam", g.AppId, g.Name);
            if (existing is not null)
            {
                var changed = false;
                if (g.Minutes > (existing.PlaytimeMinutes ?? 0)) { existing.PlaytimeMinutes = g.Minutes; changed = true; }
                if (existing.CoverUrl is null && g.CoverUrl is not null) { existing.CoverUrl = g.CoverUrl; changed = true; }
                if (existing.HeaderUrl is null && g.HeaderUrl is not null) { existing.HeaderUrl = g.HeaderUrl; changed = true; }
                if (changed) updated.Add(existing.Name);
            }
            else
            {
                var entry = ProviderUtils.MakeGameEntry("steam", "steam", g.Name, g.AppId,
                    g.CoverUrl, g.HeaderUrl, g.Minutes, source == "local");
                db.Db.Games.Add(entry);
                index.Track(entry);
                imported.Add(g.Name);
            }

            if ((i + 1) % 25 == 0 || i == games.Count - 1)
                ctx.Notify?.Invoke(new ImportProgress { Status = "running", Processed = i + 1, Total = games.Count });
        }

        db.Save();
        return new ImportResult(imported, updated, games.Count);
    }

    private static async Task<(List<SteamGame>? Games, string? Error)> FetchViaApi(
        string steamId, string apiKey, HttpClient http)
    {
        try
        {
            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={steamId}&include_appinfo=1&include_played_free_games=1&format=json";
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var resp) ||
                !resp.TryGetProperty("games", out var gamesEl))
                return (null, "no games in response");

            var list = new List<SteamGame>();
            foreach (var g in gamesEl.EnumerateArray())
            {
                var appId = g.GetProperty("appid").GetInt64().ToString();
                var name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
                var minutes = g.TryGetProperty("playtime_forever", out var pt) ? pt.GetInt32() : 0;
                list.Add(new SteamGame(appId, name, minutes, CoverUrl(appId), HeaderUrl(appId)));
            }
            return (list, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private static async Task<(List<SteamGame>? Games, string? Error)> FetchViaXml(
        string steamId, HttpClient http)
    {
        try
        {
            var raw = await http.GetStringAsync(
                $"https://steamcommunity.com/profiles/{steamId}/games/?tab=all&xml=1");
            if (string.IsNullOrEmpty(raw))
                return (null, "empty");
            if (raw.Contains("<error>", StringComparison.OrdinalIgnoreCase))
            {
                if (raw.Contains("private", StringComparison.OrdinalIgnoreCase))
                    return (null, "profile-private");
                return (null, "xml-error");
            }
            if (!raw.Contains("<game>", StringComparison.OrdinalIgnoreCase))
                return (null, "no-games");

            var list = new List<SteamGame>();
            foreach (Match block in XmlGameBlock().Matches(raw))
            {
                var appIdM = XmlAppId().Match(block.Value);
                if (!appIdM.Success) continue;
                var appId = appIdM.Groups[1].Value;
                var nameM = XmlName().Match(block.Value);
                var name = nameM.Success ? nameM.Groups[1].Value.Trim() : "Unknown";
                var hoursM = XmlHours().Match(block.Value);
                var minutes = hoursM.Success
                    ? (int)Math.Round(double.Parse(hoursM.Groups[1].Value.Replace(",", "")) * 60) : 0;
                list.Add(new SteamGame(appId, name, minutes, CoverUrl(appId), HeaderUrl(appId)));
            }
            return list.Count > 0 ? (list, null) : (null, "no games parsed");
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private static async Task<(List<SteamGame>? Games, string? Error)> FetchViaStorefront(
        string steamId, HttpClient http)
    {
        try
        {
            var html = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}/games/?tab=all");
            if (string.IsNullOrWhiteSpace(html)) return (null, "empty");
            if (html.Contains("This profile is private", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("profile_private", StringComparison.OrdinalIgnoreCase))
                return (null, "profile-private");
            if (html.Contains("Sign In", StringComparison.OrdinalIgnoreCase) &&
                html.Contains("store.steampowered.com/login", StringComparison.OrdinalIgnoreCase))
                return (null, "not-logged-in");

            var json = TryExtractGamesJsonArray(html);
            if (string.IsNullOrWhiteSpace(json))
                return (null, "no-games-data");

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, "bad-json");
            var list = new List<SteamGame>();
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                var appId = g.TryGetProperty("appid", out var aid) ? aid.GetRawText().Trim('"') : "";
                var name = g.TryGetProperty("name", out var n) ? (n.GetString() ?? "Unknown") : "Unknown";
                if (string.IsNullOrWhiteSpace(appId)) continue;
                var minutes = g.TryGetProperty("hours_forever", out var hf) &&
                              double.TryParse(hf.GetRawText().Trim('"'), out var hrs)
                    ? (int)Math.Round(hrs * 60)
                    : 0;
                list.Add(new SteamGame(appId, name, minutes, CoverUrl(appId), HeaderUrl(appId)));
            }
            return list.Count > 0 ? (list, null) : (null, "no-games-data");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string? TryExtractGamesJsonArray(string html)
    {
        foreach (var v in new[] { "var rgGames", "var g_rgGames", "rgGames", "g_rgGames" })
        {
            var idx = html.IndexOf(v, StringComparison.Ordinal);
            if (idx < 0) continue;
            var start = html.IndexOf('[', idx);
            if (start < 0) continue;
            var depth = 0;
            var inString = false;
            var esc = false;
            for (var i = start; i < html.Length; i++)
            {
                var c = html[i];
                if (inString)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return html[start..(i + 1)];
                }
            }
        }
        return null;
    }

    public static string? FindSteamRoot()
    {
        var candidates = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates.Add(@"C:\Program Files (x86)\Steam");
            candidates.Add(@"C:\Program Files\Steam");
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(home, "Steam"));
        candidates.Add(Path.Combine(home, ".local", "share", "Steam"));
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private record SteamGame(string AppId, string Name, int Minutes, string? CoverUrl, string? HeaderUrl);

    [GeneratedRegex(@"""path""\s+""([^""]+)""")]
    private static partial Regex VdfPath();
    [GeneratedRegex(@"""appid""\s+""(\d+)""")]
    private static partial Regex VdfAppId();
    [GeneratedRegex(@"""name""\s+""([^""]+)""")]
    private static partial Regex VdfName();
    [GeneratedRegex(@"""installdir""\s+""([^""]+)""")]
    private static partial Regex VdfInstallDir();
    [GeneratedRegex(@"""playtime_forever""\s+""(\d+)""")]
    private static partial Regex VdfPlaytime();
    [GeneratedRegex(@"<game>[\s\S]*?</game>")]
    private static partial Regex XmlGameBlock();
    [GeneratedRegex(@"<appID>(\d+)</appID>")]
    private static partial Regex XmlAppId();
    [GeneratedRegex(@"<name><!\[CDATA\[([\s\S]*?)\]\]></name>|<name>([^<]+)</name>")]
    private static partial Regex XmlName();
    [GeneratedRegex(@"<hoursOnRecord>([\d.,]+)</hoursOnRecord>")]
    private static partial Regex XmlHours();
}
