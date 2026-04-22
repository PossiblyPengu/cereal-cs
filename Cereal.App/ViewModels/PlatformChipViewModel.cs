using CommunityToolkit.Mvvm.ComponentModel;
using Cereal.App.Utilities;

namespace Cereal.App.ViewModels;

public partial class PlatformChipViewModel : ObservableObject
{
    public string Id { get; }
    public string Label { get; }
    public string Color { get; }
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _count;
}

public partial class PlatformChipViewModel
{
    public PlatformChipViewModel(string id, int count = 0, bool isActive = false)
    {
        Id = id;
        Label = PlatformInfo.GetLabel(id);
        Color = PlatformInfo.GetColor(id);
        _count = count;
        _isActive = isActive;
    }
}
