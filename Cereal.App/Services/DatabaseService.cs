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
        foreach (var filePath in new[] { _dbPath, _backupPath })
        {
            try
            {
                if (!File.Exists(filePath)) continue;
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<Database>(json, JsonOpts);
                if (data is null) continue;

                if (filePath == _backupPath)
                    Log.Warning("[db] Loaded from backup — primary was corrupt");

                // Purge ephemeral PSN/psremote session stubs on load
                var before = data.Games.Count;
                data.Games = data.Games
                    .Where(g => g.Platform != "psn" && g.Platform != "psremote")
                    .ToList();
                if (data.Games.Count != before)
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

    private void WriteSync(Database data)
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Copy(_dbPath, _backupPath, overwrite: true);
        }
        catch { /* best-effort backup */ }

        var tmp = _dbPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOpts));
        File.Move(tmp, _dbPath, overwrite: true);
    }
}
