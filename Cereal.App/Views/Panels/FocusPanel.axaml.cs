using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Cereal.App.ViewModels;

namespace Cereal.App.Views.Panels;

public partial class FocusPanel : UserControl
{
    public FocusPanel()
    {
        InitializeComponent();
        // Electron FocusView parity: trap Tab inside the overlay (Shift+Tab wraps).
        AddHandler(InputElement.KeyDownEvent, OnTunnelTabKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnTunnelTabKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        if (DataContext is not MainViewModel vm || vm.SelectedGame is null) return;
        if (vm.ZoomScreenshotUrl is not null) return;

        var order = CollectTabFocusables(this);
        if (order.Count == 0) return;

        var top = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var idx = IndexOfContainingFocusable(order, top);

        var next = (e.KeyModifiers & KeyModifiers.Shift) != 0
            ? (idx <= 0 ? order.Count - 1 : idx - 1)
            : (idx < 0 || idx >= order.Count - 1 ? 0 : idx + 1);

        e.Handled = true;
        Dispatcher.UIThread.Post(() => order[next].Focus(NavigationMethod.Tab));
    }

    /// <summary>Visible, enabled controls that participate in keyboard focus — in visual-tree order.</summary>
    private static List<Control> CollectTabFocusables(Visual root)
    {
        var list = new List<Control>();
        foreach (var c in root.GetVisualDescendants().OfType<Control>())
        {
            if (!c.Focusable || !c.IsEffectivelyVisible || !c.IsEffectivelyEnabled) continue;
            if (c is Button or TextBox or ComboBox or CheckBox or Slider
                or CalendarDatePicker or TimePicker or NumericUpDown or ToggleSwitch)
                list.Add(c);
        }
        return list;
    }

    private static int IndexOfContainingFocusable(IReadOnlyList<Control> order, IInputElement? focused)
    {
        if (focused is Visual v)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (ReferenceEquals(order[i], focused)) return i;
                if (order[i].IsVisualAncestorOf(v)) return i;
            }
        }
        return -1;
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
        await ConfirmDeleteSelectedAsync();
    }

    private async Task ConfirmDeleteSelectedAsync()
    {
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

    private async void Root_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedGame is null) return;

        if (vm.ZoomScreenshotUrl is not null)
        {
            if (e.Key == Key.Escape)
            {
                vm.CloseZoomCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                _ = vm.LaunchGameCommand.ExecuteAsync(vm.SelectedGame);
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                await ConfirmDeleteSelectedAsync();
                e.Handled = true;
                break;
            case Key.E:
                vm.EditGameCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
                vm.SelectedGame.ToggleFavoriteCommand.Execute(null);
                e.Handled = true;
                break;
        }
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
