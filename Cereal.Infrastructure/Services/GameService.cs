using Cereal.Core.Providers;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Business-logic layer over <see cref="IGameRepository"/>.
/// All library mutations go through here so that IMessenger notifications
/// and the upsert/merge logic are consistently applied.
/// </summary>
public sealed class GameService(
    IGameRepository games,
    ICategoryRepository categories,
    IMessenger messenger) : IGameService
{
    // ── Read ──────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct = default) =>
        games.GetAllAsync(ct);

    public Task<Game?> GetByIdAsync(string id, CancellationToken ct = default) =>
        games.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Game>> SearchAsync(string query, CancellationToken ct = default) =>
        games.SearchAsync(query, ct);

    public Task<int> CountAsync(CancellationToken ct = default) =>
        games.CountAsync(ct: ct);

    public Task<int> CountInstalledAsync(CancellationToken ct = default) =>
        games.CountInstalledAsync(ct: ct);

    public Task<IReadOnlyDictionary<string, int>> GetPlatformCountsAsync(CancellationToken ct = default) =>
        games.GetPlatformCountsAsync(ct);

    // ── Upsert / merge ────────────────────────────────────────────────────────

    public async Task<Game> UpsertAsync(Game game, CancellationToken ct = default)
    {
        // Try match by platformId, then by canonical name.
        if (!string.IsNullOrEmpty(game.PlatformId))
        {
            var existing = await games.FindByPlatformIdAsync(game.Platform, game.PlatformId, ct);
            if (existing is not null)
            {
                var merged = MergeInto(existing, game);
                await games.SaveAsync(merged, ct);
                return merged;
            }
        }

        if (!string.IsNullOrWhiteSpace(game.Name))
        {
            var canon = Canonicalize(game.Name);
            var byName = await games.FindByCanonicalNameAsync(game.Platform, canon, ct);

            if (byName is not null)
            {
                var merged = MergeInto(byName, game);
                await games.SaveAsync(merged, ct);
                return merged;
            }
        }

        // New game.
        await games.SaveAsync(game, ct);
        messenger.Send(new GameAddedMessage(game));
        return game;
    }

    public async Task<(int Processed, int NewRows, IReadOnlyList<Game> Survivors)> UpsertRangeAsync(
        IEnumerable<Game> incoming, CancellationToken ct = default)
    {
        var list = incoming as IList<Game> ?? incoming.ToList();
        if (list.Count == 0) return (0, 0, []);

        // Load existing library once — avoids N×GetAllAsync in the hot upsert loop.
        var existing  = (await games.GetAllAsync(ct)).ToList();
        var before    = existing.Count;
        var survivors = new List<Game>(list.Count);

        // Pre-build a set of existing IDs for O(1) new-game detection later.
        var existingIdSet = existing.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        // Build lookup indexes for O(1) match inside the loop.
        // Key: "platform\0platformId"
        var byPlatformId = existing
            .Where(g => !string.IsNullOrEmpty(g.PlatformId))
            .GroupBy(g => $"{g.Platform}\0{g.PlatformId}")
            .ToDictionary(grp => grp.Key, grp => grp.First());

        // Key: "platform\0canonicalName"
        var byName = existing
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .GroupBy(g => $"{g.Platform}\0{Canonicalize(g.Name)}")
            .ToDictionary(grp => grp.Key, grp => grp.First());

        foreach (var g in list)
        {
            ct.ThrowIfCancellationRequested();
            Game? match = null;

            if (!string.IsNullOrEmpty(g.PlatformId))
                byPlatformId.TryGetValue($"{g.Platform}\0{g.PlatformId}", out match);

            if (match is null && !string.IsNullOrWhiteSpace(g.Name))
                byName.TryGetValue($"{g.Platform}\0{Canonicalize(g.Name)}", out match);

            if (match is not null)
            {
                var merged = MergeInto(match, g);
                // Keep the index up-to-date for subsequent iterations.
                byPlatformId[$"{merged.Platform}\0{merged.PlatformId}"] = merged;
                if (!string.IsNullOrWhiteSpace(merged.Name))
                    byName[$"{merged.Platform}\0{Canonicalize(merged.Name)}"] = merged;
                survivors.Add(merged);
            }
            else
            {
                var newGame = string.IsNullOrEmpty(g.Id)
                    ? g with { Id = Guid.NewGuid().ToString("N") }
                    : g;
                if (!string.IsNullOrEmpty(newGame.PlatformId))
                    byPlatformId[$"{newGame.Platform}\0{newGame.PlatformId}"] = newGame;
                if (!string.IsNullOrWhiteSpace(newGame.Name))
                    byName[$"{newGame.Platform}\0{Canonicalize(newGame.Name)}"] = newGame;
                survivors.Add(newGame);
            }
        }

        // Persist all survivors in a single transaction via SaveRangeAsync.
        await games.SaveRangeAsync(survivors, ct);

        var after = before + survivors.Count(s => !existingIdSet.Contains(s.Id));
        messenger.Send(new LibraryRefreshedMessage(after));
        return (list.Count, after - before, survivors);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        games.DeleteAsync(id, ct);

    public async Task ClearLibraryAsync(CancellationToken ct = default)
    {
        await games.DeleteAllAsync(ct);
        messenger.Send(new LibraryRefreshedMessage(0));
    }

    // ── Flag helpers ──────────────────────────────────────────────────────────

    public async Task SetFavoriteAsync(string id, bool value, CancellationToken ct = default)
    {
        await games.SetFlagAsync(id, "IsFavorite", value, ct);
        await NotifyUpdatedAsync(id, ct);
    }

    public async Task SetHiddenAsync(string id, bool value, CancellationToken ct = default)
    {
        await games.SetFlagAsync(id, "IsHidden", value, ct);
        await NotifyUpdatedAsync(id, ct);
    }

    public async Task SetInstalledAsync(string id, bool value, CancellationToken ct = default)
    {
        await games.SetFlagAsync(id, "IsInstalled", value, ct);
        await NotifyUpdatedAsync(id, ct);
    }

    public async Task UpdateCoverAsync(string id, string? localCoverPath, string? localHeaderPath,
        long imgStamp, CancellationToken ct = default)
    {
        var g = await GetOrThrowAsync(id, ct);
        // null means "keep the existing value" — allows callers to update only one path.
        var cover  = localCoverPath  ?? g.LocalCoverPath;
        var header = localHeaderPath ?? g.LocalHeaderPath;
        await games.SaveAsync(g with
        {
            LocalCoverPath  = cover,
            LocalHeaderPath = header,
            ImgStamp        = imgStamp,
        }, ct);
        messenger.Send(new GameCoverUpdatedMessage(id, cover, header));
    }

    public async Task UpdateMetadataAsync(Game updated, CancellationToken ct = default)
    {
        await games.SaveAsync(updated with { UpdatedAt = DateTimeOffset.UtcNow }, ct);
    }

    public async Task UpdatePlaytimeAsync(string id, int totalMinutes, DateTimeOffset lastPlayedAt,
        CancellationToken ct = default)
    {
        var g = await GetOrThrowAsync(id, ct);
        await games.SaveAsync(g with
        {
            PlaytimeMinutes = totalMinutes,
            LastPlayedAt = lastPlayedAt,
        }, ct);
    }

    public async Task AddPlaytimeAsync(string id, int additionalMinutes, DateTimeOffset lastPlayedAt,
        CancellationToken ct = default)
    {
        await games.UpdatePlaytimeAsync(id, additionalMinutes, lastPlayedAt, ct);
        await NotifyUpdatedAsync(id, ct);
    }

    public Task SetCategoriesAsync(string id, IEnumerable<string> cats, CancellationToken ct = default) =>
        games.SetCategoriesAsync(id, cats, ct);

    // ── Category management ───────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetAllCategoriesAsync(CancellationToken ct = default) =>
        categories.GetAllAsync(ct);

    public Task AddCategoryAsync(string name, CancellationToken ct = default) =>
        categories.EnsureExistsAsync(name, ct);

    public Task DeleteCategoryAsync(string name, CancellationToken ct = default) =>
        categories.DeleteAsync(name, ct);

    public Task RenameCategoryAsync(string oldName, string newName, CancellationToken ct = default) =>
        categories.RenameAsync(oldName, newName, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Game> GetOrThrowAsync(string id, CancellationToken ct)
    {
        var g = await games.GetByIdAsync(id, ct);
        return g ?? throw new KeyNotFoundException($"Game not found: {id}");
    }

    private async Task NotifyUpdatedAsync(string id, CancellationToken ct)
    {
        var g = await games.GetByIdAsync(id, ct);
        if (g is not null) messenger.Send(new GameUpdatedMessage(g));
    }

    private static string Canonicalize(string name) =>
        s_nonAlphanumeric.Replace(name.ToLowerInvariant(), "");

    private static readonly System.Text.RegularExpressions.Regex s_nonAlphanumeric =
        new(@"[^a-z0-9]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static Game MergeInto(Game target, Game incoming)
    {
        // Name always takes the incoming value so provider updates win.
        return target with
        {
            Name        = incoming.Name,
            PlatformId  = incoming.PlatformId ?? target.PlatformId,
            CoverUrl    = incoming.CoverUrl ?? target.CoverUrl,
            HeaderUrl   = incoming.HeaderUrl ?? target.HeaderUrl,
            LocalCoverPath  = incoming.LocalCoverPath ?? target.LocalCoverPath,
            LocalHeaderPath = incoming.LocalHeaderPath ?? target.LocalHeaderPath,
            StoreUrl    = incoming.StoreUrl ?? target.StoreUrl,
            StreamUrl   = incoming.StreamUrl ?? target.StreamUrl,
            ExePath     = incoming.ExePath ?? target.ExePath,
            IsInstalled = incoming.IsInstalled || target.IsInstalled,
            PlaytimeMinutes = Math.Max(incoming.PlaytimeMinutes, target.PlaytimeMinutes),
            EpicAppName = incoming.EpicAppName ?? target.EpicAppName,
            EpicNamespace = incoming.EpicNamespace ?? target.EpicNamespace,
            EpicCatalogItemId = incoming.EpicCatalogItemId ?? target.EpicCatalogItemId,
            // Preserve existing metadata if incoming has none.
            Description = incoming.Description ?? target.Description,
            Developer   = incoming.Developer ?? target.Developer,
            Publisher   = incoming.Publisher ?? target.Publisher,
            ReleaseDate = incoming.ReleaseDate ?? target.ReleaseDate,
            Website     = incoming.Website ?? target.Website,
            Categories  = MergeCategories(target.Categories, incoming.Categories),
        };
    }

    private static IReadOnlyList<string> MergeCategories(
        IReadOnlyList<string> existing, IReadOnlyList<string> incoming)
    {
        if (incoming.Count == 0) return existing;
        var result = new List<string>(existing);
        foreach (var c in incoming)
        {
            if (!result.Contains(c, StringComparer.OrdinalIgnoreCase))
                result.Add(c);
        }
        return result;
    }
}
