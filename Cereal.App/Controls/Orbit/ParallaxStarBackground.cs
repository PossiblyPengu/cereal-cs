using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Three fullscreen layers of background stars that translate slightly with
/// the pointer position to create a parallax effect. Mirrors the
/// <c>.parallax-layer.depth-{0,1,2}</c> divs rendered inside <c>.void-layer</c>
/// in the original React app (and the <c>useParallax</c> hook).
/// </summary>
public sealed class ParallaxStarBackground : Control
{
    // Offset speeds (pixels at the extremes) per layer, mirroring
    // `PARALLAX_SPEEDS = [10, 30, 60]` in useParallax.ts.
    private static readonly double[] LayerSpeed = { 10, 30, 60 };
    private static readonly Color[] StarPalette =
    {
        Color.FromRgb(0xff, 0xff, 0xff), // neutral white
        Color.FromRgb(0xea, 0xee, 0xff), // cool white
        Color.FromRgb(0xff, 0xf3, 0xda), // warm white
    };

    private struct Star
    {
        public double Xp;       // 0..1, relative to viewport
        public double Yp;
        public double Size;
        public double Alpha;
        public uint Rgb;        // packed 0xRRGGBB
        public int Layer;       // 0..2
        public bool Twinkle;
        public double TwDur;    // seconds
        public double TwPhase;  // seconds
    }

    private Star[] _stars = Array.Empty<Star>();
    private double _offsetX;
    private double _offsetY;
    private double _targetOffsetX;
    private double _targetOffsetY;
    private DispatcherTimer? _easeTimer;
    private DispatcherTimer? _twinkleTick;
    private DateTime _t0;
    private int _starCount;

    public static readonly StyledProperty<int> StarCountProperty =
        AvaloniaProperty.Register<ParallaxStarBackground, int>(nameof(StarCount), 280);

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

    public ParallaxStarBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        StarCountProperty.Changed.AddClassHandler<ParallaxStarBackground>((o, _) => o.Build());
        AttachedToVisualTree += (_, _) =>
        {
            Build();
            _t0 = DateTime.UtcNow;
            if (AnimationsEnabled)
            {
                _twinkleTick = new DispatcherTimer(TimeSpan.FromMilliseconds(100),
                    DispatcherPriority.Background,
                    (_, _) => InvalidateVisual());
                _twinkleTick.Start();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _easeTimer?.Stop();
            _twinkleTick?.Stop();
        };
    }

    /// <summary>Called by the parent when the pointer moves, in viewport-normalized coordinates (−0.5..+0.5).</summary>
    public void UpdatePointer(double nx, double ny)
    {
        _targetOffsetX = nx;
        _targetOffsetY = ny;
        EnsureEaseTimer();
    }

    private void EnsureEaseTimer()
    {
        if (_easeTimer is not null) return;
        _easeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) =>
        {
            _offsetX += (_targetOffsetX - _offsetX) * 0.12;
            _offsetY += (_targetOffsetY - _offsetY) * 0.12;
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
        // Distribution across layers: 50% / 35% / 15% (matches original app).
        var counts = new[]
        {
            (int)(_starCount * 0.50),
            (int)(_starCount * 0.35),
            (int)(_starCount * 0.15),
        };
        var total = counts[0] + counts[1] + counts[2];
        _stars = new Star[total];
        var k = 0;
        for (var d = 0; d < 3; d++)
        {
            for (var i = 0; i < counts[d]; i++)
            {
                double sz;
                double op;
                bool bright = d == 2 ? rng.NextDouble() > 0.6 : rng.NextDouble() > 0.85;
                sz = d == 0 ? 0.4 + rng.NextDouble()
                   : d == 1 ? 0.8 + rng.NextDouble() * 1.8
                   : bright ? 1.8 + rng.NextDouble() * 2.5 : 1 + rng.NextDouble() * 1.5;
                op = d == 0 ? 0.03 + rng.NextDouble() * 0.10
                   : d == 1 ? 0.08 + rng.NextDouble() * 0.20
                   : bright ? 0.25 + rng.NextDouble() * 0.40 : 0.10 + rng.NextDouble() * 0.20;
                var color = StarPalette[rng.Next(StarPalette.Length)];
                _stars[k++] = new Star
                {
                    Xp = rng.NextDouble(),
                    Yp = rng.NextDouble(),
                    Size = sz,
                    Alpha = op,
                    Rgb = ((uint)color.R << 16) | ((uint)color.G << 8) | color.B,
                    Layer = d,
                    Twinkle = d == 0 ? rng.NextDouble() > 0.7 : rng.NextDouble() > 0.4,
                    TwDur = 3 + rng.NextDouble() * 6,
                    TwPhase = rng.NextDouble() * 8,
                };
            }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0 || _stars.Length == 0) return;

        var tSec = (DateTime.UtcNow - _t0).TotalSeconds;
        // Expand render area slightly so parallax offset doesn't reveal edges.
        const double pad = 140;

        for (var i = 0; i < _stars.Length; i++)
        {
            ref var s = ref _stars[i];
            var speed = LayerSpeed[s.Layer];
            var px = s.Xp * (w + pad * 2) - pad + _offsetX * speed;
            var py = s.Yp * (h + pad * 2) - pad + _offsetY * speed;

            var alpha = s.Alpha;
            if (s.Twinkle && AnimationsEnabled)
            {
                // 0..1 cosine wave. Drops to ~35% of base alpha at the dim end.
                var phase = (tSec + s.TwPhase) / s.TwDur * 2 * Math.PI;
                var m = (Math.Cos(phase) + 1) * 0.5;          // 0..1
                alpha *= 0.35 + 0.65 * m;
            }

            var a = (byte)Math.Clamp(alpha * 255, 0, 255);
            var rgb = s.Rgb;
            var r = (byte)((rgb >> 16) & 0xff);
            var g = (byte)((rgb >> 8) & 0xff);
            var b = (byte)(rgb & 0xff);
            var brush = new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));
            if (s.Size > 1.7)
            {
                var haloA = (byte)Math.Clamp(a * 0.25, 8, 70);
                var halo = new ImmutableSolidColorBrush(Color.FromArgb(haloA, r, g, b));
                var haloSize = s.Size * 2.2;
                ctx.DrawEllipse(halo, null, new Point(px, py), haloSize, haloSize);
            }
            ctx.DrawEllipse(brush, null, new Point(px, py), s.Size, s.Size);
        }
    }
}
