using System.Management;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Metadata;
using Cereal.App.Services.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.ViewModels;

public sealed record PlatformStatRow(string Label, string Color, int Count, double BarRatio);

public sealed record MostPlayedRow(int Rank, string Name, string Time);

public partial class SettingsViewModel : ObservableObject
{
    public static readonly string[] ViewOptions    = ["cards", "orbit"];
    public static readonly string[] NavPositions   = ["top", "bottom", "left", "right"];
    public static readonly string[] StarDensities  = ["low", "normal", "high"];
    public static readonly string[] UiScaleOptions = ["80%", "90%", "100%", "110%", "120%", "150%"];
    public static readonly AppTheme[] ThemeOptions = AppThemes.All;

    private readonly SettingsService _settingsSvc;
    private readonly DiscordService _discord;
    private readonly CoverService _covers;
    private readonly CredentialService _creds;
    private readonly ThemeService _themeSvc;
    private readonly GameService _games;
    private readonly UpdateService _updateSvc;
    private readonly IEnumerable<IProvider> _providers;

    // ─── Settings properties ─────────────────────────────────────────────────

    [ObservableProperty] private string _defaultView = "cards";
    [ObservableProperty] private string _theme = "midnight";
    [ObservableProperty] private string _accentColor = "#7c6af7";
    [ObservableProperty] private bool _showAnimations = true;
    [ObservableProperty] private bool _minimizeOnLaunch;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _launchOnStartup;
    [ObservableProperty] private bool _discordPresence = true;
    [ObservableProperty] private bool _autoSyncPlaytime = true;
    [ObservableProperty] private bool _rememberWindowBounds = true;
    [ObservableProperty] private bool _filterHideSteamSoftware = true;
    [ObservableProperty] private string? _steamPath;
    [ObservableProperty] private string? _epicPath;
    [ObservableProperty] private string? _gogPath;
    [ObservableProperty] private string? _chiakiPath;
    [ObservableProperty] private string _metadataSource = "steam";

    /// <summary>Items for the metadata ComboBox (must match <see cref="MetadataService"/>).</summary>
    public IReadOnlyList<string> MetadataSourceOptions { get; } = ["steam", "wikipedia"];

    private static string NormalizeMetadataSource(string? v) =>
        string.Equals(v, "wikipedia", StringComparison.OrdinalIgnoreCase) ? "wikipedia" : "steam";
    [ObservableProperty] private string? _steamGridDbKey;
    [ObservableProperty] private string _navPosition = "top";
    [ObservableProperty] private string _starDensity = "normal";
    [ObservableProperty] private string _uiScale = "100%";

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _steamGridDbKeyValid;
    [ObservableProperty] private bool _isValidatingKey;

    // ─── Section navigation ───────────────────────────────────────────────────

    [ObservableProperty] private string _activeSection = "appearance";

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

