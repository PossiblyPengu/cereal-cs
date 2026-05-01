using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Renders N background stars in a single Render pass for performance.
/// Large/bright stars get multi-ring halos and diffraction spikes.
/// </summary>
public class StarField : Control
{
    private struct Star
    {
        public float X;
        public float Y;
        public float Size;
        public float BaseAlpha;
        public uint  Rgb;
        public bool  Twinkles;
        public float TwinkleDuration;
        public float TwinklePhase;
        public float DriftX;
        public float DriftY;
        public float PulseAmount;
        public bool  IsGiant;     // gets spikes + 3-ring halo
    }

    private Star[] _stars = Array.Empty<Star>();
    private DispatcherTimer? _tick;
    private DateTime _t0;

    // ── Quantized brush LUTs (colour × 32 alpha steps) ─────────────────────
    private const int AlphaLevels = 32;

    // Richer palette: cooler blue, violet, warm gold added alongside existing warm/cyan/rose
    private static readonly ImmutableSolidColorBrush[] WhiteLut  = BuildLut(Colors.White);
    private static readonly ImmutableSolidColorBrush[] BlueLut   = BuildLut(Color.FromRgb(0xb0, 0xcc, 0xff)); // cool blue
    private static readonly ImmutableSolidColorBrush[] VioletLut = BuildLut(Color.FromRgb(0xc8, 0xb0, 0xff)); // violet
    private static readonly ImmutableSolidColorBrush[] GoldLut   = BuildLut(Color.FromRgb(0xff, 0xe0, 0x80)); // warm gold
    private static readonly ImmutableSolidColorBrush[] WarmLut   = BuildLut(Color.FromRgb(0xff, 0xee, 0xcf)); // warm white
    private static readonly ImmutableSolidColorBrush[] CyanLut   = BuildLut(Color.FromRgb(0xa4, 0xf0, 0xff)); // icy cyan
    private static readonly ImmutableSolidColorBrush[] RoseLut   = BuildLut(Color.FromRgb(0xff, 0xb8, 0xd4)); // soft rose

    // Packed RGB constants matching the LUT keys
    private const uint RgbWhite  = 0xffffffu;
    private const uint RgbBlue   = 0xb0ccffu;
    private const uint RgbViolet = 0xc8b0ffu;
    private const uint RgbGold   = 0xffe080u;
    private const uint RgbWarm   = 0xffeecfu;
    private const uint RgbCyan   = 0xa4f0ffu;
    private const uint RgbRose   = 0xffb8d4u;

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

    public static readonly StyledProperty<int> StarCountProperty =
        AvaloniaProperty.Register<StarField, int>(nameof(StarCount), 900);

    public static readonly StyledProperty<bool> AnimationsEnabledProperty =
        AvaloniaProperty.Register<StarField, bool>(nameof(AnimationsEnabled), true);

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

    static StarField()
    {
        AffectsRender<StarField>(StarCountProperty, AnimationsEnabledProperty);
    }

