using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App;

public partial class MainWindow : Window
{
    private DispatcherTimer? _streamBarTimer;
    private bool _streamBarPinned; // stays visible while pointer is inside

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel(
            App.Services.GetRequiredService<GameService>(),
            App.Services.GetRequiredService<SettingsService>(),
            App.Services.GetRequiredService<CoverService>(),
            App.Services.GetRequiredService<ChiakiService>(),
            App.Services.GetRequiredService<XcloudService>(),
            App.Services.GetRequiredService<SmtcService>());

        DataContext = vm;

        // Restore saved window bounds. Size can be set in ctor, but Position
        // must wait until after the window has opened or the OS may ignore it.
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        if (settings.RememberWindowBounds)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            if (settings.WindowMaximized)
                WindowState = WindowState.Maximized;

            if (settings.WindowX is int x && settings.WindowY is int y &&
                !settings.WindowMaximized)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Opened += (_, _) =>
                {
                    var target = new Avalonia.PixelPoint(x, y);
                    // Sanity: make sure the saved position is still on some monitor.
                    if (IsPositionVisible(target, Width, Height))
                        Position = target;
                };
            }
        }

        // Optional start-minimized (hidden if MinimizeToTray is also on).
        if (settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            if (settings.MinimizeToTray)
                Opened += (_, _) => Hide();
        }

        TrySetIcon();

        Closing += OnClosing;
        PositionChanged += OnPositionChanged;
        PropertyChanged += OnPropertyChanged;
        KeyDown += OnKeyDown;
        PointerMoved += OnPointerMoved;

        App.Services.GetRequiredService<LaunchService>().MinimizeRequested += OnMinimizeRequested;

        ApplyUiScale(settings.UiScale);
    }

    // Parses "90%"/"100%"/"110%" → LayoutTransform scale. Called on startup
    // and any time the setting changes (see SettingsViewModel).
    public void ApplyUiScale(string? uiScale)
    {
        var raw = (uiScale ?? "100%").TrimEnd('%');
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pct) || pct <= 0)
            pct = 100;
        var scale = Math.Clamp(pct / 100.0, 0.75, 1.5);
        if (RootScaler is not null)
            RootScaler.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void OnMinimizeRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => WindowState = WindowState.Minimized);
    }

    // Mirror the `window.confirm` check Electron does in FocusView before delete.
    // We use a lightweight modal Window instead of a system dialog so it respects
    // the app's theme and keyboard focus.
    private async Task ConfirmAndDeleteAsync(MainViewModel vm)
    {
        if (vm.SelectedGame is null) return;
        var name = vm.SelectedGame.Name;
        var id   = vm.SelectedGame.Id;

        var tb = new TextBlock
        {
            Text = $"Remove \"{name}\" from your library? This cannot be undone.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(20, 20, 20, 12),
            Foreground = (Avalonia.Media.IBrush?)this.FindResource("ThemeText") ?? Avalonia.Media.Brushes.White,
        };
        var cancel = new Button { Content = "Cancel", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = "Remove", Classes = { "danger" } };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(20, 0, 20, 20),
            Children = { cancel, confirm },
        };
        var panel = new StackPanel { Children = { tb, buttons } };
        var dlg = new Window
        {
            Title = "Remove game",
            Width = 380, Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly,
            Background = (Avalonia.Media.IBrush?)this.FindResource("ThemeSurface"),
            Content = panel,
        };

        bool confirmed = false;
        cancel.Click += (_, _) => dlg.Close();
        confirm.Click += (_, _) => { confirmed = true; dlg.Close(); };

        await dlg.ShowDialog(this);
        if (confirmed) vm.DeleteGameCommand.Execute(id);
    }

    private bool IsPositionVisible(Avalonia.PixelPoint p, double w, double h)
    {
        try
        {
            foreach (var s in Screens.All)
            {
                var r = s.WorkingArea;
                var midX = p.X + (int)(w / 2);
                var midY = p.Y + (int)(h / 2);
                if (midX >= r.X && midX < r.X + r.Width &&
                    midY >= r.Y && midY < r.Y + r.Height)
                    return true;
            }
        }
        catch { /* fall through */ }
        return false;
    }

    private void TrySetIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Cereal.App/Assets/icon.png"));
            Icon = new WindowIcon(stream);
        }
        catch { /* icon is cosmetic — ignore failures */ }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        if (settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SaveWindowBounds();
    }

    private void OnPositionChanged(object? sender, EventArgs e) => SaveWindowBounds();

    private void OnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ClientSizeProperty || e.Property == WindowStateProperty)
            SaveWindowBounds();

        // Hide to tray on minimize when enabled.
        if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
        {
            var settings = App.Services.GetRequiredService<SettingsService>().Get();
            if (settings.MinimizeToTray)
                Dispatcher.UIThread.Post(Hide);
        }
    }

    private void SaveWindowBounds()
    {
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        if (!settings.RememberWindowBounds) return;

        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = (int)ClientSize.Width;
            settings.WindowHeight = (int)ClientSize.Height;
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
        }
        App.Services.GetRequiredService<SettingsService>().Save(settings);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel { IsStreaming: true } vm) return;
        var pos = e.GetPosition(this);
        var y = pos.Y - 40; // subtract title bar height
        bool nearEdge = string.Equals(vm.ToolbarPosition, "bottom", StringComparison.OrdinalIgnoreCase)
            ? y >= ClientSize.Height - 40 - 60
            : y <= 60;
        if (nearEdge)
            ShowStreamBar();
    }

    private void ShowStreamBar()
    {
        if (StreamBarBorder is { } bar)
            bar.Opacity = 1;
        _streamBarTimer?.Stop();
        if (_streamBarPinned) return;
        _streamBarTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _streamBarTimer.Tick -= StreamBarTimerTick;
        _streamBarTimer.Tick += StreamBarTimerTick;
        _streamBarTimer.Start();
    }

    private void StreamBarTimerTick(object? sender, EventArgs e)
    {
        _streamBarTimer?.Stop();
        if (StreamBarBorder is { } bar)
            bar.Opacity = 0;
    }

    private void StreamBar_PointerEntered(object? sender, PointerEventArgs e)
    {
        _streamBarPinned = true;
        _streamBarTimer?.Stop();
        if (StreamBarBorder is { } bar)
            bar.Opacity = 1;
    }

    private void StreamBar_PointerExited(object? sender, PointerEventArgs e)
    {
        _streamBarPinned = false;
        ShowStreamBar(); // restart the hide timer
    }

    private void ToggleFullscreen_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void Minimize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseAllPanelsCommand.Execute(null);
    }

    // Media-widget collapse toggles (Electron MediaPlayer.tsx behavior).
    private void MediaArt_Click(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Media.ToggleCollapseCommand.Execute(null);
        e.Handled = true;
    }

    private void MediaCollapsedPill_Click(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Media.ToggleCollapseCommand.Execute(null);
        e.Handled = true;
    }

    private void SearchBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseSearchCommand.Execute(null);
    }

    private void SearchBox_AttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (sender as TextBox)?.Focus();
    }

    private void ContentCard_PointerPressedNoop(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true; // prevent backdrop from closing the overlay
    }

    private void SearchResult_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if ((sender as Avalonia.Controls.Control)?.DataContext is GameCardViewModel card)
        {
            e.Handled = true;
            vm.SearchSelectCommand.Execute(card);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.EscapePressed();
            return;
        }

        // Focus panel shortcuts — only when no TextBox has focus (avoid stealing typing).
        // Tab-cycling inside the panel is handled declaratively via
        // KeyboardNavigation.TabNavigation="Cycle" on the panel root.
        if (vm.ShowFocus && !IsTextInputFocused())
        {
            if (e.KeyModifiers == KeyModifiers.None)
            {
                // Enter / Space: launch the focused game (matches FocusView.tsx 70–75).
                if (e.Key is Key.Enter or Key.Space && vm.SelectedGame is not null)
                {
                    e.Handled = true;
                    vm.LaunchGameCommand.Execute(vm.SelectedGame);
                    return;
                }
                if (e.Key == Key.E && vm.EditGameCommand.CanExecute(null))
                {
                    e.Handled = true;
                    vm.EditGameCommand.Execute(null);
                    return;
                }
                if (e.Key == Key.F && vm.SelectedGame is not null)
                {
                    e.Handled = true;
                    vm.SelectedGame.ToggleFavoriteCommand.Execute(null);
                    return;
                }
                // Delete / Backspace: ask for confirmation like the Electron version.
                if (e.Key is Key.Delete or Key.Back && vm.SelectedGame is not null)
                {
                    e.Handled = true;
                    _ = ConfirmAndDeleteAsync(vm);
                    return;
                }
            }
        }

        // Search overlay keyboard navigation
        if (vm.ShowSearch)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                vm.SearchMoveSelection(1);
                return;
            }
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                vm.SearchMoveSelection(-1);
                return;
            }
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                vm.SearchConfirm(launch: e.KeyModifiers == KeyModifiers.Control);
                return;
            }
        }

        // Ctrl+K — open search overlay
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K)
        {
            e.Handled = true;
            vm.OpenSearchCommand.Execute(null);
            return;
        }

        // Ctrl+F — focus search box
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        {
            e.Handled = true;
            var search = FindSearchBox(this);
            search?.Focus();
            return;
        }

        // Ctrl+, — open settings
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.OemComma)
        {
            e.Handled = true;
            vm.OpenSettingsCommand.Execute(null);
        }
    }

    private static TextBox? FindSearchBox(Avalonia.Visual root)
    {
        if (root is TextBox tb && tb.Classes.Contains("search"))
            return tb;
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root))
        {
            var found = FindSearchBox(child);
            if (found is not null) return found;
        }
        return null;
    }

    // True when focus is on a text-input-ish control — we don't want shortcut keys
    // (E/F/Delete) to be intercepted while the user is typing (e.g. in Add/Edit dialogs).
    private bool IsTextInputFocused()
    {
        var fe = FocusManager?.GetFocusedElement();
        return fe is TextBox or AutoCompleteBox;
    }

}
