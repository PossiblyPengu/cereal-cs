// ─── PlatformsPanel ViewModel ────────────────────────────────────────────────
// Parity port of src/components/PlatformsPanel.tsx.
// Lists each supported platform with its connection/install state + controls
// to sign-in via OAuth, import library, save an API key, and disconnect.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
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
    /// <summary>Fired to open the in-app WebView (MainWindow tab + side panel) before waiting on the callback.</summary>
    public event Action<string, string>? InAppAuthNavigate;
    /// <summary>Fired when the OAuth wait ends or should tear down the WebView (success, failure, or cancel).</summary>
    public event Action? InAppAuthFlowEnded;

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
        foreach (var id in new[] { "steam", "gog", "epic", "xbox", "ea", "battlenet", "itchio", "ubisoft" })
            Platforms.Add(new PlatformRowViewModel(id, this));

        _ = RefreshAllAsync();
    }

    public IEnumerable<PlatformRowViewModel> FilteredPlatforms =>
        string.IsNullOrWhiteSpace(Filter)
            ? Platforms
            : Platforms.Where(p => p.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredPlatforms));

    [RelayCommand]
    private void ClearFilter() => Filter = string.Empty;

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

            var tabTitle = InAppAuthTabTitle(platform);
            InAppAuthNavigate?.Invoke(url, tabTitle);
            StatusMessage = $"Waiting for {platform} sign-in…";

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var account = await _auth.WaitForCallbackAsync(platform, ct: cts.Token);
            InAppAuthFlowEnded?.Invoke();
            StatusMessage = $"{platform} connected as {account.Username ?? account.AccountId}.";

            // Auto-import after sign-in
            await ImportAsync(platform);
        }
        catch (Exception ex)
        {
            InAppAuthFlowEnded?.Invoke();
            Log.Warning(ex, "[platforms] Sign-in failed for {Platform}", platform);
            StatusMessage = $"Sign-in failed: {ex.Message}";
        }
    }

    private static string InAppAuthTabTitle(string platform) => platform switch
    {
        "steam" => "Steam sign-in",
        "gog"   => "GOG sign-in",
        "epic"  => "Epic sign-in",
        "xbox"  => "Xbox sign-in",
        _       => "Sign in",
    };

    internal async Task ImportAsync(string platform)
        => await ImportAsync(platform, null);

    internal async Task ImportAsync(string platform, Action<ImportProgress>? onProgress)
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
            await _auth.RefreshTokenIfNeededAsync(platform);
            using var http = new HttpClient();
            var ctx = new ImportContext
            {
                Db = _db,
                ApiKey = _creds.GetPassword("cereal", $"{platform}_api_key"),
                Http = http,
                Notify = p => Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"{platform}: {p.Processed}/{p.Total} {p.Name ?? ""}";
                    onProgress?.Invoke(p);
                }),
            };
            var result = await provider.ImportLibrary(ctx);
            if (!string.IsNullOrEmpty(result.Error))
            {
                if (result.Error.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                    result.Error.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                    result.Error.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    result.Error.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    var refreshed = await _auth.RefreshTokenIfNeededAsync(platform);
                    if (refreshed)
                    {
                        result = await provider.ImportLibrary(ctx);
                    }
                }
            }
            if (!string.IsNullOrEmpty(result.Error))
            {
                if (platform == "steam" && string.IsNullOrWhiteSpace(_creds.GetPassword("cereal", "steam_api_key")))
                    StatusMessage = "steam import failed. Private profile? Add a Steam API key and retry.";
                else
                    StatusMessage = $"{platform} import failed: {result.Error}";
                return;
            }

            var reconciledInstalled = 0;
            if (platform is "steam" or "gog" or "epic")
                reconciledInstalled = await ReconcileInstalledAfterImportAsync(platform);

            // Enqueue covers for new games
            foreach (var g in _db.Db.Games.Where(g => g.Platform == platform))
                _covers.EnqueueGame(g.Id);

            StatusMessage = result.Imported.Count + result.Updated.Count > 0
                ? $"{platform}: {result.Imported.Count} new, {result.Updated.Count} updated"
                : $"{platform}: library already up to date";
            if (reconciledInstalled > 0)
                StatusMessage += $" - {reconciledInstalled} install states reconciled";

            var acct = _db.Db.Accounts.GetValueOrDefault(platform);
            if (acct is not null)
            {
                acct.LastSyncMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _db.Save();
            }
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

    private async Task<int> ReconcileInstalledAfterImportAsync(string platform)
    {
        var provider = GetProvider(platform);
        if (provider is null) return 0;
        try
        {
            var detected = await provider.DetectInstalled();
            var installedOnly = detected.Games.Where(g => g.Installed == true);
            return MergeGames(installedOnly);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[platforms] ReconcileInstalledAfterImportAsync failed for {Platform}", platform);
            return 0;
        }
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

    internal string? GetApiKey(string platform) =>
        _creds.GetPassword("cereal", $"{platform}_api_key");

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
    public bool IsXbox => Id == "xbox";

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private int _installedCount;
    [ObservableProperty] private string? _accountName;
    [ObservableProperty] private string? _accountAvatar;
    [ObservableProperty] private string? _accountSubLabel;
    [ObservableProperty] private string _apiKeyInput = "";
    [ObservableProperty] private bool _hasSavedApiKey;
    [ObservableProperty] private string? _apiKeyStatus;
    [ObservableProperty] private bool _isValidatingApiKey;
    [ObservableProperty] private string? _importProgressText;
    [ObservableProperty] private double _importProgressPercent;

    public string StatusLabel
    {
        get
        {
            if (IsConnected) return AccountName ?? "Connected";
            if (InstalledCount > 0) return $"{InstalledCount} installed locally";
            return "Not connected";
        }
    }

    public string DotClass => IsConnected ? "ok" : (InstalledCount > 0 || IsXbox ? "warn" : "off");

    // Combined visibility flags (simpler than MultiBindings in XAML).
    public bool ShowSignIn => SupportsSignIn && !IsConnected;
    public bool ShowReconnect => SupportsSignIn && IsConnected;
    public bool ShowRescan => !SupportsSignIn;
    public bool DotOk => IsConnected;
    public bool DotWarn => !IsConnected && (InstalledCount > 0 || IsXbox);

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(DotClass));
        OnPropertyChanged(nameof(ShowSignIn));
        OnPropertyChanged(nameof(ShowReconnect));
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
    partial void OnAccountSubLabelChanged(string? value) => OnPropertyChanged(nameof(StatusLabel));

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
        AccountName = account?.DisplayName ?? account?.Username ?? account?.AccountId;
        AccountAvatar = account?.AvatarUrl;
        AccountSubLabel = GetAccountSubLabel(account);

        var provider = _parent.GetProvider(Id);
        if (provider is not null)
        {
            try
            {
                var result = await provider.DetectInstalled();
                InstalledCount = result.Games.Count;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[platforms] DetectInstalled failed in RefreshStatusAsync for {Platform}", Id);
                InstalledCount = 0;
            }
        }

        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(DotClass));
    }

    private static string FormatAgo(long timestampMs)
    {
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
        var span = DateTimeOffset.UtcNow - dt;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private static string? GetAccountSubLabel(AccountInfo? account)
    {
        if (account is null) return null;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (account.ExpiresAt is long exp)
        {
            if (exp <= nowMs) return "Session expired - sign in again";
            if (exp <= nowMs + (15 * 60 * 1000)) return "Session expiring soon";
        }
        if (account.LastSyncMs is long ts) return $"Synced {FormatAgo(ts)}";
        return !string.IsNullOrWhiteSpace(account.AccountId) ? $"ID: {account.AccountId}" : null;
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
    private async Task Reconnect()
    {
        if (!SupportsSignIn) return;
        IsLoading = true;
        try
        {
            _parent.Disconnect(Id);
            await _parent.SignInAsync(Id);
            await RefreshStatusAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task Import()
    {
        IsImporting = true;
        ImportProgressText = "Starting import…";
        ImportProgressPercent = 0;
        try
        {
            await _parent.ImportAsync(Id, p =>
            {
                ImportProgressText = string.IsNullOrWhiteSpace(p.Name)
                    ? $"{p.Processed}/{p.Total}"
                    : $"{p.Processed}/{p.Total} - {p.Name}";
                ImportProgressPercent = p.Total > 0
                    ? Math.Clamp((double)p.Processed / p.Total, 0, 1)
                    : 0;
            });
            await RefreshStatusAsync();
            if (ImportProgressPercent >= 1)
                ImportProgressText = "Import complete.";
        }
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
            ApiKeyStatus = null;
        }
    }

    [RelayCommand]
    private void DeleteApiKey()
    {
        _parent.DeleteApiKey(Id);
        HasSavedApiKey = false;
        ApiKeyInput = "";
        ApiKeyStatus = null;
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
        ApiKeyStatus = null;
    }

    [RelayCommand]
    private async Task ValidateApiKey()
    {
        var key = string.IsNullOrWhiteSpace(ApiKeyInput)
            ? _parent.GetApiKey(Id)
            : ApiKeyInput.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            ApiKeyStatus = "Enter or save a key first.";
            return;
        }

        IsValidatingApiKey = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            if (Id == "steam")
            {
                var url = $"https://api.steampowered.com/ISteamWebAPIUtil/GetServerInfo/v1/?key={Uri.EscapeDataString(key)}";
                using var res = await http.GetAsync(url);
                ApiKeyStatus = res.IsSuccessStatusCode ? "Steam key looks valid." : "Steam key validation failed.";
            }
            else if (Id == "itchio")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.itch.io/profile/owned-keys?page=1");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var res = await http.SendAsync(req);
                ApiKeyStatus = res.IsSuccessStatusCode ? "itch.io key looks valid." : "itch.io key validation failed.";
            }
            else
            {
                ApiKeyStatus = "Validation is currently available for Steam and itch.io keys.";
            }
        }
        catch (Exception ex)
        {
            ApiKeyStatus = $"Validation failed: {ex.Message}";
        }
        finally
        {
            IsValidatingApiKey = false;
        }
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
