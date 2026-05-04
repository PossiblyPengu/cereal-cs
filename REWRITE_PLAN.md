# Cereal — Greenfield Rewrite Plan

**Created:** 2026-05-02  
**Scope:** Full greenfield rewrite. Same feature set; new architecture, new structure, better everything.

---

## 1. Why rewrite?

| Problem                                                            | Current                     | Target                                           |
| ------------------------------------------------------------------ | --------------------------- | ------------------------------------------------ |
| `MainViewModel` is 600+ lines mixing 8 concerns                    | God object                  | Decomposed, focused VMs                          |
| `App.Services.GetRequiredService<T>()` in view constructors        | Static service locator      | Full constructor injection                       |
| All services directly mutate `_db.Db.*`                            | No single point of mutation | `IGameRepository` as sole write path             |
| `games.json` — whole-document saves, fragile migrations            | JSON blob                   | SQLite with proper schema                        |
| OAuth secrets hardcoded in `AuthService.Cfg`                       | Security risk               | Loaded from config / env                         |
| `GameCardViewModel.Refresh()` fires ~30 property changes           | Wasteful                    | `OnPropertyChanged("")` or targeted minimal set  |
| Token fields on `Database` model, risk of accidental serialization | Leak risk                   | Separate credential model, never in DB graph     |
| `AllCategories` cache never invalidated                            | Stale UI                    | `WeakReferenceMessenger` invalidation            |
| Chiaki HWND embedded via Win32 on a timer                          | Fragile                     | Proper lifecycle with compositor-aware placement |
| No tests (Phase 10 QA matrix entirely unticked)                    | Untested                    | Testable architecture from day one               |
| Visual design not as polished as intended                          | Rough edges                 | Redesigned UI tokens, consistent spacing         |

---

## 2. Technology stack (deltas only — keep what works)

| Concern            | Current                                                                   | New                                                        | Reason                                                             |
| ------------------ | ------------------------------------------------------------------------- | ---------------------------------------------------------- | ------------------------------------------------------------------ |
| Persistence        | `games.json` (System.Text.Json)                                           | **SQLite via Microsoft.Data.Sqlite + Dapper**              | Atomic writes, no full-doc saves, real migrations, indexed queries |
| Cross-VM messaging | Direct event subscriptions                                                | **`WeakReferenceMessenger`** (CommunityToolkit.Mvvm)       | No manual subscribe/unsubscribe, no memory leaks                   |
| View navigation    | `_openPanels` HashSet on MainVM                                           | **`PanelRouter` service + `NavigationMessage`**            | Decoupled, testable                                                |
| Image cache        | Unbounded `ConcurrentDictionary<string, Bitmap>` on `CoverThumbConverter` | **LRU cache (fixed-size, `Bitmap.Dispose()` on eviction)** | Bounded memory use                                                 |
| Logging            | Serilog                                                                   | Serilog (keep)                                             | No change needed                                                   |
| DI                 | `Microsoft.Extensions.DependencyInjection`                                | Keep, but **keyed services** (net8) where useful           | No change to framework                                             |
| Config             | Hardcoded in source                                                       | **`appsettings.json` + env var override**                  | No secrets in source                                               |

Everything else (Avalonia 11, CommunityToolkit.Mvvm, Velopack, WebView2, DiscordRichPresence, Sqlite, DPAPI) stays.

---

## 3. New solution structure

