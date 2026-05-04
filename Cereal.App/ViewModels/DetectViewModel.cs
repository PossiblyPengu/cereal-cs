using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Providers;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.App.ViewModels;

// ── AXAML compat stubs (legacy types referenced by DetectPanel.axaml) ────────
/// <summary>AXAML compatibility stub replacing the old ProviderToggle class.</summary>
public sealed partial class ProviderToggle : ObservableObject
{
    public string PlatformId { get; init; } = string.Empty;
    public string Label      { get; init; } = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    public bool Enabled { get => IsEnabled; set => IsEnabled = value; }
}

/// <summary>AXAML compatibility stub replacing the old DetectedGameRow class.</summary>
public sealed partial class DetectedGameRow : ObservableObject
{
    public string Name       { get; init; } = string.Empty;
    public string Platform   { get; init; } = string.Empty;
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isImported;
}

/// <summary>
/// Row representing a single provider's scan result in the Detect view.
/// </summary>
public sealed partial class ProviderScanRow : ObservableObject
{
    public string PlatformId    { get; }
    public string Label         { get; }

    // AXAML compatibility aliases
    public string Name      => Label;
    public string Platform  => PlatformId;

    [ObservableProperty] private bool    _isScanning;
    [ObservableProperty] private bool    _isSelected;
    [ObservableProperty] private bool    _isImported;
    [ObservableProperty] private int     _gamesFound;
    [ObservableProperty] private bool    _isDone;
    [ObservableProperty] private string? _error;

    public ProviderScanRow(string platformId, string label)
    {
        PlatformId = platformId;
        Label      = label;
    }
}

/// <summary>
/// Drives the "Detect installed games" scan view.
/// Replaces the old DetectViewModel that used old Cereal.App.Services.Providers.
/// Iterates all registered IProvider instances in parallel and reports
/// scan results through ProviderScanRow entries.
/// </summary>
public sealed partial class DetectViewModel : ObservableObject
{
    private readonly IEnumerable<IProvider> _providers;
    private readonly IGameService _games;
    private readonly IMessenger _messenger;

    public ObservableCollection<ProviderScanRow> Rows { get; } = [];

    // AXAML compat: old view used Providers collection of ProviderToggle
    public ObservableCollection<ProviderToggle> Providers { get; } = [];

    [ObservableProperty] private bool    _isRunning;
    [ObservableProperty] private int     _totalFound;
    [ObservableProperty] private string? _statusMessage;

    // AXAML compatibility aliases
    public bool IsScanning => IsRunning;
    public int  FoundCount => TotalFound;
    public int  AddedCount => 0; // deprecated — kept for AXAML compat
    public System.Collections.ObjectModel.ObservableCollection<ProviderScanRow> Results => Rows;

    public DetectViewModel(
        IEnumerable<IProvider> providers,
        IGameService games,
        IMessenger messenger)
    {
        _providers = providers;
        _games     = games;
        _messenger = messenger;

        foreach (var p in providers)
            Rows.Add(new ProviderScanRow(p.PlatformId, Utilities.PlatformInfo.GetLabel(p.PlatformId)));
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        await ScanAsync();
    }

    // Aliases so old AXAML bindings still compile during migration
    [RelayCommand] private Task ScanAsync() => RunInternalAsync();
    [RelayCommand] private void ImportSelected() { /* Phase G */ }
    [RelayCommand] private void ImportAll() { /* Phase G */ }
    [RelayCommand] private void ImportNewOnly() { /* Phase G */ }
    [RelayCommand] private void SelectNone() { foreach (var r in Rows) r.IsDone = false; }
    [RelayCommand] private void ClearResults() { Rows.Clear(); TotalFound = 0; StatusMessage = null; }

    private async Task RunInternalAsync()
    {
        if (IsRunning) return;
        IsRunning     = true;
        TotalFound    = 0;
        StatusMessage = "Scanning...";

        foreach (var row in Rows)
        {
            row.IsScanning = true;
            row.GamesFound = 0;
            row.IsDone     = false;
            row.Error      = null;
        }

        try
        {
            var tasks = _providers.Select(async p =>
            {
                var row = Rows.FirstOrDefault(r => r.PlatformId == p.PlatformId);
                if (row is null) return;

                try
                {
                    var result = await p.DetectInstalledAsync();
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        row.Error = result.Error;
                    }
                    else if (result.Games.Count > 0)
                    {
                        await _games.UpsertRangeAsync(result.Games);
                        row.GamesFound = result.Games.Count;
                    }
                }
                catch (Exception ex)
                {
                    row.Error = ex.Message;
                    Log.Warning(ex, "[Detect] Provider {Platform} failed", p.PlatformId);
                }
                finally
                {
                    row.IsScanning = false;
                    row.IsDone     = true;
                }
            });

            await Task.WhenAll(tasks);

            TotalFound = Rows.Sum(r => r.GamesFound);
            StatusMessage = TotalFound > 0
                ? $"Found {TotalFound} installed games."
                : "No games detected.";

            if (TotalFound > 0)
                _messenger.Send(new LibraryRefreshedMessage(TotalFound));
        }
        finally
        {
            IsRunning = false;
        }
    }
}
