using System.Text.RegularExpressions;
using Cereal.Core.Models;
using Cereal.Core.Providers;
using Cereal.Core.Repositories;
using Cereal.Core.Services;
using Cereal.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.Infrastructure.Providers;

/// <summary>
/// Detects locally installed Steam games via .acf manifest files
/// and imports the full library via the Steam Web API (requires API key).
/// </summary>
public sealed partial class SteamProvider : IImportProvider
{
    public string PlatformId => "steam";

    private static string CoverUrl(string appId) =>
        $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";
    private static string HeaderUrl(string appId) =>
        $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";

    // ── IProvider ─────────────────────────────────────────────────────────────

    public Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default) =>
        Task.Run(Detect, ct);

    // ── IImportProvider ───────────────────────────────────────────────────────

    public async Task<ImportResult> ImportLibraryAsync(ImportContext ctx, CancellationToken ct = default)
    {
        // Import via Steam Web API when an API key is present
        if (!string.IsNullOrEmpty(ctx.ApiKey))
        {
            var result = await ImportViaApiAsync(ctx, ct);
            if (result is not null) return result;
        }

        // Fallback: parse ACF manifests (detect only gives installed games)
        var detected = Detect();
        foreach (var g in detected.Games)
            await ctx.Services.GetRequiredService<IGameService>().UpsertAsync(g, ct);
        var ids = detected.Games.Select(g => g.Id).ToList();
        return new ImportResult(ids, [], detected.Games.Count, null);
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    private DetectResult Detect()
    {
        var root = PlaytimeSyncService.FindSteamRoot();
        if (root is null) return new DetectResult([], "Steam not found");

        var libFolders = new List<string> { Path.Combine(root, "steamapps") };
        var vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");

        if (File.Exists(vdfPath))
        {
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdfPath)))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                var dir = Path.Combine(p, "steamapps");
                if (Directory.Exists(dir) && !libFolders.Contains(dir))
                    libFolders.Add(dir);
            }
        }

        var games = new List<Game>();
        foreach (var folder in libFolders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var acf in Directory.GetFiles(folder, "*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(acf);
                    var appId  = VdfAppId().Match(text).Groups[1].Value;
                    var name   = VdfName().Match(text).Groups[1].Value;
                    var pt     = VdfPlaytime().Match(text).Groups[1].Value;

                    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name)) continue;
                    games.Add(new Game
                    {
                        Name            = name,
                        Platform        = "steam",
                        PlatformId      = appId,
                        CoverUrl        = CoverUrl(appId),
                        HeaderUrl       = HeaderUrl(appId),
                        PlaytimeMinutes = int.TryParse(pt, out var mins) ? mins : 0,
                        IsInstalled     = true,
                        AddedAt         = DateTimeOffset.UtcNow,
                    });
                }
                catch (Exception ex) { Log.Debug(ex, "[steam] Skipping bad ACF: {File}", acf); }
            }
        }

        return new DetectResult(games);
    }

    // ── Web API import ────────────────────────────────────────────────────────

    private async Task<ImportResult?> ImportViaApiAsync(ImportContext ctx, CancellationToken ct)
    {
        // We need an account steamId — stored in account row
        var acctRepo = ctx.Services.GetRequiredService<IAccountRepository>();
        var acct = (await acctRepo.GetAllAsync(ct)).FirstOrDefault(a => a.Platform == "steam");
        var steamId = acct?.AccountId;
        if (string.IsNullOrEmpty(steamId)) return null;

        try
        {
            ctx.Notify?.Invoke(new ImportProgress("running", "steam", 0, 0, "Fetching library…"));
            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={ctx.ApiKey}" +
                      $"&steamid={steamId}&include_appinfo=true&include_played_free_games=true";
            var resp = await ctx.Http.GetStringAsync(url, ct);
            using var doc = System.Text.Json.JsonDocument.Parse(resp);

            if (!doc.RootElement.TryGetProperty("response", out var response)) return null;
            if (!response.TryGetProperty("games", out var arr)) return null;

            var games = arr.EnumerateArray()
                .Select(g =>
                {
                    var appIdStr = g.TryGetProperty("appid", out var appIdEl)
                        ? appIdEl.GetInt64().ToString() : "";
                    return new Game
                    {
                        Name        = g.TryGetProperty("name", out var n) ? n.GetString()! : "?",
                        Platform    = "steam",
                        PlatformId  = appIdStr,
                        CoverUrl    = string.IsNullOrEmpty(appIdStr) ? null : CoverUrl(appIdStr),
                        HeaderUrl   = string.IsNullOrEmpty(appIdStr) ? null : HeaderUrl(appIdStr),
                        PlaytimeMinutes = g.TryGetProperty("playtime_forever", out var pt) ? pt.GetInt32() : 0,
                        AddedAt     = DateTimeOffset.UtcNow,
                    };
                })
                .ToList();

            var svc = ctx.Services.GetRequiredService<IGameService>();
            var (_, newRows, survivors) = await svc.UpsertRangeAsync(games, ct);
            return new ImportResult(survivors.Select(g => g.Id).ToList(), [], games.Count, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[steam] Web API import failed");
            return null;
        }
    }

    [GeneratedRegex(@"""path""\s+""([^""]+)""")]
    private static partial Regex VdfPath();
    [GeneratedRegex(@"""appid""\s+""(\d+)""")]
    private static partial Regex VdfAppId();
    [GeneratedRegex(@"""name""\s+""([^""]+)""")]
    private static partial Regex VdfName();
    [GeneratedRegex(@"""playtime_forever""\s+""(\d+)""")]
    private static partial Regex VdfPlaytime();
}
