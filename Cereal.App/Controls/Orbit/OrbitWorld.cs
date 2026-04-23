using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Cereal.App.Controls.Orbit;

/// <summary>
/// Pannable / zoomable host that displays a fixed-size world canvas.
/// Replaces the WebView + CSS transform-based camera in the old OrbitView.
///
/// Mirrors the original implementation (src/components/OrbitView's inline JS —
/// see Cereal.App/Views/OrbitView.axaml.cs pre-port):
///  - drag to pan
///  - wheel to zoom (zoom-to-cursor)
///  - double-click to fit-all
///  - FlyTo() with 600ms cubic-bezier for centered focus on a point.
/// </summary>
public class OrbitWorld : Border
{
    public const double WorldWidth = 3000;
    public const double WorldHeight = 2000;

    private const double MinZoom = 0.15;
    private const double MaxZoom = 4.0;
    private const double DragThresholdPx = 4;

    private readonly Canvas _world;
    private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly TranslateTransform _translate = new() { X = 0, Y = 0 };

    // Camera state. These are the authoritative values; _scale / _translate
    // are rebuilt from them in ApplyTransform().
    private double _camX;
    private double _camY;
    private double _camZoom = 1.0;

    // Drag state
    private Point? _dragStart;              // pointer position at mousedown (screen space)
    private Point _dragCamStart;            // camera offset at mousedown
    private bool _dragMoved;

    private DispatcherTimer? _flyTimer;

    public OrbitWorld()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.Parse("#080818"));
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);

        _world = new Canvas
        {
            Width = WorldWidth,
            Height = WorldHeight,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
            // Order matters: scale first (from 0,0), then translate.
            RenderTransform = new TransformGroup
            {
                Children = { _scale, _translate }
            },
            // An explicit, transparent background ensures drag works across the
            // whole world — otherwise empty areas won't hit-test.
            Background = Brushes.Transparent,
        };

        Child = _world;

        // Re-fit once we actually have a viewport size.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(FitAll, DispatcherPriority.Background);
        SizeChanged += (_, _) =>
        {
            // If the user hasn't panned/zoomed yet, stay fit; otherwise leave the
            // user's camera alone on resize to match the original behavior.
            if (_camX == 0 && _camY == 0 && _camZoom == 1.0) FitAll();
        };
    }

    /// <summary>The 3000×2000 world canvas that consumers add children to.</summary>
    public Canvas World => _world;

    // ─── Camera ──────────────────────────────────────────────────────────────

    public void ApplyTransform()
    {
        _scale.ScaleX = _camZoom;
        _scale.ScaleY = _camZoom;
        _translate.X = _camX;
        _translate.Y = _camY;
    }

    public void FitAll()
    {
        var vw = Bounds.Width;
        var vh = Bounds.Height;
        if (vw <= 0 || vh <= 0) return;

        var zx = vw / WorldWidth;
        var zy = vh / WorldHeight;
        var z = Math.Min(zx, zy) * 0.9;
        var targetX = (vw - WorldWidth * z) / 2.0;
        var targetY = (vh - WorldHeight * z) / 2.0;
        FlyTo(targetX, targetY, z);
    }

    /// <summary>
    /// Smoothly animate camera to the given translation + zoom over 600ms.
    /// Uses cubic-bezier(.25, 1, .5, 1) easing to match the original CSS.
    /// </summary>
    public void FlyTo(double x, double y, double zoom, TimeSpan? duration = null)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var dur = duration ?? TimeSpan.FromMilliseconds(600);

        _flyTimer?.Stop();
        var fromX = _camX;
        var fromY = _camY;
        var fromZ = _camZoom;
        var start = DateTime.UtcNow;

        _flyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / dur.TotalMilliseconds;
            if (t >= 1)
            {
                _camX = x; _camY = y; _camZoom = zoom;
                ApplyTransform();
                _flyTimer?.Stop();
                _flyTimer = null;
                return;
            }
            var e = EaseOutQuint(t);
            _camX = fromX + (x - fromX) * e;
            _camY = fromY + (y - fromY) * e;
            _camZoom = fromZ + (zoom - fromZ) * e;
            ApplyTransform();
        });
        _flyTimer.Start();
    }

    // Matches CSS `cubic-bezier(.25, 1, .5, 1)` (an "easeOutQuint"-ish curve).
    private static double EaseOutQuint(double t) => 1 - Math.Pow(1 - t, 5);

    /// <summary>Sets the camera instantly with no animation (used during drag/zoom).</summary>
    public void SetCamera(double x, double y, double zoom)
    {
        _flyTimer?.Stop();
        _camX = x;
        _camY = y;
        _camZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ApplyTransform();
    }

    public (double X, double Y, double Zoom) Camera => (_camX, _camY, _camZoom);

    /// <summary>Did the pointer move more than the drag threshold since mousedown?</summary>
    public bool DragMoved => _dragMoved;

    // ─── Pointer input ──────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        var p = e.GetPosition(this);
        _dragStart = p;
        _dragCamStart = new Point(_camX, _camY);
        _dragMoved = false;
        _flyTimer?.Stop();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start) return;

        var p = e.GetPosition(this);
        var dx = p.X - start.X;
        var dy = p.Y - start.Y;
        if (Math.Abs(dx) > DragThresholdPx || Math.Abs(dy) > DragThresholdPx) _dragMoved = true;

        _camX = _dragCamStart.X + dx;
        _camY = _dragCamStart.Y + dy;
        ApplyTransform();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;

        var factor = e.Delta.Y > 0 ? 1.1 : 0.9;
        var newZoom = Math.Clamp(_camZoom * factor, MinZoom, MaxZoom);

        var m = e.GetPosition(this);
        // Keep the world-point under the cursor stationary on screen.
        _camX = m.X - (m.X - _camX) * (newZoom / _camZoom);
        _camY = m.Y - (m.Y - _camY) * (newZoom / _camZoom);
        _camZoom = newZoom;
        ApplyTransform();
    }

    static OrbitWorld()
    {
        // Double-click fit-all — attach once to the routed event so all instances
        // pick it up without per-instance subscription.
        DoubleTappedEvent.AddClassHandler<OrbitWorld>((x, _) => x.FitAll());
    }
}
