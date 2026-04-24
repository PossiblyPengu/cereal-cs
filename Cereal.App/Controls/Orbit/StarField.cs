using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Renders N background stars in a single Render pass for performance.
/// ~15% of the stars animate a sine-wave twinkle (matching the CSS
/// `@keyframes twinkle` from the original OrbitView WebView).
/// </summary>
public class StarField : Control
{
    private struct Star
    {
        public float X;
        public float Y;
        public float Size;
        public float BaseAlpha;
        public uint Rgb;           // packed 0xRRGGBB (no alpha)
        public bool Twinkles;
        public float TwinkleDuration;   // seconds
        public float TwinklePhase;      // 0..1
    }

    private Star[] _stars = Array.Empty<Star>();
    private DispatcherTimer? _tick;
    private DateTime _t0;

    // Quantized brush LUT: 3 hues × 32 alpha levels. Avoids per-star allocation
    // on the render path and avoids the "shared mutable brush" pitfall where
    // assigning .Opacity would retro-actively affect already-queued draws.
    private const int AlphaLevels = 32;
    private static readonly ImmutableSolidColorBrush[] WhiteLut = BuildLut(Colors.White);
    private static readonly ImmutableSolidColorBrush[] BlueLut = BuildLut(Color.FromRgb(0xaa, 0xaa, 0xff));
    private static readonly ImmutableSolidColorBrush[] WarmLut = BuildLut(Color.FromRgb(0xff, 0xee, 0xcf));

    private static ImmutableSolidColorBrush[] BuildLut(Color baseColor)
    {
        var lut = new ImmutableSolidColorBrush[AlphaLevels];
        for (var i = 0; i < AlphaLevels; i++)
        {
            var a = (byte)Math.Round(255.0 * (i + 1) / AlphaLevels);
            lut[i] = new ImmutableSolidColorBrush(Color.FromArgb(a, baseColor.R, baseColor.G, baseColor.B));
        }
        return lut;
    }

    public static readonly StyledProperty<int> StarCountProperty =
        AvaloniaProperty.Register<StarField, int>(nameof(StarCount), 800);

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
        Width = OrbitWorld.WorldWidth;
        Height = OrbitWorld.WorldHeight;
        AttachedToVisualTree += (_, _) => Start();
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
            _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
            {
                InvalidateVisual();
            });
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
        var rng = new Random(1337); // stable seed → same sky every session
        var count = Math.Max(0, StarCount);
        _stars = new Star[count];
        for (var i = 0; i < count; i++)
        {
            var tone = rng.NextDouble();
            var rgb = tone < 0.62 ? 0xffffffu : tone < 0.88 ? 0xaaaaffu : 0xffeecfu;
            var isLarge = rng.NextDouble() < 0.12;
            _stars[i] = new Star
            {
                X = (float)(rng.NextDouble() * OrbitWorld.WorldWidth),
                Y = (float)(rng.NextDouble() * OrbitWorld.WorldHeight),
                Size = isLarge ? (float)(1.7 + rng.NextDouble() * 1.2) : 1f,
                BaseAlpha = isLarge
                    ? (float)(0.45 + rng.NextDouble() * 0.42)
                    : (float)(0.18 + rng.NextDouble() * 0.48),
                Rgb = rgb,
                Twinkles = AnimationsEnabled && (isLarge ? rng.NextDouble() < 0.30 : rng.NextDouble() < 0.14),
                TwinkleDuration = (float)(3.0 + rng.NextDouble() * 4.0),
                TwinklePhase = (float)rng.NextDouble(),
            };
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_stars.Length == 0) return;

        var elapsed = (float)(DateTime.UtcNow - _t0).TotalSeconds;

        for (var i = 0; i < _stars.Length; i++)
        {
            ref var s = ref _stars[i];

            // Twinkle: opacity oscillates between 0.7 (base) and 0.2 (min) per the
            // original CSS @keyframes. Cos wave gives smooth ease-in/out.
            double alpha = s.BaseAlpha;
            if (s.Twinkles)
            {
                var p = ((elapsed / s.TwinkleDuration) + s.TwinklePhase) % 1.0;
                var wave = 0.5 + 0.5 * Math.Cos(p * Math.PI * 2);
                alpha = 0.2 + (0.7 - 0.2) * wave;
            }

            var lut = s.Rgb switch
            {
                0xaaaaffu => BlueLut,
                0xffeecfu => WarmLut,
                _ => WhiteLut,
            };
            var idx = Math.Clamp((int)(alpha * AlphaLevels), 0, AlphaLevels - 1);
            var brush = lut[idx];

            if (s.Size <= 1.01f)
                context.FillRectangle(brush, new Rect(s.X, s.Y, 1, 1));
            else
            {
                // Give larger stars a faint halo to avoid hard-edged dots.
                var haloAlpha = Math.Clamp(alpha * 0.18, 0.04, 0.22);
                var haloIdx = Math.Clamp((int)(haloAlpha * AlphaLevels), 0, AlphaLevels - 1);
                var haloBrush = lut[haloIdx];
                var haloR = s.Size * 2.1;
                context.DrawEllipse(haloBrush, null, new Point(s.X, s.Y), haloR, haloR);
                context.DrawEllipse(brush, null, new Point(s.X, s.Y), s.Size, s.Size);
            }
        }
    }
}
