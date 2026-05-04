using System.Text.Json;
using Cereal.Core.Models;
using Cereal.Core.Providers;
using Cereal.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.Infrastructure.Providers;

/// <summary>
/// Detects locally installed Epic Games Store titles via ProgramData manifest files.
/// Imports the full library via the Epic Library Service API (requires auth token).
/// </summary>
public sealed class EpicProvider(IAuthService auth) : IImportProvider
{
    public string PlatformId => "epic";

    public Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default) =>
        Task.Run(Detect, ct);

    public async Task<ImportResult> ImportLibraryAsync(ImportContext ctx, CancellationToken ct = default)
    {
        var session = auth.GetSession("epic");
        if (session is null) return new ImportResult([], [], 0, "Epic account not connected");

        try
        {
            ctx.Notify?.Invoke(new ImportProgress("running", "epic", 0, 0, "Fetching library…"));

            var req = new HttpRequestMessage(HttpMethod.Get,
                "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true");
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            var resp = await ctx.Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var records = doc.RootElement.TryGetProperty("records", out var r)
                ? r : doc.RootElement;
            if (records.ValueKind != JsonValueKind.Array)
            return new ImportResult([], [], 0, "Unexpected response from Epic");

            var games = records.EnumerateArray()
                .Where(r => r.TryGetProperty("catalogItemId", out _))
                .Select(r =>
                {
                    var ns   = r.TryGetProperty("catalogNamespace",  out var n) ? n.GetString() : null;
                    var id   = r.TryGetProperty("catalogItemId",     out var i) ? i.GetString() : null;
                    var name = r.TryGetProperty("title",             out var t) ? t.GetString() : null;
                    return new Game
                    {
                        Name                  = name ?? id ?? "Unknown",
                        Platform              = "epic",
                        PlatformId            = ns,
                        EpicCatalogItemId     = id,
                        EpicNamespace         = ns,
                        AddedAt               = DateTimeOffset.UtcNow,
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
            Log.Warning(ex, "[epic] Library import failed");
            return new ImportResult([], [], 0, ex.Message);
        }
    }

    private static DetectResult Detect()
    {
        var games = new List<Game>();
        try
        {
            var manifestDir = Path.Combine(
                Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData",
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifestDir)) return new DetectResult(games);

            foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    var name    = root.TryGetProperty("DisplayName",      out var dn) ? dn.GetString() : null;
                    var installLoc = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                    if (name is null) continue;

                    var ns    = root.TryGetProperty("CatalogNamespace",  out var cn) ? cn.GetString() : null;
                    var appName = root.TryGetProperty("AppName",         out var an) ? an.GetString() : null;
                    var exe   = root.TryGetProperty("LaunchExecutable",  out var le) ? le.GetString() : null;

                    games.Add(new Game
                    {
                        Name             = name,
                        Platform         = "epic",
                        PlatformId       = ns ?? appName,
                        EpicAppName      = appName,
                        EpicNamespace    = ns,
                        ExePath          = exe is not null && installLoc is not null
                                               ? Path.Combine(installLoc, exe) : null,
                        IsInstalled      = true,
                        AddedAt          = DateTimeOffset.UtcNow,
                    });
                }
                catch (Exception ex) { Log.Debug(ex, "[epic] Skipping bad manifest: {File}", file); }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[epic] DetectInstalled error"); }

        return new DetectResult(games);
    }
}
