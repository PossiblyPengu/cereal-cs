// ─── Windows Run-key integration ─────────────────────────────────────────────
// Writes/clears an entry under HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// so Cereal is auto-launched at user logon. Uses per-user key (no admin needed).

using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace Cereal.App.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "CerealLauncher";

    public static void ApplyLaunchOnStartup(bool enabled)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath ?? "";
                if (string.IsNullOrWhiteSpace(exe)) return;
                // Quote path + pass a lightweight flag so we know it's the auto-launch.
                key.SetValue(EntryName, "\"" + exe + "\" --autostart");
                Log.Information("[startup] Registered launch-on-startup: {Exe}", exe);
            }
            else
            {
                key.DeleteValue(EntryName, throwOnMissingValue: false);
                Log.Information("[startup] Removed launch-on-startup entry");
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[startup] Failed to toggle launch-on-startup"); }
    }

    public static bool IsLaunchOnStartupEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(EntryName) is not null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[startup] Failed to query launch-on-startup state");
            return false;
        }
    }
}
