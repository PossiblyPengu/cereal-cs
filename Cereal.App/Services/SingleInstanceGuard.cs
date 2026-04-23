// ─── Single-instance guard ───────────────────────────────────────────────────
// Uses a named OS Mutex (global per-user) to detect if another copy of Cereal
// is already running. When a second launch happens, we write a "wake" marker
// file that the primary instance watches so it can raise + focus its window.

using System.IO;
using Serilog;

namespace Cereal.App.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\Cereal.Launcher.SingleInstance";
    private static readonly string WakeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cereal", ".wake");

    private readonly Mutex _mutex;
    private readonly bool _owned;
    private FileSystemWatcher? _watcher;

    public bool IsPrimary => _owned;

    public event EventHandler? WakeRequested;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _owned);
    }

    // Called by the secondary instance: drop a marker file that the primary watches.
    public static void SignalWake()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WakeFile)!);
            File.WriteAllText(WakeFile, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex) { Log.Warning(ex, "[single-instance] Failed to write wake file"); }
    }

    // Called by the primary instance to listen for wake signals.
    public void StartWatching()
    {
        if (!IsPrimary) return;
        try
        {
            var dir = Path.GetDirectoryName(WakeFile)!;
            Directory.CreateDirectory(dir);

            _watcher = new FileSystemWatcher(dir, ".wake")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => WakeRequested?.Invoke(this, EventArgs.Empty);
            _watcher.Created += (_, _) => WakeRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Log.Warning(ex, "[single-instance] Failed to watch wake file"); }
    }

    public void Dispose()
    {
        try { _watcher?.Dispose(); } catch { /* best-effort */ }
        try
        {
            if (_owned) _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        catch { /* best-effort */ }
    }
}
