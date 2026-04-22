using System.Diagnostics;
using System.Runtime.InteropServices;
using Cereal.App.Models;
using Cereal.App.Services.Integrations;
using Serilog;

namespace Cereal.App.Services;

public sealed class LaunchResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Opened { get; init; }
}

public sealed class LaunchService
{
    private readonly GameService _games;
    private readonly SettingsService _settings;
    private readonly DiscordService _discord;
    private readonly ChiakiService _chiaki;
    private readonly XcloudService _xcloud;

    // gameId → session start time (for playtime tracking)
    private readonly Dictionary<string, (DateTime Start, Process? Process)> _sessions = [];

    public LaunchService(GameService games, SettingsService settings,
                         DiscordService discord, ChiakiService chiaki, XcloudService xcloud)
    {
        _games = games;
        _settings = settings;
        _discord = discord;
        _chiaki = chiaki;
        _xcloud = xcloud;
    }

    // ─── Launch ───────────────────────────────────────────────────────────────

    public async Task<LaunchResult> LaunchAsync(Game game, CancellationToken ct = default)
    {
        var platform = NormalizePlatform(game.Platform);

        // PlayStation → delegate to ChiakiService
        if (platform == "psn")
        {
            var (success, error, state) = _chiaki.StartStream(game.Id);
            return new LaunchResult { Success = success, Error = error, Opened = state };
        }

        // Xbox Cloud → delegate to XcloudService (no-op until WebView added)
        if (platform == "xbox" && game.StreamUrl is not null)
        {
            _xcloud.StartSession(game.Id, game.StreamUrl, game.Name);
            return new LaunchResult { Success = true, Opened = game.StreamUrl };
        }

        // Custom executable
        if (platform == "custom" && !string.IsNullOrEmpty(game.ExecutablePath))
        {
            return await LaunchExeAsync(game, game.ExecutablePath!, ct);
        }

        // Platform URI / launcher
        return await OpenPlatformAsync(game, ct);
    }

    private async Task<LaunchResult> OpenPlatformAsync(Game game, CancellationToken ct)
    {
        var uris = BuildPlatformUris(game);
        foreach (var uri in uris.Where(u => !string.IsNullOrEmpty(u)))
        {
            try
            {
                var result = await TryOpenUriAsync(uri, ct);
                if (result.Success)
                {
                    RecordSessionStart(game);
                    return result;
                }
            }
            catch { /* try next */ }
        }

        // Fallback: launch the platform client exe directly
        foreach (var exe in GetLauncherCandidates(game.Platform).Where(File.Exists))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                RecordSessionStart(game);
                return new LaunchResult { Success = true, Opened = exe };
            }
            catch { /* try next */ }
        }

        return new LaunchResult { Success = false, Error = "Could not open game or platform client" };
    }

    private async Task<LaunchResult> LaunchExeAsync(Game game, string exe, CancellationToken ct)
    {
        if (!File.Exists(exe))
            return new LaunchResult { Success = false, Error = $"Executable not found: {exe}" };

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? ".",
        };
        var p = Process.Start(psi);
        if (p is null) return new LaunchResult { Success = false, Error = "Failed to start process" };

        RecordSessionStart(game, p);

        if (_settings.Get().MinimizeOnLaunch)
        {
            // Minimize will be handled by the MainWindow via a message/event — skip here
        }

        SetDiscordPresence(game);

        // Track playtime when process exits
        _ = Task.Run(async () =>
        {
            try { await p.WaitForExitAsync(ct); }
            catch { /* cancelled */ }
            RecordSessionEnd(game.Id);
        }, ct);

        return new LaunchResult { Success = true, Opened = exe };
    }

    // ─── Session tracking ─────────────────────────────────────────────────────

    private void RecordSessionStart(Game game, Process? process = null)
    {
        _sessions[game.Id] = (DateTime.UtcNow, process);
        game.LastPlayed = DateTime.UtcNow.ToString("O");
        _games.Update(game);
        SetDiscordPresence(game);
        Log.Information("[launch] {Name} ({Platform}) started", game.Name, game.Platform);
    }

    private void RecordSessionEnd(string gameId)
    {
        if (!_sessions.TryGetValue(gameId, out var session)) return;
        _sessions.Remove(gameId);

        var minutes = (int)(DateTime.UtcNow - session.Start).TotalMinutes;
        if (minutes > 0)
            _games.RecordPlaySession(gameId, minutes);

        _discord.ClearPresence();
        Log.Information("[launch] Session ended for {GameId} — {Minutes}m", gameId, minutes);
    }

    private void SetDiscordPresence(Game game)
    {
        if (_discord.IsConnected)
            _discord.SetPresence(game.Name, game.Platform);
    }

    // ─── URI building ─────────────────────────────────────────────────────────

    private static string NormalizePlatform(string platform) =>
        platform == "psremote" ? "psn" : platform;

    private static IEnumerable<string> BuildPlatformUris(Game game)
    {
        var platform = NormalizePlatform(game.Platform);
        var id = game.PlatformId ?? "";
        var store = game.StoreUrl ?? "";

        return platform switch
        {
            "steam"     => [$"steam://rungameid/{id}", $"steam://nav/games/details/{id}", store],
            "epic"      => [$"com.epicgames.launcher://apps/{game.EpicAppName ?? id}?action=launch&silent=true",
                            $"com.epicgames.launcher://apps/{id}?action=launch&silent=true", store],
            "gog"       => [$"goggalaxy://openGameView/{id}", store],
            "ea"        => [$"origin2://game/launch?offerIds={id}", "origin2://library/open", store],
            "battlenet" => string.IsNullOrEmpty(id) ? ["battlenet://"] : [$"battlenet://{id}"],
            "ubisoft"   => string.IsNullOrEmpty(id) ? ["uplay://"] : [$"uplay://launch/{id}/0"],
            "itchio"    => string.IsNullOrEmpty(store) ? ["https://itch.io/my-purchases"] : [store],
            "xbox"      => ["https://www.xbox.com/play"],
            _           => string.IsNullOrEmpty(store) ? [] : [store],
        };
    }

    private static IEnumerable<string> GetLauncherCandidates(string platform)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return [];

        var pf86  = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "";
        var pf    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return platform switch
        {
            "steam"     => [$@"{pf86}\Steam\Steam.exe",            $@"{pf}\Steam\Steam.exe"],
            "epic"      => [$@"{pf86}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
                            $@"{pf}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"],
            "gog"       => [$@"{pf86}\GOG Galaxy\GalaxyClient.exe",$@"{pf}\GOG Galaxy\GalaxyClient.exe"],
            "ea"        => [$@"{pf}\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe",
                            $@"{pf86}\Origin\Origin.exe"],
            "battlenet" => [$@"{pf}\Battle.net\Battle.net.exe",    $@"{pf86}\Battle.net\Battle.net.exe"],
            "ubisoft"   => [$@"{pf}\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe",
                            $@"{pf86}\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"],
            _           => [],
        };
    }

    private static async Task<LaunchResult> TryOpenUriAsync(string uri, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(uri) { UseShellExecute = true };
        var p = Process.Start(psi);
        if (p is not null)
        {
            try { await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch { /* expected — browser/launcher stays open */ }
        }
        return new LaunchResult { Success = true, Opened = uri };
    }
}
