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

    /// <summary>Raised when the app should be minimized (after a game launch with MinimizeOnLaunch enabled).</summary>
    public event EventHandler? MinimizeRequested;

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

        // Xbox Cloud → delegate to XcloudService
        if (platform == "xbox" && game.StreamUrl is not null)
        {
            _xcloud.StartSession(game.Id, game.StreamUrl, game.Name);
            MaybeMinimize();
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

    private void MaybeMinimize()
    {
        if (_settings.Get().MinimizeOnLaunch)
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Open the platform client focused on this title (no launch). Mirrors the
    /// Electron `games:openInClient` handler.
    /// </summary>
    public Task<LaunchResult> OpenInClientAsync(Game game, CancellationToken ct = default)
        => OpenPlatformAsync(game, ct, "client");

    /// <summary>
    /// Ask the platform client to install this title. Mirrors the Electron
    /// `games:install` handler (psn/custom are not supported).
    /// </summary>
    public Task<LaunchResult> InstallAsync(Game game, CancellationToken ct = default)
    {
        var platform = NormalizePlatform(game.Platform);
        if (platform == "psn")
            return Task.FromResult(new LaunchResult { Success = false, Error = "Install is not supported for Remote Play titles" });
        if (platform == "custom")
            return Task.FromResult(new LaunchResult { Success = false, Error = "Custom games must be installed manually" });
        return OpenPlatformAsync(game, ct, "install");
    }

    private async Task<LaunchResult> OpenPlatformAsync(Game game, CancellationToken ct, string action = "play")
    {
        var uris = BuildPlatformUris(game, action);
        foreach (var uri in uris.Where(u => !string.IsNullOrEmpty(u)))
        {
            try
            {
                var result = await TryOpenUriAsync(uri, ct);
                if (result.Success)
                {
                    // Mirror original launcher behavior: URI/client launches still
                    // mark last-played and set Discord presence.
                    RecordSessionStart(game, process: null, setDiscordPresence: true);
                    MaybeMinimize();
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[launch] Failed opening URI candidate: {Uri}", uri);
            }
        }

        // Fallback: launch the platform client exe directly
        foreach (var exe in GetLauncherCandidates(game.Platform).Where(File.Exists))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                RecordSessionStart(game, process: null, setDiscordPresence: true);
                MaybeMinimize();
                return new LaunchResult { Success = true, Opened = exe };
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[launch] Failed starting launcher candidate: {Exe}", exe);
            }
        }

        return new LaunchResult { Success = false, Error = "Could not open game or platform client" };
    }

    private Task<LaunchResult> LaunchExeAsync(Game game, string exe, CancellationToken ct)
    {
        if (!File.Exists(exe))
            return Task.FromResult(new LaunchResult { Success = false, Error = $"Executable not found: {exe}" });

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? ".",
        };
        var p = Process.Start(psi);
        if (p is null) return Task.FromResult(new LaunchResult { Success = false, Error = "Failed to start process" });

        RecordSessionStart(game, p, setDiscordPresence: true);
        MaybeMinimize();

        // Track playtime when process exits
        _ = Task.Run(async () =>
        {
            try { await p.WaitForExitAsync(ct); }
            catch (Exception ex) { Log.Debug(ex, "[launch] WaitForExitAsync canceled/failed for {Exe}", exe); }
            RecordSessionEnd(game.Id);
        }, ct);

        return Task.FromResult(new LaunchResult { Success = true, Opened = exe });
    }

    // ─── Session tracking ─────────────────────────────────────────────────────

    private void RecordSessionStart(Game game, Process? process, bool setDiscordPresence)
    {
        if (process is not null)
            _sessions[game.Id] = (DateTime.UtcNow, process);
        // Launchers/URIs: last-played is still useful; playtime is only finalized when a game process exits.
        game.LastPlayed = DateTime.UtcNow.ToString("O");
        _games.Update(game);
        if (setDiscordPresence) SetDiscordPresence(game);
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

    // URIs differ per action: "play" (default), "install" or "client"; mirrors
    // electron/modules/games/launcher.js `buildPlatformUris`.
    private static IEnumerable<string> BuildPlatformUris(Game game, string action = "play")
    {
        var platform = NormalizePlatform(game.Platform);
        var id = game.PlatformId ?? "";
        var store = game.StoreUrl ?? "";
        var steamIdFromUrl = TryExtractStoreId(store, @"/app/(\d+)");
        var steamId = !string.IsNullOrEmpty(id) ? id : steamIdFromUrl;
        var epicNamespace = game.EpicNamespace ?? "";
        var epicCatalogItemId = game.EpicCatalogItemId ?? "";
        var eaOfferId = game.EaOfferId ?? id;
        var ubiGameId = game.UbisoftGameId ?? id;

        switch (platform)
        {
            case "steam":
                if (action == "install" && !string.IsNullOrEmpty(steamId))
                    return [$"steam://install/{steamId}", $"steam://nav/games/details/{steamId}", store];
                if (action == "client")
                    return ["steam://open/games", "steam://nav/library"];
                return [$"steam://rungameid/{steamId}", $"steam://nav/games/details/{steamId}", store];

            case "epic":
                var appName = game.EpicAppName ?? id;
                var epicNsCat = !string.IsNullOrEmpty(epicNamespace) && !string.IsNullOrEmpty(epicCatalogItemId)
                    ? $"com.epicgames.launcher://apps/{epicNamespace}%3A{epicCatalogItemId}?action=launch&silent=true"
                    : "";
                if (action == "install")
                    return [
                        !string.IsNullOrEmpty(appName) ? $"com.epicgames.launcher://apps/{appName}?action=install&silent=true" : "",
                        !string.IsNullOrEmpty(id) ? $"com.epicgames.launcher://apps/{id}?action=install&silent=true" : "",
                        store,
                    ];
                if (action == "client")
                    return [
                        !string.IsNullOrEmpty(appName) ? $"com.epicgames.launcher://apps/{appName}" : "",
                        !string.IsNullOrEmpty(id) ? $"com.epicgames.launcher://apps/{id}" : "",
                        store,
                    ];
                return [
                    !string.IsNullOrEmpty(appName) ? $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true" : "",
                    !string.IsNullOrEmpty(id) ? $"com.epicgames.launcher://apps/{id}?action=launch&silent=true" : "",
                    epicNsCat,
                    store,
                ];

            case "gog":
                if (action == "install") return [store, $"goggalaxy://openGameView/{id}"];
                return [$"goggalaxy://openGameView/{id}", store];

            case "ea":
                if (!string.IsNullOrEmpty(eaOfferId))
                {
                    if (action == "install")
                        return [$"origin2://store/open?offerId={eaOfferId}", $"origin2://store/open?offerIds={eaOfferId}", store];
                    return [$"origin2://game/launch?offerIds={eaOfferId}", "origin2://library/open", store];
                }
                return ["origin2://library/open"];

            case "battlenet":
                return string.IsNullOrEmpty(id) ? ["battlenet://"] : [$"battlenet://{id}"];

            case "ubisoft":
                if (!string.IsNullOrEmpty(ubiGameId))
                {
                    if (action == "install") return [$"uplay://launch/{ubiGameId}/1", store];
                    return [$"uplay://launch/{ubiGameId}/0", store];
                }
                return ["uplay://"];

            case "itchio":
                return string.IsNullOrEmpty(store) ? ["https://itch.io/my-purchases"] : [store];

            case "xbox":
                if (action == "install") return ["msxbox://", "xbox://", "https://www.xbox.com/en-US/games"];
                if (action == "client")  return ["msxbox://"];
                return ["https://www.xbox.com/play"];

            default:
                return string.IsNullOrEmpty(store) ? [] : [store];
        }
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
                            $@"{pf86}\Origin\Origin.exe",
                            $@"{local}\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe"],
            "battlenet" => [$@"{pf}\Battle.net\Battle.net.exe",    $@"{pf86}\Battle.net\Battle.net.exe"],
            "ubisoft"   => [$@"{pf}\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe",
                            $@"{pf86}\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"],
            "itchio"    => [$@"{local}\itch\app-25.6.0\itch.exe", $@"{local}\itch\app-25.5.0\itch.exe", $@"{local}\itch\app-25.4.0\itch.exe"],
            _           => [],
        };
    }

    private static string TryExtractStoreId(string? storeUrl, string pattern)
    {
        if (string.IsNullOrWhiteSpace(storeUrl)) return "";
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(storeUrl, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success && m.Groups.Count > 1 ? m.Groups[1].Value : "";
        }
        catch
        {
            Log.Debug("[launch] Failed extracting store id from URL");
            return "";
        }
    }

    private static async Task<LaunchResult> TryOpenUriAsync(string uri, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(uri) { UseShellExecute = true };
        var p = Process.Start(psi);
        if (p is not null)
        {
            try { await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch (Exception ex) { Log.Debug(ex, "[launch] URI process wait timed out/failed for {Uri}", uri); }
        }
        return new LaunchResult { Success = true, Opened = uri };
    }
}
