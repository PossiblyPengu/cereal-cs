using System.Text.Json.Serialization;

namespace Cereal.App.Models;

public class Settings
{
    [JsonPropertyName("defaultView")]
    public string DefaultView { get; set; } = "cards";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "midnight";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#7c6af7";

    [JsonPropertyName("navPosition")]
    public string NavPosition { get; set; } = "top";

    [JsonPropertyName("uiScale")]
    public string UiScale { get; set; } = "100%";

    [JsonPropertyName("starDensity")]
    public string StarDensity { get; set; } = "normal";

    [JsonPropertyName("showAnimations")]
    public bool ShowAnimations { get; set; } = true;

    [JsonPropertyName("autoSyncPlaytime")]
    public bool AutoSyncPlaytime { get; set; } = true;

    [JsonPropertyName("minimizeOnLaunch")]
    public bool MinimizeOnLaunch { get; set; } = false;

    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = false;

    [JsonPropertyName("defaultTab")]
    public string? DefaultTab { get; set; }

    [JsonPropertyName("discordPresence")]
    public bool DiscordPresence { get; set; } = true;

    [JsonPropertyName("metadataSource")]
    public string MetadataSource { get; set; } = "steamgriddb";

    [JsonPropertyName("toolbarPosition")]
    public string ToolbarPosition { get; set; } = "top";

    [JsonPropertyName("steamPath")]
    public string? SteamPath { get; set; }

    [JsonPropertyName("epicPath")]
    public string? EpicPath { get; set; }

    [JsonPropertyName("gogPath")]
    public string? GogPath { get; set; }

    [JsonPropertyName("chiakiPath")]
    public string? ChiakiPath { get; set; }

    [JsonPropertyName("firstRun")]
    public bool FirstRun { get; set; } = true;

    [JsonPropertyName("filterPlatforms")]
    public List<string> FilterPlatforms { get; set; } = [];

    [JsonPropertyName("filterCategories")]
    public List<string> FilterCategories { get; set; } = [];

    [JsonPropertyName("filterHideSteamSoftware")]
    public bool FilterHideSteamSoftware { get; set; } = true;

    [JsonPropertyName("windowX")]
    public int? WindowX { get; set; }

    [JsonPropertyName("windowY")]
    public int? WindowY { get; set; }

    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 1280;

    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 800;

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; } = false;

    [JsonPropertyName("rememberWindowBounds")]
    public bool RememberWindowBounds { get; set; } = true;
}
