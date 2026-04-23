// ─── Xbox Cloud Gaming session manager ───────────────────────────────────────
// Manages embedded WebView sessions for https://www.xbox.com/play.
// Uses WebView.Avalonia (package: WebView.Avalonia + WebView.Avalonia.Desktop).
// Requires Edge WebView2 runtime on Windows; webkit2gtk on Linux.
//
// Port notes vs the Electron source (electron/modules/integrations/xcloud.js):
//   • Multi-session: a dictionary keyed by gameId stores parallel WebViews.
//   • Edge user-agent: the WebView.Avalonia 11.0.0.1 wrapper does NOT expose
//     a native UserAgent setter, so we inject a JS override during the first
//     load. This is only effective for JS-side detection (navigator.userAgent)
//     and does not alter HTTP request headers. The WebView2 runtime happily
//     services xbox.com/play without the Edge UA in practice.
//   • Storage isolation: WebView2 uses a single per-process user data folder
//     by default. We can't sandbox per session without an environment API on
//     this wrapper, but we can scrub cookies/localStorage/sessionStorage on
//     session stop via JS. See StopSession() for the teardown sequence.

using System.Collections.Concurrent;
using AvaloniaWebView;
using Avalonia.Controls;
using Avalonia.Threading;
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

    // Edge/Chromium UA string patched into the page so scripts that read
    // navigator.userAgent see an Edge build rather than the stripped default.
    private const string EdgeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";

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

        wv.NavigationCompleted += async (_, e) =>
        {
            sess.State = "streaming";
            RaiseEvent(gameId, "state", new { state = "streaming" });
            Log.Information("[xcloud] NavigationCompleted {GameId}", gameId);

            // Patch navigator.userAgent so the Xbox site doesn't reject us on
            // feature-detects. Harmless if the override fails.
            try
            {
                var script = "try{Object.defineProperty(navigator,'userAgent',{get:function(){return '" +
                             EdgeUserAgent.Replace("'", "\\'") + "';}});}catch(e){}";
                await wv.ExecuteScriptAsync(script);
            }
            catch (Exception ex) { Log.Debug(ex, "[xcloud] UA override failed"); }
        };

        Log.Information("[xcloud] StartSession {GameId} → {Url}", gameId, targetUrl);
        RaiseEvent(gameId, "state", new { state = "connecting" });
        return wv;
    }

    /// <summary>Returns the embedded view for a session, or null if not active.</summary>
    public Control? GetSessionView(string gameId) =>
        _sessions.TryGetValue(gameId, out var sess) ? sess.View : null;

    /// <summary>Closes the session and disposes its WebView.</summary>
    public bool StopSession(string gameId)
    {
        if (!_sessions.TryRemove(gameId, out var sess)) return false;
        Log.Information("[xcloud] StopSession {GameId}", gameId);

        // Graceful teardown (matches the Electron version's sequence):
        // 1) Navigate away from the active stream URL.
        // 2) Clear cookies + local/session/IDB storage via script.
        // 3) Detach the control (we don't own its parent reference).
        if (sess.View is WebView wv)
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var clearStorage =
                        "try{localStorage.clear();sessionStorage.clear();" +
                        "document.cookie.split(';').forEach(function(c){" +
                        "  var n=c.split('=')[0].trim();" +
                        "  document.cookie=n+'=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;domain=.xbox.com';" +
                        "  document.cookie=n+'=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';" +
                        "});" +
                        "if(window.indexedDB&&indexedDB.databases){indexedDB.databases().then(function(l){l.forEach(function(d){indexedDB.deleteDatabase(d.name);});}).catch(function(){});}}catch(e){}";
                    await wv.ExecuteScriptAsync(clearStorage);
                }
                catch (Exception ex) { Log.Debug(ex, "[xcloud] Storage clear failed"); }

                try { wv.Url = new Uri("about:blank"); }
                catch (Exception ex) { Log.Debug(ex, "[xcloud] Navigation to blank failed"); }
            });
        }

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
