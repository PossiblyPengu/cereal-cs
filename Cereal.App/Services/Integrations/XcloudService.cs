// ─── Xbox Cloud Gaming session manager ────────────────────────────────────────
// Manages embedded WebView sessions for https://www.xbox.com/play.
// Uses WebView.Avalonia (package: WebView.Avalonia + WebView.Avalonia.Desktop).
// Requires Edge WebView2 runtime on Windows; webkit2gtk on Linux.

using System.Collections.Concurrent;
using AvaloniaWebView;
using Avalonia.Controls;
using Serilog;

namespace Cereal.App.Services.Integrations;

public sealed class XcloudSessionInfo
{
    public string GameId { get; init; } = "";
    public string State { get; init; } = "";   // connecting, streaming, disconnected
    public string Platform { get; } = "xbox";
    public long StartTimeMs { get; init; }
    public string Title { get; init; } = "";
}

public sealed class XcloudEventArgs : EventArgs
{
    public string GameId { get; init; } = "";
    public string Type { get; init; } = "";
    public string Platform { get; } = "xbox";
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
}

public sealed class XcloudService : IDisposable
{
    private readonly ConcurrentDictionary<string, XcloudSessionState> _sessions = new();

    public event EventHandler<XcloudEventArgs>? SessionEvent;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Opens an Xbox Cloud Gaming WebView panel and returns the control to embed.</summary>
    public Control StartSession(string gameId, string? url = null, string? title = null)
    {
        StopSession(gameId);

        var targetUrl = url ?? "https://www.xbox.com/play";
        var wv = new WebView
        {
            Url = new Uri(targetUrl),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };

        var sess = new XcloudSessionState
        {
            GameId = gameId,
            State = "connecting",
            StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Url = targetUrl,
            Title = title ?? "Xbox Cloud Gaming",
            View = wv,
        };
        _sessions[gameId] = sess;

        wv.NavigationStarting += (_, _) =>
        {
            sess.State = "connecting";
            RaiseEvent(gameId, "state", new { state = "connecting" });
        };

        wv.NavigationCompleted += (_, e) =>
        {
            sess.State = "streaming";
            RaiseEvent(gameId, "state", new { state = "streaming" });
            Log.Information("[xcloud] NavigationCompleted {GameId}", gameId);
        };

        Log.Information("[xcloud] StartSession {GameId} → {Url}", gameId, targetUrl);
        RaiseEvent(gameId, "state", new { state = "connecting" });
        return wv;
    }

    /// <summary>Closes the session and disposes its WebView.</summary>
    public bool StopSession(string gameId)
    {
        if (!_sessions.TryRemove(gameId, out var sess)) return false;
        Log.Information("[xcloud] StopSession {GameId}", gameId);
        RaiseEvent(gameId, "disconnected", new { reason = "stopped" });
        return true;
    }

    public IReadOnlyDictionary<string, XcloudSessionInfo> GetSessions() =>
        _sessions.ToDictionary(kv => kv.Key, kv => new XcloudSessionInfo
        {
            GameId = kv.Value.GameId,
            State = kv.Value.State,
            StartTimeMs = kv.Value.StartTimeMs,
            Title = kv.Value.Title,
        });

    public void Dispose()
    {
        foreach (var gameId in _sessions.Keys.ToList())
            StopSession(gameId);
    }

    private void RaiseEvent(string gameId, string type, object data)
    {
        var dict = data.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(data));
        SessionEvent?.Invoke(this, new XcloudEventArgs
        {
            GameId = gameId,
            Type = type,
            Data = dict,
        });
    }
}

internal sealed class XcloudSessionState
{
    public string GameId { get; init; } = "";
    public string State { get; set; } = "connecting";
    public long StartTimeMs { get; init; }
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public Control? View { get; init; }
}
