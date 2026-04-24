using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Providers;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views.Dialogs;

// ─── StartupWizardDialog code-behind ─────────────────────────────────────────
// Hosts the multi-step wizard. Persists the final selection via SettingsService
// + writes the SteamGridDB key through CredentialService. Each platform tile
// kicks off the in-process OAuth flow in the background.
public partial class StartupWizardDialog : Window
{
    private readonly StartupWizardViewModel _vm = new();
    private bool _hydrating;
    private readonly List<DiscoveredConsole> _discoveredConsoles = [];
    private readonly DispatcherTimer _wizardPersistDebounce;

    public StartupWizardDialog()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.Completed += OnCompleted;
        _vm.PropertyChanged += VmOnPropertyChanged;

        _wizardPersistDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _wizardPersistDebounce.Tick += (_, _) =>
        {
            _wizardPersistDebounce.Stop();
            try
            {
                PersistPartialWizardSettings();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[wizard] Partial settings persist failed");
            }
        };
        Closing += OnWizardClosing;

        // Seed defaults from existing settings so the wizard reflects current state
        // (important when re-run from Settings).
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        var (tier, recommendation) = DetectPerformanceTier();
        _hydrating = true;
        _vm.HardwareTier = tier;
        _vm.PerformanceRecommendation = recommendation;
        _vm.DefaultView = settings.DefaultView ?? "orbit";
        _vm.Theme = settings.Theme ?? "midnight";
        _vm.AccentColor = string.IsNullOrWhiteSpace(settings.AccentColor)
            ? StartupWizardViewModel.DefaultAccent
            : settings.AccentColor;
        _vm.StarDensity = settings.StarDensity ?? "normal";
        _vm.UiScale = settings.UiScale ?? "100%";
        _vm.ShowAnimations = settings.ShowAnimations;
        _vm.ToolbarPosition = settings.ToolbarPosition ?? "top";
        _vm.MinimizeOnLaunch = settings.MinimizeOnLaunch;
        _vm.CloseToTray = settings.CloseToTray;
        _vm.DiscordPresence = settings.DiscordPresence;
        _vm.AutoSyncPlaytime = settings.AutoSyncPlaytime;
        if (settings.FirstRun)
            _vm.ApplyRecommendedPerformanceCommand.Execute(null);
        _hydrating = false;
        App.Services.GetRequiredService<ThemeService>().Apply(_vm.Theme, _vm.AccentColor);

