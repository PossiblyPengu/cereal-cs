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

    public MediaViewModel Media { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _activeNav = "library";
    [ObservableProperty] private string _viewMode = "cards";
    [ObservableProperty] private GameCardViewModel? _selectedGame;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private ObservableCollection<string> _activePlatformFilters = [];
    [ObservableProperty] private ObservableCollection<string> _activeCategoryFilters = [];
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _showInstalledOnly;
    [ObservableProperty] private string _sortOrder = "name";
    [ObservableProperty] private string _quickFilter = "all";

    public IEnumerable<string> AllCategories =>
        _games.GetAll()
              .SelectMany(g => g.Categories ?? Enumerable.Empty<string>())
              .Distinct()
              .OrderBy(c => c);

    public ObservableCollection<GameCardViewModel> VisibleGames { get; } = [];
    public ObservableCollection<PlatformGroupViewModel> GameGroups { get; } = [];
    public ObservableCollection<PlatformChipViewModel> PlatformChips { get; } = [];

    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showDetect;
    [ObservableProperty] private bool _showChiaki;
    [ObservableProperty] private bool _showXcloud;
    [ObservableProperty] private bool _showFocus;
    [ObservableProperty] private bool _showSearch;
    [ObservableProperty] private string? _zoomScreenshotUrl;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string? _searchPlatformFilter;
    public bool AnyPanelOpen => ShowSettings || ShowDetect || ShowChiaki || ShowXcloud;

    // Continue banner
    [ObservableProperty] private bool _showContinueBanner;
    public GameCardViewModel? ContinueGame { get; private set; }

    // Toasts
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    // Search results
    public IEnumerable<GameCardViewModel> SearchResults =>
        string.IsNullOrEmpty(SearchQuery)
            ? []
            : VisibleGames
                .Where(g => g.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .Where(g => SearchPlatformFilter == null || g.Platform == SearchPlatformFilter)
                .Take(12);

    public IEnumerable<string> SearchActivePlatforms =>
        string.IsNullOrEmpty(SearchQuery)
            ? []
            : VisibleGames
                .Where(g => g.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Platform)
                .Distinct();

    public bool HasNoSearchResults =>
        !string.IsNullOrEmpty(SearchQuery) && !SearchResults.Any();

    public ObservableCollection<StreamTabViewModel> StreamTabs { get; } = [];

    // Active stream (first tab that is not disconnected)
    public StreamTabViewModel? ActiveStreamTab =>
        StreamTabs.FirstOrDefault(t => t.State is "streaming" or "connecting" or "launching" or "gui");
    public bool IsStreaming => ActiveStreamTab is not null;
    public bool IsStreamConnecting =>
        ActiveStreamTab?.State is "connecting" or "launching";

    public MainViewModel(
        GameService games,
        SettingsService settings,
        CoverService covers,
        ChiakiService chiaki,
        XcloudService xcloud,
        SmtcService smtc)
    {
        _games = games;
        _settings = settings;
        _covers = covers;
        _chiaki = chiaki;
        _xcloud = xcloud;
        Media = new MediaViewModel(smtc);

        ViewMode = settings.Get().DefaultView ?? "cards";

        covers.ProgressChanged += OnCoverProgress;
        chiaki.SessionEvent += OnChiakiEvent;
        chiaki.GamesRefreshed += (_, _) => Refresh();
        xcloud.SessionEvent += OnXcloudEvent;
        StreamTabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ActiveStreamTab));
            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsStreamConnecting));
        };

        Refresh();

        // Continue banner: most recently played game
        var latest = _games.GetAll()
            .Where(g => g.LastPlayed != null && g.Hidden != true)
            .OrderByDescending(g => g.LastPlayed)
            .FirstOrDefault();
        if (latest is not null)
        {
            ContinueGame = new GameCardViewModel(latest, _games);
            ShowContinueBanner = true;
        }
    }

    public void Refresh()
    {
        var all = _games.GetAll();

        var platformCounts = all.GroupBy(g => g.Platform ?? "custom")
            .ToDictionary(g => g.Key, g => g.Count());
        PlatformChips.Clear();
        foreach (var (plat, count) in platformCounts.OrderBy(kv => kv.Key))
            PlatformChips.Add(new PlatformChipViewModel(plat, count, ActivePlatformFilters.Contains(plat)));

        var preFilter = all
            .Where(g => ShowHidden || g.Hidden != true)
            .Where(g => !ShowInstalledOnly || g.Installed != false)
            .Where(g => ActivePlatformFilters.Count == 0 || ActivePlatformFilters.Contains(g.Platform))
            .Where(g => ActiveCategoryFilters.Count == 0 ||
                        (g.Categories?.Any(c => ActiveCategoryFilters.Contains(c)) ?? false))
            .Where(g => QuickFilter != "favorites" || (g.Favorite ?? false))
            .Where(g => QuickFilter != "recent" || g.LastPlayed != null)
            .Where(g => string.IsNullOrEmpty(SearchText) ||
                        g.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        var sorted = (QuickFilter == "recent")
            ? preFilter.OrderByDescending(g => g.LastPlayed ?? "").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            : SortOrder switch
            {
                "played"  => preFilter.OrderByDescending(g => g.PlaytimeMinutes ?? 0).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "recent"  => preFilter.OrderByDescending(g => g.LastPlayed ?? "").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "added"   => preFilter.OrderByDescending(g => g.AddedAt ?? "").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                _         => preFilter.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            };

        var filtered = sorted
            .Select(g => new GameCardViewModel(g, _games))
            .ToList();

        VisibleGames.Clear();
        foreach (var c in filtered) VisibleGames.Add(c);

        GameGroups.Clear();
        foreach (var grp in filtered.GroupBy(c => c.Platform ?? "custom").OrderBy(g => g.Key))
        {
            var group = new PlatformGroupViewModel(grp.Key);
            foreach (var card in grp) group.Games.Add(card);
            GameGroups.Add(group);
        }

        if (SelectedGame is not null)
        {
            var fresh = VisibleGames.FirstOrDefault(c => c.Id == SelectedGame.Id);
            SelectedGame = fresh;
            if (fresh is null) ShowFocus = false;
        }

        OnPropertyChanged(nameof(AllCategories));
    }

    private int _searchSelectedIndex = -1;

    partial void OnSearchQueryChanged(string value)
    {
        SearchPlatformFilter = null;
        _searchSelectedIndex = -1;
        OnPropertyChanged(nameof(SearchResults));
        OnPropertyChanged(nameof(SearchActivePlatforms));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    partial void OnSearchPlatformFilterChanged(string? value) =>
        OnPropertyChanged(nameof(SearchResults));

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnActivePlatformFiltersChanged(ObservableCollection<string> value) => Refresh();
    partial void OnActiveCategoryFiltersChanged(ObservableCollection<string> value) => Refresh();
    partial void OnShowHiddenChanged(bool value) => Refresh();
    partial void OnShowInstalledOnlyChanged(bool value) => Refresh();
    partial void OnSortOrderChanged(string value) => Refresh();
    partial void OnQuickFilterChanged(string value) => Refresh();

    [RelayCommand]
    private void SelectGame(GameCardViewModel? card)
    {
        SelectedGame = card;
        ShowFocus = card is not null;
    }

    // ── Screenshot zoom ───────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenZoom(string url) => ZoomScreenshotUrl = url;

    [RelayCommand]
    private void CloseZoom() => ZoomScreenshotUrl = null;

    // ── Search overlay ────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenSearch()
    {
        SearchQuery = "";
        SearchPlatformFilter = null;
        ShowSearch = true;
    }

    [RelayCommand]
    private void CloseSearch()
    {
        ShowSearch = false;
        SearchQuery = "";
        SearchPlatformFilter = null;
    }

    [RelayCommand]
    private void SearchSelect(GameCardViewModel card)
    {
        CloseSearch();
        SelectGame(card);
    }

    [RelayCommand]
    private async Task SearchLaunch(GameCardViewModel card)
    {
        CloseSearch();
        await LaunchGame(card);
    }

    [RelayCommand]
    private void SetSearchPlatformFilter(string? platform) =>
        SearchPlatformFilter = SearchPlatformFilter == platform ? null : platform;

    public void SearchMoveSelection(int delta)
    {
        var results = SearchResults.ToList();
        if (results.Count == 0) return;
        if (_searchSelectedIndex >= 0 && _searchSelectedIndex < results.Count)
            results[_searchSelectedIndex].IsSearchHighlighted = false;
        _searchSelectedIndex = Math.Clamp(_searchSelectedIndex + delta, 0, results.Count - 1);
        results[_searchSelectedIndex].IsSearchHighlighted = true;
    }

    public void SearchConfirm(bool launch)
    {
        var results = SearchResults.ToList();
        var card = _searchSelectedIndex >= 0 && _searchSelectedIndex < results.Count
            ? results[_searchSelectedIndex]
            : results.FirstOrDefault();
        if (card is null) return;
        if (launch) _ = SearchLaunchAsync(card);
        else SearchSelect(card);
    }

    private async Task SearchLaunchAsync(GameCardViewModel card)
    {
        CloseSearch();
        await LaunchGame(card);
    }

    // ── Continue banner ───────────────────────────────────────────────────────

    [RelayCommand]
    private void DismissContinueBanner() => ShowContinueBanner = false;

    // ── Toasts ────────────────────────────────────────────────────────────────

    public void ShowToast(string message)
    {
        var toast = new ToastViewModel(message);
        Toasts.Add(toast);
        Task.Delay(3000).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Remove(toast)));
    }

    public event EventHandler? AddGameRequested;
    public event EventHandler<Game>? EditGameRequested;

    [RelayCommand]
    private void ShowAddGame() => AddGameRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditGame()
    {
        if (SelectedGame is not null)
            EditGameRequested?.Invoke(this, SelectedGame.Game);
    }

    public void AddGame(Models.Game game)
    {
        _games.Add(game);
        if (!string.IsNullOrEmpty(game.CoverUrl))
            _covers.EnqueueGame(game.Id);
        Refresh();
        StatusMessage = $"Added \"{game.Name}\"";
    }

    public void UpdateGame(Models.Game game)
    {
        _games.Update(game);
        if (!string.IsNullOrEmpty(game.CoverUrl))
            _covers.EnqueueGame(game.Id);
        Refresh();
        StatusMessage = $"Saved \"{game.Name}\"";
    }

    [RelayCommand]
    private async Task LaunchGame(GameCardViewModel card)
    {
        if (card is null) return;
        StatusMessage = $"Launching {card.Name}...";
        var launch = App.Services.GetRequiredService<LaunchService>();
        var result = await launch.LaunchAsync(card.Game);
        StatusMessage = result.Success ? null : $"Failed to launch: {result.Error}";
    }

    [RelayCommand]
    private void SelectGameById(string id)
    {
        var game = _games.GetAll().FirstOrDefault(g => g.Id == id);
        if (game is null) return;
        var card = VisibleGames.FirstOrDefault(c => c.Id == id)
                   ?? new GameCardViewModel(game, _games);
        SelectGame(card);
    }

    [RelayCommand]
    private async Task LaunchGameById(string id)
    {
        var game = _games.GetAll().FirstOrDefault(g => g.Id == id);
        if (game is null) return;
        var card = VisibleGames.FirstOrDefault(c => c.Id == id)
                   ?? new GameCardViewModel(game, _games);
        await LaunchGame(card);
    }

    [RelayCommand]
    private void DeleteGame(string id)
    {
        _games.Delete(id);
        if (SelectedGame?.Id == id)
        {
            SelectedGame = null;
            ShowFocus = false;
        }
        Refresh();
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
        SortOrder = "name";
        ShowHidden = false;
        ShowInstalledOnly = false;
        QuickFilter = "all";
        Refresh();
    }

    [RelayCommand]
    private void SetSort(string sort) => SortOrder = sort;

    [RelayCommand]
    private void SetQuickFilter(string filter) => QuickFilter = filter;

    [RelayCommand]
    private void RefreshGameInfo()
    {
        if (SelectedGame is null) return;
        _covers.EnqueueGame(SelectedGame.Id);
        Refresh();
    }

    public string ViewModeToggleLabel => ViewMode == "cards" ? "Orbit" : "Cards";
    partial void OnViewModeChanged(string value) => OnPropertyChanged(nameof(ViewModeToggleLabel));

    [RelayCommand]
    private void ToggleViewMode() => ViewMode = ViewMode == "cards" ? "orbit" : "cards";

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
    [RelayCommand] private void CloseFocus()    { ShowFocus = false; SelectedGame = null; }

    [RelayCommand]
    private void CloseAllPanels()
    {
        ShowSettings = ShowDetect = ShowChiaki = ShowXcloud = false;
        OnPropertyChanged(nameof(AnyPanelOpen));
    }

    public void EscapePressed()
    {
        if (ZoomScreenshotUrl is not null) { CloseZoom(); return; }
        if (ShowSearch)   { CloseSearch(); return; }
        if (ShowFocus)    { ShowFocus = false; SelectedGame = null; return; }
        if (ShowSettings) { ShowSettings = false; OnPropertyChanged(nameof(AnyPanelOpen)); return; }
        if (ShowDetect)   { ShowDetect  = false; OnPropertyChanged(nameof(AnyPanelOpen)); return; }
        if (ShowChiaki)   { ShowChiaki  = false; OnPropertyChanged(nameof(AnyPanelOpen)); return; }
        if (ShowXcloud)   { ShowXcloud  = false; OnPropertyChanged(nameof(AnyPanelOpen)); return; }
    }

    [RelayCommand]
    private void CloseStreamTab(string gameId)
    {
        _chiaki.StopStream(gameId);
        _xcloud.StopSession(gameId);
        var tab = StreamTabs.FirstOrDefault(t => t.GameId == gameId);
        if (tab is not null) StreamTabs.Remove(tab);
    }

    private void OnCoverProgress(object? sender, CoverProgressArgs e)
    {
        if (e.Downloaded > 0)
            foreach (var card in VisibleGames) card.Refresh();
        StatusMessage = e.Done ? null : $"Downloading artwork... ({e.Remaining} remaining)";
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
                    OnPropertyChanged(nameof(ActiveStreamTab));
                    OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsStreamConnecting));
                }
            }
            else if (stateStr == "disconnected" && tab is not null)
                StreamTabs.Remove(tab);
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
            {
                tab.State = s?.ToString() ?? "";
                OnPropertyChanged(nameof(ActiveStreamTab));
                OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsStreamConnecting));
            }
        }
        else if (e.Type == "disconnected" && tab is not null)
            StreamTabs.Remove(tab);
    }
}

public class ToastViewModel
{
    public string Message { get; }
    public ToastViewModel(string message) => Message = message;
}

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
