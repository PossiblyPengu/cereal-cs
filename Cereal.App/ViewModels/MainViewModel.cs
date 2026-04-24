using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Metadata;
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
    [ObservableProperty] private string _viewMode = "orbit";
    [ObservableProperty] private GameCardViewModel? _selectedGame;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private ObservableCollection<string> _activePlatformFilters = [];
    [ObservableProperty] private ObservableCollection<string> _activeCategoryFilters = [];
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _showInstalledOnly;
    [ObservableProperty] private bool _hideSteamSoftware = true;
    [ObservableProperty] private string _sortOrder = "name";
    [ObservableProperty] private string _quickFilter = "all";
    // "top" | "bottom" | "left" | "right" — drives nav pill placement.
    [ObservableProperty] private string _toolbarPosition = "top";

    /// <summary>Matches Electron <c>--tb-scale</c> (App.tsx): shrinks floating chrome on narrow windows.</summary>
    [ObservableProperty] private double _toolbarScale = 1.0;

    public static double ComputeToolbarScale(double windowWidth) =>
        Math.Clamp((windowWidth - 600.0) / 1000.0 * 0.35 + 0.65, 0.65, 1.0);

    public Avalonia.Layout.VerticalAlignment ToolbarVerticalAlignment =>
        ToolbarPosition?.ToLowerInvariant() switch
        {
            "bottom" => Avalonia.Layout.VerticalAlignment.Bottom,
            "left" or "right" => Avalonia.Layout.VerticalAlignment.Center,
            _ => Avalonia.Layout.VerticalAlignment.Top,
        };

    public Avalonia.Layout.HorizontalAlignment ToolbarHorizontalAlignment =>
        ToolbarPosition?.ToLowerInvariant() switch
        {
            "left" => Avalonia.Layout.HorizontalAlignment.Left,
            "right" => Avalonia.Layout.HorizontalAlignment.Right,
            _ => Avalonia.Layout.HorizontalAlignment.Center,
        };

    public Avalonia.Layout.Orientation ToolbarOrientation =>
        ToolbarPosition is "left" or "right"
            ? Avalonia.Layout.Orientation.Vertical
            : Avalonia.Layout.Orientation.Horizontal;

    /// <summary>index.css <c>.nav-pill.pos-*</c> offsets (top clears ~46px title band like Electron).</summary>
    public Avalonia.Thickness ToolbarMargin =>
        ToolbarPosition?.ToLowerInvariant() switch
        {
            "left" => new Avalonia.Thickness(6, 0, 0, 0),
            "right" => new Avalonia.Thickness(0, 0, 6, 0),
            "bottom" => new Avalonia.Thickness(0, 8, 0, 6),
            _ => new Avalonia.Thickness(0, 46, 0, 8),
        };

    public CornerRadius NavPillCornerRadius =>
        ToolbarPosition is "left" or "right"
            ? new CornerRadius(16)
            : new CornerRadius(22);

    public Avalonia.Thickness NavPillPadding =>
        ToolbarPosition is "left" or "right"
            ? new Avalonia.Thickness(6, 10, 6, 10)
            : new Avalonia.Thickness(10, 4, 10, 4);

    public double NavPillMinWidth =>
        ToolbarPosition is "left" or "right" ? 68 : 0;

    // Put the now-playing widget on the opposite edge from the toolbar so they
    // never overlap (toolbar at bottom → media at top, and vice versa).
    public Avalonia.Layout.VerticalAlignment MediaVerticalAlignment =>
        string.Equals(ToolbarPosition, "bottom", StringComparison.OrdinalIgnoreCase)
            ? Avalonia.Layout.VerticalAlignment.Top
            : Avalonia.Layout.VerticalAlignment.Bottom;

    public Avalonia.Thickness StreamBarBorderThickness =>
        ToolbarPosition?.ToLowerInvariant() switch
        {
            "bottom" => new Avalonia.Thickness(0, 1, 0, 0),
            "left" => new Avalonia.Thickness(0, 0, 1, 0),
            "right" => new Avalonia.Thickness(1, 0, 0, 0),
            _ => new Avalonia.Thickness(0, 0, 0, 1),
        };

    // Left-aligned widget margin: top-16 when media is at top, bottom-16 when at bottom.
    public Avalonia.Thickness MediaWidgetMargin =>
        ToolbarPosition?.ToLowerInvariant() switch
        {
            "bottom" => new Avalonia.Thickness(16, 16, 0, 0),
            "left" => new Avalonia.Thickness(72, 0, 0, 16),
            "right" => new Avalonia.Thickness(16, 0, 72, 16),
            _ => new Avalonia.Thickness(16, 0, 0, 16),
        };

    partial void OnToolbarPositionChanged(string value)
    {
        OnPropertyChanged(nameof(ToolbarVerticalAlignment));
        OnPropertyChanged(nameof(ToolbarHorizontalAlignment));
        OnPropertyChanged(nameof(ToolbarOrientation));
        OnPropertyChanged(nameof(ToolbarMargin));
        OnPropertyChanged(nameof(NavPillCornerRadius));
        OnPropertyChanged(nameof(NavPillPadding));
        OnPropertyChanged(nameof(NavPillMinWidth));
        OnPropertyChanged(nameof(MediaVerticalAlignment));
        OnPropertyChanged(nameof(StreamBarBorderThickness));
        OnPropertyChanged(nameof(MediaWidgetMargin));
    }

    public IEnumerable<string> AllCategories =>
        _games.GetAll()
              .SelectMany(g => g.Categories ?? Enumerable.Empty<string>())
              .Distinct()
              .OrderBy(c => c);

    // Mirrors Electron's filterCount badge on the nav-pill filter button
    // (App.tsx 968): sum of platforms + categories + sort + text + flags.
    public int ActiveFilterCount =>
        ActivePlatformFilters.Count
        + ActiveCategoryFilters.Count
        + (string.IsNullOrEmpty(SearchText) ? 0 : 1)
        + (ShowInstalledOnly ? 1 : 0)
        + (!HideSteamSoftware ? 1 : 0)
        + (SortOrder != "name" && !string.IsNullOrEmpty(SortOrder) ? 1 : 0);

    public bool HasActiveFilters => ActiveFilterCount > 0;

    public ObservableCollection<GameCardViewModel> VisibleGames { get; } = [];
    public ObservableCollection<CardLayoutEntry> CardLayoutRows { get; } = [];
    public ObservableCollection<PlatformChipViewModel> PlatformChips { get; } = [];

    /// <summary>Full filtered card list for the library; <see cref="VisibleGames"/> is a scroll-expanded prefix.</summary>
    private List<GameCardViewModel>? _libraryCardsFull;
    private int _libraryCardsVisibleCount;

    /// <summary>Visible card width (150) + horizontal gap (14); must match the cards grid in MainView.</summary>
    public const int LibraryCardCellWidth = 164;

    private int _libraryColumnCount = 6;
    /// <summary>Horizontal strip count for wrapping cards; updated from the cards scroll width.</summary>
    public int LibraryColumnCount
    {
        get => _libraryColumnCount;
        set
        {
            var v = Math.Max(1, value);
            if (v == _libraryColumnCount) return;
            _libraryColumnCount = v;
            OnPropertyChanged(nameof(LibraryColumnCount));
            if (VisibleGames.Count > 0) RebuildCardLayout();
        }
    }

    private void RebuildCardLayout() => RebuildCardLayout(VisibleGames);

    private void RebuildCardLayout(IEnumerable<GameCardViewModel> cards) =>
        CardLayoutEntry.BuildRows(CardLayoutRows, cards, _libraryColumnCount);

    /// <summary>Vite card grid: expand when the scroll sentinel nears the viewport (rootMargin 400px).</summary>
    public void TryExpandLibraryCardsFromScroll(double scrollOffset, double viewportHeight, double extentHeight)
    {
        if (_libraryCardsFull is null || _libraryCardsVisibleCount >= _libraryCardsFull.Count)
            return;

        if (extentHeight <= viewportHeight + 48)
        {
            AppendLibraryCards(int.MaxValue);
            return;
        }

        const double rootMargin = 400;
        var distFromBottom = extentHeight - scrollOffset - viewportHeight;
        if (distFromBottom > rootMargin)
            return;

        AppendLibraryCards(40);
    }

    private void AppendLibraryCards(int maxMore)
    {
        if (_libraryCardsFull is null) return;
        const int batch = 40;
        var remaining = _libraryCardsFull.Count - _libraryCardsVisibleCount;
        if (remaining <= 0) return;
        var take = maxMore == int.MaxValue ? remaining : Math.Min(batch, remaining);
        for (var i = 0; i < take; i++)
            VisibleGames.Add(_libraryCardsFull[_libraryCardsVisibleCount + i]);
        _libraryCardsVisibleCount += take;
        RebuildCardLayout(VisibleGames);
    }

    private void NotifyStreamEmbeddingProps() => OnPropertyChanged(nameof(ShowChiakiEmbedHost));

    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showDetect;
    [ObservableProperty] private bool _showChiaki;
    [ObservableProperty] private bool _showXcloud;
    [ObservableProperty] private bool _showPlatforms;
    [ObservableProperty] private bool _showFocus;
    [ObservableProperty] private bool _showSearch;
    [ObservableProperty] private string? _zoomScreenshotUrl;
    [ObservableProperty] private bool _isRefreshingGameInfo;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string? _searchPlatformFilter;
    public bool AnyPanelOpen => ShowSettings || ShowDetect || ShowChiaki || ShowXcloud || ShowPlatforms;
    public IEnumerable<PanelTabViewModel> PanelTabs
    {
        get
        {
            if (ShowSettings) yield return new PanelTabViewModel("settings", "Settings");
            if (ShowDetect) yield return new PanelTabViewModel("detect", "Detect");
            if (ShowPlatforms) yield return new PanelTabViewModel("platforms", "Platforms");
            if (ShowChiaki) yield return new PanelTabViewModel("chiaki", "Remote Play");
            if (ShowXcloud) yield return new PanelTabViewModel("xcloud", "Cloud Gaming");
        }
    }
    public string RefreshInfoButtonLabel => IsRefreshingGameInfo ? "Fetching..." : "Refresh Info";
    partial void OnIsRefreshingGameInfoChanged(bool value) => OnPropertyChanged(nameof(RefreshInfoButtonLabel));
    partial void OnShowSettingsChanged(bool value) => NotifyTabsChanged();
    partial void OnShowDetectChanged(bool value) => NotifyTabsChanged();
    partial void OnShowChiakiChanged(bool value) => NotifyTabsChanged();
    partial void OnShowXcloudChanged(bool value) => NotifyTabsChanged();
    partial void OnShowPlatformsChanged(bool value) => NotifyTabsChanged();

    // Continue banner
    [ObservableProperty] private bool _showContinueBanner;
    [ObservableProperty] private GameCardViewModel? _continueGame;

    // Toasts
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    // App auto-update banner (App.tsx 1274–1294).
    // ShowAppUpdate becomes true when UpdateService reports a new version is
    // available; user can dismiss it or click "Restart" once downloaded.
    [ObservableProperty] private bool _showAppUpdate;
    [ObservableProperty] private string? _appUpdateVersion;
    [ObservableProperty] private bool _appUpdateReady;
    [ObservableProperty] private int _appUpdatePercent;
    public bool AppUpdateDownloading => ShowAppUpdate && !AppUpdateReady;
    public string AppUpdateDownloadLabel =>
        AppUpdatePercent > 0
            ? $"Downloading v{AppUpdateVersion} · {AppUpdatePercent}%"
            : $"Downloading v{AppUpdateVersion}";
    partial void OnShowAppUpdateChanged(bool value) => OnPropertyChanged(nameof(AppUpdateDownloading));
    partial void OnAppUpdateReadyChanged(bool value) => OnPropertyChanged(nameof(AppUpdateDownloading));
    partial void OnAppUpdateVersionChanged(string? value) => OnPropertyChanged(nameof(AppUpdateDownloadLabel));
    partial void OnAppUpdatePercentChanged(int value) => OnPropertyChanged(nameof(AppUpdateDownloadLabel));

    // Subtle progress pill (bottom-right), mirrors Electron's importProgress /
    // metaProgress UI in App.tsx 1232–1272. Single ObservableProperty set ==
    // show the pill, null == hide it.
    [ObservableProperty] private string? _progressPillText;
    [ObservableProperty] private double _progressPillPercent;
    [ObservableProperty] private bool _progressPillDone;
    public bool ShowProgressPill => !string.IsNullOrEmpty(ProgressPillText);
    partial void OnProgressPillTextChanged(string? value) =>
        OnPropertyChanged(nameof(ShowProgressPill));

    private CancellationTokenSource? _progressHideCts;
    private void SetProgress(string? text, double percent, bool done)
    {
        ProgressPillPercent = percent;
        ProgressPillDone = done;
        ProgressPillText = text;

        _progressHideCts?.Cancel();
        if (done && !string.IsNullOrEmpty(text))
        {
            _progressHideCts = new CancellationTokenSource();
            var token = _progressHideCts.Token;
            _ = Task.Delay(3_000, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgressPillText = null;
                    ProgressPillDone = false;
                });
            }, TaskScheduler.Default);
        }
    }

    // Search overlay uses full library scope (not the currently filtered view),
    // matching the original app behavior.
    private IEnumerable<GameCardViewModel> SearchLibrary =>
        _games.GetAll()
            .Where(g => g.Platform is not "psn" and not "psremote")
            .Select(g => new GameCardViewModel(g, _games));

    // Search results
    public IEnumerable<GameCardViewModel> SearchResults =>
        string.IsNullOrEmpty(SearchQuery)
            ? []
            : SearchLibrary
                .Where(g => g.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .Where(g => SearchPlatformFilter == null || g.Platform == SearchPlatformFilter)
                .Take(12);

    public IEnumerable<string> SearchActivePlatforms =>
        string.IsNullOrEmpty(SearchQuery)
            ? []
            : SearchLibrary
                .Where(g => g.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Platform)
                .Distinct();

    public bool HasNoSearchResults =>
        !string.IsNullOrEmpty(SearchQuery) && !SearchResults.Any();

    public ObservableCollection<StreamTabViewModel> StreamTabs { get; } = [];

    // Active stream (first true in-stream tab; GUI-only sessions are excluded
    // from overlay-active state to match source behavior).
    public StreamTabViewModel? ActiveStreamTab =>
        StreamTabs.FirstOrDefault(t => t.State is "streaming" or "connecting" or "launching");
    public StreamTabViewModel? GuiStreamTab =>
        StreamTabs.FirstOrDefault(t => t.State == "gui");
    public bool ShowGuiStreamFloat => GuiStreamTab is not null;
    public bool IsStreaming => ActiveStreamTab is not null;
    public bool IsStreamConnecting =>
        ActiveStreamTab?.State is "connecting" or "launching";

    /// <summary>Windows-only: show native host for embedded Chiaki video (parity with Electron stream bounds).</summary>
    public bool ShowChiakiEmbedHost =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && IsStreaming
        && ActiveStreamTab is { Platform: var p }
        && (string.Equals(p, "psn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "psremote", StringComparison.OrdinalIgnoreCase));

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

        var s = settings.Get();
        // Missing JSON key deserializes null — must not fall back to "cards" or orbit default is wrong.
        ViewMode = NormalizeViewMode(s.DefaultView);
        ToolbarPosition = string.IsNullOrWhiteSpace(s.ToolbarPosition) ? s.NavPosition : s.ToolbarPosition;
        HideSteamSoftware = s.FilterHideSteamSoftware;

        // Restore user-saved filter state (port parity with settings.js DEFAULT_SETTINGS).
        if (s.FilterPlatforms is { Count: > 0 })
            ActivePlatformFilters = new ObservableCollection<string>(s.FilterPlatforms);
        if (s.FilterCategories is { Count: > 0 })
            ActiveCategoryFilters = new ObservableCollection<string>(s.FilterCategories);
        if (!string.IsNullOrWhiteSpace(s.DefaultTab) && s.DefaultTab is "all" or "favorites" or "recent")
            QuickFilter = s.DefaultTab!;

        covers.ProgressChanged += OnCoverProgress;
        chiaki.SessionEvent += OnChiakiEvent;
        chiaki.GamesRefreshed += (_, _) => Refresh();
        xcloud.SessionEvent += OnXcloudEvent;
        StreamTabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ActiveStreamTab));
            OnPropertyChanged(nameof(GuiStreamTab));
            OnPropertyChanged(nameof(ShowGuiStreamFloat));
            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsStreamConnecting));
            OnPropertyChanged(nameof(ShowChiakiEmbedHost));
        };

        // Controller input from GamepadService is delivered on the UI thread.
        try
        {
            var gamepad = App.Services.GetRequiredService<GamepadService>();
            gamepad.ActionsReceived += (_, e) => HandleGamepadActions(e.Actions);
            gamepad.Connected    += (_, _) => ShowToast("Controller connected");
            gamepad.Disconnected += (_, _) => ShowToast("Controller disconnected");
        }
        catch { /* non-Windows / no-gamepad: silently skip */ }

        // Auto-update banner — mirrors App.tsx 1274–1294. The UpdateService
        // emits `UpdateAvailable` from its background check, then `UpdateReady`
        // once the payload has been staged by Velopack.
        try
        {
            var updater = App.Services.GetRequiredService<UpdateService>();
            updater.UpdateAvailable += (_, args) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AppUpdateVersion = args.NewVersion;
                AppUpdatePercent = 0;
                AppUpdateReady = false;
                ShowAppUpdate = true;
                // Kick off the download as soon as we know one is available
                // so the "Restart" button can light up when ready.
                _ = updater.DownloadAndInstallAsync();
            });
            updater.DownloadProgressChanged += (_, pct) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AppUpdatePercent = pct;
            });
            updater.UpdateReady += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AppUpdateReady = true;
            });
        }
        catch { /* non-Velopack dev run: silently skip */ }

        _games.LibraryChanged += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        Refresh();
        UpdateContinueBanner();
    }

    // ─── Gamepad ──────────────────────────────────────────────────────────────
    // Translates gamepad action strings into VM commands. Mirrors the mapping in
    // src/App.tsx's useGamepad callback.

    private int _gpIdx = -1;
    private string _gpArea = "cards"; // cards | orbit | focus | pill
    private int _gpPillIdx = 0; // 0: Remote Play, 1: Cloud Gaming

    public void HandleGamepadActions(IReadOnlyList<string> actions)
    {
        foreach (var act in actions) HandleGamepadAction(act);
    }

    private void HandleGamepadAction(string act)
    {
        // Global actions (work regardless of which panel is open):
        if (act == "back")
        {
            if (ShowFocus)       { ShowFocus = false; return; }
            if (ShowSearch)      { CloseSearch(); return; }
            if (ShowSettings)    { ShowSettings = false; return; }
            if (ShowDetect)      { ShowDetect = false; return; }
            if (ShowChiaki)      { ShowChiaki = false; return; }
            if (ShowXcloud)      { ShowXcloud = false; return; }
            if (ShowPlatforms)   { ShowPlatforms = false; return; }
            if (!string.IsNullOrEmpty(ZoomScreenshotUrl)) { ZoomScreenshotUrl = null; return; }
            return;
        }
        if (act == "start")   { ShowSettings = !ShowSettings; OnPropertyChanged(nameof(AnyPanelOpen)); return; }
        if (act == "select")  { if (ShowSearch) CloseSearch(); else OpenSearch(); return; }

        if (AnyPanelOpen) return; // Nothing else to do while a panel is open.
        if (ShowSearch)  return; // Directional nav must not bleed through the search overlay.

        // Stream pill area (cards mode): left/right switches button,
        // confirm opens selected stream panel, up returns to card grid.
        if (_gpArea == "pill" && ViewMode == "cards")
        {
            switch (act)
            {
                case "left":
                    _gpPillIdx = Math.Max(0, _gpPillIdx - 1);
                    return;
                case "right":
                    _gpPillIdx = Math.Min(1, _gpPillIdx + 1);
                    return;
                case "confirm":
                    if (_gpPillIdx == 0) OpenChiaki();
                    else OpenXcloud();
                    return;
                case "up":
                    _gpArea = "cards";
                    return;
            }
        }

        // y toggles view, but not while focus detail is open (matches Electron !focusGame guard).
        if (act == "y" && !ShowFocus) { ToggleViewMode(); return; }

        // LB / RB cycle quick-filter + active platform chips (tabs equivalent).
        if (act is "lb" or "rb")
        {
            var tabs = new List<string> { "all", "favorites", "recent" };
            foreach (var chip in PlatformChips) tabs.Add(chip.Id);
            tabs = tabs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var curIdx = Math.Max(0, tabs.IndexOf(QuickFilter));
            var delta = act == "rb" ? 1 : -1;
            curIdx = ((curIdx + delta) % tabs.Count + tabs.Count) % tabs.Count;
            QuickFilter = tabs[curIdx];
            return;
        }

        // Focus panel: navigate action buttons (play/fav/edit/delete) and confirm.
        if (ShowFocus && SelectedGame is not null)
        {
            var focusBtns = new[] { "play", "fav", "edit", "delete" };
            switch (act)
            {
                case "left":    _gpIdx = (_gpIdx - 1 + focusBtns.Length) % focusBtns.Length; break;
                case "right":   _gpIdx = (_gpIdx + 1) % focusBtns.Length; break;
                case "confirm":
                {
                    var fi = _gpIdx < 0 ? 0 : _gpIdx;
                    switch (focusBtns[fi])
                    {
                        case "play":   _ = LaunchGame(SelectedGame); break;
                        case "fav":    SelectedGame.ToggleFavoriteCommand.Execute(null); break;
                        case "edit":   EditGame(); break;
                        case "delete": DeleteGame(SelectedGame.Id); break;
                    }
                    break;
                }
                case "x": SelectedGame.ToggleFavoriteCommand.Execute(null); break;
            }
            _gpArea = "focus";
            return;
        }

        // Right stick in orbit: cycle through platform clusters (mirrors Electron r_* handling).
        if (ViewMode == "orbit" && act is "r_left" or "r_right" or "r_up" or "r_down")
        {
            var platIds = PlatformChips.Select(c => c.Id).ToList();
            if (platIds.Count > 0)
            {
                var ci = platIds.IndexOf(QuickFilter);
                if (ci < 0) ci = 0;
                ci = act is "r_right" or "r_down"
                    ? (ci + 1) % platIds.Count
                    : (ci - 1 + platIds.Count) % platIds.Count;
                QuickFilter = platIds[ci];
            }
            return;
        }

        // Library: move selection within VisibleGames and open focus on confirm.
        if (VisibleGames.Count == 0) return;
        var idx = Math.Max(0, Math.Min(_gpIdx, VisibleGames.Count - 1));

        // Keep in sync with library grid: 150 + 14 gap, ~64 horizontal margin.
        int cols = 6;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
                d.MainWindow is { } w)
                cols = Math.Max(1, (int)((w.Bounds.Width - 64) / (double)LibraryCardCellWidth));
        }
        catch { /* use default */ }

        switch (act)
        {
            case "right":   idx = Math.Min(VisibleGames.Count - 1, idx + 1); break;
            case "left":    idx = Math.Max(0, idx - 1); break;
            case "down":
                var next = Math.Min(VisibleGames.Count - 1, idx + cols);
                var isBottomRow = idx + cols >= VisibleGames.Count;
                if (ViewMode == "cards" && isBottomRow)
                {
                    _gpArea = "pill";
                    _gpPillIdx = 0;
                    return;
                }
                idx = next;
                break;
            case "up":      idx = Math.Max(0, idx - cols); break;
            case "confirm":
                _gpIdx = Math.Clamp(_gpIdx < 0 ? 0 : _gpIdx, 0, VisibleGames.Count - 1);
                SelectedGame = VisibleGames[_gpIdx];
                ShowFocus = true;
                _gpIdx = 0;
                _gpArea = "focus";
                return;
            case "x":
                VisibleGames[idx].ToggleFavoriteCommand.Execute(null);
                break;
        }
        _gpIdx = idx;
        _gpArea = ViewMode == "orbit" ? "orbit" : "cards";
        SelectedGame = VisibleGames[idx];
    }

    private void UpdateContinueBanner()
    {
        var latest = _games.GetAll()
            .Where(g => g.LastPlayed != null && g.Hidden != true)
            .OrderByDescending(g => g.LastPlayed)
            .FirstOrDefault();
        if (latest is not null)
        {
            ContinueGame = new GameCardViewModel(latest, _games);
            ShowContinueBanner = true;
        }
        else
        {
            ContinueGame = null;
            ShowContinueBanner = false;
        }
    }

    public void Refresh()
    {
        var all = _games.GetAll();

        // Hide-Steam-software filter: matches heuristics in Electron metadata.js —
        // explicit `software = true`, appdetails `type = "tool"/"application"`,
        // or category names that smell like "tool" / "software" / "soundtrack" / "demo".
        bool IsSteamSoftware(Game g)
        {
            if (g.Platform != "steam") return false;
            if (g.Software == true) return true;
            var type = g.Type?.ToLowerInvariant();
            if (type is "tool" or "application" or "demo" or "soundtrack") return true;
            if (g.Categories is { Count: > 0 } cats)
            {
                foreach (var c in cats)
                {
                    var lc = c.ToLowerInvariant();
                    if (lc.Contains("tool") || lc.Contains("software") ||
                        lc.Contains("soundtrack") || lc.Contains("benchmark") ||
                        lc.Contains("utility") || lc.Contains("utilities"))
                        return true;
                }
            }
            var n = (g.Name ?? string.Empty).ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(n,
                    @"redistributable|redistrib|steamworks|sdk|runtime|\bruntime\b|dedicated server|devkit|vr runtime|mod tools|soundtrack",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            return false;
        }

        // Drop ephemeral streaming stubs from the main grid (matches Electron: psn/psremote
        // live only as stream sessions, xbox tiles live in the xcloud panel).
        var visibleAll = all
            .Where(g => g.Platform is not "psn" and not "psremote")
            .Where(g => !HideSteamSoftware || !IsSteamSoftware(g))
            .ToList();

        var platformCounts = visibleAll.GroupBy(g => g.Platform ?? "custom")
            .ToDictionary(g => g.Key, g => g.Count());
        PlatformChips.Clear();
        foreach (var (plat, count) in platformCounts.OrderBy(kv => kv.Key))
            PlatformChips.Add(new PlatformChipViewModel(plat, count, ActivePlatformFilters.Contains(plat)));

        var preFilter = visibleAll
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
                "played"    => preFilter.OrderByDescending(g => g.PlaytimeMinutes ?? 0).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "recent"    => preFilter.OrderByDescending(g => g.LastPlayed ?? "").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "added"     => preFilter.OrderByDescending(g => g.AddedAt ?? "").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                "installed" => preFilter.OrderByDescending(g => g.Installed ?? false).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                // "default" / "name" / anything else → alphabetical.
                _           => preFilter.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            };

        var filtered = sorted
            .Select(g => new GameCardViewModel(g, _games))
            .ToList();

        // Progressive render (Vite App.tsx INITIAL_CARDS + IntersectionObserver): first chunk
        // immediately; the rest load as the user scrolls — see TryExpandLibraryCardsFromScroll.
        _libraryCardsFull = filtered;
        VisibleGames.Clear();
        CardLayoutRows.Clear();

        const int initialCards = 60;

        var first = Math.Min(initialCards, _libraryCardsFull.Count);
        for (var i = 0; i < first; i++)
            VisibleGames.Add(_libraryCardsFull[i]);
        _libraryCardsVisibleCount = first;
        RebuildCardLayout(VisibleGames);

        if (SelectedGame is not null)
        {
            var fresh = VisibleGames.FirstOrDefault(c => c.Id == SelectedGame.Id);
            SelectedGame = fresh;
            if (fresh is null) ShowFocus = false;
        }

        OnPropertyChanged(nameof(AllCategories));
        UpdateContinueBanner();
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

    partial void OnSearchPlatformFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(SearchResults));
        OnPropertyChanged(nameof(SearchActivePlatforms));
    }

    partial void OnSearchTextChanged(string value) { Refresh(); NotifyFilterCount(); }
    partial void OnActivePlatformFiltersChanged(ObservableCollection<string> value) { PersistFilters(); Refresh(); NotifyFilterCount(); }
    partial void OnActiveCategoryFiltersChanged(ObservableCollection<string> value) { PersistFilters(); Refresh(); NotifyFilterCount(); }
    partial void OnShowHiddenChanged(bool value) => Refresh();
    partial void OnShowInstalledOnlyChanged(bool value) { Refresh(); NotifyFilterCount(); }
    partial void OnHideSteamSoftwareChanged(bool value)
    {
        try
        {
            var s = _settings.Get();
            s.FilterHideSteamSoftware = value;
            _settings.Save(s);
        }
        catch { /* best-effort */ }
        Refresh();
        NotifyFilterCount();
    }
    partial void OnSortOrderChanged(string value) { Refresh(); NotifyFilterCount(); }

    private void NotifyFilterCount()
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
    }
    partial void OnQuickFilterChanged(string value) { PersistDefaultTab(); Refresh(); }

    private void PersistFilters()
    {
        try
        {
            var s = _settings.Get();
            s.FilterPlatforms  = ActivePlatformFilters.ToList();
            s.FilterCategories = ActiveCategoryFilters.ToList();
            _settings.Save(s);
        }
        catch { /* best-effort */ }
    }

    private void PersistDefaultTab()
    {
        try
        {
            var s = _settings.Get();
            s.DefaultTab = QuickFilter;
            _settings.Save(s);
        }
        catch { /* best-effort */ }
    }

    // Theme swatches on the main nav (ports App.tsx 1034–1069). Re-creates the
    // list whenever the theme changes so the "active ring" follows along.
    public IEnumerable<ThemeSwatchViewModel> ThemeSwatches =>
        Models.AppThemes.All.Select(t => new ThemeSwatchViewModel(t, _settings.Get().Theme ?? "midnight"));

    // Accent color mirror (persists on change) — used by the main-nav theme flyout
    // for the custom accent TextBox.
    public string AccentColor
    {
        get => _settings.Get().AccentColor ?? string.Empty;
        set
        {
            var trimmed = (value ?? string.Empty).Trim();
            var s = _settings.Get();
            if ((s.AccentColor ?? string.Empty) == trimmed) return;
            s.AccentColor = trimmed;
            _settings.Save(s);
            try { App.Services.GetRequiredService<Services.ThemeService>().ApplyCurrent(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[main] ApplyCurrent after accent save failed"); }
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void SetTheme(string themeId)
    {
        if (string.IsNullOrEmpty(themeId)) return;
        try
        {
            var s = _settings.Get();
            s.Theme = themeId;
            s.AccentColor = string.Empty; // reset custom accent to follow the theme
            _settings.Save(s);
            App.Services.GetRequiredService<Services.ThemeService>().ApplyCurrent();
            OnPropertyChanged(nameof(ThemeSwatches));
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[main] SetTheme failed"); }
    }

    // Toolbar position popover on the main nav (ports App.tsx 1122–1150).
    // ToolbarPosition is an [ObservableProperty] declared above; we just push
    // changes into it and the existing OnToolbarPositionChanged partial saves
    // to SettingsService on our behalf.
    [RelayCommand]
    private void SetToolbarPosition(string pos)
    {
        if (string.IsNullOrWhiteSpace(pos)) return;
        ToolbarPosition = pos.Trim().ToLowerInvariant() switch
        {
            "top" => "top",
            "bottom" => "bottom",
            "left" => "left",
            "right" => "right",
            _ => "top",
        };
    }

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
        SearchPlatformFilter = string.IsNullOrWhiteSpace(platform) || platform == "__all"
            ? null
            : (SearchPlatformFilter == platform ? null : platform);

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

    [RelayCommand]
    private void DismissAppUpdate() => ShowAppUpdate = false;

    [RelayCommand]
    private void InstallAppUpdate()
    {
        try { App.Services.GetRequiredService<UpdateService>().ApplyAndRestart(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[update] ApplyAndRestart failed"); }
    }

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
        var survivor = _games.Add(game);
        if (!string.IsNullOrEmpty(survivor.CoverUrl))
            _covers.EnqueueGame(survivor.Id);
        StatusMessage = $"Added \"{survivor.Name}\"";
    }

    public void UpdateGame(Models.Game game)
    {
        _games.Update(game);
        if (!string.IsNullOrEmpty(game.CoverUrl))
            _covers.EnqueueGame(game.Id);
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
        UpdateContinueBanner();
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

    // Port of Electron `games:install` — asks the platform client to install
    // the given title. No-op for psn/custom (surfaced as a toast).
    [RelayCommand]
    private async Task InstallGame()
    {
        if (SelectedGame is null) return;
        StatusMessage = $"Installing {SelectedGame.Name}…";
        var launch = App.Services.GetRequiredService<LaunchService>();
        var res = await launch.InstallAsync(SelectedGame.Game);
        StatusMessage = res.Success ? $"Opened installer for {SelectedGame.Name}" : $"Install failed: {res.Error}";
    }

    // Port of Electron `games:openInClient` — opens the platform client
    // focused on this title without launching it.
    [RelayCommand]
    private async Task OpenInClient()
    {
        if (SelectedGame is null) return;
        StatusMessage = $"Opening {SelectedGame.Platform} client…";
        var launch = App.Services.GetRequiredService<LaunchService>();
        var res = await launch.OpenInClientAsync(SelectedGame.Game);
        StatusMessage = res.Success ? null : $"Could not open client: {res.Error}";
    }

    [RelayCommand]
    private void DeleteGame(string id)
    {
        if (SelectedGame?.Id == id)
        {
            SelectedGame = null;
            ShowFocus = false;
        }
        _games.Delete(id);
    }

    [RelayCommand]
    private void TogglePlatformFilter(string platform)
    {
        if (ActivePlatformFilters.Contains(platform))
            ActivePlatformFilters.Remove(platform);
        else
            ActivePlatformFilters.Add(platform);
        PersistFilters();
        Refresh();
    }

    [RelayCommand]
    private void ToggleCategoryFilter(string category)
    {
        if (ActiveCategoryFilters.Contains(category))
            ActiveCategoryFilters.Remove(category);
        else
            ActiveCategoryFilters.Add(category);
        PersistFilters();
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
        HideSteamSoftware = true;
        QuickFilter = "all";
        PersistFilters();
        PersistDefaultTab();
        Refresh();
    }

    [RelayCommand]
    private void SetSort(string sort) => SortOrder = sort;

    [RelayCommand]
    private void SetQuickFilter(string filter) => QuickFilter = filter;

    [RelayCommand]
    private async Task RefreshGameInfo()
    {
        if (SelectedGame is null || IsRefreshingGameInfo) return;
        IsRefreshingGameInfo = true;
        var id = SelectedGame.Id;
        var name = SelectedGame.Name;
        StatusMessage = $"Fetching info for {name}...";
        try
        {
            var meta = App.Services.GetRequiredService<MetadataService>();
            var changed = await meta.FetchForGameAsync(SelectedGame.Game, force: true);
            _covers.EnqueueGame(id);
            Refresh();
            // Re-resolve selection so the FocusPanel picks up the refreshed Game fields.
            SelectedGame = VisibleGames.FirstOrDefault(c => c.Id == id) ?? SelectedGame;
            StatusMessage = changed
                ? $"Updated info for {name}"
                : $"No new info found for {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsRefreshingGameInfo = false;
        }
    }

    [RelayCommand]
    private void OpenSelectedGameWebsite()
    {
        var url = SelectedGame?.Website;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open website: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task FetchAllMetadata()
    {
        SetProgress("Syncing metadata…", 0, done: false);
        try
        {
            var meta = App.Services.GetRequiredService<MetadataService>();
            void OnProgress(object? sender, MetadataProgressArgs e)
            {
                var pct = e.Total > 0 ? (double)e.Completed / e.Total * 0.5 : 0; // metadata = first 50%
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    SetProgress(
                        e.Done
                            ? $"Synced · {e.Updated} updated"
                            : $"Syncing · {e.Completed}/{e.Total}",
                        e.Done ? 1.0 : pct,
                        e.Done));
            }
            meta.ProgressChanged += OnProgress;
            try
            {
                var (updated, total) = await meta.FetchAllAsync();
                foreach (var card in VisibleGames) card.Refresh();
                Refresh();
                _covers.EnqueueAll();
                // Cover queue now drives the pill via OnCoverProgress.
            }
            finally { meta.ProgressChanged -= OnProgress; }
        }
        catch (Exception ex)
        {
            SetProgress($"Metadata failed: {ex.Message}", 0, done: true);
        }
    }

    public string ViewModeToggleLabel => ViewMode == "cards" ? "Orbit" : "Cards";
    partial void OnViewModeChanged(string value) => OnPropertyChanged(nameof(ViewModeToggleLabel));

    /// <summary>Coerces persisted / UI strings to <c>orbit</c> or <c>cards</c> (default <c>orbit</c>).</summary>
    public static string NormalizeViewMode(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "orbit";
        return v.Trim().ToLowerInvariant() switch
        {
            "cards" => "cards",
            "orbit" => "orbit",
            _ => "orbit",
        };
    }

    [RelayCommand]
    private void ToggleViewMode() => ViewMode = ViewMode == "cards" ? "orbit" : "cards";

    // Electron's view-toggle calls switchView(mode) with the explicit target
    // so clicking the already-active button is a no-op. Mirror that.
    [RelayCommand]
    private void SetViewMode(string mode)
    {
        var n = NormalizeViewMode(mode);
        if (n != ViewMode) ViewMode = n;
    }

    [RelayCommand] private void OpenSettings()  { ShowSettings = true;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseSettings() { ShowSettings = false; OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenDetect()    { ShowDetect  = true;   OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseDetect()   { ShowDetect  = false;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenChiaki()    { ShowChiaki  = true;   OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseChiaki()   { ShowChiaki  = false;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenXcloud()    { ShowXcloud  = true;   OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseXcloud()   { ShowXcloud  = false;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void OpenPlatforms() { ShowPlatforms = true;  OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void ClosePlatforms(){ ShowPlatforms = false; OnPropertyChanged(nameof(AnyPanelOpen)); }
    [RelayCommand] private void CloseFocus()    { ShowFocus = false; SelectedGame = null; }

    [RelayCommand]
    private void OpenLauncherTab()
    {
        ShowSettings = ShowDetect = ShowChiaki = ShowXcloud = ShowPlatforms = false;
        ShowFocus = false;
        OnPropertyChanged(nameof(AnyPanelOpen));
    }

    [RelayCommand]
    private void SwitchToPanelTab(string? tabId)
    {
        if (string.IsNullOrWhiteSpace(tabId)) return;
        ShowSettings = tabId == "settings";
        ShowDetect = tabId == "detect";
        ShowPlatforms = tabId == "platforms";
        ShowChiaki = tabId == "chiaki";
        ShowXcloud = tabId == "xcloud";
        OnPropertyChanged(nameof(AnyPanelOpen));
    }

    [RelayCommand]
    private void ClosePanelTab(string? tabId)
    {
        if (string.IsNullOrWhiteSpace(tabId)) return;
        switch (tabId)
        {
            case "settings": ShowSettings = false; break;
            case "detect": ShowDetect = false; break;
            case "platforms": ShowPlatforms = false; break;
            case "chiaki": ShowChiaki = false; break;
            case "xcloud": ShowXcloud = false; break;
        }
        OnPropertyChanged(nameof(AnyPanelOpen));
    }

    [RelayCommand]
    private void CloseAllPanels()
    {
        ShowSettings = ShowDetect = ShowChiaki = ShowXcloud = ShowPlatforms = false;
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
        if (ShowPlatforms){ ShowPlatforms = false; OnPropertyChanged(nameof(AnyPanelOpen)); return; }
    }

    [RelayCommand]
    private void CloseStreamTab(string gameId)
    {
        _chiaki.StopStream(gameId);
        _xcloud.StopSession(gameId);
        var tab = StreamTabs.FirstOrDefault(t => t.GameId == gameId);
        if (tab is not null) StreamTabs.Remove(tab);
    }

    /// <summary>Click a stream tab in the title bar to return to its panel.</summary>
    [RelayCommand]
    private void SwitchToStreamTab(StreamTabViewModel? tab)
    {
        if (tab is null) return;
        // Close any other open overlay panels so only the stream re-opens.
        ShowSettings = ShowDetect = ShowPlatforms = false;
        if (tab.Platform is "psn" or "psremote")
        {
            ShowXcloud = false;
            ShowChiaki = true;
        }
        else if (tab.Platform == "xbox")
        {
            ShowChiaki = false;
            ShowXcloud = true;
        }
        OnPropertyChanged(nameof(AnyPanelOpen));
    }

    [RelayCommand]
    private void OpenGuiStream()
    {
        if (GuiStreamTab is not null)
            SwitchToStreamTab(GuiStreamTab);
    }

    private int _coverInitialRemaining;
    private void OnCoverProgress(object? sender, CoverProgressArgs e)
    {
        if (e.Downloaded > 0)
            foreach (var card in VisibleGames) card.Refresh();

        if (e.Done)
        {
            _coverInitialRemaining = 0;
            SetProgress("Artwork downloaded", 1.0, done: true);
            StatusMessage = null;
            return;
        }

        // Capture the initial total so we can show a proper percent bar.
        if (e.Remaining > _coverInitialRemaining) _coverInitialRemaining = e.Remaining;
        var pct = _coverInitialRemaining > 0
            ? 1.0 - (double)e.Remaining / _coverInitialRemaining
            : 0;
        SetProgress($"Syncing artwork · {e.Remaining} left", pct, done: false);
        StatusMessage = null;
    }

    private void OnChiakiEvent(object? sender, ChiakiEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleChiakiEvent(e));
    }

    /// <summary>Rich Presence for cloud/PS streams; cleared on <c>disconnected</c> in the handlers below.</summary>
    private void TrySetDiscordPresenceForStream(string gameId)
    {
        if (!_settings.Get().DiscordPresence) return;
        try
        {
            var discord = App.Services.GetRequiredService<DiscordService>();
            if (!discord.IsConnected || !discord.IsReady) return;
            var g = _games.GetAll().FirstOrDefault(x => x.Id == gameId);
            if (g is null) return;
            discord.SetPresence(g.Name, g.Platform ?? "custom", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch { /* best-effort */ }
    }

    private void HandleChiakiEvent(ChiakiEventArgs e)
    {
        var tab = StreamTabs.FirstOrDefault(t => t.GameId == e.GameId);

        if (e.Type == "state" && e.Data.TryGetValue("state", out var state))
        {
            var stateStr = state?.ToString() ?? "";
            if (stateStr is "launching" or "connecting" or "streaming" or "gui")
            {
                if (tab is null)
                {
                    var game = _games.GetAll().Find(g => g.Id == e.GameId);
                    tab = new StreamTabViewModel(e.GameId, game?.Name ?? e.GameId, "psn") { State = stateStr };
                    StreamTabs.Add(tab);
                }
                else
                {
                    tab.State = stateStr;
                }

                if (stateStr == "streaming")
                {
                    if (e.Data.TryGetValue("resolution", out var res))
                        tab.Resolution = res?.ToString();
                    TrySetDiscordPresenceForStream(e.GameId);
                }

                OnPropertyChanged(nameof(ActiveStreamTab));
                OnPropertyChanged(nameof(GuiStreamTab));
                OnPropertyChanged(nameof(ShowGuiStreamFloat));
                OnPropertyChanged(nameof(IsStreaming));
                OnPropertyChanged(nameof(IsStreamConnecting));
            }
            else if (stateStr == "disconnected" && tab is not null)
            {
                StreamTabs.Remove(tab);
                try { App.Services.GetRequiredService<Services.Integrations.DiscordService>().ClearPresence(); }
                catch { /* best-effort */ }
            }
        }
        else if (e.Type == "quality" && tab is not null)
        {
            if (e.Data.TryGetValue("bitrate", out var b) && b is double bd) tab.BitrateMbps = bd;
            if (e.Data.TryGetValue("fpsActual", out var f) && f is double fd) tab.Fps = fd;
            if (e.Data.TryGetValue("latencyMs", out var l) && l is double ld) tab.LatencyMs = ld;
        }
        else if (e.Type == "title_change")
        {
            // ChiakiService emits { titleId, titleName, gameId, gameName }.
            // The service may have swapped the active psn game underneath us; if so,
            // re-key the tab so later state/quality events route to the right row.
            var newGameId = e.Data.TryGetValue("gameId", out var gi) ? gi?.ToString() : null;
            var detected  = e.Data.TryGetValue("gameName", out var gn) ? gn?.ToString()
                          : e.Data.TryGetValue("titleName", out var tn) ? tn?.ToString()
                          : null;

            if (tab is not null)
            {
                tab.DetectedTitle = detected;
                if (!string.IsNullOrEmpty(newGameId) && newGameId != tab.GameId)
                {
                    tab.GameId = newGameId!;
                    tab.Title  = detected ?? tab.Title;
                }
            }

            // If the Chiaki-ng service just auto-created a new psn game, refresh so
            // the library sees it (matches the Electron version's `games:refresh`).
            Refresh();
            UpdateContinueBanner();

            // And make sure Discord presence shows the new title for non-URI launches.
            if (!string.IsNullOrEmpty(newGameId))
            {
                var g = _games.GetAll().FirstOrDefault(x => x.Id == newGameId);
                if (g is not null)
                {
                    try
                    {
                        var discord = App.Services.GetRequiredService<Services.Integrations.DiscordService>();
                        discord.SetPresence(g.Name, g.Platform ?? "psn",
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    }
                    catch { /* best-effort */ }
                }
            }

            var toastTitle = !string.IsNullOrWhiteSpace(detected)
                ? detected
                : (!string.IsNullOrEmpty(newGameId)
                    ? _games.GetAll().FirstOrDefault(x => x.Id == newGameId)?.Name
                    : null);
            if (!string.IsNullOrWhiteSpace(toastTitle))
                ShowToast($"Now playing: {toastTitle}");
        }

        NotifyStreamEmbeddingProps();
    }

    private void OnXcloudEvent(object? sender, XcloudEventArgs e)
    {
        var tab = StreamTabs.FirstOrDefault(t => t.GameId == e.GameId);
        if (e.Type == "state")
        {
            e.Data.TryGetValue("state", out var stateObj);
            var stateStr = stateObj?.ToString() ?? "connecting";

            if (tab is null)
            {
                var game = _games.GetAll().FirstOrDefault(g => g.Id == e.GameId);
                var title = game?.Name ?? e.GameId;
                tab = new StreamTabViewModel(e.GameId, title, "xbox") { State = stateStr };
                StreamTabs.Add(tab);
            }
            else
            {
                tab.State = stateStr;
            }

            if (stateStr == "streaming")
                TrySetDiscordPresenceForStream(e.GameId);

            OnPropertyChanged(nameof(ActiveStreamTab));
            OnPropertyChanged(nameof(GuiStreamTab));
            OnPropertyChanged(nameof(ShowGuiStreamFloat));
            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsStreamConnecting));
        }
        else if (e.Type == "disconnected" && tab is not null)
        {
            StreamTabs.Remove(tab);
            try { App.Services.GetRequiredService<Services.Integrations.DiscordService>().ClearPresence(); }
            catch { /* best-effort */ }
        }

        NotifyStreamEmbeddingProps();
    }

    private void NotifyTabsChanged()
    {
        OnPropertyChanged(nameof(AnyPanelOpen));
        OnPropertyChanged(nameof(PanelTabs));
    }
}

public class ToastViewModel
{
    public string Message { get; }
    public ToastViewModel(string message) => Message = message;
}

public partial class StreamTabViewModel : ObservableObject
{
    // Mutable so the Chiaki title_change handler can re-point a PSN tab to the
    // auto-created game it discovered underneath us (matches Electron behaviour).
    [ObservableProperty] private string _gameId;
    public string Platform { get; }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _state = "connecting";
    [ObservableProperty] private double? _bitrateMbps;
    [ObservableProperty] private double? _fps;
    [ObservableProperty] private double? _latencyMs;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _detectedTitle;

    public string DisplayTitle => !string.IsNullOrEmpty(DetectedTitle) ? DetectedTitle : Title;

    public string PlatformLabel => Platform switch
    {
        "psn" or "psremote" => "PS Remote Play",
        "xbox" => "Xbox Cloud Gaming",
        _ => Platform,
    };

    public bool HasQualityStats =>
        BitrateMbps.HasValue || Fps.HasValue || LatencyMs.HasValue;

    public string BitrateLabel => BitrateMbps is double b ? b.ToString("F1") : "—";
    public string FpsLabel => Fps is double f ? ((int)Math.Round(f)).ToString() : "—";
    public string LatencyLabel => LatencyMs is double l ? ((int)Math.Round(l)).ToString() : "—";

    public StreamTabViewModel(string gameId, string title, string platform)
    {
        _gameId = gameId;
        _title = title;
        Platform = platform;
    }

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));
    partial void OnDetectedTitleChanged(string? value) => OnPropertyChanged(nameof(DisplayTitle));

    partial void OnBitrateMbpsChanged(double? value)
    {
        OnPropertyChanged(nameof(BitrateLabel));
        OnPropertyChanged(nameof(HasQualityStats));
    }
    partial void OnFpsChanged(double? value)
    {
        OnPropertyChanged(nameof(FpsLabel));
        OnPropertyChanged(nameof(HasQualityStats));
    }
    partial void OnLatencyMsChanged(double? value)
    {
        OnPropertyChanged(nameof(LatencyLabel));
        OnPropertyChanged(nameof(HasQualityStats));
    }
}

public sealed class PanelTabViewModel
{
    public string Id { get; }
    public string Title { get; }

    public PanelTabViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }
}
