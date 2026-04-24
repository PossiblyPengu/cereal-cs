using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.Views.Panels;

// ─── PlatformsPanel code-behind ──────────────────────────────────────────────
// Resolves the VM from DI on first load (so singleton services flow through).
public partial class PlatformsPanel : UserControl
{
    public PlatformsPanel()
    {
        InitializeComponent();
        // Resolve the shared VM so events fired by the main shell stay in sync.
        if (App.Services.GetService<PlatformsPanelViewModel>() is { } vm)
        {
            DataContext = vm;

            // Forward platform row events to the main shell.
            vm.ChiakiRequested += (_, _) =>
            {
                if (this.FindAncestorOfType<MainWindow>()?.DataContext is MainViewModel m)
                {
                    m.ClosePlatformsCommand.Execute(null);
                    m.OpenChiakiCommand.Execute(null);
                }
            };
            vm.XcloudRequested += (_, _) =>
            {
                if (this.FindAncestorOfType<MainWindow>()?.DataContext is MainViewModel m)
                {
                    m.ClosePlatformsCommand.Execute(null);
                    m.OpenXcloudCommand.Execute(null);
                }
            };
            vm.InAppAuthNavigate += (url, title) =>
            {
                if (this.FindAncestorOfType<MainWindow>()?.DataContext is MainViewModel m)
                    m.OpenPlatformSignInWeb(url, title);
            };
            vm.InAppAuthFlowEnded += () =>
            {
                if (this.FindAncestorOfType<MainWindow>()?.DataContext is MainViewModel m)
                    m.DismissInAppAuthPanel();
            };
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
