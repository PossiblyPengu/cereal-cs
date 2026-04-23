// ─── System Media Transport Controls (SMTC) service ──────────────────────────
// Windows only. On Linux this returns null / no-ops gracefully.
//
// GetMediaInfo() — spawns MediaInfoTool.exe (from resources) to query the
//                  current SMTC session via WinRT GlobalSystemMediaTransportControls.
//                  We sub-process rather than call WinRT directly so the main
//                  project can stay on net8.0 (no -windows TFM required).
//
// SendMediaKey() — uses keybd_event P/Invoke directly; no sub-process needed.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Integrations;

public sealed class SmtcService
{
    private readonly PathService _paths;
    private string? _cachedToolPath;

    public SmtcService(PathService paths)
    {
        _paths = paths;
    }

    // ─── Media info ──────────────────────────────────────────────────────────

    public async Task<MediaInfo?> GetMediaInfoAsync(CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        var toolPath = ResolveToolPath();
        if (toolPath is null)
        {
            Log.Warning("[smtc] MediaInfoTool.exe not found in resources");
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo(toolPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi)!;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5_000);

            string? stdout = null;
            try
            {
                stdout = await p.StandardOutput.ReadToEndAsync(cts.Token);
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch { /* best-effort */ }
                Log.Warning("[smtc] GetMediaInfo timed out");
                return null;
            }

            if (string.IsNullOrWhiteSpace(stdout)) return null;

            using var doc = JsonDocument.Parse(stdout.Trim());
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                Log.Debug("[smtc] MediaInfoTool: {Error}", err.GetString());
                return null;
            }

            return new MediaInfo
            {
                Title    = root.TryGetProperty("title",    out var t) ? t.GetString() : null,
                Artist   = root.TryGetProperty("artist",   out var a) ? a.GetString() : null,
                Album    = root.TryGetProperty("album",    out var al) ? al.GetString() : null,
                IsPlaying = root.TryGetProperty("playing", out var pl) && pl.GetBoolean(),
                Position = root.TryGetProperty("position", out var pos) ? pos.GetDouble() : null,
                Duration = root.TryGetProperty("duration", out var dur) ? dur.GetDouble() : null,
                AlbumArtUrl = root.TryGetProperty("albumArt", out var art)
                    ? art.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[smtc] GetMediaInfo failed");
            return null;
        }
    }

    // ─── Media key ───────────────────────────────────────────────────────────

    public void SendMediaKey(string action)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        byte vk = action switch
        {
            "play" or "pause" or "playpause" => VK_MEDIA_PLAY_PAUSE,
            "next"                           => VK_MEDIA_NEXT_TRACK,
            "prev" or "previous"             => VK_MEDIA_PREV_TRACK,
            "stop"                           => VK_MEDIA_STOP,
            _ => 0,
        };
        if (vk == 0) return;

        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);  // KEYEVENTF_KEYUP
    }

    // ─── Tool path resolution ────────────────────────────────────────────────

    private string? ResolveToolPath()
    {
        if (_cachedToolPath is not null) return File.Exists(_cachedToolPath) ? _cachedToolPath : null;

        var candidates = new[]
        {
            _paths.GetResourcePath("MediaInfoTool.exe"),
            _paths.GetResourcePath("tools/MediaInfoTool.exe"),
            Path.Combine(AppContext.BaseDirectory, "MediaInfoTool.exe"),
        };

        foreach (var p in candidates)
        {
            if (File.Exists(p))
            {
                _cachedToolPath = p;
                return p;
            }
        }
        return null;
    }

    // ─── P/Invoke ────────────────────────────────────────────────────────────

    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_STOP       = 0xB2;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
