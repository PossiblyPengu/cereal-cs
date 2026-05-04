using System.Text.Json;
using Cereal.Core.Models;
using Cereal.Core.Providers;
using Cereal.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.Infrastructure.Providers;

/// <summary>
/// Imports the GOG library via the GOG Galaxy API (requires auth token).
/// Local detection is not feasible without GOG Galaxy's SQLite file.
/// </summary>
public sealed class GogProvider(IAuthService auth) : IImportProvider
{
    public string PlatformId => "gog";

    public Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default) =>
        Task.Run(DetectLocal, ct);

    public async Task<ImportResult> ImportLibraryAsync(ImportContext ctx, CancellationToken ct = default)
    {
        var session = auth.GetSession("gog");
        if (session is null) return new ImportResult([], [], 0, "GOG account not connected");

        try
        {
            ctx.Notify?.Invoke(new ImportProgress("running", "gog", 0, 0, "Fetching library…"));

            var req = new HttpRequestMessage(HttpMethod.Get,
                "https://embed.gog.com/account/getFilteredProducts?mediaType=1");
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            var resp = await ctx.Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("products", out var products))
                return new ImportResult([], [], 0, "Unexpected GOG response");

            var games = products.EnumerateArray()
                .Select(p =>
                {
                    var id = p.TryGetProperty("id", out var idProp) ? idProp.GetInt64().ToString() : null;
                    var title = p.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var image = p.TryGetProperty("image", out var img) ? img.GetString() : null;
                    return new Game
                    {
                        Name       = title ?? id ?? "?",
                        Platform   = "gog",
                        PlatformId = id,
                        CoverUrl   = image is not null
                            ? $"https:{image}_196.jpg" : null,
                        HeaderUrl  = image is not null
                            ? $"https:{image}_392.jpg" : null,
                        AddedAt    = DateTimeOffset.UtcNow,
                    };
                })
                .Where(g => !string.IsNullOrEmpty(g.Name))
                .ToList();

            var svc = ctx.Services.GetRequiredService<IGameService>();
            var (_, _, survivors) = await svc.UpsertRangeAsync(games, ct);
            return new ImportResult(survivors.Select(g => g.Id).ToList(), [], games.Count, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[gog] Library import failed");
            return new ImportResult([], [], 0, ex.Message);
        }
    }

    private static DetectResult DetectLocal()
    {
        // GOG Galaxy stores its database in a user-specific SQLite file.
        // If Galaxy is not installed we return empty but no error.
        var galaxyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GOG.com", "Galaxy", "storage");

        if (!Directory.Exists(galaxyDir))
            return new DetectResult([]);

        // Phase F: parse Galaxy.db directly via Dapper/SQLite if present.
        // For now, return a no-op result with an informational note.
        return new DetectResult([]);
    }
}
