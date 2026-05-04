using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using Cereal.App.Utilities;

namespace Cereal.App.ViewModels.Library;

/// <summary>
/// Thin card wrapper for a <see cref="Game"/> in the new library view.
/// Used by <see cref="LibraryViewModel"/> (new architecture).
/// The old <c>Cereal.App.ViewModels.GameCardViewModel</c> remains until Phase E removes it.
/// </summary>
public sealed partial class GameCardViewModel : ObservableObject
{
    public Game Game { get; }

    public GameCardViewModel(Game game) { Game = game; }

    public string Id              => Game.Id;
    public string Name            => Game.Name;
    public string Platform        => Game.Platform;
    public string? PlatformId     => Game.PlatformId;
    public string PlatformLabel   => PlatformInfo.GetLabel(Game.Platform);
    public string PlatformLetter  => PlatformInfo.GetLetter(Game.Platform);
    public string PlatformColor   => PlatformInfo.GetColor(Game.Platform);

    public string? PlatformLogoPath =>
        PlatformLogos.TryGet(Game.Platform)?.PathData;
    public bool ShowPlatformLogoFill =>
        PlatformLogos.TryGet(Game.Platform)?.Kind == PlatformLogoKind.Fill;
    public bool ShowPlatformLogoStroke =>
        PlatformLogos.TryGet(Game.Platform)?.Kind == PlatformLogoKind.Stroke;
    public bool ShowPlatformLetter =>
        PlatformLogos.TryGet(Game.Platform) is null;
    public double PlatformLogoStrokeWidth =>
        PlatformLogos.TryGet(Game.Platform)?.StrokeWidth ?? 2;

    public bool IsInstalled   => Game.IsInstalled;
    public bool IsNotInstalled => !Game.IsInstalled;
    public bool IsFavorite    => Game.IsFavorite;
    public bool IsHidden      => Game.IsHidden;

    public string Initial => string.IsNullOrEmpty(Game.Name)
        ? "?" : Game.Name[0].ToString().ToUpperInvariant();

    public string? CoverPath => Game.LocalCoverPath;
    public bool HasCover     => !string.IsNullOrEmpty(CoverPath);

    // ── Metacritic ────────────────────────────────────────────────────────────
    public int?   Metacritic       => Game.Metacritic;
    public bool   HasMetacritic    => Game.Metacritic is > 0;
    public string MetacriticColor  => Game.Metacritic is int mc
        ? mc >= 75 ? "#6dc849" : mc >= 50 ? "#fdca52" : "#fc4b37"
        : "#888888";
    public IBrush MetacriticFgBrush =>
        new SolidColorBrush(Color.Parse(MetacriticColor));
    public IBrush MetacriticBgBrush =>
        new SolidColorBrush(Color.Parse("#22" + MetacriticColor[1..]));

    // ── Metadata ──────────────────────────────────────────────────────────────
    public string? Developer    => Game.Developer;
    public string? Publisher    => Game.Publisher;
    public string? ReleaseDate  => Game.ReleaseDate?.ToString();
    public string? Description  => Game.Description;
    public string? Notes        => Game.Notes;
    public string? Website      => Game.Website;
    public IReadOnlyList<string> Screenshots => Game.Screenshots;

    public bool HasWebsite     => !string.IsNullOrWhiteSpace(Game.Website);
    public bool HasDeveloper   => !string.IsNullOrEmpty(Game.Developer);
    public bool HasPublisher   => !string.IsNullOrEmpty(Game.Publisher);
    public bool HasReleaseDate => Game.ReleaseDate is not null;
    public bool HasDescription => !string.IsNullOrEmpty(Game.Description);
    public bool HasScreenshots => Game.Screenshots.Count > 0;

    // ── Playtime ──────────────────────────────────────────────────────────────
    public int PlaytimeMinutes => Game.PlaytimeMinutes;
    public string PlaytimeFormatted => Game.PlaytimeMinutes switch
    {
        0    => "",
        < 60 => $"{Game.PlaytimeMinutes}m",
        _    => $"{Game.PlaytimeMinutes / 60}h {Game.PlaytimeMinutes % 60:D2}m",
    };
    public bool HasPlaytime => Game.PlaytimeMinutes > 0;
}
