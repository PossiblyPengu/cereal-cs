using System.Diagnostics;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.Infrastructure.Services.Integrations;

/// <summary>
/// Thin wrapper over the existing Chiaki CLI.
/// Delegates to the Chiaki executable; discovery is done via UDP broadcast.
/// </summary>
public sealed class ChiakiService : IChiakiService
{
    private readonly ISettingsService _settings;

    public ChiakiService(ISettingsService settings) => _settings = settings;

    public bool IsAvailable
    {
        get
        {
            var s = _settings.LoadAsync().GetAwaiter().GetResult();
            return !string.IsNullOrEmpty(s.ChiakiPath) && File.Exists(s.ChiakiPath);
        }
    }

    public async Task LaunchRemoteAsync(string gameId, string host,
        string? psnAccountId = null, CancellationToken ct = default)
    {
        var s = await _settings.LoadAsync(ct);
        var chiaki = s.ChiakiPath;
        if (string.IsNullOrEmpty(chiaki) || !File.Exists(chiaki))
        {
            Log.Warning("[chiaki] Chiaki path not configured or not found");
            return;
        }

        var args = $"stream --host {host}";
        if (!string.IsNullOrEmpty(psnAccountId))
            args += $" --regist-key {psnAccountId}";

        Log.Information("[chiaki] Launching: {Path} {Args}", chiaki, args);
        using var proc = Process.Start(new ProcessStartInfo(chiaki, args)
        {
            UseShellExecute = false,
            CreateNoWindow  = true,
        });

        if (proc is not null)
            await proc.WaitForExitAsync(ct);
    }

    public Task<IReadOnlyList<DiscoveredConsoleInfo>> DiscoverAsync(CancellationToken ct = default)
    {
        // Discovery requires UDP broadcast + parsing — Phase H
        Log.Information("[chiaki] Console discovery not yet implemented");
        return Task.FromResult<IReadOnlyList<DiscoveredConsoleInfo>>([]);
    }
}
