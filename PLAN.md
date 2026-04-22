# Cereal Launcher — C# Rewrite Plan

## Context

Full rewrite of `d:\CODE\cereal-launcher-vite` (Electron + React + TypeScript) into
`d:\CODE\cereal-cs` (C# + Avalonia UI + .NET 8). Target platforms: **Windows and Linux**.

**Why Avalonia, not WPF:** User explicitly requires Linux support, which rules out WPF/WinUI 3.
Avalonia is the only mature cross-platform C# desktop framework that covers both.

**Source app summary:**
- Universal game launcher aggregating Steam, Epic, GOG, Battle.net, EA, Ubisoft, itch.io, Xbox, PlayStation
- Two view modes: Orbit (3D galaxy canvas via Three.js) and Card grid
- SteamGridDB artwork, Discord Rich Presence, PlayStation via chiaki-ng, Xbox Cloud via embedded browser
- SMTC (Windows media keys) via a .NET 8 helper that already exists in the source repo
- Auto-updates via GitHub Releases (electron-updater → Velopack)
- JSON flat-file database at `%APPDATA%/Cereal/games.json`
- Secure credential store (Electron safeStorage → DPAPI on Windows, AES+machine-id on Linux)

---

## Environment

| Thing | Value |
|---|---|
| New project root | `D:\CODE\cereal-cs\` |
| Original source | `D:\CODE\cereal-launcher-vite\` |
| .NET SDK (portable, no install needed) | `D:\CODE\important files\dotnet-sdk-8.0.404-win-x64\dotnet.exe` |
| Target framework | `net8.0` |
| Avalonia version | `11.3.9` |

**To build on another machine:** copy the SDK folder or install .NET 8 SDK from https://aka.ms/dotnet/download, then `dotnet restore` and `dotnet build`.

---

## Project Structure

```
cereal-cs/
├── Cereal.sln
└── Cereal.App/
    ├── Cereal.App.csproj          ← net8.0, Avalonia 11.3.9
    ├── Program.cs                  ← entry point (Avalonia template default, needs Velopack init)
    ├── App.axaml / App.axaml.cs   ← Avalonia app bootstrap (needs DI wiring)
    ├── MainWindow.axaml / .cs     ← shell window (needs full UI build-out)
    ├── Models/
    │   ├── Game.cs                 ✅ DONE
    │   ├── Settings.cs             ✅ DONE
    │   └── Database.cs             ✅ DONE  (also: AccountInfo, ChiakiConfig, MediaInfo, ImportProgress)
    ├── Services/
    │   ├── PathService.cs          ✅ DONE  (AppData dirs, cover/header paths)
    │   ├── DatabaseService.cs      ✅ DONE  (load/save/flush/backup, debounced 150ms write)
    │   ├── GameService.cs          ✅ DONE  (CRUD, dedup, categories, playtime)
    │   ├── SettingsService.cs      ✅ DONE
    │   ├── CredentialService.cs    ✅ DONE  (DPAPI on Windows, AES/machine-id on Linux)
    │   ├── Providers/
    │   │   ├── IProvider.cs        ✅ DONE  (IProvider, IImportProvider, DetectResult, ImportResult, ImportContext)
    │   │   ├── ProviderUtils.cs    ✅ DONE  (Canonicalize, IsDlcTitle, FindExisting, MakeGameEntry)
    │   │   ├── SteamProvider.cs    ✅ DONE  (local ACF scan + XML feed + Steam API import)
    │   │   ├── EpicProvider.cs     ✅ DONE  (manifest scan + library API import)
    │   │   ├── GogProvider.cs      ✅ DONE  (goggame-*.info scan + paginated API import)
    │   │   └── LocalProviders.cs   ✅ DONE  (BattleNet, EA, Ubisoft, itch.io stub, Xbox)
    │   └── Integrations/
    │       ├── DiscordService.cs   ✅ DONE  (stubbed — needs `discord-rpc-csharp` NuGet added)
    │       ├── ChiakiService.cs    ✅ DONE  (process manager, Win32 P/Invoke embed, UDP discovery/wake, auto-reconnect)
    │       ├── XcloudService.cs    ✅ DONE  (session manager; runtime reflection-based WebView fallback implemented — replace with compile-time `Avalonia.WebView` when package/feed is available)
    │       ├── SmtcService.cs      ✅ DONE  (subprocess MediaInfoTool.exe; keybd_event P/Invoke for keys)
    │       └── CoverService.cs     ✅ DONE  (Channel queue, HTTP download, SteamGridDB search, retry logic)
    ├── ViewModels/
    │   ├── MainViewModel.cs        ❌ TODO  (game list, filters, active tab, search)
    │   ├── GameCardViewModel.cs    ❌ TODO
    │   ├── SettingsViewModel.cs    ❌ TODO
    │   └── DetectViewModel.cs      ❌ TODO
    ├── Views/
    │   ├── MainView.axaml          ❌ TODO  (card grid + orbit toggle + sidebar)
    │   ├── Panels/
    │   │   ├── SettingsPanel.axaml ❌ TODO
    │   │   ├── DetectPanel.axaml   ❌ TODO
    │   │   └── ChiakiPanel.axaml   ❌ TODO
    │   └── Dialogs/
    │       ├── AddGameDialog.axaml ❌ TODO
    │       └── ArtPickerDialog.axaml ❌ TODO
    └── Native/
        └── Win32Interop.cs         ❌ TODO  (SetParent for chiaki-ng window embedding)
```

---

## NuGet Packages (current csproj)

| Package | Version | Purpose |
|---|---|---|
| Avalonia | 11.3.9 | UI framework |
| Avalonia.Desktop | 11.3.9 | Desktop backend |
| Avalonia.Themes.Fluent | 11.3.9 | Fluent theme |
| Avalonia.Fonts.Inter | 11.3.9 | Inter font |
| Avalonia.Diagnostics | 11.3.9 | Dev tools (debug only) |
| CommunityToolkit.Mvvm | 8.3.2 | ObservableObject, RelayCommand, source gen |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI container |
| Serilog | 4.2.0 | Logging |
| Serilog.Sinks.File | 6.0.0 | Log to file |
| System.Text.Json | 8.0.5 | JSON serialization |
| Velopack | 0.0.988 (resolved: 0.0.1015) | Auto-updates via GitHub Releases |

### Added in this session

| Package | Version | Purpose |
|---|---|---|
| `WebView.Avalonia` | 11.0.0.1 | WebView control (community package). Uses WebView2 on Windows, webkit2gtk on Linux. Control: `AvaloniaWebView.WebView`, `Url` property (Uri), events: NavigationStarting/NavigationCompleted, methods: ExecuteScriptAsync/GoBack/GoForward/Reload. Init: `AvaloniaWebViewBuilder.Initialize(default)` in App.RegisterServices; `.UseDesktopWebView()` in Program.BuildAvaloniaApp. |
| `WebView.Avalonia.Desktop` | 11.0.0.1 | Desktop backend for WebView.Avalonia |

### Still need to add

| Package | Purpose | Command |
|---|---|---|
| `discord-rpc-csharp` | Discord Rich Presence (activate DiscordService stub) | `dotnet add package discord-rpc-csharp` |
| `Microsoft.Data.Sqlite` | itch.io butler.db (SQLite) | `dotnet add package Microsoft.Data.Sqlite` |
| `Avalonia.Svg` (optional) | SVG platform icons | `dotnet add package Avalonia.Svg` |

---

## What's Done

1. **Solution + project** — `Cereal.sln` + `Cereal.App.csproj` targeting net8.0 with all base NuGets restored
2. **All models** — `Game`, `Settings`, `Database`, `AccountInfo`, `ChiakiConfig`, `MediaInfo`, `ImportProgress` (Game extended with chiakiRegistKey/Morning/DisplayMode/Dualsense/Passcode, sgdbCoverUrl, storeUrl, epicAppName/Namespace/CatalogItemId, eaOfferId, ubisoftGameId)
3. **Core services** — `PathService`, `DatabaseService` (debounced writes + backup/restore), `GameService` (CRUD + dedup + categories + SetFavorite/SetHidden), `SettingsService`, `CredentialService` (DPAPI/AES cross-platform)
4. **Provider infrastructure** — `IProvider`/`IImportProvider` interfaces, `ProviderUtils` (canonicalize, dedup helpers)
5. **Platform providers** — Steam (ACF scan + XML + API), Epic (manifest scan + library API), GOG (goggame scan + paginated API), Battle.net, EA (registry), Ubisoft (registry), itch.io (stub), Xbox (directory scan)
6. **Discord service** — stubbed with commented-out discord-rpc-csharp calls, ready to activate once package added
7. **Integration services** — ChiakiService (process manager, UDP discovery/wake, Win32 P/Invoke embed, auto-reconnect), XcloudService (compile-time WebView.Avalonia — `WebView { Url }` + NavigationCompleted/Starting events, no reflection), SmtcService (subprocess + keybd_event), CoverService (Channel queue, SteamGridDB, retry)
8. **DI + startup** — App.axaml.cs wires all 9+ services, startup sequence loads DB/Discord/covers/chiaki/update-check; Program.cs does Velopack → Serilog → Avalonia
9. **ViewModels** — MainViewModel (filter/search/stream tabs), GameCardViewModel (cover/playtime/favorite), SettingsViewModel (two-way settings binding), DetectViewModel (scan + API import)
10. **UI shell** — MainWindow (custom titlebar, stream tabs, nav), MainView (card grid + search toolbar), SettingsPanel (all settings + SteamGridDB key), DetectPanel (provider checklist + results + import)
11. **LaunchService** — platform URI dispatch (Steam/Epic/GOG/EA/BattleNet/Ubisoft/itch.io/Xbox/custom exe), playtime session tracking, Discord presence on launch
12. **AuthService** — OAuth flows for Steam (OpenID), GOG, Epic, Xbox (OAuth2+XBL+XSTS) with HttpListener redirect callback
13. **Tray icon** — App.axaml TrayIcon with ShowWindow + Quit commands; MainWindow close-to-tray support
14. **UpdateService** — Velopack GitHub Releases source, CheckAsync / DownloadAndInstallAsync / ApplyAndRestart

---

## What's Left (in order)

### Step 7 — Integration services ✅ DONE

- **ChiakiService** — process manager, UDP discovery/wake, Win32 SetParent embed, auto-reconnect; `Win32Interop` inlined at bottom of ChiakiService.cs
- **XcloudService** — compile-time `AvaloniaWebView.WebView`, `Url` property for navigation, NavigationStarting/NavigationCompleted events wired; XcloudPanel hosts the returned control
- **SmtcService** — subprocess MediaInfoTool.exe + keybd_event P/Invoke
- **CoverService** — Channel queue, HTTP download, SteamGridDB search, retry

### Step 8 — Dependency injection wiring ✅ DONE

### Step 9 — ViewModels ✅ DONE

### Step 10 — UI shell (Avalonia AXAML) ✅ DONE

- Note: `AddGameDialog.axaml` and `ArtPickerDialog.axaml` are not yet wired into any command/ViewModel. The panels and main shell are complete.

### Step 11 — Game launcher logic ✅ DONE

### Step 12 — Platform auth / OAuth ✅ DONE

### Step 13 — Orbit view (3D galaxy) ✅ DONE

- `WebView.Avalonia` + `WebView.Avalonia.Desktop` packages added
- `AvaloniaWebViewBuilder.Initialize(default)` in App.RegisterServices; `.UseDesktopWebView()` in Program.BuildAvaloniaApp
- `OrbitView.axaml` + `OrbitView.axaml.cs` — creates a `WebView`, sets `HtmlContent` to self-contained Three.js HTML, calls `ExecuteScriptAsync("window.loadGames(...)")` on NavigationCompleted
- Galaxy HTML: spiral arm particles, game nodes placed on spiral (size = playtime), mouse orbit drag, scroll zoom, hover tooltip
- MainView restructured: `Panel` in row 1 with ScrollViewer (cards) + OrbitView (orbit) toggled by `ViewMode`; `ViewModeToggleLabel` computed property fixes the broken toggle button label
- Requires Edge WebView2 runtime on Windows; webkit2gtk on Linux

### Step 14 — Tray icon + window management ✅ DONE

### Step 15 — Auto-update (Velopack) ✅ DONE

### Step 16 — Packaging ✅ DONE

- `Cereal.App.csproj` updated: `OutputType` conditional (WinExe on win-*, Exe on linux-*); `ApplicationManifest` also conditional; `Version`, `Product`, `Description` metadata set
- `dotnet publish -r win-x64 --self-contained` → `Cereal.App.exe` ✅
- `dotnet publish -r linux-x64 --self-contained` → ELF `Cereal.App` ✅
- `.github/workflows/ci.yml` — matrix build (win-x64 + linux-x64) on push/PR to main
- `.github/workflows/release.yml` — triggered on `v*.*.*` tags; builds both platforms, packs with `vpk`, creates GitHub Release via `softprops/action-gh-release`
- `vpk` CLI: `dotnet tool install -g vpk` (requires .NET 9 ASP.NET Core runtime — pre-installed on all GitHub Actions runners; not needed for local dev builds)
- To release: `git tag v1.0.0 && git push --tags` → CI handles the rest

---

## Key Source Files to Reference

All in `D:\CODE\cereal-launcher-vite\`:

| Source file | What to port |
|---|---|
| `electron/modules/integrations/chiaki.js` | ChiakiService |
| `electron/modules/integrations/xcloud.js` | XcloudService |
| `electron/native/MediaInfoTool/Program.cs` | SmtcService (already C#) |
| `electron/modules/games/covers.js` | CoverService |
| `electron/modules/games/launcher.js` | LaunchService |
| `electron/providers/auth.js` | OAuth / AccountService |
| `electron/providers/steamgriddb.js` | SteamGridDbClient |
| `src/components/App.tsx` | MainViewModel structure |
| `src/constants.tsx` | Theme/platform constants → port to C# |
| `src/types.ts` | Already fully ported to Models/ |

---

## Design Decisions Made

- **Avalonia 11.3.9 over WPF** — cross-platform (Windows + Linux)
- **net8.0 not net9.0** — SDK available locally is 8.0.404; Avalonia template defaulted to 9 and was corrected
- **CommunityToolkit.Mvvm** — source-generator-based MVVM, no boilerplate
- **DPAPI on Windows / AES+machine-id on Linux** — mirrors Electron `safeStorage` behavior
- **Debounced DB writes (150ms)** — matches JS `setTimeout(..., 150)` pattern exactly
- **Velopack** — direct replacement for `electron-updater` with GitHub Releases
- **WebView for Orbit + Xbox Cloud** — reuse existing Three.js code rather than reimplementing 3D in Skia
- **itch.io uses SQLite (butler.db)** — needs `Microsoft.Data.Sqlite`; currently stubbed

---

## How to Resume on Another Machine

1. Copy `e:\CODE\cereal-cs\` (or clone from git once pushed)
2. Install .NET 8 SDK from https://aka.ms/dotnet/download (or use `winget install Microsoft.DotNet.SDK.8`)
3. `cd cereal-cs && dotnet restore && dotnet build` — should succeed with 0 errors
4. **All 16 steps are complete.** The app is functionally feature-complete.

## Remaining Polish / Known Gaps

| Item | Notes |
|---|---|
| `AddGameDialog.axaml` + `ArtPickerDialog.axaml` | ✅ DONE — AddGameDialog (form with exe/cover browse + SteamGridDB art picker), ArtPickerDialog (searches SteamGridDB by name, shows thumbnail grid). Wired via `ShowAddGameCommand` in MainViewModel; `AddGameRequested` event routed through MainView.axaml.cs; "+ Add game" button added to MainView toolbar. |
| itch.io provider | ✅ DONE — `Microsoft.Data.Sqlite` added; ItchioProvider reads caves+games tables from butler.db with SQL JOIN; falls back silently if DB schema differs. |
| Discord Rich Presence | ✅ DONE — `DiscordRichPresence` (Lachee) package added; DiscordService fully implemented with `DiscordRpcClient`, `SetPresence`, `ClearPresence`, proper event wiring. |
| App icon | ✅ DONE — `Assets/icon.ico` + `Assets/icon.png` + `Assets/icon.svg` copied from source repo. `<ApplicationIcon>` set in csproj (Windows-only). `--icon` flag wired in release.yml. |
| Orbit view (galaxy) | ✅ DONE — Rewritten as 2D CSS-transform pan/zoom galaxy matching source app. Uses `CLUSTER_CENTERS` per platform, nebula glows, orbit rings, platform suns, game orbs with cover images (file:// for local cache, HTTP for remote). Mouse drag to pan, scroll to zoom, double-click to fit all. No CDN dependency. |
| Theme definitions | ✅ DONE — `Models/AppTheme.cs` with `AppThemes.All` (9 themes) ported from `src/constants.tsx`. |
| Dynamic theming | ✅ DONE — `Services/ThemeService.cs` updates `Application.Resources` (`ThemeVoid`, `ThemeSurface`, `ThemeCard`, `ThemeAccent`, `ThemeText`, `ThemeText2`) at startup and whenever user changes theme in Settings. MainWindow Background + title bar use `{DynamicResource}`. `SettingsViewModel.OnThemeChanged` fires live preview. |
| Theme picker UI | ✅ DONE — APPEARANCE section added to SettingsPanel with ComboBox bound to `SelectedTheme` property; `DataTemplate x:DataType="models:AppTheme"` shows `Label`. |
| Window icon | ✅ DONE — `Assets/icon.png` added as `AvaloniaResource`; loaded via `AssetLoader.Open` in MainWindow constructor → `WindowIcon`. |
| WebView2 on Windows | End users must have Edge WebView2 runtime installed (usually already present on Win10/11). Optionally bundle the Evergreen bootstrapper. |
| webkit2gtk on Linux | Package in distro: `sudo apt install libwebkit2gtk-4.0-37` or `-4.1` depending on distro |
