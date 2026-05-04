using System.Net;
using System.Net.Sockets;
using Cereal.Infrastructure.Config;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// Manages OAuth flows for all platform integrations.
/// Opens a localhost HTTP callback listener on a random port (never fixed port 7373).
/// Tokens are stored via <see cref="ICredentialService"/> (DPAPI) — never in the database.
/// </summary>
public sealed class AuthService(
    AppConfig config,
    IAccountRepository accounts,
    ICredentialService credentials,
    IMessenger messenger,
    IHttpClientFactory httpFactory) : IAuthService
{
    // In-memory session cache — loaded from credential store at startup.
    private readonly Dictionary<string, AuthSession> _sessions = [];

    private const string AccessTokenSuffix  = "_access";
    private const string RefreshTokenSuffix = "_refresh";

    // ── Public interface ──────────────────────────────────────────────────────

    public bool IsAuthenticated(string platform)
    {
        if (!_sessions.TryGetValue(platform, out var s)) return false;
        if (s.ExpiresAt is long exp && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= exp)
            return false;
        return true;
    }

    public AuthSession? GetSession(string platform) =>
        _sessions.TryGetValue(platform, out var s) ? s : null;

    public async Task<AuthSession> AuthenticateAsync(string platform, CancellationToken ct = default)
    {
        var session = platform switch
        {
            "gog"   => await AuthGogAsync(ct),
            "epic"  => await AuthEpicAsync(ct),
            "xbox"  => await AuthXboxAsync(ct),
            "steam" => await AuthSteamOpenIdAsync(ct),
            _       => throw new NotSupportedException($"OAuth not supported for platform: {platform}"),
        };

        StoreSession(platform, session);
        return session;
    }

    public async Task<AuthSession?> TryRefreshAsync(string platform, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(platform, out var s)) return null;
        if (string.IsNullOrEmpty(s.RefreshToken)) return null;

        try
        {
            var refreshed = platform switch
            {
                "gog"  => await RefreshGogAsync(s.RefreshToken, ct),
                "epic" => await RefreshEpicAsync(s.RefreshToken, ct),
                _      => null,
            };

            if (refreshed is not null)
            {
                StoreSession(platform, refreshed);
                return refreshed;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[auth] Failed to refresh token for {Platform}", platform);
        }
        return null;
    }

    public async Task SignOutAsync(string platform, CancellationToken ct = default)
    {
        _sessions.Remove(platform);
        credentials.Delete(platform + AccessTokenSuffix);
        credentials.Delete(platform + RefreshTokenSuffix);
        await accounts.DeleteAsync(platform, ct);
        messenger.Send(new AuthStateChangedMessage(platform, false));
    }

    /// <summary>
    /// Load persisted tokens from the credential store into the session cache.
    /// Call once at app startup.
    /// </summary>
    public void LoadPersistedSessions()
    {
        foreach (var platform in new[] { "steam", "epic", "gog", "xbox" })
        {
            var access  = credentials.Retrieve(platform + AccessTokenSuffix);
            var refresh = credentials.Retrieve(platform + RefreshTokenSuffix);
            if (!string.IsNullOrEmpty(access))
                _sessions[platform] = new AuthSession(platform, access, refresh, null);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StoreSession(string platform, AuthSession session)
    {
        _sessions[platform] = session;
        credentials.Store(platform + AccessTokenSuffix, session.AccessToken);
        if (!string.IsNullOrEmpty(session.RefreshToken))
            credentials.Store(platform + RefreshTokenSuffix, session.RefreshToken);
        messenger.Send(new AuthStateChangedMessage(platform, true));
    }

    /// <summary>Opens a random-port localhost HTTP listener and returns the one-shot callback URL.</summary>
    private static (HttpListener listener, string callbackUrl, int port) StartCallbackListener()
    {
        var p = GetFreePort();
        var url = $"http://127.0.0.1:{p}/callback/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        return (listener, url, p);
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    /// <summary>Wait for a single callback request and return the full request URL.</summary>
    private static async Task<Uri> WaitForCallbackAsync(HttpListener listener, CancellationToken ct)
    {
        using var reg = ct.Register(listener.Abort);
        var ctx = await listener.GetContextAsync();
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/html";
        await using var w = new System.IO.StreamWriter(ctx.Response.OutputStream);
        await w.WriteAsync("<html><body><h2>Authentication complete — you can close this tab.</h2></body></html>");
        ctx.Response.Close();
        return ctx.Request.Url!;
    }

    private static string GenerateState() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // ── Platform-specific flows ───────────────────────────────────────────────

    private async Task<AuthSession> AuthGogAsync(CancellationToken ct)
    {
        var (listener, callbackUrl, _) = StartCallbackListener();
        var state = GenerateState();

        var authorizeUrl =
            $"https://auth.gog.com/auth?client_id={config.OAuth.GogClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
            $"&response_type=code&state={state}";

        OpenBrowser(authorizeUrl);

        using var _ = listener;
        var redirectUri = await WaitForCallbackAsync(listener, ct);
        var qs = System.Web.HttpUtility.ParseQueryString(redirectUri.Query);

        if (qs["state"] != state)
            throw new InvalidOperationException("OAuth state mismatch — possible CSRF");

        var code = qs["code"] ?? throw new InvalidOperationException("No code in GOG callback");

        return await ExchangeGogCodeAsync(code, callbackUrl, ct);
    }

    private async Task<AuthSession> ExchangeGogCodeAsync(string code, string redirectUri,
        CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = config.OAuth.GogClientId,
            ["client_secret"] = config.OAuth.GogClientSecret,
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
        });
        var resp = await http.PostAsync("https://auth.gog.com/token", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        return new AuthSession(
            "gog",
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToUnixTimeMilliseconds());
    }

    private async Task<AuthSession> RefreshGogAsync(string refreshToken, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = config.OAuth.GogClientId,
            ["client_secret"] = config.OAuth.GogClientSecret,
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        var resp = await http.PostAsync("https://auth.gog.com/token", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        return new AuthSession(
            "gog",
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToUnixTimeMilliseconds());
    }

    private async Task<AuthSession> AuthEpicAsync(CancellationToken ct)
    {
        var (listener, callbackUrl, _) = StartCallbackListener();
        var state = GenerateState();

        var authorizeUrl =
            $"https://www.epicgames.com/id/authorize?client_id={config.OAuth.EpicClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
            $"&response_type=code&state={state}";

        OpenBrowser(authorizeUrl);

        using var _ = listener;
        var redirectUri = await WaitForCallbackAsync(listener, ct);
        var qs = System.Web.HttpUtility.ParseQueryString(redirectUri.Query);

        if (qs["state"] != state)
            throw new InvalidOperationException("OAuth state mismatch — possible CSRF");

        var code = qs["code"] ?? throw new InvalidOperationException("No code in Epic callback");
        return await ExchangeEpicCodeAsync(code, callbackUrl, ct);
    }

    private async Task<AuthSession> ExchangeEpicCodeAsync(string code, string redirectUri,
        CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        var credentials64 = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes(
                $"{config.OAuth.EpicClientId}:{config.OAuth.EpicClientSecret}"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials64);

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]   = "authorization_code",
            ["code"]         = code,
            ["redirect_uri"] = redirectUri,
        });
        var resp = await http.PostAsync(
            "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        return new AuthSession(
            "epic",
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToUnixTimeMilliseconds());
    }

    private async Task<AuthSession> RefreshEpicAsync(string refreshToken, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        var credentials64 = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes(
                $"{config.OAuth.EpicClientId}:{config.OAuth.EpicClientSecret}"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials64);

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        var resp = await http.PostAsync(
            "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        return new AuthSession(
            "epic",
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToUnixTimeMilliseconds());
    }

    private Task<AuthSession> AuthXboxAsync(CancellationToken ct)
    {
        // Xbox uses MSAL / Microsoft account flow — implemented in Phase F.
        throw new NotImplementedException("Xbox OAuth flow — Phase F");
    }

    private Task<AuthSession> AuthSteamOpenIdAsync(CancellationToken ct)
    {
        // Steam uses OpenID 2.0 — implemented in Phase F.
        throw new NotImplementedException("Steam OpenID flow — Phase F");
    }

    private static void OpenBrowser(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warning(ex, "[auth] Could not open browser for {Url}", url); }
    }
}
