using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Cereal.App.Models;
using Cereal.App.Services.Integrations;

namespace Cereal.App.Views.Panels;

// ─── ViewModels ───────────────────────────────────────────────────────────────

public partial class ChiakiConsoleViewModel : ObservableObject
{
    private static readonly IBrush GreenBg = new SolidColorBrush(Color.Parse("#0f22c55e"));
    private static readonly IBrush GreenFg = new SolidColorBrush(Color.Parse("#22c55e"));
    private static readonly IBrush RedBg   = new SolidColorBrush(Color.Parse("#0fff4444"));
    private static readonly IBrush RedFg   = new SolidColorBrush(Color.Parse("#ff7070"));
    private static readonly IBrush GoldBg  = new SolidColorBrush(Color.Parse("#0fd4a853"));
    private static readonly IBrush GoldFg  = new SolidColorBrush(Color.Parse("#d4a853"));

    public ChiakiConsole Model { get; init; } = null!;
    public string Nickname     => Model.Nickname ?? Model.Host ?? "Unknown";
    public string Host         => Model.Host ?? "";
    public string ProfileSuffix=> string.IsNullOrEmpty(Model.Profile) ? "" : " / " + Model.Profile;
    public bool HasKeys        => !string.IsNullOrEmpty(Model.RegistKey) && !string.IsNullOrEmpty(Model.Morning);
    public bool IsNotRegistered=> !HasKeys;

    [ObservableProperty] private bool _isLive;
}

public partial class DiscoveredConsoleViewModel : ObservableObject
{
    public string Host       { get; }
    public string Name       { get; }
    public string TypeLabel  { get; }
    public bool IsReady      { get; }
    public bool IsStandby    { get; }
    public bool AlreadyAdded { get; init; }

    public DiscoveredConsoleViewModel(DiscoveredConsole dc, bool alreadyAdded)
    {
        Host = dc.Host;
        Name = dc.Name ?? "PlayStation";
        var t = (dc.Type ?? "").ToUpperInvariant();
        TypeLabel   = t.Contains("PS5") || t.Contains('5') ? "PS5"
                    : t.Contains("PS4") || t.Contains('4') ? "PS4" : "PS";
        IsReady     = dc.State == "ready";
        IsStandby   = dc.State == "standby";
        AlreadyAdded = alreadyAdded;
    }
}

public partial class ChiakiPanelViewModel : ObservableObject
{
    private static readonly IBrush GreenBg = new SolidColorBrush(Color.Parse("#0f22c55e"));
    private static readonly IBrush GreenFg = new SolidColorBrush(Color.Parse("#22c55e"));
    private static readonly IBrush GreenDot= new SolidColorBrush(Color.Parse("#22c55e"));
    private static readonly IBrush RedBg   = new SolidColorBrush(Color.Parse("#0fff4444"));
    private static readonly IBrush RedFg   = new SolidColorBrush(Color.Parse("#ff7070"));
    private static readonly IBrush RedDot  = new SolidColorBrush(Color.Parse("#ff4444"));
    private static readonly IBrush GoldBg  = new SolidColorBrush(Color.Parse("#0fd4a853"));
    private static readonly IBrush GoldFg  = new SolidColorBrush(Color.Parse("#d4a853"));

    // ── Status bar ───────────────────────────────────────────────────────────
    [ObservableProperty] private string  _statusText    = "chiaki-ng not found";
    [ObservableProperty] private string? _statusVersion;
    [ObservableProperty] private IBrush  _statusBg      = RedBg;
    [ObservableProperty] private IBrush  _statusFg      = RedFg;
    [ObservableProperty] private IBrush  _statusDot     = RedDot;

    // ── Tabs ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isTabConsoles = true;
    [ObservableProperty] private bool _isTabDiscover;
    [ObservableProperty] private bool _isTabRegister;

    public void SetTab(string tab)
    {
        IsTabConsoles = tab == "consoles";
        IsTabDiscover = tab == "discover";
        IsTabRegister = tab == "register";
    }

    // ── Consoles tab ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _showAddConsoleForm;
    public ObservableCollection<ChiakiConsoleViewModel> Consoles { get; } = [];
    public bool HasNoConsoles => Consoles.Count == 0;

    // ── Discover tab ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isDiscovering;
    public ObservableCollection<DiscoveredConsoleViewModel> DiscoveredConsoles { get; } = [];
    public bool ShowDiscoverEmpty => !IsDiscovering && DiscoveredConsoles.Count == 0;
    partial void OnIsDiscoveringChanged(bool value) => OnPropertyChanged(nameof(ShowDiscoverEmpty));

    // ── Register tab ─────────────────────────────────────────────────────────
    [ObservableProperty] private string? _regStatus;
    [ObservableProperty] private IBrush  _regStatusBg = RedBg;
    [ObservableProperty] private IBrush  _regStatusFg = RedFg;

