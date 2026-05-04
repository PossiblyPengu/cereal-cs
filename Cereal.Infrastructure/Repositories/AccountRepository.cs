using Cereal.Infrastructure.Database;

namespace Cereal.Infrastructure.Repositories;

/// <summary>
/// Persists non-secret account metadata (username, avatar, etc.).
/// Access tokens and refresh tokens are stored separately via <see cref="ICredentialService"/>.
/// </summary>
public sealed class AccountRepository(CerealDb db) : IAccountRepository
{
    public async Task<IReadOnlyList<AccountInfo>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = db.Open();
        return (await conn.QueryAsync<AccountInfo>("SELECT * FROM Accounts")).ToList();
    }

    public async Task<AccountInfo?> GetAsync(string platform, CancellationToken ct = default)
    {
        using var conn = db.Open();
        return await conn.QuerySingleOrDefaultAsync<AccountInfo>(
            "SELECT * FROM Accounts WHERE Platform = @platform", new { platform });
    }

    public async Task SaveAsync(AccountInfo account, CancellationToken ct = default)
    {
        var row = account with { UpdatedAt = DateTimeOffset.UtcNow };
        using var conn = db.Open();
        await conn.ExecuteAsync("""
            INSERT OR REPLACE INTO Accounts (
                Platform, Username, AccountId, DisplayName, AvatarUrl,
                ExpiresAt, LastSyncMs, UpdatedAt
            ) VALUES (
                @Platform, @Username, @AccountId, @DisplayName, @AvatarUrl,
                @ExpiresAt, @LastSyncMs, @UpdatedAt
            )
            """, row);
    }

    public async Task DeleteAsync(string platform, CancellationToken ct = default)
    {
        using var conn = db.Open();
        await conn.ExecuteAsync("DELETE FROM Accounts WHERE Platform = @platform", new { platform });
    }
}
