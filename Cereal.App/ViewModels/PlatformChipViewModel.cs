using CommunityToolkit.Mvvm.ComponentModel;
using Cereal.App.Utilities;

namespace Cereal.App.ViewModels;

public partial class PlatformChipViewModel : ObservableObject
{
    private readonly PlatformLogoSpec? _logo;

    public string Id { get; }
    public string Label { get; }
    public string Letter { get; }
    public string Color { get; }
    public string? LogoPathData => _logo?.PathData;
    public bool ShowLogoFill => _logo?.Kind == PlatformLogoKind.Fill;
    public bool ShowLogoStroke => _logo?.Kind == PlatformLogoKind.Stroke;
    public bool ShowLogoLetter => _logo is null;
    public double LogoStrokeWidth => _logo?.StrokeWidth ?? 2;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _count;

    public bool HasCount => Count > 0;
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(HasCount));
}

public partial class PlatformChipViewModel
{
    public PlatformChipViewModel(string id, int count = 0, bool isActive = false)
    {
        Id = id;
        _logo = PlatformLogos.TryGet(id);
        Label = PlatformInfo.GetLabel(id);
        Letter = PlatformInfo.GetLetter(id);
        Color = PlatformInfo.GetColor(id);
        _count = count;
        _isActive = isActive;
    }
}