    // ── Methods ───────────────────────────────────────────────────────────────

    public void SetStatus(string status, string? version)
    {
        StatusVersion = version;
        switch (status)
        {
            case "bundled":
                StatusText = "chiaki-ng bundled";
                StatusBg   = GreenBg; StatusFg = GreenFg; StatusDot = GreenDot;
                break;
            case "system":
                StatusText = "chiaki-ng (system)";
                StatusBg   = GreenBg; StatusFg = GreenFg; StatusDot = GreenDot;
                break;
            default:
                StatusText = "chiaki-ng not found";
                StatusBg   = RedBg;   StatusFg = RedFg;   StatusDot = RedDot;
                break;
        }
    }

    public void SetRegStatus(bool success, string? message)
    {
        RegStatus   = message ?? (success ? "Registered successfully!" : "Registration failed.");
        RegStatusBg = success ? GreenBg : RedBg;
        RegStatusFg = success ? GreenFg : RedFg;
    }

    public void SetRegPending()
    {
        RegStatus   = "Registering...";
        RegStatusBg = GoldBg;
        RegStatusFg = GoldFg;
    }

    public void UpdateConsoleSessionState(string host, bool isLive)
    {
        var vm = Consoles.FirstOrDefault(c => c.Host == host);
        if (vm is not null) vm.IsLive = isLive;
    }

    public void NotifyConsolesChanged() => OnPropertyChanged(nameof(HasNoConsoles));
}

// ─── Code-behind ──────────────────────────────────────────────────────────────

public partial class ChiakiPanel : UserControl
{
    private readonly ChiakiService _chiaki;
    public ChiakiPanelViewModel VM { get; } = new();

    public ChiakiPanel()
    {
        InitializeComponent();
        _chiaki = App.Services.GetRequiredService<ChiakiService>();
        DataContext = VM;

        Loaded += (_, _) => Load();
        _chiaki.SessionEvent += OnChiakiEvent;
    }

    private void Load()
    {
        var (status, _, version) = _chiaki.GetStatus();
        VM.SetStatus(status, version);

        var cfg = _chiaki.GetConfig();
        VM.Consoles.Clear();
        foreach (var c in cfg.Consoles)
            VM.Consoles.Add(new ChiakiConsoleViewModel { Model = c });
        VM.NotifyConsolesChanged();

        foreach (var sess in _chiaki.GetSessions())
        {
            var host   = sess.Key.Replace("console:", "");
            var isLive = sess.Value.State is "launching" or "connecting" or "streaming" or "gui";
            VM.UpdateConsoleSessionState(host, isLive);
        }
    }

    private void SaveConfig()
    {
        var cfg = _chiaki.GetConfig();
        cfg.Consoles = VM.Consoles.Select(c => c.Model).ToList();
        _chiaki.SaveConfig(cfg);
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────
    private void TabConsoles_Click(object? sender, RoutedEventArgs e) => VM.SetTab("consoles");
    private void TabDiscover_Click(object? sender, RoutedEventArgs e) => VM.SetTab("discover");
    private void TabRegister_Click(object? sender, RoutedEventArgs e) => VM.SetTab("register");
    private void OpenGui_Click(object? sender, RoutedEventArgs e) => _chiaki.OpenGui();

    // ── Consoles tab ─────────────────────────────────────────────────────────

    private void ShowAddConsole_Click(object? sender, RoutedEventArgs e)
    {
        VM.ShowAddConsoleForm = !VM.ShowAddConsoleForm;
        if (VM.ShowAddConsoleForm)
        {
            this.FindControl<TextBox>("NewNicknameBox")!.Text = "";
            this.FindControl<TextBox>("NewHostBox")!.Text     = "";
            this.FindControl<TextBox>("NewProfileBox")!.Text  = "";
        }
    }

    private void CancelAddConsole_Click(object? sender, RoutedEventArgs e)
        => VM.ShowAddConsoleForm = false;

    private void AddConsole_Click(object? sender, RoutedEventArgs e)
    {
        var nickname = this.FindControl<TextBox>("NewNicknameBox")!.Text?.Trim() ?? "";
        var host     = this.FindControl<TextBox>("NewHostBox")!.Text?.Trim() ?? "";
        var profile  = this.FindControl<TextBox>("NewProfileBox")!.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(nickname) || string.IsNullOrEmpty(host)) return;

        VM.Consoles.Add(new ChiakiConsoleViewModel
        {
            Model = new ChiakiConsole
            {
                Nickname = nickname,
                Host     = host,
                Profile  = string.IsNullOrEmpty(profile) ? null : profile,
            }
        });
        VM.ShowAddConsoleForm = false;
        VM.NotifyConsolesChanged();
        SaveConfig();
    }

