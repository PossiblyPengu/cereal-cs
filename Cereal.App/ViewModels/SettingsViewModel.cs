using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.ViewModels;

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
    [ObservableProperty] private bool _discordPresence = true;
    [ObservableProperty] private bool _autoSyncPlaytime = true;
    [ObservableProperty] private bool _rememberWindowBounds = true;
    [ObservableProperty] private bool _filterHideSteamSoftware = true;
    [ObservableProperty] private string? _steamPath;
    [ObservableProperty] private string? _epicPath;
    [ObservableProperty] private string? _gogPath;
    [ObservableProperty] private string? _chiakiPath;
    [ObservableProperty] private string _metadataSource = "steamgriddb";
    [ObservableProperty] private string? _steamGridDbKey;
    [ObservableProperty] private string _navPosition = "top";
    [ObservableProperty] private string _starDensity = "normal";
    [ObservableProperty] private string _uiScale = "100%";

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _steamGridDbKeyValid;
    [ObservableProperty] private bool _isValidatingKey;

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
    public string TotalPlaytimeLabel
    {
        get
        {
            var mins = _games.GetAll().Sum(g => g.PlaytimeMinutes ?? 0);
            if (mins <= 0) return "—";
            return mins < 60 ? $"{mins}m" : $"{mins / 60}h {mins % 60:00}m";
        }
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
    }

    public AppTheme? SelectedTheme
    {
        get => AppThemes.Find(Theme);
        set { if (value is not null) Theme = value.Id; }
    }

    partial void OnThemeChanged(string value)
    {
        _themeSvc.Apply(value);
        OnPropertyChanged(nameof(SelectedTheme));
    }

    private void LoadFromModel(Settings s)
    {
        DefaultView = s.DefaultView;
        Theme = s.Theme;
        AccentColor = s.AccentColor;
        ShowAnimations = s.ShowAnimations;
        MinimizeOnLaunch = s.MinimizeOnLaunch;
        CloseToTray = s.CloseToTray;
        DiscordPresence = s.DiscordPresence;
        AutoSyncPlaytime = s.AutoSyncPlaytime;
        RememberWindowBounds = s.RememberWindowBounds;
        FilterHideSteamSoftware = s.FilterHideSteamSoftware;
        SteamPath = s.SteamPath;
        EpicPath = s.EpicPath;
        GogPath = s.GogPath;
        ChiakiPath = s.ChiakiPath;
        MetadataSource = s.MetadataSource;
        NavPosition = s.NavPosition;
        StarDensity = s.StarDensity;
        UiScale = s.UiScale;
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
        s.DiscordPresence = DiscordPresence;
        s.AutoSyncPlaytime = AutoSyncPlaytime;
        s.RememberWindowBounds = RememberWindowBounds;
        s.FilterHideSteamSoftware = FilterHideSteamSoftware;
        s.SteamPath = SteamPath;
        s.EpicPath = EpicPath;
        s.GogPath = GogPath;
        s.ChiakiPath = ChiakiPath;
        s.MetadataSource = MetadataSource;
        s.NavPosition = NavPosition;
        s.StarDensity = StarDensity;
        s.UiScale = UiScale;
        _settingsSvc.Save(s);

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
    private void FetchAllArtwork()
    {
        _covers.EnqueueAll();
        StatusMessage = "Queued artwork download for all games.";
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

    [RelayCommand]
    private void ClearAllGames()
    {
        if (!ConfirmingClearGames) { ConfirmingClearGames = true; return; }
        var ids = _games.GetAll().Select(g => g.Id).ToList();
        foreach (var id in ids) _games.Delete(id);
        ConfirmingClearGames = false;
        OnPropertyChanged(nameof(GameCount));
        OnPropertyChanged(nameof(TotalPlaytimeLabel));
        StatusMessage = $"Cleared {ids.Count} game(s).";
    }

    [RelayCommand]
    private void CancelClearGames() => ConfirmingClearGames = false;

    [RelayCommand]
    private void ClearCovers()
    {
        if (!ConfirmingClearCovers) { ConfirmingClearCovers = true; return; }
        var paths = App.Services.GetRequiredService<PathService>();
        foreach (var g in _games.GetAll())
        {
            try { if (File.Exists(paths.GetCoverPath(g.Id)))  File.Delete(paths.GetCoverPath(g.Id));  } catch { }
            try { if (File.Exists(paths.GetHeaderPath(g.Id))) File.Delete(paths.GetHeaderPath(g.Id)); } catch { }
        }
        ConfirmingClearCovers = false;
        StatusMessage = "All local cover art cleared.";
    }

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
}
