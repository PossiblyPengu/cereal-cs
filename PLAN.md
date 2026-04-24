# Cereal C# — Living parity & roadmap vs. Electron

**Last reviewed:** 2026-04-23  
**Source app:** `E:\CODE\cereal-launcher-vite\` (Electron + React + Vite)  
**Target app:** `e:\CODE\cereal-cs\Cereal.App\` (Avalonia 11 + CommunityToolkit.Mvvm)

This file replaces the older gap list (much of that content was **obsolete**). Use it to track what still diverges from the original and to drive implementation in order.

## Legend

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

| Feature | Status | Notes |
| --- | --- | --- |
| Library / cards / filters / sorts | **OK** | Includes `FilterHideSteamSoftware`, quick filters, platform chips |
| Orbit view | **OK** | Native controls (`OrbitView`, `OrbitWorld`, stations, parallax, drift) — not WebView |
| Search (Ctrl+K) | **OK** | Platform chips, Ctrl+Enter launch, arrows + Enter |
| Focus / game detail | **OK** | Website link, inline “Fetching…”, and fade transition parity landed (Phase 8) |
| Stream bar (in-window) | **OK** | `MainWindow` bar + tabs incl. stream title/stats and toolbar-relative placement |
| Chiaki (stream, embed, title_change, auto-create PSN game) | **OK** | `ChiakiService` + `MainViewModel.HandleChiakiEvent` |
| xCloud (WebView, sessions, storage clear) | **OK** | `XcloudService` + `XcloudPanel` |
| Metadata (Steam, Wikipedia, SGDB) | **OK** | `MetadataService`; `metadataSource` `steam` / `wikipedia` in Settings |
| Playtime auto-sync (Steam) | **OK** | `PlaytimeSyncService` + background interval in `App.axaml.cs` |
| Platforms panel (OAuth, import) | **OK** | `PlatformsPanel` + `AuthService` + `IImportProvider` (incl. Xbox Title Hub) |
| Continue banner | **OK** | `UpdateContinueBanner` + `MainWindow` |
| Media (SMTC) widget | **OK** | 3s poll, art, collapse, and toolbar-relative placement (Phase 4) |
| Gamepad | **OK** | `GamepadService` + parity mapping in `HandleGamepadAction` (Phase 5) |
| Discord | **OK** | `DiscordService` + stream/uri/exe rules (Phase 6) |
| Card list performance | **OK** | Virtual `ItemsControl` + short horizontal rows (Phase 7) |
| Tray icon | **OK** | Tray visibility is now runtime-bound to `CloseToTray`/`MinimizeToTray` (Phase 3) |
| Chiaki “check update” in Settings | **OK** | Live GitHub release compare + labels in Settings (Phase 2) |
| Auto-update download % in UI | **OK** | In-app banner reflects download progress events (Phase 2) |
| `PLAN.md` (this file) | **OK** | Replaced 2026-04-23 |

---

## 3. Implementation roadmap (use as checklist)

### Phase 1 — Stream session parity

- [x] In `MainViewModel.HandleChiakiEvent`, when handling `state` = streaming, read **StreamInfo** from event payload and set `StreamTabViewModel.Resolution` (and optional codec / fps string).
- [x] Add a single **display title** (e.g. `DisplayTitle` or bind `Title` + `DetectedTitle`) so the stream bar matches `StreamOverlay.tsx` behavior.
- [x] **Reposition** the stream bar (`StreamBarBorder`) for **top / bottom** toolbar (`MainViewModel.ToolbarPosition`) — today it is top-aligned only inside content.
- [x] For **Xbox** rows: if no quality stats are ever emitted, **hide** empty stat slots (match Electron: PS stats, not xCloud codec strip). *(already correct via `HasQualityStats`)*

**Files:** `MainViewModel.cs` (Chiaki + tab VM), `MainWindow.axaml` / `MainWindow.axaml.cs` (bar layout).

### Phase 2 — Chiaki updates & app updates UX

- [x] Replace stub **`CheckChiakiUpdate`**: compare installed version from `ChiakiService.GetStatus()` to GitHub **streetpea/chiaki-ng** `releases/latest` (or equivalent); set `ChiakiUpdateAvailable` / version labels; keep “open releases” for manual download.
- [x] If Velopack exposes **download progress**, surface it on the in-app **update banner** (or secondary progress text) to match `onUpdateEvent` + `download-progress` in the React app.

**Files:** `SettingsViewModel.cs`, `UpdateService.cs`, `MainViewModel` / `MainWindow` (banner).

### Phase 3 — Tray & window shell

- [x] **Tray:** only show / enable the `TrayIcon` when `CloseToTray` or `MinimizeToTray` is true (match Electron: create/destroy tray with setting). May require code-behind or `App` startup after `SettingsService` load.
- [x] Re-test **Start minimized** + **Minimize to tray** so the window is never “lost” when tray is off. *(existing logic is safe; tray is now hidden when off so no “restore from hidden tray” trap)*

**Files:** `App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs`, `Settings` model as needed.

### Phase 4 — Media widget & toolbar

- [x] Move the **SMTC media** control so it follows **`ToolbarPosition`** (top/bottom/left/right), like `MediaPlayer.tsx` in the Electron app.

**Files:** `MainWindow.axaml` (grid rows / attached alignment), `MainViewModel` (one placement helper or bind to existing `ToolbarPosition`).

### Phase 5 — Gamepad parity

- [x] Audit `useGamepad` / `App.tsx` (and `FocusView` if any) against `HandleGamepadAction` / `HandleGamepadActions`.
- [x] Document or implement: grid navigation, focus open/close, **Play / Fav / Edit** focus indices if applicable.

**Files:** `MainViewModel.cs`, `MainWindow.axaml.cs`, `GamepadService.cs`, React reference under `cereal-launcher-vite/src`.

### Phase 6 — Discord

- [x] Ensure **clear presence** on: stream end, xCloud stop, app exit, and non-tracked **URI** launches (where no process is waited on). *(stream tabs + shutdown already cleared; URI/launcher paths no longer set presence without a waitable process)*
- [x] Match asset / icon key strategy: **small** image only for known Rich Presence art keys; optional platforms omit small asset to avoid invalid keys.

**Files:** `DiscordService.cs`, `LaunchService.cs`, `MainViewModel.cs` (stream presence + clears).

### Phase 7 — Library performance

- [x] **Virtualized** list: one `ItemsControl` + `VirtualizingStackPanel` of **rows** (section header + up to *N* cards per row); rewrap on `LibraryColumnCount` (scroll width) so only visible rows are realized.
- [x] Defer/decode optimization: card covers now use a cached downscaled thumbnail converter (`CoverThumbConverter`) in the grid template.

**Files:** `MainView.axaml`, `MainView.axaml.cs`, `CardLayoutEntry.cs`, `MainViewModel.cs`, `CoverThumbConverter.cs`.

### Phase 8 — Focus & polish

- [x] **Website** action in `FocusPanel` when `Game.Website` is set.
- [x] **Refresh info:** button shows inline “Fetching…” and disables while `RefreshGameInfoCommand` runs.
- [x] **enter/exit transitions**: fade transition for `FocusPanel` open/close.

**Files:** `FocusPanel.axaml`, `MainViewModel.cs`.

### Phase 9 — Discoverability (Detect vs Platforms)

- [x] Added a **prominent link** on `DetectPanel` to open Platform Accounts.

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

| Date | Change |
| --- | --- |
| 2026-04-23 | Replaced legacy gap list with current snapshot + phased roadmap; removed obsolete [MISSING] items (Platforms, metadata, playtime, stream bar, etc.) that are now implemented. |
| 2026-04-23 | Phase 6: Discord—URI/launcher session rules, stream presence, small-image whitelist. |
| 2026-04-23 | Phase 7: Library—virtualized section rows + column-aware card strips (`CardLayoutRows`). |
| 2026-04-23 | Phase 7 follow-up: card cover thumbnail decode cache (`CoverThumbConverter`) for lower grid decode cost. |
| 2026-04-23 | Phase 8 + 9: Focus refresh/website/fade transition; Detect panel now links directly to Platform Accounts. |
| 2026-04-23 | Parity sweep follow-up: PSN rows are retained on load; settings defaults aligned (orbit + Discord/playtime off); library import/export now supports object format with categories; Add/Edit game supports category editing; wizard auth auto-imports; unsupported left/right toolbar choices removed; platform API key validation UX added (Steam/itch.io). |
| 2026-04-23 | Wizard parity sweep: 7-step flow restored; final step merged with optional SteamGridDB key entry; welcome expanded to six-feature grid; hardware-based performance recommendations added; final recap now includes Accounts summary. |
| 2026-04-23 | Platforms/auth parity sweep: dynamic top tabs include closable panel tabs; stream-pill gamepad navigation added; local-detect providers (EA/Battle.net/Ubisoft) now implement full `IImportProvider` import pipeline with progress updates. |
| 2026-04-23 | Security parity sweep: OAuth state validation hardened for Steam/Epic/GOG/Xbox; account tokens migrated to `CredentialService`; schema v2 + startup legacy secret migration added; DB persistence now strips token fields defensively. |

When you finish a phase, tick the boxes above and bump **Last reviewed** at the top.
