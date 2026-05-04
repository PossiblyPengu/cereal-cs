using Cereal.Infrastructure.Database;

namespace Cereal.Infrastructure.Repositories;

/// <summary>
/// Persists the <see cref="Settings"/> object as a single JSON blob
/// in the <c>AppSettings</c> table under the key <c>"settings"</c>.
/// </summary>
public sealed class SettingsRepository(CerealDb db) : ISettingsRepository
{
    private const string Key = "settings";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Settings> LoadAsync(CancellationToken ct = default)
    {
        using var conn = db.Open();
        var json = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT Data FROM AppSettings WHERE Key = @Key", new { Key });

        if (json is null) return new Settings();

        try
        {
            return JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[settings] Failed to deserialize settings — using defaults");
            return new Settings();
        }
    }

    public async Task SaveAsync(Settings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        using var conn = db.Open();
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO AppSettings(Key, Data) VALUES (@Key, @Data)",
            new { Key, Data = json });
    }
}
