using System.ComponentModel;
using System.Runtime.InteropServices;
using EDock.Interop;
using EDock.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace EDock.Services;

/// <summary>Positions one dock against the full primary-screen edge and reveals it without taking focus.</summary>
public sealed class EdgeController : IDisposable
{
    private const int TriggerEdgePixels = 3;
    private readonly Window _window;
    private readonly DockSide _side;
    private readonly Func<bool> _isInteractionBusy;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _timer;
    private int _timerIntervalMs = 50;
    private readonly EdgeAnimation _animation = new();
    private nint _dpiProbe;
    private PixelRect _dpiProbeScreen;
    private PixelRect _bounds;
    private PixelRect _monitorBounds;
    private nint _monitor;
    private uint _dpi;
    private bool _autoHide = true;
    private bool _quickMode;
    private bool _keyboardHold;
    internal event EventHandler? KeyboardContextTick;
    private int _hideDelayMs = 650;
    private int _revealDelayMs = 120;
    private int _revealDurationMs = 250;
    private int _hideDurationMs = 250;
    private EdgeAnimationStyle _animationStyle = EdgeAnimationStyle.Slide;
    private string _dockFrameStyle = "Edge";
    private double _floatingTopInsetDip = 8;
    private double _floatingBottomInsetDip = 8;
    private int _cornerRadiusPixels;
    private bool _motionEnabled = true;
    private long _lastMotionCheck;
    private double _widthDip = 96;
    private double? _regularWidthDip;
    private double? _expandedWidthDip;
    private long? _outsideSince;
    private long? _hoverSince;
    private long _lastPlacementCheck;
    private long _lastFullscreenCheck;
    private bool _fullscreen;
    private bool _disposed;
    private static readonly NativeMethods.WindowSubclassProc ActivationCallback = OnActivationMessage;
    private GCHandle _activationReference;
    private nuint _activationSubclassId;
    private readonly NativeMethods.MouseHookProc _activationHookCallback;
    private nint _activationHook;
    private nint _desktopFocusWindow;
    private nint _pendingDesktopActivation;
    private int _mouseDownGeneration;
    private int _pinPointerGeneration;

    public bool IsRevealed { get; private set; }
    public bool IsQuickMode => _quickMode;
    internal bool IsPinned => !_autoHide;
    public double AvailableWidthDip { get; private set; }
    public double AvailableHeightDip { get; private set; }
    public bool IsPointerNearInnerEdge { get; private set; }
    public bool IsKeyboardTarget { get; private set; }
    public event EventHandler<bool>? RevealedChanged;
    public event EventHandler? KeyboardTargetChanged;
    public event EventHandler<bool>? InnerEdgeHoverChanged;
    public event EventHandler? AvailableSizeChanged;

