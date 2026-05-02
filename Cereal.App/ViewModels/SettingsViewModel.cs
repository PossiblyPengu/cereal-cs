using System.Management;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Metadata;
using Cereal.App.Services.Providers;
using Cereal.App.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.ViewModels;

public sealed record PlatformStatRow(string Label, string Color, int Count, double BarRatio);

public sealed record MostPlayedRow(int Rank, string Name, string Time);

public partial class SettingsViewModel : ObservableObject
{
    private sealed class LibraryExportBundle
    {
        public List<Game> Games { get; set; } = [];
        public List<string> Categories { get; set; } = [];
        public string ExportedAt { get; set; } = DateTime.UtcNow.ToString("O");
    }

    public static readonly string[] ViewOptions      = ["cards", "orbit"];
    public static readonly string[] ViewOptionLabels = ["Card Grid", "Galaxy Orbit"];
    public static readonly string[] NavPositions     = ["top", "bottom", "left", "right"];
    public static readonly string[] NavPositionLabels= ["Top", "Bottom", "Left", "Right"];
    public static readonly string[] StarDensities    = ["low", "normal", "high"];
    public static readonly string[] StarDensityLabels= ["Low", "Normal", "High"];
    public static readonly string[] UiScaleOptions   = ["90%", "100%", "110%", "125%"];
    public static readonly AppTheme[] ThemeOptions = AppThemes.All;

    private readonly SettingsService _settingsSvc;
    private readonly DiscordService _discord;
    private readonly CoverService _covers;
    private readonly CredentialService _creds;
    private readonly ThemeService _themeSvc;
    private readonly GameService _games;
    private readonly DatabaseService _db;
    private readonly UpdateService _updateSvc;
    private readonly IEnumerable<IProvider> _providers;

    // ─── ComboBox index helpers (maps raw values ↔ display labels) ──────────

    public int DefaultViewIndex
    {
        get => Math.Max(0, Array.IndexOf(ViewOptions, DefaultView));
        set { if (value >= 0 && value < ViewOptions.Length) DefaultView = ViewOptions[value]; }
    }
    public int NavPositionIndex
    {
        get => Math.Max(0, Array.IndexOf(NavPositions, NavPosition));
        set { if (value >= 0 && value < NavPositions.Length) NavPosition = NavPositions[value]; }
    }
    public int StarDensityIndex
    {
        get => Math.Max(0, Array.IndexOf(StarDensities, StarDensity));
        set { if (value >= 0 && value < StarDensities.Length) StarDensity = StarDensities[value]; }
    }

    // ─── Settings properties ─────────────────────────────────────────────────

    [ObservableProperty] private string _defaultView = "orbit";
    [ObservableProperty] private string _theme = "midnight";
    [ObservableProperty] private string _accentColor = "";
    [ObservableProperty] private bool _showAnimations = true;
    [ObservableProperty] private bool _minimizeOnLaunch;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _launchOnStartup;
    [ObservableProperty] private bool _discordPresence;
    [ObservableProperty] private bool _autoSyncPlaytime;
    [ObservableProperty] private bool _rememberWindowBounds = true;
    [ObservableProperty] private bool _filterHideSteamSoftware = true;
    [ObservableProperty] private string? _steamPath;
    [ObservableProperty] private string? _epicPath;
    [ObservableProperty] private string? _gogPath;
    [ObservableProperty] private string? _chiakiPath;
    [ObservableProperty] private string _metadataSource = "steam";

    /// <summary>Items for the metadata ComboBox (must match <see cref="MetadataService"/>).</summary>
    public IReadOnlyList<string> MetadataSourceOptions { get; } = ["steam", "wikipedia", "igdb"];

    private static string NormalizeMetadataSource(string? v) =>
        v?.ToLowerInvariant() switch
        {
            "wikipedia" => "wikipedia",
            "igdb"      => "igdb",
            _           => "steam",
        };
    private static string NormalizeUiScale(string? raw)
    {
        var scale = Cereal.App.MainWindow.ParseUiScale(raw);
        var pct = (int)Math.Round(scale * 100.0, MidpointRounding.AwayFromZero);
        return $"{pct}%";
    }
    [ObservableProperty] private string? _igdbClientId;
    [ObservableProperty] private string? _igdbClientSecret;
    [ObservableProperty] private bool _hasIgdbClientId;
    [ObservableProperty] private bool _hasIgdbClientSecret;

    [ObservableProperty] private string? _steamGridDbKey;
    [ObservableProperty] private bool _hasSteamGridDbSecret;
    [ObservableProperty] private bool _steamGridDbKeyInvalid;
    [ObservableProperty] private bool _steamGridDbShowStatusBusy;
    [ObservableProperty] private bool _steamGridDbShowStatusErr;
    [ObservableProperty] private bool _steamGridDbShowStatusOk;
    [ObservableProperty] private bool _steamGridDbShowStatusMissing;
    [ObservableProperty] private string _steamGridDbStatusOkText = "Key saved";
    [ObservableProperty] private string _navPosition = "top";
    [ObservableProperty] private string _starDensity = "normal";
    [ObservableProperty] private string _uiScale = "100%";

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasPendingChanges;
    [ObservableProperty] private string _saveHint = "Autosave is active for most toggles.";
    [ObservableProperty] private bool _steamGridDbKeyValid;
    [ObservableProperty] private bool _isValidatingKey;
    [ObservableProperty] private bool _accentColorValid = true;
    [ObservableProperty] private string _accentColorValidationMessage = string.Empty;

    // ─── Section navigation ───────────────────────────────────────────────────

    [ObservableProperty] private string _activeSection = "appearance";

