using Avalonia.Controls;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Providers;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.Views.Panels;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(
            App.Services.GetRequiredService<SettingsService>(),
            App.Services.GetRequiredService<DiscordService>(),
            App.Services.GetRequiredService<CoverService>(),
            App.Services.GetRequiredService<CredentialService>(),
            App.Services.GetRequiredService<ThemeService>(),
            App.Services.GetRequiredService<GameService>(),
            App.Services.GetRequiredService<IEnumerable<IProvider>>());
    }
}
