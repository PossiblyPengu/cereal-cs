using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cereal.App.Models;
using Cereal.App.ViewModels;
using Cereal.App.Views.Dialogs;

namespace Cereal.App.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? _vm;

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
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ViewMode))
        {
            var orbit = this.FindControl<OrbitView>("OrbitViewControl");
            if (orbit is not null && _vm?.ViewMode == "orbit")
                _ = orbit.RefreshGamesAsync();
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
}