```
Cereal.sln
├── Cereal.Core/                        ← NEW: pure business logic, zero UI deps
│   ├── Models/
│   │   ├── Game.cs                     (immutable record, no Avalonia refs)
│   │   ├── Settings.cs
│   │   ├── AccountInfo.cs
│   │   ├── Category.cs
│   │   ├── PlaytimeEntry.cs
│   │   └── AppTheme.cs
│   ├── Repositories/
│   │   ├── IGameRepository.cs          (CRUD + query interface)
│   │   ├── ISettingsRepository.cs
│   │   ├── IPlaytimeRepository.cs
│   │   ├── ICategoryRepository.cs
│   │   └── IAccountRepository.cs
│   ├── Services/                       (interfaces only in Core)
│   │   ├── ILaunchService.cs
│   │   ├── IUpdateService.cs
│   │   ├── ICoverService.cs
│   │   ├── IMetadataService.cs
│   │   ├── IAuthService.cs
│   │   ├── IProviderImportService.cs
│   │   └── ...
│   ├── Providers/
│   │   └── IProvider.cs, IImportProvider.cs
│   └── Messaging/
│       ├── LibraryMessages.cs          (GameAdded, GameUpdated, GameRemoved)
│       ├── NavigationMessages.cs       (NavigateToPanel, ClosePanel)
│       ├── SessionMessages.cs          (StreamStarted, StreamEnded)
│       └── SettingsMessages.cs         (SettingChanged<T>)
│
├── Cereal.Infrastructure/              ← NEW: SQLite repos, HTTP clients, OS integrations
│   ├── Database/
│   │   ├── CerealDb.cs                 (connection factory, migrations)
│   │   ├── Schema.sql                  (canonical DDL, versioned)
│   │   └── Migrations/
│   │       ├── M001_Initial.cs
│   │       └── M002_AddPlaytimeIndex.cs
│   ├── Repositories/
│   │   ├── GameRepository.cs
│   │   ├── SettingsRepository.cs
│   │   ├── PlaytimeRepository.cs
│   │   ├── CategoryRepository.cs
│   │   └── AccountRepository.cs
│   ├── Services/
│   │   ├── AuthService.cs
│   │   ├── CoverService.cs
│   │   ├── LaunchService.cs
│   │   ├── MetadataService.cs
│   │   ├── UpdateService.cs
│   │   ├── PlaytimeSyncService.cs
│   │   └── ...
│   ├── Providers/
│   │   ├── SteamProvider.cs
│   │   ├── EpicProvider.cs
│   │   ├── GogProvider.cs
│   │   ├── BattleNetProvider.cs
│   │   ├── EaProvider.cs
│   │   ├── UbisoftProvider.cs
│   │   ├── ItchioProvider.cs
│   │   └── XboxProvider.cs
│   ├── Integrations/
│   │   ├── ChiakiService.cs
│   │   ├── DiscordService.cs
│   │   ├── SmtcService.cs
│   │   └── XcloudService.cs
│   └── Config/
│       ├── AppConfig.cs                (loaded from appsettings.json)
│       └── appsettings.json            (defaults; gitignored copy with secrets)
│
└── Cereal.App/                         ← Avalonia UI layer only
    ├── Program.cs
    ├── App.axaml / App.axaml.cs        (DI wiring, NO static App.Services)
    ├── Shell/
    │   ├── MainWindow.axaml/.cs        (window chrome only)
    │   └── ShellViewModel.cs           (window state: title bar, tray, close/min)
    ├── ViewModels/
    │   ├── Library/
    │   │   ├── LibraryViewModel.cs     (game collection, filter/sort pipeline)
    │   │   ├── GameCardViewModel.cs    (single card display)
    │   │   └── CardLayoutViewModel.cs  (virtual row layout)
    │   ├── Navigation/
    │   │   ├── PanelRouterViewModel.cs (open/close/activate tabs)
    │   │   └── NavViewModel.cs         (nav pill state)
    │   ├── Game/
    │   │   └── FocusPanelViewModel.cs  (game detail / metadata / edit)
    │   ├── Stream/
    │   │   └── StreamSessionViewModel.cs (Chiaki + xCloud sessions, stats)
    │   ├── Media/
    │   │   └── MediaViewModel.cs       (SMTC — already clean, keep)
    │   ├── Detect/
    │   │   └── DetectViewModel.cs
    │   ├── Platforms/
    │   │   └── PlatformsPanelViewModel.cs
    │   ├── Settings/
    │   │   ├── SettingsViewModel.cs    (coordinator)
    │   │   ├── AppearanceViewModel.cs
    │   │   ├── LibrarySettingsViewModel.cs
    │   │   └── AboutViewModel.cs
    │   └── Wizard/
    │       └── StartupWizardViewModel.cs
    ├── Views/                          (AXAML — same layout, redesigned tokens)
    ├── Controls/
    │   ├── Orbit/                      (keep, minor cleanup)
    │   └── WebView2Host.cs             (keep, better lifecycle)
    ├── Theme/                          (redesigned)
    ├── Utilities/
    │   ├── Converters.cs
    │   └── ImageCache.cs               (LRU, bounded, Bitmap.Dispose on eviction)
    └── DependencyInjection/
        └── ServiceCollectionExtensions.cs  (all registrations in one place)
```

