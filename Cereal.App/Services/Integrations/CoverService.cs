// ─── Cover image download queue & SteamGridDB client ─────────────────────────
// Downloads portrait covers and wide headers for games, caching them locally.
// Uses a background Channel<string> queue with up to 2 retries per game.
// SteamGridDB API key is loaded from CredentialService.

using System.Net;
using System.Threading.Channels;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Integrations;

public sealed class CoverProgressArgs : EventArgs
{
    public int Remaining { get; init; }
    public bool Done { get; init; }
    public int Downloaded { get; init; }
}

public sealed class CoverService : IDisposable
{
    private readonly PathService _paths;
    private readonly DatabaseService _db;
    private readonly CredentialService _creds;
    private readonly HttpClient _http;

    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, int> _retries = [];
    private const int MaxRetries = 2;
    private Task? _workerTask;
    private readonly CancellationTokenSource _workerCts = new();

    public event EventHandler<CoverProgressArgs>? ProgressChanged;

    public CoverService(PathService paths, DatabaseService db, CredentialService creds)
    {
        _paths = paths;
        _db = db;
        _creds = creds;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "cereal-launcher/1.0");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // ─── Queue management ────────────────────────────────────────────────────

    public void EnqueueGame(string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        _queue.Writer.TryWrite(gameId);
        _workerTask ??= Task.Run(() => RunWorkerAsync(_workerCts.Token));
    }

    public void EnqueueAll()
    {
        foreach (var g in _db.Db.Games)
            EnqueueGame(g.Id);
    }

    // ─── Worker loop ─────────────────────────────────────────────────────────

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct))
            {
                var batch = new List<string>();
                while (batch.Count < 5 && _queue.Reader.TryRead(out var id))
                    batch.Add(id);

                var downloaded = 0;
                await Task.WhenAll(batch.Select(async gid =>
                {
                    try
                    {
                        var changed = await ProcessGameAsync(gid, ct);
                        if (changed) Interlocked.Increment(ref downloaded);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[covers] Download failed for {GameId}: {Error}", gid, ex.Message);
                        var retries = (_retries.TryGetValue(gid, out var r) ? r : 0) + 1;
                        if (retries <= MaxRetries)
                        {
                            _retries[gid] = retries;
                            _queue.Writer.TryWrite(gid);
                        }
                        else
                        {
                            _retries.Remove(gid);
                        }
                    }
                }));

                if (downloaded > 0)
                {
                    _db.Save();
                    ProgressChanged?.Invoke(this, new CoverProgressArgs
                    {
                        Remaining = _queue.Reader.Count,
                        Downloaded = downloaded,
                    });
                }

                if (_queue.Reader.Count > 0)
                    await Task.Delay(150, ct);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        finally
        {
            ProgressChanged?.Invoke(this, new CoverProgressArgs { Remaining = 0, Done = true });
        }
    }

    private async Task<bool> ProcessGameAsync(string gameId, CancellationToken ct)
    {
        var game = _db.Db.Games.Find(g => g.Id == gameId);
        if (game is null) return false;

        var changed = false;

        // Cover (portrait)
        if (!IsValidLocalFile(game.LocalCoverPath))
        {
            CleanupFile(game.LocalCoverPath);
            game.LocalCoverPath = null;

            var candidates = new[] { game.CoverUrl, game.SgdbCoverUrl }.OfType<string>().ToList();

            // If no candidates and no header yet, try SteamGridDB lookup
            if (candidates.Count == 0 && string.IsNullOrEmpty(game.HeaderUrl))
            {
                var meta = await TryFetchSteamGridDbAsync(game, ct);
                if (meta is not null)
                {
                    if (!string.IsNullOrEmpty(meta.Value.CoverUrl))  game.CoverUrl  = meta.Value.CoverUrl;
                    if (!string.IsNullOrEmpty(meta.Value.HeaderUrl)) game.HeaderUrl = meta.Value.HeaderUrl;
                    changed = true;
                    if (!string.IsNullOrEmpty(game.CoverUrl)) candidates.Add(game.CoverUrl);
                }
            }

            foreach (var url in candidates)
            {
                try
                {
                    var ext = GetUrlExtension(url);
                    var dest = Path.Combine(_paths.CoversDir, $"cover_{SanitizeId(gameId)}{ext}");
                    await DownloadFileAsync(url, dest, ct);
                    game.LocalCoverPath = dest;
                    game.ImgStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _retries.Remove(gameId);
                    changed = true;
                    break;
                }
                catch { /* try next candidate */ }
            }

            if (string.IsNullOrEmpty(game.LocalCoverPath))
            {
                var total = candidates.Count;
                if (total > 0) throw new Exception($"All {total} cover URL(s) failed");
            }
        }

        // Header (wide)
        if (!IsValidLocalFile(game.LocalHeaderPath) && !string.IsNullOrEmpty(game.HeaderUrl))
        {
            CleanupFile(game.LocalHeaderPath);
            game.LocalHeaderPath = null;

            var ext = GetUrlExtension(game.HeaderUrl);
            var dest = Path.Combine(_paths.HeadersDir, $"header_{SanitizeId(gameId)}{ext}");
            await DownloadFileAsync(game.HeaderUrl, dest, ct);
            game.LocalHeaderPath = dest;
            game.ImgStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            changed = true;
        }

        return changed;
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
        catch { return false; }
    }

    private static void CleanupFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static string GetUrlExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path).Split('?')[0];
            return string.IsNullOrEmpty(ext) ? ".jpg" : ext;
        }
        catch { return ".jpg"; }
    }

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public void Dispose()
    {
        _workerCts.Cancel();
        _queue.Writer.Complete();
        _http.Dispose();
        _workerCts.Dispose();
    }
}
