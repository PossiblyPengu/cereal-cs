using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Providers;

public class GogProvider(DatabaseService db, AuthService auth) : IImportProvider
{
    public string PlatformId => "gog";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        var dirsToScan = new[]
        {
            @"C:\GOG Games",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games"),
        }.Where(Directory.Exists);

        foreach (var dir in dirsToScan)
        {
            foreach (var gameDir in Directory.GetDirectories(dir))
            {
                try
                {
                    foreach (var infoFile in Directory.GetFiles(gameDir, "goggame-*.info"))
                    {
                        using var infoDoc = JsonDocument.Parse(File.ReadAllText(infoFile));
                        var info = infoDoc.RootElement;
                        var name = info.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (name is null) continue;

                        var gameId = info.TryGetProperty("gameId", out var gid) ? gid.GetString() : null;
                        games.Add(new Game
                        {
                            Id = Guid.NewGuid().ToString("N")[..12],
                            Name = name,
                            Platform = "gog",
                            PlatformId = gameId,
                            Installed = true,
                            AddedAt = DateTime.UtcNow.ToString("o"),
                        });
                    }
                }
                catch (Exception ex) { Log.Debug(ex, "[gog] Skipping {Dir}", gameDir); }
            }
        }

        return Task.FromResult(new DetectResult(games));
    }

    public async Task<ImportResult> ImportLibrary(ImportContext ctx)
    {
        var token = auth.GetAccessToken("gog");
        if (string.IsNullOrWhiteSpace(token))
            return new ImportResult([], [], 0, "GOG account not connected");

        try
        {
            var imported = new List<string>();
            var updated = new List<string>();
            var processedNames = new HashSet<string>();
            var allProducts = new List<JsonElement>();
            var index = ProviderUtils.GameImportIndex.FromGames(db.Db.Games);

            var firstPage = await FetchPage(ctx.Http, token, 1);
            if (firstPage is null) return new ImportResult([], [], 0, "Could not fetch GOG library");

            allProducts.AddRange(firstPage.Value.products);
            var totalPages = Math.Min(firstPage.Value.totalPages, 20);

            for (var page = 2; page <= totalPages; page++)
            {
                var p = await FetchPage(ctx.Http, token, page);
                if (p is not null) allProducts.AddRange(p.Value.products);
            }

            var idx = 0;
            foreach (var gp in allProducts)
            {
                idx++;
                var gogId = gp.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : null;
                if (gogId is null) continue;

                var title = (gp.TryGetProperty("title", out var t) ? t.GetString() : null)
                         ?? (gp.TryGetProperty("name", out var na) ? na.GetString() : null) ?? "";
                var canonical = ProviderUtils.Canonicalize(title);
                if (processedNames.Contains(canonical)) continue;
                processedNames.Add(canonical);

                var slug = (gp.TryGetProperty("slug", out var sl) ? sl.GetString() : null) ?? "";
                var coverUrl = PickCoverImage(gp);

                var existing = index.Find("gog", gogId, title);
                if (existing is not null)
                {
                    var changed = false;
                    if (existing.PlatformId is null) { existing.PlatformId = ProviderUtils.NormalizePlatformId(gogId); changed = true; }
                    if (existing.CoverUrl is null && coverUrl is not null) { existing.CoverUrl = coverUrl; changed = true; }
                    if (changed) updated.Add(existing.Name);
                }
                else
                {
                    var entry = ProviderUtils.MakeGameEntry("gog", "gog", title, gogId, coverUrl);
                    db.Db.Games.Add(entry);
                    index.Track(entry);
                    imported.Add(title);
                }

                if (idx % 10 == 0)
                    ctx.Notify?.Invoke(new ImportProgress { Status = "running", Processed = idx });
            }

            db.Save();
            ctx.Notify?.Invoke(new ImportProgress { Status = "done", Processed = processedNames.Count });
            return new ImportResult(imported, updated, allProducts.Count);
        }
        catch (Exception ex) { return new ImportResult([], [], 0, "GOG import failed: " + ex.Message); }
    }

    private static async Task<(List<JsonElement> products, int totalPages)?> FetchPage(
        HttpClient http, string token, int page)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://embed.gog.com/account/getFilteredProducts?mediaType=1&page={page}");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var resp = await http.SendAsync(req);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("products", out var prods)) return null;
            var total = root.TryGetProperty("totalPages", out var tp) ? tp.GetInt32() : 1;
            return (prods.EnumerateArray().Select(static p => p.Clone()).ToList(), total);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[gog] FetchPage failed for page {Page}", page);
            return null;
        }
    }

    private static string? PickCoverImage(JsonElement gp)
    {
        if (gp.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in media.EnumerateArray())
            {
                var type = (m.TryGetProperty("type", out var t) ? t.GetString() : null)?.ToLowerInvariant() ?? "";
                if (type.Contains("cover") || type.Contains("header") || type.Contains("hero"))
                    return m.TryGetProperty("url", out var u) ? u.GetString() : null;
            }
        }
        if (gp.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
            return "https:" + img.GetString() + "_392.jpg";
        return null;
    }
}
