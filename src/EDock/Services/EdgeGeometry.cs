using EDock.Models;

namespace EDock.Services;

/// <summary>A screen rectangle in physical pixels; right and bottom are exclusive.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Pure placement calculations, independent of WinUI and native window handles.</summary>
public static class EdgeGeometry
{
    public static int DipToPixels(double dip, uint dpi)
    {
        if (!double.IsFinite(dip))
            throw new ArgumentOutOfRangeException(nameof(dip));
        return checked((int)Math.Round(dip * (dpi == 0 ? 96 : dpi) / 96d, MidpointRounding.AwayFromZero));
    }

    public static double PixelsToDip(int pixels, uint dpi) => pixels * 96d / (dpi == 0 ? 96 : dpi);

    /// <summary>The largest dock size allowed by the monitor and the selected frame's gaps.</summary>
    public static (double WidthDip, double HeightDip) CalculateAvailableSize(PixelRect monitorBounds, uint dpi, string dockFrameStyle = "Edge", double floatingTopInsetDip = 8, double floatingBottomInsetDip = 8)
    {
        var available = CalculateBounds(monitorBounds, dpi, PixelsToDip(monitorBounds.Width, dpi), DockSide.Left, dockFrameStyle, floatingTopInsetDip, floatingBottomInsetDip);
        return (PixelsToDip(available.Width, dpi), PixelsToDip(available.Height, dpi));
    }

