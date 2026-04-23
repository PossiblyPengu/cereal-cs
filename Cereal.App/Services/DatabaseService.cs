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

                // Purge ephemeral PSN/psremote session stubs on load
                var before = data.Games.Count;
                data.Games = data.Games
                    .Where(g => g.Platform != "psn" && g.Platform != "psremote")
                    .ToList();
                if (data.Games.Count != before || migrated)
                    WriteSync(data);

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

        // Future: if (data.SchemaVersion < 2) { … data.SchemaVersion = 2; migrated = true; }

        if (data.SchemaVersion > Database.CurrentSchemaVersion)
        {
            // Database written by a newer Cereal build. We don't downgrade; we just
            // warn and keep going — the caller's code may still read it safely.
            Log.Warning("[db] Document schemaVersion={V} newer than app ({Cur})",
                data.SchemaVersion, Database.CurrentSchemaVersion);
        }

        return migrated;
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

    private void WriteSync(Database data)
    {
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
                try { File.Delete(stale); } catch { /* best-effort */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[db] Rolling backup rotation failed"); }
    }
}
