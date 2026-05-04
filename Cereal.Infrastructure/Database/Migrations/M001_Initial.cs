namespace Cereal.Infrastructure.Database.Migrations;

/// <summary>Initial schema — creates all tables for the v1 database.</summary>
public sealed class M001_Initial : IMigration
{
    public int Version => 1;

    public void Apply(IDbConnection conn, IDbTransaction tx)
    {
        conn.Execute("""
            -- ── Games ──────────────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Games (
                -- Identity
                Id                TEXT PRIMARY KEY,
                Name              TEXT NOT NULL,
                Platform          TEXT NOT NULL,
                SortName          TEXT NOT NULL DEFAULT '',
                PlatformId        TEXT,
                ExePath           TEXT,

                -- Cover art
                CoverUrl          TEXT,
                HeaderUrl         TEXT,
                LocalCoverPath    TEXT,
                LocalHeaderPath   TEXT,
                SgdbCoverUrl      TEXT,
                CoverSource       TEXT NOT NULL DEFAULT 'auto',
                ImgStamp          INTEGER,

                -- Metadata
                Description       TEXT,
                Developer         TEXT,
                Publisher         TEXT,
                ReleaseDate       TEXT,
                Metacritic        INTEGER,
                Website           TEXT,
                StoreUrl          TEXT,
                Notes             TEXT,
                Screenshots       TEXT,   -- JSON array of URLs

                -- Platform-specific IDs
                EpicAppName       TEXT,
                EpicNamespace     TEXT,
                EpicCatalogItemId TEXT,
                EaOfferId         TEXT,
                UbisoftGameId     TEXT,
                StreamUrl         TEXT,

                -- Chiaki / PlayStation
                ChiakiNickname    TEXT,
                ChiakiHost        TEXT,
                ChiakiProfile     TEXT,
                ChiakiFullscreen  INTEGER NOT NULL DEFAULT 0,
                ChiakiConsoleId   TEXT,
                ChiakiRegistKey   TEXT,
                ChiakiMorning     TEXT,
                ChiakiDisplayMode TEXT,
                ChiakiDualsense   INTEGER NOT NULL DEFAULT 0,
                ChiakiPasscode    TEXT,

                -- Flags
                IsFavorite        INTEGER NOT NULL DEFAULT 0,
                IsHidden          INTEGER NOT NULL DEFAULT 0,
                IsSoftware        INTEGER NOT NULL DEFAULT 0,
                IsCustom          INTEGER NOT NULL DEFAULT 0,
                IsInstalled       INTEGER NOT NULL DEFAULT 0,

                -- Playtime
                PlaytimeMinutes   INTEGER NOT NULL DEFAULT 0,
                LastPlayedAt      TEXT,

                -- Timestamps
                AddedAt           TEXT NOT NULL,
                UpdatedAt         TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_games_platform   ON Games(Platform);
            CREATE INDEX IF NOT EXISTS idx_games_favorite   ON Games(IsFavorite);
            CREATE INDEX IF NOT EXISTS idx_games_hidden     ON Games(IsHidden);
            CREATE INDEX IF NOT EXISTS idx_games_name       ON Games(Name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_games_platform_id ON Games(Platform, PlatformId);

            -- ── Categories ──────────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Categories (
                Name TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS GameCategories (
                GameId       TEXT NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
                CategoryName TEXT NOT NULL REFERENCES Categories(Name) ON DELETE CASCADE,
                PRIMARY KEY (GameId, CategoryName)
            );

            CREATE INDEX IF NOT EXISTS idx_gamecats_game ON GameCategories(GameId);

            -- ── Accounts (non-secret metadata only) ─────────────────────────────
            CREATE TABLE IF NOT EXISTS Accounts (
                Platform    TEXT PRIMARY KEY,
                Username    TEXT,
                AccountId   TEXT,
                DisplayName TEXT,
                AvatarUrl   TEXT,
                ExpiresAt   INTEGER,
                LastSyncMs  INTEGER,
                UpdatedAt   TEXT NOT NULL
            );

            -- ── Key-value settings store ─────────────────────────────────────────
            -- Key='settings'  → JSON-serialised Settings object
            -- Key='chiaki'    → JSON-serialised ChiakiConfig object
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key  TEXT PRIMARY KEY,
                Data TEXT NOT NULL
            );
            """, transaction: tx);
    }
}
