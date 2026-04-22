using System.Text.Json.Serialization;

namespace Cereal.App.Models;

public class Game
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("platformId")]
    public string? PlatformId { get; set; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("headerUrl")]
    public string? HeaderUrl { get; set; }

    [JsonPropertyName("localCoverPath")]
    public string? LocalCoverPath { get; set; }

    [JsonPropertyName("localHeaderPath")]
    public string? LocalHeaderPath { get; set; }

    [JsonPropertyName("_imgStamp")]
    public long? ImgStamp { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("playtimeMinutes")]
    public int? PlaytimeMinutes { get; set; }

    [JsonPropertyName("lastPlayed")]
    public string? LastPlayed { get; set; }

    [JsonPropertyName("addedAt")]
    public string? AddedAt { get; set; }

    [JsonPropertyName("favorite")]
    public bool? Favorite { get; set; }

    [JsonPropertyName("hidden")]
    public bool? Hidden { get; set; }

    [JsonPropertyName("isCustom")]
    public bool? IsCustom { get; set; }

    [JsonPropertyName("installed")]
    public bool? Installed { get; set; }

    // Custom / executable games
    [JsonPropertyName("executablePath")]
    public string? ExecutablePath { get; set; }

    // PlayStation / Chiaki
    [JsonPropertyName("chiakiNickname")]
    public string? ChiakiNickname { get; set; }

    [JsonPropertyName("chiakiHost")]
    public string? ChiakiHost { get; set; }

    [JsonPropertyName("chiakiProfile")]
    public string? ChiakiProfile { get; set; }

    [JsonPropertyName("chiakiFullscreen")]
    public bool? ChiakiFullscreen { get; set; }

    [JsonPropertyName("chiakiConsoleId")]
    public string? ChiakiConsoleId { get; set; }

    [JsonPropertyName("chiakiRegistKey")]
    public string? ChiakiRegistKey { get; set; }

    [JsonPropertyName("chiakiMorning")]
    public string? ChiakiMorning { get; set; }

    [JsonPropertyName("chiakiDisplayMode")]
    public string? ChiakiDisplayMode { get; set; }

    [JsonPropertyName("chiakiDualsense")]
    public bool? ChiakiDualsense { get; set; }

    [JsonPropertyName("chiakiPasscode")]
    public string? ChiakiPasscode { get; set; }

    [JsonPropertyName("sgdbCoverUrl")]
    public string? SgdbCoverUrl { get; set; }

    [JsonPropertyName("storeUrl")]
    public string? StoreUrl { get; set; }

    [JsonPropertyName("epicAppName")]
    public string? EpicAppName { get; set; }

    [JsonPropertyName("epicNamespace")]
    public string? EpicNamespace { get; set; }

    [JsonPropertyName("epicCatalogItemId")]
    public string? EpicCatalogItemId { get; set; }

    [JsonPropertyName("eaOfferId")]
    public string? EaOfferId { get; set; }

    [JsonPropertyName("ubisoftGameId")]
    public string? UbisoftGameId { get; set; }

    // Xbox Cloud Gaming
    [JsonPropertyName("streamUrl")]
    public string? StreamUrl { get; set; }

    // Steam-specific
    [JsonPropertyName("software")]
    public bool? Software { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    // Metadata (filled from web sources)
    [JsonPropertyName("metacritic")]
    public int? Metacritic { get; set; }

    [JsonPropertyName("developer")]
    public string? Developer { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("screenshots")]
    public List<string>? Screenshots { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }
}