    /// <summary>Expands temporarily from the configured width without exceeding the available screen.</summary>
    public static double CalculateEffectiveWidth(double normalWidthDip, double? expandedWidthDip, double availableWidthDip)
    {
        if (!double.IsFinite(normalWidthDip) || normalWidthDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(normalWidthDip));
        if (expandedWidthDip is double expanded && (!double.IsFinite(expanded) || expanded <= 0))
            throw new ArgumentOutOfRangeException(nameof(expandedWidthDip));
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidthDip));
        double normal = Math.Clamp(normalWidthDip, 72, 320);
        return Math.Min(Math.Max(normal, expandedWidthDip ?? normal), availableWidthDip);
    }

    /// <summary>Includes the inner-edge expansion handle and a small approach area inside the dock.</summary>
    public static PixelRect CalculateInnerEdgeBounds(PixelRect bounds, uint dpi, DockSide side)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("The dock must have positive dimensions.", nameof(bounds));
        int width = Math.Min(bounds.Width, Math.Max(1, DipToPixels(28, dpi)));
        int height = Math.Min(bounds.Height, Math.Max(1, DipToPixels(90, dpi)));
        int top = bounds.Top + (bounds.Height - height) / 2;
        return side == DockSide.Left
            ? new PixelRect(bounds.Right - width, top, bounds.Right, top + height)
            : new PixelRect(bounds.Left, top, bounds.Left + width, top + height);
    }

    public static PixelRect CalculateBounds(PixelRect monitorBounds, uint dpi, double widthDip, DockSide side, string dockFrameStyle = "Edge", double floatingTopInsetDip = 8, double floatingBottomInsetDip = 8)
    {
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            throw new ArgumentException("The monitor must have positive dimensions.", nameof(monitorBounds));

        bool floating = IsFloatingStyle(dockFrameStyle);
        if (!double.IsFinite(floatingTopInsetDip) || floatingTopInsetDip < 0)
            throw new ArgumentOutOfRangeException(nameof(floatingTopInsetDip));
        if (!double.IsFinite(floatingBottomInsetDip) || floatingBottomInsetDip < 0)
            throw new ArgumentOutOfRangeException(nameof(floatingBottomInsetDip));
        int gap = floating ? Math.Max(0, DipToPixels(8, dpi)) : 0;
        int horizontalGap = Math.Min(gap, (monitorBounds.Width - 1) / 2);
        int topGap = floating ? Math.Max(0, DipToPixels(floatingTopInsetDip, dpi)) : 0;
        int bottomGap = floating ? Math.Max(0, DipToPixels(floatingBottomInsetDip, dpi)) : 0;
        // Preserve at least one pixel when the requested insets exceed the
        // monitor. Proportional scaling retains symmetric defaults.
        int maxVerticalGap = Math.Max(0, monitorBounds.Height - 1);
        if (topGap + bottomGap > maxVerticalGap)
        {
            double scale = maxVerticalGap / (double)(topGap + bottomGap);
            topGap = (int)Math.Floor(topGap * scale);
            bottomGap = (int)Math.Floor(bottomGap * scale);
        }
        var width = Math.Clamp(DipToPixels(widthDip, dpi), 1, monitorBounds.Width - 2 * horizontalGap);
        var left = side == DockSide.Left ? monitorBounds.Left + horizontalGap : monitorBounds.Right - horizontalGap - width;
        return new PixelRect(left, monitorBounds.Top + topGap, left + width, monitorBounds.Bottom - bottomGap);
    }

    public static bool IsFloatingStyle(string? dockFrameStyle)
        => string.Equals(dockFrameStyle, "Floating", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dockFrameStyle, "FloatingSquare", StringComparison.OrdinalIgnoreCase);

    /// <summary>A visible slice of the dock; zero pixels produces an empty region.</summary>
    public static PixelRect CalculateRevealBounds(PixelRect bounds, DockSide side, int visiblePixels = 0)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("The dock must have positive dimensions.", nameof(bounds));
        var width = Math.Clamp(visiblePixels, 0, bounds.Width);
        return side == DockSide.Left
            ? bounds with { Right = bounds.Left + width }
            : bounds with { Left = bounds.Right - width };
    }

    /// <summary>Moves the whole dock off screen, with a physical-pixel margin for the DWM frame.</summary>
    public static PixelRect CalculateSlideBounds(PixelRect bounds, DockSide side, double visibleFraction, int offscreenMarginPixels = 2)
        => CalculateSlideBounds(bounds, bounds, side, visibleFraction, offscreenMarginPixels);

    /// <summary>Slides to the actual monitor edge, including any inset between a floating dock and that edge.</summary>
    public static PixelRect CalculateSlideBounds(PixelRect bounds, PixelRect monitorBounds, DockSide side, double visibleFraction, int offscreenMarginPixels = 2)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("The dock must have positive dimensions.", nameof(bounds));
        if (!double.IsFinite(visibleFraction))
            throw new ArgumentOutOfRangeException(nameof(visibleFraction));
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            throw new ArgumentException("The monitor must have positive dimensions.", nameof(monitorBounds));
        if (offscreenMarginPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(offscreenMarginPixels));
        int distance = side == DockSide.Left
            ? bounds.Right - monitorBounds.Left + offscreenMarginPixels
            : monitorBounds.Right - bounds.Left + offscreenMarginPixels;
        int travel = (int)Math.Round(distance * (1 - Math.Clamp(visibleFraction, 0, 1)), MidpointRounding.AwayFromZero);
        int offset = side == DockSide.Left ? -travel : travel;
        return bounds with { Left = bounds.Left + offset, Right = bounds.Right + offset };
    }

    /// <summary>A physical-pixel strip at the actual screen edge, independent of the dock's inset and region.</summary>
    public static PixelRect CalculateTriggerBounds(PixelRect monitorBounds, DockSide side, int triggerPixels = 3)
    {
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            throw new ArgumentException("The monitor must have positive dimensions.", nameof(monitorBounds));
        int width = Math.Clamp(triggerPixels, 1, monitorBounds.Width);
        return side == DockSide.Left
            ? monitorBounds with { Right = monitorBounds.Left + width }
            : monitorBounds with { Left = monitorBounds.Right - width };
    }

    /// <summary>Keeps a revealed dock open while the pointer crosses its outer or vertical floating gaps.</summary>
    public static PixelRect CalculateHoverBounds(PixelRect bounds, PixelRect monitorBounds, DockSide side)
        => side == DockSide.Left
            ? new PixelRect(monitorBounds.Left, monitorBounds.Top, bounds.Right, monitorBounds.Bottom)
            : new PixelRect(bounds.Left, monitorBounds.Top, monitorBounds.Right, monitorBounds.Bottom);

    /// <summary>Clips to the target monitor so a sliding dock cannot appear on an adjacent screen.</summary>
    public static PixelRect CalculateViewportRegion(PixelRect window, PixelRect viewport)
    {
        int left = Math.Clamp(viewport.Left - window.Left, 0, window.Width);
        int right = Math.Clamp(viewport.Right - window.Left, left, window.Width);
        int top = Math.Clamp(viewport.Top - window.Top, 0, window.Height);
        int bottom = Math.Clamp(viewport.Bottom - window.Top, top, window.Height);
        return new PixelRect(left, top, right, bottom);
    }
}
