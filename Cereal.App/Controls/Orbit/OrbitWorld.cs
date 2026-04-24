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
    // Larger than the previous 0.90 framing so the galaxy reads bigger by default.
    private const double FitAllScale = 1.12;

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
    private bool _userMovedCamera;

    private DispatcherTimer? _flyTimer;
    public event EventHandler? CameraChanged;

    public OrbitWorld()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.Parse("#080818"));
        Focusable = true;
        // Keep a normal in-app pointer; drag is still supported without forcing
        // an always-on "move" cursor across the orbit surface.
        Cursor = new Cursor(StandardCursorType.Arrow);

        _world = new Canvas
        {
            Width = WorldWidth,
            Height = WorldHeight,
            // Keep layout size fixed at 3000×2000. If the parent only offers the
            // viewport size, a stretched canvas would shrink the arrange rect and
            // break FitAll() centering (camera math assumes world 0,0 = control 0,0 + cam).
            MinWidth = WorldWidth,
            MaxWidth = WorldWidth,
            MinHeight = WorldHeight,
            MaxHeight = WorldHeight,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
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
            // Keep auto-fitting until the user explicitly pans/zooms. This
            // avoids early layout passes (very small viewport) locking in a
            // tiny/offset camera before the control reaches its final size.
            if (!_userMovedCamera)
                Dispatcher.UIThread.Post(FitAll, DispatcherPriority.Background);
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
        CameraChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True after the user has panned, wheel-zoomed, or used the zoom HUD (not mere primary click).</summary>
    public bool UserAdjustedCamera => _userMovedCamera;

    /// <summary>Clears the "user adjusted" flag and fits the full galaxy in the viewport (double-click / Fit all).</summary>
    public void ResetAndFitAll()
    {
        _userMovedCamera = false;
        FitAll();
    }

    public void FitAll()
    {
        var vw = Bounds.Width;
        var vh = Bounds.Height;
        if (vw <= 0 || vh <= 0) return;
        // Ignore transient tiny layout passes during initialization.
        if (vw < 200 || vh < 120) return;

        // Slightly tighter framing than the original web version so the galaxy
        // feels larger and less distant on first open / Fit all.
        // z = min(vw/GALAXY_W, vh/GALAXY_H) * FitAllScale
        // x = (vw - GALAXY_W * z) / 2
        // y = (vh - GALAXY_H * z) / 2
        var z = Math.Clamp(Math.Min(vw / WorldWidth, vh / WorldHeight) * FitAllScale, MinZoom, MaxZoom);
        var targetX = (vw - WorldWidth * z) / 2.0;
        var targetY = (vh - WorldHeight * z) / 2.0;
        SetCamera(targetX, targetY, z);
    }

    public void MarkUserCameraAdjustment() => _userMovedCamera = true;

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
        if (Math.Abs(dx) > DragThresholdPx || Math.Abs(dy) > DragThresholdPx)
        {
            _dragMoved = true;
            _userMovedCamera = true;
        }

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

        // If a camera fly animation is active (e.g. FitAll/FlyTo), it can
        // immediately overwrite wheel-driven camera updates and make zoom feel
        // like it snaps toward a stale target. Cancel it so zoom-to-cursor is
        // always authoritative.
        _flyTimer?.Stop();
        _flyTimer = null;
        _userMovedCamera = true;

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
        DoubleTappedEvent.AddClassHandler<OrbitWorld>((x, _) => x.ResetAndFitAll());
    }
}
