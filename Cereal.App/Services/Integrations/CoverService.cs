// ─── Cover image download queue & SteamGridDB client ─────────────────────────
// Downloads portrait covers and wide headers for games in parallel (up to 4 at once).
// Fires ProgressChanged with GameId+LocalPath per download, Done=true when the batch
// completes. SteamGridDB API key is loaded from CredentialService.

using Cereal.App.Models;
using Cereal.App.Services.Metadata;
using Serilog;

namespace Cereal.App.Services.Integrations;

public sealed class CoverProgressArgs : EventArgs
{
    /// <summary>Game whose cover was just saved. Null on Done events.</summary>
    public string? GameId { get; init; }
    /// <summary>Local file path of the newly downloaded cover.</summary>
    public string? LocalPath { get; init; }
    /// <summary>Games still pending in this batch.</summary>
    public int Remaining { get; init; }
    /// <summary>Total games in this batch (for progress-bar math).</summary>
    public int Total { get; init; }
    /// <summary>True when the batch is finished.</summary>
    public bool Done { get; init; }
}

public sealed class CoverService : IDisposable
{
    private readonly PathService _paths;
    private readonly DatabaseService _db;
    private readonly CredentialService _creds;
    private readonly MetadataService _metadata;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();

    private const int MaxParallel = 4;

    public event EventHandler<CoverProgressArgs>? ProgressChanged;

    public CoverService(PathService paths, DatabaseService db, CredentialService creds, MetadataService metadata)
    {
        _paths = paths;
        _db = db;
        _creds = creds;
        _metadata = metadata;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "cereal-launcher/1.0");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Queues a cover download for a single game (e.g. after adding it).</summary>
    public void EnqueueGame(string gameId)
    {
        if (!string.IsNullOrEmpty(gameId))
            _ = RunBatchAsync([gameId], _cts.Token);
    }

    /// <summary>Downloads covers for all games that don't already have one.</summary>
    public void EnqueueAll()
    {
        var ids = _db.Db.Games.Select(g => g.Id).ToList();
        _ = RunBatchAsync(ids, _cts.Token);
    }

    // ─── Batch runner ─────────────────────────────────────────────────────────

