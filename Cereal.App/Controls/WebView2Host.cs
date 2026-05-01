using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Cereal.App.Controls;

/// <summary>
/// Avalonia <see cref="NativeControlHost"/> that drives Microsoft.Web.WebView2
/// directly. Replaces WebView.Avalonia 11.0.0.1, whose wrapper mis-sized the
/// native HWND (it lagged behind Avalonia's layout, leaving black strips on
/// the right/bottom). Sizing here is anchored to the actual HWND client rect
/// via GetClientRect after each Avalonia arrange pass, so the WebView2 view
/// always matches what Avalonia drew.
///
/// Windows only — do not instantiate elsewhere. The Microsoft.Web.WebView2
/// assembly references work cross-platform but the native loader is Win32.
/// </summary>
public sealed class WebView2Host : NativeControlHost
{
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private IntPtr _hostHwnd;
    private bool _initStarted;
    private string? _pendingNavigation;
    private string? _userAgent;

    /// <summary>
    /// Per-session user-data folder for storage isolation (cookies, localStorage,
    /// IndexedDB). Must be set before the host is attached to the visual tree.
    /// </summary>
    public string? UserDataFolder { get; set; }

    public string? UserAgent
    {
        get => _userAgent;
        set
        {
            _userAgent = value;
            if (_core is not null && !string.IsNullOrEmpty(value))
                _core.Settings.UserAgent = value;
        }
    }

    public Uri? Source
    {
        get => _core is { Source: var s } && !string.IsNullOrEmpty(s) ? new Uri(s) : null;
        set
        {
            var url = value?.ToString();
            if (url is null) return;
            if (_core is not null) _core.Navigate(url);
            else _pendingNavigation = url;
        }
    }

    /// <summary>Fires after a navigation finishes (success or failure).</summary>
    public event EventHandler? NavigationCompleted;

    /// <summary>Fires once the underlying CoreWebView2 is created and ready.</summary>
    public event EventHandler? CoreReady;

    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (_core is null) return "";
        try { return await _core.ExecuteScriptAsync(script); }
        catch (Exception ex) { Log.Debug(ex, "[wv2] ExecuteScriptAsync failed"); return ""; }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        // We host the WebView2 inside our own child HWND so Avalonia's
        // NativeControlHost can position/size it freely, while we control
        // the WebView2's internal Bounds via the HWND's client rect.
        _hostHwnd = NativeMethods.CreateChildHwnd(parent.Handle);
        if (!_initStarted)
        {
            _initStarted = true;
            _ = InitAsync();
        }
        return new PlatformHandle(_hostHwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        try { _controller?.Close(); }
        catch (Exception ex) { Log.Debug(ex, "[wv2] Controller.Close failed"); }
        _controller = null;
        _core = null;
        base.DestroyNativeControlCore(control);
        _hostHwnd = IntPtr.Zero;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = base.ArrangeOverride(finalSize);
        // Avalonia resizes our HWND after this returns. Defer the bounds sync
        // so GetClientRect reports the new physical-pixel size.
        Dispatcher.UIThread.Post(UpdateControllerBounds, DispatcherPriority.Background);
        return s;
    }

    private void UpdateControllerBounds()
    {
        if (_controller is null || _hostHwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetClientRect(_hostHwnd, out var rect)) return;
        var w = rect.right - rect.left;
        var h = rect.bottom - rect.top;
        if (w <= 0 || h <= 0) return;
        try { _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h); }
        catch (Exception ex) { Log.Debug(ex, "[wv2] Bounds update failed ({W}x{H})", w, h); }
    }

    private async Task InitAsync()
    {
        try
        {
            var udf = UserDataFolder
                ?? Path.Combine(Path.GetTempPath(), "cereal-wv2", "default");
            Directory.CreateDirectory(udf);

            var env = await CoreWebView2Environment.CreateAsync(null, udf, null);
            _controller = await env.CreateCoreWebView2ControllerAsync(_hostHwnd);
            _core = _controller.CoreWebView2;

            UpdateControllerBounds();

            if (!string.IsNullOrEmpty(_userAgent))
                _core.Settings.UserAgent = _userAgent;

            _core.NavigationCompleted += (_, _) => Dispatcher.UIThread.Post(
                () => NavigationCompleted?.Invoke(this, EventArgs.Empty));

            // Keep popups + target=_blank links in-app. WebView2's default is
            // to hand them off to the system browser, which breaks OAuth flows
            // that chain through Microsoft / Steam / Epic intermediaries via
            // window.open(). Re-target them at the current view instead.
            _core.NewWindowRequested += OnNewWindowRequested;

            CoreReady?.Invoke(this, EventArgs.Empty);

            if (_pendingNavigation is { } pending)
            {
                _pendingNavigation = null;
                _core.Navigate(pending);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[wv2] WebView2 init failed (is the runtime installed?)");
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_core is null) return;
        e.Handled = true;
        try { _core.Navigate(e.Uri); }
        catch (Exception ex) { Log.Debug(ex, "[wv2] Same-window redirect failed for {Uri}", e.Uri); }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int X, int Y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        private const uint WS_CHILD        = 0x40000000;
        private const uint WS_VISIBLE      = 0x10000000;
        private const uint WS_CLIPCHILDREN = 0x02000000;

        public static IntPtr CreateChildHwnd(IntPtr parent)
        {
            // STATIC is a built-in window class with a default WndProc; perfect
            // as a passive container for the WebView2's internal HWND.
            return CreateWindowExW(
                0, "STATIC", "",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                0, 0, 1, 1,
                parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
