using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Providers;
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

        // Integration services
        sc.AddSingleton<DiscordService>();
        sc.AddSingleton<ChiakiService>();
        sc.AddSingleton<XcloudService>();
        sc.AddSingleton<SmtcService>();
        sc.AddSingleton<CoverService>();
        sc.AddSingleton<ThemeService>();

        // Platform providers (registered both by concrete type and as IImportProvider)
        sc.AddSingleton<SteamProvider>();
        sc.AddSingleton<EpicProvider>();
        sc.AddSingleton<GogProvider>();

        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<SteamProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<EpicProvider>());
        sc.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<GogProvider>());

        // Local detect-only providers (BattleNet, EA, Ubisoft, itch.io, Xbox)
        sc.AddSingleton<BattleNetProvider>();
        sc.AddSingleton<EaProvider>();
        sc.AddSingleton<UbisoftProvider>();
        sc.AddSingleton<ItchioProvider>();
        sc.AddSingleton<XboxProvider>();

        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<BattleNetProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<EaProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<UbisoftProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<ItchioProvider>());
        sc.AddSingleton<IProvider>(sp => sp.GetRequiredService<XboxProvider>());

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

        var covers = Services.GetRequiredService<CoverService>();
        _ = Task.Run(covers.EnqueueAll);

        Services.GetRequiredService<ChiakiService>().AutoSetupIfMissing();

        // Background update check
        _ = Task.Run(() => Services.GetRequiredService<UpdateService>().CheckAsync());

        Log.Information("[startup] Startup sequence complete");
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
