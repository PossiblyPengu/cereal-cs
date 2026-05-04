namespace Cereal.Infrastructure;

/// <summary>
/// Resolves well-known paths under <c>%AppData%\Cereal</c>.
/// Creates required directories on first access.
/// </summary>
public sealed class PathService
{
    private readonly string _appDataDir;

    public PathService()
    {
        _appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cereal");
        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(CoversDir);
        Directory.CreateDirectory(LogsDir);
    }

    public string AppDataDir => _appDataDir;

    /// <summary>SQLite database file.</summary>
    public string DatabasePath => Path.Combine(_appDataDir, "cereal.db");

    /// <summary>Cover art cache directory.</summary>
    public string CoversDir => Path.Combine(_appDataDir, "covers");

    /// <summary>Serilog rolling log directory.</summary>
    public string LogsDir => Path.Combine(_appDataDir, "logs");

    /// <summary>Legacy games.json from the previous JSON-based persistence layer.</summary>
    public string LegacyJsonPath => Path.Combine(_appDataDir, "games.json");

    /// <summary>
    /// Directory containing runtime resources bundled with the app
    /// (e.g. PowerShell helper scripts).
    /// </summary>
    public string ResourcesDir => Path.Combine(AppContext.BaseDirectory, "resources");
}
