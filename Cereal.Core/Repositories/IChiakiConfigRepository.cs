using Cereal.Core.Models;

namespace Cereal.Core.Repositories;

public interface IChiakiConfigRepository
{
    Task<ChiakiConfig> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ChiakiConfig config, CancellationToken ct = default);
}
