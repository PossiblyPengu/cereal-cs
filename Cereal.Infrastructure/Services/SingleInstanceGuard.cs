// Single-instance guard using a named OS Mutex.
// The primary instance watches a wake-file; secondary instances write it and exit.

namespace Cereal.Infrastructure.Services;

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

    /// <summary>Secondary instance: write the wake file then exit.</summary>
    public static void SignalWake()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WakeFile)!);
            File.WriteAllText(WakeFile, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex) { Log.Warning(ex, "[single-instance] Failed to write wake file"); }
    }

    /// <summary>Primary instance: watch for wake signals from secondary launches.</summary>
    public void StartWatching()
    {
        if (!IsPrimary) return;
        try
        {
            var dir = Path.GetDirectoryName(WakeFile)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(WakeFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => WakeRequested?.Invoke(this, EventArgs.Empty);
            _watcher.Created += (_, _) => WakeRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Log.Warning(ex, "[single-instance] Failed to start watching"); }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        try
        {
            if (_owned) _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        catch (Exception ex) { Log.Debug(ex, "[single-instance] Mutex dispose error"); }
    }
}
