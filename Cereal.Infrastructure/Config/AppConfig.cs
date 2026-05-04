namespace Cereal.Infrastructure.Config;

/// <summary>
/// Loaded from <c>appsettings.json</c> + <c>appsettings.local.json</c> + environment variables.
/// Bound via <c>IConfiguration.GetSection</c>.
/// </summary>
public sealed class OAuthConfig
{
    public string GogClientId { get; set; } = "46899977096215655";
    /// <summary>Must be supplied via appsettings.local.json or CEREAL_OAUTH__GOGCLIENTSECRET env var.</summary>
    public string GogClientSecret { get; set; } = "";
    public string EpicClientId { get; set; } = "34a02cf8f4414e29b15921876da36f9a";
    /// <summary>Must be supplied via appsettings.local.json or CEREAL_OAUTH__EPICCLIENTSECRET env var.</summary>
    public string EpicClientSecret { get; set; } = "";
}

public sealed class DiscordConfig
{
    public string ApplicationId { get; set; } = "";
}

public sealed class AppConfig
{
    public OAuthConfig OAuth { get; set; } = new();
    public DiscordConfig Discord { get; set; } = new();
    /// <summary>
    /// TCP port for the OAuth localhost callback.
    /// Set to 0 to let the OS assign a random available port (recommended).
    /// </summary>
    public int OAuthCallbackPort { get; set; } = 0;
}
