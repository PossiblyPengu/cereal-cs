using System.Text.Json;
using Cereal.Core.Models;
using Cereal.Core.Services;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Fetches game metadata from the Steam store API and persists via <see cref="IGameService"/>.
/// Falls back gracefully when the game is not on Steam.
/// </summary>
public sealed class MetadataService(
    IGameService gameService,
    IHttpClientFactory httpFactory) : IMetadataService
{
    public async Task FetchAndApplyAsync(string gameId, CancellationToken ct = default)
    {
        var game = await gameService.GetByIdAsync(gameId, ct);
        if (game is null) return;

        // Only fetch from Steam store for Steam games
        if (game.Platform == "steam" && !string.IsNullOrEmpty(game.PlatformId))
        {
            await FetchSteamMetadataAsync(game, ct);
            return;
        }

        Log.Debug("[meta] No metadata source for {Platform} game {Name}", game.Platform, game.Name);
    }

    private async Task FetchSteamMetadataAsync(Game game, CancellationToken ct)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            var url = $"https://store.steampowered.com/api/appdetails?appids={game.PlatformId}&l=en";
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(game.PlatformId!, out var wrapper)) return;
            if (!wrapper.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return;
            if (!wrapper.TryGetProperty("data", out var data)) return;

            var updated = game with
            {
                Description  = data.TryGetProperty("short_description", out var d) ? d.GetString() : game.Description,
                Developer    = ExtractFirst(data, "developers") ?? game.Developer,
                Publisher    = ExtractFirst(data, "publishers") ?? game.Publisher,
                ReleaseDate  = ParseReleaseDateString(data) ?? game.ReleaseDate,
                Website      = data.TryGetProperty("website", out var w) && w.ValueKind != JsonValueKind.Null
                                   ? w.GetString() : game.Website,
                Metacritic   = data.TryGetProperty("metacritic", out var mc)
                                   && mc.TryGetProperty("score", out var sc)
                               ? sc.GetInt32()
                               : game.Metacritic,
                Screenshots  = ParseScreenshots(data) ?? game.Screenshots,
                UpdatedAt    = DateTimeOffset.UtcNow,
            };

            await gameService.UpdateMetadataAsync(updated, ct);
            Log.Information("[meta] Updated metadata for {Name}", game.Name);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[meta] Failed to fetch Steam metadata for {Name}", game.Name);
        }
    }

    private static string? ExtractFirst(JsonElement data, string key)
    {
        if (!data.TryGetProperty(key, out var arr)) return null;
        if (arr.ValueKind != JsonValueKind.Array) return null;
        return arr.EnumerateArray().FirstOrDefault().GetString();
    }

    private static string? ParseReleaseDateString(JsonElement data)
    {
        if (!data.TryGetProperty("release_date", out var rd)) return null;
        if (!rd.TryGetProperty("date", out var dt)) return null;
        return dt.GetString();
    }

    private static IReadOnlyList<string>? ParseScreenshots(JsonElement data)
    {
        if (!data.TryGetProperty("screenshots", out var arr)) return null;
        if (arr.ValueKind != JsonValueKind.Array) return null;
        var urls = arr.EnumerateArray()
            .Select(s => s.TryGetProperty("path_full", out var p) ? p.GetString() : null)
            .Where(u => u is not null)
            .Cast<string>()
            .ToList();
        return urls.Count > 0 ? urls : null;
    }
}
