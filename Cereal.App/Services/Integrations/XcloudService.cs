// ─── Xbox Cloud Gaming session manager ───────────────────────────────────────
// Manages embedded WebView2 sessions for https://www.xbox.com/play.
// Drives Microsoft.Web.WebView2 directly via Cereal.App.Controls.WebView2Host
// (Windows-only). Replaces the WebView.Avalonia 11.0.0.1 wrapper which had
// chronic native-HWND sizing bugs.
//
// Improvements over the old wrapper-based implementation:
//   • Per-session storage isolation via a unique UserDataFolder, instead of
//     the JS storage-clear teardown hack.
//   • Real CoreWebView2.Settings.UserAgent — no JS Object.defineProperty UA spoof.
//   • NavigationCompleted comes from WebView2 itself; no JS-injected detection.
//   • Sizing is anchored to the actual HWND client rect, so the view always
//     fills its Avalonia container regardless of UI scale or layout transforms.

using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cereal.App.Controls;
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

    private const string EdgeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";

    // Force the hosted page to use available width instead of a centered
    // max-width shell. xbox.com is an SPA that re-renders containers, so we
    // re-apply via MutationObserver.
    private const string FullBleedLayoutPatchScript =
        "try{" +
        "if(window.__cerealXcloudFullBleedInstalled)return;" +
        "window.__cerealXcloudFullBleedInstalled=true;" +
        "const css=" +
        "'html,body,#app,#root,main,[role=\"main\"],#PageContent,[data-testid=\"PageContent\"]{' +" +
        "'width:100% !important;max-width:none !important;min-width:0 !important;" +
        "min-height:100% !important;height:100% !important;margin:0 !important;box-sizing:border-box !important;}" +
        "body > div, body > main, main > div, [class*=\"container\"], [class*=\"Container\"], [class*=\"shell\"], [class*=\"Shell\"], [class*=\"layout\"], [class*=\"Layout\"]{' +" +
        "'max-width:none !important;min-width:0 !important;width:100% !important;" +
        "min-height:100% !important;margin-left:0 !important;margin-right:0 !important;box-sizing:border-box !important;}" +
        "section,article{max-width:none !important;}" +
        ";" +
        "const apply=function(){" +
        "let style=document.getElementById('cereal-xcloud-fullbleed-style');" +
        "if(!style){style=document.createElement('style');style.id='cereal-xcloud-fullbleed-style';document.head&&document.head.appendChild(style);}"+
        "if(style.textContent!==css)style.textContent=css;" +
        "const root=document.documentElement;const body=document.body;" +
        "if(root){root.style.width='100%';root.style.maxWidth='none';root.style.minHeight='100%';root.style.height='100%';}" +
        "if(body){body.style.width='100%';body.style.maxWidth='none';body.style.minHeight='100%';" +
        "body.style.height='100%';body.style.margin='0';}" +
        "};" +
        "apply();" +
        "const obs=new MutationObserver(function(){apply();});" +
        "obs.observe(document.documentElement||document,{childList:true,subtree:true,attributes:true});" +
        "}catch(e){}";

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Opens an Xbox Cloud Gaming WebView session and returns the control to embed.</summary>
    public Control StartSession(string gameId, string? url = null, string? title = null)
    {
        StopSession(gameId);
        var targetUrl = url ?? "https://www.xbox.com/play";

        if (!OperatingSystem.IsWindows())
        {
            return new TextBlock
            {
                Text = "Xbox Cloud Gaming is only supported on Windows.",
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var udf = GetSessionUserDataFolder(gameId);
        Directory.CreateDirectory(udf);

        var wv = new WebView2Host
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            UserDataFolder = udf,
            UserAgent = EdgeUserAgent,
        };

        var sess = new XcloudSessionState
        {
            GameId = gameId,
            State = "connecting",
            StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Url = targetUrl,
            Title = title ?? "Xbox Cloud Gaming",
            View = wv,
            UserDataFolder = udf,
        };
        _sessions[gameId] = sess;

        // Navigate once the core is ready (CreateCoreWebView2ControllerAsync
        // completes asynchronously after the host is attached).
        wv.CoreReady += (_, _) =>
        {
            try { wv.Source = new Uri(targetUrl); }
            catch (Exception ex) { Log.Debug(ex, "[xcloud] Initial Source set failed"); }
        };

        wv.NavigationCompleted += async (_, _) =>
        {
            sess.State = "streaming";
            RaiseEvent(gameId, "state", new() { ["state"] = "streaming" });
            Log.Information("[xcloud] NavigationCompleted {GameId}", gameId);

            try { await wv.ExecuteScriptAsync(FullBleedLayoutPatchScript); }
            catch (Exception ex) { Log.Debug(ex, "[xcloud] Full-bleed layout patch failed"); }
        };

        Log.Information("[xcloud] StartSession {GameId} → {Url}", gameId, targetUrl);
        RaiseEvent(gameId, "state", new() { ["state"] = "connecting" });
        return wv;
    }

    /// <summary>Returns the embedded view for a session, or null if not active.</summary>
    public Control? GetSessionView(string gameId) =>
        _sessions.TryGetValue(gameId, out var sess) ? sess.View : null;

    /// <summary>Returns the last-navigated URL for a session.</summary>
    public string? GetSessionUrl(string gameId) =>
        _sessions.TryGetValue(gameId, out var sess) ? sess.Url : null;

    /// <summary>Navigates an existing session to a new URL.</summary>
    public void NavigateSession(string gameId, string url)
    {
        if (!_sessions.TryGetValue(gameId, out var sess)) return;
        sess.Url = url;
        try
        {
            if (sess.View is WebView2Host wv) wv.Source = new Uri(url);
        }
        catch (Exception ex) { Log.Debug(ex, "[xcloud] NavigateSession failed"); }
        Log.Information("[xcloud] NavigateSession {GameId} → {Url}", gameId, url);
    }

    /// <summary>Closes the session and releases its WebView2 resources.</summary>
    public bool StopSession(string gameId)
    {
        if (!_sessions.TryRemove(gameId, out var sess)) return false;
        Log.Information("[xcloud] StopSession {GameId}", gameId);

        // The WebView2Host disposes its CoreWebView2Controller when Avalonia
        // detaches the native control (panel removes it from the visual tree).
        // Clean up the UserDataFolder afterwards on a background task — WebView2
        // holds file locks until the controller is fully released.
        if (!string.IsNullOrEmpty(sess.UserDataFolder))
        {
            var udf = sess.UserDataFolder;
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                try { Directory.Delete(udf, recursive: true); }
                catch (Exception ex) { Log.Debug(ex, "[xcloud] UDF cleanup failed for {Udf}", udf); }
            });
        }

        RaiseEvent(gameId, "disconnected", new() { ["reason"] = "stopped" });
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

    private void RaiseEvent(string gameId, string type, Dictionary<string, object?> data)
    {
        SessionEvent?.Invoke(this, new XcloudEventArgs { GameId = gameId, Type = type, Data = data });
    }

    private static string GetSessionUserDataFolder(string gameId)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cereal", "wv2-sessions");
        var safe = SanitizeForFs(gameId);
        return Path.Combine(root, safe);
    }

    private static string SanitizeForFs(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.ToString();
    }
}

internal sealed class XcloudSessionState
{
    public string GameId { get; init; } = "";
    public string State { get; set; } = "connecting";
    public long StartTimeMs { get; init; }
    public string Url { get; set; } = "";
    public string Title { get; init; } = "";
    public Control? View { get; init; }
    public string? UserDataFolder { get; init; }
}