    public StarField()
    {
        IsHitTestVisible = false;
        Width  = OrbitWorld.WorldWidth;
        Height = OrbitWorld.WorldHeight;
        AttachedToVisualTree  += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StarCountProperty || change.Property == AnimationsEnabledProperty)
        {
            if (this.GetVisualRoot() is not null) Start();
        }
    }

    private void Start()
    {
        Stop();
        Build();
        _t0 = DateTime.UtcNow;
        if (AnimationsEnabled && HasTwinklers())
        {
            _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
                (_, _) => InvalidateVisual());
            _tick.Start();
        }
        InvalidateVisual();
    }

    private void Stop()
    {
        _tick?.Stop();
        _tick = null;
    }

    private bool HasTwinklers()
    {
        foreach (var s in _stars) if (s.Twinkles) return true;
        return false;
    }

    private void Build()
    {
        var rng   = new Random(1337);
        var count = Math.Max(0, StarCount);
        var fieldW = Math.Max(1, GetFieldWidth());
        var fieldH = Math.Max(1, GetFieldHeight());
        _stars    = new Star[count];

        for (var i = 0; i < count; i++)
        {
            // ~2% of stars are "giants"
            var isGiant = rng.NextDouble() < 0.02;

            // Size classification
            var starClass = rng.NextDouble();
            var isTiny    = !isGiant && starClass < 0.18;
            var isLarge   = !isGiant && !isTiny && starClass > 0.84;

            float size, baseAlpha;
            if (isGiant)
            {
                size      = (float)(3.2 + rng.NextDouble() * 2.8);
                baseAlpha = (float)(0.60 + rng.NextDouble() * 0.35);
            }
            else if (isTiny)
            {
                size      = (float)(0.65 + rng.NextDouble() * 0.30);
                baseAlpha = (float)(0.10 + rng.NextDouble() * 0.16);
            }
            else if (isLarge)
            {
                size      = (float)(1.9  + rng.NextDouble() * 1.55);
                baseAlpha = (float)(0.40 + rng.NextDouble() * 0.45);
            }
            else
            {
                size      = (float)(0.90 + rng.NextDouble() * 0.50);
                baseAlpha = (float)(0.16 + rng.NextDouble() * 0.48);
            }

            // Colour distribution:
            // 32% white · 20% blue · 12% violet · 10% gold · 12% warm · 8% cyan · 6% rose
            uint rgb;
            if (isGiant)
            {
                var t = rng.NextDouble();
                rgb = t < 0.40 ? RgbWhite : t < 0.60 ? RgbGold : t < 0.78 ? RgbBlue : RgbViolet;
            }
            else
            {
                var t = rng.NextDouble();
                rgb = t < 0.32 ? RgbWhite
                    : t < 0.52 ? RgbBlue
                    : t < 0.64 ? RgbViolet
                    : t < 0.74 ? RgbGold
                    : t < 0.86 ? RgbWarm
                    : t < 0.94 ? RgbCyan
                    :            RgbRose;
            }

            // Drift: giants are stationary anchors
            var driftDir   = rng.NextDouble() * Math.PI * 2;
            var driftSpeed = (isGiant || isLarge) ? 0f : (float)(0.6 + rng.NextDouble() * 2.2);

            _stars[i] = new Star
            {
                X               = (float)(rng.NextDouble() * fieldW),
                Y               = (float)(rng.NextDouble() * fieldH),
                Size            = size,
                BaseAlpha       = baseAlpha,
                Rgb             = rgb,
                Twinkles        = AnimationsEnabled && (isGiant ? rng.NextDouble() < 0.80
                                                      : isLarge ? rng.NextDouble() < 0.32
                                                      :           rng.NextDouble() < 0.12),
                TwinkleDuration = isGiant ? (float)(3.5 + rng.NextDouble() * 4.0)
                                          : (float)(2.5 + rng.NextDouble() * 4.5),
                TwinklePhase    = (float)rng.NextDouble(),
                DriftX          = (float)(Math.Cos(driftDir) * driftSpeed),
                DriftY          = (float)(Math.Sin(driftDir) * driftSpeed),
                PulseAmount     = isTiny ? (float)(0.04 + rng.NextDouble() * 0.06)
                                         : (float)(0.02 + rng.NextDouble() * 0.04),
                IsGiant         = isGiant,
            };
        }
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (_stars.Length == 0) return;

        var elapsed = (float)(DateTime.UtcNow - _t0).TotalSeconds;

        for (var i = 0; i < _stars.Length; i++)
        {
            ref var s = ref _stars[i];

            // ── Alpha (twinkle / pulse) ────────────────────────────────────
            double alpha = s.BaseAlpha;
            if (s.Twinkles)
            {
                var p    = ((elapsed / s.TwinkleDuration) + s.TwinklePhase) % 1.0;
                var wave = 0.5 + 0.5 * Math.Cos(p * Math.PI * 2);
                // Giants pulse between ~15% and 100% for dramatic shimmer
                alpha = s.IsGiant ? 0.15 + 0.85 * wave : 0.20 + 0.70 * wave;
            }
            else if (AnimationsEnabled && s.PulseAmount > 0)
            {
                var pulse = 0.5 + 0.5 * Math.Sin((elapsed / 5.5 + s.TwinklePhase) * Math.PI * 2);
                alpha *= 1.0 - s.PulseAmount + s.PulseAmount * pulse;
            }

            // ── Position ────────────────────────────────────────────────────
            var x = s.X;
            var y = s.Y;
            if (AnimationsEnabled && (Math.Abs(s.DriftX) > 0.001f || Math.Abs(s.DriftY) > 0.001f))
            {
                x = Wrap(x + s.DriftX * elapsed, GetFieldWidth());
                y = Wrap(y + s.DriftY * elapsed, GetFieldHeight());
            }

            // ── LUT lookup ──────────────────────────────────────────────────
            var lut = s.Rgb switch
            {
                RgbBlue   => BlueLut,
                RgbViolet => VioletLut,
                RgbGold   => GoldLut,
                RgbWarm   => WarmLut,
                RgbCyan   => CyanLut,
                RgbRose   => RoseLut,
                _         => WhiteLut,
            };
            var idx   = Math.Clamp((int)(alpha * AlphaLevels), 0, AlphaLevels - 1);
            var brush = lut[idx];

            // ── Render ──────────────────────────────────────────────────────
            if (s.IsGiant || s.Size > 2.5f)
            {
                // Giant / very bright: outer glow + mid halo + inner halo + spikes + core

                var og = Math.Clamp((int)(alpha * 0.05 * AlphaLevels), 0, AlphaLevels - 1);
                if (og > 0) ctx.DrawEllipse(lut[og], null, new Point(x, y), s.Size * 6.0, s.Size * 6.0);

                var mh = Math.Clamp((int)(alpha * 0.12 * AlphaLevels), 0, AlphaLevels - 1);
                if (mh > 0) ctx.DrawEllipse(lut[mh], null, new Point(x, y), s.Size * 3.2, s.Size * 3.2);

                var ih = Math.Clamp((int)(alpha * 0.28 * AlphaLevels), 0, AlphaLevels - 1);
                if (ih > 0) ctx.DrawEllipse(lut[ih], null, new Point(x, y), s.Size * 1.7, s.Size * 1.7);

                // Diffraction spikes
                var spike = s.Size * 4.2;
                var iSa   = Math.Clamp((int)(alpha * 0.30 * AlphaLevels), 0, AlphaLevels - 1);
                if (iSa > 0)
                {
                    var ib  = lut[iSa];
                    var len = spike * 0.45;
                    ctx.FillRectangle(ib, new Rect(x - len, y - 0.7, len * 2, 1.4));
                    ctx.FillRectangle(ib, new Rect(x - 0.7, y - len, 1.4, len * 2));
                }
                var oSa = Math.Clamp((int)(alpha * 0.08 * AlphaLevels), 0, AlphaLevels - 1);
                if (oSa > 0)
                {
                    var ob = lut[oSa];
                    ctx.FillRectangle(ob, new Rect(x - spike, y - 0.5, spike * 2, 1.0));
                    ctx.FillRectangle(ob, new Rect(x - 0.5,  y - spike, 1.0, spike * 2));
                }

                ctx.DrawEllipse(brush, null, new Point(x, y), s.Size, s.Size);
            }
            else if (s.Size > 1.5f)
            {
                // Medium-bright: 2-ring halo
                var h2 = Math.Clamp((int)(alpha * 0.10 * AlphaLevels), 0, AlphaLevels - 1);
                if (h2 > 0) ctx.DrawEllipse(lut[h2], null, new Point(x, y), s.Size * 3.0, s.Size * 3.0);

                var h1 = Math.Clamp((int)(alpha * 0.22 * AlphaLevels), 0, AlphaLevels - 1);
                if (h1 > 0) ctx.DrawEllipse(lut[h1], null, new Point(x, y), s.Size * 1.8, s.Size * 1.8);

                ctx.DrawEllipse(brush, null, new Point(x, y), s.Size, s.Size);
            }
            else if (s.Size > 1.01f)
            {
                // Small-medium: subtle single halo
                var ha = Math.Clamp((int)(alpha * 0.18 * AlphaLevels), 0, AlphaLevels - 1);
                if (ha > 0) ctx.DrawEllipse(lut[ha], null, new Point(x, y), s.Size * 2.1, s.Size * 2.1);
                ctx.DrawEllipse(brush, null, new Point(x, y), s.Size, s.Size);
            }
            else
            {
                // Tiny: single pixel rect (fastest path)
                ctx.FillRectangle(brush, new Rect(x, y, 1, 1));
            }
        }
    }

    private static float Wrap(double value, double max)
    {
        if (max <= 0) return 0;
        var v = value % max;
        if (v < 0) v += max;
        return (float)v;
    }

    private double GetFieldWidth()
    {
        var w = Width;
        return double.IsNaN(w) || w <= 0 ? OrbitWorld.WorldWidth : w;
    }

    private double GetFieldHeight()
    {
        var h = Height;
        return double.IsNaN(h) || h <= 0 ? OrbitWorld.WorldHeight : h;
    }
}
