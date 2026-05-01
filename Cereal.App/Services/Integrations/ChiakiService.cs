using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Cereal.App.Models;
using Serilog;

namespace Cereal.App.Services.Integrations;

// ─── Data types ───────────────────────────────────────────────────────────────

public sealed class ChiakiSessionInfo
{
    public string GameId { get; init; } = "";
    public string State { get; internal set; } = "launching";
    public long StartTimeMs { get; init; }
    public Dictionary<string, object?> StreamInfo { get; internal set; } = [];
    public Dictionary<string, object?> Quality { get; internal set; } = [];
    public int? ExitCode { get; internal set; }
    public int ReconnectAttempts { get; internal set; }
    public bool IsEmbedded { get; internal set; }
}

public sealed class DiscoveredConsole
{
    public string Host { get; set; } = "";
    public string State { get; set; } = "";  // "ready" or "standby"
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? HostId { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? RunningTitleId { get; set; }
    public string? RunningTitle { get; set; }
}

public sealed class ChiakiEventArgs : EventArgs
{
    public string GameId { get; init; } = "";
    public string Type { get; init; } = "";
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
}

// ─── Internal session state ────────────────────────────────────────────────────

internal sealed class ChiakiSession
{
    public string GameId { get; set; } = "";
    public string State { get; set; } = "launching";
    public long StartTimeMs { get; set; }
    public Dictionary<string, object?> StreamInfo { get; set; } = [];
    public Dictionary<string, object?> Quality { get; set; } = [];
    public int? ExitCode { get; set; }
    public int ReconnectAttempts { get; set; }
    public bool IsEmbedded { get; set; }

    public Process? Process { get; set; }
    public string? CurrentGameId { get; set; }
    public long TitleStartTimeMs { get; set; }
    public string? CurrentTitleId { get; set; }
    public CancellationTokenSource? ReconnectCts { get; set; }

    // Win32 embedding (Windows only)
    public nint EmbedWindowHandle { get; set; }
}

// ─── Service ──────────────────────────────────────────────────────────────────

public sealed class ChiakiService : IDisposable
{
    private readonly PathService _paths;
    private readonly DatabaseService _db;
    private readonly ConcurrentDictionary<string, ChiakiSession> _sessions = new();

    private static IEnumerable<string> GetSystemCandidates()
    {
        var names = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "chiaki-ng.exe", "chiaki.exe" }
            : new[] { "chiaki-ng", "chiaki" };

