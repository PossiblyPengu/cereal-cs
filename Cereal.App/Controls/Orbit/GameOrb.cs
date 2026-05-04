using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// A single clickable game orb on the orbit view. Positions itself absolutely
/// on the parent world canvas, loads a cover (or letter fallback), animates
/// scale/glow on hover, shows a tooltip, and forwards select/launch events.
/// </summary>
public class GameOrb : Border
{
    private readonly string _gameId;
    private readonly OrbitWorld _world;
    private readonly TextBlock _nameLabel;
    private readonly Color _accent;
    private readonly double _size;

    // Drift (the slow floating motion applied in addition to the home position).
    private readonly double _driftX;           // max horizontal amplitude
    private readonly double _driftY;           // max vertical amplitude
    private readonly double _driftPeriodSec;   // full cycle time
    private readonly double _driftPhaseSec;    // per-orb phase offset
    private readonly bool _driftEnabled;
    private double _homeCx;
    private double _homeCy;
    private DispatcherTimer? _driftTimer;
    private DateTime _driftStart;

    public event Action<string>? SelectRequested;
    public event Action<string>? LaunchRequested;

    public GameOrb(
        OrbitWorld world,
        string gameId,
        string gameName,
        string platformLabel,
        int playtimeMinutes,
        Color accentColor,
        string? coverPath,
        bool animations = true)
    {
        _world = world;
        _gameId = gameId;
        _accent = accentColor;
        _size = 44 + Math.Min(playtimeMinutes / 300.0, 20);

        // Deterministic per-game drift so the galaxy looks alive but doesn't
        // re-randomize on every rebuild. Mirrors the original placeOrb() which
        // derived drift from a hash of the game id.
        var seed = StableHash(string.IsNullOrEmpty(gameId) ? gameName ?? "" : gameId);
        _driftX = (((seed >> 4) & 0xff) / 255.0 - 0.5) * 10.0;   // ±5 px
        _driftY = (((seed >> 12) & 0xff) / 255.0 - 0.5) * 10.0;  // ±5 px
        _driftPeriodSec = 18 + ((seed >> 20) & 0x3f) / 63.0 * 10.0; // 18–28 s
        _driftPhaseSec = (((seed >> 8) & 0xff) / 255.0) * _driftPeriodSec;
        _driftEnabled = animations;

        Width = _size;
        Height = _size;
        CornerRadius = new CornerRadius(_size / 2);
        BorderThickness = new Thickness(2);
        BorderBrush = Brushes.Transparent;
        Background = new ImmutableSolidColorBrush(Color.Parse("#1a1a3a"));
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        IsHitTestVisible = true;

        // Scale origin = center; we'll lerp between scale(1) and scale(1.35) on hover.
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = TransformOperations.Parse("scale(1)");

        // Transitions (200ms — matches the original CSS `transition: transform .2s,
        // box-shadow .2s, border-color .2s`).
        Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(200),
            },
            new BrushTransition
            {
                Property = BorderBrushProperty,
                Duration = TimeSpan.FromMilliseconds(200),
            },
            new BoxShadowsTransition
            {
                Property = BoxShadowProperty,
                Duration = TimeSpan.FromMilliseconds(200),
            },
        };

        // Content: cover image if available, else fallback initial.
        if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
        {
            try
            {
                var bmp = new Bitmap(coverPath);
                Child = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
            }
            catch
            {
                Child = BuildFallback(gameName ?? string.Empty);
            }
        }
        else
        {
            Child = BuildFallback(gameName ?? string.Empty);
        }

        // Name label (sibling on the world canvas — positioned below the orb
        // by the caller after we're added).
        _nameLabel = new TextBlock
        {
            Text = gameName,
            FontSize = 11,
            Foreground = new ImmutableSolidColorBrush(Color.FromArgb(0xb3, 255, 255, 255)),
            MaxWidth = 120,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0,
            Effect = new DropShadowEffect
            {
                OffsetX = 0, OffsetY = 1, BlurRadius = 4,
                Color = Colors.Black, Opacity = 0.9,
            },
        };
        _nameLabel.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(200),
            },
        };

        // Tooltip content (e.g. "Portal 2 · Steam · 12h").
        var hoursSuffix = playtimeMinutes > 0 ? $" · {Math.Round(playtimeMinutes / 60.0)}h" : "";
        ToolTip.SetTip(this, $"{gameName} · {platformLabel}{hoursSuffix}");

        this.ZIndex = 10;

        DoubleTapped += (_, e) =>
        {
            LaunchRequested?.Invoke(_gameId);
            e.Handled = true;
        };
    }

    /// <summary>Adds the orb + its name-label to the world canvas at (cx, cy).</summary>
    public void PlaceOn(Canvas canvas, double cx, double cy)
    {
        _homeCx = cx;
        _homeCy = cy;
        Canvas.SetLeft(this, cx - _size / 2);
        Canvas.SetTop(this, cy - _size / 2);
        canvas.Children.Add(this);

        Canvas.SetLeft(_nameLabel, cx - 60);
        // Position below the orb with a small gap (matches `bottom:-24px` in CSS).
        Canvas.SetTop(_nameLabel, cy + _size / 2 + 6);
        _nameLabel.ZIndex = 11;
        _nameLabel.Width = 120;
        canvas.Children.Add(_nameLabel);

        if (_driftEnabled && (_driftX != 0 || _driftY != 0))
            StartDrift();
    }

    // Slow ease-in-out-alternate drift, ported from `@keyframes orbDrift` in
    // index.css. Updates `Canvas.Left/Top` so we don't clobber the hover scale
    // on `RenderTransform`. 30 FPS is more than enough for this subtle motion.
    private void StartDrift()
    {
        _driftStart = DateTime.UtcNow;
        _driftTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnDriftTick);
        _driftTimer.Start();
        DetachedFromVisualTree += (_, _) => { _driftTimer?.Stop(); _driftTimer = null; };
    }

    private void OnDriftTick(object? sender, EventArgs e)
    {
        var tSec = (DateTime.UtcNow - _driftStart).TotalSeconds + _driftPhaseSec;
        // Sine-wave from -1 to 1. Period = _driftPeriodSec.
        var s = Math.Sin(tSec / _driftPeriodSec * 2 * Math.PI);
        var offX = _driftX * s;
        var offY = _driftY * s;
        Canvas.SetLeft(this, _homeCx - _size / 2 + offX);
        Canvas.SetTop(this, _homeCy - _size / 2 + offY);
        Canvas.SetLeft(_nameLabel, _homeCx - 60 + offX);
        Canvas.SetTop(_nameLabel, _homeCy + _size / 2 + 6 + offY);
    }

    private static int StableHash(string s)
    {
        var h = 2166136261u; // FNV-1a for determinism across runs
        for (var i = 0; i < s.Length; i++)
        {
            h ^= s[i];
            h *= 16777619u;
        }
        return (int)h;
    }

    private TextBlock BuildFallback(string name)
    {
        var initial = string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString();
        return new TextBlock
        {
            Text = initial,
            FontWeight = FontWeight.Bold,
            FontSize = Math.Round(_size * 0.38),
            Foreground = new ImmutableSolidColorBrush(Color.FromArgb(0xb3, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ─── Hover / click ──────────────────────────────────────────────────────

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        RenderTransform = TransformOperations.Parse("scale(1.35)");
        BorderBrush = new ImmutableSolidColorBrush(_accent);
        BoxShadow = BoxShadows.Parse(
            $"0 0 20 0 {ToHex(_accent)}, 0 8 30 0 #80000000");
        this.ZIndex = 100;
        _nameLabel.Opacity = 1;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        RenderTransform = TransformOperations.Parse("scale(1)");
        BorderBrush = Brushes.Transparent;
        BoxShadow = default;
        this.ZIndex = 10;
        _nameLabel.Opacity = 0;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // Suppress "click" if the user was dragging the world camera.
        if (_world.DragMoved) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        SelectRequested?.Invoke(_gameId);
        e.Handled = true;
    }

    private static string ToHex(Color c) =>
        $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public string GameId => _gameId;

    /// <summary>
    /// Swaps the orb's child to a loaded cover image if it is still showing the fallback letter.
    /// Safe to call on the UI thread after a cover file has been downloaded.
    /// </summary>
    public void UpdateCover(string path)
    {
        if (Child is Image) return; // already showing a real image
        if (!File.Exists(path)) return;
        try
        {
            var bmp = new Bitmap(path);
            Child = new Image
            {
                Source = bmp,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
        }
        catch { /* leave fallback intact */ }
    }
}
