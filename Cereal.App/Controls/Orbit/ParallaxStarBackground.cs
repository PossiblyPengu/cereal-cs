using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Three fullscreen layers of background stars that translate slightly with
/// the pointer position to create a parallax effect. Mirrors the
/// <c>.parallax-layer.depth-{0,1,2}</c> divs rendered inside <c>.void-layer</c>
/// in the original React app (and the <c>useParallax</c> hook).
/// </summary>
public sealed class ParallaxStarBackground : Control
{
    private static readonly double[] LayerSpeed = { 24, 68, 128 };
    private const int AlphaLevels = 32;

    // Richer seven-color palette: white, blue-white, blue, violet, gold, cyan, rose
    private static readonly Color[] StarPalette =
    {
        Color.FromRgb(0xff, 0xff, 0xff), // pure white
        Color.FromRgb(0xe8, 0xf2, 0xff), // blue-white
        Color.FromRgb(0xb0, 0xcc, 0xff), // blue star
        Color.FromRgb(0xc8, 0xb0, 0xff), // violet star
        Color.FromRgb(0xff, 0xec, 0x9a), // warm gold
        Color.FromRgb(0xa4, 0xf0, 0xff), // icy cyan
        Color.FromRgb(0xff, 0xb8, 0xd4), // soft rose
    };

    private static readonly IReadOnlyDictionary<uint, ImmutableSolidColorBrush[]> ColorLuts = BuildColorLuts();

    private struct Star
    {
        public double Xp;
        public double Yp;
        public double Size;
        public double Alpha;
        public uint Rgb;
        public int Layer;
        public bool Twinkle;
        public double TwDur;
        public double TwPhase;
        public double DriftX;
        public double DriftY;
        public bool IsGiant;   // top-tier bright star — gets spikes + 3-ring halo
    }

    // Fixed nebula wisps — faint colored clouds that move with the deepest parallax layer.
    private struct Wisp
    {
        public float Xp, Yp;       // 0..1 normalized viewport position
        public float RxFrac;       // rx as fraction of viewport width
        public float RyFrac;       // ry as fraction of viewport height
        public uint  Rgb;
        public float Alpha;
        public int   Layer;
    }

    private static readonly Wisp[] WispDefs =
    {
        new() { Xp=0.22f, Yp=0.28f, RxFrac=0.17f, RyFrac=0.12f, Rgb=0x6677ffu, Alpha=0.045f, Layer=0 },
        new() { Xp=0.78f, Yp=0.64f, RxFrac=0.14f, RyFrac=0.19f, Rgb=0xaa66ffu, Alpha=0.032f, Layer=0 },
        new() { Xp=0.48f, Yp=0.11f, RxFrac=0.09f, RyFrac=0.07f, Rgb=0x44d4ffu, Alpha=0.040f, Layer=0 },
        new() { Xp=0.60f, Yp=0.84f, RxFrac=0.10f, RyFrac=0.12f, Rgb=0xff6688u, Alpha=0.028f, Layer=1 },
        new() { Xp=0.12f, Yp=0.68f, RxFrac=0.12f, RyFrac=0.08f, Rgb=0x5566ffu, Alpha=0.030f, Layer=1 },
        new() { Xp=0.88f, Yp=0.18f, RxFrac=0.08f, RyFrac=0.10f, Rgb=0x33bbffu, Alpha=0.025f, Layer=0 },
    };

    // Pre-built brushes for wisps (constant alpha, no allocation at render time).
    private readonly ImmutableSolidColorBrush[] _wispBrushes;

    private Star[] _stars = Array.Empty<Star>();
    private double _offsetX;
    private double _offsetY;
    private double _targetOffsetX;
    private double _targetOffsetY;
    private DispatcherTimer? _easeTimer;
    private DispatcherTimer? _twinkleTick;
    private TopLevel? _trackedTopLevel;
    private DateTime _t0;
    private int _starCount;

    public static readonly StyledProperty<int> StarCountProperty =
        AvaloniaProperty.Register<ParallaxStarBackground, int>(nameof(StarCount), 320);

    public static readonly StyledProperty<bool> AnimationsEnabledProperty =
        AvaloniaProperty.Register<ParallaxStarBackground, bool>(nameof(AnimationsEnabled), true);

