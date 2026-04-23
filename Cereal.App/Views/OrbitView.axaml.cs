using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Cereal.App.Controls.Orbit;
using Cereal.App.Services;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views;

public partial class OrbitView : UserControl
{
    private OrbitWorld? _world;
    private readonly List<GameOrb> _orbs = [];
    private readonly List<SpaceStation> _stations = [];
    private ShootingStarScheduler? _shootingStars;

    // Platforms shown as space stations on the orbit view.
    private static readonly string[] StationPlatforms = { "psn", "xbox" };

    public OrbitView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PointerMoved += OnViewPointerMoved;
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
            BuildScene();
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
        Rebuild();
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
    }

    private void BuildScene()
    {
        if (_world is null) return;
        var settings = App.Services.GetRequiredService<SettingsService>().Get();
        var starCount = settings.StarDensity switch { "low" => 300, "high" => 1500, _ => 800 };

        // 1. Background stars (inside the world, pan/zoom with the camera).
        _world.World.Children.Add(new StarField
        {
            StarCount = starCount,
            AnimationsEnabled = settings.ShowAnimations,
        });

        // Fullscreen parallax layer sitting behind the world (outside the
        // camera transform). Density mirrors the original useMemo().
        var parallax = this.FindControl<ParallaxStarBackground>("Parallax");
        if (parallax is not null)
        {
            var bgCount = settings.StarDensity switch { "low" => 120, "high" => 500, _ => 280 };
            parallax.AnimationsEnabled = settings.ShowAnimations;
            parallax.StarCount = bgCount;
        }

        // 2. Ambient background nebulae (fixed positions, not tied to any
        //    platform). Matches the 5 absolute-positioned glow divs in App.tsx.
        NebulaCluster.BuildAmbient(_world.World);

        // 3. Build clusters for platforms the user actually has (excluding
        //    streaming stations, which get a different visual below).
        var games = App.Services.GetRequiredService<GameService>().GetAll();
        var byPlat = games
            .GroupBy(g => string.IsNullOrEmpty(g.Platform) ? "custom" : g.Platform)
            .ToDictionary(grp => grp.Key, grp => grp.ToList());

        foreach (var plat in byPlat.Keys)
        {
            if (StationPlatforms.Contains(plat)) continue;
            NebulaCluster.Build(_world.World, plat, settings.ShowAnimations);
        }

        // 4. Place game orbs for non-streaming platforms. Matches `placeOrb()`.
        var paths = App.Services.GetRequiredService<PathService>();
        foreach (var (plat, platGames) in byPlat)
        {
            if (StationPlatforms.Contains(plat)) continue;

            var center = NebulaCluster.Centers.TryGetValue(plat, out var c)
                ? c : NebulaCluster.Centers["custom"];
            var color = NebulaCluster.Colors.TryGetValue(plat, out var col)
                ? col : Avalonia.Media.Color.Parse("#aaaaff");
            var platLabel = NebulaCluster.Labels.TryGetValue(plat, out var lbl)
                ? lbl : plat;

            var total = platGames.Count;
            for (var i = 0; i < total; i++)
            {
                var g = platGames[i];
                var (x, y) = ScatterOrbit(g.Id, g.Name, i, total, center.X, center.Y);

                var localCover = paths.GetCoverPath(g.Id);
                var coverPath = File.Exists(localCover) ? localCover : null;

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
            NebulaCluster.Build(_world.World, plat, settings.ShowAnimations);

            var letter = label.Length > 0 ? char.ToUpperInvariant(label[0]).ToString() : "?";
            var station = new SpaceStation(_world, color, label, letter, gameCount);
            station.Tag = plat;
            station.Clicked += OnStationClicked;
            station.PlaceOn(_world.World, center.X, center.Y);
            _stations.Add(station);

            // If the user has game entries under this platform (e.g. imported
            // Chiaki titles), still show orbs around the station.
            if (byPlat.TryGetValue(plat, out var platGames) && platGames.Count > 0)
            {
                var total = platGames.Count;
                for (var i = 0; i < total; i++)
                {
                    var g = platGames[i];
                    var (x, y) = ScatterOrbit(g.Id, g.Name, i, total, center.X, center.Y);
                    var localCover = paths.GetCoverPath(g.Id);
                    var coverPath = File.Exists(localCover) ? localCover : null;

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
            }
        }

        // 6. Occasional shooting-star streaks (respects "show animations").
        if (settings.ShowAnimations)
        {
            _shootingStars = new ShootingStarScheduler(_world.World);
            _shootingStars.Start();
        }
    }

    private void OnStationClicked(SpaceStation station)
    {
        if (DataContext is not MainViewModel vm) return;
        var platform = station.Tag as string;
        if (platform == "psn") vm.OpenChiakiCommand.Execute(null);
        else if (platform == "xbox") vm.OpenXcloudCommand.Execute(null);
    }

    // Hash-based scatter — stable per game (doesn't jitter across launches).
    // Mirrors placeOrb() in the original JS exactly.
    private static (double X, double Y) ScatterOrbit(
        string id, string name, int idx, int totalForPlatform, double cx, double cy)
    {
        var seed = HashString(string.IsNullOrEmpty(id) ? (name ?? "") : id);
        var angle = ((seed & 0xfff) / (double)0xfff) * Math.PI * 2;
        var rings = (int)Math.Ceiling(totalForPlatform / 8.0);
        var ring = (idx % Math.Max(1, rings)) + 1;
        var baseR = 120 + ring * 55;
        var r = baseR + ((seed >> 12) & 0xff) / 255.0 * 30 - 15;
        var x = cx + Math.Cos(angle + idx * 0.8) * r;
        var y = cy + Math.Sin(angle + idx * 0.8) * r;
        return (x, y);
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
