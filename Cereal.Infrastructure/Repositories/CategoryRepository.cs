using Cereal.Infrastructure.Database;

namespace Cereal.Infrastructure.Repositories;

public sealed class CategoryRepository(CerealDb db) : ICategoryRepository
{
    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = db.Open();
        return (await conn.QueryAsync<string>("SELECT Name FROM Categories ORDER BY Name COLLATE NOCASE")).ToList();
    }

    public async Task EnsureExistsAsync(string name, CancellationToken ct = default)
    {
        using var conn = db.Open();
        await conn.ExecuteAsync("INSERT OR IGNORE INTO Categories(Name) VALUES (@name)", new { name });
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        using var conn = db.Open();
        // GameCategories rows are deleted by ON DELETE CASCADE.
        await conn.ExecuteAsync("DELETE FROM Categories WHERE Name = @name", new { name });
    }

    public async Task RenameAsync(string oldName, string newName, CancellationToken ct = default)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "INSERT OR IGNORE INTO Categories(Name) VALUES (@newName)", new { newName }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE GameCategories
            SET CategoryName = @newName
            WHERE CategoryName = @oldName
            """, new { newName, oldName }, tx);
        await conn.ExecuteAsync(
            "DELETE FROM Categories WHERE Name = @oldName", new { oldName }, tx);
        tx.Commit();
    }
}
