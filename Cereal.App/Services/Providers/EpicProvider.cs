using System.Runtime.InteropServices;
using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Providers;

public class EpicProvider(DatabaseService db, AuthService auth) : IImportProvider
{
    public string PlatformId => "epic";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var manifestDir = Path.Combine(
                Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData",
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifestDir)) return Task.FromResult(new DetectResult(games));

            foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
            {
                try
                {
                    var content = JsonDocument.Parse(File.ReadAllText(file));
                    var root = content.RootElement;
                    var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                    var installLoc = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;

                    if (displayName is null || installLoc is null) continue;

                    var catalogNamespace = root.TryGetProperty("CatalogNamespace", out var cn) ? cn.GetString() : null;
                    var appName = root.TryGetProperty("AppName", out var an) ? an.GetString() : null;
                    var launchExe = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;

                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = displayName,
                        Platform = "epic",
                        PlatformId = catalogNamespace ?? appName,
                        ExecutablePath = launchExe is not null ? Path.Combine(installLoc, launchExe) : null,
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch (Exception ex) { Log.Debug(ex, "[epic] Skipping bad manifest: {File}", file); }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[epic] DetectInstalled error"); }

        return Task.FromResult(new DetectResult(games));
    }

    public async Task<ImportResult> ImportLibrary(ImportContext ctx)
    {
        var acct = db.Db.Accounts.GetValueOrDefault("epic");
        var token = auth.GetAccessToken("epic");
        if (string.IsNullOrWhiteSpace(token) || acct?.AccountId is null)
            return new ImportResult([], [], 0, "Epic account not connected");

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var resp = await ctx.Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var records = doc.RootElement.TryGetProperty("records", out var r) ? r : doc.RootElement;
            if (records.ValueKind != JsonValueKind.Array)
                return new ImportResult([], [], 0, "Unexpected response from Epic");

            var imported = new List<string>();
            var updated = new List<string>();
            var processedKeys = new HashSet<string>();
            var processedNames = new HashSet<string>();
            var idx = 0;
            var index = ProviderUtils.GameImportIndex.FromGames(db.Db.Games);

            foreach (var rec in records.EnumerateArray())
            {
                idx++;
                var ns = TryGet(rec, "namespace") ?? TryGet(rec, "catalogNamespace") ?? "";
                var catId = TryGet(rec, "catalogItemId") ?? TryGet(rec, "catalogId") ?? "";
                var appName = TryGet(rec, "appName") ?? "";
                var title = rec.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("title", out var t)
                    ? t.GetString() ?? TryGet(rec, "title") ?? TryGet(rec, "appName") ?? "Unknown"
                    : TryGet(rec, "title") ?? TryGet(rec, "appName") ?? "Unknown";

                var keyId = !string.IsNullOrEmpty(ns) ? ns : !string.IsNullOrEmpty(catId) ? catId : title;
                if (string.IsNullOrEmpty(keyId)) continue;

                var canonical = ProviderUtils.Canonicalize(title);
                if (processedKeys.Contains(keyId) || processedNames.Contains(canonical)) continue;
                processedKeys.Add(keyId);
                processedNames.Add(canonical);

                var coverUrl = PickCoverImage(rec);
                var existing = index.Find("epic", keyId, title);

                if (existing is not null)
                {
                    var changed = false;
                    if (existing.PlatformId is null) { existing.PlatformId = ProviderUtils.NormalizePlatformId(keyId); changed = true; }
                    if (existing.CoverUrl is null && coverUrl is not null) { existing.CoverUrl = coverUrl; changed = true; }
                    if (changed) updated.Add(existing.Name);
                }
                else
                {
                    var entry = ProviderUtils.MakeGameEntry("epic", "epic", title, keyId, coverUrl);
                    db.Db.Games.Add(entry);
                    index.Track(entry);
                    imported.Add(title);
                }

                if (idx % 10 == 0)
                    ctx.Notify?.Invoke(new ImportProgress { Status = "running", Processed = idx });
            }

            db.Save();
            ctx.Notify?.Invoke(new ImportProgress { Status = "done", Processed = processedKeys.Count });
            return new ImportResult(imported, updated, processedKeys.Count);
        }
        catch (Exception ex) { return new ImportResult([], [], 0, "Epic import failed: " + ex.Message); }
    }

    private static string? PickCoverImage(JsonElement rec)
    {
        try
        {
            JsonElement keys = default;
            if (rec.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("keyImages", out var ki))
                keys = ki;
            else if (rec.TryGetProperty("keyImages", out var ki2))
                keys = ki2;

            if (keys.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in keys.EnumerateArray())
                {
                    var type = (k.TryGetProperty("type", out var t) ? t.GetString() : null)?.ToLowerInvariant() ?? "";
                    if (type.Contains("key") || type.Contains("offer") || type.Contains("hero"))
                        return k.TryGetProperty("url", out var u) ? u.GetString() : null;
                }
                var first = keys.EnumerateArray().FirstOrDefault();
                if (first.ValueKind != JsonValueKind.Undefined)
                    return first.TryGetProperty("url", out var u) ? u.GetString() : null;
            }
        }
        catch { /* fall through */ }
        return null;
    }

    private static string? TryGet(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
