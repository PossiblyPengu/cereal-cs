using Avalonia.Controls;
using Avalonia.Platform;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel(
            App.Services.GetRequiredService<GameService>(),
            App.Services.GetRequiredService<SettingsService>(),
            App.Services.GetRequiredService<CoverService>(),
            App.Services.GetRequiredService<ChiakiService>(),
            App.Services.GetRequiredService<XcloudService>());

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
}
