// ─── OAuth / Platform Auth Service ────────────────────────────────────────────
// Handles OAuth flows for Steam (OpenID), GOG (OAuth2), Epic (OAuth2), Xbox (OAuth2+XBL).
// Uses System.Net.HttpListener for the redirect callback.

using System.Net;
using System.Text;
using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services;

public sealed class AuthService
{
    private readonly DatabaseService _db;
    private readonly CredentialService _creds;
    private readonly HttpClient _http;

    // OAuth app credentials (public values from the JS source)
    private static class Cfg
    {
        public static class Gog
        {
            public const string ClientId     = "46899977096215655";
            public const string ClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
            public const string RedirectUri  = "https://embed.gog.com/on_login_success?origin=client";
            public const string AuthUrl      = "https://login.gog.com/auth";
            public const string TokenUrl     = "https://auth.gog.com/token";
        }
        public static class Epic
        {
            public const string ClientId      = "34a02cf8f4414e29b15921876da36f9a";
            public const string ClientSecret  = "daafbccc737745039dffe53d94fc76cf";
            public const string RedirectApiUrl = "https://www.epicgames.com/id/api/redirect";
            public const string AuthUrl       = "https://www.epicgames.com/id/login";
            public const string TokenUrl      = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";
        }
        public static class Xbox
        {
            public const string ClientId    = "1fec8e78-bce4-4aaf-ab1b-5451cc387264";
            public const string RedirectUri = "https://login.microsoftonline.com/common/oauth2/nativeclient";
            public const string Scope       = "XboxLive.signin XboxLive.offline_access openid profile";
            public const string AuthUrl     = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
            public const string TokenUrl    = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
            public const string XblAuthUrl  = "https://user.auth.xboxlive.com/user/authenticate";
            public const string XstsAuthUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        }
        public static class Steam
        {
            public const string OpenIdUrl  = "https://steamcommunity.com/openid/login";
            public const string ReturnUrl  = "http://localhost:7373/steam-callback";
            public const string Realm      = "http://localhost:7373/";
        }
    }

