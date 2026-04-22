using System.Reflection;

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

    public string GetCoverPath(string gameId) =>
        Path.Combine(CoversDir, $"{SanitizeId(gameId)}.jpg");

    public string GetHeaderPath(string gameId) =>
        Path.Combine(HeadersDir, $"{SanitizeId(gameId)}.jpg");

    public string GetResourcePath(string relativePath) =>
        Path.Combine(ResourcesDir, relativePath);

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
