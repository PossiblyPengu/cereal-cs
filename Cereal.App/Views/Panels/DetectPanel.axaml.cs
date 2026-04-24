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

    private void OpenPlatforms_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
            d.MainWindow?.DataContext is MainViewModel mvm)
        {
            mvm.CloseDetectCommand.Execute(null);
            mvm.OpenPlatformsCommand.Execute(null);
        }
    }
}
