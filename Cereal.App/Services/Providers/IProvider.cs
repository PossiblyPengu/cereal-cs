using Cereal.App.Models;

namespace Cereal.App.Services.Providers;

public interface IProvider
{
    string PlatformId { get; }
    Task<DetectResult> DetectInstalled();
}

public interface IImportProvider : IProvider
{
    Task<ImportResult> ImportLibrary(ImportContext ctx);
}

public record DetectResult(List<Game> Games, string? Error = null);

public record ImportResult(
    List<string> Imported,
    List<string> Updated,
    int Total,
    string? Error = null);

public class ImportContext
{
    public DatabaseService Db { get; init; } = null!;
    public string? ApiKey { get; init; }
    public Action<ImportProgress>? Notify { get; init; }
    public HttpClient Http { get; init; } = null!;
}
