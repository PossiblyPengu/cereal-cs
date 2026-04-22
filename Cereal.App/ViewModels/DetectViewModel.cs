using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Providers;

namespace Cereal.App.ViewModels;

public partial class DetectViewModel : ObservableObject
{
    private readonly GameService _games;
    private readonly DatabaseService _db;
    private readonly IEnumerable<IProvider> _allProviders;
    private readonly IEnumerable<IImportProvider> _importProviders;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _foundCount;
    [ObservableProperty] private int _addedCount;

    public ObservableCollection<ProviderToggle> Providers { get; } = [];
    public ObservableCollection<DetectedGameRow> Results { get; } = [];

    public DetectViewModel(
        GameService games,
        DatabaseService db,
        IEnumerable<IProvider> allProviders,
        IEnumerable<IImportProvider> importProviders)
    {
        _games = games;
        _db = db;
        _allProviders = allProviders;
        _importProviders = importProviders;

        foreach (var p in allProviders)
            Providers.Add(new ProviderToggle(p.PlatformId, true));
    }

    // ─── Detect (local scan) ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task Scan()
    {
        IsScanning = true;
        StatusMessage = "Scanning installed games…";
        Results.Clear();
        FoundCount = 0;
        AddedCount = 0;

        var enabledIds = Providers.Where(p => p.Enabled).Select(p => p.PlatformId).ToHashSet();

        foreach (var provider in _allProviders.Where(p => enabledIds.Contains(p.PlatformId)))
        {
            StatusMessage = $"Scanning {provider.PlatformId}…";
            try
            {
                var result = await provider.DetectInstalled();
                foreach (var game in result.Games)
                {
                    Results.Add(new DetectedGameRow(game, provider.PlatformId));
                    FoundCount++;
                }
                if (result.Error is not null)
                    Results.Add(new DetectedGameRow(null, provider.PlatformId) { Error = result.Error });
            }
            catch (Exception ex)
            {
                Results.Add(new DetectedGameRow(null, provider.PlatformId) { Error = ex.Message });
            }
        }

        StatusMessage = $"Scan complete — {FoundCount} games found.";
        IsScanning = false;
    }

    private bool CanScan() => !IsScanning;

    // ─── Import selected ─────────────────────────────────────────────────────

    [RelayCommand]
    private void ImportSelected()
    {
        var selected = Results.Where(r => r.IsSelected && r.Game is not null).ToList();
        AddedCount = 0;
        foreach (var row in selected)
        {
            _games.Add(row.Game!);
            row.IsImported = true;
            AddedCount++;
        }
        StatusMessage = AddedCount > 0 ? $"Imported {AddedCount} game(s)." : "Nothing new to import.";
    }

    [RelayCommand]
    private void ImportAll()
    {
        foreach (var r in Results.Where(r => r.Game is not null)) r.IsSelected = true;
        ImportSelected();
    }

    // ─── Full API import ──────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ImportFromApi(string platformId)
    {
        var provider = _importProviders.FirstOrDefault(p => p.PlatformId == platformId);
        if (provider is null) return;

        IsScanning = true;
        StatusMessage = $"Importing library from {platformId}…";
        AddedCount = 0;

        try
        {
            var progress = new List<ImportProgress>();
            var ctx = new ImportContext
            {
                Db   = _db,
                Http = new System.Net.Http.HttpClient(),
                Notify = p =>
                {
                    progress.Add(p);
                    StatusMessage = $"[{platformId}] {p.Name ?? p.Status} ({p.Processed}/{p.Total})";
                },
            };

            var result = await provider.ImportLibrary(ctx);
            AddedCount = result.Imported.Count;
            StatusMessage = $"Imported {result.Imported.Count} game(s) from {platformId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}

// ─── Supporting types ──────────────────────────────────────────────────────────

public partial class ProviderToggle(string platformId, bool enabled) : ObservableObject
{
    public string PlatformId { get; } = platformId;
    [ObservableProperty] private bool _enabled = enabled;
}

public partial class DetectedGameRow : ObservableObject
{
    public Game? Game { get; }
    public string PlatformId { get; }
    public string? Error { get; set; }
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isImported;

    public string Name => Game?.Name ?? (Error is not null ? $"[{PlatformId}] {Error}" : $"[{PlatformId} error]");
    public string Platform => Game?.Platform ?? PlatformId;

    public DetectedGameRow(Game? game, string platformId)
    {
        Game = game;
        PlatformId = platformId;
    }
}