---

## 4. ViewModel decomposition detail

### Current: `MainViewModel` (~600+ lines, 8 concerns)

| Concern extracted                                                   | New home                                                |
| ------------------------------------------------------------------- | ------------------------------------------------------- |
| Library state, filter/sort pipeline                                 | `LibraryViewModel`                                      |
| Panel tab management (`_openPanels` HashSet, active tab)            | `PanelRouterViewModel`                                  |
| Toolbar position + derived geometry (~15 `OnPropertyChanged` calls) | `ShellViewModel` + AXAML value converters               |
| Stream session state (Chiaki/xCloud events, stats)                  | `StreamSessionViewModel`                                |
| Gamepad dispatch                                                    | `GamepadDispatcher` (plain class, not VM)               |
| Cover download callbacks                                            | `CoverService` events → `WeakReferenceMessenger`        |
| Media (SMTC) state                                                  | `MediaViewModel` (already separate, wire via messenger) |
| Search overlay state                                                | `SearchViewModel` (small, owns the Ctrl+K overlay)      |

### `LibraryViewModel` responsibilities

- Owns `ObservableCollection<GameCardViewModel> DisplayedCards`
- Subscribes to `GameAdded / GameUpdated / GameRemoved` messages
- Filter pipeline: `ImmutableList<Game>` source → filter predicates → sort → group into `CardLayoutRows`
- Exposes `FilteredCount`, `TotalCount`, platform chip states
- `LoadNextPageCommand` (scroll-triggered virtualization)

### `PanelRouterViewModel` responsibilities

- `ObservableCollection<PanelTabViewModel> Tabs`
- `PanelTabViewModel? ActiveTab`
- `OpenPanelCommand(string panelId)` — idempotent
- `ClosePanelCommand(PanelTabViewModel tab)`
- Receives `NavigateToPanel` messages from anywhere in the app

---

## 5. Database schema (SQLite)

```sql
-- M001_Initial.sql
CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Games (
    Id              TEXT PRIMARY KEY,  -- ULID
    Name            TEXT NOT NULL,
    Platform        TEXT NOT NULL,
    SortName        TEXT,
    ExePath         TEXT,
    CoverPath       TEXT,
    HeaderPath      TEXT,
    -- Platform-specific IDs stored as JSON column (flexible, indexed via json_extract)
    PlatformIds     TEXT,              -- JSON object
    -- Metadata
    Description     TEXT,
    Developer       TEXT,
    Publisher       TEXT,
    ReleaseYear     INTEGER,
    Metacritic      INTEGER,
    Website         TEXT,
    Tags            TEXT,              -- JSON array
    -- Flags
    IsFavorite      INTEGER NOT NULL DEFAULT 0,
    IsHidden        INTEGER NOT NULL DEFAULT 0,
    IsSoftware      INTEGER NOT NULL DEFAULT 0,
    -- Cover state
    CoverSource     TEXT,              -- 'sgdb' | 'local' | 'custom'
    ImgStamp        INTEGER,           -- cache-bust timestamp
    -- Timestamps
    AddedAt         TEXT NOT NULL,     -- ISO-8601
    LastPlayedAt    TEXT,
    UpdatedAt       TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_games_platform ON Games(Platform);
CREATE INDEX IF NOT EXISTS idx_games_favorite ON Games(IsFavorite);
CREATE INDEX IF NOT EXISTS idx_games_hidden   ON Games(IsHidden);
CREATE INDEX IF NOT EXISTS idx_games_name     ON Games(Name COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS Categories (
    Id   TEXT PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS GameCategories (
    GameId     TEXT NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
    CategoryId TEXT NOT NULL REFERENCES Categories(Id) ON DELETE CASCADE,
    PRIMARY KEY (GameId, CategoryId)
);

CREATE TABLE IF NOT EXISTS Playtime (
    GameId      TEXT NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
    Source      TEXT NOT NULL,         -- 'steam' | 'local' | 'manual'
    MinutesTotal INTEGER NOT NULL DEFAULT 0,
    LastSynced  TEXT,
    PRIMARY KEY (GameId, Source)
);

-- Schema version tracking
CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version   INTEGER PRIMARY KEY,
    AppliedAt TEXT NOT NULL
);
INSERT OR IGNORE INTO SchemaVersion VALUES (1, datetime('now'));
```

