using Cereal.Core.Models;

namespace Cereal.Core.Repositories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default);
    Task EnsureExistsAsync(string name, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
    Task RenameAsync(string oldName, string newName, CancellationToken ct = default);
}
