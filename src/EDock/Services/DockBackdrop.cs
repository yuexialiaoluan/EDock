using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EDock.Services;

// Each dock owns its backdrop. Keeping it active avoids acrylic turning opaque
// merely because a hover-revealed toolbar intentionally does not take focus.
internal sealed class DockBackdrop : SystemBackdrop
{
    private const uint WmEraseBackground = 0x0014;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmDwmCompositionChanged = 0x031E;
    private const uint DwmBlurEnable = 0x00000001;
    private const uint DwmBlurRegion = 0x00000002;
    private const int BlackBrush = 4;
    // Native subclasses do not root managed callbacks. This static delegate and
    // the GCHandle retained until removal keep both callback and instance alive.
    private static readonly WindowSubclassProc SubclassCallback = OnWindowMessage;
    // Windows.UI.Composition requires the Windows (not WinAppSDK) dispatcher.
    // Retain one controller for the UI thread for the lifetime of the application.
    private static nint _windowsDispatcher;
    private readonly bool _acrylic;
    private bool _dark;
    private double _opacity;
    private DesktopAcrylicController? _controller;
    private Windows.UI.Composition.Compositor? _compositor;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private SystemBackdropConfiguration? _configuration;
    private nint _transparentWindow;
    private nuint _subclassId;
    private GCHandle _windowReference;
    private bool _transparentActive;

    public DockBackdrop(bool acrylic, bool dark, double opacity)
    {
        _acrylic = acrylic;
        Update(dark, opacity);
    }