**Migration system:** Each `IMigration : { int Version; void Apply(IDbConnection db); }` is discovered by DI, sorted, and applied in `CerealDb.EnsureMigrated()` on startup. No manual version constants scattered across code.

**Key benefits over JSON blob:**

- Atomic row-level writes (no full-doc serialization)
- `GameRepository.Save(Game)` does an `INSERT OR REPLACE` — always safe
- Query-time filtering happens in SQL, not in-memory LINQ on every render
- Foreign keys enforce category/playtime consistency

---

## 6. Persistence layer pattern

```csharp
// Core interface — lives in Cereal.Core
public interface IGameRepository
{
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken ct = default);
    Task<Game?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Game?> FindByPlatformIdAsync(string platform, string platformId, CancellationToken ct = default);
    Task<IReadOnlyList<Game>> SearchAsync(string query, CancellationToken ct = default);
    Task SaveAsync(Game game, CancellationToken ct = default);           // INSERT OR REPLACE
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<int> CountAsync(string? platform = null, CancellationToken ct = default);
}

// Infrastructure implementation — uses Dapper
internal sealed class GameRepository(CerealDb db, IMessenger messenger) : IGameRepository
{
    public async Task SaveAsync(Game game, CancellationToken ct = default)
    {
        using var conn = db.Open();
        await conn.ExecuteAsync(Sql.UpsertGame, GameMapper.ToRow(game));
        messenger.Send(new GameUpdatedMessage(game));   // single notification point
    }
    // ...
}
```

**Rule:** `IGameRepository` is the **only** way any service writes a `Game`. `DatabaseService` (the old pattern) is gone. Services receive `IGameRepository` by injection.

---

## 7. Configuration (no hardcoded secrets)

```jsonc
// Cereal.Infrastructure/Config/appsettings.json  (committed — public defaults only)
{
  "OAuth": {
    "GogClientId": "46899977096215655",
    "GogClientSecret": "", // empty — must come from env or local override
    "EpicClientId": "34a02cf8f4414e29b15921876da36f9a",
    "EpicClientSecret": "",
  },
  "Discord": {
    "ApplicationId": "1234567890",
  },
  "OAuthCallbackPort": 0, // 0 = pick random available port
}
```

```jsonc
// appsettings.local.json  (.gitignored — developer machine secrets)
{
  "OAuth": {
    "GogClientSecret": "...",
    "EpicClientSecret": "...",
  },
}
```

`AppConfig` is loaded in DI via `IConfiguration` with:

1. `appsettings.json` (defaults, committed)
2. `appsettings.local.json` (local override, gitignored)
3. Environment variables (`CEREAL_OAUTH__GOGCLIENTSECRET`, etc.)

OAuth callback port is randomised (`OAuthCallbackPort = 0` → `TcpListener` on port 0 → OS assigns).

---

## 8. Messaging (WeakReferenceMessenger)

Replace all direct `event` subscriptions with messages. VMs and services register recipients; messenger holds weak refs so nothing leaks if a VM is GC'd.

```csharp
// Cereal.Core/Messaging/LibraryMessages.cs
public record GameAddedMessage(Game Game);
public record GameUpdatedMessage(Game Game);
public record GameRemovedMessage(string GameId);
public record LibraryRefreshedMessage(int Count);

// Cereal.Core/Messaging/NavigationMessages.cs
public record NavigateToPanelMessage(string PanelId, object? Parameter = null);
public record ClosePanelMessage(string PanelId);
public record FocusGameMessage(string GameId);

// Cereal.Core/Messaging/SessionMessages.cs
public record StreamStartedMessage(string SessionId, string Platform, string GameName);
public record StreamEndedMessage(string SessionId);
public record StreamStatsUpdatedMessage(string SessionId, StreamStats Stats);

// Cereal.Core/Messaging/SettingsMessages.cs
public record SettingChangedMessage<T>(string Key, T NewValue);
```

**Usage pattern:**

```csharp
// Sender (GameRepository)
_messenger.Send(new GameUpdatedMessage(game));

// Receiver (LibraryViewModel)
public LibraryViewModel(IMessenger messenger, ...)
{
    messenger.Register<GameUpdatedMessage>(this, (r, msg) => r.OnGameUpdated(msg.Game));
}
```

---

