using Cereal.Core.Models;

namespace Cereal.Core.Repositories;

/// <summary>
/// Sole write path for <see cref="Game"/> persistence.
/// All services that need to mutate the library must go through this interface.
/// </summary>
public interface IGameRepository
{
    /// <summary>Return the full library, categories joined.</summary>
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct = default);

    Task<Game?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Game?> FindByPlatformIdAsync(string platform, string platformId, CancellationToken ct = default);

    /// <summary>Case-insensitive canonical-name match within a platform.</summary>
    Task<Game?> FindByCanonicalNameAsync(string platform, string canonicalName, CancellationToken ct = default);

    /// <summary>Case-insensitive full-text search over name, developer, publisher.</summary>
    Task<IReadOnlyList<Game>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>INSERT OR REPLACE a game.  Fires <see cref="Messaging.LibraryMessages"/> via IMessenger.</summary>
    Task SaveAsync(Game game, CancellationToken ct = default);

    /// <summary>Batch save — single transaction, single messenger notification per game.</summary>
    Task SaveRangeAsync(IEnumerable<Game> games, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Delete every game, category link, and playtime record in a single transaction.</summary>
    Task DeleteAllAsync(CancellationToken ct = default);

    /// <summary>Fast single-column flag update — avoids a full read + INSERT OR REPLACE.</summary>
    Task SetFlagAsync(string id, string column, bool value, CancellationToken ct = default);

    Task<int> CountAsync(string? platform = null, CancellationToken ct = default);

    /// <summary>Count games with <c>IsInstalled = 1</c>, optionally filtered by platform.</summary>
    Task<int> CountInstalledAsync(string? platform = null, CancellationToken ct = default);

    /// <summary>
    /// Returns game counts grouped by platform in a single SQL query.
    /// Avoids N round-trips when a caller needs counts for multiple platforms at once.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetPlatformCountsAsync(CancellationToken ct = default);

    /// <summary>Replace the full category list for a game.</summary>
    Task SetCategoriesAsync(string gameId, IEnumerable<string> categories, CancellationToken ct = default);

    /// <summary>
    /// Add <paramref name="additionalMinutes"/> to the stored playtime and update LastPlayedAt.
    /// </summary>
    Task UpdatePlaytimeAsync(string gameId, int additionalMinutes, DateTimeOffset lastPlayed,
        CancellationToken ct = default);
}
