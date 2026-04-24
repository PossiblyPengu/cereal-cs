using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Cereal.App.Models;
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

        // React to settings changes so tray visibility stays in sync at runtime.
        Services.GetRequiredService<SettingsService>().SettingsSaved += (_, s) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTrayVisibility(s));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownRequested += OnShutdownRequested;

            window.Opened += async (_, _) =>
            {
                var settingsSvc = Services.GetRequiredService<SettingsService>();
                if (settingsSvc.Get().FirstRun)
                    await new StartupWizardDialog().ShowDialog(window);
                // Wizard (and older DBs without defaultView) may disagree with ctor — sync from disk.
                if (window.DataContext is MainViewModel mvm)
                    mvm.ViewMode = MainViewModel.NormalizeViewMode(settingsSvc.Get().DefaultView);
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
        sc.AddSingleton<DevDataService>();

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
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<BattleNetProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<EaProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<UbisoftProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<ItchioProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<XboxProvider>());

        // Register import-capable providers as IProvider too so status/detect
        // checks in Platforms panel work uniformly across all rows.
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<SteamProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<EpicProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<GogProvider>());
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
        Services.GetRequiredService<AuthService>().MigrateLegacySecrets();
        SeedDevPlaceholdersIfEnabled();

        Services.GetRequiredService<ThemeService>().ApplyCurrent();

        var settings = Services.GetRequiredService<SettingsService>().Get();
        if (settings.DiscordPresence)
            Services.GetRequiredService<DiscordService>().Connect();

        // Keep the Windows Run key in sync with the persisted setting.
        StartupService.ApplyLaunchOnStartup(settings.LaunchOnStartup);

        // Only show the system-tray icon when at least one tray feature is enabled,
        // matching Electron's create/destroy-on-setting behaviour.
        ApplyTrayVisibility(settings);

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

    private static void SeedDevPlaceholdersIfEnabled()
    {
        var clearRaw = Environment.GetEnvironmentVariable("CEREAL_DEV_PLACEHOLDERS_CLEAR");
        var clear = string.Equals(clearRaw, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(clearRaw, "true", StringComparison.OrdinalIgnoreCase);
        if (clear)
        {
            var removed = Services.GetRequiredService<DevDataService>().ClearPlaceholders();
            Log.Information("[startup] Cleared {Count} placeholder dev games", removed);
        }

        var raw = Environment.GetEnvironmentVariable("CEREAL_DEV_PLACEHOLDERS");
        if (!int.TryParse(raw, out var count) || count <= 0) return;

        var forceRaw = Environment.GetEnvironmentVariable("CEREAL_DEV_PLACEHOLDERS_FORCE");
        var force = string.Equals(forceRaw, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(forceRaw, "true", StringComparison.OrdinalIgnoreCase);

        var added = Services.GetRequiredService<DevDataService>().SeedPlaceholders(count, force);
        if (added > 0)
            Log.Information("[startup] Seeded {Count} placeholder dev games", added);
        else
            Log.Information("[startup] Placeholder seed requested but skipped (already present)");
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

    private static void ApplyTrayVisibility(Settings s)
    {
        var icons = TrayIcon.GetIcons(Current!);
        if (icons is null) return;
        var visible = s.CloseToTray || s.MinimizeToTray;
        foreach (var icon in icons)
            icon.IsVisible = visible;
    }

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
        catch (Exception ex) { Log.Debug(ex, "[shutdown] Discord dispose failed"); }
        try { Services.GetRequiredService<ChiakiService>().Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "[shutdown] Chiaki dispose failed"); }
        try { Services.GetRequiredService<XcloudService>().Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "[shutdown] Xcloud dispose failed"); }
        try { Services.GetRequiredService<CoverService>().Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "[shutdown] CoverService dispose failed"); }
    }
}
