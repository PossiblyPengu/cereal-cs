// ─── Game Metadata Fetching ──────────────────────────────────────────────────
// Port of electron/modules/metadata/metadata.js.
// Default sources require ZERO accounts or API keys:
//   - Steam Store: searches Steam's entire catalog for any game
//   - Wikipedia: free encyclopedia API for descriptions + info
// Optional: SteamGridDB (requires free API key) for high-quality game art

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Metadata;

public sealed class FetchedMetadata
{
    public string? Description { get; init; }
    public string? Developer { get; init; }
    public string? Publisher { get; init; }
    public string? ReleaseDate { get; init; }
    public List<string> Genres { get; init; } = [];
    public string? CoverUrl { get; init; }
    public string? SgdbCoverUrl { get; init; }
    public string? HeaderUrl { get; init; }
    public List<string> Screenshots { get; init; } = [];
    public int? Metacritic { get; init; }
    public string? Website { get; init; }
    public string Source { get; init; } = "";
    public bool IsSoftware { get; init; }
}

public sealed class MetadataProgressArgs : EventArgs
{
    public int Completed { get; init; }
    public int Total { get; init; }
    public int Updated { get; init; }
    public bool Done { get; init; }
    public string? CurrentGame { get; init; }
}

public sealed class MetadataService
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;
    private readonly CredentialService _creds;
    private readonly HttpClient _http;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    private sealed record CacheEntry(FetchedMetadata Data, DateTime At);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public event EventHandler<MetadataProgressArgs>? ProgressChanged;

    public MetadataService(DatabaseService db, SettingsService settings, CredentialService creds)
    {
        _db = db;
        _settings = settings;
        _creds = creds;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/json, */*");
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    private string? SteamGridDbKey =>
        _creds.GetPassword("cereal", "steamgriddb_key");

    /// <summary>Valid values: <c>steam</c> (Steam Store search first) and <c>wikipedia</c>.
    /// Legacy or invalid values (e.g. <c>steamgriddb</c>) are treated as <c>steam</c>.</summary>
    private string MetadataSource =>
        string.Equals(_settings.Get().MetadataSource, "wikipedia", StringComparison.OrdinalIgnoreCase)
            ? "wikipedia"
            : "steam";

    // ─── Core entry points ────────────────────────────────────────────────────

    /// <summary>
    /// Fetch best available metadata for a game from the default free sources,
    /// optionally enhancing cover/header art with SteamGridDB when a key is set.
    /// </summary>
    public async Task<FetchedMetadata?> FetchAsync(Game game, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(game.Name)) return null;

        var cacheKey = (game.Platform ?? "") + ":" + (game.PlatformId ?? game.Name);
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.At < CacheTtl)
            return cached.Data;

        FetchedMetadata? meta = null;

        // Steam games: try Steam appId first, then search fallback
        if (game.Platform == "steam")
        {
            if (!string.IsNullOrEmpty(game.PlatformId))
                meta = await FetchSteamAsync(game.PlatformId!, ct);
            meta ??= await FetchSteamSearchAsync(game.Name, ct);
        }

        // Fallback for all platforms
        if (meta is null)
        {
            if (MetadataSource == "wikipedia")
            {
                meta = await FetchWikipediaAsync(game.Name, ct)
                       ?? await FetchSteamSearchAsync(game.Name, ct);
            }
            else
            {
                meta = await FetchSteamSearchAsync(game.Name, ct)
                       ?? await FetchWikipediaAsync(game.Name, ct);
            }
        }

        // Enhance with SteamGridDB art if a key is available
        var sgdbKey = SteamGridDbKey;
        if (meta is not null && !string.IsNullOrEmpty(sgdbKey))
        {
            try
            {
                var art = await FetchSteamGridDbArtAsync(game.Name, sgdbKey!, ct);
                if (art is not null)
                {
                    meta = new FetchedMetadata
                    {
                        Description = meta.Description,
                        Developer = meta.Developer,
                        Publisher = meta.Publisher,
                        ReleaseDate = meta.ReleaseDate,
                        Genres = meta.Genres,
                        // Official Steam portrait wins; SGDB fills the gap as sgdbCoverUrl fallback
                        CoverUrl = string.IsNullOrEmpty(meta.CoverUrl) ? art.Value.CoverUrl : meta.CoverUrl,
                        SgdbCoverUrl = !string.IsNullOrEmpty(meta.CoverUrl) ? art.Value.CoverUrl : null,
                        HeaderUrl = !string.IsNullOrEmpty(art.Value.HeaderUrl) ? art.Value.HeaderUrl : meta.HeaderUrl,
                        Screenshots = meta.Screenshots,
                        Metacritic = meta.Metacritic,
                        Website = meta.Website,
                        Source = meta.Source,
                        IsSoftware = meta.IsSoftware,
                    };
                }
            }
            catch { /* SGDB art fetch failed, continue without */ }
        }

        if (meta is not null)
            _cache[cacheKey] = new CacheEntry(meta, DateTime.UtcNow);

        return meta;
    }

    /// <summary>Merges fetched metadata into a Game in-place. Returns true if anything changed.</summary>
    public static bool Apply(Game game, FetchedMetadata meta)
    {
        var changed = false;

        // Only fill missing — never overwrite user customisations
        if (string.IsNullOrEmpty(game.CoverUrl) && !string.IsNullOrEmpty(meta.CoverUrl))
        { game.CoverUrl = meta.CoverUrl; changed = true; }
        if (string.IsNullOrEmpty(game.SgdbCoverUrl) && !string.IsNullOrEmpty(meta.SgdbCoverUrl))
        { game.SgdbCoverUrl = meta.SgdbCoverUrl; changed = true; }
        if (string.IsNullOrEmpty(game.Description) && !string.IsNullOrEmpty(meta.Description))
        { game.Description = meta.Description; changed = true; }
        if (string.IsNullOrEmpty(game.Developer) && !string.IsNullOrEmpty(meta.Developer))
        { game.Developer = meta.Developer; changed = true; }
        if (string.IsNullOrEmpty(game.Publisher) && !string.IsNullOrEmpty(meta.Publisher))
        { game.Publisher = meta.Publisher; changed = true; }
        if (string.IsNullOrEmpty(game.ReleaseDate) && !string.IsNullOrEmpty(meta.ReleaseDate))
        { game.ReleaseDate = meta.ReleaseDate; changed = true; }
        if (string.IsNullOrEmpty(game.HeaderUrl))
        {
            var headerFallback = meta.HeaderUrl ?? meta.CoverUrl ?? meta.Screenshots.FirstOrDefault();
            if (!string.IsNullOrEmpty(headerFallback)) { game.HeaderUrl = headerFallback; changed = true; }
        }
        if ((game.Screenshots is null || game.Screenshots.Count == 0) && meta.Screenshots.Count > 0)
        { game.Screenshots = [.. meta.Screenshots]; changed = true; }
        if (game.Metacritic is null && meta.Metacritic.HasValue)
        { game.Metacritic = meta.Metacritic; changed = true; }
        if (string.IsNullOrEmpty(game.Website) && !string.IsNullOrEmpty(meta.Website))
        { game.Website = meta.Website; changed = true; }

        // Merge genres into categories (preserving existing tags, case-insensitive dedup)
        if (meta.Genres.Count > 0)
        {
            var existing = (game.Categories ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            var add = meta.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
            var merged = existing.Concat(add)
                .GroupBy(x => x.ToLowerInvariant())
                .Select(grp => grp.First())
                .ToList();
            var existingNorm = string.Join("|", existing.Select(x => x.ToLowerInvariant()));
            var mergedNorm   = string.Join("|", merged.Select(x => x.ToLowerInvariant()));
            if (!string.Equals(existingNorm, mergedNorm, StringComparison.Ordinal))
            {
                game.Categories = merged;
                changed = true;
            }
        }

        // If Steam flagged as non-game software, mark + add "Software" category
        if (meta.Source == "steam" && meta.IsSoftware)
        {
            if (game.Software != true) { game.Software = true; changed = true; }
            var cats = game.Categories ?? [];
            if (!cats.Any(c => string.Equals(c, "Software", StringComparison.OrdinalIgnoreCase)))
            {
                game.Categories = [.. cats, "Software"];
                changed = true;
            }
        }

        return changed;
    }

    public void InvalidateCache(Game game)
    {
        var cacheKey = (game.Platform ?? "") + ":" + (game.PlatformId ?? game.Name);
        _cache.TryRemove(cacheKey, out _);
    }

    // ─── Fetch for a specific game (used by "Refresh Info") ───────────────────

    // ─── Fetch-by-name (used by AddGame "Fetch metadata" button) ──────────────
    // Runs the same discovery pipeline as FetchAsync but against a free-text
    // name (no DB mutation) so the caller can prefill a new-game form.
    public async Task<FetchedMetadata?> FetchForNameAsync(string name, string? platform = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var stub = new Game { Id = "stub", Name = name.Trim(), Platform = platform ?? "custom" };
        return await FetchAsync(stub, ct);
    }

    public async Task<bool> FetchForGameAsync(Game game, bool force = false, CancellationToken ct = default)
    {
        if (force) InvalidateCache(game);
        var meta = await FetchAsync(game, ct);
        if (meta is null) return false;
        var changed = force ? ApplyForce(game, meta) : Apply(game, meta);
        if (changed)
        {
            // Persist the game back to DB
            var idx = _db.Db.Games.FindIndex(g => g.Id == game.Id);
            if (idx >= 0) _db.Db.Games[idx] = game;
            _db.Save();
        }
        return changed;
    }

    private static bool ApplyForce(Game game, FetchedMetadata meta)
    {
        var prevCover = game.CoverUrl;
        var prevHeader = game.HeaderUrl;

        game.CoverUrl = !string.IsNullOrWhiteSpace(meta.CoverUrl)
            ? meta.CoverUrl
            : game.CoverUrl;
        game.SgdbCoverUrl = !string.IsNullOrWhiteSpace(meta.SgdbCoverUrl)
            ? meta.SgdbCoverUrl
            : game.SgdbCoverUrl;
        game.Description = !string.IsNullOrWhiteSpace(meta.Description) ? meta.Description : game.Description;
        game.Developer = !string.IsNullOrWhiteSpace(meta.Developer) ? meta.Developer : game.Developer;
        game.Publisher = !string.IsNullOrWhiteSpace(meta.Publisher) ? meta.Publisher : game.Publisher;
        game.ReleaseDate = !string.IsNullOrWhiteSpace(meta.ReleaseDate) ? meta.ReleaseDate : game.ReleaseDate;
        game.HeaderUrl = !string.IsNullOrWhiteSpace(meta.HeaderUrl)
            ? meta.HeaderUrl
            : (!string.IsNullOrWhiteSpace(meta.CoverUrl) ? meta.CoverUrl : game.HeaderUrl);
        if (meta.Screenshots.Count > 0) game.Screenshots = [.. meta.Screenshots];
        if (meta.Metacritic.HasValue) game.Metacritic = meta.Metacritic;
        game.Website = !string.IsNullOrWhiteSpace(meta.Website) ? meta.Website : game.Website;
        if (meta.Genres.Count > 0) game.Categories = [.. meta.Genres.Distinct(StringComparer.OrdinalIgnoreCase)];

        if (!string.Equals(prevCover, game.CoverUrl, StringComparison.Ordinal))
            game.LocalCoverPath = null;
        if (!string.Equals(prevHeader, game.HeaderUrl, StringComparison.Ordinal))
            game.LocalHeaderPath = null;
        if (!string.Equals(prevCover, game.CoverUrl, StringComparison.Ordinal) ||
            !string.Equals(prevHeader, game.HeaderUrl, StringComparison.Ordinal))
            game.ImgStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return true;
    }

    // ─── Batch: fetch metadata for ALL games (with progress + throttle) ───────

    public async Task<(int Updated, int Total)> FetchAllAsync(CancellationToken ct = default)
    {
        var games = _db.Db.Games.ToList();
        var total = games.Count;
        var updated = 0;
        var completed = 0;

        // Process in batches of 3 with a 200ms delay between batches
        const int batchSize = 3;
        for (var i = 0; i < games.Count; i += batchSize)
        {
            var batch = games.Skip(i).Take(batchSize).ToList();
            var results = await Task.WhenAll(batch.Select(async g =>
            {
                try
                {
                    var meta = await FetchAsync(g, ct);
                    return meta is not null && Apply(g, meta);
                }
                catch (Exception ex)
                {
                    Log.Debug("[metadata] Fetch failed for {Name}: {Error}", g.Name, ex.Message);
                    return false;
                }
            }));

            foreach (var changed in results) { if (changed) updated++; }
            completed += batch.Count;

            ProgressChanged?.Invoke(this, new MetadataProgressArgs
            {
                Completed = completed,
                Total = total,
                Updated = updated,
                CurrentGame = batch.LastOrDefault()?.Name,
            });

            // Persist periodically
            _db.Save();

            if (i + batchSize < games.Count)
                await Task.Delay(200, ct);
        }

        ProgressChanged?.Invoke(this, new MetadataProgressArgs
        {
            Completed = total, Total = total, Updated = updated, Done = true,
        });

        return (updated, total);
    }

    // ─── Steam (appid → appdetails) ───────────────────────────────────────────

    private async Task<FetchedMetadata?> FetchSteamAsync(string appId, CancellationToken ct)
    {
        try
        {
            var data = await HttpGetJsonAsync(
                $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", ct);
            if (data is null) return null;

            if (!data.Value.TryGetProperty(appId, out var wrapper)) return null;
            if (!wrapper.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            if (!wrapper.TryGetProperty("data", out var info)) return null;

            // Detect non-game software entries
            var isSoftware = false;
            if (info.TryGetProperty("type", out var typeEl))
            {
                var t = typeEl.GetString();
                if (!string.IsNullOrEmpty(t) && !string.Equals(t, "game", StringComparison.OrdinalIgnoreCase))
                    isSoftware = true;
            }
            if (!isSoftware && info.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cats.EnumerateArray())
                {
                    var desc = c.TryGetProperty("description", out var d) ? (d.GetString() ?? "") : "";
                    var lc = desc.ToLowerInvariant();
                    if (lc.Contains("software") || lc.Contains("utility") || lc.Contains("application"))
                    { isSoftware = true; break; }
                }
            }
            if (!isSoftware && info.TryGetProperty("genres", out var gens) && gens.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in gens.EnumerateArray())
                {
                    var desc = g.TryGetProperty("description", out var d) ? (d.GetString() ?? "") : "";
                    if (desc.ToLowerInvariant().Contains("software")) { isSoftware = true; break; }
                }
            }

            // Validate the portrait library capsule exists (HEAD 2x then 1x)
            string? coverUrl = null;
            var capsules = new[]
            {
                $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg",
                $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
            };
            foreach (var url in capsules)
            {
                if (await HeadOkAsync(url, ct)) { coverUrl = url; break; }
            }

            // Header: header_image from appdetails, else library_hero
            var headerUrl = info.TryGetProperty("header_image", out var hdr) ? hdr.GetString() : null;
            if (string.IsNullOrEmpty(headerUrl))
                headerUrl = $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg";

            var genres = new List<string>();
            if (info.TryGetProperty("genres", out var g2) && g2.ValueKind == JsonValueKind.Array)
                foreach (var g in g2.EnumerateArray())
                    if (g.TryGetProperty("description", out var d) && d.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                        genres.Add(s);

            var screenshots = new List<string>();
            if (info.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in shots.EnumerateArray().Take(4))
                    if (s.TryGetProperty("path_full", out var pf) && pf.GetString() is { } url && !string.IsNullOrEmpty(url))
                        screenshots.Add(url);
            }

            int? metacritic = null;
            if (info.TryGetProperty("metacritic", out var mc) && mc.TryGetProperty("score", out var score))
                metacritic = score.GetInt32();

            string Dev(string prop)
            {
                if (!info.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
                return arr.GetArrayLength() > 0 ? (arr[0].GetString() ?? "") : "";
            }

            var description = info.TryGetProperty("short_description", out var sd) ? (sd.GetString() ?? "") : "";
            if (description.Length > 500) description = description[..500];

            return new FetchedMetadata
            {
                Description = description,
                Developer = Dev("developers"),
                Publisher = Dev("publishers"),
                ReleaseDate = info.TryGetProperty("release_date", out var rd) &&
                              rd.TryGetProperty("date", out var date) ? date.GetString() ?? "" : "",
                Genres = genres,
                CoverUrl = coverUrl,
                HeaderUrl = headerUrl,
                Screenshots = screenshots,
                Metacritic = metacritic,
                Website = info.TryGetProperty("website", out var ws) ? ws.GetString() ?? "" : "",
                Source = "steam",
                IsSoftware = isSoftware,
            };
        }
        catch (Exception ex)
        {
            Log.Debug("[metadata] Steam fetch failed for {AppId}: {Error}", appId, ex.Message);
            return null;
        }
    }

    // ─── Steam search by name ─────────────────────────────────────────────────

    private async Task<FetchedMetadata?> FetchSteamSearchAsync(string name, CancellationToken ct)
    {
        try
        {
            var data = await HttpGetJsonAsync(
                $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(name)}&l=english&cc=US", ct);
            if (data is null) return null;
            if (!data.Value.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return null;

            var lower = Normalize(name);
            var bestId = items[0].TryGetProperty("id", out var fid) ? fid.GetInt64().ToString() : null;
            foreach (var item in items.EnumerateArray())
            {
                var itemName = item.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                if (Normalize(itemName) == lower)
                {
                    if (item.TryGetProperty("id", out var id)) bestId = id.GetInt64().ToString();
                    break;
                }
            }

            return bestId is null ? null : await FetchSteamAsync(bestId, ct);
        }
        catch (Exception ex)
        {
            Log.Debug("[metadata] Steam search failed for {Name}: {Error}", name, ex.Message);
            return null;
        }
    }

    // ─── Wikipedia (MediaWiki API) ────────────────────────────────────────────

    private async Task<FetchedMetadata?> FetchWikipediaAsync(string name, CancellationToken ct)
    {
        try
        {
            var q = Uri.EscapeDataString(name + " video game");
            var search = await HttpGetJsonAsync(
                $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={q}&srnamespace=0&srlimit=5&format=json", ct);
            if (search is null) return null;
            if (!search.Value.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("search", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0) return null;

            var lower = Normalize(name);
            var bestTitle = results[0].TryGetProperty("title", out var t0) ? (t0.GetString() ?? "") : "";
            foreach (var r in results.EnumerateArray())
            {
                var title = r.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
                var titleLower = Regex.Replace(Normalize(title), "videogame$", "");
                if (titleLower == lower) { bestTitle = title; break; }
            }
            if (string.IsNullOrEmpty(bestTitle)) return null;

            var titleEnc = Uri.EscapeDataString(bestTitle);
            var detail = await HttpGetJsonAsync(
                $"https://en.wikipedia.org/w/api.php?action=query&titles={titleEnc}" +
                "&prop=extracts|pageimages|revisions&exintro=true&explaintext=true" +
                "&pithumbsize=600&rvprop=content&rvslots=main&rvsection=0&format=json", ct);
            if (detail is null) return null;
            if (!detail.Value.TryGetProperty("query", out var dq) ||
                !dq.TryGetProperty("pages", out var pages)) return null;

            JsonElement? page = null;
            foreach (var prop in pages.EnumerateObject()) { page = prop.Value; break; }
            if (page is null) return null;
            if (page.Value.TryGetProperty("missing", out _)) return null;

            var extract = page.Value.TryGetProperty("extract", out var ex) ? (ex.GetString() ?? "") : "";
            if (extract.Length > 500) extract = extract[..500];

            var thumb = page.Value.TryGetProperty("thumbnail", out var th) &&
                        th.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";

            var wikitext = "";
            if (page.Value.TryGetProperty("revisions", out var revs) && revs.ValueKind == JsonValueKind.Array &&
                revs.GetArrayLength() > 0 && revs[0].TryGetProperty("slots", out var slots) &&
                slots.TryGetProperty("main", out var main) && main.TryGetProperty("*", out var wtext))
            {
                wikitext = wtext.GetString() ?? "";
            }

            string InfoField(string field)
            {
                var re = new Regex(@"\|\s*" + Regex.Escape(field) + @"\s*=\s*(.+)", RegexOptions.IgnoreCase);
                var m = re.Match(wikitext);
                if (!m.Success) return "";
                var raw = m.Groups[1].Value;
                raw = Regex.Replace(raw, @"\[\[([^|\]]*\|)?([^\]]*)\]\]", "$2");
                raw = Regex.Replace(raw, @"\{\{[^}]*\}\}", "");
                raw = Regex.Replace(raw, @"<[^>]+>", "");
                return raw.Trim();
            }

            var developer = InfoField("developer");
            var publisher = InfoField("publisher");
            var released = InfoField("released");
            if (string.IsNullOrEmpty(released)) released = InfoField("release_date");
            var genreRaw = InfoField("genre");
            var genres = string.IsNullOrEmpty(genreRaw)
                ? []
                : genreRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(5).ToList();

            if (string.IsNullOrEmpty(extract) && string.IsNullOrEmpty(developer))
                return null;

            return new FetchedMetadata
            {
                Description = extract,
                Developer = developer,
                Publisher = publisher,
                ReleaseDate = Regex.Replace(released, @"\{\{.*?\}\}", "").Trim()
                                   .Substring(0, Math.Min(30, Regex.Replace(released, @"\{\{.*?\}\}", "").Trim().Length)),
                Genres = genres,
                CoverUrl = thumb,
                HeaderUrl = "",
                Screenshots = [],
                Metacritic = null,
                Website = $"https://en.wikipedia.org/wiki/{titleEnc}",
                Source = "wikipedia",
            };
        }
        catch (Exception ex)
        {
            Log.Debug("[metadata] Wikipedia fetch failed for {Name}: {Error}", name, ex.Message);
            return null;
        }
    }

    // ─── SteamGridDB art enhancement ──────────────────────────────────────────

    private async Task<(string? CoverUrl, string? HeaderUrl)?> FetchSteamGridDbArtAsync(
        string name, string apiKey, CancellationToken ct)
    {
        try
        {
            var search = await SgdbGetAsync(
                $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(name)}",
                apiKey, ct);
            if (search is null) return null;
            if (!search.Value.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            if (!search.Value.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;

            var gameId = data[0].GetProperty("id").GetInt64();

            var covers = await SgdbGetAsync(
                $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900&limit=1", apiKey, ct);
            var heroes = await SgdbGetAsync(
                $"https://www.steamgriddb.com/api/v2/heroes/game/{gameId}?limit=1", apiKey, ct);

            string? coverUrl = null, headerUrl = null;
            if (covers?.TryGetProperty("data", out var cd) == true && cd.ValueKind == JsonValueKind.Array &&
                cd.GetArrayLength() > 0 && cd[0].TryGetProperty("url", out var cu))
                coverUrl = cu.GetString();
            if (heroes?.TryGetProperty("data", out var hd) == true && hd.ValueKind == JsonValueKind.Array &&
                hd.GetArrayLength() > 0 && hd[0].TryGetProperty("url", out var hu))
                headerUrl = hu.GetString();

            if (string.IsNullOrEmpty(coverUrl) && string.IsNullOrEmpty(headerUrl)) return null;
            return (coverUrl, headerUrl);
        }
        catch (Exception ex)
        {
            Log.Debug("[metadata] SteamGridDB art fetch failed for {Name}: {Error}", name, ex.Message);
            return null;
        }
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private async Task<JsonElement?> HttpGetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var text = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private async Task<bool> HeadOkAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<JsonElement?> SgdbGetAsync(string url, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var text = await resp.Content.ReadAsStringAsync(ct);
        try { return JsonDocument.Parse(text).RootElement.Clone(); } catch { return null; }
    }

    private static string Normalize(string s) =>
        Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
}