    // Updating a connected material must not disconnect its composition target.
    // In particular a transparent target is independent of theme and opacity;
    // only the XAML background layers above it change.
    public void Update(bool dark, double opacity)
    {
        _dark = dark;
        _opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        var tint = _dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252);
        if (_configuration is not null)
            _configuration.Theme = _dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        if (_controller is not null)
        {
            _controller.TintColor = tint;
            _controller.TintOpacity = (float)_opacity;
            _controller.FallbackColor = tint;
        }
        else if (_acrylic && _brush is not null)
            _brush.Color = tint;
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // This backdrop deliberately owns its configuration: docks stay active,
        // and the selected settings scope supplies the theme through Update.
        // We never request the base class's default configuration. Its default
        // change handler can look up a target that has already disconnected
        // during a dock/scope refresh and throw E_INVALIDARG into XAML. Ignore
        // those default notifications instead of forwarding them to that table.
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        var tint = _dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252);
        if (_acrylic && DesktopAcrylicController.IsSupported())
        {
            _configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = _dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light
            };
            _controller = new DesktopAcrylicController
            {
                TintColor = tint, TintOpacity = (float)_opacity,
                LuminosityOpacity = .55f, FallbackColor = tint
            };
            _controller.SetSystemBackdropConfiguration(_configuration);
            _controller.AddSystemBackdropTarget(target);
        }
        else
        {
            EnsureWindowsDispatcher();
            _compositor = new Windows.UI.Composition.Compositor();
            _brush = _compositor.CreateColorBrush(_acrylic ? tint : Colors.Transparent);
            try
            {
                if (!_acrylic) AttachTransparentWindow(xamlRoot);
                target.SystemBackdrop = _brush;
            }
            catch
            {
                DetachTransparentWindow();
                _brush.Dispose(); _brush = null;
                _compositor.Dispose(); _compositor = null;
                throw;
            }
        }
    }

    private void AttachTransparentWindow(XamlRoot xamlRoot)
    {
        _transparentWindow = Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        if (_transparentWindow == 0) throw new InvalidOperationException("Cannot find the dock window for transparency.");
        _windowReference = GCHandle.Alloc(this);
        _subclassId = unchecked((nuint)GCHandle.ToIntPtr(_windowReference));
        if (!SetWindowSubclass(_transparentWindow, SubclassCallback, _subclassId, _subclassId))
        {
            int error = Marshal.GetLastWin32Error();
            ReleaseWindowReference();
            throw new Win32Exception(error, "Cannot install the dock transparency callback.");
        }
        _transparentActive = true;
        ConfigureTransparentWindow(_transparentWindow);
        ClearInitialBackground(_transparentWindow);
    }

    private static void ConfigureTransparentWindow(nint hwnd)
    {
        // The XAML island is transparent, but its parent HWND otherwise keeps
        // WinUI's opaque themed GDI fill beneath the composition content.
        var margins = new Margins();
        Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(hwnd, ref margins));
        nint region = CreateRectRgn(-2, -2, -1, -1);
        if (region == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var blur = new DwmBlurBehind
            {
                Flags = DwmBlurEnable | DwmBlurRegion,
                Enable = 1,
                Region = region
            };
            Marshal.ThrowExceptionForHR(DwmEnableBlurBehindWindow(hwnd, ref blur));
        }
        finally { DeleteObject(region); }
    }

    private static void ClearInitialBackground(nint hwnd)
    {
        nint dc = GetDC(hwnd);
        if (dc == 0) return;
        try { ClearBackground(hwnd, dc); }
        finally { ReleaseDC(hwnd, dc); }
    }

    private static bool ClearBackground(nint hwnd, nint dc)
    {
        // Within a DWM-enabled transparent HWND, black GDI pixels have zero
        // alpha. Suppress WinUI's later theme-colored WM_ERASEBKGND fills.
        return dc != 0 && GetClientRect(hwnd, out var bounds)
            && FillRect(dc, ref bounds, GetStockObject(BlackBrush)) != 0;
    }

    private static nint OnWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        try
        {
            var backdrop = GCHandle.FromIntPtr(unchecked((nint)referenceData)).Target as DockBackdrop;
            if (backdrop is not null)
            {
                if (message == WmNcDestroy)
                {
                    RemoveWindowSubclass(hwnd, SubclassCallback, subclassId);
                    backdrop.ReleaseWindowReference();
                }
                else if (backdrop._transparentActive && backdrop._transparentWindow == hwnd)
                {
                    if (message == WmEraseBackground && ClearBackground(hwnd, unchecked((nint)wParam))) return 1;
                    if (message == WmDwmCompositionChanged)
                    {
                        ConfigureTransparentWindow(hwnd);
                        ClearInitialBackground(hwnd);
                        return 0;
                    }
                }
            }
        }
        catch { /* Never let managed exceptions escape a native window callback. */ }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void DetachTransparentWindow()
    {
        _transparentActive = false;
        nint hwnd = _transparentWindow;
        if (hwnd == 0) return;
        var blur = new DwmBlurBehind { Flags = DwmBlurEnable };
        DwmEnableBlurBehindWindow(hwnd, ref blur);
        if (RemoveWindowSubclass(hwnd, SubclassCallback, _subclassId) || !IsWindow(hwnd))
            ReleaseWindowReference();
        // If removal unexpectedly fails on a live HWND, keep its GCHandle until
        // WM_NCDESTROY rather than leave a native callback with freed reference data.
    }

    private void ReleaseWindowReference()
    {
        _transparentActive = false;
        _transparentWindow = 0;
        _subclassId = 0;
        if (_windowReference.IsAllocated) _windowReference.Free();
    }

    private static void EnsureWindowsDispatcher()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _windowsDispatcher));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int Size; public int ThreadType; public int ApartmentType; }

    [DllImport("CoreMessaging.dll", ExactSpelling = true)]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind { public uint Flags; public int Enable; public nint Region; public int TransitionOnMaximized; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientRect { public int Left, Top, Right, Bottom; }

    [DllImport("comctl32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, WindowSubclassProc callback, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, WindowSubclassProc callback, nuint subclassId);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref DwmBlurBehind blur);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint GetStockObject(int objectId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out ClientRect bounds);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int FillRect(nint dc, ref ClientRect bounds, nint brush);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        DetachTransparentWindow();
        _controller?.RemoveAllSystemBackdropTargets();
        _controller?.Dispose(); _controller = null;
        target.SystemBackdrop = null;
        _brush?.Dispose(); _brush = null;
        _compositor?.Dispose(); _compositor = null;
        _configuration = null;
        base.OnTargetDisconnected(target);
    }
}
