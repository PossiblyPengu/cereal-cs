namespace Cereal.Infrastructure.Database.Migrations;

public interface IMigration
{
    int Version { get; }
    void Apply(IDbConnection conn, IDbTransaction tx);
}
