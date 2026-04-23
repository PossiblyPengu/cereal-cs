using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Metadata;
using Cereal.App.Services.Providers;
using Cereal.App.ViewModels;
using Cereal.App.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App;

public partial class App : Application
{
    /// <summary>Global service provider — available after OnFrameworkInitializationCompleted.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    // Tray icon commands (bound in App.axaml)
    public static readonly CommunityToolkit.Mvvm.Input.RelayCommand ShowWindowCommand = new(ShowWindow);
    public static readonly CommunityToolkit.Mvvm.Input.RelayCommand QuitCommand       = new(Quit);

    public override void RegisterServices()
    {
        base.RegisterServices();
        AvaloniaWebViewBuilder.Initialize(default);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServices();
        RunStartupSequence();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownRequested += OnShutdownRequested;

            window.Opened += async (_, _) =>
            {
                var settings = Services.GetRequiredService<SettingsService>();
                if (settings.Get().FirstRun)
                {
                    var wizard = new StartupWizardDialog();
                    await wizard.ShowDialog(window);
                }
            };

            // Bring window to foreground when a secondary launch pokes us.
            if (Program.InstanceGuard is { } guard)
            {
                guard.StartWatching();
                guard.WakeRequested += (_, _) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(ShowWindow);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ─── Service registration ─────────────────────────────────────────────────

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();

        // Infrastructure
        sc.AddSingleton<PathService>();
        sc.AddSingleton<DatabaseService>();

        // Core services
        sc.AddSingleton<GameService>();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<CredentialService>();
        sc.AddSingleton<AuthService>();
        sc.AddSingleton<LaunchService>();
        sc.AddSingleton<UpdateService>();
        sc.AddSingleton<PlaytimeSyncService>();
        sc.AddSingleton<GamepadService>();

        // Integration services
        sc.AddSingleton<DiscordService>();
        sc.AddSingleton<ChiakiService>();
        sc.AddSingleton<XcloudService>();
        sc.AddSingleton<SmtcService>();
        sc.AddSingleton<CoverService>();
        sc.AddSingleton<ThemeService>();
        sc.AddSingleton<MetadataService>();

        // Platform providers (registered both by concrete type and as IImportProvider)
        sc.AddSingleton<SteamProvider>();
        sc.AddSingleton<EpicProvider>();
        sc.AddSingleton<GogProvider>();

        // itch.io + Xbox also implement IImportProvider now (API / Title Hub imports).
        sc.AddSingleton<BattleNetProvider>();
        sc.AddSingleton<EaProvider>();
        sc.AddSingleton<UbisoftProvider>();
        sc.AddSingleton<ItchioProvider>();
        sc.AddSingleton<XboxProvider>();

        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<SteamProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<EpicProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<GogProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<ItchioProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<XboxProvider>());

        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<BattleNetProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<EaProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<UbisoftProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<ItchioProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<XboxProvider>());

        // View models with DI-wired dependencies
        sc.AddSingleton<PlatformsPanelViewModel>();

        return sc.BuildServiceProvider();
    }

    // ─── Startup sequence ─────────────────────────────────────────────────────

    private static void RunStartupSequence()
    {
        var db = Services.GetRequiredService<DatabaseService>();
        db.Load();
        Log.Information("[startup] Database loaded — {Count} games", db.Db.Games.Count);

        Services.GetRequiredService<ThemeService>().ApplyCurrent();

        var settings = Services.GetRequiredService<SettingsService>().Get();
        if (settings.DiscordPresence)
            Services.GetRequiredService<DiscordService>().Connect();

        // Keep the Windows Run key in sync with the persisted setting.
        StartupService.ApplyLaunchOnStartup(settings.LaunchOnStartup);

        var covers = Services.GetRequiredService<CoverService>();
        _ = Task.Run(covers.EnqueueAll);

        // Auto-sync Steam playtime if enabled: one sync at startup + every 30 min
        // (matches src/App.tsx 303-319 — setTimeout(sync,3000) + setInterval(30min)).
        if (settings.AutoSyncPlaytime)
        {
            _ = Task.Run(async () =>
            {
                async Task RunOnce()
                {
                    try
                    {
                        var result = await Services.GetRequiredService<PlaytimeSyncService>().SyncAsync();
                        if (result.UpdatedCount > 0)
                            Log.Information("[playtime] Auto-sync updated {Count} game(s)", result.UpdatedCount);
                    }
                    catch (Exception ex) { Log.Warning(ex, "[playtime] Auto-sync failed"); }
                }
                await Task.Delay(3_000);
                await RunOnce();
                var tick = TimeSpan.FromMinutes(30);
                while (true)
                {
                    await Task.Delay(tick);
                    if (!Services.GetRequiredService<SettingsService>().Get().AutoSyncPlaytime) continue;
                    await RunOnce();
                }
            });
        }

        Services.GetRequiredService<ChiakiService>().AutoSetupIfMissing();

        // Start gamepad polling in the background so the UI picks up controller
        // presses via MainViewModel.HandleGamepadAction without blocking startup.
        Services.GetRequiredService<GamepadService>().Start();

        // Background update check
        _ = Task.Run(() => Services.GetRequiredService<UpdateService>().CheckAsync());

        Log.Information("[startup] Startup sequence complete");
    }

    /// <summary>
    /// Best-effort clipboard read. Used by paste-key buttons that mirror the
    /// Electron version's `window.api.readClipboard()`.
    /// </summary>
    public static async Task<string> ReadClipboardTextAsync()
    {
        try
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is { } win)
            {
                var clip = TopLevel.GetTopLevel(win)?.Clipboard;
                if (clip is not null)
                {
                    #pragma warning disable CS0618 // GetTextAsync is marked obsolete but still works
                    return (await clip.GetTextAsync()) ?? string.Empty;
                    #pragma warning restore CS0618
                }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[App] ReadClipboardTextAsync failed"); }
        return string.Empty;
    }

    // ─── Tray icon handlers ───────────────────────────────────────────────────

    private static void ShowWindow()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var win = desktop.MainWindow;
            if (win is not null)
            {
                win.Show();
                win.Activate();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
            }
        }
    }

    private static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // ─── Shutdown ─────────────────────────────────────────────────────────────

    private static void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Log.Information("[shutdown] Flushing database…");
        try { Services.GetRequiredService<DatabaseService>().Flush(); }
        catch (Exception ex) { Log.Warning(ex, "[shutdown] DB flush failed"); }

        try { Services.GetRequiredService<DiscordService>().Dispose(); }
        catch { /* best-effort */ }
        try { Services.GetRequiredService<ChiakiService>().Dispose(); }
        catch { /* best-effort */ }
        try { Services.GetRequiredService<XcloudService>().Dispose(); }
        catch { /* best-effort */ }
        try { Services.GetRequiredService<CoverService>().Dispose(); }
        catch { /* best-effort */ }
    }
}
