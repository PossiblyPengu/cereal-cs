using System.Text.Json.Serialization;

namespace Cereal.App.Models;

public class Database
{
    // Bump whenever the on-disk shape changes; DatabaseService runs one-time
    // migrations between these versions.
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("games")]
    public List<Game> Games { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("playtime")]
    public Dictionary<string, int> Playtime { get; set; } = [];

    [JsonPropertyName("accounts")]
    public Dictionary<string, AccountInfo> Accounts { get; set; } = [];

    [JsonPropertyName("settings")]
    public Settings Settings { get; set; } = new();

    [JsonPropertyName("chiakiConfig")]
    public ChiakiConfig ChiakiConfig { get; set; } = new();
}

public class AccountInfo
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresAt")]
    public long? ExpiresAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public class ChiakiConfig
{
    [JsonPropertyName("executablePath")]
    public string? ExecutablePath { get; set; }

    [JsonPropertyName("displayMode")]
    public string? DisplayMode { get; set; }

    [JsonPropertyName("dualsense")]
    public bool? Dualsense { get; set; }

    [JsonPropertyName("consoles")]
    public List<ChiakiConsole> Consoles { get; set; } = [];
}

public class ChiakiConsole
{
    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("registKey")]
    public string? RegistKey { get; set; }

    [JsonPropertyName("morning")]
    public string? Morning { get; set; }
}

public class MediaInfo
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtUrl { get; set; }
    public bool IsPlaying { get; set; }
    public double? Position { get; set; }
    public double? Duration { get; set; }
}

public class ImportProgress
{
    public string Status { get; set; } = "running"; // running | done | error
    public string? Provider { get; set; }
    public int Processed { get; set; }
    public int Total { get; set; }
    public string? Name { get; set; }
}
