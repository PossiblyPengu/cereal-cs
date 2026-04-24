using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.ViewModels;
using Cereal.App.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private GameService? _gameLibrary;

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        if (this.FindControl<ScrollViewer>("LibraryScroll") is { } sc)
        {
            sc.SizeChanged += LibraryScroll_OnSizeChanged;
            UpdateLibraryColumnCount();
        }
        AttachToolbarScaleWatcher();
    }

    private void OnUnloaded(object? s, RoutedEventArgs e)
    {
        DetachToolbarScaleWatcher();
        DetachGameLibraryListener();
    }

    private void LibraryScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_vm is null || sender is not ScrollViewer sc) return;
        _vm.TryExpandLibraryCardsFromScroll(sc.Offset.Y, sc.Viewport.Height, sc.Extent.Height);
    }

    private void LibraryScroll_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateLibraryColumnCount();
        if (_vm is null || sender is not ScrollViewer sc) return;
        _vm.TryExpandLibraryCardsFromScroll(sc.Offset.Y, sc.Viewport.Height, sc.Extent.Height);
    }

    private void UpdateLibraryColumnCount()
    {
        if (this.FindControl<ScrollViewer>("LibraryScroll") is not { } sc) return;
        if (_vm is null) return;
        var w = sc.Bounds.Width;
        if (w < 80) return;
        _vm.LibraryColumnCount = Math.Max(1, (int)((w - 64) / (double)MainViewModel.LibraryCardCellWidth));
    }

    private MainViewModel? _vm;
    private Window? _toolbarScaleWindow;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.AddGameRequested -= OnAddGameRequested;
            _vm.EditGameRequested -= OnEditGameRequested;
        }

        _vm = DataContext as MainViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.AddGameRequested += OnAddGameRequested;
            _vm.EditGameRequested += OnEditGameRequested;
            UpdateLibraryColumnCount();
            AttachGameLibraryListener();
        }
        else
            DetachGameLibraryListener();

        if (IsLoaded)
            AttachToolbarScaleWatcher();
    }

    private void AttachGameLibraryListener()
    {
        DetachGameLibraryListener();
        _gameLibrary = App.Services.GetRequiredService<GameService>();
        _gameLibrary.LibraryChanged += OnGameLibraryChanged;
    }

    private void DetachGameLibraryListener()
    {
        if (_gameLibrary is null) return;
        _gameLibrary.LibraryChanged -= OnGameLibraryChanged;
        _gameLibrary = null;
    }

    private void OnGameLibraryChanged(object? sender, EventArgs e)
    {
        if (_vm?.ViewMode != "orbit") return;
        if (this.FindControl<OrbitView>("OrbitViewControl") is { } orbit)
            _ = orbit.RefreshGamesAsync();
    }

    private void AttachToolbarScaleWatcher()
    {
        DetachToolbarScaleWatcher();
        if (TopLevel.GetTopLevel(this) is not Window w) return;
        _toolbarScaleWindow = w;
        w.PropertyChanged += ToolbarScaleWindow_PropertyChanged;
        UpdateToolbarScaleFromWindow(w);
    }

    private void DetachToolbarScaleWatcher()
    {
        if (_toolbarScaleWindow is null) return;
        _toolbarScaleWindow.PropertyChanged -= ToolbarScaleWindow_PropertyChanged;
        _toolbarScaleWindow = null;
    }

    private void ToolbarScaleWindow_PropertyChanged(object? s, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.ClientSizeProperty && s is Window w)
            UpdateToolbarScaleFromWindow(w);
    }

    private void UpdateToolbarScaleFromWindow(Window w)
    {
        if (_vm is null) return;
        var scale = MainViewModel.ComputeToolbarScale(w.ClientSize.Width);
        if (System.Math.Abs(_vm.ToolbarScale - scale) > 0.0005)
            _vm.ToolbarScale = scale;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ViewMode))
        {
            var orbit = this.FindControl<OrbitView>("OrbitViewControl");
            if (orbit is not null && _vm?.ViewMode == "orbit")
            {
                orbit.EnsureFittedForShow();
                orbit.PlayEntranceAnimation();
                _ = orbit.RefreshGamesAsync();
            }
        }
    }

    private async void OnAddGameRequested(object? sender, EventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dlg = new AddGameDialog();
        var result = await dlg.ShowDialog<AddGameResult?>(owner);
        if (result is not null)
            _vm?.AddGame(result.Game);
    }

    private async void OnEditGameRequested(object? sender, Game game)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dlg = new AddGameDialog();
        dlg.LoadGame(game);
        var result = await dlg.ShowDialog<AddGameResult?>(owner);
        if (result is not null)
            _vm?.UpdateGame(result.Game);
    }

    // ── Game card interactions ───────────────────────────────────────────────

    private void Card_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: GameCardViewModel card }) return;
        _vm?.SelectGameCommand.Execute(card);
    }

    private void Card_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: GameCardViewModel card }) return;
        _ = _vm?.LaunchGameCommand.ExecuteAsync(card);
        e.Handled = true;
    }

    private void PlayBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: GameCardViewModel card }) return;
        _ = _vm?.LaunchGameCommand.ExecuteAsync(card);
        e.Handled = true;
    }

    private void FavBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: GameCardViewModel card }) return;
        card.ToggleFavoriteCommand.Execute(null);
        e.Handled = true;
    }

    private void Card_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || sender is not Control { DataContext: GameCardViewModel card }) return;
        var idx = _vm.VisibleGames.IndexOf(card);
        if (idx < 0) return;

        var cols = Math.Max(1, _vm.LibraryColumnCount);
        var next = idx;
        switch (e.Key)
        {
            case Key.Right: next = Math.Min(_vm.VisibleGames.Count - 1, idx + 1); break;
            case Key.Left:  next = Math.Max(0, idx - 1); break;
            case Key.Down:  next = Math.Min(_vm.VisibleGames.Count - 1, idx + cols); break;
            case Key.Up:    next = Math.Max(0, idx - cols); break;
            case Key.Enter:
            case Key.Space:
                _vm.SelectGameCommand.Execute(card);
                e.Handled = true;
                return;
            default:
                return;
        }

        var target = _vm.VisibleGames[next];
        _vm.SelectGameCommand.Execute(target);
        e.Handled = true;
    }
}
