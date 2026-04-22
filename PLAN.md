# Cereal C# — Remaining 1:1 Implementation Plan

## Project context

**Source (TypeScript/Electron/React):** `E:\CODE\cereal-launcher-vite\src\`
**Target (C#/Avalonia):** `e:\CODE\cereal-cs\Cereal.App\`

The C# app is a desktop game launcher using Avalonia UI 11.3.9, CommunityToolkit.Mvvm, and compiled XAML bindings (`x:DataType`). Architecture:

- `MainWindow.axaml` — shell with tab bar + overlay Panel stack
- `Views/MainView.axaml` — floating nav pill + card grid / orbit view
- `Views/Panels/` — side panels (Settings, Detect, Chiaki, Xcloud, Focus)
- `ViewModels/MainViewModel.cs` — central state; `GameCardViewModel.cs` — per-game state
- `Services/` — GameService, CoverService, ChiakiService, SettingsService, etc.
- `Models/Game.cs`, `Models/Database.cs` — data model

**Key Avalonia patterns used throughout:**
- `IBrush` (not hex strings) for dynamically-colored bindings: `new SolidColorBrush(Color.Parse("#..."))`
- `StringConverters.IsNotNullOrEmpty` and `ObjectConverters.IsNotNull` from `Cereal.App` namespace for visibility bindings
- `Classes.active="{Binding ...}"` for conditional CSS-like class application
- `[ObservableProperty]` + `[RelayCommand]` from CommunityToolkit.Mvvm
- Partial classes: ViewModel files use `public partial class Foo : ObservableObject`
- The XAML `x:DataType` must match the actual DataContext type set in code-behind

---

## What is already implemented ✅

1. Full FocusPanel with metadata (Metacritic badge, dev/pub/release, categories, description, notes), play/fav/edit/delete actions, backdrop close, ESC close
2. Game card grid with click→FocusPanel, double-click→launch, hover Play/Fav buttons
3. Game model metadata fields: `Metacritic`, `Developer`, `Publisher`, `ReleaseDate`, `Description`, `Notes`, `Screenshots`, `Website`
4. AddGameDialog (add and edit modes) with metadata expander, file browse for exe/cover/header, live cover preview, ArtPickerDialog integration
5. Platform chips in nav pill with toggle-filter behavior
6. Sort order (`SortOrder` on MainViewModel: "name", "played", "recent", "added") wired into `Refresh()`
7. Sort/filter flyout button (`⚏`) in nav pill with Show Hidden toggle
8. ChiakiPanel: 3-tab design (Consoles, Discover, Register) with console add/remove/connect/wake/stop, registration flow, session live-state sync
9. `ChiakiConsole` model has `RegistKey` and `Morning` fields; `ChiakiService` has `GetConfig()` and `SaveConfig()`
10. Keyboard shortcuts: Escape (priority chain), Ctrl+F (focus search), Ctrl+, (open settings)
11. Window bounds persistence, close-to-tray
12. OrbitView (galaxy view), XcloudPanel (WebView), DetectPanel
13. SettingsPanel — functional but uses old blue-tinted colors, missing some sections

---

## Remaining work — ordered by impact

---

### 1. Filter flyout: category chips + "Installed only" toggle

**File:** `e:\CODE\cereal-cs\Cereal.App\Views\MainView.axaml`
**File:** `e:\CODE\cereal-cs\Cereal.App\ViewModels\MainViewModel.cs`

The current filter flyout (the `⚏` button) has sort buttons and a "Show hidden" checkbox. The source app's filter popover also has category chips and installed-only.

#### ViewModel changes (`MainViewModel.cs`)

Add observable property:
```csharp
[ObservableProperty] private bool _showInstalledOnly;
partial void OnShowInstalledOnlyChanged(bool value) => Refresh();
```

Add to `Refresh()` filter chain after the ShowHidden filter:
```csharp
.Where(g => !ShowInstalledOnly || g.Installed != false)
```

Expose categories for the flyout:
```csharp
public IEnumerable<string> AllCategories =>
    _games.GetAll()
          .SelectMany(g => g.Categories ?? Enumerable.Empty<string>())
          .Distinct()
          .OrderBy(c => c);
```

Also update `ClearFilters` to reset `ShowInstalledOnly = false`.

Add "Hide Steam software" filter in `Refresh()`:
```csharp
.Where(g => !_settings.Get().FilterHideSteamSoftware ||
            g.Platform != "steam" || g.IsCustom == true)
