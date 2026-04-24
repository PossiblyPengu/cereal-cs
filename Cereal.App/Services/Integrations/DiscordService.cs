using DiscordRPC;
using DiscordRPC.Logging;
using Serilog;

namespace Cereal.App.Services.Integrations;

public class DiscordService : IDisposable
{
    private const string ClientId = "1338877643523145789";

    private static readonly Dictionary<string, string> PlatformLabels = new()
    {
        ["steam"] = "Steam",
        ["epic"] = "Epic Games",
        ["gog"] = "GOG",
        ["psn"] = "PlayStation",
        ["xbox"] = "Xbox",
        ["custom"] = "PC",
        ["psremote"] = "PlayStation",
        ["battlenet"] = "Battle.net",
        ["ea"] = "EA App",
        ["ubisoft"] = "Ubisoft Connect",
        ["itchio"] = "itch.io",
    };

    /// <summary>
    /// Asset keys that must exist on the Discord application (Developer Portal → Rich Presence Artwork).
    /// Only these are used for <see cref="Assets.SmallImageKey"/>; unknown platforms omit the small asset
    /// so Discord does not show a broken icon.
    /// </summary>
    private static readonly HashSet<string> KnownSmallImageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "epic", "gog", "psn", "xbox", "custom", "battlenet", "ea", "ubisoft", "itchio", "psremote",
    };

    private DiscordRpcClient? _client;
    private bool _ready;

    public bool IsConnected => _client is not null;
    public bool IsReady => _ready;

    public void Connect()
    {
        if (_client is not null) return;
        try
        {
            var client = new DiscordRpcClient(ClientId, logger: new NullLogger());
            client.OnReady += (_, _) =>
            {
                _ready = true;
                Log.Information("[discord] RPC ready");
            };
            client.OnError += (_, e) =>
                Log.Warning("[discord] RPC error {Code}: {Message}", e.Code, e.Message);
            client.Initialize();
            _client = client;
            Log.Information("[discord] Connecting to Discord RPC");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[discord] Could not connect");
            _client = null;
        }
    }

    public void Disconnect()
    {
        if (_client is null) return;
        try { _client.ClearPresence(); }
        catch (Exception ex) { Log.Debug(ex, "[discord] ClearPresence during Disconnect failed"); }
        try { _client.Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "[discord] Dispose during Disconnect failed"); }
        _client = null;
        _ready = false;
    }

    public void SetPresence(string gameName, string platform, long? startTimestamp = null)
    {
        if (_client is null || !_ready) return;

        platform = string.IsNullOrWhiteSpace(platform) ? "custom" : platform.Trim().ToLowerInvariant();

        var state = "via " + (PlatformLabels.GetValueOrDefault(platform) ?? "Cereal Launcher");
        var tsStart = startTimestamp.HasValue
            ? Timestamps.FromUnixMilliseconds((ulong)startTimestamp.Value)
            : DateTime.UtcNow;
        var ts = new Timestamps(tsStart);

        try
        {
            var label = PlatformLabels.GetValueOrDefault(platform) ?? "Game";
            var assets = new Assets
            {
                LargeImageKey = "cereal_logo",
                LargeImageText = "Cereal Launcher",
            };
            if (KnownSmallImageKeys.Contains(platform))
            {
                assets.SmallImageKey = platform;
                assets.SmallImageText = label;
            }

            _client.SetPresence(new RichPresence
            {
                Details = gameName,
                State = state,
                Timestamps = ts,
                Assets = assets,
            });
            Log.Debug("[discord] Presence set: {Game} ({Platform})", gameName, platform);
        }
        catch (Exception ex) { Log.Warning(ex, "[discord] SetPresence failed"); }
    }

    public void ClearPresence()
    {
        if (_client is null || !_ready) return;
        try { _client.ClearPresence(); }
        catch (Exception ex) { Log.Warning(ex, "[discord] ClearPresence failed"); }
    }

    public object GetStatus() => new { ready = _ready, connected = IsConnected };

    public void Dispose() => Disconnect();
}
