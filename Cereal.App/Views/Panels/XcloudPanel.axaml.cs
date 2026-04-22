using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cereal.App.Services.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views.Panels;

public partial class XcloudPanel : UserControl
{
    private readonly XcloudService _xcloud;
    public ObservableCollection<XcloudSessionInfo> Sessions { get; } = new();

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

            var host = this.FindControl<Border>("WebViewHost");
            if (host is null) return;

            var control = _xcloud.StartSession(gameId, url, "Xbox Cloud");
            host.Child = control;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[xcloud] Start_Click failed");
        }
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        var host = this.FindControl<Border>("WebViewHost");
        if (host is not null) host.Child = null;

        foreach (var sess in Sessions.ToList())
            _xcloud.StopSession(sess.GameId);
    }

    private void OnSessionEvent(object? sender, XcloudEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var existing = Sessions.FirstOrDefault(s => s.GameId == e.GameId);
                if (existing is not null) Sessions.Remove(existing);
                if (e.Type == "disconnected") return;

                var state = e.Data.TryGetValue("state", out var s) ? s?.ToString() ?? "" : "";
                Sessions.Add(new XcloudSessionInfo
                {
                    GameId = e.GameId,
                    State = state,
                    StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[xcloud] OnSessionEvent handler failed");
            }
        });
    }
}
