using System.Runtime.InteropServices;
using System.Text.Json;
using Cereal.App.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Serilog;

namespace Cereal.App.Services.Providers;

// ─── Battle.net ───────────────────────────────────────────────────────────────

public class BattleNetProvider : IProvider
{
    public string PlatformId => "battlenet";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var dir = Path.Combine(
                Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData",
                "Battle.net", "Agent", "data", "cache");
            if (!Directory.Exists(dir)) return Task.FromResult(new DetectResult(games));

            foreach (var file in Directory.GetFiles(dir, "*.catalog", SearchOption.AllDirectories))
            {
                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
                    var name = doc.TryGetProperty("product_name", out var n) ? n.GetString() : null;
                    if (name is null) continue;
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "battlenet",
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[battlenet] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── EA App ───────────────────────────────────────────────────────────────────

public class EaProvider : IProvider
{
    public string PlatformId => "ea";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(new DetectResult(games));

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Electronic Arts");
            if (key is null) return Task.FromResult(new DetectResult(games));

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    var install = sub?.GetValue("Install Dir") as string
                               ?? sub?.GetValue("InstallDir") as string;
                    if (install is null) continue;
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = subName,
                        Platform = "ea",
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ea] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── Ubisoft Connect ──────────────────────────────────────────────────────────

public class UbisoftProvider : IProvider
{
    public string PlatformId => "ubisoft";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Task.FromResult(new DetectResult(games));

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (key is null) return Task.FromResult(new DetectResult(games));

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    var installDir = sub?.GetValue("InstallDir") as string;
                    if (installDir is null) continue;
                    var name = Path.GetFileName(installDir.TrimEnd(Path.DirectorySeparatorChar));
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "ubisoft",
                        PlatformId = subName,
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ubisoft] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── itch.io ─────────────────────────────────────────────────────────────────

public class ItchioProvider : IProvider
{
    public string PlatformId => "itchio";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dbPath = Path.Combine(appData, "itch", "db", "butler.db");
            if (!File.Exists(dbPath)) return Task.FromResult(new DetectResult(games));

            // butler.db is a SQLite database. Schema relevant tables:
            //   caves(id, game_id, install_folder_name, last_touched_at)
            //   games(id, title, cover_url, still_cover_url, type, classification)
            var connStr = $"Data Source={dbPath};Mode=ReadOnly;";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT c.id, g.title, g.cover_url, c.install_folder_name, c.last_touched_at
                FROM caves c
                JOIN games g ON c.game_id = g.id
                WHERE g.classification = 'game'
                ORDER BY g.title";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var title = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (string.IsNullOrEmpty(title)) continue;

                games.Add(new Game
                {
                    Id = Guid.NewGuid().ToString("N")[..12],
                    Name = title,
                    Platform = "itchio",
                    PlatformId = reader.IsDBNull(0) ? null : reader.GetString(0),
                    CoverUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                    LastPlayed = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Installed = true,
                    AddedAt = DateTime.UtcNow.ToString("o"),
                });
            }

            Log.Information("[itchio] Found {Count} installed games in butler.db", games.Count);
        }
        catch (SqliteException ex)
        {
            Log.Debug(ex, "[itchio] Could not read butler.db (schema may differ)");
        }
        catch (Exception ex) { Log.Debug(ex, "[itchio] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}

// ─── Xbox ────────────────────────────────────────────────────────────────────

public class XboxProvider : IProvider
{
    public string PlatformId => "xbox";

    public Task<DetectResult> DetectInstalled()
    {
        var games = new List<Game>();
        try
        {
            var xboxGamesDir = @"C:\XboxGames";
            if (Directory.Exists(xboxGamesDir))
            {
                foreach (var dir in Directory.GetDirectories(xboxGamesDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName == "Content") continue;
                    // CamelCase → spaces
                    var name = System.Text.RegularExpressions.Regex.Replace(dirName, @"([A-Z])", " $1").Trim();
                    games.Add(new Game
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Name = name,
                        Platform = "xbox",
                        Installed = true,
                        AddedAt = DateTime.UtcNow.ToString("o"),
                    });
                }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[xbox] DetectInstalled"); }
        return Task.FromResult(new DetectResult(games));
    }
}
