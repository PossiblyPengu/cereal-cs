namespace Cereal.Core.Models;

/// <summary>
/// Represents a single game in the library.
/// All properties are settable for Dapper mapping compatibility;
/// use <c>with</c> expressions for non-destructive updates.
/// </summary>
public sealed record Game
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;

    /// <summary>Canonical sort key (strips leading "The ", "A ", etc.).</summary>
    public string SortName { get; set; } = string.Empty;

    /// <summary>Platform-specific game identifier (Steam AppID, Epic app name, etc.).</summary>
    public string? PlatformId { get; set; }

    // ── Launch ────────────────────────────────────────────────────────────────
    /// <summary>Path to executable for custom / local games.</summary>
    public string? ExePath { get; set; }

    // ── Cover art ─────────────────────────────────────────────────────────────
    public string? CoverUrl { get; set; }
    public string? HeaderUrl { get; set; }
    public string? LocalCoverPath { get; set; }
    public string? LocalHeaderPath { get; set; }
    public string? SgdbCoverUrl { get; set; }
    /// <summary>Source that provided cover art: "sgdb" | "local" | "custom" | "auto".</summary>
    public string CoverSource { get; set; } = "auto";
    /// <summary>Unix ms timestamp used to bust browser/image caches after art updates.</summary>
    public long? ImgStamp { get; set; }

    // ── Metadata ──────────────────────────────────────────────────────────────
    public string? Description { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? ReleaseDate { get; set; }
    public int? Metacritic { get; set; }
    public string? Website { get; set; }
    public string? StoreUrl { get; set; }
    public string? Notes { get; set; }
    /// <summary>Screenshot URLs (serialised as JSON in the DB column).</summary>
    public IReadOnlyList<string> Screenshots { get; set; } = [];

    // ── Platform-specific IDs ─────────────────────────────────────────────────
    public string? EpicAppName { get; set; }
    public string? EpicNamespace { get; set; }
    public string? EpicCatalogItemId { get; set; }
    public string? EaOfferId { get; set; }
    public string? UbisoftGameId { get; set; }

    // ── Xbox Cloud ────────────────────────────────────────────────────────────
    /// <summary>xCloud streaming URL opened in the embedded WebView2.</summary>
    public string? StreamUrl { get; set; }

    // ── Chiaki / PlayStation ──────────────────────────────────────────────────
    public string? ChiakiNickname { get; set; }
    public string? ChiakiHost { get; set; }
    public string? ChiakiProfile { get; set; }
    public bool ChiakiFullscreen { get; set; }
    public string? ChiakiConsoleId { get; set; }
    public string? ChiakiRegistKey { get; set; }
    public string? ChiakiMorning { get; set; }
    public string? ChiakiDisplayMode { get; set; }
    public bool ChiakiDualsense { get; set; }
    public string? ChiakiPasscode { get; set; }

    // ── Flags ─────────────────────────────────────────────────────────────────
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    /// <summary>Steam: true when the app is a non-game software title.</summary>
    public bool IsSoftware { get; set; }
    public bool IsCustom { get; set; }
    public bool IsInstalled { get; set; }

    // ── Playtime ──────────────────────────────────────────────────────────────
    public int PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }

    // ── Categories (populated by the repository join, not a DB column) ────────
    public IReadOnlyList<string> Categories { get; set; } = [];

    // ── Timestamps ────────────────────────────────────────────────────────────
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
