using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public XcloudPanel()
    {
        InitializeComponent();
        _xcloud = App.Services.GetRequiredService<XcloudService>();
        DataContext = this;
        _xcloud.SessionEvent += OnSessionEvent;
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var tb = this.FindControl<TextBox>("UrlBox");
            var url = string.IsNullOrWhiteSpace(tb?.Text) ? "https://www.xbox.com/play" : tb.Text;
            var gameId = "xbox:" + Guid.NewGuid().ToString("n");
            SpawnSession(gameId, url, "Xbox Cloud");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[xcloud] Start_Click failed");
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainViewModel vm })
            vm.CloseXcloudCommand.Execute(null);
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        // Stop only the active session so other parallel sessions keep running.
        if (_activeGameId is null)
        {
            foreach (var sess in Sessions.ToList())
                _xcloud.StopSession(sess.GameId);
            SetHostContent(null);
            return;
        }

        var id = _activeGameId;
        _xcloud.StopSession(id);

        // Switch to the next session, if any.
        var next = Sessions.FirstOrDefault(s => s.GameId != id);
        if (next is not null)
            ShowSession(next.GameId);
        else
            SetHostContent(null);
    }

    private void Session_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: XcloudSessionInfo info })
            ShowSession(info.GameId);
    }

    private void SpawnSession(string gameId, string url, string title)
    {
        var control = _xcloud.StartSession(gameId, url, title);
        SetHostContent(control);
        _activeGameId = gameId;
    }

    private void ShowSession(string gameId)
    {
        var view = _xcloud.GetSessionView(gameId);
        if (view is null) return;
        // Detach from any previous parent so we can re-parent cleanly.
        if (view.Parent is ContentControl prevCc) prevCc.Content = null;
        if (view.Parent is Border prevBorder) prevBorder.Child = null;
        SetHostContent(view);
        _activeGameId = gameId;
    }

    private void SetHostContent(Control? control)
    {
        var host = this.FindControl<Border>("WebViewHost");
        if (host is null) return;
        host.Child = control;
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
                    if (_activeGameId == e.GameId) _activeGameId = null;
                    return;
                }

                var state = e.Data.TryGetValue("state", out var s) ? s?.ToString() ?? "" : "";
                Sessions.Add(new XcloudSessionInfo
                {
                    GameId = e.GameId,
                    State = state,
                    StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Title = existing?.Title ?? "Xbox Cloud Gaming",
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[xcloud] OnSessionEvent handler failed");
            }
        });
    }
}
