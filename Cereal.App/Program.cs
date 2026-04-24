using Avalonia;
using Avalonia.WebView.Desktop;
using Cereal.App.Services;
using Serilog;
using Velopack;

namespace Cereal.App;

class Program
{
    // Held for the lifetime of the primary process.
    public static SingleInstanceGuard? InstanceGuard { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack MUST run before anything else. It handles update squirrel events
        // (install/uninstall hooks) and may exit the process immediately.
        VelopackApp.Build().Run();

        // Single-instance guard — if another Cereal is already running, signal it
        // and exit immediately. If mutex creation fails (environment policy /
        // ACL edge-cases), fail-open and continue startup instead of hard-exiting.
        try
        {
            InstanceGuard = new SingleInstanceGuard();
            if (!InstanceGuard.IsPrimary)
            {
                SingleInstanceGuard.SignalWake();
                InstanceGuard.Dispose();
                return;
            }
        }
        catch (Exception ex)
        {
            InstanceGuard = null;
            Console.Error.WriteLine($"[cereal] Single-instance guard unavailable, continuing startup: {ex.Message}");
        }

        // Bootstrap Serilog to a file next to the database so logs survive crashes.
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cereal", "logs", "cereal.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Cereal Launcher starting ===");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception — application terminating");
        }
        finally
        {
            Log.Information("=== Cereal Launcher stopped ===");
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseDesktopWebView();
}
