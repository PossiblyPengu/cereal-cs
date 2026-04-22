using Avalonia.Controls;
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
        }

        _vm = DataContext as MainViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.AddGameRequested += OnAddGameRequested;
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
}
