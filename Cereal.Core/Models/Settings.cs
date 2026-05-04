namespace Cereal.Core.Models;

/// <summary>
/// All user-configurable application settings.
/// Serialised as a single JSON blob to the AppSettings table.
/// </summary>
public sealed record Settings
{
    // ── Appearance ────────────────────────────────────────────────────────────
    public string Theme { get; set; } = "midnight";
    public string AccentColor { get; set; } = "";
    public string NavPosition { get; set; } = "top";
    public string ToolbarPosition { get; set; } = "top";
    public string UiScale { get; set; } = "100%";
    public string StarDensity { get; set; } = "normal";
    public bool ShowAnimations { get; set; } = true;

    // ── Library ───────────────────────────────────────────────────────────────
    public string DefaultView { get; set; } = "orbit";
    public string MetadataSource { get; set; } = "steam";
    public bool FilterHideSteamSoftware { get; set; } = true;
    public List<string> FilterPlatforms { get; set; } = [];
    public List<string> FilterCategories { get; set; } = [];

    // ── Behaviour ─────────────────────────────────────────────────────────────
    public bool MinimizeOnLaunch { get; set; }
    public bool AutoSyncPlaytime { get; set; }

    // ── System ────────────────────────────────────────────────────────────────
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartMinimized { get; set; }
    public bool LaunchOnStartup { get; set; }

    // ── Integrations ──────────────────────────────────────────────────────────
    public bool DiscordPresence { get; set; }
    /// <summary>SteamGridDB personal API key (never stored as a secret — low risk).</summary>
    public string? SteamGridDbKey { get; set; }
    /// <summary>Steam Web API key for playtime sync.</summary>
    public string? SteamApiKey { get; set; }

    // ── Custom paths ──────────────────────────────────────────────────────────
    public string? SteamPath { get; set; }
    public string? EpicPath { get; set; }
    public string? GogPath { get; set; }
    public string? ChiakiPath { get; set; }

    // ── Window state ──────────────────────────────────────────────────────────
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public bool RememberWindowBounds { get; set; } = true;

    // ── Internal ──────────────────────────────────────────────────────────────
    public bool FirstRun { get; set; } = true;
    public string? DefaultTab { get; set; }
}
