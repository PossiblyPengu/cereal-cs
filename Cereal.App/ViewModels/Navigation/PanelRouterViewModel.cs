using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;

namespace Cereal.App.ViewModels;

/// <summary>
/// A single open panel tab (settings, detect, platforms, game focus, etc.).
/// </summary>
public sealed partial class NavTabViewModel : ObservableObject
{
    public string PanelId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public object? Parameter { get; init; }
    [ObservableProperty] private bool _isActive;
}

/// <summary>
/// Manages which panels are open and which tab is active.
/// Decouples all panel navigation from <c>MainViewModel</c>.
/// Any component can send a <see cref="NavigateToPanelMessage"/> to open a panel.
/// </summary>
public sealed partial class PanelRouterViewModel : ObservableObject,
    IRecipient<NavigateToPanelMessage>,
    IRecipient<ClosePanelMessage>
{
    public ObservableCollection<NavTabViewModel> Tabs { get; } = [];

    [ObservableProperty] private NavTabViewModel? _activeTab;

    public PanelRouterViewModel(IMessenger messenger)
    {
        messenger.Register<NavigateToPanelMessage>(this);
        messenger.Register<ClosePanelMessage>(this);
    }

    public void Receive(NavigateToPanelMessage msg) =>
        OpenOrActivate(msg.PanelId, msg.Parameter);

    public void Receive(ClosePanelMessage msg) =>
        CloseTab(Tabs.FirstOrDefault(t => t.PanelId == msg.PanelId));

    [RelayCommand]
    private void ActivateTab(NavTabViewModel tab)
    {
        if (ActiveTab is { } prev) prev.IsActive = false;
        tab.IsActive = true;
        ActiveTab = tab;
    }

    [RelayCommand]
    private void CloseTab(NavTabViewModel? tab)
    {
        if (tab is null) return;
        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (ActiveTab == tab)
        {
            ActiveTab = Tabs.Count > 0
                ? Tabs[Math.Min(idx, Tabs.Count - 1)]
                : null;
            if (ActiveTab is not null) ActiveTab.IsActive = true;
        }
    }

    private void OpenOrActivate(string panelId, object? parameter)
    {
        var existing = Tabs.FirstOrDefault(t => t.PanelId == panelId);
        if (existing is not null)
        {
            ActivateTab(existing);
            return;
        }

        var tab = new NavTabViewModel
        {
            PanelId   = panelId,
            Title     = GetTitle(panelId),
            Icon      = GetIcon(panelId),
            Parameter = parameter,
        };
        Tabs.Add(tab);
        ActivateTab(tab);
    }

    private static string GetTitle(string panelId) => panelId switch
    {
        "settings"  => "Settings",
        "detect"    => "Detect",
        "platforms" => "Platforms",
        "chiaki"    => "Chiaki",
        "xcloud"    => "Xbox Cloud",
        "focus"     => "Game",
        _           => panelId,
    };

    private static string GetIcon(string panelId) => panelId switch
    {
        "settings"  => "M12 15.5A3.5 3.5 0 0 1 8.5 12 3.5 3.5 0 0 1 12 8.5a3.5 3.5 0 0 1 3.5 3.5 3.5 3.5 0 0 1-3.5 3.5m7.43-2.92c.04-.32.07-.64.07-.97s-.03-.66-.07-1l2.14-1.63c.19-.15.24-.42.12-.64l-2-3.46c-.12-.22-.39-.3-.61-.22l-2.49 1c-.52-.4-1.08-.73-1.69-.98l-.38-2.65C14.46 2.18 14.25 2 14 2h-4c-.25 0-.46.18-.49.42l-.38 2.65c-.61.25-1.17.59-1.69.98l-2.49-1c-.23-.09-.49 0-.61.22l-2 3.46c-.13.22-.07.49.12.64L4.57 11c-.04.34-.07.67-.07 1s.03.65.07.97l-2.11 1.66c-.19.15-.25.42-.12.64l2 3.46c.12.22.39.3.61.22l2.49-1c.52.4 1.08.73 1.69.98l.38 2.65c.03.24.24.42.49.42h4c.25 0 .46-.18.49-.42l.38-2.65c.61-.25 1.17-.58 1.69-.98l2.49 1c.23.09.49 0 .61-.22l2-3.46c.12-.22.07-.49-.12-.64l-2.11-1.66Z",
        "detect"    => "M9.5 3A6.5 6.5 0 0 1 16 9.5c0 1.61-.59 3.09-1.56 4.23l.27.27h.79l5 5-1.5 1.5-5-5v-.79l-.27-.27A6.516 6.516 0 0 1 9.5 16 6.5 6.5 0 0 1 3 9.5 6.5 6.5 0 0 1 9.5 3m0 2C7 5 5 7 5 9.5S7 14 9.5 14 14 12 14 9.5 12 5 9.5 5Z",
        "platforms" => "M17 12h-5v5h5v-5zM16 1v2H8V1H6v2H5c-1.11 0-1.99.9-1.99 2L3 19c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2h-1V1h-2zm3 18H5V8h14v11z",
        "chiaki"    => "M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H8v3H6v-3H3v-2h3V8h2v3h3v2zm4.5 2c-.83 0-1.5-.67-1.5-1.5S14.67 12 15.5 12s1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm4-3c-.83 0-1.5-.67-1.5-1.5S18.67 9 19.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z",
        _           => "",
    };
}
