using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Services;

namespace Cereal.App.ViewModels;

/// <summary>
/// Drives the Ctrl+K search overlay.
/// Filters the game list in real-time; Ctrl+Enter launches the top hit.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject,
    IRecipient<OpenSearchMessage>,
    IRecipient<CloseSearchMessage>,
    IDisposable
{
    private readonly IGameService _games;
    private readonly ILaunchService _launch;

    public SearchViewModel(IGameService games, ILaunchService launch, IMessenger messenger)
    {
        _games  = games;
        _launch = launch;
        messenger.Register<OpenSearchMessage>(this);
        messenger.Register<CloseSearchMessage>(this);
    }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private IReadOnlyList<SearchResultViewModel> _results = [];
    [ObservableProperty] private int _selectedIndex;

    private CancellationTokenSource? _searchCts;

    partial void OnQueryChanged(string value)
    {
        SelectedIndex = 0;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = RefreshResultsAsync(value, _searchCts.Token);
    }

    public void Receive(OpenSearchMessage _)  { IsOpen = true; Query = ""; }
    public void Receive(CloseSearchMessage _) => Close();

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Query  = "";
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedIndex > 0) SelectedIndex--;
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedIndex < Results.Count - 1) SelectedIndex++;
    }

    [RelayCommand]
    private async Task LaunchSelected()
    {
        if (SelectedIndex < Results.Count)
        {
            await _launch.LaunchAsync(Results[SelectedIndex].Game);
            Close();
        }
    }

    private async Task RefreshResultsAsync(string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            Results = [];
            return;
        }
        try
        {
            // Debounce: wait a short period before hitting the DB
            await Task.Delay(150, ct);
            var hits = await _games.SearchAsync(q, ct);
            Results = hits
                .Take(12)
                .Select(g => new SearchResultViewModel(g))
                .ToList();
        }
        catch (OperationCanceledException) { /* superseded by newer query */ }
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}

public sealed class SearchResultViewModel(Cereal.Core.Models.Game game)
{
    public Cereal.Core.Models.Game Game { get; } = game;
    public string Name     => Game.Name;
    public string Platform => Game.Platform;
    public string? CoverPath => Game.LocalCoverPath;
}
