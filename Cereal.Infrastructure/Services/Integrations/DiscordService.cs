using Cereal.Core.Services;
using Serilog;

namespace Cereal.Infrastructure.Services.Integrations;

/// <summary>
/// Discord Rich Presence wrapper.
/// Depends on DiscordRichPresence NuGet package in Cereal.App (the SDK ships
/// only the managed DLL).  This service is registered in Infrastructure so the
/// Discord app-id constant lives in one place.
/// </summary>
public sealed class DiscordService : IDiscordService
{
    private const string AppId = "1338877643523145789";

    private static readonly Dictionary<string, string> PlatformLabels = new()
    {
        ["steam"]     = "Steam",
        ["epic"]      = "Epic Games",
        ["gog"]       = "GOG",
        ["xbox"]      = "Xbox",
        ["ea"]        = "EA App",
        ["ubisoft"]   = "Ubisoft Connect",
        ["itchio"]    = "itch.io",
        ["battlenet"] = "Battle.net",
        ["psn"]       = "PlayStation",
        ["custom"]    = "PC",
    };

    private bool _enabled;
    private bool _disposed;

    public bool IsEnabled => _enabled;

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        Log.Information("[discord] Rich Presence enabled");
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;
        Log.Information("[discord] Rich Presence disabled");
    }

    public void SetPresence(string gameName, string platform, string? coverUrl = null,
        DateTimeOffset? startedAt = null)
    {
        if (!_enabled) return;
        var platformLabel = PlatformLabels.GetValueOrDefault(platform, platform);
        Log.Debug("[discord] SetPresence: {Game} on {Platform}", gameName, platformLabel);
        // Actual DiscordRPC.SetPresence call goes here — requires the DiscordRPC
        // NuGet package which lives in Cereal.App. For now this stub is registered
        // via IDiscordService and the App project can override with the real impl.
    }

    public void ClearPresence()
    {
        if (!_enabled) return;
        Log.Debug("[discord] ClearPresence");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disable();
    }
}