## 9. Image cache (bounded LRU)

```csharp
// Cereal.App/Utilities/ImageCache.cs
public sealed class ImageCache : IDisposable
{
    private const int DefaultCapacity = 200;
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (Bitmap Bmp, LinkedListNode<string> Node)> _map = new();
    private readonly int _capacity;
    private readonly Lock _lock = new();

    public bool TryGet(string path, [NotNullWhen(true)] out Bitmap? bmp) { ... }

    public void Put(string path, Bitmap bmp)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(path, out var existing))
            {
                _order.Remove(existing.Node);
                existing.Bmp.Dispose();
                _map.Remove(path);
            }
            if (_map.Count >= _capacity)
                Evict();
            var node = _order.AddFirst(path);
            _map[path] = (bmp, node);
        }
    }

    private void Evict()
    {
        var last = _order.Last!.Value;
        _map[last].Bmp.Dispose();
        _map.Remove(last);
        _order.RemoveLast();
    }

    public void Dispose() { /* dispose all cached bitmaps */ }
}
```

Registered as `services.AddSingleton<ImageCache>()`. `CoverThumbConverter` accepts `ImageCache` by constructor injection (not static field).

---

## 10. DI wiring (no static App.Services)

```csharp
// Cereal.App/DependencyInjection/ServiceCollectionExtensions.cs
public static class ServiceRegistration
{
    public static IServiceCollection AddCerealCore(this IServiceCollection s, IConfiguration config)
    {
        // Infrastructure
        s.AddSingleton<CerealDb>();
        s.AddSingleton<IGameRepository, GameRepository>();
        s.AddSingleton<ISettingsRepository, SettingsRepository>();
        s.AddSingleton<IPlaytimeRepository, PlaytimeRepository>();
        s.AddSingleton<ICategoryRepository, CategoryRepository>();
        s.AddSingleton<IAccountRepository, AccountRepository>();

        // Messenger
        s.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Services
        s.AddSingleton<IGameService, GameService>();
        s.AddSingleton<ILaunchService, LaunchService>();
        s.AddSingleton<IAuthService, AuthService>();
        s.AddSingleton<ICoverService, CoverService>();
        s.AddSingleton<IMetadataService, MetadataService>();
        s.AddSingleton<IUpdateService, UpdateService>();
        s.AddSingleton<PlaytimeSyncService>();

        // Platform providers
        s.AddSingleton<IProvider, SteamProvider>();
        s.AddSingleton<IProvider, EpicProvider>();
        s.AddSingleton<IProvider, GogProvider>();
        // ... etc

        // Integrations (lazy: only instantiated when first resolved)
        s.AddSingleton<IDiscordService, DiscordService>();
        s.AddSingleton<IChiakiService, ChiakiService>();
        s.AddSingleton<IXcloudService, XcloudService>();
        s.AddSingleton<ISmtcService, SmtcService>();

        // App utilities
        s.AddSingleton<ImageCache>();
        s.AddSingleton<PathService>();
        s.AddSingleton<SingleInstanceGuard>();

        // ViewModels (transient where multiple instances ok; singleton for shell)
        s.AddSingleton<ShellViewModel>();
        s.AddSingleton<LibraryViewModel>();
        s.AddSingleton<PanelRouterViewModel>();
        s.AddSingleton<StreamSessionViewModel>();
        s.AddSingleton<MediaViewModel>();
        s.AddSingleton<SearchViewModel>();
        s.AddTransient<SettingsViewModel>();
        s.AddTransient<DetectViewModel>();
        s.AddTransient<PlatformsPanelViewModel>();
        s.AddTransient<FocusPanelViewModel>();

        // Config
        s.AddSingleton(config.GetSection("OAuth").Get<OAuthConfig>()!);
        s.AddSingleton(config.GetSection("Discord").Get<DiscordConfig>()!);

        return s;
    }
}

// App.axaml.cs — views receive VMs by constructor injection
public override void OnFrameworkInitializationCompleted()
{
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.local.json", optional: true)
        .AddEnvironmentVariables("CEREAL_")
        .Build();

    var services = new ServiceCollection()
        .AddCerealCore(config)
        .BuildServiceProvider();

    // MainWindow gets its VM injected — NO App.Services static access
    var shell = services.GetRequiredService<ShellViewModel>();
    var window = new MainWindow(shell, services.GetRequiredService<LibraryViewModel>(), ...);
    ...
}
```

