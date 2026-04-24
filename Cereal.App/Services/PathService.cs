using System.Reflection;
using Serilog;

namespace Cereal.App.Services;

public class PathService
{
    public string AppDataDir { get; }
    public string CoversDir { get; }
    public string HeadersDir { get; }
    public string DatabasePath { get; }
    public string LogsDir { get; }
    public string ResourcesDir { get; }

    public PathService()
    {
        AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cereal");

        CoversDir = Path.Combine(AppDataDir, "covers");
        HeadersDir = Path.Combine(AppDataDir, "headers");
        LogsDir = Path.Combine(AppDataDir, "logs");
        DatabasePath = Path.Combine(AppDataDir, "games.json");

        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        ResourcesDir = Path.Combine(exeDir, "resources");

        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(CoversDir);
        Directory.CreateDirectory(HeadersDir);
        Directory.CreateDirectory(LogsDir);
    }

    // Cover/header files are written by CoverService using `cover_<id>.<ext>` and
    // `header_<id>.<ext>` so we can round-trip any image extension. Resolve an
    // existing file if there is one, else fall back to the canonical `.jpg` path.
    public string GetCoverPath(string gameId) => ResolveOrDefault(CoversDir, "cover_", gameId);
    public string GetHeaderPath(string gameId) => ResolveOrDefault(HeadersDir, "header_", gameId);

    private static string ResolveOrDefault(string dir, string prefix, string gameId)
    {
        var safe = SanitizeId(gameId);
        try
        {
            var matches = Directory.EnumerateFiles(dir, $"{prefix}{safe}.*");
            var first = matches.FirstOrDefault();
            if (first is not null) return first;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[paths] Failed resolving asset path in {Dir}", dir);
        }
        return Path.Combine(dir, $"{prefix}{safe}.jpg");
    }

    public string GetResourcePath(string relativePath) =>
        Path.Combine(ResourcesDir, relativePath);

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
