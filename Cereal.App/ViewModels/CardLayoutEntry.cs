using System.Collections.ObjectModel;
using Avalonia;
using Cereal.App.Utilities;

namespace Cereal.App.ViewModels;

/// <summary>Virtualized card grid row: a horizontal strip of 1..N <see cref="GameCardViewModel"/>.</summary>
public sealed class GameCardRowViewModel : CardLayoutEntry
{
    public IReadOnlyList<GameCardViewModel> Cards { get; }
    public Thickness RowMargin { get; }

    public GameCardRowViewModel(IReadOnlyList<GameCardViewModel> cards, Thickness rowMargin)
    {
        Cards = cards;
        RowMargin = rowMargin;
    }
}

/// <summary>Section title row in the library — one per platform group when grouped.</summary>
public sealed class PlatformSectionRowViewModel : CardLayoutEntry
{
    public string PlatformLabel { get; }
    public string PlatformColor { get; }
    public string CountLabel { get; }
    public Thickness SectionMargin { get; }

    public PlatformSectionRowViewModel(string platformId, int count, bool isFirst)
    {
        PlatformLabel = PlatformInfo.GetLabel(platformId);
        PlatformColor = PlatformInfo.GetColor(platformId);
        CountLabel = $"{count}";
        SectionMargin = isFirst
            ? new Thickness(2, 0, 0, 14)
            : new Thickness(2, 28, 0, 14);
    }
}

public abstract class CardLayoutEntry
{
    public static void BuildRows(
        ObservableCollection<CardLayoutEntry> target,
        IEnumerable<GameCardViewModel> allCards,
        int columnCount)
    {
        target.Clear();
        var cols = Math.Max(1, columnCount);
        var first = true;
        foreach (var grp in allCards
                     .GroupBy(c => c.Platform ?? "custom")
                     .OrderBy(g => g.Key))
        {
            var list = grp.ToList();
            if (list.Count == 0) continue;
            target.Add(new PlatformSectionRowViewModel(grp.Key, list.Count, first));
            first = false;
            for (var i = 0; i < list.Count; i += cols)
            {
                var row = new List<GameCardViewModel>(cols);
                for (var j = i; j < i + cols && j < list.Count; j++) row.Add(list[j]);
                var lastInGroup = i + cols >= list.Count;
                var bottom = lastInGroup ? 28.0 : 14.0;
                target.Add(new GameCardRowViewModel(row, new Thickness(0, 0, 0, bottom)));
            }
        }
    }
}
