# Cereal C# — Living parity & roadmap vs. Electron

**Last reviewed:** 2026-04-23  
**Source app:** `E:\CODE\cereal-launcher-vite\` (Electron + React + Vite)  
**Target app:** `e:\CODE\cereal-cs\Cereal.App\` (Avalonia 11 + CommunityToolkit.Mvvm)

This file replaces the older gap list (much of that content was **obsolete**). Use it to track what still diverges from the original and to drive implementation in order.

### Legend

| Tag         | Meaning                                              |
| ----------- | ---------------------------------------------------- |
| **OK**      | Aligned with the Electron app for practical purposes |
| **PARTIAL** | Works but missing polish, edge cases, or full parity |
| **TODO**    | Not done or not verified                             |

---

## 1. Architecture (correct as of last review)

| Area            | Electron                    | C#                                                                                          |
| --------------- | --------------------------- | ------------------------------------------------------------------------------------------- |
| API surface     | `preload.js` → `window.api` | In-process services + DI (`App.axaml.cs`)                                                   |
| Data store      | `games.json` + backup story | `games.json` with **`.bak` + rolling `bak*`** and **schema migrations** (`DatabaseService`) |
| Secrets         | `safeStore`                 | `CredentialService` (DPAPI / encrypted store)                                               |
| Logging         | `logger.js`                 | Serilog (rolling file under `%AppData%\Cereal\logs`)                                        |
| Updates         | `electron-updater`          | **Velopack** (`UpdateService` → GitHub releases)                                            |
| Themes          | CSS variables               | `ThemeService` + `DynamicResource`                                                          |
| Single instance | App semantics               | `SingleInstanceGuard` in `Program.cs`                                                       |

---

## 2. Major features — status snapshot

| Feature                                                    | Status      | Notes                                                                                                   |
| ---------------------------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------------- |
| Library / cards / filters / sorts                          | **OK**      | Includes `FilterHideSteamSoftware`, quick filters, platform chips                                       |
| Orbit view                                                 | **OK**      | Native controls (`OrbitView`, `OrbitWorld`, stations, parallax, drift) — not WebView                    |
| Search (Ctrl+K)                                            | **OK**      | Platform chips, Ctrl+Enter launch, arrows + Enter                                                       |
| Focus / game detail                                        | **PARTIAL** | Optional: website link, “Fetching…” on refresh, CSS-like transitions                                    |
| Stream bar (in-window)                                     | **PARTIAL** | `MainWindow` bar + tabs; see Phase 1 for **resolution / StreamInfo** and **toolbar-relative placement** |
| Chiaki (stream, embed, title_change, auto-create PSN game) | **OK**      | `ChiakiService` + `MainViewModel.HandleChiakiEvent`                                                     |
| xCloud (WebView, sessions, storage clear)                  | **OK**      | `XcloudService` + `XcloudPanel`                                                                         |
| Metadata (Steam, Wikipedia, SGDB)                          | **OK**      | `MetadataService`; `metadataSource` `steam` / `wikipedia` in Settings                                   |
| Playtime auto-sync (Steam)                                 | **OK**      | `PlaytimeSyncService` + background interval in `App.axaml.cs`                                           |
| Platforms panel (OAuth, import)                            | **OK**      | `PlatformsPanel` + `AuthService` + `IImportProvider` (incl. Xbox Title Hub)                             |
| Continue banner                                            | **OK**      | `UpdateContinueBanner` + `MainWindow`                                                                   |
| Media (SMTC) widget                                        | **PARTIAL** | 3s poll, art, collapse; **position vs toolbar** still static bottom-left (Phase 4)                      |
| Gamepad                                                    | **PARTIAL** | `GamepadService` exists; **full App.tsx parity** not audited (Phase 5)                                  |
| Discord                                                    | **PARTIAL** | `DiscordService`; clear rules for URI launch / long sessions (Phase 6)                                  |
| Card list performance                                      | **PARTIAL** | No virtualization for huge libraries (Phase 7)                                                          |
| Tray icon                                                  | **PARTIAL** | Always defined in `App.axaml`; Electron only creates when `closeToTray` (Phase 3)                       |
| Chiaki “check update” in Settings                          | **TODO**    | `CheckChiakiUpdateCommand` is still a **stub** (Phase 2)                                                |
| Auto-update download % in UI                               | **PARTIAL** | Banner exists; **fine-grained %** may still trail electron-updater (Phase 2)                            |
| `PLAN.md` (this file)                                      | **OK**      | Replaced 2026-04-23                                                                                     |

---

## 3. Implementation roadmap (use as checklist)

### Phase 1 — Stream session parity

- [x] In `MainViewModel.HandleChiakiEvent`, when handling `state` = streaming, read **StreamInfo** from event payload and set `StreamTabViewModel.Resolution` (and optional codec / fps string).
- [x] Add a single **display title** (e.g. `DisplayTitle` or bind `Title` + `DetectedTitle`) so the stream bar matches `StreamOverlay.tsx` behavior.
- [x] **Reposition** the stream bar (`StreamBarBorder`) for **bottom / left / right** toolbar (`MainViewModel.ToolbarPosition`) — today it is top-aligned only inside content.
- [x] For **Xbox** rows: if no quality stats are ever emitted, **hide** empty stat slots (match Electron: PS stats, not xCloud codec strip). *(already correct via `HasQualityStats`)*

**Files:** `MainViewModel.cs` (Chiaki + tab VM), `MainWindow.axaml` / `MainWindow.axaml.cs` (bar layout).

### Phase 2 — Chiaki updates & app updates UX

- [ ] Replace stub **`CheckChiakiUpdate`**: compare installed version from `ChiakiService.GetStatus()` to GitHub **streetpea/chiaki-ng** `releases/latest` (or equivalent); set `ChiakiUpdateAvailable` / version labels; keep “open releases” for manual download.
- [ ] If Velopack exposes **download progress**, surface it on the in-app **update banner** (or secondary progress text) to match `onUpdateEvent` + `download-progress` in the React app.

**Files:** `SettingsViewModel.cs`, `UpdateService.cs`, `MainViewModel` / `MainWindow` (banner).

### Phase 3 — Tray & window shell

- [ ] **Tray:** only show / enable the `TrayIcon` when `CloseToTray` or `MinimizeToTray` is true (match Electron: create/destroy tray with setting). May require code-behind or `App` startup after `SettingsService` load.
- [ ] Re-test **Start minimized** + **Minimize to tray** so the window is never “lost” when tray is off.

**Files:** `App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs`, `Settings` model as needed.

### Phase 4 — Media widget & toolbar

- [ ] Move the **SMTC media** control so it follows **`ToolbarPosition`** (top/bottom/left/right), like `MediaPlayer.tsx` in the Electron app.

**Files:** `MainWindow.axaml` (grid rows / attached alignment), `MainViewModel` (one placement helper or bind to existing `ToolbarPosition`).

### Phase 5 — Gamepad parity

- [ ] Audit `useGamepad` / `App.tsx` (and `FocusView` if any) against `HandleGamepadAction` / `HandleGamepadActions`.
- [ ] Document or implement: grid navigation, focus open/close, **Play / Fav / Edit** focus indices if applicable.

**Files:** `MainViewModel.cs`, `MainWindow.axaml.cs`, `GamepadService.cs`, React reference under `cereal-launcher-vite/src`.

### Phase 6 — Discord

- [ ] Ensure **clear presence** on: stream end, xCloud stop, app exit, and non-tracked **URI** launches (where no process is waited on).
- [ ] Match asset / icon key strategy where the Electron app relies on uploaded Discord application assets.

**Files:** `DiscordService.cs`, `LaunchService.cs`, `ChiakiService` / `XcloudService` session teardown paths.

### Phase 7 — Library performance

- [ ] Introduce **virtualized** or batched card rendering for very large libraries (`ItemsRepeater` + appropriate layout, or similar).
- [ ] Optional: defer / thumbnail decode for off-screen cards.

**Files:** `MainView.axaml` (cards area), new helper controls if any.

### Phase 8 — Focus & polish

- [ ] Optional: **Website** `Hyperlink` in `FocusPanel` if `Game.Website` is set.
- [ ] **Refresh info:** disable button or show inline “Fetching…” while `RefreshGameInfoCommand` runs.
- [ ] Optional: **enter/exit transitions** for `FocusPanel` (opacity / slide) to echo CSS in the original.

**Files:** `FocusPanel.axaml`, `MainViewModel.cs`.

### Phase 9 — Discoverability (Detect vs Platforms)

- [ ] Either add **“Cloud import”** actions on `DetectPanel` that delegate to the same import paths as `PlatformsPanel`, or add a **prominent link** to open Platform Accounts. Reduces “dead” feeling of detect-only.

**Files:** `DetectPanel.axaml`, `DetectViewModel.cs`, `PlatformsPanelViewModel.cs`.

### Phase 10 — QA matrix

- [ ] Steam / Epic / GOG / custom exe launch
- [ ] PS Remote Play: stream, stop, **title change** → new PSN game in library
- [ ] xCloud: two sessions, stop, tab switch
- [ ] Metadata fetch single + **fetch all**
- [ ] Update check + install
- [ ] Second-instance **wake** + single instance
- [ ] Tray: close to tray, show from tray, quit
- [ ] Gamepad smoke test (grid + focus)

---

## 4. Key reference files

| Area      | C#                                                  | Electron (indicative)                 |
| --------- | --------------------------------------------------- | ------------------------------------- |
| API list  | (services)                                          | `electron/preload.js`                 |
| Stream UI | `MainWindow.axaml` stream bar, `StreamTabViewModel` | `StreamOverlay.tsx`, `TabBar.tsx`     |
| Search    | `MainWindow.axaml` search panel, `MainViewModel`    | `SearchOverlay.tsx`                   |
| Focus     | `FocusPanel.axaml`                                  | `FocusView.tsx`                       |
| Settings  | `SettingsPanel.axaml`, `SettingsViewModel.cs`       | `SettingsPanel.tsx`                   |
| Chiaki    | `ChiakiService.cs`, `ChiakiPanel`                   | `electron` chiaki + `ChiakiPanel.tsx` |
| xCloud    | `XcloudService.cs`, `XcloudPanel`                   | `xcloud` module + `XcloudPanel.tsx`   |
| Orbit     | `Views/OrbitView.*`, `Controls/Orbit/*`             | `Orbit` / `App.tsx` + CSS             |

---

## 5. Changelog of this document

| Date       | Change                                                                                                                                                                        |
| ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-23 | Replaced legacy gap list with current snapshot + phased roadmap; removed obsolete [MISSING] items (Platforms, metadata, playtime, stream bar, etc.) that are now implemented. |

When you finish a phase, tick the boxes above and bump **Last reviewed** at the top.
