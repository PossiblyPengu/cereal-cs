using Avalonia.Controls;
using Avalonia.Input;
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

        // Restore saved window bounds
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        if (settings.RememberWindowBounds)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            if (settings.WindowX is int x && settings.WindowY is int y)
                Position = new Avalonia.PixelPoint(x, y);
            if (settings.WindowMaximized)
                WindowState = WindowState.Maximized;
        }

        TrySetIcon();

        Closing += OnClosing;
        PositionChanged += OnPositionChanged;
        PropertyChanged += OnPropertyChanged;
        KeyDown += OnKeyDown;
        PointerMoved += OnPointerMoved;
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
        if (DataContext is not MainViewModel { IsStreaming: true }) return;
        var y = e.GetPosition(this).Y - 40; // subtract title bar height
        if (y <= 60)
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

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseAllPanelsCommand.Execute(null);
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
}
