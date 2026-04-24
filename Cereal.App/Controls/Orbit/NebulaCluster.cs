using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Cereal.App.Utilities;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Adds the per-platform "cluster" visuals (nebulae + orbit rings + sun +
/// watermark label) to an <see cref="OrbitWorld"/>'s world canvas at a given
/// center point. Mirrors the <c>buildCluster()</c> function in the original
/// OrbitView JS.
/// </summary>
internal static class NebulaCluster
{
    public readonly record struct ClusterCenter(double X, double Y);

    /// <summary>Default hub for platforms not in <see cref="Centers"/> (matches Vite <c>CLUSTER_CENTERS[plat] || {'x':1500,'y':1000}</c>).</summary>
    public static readonly ClusterCenter DefaultHub = new(1500, 1000);

    /// <summary>
    /// Map stored <see cref="Cereal.App.Models.Game.Platform"/> values to keys used in
    /// <see cref="Centers"/> so each provider's games orbit the correct sun.
    /// </summary>
    public static string NormalizeOrbitPlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return "custom";
        var s = platform.Trim().ToLowerInvariant();
        return s switch
        {
            "psremote" => "psn",
            "playstation" => "psn",
            "xcloud" => "xbox",
            _ => s,
        };
    }

    // Hard-coded cluster positions — copied verbatim from the original JS
    // (CLUSTER_CENTERS in the old OrbitHtml template).
    public static readonly IReadOnlyDictionary<string, ClusterCenter> Centers =
        new Dictionary<string, ClusterCenter>
        {
            ["steam"] = new(480, 580),
            ["epic"] = new(1450, 400),
            ["gog"] = new(2400, 560),
            ["psn"] = new(560, 1400),
            ["xbox"] = new(1600, 1350),
            ["custom"] = new(2500, 1350),
            ["battlenet"] = new(950, 900),
            ["ea"] = new(1450, 950),
            ["ubisoft"] = new(2000, 900),
            ["itchio"] = new(2000, 1550),
        };

    public static readonly IReadOnlyDictionary<string, Color> Colors =
        new Dictionary<string, Color>
        {
            ["steam"] = Color.Parse("#64b4f5"),
            ["epic"] = Color.Parse("#cccccc"),
            ["gog"] = Color.Parse("#b36ef5"),
            ["psn"] = Color.Parse("#0070d1"),
            ["xbox"] = Color.Parse("#107c10"),
            ["custom"] = Color.Parse("#d4a853"),
            ["battlenet"] = Color.Parse("#009ae5"),
            ["ea"] = Color.Parse("#f44040"),
            ["ubisoft"] = Color.Parse("#0070ff"),
            ["itchio"] = Color.Parse("#fa5c5c"),
        };

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>
        {
            ["steam"] = "Steam",
            ["epic"] = "Epic",
            ["gog"] = "GOG",
            ["psn"] = "PlayStation",
            ["xbox"] = "Xbox",
            ["custom"] = "Custom",
            ["battlenet"] = "Battle.net",
            ["ea"] = "EA",
            ["ubisoft"] = "Ubisoft",
            ["itchio"] = "itch.io",
        };

    public static void Build(Canvas world, string platform, bool animations)
    {
        var key = NormalizeOrbitPlatform(platform);
        if (!Centers.TryGetValue(key, out var c)) return;
        Build(world, key, c.X, c.Y, animations);
    }

    public static void Build(Canvas world, string platform, double cx, double cy, bool animations)
    {
        var color = Colors.TryGetValue(platform, out var col) ? col : Color.Parse("#aaaaff");
        var label = Labels.TryGetValue(platform, out var lbl) ? lbl : platform;

        AddNebulae(world, cx, cy, color);
        AddRings(world, cx, cy);
        AddFlare(world, cx, cy, color);
        AddCorona(world, cx, cy, color, animations);
        AddCore(world, platform, cx, cy, color, label);
        AddWatermark(world, cx, cy, label);
    }

    // Ambient background nebulae that float across the world, unrelated to
    // any specific platform cluster. Mirrors the five absolute-positioned
    // divs in App.tsx lines ~809-813.
    public static void BuildAmbient(Canvas world)
    {
        ReadOnlySpan<(double X, double Y, double W, double H, Color Col, double Op)> specs = stackalloc (double, double, double, double, Color, double)[]
        {
            ( 950,  500, 400, 160, Color.FromRgb(100, 192, 244), 0.02),
            (1850,  480, 350, 140, Color.FromRgb(180,  74, 255), 0.015),
            (1050,  950, 500, 200, Color.FromRgb(212, 168,  83), 0.015),
            (2050,  950, 400, 160, Color.FromRgb( 16, 124,  16), 0.015),
            ( 600, 1000, 350, 300, Color.FromRgb(  0, 112, 209), 0.01),
        };

        foreach (var s in specs)
        {
            var gradient = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(s.Op * 255 * 6), s.Col.R, s.Col.G, s.Col.B), 0),
                    new GradientStop(Color.FromArgb(0, s.Col.R, s.Col.G, s.Col.B), 1),
                },
            };
            var glow = new Ellipse
            {
                Width = s.W,
                Height = s.H,
                Fill = gradient,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(glow, s.X - s.W / 2);
            Canvas.SetTop(glow, s.Y - s.H / 2);
            world.Children.Add(glow);
        }
    }

    // ─── Nebulae (3 soft glows) ─────────────────────────────────────────────
    // The original CSS used `filter:blur(100px)` on solid-colored discs. We
    // approximate this with RadialGradientBrush which falls off smoothly and
    // is free on the GPU (no real blur pass required).
    private static void AddNebulae(Canvas world, double cx, double cy, Color color)
    {
        // (width, height, peakOpacity) — matches `[[600,600,.07],[350,350,.03],[280,280,.02]]`
        ReadOnlySpan<(double W, double H, double Op)> specs = stackalloc (double, double, double)[]
        {
            (600, 600, 0.07),
            (350, 350, 0.03),
            (280, 280, 0.02),
        };

        foreach (var (w, h, op) in specs)
        {
            var gradient = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(op * 255), color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb((byte)(op * 128), color.R, color.G, color.B), 0.55),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
                },
            };

            var nebula = new Ellipse
            {
                Width = w,
                Height = h,
                Fill = gradient,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(nebula, cx - w / 2);
            Canvas.SetTop(nebula, cy - h / 2);
            world.Children.Add(nebula);
        }
    }

    // ─── Orbit rings ────────────────────────────────────────────────────────
    private static void AddRings(Canvas world, double cx, double cy)
    {
        ReadOnlySpan<double> radii = stackalloc double[] { 90, 150, 220, 300 };
        foreach (var r in radii)
        {
            var ring = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Stroke = new ImmutableSolidColorBrush(
                    Color.FromArgb((byte)(r < 160 ? 10 : 5), 255, 255, 255)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);
            world.Children.Add(ring);
        }
    }

    // ─── Sun flare (outer glow, 200x200, non-pulsing) ───────────────────────
    // Matches the `galaxy-sun-flare` div — a wider, fainter halo sitting
    // behind the corona to give the sun a longer-reach glow.
    private static void AddFlare(Canvas world, double cx, double cy, Color color)
    {
        const double size = 200;
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(color, 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.70),
            },
        };

        var flare = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = brush,
            Opacity = 0.10,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(flare, cx - size / 2);
        Canvas.SetTop(flare, cy - size / 2);
        world.Children.Add(flare);
    }

    // ─── Sun corona (pulsing) ───────────────────────────────────────────────
    private static void AddCorona(Canvas world, double cx, double cy, Color color, bool animations)
    {
        const double size = 130;
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(color, 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.65),
            },
        };

        var corona = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = brush,
            Opacity = 0.35,
            RenderTransformOrigin = RelativePoint.Center,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(corona, cx - size / 2);
        Canvas.SetTop(corona, cy - size / 2);
        // Z-index: corona behind core; both in front of rings/nebulae.
        world.Children.Add(corona);

        if (!animations) return;

        // Use a timer-driven pulse to avoid transform-animation target casting
        // issues seen with some Avalonia runtime combinations.
        var scale = new ScaleTransform(1, 1);
        corona.RenderTransform = scale;
        corona.RenderTransformOrigin = RelativePoint.Center;
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalSeconds;
            var s = (Math.Sin(t / 4d * Math.PI * 2d) + 1d) / 2d; // 0..1
            corona.Opacity = 0.30 + (0.20 * s);
            var sc = 1.0 + (0.12 * s);
            scale.ScaleX = sc;
            scale.ScaleY = sc;
        });
        timer.Start();
        corona.DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    // ─── Sun core (letter disc) ─────────────────────────────────────────────
    private static void AddCore(Canvas world, string platform, double cx, double cy, Color color, string label)
    {
        const double size = 56;

        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.35, 0.35, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.35, 0.35, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x33, 255, 255, 255), 0),
                new GradientStop(color, 1),
            },
        };

        var core = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = brush,
            Stroke = new ImmutableSolidColorBrush(Color.FromArgb(0x1a, 255, 255, 255)),
            StrokeThickness = 2,
            Effect = new DropShadowEffect
            {
                BlurRadius = 60,
                Color = color,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 1,
            },
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(core, cx - size / 2);
        Canvas.SetTop(core, cy - size / 2);
        world.Children.Add(core);

        // Official platform icon centered in the core (fallback to letter if missing).
        if (PlatformLogos.TryGet(platform) is { } logo)
        {
            var iconCanvas = new Canvas
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
            };

            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(logo.PathData),
                Stretch = Stretch.Uniform,
                Width = 24,
                Height = 24,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(path, (size - 24) / 2);
            Canvas.SetTop(path, (size - 24) / 2);

            if (logo.IsStroke)
            {
                path.Stroke = new ImmutableSolidColorBrush(Color.FromArgb(0xee, 255, 255, 255));
                path.StrokeThickness = logo.StrokeWidth;
                path.StrokeLineCap = PenLineCap.Round;
                path.Fill = Brushes.Transparent;
            }
            else
            {
                path.Fill = new ImmutableSolidColorBrush(Color.FromArgb(0xee, 255, 255, 255));
            }

            iconCanvas.Children.Add(path);
            Canvas.SetLeft(iconCanvas, cx - size / 2);
            Canvas.SetTop(iconCanvas, cy - size / 2);
            world.Children.Add(iconCanvas);
        }
        else
        {
            var letter = new TextBlock
            {
                Text = label.Length > 0 ? char.ToUpperInvariant(label[0]).ToString() : "?",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new ImmutableSolidColorBrush(Color.FromArgb(0xe6, 255, 255, 255)),
                Width = size,
                Height = size,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(letter, cx - size / 2);
            Canvas.SetTop(letter, cy - size / 2 + (size - 24) / 2);
            world.Children.Add(letter);
        }
    }

    // ─── Cluster watermark (giant faded label) ──────────────────────────────
    private static void AddWatermark(Canvas world, double cx, double cy, string label)
    {
        var tb = new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 80,
            FontWeight = FontWeight.Thin,
            Foreground = new ImmutableSolidColorBrush(Color.FromArgb(6, 255, 255, 255)),
            IsHitTestVisible = false,
            // Emulate letter-spacing: 8px
            LetterSpacing = 8,
        };

        // Center the label on (cx, cy). We measure after it's added to a layout
        // pass, but at this point the control isn't in the tree yet, so we
        // attach a one-shot handler that re-positions once it has bounds.
        world.Children.Add(tb);
        void Reposition()
        {
            Canvas.SetLeft(tb, cx - tb.Bounds.Width / 2);
            Canvas.SetTop(tb, cy - tb.Bounds.Height / 2);
        }
        tb.AttachedToVisualTree += (_, _) => Reposition();
        tb.LayoutUpdated += (_, _) => Reposition();
    }
}
