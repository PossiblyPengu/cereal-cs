using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GameService _games;
    private readonly SettingsService _settings;
    private readonly CoverService _covers;
    private readonly ChiakiService _chiaki;
    private readonly XcloudService _xcloud;

    // ─── Observable state ────────────────────────────────────────────────────

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _activeNav = "library";   // library | settings | detect | chiaki
    [ObservableProperty] private string _viewMode = "cards";      // cards | orbit
    [ObservableProperty] private GameCardViewModel? _selectedGame;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;

    // Active filter chips
    [ObservableProperty] private ObservableCollection<string> _activePlatformFilters = [];
    [ObservableProperty] private ObservableCollection<string> _activeCategoryFilters = [];
    [ObservableProperty] private bool _showHidden;

    // Flat filtered view (for orbit)
    public ObservableCollection<GameCardViewModel> VisibleGames { get; } = [];

    // Grouped by platform (for card grid)
    public ObservableCollection<PlatformGroupViewModel> GameGroups { get; } = [];

    // Platform filter chips in nav pill
    public ObservableCollection<PlatformChipViewModel> PlatformChips { get; } = [];

    // Overlay panel visibility
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showDetect;
    [ObservableProperty] private bool _showChiaki;
    [ObservableProperty] private bool _showXcloud;
    public bool AnyPanelOpen => ShowSettings || ShowDetect || ShowChiaki || ShowXcloud;

    // Tab bar sessions (chiaki / xcloud streams)
    public ObservableCollection<StreamTabViewModel> StreamTabs { get; } = [];

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MainViewModel(
        GameService games,
        SettingsService settings,
        CoverService covers,
        ChiakiService chiaki,
        XcloudService xcloud)
    {
        _games = games;
        _settings = settings;
        _covers = covers;
        _chiaki = chiaki;
        _xcloud = xcloud;

        // Apply saved default view
        ViewMode = settings.Get().DefaultView ?? "cards";

        // Subscribe to cover/chiaki events
        covers.ProgressChanged += OnCoverProgress;
        chiaki.SessionEvent += OnChiakiEvent;
        chiaki.GamesRefreshed += (_, _) => Refresh();
        xcloud.SessionEvent += OnXcloudEvent;

        Refresh();
    }

    // ─── Library refresh ─────────────────────────────────────────────────────

    public void Refresh()
    {
        var all = _games.GetAll();

        // Rebuild platform chips from all games (not filtered)
        var platformCounts = all.GroupBy(g => g.Platform ?? "custom")
            .ToDictionary(g => g.Key, g => g.Count());
        PlatformChips.Clear();
        foreach (var (plat, count) in platformCounts.OrderBy(kv => kv.Key))
            PlatformChips.Add(new PlatformChipViewModel(plat, count, ActivePlatformFilters.Contains(plat)));

        // Apply filters
        var filtered = all
            .Where(g => ShowHidden || g.Hidden != true)
            .Where(g => ActivePlatformFilters.Count == 0 || ActivePlatformFilters.Contains(g.Platform))
            .Where(g => ActiveCategoryFilters.Count == 0 ||
                        (g.Categories?.Any(c => ActiveCategoryFilters.Contains(c)) ?? false))
            .Where(g => string.IsNullOrEmpty(SearchText) ||
                        g.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GameCardViewModel(g, _games))
            .ToList();

        // Flat list for orbit view
        VisibleGames.Clear();
        foreach (var c in filtered) VisibleGames.Add(c);

        // Grouped by platform for card grid
        GameGroups.Clear();
        foreach (var grp in filtered.GroupBy(c => c.Platform ?? "custom")
                                    .OrderBy(g => g.Key))
        {
            var group = new PlatformGroupViewModel(grp.Key);
            foreach (var card in grp) group.Games.Add(card);
            GameGroups.Add(group);
        }
    }

    // Re-run filter whenever search or filter chips change
    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnActivePlatformFiltersChanged(ObservableCollection<string> value) => Refresh();
    partial void OnActiveCategoryFiltersChanged(ObservableCollection<string> value) => Refresh();
    partial void OnShowHiddenChanged(bool value) => Refresh();

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectGame(GameCardViewModel? card) => SelectedGame = card;

    /// <summary>Raised when the UI should show the AddGame dialog (avoids ViewModel → View dependency).</summary>
    public event EventHandler? AddGameRequested;

    [RelayCommand]
    private void ShowAddGame() => AddGameRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Called by the View after AddGameDialog closes with a result.</summary>
    public void AddGame(Models.Game game)
    {
        _games.Add(game);
        if (!string.IsNullOrEmpty(game.CoverUrl))
            _covers.EnqueueGame(game.Id);
        Refresh();
        StatusMessage = $"Added \"{game.Name}\"";
    }

    [RelayCommand]
    private async Task LaunchGame(GameCardViewModel card)
    {
        if (card is null) return;
        StatusMessage = $"Launching {card.Name}…";
        var launch = App.Services.GetRequiredService<LaunchService>();
        var result = await launch.LaunchAsync(card.Game);
        StatusMessage = result.Success ? null : $"Failed to launch: {result.Error}";
    }

    [RelayCommand]
    private void TogglePlatformFilter(string platform)
    {
        if (ActivePlatformFilters.Contains(platform))
            ActivePlatformFilters.Remove(platform);
        else
            ActivePlatformFilters.Add(platform);
        Refresh();
    }

    [RelayCommand]
    private void ToggleCategoryFilter(string category)
    {
        if (ActiveCategoryFilters.Contains(category))
            ActiveCategoryFilters.Remove(category);
        else
            ActiveCategoryFilters.Add(category);
        Refresh();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        ActivePlatformFilters.Clear();
        ActiveCategoryFilters.Clear();
        SearchText = "";
        Refresh();
    }

    // Label for the toggle button: shows what you'll switch TO
    public string ViewModeToggleLabel => ViewMode == "cards" ? "Orbit" : "Cards";
    partial void OnViewModeChanged(string value) => OnPropertyChanged(nameof(ViewModeToggleLabel));

    [RelayCommand]
    private void ToggleViewMode() =>
        ViewMode = ViewMode == "cards" ? "orbit" : "cards";

    [RelayCommand]
    private void Navigate(string nav) => ActiveNav = nav;

    [RelayCommand] private void OpenSettings()  { ShowSettings = true;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseSettings() { ShowSettings = false; OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenDetect()    { ShowDetect  = true;   OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseDetect()   { ShowDetect  = false;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenChiaki()    { ShowChiaki  = true;   OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseChiaki()   { ShowChiaki  = false;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenXcloud()    { ShowXcloud  = true;   OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseXcloud()   { ShowXcloud  = false;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseAllPanels() { ShowSettings = ShowDetect = ShowChiaki = ShowXcloud = false; OnPropertyChanged(nameof(AnyPanelOpen)); }

    [RelayCommand]
    private void CloseStreamTab(string gameId)
    {
        _chiaki.StopStream(gameId);
        _xcloud.StopSession(gameId);
        var tab = StreamTabs.FirstOrDefault(t => t.GameId == gameId);
        if (tab is not null) StreamTabs.Remove(tab);
    }

    // ─── Event handlers ───────────────────────────────────────────────────────

    private void OnCoverProgress(object? sender, CoverProgressArgs e)
    {
        if (e.Downloaded > 0)
        {
            // Refresh visible cards' cover paths
            foreach (var card in VisibleGames)
                card.Refresh();
        }
        StatusMessage = e.Done ? null : $"Downloading artwork… ({e.Remaining} remaining)";
    }

    private void OnChiakiEvent(object? sender, ChiakiEventArgs e)
    {
        if (e.Type == "state" && e.Data.TryGetValue("state", out var state))
        {
            var stateStr = state?.ToString() ?? "";
            var tab = StreamTabs.FirstOrDefault(t => t.GameId == e.GameId);

            if (stateStr is "launching" or "connecting" or "streaming" or "gui")
            {
                if (tab is null)
                {
                    var game = _games.GetAll().Find(g => g.Id == e.GameId);
                    StreamTabs.Add(new StreamTabViewModel(e.GameId, game?.Name ?? e.GameId, "psn"));
                }
                else
                {
                    tab.State = stateStr;
                }
            }
            else if (stateStr == "disconnected")
            {
                if (tab is not null) StreamTabs.Remove(tab);
            }
        }
    }

    private void OnXcloudEvent(object? sender, XcloudEventArgs e)
    {
        var tab = StreamTabs.FirstOrDefault(t => t.GameId == e.GameId);
        if (e.Type == "state")
        {
            if (tab is null)
                StreamTabs.Add(new StreamTabViewModel(e.GameId, e.GameId, "xbox"));
            else if (e.Data.TryGetValue("state", out var s))
                tab.State = s?.ToString() ?? "";
        }
        else if (e.Type == "disconnected" && tab is not null)
        {
            StreamTabs.Remove(tab);
        }
    }
}

// ─── Stream tab (chiaki / xcloud session tab in title bar) ───────────────────

public partial class StreamTabViewModel : ObservableObject
{
    public string GameId { get; }
    public string Title { get; }
    public string Platform { get; }
    [ObservableProperty] private string _state = "connecting";

    public StreamTabViewModel(string gameId, string title, string platform)
    {
        GameId = gameId;
        Title = title;
        Platform = platform;
    }
}