    public EdgeController(Window window, DockSide side, Func<bool>? isInteractionBusy = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _side = side;
        _isInteractionBusy = isInteractionBusy ?? (() => false);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _activationHookCallback = OnMouseActivation;

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Remove residual non-client resize/caption styles after the presenter has
        // removed its title bar. DWM's one-pixel border is controlled separately.
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlStyle).ToInt64();
        style &= ~(NativeMethods.WsCaption | NativeMethods.WsThickFrame);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlStyle, new nint(style));
        var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        exStyle = (exStyle | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new nint(exStyle));
        var cornerPreference = NativeMethods.DwmwcpDoNotRound;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        var borderColor = NativeMethods.DwmwaColorNone;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DwmwaBorderColor, ref borderColor, sizeof(int));

        RefreshMotionPreference();
        RefreshPlacement();
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
        _timer = window.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(_timerIntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _window.Closed += OnWindowClosed;
        TrackDesktopActivation();
        _timer.Start();
    }

    private void TrackDesktopActivation()
    {
        try
        {
            _activationReference = GCHandle.Alloc(this);
            nuint subclassId = unchecked((nuint)GCHandle.ToIntPtr(_activationReference));
            if (!NativeMethods.SetWindowSubclass(_hwnd, ActivationCallback, subclassId, subclassId))
            {
                ReleaseActivationReference();
                return;
            }
            _activationSubclassId = subclassId;
            // Sample the actual desktop before WinUI handles the physical press.
            // WM_ACTIVATE and CBT can already report a transient null foreground.
            _activationHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLowLevel,
                _activationHookCallback, NativeMethods.GetModuleHandle(null), 0);
        }
        catch
        {
            // Focus restoration is optional; dock startup must still succeed.
            ReleaseActivationReference();
        }
    }

    private unsafe nint OnMouseActivation(int code, nint message, nint data)
    {
        try
        {
            if (!_disposed && code >= 0)
            {
                int mouseMessage = unchecked((int)message);
                if (mouseMessage is NativeMethods.WmLeftButtonDown or 0x0204 or 0x0207 or 0x020B)
                {
                    // Every new button press retires both an unclaimed sample and
                    // any older pin gesture. The hook does not allocate or consume input.
                    _pendingDesktopActivation = 0;
                    _desktopFocusWindow = 0;
                    _pinPointerGeneration = 0;
                    _mouseDownGeneration = _mouseDownGeneration == int.MaxValue ? 1 : _mouseDownGeneration + 1;
                    if (mouseMessage == NativeMethods.WmLeftButtonDown && data != 0)
                    {
                        nint previous = NativeMethods.GetForegroundWindow();
                        if (WindowClassReader.Read(previous) == BackgroundWindowKind.Desktop
                            && NativeMethods.IsPointerOverWindow(_hwnd, ((NativeMethods.LowLevelMouseData*)data)->Position)
                            && NativeMethods.GetForegroundWindow() == previous)
                            _pendingDesktopActivation = previous;
                    }
                }
            }
        }
        catch { /* Every mouse event must continue, even if tracking fails. */ }
        return NativeMethods.CallNextHookEx(_activationHook, code, message, data);
    }

    private static nint OnActivationMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        try
        {
            if (GCHandle.FromIntPtr(unchecked((nint)referenceData)).Target is EdgeController edge)
            {
                if (message == NativeMethods.WmNcDestroy)
                {
                    NativeMethods.RemoveWindowSubclass(hwnd, ActivationCallback, subclassId);
                    edge._activationSubclassId = 0;
                    edge.DetachActivationHook();
                    edge.ReleaseActivationReference();
                }
                else if (!edge._disposed && message == NativeMethods.WmActivate)
                {
                    if ((wParam & 0xffff) == 0)
                    {
                        edge._desktopFocusWindow = 0;
                        edge._pendingDesktopActivation = 0;
                        edge._pinPointerGeneration = 0;
                    }
                }
            }
        }
        catch { /* Managed exceptions must not cross a native callback boundary. */ }
        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    internal int BeginPinPointerGesture()
    {
        nint desktop = _pendingDesktopActivation;
        _pendingDesktopActivation = 0;
        _desktopFocusWindow = 0;
        _pinPointerGeneration = 0;
        // Called only by the pin's physical mouse PointerPressed, never Click or
        // UI automation. The event owns this down even if an up is already queued.
        if (!_disposed && desktop != 0)
        {
            _desktopFocusWindow = desktop;
            _pinPointerGeneration = _mouseDownGeneration;
        }
        return _pinPointerGeneration;
    }

    internal void CancelPinPointerGesture(int generation)
    {
        if (generation == 0 || generation != _pinPointerGeneration) return;
        _pinPointerGeneration = 0;
        _desktopFocusWindow = 0;
    }

    internal bool RestoreDesktopFocus()
    {
        nint desktop = _desktopFocusWindow;
        int generation = _pinPointerGeneration;
        _desktopFocusWindow = 0;
        _pinPointerGeneration = 0;
        // Explorer's foreground WorkerW can report itself as invisible. The
        // exact desktop captured for this pointer gesture remains the authority.
        if (_disposed || generation == 0 || NativeMethods.GetForegroundWindow() != _hwnd || desktop == 0
            || !NativeMethods.IsWindow(desktop)
            || !NativeMethods.IsWindowEnabled(desktop) || NativeMethods.IsIconic(desktop)
            || WindowClassReader.Read(desktop) != BackgroundWindowKind.Desktop)
            return false;
        return NativeMethods.SetForegroundWindow(desktop);
    }

    private void ReleaseActivationReference()
    {
        _desktopFocusWindow = 0;
        _pendingDesktopActivation = 0;
        _pinPointerGeneration = 0;
        if (_activationSubclassId == 0 && _activationHook == 0 && _activationReference.IsAllocated)
            _activationReference.Free();
    }

    private void DetachActivationHook()
    {
        if (_activationHook != 0 && NativeMethods.UnhookWindowsHookEx(_activationHook)) _activationHook = 0;
    }

    private void DetachActivationTracking()
    {
        DetachActivationHook();
        if (_activationSubclassId != 0
            && (NativeMethods.RemoveWindowSubclass(_hwnd, ActivationCallback, _activationSubclassId) || !NativeMethods.IsWindow(_hwnd)))
            _activationSubclassId = 0;
        // Retain the GCHandle until both native callbacks are gone.
        ReleaseActivationReference();
    }

    public void Configure(bool autoHide, int hideDelayMs, int revealDelayMs, double widthDip,
        int revealDurationMs = 250, int hideDurationMs = 250, string animationStyle = "Slide", string dockFrameStyle = "Edge", double? regularWidthDip = null,
        double floatingTopInsetDip = 8, double floatingBottomInsetDip = 8)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(widthDip) || widthDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthDip));
        _autoHide = autoHide;
        _hideDelayMs = Math.Clamp(hideDelayMs, 100, 30_000);
        _revealDelayMs = Math.Clamp(revealDelayMs, 0, 5_000);
        _widthDip = Math.Clamp(widthDip, 72, 320);
        if (regularWidthDip is double regular && (!double.IsFinite(regular) || regular <= 0))
            throw new ArgumentOutOfRangeException(nameof(regularWidthDip));
        _regularWidthDip = regularWidthDip;
        _revealDurationMs = EdgeAnimation.ClampDuration(revealDurationMs);
        _hideDurationMs = EdgeAnimation.ClampDuration(hideDurationMs);
        _animationStyle = EdgeAnimation.ParseStyle(animationStyle);
        _dockFrameStyle = dockFrameStyle switch
        {
            "Floating" => "Floating",
            "FloatingSquare" => "FloatingSquare",
            _ => "Edge"
        };
        if (!double.IsFinite(floatingTopInsetDip) || floatingTopInsetDip < 0)
            throw new ArgumentOutOfRangeException(nameof(floatingTopInsetDip));
        if (!double.IsFinite(floatingBottomInsetDip) || floatingBottomInsetDip < 0)
            throw new ArgumentOutOfRangeException(nameof(floatingBottomInsetDip));
        _floatingTopInsetDip = Math.Clamp(floatingTopInsetDip, 0, 200);
        _floatingBottomInsetDip = Math.Clamp(floatingBottomInsetDip, 0, 200);
        _outsideSince = null;
        _hoverSince = null;
        RefreshMotionPreference();
        if (_animation.IsRunning)
            _animation.Start(IsRevealed, IsRevealed ? _revealDurationMs : _hideDurationMs, ShouldAnimate, Environment.TickCount64);
        RefreshPlacement();
        ApplyAnimationFrame();
        if (!_autoHide || _quickMode || _keyboardHold) SetRevealed(true);
    }

    public void Reveal()
    {
        if (_disposed) return;
        _outsideSince = null;
        _hoverSince = null;
        SetRevealed(true);
    }

    public void SetExpandedWidth(double? widthDip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (widthDip is double width && (!double.IsFinite(width) || width <= 0))
            throw new ArgumentOutOfRangeException(nameof(widthDip));
        if (_expandedWidthDip == widthDip) return;
        _expandedWidthDip = widthDip;
        RefreshPlacement();
    }

    /// <summary>Temporarily reveals the dock; the configured pin and auto-hide settings remain intact.</summary>
    public void SetQuickMode(bool enabled)
    {
        if (_disposed || _quickMode == enabled) return;
        _quickMode = enabled;
        _outsideSince = null;
        _hoverSince = null;
        _lastFullscreenCheck = 0;
        if (enabled)
        {
            SetInnerEdgeHover(false);
            Reveal();
        }
        else if (!_autoHide || _keyboardHold) Reveal();
        else SetRevealed(false);
    }

    internal void SetKeyboardHold(bool enabled)
    {
        if (_disposed || _keyboardHold == enabled) return;
        _keyboardHold = enabled;
        _outsideSince = null;
        _hoverSince = null;
        _lastFullscreenCheck = 0;
        if (enabled || _quickMode || !_autoHide) Reveal();
        else SetRevealed(false);
    }

    public void RefreshPlacement()
    {
        if (_disposed) return;
        // The primary screen contains the desktop origin. Always resolve it afresh
        // so display changes cannot leave a dock following an adjacent monitor.
        var monitor = NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot obtain the primary monitor bounds.");
        // Resolve geometry in physical pixels so each floating inset respects
        // the monitor's current DPI and position.
        var screen = new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        var dpi = ReadTargetDpi(screen);
        string frameStyle = _dockFrameStyle;
        var available = EdgeGeometry.CalculateAvailableSize(screen, dpi, frameStyle, _floatingTopInsetDip, _floatingBottomInsetDip);
        bool availableChanged = Math.Abs(AvailableWidthDip - available.WidthDip) > .1 || Math.Abs(AvailableHeightDip - available.HeightDip) > .1;
        AvailableWidthDip = available.WidthDip;
        AvailableHeightDip = available.HeightDip;
        var width = EdgeGeometry.CalculateEffectiveWidth(_widthDip, _expandedWidthDip ?? _regularWidthDip, AvailableWidthDip);
        var bounds = EdgeGeometry.CalculateBounds(screen, dpi, width, _side, frameStyle, _floatingTopInsetDip, _floatingBottomInsetDip);
        var changed = bounds != _bounds || dpi != _dpi || screen != _monitorBounds || monitor != _monitor;
        _monitor = monitor;
        _monitorBounds = screen;
        _dpi = dpi;
        _bounds = bounds;
        _cornerRadiusPixels = string.Equals(_dockFrameStyle, "Floating", StringComparison.Ordinal)
            ? Math.Min(EdgeGeometry.DipToPixels(16, dpi), Math.Min(bounds.Width, bounds.Height) / 2)
            : 0;
        _lastPlacementCheck = Environment.TickCount64;
        if (!changed) return;
        ApplyAnimationFrame(frameChanged: true);
        if (availableChanged) AvailableSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private uint ReadTargetDpi(PixelRect screen)
    {
        // A sliding HWND can temporarily occupy an adjacent monitor. Its DPI must
        // not resize the primary-screen dock. Query a hidden 1px HWND on the target
        // monitor with GetDpiForWindow, the supported per-monitor-aware API.
        if (_dpiProbe == 0)
        {
            _dpiProbe = NativeMethods.CreateWindowEx((uint)NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate,
                "STATIC", null, NativeMethods.WsPopup, screen.Left, screen.Top, 1, 1, 0, 0, 0, 0);
            if (_dpiProbe == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the monitor DPI probe.");
            _dpiProbeScreen = screen;
        }
        else if (_dpiProbeScreen != screen)
        {
            if (!NativeMethods.SetWindowPos(_dpiProbe, 0, screen.Left, screen.Top, 1, 1,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot move the monitor DPI probe.");
            _dpiProbeScreen = screen;
        }
        var dpi = NativeMethods.GetDpiForWindow(_dpiProbe);
        return dpi == 0 ? (_dpi == 0 ? 96 : _dpi) : dpi;
    }

    private bool ShouldAnimate => _motionEnabled && _animationStyle != EdgeAnimationStyle.None;

    private void RefreshMotionPreference()
    {
        _lastMotionCheck = Environment.TickCount64;
        if (NativeMethods.SystemParametersInfo(NativeMethods.SpiGetClientAreaAnimation, 0, out bool enabled, 0))
            _motionEnabled = enabled;
        if (!_motionEnabled && _animation.IsRunning)
        {
            _animation.Finish();
            ApplyAnimationFrame();
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed) return;
        // Reuse edge detection to discover desktop input when a fixed dock's
        // pointer is elsewhere, even if Ctrl quick mode is disabled.
        if (_keyboardHold || !_autoHide) KeyboardContextTick?.Invoke(this, EventArgs.Empty);
        var now = Environment.TickCount64;
        if (now - _lastMotionCheck >= 1000) RefreshMotionPreference();
        UpdateAnimation(now);
        if (now - _lastPlacementCheck >= 500) RefreshPlacement();
        bool hasCursor = NativeMethods.GetCursorPos(out var cursor);
        bool busy = _isInteractionBusy();
        if (_autoHide && !_quickMode && !_keyboardHold && !busy && now - _lastFullscreenCheck >= 250)
        {
            _lastFullscreenCheck = now;
            _fullscreen = IsForegroundFullscreen();
        }
        UpdatePointerInteractions(hasCursor ? cursor : null, busy || _quickMode || _keyboardHold || (_autoHide && _fullscreen));
        if (_quickMode || _keyboardHold)
        {
            _outsideSince = null;
            _hoverSince = null;
            if (!IsRevealed) Reveal();
            return;
        }
        if (!_autoHide || !hasCursor) return;
        if (busy)
        {
            _outsideSince = null;
            if (!IsRevealed) Reveal();
            return;
        }
        if (_fullscreen)
        {
            _hoverSince = null;
            if (IsRevealed) SetRevealed(false);
            return;
        }

        if (!IsRevealed)
        {
            // Detection is independent of the HWND. Floating Reveal can have an
            // empty region when hidden without intercepting the desktop gap.
            var edge = EdgeGeometry.CalculateTriggerBounds(_monitorBounds, _side, TriggerEdgePixels);
            if (!edge.Contains(cursor.X, cursor.Y))
            {
                _hoverSince = null;
                return;
            }
            _hoverSince ??= now;
            if (now - _hoverSince.Value >= _revealDelayMs) Reveal();
            return;
        }

        var hoverBounds = EdgeGeometry.CalculateHoverBounds(_bounds, _monitorBounds, _side);
        if (hoverBounds.Contains(cursor.X, cursor.Y))
        {
            _outsideSince = null;
            return;
        }
        _outsideSince ??= now;
        if (now - _outsideSince.Value >= _hideDelayMs) SetRevealed(false);
    }

    private void UpdatePointerInteractions(NativeMethods.Point? cursor, bool busy)
    {
        bool eligible = !busy && IsRevealed && !_animation.IsRunning && _animation.Value >= 1
            && cursor is NativeMethods.Point point && NativeMethods.IsPointerOverWindow(_hwnd, point);
        SetKeyboardTarget(eligible);
        bool nearInnerEdge = eligible && cursor is NativeMethods.Point innerPoint
            && EdgeGeometry.CalculateInnerEdgeBounds(_bounds, _dpi, _side).Contains(innerPoint.X, innerPoint.Y);
        SetInnerEdgeHover(nearInnerEdge);
    }

    private void SetKeyboardTarget(bool eligible)
    {
        if (IsKeyboardTarget == eligible) return;
        IsKeyboardTarget = eligible;
        KeyboardTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetInnerEdgeHover(bool value)
    {
        if (IsPointerNearInnerEdge == value) return;
        IsPointerNearInnerEdge = value;
        InnerEdgeHoverChanged?.Invoke(this, value);
    }

    private void SetRevealed(bool revealed)
    {
        if (IsRevealed == revealed) return;
        IsRevealed = revealed;
        if (!revealed) { SetKeyboardTarget(false); SetInnerEdgeHover(false); }
        // Restore cold content before starting the reveal clock, so decoding
        // and layout preparation do not consume the slide animation's duration.
        if (revealed) RevealedChanged?.Invoke(this, true);
        _animation.Start(revealed, revealed ? _revealDurationMs : _hideDurationMs, ShouldAnimate, Environment.TickCount64);
        UpdateTimerCadence();
        _outsideSince = null;
        _hoverSince = null;
        ApplyAnimationFrame();
        if (!revealed) RevealedChanged?.Invoke(this, false);
    }

    private void ApplyAnimationFrame(bool frameChanged = false)
    {
        bool reveal = _animationStyle == EdgeAnimationStyle.Reveal;
        var window = reveal ? _bounds : EdgeGeometry.CalculateSlideBounds(_bounds, _monitorBounds, _side, _animation.Value);
        var viewport = EdgeGeometry.CalculateViewportRegion(window, _monitorBounds);
        if (reveal)
        {
            int width = (int)Math.Round(_bounds.Width * _animation.Value);
            var revealed = EdgeGeometry.CalculateRevealBounds(_bounds, _side, width);
            viewport = new PixelRect(revealed.Left - window.Left, 0, revealed.Right - window.Left, window.Height);
        }
        // Mouse-edge detection is independent of the HWND. Every hidden style
        // uses an empty native region, including None and reduced motion.
        bool empty = _animation.Value <= 0 || viewport.Width <= 0 || viewport.Height <= 0;
        if (empty) viewport = new PixelRect(0, 0, 0, 0);
        ApplyWindowRegion(viewport, window, redraw: reveal || empty);
        // A fully hidden dock needs no visible composition surface. Edge
        // detection uses screen coordinates and keeps working while HWND-hidden.
        uint flags = NativeMethods.SwpNoActivate | (empty ? NativeMethods.SwpHideWindow : NativeMethods.SwpShowWindow);
        if (frameChanged) flags |= NativeMethods.SwpFrameChanged;
        // The entire HWND moves without activating; its region also clips any
        // adjacent monitor. Keep its full size and height throughout the glide.
        if (!NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, window.Left, window.Top, window.Width, window.Height, flags))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot position the edge dock.");
    }

    private void ApplyWindowRegion(PixelRect viewport, PixelRect window, bool redraw)
    {
        if (_cornerRadiusPixels == 0 && viewport == new PixelRect(0, 0, window.Width, window.Height))
        {
            if (NativeMethods.SetWindowRgn(_hwnd, 0, redraw) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot reveal the edge dock.");
            return;
        }
        var region = NativeMethods.CreateRectRgn(viewport.Left, viewport.Top, viewport.Right, viewport.Bottom);
        if (region == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the edge region.");
        if (_cornerRadiusPixels > 0)
        {
            // Intersect the shape with both the animation viewport and the target
            // screen. This clips transparent, image and acrylic backdrops equally.
            var round = NativeMethods.CreateRoundRectRgn(0, 0, window.Width + 1, window.Height + 1,
                _cornerRadiusPixels * 2, _cornerRadiusPixels * 2);
            if (round == 0)
            {
                int roundError = Marshal.GetLastWin32Error();
                NativeMethods.DeleteObject(region);
                throw new Win32Exception(roundError, "Cannot create the rounded dock region.");
            }
            int result = NativeMethods.CombineRgn(region, region, round, NativeMethods.RgnAnd);
            int combineError = Marshal.GetLastWin32Error();
            NativeMethods.DeleteObject(round);
            if (result == NativeMethods.RgnError)
            {
                NativeMethods.DeleteObject(region);
                throw new Win32Exception(combineError, "Cannot clip the rounded dock region.");
            }
        }
        if (NativeMethods.SetWindowRgn(_hwnd, region, redraw) != 0) return; // Windows owns a successfully assigned region.
        int error = Marshal.GetLastWin32Error();
        NativeMethods.DeleteObject(region);
        throw new Win32Exception(error, "Cannot hide the edge dock.");
    }

    private void UpdateAnimation(long now)
    {
        if (_animation.Update(now)) ApplyAnimationFrame();
        UpdateTimerCadence();
    }

    private void UpdateTimerCadence()
    {
        int intervalMs = IsRevealed || _animation.IsRunning ? 16 : 50;
        // This runs on every edge-detection tick. Keep unchanged cadence checks
        // managed so hidden docks do not repeatedly enter the WinRT timer getter.
        if (_timerIntervalMs == intervalMs) return;
        _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _timerIntervalMs = intervalMs;
    }

    private bool IsForegroundFullscreen()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || NativeMethods.IsIconic(foreground)) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == (uint)Environment.ProcessId) return false;
        if (WindowClassReader.Read(foreground) != BackgroundWindowKind.Other) return false;
        if (NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToPrimary) != _monitor
            || !NativeMethods.GetWindowRect(foreground, out var rect)) return false;
        return rect.Left <= _monitorBounds.Left && rect.Top <= _monitorBounds.Top
            && rect.Right >= _monitorBounds.Right && rect.Bottom >= _monitorBounds.Bottom;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetKeyboardTarget(false);
        _timer.Stop();
        _timer.Tick -= OnTick;
        _window.Closed -= OnWindowClosed;
        DetachActivationTracking();
        // If removal fails on a live HWND, retain the reference until
        // WM_NCDESTROY so no callback can observe a freed GCHandle.
        if (_dpiProbe != 0)
        {
            NativeMethods.DestroyWindow(_dpiProbe);
            _dpiProbe = 0;
        }
    }
}