**Zero `App.Services` usage in view constructors.** All VMs come from constructor parameters.

---

## 11. LibraryViewModel filter pipeline

```csharp
// Fast, O(1) updates via messages + one synchronous LINQ pass
public sealed partial class LibraryViewModel : ObservableObject,
    IRecipient<GameAddedMessage>,
    IRecipient<GameUpdatedMessage>,
    IRecipient<GameRemovedMessage>
{
    private ImmutableList<Game> _allGames = ImmutableList<Game>.Empty;
    private readonly ObservableCollection<CardLayoutEntry> _rows = [];

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string? _activePlatform;
    [ObservableProperty] private SortMode _sortMode = SortMode.LastPlayed;
    [ObservableProperty] private bool _showHidden;

    // Triggered by any filter change
    partial void OnSearchQueryChanged(string _) => Refilter();
    partial void OnActivePlatformChanged(string? _) => Refilter();
    partial void OnSortModeChanged(SortMode _) => Refilter();

    private void Refilter()
    {
        // Pure function: no side effects, no UI thread requirement until SetRows
        var filtered = _allGames
            .Where(MatchesPlatform)
            .Where(MatchesSearch)
            .Where(g => _showHidden || !g.IsHidden)
            .OrderBy(GetSortKey)
            .ToList();

        Dispatcher.UIThread.Post(() => SetRows(filtered), DispatcherPriority.Background);
    }

    // IRecipient implementations keep _allGames in sync
    void IRecipient<GameAddedMessage>.Receive(GameAddedMessage msg) =>
        _allGames = _allGames.Add(msg.Game);
    void IRecipient<GameUpdatedMessage>.Receive(GameUpdatedMessage msg) =>
        _allGames = _allGames.Replace(_allGames.First(g => g.Id == msg.Game.Id), msg.Game);
    void IRecipient<GameRemovedMessage>.Receive(GameRemovedMessage msg) =>
        _allGames = _allGames.RemoveAll(g => g.Id == msg.GameId);
}
```

---

## 12. UI design tokens (new)

### Spacing scale

```
--space-1:  4px
--space-2:  8px
--space-3: 12px
--space-4: 16px
--space-5: 24px
--space-6: 32px
--space-7: 48px
--space-8: 64px
```

### Typography scale (Inter)

```
--text-xs:   11px  400  (captions, badges)
--text-sm:   13px  400  (secondary, metadata)
--text-base: 14px  400  (body)
--text-md:   15px  500  (card titles)
--text-lg:   18px  600  (panel headers)
--text-xl:   22px  700  (section headings)
--text-2xl:  28px  700  (focus panel title)
```

### Component patterns

- Cards: `CornerRadius=8`, `BoxShadow="0 2 12 0 #18000000"` + hover `"0 4 24 0 #28000000"`
- Panels: `CornerRadius=12` top edge (slides up from bottom or right)
- Buttons: `CornerRadius=6` default, `CornerRadius=20` pill (nav, chips)
- Focus panel header: full-bleed image with bottom gradient overlay, not cropped thumbnail
- Transition: `0.18s ease` for opacity/transform (down from 0.3s — more snappy)
- Consistent 16px outer padding on all panels

### Color token naming (replaces arbitrary slot numbers)

```
--color-bg-base          surface behind everything (darkest)
--color-bg-surface       card / panel surfaces
--color-bg-elevated      hover states, dropdowns
--color-bg-overlay       modal backdrop
--color-accent           primary brand accent
--color-accent-dim       accent at 40% opacity (chips, badges)
--color-accent-glow      accent at 15% opacity (hover rings, glows)
--color-text-primary     main text
--color-text-secondary   secondary / metadata text
--color-text-muted       placeholders, disabled
--color-text-on-accent   text on accent backgrounds
--color-border-subtle    hairline borders
--color-border-default   standard borders
--color-danger           destructive actions
--color-success          confirmations
--color-warning          update banners, notices
```

---

## 13. Performance targets

