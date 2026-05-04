using Cereal.Core.Services;
using Serilog;

namespace Cereal.Infrastructure.Services.Integrations;

/// <summary>
/// Xbox Cloud Gaming (xCloud) session launcher.
/// Uses the Xbox Game Pass WebView URI scheme.
/// </summary>
public sealed class XcloudService : IXcloudService
{
    private readonly IAuthService _auth;

    public XcloudService(IAuthService auth) => _auth = auth;

    public bool IsAuthenticated => _auth.IsAuthenticated("xbox");

    public Task LaunchAsync(string titleId, CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            Log.Warning("[xcloud] Cannot launch — Xbox not authenticated");
            return Task.CompletedTask;
        }

        // Xbox Cloud Gaming uses the ms-xgpuweb:// URI scheme
        var uri = $"ms-xgpuweb://play/{titleId}";
        Log.Information("[xcloud] Launching title {TitleId} via URI {Uri}", titleId, uri);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[xcloud] LaunchAsync failed for {TitleId}", titleId);
        }

        return Task.CompletedTask;
    }
}
