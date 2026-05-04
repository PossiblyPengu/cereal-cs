using Cereal.Infrastructure.Database;

namespace Cereal.Infrastructure.Repositories;

/// <summary>
/// Persists <see cref="ChiakiConfig"/> as a JSON blob in AppSettings under key "chiaki".
/// </summary>
public sealed class ChiakiConfigRepository(CerealDb db) : IChiakiConfigRepository
{
    private const string Key = "chiaki";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ChiakiConfig> LoadAsync(CancellationToken ct = default)
    {
        using var conn = db.Open();
        var json = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT Data FROM AppSettings WHERE Key = @Key", new { Key });
        if (json is null) return new ChiakiConfig();
        try
        {
            return JsonSerializer.Deserialize<ChiakiConfig>(json, JsonOpts) ?? new ChiakiConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[chiaki] Failed to deserialize ChiakiConfig — using defaults");
            return new ChiakiConfig();
        }
    }

    public async Task SaveAsync(ChiakiConfig config, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(config, JsonOpts);
        using var conn = db.Open();
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO AppSettings(Key, Data) VALUES (@Key, @Data)",
            new { Key, Data = json });
    }
}
