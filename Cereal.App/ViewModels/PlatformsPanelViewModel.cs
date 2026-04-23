// ─── PlatformsPanel ViewModel ────────────────────────────────────────────────
// Parity port of src/components/PlatformsPanel.tsx.
// Lists each supported platform with its connection/install state + controls
// to sign-in via OAuth, import library, save an API key, and disconnect.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Providers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.ViewModels;

// Parse hex color strings into brushes for the platform pill backgrounds.
public sealed class ColorStringToBrushConverter : IValueConverter
{
    public static readonly ColorStringToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Color.TryParse(s, out var c))
            return new SolidColorBrush(c);
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class PlatformsPanelViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly GameService _games;
    private readonly DatabaseService _db;
    private readonly CoverService _covers;
    private readonly CredentialService _creds;
    private readonly IEnumerable<IImportProvider> _importProviders;
    private readonly IEnumerable<IProvider> _allProviders;

    public ObservableCollection<PlatformRowViewModel> Platforms { get; } = [];

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string? _statusMessage;

    public event EventHandler? ChiakiRequested;
    public event EventHandler? XcloudRequested;

    public PlatformsPanelViewModel(AuthService auth, GameService games, DatabaseService db,
        CoverService covers, CredentialService creds,
        IEnumerable<IImportProvider> importProviders, IEnumerable<IProvider> allProviders)
    {
        _auth = auth;
        _games = games;
        _db = db;
        _covers = covers;
        _creds = creds;
        _importProviders = importProviders;
        _allProviders = allProviders;

        // Ordered list mirrors source PLATS array
        foreach (var id in new[] { "steam", "gog", "epic", "xbox", "psn", "ea", "battlenet", "itchio", "ubisoft" })
            Platforms.Add(new PlatformRowViewModel(id, this));

        _ = RefreshAllAsync();
    }

    public IEnumerable<PlatformRowViewModel> FilteredPlatforms =>
        string.IsNullOrWhiteSpace(Filter)
            ? Platforms
            : Platforms.Where(p => p.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredPlatforms));

    public async Task RefreshAllAsync()
    {
        foreach (var row in Platforms)
            await row.RefreshStatusAsync();
    }

    [RelayCommand] private void OpenChiaki() => ChiakiRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void OpenXcloud() => XcloudRequested?.Invoke(this, EventArgs.Empty);

    // ─── Helpers used by rows ────────────────────────────────────────────────

    internal IImportProvider? GetImportProvider(string id) =>
        _importProviders.FirstOrDefault(p => p.PlatformId == id);

    internal IProvider? GetProvider(string id) =>
        _allProviders.FirstOrDefault(p => p.PlatformId == id);

    internal AccountInfo? GetAccount(string id) =>
        _db.Db.Accounts.GetValueOrDefault(id);

    internal async Task SignInAsync(string platform)
    {
        try
        {
            var url = platform switch
            {
                "steam" => _auth.GetSteamAuthUrl(),
                "gog"   => _auth.GetGogAuthUrl(),
                "epic"  => _auth.GetEpicAuthUrl(),
                "xbox"  => _auth.GetXboxAuthUrl(),
                _       => throw new NotSupportedException("Sign-in not supported for " + platform),
            };

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusMessage = $"Waiting for {platform} sign-in…";

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var account = await _auth.WaitForCallbackAsync(platform, ct: cts.Token);
            StatusMessage = $"{platform} connected as {account.Username ?? account.AccountId}.";

            // Auto-import after sign-in
            await ImportAsync(platform);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[platforms] Sign-in failed for {Platform}", platform);
            StatusMessage = $"Sign-in failed: {ex.Message}";
        }
    }

    internal async Task ImportAsync(string platform)
    {
        var provider = GetImportProvider(platform);
        if (provider is null)
        {
            // Fallback to local-detect-only providers
            var local = GetProvider(platform);
            if (local is null) { StatusMessage = $"No provider for {platform}"; return; }
            StatusMessage = $"Detecting {platform} games…";
            var det = await local.DetectInstalled();
            var added = MergeGames(det.Games);
            StatusMessage = $"{platform}: {added} game(s) merged from local detection.";
            return;
        }

        StatusMessage = $"Importing {platform} library…";
        try
        {
            var ctx = new ImportContext
            {
                Db = _db,
                ApiKey = _creds.GetPassword("cereal", $"{platform}_api_key"),
                Http = new HttpClient(),
                Notify = p => Dispatcher.UIThread.Post(() =>
                    StatusMessage = $"{platform}: {p.Processed}/{p.Total} {p.Name ?? ""}"),
            };
            var result = await provider.ImportLibrary(ctx);
            if (!string.IsNullOrEmpty(result.Error))
            {
                StatusMessage = $"{platform} import failed: {result.Error}";
                return;
            }

            // Enqueue covers for new games
            foreach (var g in _db.Db.Games.Where(g => g.Platform == platform))
                _covers.EnqueueGame(g.Id);

            StatusMessage = result.Imported.Count + result.Updated.Count > 0
                ? $"{platform}: {result.Imported.Count} new, {result.Updated.Count} updated"
                : $"{platform}: library already up to date";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[platforms] Import failed for {Platform}", platform);
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    private int MergeGames(IEnumerable<Game> games)
    {
        var added = 0;
        foreach (var g in games)
        {
            _games.Add(g);
            added++;
        }
        return added;
    }

    internal void Disconnect(string platform)
    {
        _auth.SignOut(platform);
        StatusMessage = $"{platform} disconnected.";
    }

    internal void SaveApiKey(string platform, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { StatusMessage = "Enter a key to save."; return; }
        _creds.SetPassword("cereal", $"{platform}_api_key", key);
        StatusMessage = $"{platform} API key saved.";
    }

    internal void DeleteApiKey(string platform)
    {
        _creds.DeletePassword("cereal", $"{platform}_api_key");
        StatusMessage = $"{platform} API key deleted.";
    }

    internal bool HasApiKey(string platform) =>
        !string.IsNullOrEmpty(_creds.GetPassword("cereal", $"{platform}_api_key"));

    // Short, transient status line used by paste-key handlers (mirrors the
    // Electron version's top-right toast).
    internal void Flash(string msg) => StatusMessage = msg;
}

public partial class PlatformRowViewModel : ObservableObject
{
    private readonly PlatformsPanelViewModel _parent;

    public string Id { get; }
    public string Name { get; }
    public string Icon { get; }
    public string Color { get; }
    public string? Note { get; }
    public string? ApiKeyLabel { get; }
    public string? ApiKeyHelp { get; }
    public string? ApiKeyUrl { get; }
    public bool SupportsApiKey => ApiKeyLabel is not null;
    public bool SupportsSignIn { get; }
    public bool IsPsn => Id == "psn";
    public bool IsXbox => Id == "xbox";

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private int _installedCount;
    [ObservableProperty] private string? _accountName;
    [ObservableProperty] private string? _accountAvatar;
    [ObservableProperty] private string _apiKeyInput = "";
    [ObservableProperty] private bool _hasSavedApiKey;

    public string StatusLabel
    {
        get
        {
            if (IsConnected) return AccountName ?? "Connected";
            if (InstalledCount > 0) return $"{InstalledCount} installed locally";
            return IsPsn ? "Not configured" : "Not connected";
        }
    }

    public string DotClass => IsConnected ? "ok" : (InstalledCount > 0 || IsXbox ? "warn" : "off");

    // Combined visibility flags (simpler than MultiBindings in XAML).
    public bool ShowSignIn => SupportsSignIn && !IsConnected;
    public bool ShowRescan => !SupportsSignIn && !IsPsn;
    public bool DotOk => IsConnected;
    public bool DotWarn => !IsConnected && (InstalledCount > 0 || IsXbox);

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(DotClass));
        OnPropertyChanged(nameof(ShowSignIn));
        OnPropertyChanged(nameof(DotOk));
        OnPropertyChanged(nameof(DotWarn));
    }

    partial void OnInstalledCountChanged(int value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(DotClass));
        OnPropertyChanged(nameof(DotWarn));
    }

    partial void OnAccountNameChanged(string? value) => OnPropertyChanged(nameof(StatusLabel));

    public PlatformRowViewModel(string id, PlatformsPanelViewModel parent)
    {
        _parent = parent;
        Id = id;

        (Name, Icon, Color, Note, ApiKeyLabel, ApiKeyHelp, ApiKeyUrl, SupportsSignIn) = id switch
        {
            "steam" => ("Steam", "S", "#1b2838",
                "Sign in to import your library. An API key is only needed for private profiles.",
                "API Key (optional — for private profiles)",
                "Only needed if your Steam profile is set to private. Get your key at steamcommunity.com/dev/apikey.",
                "https://steamcommunity.com/dev/apikey", true),
            "gog" => ("GOG", "G", "#3a1a50", null, null, null, null, true),
            "epic" => ("Epic Games", "E", "#2a2a2a",
                "Epic's developer APIs require special registration and may limit library imports.",
                null, null, null, true),
            "xbox" => ("Xbox", "X", "#0e6a0e", null, null, null, null, true),
            "psn"  => ("PlayStation", "P", "#003087", null, null, null, null, false),
            "ea"   => ("EA App", "EA", "#0f6fc6",
                "Scans your local EA App installation for installed games.", null, null, null, false),
            "battlenet" => ("Battle.net", "BN", "#148eff",
                "Scans your local Battle.net installation for installed games.", null, null, null, false),
            "itchio" => ("itch.io", "io", "#e8395c",
                "Scans locally installed itch.io games.",
                "API Key (optional)",
                "An itch.io API key enables importing your full purchased games library.",
                "https://itch.io/user/settings/api-keys", false),
            "ubisoft" => ("Ubisoft Connect", "U", "#003791",
                "Scans your local Ubisoft Connect installation for installed games.", null, null, null, false),
            _ => (id, id[..1].ToUpperInvariant(), "#888", null, null, null, null, false),
        };

        HasSavedApiKey = _parent.HasApiKey(id);
    }

    public async Task RefreshStatusAsync()
    {
        var account = _parent.GetAccount(Id);
        IsConnected = account is not null;
        AccountName = account?.Username ?? account?.AccountId;

        var provider = _parent.GetProvider(Id);
        if (provider is not null)
        {
            try
            {
                var result = await provider.DetectInstalled();
                InstalledCount = result.Games.Count;
            }
            catch { InstalledCount = 0; }
        }

        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(DotClass));
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (!SupportsSignIn) return;
        IsLoading = true;
        try { await _parent.SignInAsync(Id); await RefreshStatusAsync(); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task Import()
    {
        IsImporting = true;
        try { await _parent.ImportAsync(Id); await RefreshStatusAsync(); }
        finally { IsImporting = false; }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        _parent.Disconnect(Id);
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        _parent.SaveApiKey(Id, ApiKeyInput);
        if (!string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            ApiKeyInput = "";
            HasSavedApiKey = true;
        }
    }

    [RelayCommand]
    private void DeleteApiKey()
    {
        _parent.DeleteApiKey(Id);
        HasSavedApiKey = false;
        ApiKeyInput = "";
    }

    [RelayCommand]
    private async Task PasteApiKey()
    {
        var txt = (await App.ReadClipboardTextAsync()).Trim();
        if (string.IsNullOrEmpty(txt))
        {
            _parent.Flash("Clipboard is empty");
            return;
        }
        ApiKeyInput = txt;
        _parent.SaveApiKey(Id, txt);
        ApiKeyInput = "";
        HasSavedApiKey = true;
        _parent.Flash("Pasted key saved");
    }

    [RelayCommand]
    private void OpenApiKeyUrl()
    {
        if (!string.IsNullOrEmpty(ApiKeyUrl))
            Process.Start(new ProcessStartInfo(ApiKeyUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenChiaki() => _parent.OpenChiakiCommand.Execute(null);

    [RelayCommand]
    private void OpenXcloud() => _parent.OpenXcloudCommand.Execute(null);
}