        // Update Chiaki status once loaded.
        Loaded += (_, _) =>
        {
            RefreshChiakiStatus();
            RefreshAccountTiles();
        };
    }

    // ─── Completion ──────────────────────────────────────────────────────────
    private void OnCompleted(object? sender, WizardResult result)
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var s = settings.Get();
        s.DefaultView = result.DefaultView;
        s.Theme = result.Theme;
        s.AccentColor = result.AccentColor;
        s.StarDensity = result.StarDensity;
        s.UiScale = result.UiScale;
        s.ShowAnimations = result.ShowAnimations;
        s.ToolbarPosition = result.ToolbarPosition;
        s.NavPosition = result.ToolbarPosition;
        s.MinimizeOnLaunch = result.MinimizeOnLaunch;
        s.CloseToTray = result.CloseToTray;
        s.DiscordPresence = result.DiscordPresence;
        s.AutoSyncPlaytime = result.AutoSyncPlaytime;
        s.FirstRun = false;
        settings.Save(s);
        App.Services.GetRequiredService<ThemeService>().Apply(result.Theme, result.AccentColor);

        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            && d.MainWindow?.DataContext is MainViewModel mvm)
            mvm.ViewMode = MainViewModel.NormalizeViewMode(result.DefaultView);

        if (!string.IsNullOrWhiteSpace(result.SteamGridDbKey))
        {
            var creds = App.Services.GetRequiredService<CredentialService>();
            creds.SetPassword("cereal", "steamgriddb_key", result.SteamGridDbKey);
        }

        Close(true);
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hydrating) return;
        if (e.PropertyName is nameof(StartupWizardViewModel.Theme) or nameof(StartupWizardViewModel.AccentColor))
        {
            App.Services.GetRequiredService<ThemeService>().Apply(_vm.Theme, _vm.AccentColor);
        }
        if (e.PropertyName == nameof(StartupWizardViewModel.Step) && _vm.Step == 5)
        {
            RefreshChiakiStatus();
            _ = DiscoverConsolesAsync();
        }
        if (e.PropertyName == nameof(StartupWizardViewModel.Step) && _vm.Step == 3)
            RefreshAccountTiles();
        SchedulePersistPartialWizardSettings();
    }

    private void SchedulePersistPartialWizardSettings()
    {
        _wizardPersistDebounce.Stop();
        _wizardPersistDebounce.Start();
    }

    private void OnWizardClosing(object? sender, WindowClosingEventArgs e)
    {
        _wizardPersistDebounce.Stop();
        if (_hydrating) return;
        try
        {
            PersistPartialWizardSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[wizard] Final persist on close failed");
        }
    }

    private void PersistPartialWizardSettings()
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var s = settings.Get();
        s.DefaultView = _vm.DefaultView;
        s.Theme = _vm.Theme;
        s.AccentColor = _vm.AccentColor;
        s.StarDensity = _vm.StarDensity;
        s.UiScale = _vm.UiScale;
        s.ShowAnimations = _vm.ShowAnimations;
        s.ToolbarPosition = _vm.ToolbarPosition;
        s.NavPosition = _vm.ToolbarPosition;
        s.MinimizeOnLaunch = _vm.MinimizeOnLaunch;
        s.CloseToTray = _vm.CloseToTray;
        s.DiscordPresence = _vm.DiscordPresence;
        s.AutoSyncPlaytime = _vm.AutoSyncPlaytime;
        settings.Save(s);
    }

    // ─── Default view cards ──────────────────────────────────────────────────
    private void SelectCards_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        => _vm.DefaultView = "cards";

    private void SelectOrbit_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        => _vm.DefaultView = "orbit";

    // ─── Performance chips ───────────────────────────────────────────────────
    private void Density_Low(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.StarDensity = "low";
    private void Density_Normal(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.StarDensity = "normal";
    private void Density_High(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.StarDensity = "high";

    private void Scale_90(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.UiScale = "90%";
    private void Scale_100(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.UiScale = "100%";
    private void Scale_110(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.UiScale = "110%";
    private void Scale_125(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.UiScale = "125%";

    private void Toolbar_Top(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.ToolbarPosition = "top";
    private void Toolbar_Bottom(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.ToolbarPosition = "bottom";
    private void Toolbar_Left(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.ToolbarPosition = "left";
    private void Toolbar_Right(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _vm.ToolbarPosition = "right";

    // ─── Accounts step — delegate to AuthService ─────────────────────────────
    private void AuthSteam_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _ = SignInAsync("steam");
    private void AuthGog_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _ = SignInAsync("gog");
    private void AuthEpic_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _ = SignInAsync("epic");
    private void AuthXbox_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => _ = SignInAsync("xbox");

    private async void RetrySteamApiKey_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var key = this.FindControl<TextBox>("SteamApiKeyBox")?.Text?.Trim();
        if (string.IsNullOrEmpty(key)) return;
        var creds = App.Services.GetRequiredService<CredentialService>();
        creds.SetPassword("cereal", "steam_api_key", key);
        this.FindControl<StackPanel>("SteamApiKeyPanel")!.IsVisible = false;
        var status = this.FindControl<TextBlock>("AuthStatus");
        await ImportAfterSignInAsync("steam", status);
        RefreshAccountTiles();
    }

    private async Task SignInAsync(string platform)
    {
        var auth = App.Services.GetRequiredService<AuthService>();
        var status = this.FindControl<TextBlock>("AuthStatus");
        try
        {
            if (status is not null) status.Text = $"Waiting for {platform} sign-in…";
            var url = platform switch
            {
                "steam" => auth.GetSteamAuthUrl(),
                "gog"   => auth.GetGogAuthUrl(),
                "epic"  => auth.GetEpicAuthUrl(),
                "xbox"  => auth.GetXboxAuthUrl(),
                _       => null,
            };
            if (url is null) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var account = await auth.WaitForCallbackAsync(platform, ct: cts.Token);
            if (status is not null)
                status.Text = $"{platform} connected as {account.Username ?? account.AccountId}";

            await ImportAfterSignInAsync(platform, status);
            RefreshAccountTiles();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[wizard] {Platform} sign-in failed", platform);
            if (status is not null) status.Text = $"{platform} sign-in failed: {ex.Message}";
        }
    }

    private async Task ImportAfterSignInAsync(string platform, TextBlock? status)
    {
        var provider = App.Services.GetRequiredService<IEnumerable<IImportProvider>>()
            .FirstOrDefault(p => p.PlatformId == platform);
        if (provider is null)
            return;

        try
        {
            if (status is not null)
                status.Text = $"{platform}: importing library…";

            var auth = App.Services.GetRequiredService<AuthService>();
            await auth.RefreshTokenIfNeededAsync(platform);
            var creds = App.Services.GetRequiredService<CredentialService>();
            var ctx = new ImportContext
            {
                Db = App.Services.GetRequiredService<DatabaseService>(),
                ApiKey = creds.GetPassword("cereal", $"{platform}_api_key"),
                Http = new HttpClient(),
                Notify = p =>
                {
                    if (status is null) return;
                    Dispatcher.UIThread.Post(() =>
                        status.Text = $"{platform}: {p.Processed}/{p.Total} {p.Name ?? ""}".Trim());
                },
            };
            var result = await provider.ImportLibrary(ctx);
            if (!string.IsNullOrWhiteSpace(result.Error) &&
                (result.Error.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                 result.Error.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                 result.Error.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                 result.Error.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)))
            {
                var refreshed = await auth.RefreshTokenIfNeededAsync(platform);
                if (refreshed)
                    result = await provider.ImportLibrary(ctx);
            }
            if (status is not null)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    var showKeyPanel = platform == "steam" && string.IsNullOrWhiteSpace(creds.GetPassword("cereal", "steam_api_key"));
                    status.Text = showKeyPanel
                        ? "Import failed. Private profile? Enter a Steam Web API key below and retry."
                        : $"{platform} import failed: {result.Error}";
                    if (showKeyPanel)
                        this.FindControl<StackPanel>("SteamApiKeyPanel")!.IsVisible = true;
                }
                else
                {
                    status.Text = $"{platform}: {result.Imported.Count} new, {result.Updated.Count} updated";
                }
            }

            // Reconcile local install state after cloud import for parity with source flow.
            if (platform is "steam" or "gog" or "epic")
            {
                var localProvider = App.Services.GetRequiredService<IEnumerable<IProvider>>()
                    .FirstOrDefault(p => p.PlatformId == platform);
                if (localProvider is not null)
                {
                    var detected = await localProvider.DetectInstalled();
                    var games = App.Services.GetRequiredService<GameService>();
                    foreach (var g in detected.Games.Where(g => g.Installed == true))
                        games.Add(g);
                }
            }

            var db = App.Services.GetRequiredService<DatabaseService>();
            var acct = db.Db.Accounts.GetValueOrDefault(platform);
            if (acct is not null)
            {
                acct.LastSyncMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                db.Save();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[wizard] {Platform} post-auth import failed", platform);
            if (status is not null)
                status.Text = $"{platform} connected, but import failed: {ex.Message}";
        }
    }

    // ─── PlayStation / Chiaki step ───────────────────────────────────────────
    private void RefreshChiakiStatus()
    {
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        var (status, _, version) = chiaki.GetStatus();
        var statusText = this.FindControl<TextBlock>("ChiakiStatus");
        var downloadBtn = this.FindControl<Button>("ChiakiDownloadBtn");
        if (statusText is null || downloadBtn is null) return;

        var vm = DataContext as StartupWizardViewModel;
        if (status == "missing")
        {
            statusText.Text = "Not installed";
            downloadBtn.IsVisible = true;
            if (vm is not null) vm.ChiakiReady = false;
        }
        else
        {
            statusText.Text = version is not null ? $"Installed (v{version})" : "Installed";
            downloadBtn.IsVisible = false;
            if (vm is not null) vm.ChiakiReady = true;
        }
    }

    private async void PasteSgdbKey_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var txt = (await App.ReadClipboardTextAsync()).Trim();
        if (!string.IsNullOrEmpty(txt))
            _vm.SteamGridDbKey = txt;
    }

    private void ChiakiDownload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        var statusText = this.FindControl<TextBlock>("ChiakiStatus");
        if (statusText is not null) statusText.Text = "Downloading chiaki-ng…";

        // AutoSetupIfMissing runs PowerShell script in a background task.
        chiaki.AutoSetupIfMissing();

        // Poll until installed or 5 minutes pass.
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(5000);
                var (status, _, _) = chiaki.GetStatus();
                if (status != "missing")
                {
                    await Dispatcher.UIThread.InvokeAsync(RefreshChiakiStatus);
                    return;
                }
            }
        });
    }

    private void OpenChiaki_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { App.Services.GetRequiredService<ChiakiService>().OpenGui(); }
        catch (Exception ex) { Log.Warning(ex, "[wizard] chiaki OpenGui failed"); }
    }

    private async void DiscoverConsoles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await DiscoverConsolesAsync();

    private async Task DiscoverConsolesAsync()
    {
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        SetChiakiWizardStatus("Discovering consoles on your network…");
        try
        {
            var (ok, consoles, err) = await chiaki.DiscoverConsolesAsync();
            if (!ok)
            {
                SetChiakiWizardStatus($"Discovery failed: {err}");
                return;
            }

            _discoveredConsoles.Clear();
            _discoveredConsoles.AddRange(consoles);

            var box = this.FindControl<ComboBox>("ConsoleHostBox");
            if (box is not null)
            {
                box.ItemsSource = _discoveredConsoles
                    .Select(c => $"{c.Host} {(string.IsNullOrWhiteSpace(c.Name) ? "" : $"({c.Name})")} [{c.State}]")
                    .ToList();
                if (_discoveredConsoles.Count > 0) box.SelectedIndex = 0;
            }

            SetChiakiWizardStatus(_discoveredConsoles.Count > 0
                ? $"Found {_discoveredConsoles.Count} console(s)."
                : "No consoles discovered. Use manual host IP.");
        }
        catch (Exception ex)
        {
            SetChiakiWizardStatus($"Discovery failed: {ex.Message}");
        }
    }

    private async void RegisterConsole_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var host = ResolveWizardHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            SetChiakiWizardStatus("Select a discovered console or enter a manual host IP.");
            return;
        }

        var psn = this.FindControl<TextBox>("PsnAccountIdBox")?.Text?.Trim();
        var pin = this.FindControl<TextBox>("PsnPinBox")?.Text?.Trim();
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        SetChiakiWizardStatus("Registering console…");
        try
        {
            var (ok, regist, morning, err) = await chiaki.RegisterConsoleAsync(host, psn, pin);
            if (!ok)
            {
                SetChiakiWizardStatus($"Registration failed: {err}");
                return;
            }
            SetChiakiWizardStatus($"Registration successful. Key: {(string.IsNullOrWhiteSpace(regist) ? "(not returned)" : regist)}");
        }
        catch (Exception ex)
        {
            SetChiakiWizardStatus($"Registration failed: {ex.Message}");
        }
    }

    private async void WakeConsole_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var host = ResolveWizardHost();
        if (string.IsNullOrWhiteSpace(host))
        {
            SetChiakiWizardStatus("Select a discovered console or enter a manual host IP.");
            return;
        }
        var pin = this.FindControl<TextBox>("PsnPinBox")?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pin))
        {
            SetChiakiWizardStatus("Enter registration key in PIN box to wake console.");
            return;
        }
        var chiaki = App.Services.GetRequiredService<ChiakiService>();
        var (ok, err, method) = await chiaki.WakeConsoleAsync(host, pin);
        SetChiakiWizardStatus(ok ? $"Wake signal sent via {method}." : $"Wake failed: {err}");
    }

    private string? ResolveWizardHost()
    {
        var manual = this.FindControl<TextBox>("ManualHostBox")?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(manual)) return manual;
        var idx = this.FindControl<ComboBox>("ConsoleHostBox")?.SelectedIndex ?? -1;
        if (idx >= 0 && idx < _discoveredConsoles.Count) return _discoveredConsoles[idx].Host;
        return null;
    }

    private void SetChiakiWizardStatus(string text)
    {
        var tb = this.FindControl<TextBlock>("ChiakiWizardStatus");
        if (tb is not null) tb.Text = text;
    }

    private void RefreshAccountTiles()
    {
        var db = App.Services.GetRequiredService<DatabaseService>();
        var games = db.Db.Games;
        RefreshAccountTile("steam", "SteamStatus", "SteamMeta", "SteamAvatarWrap", "SteamAvatar", "SteamAuthBtn", games);
        RefreshAccountTile("gog", "GogStatus", "GogMeta", "GogAvatarWrap", "GogAvatar", "GogAuthBtn", games);
        RefreshAccountTile("epic", "EpicStatus", "EpicMeta", "EpicAvatarWrap", "EpicAvatar", "EpicAuthBtn", games);
        RefreshAccountTile("xbox", "XboxStatus", "XboxMeta", "XboxAvatarWrap", "XboxAvatar", "XboxAuthBtn", games);
        UpdateAccountsSummary(db, games);
    }

    private void RefreshAccountTile(
        string platform,
        string statusName,
        string metaName,
        string avatarWrapName,
        string avatarName,
        string authButtonName,
        List<Cereal.App.Models.Game> games)
    {
        var db = App.Services.GetRequiredService<DatabaseService>();
        var account = db.Db.Accounts.GetValueOrDefault(platform);
        var status = this.FindControl<TextBlock>(statusName);
        var meta = this.FindControl<TextBlock>(metaName);
        var avatarWrap = this.FindControl<Border>(avatarWrapName);
        var avatar = this.FindControl<Image>(avatarName);
        var authButton = this.FindControl<Button>(authButtonName);
        if (status is null || meta is null || avatarWrap is null || avatar is null || authButton is null) return;

        if (account is null)
        {
            status.Text = "Not connected";
            meta.Text = "0 games imported";
            avatarWrap.IsVisible = false;
            avatar.Source = null;
            authButton.Content = "Sign in";
            authButton.IsEnabled = true;
            return;
        }

        status.Text = account.DisplayName ?? account.Username ?? account.AccountId ?? "Connected";
        var count = games.Count(g => string.Equals(g.Platform, platform, StringComparison.OrdinalIgnoreCase));
        var sync = account.LastSyncMs is long ts ? $" - synced {FormatAgo(ts)}" : "";
        meta.Text = $"{count} game{(count == 1 ? "" : "s")} imported{sync}";
        authButton.Content = "Connected";
        authButton.IsEnabled = false;

        if (!string.IsNullOrWhiteSpace(account.AvatarUrl) &&
            Uri.TryCreate(account.AvatarUrl, UriKind.Absolute, out var avatarUri))
        {
            avatarWrap.IsVisible = true;
            avatar.Source = new Bitmap(account.AvatarUrl);
        }
        else
        {
            avatarWrap.IsVisible = false;
            avatar.Source = null;
        }
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

    private void UpdateAccountsSummary(DatabaseService db, List<Cereal.App.Models.Game> games)
    {
        var platforms = new[] { "steam", "gog", "epic", "xbox" };
        var connected = platforms
            .Where(p => db.Db.Accounts.TryGetValue(p, out var acct) &&
                        !string.IsNullOrWhiteSpace(acct.AccountId ?? acct.Username ?? acct.DisplayName))
            .ToList();

        if (connected.Count == 0)
        {
            _vm.AccountsSummary = "No connected platforms yet";
            return;
        }

        var imported = games.Count(g => connected.Contains(g.Platform ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        _vm.AccountsSummary = $"{connected.Count} connected ({string.Join(", ", connected.Select(ToPlatformLabel))}) - {imported} imported games";
    }

    private static string ToPlatformLabel(string platform) => platform.ToLowerInvariant() switch
    {
        "gog" => "GOG",
        "epic" => "Epic Games",
        "steam" => "Steam",
        "xbox" => "Xbox",
        _ => platform.Length == 0 ? platform : char.ToUpperInvariant(platform[0]) + platform[1..],
    };

    private static (string Tier, string Recommendation) DetectPerformanceTier()
    {
        try
        {
            var cores = Environment.ProcessorCount;
            var memBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var memGb = memBytes > 0 ? memBytes / 1024d / 1024d / 1024d : 0;

            if (cores >= 12 && memGb >= 16)
                return ("High", "High-end system detected: high star density and animations recommended.");
            if (cores <= 4 || (memGb > 0 && memGb < 8))
                return ("Low", "Lower-spec system detected: low star density and reduced effects recommended.");
            return ("Balanced", "Balanced system detected: normal star density and standard effects recommended.");
        }
        catch
        {
            return ("Balanced", "Unable to detect hardware exactly; balanced defaults recommended.");
        }
    }
}
