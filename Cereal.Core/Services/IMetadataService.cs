using Cereal.Core.Models;

namespace Cereal.Core.Services;

public interface IMetadataService
{
    /// <summary>
    /// Fetch metadata for a game from the configured source (Steam, Wikipedia, etc.)
    /// and persist it via <see cref="IGameService.UpdateMetadataAsync"/>.
    /// </summary>
    Task FetchAndApplyAsync(string gameId, CancellationToken ct = default);
}