    public int StarCount
    {
        get => GetValue(StarCountProperty);
        set => SetValue(StarCountProperty, value);
    }

    public bool AnimationsEnabled
    {
        get => GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    public bool InteractionActive { get; set; }

    public ParallaxStarBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        // Pre-build wisp brushes once
        _wispBrushes = WispDefs.Select(w =>
        {
            var rgb = w.Rgb;
            var r = (byte)((rgb >> 16) & 0xff);
            var g = (byte)((rgb >>  8) & 0xff);
            var b = (byte)( rgb        & 0xff);
            var a = (byte)Math.Clamp(w.Alpha * 255, 0, 255);
            return new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));
        }).ToArray();

        StarCountProperty.Changed.AddClassHandler<ParallaxStarBackground>((o, _) => o.Build());
        AttachedToVisualTree += (_, _) =>
        {
            Build();
            _t0 = DateTime.UtcNow;
            AttachTopLevelPointerTracking();
            if (AnimationsEnabled)
            {
                _twinkleTick = new DispatcherTimer(TimeSpan.FromMilliseconds(80),
                    DispatcherPriority.Background,
                    (_, _) => InvalidateVisual());
                _twinkleTick.Start();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _easeTimer?.Stop();
            _twinkleTick?.Stop();
            DetachTopLevelPointerTracking();
        };
    }

    private static IReadOnlyDictionary<uint, ImmutableSolidColorBrush[]> BuildColorLuts()
    {
        var map = new Dictionary<uint, ImmutableSolidColorBrush[]>(StarPalette.Length);
        foreach (var c in StarPalette)
        {
            var rgb = ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            map[rgb] = BuildLut(c);
        }
        return map;
    }

    private static ImmutableSolidColorBrush[] BuildLut(Color c)
    {
        var lut = new ImmutableSolidColorBrush[AlphaLevels];
        for (var i = 0; i < AlphaLevels; i++)
        {
            var a = (byte)Math.Round(255.0 * (i + 1) / AlphaLevels);
            lut[i] = new ImmutableSolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }
        return lut;
    }

    private static IBrush GetBrush(uint rgb, double alpha)
    {
        if (alpha <= 0) return Brushes.Transparent;
        if (!ColorLuts.TryGetValue(rgb, out var lut))
            return Brushes.White;
        var idx = Math.Clamp((int)(alpha * AlphaLevels), 0, AlphaLevels - 1);
        return lut[idx];
    }

    public void UpdatePointer(double nx, double ny)
    {
        if (Math.Abs(_targetOffsetX - nx) < 0.0005 && Math.Abs(_targetOffsetY - ny) < 0.0005)
            return;
        _targetOffsetX = nx;
        _targetOffsetY = ny;
        EnsureEaseTimer();
    }

    private void EnsureEaseTimer()
    {
        if (_easeTimer is not null) return;
        _easeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) =>
        {
            _offsetX += (_targetOffsetX - _offsetX) * 0.18;
            _offsetY += (_targetOffsetY - _offsetY) * 0.18;
            InvalidateVisual();
            if (Math.Abs(_targetOffsetX - _offsetX) < 0.001 && Math.Abs(_targetOffsetY - _offsetY) < 0.001)
            {
                _easeTimer?.Stop();
                _easeTimer = null;
            }
        });
        _easeTimer.Start();
    }

    private void Build()
    {
        _starCount = Math.Max(0, StarCount);
        if (_starCount == 0) { _stars = Array.Empty<Star>(); InvalidateVisual(); return; }

        var rng = new Random(1337);
        // 50% far / 35% mid / 15% near
        var counts = new[] { (int)(_starCount * 0.50), (int)(_starCount * 0.35), (int)(_starCount * 0.15) };
        var total  = counts[0] + counts[1] + counts[2];
        _stars = new Star[total];
        var k = 0;
        for (var d = 0; d < 3; d++)
        {
            for (var i = 0; i < counts[d]; i++)
            {
                // ~5% of near-layer (d==2) stars become "giants"
                var isGiant = d == 2 && rng.NextDouble() < 0.05;
                double sz, op;
                if (isGiant)
                {
                    sz = 4.0 + rng.NextDouble() * 3.5;
                    op = 0.55 + rng.NextDouble() * 0.35;
                }
                else
                {
                    var bright = d == 2 ? rng.NextDouble() > 0.6 : rng.NextDouble() > 0.85;
                    sz = d == 0 ? 0.4  + rng.NextDouble()
                       : d == 1 ? 0.8  + rng.NextDouble() * 1.8
                       : bright ? 1.8  + rng.NextDouble() * 2.5 : 1.0 + rng.NextDouble() * 1.5;
                    op = d == 0 ? 0.08 + rng.NextDouble() * 0.16
                       : d == 1 ? 0.14 + rng.NextDouble() * 0.26
                       : bright ? 0.40 + rng.NextDouble() * 0.45 : 0.20 + rng.NextDouble() * 0.28;
                }

                // Colour: giants skew warm/white; deep stars skew blue/violet
                Color color;
                if (isGiant)
                {
                    var t = rng.NextDouble();
                    color = t < 0.45 ? StarPalette[0]   // white
                          : t < 0.70 ? StarPalette[4]   // gold
                          : t < 0.85 ? StarPalette[1]   // blue-white
                          :            StarPalette[3];   // violet
                }
                else
                {
                    var t = rng.NextDouble();
                    color = t < 0.32 ? StarPalette[0]   // white
                          : t < 0.50 ? StarPalette[1]   // blue-white
                          : t < 0.64 ? StarPalette[2]   // blue
                          : t < 0.74 ? StarPalette[3]   // violet
                          : t < 0.84 ? StarPalette[4]   // gold
                          : t < 0.93 ? StarPalette[5]   // cyan
                          :            StarPalette[6];  // rose
                }

                _stars[k++] = new Star
                {
                    Xp       = rng.NextDouble(),
                    Yp       = rng.NextDouble(),
                    Size     = sz,
                    Alpha    = op,
                    Rgb      = ((uint)color.R << 16) | ((uint)color.G << 8) | color.B,
                    Layer    = d,
                    Twinkle  = d == 0 ? rng.NextDouble() > 0.75 : rng.NextDouble() > 0.42,
                    TwDur    = isGiant ? 4 + rng.NextDouble() * 4 : 2.5 + rng.NextDouble() * 5,
                    TwPhase  = rng.NextDouble() * 8,
                    DriftX   = (rng.NextDouble() - 0.5) * (d == 2 ? 5.2 : d == 1 ? 2.8 : 1.2),
                    DriftY   = (rng.NextDouble() - 0.5) * (d == 2 ? 3.2 : d == 1 ? 1.8 : 0.8),
                    IsGiant  = isGiant,
                };
            }
        }
        InvalidateVisual();
    }

    private void AttachTopLevelPointerTracking()
    {
        DetachTopLevelPointerTracking();
        _trackedTopLevel = TopLevel.GetTopLevel(this);
        if (_trackedTopLevel is null) return;
        _trackedTopLevel.AddHandler(PointerMovedEvent, OnTopLevelPointerMoved,
            Avalonia.Interactivity.RoutingStrategies.Tunnel |
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void DetachTopLevelPointerTracking()
    {
        if (_trackedTopLevel is null) return;
        _trackedTopLevel.RemoveHandler(PointerMovedEvent, OnTopLevelPointerMoved);
        _trackedTopLevel = null;
    }

    private void OnTopLevelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsVisible) return;
        var p = e.GetPosition(this);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // Skip updates when pointer is outside the control bounds.
        if (p.X < 0 || p.Y < 0 || p.X > w || p.Y > h) return;
        UpdatePointer(p.X / w - 0.5, p.Y / h - 0.5);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var tSec = (DateTime.UtcNow - _t0).TotalSeconds;
        const double pad = 420;

        // ── 1. Nebula wisps (drawn first, behind stars) ──────────────────────
        for (var wi = 0; wi < WispDefs.Length; wi++)
        {
            ref readonly var wd = ref WispDefs[wi];
            var speed = LayerSpeed[wd.Layer];
            var wx = wd.Xp * w + _offsetX * speed * 0.4;
            var wy = wd.Yp * h + _offsetY * speed * 0.4;
            var rx = wd.RxFrac * w;
            var ry = wd.RyFrac * h;
            var brush = _wispBrushes[wi];
            // Three nested rings for a soft gaussian-like falloff
            ctx.DrawEllipse(brush, null, new Point(wx, wy), rx * 0.50, ry * 0.50);
            ctx.DrawEllipse(brush, null, new Point(wx, wy), rx * 0.80, ry * 0.80);
            ctx.DrawEllipse(brush, null, new Point(wx, wy), rx,        ry       );
        }

        // ── 2. Stars ─────────────────────────────────────────────────────────
        if (_stars.Length == 0) return;

        for (var i = 0; i < _stars.Length; i++)
        {
            ref var s = ref _stars[i];
            var speed  = LayerSpeed[s.Layer];
            var driftX = AnimationsEnabled ? s.DriftX * tSec : 0;
            var driftY = AnimationsEnabled ? s.DriftY * tSec : 0;
            var px = s.Xp * (w + pad * 2) - pad + _offsetX * speed + driftX;
            var py = s.Yp * (h + pad * 2) - pad + _offsetY * speed + driftY;

            // Twinkle
            var alpha = s.Alpha;
            if (s.Twinkle && AnimationsEnabled)
            {
                var phase = (tSec + s.TwPhase) / s.TwDur * 2 * Math.PI;
                var m = (Math.Cos(phase) + 1) * 0.5;
                // Giants twinkle more dramatically
                alpha *= s.IsGiant ? 0.20 + 0.80 * m : 0.35 + 0.65 * m;
            }

            if (alpha <= 0) continue;
            var rgb = s.Rgb;
            var brush = GetBrush(rgb, alpha);

            if (!InteractionActive && (s.IsGiant || s.Size > 2.8))
            {
                // ── Giant / very-bright star: 3-ring halo + diffraction spikes ──

                // Outer glow (faintest)
                var g3 = GetBrush(rgb, alpha * 0.06);
                ctx.DrawEllipse(g3, null, new Point(px, py), s.Size * 5.5, s.Size * 5.5);

                // Mid halo
                var g2 = GetBrush(rgb, alpha * 0.14);
                ctx.DrawEllipse(g2, null, new Point(px, py), s.Size * 3.0, s.Size * 3.0);

                // Inner halo
                var g1 = GetBrush(rgb, alpha * 0.30);
                ctx.DrawEllipse(g1, null, new Point(px, py), s.Size * 1.6, s.Size * 1.6);

                // Diffraction spikes — 4-point cross
                var spike = s.Size * 4.0;
                var iS = GetBrush(rgb, alpha * 0.32);
                var iLen = spike * 0.45;
                ctx.FillRectangle(iS, new Rect(px - iLen, py - 0.6, iLen * 2, 1.2));
                ctx.FillRectangle(iS, new Rect(px - 0.6, py - iLen, 1.2, iLen * 2));
                if (alpha > 0.04)
                {
                    var oS = GetBrush(rgb, alpha * 0.10);
                    ctx.FillRectangle(oS, new Rect(px - spike, py - 0.6, spike * 2, 1.2));
                    ctx.FillRectangle(oS, new Rect(px - 0.6, py - spike, 1.2, spike * 2));
                }

                ctx.DrawEllipse(brush, null, new Point(px, py), s.Size, s.Size);
            }
            else if (!InteractionActive && s.Size > 1.7)
            {
                // ── Medium-bright star: 2-ring halo ──────────────────────────
                var h2 = GetBrush(rgb, alpha * 0.10);
                ctx.DrawEllipse(h2, null, new Point(px, py), s.Size * 3.0, s.Size * 3.0);

                var h1 = GetBrush(rgb, alpha * 0.26);
                ctx.DrawEllipse(h1, null, new Point(px, py), s.Size * 1.8, s.Size * 1.8);

                ctx.DrawEllipse(brush, null, new Point(px, py), s.Size, s.Size);
            }
            else if (!InteractionActive && s.Size > 0.9)
            {
                // ── Small but visible star: subtle halo ───────────────────────
                var hb = GetBrush(rgb, alpha * 0.20);
                ctx.DrawEllipse(hb, null, new Point(px, py), s.Size * 2.2, s.Size * 2.2);
                ctx.DrawEllipse(brush, null, new Point(px, py), s.Size, s.Size);
            }
            else
            {
                // ── Interaction mode / tiny stars: cheapest draw path ─────────
                ctx.DrawEllipse(brush, null, new Point(px, py), s.Size, s.Size);
            }
        }
    }
}
