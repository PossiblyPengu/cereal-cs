// Windows run-key integration — writes/clears HKCU\...\Run for launch-on-startup.
// Non-Windows: no-op (startup management is platform-specific).

namespace Cereal.Infrastructure.Services;

public static class StartupService
{
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "CerealLauncher";

    public static void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                         ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath ?? "";
                if (string.IsNullOrWhiteSpace(exe)) return;
                key.SetValue(EntryName, $"\"{exe}\" --autostart");
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

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(EntryName) is not null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[startup] Failed to query startup state");
            return false;
        }
    }
}