```

#### XAML changes (`MainView.axaml`)

Inside the filter flyout `<StackPanel>`, after the show-hidden checkbox:

```xml
<!-- Installed only -->
<StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
  <CheckBox IsChecked="{Binding ShowInstalledOnly}" Foreground="#b0aaa0"/>
  <TextBlock Text="Installed only" FontSize="12" Foreground="#b0aaa0"
             VerticalAlignment="Center"/>
</StackPanel>

<!-- Category chips -->
<TextBlock Text="CATEGORIES" FontSize="9" FontWeight="Bold"
           LetterSpacing="1.5" Foreground="#40b0aaa0"
           IsVisible="{Binding AllCategories,
             Converter={x:Static conv:ObjectConverters.IsNotNull}}"/>
<ItemsControl ItemsSource="{Binding AllCategories}">
  <ItemsControl.ItemsPanel>
    <ItemsPanelTemplate>
      <WrapPanel Orientation="Horizontal"/>
    </ItemsPanelTemplate>
  </ItemsControl.ItemsPanel>
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <Button Content="{Binding}" Margin="0,0,4,4" Padding="8,4" FontSize="10"
              CornerRadius="6" BorderThickness="1" Cursor="Hand"
              Command="{Binding $parent[UserControl].((vm:MainViewModel)DataContext).ToggleCategoryFilterCommand}"
              CommandParameter="{Binding}">
        <Button.Styles>
          <Style Selector="Button">
            <Setter Property="Background" Value="#10ffffff"/>
            <Setter Property="Foreground" Value="#b0aaa0"/>
            <Setter Property="BorderBrush" Value="#10ffffff"/>
          </Style>
        </Button.Styles>
      </Button>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

### 2. SettingsPanel visual redesign

**Files:**
- `e:\CODE\cereal-cs\Cereal.App\Views\Panels\SettingsPanel.axaml`
- `e:\CODE\cereal-cs\Cereal.App\ViewModels\SettingsViewModel.cs`

The current panel uses old blue-tinted colors. Replace throughout:

| Old | New |
|-----|-----|
| `Background="#1a1a3a"` | `Background="#08ffffff"` |
| `BorderBrush="#33aaaaff"` | `BorderBrush="#14ffffff"` |
| `Foreground="#88ccccff"` | `Foreground="#50b0aaa0"` |
| `Background="#7c6af7"` buttons | `Background="#d4a853"` + `Foreground="#07070d"` |
| `Background="#33ffffff"` ghost btn | `Background="#14ffffff"` + `Foreground="#b0aaa0"` |

Also update section labels to match the rest of the app:
```xml
<TextBlock Text="APPEARANCE" FontSize="9" FontWeight="Bold"
           LetterSpacing="1.5" Foreground="#40b0aaa0" Margin="0,0,0,8"/>
<Border Height="1" Background="#0fffffff" Margin="0,0,0,12"/>
```

#### System section additions

Add to the SettingsPanel, in the artwork/system area:

```xml
<StackPanel Orientation="Horizontal" Spacing="8" Margin="0,12,0,0">
  <Button Content="Fetch All Metadata"
          Command="{Binding FetchAllMetadataCommand}"
          Background="#14ffffff" Foreground="#b0aaa0"
          CornerRadius="6" Padding="12,7" BorderThickness="1"
          BorderBrush="#10ffffff" Cursor="Hand"/>
  <Button Content="Rescan All Platforms"
          Command="{Binding RescanAllCommand}"
          Background="#14ffffff" Foreground="#b0aaa0"
          CornerRadius="6" Padding="12,7" BorderThickness="1"
          BorderBrush="#10ffffff" Cursor="Hand"/>
</StackPanel>
```

#### ViewModel additions (`SettingsViewModel.cs`)

```csharp
[RelayCommand]
private async Task FetchAllMetadata()
{
    StatusMessage = "Fetching metadata for all games...";
    var meta = App.Services.GetRequiredService<MetadataService>();
    var (updated, total) = await meta.FetchAllAsync();
    StatusMessage = $"Updated metadata for {updated} of {total} games.";
}

[RelayCommand]
private async Task RescanAll()
{
    StatusMessage = "Scanning all platforms...";
    // Use DetectViewModel or the individual scanner services to re-detect
    StatusMessage = "Scan complete.";
}
```

#### About section

Add to bottom of SettingsPanel.axaml:

