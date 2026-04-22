using Avalonia.Controls;
using Cereal.App.Services;
using Cereal.App.Services.Providers;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.Views.Panels;

public partial class DetectPanel : UserControl
{
    public DetectPanel()
    {
        InitializeComponent();
        DataContext = new DetectViewModel(
            App.Services.GetRequiredService<GameService>(),
            App.Services.GetRequiredService<DatabaseService>(),
            App.Services.GetServices<IProvider>(),
            App.Services.GetServices<IImportProvider>());
    }
}