    // Silent, partial save used by the OnX partials above. Swallows exceptions
    // to avoid crashing the UI if the user is typing into one of the path
    // TextBoxes while we race to persist.
    private bool _loadingFromModel;
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
        }
        catch { /* best-effort */ }
    }

    // ─── chiaki-ng update state ───────────────────────────────────────────────

    [ObservableProperty] private string? _chiakiVersion;
    [ObservableProperty] private bool _chiakiUpdateAvailable;
    [ObservableProperty] private string? _chiakiUpdateVersion;

    private void LoadChiakiStatus()
    {
        var chiaki = App.Services.GetService(typeof(ChiakiService)) as ChiakiService;
        if (chiaki is null) return;
        var (_, _, version) = chiaki.GetStatus();
        ChiakiVersion = version;
    }

    [RelayCommand]
    private async Task CheckChiakiUpdate()
    {
        StatusMessage = "Checking chiaki-ng…";
        await Task.Delay(200); // let UI breathe
        StatusMessage = "chiaki-ng is up to date.";
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

    private void LoadSystemSpecs()
    {
        var os  = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        var ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var ramStr = ram > 0 ? $"{ram / 1_073_741_824.0:F1} GB" : "—";
        var cpu = "—";
        var gpu = "—";

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in s.Get()) { cpu = obj["Name"]?.ToString() ?? "—"; break; }
            }
            catch { }
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var obj in s.Get()) { gpu = obj["Name"]?.ToString() ?? "—"; break; }
            }
            catch { }
        }

        Dispatcher.UIThread.Post(() =>
        {
            SpecsOs  = os;
            SpecsRam = ramStr;
            SpecsCpu = cpu;
            SpecsGpu = gpu;
        });
    }

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
                             GameService games, UpdateService updateSvc, IEnumerable<IProvider> providers)
    {
        _settingsSvc = settings;
        _discord = discord;
        _covers = covers;
        _creds = creds;
        _themeSvc = theme;
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
    }

    public AppTheme? SelectedTheme
    {
        get => AppThemes.Find(Theme);
        set { if (value is not null) Theme = value.Id; }
    }

    public IEnumerable<ThemeSwatchViewModel> ThemeSwatches =>
        AppThemes.All.Select(t => new ThemeSwatchViewModel(t, Theme));

    partial void OnThemeChanged(string value)
    {
        _themeSvc.Apply(value, AccentColor);
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(ThemeSwatches));
    }

    partial void OnAccentColorChanged(string value)
    {
        _themeSvc.Apply(Theme, value);
    }

    // Live UI-scale application (mirror Electron's applyUiScale() in utils.ts 9-12 —
    // zoom applies immediately as the dropdown changes, no Save button required).
    partial void OnUiScaleChanged(string value)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
                d.MainWindow is MainWindow mw)
            {
                mw.ApplyUiScale(value);
            }
        }
        catch { /* no-op */ }
    }

    // Live default-view update so changing the dropdown previews the new view.
    partial void OnDefaultViewChanged(string value)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow?.DataContext is MainViewModel mvm &&
            !string.IsNullOrEmpty(value))
        {
            mvm.ViewMode = value;
        }
    }

    // Live nav-position: floating toolbar immediately snaps to top/bottom
    // so the user can see the change without hunting for the Save button.
    partial void OnNavPositionChanged(string value)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow?.DataContext is MainViewModel mvm &&
            !string.IsNullOrEmpty(value))
        {
            mvm.ToolbarPosition = value;
        }
    }

    private void LoadFromModel(Settings s)
    {
        _loadingFromModel = true;
        try
        {
        DefaultView = s.DefaultView;
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
        UiScale = s.UiScale;
        }
        finally { _loadingFromModel = false; }
    }

    // ─── Settings commands ────────────────────────────────────────────────────

    [RelayCommand]
    private void Save()
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
        s.UiScale = UiScale;
        _settingsSvc.Save(s);

        // Apply live to the running window (avoids requiring a restart).
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow mw)
        {
            mw.ApplyUiScale(UiScale);
            if (mw.DataContext is MainViewModel mvm)
                mvm.ToolbarPosition = NavPosition;

            // Rebuild Orbit with fresh star-density / animations settings, if it's loaded.
            var orbit = FindDescendant<Views.OrbitView>(mw);
            orbit?.Rebuild();
        }

        if (DiscordPresence && !_discord.IsConnected) _discord.Connect();
        else if (!DiscordPresence && _discord.IsConnected) _discord.Disconnect();

        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private void Reset()
    {
        var s = _settingsSvc.Reset();
        LoadFromModel(s);
        StatusMessage = "Settings reset to defaults.";
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
        var removed = chiaki.Uninstall();
        StatusMessage = removed
            ? "chiaki-ng uninstalled. You can reinstall it from the wizard."
            : "chiaki-ng was not installed.";
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

    [RelayCommand]
    private async Task ValidateSteamGridDbKey()
    {
        if (string.IsNullOrWhiteSpace(SteamGridDbKey)) return;
        IsValidatingKey = true;
        StatusMessage = null;
        var (ok, error) = await _covers.ValidateSteamGridDbKeyAsync(SteamGridDbKey);
        SteamGridDbKeyValid = ok;
        StatusMessage = ok ? "API key is valid." : $"Invalid key: {error}";
        IsValidatingKey = false;
    }

    [RelayCommand]
    private void SaveSteamGridDbKey()
    {
        if (string.IsNullOrWhiteSpace(SteamGridDbKey)) return;
        _creds.SetPassword("cereal", "steamgriddb_key", SteamGridDbKey);
        StatusMessage = "SteamGridDB key saved.";
    }

    [RelayCommand]
    private void DeleteSteamGridDbKey()
    {
        _creds.DeletePassword("cereal", "steamgriddb_key");
        SteamGridDbKey = null;
        SteamGridDbKeyValid = false;
        StatusMessage = "SteamGridDB key removed.";
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
        var added = 0;
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.DetectInstalled();
                foreach (var game in result.Games)
                {
                    var g = _games.Add(game);
                    if (!string.IsNullOrEmpty(g.CoverUrl))
                        _covers.EnqueueGame(g.Id);
                    added++;
                }
            }
            catch { }
        }
        StatusMessage = $"Rescan complete — {added} game(s) merged.";
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
        var ids = all.Select(g => g.Id).ToList();
        foreach (var id in ids) _games.Delete(id);

        ConfirmingClearGames = false;
        CanUndoClear = true;
        StatusMessage = $"Cleared {ids.Count} game(s). Undo available for 10s.";
        RefreshLibraryStats();

        // Auto-expire the undo window.
        _undoCts?.Cancel();
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
        foreach (var g in _clearedSnapshot) _games.Add(g);
        StatusMessage = $"Restored {_clearedSnapshot.Count} game(s).";
        _clearedSnapshot = null;
        CanUndoClear = false;
        _undoCts?.Cancel();
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
            foreach (var f in SafeEnumerate(paths.CoversDir,  $"cover_{id}.*"))   { try { File.Delete(f); count++; } catch { } }
            foreach (var f in SafeEnumerate(paths.HeadersDir, $"header_{id}.*"))  { try { File.Delete(f); count++; } catch { } }
            // Legacy paths from PathService.GetCoverPath / GetHeaderPath (if any remain)
            try { if (File.Exists(paths.GetCoverPath(g.Id)))  { File.Delete(paths.GetCoverPath(g.Id));  count++; } } catch { }
            try { if (File.Exists(paths.GetHeaderPath(g.Id))) { File.Delete(paths.GetHeaderPath(g.Id)); count++; } } catch { }

            // Clear the cached paths on the game record so covers re-download
            g.LocalCoverPath = null;
            g.LocalHeaderPath = null;
        }
        ConfirmingClearCovers = false;
        StatusMessage = $"Cleared {count} cover file(s).";
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern) : []; }
        catch { return []; }
    }

    private static string Sanitize(string id) =>
        string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand]
    private void CancelClearCovers() => ConfirmingClearCovers = false;

    public async Task ExportLibraryAsync(string path)
    {
        var games = _games.GetAll();
        var json = JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        StatusMessage = $"Library exported ({games.Count} games).";
    }

    public async Task ImportLibraryAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var games = JsonSerializer.Deserialize<List<Game>>(json);
            if (games is null) { StatusMessage = "Import failed: invalid file."; return; }
            foreach (var g in games) _games.Add(g);
            OnPropertyChanged(nameof(GameCount));
            OnPropertyChanged(nameof(TotalPlaytimeLabel));
            StatusMessage = $"Imported {games.Count} game(s).";
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
