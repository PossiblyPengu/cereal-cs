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
    public static readonly string[] ViewOptions = ["cards", "orbit"];
    public static readonly AppTheme[] ThemeOptions = AppThemes.All;
    private readonly SettingsService _settingsSvc;
    private readonly DiscordService _discord;
    private readonly CoverService _covers;
    private readonly CredentialService _creds;
    private readonly ThemeService _themeSvc;
    private readonly GameService _games;
    private readonly IEnumerable<IProvider> _providers;

    // ─── Bound properties (shadow the Settings model for live two-way binding) ─

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

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _steamGridDbKeyValid;
    [ObservableProperty] private bool _isValidatingKey;

    public SettingsViewModel(SettingsService settings, DiscordService discord,
                             CoverService covers, CredentialService creds, ThemeService theme,
                             GameService games, IEnumerable<IProvider> providers)
    {
        _settingsSvc = settings;
        _discord = discord;
        _covers = covers;
        _creds = creds;
        _themeSvc = theme;
        _games = games;
        _providers = providers;
        LoadFromModel(settings.Get());
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
    }

    // ─── Commands ────────────────────────────────────────────────────────────

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
        _settingsSvc.Save(s);

        // Toggle Discord live
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

    public string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public string DataPath =>
        App.Services.GetRequiredService<PathService>().AppDataDir;

    [RelayCommand]
    private void OpenDataFolder() =>
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(DataPath) { UseShellExecute = true });
}
