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
    private const double FitAllPaddingPx = 96;
    private const double PanSlackPx = 280;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !change.GetNewValue<bool>())
            CancelDragState();
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

        // Keep explicit viewport padding so Fit All never crops edge content.
        // z = min((vw-2p)/GALAXY_W, (vh-2p)/GALAXY_H)
        // x = (vw - GALAXY_W * z) / 2
        // y = (vh - GALAXY_H * z) / 2
        var availW = Math.Max(1, vw - FitAllPaddingPx * 2);
        var availH = Math.Max(1, vh - FitAllPaddingPx * 2);
        var z = Math.Clamp(Math.Min(availW / WorldWidth, availH / WorldHeight), MinZoom, MaxZoom);
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
        (x, y) = ClampCameraPosition(x, y, zoom);
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
        _camZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        (_camX, _camY) = ClampCameraPosition(x, y, _camZoom);
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
        // Capture is deferred until the drag threshold is exceeded so that taps
        // on child controls (GameOrb, SpaceStation) still receive PointerReleased.
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start) return;
        // If we missed PointerReleased while another surface was active (for
        // example after switching away to an embedded stream panel), avoid
        // getting stuck in drag mode when orbit becomes active again.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelDragState();
            return;
        }

        var p = e.GetPosition(this);
        var dx = p.X - start.X;
        var dy = p.Y - start.Y;
        if (Math.Abs(dx) > DragThresholdPx || Math.Abs(dy) > DragThresholdPx)
        {
            if (!_dragMoved)
                e.Pointer.Capture(this);
            _dragMoved = true;
            _userMovedCamera = true;
        }

        (_camX, _camY) = ClampCameraPosition(_dragCamStart.X + dx, _dragCamStart.Y + dy, _camZoom);
        ApplyTransform();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        CancelDragState();
        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDragState();
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
        var nextX = m.X - (m.X - _camX) * (newZoom / _camZoom);
        var nextY = m.Y - (m.Y - _camY) * (newZoom / _camZoom);
        (_camX, _camY) = ClampCameraPosition(nextX, nextY, newZoom);
        _camZoom = newZoom;
        ApplyTransform();
    }

    private (double X, double Y) ClampCameraPosition(double x, double y, double zoom)
    {
        var vw = Bounds.Width;
        var vh = Bounds.Height;
        if (vw <= 0 || vh <= 0)
            return (x, y);

        var scaledW = WorldWidth * zoom;
        var scaledH = WorldHeight * zoom;

        // Keep clamp ranges continuous across zoom thresholds to avoid visible
        // camera snaps when crossing from "world smaller than viewport" to
        // "world larger than viewport" (or vice versa).
        double minX, maxX;
        if (scaledW <= vw)
        {
            var cx = (vw - scaledW) / 2.0;
            minX = cx - PanSlackPx;
            maxX = cx + PanSlackPx;
        }
        else
        {
            minX = (vw - scaledW) - PanSlackPx;
            maxX = PanSlackPx;
        }

        double minY, maxY;
        if (scaledH <= vh)
        {
            var cy = (vh - scaledH) / 2.0;
            minY = cy - PanSlackPx;
            maxY = cy + PanSlackPx;
        }
        else
        {
            minY = (vh - scaledH) - PanSlackPx;
            maxY = PanSlackPx;
        }

        var clampedX = Math.Clamp(x, minX, maxX);
        var clampedY = Math.Clamp(y, minY, maxY);

        return (clampedX, clampedY);
    }

    static OrbitWorld()
    {
        // Double-click fit-all — attach once to the routed event so all instances
        // pick it up without per-instance subscription.
        DoubleTappedEvent.AddClassHandler<OrbitWorld>((x, _) => x.ResetAndFitAll());
    }

    private void CancelDragState()
    {
        _dragStart = null;
        _dragMoved = false;
    }
}
