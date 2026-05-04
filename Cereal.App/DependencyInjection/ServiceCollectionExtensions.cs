using Cereal.App.ViewModels;
using Cereal.App.ViewModels.Dialogs;
using Cereal.App.ViewModels.Panels;
using Cereal.App.ViewModels.Settings;
using Cereal.Core.Services;
using Cereal.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.DependencyInjection;

/// <summary>
/// Builds the application's <see cref="IServiceProvider"/>.
/// Called once from Program.cs — no static App.Services accessor anywhere.
/// </summary>
public static class AppServiceCollectionExtensions
{
    public static IServiceProvider BuildAppServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables("CEREAL_")
            .AddInMemoryCollection(AppDefaults.ConfigValues)
            .Build();

        var services = new ServiceCollection();

        // Infrastructure layer (Core repos, DB, platform services, etc.)
        services.AddInfrastructure(configuration);

        // Shell + navigation
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<PanelRouterViewModel>();
        services.AddSingleton<SearchViewModel>();

        // Library
        services.AddSingleton<LibraryViewModel>();

        // Panels
        services.AddSingleton<FocusPanelViewModel>();
        services.AddTransient<Cereal.App.ViewModels.Panels.PlatformsPanelViewModel>();

        // Settings sections
        services.AddTransient<AppearanceSettingsViewModel>();
        services.AddTransient<LibrarySettingsViewModel>();
        services.AddTransient<AccountsSettingsViewModel>();
        services.AddTransient<AboutViewModel>();

        // Dialogs
        services.AddTransient<AddGameDialogViewModel>();
        services.AddTransient<ArtPickerDialogViewModel>();

        // Detect
        services.AddTransient<DetectViewModel>();

        // Top-level window
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Default configuration values embedded at build time.
/// Secrets (client ids / keys) come from environment variables or user settings;
/// never hardcoded here.
/// </summary>
internal static class AppDefaults
{
    public static readonly Dictionary<string, string?> ConfigValues = new()
    {
        // Discord Application ID (non-secret, visible in public OAuth2 page)
        ["Discord:ApplicationId"] = "1194000000000000000",
        // OAuth client IDs are non-secret; secrets come from env vars
        ["OAuth:GogClientId"]   = "46899977096215655",
        ["OAuth:EpicClientId"]  = "34a02cf8f4414e29b15921876da36f9a",
    };
}