    private void ConnectConsole_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChiakiConsoleViewModel cvm })
        {
            var (success, error, _) = _chiaki.StartStreamDirect(
                cvm.Host, cvm.Nickname, cvm.Model.Profile,
                cvm.Model.RegistKey, cvm.Model.Morning);
            if (!success)
                Log.Warning("[chiaki] Connect failed: {Error}", error);
        }
    }

    private async void WakeConsole_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChiakiConsoleViewModel cvm })
        {
            if (string.IsNullOrEmpty(cvm.Model.RegistKey)) return;
            var (success, error, _) = await _chiaki.WakeConsoleAsync(
                cvm.Host, cvm.Model.RegistKey!, CancellationToken.None);
            if (!success)
                Log.Warning("[chiaki] Wake failed: {Error}", error);
        }
    }

    private void StopConsole_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string host })
            _chiaki.StopStream("console:" + host);
    }

    private void RemoveConsole_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChiakiConsoleViewModel cvm })
        {
            VM.Consoles.Remove(cvm);
            VM.NotifyConsolesChanged();
            SaveConfig();
        }
    }

    // ── Discover tab ─────────────────────────────────────────────────────────

    private async void Discover_Click(object? sender, RoutedEventArgs e)
    {
        VM.IsDiscovering = true;
        VM.DiscoveredConsoles.Clear();
        try
        {
            var (success, consoles, error) = await _chiaki.DiscoverConsolesAsync();
            var existingHosts = VM.Consoles.Select(c => c.Host).ToHashSet();
            foreach (var dc in consoles)
                VM.DiscoveredConsoles.Add(new DiscoveredConsoleViewModel(dc, existingHosts.Contains(dc.Host)));
            if (!success)
                Log.Warning("[chiaki] Discover error: {Error}", error);
        }
        catch (Exception ex) { Log.Error(ex, "[chiaki] Discovery failed"); }
        finally { VM.IsDiscovering = false; }
    }

    private void AddDiscovered_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiscoveredConsoleViewModel dvm })
        {
            this.FindControl<TextBox>("NewNicknameBox")!.Text = dvm.Name;
            this.FindControl<TextBox>("NewHostBox")!.Text     = dvm.Host;
            this.FindControl<TextBox>("NewProfileBox")!.Text  = "";
            VM.ShowAddConsoleForm = true;
            VM.SetTab("consoles");
        }
    }

    private void RegisterDiscovered_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiscoveredConsoleViewModel dvm })
        {
            this.FindControl<TextBox>("RegHostBox")!.Text = dvm.Host;
            VM.SetTab("register");
        }
    }

    // ── Register tab ─────────────────────────────────────────────────────────

    private async void Register_Click(object? sender, RoutedEventArgs e)
    {
        var host      = this.FindControl<TextBox>("RegHostBox")!.Text?.Trim() ?? "";
        var accountId = this.FindControl<TextBox>("RegAccountIdBox")!.Text?.Trim() ?? "";
        var pin       = this.FindControl<TextBox>("RegPinBox")!.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(pin)) return;

        VM.SetRegPending();

        var (success, registKey, morning, error) = await _chiaki.RegisterConsoleAsync(
            host, accountId, pin, CancellationToken.None);

        if (success)
        {
            VM.SetRegStatus(true, "Registered successfully! Keys saved.");
            var existing = VM.Consoles.FirstOrDefault(c => c.Host == host);
            if (existing is not null)
            {
                existing.Model.RegistKey = registKey;
                existing.Model.Morning   = morning;
            }
            else
            {
                VM.Consoles.Add(new ChiakiConsoleViewModel
                {
                    Model = new ChiakiConsole
                    {
                        Nickname  = host,
                        Host      = host,
                        RegistKey = registKey,
                        Morning   = morning,
                    }
                });
                VM.NotifyConsolesChanged();
            }
            SaveConfig();
        }
        else
        {
            VM.SetRegStatus(false, "Registration failed" + (error is not null ? ": " + error : ""));
        }
    }

    private void RegReset_Click(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("RegHostBox")!.Text      = "";
        this.FindControl<TextBox>("RegAccountIdBox")!.Text = "";
        this.FindControl<TextBox>("RegPinBox")!.Text       = "";
        VM.RegStatus = null;
    }

    // ── Session events ────────────────────────────────────────────────────────

    private void OnChiakiEvent(object? sender, ChiakiEventArgs e)
    {
        try
        {
            if (e.Type != "state" && e.Type != "disconnected") return;
            var host   = e.GameId.Replace("console:", "");
            var isLive = e.Type == "state" && e.Data.TryGetValue("state", out var st) &&
                         st?.ToString() is "launching" or "connecting" or "streaming" or "gui";
            Dispatcher.UIThread.Post(() => VM.UpdateConsoleSessionState(host, isLive));
        }
        catch (Exception ex) { Log.Warning(ex, "[chiaki] event handler failed"); }
    }
}
