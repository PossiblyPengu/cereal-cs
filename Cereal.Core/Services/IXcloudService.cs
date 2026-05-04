namespace Cereal.Core.Services;

/// <summary>
/// Xbox Cloud Gaming (xCloud) session management.
/// </summary>
public interface IXcloudService
{
    /// <summary>Open an xCloud session for the given title id.</summary>
    Task LaunchAsync(string titleId, CancellationToken ct = default);

    /// <summary>Returns true if the Xbox auth session is active.</summary>
    bool IsAuthenticated { get; }
}
