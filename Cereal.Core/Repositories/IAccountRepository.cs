using Cereal.Core.Models;

namespace Cereal.Core.Repositories;

public interface IAccountRepository
{
    Task<IReadOnlyList<AccountInfo>> GetAllAsync(CancellationToken ct = default);
    Task<AccountInfo?> GetAsync(string platform, CancellationToken ct = default);
    /// <summary>Upsert non-secret account metadata.  Tokens are stored separately via ICredentialService.</summary>
    Task SaveAsync(AccountInfo account, CancellationToken ct = default);
    Task DeleteAsync(string platform, CancellationToken ct = default);
}
