using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media.Transformation;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Spawns occasional streaking "shooting stars" on the world canvas at random
/// intervals. Ports the original JS' <c>scheduleShootingStar()</c> timer
/// (4–12 s delay) + <c>spawnShootingStar()</c> renderer.
/// </summary>
internal sealed class ShootingStarScheduler
{
    private readonly Canvas _world;
    private readonly Random _rng = new();
    private DispatcherTimer? _timer;

    public ShootingStarScheduler(Canvas world) { _world = world; }

    public void Start()
    {
        Stop();
        ScheduleNext();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void ScheduleNext()
    {
        var delay = TimeSpan.FromMilliseconds(4000 + _rng.NextDouble() * 8000);
        _timer = new DispatcherTimer(delay, DispatcherPriority.Background, (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            Spawn();
            ScheduleNext();
        });
        _timer.Start();
    }

    private void Spawn()
    {
        var len = 80 + _rng.NextDouble() * 120;
        var angleDeg = -15 + _rng.NextDouble() * 30;
        var x = _rng.NextDouble() * OrbitWorld.WorldWidth;
        var y = _rng.NextDouble() * OrbitWorld.WorldHeight;
        var durMs = 600 + _rng.NextDouble() * 500;

        // The streak is a thin gradient-filled rectangle, rotated and translated
        // along its own X axis. We use a TransformOperations-based RenderTransform
        // so the animation can cleanly interpolate both rotation and translation
        // in a single property.
        var streak = new Rectangle
        {
            Width = len,
            Height = 1,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0xcc, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                },
            },
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        };
        Canvas.SetLeft(streak, x);
        Canvas.SetTop(streak, y);

        streak.RenderTransform =
            TransformOperations.Parse($"rotate({angleDeg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}deg) translate(0px)");
        _world.Children.Add(streak);

        // 3-stop keyframe animation: opacity 0→1→0, translate 0→0.3L→L.
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durMs),
            Easing = new QuadraticEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(Visual.RenderTransformProperty,
                            TransformOperations.Parse($"rotate({angleDeg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}deg) translate(0px)")),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(0.3d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(Visual.RenderTransformProperty,
                            TransformOperations.Parse($"rotate({angleDeg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}deg) translate({(len * 0.3).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px)")),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(Visual.RenderTransformProperty,
                            TransformOperations.Parse($"rotate({angleDeg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}deg) translate({len.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px)")),
                    },
                },
            },
        };

        _ = animation.RunAsync(streak).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() => _world.Children.Remove(streak)));
    }
}
