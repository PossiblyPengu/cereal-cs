using Cereal.Core.Models;

namespace Cereal.Core.Services;

public interface IAuthService
{
    /// <summary>True when the platform has a stored, non-expired token.</summary>
    bool IsAuthenticated(string platform);

    /// <summary>Returns the in-memory token (null if not authenticated).</summary>
    AuthSession? GetSession(string platform);

    /// <summary>
    /// Start an OAuth flow for the platform.
    /// Opens a browser / embedded WebView and resolves with the token when the flow completes.
    /// </summary>
    Task<AuthSession> AuthenticateAsync(string platform, CancellationToken ct = default);

    /// <summary>Refresh an expired access token using the stored refresh token.</summary>
    Task<AuthSession?> TryRefreshAsync(string platform, CancellationToken ct = default);

    /// <summary>Sign out: revoke tokens and delete stored credentials.</summary>
    Task SignOutAsync(string platform, CancellationToken ct = default);
}
