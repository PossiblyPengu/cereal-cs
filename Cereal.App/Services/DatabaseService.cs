using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services;

public class DatabaseService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly List<string> DefaultCategories =
    [
        "Action", "Adventure", "RPG", "Strategy", "Puzzle",
        "Simulation", "Sports", "FPS", "Indie", "Multiplayer"
    ];

    private readonly string _dbPath;
    private readonly string _backupPath;
    private Database _db = new();

    private Timer? _saveTimer;
    private readonly object _saveLock = new();

    public Database Db => _db;

    public DatabaseService(PathService paths)
    {
        _dbPath = paths.DatabasePath;
        _backupPath = _dbPath + ".bak";
    }

    public Database Load()
    {
        // Try primary first, then a ring of dated backups.
        var candidates = new List<string> { _dbPath, _backupPath };
        candidates.AddRange(Directory
            .EnumerateFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + ".bak*")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f)));

        foreach (var filePath in candidates.Distinct())
        {
            try
            {
                if (!File.Exists(filePath)) continue;
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<Database>(json, JsonOpts);
                if (data is null) continue;

                if (filePath != _dbPath)
                    Log.Warning("[db] Loaded from backup {Path} — primary was unreadable", filePath);

                // Apply one-time schema migrations.
                var migrated = Migrate(data);

                // Keep persisted PSN rows (auto-created by Chiaki title_change) so
                // they survive restarts like the Electron version.
                if (migrated)
                    WriteSync(data, sanitizeSecrets: false);

                _db = data;
                return _db;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[db] Failed to load {Path}", filePath);
            }
        }

        // Fresh start
        _db = new Database { Categories = new List<string>(DefaultCategories) };
        WriteSync(_db);
        return _db;
    }

    // ─── Schema migrations ──────────────────────────────────────────────────────
    // Upgrades an older `Database` document to `CurrentSchemaVersion` in-place.
    // Returns true when any changes were made (caller persists the file).
    private static bool Migrate(Database data)
    {
        var migrated = false;

        if (data.SchemaVersion < 1)
        {
            // v0 → v1: no field shape changes, just stamp a version.
            data.SchemaVersion = 1;
            migrated = true;
        }

        if (data.SchemaVersion < 2)
        {
            // v1 → v2: schema bump for secure-credential migration. Legacy token
            // fields are migrated to CredentialService by AuthService at startup,
            // then scrubbed on subsequent saves.
            data.SchemaVersion = 2;
            migrated = true;
        }

        if (data.SchemaVersion < 3)
        {
            // v2 → v3: trim platform IDs, merge duplicate rows (same platform + id).
            NormalizeGamePlatformIds(data.Games);
            var dupes = MergeDuplicateGames(data.Games, data.Playtime);
            if (dupes > 0)
                Log.Information("[db] Migration v3 merged {Count} duplicate game row(s)", dupes);
            data.SchemaVersion = 3;
            migrated = true;
        }

        if (data.SchemaVersion > Database.CurrentSchemaVersion)
        {
            // Database written by a newer Cereal build. We don't downgrade; we just
            // warn and keep going — the caller's code may still read it safely.
            Log.Warning("[db] Document schemaVersion={V} newer than app ({Cur})",
                data.SchemaVersion, Database.CurrentSchemaVersion);
        }

        data.Settings ??= new Settings();
        // Older JSON often omits defaultView → null at runtime; persist explicit orbit.
        if (string.IsNullOrWhiteSpace(data.Settings.DefaultView))
        {
            data.Settings.DefaultView = "orbit";
            migrated = true;
        }

        return migrated;
    }

    private static void NormalizeGamePlatformIds(List<Game> games)
    {
        foreach (var g in games)
            g.PlatformId = string.IsNullOrWhiteSpace(g.PlatformId) ? null : g.PlatformId.Trim();
    }

    /// <summary>Collapse duplicate library rows that share platform + platformId.</summary>
    /// <returns>Number of duplicate rows removed.</returns>
    private static int MergeDuplicateGames(List<Game> games, Dictionary<string, int> playtime)
    {
        var groups = games
            .Where(g => !string.IsNullOrWhiteSpace(g.PlatformId))
            .GroupBy(g => ($"{g.Platform}".ToLowerInvariant(), g.PlatformId!));

        var removed = new HashSet<Game>();
        foreach (var grp in groups)
        {
            var list = grp.ToList();
            if (list.Count < 2) continue;

            // Keep the oldest AddedAt when available, else first in file order.
            var keep = list
                .OrderBy(g => g.AddedAt ?? "\uffff")
                .ThenBy(g => g.Id, StringComparer.Ordinal)
                .First();
            foreach (var dup in list)
            {
                if (ReferenceEquals(dup, keep)) continue;
                MergeGameRow(keep, dup, playtime);
                removed.Add(dup);
            }
        }

        if (removed.Count == 0) return 0;
        games.RemoveAll(removed.Contains);
        foreach (var g in removed)
            playtime.Remove(g.Id);
        return removed.Count;
    }

    private static void MergeGameRow(Game keep, Game dup, Dictionary<string, int> playtime)
    {
        keep.Name = string.IsNullOrWhiteSpace(keep.Name) ? dup.Name : keep.Name;
        keep.PlatformId ??= dup.PlatformId;
        keep.CoverUrl ??= dup.CoverUrl;
        keep.HeaderUrl ??= dup.HeaderUrl;
        keep.LocalCoverPath ??= dup.LocalCoverPath;
        keep.LocalHeaderPath ??= dup.LocalHeaderPath;
        keep.ExecutablePath ??= dup.ExecutablePath;
        keep.StoreUrl ??= dup.StoreUrl;
        keep.StreamUrl ??= dup.StreamUrl;
        keep.SgdbCoverUrl ??= dup.SgdbCoverUrl;
        keep.EpicAppName ??= dup.EpicAppName;
        keep.EpicNamespace ??= dup.EpicNamespace;
        keep.EpicCatalogItemId ??= dup.EpicCatalogItemId;
        keep.ChiakiNickname ??= dup.ChiakiNickname;
        keep.ChiakiHost ??= dup.ChiakiHost;
        keep.ChiakiProfile ??= dup.ChiakiProfile;
        keep.ChiakiConsoleId ??= dup.ChiakiConsoleId;
        keep.ChiakiRegistKey ??= dup.ChiakiRegistKey;
        keep.ChiakiMorning ??= dup.ChiakiMorning;
        keep.Description ??= dup.Description;
        keep.Developer ??= dup.Developer;
        keep.Publisher ??= dup.Publisher;
        keep.ReleaseDate ??= dup.ReleaseDate;
        keep.Website ??= dup.Website;
        keep.Notes ??= dup.Notes;

        keep.PlaytimeMinutes = Math.Max(keep.PlaytimeMinutes ?? 0, dup.PlaytimeMinutes ?? 0);
        keep.Favorite = (keep.Favorite == true) || (dup.Favorite == true);
        keep.Hidden = (keep.Hidden == true) || (dup.Hidden == true);
        keep.Installed = (keep.Installed == true) || (dup.Installed == true);

        if (dup.Categories is { Count: > 0 })
        {
            keep.Categories ??= [];
            foreach (var c in dup.Categories)
            {
                if (!keep.Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
                    keep.Categories.Add(c);
            }
        }

        if (dup.Screenshots is { Count: > 0 })
        {
            keep.Screenshots ??= [];
            foreach (var u in dup.Screenshots)
            {
                if (!keep.Screenshots.Contains(u))
                    keep.Screenshots.Add(u);
            }
        }

        if (playtime.TryGetValue(dup.Id, out var dupPt))
        {
            playtime.TryGetValue(keep.Id, out var keepPt);
            playtime[keep.Id] = keepPt + dupPt;
        }

        if (!string.IsNullOrEmpty(dup.LastPlayed))
        {
            if (string.IsNullOrEmpty(keep.LastPlayed) ||
                string.Compare(keep.LastPlayed, dup.LastPlayed, StringComparison.Ordinal) < 0)
                keep.LastPlayed = dup.LastPlayed;
        }
    }

    public void Save()
    {
        lock (_saveLock)
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ =>
            {
                lock (_saveLock) { _saveTimer = null; }
                try { WriteSync(_db); }
                catch (Exception ex) { Log.Error(ex, "[db] Failed to save"); }
            }, null, TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        lock (_saveLock)
        {
            if (_saveTimer is null) return;
            _saveTimer.Dispose();
            _saveTimer = null;
        }
        try { WriteSync(_db); }
        catch (Exception ex) { Log.Error(ex, "[db] Failed to flush"); }
    }

    // Keep this many rolling hourly .bakN files in addition to the `.bak` snapshot
    // that's refreshed on every write.
    private const int RollingBackupCount = 3;
    private static readonly TimeSpan RollingBackupInterval = TimeSpan.FromHours(1);

    private void WriteSync(Database data, bool sanitizeSecrets = true)
    {
        // Defense-in-depth: account secrets are stored in CredentialService.
        // Never persist token material in games.json.
        if (sanitizeSecrets)
            SanitizeSecretsForPersistence(data);
        data.SchemaVersion = Database.CurrentSchemaVersion;

        try
        {
            if (File.Exists(_dbPath))
            {
                File.Copy(_dbPath, _backupPath, overwrite: true);
                RotateRollingBackups();
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[db] Backup copy failed"); }

        var tmp = _dbPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOpts));
        File.Move(tmp, _dbPath, overwrite: true);
    }

    private static void SanitizeSecretsForPersistence(Database data)
    {
        foreach (var acct in data.Accounts.Values)
        {
            acct.AccessToken = null;
            acct.RefreshToken = null;
            acct.Extra?.Remove("xstsToken");
        }
    }

    // Maintains a tiny ring of timestamped backups (`.bak1`, `.bak2`, …) so users
    // can recover from a bad write even after the `.bak` mirror gets overwritten.
    private void RotateRollingBackups()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dbPath)!;
            var name = Path.GetFileName(_dbPath);
            var pattern = name + ".bak*";

            var existing = Directory.EnumerateFiles(dir, pattern)
                .Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();

            var newestAge = existing.Count == 0
                ? TimeSpan.MaxValue
                : DateTime.UtcNow - File.GetLastWriteTimeUtc(existing[0]);
            if (newestAge < RollingBackupInterval) return;

            var target = Path.Combine(dir, $"{name}.bak{DateTime.UtcNow:yyyyMMddHHmm}");
            File.Copy(_backupPath, target, overwrite: true);

            foreach (var stale in existing.Skip(RollingBackupCount - 1))
            {
                try { File.Delete(stale); }
                catch (Exception ex) { Log.Debug(ex, "[db] Failed deleting stale rolling backup: {Path}", stale); }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[db] Rolling backup rotation failed"); }
    }
}
