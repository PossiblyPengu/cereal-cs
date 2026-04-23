using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Utilities;

namespace Cereal.App.ViewModels;

public partial class GameCardViewModel : ObservableObject
{
    private readonly GameService _games;
    public Game Game { get; }

    public string Id => Game.Id;
    public string Name => Game.Name;
    public string Platform => Game.Platform;
    public string? PlatformId => Game.PlatformId;
    public string PlatformLabel  => PlatformInfo.GetLabel(Game.Platform);
    public string PlatformLetter => PlatformInfo.GetLetter(Game.Platform);
    public string PlatformColor  => PlatformInfo.GetColor(Game.Platform);
    public bool IsNotInstalled   => Game.Installed == false;
    public string Initial => string.IsNullOrEmpty(Game.Name) ? "?" : Game.Name[0].ToString().ToUpperInvariant();
    public bool HasCover => !string.IsNullOrEmpty(CoverPath);

    // Metadata
    public int? Metacritic => Game.Metacritic;
    public bool HasMetacritic => Game.Metacritic is > 0;
    public string MetacriticColor => Game.Metacritic is int mc
        ? mc >= 75 ? "#6dc849" : mc >= 50 ? "#fdca52" : "#fc4b37"
        : "#888888";
    public IBrush MetacriticFgBrush => new SolidColorBrush(Color.Parse(MetacriticColor));
    public IBrush MetacriticBgBrush => new SolidColorBrush(Color.Parse(
        "#22" + MetacriticColor[1..]));
    public string? Developer => Game.Developer;
    public string? Publisher => Game.Publisher;
    public string? ReleaseDate => Game.ReleaseDate;
    public string? Description => Game.Description;
    public string? Notes => Game.Notes;
    public List<string>? Screenshots => Game.Screenshots;
    public string? Website => Game.Website;
    public bool HasDeveloper => !string.IsNullOrEmpty(Game.Developer);
    public bool HasPublisher => !string.IsNullOrEmpty(Game.Publisher);
    public bool HasReleaseDate => !string.IsNullOrEmpty(Game.ReleaseDate);
    public bool HasAnyInfoItem => HasDeveloper || HasPublisher || HasReleaseDate;
    public bool HasDescription => !string.IsNullOrEmpty(Game.Description);
    public bool HasNotes => !string.IsNullOrEmpty(Game.Notes);
    public bool HasCategories => Game.Categories?.Count > 0;
    public bool HasScreenshots => Game.Screenshots?.Count > 0;
    public string FavoriteLabel => IsFavorite ? "Unfav" : "Fav";

    [ObservableProperty] private string? _coverPath;
    [ObservableProperty] private string? _headerPath;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private bool _isSearchHighlighted;

    public string PlaytimeLabel
    {
        get
        {
            var mins = Game.PlaytimeMinutes ?? 0;
            if (mins < 60) return mins > 0 ? $"{mins}m" : "";
            return $"{mins / 60}h {mins % 60:00}m";
        }
    }

    public string LastPlayedLabel
    {
        get
        {
            if (Game.LastPlayed is null) return "Never";
            if (DateTime.TryParse(Game.LastPlayed, out var dt))
            {
                var ago = DateTime.UtcNow - dt.ToUniversalTime();
                if (ago.TotalMinutes < 2) return "Just now";
                if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes}m ago";
                if (ago.TotalDays < 1) return $"{(int)ago.TotalHours}h ago";
                if (ago.TotalDays < 30) return $"{(int)ago.TotalDays}d ago";
                return dt.ToString("MMM d, yyyy");
            }
            return "";
        }
    }

    public GameCardViewModel(Game game, GameService games)
    {
        Game = game;
        _games = games;
        _isFavorite = game.Favorite ?? false;
        _isHidden = game.Hidden ?? false;
        _coverPath = game.LocalCoverPath;
        _headerPath = game.LocalHeaderPath;
    }

    public void Refresh()
    {
        CoverPath = Game.LocalCoverPath;
        HeaderPath = Game.LocalHeaderPath;
        IsFavorite = Game.Favorite ?? false;
        IsHidden = Game.Hidden ?? false;
        OnPropertyChanged(nameof(PlaytimeLabel));
        OnPropertyChanged(nameof(LastPlayedLabel));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(Metacritic));
        OnPropertyChanged(nameof(HasMetacritic));
        OnPropertyChanged(nameof(Developer));
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(ReleaseDate));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasDeveloper));
        OnPropertyChanged(nameof(HasPublisher));
        OnPropertyChanged(nameof(HasReleaseDate));
        OnPropertyChanged(nameof(HasAnyInfoItem));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasCategories));
        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(FavoriteLabel));
    }

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteLabel));

    [RelayCommand]
    private void ToggleFavorite()
    {
        _games.SetFavorite(Game.Id, !IsFavorite);
        IsFavorite = Game.Favorite ?? false;
    }

    [RelayCommand]
    private void ToggleHidden()
    {
        _games.SetHidden(Game.Id, !IsHidden);
        IsHidden = Game.Hidden ?? false;
    }
}
