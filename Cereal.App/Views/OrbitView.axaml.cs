using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Cereal.App.Controls.Orbit;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views;

public partial class OrbitView : UserControl
{
    public static readonly Avalonia.StyledProperty<string> ZoomPercentLabelProperty =
        Avalonia.AvaloniaProperty.Register<OrbitView, string>(nameof(ZoomPercentLabel), "100%");

    private OrbitWorld? _world;
    private readonly List<GameOrb> _orbs = [];
    private readonly List<SpaceStation> _stations = [];
    private ShootingStarScheduler? _shootingStars;
    private DispatcherTimer? _galaxyIntroTimer;

    public string ZoomPercentLabel
    {
        get => GetValue(ZoomPercentLabelProperty);
        private set => SetValue(ZoomPercentLabelProperty, value);
    }

    // Platforms shown as space stations on the orbit view.
    private static readonly string[] StationPlatforms = { "psn", "xbox" };

    public OrbitView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PointerMoved += OnViewPointerMoved;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
            EnsureFittedForShow();
    }

    /// <summary>When switching to orbit, apply FitAll if the user has not panned/zoomed (handles hidden-first load).</summary>
    public void EnsureFittedForShow()
    {
        if (_world is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_world is null || !IsVisible) return;
            if (!_world.UserAdjustedCamera)
            {
                _world.FitAll();
                UpdateZoomLabel();
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnViewPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var parallax = this.FindControl<ParallaxStarBackground>("Parallax");
        parallax?.UpdatePointer(p.X / w - 0.5, p.Y / h - 0.5);
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_world is not null) return;
        try
        {
            _world = this.FindControl<OrbitWorld>("World");
            if (_world is null)
            {
                ShowError("Orbit world control not found.");
                return;
            }
            _world.CameraChanged += OnWorldCameraChanged;
            BuildScene();
            _world.FitAll();
            UpdateZoomLabel();
            if (IsVisible)
                PlayEntranceAnimation();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[orbit] Failed to initialize world");
            ShowError(ex.Message);
        }
    }

    /// <summary>Re-scatter orbs after the game library has changed.</summary>
    public Task RefreshGamesAsync()
    {
        if (_world is null) return Task.CompletedTask;
        try
        {
            Rebuild();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[orbit] RefreshGamesAsync failed");
            ShowError(ex.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>Full scene rebuild (stars + clusters + orbs) — triggered by
    /// settings changes such as star density or animations toggle.</summary>
    public void Rebuild()
    {
        if (_world is null) return;
        foreach (var o in _orbs)
        {
            o.SelectRequested -= OnSelectRequested;
            o.LaunchRequested -= OnLaunchRequested;
        }
        _orbs.Clear();
        foreach (var s in _stations) s.Clicked -= OnStationClicked;
        _stations.Clear();
        _shootingStars?.Stop();
        _shootingStars = null;
        _world.World.Children.Clear();
        BuildScene();
        _world.ResetAndFitAll();
        UpdateZoomLabel();
    }

    private void OnWorldCameraChanged(object? sender, EventArgs e) => UpdateZoomLabel();

    /// <summary>index.css <c>galaxy-entering</c> / <c>galaxyIn</c> — scale from zoomed with opacity ramp.</summary>
    public void PlayEntranceAnimation()
    {
        if (_world is null) return;
        _galaxyIntroTimer?.Stop();
        var scale = new ScaleTransform(3.8, 3.8);
        _world.RenderTransform = scale;
        _world.Opacity = 0;
        var start = DateTime.UtcNow;
        const double durationMs = 820;
        _galaxyIntroTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _galaxyIntroTimer.Tick += (_, _) =>
        {
            var u = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / durationMs);
            var opacity = Math.Min(1, u / 0.22);
            var ease = 1 - Math.Pow(1 - u, 3);
            _world.Opacity = opacity;
            var s = 3.8 + (1 - 3.8) * ease;
            scale.ScaleX = scale.ScaleY = s;
            if (u >= 1)
            {
                _galaxyIntroTimer?.Stop();
                _world.Opacity = 1;
                scale.ScaleX = scale.ScaleY = 1;
            }
        };
        _galaxyIntroTimer.Start();
    }

    private void UpdateZoomLabel()
    {
        if (_world is null) return;
        var pct = (int)Math.Round(_world.Camera.Zoom * 100);
        ZoomPercentLabel = $"{pct}%";
    }

    private void FitAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _world?.ResetAndFitAll();
        UpdateZoomLabel();
    }

    private void ZoomIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_world is null) return;
        _world.MarkUserCameraAdjustment();
        var cam = _world.Camera;
        _world.SetCamera(cam.X, cam.Y, cam.Zoom * 1.1);
        UpdateZoomLabel();
    }

    private void ZoomOut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_world is null) return;
        _world.MarkUserCameraAdjustment();
        var cam = _world.Camera;
        _world.SetCamera(cam.X, cam.Y, cam.Zoom * 0.9);
        UpdateZoomLabel();
    }

    private void BuildScene()
    {
        if (_world is null) return;
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        var starCount = settings.StarDensity switch { "low" => 300, "high" => 1500, _ => 800 };

        // 1. Background stars (inside the world, pan/zoom with the camera).
        try
        {
            _world.World.Children.Add(new StarField
            {
                StarCount = starCount,
                AnimationsEnabled = settings.ShowAnimations,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[orbit] Failed to add StarField");
        }

        // Fullscreen parallax layer sitting behind the world (outside the
        // camera transform). Density mirrors the original useMemo().
        var parallax = this.FindControl<ParallaxStarBackground>("Parallax");
        if (parallax is not null)
        {
            try
            {
                var bgCount = settings.StarDensity switch { "low" => 120, "high" => 500, _ => 280 };
                parallax.AnimationsEnabled = settings.ShowAnimations;
                parallax.StarCount = bgCount;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[orbit] Failed to configure parallax background");
            }
        }

        // 2. Ambient background nebulae (fixed positions, not tied to any
        //    platform). Matches the 5 absolute-positioned glow divs in App.tsx.
        try
        {
            NebulaCluster.BuildAmbient(_world.World);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[orbit] Failed to add ambient nebulae");
        }

        // 3–4. Library orbs — parity with Vite App.tsx orbData: multi-arm spiral per
        //    platform hub, collision separation, installed-only, normalized platform keys.
        var dbGames = App.Services.GetRequiredService<GameService>().GetAll();
        var games = dbGames.Where(g => g.Installed != false).ToList();
        var sortOrder = (DataContext as MainViewModel)?.SortOrder ?? "name";

        var byPlat = games
            .GroupBy(g => NebulaCluster.NormalizeOrbitPlatform(g.Platform))
            .ToDictionary(grp => grp.Key, grp => grp.ToList());

        foreach (var plat in byPlat.Keys)
        {
            if (StationPlatforms.Contains(plat)) continue;
            try
            {
                NebulaCluster.Build(_world.World, plat, settings.ShowAnimations);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[orbit] Failed to build cluster for {Platform}", plat);
            }
        }

        var paths = App.Services.GetRequiredService<PathService>();
        foreach (var (plat, platGames) in byPlat)
        {
            if (StationPlatforms.Contains(plat)) continue;

            var center = NebulaCluster.Centers.TryGetValue(plat, out var c)
                ? c
                : NebulaCluster.DefaultHub;
            var color = NebulaCluster.Colors.TryGetValue(plat, out var col)
                ? col
                : Avalonia.Media.Color.Parse("#aaaaff");
            var platLabel = NebulaCluster.Labels.TryGetValue(plat, out var lbl)
                ? lbl
                : plat;

            var sorted = SortGamesForOrbit(platGames, sortOrder);
            var positions = PlaceOrbsSpiralArms(sorted, center.X, center.Y);

            for (var i = 0; i < sorted.Count; i++)
            {
                var g = sorted[i];
                var (x, y) = positions[i];

                var localCover = paths.GetCoverPath(g.Id);
                var coverPath = File.Exists(localCover) ? localCover : null;

                try
                {
                    var orb = new GameOrb(
                        _world,
                        gameId: g.Id,
                        gameName: g.Name ?? "",
                        platformLabel: platLabel,
                        playtimeMinutes: g.PlaytimeMinutes ?? 0,
                        accentColor: color,
                        coverPath: coverPath,
                        animations: settings.ShowAnimations);

                    orb.SelectRequested += OnSelectRequested;
                    orb.LaunchRequested += OnLaunchRequested;
                    orb.PlaceOn(_world.World, x, y);
                    _orbs.Add(orb);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[orbit] Failed to create orb for {GameId}", g.Id);
                }
            }
        }

        // 5. Space stations for streaming platforms (PSN / Xbox). These are
        //    always shown regardless of whether games exist yet; clicking opens
        //    the corresponding Chiaki / xCloud panel.
        foreach (var plat in StationPlatforms)
        {
            if (!NebulaCluster.Centers.TryGetValue(plat, out var center)) continue;
            var color = NebulaCluster.Colors.TryGetValue(plat, out var col) ? col : Avalonia.Media.Color.Parse("#aaaaff");
            var label = NebulaCluster.Labels.TryGetValue(plat, out var lbl) ? lbl : plat;
            var gameCount = byPlat.TryGetValue(plat, out var gs) ? gs.Count : 0;

            // Give each station a faint nebula + watermark so the cluster still
            // reads at a distance even without orbs.
            try
            {
                NebulaCluster.Build(_world.World, plat, settings.ShowAnimations);

                var letter = label.Length > 0 ? char.ToUpperInvariant(label[0]).ToString() : "?";
                var station = new SpaceStation(_world, color, label, letter, gameCount);
                station.Tag = plat;
                station.Clicked += OnStationClicked;
                station.PlaceOn(_world.World, center.X, center.Y);
                _stations.Add(station);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[orbit] Failed to create space station for {Platform}", plat);
            }

            // If the user has game entries under this platform (e.g. imported
            // Chiaki titles), still show orbs around the station.
            if (byPlat.TryGetValue(plat, out var platGames) && platGames.Count > 0)
            {
                var stationGames = platGames.Where(g => g.Installed != false).ToList();
                var sorted = SortGamesForOrbit(stationGames, sortOrder);
                var positions = PlaceOrbsSpiralArms(sorted, center.X, center.Y);
                for (var i = 0; i < sorted.Count; i++)
                {
                    var g = sorted[i];
                    var (x, y) = positions[i];
                    var localCover = paths.GetCoverPath(g.Id);
                    var coverPath = File.Exists(localCover) ? localCover : null;

                    try
                    {
                        var orb = new GameOrb(
                            _world,
                            gameId: g.Id,
                            gameName: g.Name ?? "",
                            platformLabel: label,
                            playtimeMinutes: g.PlaytimeMinutes ?? 0,
                            accentColor: color,
                            coverPath: coverPath,
                            animations: settings.ShowAnimations);

                        orb.SelectRequested += OnSelectRequested;
                        orb.LaunchRequested += OnLaunchRequested;
                        orb.PlaceOn(_world.World, x, y);
                        _orbs.Add(orb);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[orbit] Failed to create station orb for {GameId}", g.Id);
                    }
                }
            }
        }

        // 6. Occasional shooting-star streaks (respects "show animations").
        if (settings.ShowAnimations)
        {
            try
            {
                _shootingStars = new ShootingStarScheduler(_world.World);
                _shootingStars.Start();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[orbit] Failed to start shooting star scheduler");
            }
        }

        Log.Information("[orbit] Scene built: {Orbs} orbs, {Stations} stations", _orbs.Count, _stations.Count);
    }

    private void OnStationClicked(SpaceStation station)
    {
        if (DataContext is not MainViewModel vm) return;
        var platform = station.Tag as string;
        if (platform == "psn") vm.OpenChiakiCommand.Execute(null);
        else if (platform == "xbox") vm.OpenXcloudCommand.Execute(null);
    }

    private static List<Game> SortGamesForOrbit(List<Game> gms, string sortOrder) =>
        sortOrder switch
        {
            "played" => gms.OrderByDescending(g => g.PlaytimeMinutes ?? 0)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "recent" => gms.OrderByDescending(g => g.LastPlayed ?? "")
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "installed" => gms.OrderByDescending(g => g.Installed == false ? 0 : 1)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "added" => gms.OrderByDescending(g => g.AddedAt ?? "")
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => gms.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        };

    /// <summary>
    /// Multi-arm spiral + radial jitter + separation passes — deterministic counterpart
    /// to Vite <c>orbData</c> (App.tsx ~596–636).
    /// </summary>
    private static List<(double X, double Y)> PlaceOrbsSpiralArms(
        IReadOnlyList<Game> sortedGms, double cx, double cy)
    {
        var count = sortedGms.Count;
        if (count == 0) return [];

        var nArms = Math.Max(2, (int)Math.Ceiling(count / 6.0));
        var orbs = new List<OrbSlot>(count);
        for (var i = 0; i < count; i++)
        {
            var game = sortedGms[i];
            var pt = game.PlaytimeMinutes ?? 0;
            // Match <see cref="GameOrb"/> disc diameter for separation passes.
            var sz = 44 + Math.Min(pt / 300.0, 20);

            var arm = i % nArms;
            var ai = i / nArms;
            var armOff = arm * (Math.PI * 2 / nArms);
            var theta = armOff + 0.65 * (ai + 1);
            var r = 70 + 50 * (ai + 1) + sz * 0.3;
            var h = HashString(string.IsNullOrEmpty(game.Id) ? game.Name ?? "" : game.Id);
            var jitter = (((h >> ((i * 3) & 15)) & 0xff) / 255.0 - 0.5) * 16;
            var sx = jitter * Math.Cos(theta + Math.PI / 2);
            var sy = jitter * Math.Sin(theta + Math.PI / 2);
            orbs.Add(new OrbSlot(cx + r * Math.Cos(theta) + sx, cy + r * Math.Sin(theta) + sy, sz));
        }

        for (var pass = 0; pass < 3; pass++)
        {
            for (var i = 0; i < orbs.Count; i++)
            {
                for (var j = i + 1; j < orbs.Count; j++)
                {
                    var a = orbs[i];
                    var b = orbs[j];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    var minDist = (a.Size + b.Size) / 2 + 12;
                    if (dist >= minDist || dist <= 0) continue;
                    var push = (minDist - dist) / 2 + 2;
                    var nx = dx / dist;
                    var ny = dy / dist;
                    orbs[i] = new OrbSlot(a.X - nx * push, a.Y - ny * push, a.Size);
                    orbs[j] = new OrbSlot(b.X + nx * push, b.Y + ny * push, b.Size);
                }
            }
        }

        return orbs.ConvertAll(o => (o.X, o.Y));
    }

    private struct OrbSlot
    {
        public double X;
        public double Y;
        public double Size;

        public OrbSlot(double x, double y, double size)
        {
            X = x;
            Y = y;
            Size = size;
        }
    }

    // Reproduces `hashStr` from the original JS (Math.imul(31, h) + char).
    private static int HashString(string s)
    {
        var h = 0;
        for (var i = 0; i < s.Length; i++)
        {
            unchecked { h = (int)(31u * (uint)h) + s[i]; }
        }
        return h;
    }

    private void OnSelectRequested(string gameId)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SelectGameByIdCommand.Execute(gameId);
    }

    private void OnLaunchRequested(string gameId)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.LaunchGameByIdCommand.Execute(gameId);
    }

    private void ShowError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var error = this.FindControl<Border>("ErrorOverlay");
            var text = this.FindControl<TextBlock>("ErrorText");
            if (error is not null) error.IsVisible = true;
            if (text is not null) text.Text = $"Orbit view unavailable: {message}";
        });
    }
}