        // Search every directory on PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in names)
                yield return Path.Combine(dir, name);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Per-user install (LocalApplicationData is always dynamic)
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "chiaki-ng", "chiaki-ng.exe");
            // Per-machine install via ProgramFiles env var (respects 32/64-bit and custom installs)
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf))
                yield return Path.Combine(pf, "chiaki-ng", "chiaki-ng.exe");
        }
    }


    public event EventHandler<ChiakiEventArgs>? SessionEvent;
    public event EventHandler? GamesRefreshed;

    public ChiakiService(PathService paths, DatabaseService db)
    {
        _paths = paths;
        _db = db;
    }

    // ─── Executable resolution ───────────────────────────────────────────────

    public string? GetChiakiDir()
    {
        var bundled = Path.Combine(_paths.AppDataDir, "chiaki-ng");
        if (Directory.Exists(bundled)) return bundled;
        var devRes = _paths.GetResourcePath("chiaki-ng");
        if (Directory.Exists(devRes)) return devRes;
        return null;
    }

    public string? GetBundledExe()
    {
        var dir = GetChiakiDir();
        if (dir is null) return null;

        foreach (var name in new[] { "chiaki.exe", "chiaki-ng.exe", "chiaki", "chiaki-ng" })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        // One subdirectory deep
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            foreach (var name in new[] { "chiaki.exe", "chiaki-ng.exe", "chiaki", "chiaki-ng" })
            {
                var p = Path.Combine(sub, name);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    public string? GetBundledVersion()
    {
        var dir = GetChiakiDir();
        if (dir is null) return null;
        var vf = Path.Combine(dir, ".version");
        try { return File.Exists(vf) ? File.ReadAllText(vf).Trim() : null; }
        catch (Exception ex)
        {
            Log.Debug(ex, "[chiaki] Failed reading bundled version file");
            return null;
        }
    }

    private string? GetUserConfiguredExe()
    {
        var c1 = _db.Db.Settings.ChiakiPath;
        if (!string.IsNullOrEmpty(c1) && File.Exists(c1)) return c1;
        var c2 = _db.Db.ChiakiConfig.ExecutablePath;
        if (!string.IsNullOrEmpty(c2) && File.Exists(c2)) return c2;
        return null;
    }

    public string? ResolveExe(string? fallback = null)
    {
        var bundled = GetBundledExe();
        if (bundled is not null) return bundled;

        foreach (var p in GetSystemCandidates())
            if (File.Exists(p)) return p;

        var configured = GetUserConfiguredExe();
        if (configured is not null) return configured;

        if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback)) return fallback;

        return null;
    }

    public (string Status, string? ExePath, string? Version) GetStatus()
    {
        var exe = GetBundledExe();
        if (exe is not null) return ("bundled", exe, GetBundledVersion());
        foreach (var p in GetSystemCandidates())
            if (File.Exists(p)) return ("system", p, null);
        var configured = GetUserConfiguredExe();
        if (configured is not null) return ("system", configured, null);
        return ("missing", null, null);
    }

    // ─── Install management ──────────────────────────────────────────────────

    /// <summary>
    /// Removes the bundled chiaki-ng install and/or clears the user-configured path.
    /// Returns: "bundled" if the bundled dir was deleted, "config" if only the config path was cleared,
    /// "system" if chiaki is on PATH (can't be removed), or "none" if nothing was found.
    /// </summary>
    public string UninstallFull()
    {
        // Stop any running sessions first.
        try { foreach (var id in _sessions.Keys.ToList()) StopSession(id); }
        catch (Exception ex) { Log.Debug(ex, "[chiaki] Failed stopping sessions during uninstall"); }

        var removedBundled = false;
        var dir = Path.Combine(_paths.AppDataDir, "chiaki-ng");
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); removedBundled = true; }
            catch (Exception ex) { Log.Warning(ex, "[chiaki] Failed to delete {Dir}", dir); }
        }

        // Clear user-configured path from both possible locations.
        var clearedConfig = false;
        if (!string.IsNullOrEmpty(_db.Db.Settings.ChiakiPath))
        {
            _db.Db.Settings.ChiakiPath = null;
            clearedConfig = true;
        }
        if (!string.IsNullOrEmpty(_db.Db.ChiakiConfig.ExecutablePath))
        {
            _db.Db.ChiakiConfig.ExecutablePath = null;
            clearedConfig = true;
        }
        if (clearedConfig) _db.Save();

        if (removedBundled) return "bundled";
        if (clearedConfig)  return "config";

        // Still on system PATH — we can't uninstall it.
        if (GetSystemCandidates().Any(File.Exists)) return "system";

        return "none";
    }

    // Keep the old bool overload so nothing else breaks.
    public bool Uninstall() => UninstallFull() is "bundled" or "config";

    public async Task<(bool Updated, string? Version, string? Error)> CheckAndUpdateAsync()
    {
        // Reuses the auto-setup script. Treats script success as "up to date" or "updated".
        var scriptPath = _paths.GetResourcePath("scripts/setup-chiaki.ps1");
        if (!File.Exists(scriptPath))
            return (false, GetBundledVersion(), "setup-chiaki.ps1 missing");

        var installDir = Path.Combine(_paths.AppDataDir, "chiaki-ng");
        var psi = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");            psi.ArgumentList.Add(scriptPath);
        // Match Electron chiaki:update — without -Force the script exits early when chiaki-ng
        // is already present, so GitHub updates would never apply.
        psi.ArgumentList.Add("-Force");
        psi.ArgumentList.Add("-InstallDir");      psi.ArgumentList.Add(installDir);

        try
        {
            using var p = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await p.WaitForExitAsync(cts.Token);
            if (p.ExitCode != 0)
                return (false, GetBundledVersion(), $"Exit {p.ExitCode}");
            return (true, GetBundledVersion(), null);
        }
        catch (Exception ex)
        {
            return (false, GetBundledVersion(), ex.Message);
        }
    }

    // ─── CLI args ────────────────────────────────────────────────────────────

    private string[] BuildArgs(Game game, ChiakiConfig config)
    {
        var host = game.ChiakiHost ?? "";
        if (string.IsNullOrEmpty(host)) return [];

        var args = new List<string> { "stream" };
        args.Add(game.ChiakiNickname ?? "default");
        args.Add(host);

        if (!string.IsNullOrEmpty(game.ChiakiRegistKey)) { args.Add("--registkey"); args.Add(game.ChiakiRegistKey); }
        if (!string.IsNullOrEmpty(game.ChiakiMorning))   { args.Add("--morning");   args.Add(game.ChiakiMorning);   }
        if (!string.IsNullOrEmpty(game.ChiakiProfile))   { args.Add("--profile");   args.Add(game.ChiakiProfile);   }

        args.Add("--exit-app-on-stream-exit");

        var displayMode = game.ChiakiDisplayMode ?? config.DisplayMode ?? "fullscreen";
        if (displayMode == "zoom")         args.Add("--zoom");
        else if (displayMode == "stretch") args.Add("--stretch");
        else                               args.Add("--fullscreen");

        if (game.ChiakiDualsense == true || config.Dualsense == true) args.Add("--dualsense");
        if (!string.IsNullOrEmpty(game.ChiakiPasscode)) { args.Add("--passcode"); args.Add(game.ChiakiPasscode!); }

        return [.. args];
    }

    // ─── Session management ──────────────────────────────────────────────────

    public (bool Success, string? Error, string? State) StartStream(string gameId)
    {
        var game = _db.Db.Games.Find(g => g.Id == gameId);
        if (game is null) return (false, "Game not found", null);

        var exe = ResolveExe(game.ExecutablePath);
        if (exe is null) return (false, "chiaki-ng not found", null);

        var args = BuildArgs(game, _db.Db.ChiakiConfig);
        var session = StartSession(gameId, exe, args);

        game.LastPlayed = DateTime.UtcNow.ToString("O");
        _db.Save();

        return (true, null, session.State);
    }

    public (bool Success, string? Error, string? State) StartStreamDirect(
        string host, string? nickname = null, string? profile = null,
        string? registKey = null, string? morning = null, string? displayMode = null)
    {
        var exe = ResolveExe();
        if (exe is null) return (false, "chiaki-ng not found", null);

        var sessionKey = "console:" + host;
        var fake = new Game
        {
            ChiakiHost = host, ChiakiNickname = nickname,
            ChiakiProfile = profile, ChiakiRegistKey = registKey,
            ChiakiMorning = morning, ChiakiDisplayMode = displayMode,
        };
        var args = BuildArgs(fake, _db.Db.ChiakiConfig);
        var session = StartSession(sessionKey, exe, args);
        return (true, null, session.State);
    }

    public bool StopStream(string gameId)
    {
        return StopSession(gameId);
    }

    public void OpenGui()
    {
        var exe = ResolveExe();
        if (exe is null) return;
        var dir = Path.GetDirectoryName(exe) ?? ".";
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
        };
        var p = Process.Start(psi);
        p?.Dispose();
    }

    public ChiakiConfig GetConfig() => _db.Db.ChiakiConfig;

    public void SaveConfig(ChiakiConfig config)
    {
        _db.Db.ChiakiConfig = config;
        _db.Save();
    }

    public IReadOnlyDictionary<string, ChiakiSessionInfo> GetSessions() =>
        _sessions.ToDictionary(kv => kv.Key, kv => new ChiakiSessionInfo
        {
            GameId = kv.Value.GameId,
            State = kv.Value.State,
            StartTimeMs = kv.Value.StartTimeMs,
            StreamInfo = kv.Value.StreamInfo,
            Quality = kv.Value.Quality,
            ExitCode = kv.Value.ExitCode,
            ReconnectAttempts = kv.Value.ReconnectAttempts,
            IsEmbedded = kv.Value.IsEmbedded,
        });

    private ChiakiSession StartSession(string gameId, string exe, string[] args)
    {
        StopSession(gameId);

        var session = new ChiakiSession
        {
            GameId = gameId,
            State = "launching",
            StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CurrentGameId = gameId,
            TitleStartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var chiakiDir = Path.GetDirectoryName(exe) ?? ".";

        if (args.Length == 0)
        {
            // GUI mode — detached, no event tracking
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = chiakiDir,
                UseShellExecute = false,
            };
            session.Process = Process.Start(psi);
            session.State = "gui";
            _sessions[gameId] = session;
            RaiseEvent(gameId, "state", new() { ["state"] = "gui" });
            return session;
        }

        var psiManaged = new ProcessStartInfo(exe)
        {
            WorkingDirectory = chiakiDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // PATH prepend so chiaki can find its DLLs
        var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        psiManaged.Environment["PATH"] = chiakiDir + Path.PathSeparator + existingPath;
        foreach (var a in args) psiManaged.ArgumentList.Add(a);

        session.Process = Process.Start(psiManaged)!;
        _sessions[gameId] = session;

        var stderrBuf = new StringBuilder();

        void ProcessLine(string line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) return;
            if (trimmed.StartsWith('{'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    HandleJsonEvent(gameId, doc.RootElement);
                    return;
                }
                catch (Exception ex) { Log.Debug(ex, "[chiaki] Non-JSON process line ignored"); }
            }
            HandleLogLine(gameId, trimmed);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (await session.Process.StandardOutput.ReadLineAsync() is { } line)
                    ProcessLine(line);
            }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] stdout read loop ended"); }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await session.Process.StandardError.ReadLineAsync() is { } line)
                {
                    stderrBuf.AppendLine(line);
                    if (stderrBuf.Length > 4096)
                    {
                        var s = stderrBuf.ToString();
                        stderrBuf.Clear();
                        stderrBuf.Append(s[^4096..]);
                    }
                    ProcessLine(line);
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] stderr read loop ended"); }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await session.Process.WaitForExitAsync();
            }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] process wait loop ended"); }

            session.ExitCode = session.Process.ExitCode;
            session.State = "disconnected";

            StopEmbedding(session);

            var code = session.ExitCode;
            var signal = session.Process.ExitCode < 0;
            string reason;
            bool wasError;
            if (code == 0)        { reason = "clean_exit"; wasError = false; }
            else if (signal)      { reason = "killed";     wasError = false; }
            else                  { reason = "error";      wasError = true; }

            var elapsed = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - session.StartTimeMs) / 60000;

            RaiseEvent(gameId, "disconnected", new()
            {
                ["reason"]         = reason,
                ["wasError"]       = wasError,
                ["exitCode"]       = code,
                ["sessionMinutes"] = elapsed,
                ["stderr"]         = wasError ? stderrBuf.ToString()[^Math.Min(1024, stderrBuf.Length)..] : "",
            });

            // Attribute playtime to the current title
            var trackId = session.CurrentGameId ?? gameId;
            var titleElapsed = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - session.TitleStartTimeMs) / 60000;
            if (titleElapsed > 0)
            {
                var trackedGame = _db.Db.Games.Find(g => g.Id == trackId);
                if (trackedGame is not null)
                {
                    trackedGame.PlaytimeMinutes = (trackedGame.PlaytimeMinutes ?? 0) + (int)titleElapsed;
                    trackedGame.LastPlayed = DateTime.UtcNow.ToString("O");
                    _db.Save();
                    GamesRefreshed?.Invoke(this, EventArgs.Empty);
                }
            }

            // Auto-reconnect on non-auth error, up to 5 attempts
            var stderr = stderrBuf.ToString().ToLowerInvariant();
            var isAuthError = stderr.Contains("regist failed") || stderr.Contains("auth") || stderr.Contains("invalid psn");
            var reconnectAttempts = session.ReconnectAttempts;
            if (code != 0 && !isAuthError && reconnectAttempts < 5)
            {
                var next = reconnectAttempts + 1;
                var delayMs = (int)Math.Min(1000 * Math.Pow(2, next - 1), 16000);
                RaiseEvent(gameId, "reconnecting", new() { ["attempt"] = next, ["maxAttempts"] = 5, ["delayMs"] = delayMs });
                session.ReconnectCts?.Cancel();
                session.ReconnectCts?.Dispose();
                session.ReconnectCts = new CancellationTokenSource();
                var cts = session.ReconnectCts;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs, cts.Token);
                        if (_sessions.ContainsKey(gameId))
                        {
                            var newSession = StartSession(gameId, exe, args);
                            newSession.ReconnectAttempts = next;
                        }
                    }
                    catch (OperationCanceledException) { /* stopped */ }
                });
            }
            else
            {
                _sessions.TryRemove(gameId, out _);
            }
        });

        RaiseEvent(gameId, "state", new() { ["state"] = "launching" });

        // Start Win32 embedding on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _ = Task.Run(() => StartEmbedding(gameId, session));

        return session;
    }

    private bool StopSession(string gameId)
    {
        if (!_sessions.TryRemove(gameId, out var session)) return false;

        session.ReconnectCts?.Cancel();
        session.ReconnectCts?.Dispose();
        session.ReconnectCts = null;
        StopEmbedding(session);

        if (session.Process is { HasExited: false })
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo("taskkill", $"/pid {session.Process.Id} /t /f") { UseShellExecute = false });
                else
                    session.Process.Kill(entireProcessTree: true);

                // Force-kill fallback
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    try { if (!session.Process.HasExited) session.Process.Kill(); }
                    catch (Exception ex) { Log.Debug(ex, "[chiaki] force-kill fallback found dead process"); }
                });
            }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] stop-session process already dead"); }
        }
        return true;
    }

    // ─── Event parsing ───────────────────────────────────────────────────────

    private void HandleJsonEvent(string gameId, JsonElement evt)
    {
        if (!_sessions.TryGetValue(gameId, out var session)) return;

        var eventName = evt.TryGetProperty("event", out var ep) ? ep.GetString() : null;
        switch (eventName)
        {
            case "connecting":
                session.State = "connecting";
                RaiseEvent(gameId, "state", new()
                {
                    ["state"]   = "connecting",
                    ["host"]    = evt.TryGetProperty("host",    out var h) ? h.GetString() : null,
                    ["console"] = evt.TryGetProperty("console", out var c) ? c.GetString() : null,
                });
                break;
            case "streaming":
                session.State = "streaming";
                session.StreamInfo = new()
                {
                    ["resolution"] = evt.TryGetProperty("resolution", out var res) ? res.GetString() : null,
                    ["codec"]      = evt.TryGetProperty("codec",      out var cod) ? cod.GetString() : null,
                    ["fps"]        = evt.TryGetProperty("fps",        out var fps) ? fps.GetDouble() : null,
                };
                RaiseEvent(gameId, "state", new() { ["state"] = "streaming", ["StreamInfo"] = session.StreamInfo });
                break;
            case "quality":
                session.Quality = new()
                {
                    ["bitrate"]    = evt.TryGetProperty("bitrate_mbps",  out var bps) ? bps.GetDouble() : null,
                    ["packetLoss"] = evt.TryGetProperty("packet_loss",   out var pl)  ? pl.GetDouble()  : null,
                    ["fpsActual"]  = evt.TryGetProperty("fps_actual",    out var fa)  ? fa.GetDouble()  : null,
                    ["latencyMs"]  = evt.TryGetProperty("latency_ms",    out var lat) ? lat.GetDouble() : null,
                };
                RaiseEvent(gameId, "quality", session.Quality);
                break;
            case "title_change":
                HandleTitleChange(gameId, evt);
                break;
            case "disconnected":
                session.State = "disconnected";
                RaiseEvent(gameId, "chiaki_disconnect", new()
                {
                    ["reason"]   = evt.TryGetProperty("reason",    out var r) ? r.GetString() : null,
                    ["wasError"] = evt.TryGetProperty("was_error", out var we) && we.GetBoolean(),
                });
                break;
            default:
                // pass unknown events through
                break;
        }
    }

    private void HandleTitleChange(string originalGameId, JsonElement evt)
    {
        if (!_sessions.TryGetValue(originalGameId, out var session)) return;

        var titleId   = evt.TryGetProperty("title_id",   out var ti) ? ti.GetString()?.Trim() ?? "" : "";
        var titleName = evt.TryGetProperty("title_name", out var tn) ? tn.GetString()?.Trim() ?? "" : "";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (session.CurrentTitleId == titleId) return;

        // Attribute elapsed time to previous game
        session.CurrentTitleId = titleId;
        session.TitleStartTimeMs = now;

        if (string.IsNullOrEmpty(titleId))
        {
            session.CurrentGameId = null;
            RaiseEvent(originalGameId, "title_change", new() { ["titleId"] = "", ["titleName"] = "", ["gameId"] = null });
            return;
        }

        // PlayStation is streaming-only: we do not create/persist tracked game rows.
        session.CurrentGameId = null;

        RaiseEvent(originalGameId, "title_change", new()
        {
            ["titleId"]   = titleId,
            ["titleName"] = titleName,
            ["gameId"]    = null,
            ["gameName"]  = titleName,
        });
    }

    private void HandleLogLine(string gameId, string line)
    {
        if (!_sessions.TryGetValue(gameId, out var session)) return;
        var lower = line.ToLowerInvariant();

        if (lower.Contains("starting session request") || lower.Contains("starting ctrl"))
        {
            if (session.State != "streaming")
            {
                session.State = "connecting";
                RaiseEvent(gameId, "state", new() { ["state"] = "connecting" });
            }
        }
        else if (lower.Contains("senkusha completed successfully")
              || lower.Contains("streamconnection completed")
              || lower.Contains("stream connection started")
              || lower.Contains("video decoder"))
        {
            if (session.State != "streaming")
            {
                session.State = "streaming";
                session.ReconnectAttempts = 0;
                RaiseEvent(gameId, "state", new() { ["state"] = "streaming" });
            }
        }
        else if (lower.Contains("ctrl has failed")
              || lower.Contains("streamconnection run failed")
              || lower.Contains("remote disconnected"))
        {
            RaiseEvent(gameId, "log", new() { ["level"] = "error", ["message"] = line });
        }
    }

    // ─── Win32 embedding ─────────────────────────────────────────────────────

    private async Task StartEmbedding(string gameId, ChiakiSession session)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (session.Process is null) return;

        // Poll until Chiaki creates its window or the process exits (10s timeout).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        nint chiakiHwnd = nint.Zero;
        while (chiakiHwnd == nint.Zero && !session.Process.HasExited)
        {
            try { await Task.Delay(150, cts.Token); } catch (OperationCanceledException) { break; }
            chiakiHwnd = Win32Interop.FindProcessMainWindow(session.Process.Id);
        }

        if (chiakiHwnd == nint.Zero)
        {
            Log.Warning("[chiaki] Could not find chiaki window for embedding");
            RaiseEvent(gameId, "embedded", new() { ["embedded"] = false, ["error"] = "Window not found" });
            return;
        }

        session.EmbedWindowHandle = chiakiHwnd;
        Win32Interop.EmbedWindow(chiakiHwnd, session.Process.Id);
        session.IsEmbedded = true;
        RaiseEvent(gameId, "embedded", new() { ["embedded"] = true });
    }

    private static void StopEmbedding(ChiakiSession session)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (session.EmbedWindowHandle != nint.Zero)
        {
            Win32Interop.UnembedWindow(session.EmbedWindowHandle);
            session.EmbedWindowHandle = nint.Zero;
            session.IsEmbedded = false;
        }
    }

    // ─── Console discovery (UDP) ─────────────────────────────────────────────

    public async Task<(bool Success, List<DiscoveredConsole> Consoles, string? Error)> DiscoverConsolesAsync(
        CancellationToken ct = default)
    {
        // Matches chiaki-ng discovery.c: LF (not CRLF), PS4 port 987, PS5 port 9302
        var targets = new[]
        {
            (Port: 987,  Srch: "SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00020020\n"u8.ToArray()),
            (Port: 9302, Srch: "SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00030010\n"u8.ToArray()),
        };

        var found = new Dictionary<string, DiscoveredConsole>();

        using var udp = new UdpClient();
        try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); }
        catch (Exception ex) { return (false, [], ex.Message); }

        udp.Client.EnableBroadcast = true;

        // Receive loop
        var receiveTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(ct);
                    ParseDiscoveryResponse(result.Buffer, result.RemoteEndPoint.Address.ToString(), found);
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] discovery receive loop ended"); }
        }, ct);

        // Compute broadcast addresses
        var broadcasts = new HashSet<string> { "255.255.255.255" };
        foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var addr in iface.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = addr.Address.GetAddressBytes();
                var mask = addr.IPv4Mask.GetAddressBytes();
                var bcast = new byte[4];
                for (var i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | ~mask[i]);
                broadcasts.Add(new IPAddress(bcast).ToString());
            }
        }

        async Task SendRound()
        {
            foreach (var bcast in broadcasts)
            {
                foreach (var (port, srch) in targets)
                {
                    try
                    {
                        var ep = new IPEndPoint(IPAddress.Parse(bcast), port);
                        await udp.SendAsync(srch, srch.Length, ep);
                    }
                    catch (Exception ex) { Log.Debug(ex, "[chiaki] discovery send failed to {Broadcast}:{Port}", bcast, port); }
                }
            }
        }

        await SendRound();
        await Task.Delay(500, ct);
        await SendRound();
        await Task.Delay(1000, ct);
        await SendRound();
        await Task.Delay(2500, ct);

        udp.Close();
        try { await receiveTask; } catch (Exception ex) { Log.Debug(ex, "[chiaki] discovery receive task completion"); }

        return (true, [.. found.Values], null);
    }

    private static void ParseDiscoveryResponse(byte[] data, string host, Dictionary<string, DiscoveredConsole> found)
    {
        var text = Encoding.UTF8.GetString(data);
        var statusMatch = System.Text.RegularExpressions.Regex.Match(text, @"^HTTP/1\.1\s+(\d+)");
        if (!statusMatch.Success) return;
        var httpCode = int.Parse(statusMatch.Groups[1].Value);
        if (httpCode != 200 && httpCode != 620) return;

        var state = httpCode == 200 ? "ready" : "standby";
        var entry = found.TryGetValue(host, out var existing) ? existing : new DiscoveredConsole { Host = host };
        entry.State = state;

        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var val = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "host-name":   entry.Name = val; break;
                case "host-type":   entry.Type = val; break;
                case "host-id":     entry.HostId = val; break;
                case "system-version": entry.FirmwareVersion = val; break;
                case "running-app-titleid": entry.RunningTitleId = val; break;
                case "running-app-name":    entry.RunningTitle = val; break;
            }
        }
        found[host] = entry;
    }

    // ─── Wake console ────────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error, string Method)> WakeConsoleAsync(
        string host, string registKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(registKey))
            return (false, "No registration key", "");

        // Prefer CLI
        var exe = ResolveExe();
        if (exe is not null)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("wakeup");
            psi.ArgumentList.Add("--host");     psi.ArgumentList.Add(host);
            psi.ArgumentList.Add("--regist-key"); psi.ArgumentList.Add(registKey);

            try
            {
                using var p = Process.Start(psi)!;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(10_000);
                try
                {
                    await p.WaitForExitAsync(cts.Token);
                    return (p.ExitCode == 0, null, "chiaki-cli");
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(); }
                    catch (Exception ex) { Log.Debug(ex, "[chiaki] wake cli process already exited"); }
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] wake via cli failed; falling back to UDP"); }
        }

        // Direct UDP wake packet (PS4 port 987, PS5 port 9302)
        return await SendUdpWakeAsync(host, registKey, ct);
    }

    private static async Task<(bool Success, string? Error, string Method)> SendUdpWakeAsync(
        string host, string registKey, CancellationToken ct)
    {
        var targets = new[]
        {
            (Port: 987,  Msg: $"WAKEUP * HTTP/1.1\nclient-type:vr\nauth-type:R\nmodel:w\napp-type:r\nuser-credential:{registKey}\ndevice-discovery-protocol-version:00020020\n"),
            (Port: 9302, Msg: $"WAKEUP * HTTP/1.1\nclient-type:vr\nauth-type:R\nmodel:w\napp-type:r\nuser-credential:{registKey}\ndevice-discovery-protocol-version:00030010\n"),
        };

        try
        {
            using var udp = new UdpClient();
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            udp.Client.EnableBroadcast = true;

            var hosts = new List<string> { host };
            var parts = host.Split('.');
            if (parts.Length == 4) { parts[3] = "255"; hosts.Add(string.Join('.', parts)); }

            foreach (var h in hosts)
            {
                foreach (var (port, msg) in targets)
                {
                    var bytes = Encoding.UTF8.GetBytes(msg);
                    await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(h), port));
                }
            }
            await Task.Delay(500, ct);
            return (true, null, "udp");
        }
        catch (Exception ex)
        {
            return (false, ex.Message, "udp");
        }
    }

    // ─── Console registration ────────────────────────────────────────────────

    public async Task<(bool Success, string? RegistKey, string? Morning, string? Error)> RegisterConsoleAsync(
        string host, string? psnAccountId, string? pin, CancellationToken ct = default)
    {
        var exe = ResolveExe();
        if (exe is null) return (false, null, null, "chiaki-ng not found");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("register");
        psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(host);
        if (!string.IsNullOrEmpty(psnAccountId)) { psi.ArgumentList.Add("--psn-account-id"); psi.ArgumentList.Add(psnAccountId!); }
        if (!string.IsNullOrEmpty(pin))          { psi.ArgumentList.Add("--pin");             psi.ArgumentList.Add(pin!); }

        using var p = Process.Start(psi)!;
        var output = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(30_000);
        try { await p.WaitForExitAsync(cts.Token); }
        catch
        {
            try { p.Kill(); }
            catch (Exception ex) { Log.Debug(ex, "[chiaki] register process already exited after timeout"); }
            return (false, null, null, "Registration timed out");
        }

        var raw = output.ToString();
        if (p.ExitCode != 0) return (false, null, null, raw.Length > 0 ? raw : $"Exit {p.ExitCode}");

        var registKey = System.Text.RegularExpressions.Regex.Match(raw, @"regist[_-]?key[=:]\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        var morning   = System.Text.RegularExpressions.Regex.Match(raw, @"morning[=:]\s*(\S+)",        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        return (true, registKey.Length > 0 ? registKey : null, morning.Length > 0 ? morning : null, null);
    }

    // ─── Auto-setup ──────────────────────────────────────────────────────────

    public void AutoSetupIfMissing()
    {
        if (GetBundledExe() is not null) return;
        if (GetSystemCandidates().Any(File.Exists)) return;
        if (GetUserConfiguredExe() is not null) return;

        Log.Information("[chiaki] Not found — starting automatic setup...");
        RaiseEvent("", "setup_started", []);

        var scriptPath = _paths.GetResourcePath("scripts/setup-chiaki.ps1");
        if (!File.Exists(scriptPath))
        {
            Log.Warning("[chiaki] setup-chiaki.ps1 not found, skipping auto-setup");
            return;
        }

        var installDir = Path.Combine(_paths.AppDataDir, "chiaki-ng");
        _ = Task.Run(async () =>
        {
            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("-InstallDir");      psi.ArgumentList.Add(installDir);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                using var p = Process.Start(psi)!;
                await p.WaitForExitAsync(cts.Token);
                if (p.ExitCode == 0)
                {
                    var version = GetBundledVersion();
                    Log.Information("[chiaki] Auto-setup complete — v{Version}", version);
                    RaiseEvent("", "setup_complete", new() { ["version"] = version });
                }
                else
                {
                    Log.Error("[chiaki] Auto-setup failed (exit {Code})", p.ExitCode);
                    RaiseEvent("", "setup_failed", new() { ["error"] = $"Setup exited with code {p.ExitCode}" });
                }
            }
            catch (OperationCanceledException)
            {
                Log.Error("[chiaki] Auto-setup timed out");
                RaiseEvent("", "setup_failed", new() { ["error"] = "Setup timed out after 5 minutes" });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[chiaki] Auto-setup spawn error");
                RaiseEvent("", "setup_failed", new() { ["error"] = ex.Message });
            }
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void RaiseEvent(string gameId, string type, Dictionary<string, object?> data)
    {
        SessionEvent?.Invoke(this, new ChiakiEventArgs { GameId = gameId, Type = type, Data = data });
    }

    // Reparent an existing session window into a host (Windows-only).
    public bool EmbedSessionToHost(string gameId, nint hostHwnd, int x, int y, int width, int height)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        if (!_sessions.TryGetValue(gameId, out var session)) return false;
        if (session.EmbedWindowHandle == nint.Zero) return false;

        try
        {
            Win32Interop.ReparentAndPosition(session.EmbedWindowHandle, hostHwnd, x, y, width, height);
            session.IsEmbedded = true;
            RaiseEvent(gameId, "embedded", new() { ["embedded"] = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[chiaki] EmbedSessionToHost failed");
            return false;
        }
    }

    /// <summary>
    /// Reparent the first Chiaki window we are tracking (streaming/connecting).
    /// Used when the UI tab's <c>GameId</c> no longer matches the dictionary key after title detection.
    /// </summary>
    public bool EmbedAnyStreamingSessionToHost(nint hostHwnd, int x, int y, int width, int height)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        foreach (var kv in _sessions)
        {
            var session = kv.Value;
            if (session.EmbedWindowHandle == nint.Zero) continue;
            if (session.State is not ("streaming" or "connecting" or "launching")) continue;
            try
            {
                Win32Interop.ReparentAndPosition(session.EmbedWindowHandle, hostHwnd, x, y, width, height);
                session.IsEmbedded = true;
                RaiseEvent(kv.Key, "embedded", new() { ["embedded"] = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[chiaki] EmbedAnyStreamingSessionToHost failed for session {GameId}", kv.Key);
                return false;
            }
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var gameId in _sessions.Keys.ToList())
            StopSession(gameId);
    }
}

// ─── Win32 Interop ────────────────────────────────────────────────────────────

internal static class Win32Interop
{
    [DllImport("user32.dll")]
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private const uint SWP_NOZORDER     = 0x0004;
    private const uint SWP_SHOWWINDOW   = 0x0040;
    private const uint WS_CHILD         = 0x40000000;
    private const int  GWL_STYLE        = -16;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern long GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern long SetWindowLong(nint hWnd, int nIndex, long dwNewLong);

    public static nint FindProcessMainWindow(int pid)
    {
        nint found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var winPid);
            if ((int)winPid == pid && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false; // stop
            }
            return true;
        }, nint.Zero);
        return found;
    }

    public static void ReparentAndPosition(nint childHwnd, nint parentHwnd, int X, int Y, int cx, int cy)
    {
        try
        {
            SetParent(childHwnd, parentHwnd);
            var style = GetWindowLong(childHwnd, GWL_STYLE);
            SetWindowLong(childHwnd, GWL_STYLE, style | WS_CHILD);
            SetWindowPos(childHwnd, nint.Zero, X, Y, cx, cy, SWP_NOZORDER | SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[win32] ReparentAndPosition failed");
        }
    }

    public static void EmbedWindow(nint childHwnd, int pid)
    {
        try
        {
            var style = GetWindowLong(childHwnd, GWL_STYLE);
            SetWindowLong(childHwnd, GWL_STYLE, style | WS_CHILD);
            // SetParent into the desktop for now; ViewModel will update with actual host handle
            SetWindowPos(childHwnd, nint.Zero, 0, 0, 1280, 720, SWP_NOZORDER | SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[win32] EmbedWindow failed");
        }
    }

    public static void UnembedWindow(nint childHwnd)
    {
        try
        {
            var style = GetWindowLong(childHwnd, GWL_STYLE);
            SetWindowLong(childHwnd, GWL_STYLE, style & ~(long)WS_CHILD);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[win32] UnembedWindow best-effort failure");
        }
    }
}
