using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Cereal.Core.Models;
using Cereal.Core.Services;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Downloads game covers in a bounded background queue (max 4 parallel).
/// Stores files under <see cref="PathService.CoversDir"/>.
/// Sends <see cref="Cereal.Core.Messaging.GameCoverUpdatedMessage"/> after each successful download
/// (via <see cref="IGameService.UpdateCoverAsync"/>).
/// </summary>
public sealed class CoverService : ICoverService, IDisposable
{
    private readonly PathService _paths;
    private readonly IGameService _games;
    private readonly ISettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;

    private readonly SemaphoreSlim _throttle = new(4, 4);
    private readonly ConcurrentDictionary<string, byte> _inflight = [];

    public CoverService(
        PathService paths,
        IGameService games,
        ISettingsService settings,
        IHttpClientFactory httpFactory)
    {
        _paths      = paths;
        _games      = games;
        _settings   = settings;
        _httpFactory = httpFactory;
    }

    // ── Public interface ──────────────────────────────────────────────────────

    public void EnqueueDownload(string gameId, string? coverUrl, string? headerUrl)
    {
        if (!string.IsNullOrEmpty(coverUrl) && _inflight.TryAdd(gameId + ":cover", 0))
            _ = DownloadAndPersistAsync(gameId, coverUrl, CoverType.Cover);

        if (!string.IsNullOrEmpty(headerUrl) && _inflight.TryAdd(gameId + ":header", 0))
            _ = DownloadAndPersistAsync(gameId, headerUrl, CoverType.Header);
    }

    public async Task<IReadOnlyList<CoverCandidate>> SearchAsync(string gameName,
        CancellationToken ct = default)
    {
        var key = _settings.Current.SteamGridDbKey;
        if (string.IsNullOrEmpty(key)) return [];

        try
        {
            using var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", key);

            var url = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(gameName)}";
            var resp = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(resp);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

            // Find game id, then fetch grids
            var gameId = data.EnumerateArray().FirstOrDefault().TryGetProperty("id", out var idProp)
                ? idProp.GetInt32()
                : 0;
            if (gameId == 0) return [];

            var gridUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900";
            var gridResp = await http.GetStringAsync(gridUrl, ct);
            using var gridDoc = JsonDocument.Parse(gridResp);

            if (!gridDoc.RootElement.TryGetProperty("data", out var grids)) return [];

            return grids.EnumerateArray()
                .Select(g => new CoverCandidate(
                    Url:          g.TryGetProperty("url",   out var u) ? u.GetString()! : "",
                    ThumbnailUrl: g.TryGetProperty("thumb", out var t) ? t.GetString()! : "",
                    Width:        g.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                    Height:       g.TryGetProperty("height",out var h) ? h.GetInt32() : 0,
                    Style:        g.TryGetProperty("style", out var s) ? s.GetString() : null))
                .Where(c => !string.IsNullOrEmpty(c.Url))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[cover] SteamGridDB search failed for {Name}", gameName);
            return [];
        }
    }

    public async Task<string?> DownloadAndSaveAsync(string gameId, string url, CoverType type,
        CancellationToken ct = default)
    {
        var local = await FetchToFileAsync(gameId, url, type, ct);
        if (local is null) return null;

        // Pass null for the path we are NOT updating — IGameService.UpdateCoverAsync
        // will null-coalesce to the existing stored value so nothing is overwritten.
        var imgStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _games.UpdateCoverAsync(
            gameId,
            type == CoverType.Cover   ? local : null,
            type == CoverType.Header  ? local : null,
            imgStamp, ct);

        return local;
    }

    public void Dispose() => _throttle.Dispose();

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task DownloadAndPersistAsync(string gameId, string url, CoverType type)
    {
        try
        {
            await _throttle.WaitAsync();
            await DownloadAndSaveAsync(gameId, url, type);
        }
        finally
        {
            _throttle.Release();
            _inflight.TryRemove(gameId + ":" + (type == CoverType.Cover ? "cover" : "header"), out _);
        }
    }

    private async Task<string?> FetchToFileAsync(string gameId, string url, CoverType type,
        CancellationToken ct = default)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (bytes.Length == 0) return null;

            var ext  = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var suffix = type == CoverType.Header ? "_header" : "";
            var fileName = $"{gameId}{suffix}{ext}";
            var path = Path.Combine(_paths.CoversDir, fileName);

            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[cover] Failed to download {Url} for {Id}", url, gameId);
            return null;
        }
    }
}
