using Cereal.Core.Models;

namespace Cereal.Core.Services;

public interface IGameService
{
    /// <summary>Returns all games currently in the library.</summary>
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single game by its internal id, or null if not found.</summary>
    Task<Game?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Full-text search (name, developer, publisher). Used by the search overlay.</summary>
    Task<IReadOnlyList<Game>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>Total game count. Avoids loading the full library just to count.</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Count of games with <c>IsInstalled = true</c>.</summary>
    Task<int> CountInstalledAsync(CancellationToken ct = default);

    /// <summary>
    /// Per-platform game counts in a single query — use instead of calling
    /// <see cref="CountAsync"/> N times.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetPlatformCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Smart upsert: matches by platformId then by canonical name.
    /// Merges metadata if a match is found; adds a new row otherwise.
    /// Returns the surviving (merged or new) game.
    /// </summary>
    Task<Game> UpsertAsync(Game game, CancellationToken ct = default);

    /// <summary>Batch upsert — single DB transaction, single IMessenger LibraryRefreshed notification.</summary>
    Task<(int Processed, int NewRows, IReadOnlyList<Game> Survivors)> UpsertRangeAsync(
        IEnumerable<Game> games, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Remove every game and playtime entry (danger-zone "clear library").</summary>
    Task ClearLibraryAsync(CancellationToken ct = default);

    // ── Per-field helpers ─────────────────────────────────────────────────────

    Task SetFavoriteAsync(string id, bool value, CancellationToken ct = default);
    Task SetHiddenAsync(string id, bool value, CancellationToken ct = default);
    Task SetInstalledAsync(string id, bool value, CancellationToken ct = default);

    /// <summary>Update local cover / header paths and imgStamp after a cover download.</summary>
    Task UpdateCoverAsync(string id, string? localCoverPath, string? localHeaderPath,
        long imgStamp, CancellationToken ct = default);

    /// <summary>Update all metadata fields fetched from Steam / Wikipedia / SGDB.</summary>
    Task UpdateMetadataAsync(Game updated, CancellationToken ct = default);

    Task UpdatePlaytimeAsync(string id, int totalMinutes, DateTimeOffset lastPlayedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="additionalMinutes"/> to the stored playtime total.
    /// Use this for session-end recording where only the delta is known.
    /// </summary>
    Task AddPlaytimeAsync(string id, int additionalMinutes, DateTimeOffset lastPlayedAt,
        CancellationToken ct = default);

    Task SetCategoriesAsync(string id, IEnumerable<string> categories, CancellationToken ct = default);

    // ── Category management ───────────────────────────────────────────────────

    Task<IReadOnlyList<string>> GetAllCategoriesAsync(CancellationToken ct = default);
    Task AddCategoryAsync(string name, CancellationToken ct = default);
    Task DeleteCategoryAsync(string name, CancellationToken ct = default);
    Task RenameCategoryAsync(string oldName, string newName, CancellationToken ct = default);
}
