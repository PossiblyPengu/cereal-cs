using Cereal.Infrastructure.Database.Migrations;

namespace Cereal.Infrastructure.Database;

/// <summary>
/// Connection factory and migration runner for Cereal's SQLite database.
/// Call <see cref="EnsureMigrated"/> once at startup before any repository use.
/// </summary>
public sealed class CerealDb
{
    private readonly string _dbPath;
    private readonly IReadOnlyList<IMigration> _migrations;

    static CerealDb()
    {
        // Register Dapper type handlers once at class load time.
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new JsonListHandler());
    }

    public CerealDb(PathService paths, IEnumerable<IMigration> migrations)
    {
        _dbPath = paths.DatabasePath;
        _migrations = [.. migrations.OrderBy(m => m.Version)];
    }

    /// <summary>Open a new, ready-to-use connection.  Caller is responsible for disposal.</summary>
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // foreign_keys must be enabled on every connection (connection-level pragma).
        // journal_mode=WAL is a persistent, file-level pragma — set once in EnsureMigrated.
        conn.Execute("PRAGMA foreign_keys=ON;");
        return conn;
    }

    /// <summary>Apply all pending migrations in a single transaction each.</summary>
    public void EnsureMigrated()
    {
        using var conn = Open();
        // WAL mode is persistent on the DB file; setting it here avoids the round-trip
        // on every subsequent connection open.
        conn.Execute("PRAGMA journal_mode=WAL;");

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version   INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            """);

        var current = conn.QuerySingleOrDefault<int?>(
            "SELECT MAX(Version) FROM SchemaVersion") ?? 0;

        foreach (var migration in _migrations.Where(m => m.Version > current))
        {
            Log.Information("[db] Applying migration v{Version}: {Name}", migration.Version, migration.GetType().Name);
            using var tx = conn.BeginTransaction();
            migration.Apply(conn, tx);
            conn.Execute(
                "INSERT INTO SchemaVersion(Version, AppliedAt) VALUES (@v, @t)",
                new { v = migration.Version, t = DateTimeOffset.UtcNow.ToString("O") },
                tx);
            tx.Commit();
        }
    }

    // ── Dapper type handlers ──────────────────────────────────────────────────

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter p, DateTimeOffset v) =>
            p.Value = v.ToString("O");
        public override DateTimeOffset Parse(object v) =>
            DateTimeOffset.Parse((string)v);
    }

    private sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override void SetValue(IDbDataParameter p, DateTimeOffset? v) =>
            p.Value = v.HasValue ? v.Value.ToString("O") : DBNull.Value;
        public override DateTimeOffset? Parse(object v) =>
            v is string s ? DateTimeOffset.Parse(s) : null;
    }

    private sealed class JsonListHandler : SqlMapper.TypeHandler<IReadOnlyList<string>>
    {
        public override void SetValue(IDbDataParameter p, IReadOnlyList<string>? v) =>
            p.Value = v is { Count: > 0 } ? JsonSerializer.Serialize(v) : DBNull.Value;
        public override IReadOnlyList<string> Parse(object v) =>
            v is string s && s.Length > 0
                ? JsonSerializer.Deserialize<List<string>>(s) ?? []
                : [];
    }
}