```xml
<TextBlock Text="ABOUT" FontSize="9" FontWeight="Bold"
           LetterSpacing="1.5" Foreground="#40b0aaa0" Margin="0,24,0,8"/>
<Border Height="1" Background="#0fffffff" Margin="0,0,0,12"/>
<TextBlock Text="{Binding AppVersion}" FontSize="12" Foreground="#706b63"/>
<TextBlock Text="{Binding DataPath}" FontSize="11" Foreground="#50b0aaa0"
           TextWrapping="Wrap" Margin="0,4,0,0"/>
<Button Content="Open data folder" Margin="0,8,0,0"
        Command="{Binding OpenDataFolderCommand}"
        Background="#14ffffff" Foreground="#b0aaa0"
        CornerRadius="6" Padding="12,7" BorderThickness="1"
        BorderBrush="#10ffffff" Cursor="Hand"/>
```

In `SettingsViewModel.cs`:
```csharp
public string AppVersion => System.Reflection.Assembly
    .GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

public string DataPath => App.Services
    .GetRequiredService<PathService>().AppDataDir;

[RelayCommand]
private void OpenDataFolder() =>
    System.Diagnostics.Process.Start(
        new System.Diagnostics.ProcessStartInfo(DataPath) { UseShellExecute = true });
```

---

### 3. Favorites and Recent quick-filter tabs

**Source behavior:** Source nav pill has "All", "Favorites", "Recent" tab chips that filter the game list.

**File:** `e:\CODE\cereal-cs\Cereal.App\ViewModels\MainViewModel.cs`

Add:
```csharp
[ObservableProperty] private string _quickFilter = "all";
partial void OnQuickFilterChanged(string value) => Refresh();

[RelayCommand]
private void SetQuickFilter(string filter) => QuickFilter = filter;
```

In `Refresh()`, add after ShowHidden filter:
```csharp
.Where(g => QuickFilter != "favorites" || (g.Favorite ?? false))
.Where(g => QuickFilter != "recent" || g.LastPlayed != null)
```

For "recent" quick filter, override sort:
```csharp
var sorted = (QuickFilter == "recent")
    ? preFilter.OrderByDescending(g => g.LastPlayed ?? "")
    : SortOrder switch { /* existing switch */ };
```

Update `ClearFilters` to also reset `QuickFilter = "all"`.

**File:** `e:\CODE\cereal-cs\Cereal.App\Views\MainView.axaml`

Replace the existing "All" chip with three chips before the platform chips:

```xml
<Button Classes="chip" Content="All"
        Classes.active="{Binding QuickFilter,
          Converter={x:Static conv:StringConverters.IsNotNullOrEmpty},
          ConverterParameter=all}"
        Command="{Binding SetQuickFilterCommand}"
        CommandParameter="all"/>
<Button Classes="chip" Content="Favs"
        Classes.active="{Binding QuickFilter,
          Converter={x:Static conv:StringConverters.IsNotNullOrEmpty},
          ConverterParameter=favorites}"
        Command="{Binding SetQuickFilterCommand}"
        CommandParameter="favorites"/>
<Button Classes="chip" Content="Recent"
        Classes.active="{Binding QuickFilter,
          Converter={x:Static conv:StringConverters.IsNotNullOrEmpty},
          ConverterParameter=recent}"
        Command="{Binding SetQuickFilterCommand}"
        CommandParameter="recent"/>
```

---

### 4. FocusPanel: "Refresh Info" button

**Source behavior:** Platform badge row has a "Refresh" button that re-fetches SGDB metadata for the selected game.

**File:** `e:\CODE\cereal-cs\Cereal.App\Views\Panels\FocusPanel.axaml`

In the platform badge grid (~line 160, `ColumnDefinitions="*,Auto"`), add to Grid.Column=1:

```xml
<Button Grid.Column="1" Classes="focus-ghost" Content="Refresh Info"
        Command="{Binding RefreshGameInfoCommand}"
        IsVisible="{Binding SelectedGame, Converter={x:Static conv:ObjectConverters.IsNotNull}}"/>
```

**File:** `e:\CODE\cereal-cs\Cereal.App\ViewModels\MainViewModel.cs`

```csharp
[RelayCommand]
private async Task RefreshGameInfo()
{
    if (SelectedGame is null) return;
    StatusMessage = $"Fetching info for {SelectedGame.Name}...";
    var meta = App.Services.GetRequiredService<MetadataService>();
    await meta.FetchForGameAsync(SelectedGame.Game);
    _covers.EnqueueGame(SelectedGame.Id);
    Refresh();
    StatusMessage = null;
}
```

