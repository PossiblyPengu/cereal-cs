namespace Cereal.Core.Models;

/// <summary>
/// Non-secret account metadata stored in the database.
/// Access tokens and refresh tokens are NEVER stored here;
/// they live in <c>ICredentialService</c> (DPAPI-backed on Windows).
/// </summary>
public sealed record AccountInfo
{
    public string Platform { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? AccountId { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    /// <summary>Token expiry as Unix milliseconds (used to decide whether to refresh, not for display).</summary>
    public long? ExpiresAt { get; set; }
    public long? LastSyncMs { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// In-memory-only token bundle.  Never serialised to disk.
/// Populated by <c>ICredentialService</c> at startup and after OAuth flows.
/// </summary>
public sealed record AuthSession(
    string Platform,
    string AccessToken,
    string? RefreshToken,
    long? ExpiresAt);
