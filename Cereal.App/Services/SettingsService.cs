using Cereal.App.Models;

namespace Cereal.App.Services;

public class SettingsService
{
    private readonly DatabaseService _db;

    public event EventHandler<Settings>? SettingsSaved;

    public SettingsService(DatabaseService db) => _db = db;

    public Settings Get() => _db.Db.Settings;

    public Settings Save(Settings updated)
    {
        _db.Db.Settings = updated;
        _db.Save();
        SettingsSaved?.Invoke(this, updated);
        return updated;
    }

    public Settings Reset()
    {
        _db.Db.Settings = new Settings();
        _db.Save();
        return _db.Db.Settings;
    }
}
