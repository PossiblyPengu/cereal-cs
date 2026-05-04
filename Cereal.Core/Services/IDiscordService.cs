namespace Cereal.Core.Services;

/// <summary>
/// Discord Rich Presence integration.
/// </summary>
public interface IDiscordService : IDisposable
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();

    /// <summary>Update presence to show the given game is being played.</summary>
    void SetPresence(string gameName, string platform, string? coverUrl = null,
        DateTimeOffset? startedAt = null);

    /// <summary>Clear presence (e.g. game stopped).</summary>
    void ClearPresence();
}