| Metric                                  | Current (approx) | Target                  |
| --------------------------------------- | ---------------- | ----------------------- |
| Cold startup to library visible         | ~2.5s            | < 1.2s                  |
| Filter/sort on 500 games                | ~120ms           | < 20ms                  |
| Cover thumbnail decode (per card, cold) | ~40ms blocking   | async + LRU hit = < 1ms |
| Peak working set (idle, 500 games)      | ~380MB           | < 220MB                 |
| Orbit view: 100 orbs at 60fps           | OK               | Keep                    |

**Startup optimisation tactics:**

1. `CerealDb` opens connection on first query, not at registration
2. Heavy services (Discord, SMTC, WebView2) registered as `Lazy<T>` — never init if feature disabled
3. `IGameRepository.GetAllAsync()` issued on a background thread immediately; library shown incrementally as rows arrive
4. Startup wizard check is synchronous/fast (single DB Settings query)

**Cover decode optimisation:**

- Decode occurs on a `Task.Run` thread pool thread
- Avalonia `WriteableBitmap` used for thumbnails (smaller than `Bitmap` for full cover decode)
- LRU cache (200 entries ≈ 40MB for 200×300 thumbnails) — evictions dispose the bitmap
- `CoverThumbConverter` returns a `Task<Bitmap>` via `AsyncValueConverter` pattern

---

## 14. Security fixes

| Issue                                          | Fix                                                                                                                                   |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| GOG/Epic OAuth secrets hardcoded               | `appsettings.local.json` + env vars (see §7)                                                                                          |
| Fixed OAuth callback port 7373                 | Randomised (`TcpListener` port 0)                                                                                                     |
| `AccountInfo` token fields on `Database` model | Removed from DB graph; `IAccountRepository` writes only to `CredentialService` (DPAPI). Tokens live in memory only, loaded at startup |
| Accidental token serialization risk            | Token fields only on a separate `AuthSession` class, never part of `Game` or `Settings` models                                        |

---

## 15. Implementation phases

### Phase A — New solution skeleton + persistence

**Goal:** New project structure compiles; SQLite schema + migrations work; repos return data.

- [ ] Create `Cereal.Core` project, move/rewrite models as immutable records
- [ ] Create `Cereal.Infrastructure` project
- [ ] Implement `CerealDb` (connection factory + migration runner)
- [ ] Write `M001_Initial.sql` schema
- [ ] Implement all 5 repositories (`GameRepository`, `SettingsRepository`, `PlaytimeRepository`, `CategoryRepository`, `AccountRepository`)
- [ ] Write `AppConfig` + IConfiguration wiring
- [ ] Unit test repositories (in-memory SQLite)

### Phase B — Core services + messaging

**Goal:** Services work in isolation; all cross-VM comms go through messenger.

- [ ] Define all `IMessenger` message types
- [ ] Implement `GameService` (wraps `IGameRepository`, sends messages)
- [ ] Implement `SettingsService` (wraps `ISettingsRepository`)
- [ ] Implement `AuthService` (OAuth flows, random port, secrets from config)
- [ ] Implement `LaunchService`
- [ ] Implement `CoverService` (with `ImageCache`)
- [ ] Implement `MetadataService`
- [ ] Implement `UpdateService`
- [ ] Implement `PlaytimeSyncService`

### Phase C — Shell + navigation

**Goal:** App opens with empty library; panel routing works; settings panel functional.

- [ ] New `Cereal.App` DI wiring (`ServiceCollectionExtensions`)
- [ ] `ShellViewModel` (window chrome, toolbar position, tray)
- [ ] `PanelRouterViewModel` (open/close/activate tabs via messages)
- [ ] `SearchViewModel` (Ctrl+K overlay)
- [ ] `MainWindow.axaml` redesign (new design tokens, no static `App.Services`)
- [ ] `MainView.axaml` with nav pill + panel host

### Phase D — Library

**Goal:** Games load, display in grid, filter/sort work, covers load.

- [ ] `LibraryViewModel` (filter pipeline, messages, pagination)
- [ ] `GameCardViewModel` (minimal property set, `OnPropertyChanged("")` on refresh)
- [ ] Virtual card layout (`CardLayoutViewModel`)
- [ ] Platform chips (`PlatformChipViewModel`)
- [ ] Card AXAML redesign (new tokens, hover animation)
- [ ] `ImageCache` + `CoverThumbConverter` (async + LRU)

### Phase E — Game detail + editing

**Goal:** Focus panel, add/edit game dialog functional.