    /// <summary>Page title for the active settings section (bound in SettingsPanel header).</summary>
    public string SectionTitle => ActiveSection switch
    {
        "appearance" => "Appearance & layout",
        "library" => "Library & integrations",
        "behavior" => "Behavior & runtime",
        "system" => "System & updates",
        "about" => "About & diagnostics",
        "danger" => "Danger zone",
        _ => "Settings",
    };

    /// <summary>One-line description under the page title.</summary>
    public string SectionSubtitle => ActiveSection switch
    {
        "appearance" => "Themes, accent, navigation, and Orbit options.",
        "library" => "Backups, metadata, platform actions, and API keys.",
        "behavior" => "Startup, tray, sync, and runtime integrations.",
        "system" => "Platform paths, metadata, and updates.",
        "about" => "Library stats, platforms, and system information.",
        "danger" => "Destructive actions — signing out and clearing data cannot be undone.",
        _ => string.Empty,
    };

    /// <summary>True when the Danger zone section is open — used for header chrome.</summary>
    public bool IsDangerSection => ActiveSection == "danger";

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionSubtitle));
        OnPropertyChanged(nameof(IsDangerSection));
    }

    [RelayCommand]
    private void SetSection(string section)
    {
        ActiveSection = section;
        if (section == "about") RefreshLibraryStats();
    }

    public bool IsSection(string s) => ActiveSection == s;

    // ─── Discord status ───────────────────────────────────────────────────────

    public Avalonia.Media.IBrush DiscordStatusColor =>
        _discord.IsConnected && _discord.IsReady
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22c55e"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#50b0aaa0"));

    public string DiscordStatusLabel =>
        _discord.IsConnected && _discord.IsReady ? "Connected" : "Not connected";

    partial void OnDiscordPresenceChanged(bool value)
    {
        OnPropertyChanged(nameof(DiscordStatusColor));
        OnPropertyChanged(nameof(DiscordStatusLabel));
        AutoSave();
    }

    // Electron's SettingsPanel writes every toggle flip straight to settings via
    // `saveSettings({ [key]: val })` (src/components/SettingsPanel.tsx 118-127).
    // These partials mirror that auto-save behaviour.
    partial void OnShowAnimationsChanged(bool value)
    {
        AutoSave();
        RebuildOrbitIfLoaded();
    }
    partial void OnMinimizeOnLaunchChanged(bool value) => AutoSave();
    partial void OnCloseToTrayChanged(bool value) => AutoSave();
    partial void OnMinimizeToTrayChanged(bool value) => AutoSave();
    partial void OnStartMinimizedChanged(bool value) => AutoSave();
    partial void OnLaunchOnStartupChanged(bool value)
    {
        StartupService.ApplyLaunchOnStartup(value);
        AutoSave();
    }
    partial void OnAutoSyncPlaytimeChanged(bool value) => AutoSave();
    partial void OnRememberWindowBoundsChanged(bool value) => AutoSave();
    partial void OnFilterHideSteamSoftwareChanged(bool value)
    {
        AutoSave();
        // Re-run the library filter on the main view so the toggle takes effect live.
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow?.DataContext is MainViewModel mvm)
        {
            mvm.Refresh();
        }
    }
    partial void OnStarDensityChanged(string value)
    {
        OnPropertyChanged(nameof(StarDensityIndex));
        AutoSave();
        RebuildOrbitIfLoaded();
    }

    // Tells the already-loaded OrbitView (if any) to re-scatter stars/orbs so
    // the new density/animations settings are immediately visible.
    private static void RebuildOrbitIfLoaded()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow is MainWindow mw)
        {
            var orbit = FindDescendant<Views.OrbitView>(mw);
            orbit?.Rebuild();
        }
    }
    partial void OnMetadataSourceChanged(string value) => AutoSave();
    partial void OnSteamPathChanged(string? value) => MarkPendingChange();
    partial void OnEpicPathChanged(string? value) => MarkPendingChange();
    partial void OnGogPathChanged(string? value) => MarkPendingChange();
    partial void OnChiakiPathChanged(string? value) => MarkPendingChange();

    // Silent, partial save used by the OnX partials above. Swallows exceptions
    // to avoid crashing the UI if the user is typing into one of the path
    // TextBoxes while we race to persist.
    private bool _loadingFromModel;
    private void MarkPendingChange()
    {
        if (_loadingFromModel) return;
        HasPendingChanges = true;
        SaveHint = "You have unsaved changes.";
    }

    private void ClearPendingChanges(string message)
    {
        HasPendingChanges = false;
        SaveHint = message;
    }

    private void AutoSave()
    {
        if (_loadingFromModel) return;
        try
        {
            var s = _settingsSvc.Get();
            s.DefaultView = DefaultView;
            s.Theme = Theme;
            s.AccentColor = AccentColor;
            s.ShowAnimations = ShowAnimations;
            s.MinimizeOnLaunch = MinimizeOnLaunch;
            s.CloseToTray = CloseToTray;
            s.MinimizeToTray = MinimizeToTray;
            s.StartMinimized = StartMinimized;
            s.LaunchOnStartup = LaunchOnStartup;
            s.DiscordPresence = DiscordPresence;
            s.AutoSyncPlaytime = AutoSyncPlaytime;
            s.RememberWindowBounds = RememberWindowBounds;
            s.FilterHideSteamSoftware = FilterHideSteamSoftware;
            s.MetadataSource = NormalizeMetadataSource(MetadataSource);
            s.NavPosition = NavPosition;
            s.ToolbarPosition = NavPosition;
            s.StarDensity = StarDensity;
            s.UiScale = UiScale;
            _settingsSvc.Save(s);

            if (DiscordPresence && !_discord.IsConnected) _discord.Connect();
            else if (!DiscordPresence && _discord.IsConnected) _discord.Disconnect();
            if (!HasPendingChanges)
                SaveHint = "Autosaved.";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[settings] AutoSave failed");
        }
    }

    // ─── chiaki-ng update state ───────────────────────────────────────────────

    [ObservableProperty] private string? _chiakiVersion;
    [ObservableProperty] private bool _chiakiUpdateAvailable;
    [ObservableProperty] private string? _chiakiUpdateVersion;
    [ObservableProperty] private bool _chiakiCanUninstall;

    private void LoadChiakiStatus()
    {
        var chiaki = App.Services.GetService(typeof(ChiakiService)) as ChiakiService;
        if (chiaki is null) return;
        var (status, exe, version) = chiaki.GetStatus();
        // For system/config installs there's no .version file — use a placeholder
        // so the UI knows chiaki-ng is present (not null = installed).
        ChiakiVersion = status == "missing" ? null : (version ?? "installed");
        // Show Uninstall whenever the bundled dir exists or a custom path is configured,
        // regardless of whether detection succeeded.
        ChiakiCanUninstall = chiaki.GetChiakiDir() is not null
            || !string.IsNullOrEmpty(ChiakiPath);
    }

    private static readonly HttpClient _http = CreateHttpClient();
    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.Add("User-Agent", "cereal-cs");
        c.Timeout = TimeSpan.FromSeconds(10);
        return c;
    }

    [RelayCommand]
    private async Task CheckChiakiUpdate()
    {
        StatusMessage = "Checking chiaki-ng…";
        try
        {
            var json = await _http.GetStringAsync(
                "https://api.github.com/repos/streetpea/chiaki-ng/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latestStr = tag.TrimStart('v');

            ChiakiUpdateVersion = latestStr;

            var hasInstalled = Version.TryParse(ChiakiVersion, out var installed);
            var hasLatest    = Version.TryParse(latestStr, out var latest);

            if (hasInstalled && hasLatest && latest > installed)
            {
                ChiakiUpdateAvailable = true;
                StatusMessage = $"chiaki-ng {latestStr} is available.";
            }
            else if (!hasInstalled && hasLatest)
            {
                // system install or unknown version — show latest but can't compare
                ChiakiUpdateAvailable = true;
                StatusMessage = $"chiaki-ng {latestStr} available (installed version unknown).";
            }
            else
            {
                ChiakiUpdateAvailable = false;
                StatusMessage = "chiaki-ng is up to date.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update check failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DownloadChiakiUpdate()
    {
        StatusMessage = "Opening chiaki-ng releases page…";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://github.com/streetpea/chiaki-ng/releases/latest") { UseShellExecute = true });
    }

    // ─── System specs ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _specsCpu = "—";
    [ObservableProperty] private string _specsGpu = "—";
    [ObservableProperty] private string _specsRam = "—";
    [ObservableProperty] private string _specsOs  = "—";
    [ObservableProperty] private string _specsTier = "Balanced";
    [ObservableProperty] private string _specsRecommendation = "Balanced defaults recommended.";
    [ObservableProperty] private string _recommendedStarDensity = "normal";
    [ObservableProperty] private string _recommendedUiScale = "100%";
    [ObservableProperty] private bool _recommendedShowAnimations = true;
    public string RecommendedProfileLabel =>
        $"{RecommendedStarDensity} stars · {RecommendedUiScale} UI · animations {(RecommendedShowAnimations ? "on" : "off")}";

    private void LoadSystemSpecs()
    {
        HardwareSnapshot snapshot;
        PerformanceRecommendation rec;
        try
        {
            snapshot = PerformanceAdvisor.Detect();
            rec = PerformanceAdvisor.Recommend(snapshot);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[settings] Hardware detection failed");
            snapshot = new HardwareSnapshot("—", "—", System.Runtime.InteropServices.RuntimeInformation.OSDescription, 0, Math.Max(1, Environment.ProcessorCount));
            rec = new PerformanceRecommendation("Balanced", "Balanced defaults recommended.", "normal", "100%", true);
        }

        var cpu = snapshot.Cpu;
        if (snapshot.LogicalCores > 1 && cpu != "—")
            cpu += $" · {snapshot.LogicalCores} threads";

        Dispatcher.UIThread.Post(() =>
        {
            SpecsOs  = snapshot.Os;
            SpecsRam = PerformanceAdvisor.FormatRam(snapshot.TotalMemoryBytes);
            SpecsCpu = cpu;
            SpecsGpu = snapshot.Gpu;
            SpecsTier = rec.Tier;
            SpecsRecommendation = rec.Summary;
            RecommendedStarDensity = rec.StarDensity;
            RecommendedUiScale = rec.UiScale;
            RecommendedShowAnimations = rec.ShowAnimations;
        });
    }

    [RelayCommand]
    private void ApplyRecommendedPerformance()
    {
        StarDensity = RecommendedStarDensity;
        UiScale = RecommendedUiScale;
        ShowAnimations = RecommendedShowAnimations;
        SaveHint = $"Applied {SpecsTier.ToLowerInvariant()} profile.";
    }

    partial void OnRecommendedStarDensityChanged(string value) => OnPropertyChanged(nameof(RecommendedProfileLabel));
    partial void OnRecommendedUiScaleChanged(string value) => OnPropertyChanged(nameof(RecommendedProfileLabel));
    partial void OnRecommendedShowAnimationsChanged(bool value) => OnPropertyChanged(nameof(RecommendedProfileLabel));

    // ─── Update state ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _updateState = "idle"; // idle|checking|available|downloading|ready|error
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private int _updateProgress;
    public bool UpdateAvailable  => UpdateState == "available";
    public bool UpdateDownloading => UpdateState == "downloading";
    public bool UpdateReady      => UpdateState == "ready";

    partial void OnUpdateStateChanged(string value)
    {
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateDownloading));
        OnPropertyChanged(nameof(UpdateReady));
    }

    // ─── Danger zone confirm state ────────────────────────────────────────────

    [ObservableProperty] private bool _confirmingClearGames;
    [ObservableProperty] private bool _confirmingClearCovers;

    // ─── Library stats ────────────────────────────────────────────────────────

    public int GameCount => _games.GetAll().Count;
    public int InstalledCount => _games.GetAll().Count(g => g.Installed != false);
    public int FavoriteCount => _games.GetAll().Count(g => g.Favorite == true);

    private static string FormatMinutes(int mins)
    {
        if (mins <= 0) return "—";
        if (mins < 60) return $"{mins} min";
        var h = mins / 60;
        var r = mins % 60;
        return r == 0 ? $"{h}h" : $"{h}h {r}m";
    }

    public string TotalPlaytimeLabel =>
        FormatMinutes(_games.GetAll().Sum(g => g.PlaytimeMinutes ?? 0));

    // Per-platform game counts for the About → library breakdown card.
    public List<PlatformStatRow> PlatformBreakdown
    {
        get
        {
            var groups = _games.GetAll()
                .GroupBy(g => string.IsNullOrWhiteSpace(g.Platform) ? "other" : g.Platform!)
                .OrderByDescending(g => g.Count())
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToList();
            var max = Math.Max(groups.Count == 0 ? 1 : groups.Max(x => x.Count), 1);
            return groups.Select(g => new PlatformStatRow(
                Cereal.App.Utilities.PlatformInfo.GetLabel(g.Key),
                Cereal.App.Utilities.PlatformInfo.GetColor(g.Key),
                g.Count,
                (double)g.Count / max)).ToList();
        }
    }

    public List<MostPlayedRow> MostPlayed =>
        _games.GetAll()
            .Where(g => (g.PlaytimeMinutes ?? 0) > 0)
            .OrderByDescending(g => g.PlaytimeMinutes ?? 0)
            .Take(3)
            .Select((g, i) => new MostPlayedRow(i + 1, g.Name ?? "(unnamed)",
                FormatMinutes(g.PlaytimeMinutes ?? 0)))
            .ToList();

    public bool HasMostPlayed => MostPlayed.Count > 0;
    public bool HasPlatformBreakdown => PlatformBreakdown.Count > 0;

    public string MostPlayedLabel
    {
        get
        {
            var top = MostPlayed.FirstOrDefault();
            return top is null ? "No playtime recorded yet." : $"{top.Name} · {top.Time}";
        }
    }

    public void RefreshLibraryStats()
    {
        OnPropertyChanged(nameof(GameCount));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(TotalPlaytimeLabel));
        OnPropertyChanged(nameof(PlatformBreakdown));
        OnPropertyChanged(nameof(HasPlatformBreakdown));
        OnPropertyChanged(nameof(MostPlayed));
        OnPropertyChanged(nameof(HasMostPlayed));
        OnPropertyChanged(nameof(MostPlayedLabel));
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public SettingsViewModel(SettingsService settings, DiscordService discord,
                             CoverService covers, CredentialService creds, ThemeService theme,
                             DatabaseService db,
                             GameService games, UpdateService updateSvc, IEnumerable<IProvider> providers)
    {
        _settingsSvc = settings;
        _discord = discord;
        _covers = covers;
        _creds = creds;
        _themeSvc = theme;
        _db = db;
        _games = games;
        _updateSvc = updateSvc;
        _providers = providers;
        LoadFromModel(settings.Get());

        updateSvc.UpdateAvailable += (_, args) => Dispatcher.UIThread.Post(() =>
        {
            UpdateVersion = args.NewVersion;
            UpdateState = "available";
        });
        updateSvc.UpdateReady += (_, _) => Dispatcher.UIThread.Post(() =>
            UpdateState = "ready");

        LoadChiakiStatus();
        _ = Task.Run(LoadSystemSpecs);
        SyncSteamGridDbSecret();
        RefreshSteamGridDbUi();
        RefreshIgdbUi();
    }

    public string SteamGridDbKeyWatermark =>
        HasSteamGridDbSecret && string.IsNullOrWhiteSpace(SteamGridDbKey)
            ? "Saved — paste to replace"
            : "Paste API key here";
    public string SteamGridDbInlineHint =>
        IsValidatingKey ? "Validating key..."
        : SteamGridDbKeyInvalid ? "Key is invalid."
        : SteamGridDbKeyValid ? "Key is valid."
        : HasSteamGridDbSecret ? "A key is saved. Paste to replace."
        : "Enter your SteamGridDB API key.";

    private void SyncSteamGridDbSecret() =>
        HasSteamGridDbSecret = !string.IsNullOrEmpty(
            _creds.GetPassword("cereal", "steamgriddb_key"));

    private void RefreshIgdbUi()
    {
        HasIgdbClientId     = !string.IsNullOrEmpty(_creds.GetPassword("cereal", "igdb_client_id"));
        HasIgdbClientSecret = !string.IsNullOrEmpty(_creds.GetPassword("cereal", "igdb_client_secret"));
    }

    [RelayCommand]
    private void SaveIgdbKeys()
    {
        if (string.IsNullOrWhiteSpace(IgdbClientId) || string.IsNullOrWhiteSpace(IgdbClientSecret))
        {
            StatusMessage = "Enter both Client ID and Client Secret from the Twitch developer console.";
            return;
        }
        _creds.SetPassword("cereal", "igdb_client_id", IgdbClientId!.Trim());
        _creds.SetPassword("cereal", "igdb_client_secret", IgdbClientSecret!.Trim());
        IgdbClientId = null;
        IgdbClientSecret = null;
        RefreshIgdbUi();
        StatusMessage = "IGDB (Twitch) credentials saved.";
    }

    [RelayCommand]
    private void DeleteIgdbKeys()
    {
        _creds.DeletePassword("cereal", "igdb_client_id");
        _creds.DeletePassword("cereal", "igdb_client_secret");
        IgdbClientId = null;
        IgdbClientSecret = null;
        RefreshIgdbUi();
        StatusMessage = "IGDB credentials removed.";
    }

    [RelayCommand]
    private void OpenIgdbDevPortal()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://dev.twitch.tv/console/apps")
            { UseShellExecute = true });
        }
        catch (Exception ex) { StatusMessage = "Couldn't open browser: " + ex.Message; }
    }

    private void RefreshSteamGridDbUi()
    {
        SteamGridDbShowStatusBusy = IsValidatingKey;
        SteamGridDbShowStatusErr = !IsValidatingKey && SteamGridDbKeyInvalid;
        var okValid = !IsValidatingKey && !SteamGridDbKeyInvalid && SteamGridDbKeyValid
                      && !string.IsNullOrWhiteSpace(SteamGridDbKey);
        var okSaved = !IsValidatingKey && !SteamGridDbKeyInvalid && HasSteamGridDbSecret
                      && string.IsNullOrWhiteSpace(SteamGridDbKey);
        SteamGridDbShowStatusOk = okValid || okSaved;
        SteamGridDbStatusOkText = okSaved ? "Key saved" : "Valid";
        SteamGridDbShowStatusMissing = !IsValidatingKey && !SteamGridDbKeyInvalid
            && !HasSteamGridDbSecret && string.IsNullOrWhiteSpace(SteamGridDbKey) && !okValid;
        OnPropertyChanged(nameof(SteamGridDbKeyWatermark));
        OnPropertyChanged(nameof(SteamGridDbInlineHint));
    }

    partial void OnIsValidatingKeyChanged(bool value) => RefreshSteamGridDbUi();
    partial void OnHasSteamGridDbSecretChanged(bool value) => RefreshSteamGridDbUi();
    partial void OnSteamGridDbKeyInvalidChanged(bool value) => RefreshSteamGridDbUi();
    partial void OnSteamGridDbKeyValidChanged(bool value) => RefreshSteamGridDbUi();

    partial void OnSteamGridDbKeyChanged(string? value)
    {
        SteamGridDbKeyInvalid = false;
        RefreshSteamGridDbUi();
    }

    public AppTheme? SelectedTheme
    {
        get => AppThemes.Find(Theme);
        set { if (value is not null) Theme = value.Id; }
    }

    public IEnumerable<ThemeSwatchViewModel> ThemeSwatches =>
        AppThemes.All.Select(t => new ThemeSwatchViewModel(t, Theme));

    /// <summary>Watermark hint for the accent field — shows the active theme's default accent so the user knows what “leave blank” means.</summary>
    public string ThemeAccentWatermark =>
        AppThemes.Find(Theme)?.Accent ?? "#d4a853";

    partial void OnThemeChanged(string value)
    {
        _themeSvc.Apply(value, AccentColor);
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(ThemeSwatches));
        OnPropertyChanged(nameof(ThemeAccentWatermark));
    }

    partial void OnAccentColorChanged(string value)
    {
        var isBlank = string.IsNullOrWhiteSpace(value);
        var isValid = isBlank || Color.TryParse(value, out _);
        AccentColorValid = isValid;
        AccentColorValidationMessage = isValid ? string.Empty : "Use a valid hex color like #d4a853.";
        if (isValid)
        {
            _themeSvc.Apply(Theme, value);
            MarkPendingChange();
        }
    }

    // Live UI-scale application (mirror Electron's applyUiScale() in utils.ts 9-12 —
    // zoom applies immediately as the dropdown changes, no Save button required).
    partial void OnUiScaleChanged(string value)
    {
        value = NormalizeUiScale(value);
        if (!string.Equals(UiScale, value, StringComparison.Ordinal))
        {
            UiScale = value;
            return;
        }
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
                d.MainWindow is MainWindow mw)
            {
                mw.ApplyUiScale(value);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[settings] Live ApplyUiScale failed");
        }
        MarkPendingChange();
    }

    // Live default-view update so changing the dropdown previews the new view.
    partial void OnDefaultViewChanged(string value)
    {
        OnPropertyChanged(nameof(DefaultViewIndex));
        if (_loadingFromModel) return;
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow?.DataContext is MainViewModel mvm)
        {
            mvm.ViewMode = MainViewModel.NormalizeViewMode(value);
        }
        MarkPendingChange();
    }

    // Live nav-position: floating toolbar immediately snaps to top/bottom
    // so the user can see the change without hunting for the Save button.
    partial void OnNavPositionChanged(string value)
    {
        OnPropertyChanged(nameof(NavPositionIndex));
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow?.DataContext is MainViewModel mvm &&
            !string.IsNullOrEmpty(value))
        {
            mvm.ToolbarPosition = value;
        }
        MarkPendingChange();
    }

    private void LoadFromModel(Settings s)
    {
        _loadingFromModel = true;
        try
        {
        DefaultView = MainViewModel.NormalizeViewMode(s.DefaultView);
        Theme = s.Theme;
        AccentColor = s.AccentColor;
        ShowAnimations = s.ShowAnimations;
        MinimizeOnLaunch = s.MinimizeOnLaunch;
        CloseToTray = s.CloseToTray;
        MinimizeToTray = s.MinimizeToTray;
        StartMinimized = s.StartMinimized;
        LaunchOnStartup = s.LaunchOnStartup;
        DiscordPresence = s.DiscordPresence;
        AutoSyncPlaytime = s.AutoSyncPlaytime;
        RememberWindowBounds = s.RememberWindowBounds;
        FilterHideSteamSoftware = s.FilterHideSteamSoftware;
        SteamPath = s.SteamPath;
        EpicPath = s.EpicPath;
        GogPath = s.GogPath;
        ChiakiPath = s.ChiakiPath;
        MetadataSource = NormalizeMetadataSource(s.MetadataSource);
        NavPosition = s.NavPosition;
        StarDensity = s.StarDensity;
        UiScale = NormalizeUiScale(s.UiScale);
        RefreshIgdbUi();
        }
        finally { _loadingFromModel = false; }
        AccentColorValid = string.IsNullOrWhiteSpace(AccentColor) || Color.TryParse(AccentColor, out _);
        AccentColorValidationMessage = AccentColorValid ? string.Empty : "Use a valid hex color like #d4a853.";
        ClearPendingChanges("No unsaved changes.");
    }

    // ─── Settings commands ────────────────────────────────────────────────────

    [RelayCommand]
    private void Save()
    {
        if (!AccentColorValid)
        {
            StatusMessage = "Fix accent color before saving.";
            return;
        }
        var s = _settingsSvc.Get();
        s.DefaultView = DefaultView;
        s.Theme = Theme;
        s.AccentColor = AccentColor;
        s.ShowAnimations = ShowAnimations;
        s.MinimizeOnLaunch = MinimizeOnLaunch;
        s.CloseToTray = CloseToTray;
        s.MinimizeToTray = MinimizeToTray;
        s.StartMinimized = StartMinimized;
        s.LaunchOnStartup = LaunchOnStartup;
        StartupService.ApplyLaunchOnStartup(LaunchOnStartup);
        s.DiscordPresence = DiscordPresence;
        s.AutoSyncPlaytime = AutoSyncPlaytime;
        s.RememberWindowBounds = RememberWindowBounds;
        s.FilterHideSteamSoftware = FilterHideSteamSoftware;
        s.SteamPath = SteamPath;
        s.EpicPath = EpicPath;
        s.GogPath = GogPath;
        s.ChiakiPath = ChiakiPath;
        s.MetadataSource = NormalizeMetadataSource(MetadataSource);
        s.NavPosition = NavPosition;
        s.ToolbarPosition = NavPosition; // Keep the two aliases in sync.
        s.StarDensity = StarDensity;
        s.UiScale = NormalizeUiScale(UiScale);
        _settingsSvc.Save(s);

        // Apply live to the running window (avoids requiring a restart).
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mw)
        {
            mw.ApplyUiScale(UiScale);
            if (mw.DataContext is MainViewModel mvm)
            {
                mvm.ToolbarPosition = NavPosition;
                mvm.ViewMode = MainViewModel.NormalizeViewMode(DefaultView);
            }

            // Rebuild Orbit with fresh star-density / animations settings, if it's loaded.
            var orbit = FindDescendant<Views.OrbitView>(mw);
            orbit?.Rebuild();
        }

        if (DiscordPresence && !_discord.IsConnected) _discord.Connect();
        else if (!DiscordPresence && _discord.IsConnected) _discord.Disconnect();

        LoadChiakiStatus();
        StatusMessage = "Settings saved.";
        ClearPendingChanges("All changes saved.");
    }

    [RelayCommand]
    private void Reset()
    {
        var s = _settingsSvc.Reset();
        LoadFromModel(s);
        StatusMessage = "Settings reset to defaults.";
        ClearPendingChanges("Settings restored to defaults.");
    }

    [RelayCommand]
    private async Task UpdateChiaki()
    {
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        StatusMessage = "Checking chiaki-ng for updates…";
        var (ok, version, err) = await chiaki.CheckAndUpdateAsync();
        StatusMessage = ok
            ? (version is not null ? $"chiaki-ng is up to date (v{version})" : "chiaki-ng is up to date")
            : $"chiaki-ng update failed: {err}";
    }

    [RelayCommand]
    private void UninstallChiaki()
    {
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        var result = chiaki.UninstallFull();
        StatusMessage = result switch
        {
            "bundled" => "chiaki-ng uninstalled. You can reinstall it from the wizard.",
            "config"  => "Custom chiaki-ng path cleared.",
            "system"  => "chiaki-ng is a system install — remove it via your package manager or installer.",
            _         => "chiaki-ng was not found.",
        };
        // Clear the path textbox and refresh the status card.
        ChiakiPath = null;
        LoadChiakiStatus();
    }

    [RelayCommand]
    private void OpenChiakiGui()
    {
        try { App.Services.GetRequiredService<ChiakiService>().OpenGui(); }
        catch (Exception ex) { StatusMessage = "Could not open chiaki-ng: " + ex.Message; }
    }

    [RelayCommand]
    private async Task SyncPlaytime()
    {
        var svc = App.Services.GetRequiredService<PlaytimeSyncService>();
        StatusMessage = "Syncing Steam playtime…";
        var r = await svc.SyncAsync();
        if (r.Error is not null)
            StatusMessage = "Playtime sync failed: " + r.Error;
        else if (r.UpdatedCount == 0)
            StatusMessage = "Playtime already up to date.";
        else
            StatusMessage = $"Playtime synced — {r.UpdatedCount} game(s) updated.";
    }

    [RelayCommand]
    private async Task RerunWizard()
    {
        // Mark FirstRun and immediately relaunch the wizard as a modal.
        var s = _settingsSvc.Get();
        s.FirstRun = true;
        _settingsSvc.Save(s);
        StatusMessage = "Launching setup wizard…";

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is not null)
            {
                var wizard = new Views.Dialogs.StartupWizardDialog();
                await wizard.ShowDialog(lifetime.MainWindow);
            }
        });
    }

    /// <summary>Vite SettingsPanel: validate + save in one step (no separate Validate button).</summary>
    [RelayCommand]
    private async Task SaveSteamGridDbKey()
    {
        if (string.IsNullOrWhiteSpace(SteamGridDbKey)) return;
        IsValidatingKey = true;
        SteamGridDbKeyInvalid = false;
        StatusMessage = null;
        RefreshSteamGridDbUi();
        try
        {
            var (ok, error) = await _covers.ValidateSteamGridDbKeyAsync(SteamGridDbKey);
            if (!ok)
            {
                SteamGridDbKeyInvalid = true;
                StatusMessage = $"Key is invalid: {error}";
                return;
            }
            _creds.SetPassword("cereal", "steamgriddb_key", SteamGridDbKey);
            HasSteamGridDbSecret = true;
            SteamGridDbKey = string.Empty;
            SteamGridDbKeyValid = false;
            SteamGridDbKeyInvalid = false;
            StatusMessage = "SteamGridDB key saved.";
        }
        finally
        {
            IsValidatingKey = false;
            RefreshSteamGridDbUi();
        }
    }

    [RelayCommand]
    private void DeleteSteamGridDbKey()
    {
        _creds.DeletePassword("cereal", "steamgriddb_key");
        SteamGridDbKey = null;
        SteamGridDbKeyValid = false;
        HasSteamGridDbSecret = false;
        SteamGridDbKeyInvalid = false;
        StatusMessage = "SteamGridDB key removed.";
        RefreshSteamGridDbUi();
    }

    [RelayCommand]
    private void OpenSteamGridDbPreferences()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("https://www.steamgriddb.com/profile/preferences/api")
            { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { StatusMessage = "Couldn't open browser: " + ex.Message; }
    }

    /// <summary>Electron <c>steamgriddb:login</c> parity — open prefs, then paste + validate + save.</summary>
    [RelayCommand]
    private async Task SteamGridDbGuidedLoginAsync()
    {
        OpenSteamGridDbPreferences();

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime life
            || life.MainWindow is null)
            return;

        var pasteBtn = new Button { Content = "Paste API key", Padding = new Avalonia.Thickness(14, 8), Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(14, 8) };

        var dlg = new Window
        {
            Title = "SteamGridDB",
            Width = 440,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#12122a"),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Copy your API key from the SteamGridDB page that opened, then click “Paste API key”.",
                        Foreground = Brush.Parse("#e8e4de"),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancelBtn, pasteBtn },
                    },
                },
            },
        };

        var pasted = false;
        pasteBtn.Click += (_, _) => { pasted = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => dlg.Close();

        await dlg.ShowDialog(life.MainWindow);
        if (!pasted) return;

        var apiKey = (await App.ReadClipboardTextAsync()).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            StatusMessage = "Clipboard is empty. Copy your SteamGridDB API key first, then try again.";
            return;
        }

        var (ok, error) = await _covers.ValidateSteamGridDbKeyAsync(apiKey);
        if (!ok)
        {
            StatusMessage = $"API key appears invalid: {error}";
            return;
        }

        _creds.SetPassword("cereal", "steamgriddb_key", apiKey);
        HasSteamGridDbSecret = true;
        SteamGridDbKey = string.Empty;
        SteamGridDbKeyValid = false;
        SteamGridDbKeyInvalid = false;
        StatusMessage = "SteamGridDB key saved.";
        RefreshSteamGridDbUi();
    }

    [RelayCommand]
    private void FetchAllArtwork()
    {
        _covers.EnqueueAll();
        StatusMessage = "Queued artwork download for all games.";
    }

    [RelayCommand]
    private async Task FetchAllMetadata()
    {
        var meta = App.Services.GetRequiredService<MetadataService>();
        StatusMessage = "Fetching metadata for all games…";

        void OnProgress(object? _, MetadataProgressArgs e) =>
            Dispatcher.UIThread.Post(() => StatusMessage = e.Done
                ? $"Updated {e.Updated} of {e.Total} games."
                : $"Fetching metadata ({e.Completed}/{e.Total})…");

        meta.ProgressChanged += OnProgress;
        try
        {
            var (updated, total) = await meta.FetchAllAsync();
            _covers.EnqueueAll();
            StatusMessage = $"Metadata complete — updated {updated} of {total} games.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Metadata fetch failed: {ex.Message}";
        }
        finally { meta.ProgressChanged -= OnProgress; }
    }

    [RelayCommand]
    private async Task RescanAll()
    {
        StatusMessage = "Scanning all platforms…";
        var merged = 0;
        var toMerge = new List<Game>();
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.DetectInstalled();
                if (result.Games.Count > 0)
                    toMerge.AddRange(result.Games);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[settings] Rescan failed for provider {Provider}",
                    provider.GetType().Name);
            }
        }

        if (toMerge.Count > 0)
        {
            var (processed, _, survivors) = _games.AddRangeWithSurvivors(toMerge);
            merged = processed;
            foreach (var g in survivors)
            {
                if (!string.IsNullOrEmpty(g.CoverUrl))
                    _covers.EnqueueGame(g.Id);
            }
        }

        StatusMessage = $"Rescan complete — {merged} game(s) merged.";
    }

    // ─── Update commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckForUpdate()
    {
        UpdateState = "checking";
        StatusMessage = "Checking for updates…";
        await _updateSvc.CheckAsync();
        if (UpdateState == "checking")
        {
            UpdateState = "idle";
            StatusMessage = "Already up to date.";
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        UpdateState = "downloading";
        await _updateSvc.DownloadAndInstallAsync();
    }

    [RelayCommand]
    private void InstallUpdate() => _updateSvc.ApplyAndRestart();

    // ─── Danger zone commands ─────────────────────────────────────────────────

    // Snapshot of the last Clear-All so "Undo" can restore the exact rows.
    [ObservableProperty] private bool _canUndoClear;
    private List<Cereal.App.Models.Game>? _clearedSnapshot;
    private CancellationTokenSource? _undoCts;

    [RelayCommand]
    private void ClearAllGames()
    {
        if (!ConfirmingClearGames) { ConfirmingClearGames = true; return; }

        // Deep-copy via JSON so playtime / categories / custom art survive undo.
        var all = _games.GetAll();
        var json = System.Text.Json.JsonSerializer.Serialize(all);
        _clearedSnapshot = System.Text.Json.JsonSerializer.Deserialize<List<Cereal.App.Models.Game>>(json) ?? [];
        var count = all.Count;
        _games.ClearLibrary();

        ConfirmingClearGames = false;
        CanUndoClear = true;
        StatusMessage = $"Cleared {count} game(s). Undo available for 10s.";
        RefreshLibraryStats();

        // Auto-expire the undo window.
        _undoCts?.Cancel();
        _undoCts?.Dispose();
        _undoCts = null;
        _undoCts = new CancellationTokenSource();
        var token = _undoCts.Token;
        _ = Task.Delay(10_000, token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                _clearedSnapshot = null;
                CanUndoClear = false;
            });
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void UndoClearGames()
    {
        if (_clearedSnapshot is null) return;
        _games.AddRange(_clearedSnapshot);
        StatusMessage = $"Restored {_clearedSnapshot.Count} game(s).";
        _clearedSnapshot = null;
        CanUndoClear = false;
        _undoCts?.Cancel();
        _undoCts?.Dispose();
        _undoCts = null;
        RefreshLibraryStats();
    }

    [RelayCommand]
    private void CancelClearGames() => ConfirmingClearGames = false;

    [RelayCommand]
    private void ClearCovers()
    {
        if (!ConfirmingClearCovers) { ConfirmingClearCovers = true; return; }
        var paths = App.Services.GetRequiredService<PathService>();
        var count = 0;

        // Delete any file in covers/ or headers/ matching cover_<id>.* / header_<id>.*
        // (CoverService writes extensions based on URL, e.g. .jpg, .png)
        foreach (var g in _games.GetAll())
        {
            var id = Sanitize(g.Id);
            foreach (var f in SafeEnumerate(paths.CoversDir,  $"cover_{id}.*"))
            {
                try { File.Delete(f); count++; }
                catch (Exception ex) { Log.Debug(ex, "[settings] Delete cover file {Path}", f); }
            }
            foreach (var f in SafeEnumerate(paths.HeadersDir, $"header_{id}.*"))
            {
                try { File.Delete(f); count++; }
                catch (Exception ex) { Log.Debug(ex, "[settings] Delete header file {Path}", f); }
            }
            // Legacy paths from PathService.GetCoverPath / GetHeaderPath (if any remain)
            try
            {
                if (File.Exists(paths.GetCoverPath(g.Id)))
                {
                    File.Delete(paths.GetCoverPath(g.Id));
                    count++;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[settings] Delete legacy cover"); }
            try
            {
                if (File.Exists(paths.GetHeaderPath(g.Id)))
                {
                    File.Delete(paths.GetHeaderPath(g.Id));
                    count++;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[settings] Delete legacy header"); }

            // Clear the cached paths on the game record so covers re-download
            g.LocalCoverPath = null;
            g.LocalHeaderPath = null;
        }
        ConfirmingClearCovers = false;
        StatusMessage = $"Cleared {count} cover file(s).";
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern) : [];
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[settings] SafeEnumerate failed for {Dir} {Pattern}", dir, pattern);
            return [];
        }
    }

    private static string Sanitize(string id) =>
        string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand]
    private void CancelClearCovers() => ConfirmingClearCovers = false;

    public async Task ExportLibraryAsync(string path)
    {
        var bundle = new LibraryExportBundle
        {
            Games = _games.GetAll(),
            Categories = _db.Db.Categories?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList() ?? [],
            ExportedAt = DateTime.UtcNow.ToString("O"),
        };
        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        StatusMessage = $"Library exported ({bundle.Games.Count} games).";
    }

    public async Task ImportLibraryAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var games = new List<Game>();
            List<string>? categories = null;

            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    games = JsonSerializer.Deserialize<List<Game>>(json) ?? [];
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("games", out var gEl) && gEl.ValueKind == JsonValueKind.Array)
                        games = JsonSerializer.Deserialize<List<Game>>(gEl.GetRawText()) ?? [];
                    if (root.TryGetProperty("categories", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
                        categories = JsonSerializer.Deserialize<List<string>>(cEl.GetRawText()) ?? [];
                }
            }

            if (games.Count == 0 && categories is null)
            {
                StatusMessage = "Import failed: invalid file.";
                return;
            }

            var (processed, newRows) = _games.AddRange(games);
            if (categories is { Count: > 0 })
            {
                _db.Db.Categories ??= [];
                foreach (var c in categories.Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    if (!_db.Db.Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
                        _db.Db.Categories.Add(c.Trim());
                }
                _db.Save();
            }
            OnPropertyChanged(nameof(GameCount));
            OnPropertyChanged(nameof(TotalPlaytimeLabel));
            StatusMessage = newRows < processed
                ? $"Imported {newRows} new, merged {processed - newRows} with existing ({processed} total)."
                : $"Imported {newRows} game(s).";
        }
        catch (Exception ex) { StatusMessage = $"Import failed: {ex.Message}"; }
    }

    // ─── About ────────────────────────────────────────────────────────────────

    public string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public string DataPath =>
        App.Services.GetRequiredService<PathService>().AppDataDir;

    [RelayCommand]
    private void OpenDataFolder() =>
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(DataPath) { UseShellExecute = true });

    // Walks the visual tree from `root` and returns the first descendant of type T.
    private static T? FindDescendant<T>(Avalonia.Visual root) where T : class
    {
        if (root is T match) return match;
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root))
        {
            var found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}
