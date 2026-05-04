using Cereal.Core.Messaging;
using Cereal.Core.Providers;
using Cereal.Core.Repositories;
using Cereal.Core.Services;
using Cereal.Infrastructure.Config;
using Cereal.Infrastructure.Database;
using Cereal.Infrastructure.Database.Migrations;
using Cereal.Infrastructure.Providers;
using Cereal.Infrastructure.Repositories;
using Cereal.Infrastructure.Services;
using Cereal.Infrastructure.Services.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.Infrastructure;

/// <summary>
/// Extension methods to wire all Infrastructure services into the DI container.
/// Called once from <c>Cereal.App</c>'s startup.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Config ────────────────────────────────────────────────────────────
        var appConfig = new AppConfig();
        configuration.Bind(appConfig);
        services.AddSingleton(appConfig);
        services.AddSingleton(appConfig.OAuth);
        services.AddSingleton(appConfig.Discord);

        // ── Messenger ─────────────────────────────────────────────────────────
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // ── Path + DB ─────────────────────────────────────────────────────────
        services.AddSingleton<PathService>();

        // Migrations — add each new IMigration class here.
        services.AddSingleton<IMigration, M001_Initial>();

        services.AddSingleton<CerealDb>();

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddSingleton<IGameRepository, GameRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        services.AddSingleton<IAccountRepository, AccountRepository>();
        services.AddSingleton<IChiakiConfigRepository, ChiakiConfigRepository>();

        // ── Core services ─────────────────────────────────────────────────────
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IGameService, GameService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<ICoverService, CoverService>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<PlaytimeSyncService>();
        services.AddSingleton<SingleInstanceGuard>();

        // ── Integration services ──────────────────────────────────────────────
        services.AddSingleton<IChiakiService, ChiakiService>();
        services.AddSingleton<IDiscordService, DiscordService>();
        services.AddSingleton<ISmtcService,   SmtcService>();
        services.AddSingleton<IXcloudService, XcloudService>();

        // ── Providers ─────────────────────────────────────────────────────────
        // Register concrete types first so IProvider + IImportProvider aliases
        // resolve to the SAME singleton instance (not two separate ones).
        services.AddSingleton<SteamProvider>();
        services.AddSingleton<EpicProvider>();
        services.AddSingleton<GogProvider>();

        services.AddSingleton<IProvider>(sp => sp.GetRequiredService<SteamProvider>());
        services.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<SteamProvider>());
        services.AddSingleton<IProvider>(sp => sp.GetRequiredService<EpicProvider>());
        services.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<EpicProvider>());
        services.AddSingleton<IProvider>(sp => sp.GetRequiredService<GogProvider>());
        services.AddSingleton<IImportProvider>(sp => sp.GetRequiredService<GogProvider>());
        // Local-only providers (EA, Ubisoft, itch.io) — register as both concrete and IProvider
        foreach (var provider in LocalProviders.All)
        {
            var captured = provider;
            services.AddSingleton(captured.GetType(), _ => (object)captured);
            services.AddSingleton<IProvider>(_ => captured);
        }

        // ── Shared HTTP client ────────────────────────────────────────────────
        services.AddHttpClient();

        return services;
    }
}
