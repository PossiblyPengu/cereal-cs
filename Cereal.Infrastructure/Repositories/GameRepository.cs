using Cereal.Infrastructure.Database;

namespace Cereal.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="IGameRepository"/>.
/// Uses Dapper for object mapping.  Categories are loaded via a separate join query
/// and attached in memory rather than a SQL JOIN to keep the mapping simple.
/// </summary>
public sealed class GameRepository(CerealDb db, IMessenger messenger) : IGameRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = db.Open();
        var games = (await conn.QueryAsync<Game>(
            "SELECT * FROM Games ORDER BY SortName COLLATE NOCASE, Name COLLATE NOCASE")).ToList();
        await AttachCategoriesAsync(conn, games);
        return games;
    }

    public async Task<Game?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var conn = db.Open();
        var game = await conn.QuerySingleOrDefaultAsync<Game>(
            "SELECT * FROM Games WHERE Id = @id", new { id });
        if (game is not null)
            await AttachCategoriesAsync(conn, [game]);
        return game;
    }

    public async Task<Game?> FindByPlatformIdAsync(string platform, string platformId,
        CancellationToken ct = default)
    {
        using var conn = db.Open();
        var game = await conn.QuerySingleOrDefaultAsync<Game>(
            "SELECT * FROM Games WHERE Platform = @platform AND PlatformId = @platformId",
            new { platform, platformId });
        if (game is not null)
            await AttachCategoriesAsync(conn, [game]);
        return game;
    }

    public async Task<Game?> FindByCanonicalNameAsync(string platform, string canonicalName,
        CancellationToken ct = default)
    {
        // Load all games for the platform and match canonically in memory.
        // This avoids LOWER/REPLACE SQL that varies per DB; library is small.
        using var conn = db.Open();
        var rows = (await conn.QueryAsync<Game>(
            "SELECT * FROM Games WHERE Platform = @platform", new { platform })).ToList();
        var match = rows.FirstOrDefault(g =>
            !string.IsNullOrWhiteSpace(g.Name) &&
            Canonicalize(g.Name) == canonicalName);
        if (match is not null)
            await AttachCategoriesAsync(conn, [match]);
        return match;
    }

    public async Task<IReadOnlyList<Game>> SearchAsync(string query, CancellationToken ct = default)
    {
        using var conn = db.Open();
        var like = $"%{query}%";
        var games = (await conn.QueryAsync<Game>("""
            SELECT * FROM Games
            WHERE Name       LIKE @like COLLATE NOCASE
               OR Developer  LIKE @like COLLATE NOCASE
               OR Publisher  LIKE @like COLLATE NOCASE
            ORDER BY SortName COLLATE NOCASE
            """, new { like })).ToList();
        await AttachCategoriesAsync(conn, games);
        return games;
    }

    public async Task<int> CountAsync(string? platform = null, CancellationToken ct = default)
    {
        using var conn = db.Open();
        return platform is null
            ? await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM Games")
            : await conn.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM Games WHERE Platform = @platform", new { platform });
    }

    public async Task<int> CountInstalledAsync(string? platform = null, CancellationToken ct = default)
    {
        using var conn = db.Open();
        return platform is null
            ? await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM Games WHERE IsInstalled = 1")
            : await conn.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM Games WHERE IsInstalled = 1 AND Platform = @platform",
                new { platform });
    }

    public async Task<IReadOnlyDictionary<string, int>> GetPlatformCountsAsync(
        CancellationToken ct = default)
    {
        using var conn = db.Open();
        var rows = await conn.QueryAsync<(string Platform, int Count)>(
            "SELECT Platform, COUNT(*) AS Count FROM Games GROUP BY Platform");
        return rows.ToDictionary(r => r.Platform, r => r.Count);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task SaveAsync(Game game, CancellationToken ct = default)
    {
        game = PrepareForSave(game);
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        await UpsertGameAsync(conn, game, tx);
        tx.Commit();

        // Decide message type based on whether the row already existed.
        // We optimistically send GameUpdated; the first insert has no prior row so the
        // distinction between Added/Updated is handled by GameService.UpsertAsync.
        messenger.Send(new GameUpdatedMessage(game));
    }

    public async Task SaveRangeAsync(IEnumerable<Game> games, CancellationToken ct = default)
    {
        var list = games.Select(PrepareForSave).ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var g in list)
            await UpsertGameAsync(conn, g, tx);
        tx.Commit();

        foreach (var g in list)
            messenger.Send(new GameUpdatedMessage(g));

        messenger.Send(new LibraryRefreshedMessage(list.Count));
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        using var conn = db.Open();
        await conn.ExecuteAsync("DELETE FROM Games WHERE Id = @id", new { id });
        messenger.Send(new GameRemovedMessage(id));
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        using var conn = db.Open();
        using var tx   = conn.BeginTransaction();
        await conn.ExecuteAsync("DELETE FROM GameCategories",   transaction: tx);
        await conn.ExecuteAsync("DELETE FROM PlaytimeSessions", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM Games",            transaction: tx);
        tx.Commit();
    }

    public async Task SetFlagAsync(string id, string column, bool value, CancellationToken ct = default)
    {
        // Column name is an internal enum-like identifier — not from user input.
        // Validate against known flag columns to prevent SQL injection.
        if (!_flagColumns.Contains(column))
            throw new ArgumentException($"Unknown flag column: {column}", nameof(column));

        using var conn = db.Open();
        await conn.ExecuteAsync(
            $"UPDATE Games SET {column} = @value, UpdatedAt = @now WHERE Id = @id",
            new { id, value, now = DateTimeOffset.UtcNow });
    }

    private static readonly HashSet<string> _flagColumns =
    [
        "IsFavorite", "IsHidden", "IsInstalled", "IsSoftware", "IsCustom"
    ];

    public async Task UpdatePlaytimeAsync(string gameId, int additionalMinutes,
        DateTimeOffset lastPlayed, CancellationToken ct = default)
    {
        using var conn = db.Open();
        await conn.ExecuteAsync(
            """
            UPDATE Games
            SET PlaytimeMinutes = PlaytimeMinutes + @additionalMinutes,
                LastPlayedAt    = @lastPlayed,
                UpdatedAt       = @now
            WHERE Id = @gameId
            """,
            new { gameId, additionalMinutes, lastPlayed, now = DateTimeOffset.UtcNow });
    }

    public async Task SetCategoriesAsync(string gameId, IEnumerable<string> categories,
        CancellationToken ct = default)
    {
        var cats = categories.ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        // Ensure all category names exist in the Categories table.
        foreach (var cat in cats)
            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO Categories(Name) VALUES (@cat)", new { cat }, tx);

        // Replace the game's category associations.
        await conn.ExecuteAsync(
            "DELETE FROM GameCategories WHERE GameId = @gameId", new { gameId }, tx);
        foreach (var cat in cats)
            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO GameCategories(GameId, CategoryName) VALUES (@gameId, @cat)",
                new { gameId, cat }, tx);

        tx.Commit();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Game PrepareForSave(Game game)
    {
        if (string.IsNullOrEmpty(game.Id))
            game = game with { Id = Guid.NewGuid().ToString("N") };
        if (string.IsNullOrEmpty(game.SortName))
            game = game with { SortName = MakeSortName(game.Name) };
        if (game.AddedAt == default)
            game = game with { AddedAt = DateTimeOffset.UtcNow };
        game = game with { UpdatedAt = DateTimeOffset.UtcNow };
        return game;
    }

    private static string MakeSortName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        foreach (var prefix in new[] { "the ", "a ", "an " })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name[prefix.Length..].TrimStart();
        }
        return name;
    }

    private static string Canonicalize(string name) =>
        s_nonAlphanumeric.Replace(name.ToLowerInvariant(), "");

    private static readonly System.Text.RegularExpressions.Regex s_nonAlphanumeric =
        new(@"[^a-z0-9]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static async Task UpsertGameAsync(IDbConnection conn, Game g, IDbTransaction tx)
    {
        await conn.ExecuteAsync("""
            INSERT OR REPLACE INTO Games (
                Id, Name, Platform, SortName, PlatformId, ExePath,
                CoverUrl, HeaderUrl, LocalCoverPath, LocalHeaderPath,
                SgdbCoverUrl, CoverSource, ImgStamp,
                Description, Developer, Publisher, ReleaseDate,
                Metacritic, Website, StoreUrl, Notes, Screenshots,
                EpicAppName, EpicNamespace, EpicCatalogItemId,
                EaOfferId, UbisoftGameId, StreamUrl,
                ChiakiNickname, ChiakiHost, ChiakiProfile, ChiakiFullscreen,
                ChiakiConsoleId, ChiakiRegistKey, ChiakiMorning,
                ChiakiDisplayMode, ChiakiDualsense, ChiakiPasscode,
                IsFavorite, IsHidden, IsSoftware, IsCustom, IsInstalled,
                PlaytimeMinutes, LastPlayedAt, AddedAt, UpdatedAt
            ) VALUES (
                @Id, @Name, @Platform, @SortName, @PlatformId, @ExePath,
                @CoverUrl, @HeaderUrl, @LocalCoverPath, @LocalHeaderPath,
                @SgdbCoverUrl, @CoverSource, @ImgStamp,
                @Description, @Developer, @Publisher, @ReleaseDate,
                @Metacritic, @Website, @StoreUrl, @Notes, @Screenshots,
                @EpicAppName, @EpicNamespace, @EpicCatalogItemId,
                @EaOfferId, @UbisoftGameId, @StreamUrl,
                @ChiakiNickname, @ChiakiHost, @ChiakiProfile, @ChiakiFullscreen,
                @ChiakiConsoleId, @ChiakiRegistKey, @ChiakiMorning,
                @ChiakiDisplayMode, @ChiakiDualsense, @ChiakiPasscode,
                @IsFavorite, @IsHidden, @IsSoftware, @IsCustom, @IsInstalled,
                @PlaytimeMinutes, @LastPlayedAt, @AddedAt, @UpdatedAt
            )
            """, new GameParams(g), tx);
    }

    private static async Task AttachCategoriesAsync(IDbConnection conn, List<Game> games)
    {
        if (games.Count == 0) return;

        var ids = games.Select(g => g.Id).ToArray();
        var rows = await conn.QueryAsync<CategoryRow>(
            "SELECT GameId, CategoryName FROM GameCategories WHERE GameId IN @ids",
            new { ids });

        var map = rows.GroupBy(r => r.GameId)
                      .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.CategoryName).ToList());

        for (var i = 0; i < games.Count; i++)
            games[i] = games[i] with { Categories = map.GetValueOrDefault(games[i].Id) ?? [] };
    }

    // ── Dapper parameter helper ───────────────────────────────────────────────

    /// <summary>
    /// Explicit parameter mapping so Dapper sees simple types only
    /// (IReadOnlyList&lt;string&gt; is handled by the registered TypeHandler).
    /// </summary>
    private sealed class GameParams(Game g)
    {
        public string Id => g.Id;
        public string Name => g.Name;
        public string Platform => g.Platform;
        public string SortName => g.SortName;
        public string? PlatformId => g.PlatformId;
        public string? ExePath => g.ExePath;
        public string? CoverUrl => g.CoverUrl;
        public string? HeaderUrl => g.HeaderUrl;
        public string? LocalCoverPath => g.LocalCoverPath;
        public string? LocalHeaderPath => g.LocalHeaderPath;
        public string? SgdbCoverUrl => g.SgdbCoverUrl;
        public string CoverSource => g.CoverSource;
        public long? ImgStamp => g.ImgStamp;
        public string? Description => g.Description;
        public string? Developer => g.Developer;
        public string? Publisher => g.Publisher;
        public string? ReleaseDate => g.ReleaseDate;
        public int? Metacritic => g.Metacritic;
        public string? Website => g.Website;
        public string? StoreUrl => g.StoreUrl;
        public string? Notes => g.Notes;
        // TypeHandler converts IReadOnlyList<string> → JSON TEXT
        public IReadOnlyList<string> Screenshots => g.Screenshots;
        public string? EpicAppName => g.EpicAppName;
        public string? EpicNamespace => g.EpicNamespace;
        public string? EpicCatalogItemId => g.EpicCatalogItemId;
        public string? EaOfferId => g.EaOfferId;
        public string? UbisoftGameId => g.UbisoftGameId;
        public string? StreamUrl => g.StreamUrl;
        public string? ChiakiNickname => g.ChiakiNickname;
        public string? ChiakiHost => g.ChiakiHost;
        public string? ChiakiProfile => g.ChiakiProfile;
        public bool ChiakiFullscreen => g.ChiakiFullscreen;
        public string? ChiakiConsoleId => g.ChiakiConsoleId;
        public string? ChiakiRegistKey => g.ChiakiRegistKey;
        public string? ChiakiMorning => g.ChiakiMorning;
        public string? ChiakiDisplayMode => g.ChiakiDisplayMode;
        public bool ChiakiDualsense => g.ChiakiDualsense;
        public string? ChiakiPasscode => g.ChiakiPasscode;
        public bool IsFavorite => g.IsFavorite;
        public bool IsHidden => g.IsHidden;
        public bool IsSoftware => g.IsSoftware;
        public bool IsCustom => g.IsCustom;
        public bool IsInstalled => g.IsInstalled;
        public int PlaytimeMinutes => g.PlaytimeMinutes;
        // TypeHandler converts DateTimeOffset? → ISO-8601 TEXT
        public DateTimeOffset? LastPlayedAt => g.LastPlayedAt;
        public DateTimeOffset AddedAt => g.AddedAt;
        public DateTimeOffset UpdatedAt => g.UpdatedAt;
    }

    private sealed class CategoryRow
    {
        public string GameId { get; set; } = "";
        public string CategoryName { get; set; } = "";
    }
}