    private async Task RunBatchAsync(IReadOnlyList<string> gameIds, CancellationToken ct)
    {
        // Skip games that already have a valid cached cover.
        var toProcess = gameIds
            .Select(id => _db.Db.Games.Find(g => g.Id == id))
            .OfType<Game>()
            .Where(g => !IsValidLocalFile(g.LocalCoverPath))
            .Select(g => g.Id)
            .ToList();

        if (toProcess.Count == 0)
        {
            ProgressChanged?.Invoke(this, new CoverProgressArgs { Done = true });
            return;
        }

        var total = toProcess.Count;
        var remaining = total;
        var sem = new SemaphoreSlim(MaxParallel, MaxParallel);
        try
        {
            var tasks = toProcess.Select(async id =>
            {
                await sem.WaitAsync(ct);
                string? localPath = null;
                try
                {
                    localPath = await ProcessGameAsync(id, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Warning("[covers] {Id}: {Error}", id, ex.Message);
                }
                finally
                {
                    sem.Release();
                    var rem = Interlocked.Decrement(ref remaining);
                    if (localPath is not null)
                    {
                        ProgressChanged?.Invoke(this, new CoverProgressArgs
                        {
                            GameId = id,
                            LocalPath = localPath,
                            Remaining = rem,
                            Total = total,
                        });
                    }
                }
            }).ToList();

            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { return; }
        }
        finally
        {
            sem.Dispose();
        }

        _db.Save();
        ProgressChanged?.Invoke(this, new CoverProgressArgs { Done = true, Total = total });
    }

    // ─── Per-game processing ──────────────────────────────────────────────────

    /// <summary>Downloads cover and header for one game. Returns the cover path if newly downloaded, null otherwise.</summary>
    private async Task<string?> ProcessGameAsync(string gameId, CancellationToken ct)
    {
        var game = _db.Db.Games.Find(g => g.Id == gameId);
        if (game is null) return null;

        string? newCoverPath = null;

        if (!IsValidLocalFile(game.LocalCoverPath))
        {
            CleanupFile(game.LocalCoverPath);
            game.LocalCoverPath = null;
            newCoverPath = await DownloadCoverAsync(game, gameId, ct);
            if (newCoverPath is not null)
            {
                game.LocalCoverPath = newCoverPath;
                game.ImgStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        if (!IsValidLocalFile(game.LocalHeaderPath) && !string.IsNullOrEmpty(game.HeaderUrl))
        {
            CleanupFile(game.LocalHeaderPath);
            game.LocalHeaderPath = null;
            var ext = GetUrlExtension(game.HeaderUrl);
            var dest = Path.Combine(_paths.HeadersDir, $"header_{SanitizeId(gameId)}{ext}");
            try
            {
                await DownloadFileAsync(game.HeaderUrl, dest, ct);
                game.LocalHeaderPath = dest;
                game.ImgStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[covers] Header failed for {GameId}", gameId);
            }
        }

        return newCoverPath;
    }

    /// <summary>Resolves the best available cover URL, downloads it, and returns the local path.</summary>
    private async Task<string?> DownloadCoverAsync(Game game, string gameId, CancellationToken ct)
    {
        var candidates = BuildCoverCandidates(game);

        // No stored URLs → try SteamGridDB (requires API key) first
        if (candidates.Count == 0)
        {
            var sgdb = await TryFetchSteamGridDbAsync(game, ct);
            if (sgdb is not null)
            {
                if (!string.IsNullOrEmpty(sgdb.Value.CoverUrl))
                {
                    game.CoverUrl = sgdb.Value.CoverUrl;
                    candidates.Add(sgdb.Value.CoverUrl);
                }
                if (!string.IsNullOrEmpty(sgdb.Value.HeaderUrl) && string.IsNullOrEmpty(game.HeaderUrl))
                    game.HeaderUrl = sgdb.Value.HeaderUrl;
            }
        }

        foreach (var url in candidates)
        {
            var ext = GetUrlExtension(url);
            var dest = Path.Combine(_paths.CoversDir, $"cover_{SanitizeId(gameId)}{ext}");
            try
            {
                await DownloadFileAsync(url, dest, ct);
                return dest;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[covers] URL failed for {GameId}: {Url}", gameId, url);
            }
        }

        // All known URLs failed or none existed. Search Steam store by game name —
        // this works for any platform (GOG, Epic, custom) and requires no API key.
        return await TryMetadataFallbackAsync(game, gameId, ct);
    }

    /// <summary>
    /// Last-resort fallback: asks MetadataService to search Steam by name.
    /// Works for any platform. Results are cached in MetadataService for 7 days.
    /// </summary>
    private async Task<string?> TryMetadataFallbackAsync(Game game, string gameId, CancellationToken ct)
    {
        try
        {
            var meta = await _metadata.FetchAsync(game, ct);
            var url = meta?.CoverUrl;
            if (string.IsNullOrEmpty(url)) return null;

            // Persist the URL so future restarts don't need to search again.
            if (string.IsNullOrEmpty(game.CoverUrl))
                game.CoverUrl = url;

            var ext = GetUrlExtension(url);
            var dest = Path.Combine(_paths.CoversDir, $"cover_{SanitizeId(gameId)}{ext}");
            await DownloadFileAsync(url, dest, ct);
            return dest;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[covers] Metadata fallback failed for {GameId}", gameId);
            return null;
        }
    }

    /// <summary>Builds an ordered list of cover URL candidates from stored game data.</summary>
    private static List<string> BuildCoverCandidates(Game game)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(game.CoverUrl))     candidates.Add(game.CoverUrl);
        if (!string.IsNullOrEmpty(game.SgdbCoverUrl)) candidates.Add(game.SgdbCoverUrl);

        // Steam CDN fallback for games whose CoverUrl wasn't stored at scan time.
        if (candidates.Count == 0
            && string.Equals(game.Platform, "steam", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(game.PlatformId))
        {
            var sid = game.PlatformId;
            candidates.Add($"https://shared.steamstatic.com/store_item_assets/steam/apps/{sid}/library_600x900_2x.jpg");
            candidates.Add($"https://shared.steamstatic.com/store_item_assets/steam/apps/{sid}/library_600x900.jpg");
        }

        return candidates;
    }

    // ─── SteamGridDB ─────────────────────────────────────────────────────────

    private async Task<(string? CoverUrl, string? HeaderUrl)?> TryFetchSteamGridDbAsync(
        Game game, CancellationToken ct)
    {
        var apiKey = _creds.GetPassword("cereal", "steamgriddb_key");
        if (string.IsNullOrEmpty(apiKey)) return null;

        try
        {
            // Search by name
            var searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(game.Name)}";
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var searchResp = await _http.SendAsync(searchReq, ct);
            if (!searchResp.IsSuccessStatusCode) return null;

            using var searchDoc = System.Text.Json.JsonDocument.Parse(
                await searchResp.Content.ReadAsStringAsync(ct));
            var root = searchDoc.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return null;
            var sgdbId = data[0].GetProperty("id").GetInt64();

            // Fetch portrait cover (type=600x900)
            string? coverUrl = null;
            string? headerUrl = null;

            var gridsUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{sgdbId}?dimensions=600x900&limit=1";
            using var gridsReq = new HttpRequestMessage(HttpMethod.Get, gridsUrl);
            gridsReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var gridsResp = await _http.SendAsync(gridsReq, ct);
            if (gridsResp.IsSuccessStatusCode)
            {
                using var gridsDoc = System.Text.Json.JsonDocument.Parse(
                    await gridsResp.Content.ReadAsStringAsync(ct));
                var gd = gridsDoc.RootElement;
                if (gd.TryGetProperty("data", out var gdata) && gdata.GetArrayLength() > 0)
                    coverUrl = gdata[0].TryGetProperty("url", out var u) ? u.GetString() : null;
            }

            // Fetch hero / header
            var heroesUrl = $"https://www.steamgriddb.com/api/v2/heroes/game/{sgdbId}?limit=1";
            using var heroesReq = new HttpRequestMessage(HttpMethod.Get, heroesUrl);
            heroesReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var heroesResp = await _http.SendAsync(heroesReq, ct);
            if (heroesResp.IsSuccessStatusCode)
            {
                using var heroesDoc = System.Text.Json.JsonDocument.Parse(
                    await heroesResp.Content.ReadAsStringAsync(ct));
                var hd = heroesDoc.RootElement;
                if (hd.TryGetProperty("data", out var hdata) && hdata.GetArrayLength() > 0)
                    headerUrl = hdata[0].TryGetProperty("url", out var u) ? u.GetString() : null;
            }

            return (coverUrl, headerUrl);
        }
        catch (Exception ex)
        {
            Log.Debug("[covers] SteamGridDB lookup failed for {Name}: {Error}", game.Name, ex.Message);
            return null;
        }
    }

    /// <summary>Validates a SteamGridDB API key by looking up a known game (HL2, Steam ID 220).</summary>
    public async Task<(bool Ok, string? Error)> ValidateSteamGridDbKeyAsync(string apiKey, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://www.steamgriddb.com/api/v2/games/steam/220");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return (false, $"HTTP {(int)resp.StatusCode}");
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync(ct));
            var success = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
            return success ? (true, null) : (false, "Invalid key");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ─── HTTP download ────────────────────────────────────────────────────────

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}", null, resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length < 1024)
            throw new InvalidDataException($"File too small ({bytes.Length} bytes)");

        await File.WriteAllBytesAsync(destPath, bytes, ct);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsValidLocalFile(string? path)
    {
        try { return !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length >= 1024; }
        catch (Exception ex)
        {
            Log.Debug(ex, "[covers] IsValidLocalFile failed for {Path}", path);
            return false;
        }
    }

    private static void CleanupFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Debug(ex, "[covers] Failed deleting file {Path}", path); }
    }

    private static string GetUrlExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path).Split('?')[0];
            return string.IsNullOrEmpty(ext) ? ".jpg" : ext;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[covers] Failed to infer extension from URL: {Url}", url);
            return ".jpg";
        }
    }

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }
}
