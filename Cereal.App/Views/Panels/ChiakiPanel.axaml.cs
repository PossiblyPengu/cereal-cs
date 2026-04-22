using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using System.Threading;
using System;
using System.Diagnostics;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Cereal.App.Services.Integrations;

namespace Cereal.App.Views.Panels;

public partial class ChiakiPanel : UserControl
{
    private readonly ChiakiService _chiaki;
    public ObservableCollection<DiscoveredConsole> Consoles { get; } = new();

    public ChiakiPanel()
    {
        InitializeComponent();
        _chiaki = App.Services.GetRequiredService<ChiakiService>();
        DataContext = this;
        _chiaki.SessionEvent += OnChiakiEvent;
    }

    private async void Discover_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var (success, consoles, error) = await _chiaki.DiscoverConsolesAsync();
            Consoles.Clear();
            foreach (var c in consoles) Consoles.Add(c);
            if (!success) Log.Warning("[chiaki] Discover returned error: {Error}", error);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[chiaki] Discovery failed");
        }
    }

    private void OpenGui_Click(object? sender, RoutedEventArgs e)
    {
        _chiaki.OpenGui();
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is DiscoveredConsole dc)
        {
            var (success, error, state) = _chiaki.StartStreamDirect(dc.Host);
            if (!success) Log.Warning("[chiaki] StartStreamDirect failed: {Error}", error);
        }
    }

    private async void Wake_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is DiscoveredConsole dc)
        {
            var (success, error, method) = await _chiaki.WakeConsoleAsync(dc.Host, "", CancellationToken.None);
            if (!success) Log.Warning("[chiaki] Wake failed: {Error}", error);
        }
    }

    private void OnChiakiEvent(object? sender, ChiakiEventArgs e)
    {
        try
        {
            if (e.Type != "embedded") return;
            if (!e.Data.TryGetValue("embedded", out var obj)) return;
            if (!(obj is bool embedded) || !embedded) return;

            var window = this.VisualRoot as Window;
            if (window is null) return;

            var host = this.FindControl<Border>("EmbedHost");
            if (host is null) return;

            var topLeft = host.TranslatePoint(new Avalonia.Point(0, 0), window);
            if (!topLeft.HasValue) return;

            var rect = host.Bounds;
            var x = (int)topLeft.Value.X;
            var y = (int)topLeft.Value.Y;
            var w = (int)rect.Width;
            var h = (int)rect.Height;

            var parentHwnd = Win32Interop.FindProcessMainWindow(Process.GetCurrentProcess().Id);
            _chiaki.EmbedSessionToHost(e.GameId, parentHwnd, x, y, w, h);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[chiaki] OnChiakiEvent handler failed");
        }
    }
}
