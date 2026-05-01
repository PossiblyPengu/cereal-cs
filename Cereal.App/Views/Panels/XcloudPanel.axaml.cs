using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cereal.App.Services.Integrations;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views.Panels;

public partial class XcloudPanel : UserControl
{
    private readonly XcloudService _xcloud;
    public ObservableCollection<XcloudSessionInfo> Sessions { get; } = new();

    private string? _activeGameId;
    private bool _autoStartPending;

    // Cached control references
    private Border?    _statusDot;
    private Border?    _statePill;
    private TextBlock? _statePillText;
    private Button?    _stopBtn;
    private Grid?      _contentGrid;
    private Control?   _activeWebView;
    private StackPanel? _emptyState;
    private TextBlock? _emptySubtitle;

    public XcloudPanel()
    {
        InitializeComponent();
        _xcloud = App.Services.GetRequiredService<XcloudService>();
        DataContext = this;

        _statusDot     = this.FindControl<Border>("StatusDot");
        _statePill     = this.FindControl<Border>("StatePill");
        _statePillText = this.FindControl<TextBlock>("StatePillText");
        _stopBtn       = this.FindControl<Button>("StopBtn");
        _contentGrid   = this.FindControl<Grid>("ContentGrid");
        _emptyState    = this.FindControl<StackPanel>("EmptyState");
        _emptySubtitle = this.FindControl<TextBlock>("EmptySubtitle");

        _xcloud.SessionEvent += OnSessionEvent;
    }

    // ── Auto-start ─────────────────────────────────────────────────────────────

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;

        var visible = change.NewValue is true;

        // Mirror visibility to the embedded view so the native HWND hides/shows
        // with the panel.
        if (_activeWebView is not null)
            _activeWebView.IsVisible = visible;

        if (visible && !_autoStartPending && _activeGameId is null && Sessions.Count == 0)
        {
            _autoStartPending = true;
            Dispatcher.UIThread.Post(AutoStart, DispatcherPriority.Loaded);
        }
    }

    private void AutoStart()
    {
        _autoStartPending = false;
        if (_activeGameId is not null || Sessions.Count > 0) return;
        try
        {
            var gameId = "xbox:" + Guid.NewGuid().ToString("n");
            SpawnSession(gameId, "https://www.xbox.com/play");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[xcloud] AutoStart failed");
        }
    }

    // No-op kept for source compatibility with MainWindow.TryRelayoutXcloudHost.
    // WebView2Host self-syncs sizing via ArrangeOverride, so no nudge is needed.
    public void ForceHostRelayout() { }

    // ── Button handlers ────────────────────────────────────────────────────────

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainViewModel vm })
            vm.CloseXcloudCommand.Execute(null);
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        var id = _activeGameId;
        _activeGameId = null;

        if (id is null)
            foreach (var sess in Sessions.ToList())
                _xcloud.StopSession(sess.GameId);
        else
            _xcloud.StopSession(id);

        SetHostContent(null);
    }

    // ── Window controls (we cover MainWindow's title bar while xcloud is open) ─

    private void ControlBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Buttons mark the event handled, so this only fires on bare bar surface.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && TopLevel.GetTopLevel(this) is Window w)
            w.BeginMoveDrag(e);
    }

    private void WindowMinimize_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window w)
            w.WindowState = WindowState.Minimized;
    }

    private void WindowMaximize_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private void WindowClose_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window w)
            w.Close();
    }

    private void Browser_Click(object? sender, RoutedEventArgs e)
    {
        var url = (_activeGameId is not null ? _xcloud.GetSessionUrl(_activeGameId) : null)
                  ?? "https://www.xbox.com/play";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[xcloud] Browser_Click failed");
        }
    }

    // ── Session management ─────────────────────────────────────────────────────

    private void SpawnSession(string gameId, string url)
    {
        var control = _xcloud.StartSession(gameId, url, "Xbox Cloud Gaming");
        _activeGameId = gameId;
        SetHostContent(control);
    }

    private void SetHostContent(Control? control)
    {
        if (_activeWebView is not null && _contentGrid is not null)
            _contentGrid.Children.Remove(_activeWebView);

        _activeWebView = control;

        if (control is not null && _contentGrid is not null)
        {
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            control.VerticalAlignment = VerticalAlignment.Stretch;
            control.Margin = new Avalonia.Thickness(0);
            control.ZIndex = 1;
            _contentGrid.Children.Add(control);
        }

        if (_emptyState is not null)
            _emptyState.IsVisible = control is null;
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        var hasSession = _activeGameId is not null;
        var sessState = hasSession
            ? Sessions.FirstOrDefault(s => s.GameId == _activeGameId)?.State ?? "connecting"
            : null;

        // Status dot
        if (_statusDot is not null)
        {
            _statusDot.Background = sessState switch
            {
                "streaming"  => new SolidColorBrush(Color.Parse("#4ade80")),
                "connecting" => new SolidColorBrush(Color.Parse("#fbbf24")),
                _            => new SolidColorBrush(Color.Parse("#25b0aaa0")),
            };
        }

        // State pill
        if (_statePill is not null && _statePillText is not null)
        {
            _statePill.IsVisible = hasSession;
            switch (sessState)
            {
                case "streaming":
                    _statePillText.Text       = "LIVE";
                    _statePillText.Foreground  = new SolidColorBrush(Color.Parse("#4ade80"));
                    _statePill.Background      = new SolidColorBrush(Color.Parse("#1a4ade80"));
                    break;
                case "connecting":
                    _statePillText.Text       = "CONNECTING";
                    _statePillText.Foreground  = new SolidColorBrush(Color.Parse("#fbbf24"));
                    _statePill.Background      = new SolidColorBrush(Color.Parse("#1afbbf24"));
                    break;
                default:
                    _statePillText.Text = sessState?.ToUpperInvariant() ?? "";
                    break;
            }
        }

        // Stop button
        if (_stopBtn is not null)
            _stopBtn.IsVisible = hasSession;

        // Empty state subtitle
        if (_emptySubtitle is not null && !hasSession)
            _emptySubtitle.Text = "Close this panel to return to the library.";
    }

    private void OnSessionEvent(object? sender, XcloudEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var existing = Sessions.FirstOrDefault(s => s.GameId == e.GameId);
                if (existing is not null) Sessions.Remove(existing);

                if (e.Type == "disconnected")
                {
                    if (_activeGameId == e.GameId)
                    {
                        _activeGameId = null;
                        SetHostContent(null);
                    }
                    UpdateUiState();
                    return;
                }

                var state = e.Data.TryGetValue("state", out var s) ? s?.ToString() ?? "" : "";
                Sessions.Add(new XcloudSessionInfo
                {
                    GameId       = e.GameId,
                    State        = state,
                    StartTimeMs  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Title        = existing?.Title ?? "Xbox Cloud Gaming",
                });
                UpdateUiState();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[xcloud] OnSessionEvent handler failed");
            }
        });
    }
}
