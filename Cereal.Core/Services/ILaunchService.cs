using Cereal.Core.Models;

namespace Cereal.Core.Services;

public interface ILaunchService
{
    /// <summary>Launch a game using its platform-appropriate URI or executable path.</summary>
    Task LaunchAsync(Game game, CancellationToken ct = default);

    /// <summary>Stop any active playtime tracking session for the game.</summary>
    Task StopTrackingAsync(string gameId, CancellationToken ct = default);
}
