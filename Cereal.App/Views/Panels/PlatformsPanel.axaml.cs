using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
                var m = ResolveMainViewModel();
                if (m is null) return;
                m.ClosePlatformsCommand.Execute(null);
                m.OpenChiakiCommand.Execute(null);
            };
            vm.XcloudRequested += (_, _) =>
            {
                var m = ResolveMainViewModel();
                if (m is null) return;
                m.ClosePlatformsCommand.Execute(null);
                m.OpenXcloudCommand.Execute(null);
            };
            vm.InAppAuthNavigate += (url, title) =>
            {
                var m = ResolveMainViewModel();
                if (m is null) return;
                m.OpenPlatformSignInWeb(url, title);
            };
            vm.InAppAuthFlowEnded += () =>
            {
                var m = ResolveMainViewModel();
                if (m is null) return;
                m.DismissInAppAuthPanel();
            };
        }
    }

    private MainViewModel? ResolveMainViewModel()
    {
        // Prefer the current top-level hosting this control.
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainViewModel vmFromTopLevel })
            return vmFromTopLevel;

        // Fallback for embedded/templated visual trees.
        if (this.FindAncestorOfType<MainWindow>()?.DataContext is MainViewModel vmFromAncestor)
            return vmFromAncestor;

        // Last resort: desktop main window.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is MainViewModel vmFromDesktop)
            return vmFromDesktop;

        return null;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
