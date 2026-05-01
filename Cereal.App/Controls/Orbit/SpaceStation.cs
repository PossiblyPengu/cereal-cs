using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Cereal.App.Utilities;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Clickable orbital "space station" used for streaming platforms (PSN &amp; Xbox).
/// A 120px rotating outer ring with 4 blinking docks, an 80px counter-rotating
/// inner ring, N/S/E/W spokes, and a central hub with the platform icon/letter.
/// Mirrors the original `.space-station` DOM/CSS in index.css.
/// </summary>
public sealed class SpaceStation : Panel
{
    public event Action<SpaceStation>? Clicked;

    private readonly OrbitWorld _world;
    private readonly Border _hub;
    private readonly TextBlock _labelText;
    private readonly DispatcherTimer _floatTimer;
    private readonly DateTime _floatStart;

    public SpaceStation(OrbitWorld world, string platform, Color color, string label, int gameCount)
    {
        _world = world;
        Width = 160;
        Height = 160;
        Cursor = new Cursor(StandardCursorType.Hand);

        // ─── Outer glow (approx. of blur(50px)) ──────────────────────────────
        var glow = new Ellipse
        {
            Width = 200,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(color, 0),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.75),
                },
            },
            Opacity = 0.30,
            IsHitTestVisible = false,
        };
        Children.Add(glow);

        // ─── Spokes (horizontal + vertical, 100px, 1px) ─────────────────────
        var spokeColor = Color.FromArgb((byte)(255 * 0.08), color.R, color.G, color.B);
        var spokeBrush = new ImmutableSolidColorBrush(spokeColor);
        Children.Add(new Rectangle
        {
            Width = 1, Height = 100, Fill = spokeBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        });
        Children.Add(new Rectangle
        {
            Width = 100, Height = 1, Fill = spokeBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        });

        // ─── Outer ring (120px, rotates 60s) ─────────────────────────────────
        var outerRing = BuildRing(
            size: 120,
            strokeBrush: new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * 0.35), color.R, color.G, color.B)),
            strokeThickness: 1.5,
            rotationSeconds: 60,
            reverse: false,
            docks: true,
            dockColor: color);
        Children.Add(outerRing);

        // ─── Inner ring (80px, counter-rotates 40s) ──────────────────────────
        var innerRing = BuildRing(
            size: 80,
            strokeBrush: new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * 0.20), color.R, color.G, color.B)),
            strokeThickness: 1,
            rotationSeconds: 40,
            reverse: true,
            docks: false,
            dockColor: color);
        Children.Add(innerRing);

        // ─── Central hub ─────────────────────────────────────────────────────
        Control hubContent;
        if (PlatformLogos.TryGet(platform) is { } logo)
        {
            var iconPath = new Avalonia.Controls.Shapes.Path
            {
                Data = Avalonia.Media.Geometry.Parse(logo.PathData),
                Stretch = Stretch.Uniform,
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            if (logo.IsStroke)
            {
                iconPath.Stroke = new ImmutableSolidColorBrush(Color.FromArgb(0xee, 255, 255, 255));
                iconPath.StrokeThickness = logo.StrokeWidth;
                iconPath.StrokeLineCap = Avalonia.Media.PenLineCap.Round;
                iconPath.Fill = Brushes.Transparent;
            }
            else
            {
                iconPath.Fill = new ImmutableSolidColorBrush(Color.FromArgb(0xee, 255, 255, 255));
            }
            hubContent = iconPath;
        }
        else
        {
            var letter = label.Length > 0 ? char.ToUpperInvariant(label[0]).ToString() : "?";
            hubContent = new TextBlock
            {
                Text = letter,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new ImmutableSolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        _hub = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            BorderBrush = new ImmutableSolidColorBrush(color),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.35, 0.35, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.35, 0.35, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x1a, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0xe6, 10, 10, 24), 0.7),
                },
            },
            Effect = new DropShadowEffect
            {
                BlurRadius = 40,
                Color = color,
                Opacity = 1,
                OffsetX = 0,
                OffsetY = 0,
            },
            Child = hubContent,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = RelativePoint.Center,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(220),
                },
            },
        };
        Children.Add(_hub);

        // ─── Label below ─────────────────────────────────────────────────────
        var labelContent = gameCount > 0 ? $"{label}  {gameCount}" : label;
        _labelText = new TextBlock
        {
            Text = labelContent,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new ImmutableSolidColorBrush(Color.FromArgb(0xcc, 232, 228, 222)),
            Padding = new Thickness(10, 3),
            Background = new ImmutableSolidColorBrush(Color.FromArgb(0xd9, 7, 7, 13)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -10),
            Opacity = 0.5,
            IsHitTestVisible = false,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(120),
                },
            },
        };
        Children.Add(_labelText);

        // ─── Subtle float animation (6s, vertical bob) ───────────────────────
        _floatStart = DateTime.UtcNow;
        RenderTransform = new TranslateTransform(0, 0);
        _floatTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            if (RenderTransform is not TranslateTransform t) return;
            var secs = (DateTime.UtcNow - _floatStart).TotalSeconds;
            var y = -3.0 + Math.Sin(secs / 6.0 * Math.PI * 2) * 3.0; // [-6..0]
            t.Y = y;
        });
        _floatTimer.Start();
        DetachedFromVisualTree += (_, _) => _floatTimer.Stop();
    }

    private static Canvas BuildRing(double size, IBrush strokeBrush, double strokeThickness,
        double rotationSeconds, bool reverse, bool docks, Color dockColor)
    {
        var ring = new Canvas
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        var circle = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
        };
        ring.Children.Add(circle);

        if (docks)
        {
            var dockBrush = new ImmutableSolidColorBrush(dockColor);
            var positions = new (double X, double Y, double DelaySec)[]
            {
                (size / 2,  -2.5,      0.0),   // top
                (size / 2,  size - 2.5, 0.5),  // bottom
                (-2.5,      size / 2,   1.0),  // left
                (size - 2.5, size / 2,  1.5),  // right
            };
            foreach (var (x, y, delay) in positions)
            {
                var dock = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = dockBrush,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 6,
                        Color = dockColor,
                        Opacity = 1,
                        OffsetX = 0,
                        OffsetY = 0,
                    },
                    Opacity = 0.5,
                };
                Canvas.SetLeft(dock, x - 2.5);
                Canvas.SetTop(dock, y - 2.5);
                ring.Children.Add(dock);

                var blink = new Animation
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Delay = TimeSpan.FromSeconds(delay),
                    IterationCount = new IterationCount(ulong.MaxValue),
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0.25) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1.0) } },
                    },
                };
                _ = blink.RunAsync(dock);
            }
        }

        // Rotation around center.
        var rot = new RotateTransform(0);
        var spinStart = DateTime.UtcNow;
        var sign = reverse ? -1.0 : 1.0;
        ring.RenderTransform = rot;
        ring.RenderTransformOrigin = RelativePoint.Center;
        var spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - spinStart).TotalSeconds;
            rot.Angle = sign * (elapsed / rotationSeconds * 360.0);
        });
        spinTimer.Start();
        ring.DetachedFromVisualTree += (_, _) => spinTimer.Stop();

        return ring;
    }

    public void PlaceOn(Canvas canvas, double cx, double cy)
    {
        Canvas.SetLeft(this, cx - Width / 2);
        Canvas.SetTop(this, cy - Height / 2);
        canvas.Children.Add(this);
        this.ZIndex = 8;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_hub.RenderTransform is ScaleTransform s) { s.ScaleX = 1.1; s.ScaleY = 1.1; }
        _labelText.Opacity = 0.9;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hub.RenderTransform is ScaleTransform s) { s.ScaleX = 1; s.ScaleY = 1; }
        _labelText.Opacity = 0.5;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_world.DragMoved) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        Clicked?.Invoke(this);
        e.Handled = true;
    }
}
