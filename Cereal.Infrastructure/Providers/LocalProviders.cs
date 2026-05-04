using System.Text.Json;
using Cereal.Core.Models;
using Cereal.Core.Providers;

namespace Cereal.Infrastructure.Providers;

/// <summary>
/// Scans common local launcher directories to detect EA, Ubisoft Connect, itch.io,
/// and Battle.net installed games (read-only detection, no API import needed).
/// </summary>
public static class LocalProviders
{
    public static IEnumerable<IProvider> All => [new EaProvider(), new UbisoftProvider(), new ItchProvider()];
}

// ── EA App / EA Desktop ───────────────────────────────────────────────────────

public sealed class EaProvider : IProvider
{
    public string PlatformId => "ea";

    public Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default) =>
        Task.Run(Detect, ct);

    private static DetectResult Detect()
    {
        var games = new List<Game>();
        try
        {
            var manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "EA Desktop", "InstallData");
            if (!Directory.Exists(manifests)) return new DetectResult(games);

            foreach (var dir in Directory.EnumerateDirectories(manifests))
            {
                var installerData = Path.Combine(dir, "installerdata.xml");
                if (!File.Exists(installerData)) continue;
                try
                {
                    var content = File.ReadAllText(installerData);
                    // Minimal parse: grab contentID and name from XML.
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(
                        content, @"<contentID>([^<]+)</contentID>");
                    var titleMatch = System.Text.RegularExpressions.Regex.Match(
                        content, @"<displayName>([^<]+)</displayName>");
                    if (!nameMatch.Success) continue;
                    games.Add(new Game
                    {
                        Name        = titleMatch.Success ? titleMatch.Groups[1].Value : nameMatch.Groups[1].Value,
                        Platform    = "ea",
                        PlatformId  = nameMatch.Groups[1].Value,
                        EaOfferId   = nameMatch.Groups[1].Value,
                        IsInstalled = true,
                        AddedAt     = DateTimeOffset.UtcNow,
                    });
                }
                catch (Exception ex) { Log.Debug(ex, "[ea] Skipping {Dir}", dir); }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ea] DetectInstalled error"); }
        return new DetectResult(games);
    }
}

// ── Ubisoft Connect ───────────────────────────────────────────────────────────

public sealed class UbisoftProvider : IProvider
{
    public string PlatformId => "ubisoft";

    public Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default) =>
        Task.Run(Detect, ct);

    private static DetectResult Detect()
    {
        var games = new List<Game>();
        try
        {
            // Ubisoft stores installed game IDs in the registry under Uplay Install entries.
            if (!OperatingSystem.IsWindows()) return new DetectResult(games);
            using var ubi = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (ubi is null) return new DetectResult(games);

            foreach (var subKey in ubi.GetSubKeyNames())
            {
                using var sub = ubi.OpenSubKey(subKey);
                var installDir = sub?.GetValue("InstallDir") as string;
                if (installDir is null) continue;
                var exeName = Path.GetFileName(
                    Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly)
                             .FirstOrDefault() ?? "");
                games.Add(new Game
                {
                    Name            = exeName.Replace(".exe", ""),
                    Platform        = "ubisoft",
                    PlatformId      = subKey,
                    UbisoftGameId   = subKey,
                    ExePath         = Path.Combine(installDir, exeName),
                    IsInstalled     = true,
                    AddedAt         = DateTimeOffset.UtcNow,
                });
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ubisoft] DetectInstalled error"); }
        return new DetectResult(games);
    }
}

// ── itch.io ───────────────────────────────────────────────────────────────────

public sealed class ItchProvider : IProvider
{
    public string PlatformId => "itchio";

    public Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default) =>
        Task.Run(Detect, ct);

    private static DetectResult Detect()
    {
        var games = new List<Game>();
        try
        {
            // itch app stores its database at %AppData%\itch\db\butler.db (SQLite)
            // and manifests under %AppData%\itch\apps\**\\.itch\receipt.json.json.
            var appsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "itch", "apps");
            if (!Directory.Exists(appsDir)) return new DetectResult(games);

            foreach (var gameDir in Directory.EnumerateDirectories(appsDir))
            {
                var receipt = Path.Combine(gameDir, ".itch", "receipt.json");
                if (!File.Exists(receipt)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(receipt));
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("game", out var g)) continue;
                    var id    = g.TryGetProperty("id",    out var idP) ? idP.GetInt64().ToString() : null;
                    var title = g.TryGetProperty("title", out var tP)  ? tP.GetString() : null;
                    var url   = g.TryGetProperty("url",   out var uP)  ? uP.GetString() : null;
                    if (id is null) continue;
                    games.Add(new Game
                    {
                        Name        = title ?? id,
                        Platform    = "itchio",
                        PlatformId  = id,
                        StoreUrl    = url,
                        IsInstalled = true,
                        AddedAt     = DateTimeOffset.UtcNow,
                    });
                }
                catch (Exception ex) { Log.Debug(ex, "[itch] Skipping {Dir}", gameDir); }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[itch] DetectInstalled error"); }
        return new DetectResult(games);
    }
}
