using System.Diagnostics;
using System.Runtime.InteropServices;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.Infrastructure.Services.Integrations;

/// <summary>
/// SMTC / media key integration.  Windows-only; all methods are no-ops on other platforms.
/// </summary>
public sealed class SmtcService : ISmtcService
{
    // Win32 VK codes for media keys
    private const byte VK_MEDIA_PLAY_PAUSE  = 0xB3;
    private const byte VK_MEDIA_STOP        = 0xB2;
    private const byte VK_MEDIA_NEXT_TRACK  = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK  = 0xB1;
    private const byte VK_VOLUME_UP         = 0xAF;
    private const byte VK_VOLUME_DOWN       = 0xAE;
    private const byte VK_VOLUME_MUTE       = 0xAD;
    private const byte KEYEVENTF_EXTENDEDKEY = 0x01;
    private const byte KEYEVENTF_KEYUP       = 0x02;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, byte dwFlags, int dwExtraInfo);

    public void SendMediaKey(MediaKey key)
    {
        if (!OperatingSystem.IsWindows()) return;
        byte vk = key switch
        {
            MediaKey.PlayPause  => VK_MEDIA_PLAY_PAUSE,
            MediaKey.Stop       => VK_MEDIA_STOP,
            MediaKey.Next       => VK_MEDIA_NEXT_TRACK,
            MediaKey.Previous   => VK_MEDIA_PREV_TRACK,
            MediaKey.VolumeUp   => VK_VOLUME_UP,
            MediaKey.VolumeDown => VK_VOLUME_DOWN,
            MediaKey.Mute       => VK_VOLUME_MUTE,
            _                   => 0,
        };
        if (vk == 0) return;
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
    }

    public Task<SmtcSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default)
    {
        // Full WinRT SMTC query requires Windows-specific APIs or a sub-process.
        // Phase H implementation will use GlobalSystemMediaTransportControlsSessionManager.
        Log.Debug("[smtc] GetCurrentSessionAsync — stub");
        return Task.FromResult<SmtcSessionInfo?>(null);
    }
}
