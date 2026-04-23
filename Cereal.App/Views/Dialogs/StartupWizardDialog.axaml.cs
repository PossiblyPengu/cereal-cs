using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
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

    public StartupWizardDialog()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.Completed += OnCompleted;

        // Seed defaults from existing settings so the wizard reflects current state
        // (important when re-run from Settings).
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        _vm.DefaultView = settings.DefaultView ?? "cards";
        _vm.StarDensity = settings.StarDensity ?? "normal";
        _vm.UiScale = settings.UiScale ?? "100%";
        _vm.ShowAnimations = settings.ShowAnimations;
        _vm.ToolbarPosition = settings.ToolbarPosition ?? "top";
        _vm.MinimizeOnLaunch = settings.MinimizeOnLaunch;
        _vm.CloseToTray = settings.CloseToTray;
        _vm.DiscordPresence = settings.DiscordPresence;
        _vm.AutoSyncPlaytime = settings.AutoSyncPlaytime;

        // Update Chiaki status once loaded.
        Loaded += (_, _) => RefreshChiakiStatus();
    }

    // ─── Completion ──────────────────────────────────────────────────────────
    private void OnCompleted(object? sender, WizardResult result)
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var s = settings.Get();
        s.DefaultView = result.DefaultView;
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

        if (!string.IsNullOrWhiteSpace(result.SteamGridDbKey))
        {
            var creds = App.Services.GetRequiredService<CredentialService>();
            creds.SetPassword("cereal", "steamgriddb_key", result.SteamGridDbKey);
        }

        Close(true);
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
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[wizard] {Platform} sign-in failed", platform);
            if (status is not null) status.Text = $"{platform} sign-in failed: {ex.Message}";
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

        if (status == "missing")
        {
            statusText.Text = "Not installed";
            downloadBtn.IsVisible = true;
        }
        else
        {
            statusText.Text = version is not null ? $"Installed (v{version})" : "Installed";
            downloadBtn.IsVisible = false;
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
}
