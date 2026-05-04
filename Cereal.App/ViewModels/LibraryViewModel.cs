using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using Cereal.Core.Services;
using Cereal.App.ViewModels.Library;

namespace Cereal.App.ViewModels;

/// <summary>
/// Owns the game library: all games, filtered+sorted view, and active platform/category
/// chip selection.  Replaces the 600-line MainViewModel god-object.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject,
    IRecipient<GameAddedMessage>,
    IRecipient<GameUpdatedMessage>,
    IRecipient<GameRemovedMessage>,
    IRecipient<LibraryRefreshedMessage>
{
    private readonly IGameService _games;
    private readonly IMessenger _messenger;

    // Full source list — never modified except on library refresh
    private IReadOnlyList<Game> _all = [];

    public LibraryViewModel(IGameService games, IMessenger messenger)
    {
        _games    = games;
        _messenger = messenger;
        messenger.Register<GameAddedMessage>(this);
        messenger.Register<GameUpdatedMessage>(this);
        messenger.Register<GameRemovedMessage>(this);
        messenger.Register<LibraryRefreshedMessage>(this);
    }

    // ── Filtered view ─────────────────────────────────────────────────────────

    public ObservableCollection<Library.GameCardViewModel> Cards { get; } = [];

    [ObservableProperty] private bool _isLoading;

    // ── Filter / sort state ────────────────────────────────────────────────────

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _activePlatform = "";
    [ObservableProperty] private string _activeCategory = "";
    [ObservableProperty] private SortMode _sortMode = SortMode.Name;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _favoritesOnly;

    partial void OnSearchTextChanged(string value)     => ApplyFilters();
    partial void OnActivePlatformChanged(string value) => ApplyFilters();
    partial void OnActiveCategoryChanged(string value) => ApplyFilters();
    partial void OnSortModeChanged(SortMode value)     => ApplyFilters();
    partial void OnShowHiddenChanged(bool value)       => ApplyFilters();
    partial void OnFavoritesOnlyChanged(bool value)    => ApplyFilters();

    // ── Platform chips ─────────────────────────────────────────────────────────

    public ObservableCollection<PlatformChipViewModel> Platforms { get; } = [];

    // ── Load command ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            _all = await _games.GetAllAsync();
            RebuildPlatformChips();
            ApplyFilters();
        }
        finally { IsLoading = false; }
    }

    // ── Messaging ─────────────────────────────────────────────────────────────

    public void Receive(GameAddedMessage msg)
    {
        var updated = new List<Game>(_all) { msg.Game };
        Dispatcher.UIThread.Post(() =>
        {
            _all = updated;
            RebuildPlatformChips();
            ApplyFilters();
        });
    }

    public void Receive(GameUpdatedMessage msg)
    {
        var updated = _all.Select(g => g.Id == msg.Game.Id ? msg.Game : g).ToList();
        Dispatcher.UIThread.Post(() =>
        {
            _all = updated;
            ApplyFilters();
        });
    }

    public void Receive(GameRemovedMessage msg)
    {
        var updated = _all.Where(g => g.Id != msg.GameId).ToList();
        Dispatcher.UIThread.Post(() =>
        {
            _all = updated;
            RebuildPlatformChips();
            ApplyFilters();
        });
    }

    public void Receive(LibraryRefreshedMessage msg) =>
        Dispatcher.UIThread.Post(() => _ = LoadAsync());

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyFilters()
    {
        var filtered = _all.AsEnumerable();

        if (!ShowHidden)
            filtered = filtered.Where(g => !g.IsHidden);

        if (FavoritesOnly)
            filtered = filtered.Where(g => g.IsFavorite);

        if (!string.IsNullOrEmpty(ActivePlatform))
            filtered = filtered.Where(g => g.Platform == ActivePlatform);

        if (!string.IsNullOrEmpty(ActiveCategory))
            filtered = filtered.Where(g => g.Categories.Contains(ActiveCategory));

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            filtered = filtered.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        filtered = SortMode switch
        {
            SortMode.Name         => filtered.OrderBy(g => g.SortName),
            SortMode.LastPlayed   => filtered.OrderByDescending(g => g.LastPlayedAt),
            SortMode.Playtime     => filtered.OrderByDescending(g => g.PlaytimeMinutes),
            SortMode.RecentlyAdded => filtered.OrderByDescending(g => g.AddedAt),
            SortMode.Platform     => filtered.OrderBy(g => g.Platform).ThenBy(g => g.SortName),
            _ => filtered.OrderBy(g => g.SortName),
        };

        var newGames = filtered.ToList();

        // Diff the collection: update in-place, append, then trim — avoids full Clear() + scroll reset.
        for (int i = 0; i < newGames.Count; i++)
        {
            if (i < Cards.Count)
            {
                if (Cards[i].Game.Id != newGames[i].Id)
                    Cards[i] = new Library.GameCardViewModel(newGames[i]);
                else if (!ReferenceEquals(Cards[i].Game, newGames[i]))
                    Cards[i] = new Library.GameCardViewModel(newGames[i]); // data changed
            }
            else
            {
                Cards.Add(new Library.GameCardViewModel(newGames[i]));
            }
        }

        while (Cards.Count > newGames.Count)
            Cards.RemoveAt(Cards.Count - 1);
    }

    private void RebuildPlatformChips()
    {
        var platforms = _all
            .Where(g => !g.IsHidden || ShowHidden)
            .GroupBy(g => g.Platform)
            .OrderBy(g => g.Key)
            .Select(g => new PlatformChipViewModel(g.Key, g.Count()))
            .ToList();

        Platforms.Clear();
        foreach (var p in platforms)
            Platforms.Add(p);
    }
}

public enum SortMode { Name, LastPlayed, Playtime, RecentlyAdded, Platform }
