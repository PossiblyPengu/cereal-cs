using Avalonia.Controls;
using Avalonia.Input;
using Cereal.App.ViewModels;

namespace Cereal.App.Views.Panels;

public partial class FocusPanel : UserControl
{
    public FocusPanel()
    {
        InitializeComponent();
    }

    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseFocusCommand.Execute(null);
    }

    private void ContentCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void Screenshot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel vm) return;
        if ((sender as Avalonia.Controls.Control)?.DataContext is string url)
            vm.OpenZoomCommand.Execute(url);
    }

    private void Zoom_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseZoomCommand.Execute(null);
    }

    private void ZoomImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true; // don't propagate to Zoom_PointerPressed
    }

    private async void Delete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel vm || vm.SelectedGame is null) return;

        var name = vm.SelectedGame.Name;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dlg = new Avalonia.Controls.Window
        {
            Title = "Remove game",
            Width = 360,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#12122a"),
            Content = BuildConfirmContent(name, out var yesBtn, out var noBtn),
        };

        bool confirmed = false;
        yesBtn.Click += (_, _) => { confirmed = true; dlg.Close(); };
        noBtn.Click += (_, _) => dlg.Close();

        await dlg.ShowDialog(owner);
        if (confirmed)
            vm.DeleteGameCommand.Execute(vm.SelectedGame.Id);
    }

    private static Avalonia.Controls.Control BuildConfirmContent(
        string name,
        out Avalonia.Controls.Button yesBtn,
        out Avalonia.Controls.Button noBtn)
    {
        var sp = new Avalonia.Controls.StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
        };
        sp.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = $"Remove \"{name}\" from library?",
            Foreground = Avalonia.Media.Brush.Parse("#e8e4de"),
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var row = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10,
        };

        noBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Padding = new Avalonia.Thickness(14, 8),
            Background = Avalonia.Media.Brush.Parse("#18ffffff"),
            Foreground = Avalonia.Media.Brush.Parse("#b0aaa0"),
        };
        yesBtn = new Avalonia.Controls.Button
        {
            Content = "Remove",
            Padding = new Avalonia.Thickness(14, 8),
            Background = Avalonia.Media.Brush.Parse("#cc3333"),
            Foreground = Avalonia.Media.Brush.Parse("White"),
        };

        row.Children.Add(noBtn);
        row.Children.Add(yesBtn);
        sp.Children.Add(row);
        return sp;
    }
}
