using Cereal.Core.Models;

namespace Cereal.Core.Providers;

/// <summary>Detects locally installed games for a single platform.</summary>
public interface IProvider
{
    /// <summary>Short platform identifier, e.g. "steam", "epic", "gog".</summary>
    string PlatformId { get; }

    Task<DetectResult> DetectInstalledAsync(CancellationToken ct = default);
}

/// <summary>Detects AND imports the full cloud library for a platform that supports OAuth.</summary>
public interface IImportProvider : IProvider
{
    Task<ImportResult> ImportLibraryAsync(ImportContext ctx, CancellationToken ct = default);
}

public sealed record DetectResult(IReadOnlyList<Game> Games, string? Error = null);

public sealed record ImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Updated,
    int Total,
    string? Error = null);

/// <summary>Context object passed to <see cref="IImportProvider.ImportLibraryAsync"/>.</summary>
public sealed class ImportContext
{
    public required IServiceProvider Services { get; init; }
    public string? ApiKey { get; init; }
    public Action<ImportProgress>? Notify { get; init; }
    public HttpClient Http { get; init; } = new();
}

/// <summary>Progress notification emitted during an import operation.</summary>
public sealed record ImportProgress(
    string Status,       // "running" | "done" | "error"
    string? Provider,
    int Processed,
    int Total,
    string? Name);
