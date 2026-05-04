using Avalonia.Controls;
using Cereal.App.ViewModels;

namespace Cereal.App.Views.Panels;

public partial class DetectPanel : UserControl
{
    public DetectPanel()
    {
        InitializeComponent();
        // DataContext is set by the DI-resolved parent (MainView / MainWindow).
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
