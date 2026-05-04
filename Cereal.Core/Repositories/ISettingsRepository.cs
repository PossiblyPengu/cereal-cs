using Cereal.Core.Models;

namespace Cereal.Core.Repositories;

public interface ISettingsRepository
{
    Task<Settings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(Settings settings, CancellationToken ct = default);
}
