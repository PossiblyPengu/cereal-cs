using System.Collections.ObjectModel;
using Cereal.App.Utilities;

namespace Cereal.App.ViewModels;

public class PlatformGroupViewModel
{
    public string PlatformId { get; }
    public string PlatformLabel { get; }
    public string PlatformColor { get; }
    public string CountLabel => $"{Games.Count}";
    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    public PlatformGroupViewModel(string platformId)
    {
        PlatformId = platformId;
        PlatformLabel = PlatformInfo.GetLabel(platformId);
        PlatformColor = PlatformInfo.GetColor(platformId);
    }
}