    public AuthService(DatabaseService db, CredentialService creds)
    {
        _db = db;
        _creds = creds;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "cereal-launcher/1.0");
    }

    // ─── Auth URL builders ────────────────────────────────────────────────────

    public string GetSteamAuthUrl()
    {
        var p = new Dictionary<string, string>
        {
            ["openid.ns"]         = "http://specs.openid.net/auth/2.0",
            ["openid.mode"]       = "checkid_setup",
            ["openid.return_to"]  = Cfg.Steam.ReturnUrl,
            ["openid.realm"]      = Cfg.Steam.Realm,
            ["openid.identity"]   = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
        };
        return Cfg.Steam.OpenIdUrl + "?" + ToQueryString(p);
    }

    public string GetGogAuthUrl()
    {
        var state = Guid.NewGuid().ToString("N");
        return $"{Cfg.Gog.AuthUrl}?client_id={Cfg.Gog.ClientId}" +
               $"&redirect_uri={Uri.EscapeDataString(Cfg.Gog.RedirectUri)}" +
               $"&response_type=code&layout=client2&state={state}";
    }

    public string GetEpicAuthUrl()
    {
        var redirectUrl = $"{Cfg.Epic.RedirectApiUrl}?clientId={Cfg.Epic.ClientId}&responseType=code";
        return $"{Cfg.Epic.AuthUrl}?redirectUrl={Uri.EscapeDataString(redirectUrl)}";
    }

    public string GetXboxAuthUrl()
    {
        var state = Guid.NewGuid().ToString("N");
        return $"{Cfg.Xbox.AuthUrl}?client_id={Cfg.Xbox.ClientId}" +
               $"&response_type=code&redirect_uri={Uri.EscapeDataString(Cfg.Xbox.RedirectUri)}" +
               $"&scope={Uri.EscapeDataString(Cfg.Xbox.Scope)}&response_mode=query&state={state}";
    }

    // ─── OAuth callback listener ──────────────────────────────────────────────

    /// <summary>
    /// Opens an HttpListener on localhost and waits for the OAuth redirect, then
    /// exchanges the code for tokens and stores them. The returned Task resolves
    /// with the account that was saved, or throws on failure.
    /// The caller is responsible for opening a browser to the auth URL.
    /// </summary>
    public async Task<AccountInfo> WaitForCallbackAsync(
        string platform, string listenPrefix = "http://localhost:7373/",
        CancellationToken ct = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(listenPrefix);
        listener.Start();

        Log.Information("[auth] Listening for {Platform} OAuth callback on {Prefix}", platform, listenPrefix);

        HttpListenerContext ctx;
        try
        {
            var task = listener.GetContextAsync();
            using var reg = ct.Register(() => listener.Stop());
            ctx = await task;
        }
        catch (HttpListenerException)
        {
            throw new OperationCanceledException("Auth callback listener was cancelled", ct);
        }

        var query = ctx.Request.QueryString;
        ctx.Response.StatusCode = 200;
        var html = "<html><body style='font-family:sans-serif;background:#111;color:#fff;text-align:center;padding:60px'>" +
                   "<h2>Authenticated ✓</h2><p>You can close this window.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.Close();
        listener.Stop();

        return platform switch
        {
            "steam" => await CompleteSteamAuthAsync(query, ct),
            "gog"   => await CompleteGogAuthAsync(query["code"] ?? throw new Exception("No code in callback"), ct),
            "epic"  => await CompleteEpicAuthAsync(query["code"] ?? throw new Exception("No code in callback"), ct),
            "xbox"  => await CompleteXboxAuthAsync(query["code"] ?? throw new Exception("No code in callback"), ct),
            _       => throw new NotSupportedException($"Unknown platform: {platform}"),
        };
    }

    // ─── Steam (OpenID) ───────────────────────────────────────────────────────

    private async Task<AccountInfo> CompleteSteamAuthAsync(System.Collections.Specialized.NameValueCollection q, CancellationToken ct)
    {
        var claimedId = q["openid.claimed_id"] ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(claimedId, @"(\d{17})$");
        if (!m.Success) throw new Exception("Could not extract Steam ID from callback");

        var steamId = m.Groups[1].Value;
        var profileUrl = $"https://steamcommunity.com/profiles/{steamId}/?xml=1";
        var xml = await _http.GetStringAsync(profileUrl, ct);

        string GetCdata(string tag)
        {
            var mr = System.Text.RegularExpressions.Regex.Match(xml, $@"<{tag}><!\[CDATA\[([\s\S]*?)\]\]></{tag}>");
            return mr.Success ? mr.Groups[1].Value : "";
        }
        string GetTag(string tag)
        {
            var mr = System.Text.RegularExpressions.Regex.Match(xml, $@"<{tag}>([^<]*)</{tag}>");
            return mr.Success ? mr.Groups[1].Value : "";
        }

        var account = new AccountInfo
        {
            AccountId = steamId,
            Username = GetCdata("steamID").Length > 0 ? GetCdata("steamID") : GetTag("steamID"),
        };
        _db.Db.Accounts["steam"] = account;
        _db.Save();
        _creds.SetPassword("cereal", "steam_id", steamId);
        Log.Information("[auth] Steam authenticated as {Name} ({Id})", account.Username, steamId);
        return account;
    }

    // ─── GOG ─────────────────────────────────────────────────────────────────

    private async Task<AccountInfo> CompleteGogAuthAsync(string code, CancellationToken ct)
    {
        var url = $"{Cfg.Gog.TokenUrl}?client_id={Cfg.Gog.ClientId}&client_secret={Cfg.Gog.ClientSecret}" +
                  $"&grant_type=authorization_code&code={Uri.EscapeDataString(code)}" +
                  $"&redirect_uri={Uri.EscapeDataString(Cfg.Gog.RedirectUri)}";
        using var resp = await _http.GetAsync(url, ct);
        var data = await ParseJsonAsync(resp, ct);

        var account = new AccountInfo
        {
            AccessToken  = data.GetStringOrNull("access_token")  ?? throw new Exception("No access_token"),
            RefreshToken = data.GetStringOrNull("refresh_token"),
            ExpiresAt    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                           (data.GetLongOrNull("expires_in") ?? 3600) * 1000,
            AccountId    = data.GetStringOrNull("user_id"),
        };
        _db.Db.Accounts["gog"] = account;
        _db.Save();
        Log.Information("[auth] GOG authenticated (user_id={Id})", account.AccountId);
        return account;
    }

    public async Task<bool> RefreshGogTokenAsync(CancellationToken ct = default)
    {
        var account = _db.Db.Accounts.GetValueOrDefault("gog");
        if (account?.RefreshToken is null) return false;

        var url = $"{Cfg.Gog.TokenUrl}?client_id={Cfg.Gog.ClientId}&client_secret={Cfg.Gog.ClientSecret}" +
                  $"&grant_type=refresh_token&refresh_token={Uri.EscapeDataString(account.RefreshToken)}";
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            var data = await ParseJsonAsync(resp, ct);
            account.AccessToken  = data.GetStringOrNull("access_token") ?? account.AccessToken;
            account.RefreshToken = data.GetStringOrNull("refresh_token") ?? account.RefreshToken;
            account.ExpiresAt    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                                   (data.GetLongOrNull("expires_in") ?? 3600) * 1000;
            _db.Save();
            return true;
        }
        catch { return false; }
    }

    // ─── Epic ─────────────────────────────────────────────────────────────────

    private async Task<AccountInfo> CompleteEpicAuthAsync(string exchangeCode, CancellationToken ct)
    {
        var basic = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{Cfg.Epic.ClientId}:{Cfg.Epic.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, Cfg.Epic.TokenUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "exchange_code",
            ["exchange_code"] = exchangeCode,
            ["token_type"] = "eg1",
        });
        using var resp = await _http.SendAsync(req, ct);
        var data = await ParseJsonAsync(resp, ct);

        var account = new AccountInfo
        {
            AccessToken  = data.GetStringOrNull("access_token") ?? throw new Exception("No access_token"),
            RefreshToken = data.GetStringOrNull("refresh_token"),
            ExpiresAt    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                           (data.GetLongOrNull("expires_in") ?? 3600) * 1000,
            AccountId    = data.GetStringOrNull("account_id"),
            Username     = data.GetStringOrNull("displayName") ?? data.GetStringOrNull("display_name"),
        };
        _db.Db.Accounts["epic"] = account;
        _db.Save();
        Log.Information("[auth] Epic authenticated as {Name}", account.Username);
        return account;
    }

    // ─── Xbox / Microsoft ─────────────────────────────────────────────────────

    private async Task<AccountInfo> CompleteXboxAuthAsync(string code, CancellationToken ct)
    {
        // 1. Exchange code for MS token
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, Cfg.Xbox.TokenUrl);
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]    = Cfg.Xbox.ClientId,
            ["grant_type"]   = "authorization_code",
            ["code"]         = code,
            ["redirect_uri"] = Cfg.Xbox.RedirectUri,
            ["scope"]        = Cfg.Xbox.Scope,
        });
        using var tokenResp = await _http.SendAsync(tokenReq, ct);
        var ms = await ParseJsonAsync(tokenResp, ct);
        var msToken = ms.GetStringOrNull("access_token") ?? throw new Exception("MS token exchange failed");

        // 2. Authenticate with Xbox Live
        var xblBody = JsonSerializer.Serialize(new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        });
        using var xblReq = new HttpRequestMessage(HttpMethod.Post, Cfg.Xbox.XblAuthUrl);
        xblReq.Headers.Add("x-xbl-contract-version", "1");
        xblReq.Content = new StringContent(xblBody, Encoding.UTF8, "application/json");
        using var xblResp = await _http.SendAsync(xblReq, ct);
        var xbl = await ParseJsonAsync(xblResp, ct);
        var xblToken  = xbl.GetStringOrNull("Token")                            ?? throw new Exception("XBL auth failed");
        var userHash  = xbl.GetNestedString("DisplayClaims", "xui", 0, "uhs")  ?? "";

        // 3. Authenticate with XSTS
        var xstsBody = JsonSerializer.Serialize(new
        {
            Properties   = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "http://xboxlive.com",
            TokenType    = "JWT",
        });
        using var xstsReq = new HttpRequestMessage(HttpMethod.Post, Cfg.Xbox.XstsAuthUrl);
        xstsReq.Headers.Add("x-xbl-contract-version", "1");
        xstsReq.Content = new StringContent(xstsBody, Encoding.UTF8, "application/json");
        using var xstsResp = await _http.SendAsync(xstsReq, ct);
        var xsts = await ParseJsonAsync(xstsResp, ct);
        var xstsToken = xsts.GetStringOrNull("Token")                            ?? throw new Exception("XSTS auth failed");
        var gamertag  = xsts.GetNestedString("DisplayClaims", "xui", 0, "gtg") ?? "";
        var xuid      = xsts.GetNestedString("DisplayClaims", "xui", 0, "xid") ?? "";

        var account = new AccountInfo
        {
            AccessToken  = xblToken,
            RefreshToken = ms.GetStringOrNull("refresh_token"),
            ExpiresAt    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                           (ms.GetLongOrNull("expires_in") ?? 3600) * 1000,
            AccountId    = xuid,
            Username     = gamertag,
            Extra        = new Dictionary<string, object?> { ["xstsToken"] = xstsToken, ["userHash"] = userHash },
        };
        _db.Db.Accounts["xbox"] = account;
        _db.Save();
        _creds.SetPassword("cereal", "xbox_xsts", xstsToken);
        Log.Information("[auth] Xbox authenticated as {Gamertag} (xuid={Xuid})", gamertag, xuid);
        return account;
    }

    // ─── Stored account access ────────────────────────────────────────────────

    public AccountInfo? GetAccount(string platform) =>
        _db.Db.Accounts.GetValueOrDefault(platform);

    public void SignOut(string platform)
    {
        _db.Db.Accounts.Remove(platform);
        _db.Save();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string ToQueryString(Dictionary<string, string> p) =>
        string.Join("&", p.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}

// Extension helpers for JsonElement
internal static class JsonElementExt
{
    public static string? GetStringOrNull(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static long? GetLongOrNull(this JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt64(out var l) ? l : null;

    public static string? GetNestedString(this JsonElement e, string key1, string key2, int arrayIndex, string key3)
    {
        if (!e.TryGetProperty(key1, out var o1)) return null;
        if (!o1.TryGetProperty(key2, out var arr)) return null;
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() <= arrayIndex) return null;
        var item = arr[arrayIndex];
        return item.TryGetProperty(key3, out var v) ? v.GetString() : null;
    }
}
