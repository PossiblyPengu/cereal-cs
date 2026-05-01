using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Metadata;

/// <summary>IGDB via Twitch OAuth (client credentials). Docs: https://api-docs.igdb.com/</summary>
public sealed partial class MetadataService
{
    private string? IgdbClientId => _creds.GetPassword("cereal", "igdb_client_id");
    private string? IgdbClientSecret => _creds.GetPassword("cereal", "igdb_client_secret");

    private bool HasIgdbCredentials =>
        !string.IsNullOrWhiteSpace(IgdbClientId) && !string.IsNullOrWhiteSpace(IgdbClientSecret);

    private string? _igdbAccessToken;
    private DateTimeOffset _igdbTokenExpiresUtc;

    private async Task<string?> EnsureIgdbTokenAsync(CancellationToken ct)
    {
        if (!HasIgdbCredentials) return null;
        if (!string.IsNullOrEmpty(_igdbAccessToken) && DateTimeOffset.UtcNow < _igdbTokenExpiresUtc)
            return _igdbAccessToken;

        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = IgdbClientId!,
                ["client_secret"] = IgdbClientSecret!,
                ["grant_type"]    = "client_credentials",
            });
            using var resp = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var tok)) return null;
            var token = tok.GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _igdbAccessToken = token;
            _igdbTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 120));
            return token;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[metadata] IGDB token request failed");
            return null;
        }
    }

    private async Task<FetchedMetadata?> FetchIgdbForGameAsync(Game game, CancellationToken ct)
    {
        var token = await EnsureIgdbTokenAsync(ct);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(IgdbClientId)) return null;

        // Resolve IGDB game id: Steam app id, or name search
        int? gameId = null;
        if (string.Equals(game.Platform, "steam", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(game.PlatformId))
        {
            gameId = await ResolveIgdbIdFromSteamAsync(game.PlatformId!, token, ct);
        }

        JsonElement? gameJson = null;
        if (gameId.HasValue)
        {
            var q = "fields name,summary,first_release_date,cover.image_id,genres.name," +
                    "involved_companies.company.name,involved_companies.developer,involved_companies.publisher," +
                    "websites.url,websites.category,screenshots.image_id,total_rating,url; " +
                    $"where id = {gameId.Value}; limit 1;";
            var arr = await IgdbApiPostArrayAsync("games", q, token, ct);
            if (arr is { Count: > 0 }) gameJson = arr[0];
        }

        if (gameJson is null)
        {
            var esc = game.Name.Replace("\"", "\\\"");
            var q = "search \"" + esc + "\"; " +
                    "fields name,summary,first_release_date,cover.image_id,genres.name," +
                    "involved_companies.company.name,involved_companies.developer,involved_companies.publisher," +
                    "websites.url,websites.category,screenshots.image_id,total_rating,url; " +
                    "limit 3;";
            var arr = await IgdbApiPostArrayAsync("games", q, token, ct);
            if (arr is null or { Count: 0 }) return null;
            // Pick best name match
            var lower = Normalize(game.Name);
            gameJson = arr[0];
            foreach (var el in arr)
            {
                if (!el.TryGetProperty("name", out var n)) continue;
                if (Normalize(n.GetString() ?? "") == lower) { gameJson = el; break; }
            }
        }

        return gameJson is { } g ? MapIgdbGameToMetadata(g) : null;
    }

    private async Task<int?> ResolveIgdbIdFromSteamAsync(
        string steamAppId, string token, CancellationToken ct)
    {
        var q = "fields game; where category = 1 & uid = \"" + steamAppId + "\"; limit 1;";
        var arr = await IgdbApiPostArrayAsync("external_games", q, token, ct);
        if (arr is not { Count: > 0 }) return null;
        if (!arr[0].TryGetProperty("game", out var g)) return null;
        return g.ValueKind == JsonValueKind.Number ? g.GetInt32() : null;
    }

    private async Task<List<JsonElement>?> IgdbApiPostArrayAsync(
        string endpoint, string apicalypse, string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint.TrimStart('/'));
            req.Content = new StringContent(apicalypse, Encoding.UTF8, "text/plain");
            req.Headers.TryAddWithoutValidation("Client-ID", IgdbClientId!);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Debug("[metadata] IGDB {Endpoint} failed: {Status}", endpoint, (int)resp.StatusCode);
                return null;
            }
            var text = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            return doc.RootElement.EnumerateArray().ToList();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[metadata] IGDB {Endpoint} request failed", endpoint);
            return null;
        }
    }

    private static FetchedMetadata? MapIgdbGameToMetadata(JsonElement g)
    {
        var name = g.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(name)) return null;

        var summary = g.TryGetProperty("summary", out var s) ? (s.GetString() ?? "") : "";
        if (summary.Length > 500) summary = summary[..500];

        string? coverUrl = null;
        if (g.TryGetProperty("cover", out var cover) && cover.ValueKind == JsonValueKind.Object
            && cover.TryGetProperty("image_id", out var imgId))
        {
            var id = imgId.GetString();
            if (!string.IsNullOrEmpty(id))
                coverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/" + id + ".jpg";
        }

        var genres = new List<string>();
        if (g.TryGetProperty("genres", out var genArr) && genArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var ge in genArr.EnumerateArray())
            {
                if (ge.TryGetProperty("name", out var gn))
                {
                    var gs = gn.GetString();
                    if (!string.IsNullOrWhiteSpace(gs)) genres.Add(gs!);
                }
            }
        }

        string? dev = null, pub = null;
        if (g.TryGetProperty("involved_companies", out var ic) && ic.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in ic.EnumerateArray())
            {
                if (!row.TryGetProperty("company", out var comp) || comp.ValueKind != JsonValueKind.Object) continue;
                var cname = comp.TryGetProperty("name", out var cn) ? (cn.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(cname)) continue;
                if (row.TryGetProperty("developer", out var d) && d.GetBoolean()) dev ??= cname;
                if (row.TryGetProperty("publisher", out var p) && p.GetBoolean()) pub ??= cname;
            }
        }

        var screenshots = new List<string>();
        if (g.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
        {
            foreach (var sh in shots.EnumerateArray().Take(4))
            {
                if (sh.TryGetProperty("image_id", out var sid))
                {
                    var id = sid.GetString();
                    if (!string.IsNullOrEmpty(id))
                        screenshots.Add("https://images.igdb.com/igdb/image/upload/t_screenshot_huge/" + id + ".jpg");
                }
            }
        }

        string? website = null;
        if (g.TryGetProperty("websites", out var ws) && ws.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in ws.EnumerateArray())
            {
                if (!w.TryGetProperty("url", out var u)) continue;
                var url = u.GetString();
                if (string.IsNullOrEmpty(url)) continue;
                if (w.TryGetProperty("category", out var cat) && cat.GetInt32() == 1) { website = url; break; }
                website ??= url;
            }
        }

        // Official site category in IGDB = 1
        string release = "";
        if (g.TryGetProperty("first_release_date", out var frd) && frd.ValueKind == JsonValueKind.Number)
        {
            try
            {
                var dto = DateTimeOffset.FromUnixTimeSeconds(frd.GetInt64());
                release = dto.ToString("yyyy-MM-dd");
            }
            catch { /* ignore */ }
        }

        int? metacritic = null;
        if (g.TryGetProperty("total_rating", out var tr) && tr.ValueKind == JsonValueKind.Number)
            metacritic = (int)Math.Round(tr.GetDouble());

        var pageUrl = g.TryGetProperty("url", out var uu) ? (uu.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(website) && !string.IsNullOrEmpty(pageUrl))
        {
            website = pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? pageUrl
                : "https:" + (pageUrl.StartsWith("//", StringComparison.Ordinal) ? pageUrl : "//" + pageUrl);
        }

        return new FetchedMetadata
        {
            Description = summary,
            Developer   = dev ?? "",
            Publisher   = pub ?? "",
            ReleaseDate = release,
            Genres      = genres,
            CoverUrl    = coverUrl,
            HeaderUrl   = string.IsNullOrEmpty(coverUrl) ? null : coverUrl,
            Screenshots = screenshots,
            Metacritic  = metacritic,
            Website     = website ?? "",
            Source      = "igdb",
            IsSoftware  = false,
        };
    }
}
