using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using Cereal.Core.Services;
using Cereal.App.ViewModels.Library;
using LibGcvm = Cereal.App.ViewModels.Library.GameCardViewModel;

namespace Cereal.App.ViewModels;

/// <summary>
/// Drives the game focus (detail) panel — the right-side drawer shown when a
/// card is selected.  Receives <see cref="FocusGameMessage"/> and updates its
/// content without any panel-to-panel direct coupling.
/// </summary>
public sealed partial class FocusPanelViewModel : ObservableObject,
    IRecipient<FocusGameMessage>,
    IRecipient<GameUpdatedMessage>
{
    private readonly IGameService _games;
    private readonly ILaunchService _launch;
    private readonly ICoverService _covers;
    private readonly IMetadataService _meta;
    private readonly IMessenger _messenger;

    public FocusPanelViewModel(
        IGameService games,
        ILaunchService launch,
        ICoverService covers,
        IMetadataService meta,
        IMessenger messenger)
    {
        _games    = games;
        _launch   = launch;
        _covers   = covers;
        _meta     = meta;
        _messenger = messenger;
        messenger.Register<FocusGameMessage>(this);
        messenger.Register<GameUpdatedMessage>(this);
    }

    // ── Displayed game ────────────────────────────────────────────────────────

    [ObservableProperty] private LibGcvm? _card;
    [ObservableProperty] private bool _isVisible;

    // ── Status ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _isFetchingMeta;
    [ObservableProperty] private string? _statusMessage;

    // ── Messaging ─────────────────────────────────────────────────────────────

    public void Receive(FocusGameMessage msg) => _ = LoadAsync(msg.GameId);

    public void Receive(GameUpdatedMessage msg)
    {
        if (Card?.Game.Id == msg.Game.Id)
            Card = new LibGcvm(msg.Game);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (Card is null) return;
        IsLaunching = true;
        try { await _launch.LaunchAsync(Card.Game); }
        finally { IsLaunching = false; }
    }

    [RelayCommand]
    private async Task FetchMetadataAsync()
    {
        if (Card is null) return;
        IsFetchingMeta = true;
        StatusMessage = "Fetching metadata…";
        try
        {
            await _meta.FetchAndApplyAsync(Card.Game.Id);
            StatusMessage = "Metadata updated.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally { IsFetchingMeta = false; }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (Card is null) return;
        await _games.SetFavoriteAsync(Card.Game.Id, !Card.IsFavorite);
    }

    [RelayCommand]
    private async Task ToggleHiddenAsync()
    {
        if (Card is null) return;
        await _games.SetHiddenAsync(Card.Game.Id, !Card.IsHidden);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Card is null) return;
        await _games.DeleteAsync(Card.Game.Id);
        Close();
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        Card      = null;
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync(string gameId)
    {
        var game = await _games.GetByIdAsync(gameId);
        if (game is null) return;
        Card      = new LibGcvm(game);
        IsVisible = true;
    }
}
