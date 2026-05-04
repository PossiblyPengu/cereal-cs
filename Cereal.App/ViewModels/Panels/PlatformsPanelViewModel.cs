using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using Cereal.Core.Providers;
using Cereal.Core.Services;
using Cereal.App.Utilities;
using Serilog;

namespace Cereal.App.ViewModels.Panels;

/// <summary>
/// Row entry for a single platform in the Platforms panel.
/// </summary>
public sealed partial class PlatformRowViewModel : ObservableObject
{
    public string Id { get; }
    public string Label { get; }
    public string Color { get; }
    public string? LogoPath => PlatformLogos.TryGet(Id)?.PathData;

    [ObservableProperty] private bool _isDetecting;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private int  _importProgress;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _connectedAs;
    [ObservableProperty] private int _gameCount;
    [ObservableProperty] private string? _statusMessage;

    public PlatformRowViewModel(string id)
    {
        Id    = id;
        Label = PlatformInfo.GetLabel(id);
        Color = PlatformInfo.GetColor(id);
    }
}

/// <summary>
/// Drives the Platforms panel.  Replaces the old <c>PlatformsPanelViewModel</c>
/// that had direct App.Services access; everything now arrives via constructor DI
/// and messaging.
/// </summary>
public sealed partial class PlatformsPanelViewModel : ObservableObject,
    IRecipient<LibraryRefreshedMessage>
{
    private readonly IAuthService _auth;
    private readonly IGameService _games;
    private readonly ISettingsService _settings;
    private readonly IEnumerable<IProvider> _providers;
    private readonly IEnumerable<IImportProvider> _importProviders;
    private readonly IMessenger _messenger;
    private readonly IServiceProvider _serviceProvider;

    public ObservableCollection<PlatformRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string? _globalStatus;

    public PlatformsPanelViewModel(
        IAuthService auth,
        IGameService games,
        ISettingsService settings,
        IEnumerable<IProvider> providers,
        IEnumerable<IImportProvider> importProviders,
        IMessenger messenger,
        IServiceProvider serviceProvider)
    {
        _auth            = auth;
        _games           = games;
        _settings        = settings;
        _providers       = providers;
        _importProviders = importProviders;
        _messenger       = messenger;
        _serviceProvider = serviceProvider;
        messenger.Register(this);

        foreach (var id in new[] { "steam", "gog", "epic", "xbox", "ea", "battlenet", "itchio", "ubisoft" })
            Rows.Add(new PlatformRowViewModel(id));

        _ = RefreshCountsAsync();
    }

    public void Receive(LibraryRefreshedMessage msg) => _ = RefreshCountsAsync();

    // ── Detect installed ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task DetectAsync(string platformId)
    {
        var row = Rows.FirstOrDefault(r => r.Id == platformId);
        if (row is null) return;

        var provider = _providers.FirstOrDefault(p => p.PlatformId == platformId);
        if (provider is null) return;

        row.IsDetecting = true;
        row.StatusMessage = "Detecting…";
        try
        {
            var result = await provider.DetectInstalledAsync();
            if (!string.IsNullOrEmpty(result.Error))
            {
                row.StatusMessage = result.Error;
                return;
            }

            if (result.Games.Count == 0)
            {
                row.StatusMessage = "No games detected.";
                return;
            }

            var (_, newRows, survivors) = await _games.UpsertRangeAsync(result.Games);
            row.GameCount     = survivors.Count;
            row.StatusMessage = $"Detected {survivors.Count} games.";
        }
        catch (Exception ex)
        {
            row.StatusMessage = $"Error: {ex.Message}";
            Log.Warning(ex, "[platforms] DetectAsync failed for {Platform}", platformId);
        }
        finally { row.IsDetecting = false; }
    }

    // ── Import library ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportAsync(string platformId)
    {
        var row = Rows.FirstOrDefault(r => r.Id == platformId);
        if (row is null) return;

        var provider = _importProviders.FirstOrDefault(p => p.PlatformId == platformId);
        if (provider is null) { row.StatusMessage = "Import not supported."; return; }

        row.IsImporting = true;
        row.StatusMessage = "Importing…";
        try
        {
            var appSettings = _settings.Current;
            var ctx = new ImportContext
            {
                Services = _serviceProvider,
                ApiKey   = appSettings.SteamGridDbKey,
                Notify   = p =>
                {
                    row.StatusMessage    = p.Name ?? p.Status;
                    row.ImportProgress   = p.Total > 0 ? (int)(p.Processed * 100.0 / p.Total) : 0;
                },
            };
            var result = await provider.ImportLibraryAsync(ctx);
            row.StatusMessage = result.Error is not null
                ? $"Error: {result.Error}"
                : $"Imported {result.Imported.Count} games.";
        }
        catch (Exception ex)
        {
            row.StatusMessage = $"Error: {ex.Message}";
            Log.Warning(ex, "[platforms] ImportAsync failed for {Platform}", platformId);
        }
        finally { row.IsImporting = false; }
    }

    // ── OAuth sign-in ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ConnectAsync(string platformId)
    {
        var row = Rows.FirstOrDefault(r => r.Id == platformId);
        if (row is null) return;
        try
        {
            var session = await _auth.AuthenticateAsync(platformId);
            row.IsConnected = true;
            row.StatusMessage = "Connected.";
        }
        catch (NotImplementedException)
        {
            row.StatusMessage = "Not yet supported.";
        }
        catch (Exception ex)
        {
            row.StatusMessage = $"Auth failed: {ex.Message}";
            Log.Warning(ex, "[platforms] ConnectAsync failed for {Platform}", platformId);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync(string platformId)
    {
        var row = Rows.FirstOrDefault(r => r.Id == platformId);
        if (row is null) return;
        await _auth.SignOutAsync(platformId);
        row.IsConnected  = false;
        row.ConnectedAs  = null;
        row.StatusMessage = "Disconnected.";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RefreshCountsAsync()
    {
        // Single GROUP BY query — no full library load.
        var counts = await _games.GetPlatformCountsAsync();
        foreach (var row in Rows)
            row.GameCount = counts.TryGetValue(row.Id, out var c) ? c : 0;

        // Reflect auth state
        foreach (var row in Rows)
        {
            row.IsConnected = _auth.IsAuthenticated(row.Id);
        }
    }
}