---

### 5. FocusPanel: screenshot strip

**Source behavior:** FocusView shows a horizontal strip of screenshot thumbnails below the description when `game.screenshots` is populated.

**File:** `e:\CODE\cereal-cs\Cereal.App\ViewModels\GameCardViewModel.cs`

```csharp
public bool HasScreenshots => Game.Screenshots?.Count > 0;
// Add to Refresh():
OnPropertyChanged(nameof(HasScreenshots));
```

**File:** `e:\CODE\cereal-cs\Cereal.App\Views\Panels\FocusPanel.axaml`

After the description TextBlock, inside the right ScrollViewer StackPanel:

```xml
<ScrollViewer IsVisible="{Binding SelectedGame.HasScreenshots}"
              HorizontalScrollBarVisibility="Auto"
              VerticalScrollBarVisibility="Disabled"
              Margin="0,0,0,10">
  <ItemsControl ItemsSource="{Binding SelectedGame.Game.Screenshots}">
    <ItemsControl.ItemsPanel>
      <ItemsPanelTemplate>
        <StackPanel Orientation="Horizontal" Spacing="6"/>
      </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <Border CornerRadius="4" ClipToBounds="True" Width="120" Height="68">
          <Image Source="{Binding}" Stretch="UniformToFill"/>
        </Border>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
</ScrollViewer>
```

---

## Build verification

After each change group:

```bash
cd e:\CODE\cereal-cs
dotnet build Cereal.App/Cereal.App.csproj --no-restore 2>&1 | tail -30
```

**Expected:** 0 errors. Pre-existing ignorable warnings:
- `NU1603` — Velopack version mismatch
- `AVLN3001` — ArtPickerDialog no public constructor
- `CS8826` — partial method signature differences (fix by matching parameter names exactly)

---

## Key files reference

| File | Purpose |
|------|---------|
| `ViewModels/MainViewModel.cs` | Central state: filter, sort, panels, refresh |
| `ViewModels/GameCardViewModel.cs` | Per-game display state + IBrush computed props |
| `Views/MainView.axaml` | Nav pill + card grid + orbit view |
| `Views/MainView.axaml.cs` | Card_Tapped, FavBtn_Click, edit dialog wiring |
| `Views/Panels/FocusPanel.axaml` + `.cs` | Game detail overlay |
| `Views/Panels/ChiakiPanel.axaml` + `.cs` | PlayStation Remote Play panel |
| `Views/Panels/SettingsPanel.axaml` | Settings panel XAML |
| `ViewModels/SettingsViewModel.cs` | Settings state + commands |
| `Models/Game.cs` | Game data model |
| `Models/Database.cs` | DB root + ChiakiConfig + ChiakiConsole |
| `Services/Integrations/ChiakiService.cs` | Chiaki-ng integration |
| `Utilities/Converters.cs` | `StringConverters`, `ObjectConverters` (namespace `Cereal.App`) |

---

## Color / style system

| Token | Value |
|-------|-------|
| Void bg | `#07070d` |
| Card bg | `#101018` |
| Panel glass | `#0dffffff` or `#e8101018` |
| Gold accent | `#d4a853` |
| Muted text | `#b0aaa0` |
| Dim text | `#706b63` |
| Subtle text | `#3d3a35` |
| Border | `#0fffffff` – `#1affffff` |
| Green (live/ok) | `#22c55e` |
| Red (danger) | `#ff4444` |
| IBrush green bg | `new SolidColorBrush(Color.Parse("#0f22c55e"))` |
| IBrush red bg | `new SolidColorBrush(Color.Parse("#0fff4444"))` |
| IBrush gold bg | `new SolidColorBrush(Color.Parse("#0fd4a853"))` |

---

## Priority order

| # | Task | Effort |
|---|------|--------|
| 1 | Filter flyout: category chips + installed-only | Small |
| 2 | SettingsPanel visual restyle + About section | Medium |
| 3 | Favorites / Recent quick-filter tabs | Small |
| 4 | FocusPanel: Refresh Info button | Small |
| 5 | FocusPanel: screenshot strip | Small |
| 6 | SettingsPanel: Fetch All Metadata + Rescan All | Medium |

Each task is independent — they can be done in any order.