- [ ] `FocusPanelViewModel`
- [ ] `FocusPanel.axaml` redesign (full-bleed header, gradient overlay)
- [ ] `AddGameDialog` (reworked, categories, art picker)
- [ ] `ArtPickerDialog` (SteamGridDB browse)

### Phase F — Providers + import

**Goal:** All platform providers detect and import games.

- [ ] Port all providers to receive `IGameRepository` by injection
- [ ] `DetectViewModel` + `DetectPanel`
- [ ] `PlatformsPanelViewModel` + `PlatformsPanel` (OAuth flows)
- [ ] `PlatformAuthPanel` (WebView2 OAuth)

### Phase G — Streaming

**Goal:** Chiaki and xCloud sessions work; stream bar shows stats.

- [ ] `ChiakiService` (improved lifecycle, proper HWND management)
- [ ] `XcloudService`
- [ ] `StreamSessionViewModel`
- [ ] Stream bar redesign
- [ ] `ChiakiPanel`, `XcloudPanel`

### Phase H — Settings

**Goal:** All settings sections functional; import/export; danger zone.

- [ ] `SettingsViewModel` decomposed into section VMs
- [ ] `SettingsPanel.axaml` redesign
- [ ] `StartupWizardViewModel` + `StartupWizardDialog`

### Phase I — Integrations + system services

**Goal:** Discord, SMTC, Gamepad, tray, startup, updates all functional.

- [ ] `DiscordService` (lazy init)
- [ ] `SmtcService` (lazy init)
- [ ] `GamepadDispatcher`
- [ ] `StartupService`
- [ ] `SingleInstanceGuard`
- [ ] `UpdateService` UI wiring (banner progress)
- [ ] Tray icon wiring

### Phase J — Orbit view + polish

**Goal:** Orbit view works; visual polish pass; all design tokens applied consistently.

- [ ] Port `OrbitWorld` + child controls to new architecture
- [ ] `OrbitView.axaml` + `OrbitViewModel`
- [ ] Visual polish pass (spacing, typography, animation durations)
- [ ] Accessibility pass (keyboard nav, focus rings)

### Phase K — QA + migration tooling

**Goal:** Import from old `games.json`; all core flows tested.

- [ ] `LegacyMigrator` — reads `games.json` v0–3, imports to new SQLite schema
- [ ] Integration test: legacy JSON → SQLite round-trip
- [ ] End-to-end smoke tests for launch, import, cover, OAuth flows
- [ ] Performance benchmarks vs. targets in §13

---

## 16. Migration from existing data

`LegacyMigrator` will be included in Phase K. It:

1. Checks for `%AppData%\Cereal\games.json` on first launch
2. Reads and validates the JSON (supports schema versions 0–3)
3. Inserts all games into SQLite via `IGameRepository.SaveAsync`
4. Moves tokens to `CredentialService` (skipping any that were accidentally serialized)
5. Renames `games.json` → `games.json.migrated` (non-destructive)
6. Sets `Settings["LegacyMigratedAt"]` in DB so it never runs again

---

## 17. Things explicitly NOT changing

| Item                                                          | Reason                                                           |
| ------------------------------------------------------------- | ---------------------------------------------------------------- |
| Avalonia version (11.3.9)                                     | Stable; no breaking change benefit                               |
| CommunityToolkit.Mvvm source generators                       | Works well; keep `[ObservableProperty]`, `[RelayCommand]`        |
| Velopack update mechanism                                     | Already integrated; no replacement needed                        |
| Serilog                                                       | No issues                                                        |
| Orbit view visual style                                       | Looks great, just needs lifecycle cleanup                        |
| Platform-specific feature guards (SMTC/WebView2 Windows-only) | Keep the guards, improve the error messaging                     |
| Chiaki subprocess model                                       | Fundamental to how chiaki-ng works; keep, but clean up lifecycle |

---

## 18. Definition of done

- [ ] All Phase A–J items checked
- [ ] App cold-starts in < 1.2s on a mid-range machine
- [ ] `games.json` from old app imports without data loss
- [ ] No `App.Services` static access in any view
- [ ] No secrets in committed source
- [ ] `ImageCache` evictions are verified (no bitmap memory leak)
- [ ] OAuth callback port randomises correctly (manual test)
- [ ] All PLAN.md features remain at **OK** status
- [ ] REWRITE_PLAN.md Phase K QA matrix ticked
