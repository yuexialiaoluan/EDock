using EDock.Models;
using EDock.Services;
using System.Security.AccessControl;

var suite = new CoreSuite();
suite.Run("Directional navigation follows real rows and columns through incomplete normal grids", () =>
{
    DockNavigationCell[] cells = [new(0, 0), new(80, 0), new(160, 0), new(0, 70), new(80, 70)];
    Check.Equal(3, DockNavigationLogic.Neighbor(cells, 0, 1, false));
    Check.Equal(0, DockNavigationLogic.Neighbor(cells, 3, 1, false));
    Check.Equal(2, DockNavigationLogic.Neighbor(cells, 2, 1, false));
    Check.Equal(3, DockNavigationLogic.Neighbor(cells, 4, 1, true));
    Check.Equal(4, DockNavigationLogic.Neighbor(cells, 3, -1, true));
    Check.Equal(1, DockNavigationLogic.Neighbor(cells, 0, 1, true));
});
suite.Run("Expanded navigation stays in the visual row across a short final column", () =>
{
    DockNavigationCell[] cells = [new(0, 0), new(0, 70), new(0, 140), new(80, 0), new(80, 70)];
    Check.Equal(3, DockNavigationLogic.Neighbor(cells, 0, 1, true));
    Check.Equal(4, DockNavigationLogic.Neighbor(cells, 1, 1, true));
    Check.Equal(2, DockNavigationLogic.Neighbor(cells, 2, 1, true));
    Check.Equal(3, DockNavigationLogic.Neighbor(cells, 4, 1, false));
});
suite.Run("Repeated copies preserve their current visual column rather than logical item index", () =>
{
    // Three distinct copies of one logical item can occupy different columns.
    DockNavigationCell[] cells = [new(0, 0), new(80, 0), new(160, 0), new(0, 70), new(80, 70), new(160, 70), new(0, 140), new(80, 140), new(160, 140)];
    Check.Equal(8, DockNavigationLogic.Neighbor(cells, 5, 1, false));
    Check.Equal(3, DockNavigationLogic.Neighbor(cells, 5, 1, true));
    Check.Equal(4, DockNavigationLogic.Neighbor(cells, 5, -1, true));
});
suite.Run("Empty and singleton navigation are safe", () =>
{
    Check.Equal(-1, DockNavigationLogic.Neighbor([], 0, 1, true));
    Check.Equal(0, DockNavigationLogic.Neighbor([new(0, 0)], 0, -1, false));
});
suite.Run("DIP scaling at 96, 144, 192 DPI", () =>
{
    Check.Equal(40, EdgeGeometry.DipToPixels(40, 96));
    Check.Equal(60, EdgeGeometry.DipToPixels(40, 144));
    Check.Equal(80, EdgeGeometry.DipToPixels(40, 192));
    Check.Equal(40, EdgeGeometry.DipToPixels(40, 0));
});
suite.Run("Full monitor bounds touch top and bottom at three scales", () =>
{
    var screen = new PixelRect(0, 0, 1920, 1080);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    {
        int width = EdgeGeometry.DipToPixels(80, dpi);
        Check.Equal(new PixelRect(0, 0, width, 1080),
            EdgeGeometry.CalculateBounds(screen, dpi, 80, DockSide.Left));
        Check.Equal(new PixelRect(1920 - width, 0, 1920, 1080),
            EdgeGeometry.CalculateBounds(screen, dpi, 80, DockSide.Right));
    }
});
suite.Run("Negative monitor coordinates remain absolute", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    Check.Equal(new PixelRect(-1920, -200, -1840, 880),
        EdgeGeometry.CalculateBounds(screen, 96, 80, DockSide.Left));
    Check.Equal(new PixelRect(-80, -200, 0, 880),
        EdgeGeometry.CalculateBounds(screen, 96, 80, DockSide.Right));
});
suite.Run("Rectangle hit testing excludes right and bottom edges", () =>
{
    var rect = new PixelRect(-10, -20, 10, 20);
    Check.True(rect.Contains(-10, -20));
    Check.True(rect.Contains(9, 19));
    Check.False(rect.Contains(10, 0));
    Check.False(rect.Contains(0, 20));
    Check.False(rect.Contains(-11, 0));
    Check.Equal(20, rect.Width);
    Check.Equal(40, rect.Height);
});
suite.Run("Dock width is clamped inside full monitor bounds", () =>
{
    var area = new PixelRect(-120, 0, 120, 800);
    Check.Equal(240, EdgeGeometry.CalculateBounds(area, 192, 1000, DockSide.Left).Width);
    Check.Equal(1, EdgeGeometry.CalculateBounds(area, 96, -10, DockSide.Right).Width);
});
suite.Run("Reveal clips use the requested physical width at each edge", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    {
        var left = EdgeGeometry.CalculateRevealBounds(
            EdgeGeometry.CalculateBounds(screen, dpi, 88, DockSide.Left), DockSide.Left, 3);
        Check.Equal(new PixelRect(-1920, -200, -1917, 880), left);
        Check.True(left.Contains(-1920, -200));
        Check.True(left.Contains(-1918, 879));
        Check.False(left.Contains(-1917, 0));
        Check.False(left.Contains(-1920, 880));
        Check.Equal(new PixelRect(-3, -200, 0, 880), EdgeGeometry.CalculateRevealBounds(
            EdgeGeometry.CalculateBounds(screen, dpi, 88, DockSide.Right), DockSide.Right, 3));
    }
});
suite.Run("Wide dock settings scale without introducing vertical gaps", () =>
{
    var screen = new PixelRect(0, 0, 3840, 2160);
    foreach (double widthDip in new double[] { 72, 160, 240, 320 })
    {
        var left = EdgeGeometry.CalculateBounds(screen, 192, widthDip, DockSide.Left);
        var right = EdgeGeometry.CalculateBounds(screen, 192, widthDip, DockSide.Right);
        Check.Equal((int)(widthDip * 2), left.Width);
        Check.Equal(left.Width, right.Width);
        Check.Equal(0, left.Top);
        Check.Equal(2160, left.Bottom);
        Check.Equal(0, right.Top);
        Check.Equal(2160, right.Bottom);
    }
});
suite.Run("Small monitor and invalid geometry inputs are handled", () =>
{
    var tiny = new PixelRect(15, -8, 16, -7);
    Check.Equal(tiny, EdgeGeometry.CalculateBounds(tiny, 192, 320, DockSide.Right));
    Check.Equal(new PixelRect(15, -8, 15, -7), EdgeGeometry.CalculateRevealBounds(tiny, DockSide.Left));
    Check.Throws<ArgumentException>(() => EdgeGeometry.CalculateBounds(new PixelRect(0, 0, 0, 1), 96, 88, DockSide.Left));
    Check.Throws<ArgumentException>(() => EdgeGeometry.CalculateBounds(new PixelRect(0, 1, 1, 1), 96, 88, DockSide.Right));
    Check.Throws<ArgumentOutOfRangeException>(() => EdgeGeometry.DipToPixels(double.NaN, 96));
    Check.Throws<ArgumentOutOfRangeException>(() => EdgeGeometry.DipToPixels(double.PositiveInfinity, 96));
});
suite.Run("Slide geometry hides whole windows beyond the physical screen edge", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    {
        var shown = EdgeGeometry.CalculateBounds(screen, dpi, 88, side);
        var hidden = EdgeGeometry.CalculateSlideBounds(shown, side, 0);
        var middle = EdgeGeometry.CalculateSlideBounds(shown, side, .5);
        Check.Equal(shown, EdgeGeometry.CalculateSlideBounds(shown, side, 1));
        Check.Equal(shown, EdgeGeometry.CalculateSlideBounds(shown, side, 2));
        Check.Equal(hidden, EdgeGeometry.CalculateSlideBounds(shown, side, -1));
        Check.Equal(shown.Width, hidden.Width);
        Check.Equal(shown.Height, hidden.Height);
        Check.Equal(shown.Top, hidden.Top);
        Check.Equal(shown.Bottom, hidden.Bottom);
        if (side == DockSide.Left)
        {
            Check.Equal(screen.Left - 2, hidden.Right);
            Check.True(hidden.Left < middle.Left && middle.Left < shown.Left);
        }
        else
        {
            Check.Equal(screen.Right + 2, hidden.Left);
            Check.True(shown.Left < middle.Left && middle.Left < hidden.Left);
        }
        var clip = EdgeGeometry.CalculateViewportRegion(hidden, screen);
        Check.Equal(0, clip.Width);
        Check.Equal(screen.Height, clip.Height);
    }
    var narrow = new PixelRect(5, 6, 7, 106);
    Check.Equal(new PixelRect(1, 6, 3, 106), EdgeGeometry.CalculateSlideBounds(narrow, DockSide.Left, 0));
    Check.Throws<ArgumentOutOfRangeException>(() =>
        EdgeGeometry.CalculateSlideBounds(narrow, DockSide.Left, double.NaN));
    Check.Throws<ArgumentException>(() =>
        EdgeGeometry.CalculateSlideBounds(new PixelRect(0, 0, 0, 10), DockSide.Right, 0));
    Check.Throws<ArgumentOutOfRangeException>(() =>
        EdgeGeometry.CalculateSlideBounds(narrow, DockSide.Left, 0, -1));
});
suite.Run("Invisible slide endpoints retain independent reachable edge triggers", () =>
{
    foreach (var screen in new[]
    {
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(-2560, -300, 0, 1300),
        new PixelRect(1920, 120, 4480, 1560),
        new PixelRect(-3, 7, -2, 8),
        new PixelRect(15, -8, 17, -5)
    })
    foreach (uint dpi in new uint[] { 0, 96, 120, 144, 192 })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    foreach (string frame in new[] { "Edge", "Floating" })
    foreach (double width in new[] { 72d, 320d, 9999d })
    {
        var shown = EdgeGeometry.CalculateBounds(screen, dpi, width, side, frame);
        var hidden = EdgeGeometry.CalculateSlideBounds(shown, screen, side, 0);
        Check.Equal(shown.Width, hidden.Width);
        Check.Equal(shown.Height, hidden.Height);
        Check.Equal(shown.Top, hidden.Top);
        Check.True(side == DockSide.Left ? hidden.Right <= screen.Left - 2 : hidden.Left >= screen.Right + 2);
        var hiddenClip = EdgeGeometry.CalculateViewportRegion(hidden, screen);
        Check.Equal(0, hiddenClip.Width);
        var trigger = EdgeGeometry.CalculateTriggerBounds(screen, side);
        Check.Equal(Math.Min(3, screen.Width), trigger.Width);
        Check.Equal(screen.Height, trigger.Height);
        int edgeX = side == DockSide.Left ? screen.Left : screen.Right - 1;
        Check.True(trigger.Contains(edgeX, screen.Top));
        Check.True(trigger.Contains(edgeX, screen.Bottom - 1));
        Check.False(hidden.Contains(edgeX, screen.Top));
        Check.Equal(shown, EdgeGeometry.CalculateSlideBounds(shown, screen, side, 1));

        int previousX = hidden.Left;
        foreach (double fraction in new[] { .001, .01, .1, .25, .5, .75, .9, .99, 1d })
        {
            var moving = EdgeGeometry.CalculateSlideBounds(shown, screen, side, fraction);
            Check.True(side == DockSide.Left ? moving.Left >= previousX : moving.Left <= previousX);
            previousX = moving.Left;
            var clip = EdgeGeometry.CalculateViewportRegion(moving, screen);
            Check.True(clip.Left >= 0 && clip.Right <= moving.Width && clip.Width >= 0);
            Check.True(clip.Top >= 0 && clip.Bottom <= moving.Height && clip.Height >= 0);
            if (clip.Width > 0 && clip.Height > 0)
            {
                Check.True(moving.Left + clip.Left >= screen.Left);
                Check.True(moving.Left + clip.Right <= screen.Right);
                Check.True(moving.Top + clip.Top >= screen.Top);
                Check.True(moving.Top + clip.Bottom <= screen.Bottom);
            }
        }
    }
});
suite.Run("Reveal clipping permits fully empty regions and restores the whole dock", () =>
{
    foreach (var bounds in new[] { new PixelRect(-100, -20, -12, 400), new PixelRect(15, -8, 16, -7) })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    {
        var empty = EdgeGeometry.CalculateRevealBounds(bounds, side);
        Check.Equal(0, empty.Width);
        Check.Equal(bounds.Height, empty.Height);
        Check.False(empty.Contains(empty.Left, empty.Top));
        Check.Equal(empty, EdgeGeometry.CalculateRevealBounds(bounds, side, -1));
        Check.Equal(bounds, EdgeGeometry.CalculateRevealBounds(bounds, side, bounds.Width));
        Check.Equal(bounds, EdgeGeometry.CalculateRevealBounds(bounds, side, int.MaxValue));
        for (int width = 0; width <= bounds.Width; width++)
        {
            var clip = EdgeGeometry.CalculateRevealBounds(bounds, side, width);
            Check.Equal(width, clip.Width);
            Check.Equal(bounds.Top, clip.Top);
            Check.Equal(bounds.Bottom, clip.Bottom);
            Check.True(side == DockSide.Left ? clip.Left == bounds.Left : clip.Right == bounds.Right);
        }
    }
    Check.Throws<ArgumentException>(() => EdgeGeometry.CalculateRevealBounds(new PixelRect(0, 0, 0, 1), DockSide.Left));
});
suite.Run("Slide clipping excludes neighboring monitors and preserves local coordinates", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    Check.Equal(new PixelRect(20, 0, 80, 1080), EdgeGeometry.CalculateViewportRegion(
        new PixelRect(-1940, -200, -1860, 880), screen));
    Check.Equal(new PixelRect(0, 0, 40, 1080), EdgeGeometry.CalculateViewportRegion(
        new PixelRect(-40, -200, 40, 880), screen));
    Check.Equal(new PixelRect(0, 0, 80, 1080), EdgeGeometry.CalculateViewportRegion(
        new PixelRect(-100, -200, -20, 880), screen));
    Check.Equal(0, EdgeGeometry.CalculateViewportRegion(
        new PixelRect(20, -200, 100, 880), screen).Width);
    Check.Equal(new PixelRect(0, 20, 80, 120), EdgeGeometry.CalculateViewportRegion(
        new PixelRect(-100, -220, -20, -100), screen));
});
suite.Run("Floating geometry applies eight DIP gaps at three scales and negative origins", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    {
        int gap = EdgeGeometry.DipToPixels(8, dpi);
        int width = EdgeGeometry.DipToPixels(88, dpi);
        var left = EdgeGeometry.CalculateBounds(screen, dpi, 88, DockSide.Left, "Floating");
        var right = EdgeGeometry.CalculateBounds(screen, dpi, 88, DockSide.Right, "Floating");
        Check.Equal(new PixelRect(screen.Left + gap, screen.Top + gap,
            screen.Left + gap + width, screen.Bottom - gap), left);
        Check.Equal(new PixelRect(screen.Right - gap - width, screen.Top + gap,
            screen.Right - gap, screen.Bottom - gap), right);
        foreach (var side in new[] { DockSide.Left, DockSide.Right })
        {
            Check.Equal(EdgeGeometry.CalculateBounds(screen, dpi, 88, side),
                EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Edge"));
            Check.Equal(EdgeGeometry.CalculateBounds(screen, dpi, 88, side),
                EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Unknown"));
        }
    }
    var tiny = new PixelRect(-3, 7, -2, 8);
    Check.Equal(tiny, EdgeGeometry.CalculateBounds(tiny, 192, 320, DockSide.Left, "Floating"));
    var small = new PixelRect(15, 20, 25, 30);
    Check.Equal(new PixelRect(19, 24, 21, 26),
        EdgeGeometry.CalculateBounds(small, 192, 320, DockSide.Right, "Floating"));
});
suite.Run("Floating square shares floating bounds and configurable vertical insets", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    {
        Check.True(EdgeGeometry.IsFloatingStyle("Floating"));
        Check.True(EdgeGeometry.IsFloatingStyle("fLoAtInGsQuArE"));
        Check.False(EdgeGeometry.IsFloatingStyle("Edge"));
        var roundedDefault = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Floating");
        Check.Equal(roundedDefault, EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "FloatingSquare"));

        int top = EdgeGeometry.DipToPixels(20, dpi);
        int bottom = EdgeGeometry.DipToPixels(36, dpi);
        var custom = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "FloatingSquare", 20, 36);
        Check.Equal(screen.Top + top, custom.Top);
        Check.Equal(screen.Bottom - bottom, custom.Bottom);
        Check.Equal(roundedDefault.Left, custom.Left);
        Check.Equal(roundedDefault.Right, custom.Right);

        var available = EdgeGeometry.CalculateAvailableSize(screen, dpi, "FloatingSquare", 20, 36);
        Check.Equal(EdgeGeometry.PixelsToDip(screen.Width - 2 * EdgeGeometry.DipToPixels(8, dpi), dpi), available.WidthDip);
        Check.Equal(EdgeGeometry.PixelsToDip(custom.Height, dpi), available.HeightDip);

        var noVerticalGap = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "FloatingSquare", 0, 0);
        Check.Equal(screen.Top, noVerticalGap.Top);
        Check.Equal(screen.Bottom, noVerticalGap.Bottom);
        var edgeIgnoresFloatingInsets = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Edge", 20, 36);
        Check.Equal(EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Edge"), edgeIgnoresFloatingInsets);
    }

    var cramped = new PixelRect(10, 20, 110, 30);
    var bounded = EdgeGeometry.CalculateBounds(cramped, 192, 88, DockSide.Left, "FloatingSquare", 200, 0);
    Check.True(bounded.Height >= 1);
    Check.True(bounded.Top >= cramped.Top && bounded.Bottom <= cramped.Bottom);
});
suite.Run("Floating trigger and hover bounds cover physical screen edges and outer gaps", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    {
        var bounds = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Floating");
        var trigger = EdgeGeometry.CalculateTriggerBounds(screen, side);
        Check.Equal(3, trigger.Width);
        Check.Equal(screen.Top, trigger.Top);
        Check.Equal(screen.Bottom, trigger.Bottom);
        var hover = EdgeGeometry.CalculateHoverBounds(bounds, screen, side);
        int screenEdgeX = side == DockSide.Left ? screen.Left : screen.Right - 1;
        int gapX = side == DockSide.Left ? bounds.Left - 1 : bounds.Right;
        Check.True(trigger.Contains(screenEdgeX, screen.Top));
        Check.True(trigger.Contains(screenEdgeX, screen.Bottom - 1));
        Check.True(hover.Contains(gapX, bounds.Top));
        Check.True(hover.Contains(bounds.Left, screen.Top));
        Check.True(hover.Contains(bounds.Right - 1, screen.Bottom - 1));
        Check.False(hover.Contains(gapX, screen.Bottom));
        int insideBeyondDockX = side == DockSide.Left ? bounds.Right : bounds.Left - 1;
        Check.False(hover.Contains(insideBeyondDockX, bounds.Top));
    }
});
suite.Run("Floating slide hides against the actual screen edge while Edge geometry stays compatible", () =>
{
    var screen = new PixelRect(-1920, -200, 0, 880);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    {
        var shown = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Floating");
        var hidden = EdgeGeometry.CalculateSlideBounds(shown, screen, side, 0);
        Check.Equal(shown, EdgeGeometry.CalculateSlideBounds(shown, screen, side, 1));
        Check.Equal(shown.Width, hidden.Width);
        Check.Equal(shown.Top, hidden.Top);
        Check.Equal(shown.Bottom, hidden.Bottom);
        if (side == DockSide.Left) Check.Equal(screen.Left - 2, hidden.Right);
        else Check.Equal(screen.Right + 2, hidden.Left);
        var clip = EdgeGeometry.CalculateViewportRegion(hidden, screen);
        Check.Equal(0, clip.Width);
        Check.Equal(shown.Height, clip.Height);
        var edgeShown = EdgeGeometry.CalculateBounds(screen, dpi, 88, side, "Edge");
        foreach (double fraction in new[] { 0d, .25, .5, .75, 1d })
            Check.Equal(EdgeGeometry.CalculateSlideBounds(edgeShown, side, fraction),
                EdgeGeometry.CalculateSlideBounds(edgeShown, screen, side, fraction));
    }
});
suite.Run("Expanded dock geometry preserves side anchors and restores the normal footprint", () =>
{
    var screen = new PixelRect(-2560, -300, 0, 1300);
    foreach (uint dpi in new uint[] { 96, 144, 192 })
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    foreach (string style in new[] { "Edge", "Floating" })
    {
        var normal = EdgeGeometry.CalculateBounds(screen, dpi, 176, side, style);
        var expanded = EdgeGeometry.CalculateBounds(screen, dpi, 720, side, style);
        Check.Equal(EdgeGeometry.DipToPixels(720, dpi), expanded.Width);
        Check.Equal(normal.Top, expanded.Top);
        Check.Equal(normal.Bottom, expanded.Bottom);
        if (side == DockSide.Left) Check.Equal(normal.Left, expanded.Left);
        else Check.Equal(normal.Right, expanded.Right);
        Check.Equal(normal, EdgeGeometry.CalculateBounds(screen, dpi, 176, side, style));
        var hidden = EdgeGeometry.CalculateSlideBounds(expanded, screen, side, 0);
        Check.Equal(0, EdgeGeometry.CalculateViewportRegion(hidden, screen).Width);
        Check.Equal(expanded.Height, hidden.Height);
        Check.Equal(expanded, EdgeGeometry.CalculateSlideBounds(expanded, screen, side, 1));
        Check.Equal(3, EdgeGeometry.CalculateTriggerBounds(screen, side).Width);
    }
});
suite.Run("Expanded dock width clamps to the monitor and preserves floating margins", () =>
{
    var screen = new PixelRect(-600, 100, 600, 900);
    foreach (var side in new[] { DockSide.Left, DockSide.Right })
    foreach (string style in new[] { "Edge", "Floating" })
    {
        int margin = style == "Floating" ? 16 : 0;
        var expanded = EdgeGeometry.CalculateBounds(screen, 192, 2000, side, style);
        Check.Equal(screen.Width - 2 * margin, expanded.Width);
        Check.Equal(screen.Left + margin, expanded.Left);
        Check.Equal(screen.Right - margin, expanded.Right);
        Check.Equal(screen.Top + margin, expanded.Top);
        Check.Equal(screen.Bottom - margin, expanded.Bottom);
        foreach (double fraction in new[] { 0d, .25, .5, .75, 1d })
        {
            var moving = EdgeGeometry.CalculateSlideBounds(expanded, screen, side, fraction);
            var clip = EdgeGeometry.CalculateViewportRegion(moving, screen);
            Check.Equal(expanded.Width, moving.Width);
            Check.True(clip.Width >= 0 && clip.Width <= expanded.Width);
            Check.True(clip.Left >= 0 && clip.Right <= moving.Width);
        }
        var hover = EdgeGeometry.CalculateHoverBounds(expanded, screen, side);
        Check.True(hover.Contains(side == DockSide.Left ? screen.Left : screen.Right - 1, screen.Top));
        Check.True(hover.Contains(expanded.Left, screen.Bottom - 1));
    }
});
suite.Run("Animation progresses by elapsed time and supports smooth reversals", () =>
{
    var animation = new EdgeAnimation();
    Check.Equal(0d, animation.Value);
    Check.False(animation.IsRunning);
    animation.Start(true, 400, true, 1000);
    Check.True(animation.IsRunning);
    animation.Update(1100);
    double first = animation.Value;
    Check.True(first > 0 && first < .5);
    animation.Update(1200);
    Check.Equal(.5, animation.Value);
    animation.Start(false, 200, true, 1200);
    Check.Equal(.5, animation.Value);
    animation.Update(1300);
    Check.Equal(.25, animation.Value);
    Check.True(animation.IsRunning);
    animation.Update(1400);
    Check.Equal(0d, animation.Value);
    Check.False(animation.IsRunning);
    Check.False(animation.Update(1500));
    animation.Start(true, 400, true, 2000);
    animation.Update(2200);
    animation.Finish();
    Check.Equal(1d, animation.Value);
    Check.False(animation.IsRunning);
});
suite.Run("Animation supports reduced motion, duration bounds and invalid style fallback", () =>
{
    var animation = new EdgeAnimation();
    animation.Start(true, 250, false, 0);
    Check.Equal(1d, animation.Value);
    Check.False(animation.IsRunning);
    animation.Start(false, 0, true, 100);
    animation.Update(140);
    Check.Equal(.5, animation.Value);
    animation.Update(180);
    Check.Equal(0d, animation.Value);
    animation.Start(true, 5000, true, 200);
    animation.Update(950);
    Check.Equal(.5, animation.Value);
    animation.Update(1700);
    Check.Equal(1d, animation.Value);
    Check.Equal(80, EdgeAnimation.ClampDuration(int.MinValue));
    Check.Equal(1500, EdgeAnimation.ClampDuration(int.MaxValue));
    Check.Equal(250, EdgeAnimation.ClampDuration(250));
    Check.Equal(EdgeAnimationStyle.Slide, EdgeAnimation.ParseStyle(null));
    Check.Equal(EdgeAnimationStyle.Slide, EdgeAnimation.ParseStyle("invalid"));
    Check.Equal(EdgeAnimationStyle.Slide, EdgeAnimation.ParseStyle("sLiDe"));
    Check.Equal(EdgeAnimationStyle.Reveal, EdgeAnimation.ParseStyle("reveal"));
    Check.Equal(EdgeAnimationStyle.None, EdgeAnimation.ParseStyle("NONE"));
});
suite.Run("Missing settings return defaults without touching other folders", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = store.Load();
        Check.True(settings.LeftEnabled);
        Check.True(settings.RightEnabled);
        Check.True(settings.AutoHide);
        Check.Equal(88, settings.DockWidth);
        Check.Equal(40d, settings.IconSize);
        Check.Equal(8d, settings.IconSpacing);
        Check.Equal(1, settings.NormalColumns);
        Check.Equal(0d, settings.FooterOffset);
        Check.True(settings.ShowShortcutNames);
        Check.Equal("ED", settings.HeaderText);
        Check.Equal("Icon", settings.ShortcutDisplayMode);
        Check.Equal("Edge", settings.DockFrameStyle);
        Check.Equal("Solid", settings.BackgroundStyle);
        Check.Equal(string.Empty, settings.BackgroundImagePath);
        Check.Equal(.82, settings.BackgroundOpacity);
        Check.Equal(.35, settings.BackgroundImageOverlayOpacity);
        Check.False(settings.SeparateSideSettings);
        Check.Equal(250, settings.RevealDurationMs);
        Check.Equal(250, settings.HideDurationMs);
        Check.Equal("Slide", settings.AnimationStyle);
        Check.True(settings.LoopScrolling);
        Check.True(settings.LeftSettings is null && settings.RightSettings is null);
        Check.True(settings.LeftAutoHideOverride is null && settings.RightAutoHideOverride is null);
        Check.Equal(Path.Combine(directory, "settings.json"), store.SettingsFilePath);
        Check.Equal(0, settings.LeftShortcuts.Count);
        Check.Equal(0, settings.RightShortcuts.Count);
    });
});
suite.Run("Settings and Unicode shortcut metadata round trip", () =>
{
    WithTempDirectory(directory =>
    {
        var settings = new DockSettings
        {
            LeftEnabled = false, RightEnabled = true, DockWidth = 104, AutoHide = false,
            HideDelayMs = 900, RevealDelayMs = 180, OpenOnStartup = true, Theme = "Dark",
            ShowShortcutNames = false, HeaderText = "工作区", ShortcutDisplayMode = "Wide",
            BackgroundStyle = "Image", BackgroundImagePath = @"C:\背景 图片\风景.jpg",
            BackgroundOpacity = .64, BackgroundImageOverlayOpacity = .23
        };
        settings.LeftShortcuts.Add(new ShortcutItem
        {
            Id = "roundtrip-left", Name = "工作文件夹", TargetPath = @"C:\工作 文件\项目.lnk",
            Arguments = "--name \"two words\"", WorkingDirectory = @"C:\工作 文件", IconPath = @"C:\图标.ico",
            CustomIconPath = @"C:\自定义 图片\横图.png", ImageCrop = "Bottom"
        });
        settings.RightShortcuts.Add(new ShortcutItem
        { Id = "roundtrip-right", Name = "Docs", TargetPath = "https://example.com/docs?q=edge" });
        var store = new SettingsStore(directory);
        store.Save(settings);
        var loaded = new SettingsStore(directory).Load();
        Check.False(loaded.LeftEnabled);
        Check.True(loaded.RightEnabled);
        Check.Equal(104, loaded.DockWidth);
        Check.False(loaded.AutoHide);
        Check.Equal(900, loaded.HideDelayMs);
        Check.Equal(180, loaded.RevealDelayMs);
        Check.True(loaded.OpenOnStartup);
        Check.Equal("Dark", loaded.Theme);
        Check.False(loaded.ShowShortcutNames);
        Check.Equal("工作区", loaded.HeaderText);
        Check.Equal("Wide", loaded.ShortcutDisplayMode);
        Check.Equal("Image", loaded.BackgroundStyle);
        Check.Equal(settings.BackgroundImagePath, loaded.BackgroundImagePath);
        Check.Equal(.64, loaded.BackgroundOpacity);
        Check.Equal(.23, loaded.BackgroundImageOverlayOpacity);
        Check.Equal(1, loaded.LeftShortcuts.Count);
        Check.Equal(1, loaded.RightShortcuts.Count);
        var expected = settings.LeftShortcuts[0];
        var actual = loaded.LeftShortcuts[0];
        Check.Equal(expected.Id, actual.Id);
        Check.Equal(expected.Name, actual.Name);
        Check.Equal(expected.TargetPath, actual.TargetPath);
        Check.Equal(expected.Arguments, actual.Arguments);
        Check.Equal(expected.WorkingDirectory, actual.WorkingDirectory);
        Check.Equal(expected.IconPath, actual.IconPath);
        Check.Equal(expected.CustomIconPath, actual.CustomIconPath);
        Check.Equal(expected.ImageCrop, actual.ImageCrop);
    });
});
suite.Run("Text display mode persists in shared and independent side options without changing icon label preferences", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings
        {
            ShortcutDisplayMode = "Text", ShowShortcutNames = false, SeparateSideSettings = true,
            LeftSettings = new DockOptions { ShortcutDisplayMode = "Text", ShowShortcutNames = false },
            RightSettings = new DockOptions { ShortcutDisplayMode = "Wide", ShowShortcutNames = false }
        };
        settings.LeftShortcuts.Add(new ShortcutItem { Name = "工作 应用", TargetPath = @"C:\工作\应用.exe" });
        store.Save(settings);
        var loaded = store.Load();
        Check.True(store.LastError is null);
        Check.Equal("Text", loaded.ShortcutDisplayMode);
        Check.Equal("Text", loaded.GetSideOptions(DockSide.Left).ShortcutDisplayMode);
        Check.Equal("Wide", loaded.GetSideOptions(DockSide.Right).ShortcutDisplayMode);
        Check.False(loaded.ShowShortcutNames);
        Check.False(loaded.GetSideOptions(DockSide.Left).ShowShortcutNames);
        Check.Equal("工作 应用", loaded.LeftShortcuts[0].Name);
        var copied = loaded.GetSideOptions(DockSide.Left).Copy();
        var target = new DockOptions();
        target.CopyFrom(copied);
        Check.Equal("Text", target.ShortcutDisplayMode);
        Check.False(target.ShowShortcutNames);
        loaded.SeparateSideSettings = false;
        store.Save(loaded);
        loaded = store.Load();
        Check.Equal("Text", loaded.GetSideOptions(DockSide.Left).ShortcutDisplayMode);
        Check.Equal("Text", loaded.GetSideOptions(DockSide.Right).ShortcutDisplayMode);
        Check.Equal("Wide", loaded.RightSettings!.ShortcutDisplayMode);
    });
});
suite.Run("Repeated atomic replacement leaves complete JSON and no temp files", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings();
        for (int index = 0; index < 30; index++)
        {
            settings.HideDelayMs = 500 + index;
            store.Save(settings);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
            Check.Equal(500 + index, new SettingsStore(directory).Load().HideDelayMs);
        }
        Check.False(Directory.EnumerateFiles(directory).Any(path =>
            path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)));
        var blocker = new FileStream(store.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            var previous = settings.HideDelayMs;
            settings.HideDelayMs = 1200;
            bool rejected = false;
            try { store.Save(settings); } catch (IOException) { rejected = true; }
            Check.True(rejected);
            blocker.Dispose();
            Check.Equal(previous, new SettingsStore(directory).Load().HideDelayMs);
        }
        finally { blocker.Dispose(); }
    });
});
suite.Run("Corrupt settings are backed up and recover to defaults", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        Directory.CreateDirectory(directory);
        const string corrupt = "{ invalid json";
        File.WriteAllText(store.SettingsFilePath, corrupt);
        var loaded = store.Load();
        Check.True(loaded.AutoHide);
        Check.True(loaded.LeftEnabled);
        Check.True(loaded.RightEnabled);
        Check.True(Directory.EnumerateFiles(directory).Any(path =>
            path != store.SettingsFilePath && File.ReadAllText(path) == corrupt));
        store.Save(loaded);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.SettingsFilePath));
        Check.True(new SettingsStore(directory).Load().AutoHide);
    });
});
suite.Run("Shortcut import preserves paths with spaces and document extensions", () =>
{
    WithTempDirectory(directory =>
    {
        string target = Path.Combine(directory, "Project document.txt");
        File.WriteAllText(target, "test");
        var item = ShortcutService.ImportPath(target);
        Check.Equal(target, item.TargetPath);
        Check.True(!string.IsNullOrWhiteSpace(item.Name));
        Check.True(!string.IsNullOrWhiteSpace(item.Id));
        var folder = ShortcutService.ImportPath(directory);
        Check.Equal(directory, folder.TargetPath);
    });
});
suite.Run("Settings preserve both disabled docks and clamp width to UI limits", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings { LeftEnabled = false, RightEnabled = false, DockWidth = 20 };
        store.Save(settings);
        var loaded = store.Load();
        Check.False(loaded.LeftEnabled);
        Check.False(loaded.RightEnabled);
        Check.Equal(72, loaded.DockWidth);
        settings.DockWidth = 500;
        store.Save(settings);
        Check.Equal(320, store.Load().DockWidth);
        settings.DockWidth = double.NaN;
        store.Save(settings);
        Check.Equal(88, store.Load().DockWidth);
    });
});
suite.Run("Both disabled docks preserve quick mode preferences and shortcuts across reloads", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        foreach (string side in new[] { "Left", "Right", "Both" })
        {
            var settings = new DockSettings
            {
                LeftEnabled = false, RightEnabled = false,
                QuickModeEnabled = true, QuickModeSide = side
            };
            settings.LeftShortcuts.Add(new ShortcutItem { Id = "left-kept", Name = "Left", TargetPath = @"C:\Apps\left.exe" });
            settings.RightShortcuts.Add(new ShortcutItem { Id = "right-kept", Name = "Right", TargetPath = @"C:\Apps\right.exe" });
            store.Save(settings);
            var loaded = new SettingsStore(directory).Load();
            Check.False(loaded.LeftEnabled);
            Check.False(loaded.RightEnabled);
            Check.True(loaded.QuickModeEnabled);
            Check.Equal(side, loaded.QuickModeSide);
            Check.Equal("left-kept", loaded.LeftShortcuts.Single().Id);
            Check.Equal("right-kept", loaded.RightShortcuts.Single().Id);
            loaded.RightEnabled = true;
            store.Save(loaded);
            loaded = new SettingsStore(directory).Load();
            Check.False(loaded.LeftEnabled);
            Check.True(loaded.RightEnabled);
            Check.Equal("left-kept", loaded.LeftShortcuts.Single().Id);
            Check.Equal("right-kept", loaded.RightShortcuts.Single().Id);
        }
    });
});
suite.Run("Legacy settings preserve shortcuts and gain appearance defaults", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.SettingsFilePath, """
            {
              "DockWidth": 144, "AutoHide": false, "Theme": "Light",
              "LeftShortcuts": [{ "Id": "legacy", "Name": "旧版快捷方式", "TargetPath": "C:\\旧版\\应用.lnk", "IconPath": "C:\\旧版\\图标.ico" }]
            }
            """);
        var loaded = store.Load();
        Check.True(store.LastError is null);
        Check.Equal(144d, loaded.DockWidth);
        Check.False(loaded.AutoHide);
        Check.Equal("Light", loaded.Theme);
        Check.True(loaded.ShowShortcutNames);
        Check.Equal("ED", loaded.HeaderText);
        Check.Equal("Icon", loaded.ShortcutDisplayMode);
        Check.Equal("Solid", loaded.BackgroundStyle);
        Check.Equal(.82, loaded.BackgroundOpacity);
        Check.Equal(.35, loaded.BackgroundImageOverlayOpacity);
        Check.False(loaded.SeparateSideSettings);
        Check.Equal(250, loaded.GetSideOptions(DockSide.Left).RevealDurationMs);
        Check.Equal(250, loaded.GetSideOptions(DockSide.Right).HideDurationMs);
        Check.Equal("Slide", loaded.AnimationStyle);
        Check.True(loaded.LoopScrolling);
        Check.Equal(144d, loaded.GetSideOptions(DockSide.Left).DockWidth);
        Check.Equal(144d, loaded.GetSideOptions(DockSide.Right).DockWidth);
        Check.Equal(1, loaded.LeftShortcuts.Count);
        Check.Equal(@"C:\旧版\应用.lnk", loaded.LeftShortcuts[0].TargetPath);
        Check.Equal(@"C:\旧版\图标.ico", loaded.LeftShortcuts[0].IconPath);
        Check.Equal(string.Empty, loaded.LeftShortcuts[0].CustomIconPath);
        Check.Equal("Center", loaded.LeftShortcuts[0].ImageCrop);
        Check.Equal("Edge", loaded.DockFrameStyle);
        store.Save(loaded);
        Check.Equal("legacy", store.Load().LeftShortcuts[0].Id);
        Check.Equal(1, Directory.EnumerateFiles(directory).Count());
    });
});
suite.Run("Default icon layout retains the existing single column footprint", () =>
{
    var options = new DockOptions();
    Check.Equal(88d, DockLayoutLogic.PreferredWidth(options));
    Check.Equal(88d, DockLayoutLogic.NormalWidth(options, 1920));
    Check.Equal(76d, DockLayoutLogic.ColumnWidth(options, 1920));
    Check.Equal(40d, DockLayoutLogic.IconHeight(options, 76));
    Check.Equal(73d, DockLayoutLogic.TileHeight(options, 76));
    options.ShowShortcutNames = false;
    Check.Equal(54d, DockLayoutLogic.TileHeight(options, 76));
    options.ShortcutDisplayMode = "Wide";
    Check.Equal(42d, DockLayoutLogic.IconHeight(options, 76));
    options.ShowShortcutNames = true;
    Check.Equal(78d, DockLayoutLogic.TileHeight(options, 76));
    Check.Equal(1, DockLayoutLogic.FooterColumns(88));
    var footer = DockLayoutLogic.Footer(options, 88, 1080, 78);
    Check.Equal(12d, footer.BottomMargin);
    Check.Equal(0d, footer.BottomSpace);
    Check.Equal(840d, footer.ViewportHeight);
});
suite.Run("Normal grids fit requested columns and icon sizes beyond the legacy width limit", () =>
{
    foreach (int columns in new[] { 1, 2, 3, 6 })
    foreach (double iconSize in new[] { 16d, 40d, 128d })
    foreach (double spacing in new[] { 0d, 8d, 64d })
    {
        var options = new DockOptions { NormalColumns = columns, IconSize = iconSize, IconSpacing = spacing };
        double width = DockLayoutLogic.NormalWidth(options, 1920);
        double column = DockLayoutLogic.ColumnWidth(options, 1920);
        double gap = DockLayoutLogic.EffectiveSpacing(options, 1920);
        Check.True(width >= 88 && width <= 1920);
        Check.Equal(iconSize, DockLayoutLogic.IconHeight(options, column));
        Check.Equal(spacing, gap);
        Check.Near(width - 12, columns * column + (columns - 1) * gap, .00001);
        if (columns == 6 && iconSize == 128) Check.True(width > 320);
    }
    var wide = new DockOptions { IconSize = 40, ShortcutDisplayMode = "Wide" };
    double baseline = DockLayoutLogic.IconHeight(wide, 76);
    wide.IconSize = 80;
    Check.Equal(baseline * 2, DockLayoutLogic.IconHeight(wide, 76));
});
suite.Run("Text rows omit icon space and retain a readable height with hidden icon labels", () =>
{
    foreach (bool showNames in new[] { false, true })
    foreach (double iconSize in new[] { 16d, 40d, 128d })
    foreach (double columnWidth in new[] { 1d, 60d, 76d, 308d })
    {
        var options = new DockOptions { ShortcutDisplayMode = "Text", ShowShortcutNames = showNames, IconSize = iconSize };
        Check.Equal(0d, DockLayoutLogic.IconHeight(options, columnWidth));
        Check.Equal(40d, DockLayoutLogic.TileHeight(options, columnWidth));
        // Footer placement and expanded capacity use this same row measurement.
        options.FooterOffset = 600;
        var footer = DockLayoutLogic.Footer(options, 88, 1080, DockLayoutLogic.TileHeight(options, columnWidth));
        Check.True(footer.ViewportHeight >= 40);
    }
});
suite.Run("Text grids honor column count and spacing independently of stored icon size", () =>
{
    foreach (int columns in new[] { 1, 2, 3, 6 })
    foreach (double spacing in new[] { 0d, 8d, 64d })
    foreach (double monitorWidth in new[] { 72d, 120d, 640d, 1920d })
    {
        var options = new DockOptions { ShortcutDisplayMode = "Text", NormalColumns = columns, IconSpacing = spacing };
        double preferred = DockLayoutLogic.PreferredWidth(options);
        double width = DockLayoutLogic.NormalWidth(options, monitorWidth);
        double column = DockLayoutLogic.ColumnWidth(options, monitorWidth);
        double gap = DockLayoutLogic.EffectiveSpacing(options, monitorWidth);
        options.IconSize = 128;
        Check.Equal(preferred, DockLayoutLogic.PreferredWidth(options));
        Check.True(width <= monitorWidth);
        Check.True(column >= 1 && gap >= 0 && gap <= spacing);
        Check.Near(width - 12, columns * column + (columns - 1) * gap, .00001);
        if (monitorWidth >= preferred) Check.True(column >= 60);
    }
});
suite.Run("Constrained monitor widths reduce grid spacing and icons without overlapping cells", () =>
{
    foreach (double monitorWidth in new[] { 72d, 120d, 240d, 640d })
    foreach (int columns in new[] { 2, 3, 6 })
    {
        var options = new DockOptions { IconSize = 128, IconSpacing = 64, NormalColumns = columns };
        double width = DockLayoutLogic.NormalWidth(options, monitorWidth);
        double column = DockLayoutLogic.ColumnWidth(options, monitorWidth);
        double gap = DockLayoutLogic.EffectiveSpacing(options, monitorWidth);
        double icon = DockLayoutLogic.IconHeight(options, column);
        Check.True(width <= monitorWidth);
        Check.True(column >= 1 && gap >= 0 && gap <= 64);
        Check.True(icon >= 1 && icon <= column);
        Check.Near(width - 12, columns * column + (columns - 1) * gap, .00001);
        double previousRight = 6 + column;
        for (int index = 1; index < columns; index++)
        {
            double nextLeft = 6 + index * (column + gap);
            Check.True(nextLeft >= previousRight);
            previousRight = nextLeft + column;
        }
        Check.Near(width - 6, previousRight, .00001);
    }
});
suite.Run("Footer controls move up and down while preserving an accessible icon row", () =>
{
    foreach (var size in new[] { (Width: 88d, Height: 1080d), (Width: 144d, Height: 600d), (Width: 220d, Height: 320d) })
    {
        var options = new DockOptions();
        double tile = DockLayoutLogic.TileHeight(options, DockLayoutLogic.ColumnWidth(options, size.Width));
        var baseline = DockLayoutLogic.Footer(options, size.Width, size.Height, tile);
        options.FooterOffset = 30;
        var raised = DockLayoutLogic.Footer(options, size.Width, size.Height, tile);
        Check.Equal(12d, raised.BottomMargin);
        Check.Equal(30d, raised.BottomSpace);
        Check.Equal(baseline.ViewportHeight - 30, raised.ViewportHeight);
        options.FooterOffset = -12;
        var lowered = DockLayoutLogic.Footer(options, size.Width, size.Height, tile);
        Check.Equal(0d, lowered.BottomMargin);
        Check.Equal(0d, lowered.BottomSpace);
        Check.Equal(baseline.ViewportHeight + 12, lowered.ViewportHeight);
        options.FooterOffset = 600;
        var maximum = DockLayoutLogic.Footer(options, size.Width, size.Height, tile);
        Check.True(maximum.ViewportHeight >= tile);
        Check.Near(baseline.ViewportHeight, maximum.BottomSpace + maximum.ViewportHeight, .00001);
        options.FooterOffset = 0;
        Check.Equal(baseline, DockLayoutLogic.Footer(options, size.Width, size.Height, tile));
    }
    var tiny = DockLayoutLogic.Footer(new DockOptions { FooterOffset = 600 }, 88, 100, 73);
    Check.Equal(1d, tiny.ViewportHeight);
    Check.Equal(0d, tiny.BottomSpace);
    Check.Equal(1, DockLayoutLogic.FooterColumns(103));
    Check.Equal(2, DockLayoutLogic.FooterColumns(104));
    Check.Equal(2, DockLayoutLogic.FooterColumns(199));
    Check.Equal(4, DockLayoutLogic.FooterColumns(200));
});
suite.Run("Multi column scroll cycles join last to first on every full row and period", () =>
{
    foreach (int count in new[] { 1, 2, 3, 5, 7, 12, 26 })
    foreach (int columns in new[] { 1, 2, 3, 4, 6 })
    {
        Check.Equal(count, DockLayoutLogic.CycleItemCount(count, columns, false));
        int period = DockLayoutLogic.CycleItemCount(count, columns, true);
        Check.True(period >= count);
        Check.Equal(0, period % count);
        Check.Equal(0, period % columns);
        // Two displayed periods must read as one uninterrupted logical sequence,
        // including incomplete original rows and the recentering boundary.
        var displayed = Enumerable.Range(0, 2 * period).Select(index => index % period % count).ToArray();
        for (int index = 0; index < displayed.Length - 1; index++)
            Check.Equal((displayed[index] + 1) % count, displayed[index + 1]);
        for (int row = 0; row < displayed.Length / columns; row++)
            Check.Equal(columns, displayed.Skip(row * columns).Take(columns).Count());
        Check.Equal(0, displayed[period]);
    }
    Check.Equal(0, DockLayoutLogic.CycleItemCount(0, 3, true));
    Check.Equal(0, DockLayoutLogic.CycleItemCount(-1, 3, false));
});
suite.Run("Existing shared and independent configurations retain the current icon layout", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.SettingsFilePath, """
            {
              "DockWidth": 144, "SeparateSideSettings": true,
              "LeftSettings": { "DockWidth": 180, "ShowShortcutNames": false },
              "RightSettings": { "DockWidth": 220, "ShortcutDisplayMode": "Wide" },
              "LeftShortcuts": [{ "Id": "preserved", "Name": "已有图标", "TargetPath": "C:\\existing.lnk" }]
            }
            """);
        var loaded = store.Load();
        Check.True(store.LastError is null);
        foreach (var options in new[] { loaded.Copy(), loaded.GetSideOptions(DockSide.Left), loaded.GetSideOptions(DockSide.Right) })
        {
            Check.Equal(40d, options.IconSize);
            Check.Equal(8d, options.IconSpacing);
            Check.Equal(1, options.NormalColumns);
            Check.Equal(0d, options.FooterOffset);
        }
        Check.Equal(180d, loaded.GetSideOptions(DockSide.Left).DockWidth);
        Check.False(loaded.GetSideOptions(DockSide.Left).ShowShortcutNames);
        Check.Equal("Wide", loaded.GetSideOptions(DockSide.Right).ShortcutDisplayMode);
        store.Save(loaded);
        var restarted = new SettingsStore(directory).Load();
        Check.Equal("preserved", restarted.LeftShortcuts.Single().Id);
        Check.Equal(40d, restarted.GetSideOptions(DockSide.Right).IconSize);
        Check.False(Directory.EnumerateFiles(directory).Any(path => path != store.SettingsFilePath));
    });
});
suite.Run("Invalid layout settings are repaired before persistence without losing shortcut data", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings
        {
            SeparateSideSettings = true,
            IconSize = -1, IconSpacing = -1, NormalColumns = 0, FooterOffset = -100,
            LeftSettings = new DockOptions { IconSize = 1000, IconSpacing = 1000, NormalColumns = 100, FooterOffset = 1000 },
            RightSettings = new DockOptions { IconSize = double.NaN, IconSpacing = double.PositiveInfinity, NormalColumns = 4, FooterOffset = double.NegativeInfinity }
        };
        settings.LeftShortcuts.Add(new ShortcutItem { Id = "repair-preserved", TargetPath = @"C:\existing.lnk" });
        store.Save(settings);
        var loaded = new SettingsStore(directory).Load();
        Check.Equal(16d, loaded.IconSize);
        Check.Equal(0d, loaded.IconSpacing);
        Check.Equal(1, loaded.NormalColumns);
        Check.Equal(-12d, loaded.FooterOffset);
        var left = loaded.GetSideOptions(DockSide.Left);
        Check.Equal(128d, left.IconSize);
        Check.Equal(64d, left.IconSpacing);
        Check.Equal(6, left.NormalColumns);
        Check.Equal(600d, left.FooterOffset);
        var right = loaded.GetSideOptions(DockSide.Right);
        Check.Equal(40d, right.IconSize);
        Check.Equal(8d, right.IconSpacing);
        Check.Equal(4, right.NormalColumns);
        Check.Equal(0d, right.FooterOffset);
        Check.Equal("repair-preserved", loaded.LeftShortcuts.Single().Id);

        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            loaded.IconSize = loaded.IconSpacing = loaded.FooterOffset = invalid;
            store.Save(loaded);
            var repaired = new SettingsStore(directory).Load();
            Check.Equal(40d, repaired.IconSize);
            Check.Equal(8d, repaired.IconSpacing);
            Check.Equal(0d, repaired.FooterOffset);
        }
    });
});
suite.Run("Icon layout drafts remain independent while switching shared and separate settings", () =>
{
    var settings = new DockSettings
    {
        IconSize = 44, IconSpacing = 7, NormalColumns = 2, FooterOffset = 25,
        LeftSettings = new DockOptions { IconSize = 64, IconSpacing = 12, NormalColumns = 3, FooterOffset = 75 },
        RightSettings = new DockOptions { IconSize = 24, IconSpacing = 4, NormalColumns = 4, FooterOffset = -8 }
    };
    var sharedLeft = settings.GetSideOptions(DockSide.Left);
    var sharedRight = settings.GetSideOptions(DockSide.Right);
    Check.Equal(44d, sharedLeft.IconSize);
    Check.Equal(7d, sharedRight.IconSpacing);
    Check.Equal(2, sharedRight.NormalColumns);
    Check.Equal(25d, sharedLeft.FooterOffset);
    sharedLeft.IconSize = 128;
    sharedLeft.IconSpacing = 64;
    sharedRight.NormalColumns = 6;
    sharedRight.FooterOffset = 600;
    Check.Equal(44d, settings.IconSize);
    Check.Equal(7d, settings.IconSpacing);
    Check.Equal(2, settings.NormalColumns);
    Check.Equal(25d, settings.FooterOffset);

    settings.SeparateSideSettings = true;
    var left = settings.GetSideOptions(DockSide.Left);
    var right = settings.GetSideOptions(DockSide.Right);
    Check.Equal(64d, left.IconSize);
    Check.Equal(12d, left.IconSpacing);
    Check.Equal(3, left.NormalColumns);
    Check.Equal(75d, left.FooterOffset);
    Check.Equal(24d, right.IconSize);
    Check.Equal(4d, right.IconSpacing);
    Check.Equal(4, right.NormalColumns);
    Check.Equal(-8d, right.FooterOffset);
    left.IconSize = right.IconSize = 100;
    left.IconSpacing = right.IconSpacing = 60;
    left.NormalColumns = right.NormalColumns = 5;
    left.FooterOffset = right.FooterOffset = 300;
    Check.Equal(64d, settings.GetStoredSideOptions(DockSide.Left).IconSize);
    Check.Equal(12d, settings.GetStoredSideOptions(DockSide.Left).IconSpacing);
    Check.Equal(4, settings.GetStoredSideOptions(DockSide.Right).NormalColumns);
    Check.Equal(-8d, settings.GetStoredSideOptions(DockSide.Right).FooterOffset);
    settings.RightSettings = null;
    Check.Equal(44d, settings.GetSideOptions(DockSide.Right).IconSize);
    Check.Equal(7d, settings.GetSideOptions(DockSide.Right).IconSpacing);
    Check.Equal(2, settings.GetSideOptions(DockSide.Right).NormalColumns);
    Check.Equal(25d, settings.GetSideOptions(DockSide.Right).FooterOffset);
});
suite.Run("Custom icon layout and both side drafts survive save and restart", () =>
{
    WithTempDirectory(directory =>
    {
        var settings = new DockSettings
        {
            SeparateSideSettings = true, IconSize = 47.5, IconSpacing = 9.5, NormalColumns = 2, FooterOffset = 35.5,
            LeftSettings = new DockOptions { IconSize = 72.5, IconSpacing = 14.5, NormalColumns = 3, FooterOffset = 98.5 },
            RightSettings = new DockOptions { IconSize = 28.5, IconSpacing = .5, NormalColumns = 6, FooterOffset = -10.5 }
        };
        var store = new SettingsStore(directory);
        store.Save(settings);
        var loaded = new SettingsStore(directory).Load();
        var left = loaded.GetSideOptions(DockSide.Left);
        var right = loaded.GetSideOptions(DockSide.Right);
        Check.Equal(72.5, left.IconSize);
        Check.Equal(14.5, left.IconSpacing);
        Check.Equal(3, left.NormalColumns);
        Check.Equal(98.5, left.FooterOffset);
        Check.Equal(28.5, right.IconSize);
        Check.Equal(.5, right.IconSpacing);
        Check.Equal(6, right.NormalColumns);
        Check.Equal(-10.5, right.FooterOffset);
        loaded.SeparateSideSettings = false;
        store.Save(loaded);
        loaded = new SettingsStore(directory).Load();
        foreach (var side in new[] { DockSide.Left, DockSide.Right })
        {
            var shared = loaded.GetSideOptions(side);
            Check.Equal(47.5, shared.IconSize);
            Check.Equal(9.5, shared.IconSpacing);
            Check.Equal(2, shared.NormalColumns);
            Check.Equal(35.5, shared.FooterOffset);
        }
        loaded.SeparateSideSettings = true;
        Check.Equal(72.5, loaded.GetSideOptions(DockSide.Left).IconSize);
        Check.Equal(14.5, loaded.GetSideOptions(DockSide.Left).IconSpacing);
        Check.Equal(6, loaded.GetSideOptions(DockSide.Right).NormalColumns);
        Check.Equal(-10.5, loaded.GetSideOptions(DockSide.Right).FooterOffset);
    });
});
suite.Run("Options copies preserve all fields and remain independent", () =>
{
    var original = new DockOptions
    {
        DockWidth = 218, AutoHide = false, HideDelayMs = 981, RevealDelayMs = 174,
        RevealDurationMs = 432, HideDurationMs = 765, AnimationStyle = "Reveal", LoopScrolling = false,
        Theme = "Dark", ShowShortcutNames = false, HeaderText = "独立选项", ShortcutDisplayMode = "Wide", DockFrameStyle = "Floating",
        BackgroundStyle = "Image", BackgroundImagePath = @"C:\背景\图像.jpg", BackgroundOpacity = .31,
        BackgroundImageOverlayOpacity = .27,
        IconSize = 56, IconSpacing = 11, NormalColumns = 3, FooterOffset = 42
    };
    var copy = original.Copy();
    Check.False(ReferenceEquals(original, copy));
    Check.Equal(System.Text.Json.JsonSerializer.Serialize(original), System.Text.Json.JsonSerializer.Serialize(copy));
    copy.HeaderText = "副本";
    copy.RevealDurationMs = 100;
    copy.IconSize = 24;
    copy.IconSpacing = 2;
    copy.NormalColumns = 1;
    copy.FooterOffset = -10;
    Check.Equal("独立选项", original.HeaderText);
    Check.Equal(432, original.RevealDurationMs);
    Check.Equal(56d, original.IconSize);
    Check.Equal(11d, original.IconSpacing);
    Check.Equal(3, original.NormalColumns);
    Check.Equal(42d, original.FooterOffset);
    var target = new DockSettings { LeftEnabled = false, RightEnabled = true, SeparateSideSettings = true };
    target.LeftShortcuts.Add(new ShortcutItem { TargetPath = "https://example.com/" });
    target.CopyFrom(original);
    Check.Equal(System.Text.Json.JsonSerializer.Serialize(original), System.Text.Json.JsonSerializer.Serialize(target.Copy()));
    Check.False(target.LeftEnabled);
    Check.True(target.RightEnabled && target.SeparateSideSettings);
    Check.Equal(1, target.LeftShortcuts.Count);
});
suite.Run("Shared options ignore stored side drafts and return isolated copies", () =>
{
    var settings = new DockSettings
    {
        DockWidth = 132, HeaderText = "统一", AnimationStyle = "Slide", RevealDurationMs = 410,
        LeftSettings = new DockOptions { DockWidth = 200, HeaderText = "左侧草稿" },
        RightSettings = new DockOptions { DockWidth = 300, HeaderText = "右侧草稿" }
    };
    var left = settings.GetSideOptions(DockSide.Left);
    var right = settings.GetSideOptions(DockSide.Right);
    Check.Equal(132d, left.DockWidth);
    Check.Equal(132d, right.DockWidth);
    Check.Equal("统一", left.HeaderText);
    Check.Equal(410, right.RevealDurationMs);
    left.DockWidth = 240;
    right.HeaderText = "临时更改";
    Check.Equal(132d, settings.DockWidth);
    Check.Equal("统一", settings.HeaderText);
    Check.Equal(200d, settings.GetStoredSideOptions(DockSide.Left).DockWidth);
    Check.Equal("右侧草稿", settings.GetStoredSideOptions(DockSide.Right).HeaderText);
});
suite.Run("Independent side options isolate edits and fall back to shared options when absent", () =>
{
    var settings = new DockSettings
    {
        SeparateSideSettings = true, DockWidth = 100, HeaderText = "默认", LoopScrolling = true,
        LeftSettings = new DockOptions { DockWidth = 180, HeaderText = "工作", LoopScrolling = false }
    };
    var left = settings.GetSideOptions(DockSide.Left);
    var right = settings.GetSideOptions(DockSide.Right);
    Check.Equal(180d, left.DockWidth);
    Check.Equal("工作", left.HeaderText);
    Check.False(left.LoopScrolling);
    Check.Equal(100d, right.DockWidth);
    Check.Equal("默认", right.HeaderText);
    Check.True(right.LoopScrolling);
    left.DockWidth = 250;
    right.DockWidth = 300;
    Check.Equal(180d, settings.LeftSettings.DockWidth);
    Check.Equal(100d, settings.DockWidth);
    Check.True(settings.RightSettings is null);
});
suite.Run("Pin overrides affect only their own side in shared and independent modes", () =>
{
    foreach (bool separate in new[] { false, true })
    {
        var settings = new DockSettings
        {
            SeparateSideSettings = separate, AutoHide = true,
            LeftSettings = new DockOptions { AutoHide = true },
            RightSettings = new DockOptions { AutoHide = true }
        };
        settings.SetSideAutoHide(DockSide.Left, false);
        Check.False(settings.GetSideOptions(DockSide.Left).AutoHide);
        Check.True(settings.GetSideOptions(DockSide.Right).AutoHide);
        Check.True(settings.AutoHide && settings.LeftSettings.AutoHide && settings.RightSettings.AutoHide);
        settings.SetSideAutoHide(DockSide.Right, false);
        settings.SetSideAutoHide(DockSide.Left, true);
        Check.True(settings.GetSideOptions(DockSide.Left).AutoHide);
        Check.False(settings.GetSideOptions(DockSide.Right).AutoHide);
        settings.ClearSideAutoHideOverride(DockSide.Left);
        Check.True(settings.LeftAutoHideOverride is null);
        Check.False(settings.GetSideOptions(DockSide.Right).AutoHide);
        settings.ClearSideAutoHideOverride(DockSide.Right);
        Check.True(settings.RightAutoHideOverride is null);
        Check.True(settings.GetSideOptions(DockSide.Left).AutoHide && settings.GetSideOptions(DockSide.Right).AutoHide);
    }
});
suite.Run("Shared and independent drafts, animation settings and pins survive persistence", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings
        {
            SeparateSideSettings = true, DockWidth = 126, HeaderText = "统一",
            RevealDurationMs = 280, HideDurationMs = 370, AnimationStyle = "Slide", LoopScrolling = false,
            LeftSettings = new DockOptions
            {
                DockWidth = 200, HeaderText = "工作", RevealDurationMs = 450, HideDurationMs = 820,
                AnimationStyle = "Reveal", LoopScrolling = false, BackgroundStyle = "Transparent", BackgroundOpacity = .2
            },
            RightSettings = new DockOptions
            {
                DockWidth = 260, HeaderText = "游戏", RevealDurationMs = 600, HideDurationMs = 900,
                AnimationStyle = "None", LoopScrolling = true, Theme = "Dark", ShortcutDisplayMode = "Wide"
            }
        };
        settings.SetSideAutoHide(DockSide.Left, false);
        settings.LeftShortcuts.Add(new ShortcutItem { Id = "preserve-side-list", TargetPath = "https://example.com/" });
        store.Save(settings);
        var loaded = store.Load();
        Check.True(loaded.SeparateSideSettings);
        Check.Equal(126d, loaded.DockWidth);
        Check.Equal(280, loaded.RevealDurationMs);
        Check.Equal(370, loaded.HideDurationMs);
        Check.False(loaded.LoopScrolling);
        var left = loaded.GetSideOptions(DockSide.Left);
        var right = loaded.GetSideOptions(DockSide.Right);
        Check.Equal(200d, left.DockWidth);
        Check.Equal("工作", left.HeaderText);
        Check.Equal(450, left.RevealDurationMs);
        Check.Equal(820, left.HideDurationMs);
        Check.Equal("Reveal", left.AnimationStyle);
        Check.Equal("Transparent", left.BackgroundStyle);
        Check.Equal(.2, left.BackgroundOpacity);
        Check.False(left.AutoHide || left.LoopScrolling);
        Check.Equal(260d, right.DockWidth);
        Check.Equal("游戏", right.HeaderText);
        Check.Equal(600, right.RevealDurationMs);
        Check.Equal(900, right.HideDurationMs);
        Check.Equal("None", right.AnimationStyle);
        Check.Equal("Wide", right.ShortcutDisplayMode);
        Check.True(right.AutoHide && right.LoopScrolling);
        Check.Equal("preserve-side-list", loaded.LeftShortcuts[0].Id);
        loaded.SeparateSideSettings = false;
        store.Save(loaded);
        loaded = store.Load();
        Check.False(loaded.GetSideOptions(DockSide.Left).AutoHide);
        Check.True(loaded.GetSideOptions(DockSide.Right).AutoHide);
        Check.Equal(126d, loaded.GetSideOptions(DockSide.Right).DockWidth);
        Check.Equal("工作", loaded.LeftSettings!.HeaderText);
        loaded.SeparateSideSettings = true;
        Check.Equal(260d, loaded.GetSideOptions(DockSide.Right).DockWidth);
    });
});
suite.Run("Animation durations and invalid styles normalize in shared and side options", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings
        {
            SeparateSideSettings = true, RevealDurationMs = -1, HideDurationMs = 9000, AnimationStyle = "unknown",
            LeftSettings = new DockOptions { RevealDurationMs = int.MinValue, HideDurationMs = int.MaxValue, AnimationStyle = null! },
            RightSettings = new DockOptions { RevealDurationMs = 80, HideDurationMs = 1500, AnimationStyle = "None" }
        };
        store.Save(settings);
        var loaded = store.Load();
        Check.Equal(80, loaded.RevealDurationMs);
        Check.Equal(1500, loaded.HideDurationMs);
        Check.Equal("Slide", loaded.AnimationStyle);
        Check.Equal(80, loaded.LeftSettings!.RevealDurationMs);
        Check.Equal(1500, loaded.LeftSettings.HideDurationMs);
        Check.Equal("Slide", loaded.LeftSettings.AnimationStyle);
        Check.Equal(80, loaded.RightSettings!.RevealDurationMs);
        Check.Equal(1500, loaded.RightSettings.HideDurationMs);
        Check.Equal("None", loaded.RightSettings.AnimationStyle);
        foreach (string style in new[] { "Slide", "Reveal", "None" })
        {
            loaded.AnimationStyle = style;
            loaded.RevealDurationMs = 275;
            loaded.HideDurationMs = 625;
            store.Save(loaded);
            Check.Equal(style, store.Load().AnimationStyle);
            Check.Equal(275, store.Load().RevealDurationMs);
            Check.Equal(625, store.Load().HideDurationMs);
        }
    });
});
suite.Run("Loop scrolling reaches each boundary before wrapping in both directions", () =>
{
    Check.Equal(40d, DockScrollLogic.NextOffset(0, 100, 40, true));
    Check.Equal(100d, DockScrollLogic.NextOffset(80, 100, 40, true));
    Check.Equal(0d, DockScrollLogic.NextOffset(100, 100, 40, true));
    Check.Equal(60d, DockScrollLogic.NextOffset(100, 100, -40, true));
    Check.Equal(0d, DockScrollLogic.NextOffset(20, 100, -40, true));
    Check.Equal(100d, DockScrollLogic.NextOffset(0, 100, -40, true));
    Check.Equal(0d, DockScrollLogic.NextOffset(0, 100, 0, true));
    Check.Equal(100d, DockScrollLogic.NextOffset(100, 100, 0, true));
    double offset = 0;
    for (int step = 0; step < 3; step++) offset = DockScrollLogic.NextOffset(offset, 100, 40, true);
    Check.Equal(100d, offset);
    Check.Equal(0d, DockScrollLogic.NextOffset(offset, 100, 40, true));
});
suite.Run("Scrolling clamps without looping and safely handles invalid ranges and inputs", () =>
{
    Check.Equal(100d, DockScrollLogic.NextOffset(100, 100, 40, false));
    Check.Equal(0d, DockScrollLogic.NextOffset(0, 100, -40, false));
    Check.Equal(10d, DockScrollLogic.NextOffset(-100, 100, 10, false));
    Check.Equal(90d, DockScrollLogic.NextOffset(200, 100, -10, false));
    Check.Equal(100d, DockScrollLogic.NextOffset(50, 100, double.MaxValue, false));
    Check.Equal(0d, DockScrollLogic.NextOffset(50, 100, -double.MaxValue, false));
    Check.Equal(10d, DockScrollLogic.NextOffset(double.NaN, 100, 10, false));
    foreach (double maximum in new[] { 0d, -10d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Check.Equal(0d, DockScrollLogic.NextOffset(50, maximum, 10, true));
    foreach (double delta in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Check.Equal(0d, DockScrollLogic.NextOffset(50, 100, delta, true));
});
suite.Run("Continuous scroll recentering preserves phase across positive negative and exact seams", () =>
{
    Check.Equal(100d, DockScrollLogic.Recenter(0, 100));
    Check.Equal(100d, DockScrollLogic.Recenter(100, 100));
    Check.Equal(100d, DockScrollLogic.Recenter(-100, 100));
    Check.Equal(100d, DockScrollLogic.Recenter(10_000, 100));
    Check.Equal(100d, DockScrollLogic.Recenter(-10_000, 100));
    Check.Equal(125d, DockScrollLogic.Recenter(25, 100));
    Check.Equal(125d, DockScrollLogic.Recenter(10_025, 100));
    Check.Equal(175d, DockScrollLogic.Recenter(-10_025, 100));
    foreach (double value in new[] { -1234.75, -.25, 0, 61.25, 3845.5 })
    {
        double recentered = DockScrollLogic.Recenter(value, 83.5);
        Check.True(recentered >= 83.5 && recentered < 167);
        Check.Near(0, (recentered - value) % 83.5, 1e-8);
        Check.Near(recentered, DockScrollLogic.Recenter(recentered, 83.5), 1e-8);
    }
    foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        Check.Equal(0d, DockScrollLogic.Recenter(invalid, 100));
        Check.Equal(0d, DockScrollLogic.Recenter(10, invalid));
    }
    Check.Equal(0d, DockScrollLogic.Recenter(10, 0));
    Check.Equal(0d, DockScrollLogic.Recenter(10, -100));
});
suite.Run("Smooth scroll follows changing targets and converges at sixteen and thirty two millisecond frames", () =>
{
    double current = DockScrollLogic.AnimateOffset(0, 120, 32);
    Check.True(current > 0 && current < 120);
    double reversed = DockScrollLogic.AnimateOffset(current, -60, 16);
    Check.True(reversed < current && reversed > -60);
    for (int i = 0; i < 100; i++) reversed = DockScrollLogic.AnimateOffset(reversed, -60, 16);
    Check.Equal(-60d, reversed);
    double sixteen = 0, thirtyTwo = 0;
    for (int i = 0; i < 12; i++) sixteen = DockScrollLogic.AnimateOffset(sixteen, 72, 16);
    for (int i = 0; i < 6; i++) thirtyTwo = DockScrollLogic.AnimateOffset(thirtyTwo, 72, 32);
    Check.Near(sixteen, thirtyTwo, 1e-9);
    Check.Near(72 * (1 - Math.Exp(-192d / 55)), sixteen, 1e-9);
    Check.Equal(20d, DockScrollLogic.AnimateOffset(20, 20, 16));
    Check.Equal(20d, DockScrollLogic.AnimateOffset(19.95, 20, 16));
});
suite.Run("Smooth scroll rejects nonfinite values and bounds delayed frames", () =>
{
    foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        Check.Equal(0d, DockScrollLogic.AnimateOffset(invalid, 20, 16));
        Check.Equal(0d, DockScrollLogic.AnimateOffset(10, invalid, 16));
        Check.Equal(10d, DockScrollLogic.AnimateOffset(10, 20, invalid));
    }
    Check.Equal(10d, DockScrollLogic.AnimateOffset(10, 20, 0));
    Check.Equal(10d, DockScrollLogic.AnimateOffset(10, 20, -16));
    Check.Equal(DockScrollLogic.AnimateOffset(0, 100, 64),
        DockScrollLogic.AnimateOffset(0, 100, double.MaxValue));
});
suite.Run("Short lists bounce opposite to each wheel direction and settle to rest", () =>
{
    foreach (double delta in new[] { -72d, 72d })
    {
        var bounce = new DockScrollBounce();
        Check.False(bounce.IsRunning);
        bounce.Push(delta);
        Check.True(bounce.IsRunning);
        bounce.Update(16);
        Check.True(bounce.Value * delta < 0);
        Check.True(Math.Abs(bounce.Value) <= 18);
        for (int i = 0; i < 250; i++) bounce.Update(16);
        Check.Equal(0d, bounce.Value);
        Check.Equal(0d, bounce.Velocity);
        Check.False(bounce.IsRunning);
    }
});
suite.Run("Repeated and reversed wheel impulses keep bounce bounded at different frame rates", () =>
{
    foreach (double frameMs in new[] { 16d, 32d })
    foreach (double delta in new[] { -72d, 72d })
    {
        var bounce = new DockScrollBounce();
        for (int i = 0; i < 100; i++)
        {
            bounce.Push(delta);
            bounce.Update(frameMs);
            Check.True(double.IsFinite(bounce.Value) && Math.Abs(bounce.Value) <= 18);
            Check.True(double.IsFinite(bounce.Velocity) && Math.Abs(bounce.Velocity) <= 420);
        }
        double before = bounce.Value;
        for (int i = 0; i < 5; i++) { bounce.Push(-delta); bounce.Update(frameMs); }
        Check.True((bounce.Value - before) * delta > 0);
        for (int i = 0; i < 250; i++) bounce.Update(frameMs);
        Check.Equal(0d, bounce.Value);
        Check.Equal(0d, bounce.Velocity);
        Check.False(bounce.IsRunning);
    }
});
suite.Run("Bounce ignores invalid inputs supports delayed frames and resets explicitly", () =>
{
    var bounce = new DockScrollBounce();
    bounce.Push(72);
    bounce.Update(16);
    double value = bounce.Value, velocity = bounce.Velocity;
    foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        bounce.Push(invalid);
        bounce.Update(invalid);
        Check.Equal(value, bounce.Value);
        Check.Equal(velocity, bounce.Velocity);
    }
    bounce.Update(0); bounce.Update(-16);
    Check.Equal(value, bounce.Value);
    Check.Equal(velocity, bounce.Velocity);
    bounce.Push(double.MaxValue);
    bounce.Update(double.MaxValue);
    Check.True(double.IsFinite(bounce.Value) && Math.Abs(bounce.Value) <= 18);
    Check.True(double.IsFinite(bounce.Velocity) && Math.Abs(bounce.Velocity) <= 420);
    bounce.Reset();
    Check.Equal(0d, bounce.Value);
    Check.Equal(0d, bounce.Velocity);
    Check.False(bounce.IsRunning);
    var sixteen = new DockScrollBounce(); var thirtyTwo = new DockScrollBounce();
    sixteen.Push(72); thirtyTwo.Push(72);
    for (int i = 0; i < 12; i++) sixteen.Update(16);
    for (int i = 0; i < 6; i++) thirtyTwo.Update(32);
    Check.Near(sixteen.Value, thirtyTwo.Value, 1e-8);
    Check.Near(sixteen.Velocity, thirtyTwo.Velocity, 1e-8);
});
suite.Run("Appearance normalization repairs invalid editable JSON values", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.SettingsFilePath, """
            {
              "HeaderText": null, "ShortcutDisplayMode": "Unknown",
              "BackgroundStyle": "Unknown", "BackgroundImagePath": null,
              "BackgroundOpacity": -2, "BackgroundImageOverlayOpacity": 2,
              "LeftShortcuts": [{ "TargetPath": "https://example.com/", "CustomIconPath": null, "ImageCrop": "Unknown" }]
            }
            """);
        var loaded = store.Load();
        Check.True(store.LastError is null);
        Check.Equal("ED", loaded.HeaderText);
        Check.Equal("Icon", loaded.ShortcutDisplayMode);
        Check.Equal("Solid", loaded.BackgroundStyle);
        Check.Equal(string.Empty, loaded.BackgroundImagePath);
        Check.Equal(0d, loaded.BackgroundOpacity);
        Check.Equal(1d, loaded.BackgroundImageOverlayOpacity);
        Check.Equal(string.Empty, loaded.LeftShortcuts[0].CustomIconPath);
        Check.Equal("Center", loaded.LeftShortcuts[0].ImageCrop);
        loaded.BackgroundOpacity = double.NaN;
        loaded.BackgroundImageOverlayOpacity = double.PositiveInfinity;
        store.Save(loaded);
        loaded = store.Load();
        Check.Equal(.82, loaded.BackgroundOpacity);
        Check.Equal(.35, loaded.BackgroundImageOverlayOpacity);
    });
});
suite.Run("Missing and invalid crops default to center while explicit legacy crops remain", () =>
{
    Check.Equal("Center", new ShortcutItem().ImageCrop);
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.SettingsFilePath, """
            {
              "LeftShortcuts": [
                { "Id": "missing", "TargetPath": "C:\\missing-field.lnk" },
                { "Id": "invalid", "TargetPath": "C:\\invalid-crop.lnk", "ImageCrop": "Unknown" },
                { "Id": "null", "TargetPath": "C:\\null-crop.lnk", "ImageCrop": null },
                { "Id": "top", "TargetPath": "C:\\user-top.lnk", "ImageCrop": "Top" },
                { "Id": "bottom", "TargetPath": "C:\\user-bottom.lnk", "ImageCrop": "Bottom" },
                { "Id": "fit", "TargetPath": "C:\\user-fit.lnk", "ImageCrop": "Fit" }
              ]
            }
            """);
        var loaded = store.Load();
        Check.True(store.LastError is null);
        Check.Equal(6, loaded.LeftShortcuts.Count);
        foreach (var item in loaded.LeftShortcuts.Take(3)) Check.Equal("Center", item.ImageCrop);
        Check.Equal("Top", loaded.LeftShortcuts[3].ImageCrop);
        Check.Equal("Bottom", loaded.LeftShortcuts[4].ImageCrop);
        Check.Equal("Fit", loaded.LeftShortcuts[5].ImageCrop);
        store.Save(loaded);
        loaded = store.Load();
        Check.Equal("Center", loaded.LeftShortcuts[0].ImageCrop);
        Check.Equal("Top", loaded.LeftShortcuts[3].ImageCrop);
        Check.Equal("bottom", loaded.LeftShortcuts[4].Id);
        Check.Equal(@"C:\user-top.lnk", loaded.LeftShortcuts[3].TargetPath);
    });
});
suite.Run("Dock frame style defaults and invalid values normalize independently", () =>
{
    Check.Equal("Edge", new DockOptions().DockFrameStyle);
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.SettingsFilePath, """
            {
              "DockFrameStyle": "Unknown", "SeparateSideSettings": true,
              "LeftSettings": { "DockFrameStyle": null },
              "RightSettings": { "DockFrameStyle": "Floating" }
            }
            """);
        var loaded = store.Load();
        Check.True(store.LastError is null);
        Check.Equal("Edge", loaded.DockFrameStyle);
        Check.Equal("Edge", loaded.GetSideOptions(DockSide.Left).DockFrameStyle);
        Check.Equal("Floating", loaded.GetSideOptions(DockSide.Right).DockFrameStyle);
        loaded.LeftSettings!.DockFrameStyle = "floating";
        store.Save(loaded);
        Check.Equal("Edge", store.Load().LeftSettings!.DockFrameStyle);
    });
});
suite.Run("Dock frame styles preserve shared and independent drafts through copies and persistence", () =>
{
    WithTempDirectory(directory =>
    {
        var settings = new DockSettings
        {
            DockFrameStyle = "Floating",
            LeftSettings = new DockOptions { DockFrameStyle = "Edge" },
            RightSettings = new DockOptions { DockFrameStyle = "Floating" }
        };
        Check.Equal("Floating", settings.Copy().DockFrameStyle);
        var destination = new DockOptions();
        destination.CopyFrom(settings);
        Check.Equal("Floating", destination.DockFrameStyle);
        Check.Equal("Floating", settings.GetSideOptions(DockSide.Left).DockFrameStyle);
        Check.Equal("Floating", settings.GetSideOptions(DockSide.Right).DockFrameStyle);
        settings.GetSideOptions(DockSide.Right).DockFrameStyle = "Edge";
        Check.Equal("Floating", settings.RightSettings!.DockFrameStyle);
        settings.SeparateSideSettings = true;
        Check.Equal("Edge", settings.GetSideOptions(DockSide.Left).DockFrameStyle);
        Check.Equal("Floating", settings.GetSideOptions(DockSide.Right).DockFrameStyle);
        var store = new SettingsStore(directory);
        store.Save(settings);
        var loaded = store.Load();
        Check.Equal("Floating", loaded.DockFrameStyle);
        Check.Equal("Edge", loaded.GetSideOptions(DockSide.Left).DockFrameStyle);
        Check.Equal("Floating", loaded.GetSideOptions(DockSide.Right).DockFrameStyle);
        loaded.SeparateSideSettings = false;
        store.Save(loaded);
        loaded = store.Load();
        Check.Equal("Floating", loaded.GetSideOptions(DockSide.Left).DockFrameStyle);
        Check.Equal("Edge", loaded.LeftSettings!.DockFrameStyle);
        loaded.SeparateSideSettings = true;
        Check.Equal("Edge", loaded.GetSideOptions(DockSide.Left).DockFrameStyle);
    });
});
suite.Run("Blank header and all supported appearance modes survive persistence", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        var settings = new DockSettings
        {
            HeaderText = "   ", DockWidth = 320, ShowShortcutNames = false,
            BackgroundImagePath = @"  C:\背景 图片\风景.jpg  ",
            BackgroundOpacity = 0, BackgroundImageOverlayOpacity = 1
        };
        settings.LeftShortcuts.Add(new ShortcutItem
        {
            TargetPath = "https://example.com/", CustomIconPath = @"  C:\自定义 图片\封面.png  "
        });
        foreach (string background in new[] { "Solid", "Transparent", "Acrylic", "Image" })
        foreach (string display in new[] { "Icon", "Wide", "Text" })
        foreach (string crop in new[] { "Top", "Center", "Bottom", "Fit" })
        {
            settings.BackgroundStyle = background;
            settings.ShortcutDisplayMode = display;
            settings.LeftShortcuts[0].ImageCrop = crop;
            store.Save(settings);
            var loaded = store.Load();
            Check.True(store.LastError is null);
            Check.Equal(string.Empty, loaded.HeaderText);
            Check.Equal(320d, loaded.DockWidth);
            Check.False(loaded.ShowShortcutNames);
            Check.Equal(background, loaded.BackgroundStyle);
            Check.Equal(display, loaded.ShortcutDisplayMode);
            Check.Equal(crop, loaded.LeftShortcuts[0].ImageCrop);
            Check.Equal(@"C:\背景 图片\风景.jpg", loaded.BackgroundImagePath);
            Check.Equal(@"C:\自定义 图片\封面.png", loaded.LeftShortcuts[0].CustomIconPath);
            Check.Equal(0d, loaded.BackgroundOpacity);
            Check.Equal(1d, loaded.BackgroundImageOverlayOpacity);
        }
        settings.HeaderText = "  " + new string('界', 40) + "  ";
        store.Save(settings);
        Check.Equal(new string('界', 32), store.Load().HeaderText);
    });
});
suite.Run("Web shortcut import supports HTTP and HTTPS", () =>
{
    var item = ShortcutService.ImportPath("  https://example.com/docs?q=edge#section  ");
    Check.Equal("https://example.com/docs?q=edge#section", item.TargetPath);
    Check.Equal("example.com", item.Name);
    Check.Equal(string.Empty, item.WorkingDirectory);
    Check.Equal(string.Empty, item.IconPath);
    Check.Equal("http://example.com/", ShortcutService.ImportPath("http://example.com").TargetPath);
});
suite.Run("Malformed URLs and unsupported schemes are rejected before shell launch", () =>
{
    foreach (var target in new[]
    {
        "https://", "https:///missing-host", "http:example.com", "https://bad host/",
        "javascript:alert(1)", "file:///C:/Windows", "shell:AppsFolder", "ftp://example.com/",
        "data:text/html,test"
    })
    {
        Check.Throws<ArgumentException>(() => ShortcutService.ImportPath(target));
        Check.Throws<ArgumentException>(() => ShortcutService.Launch(new ShortcutItem { TargetPath = target }));
    }
    Check.Throws<ArgumentException>(() => ShortcutService.ImportPath("bad\0path"));
    Check.Throws<ArgumentException>(() => ShortcutService.ImportPath(new string('"', 2)));
    WithTempDirectory(directory =>
        Check.Throws<FileNotFoundException>(() => ShortcutService.ImportPath(Path.Combine(directory, "missing.exe"))));
});
suite.Run("Quoted environment paths and shortcut files preserve their original targets", () =>
{
    WithTempDirectory(directory =>
    {
        string variable = "EDOCK_TEST_" + Guid.NewGuid().ToString("N");
        string shortcut = Path.Combine(directory, "My application.lnk");
        string internetShortcut = Path.Combine(directory, "Project web.url");
        File.WriteAllBytes(shortcut, Array.Empty<byte>());
        File.WriteAllText(internetShortcut, "[InternetShortcut]\r\nURL=https://example.com/\r\n");
        Environment.SetEnvironmentVariable(variable, directory);
        try
        {
            string quotedEnvironmentPath = '"' + Path.Combine("%" + variable + "%", "My application.lnk") + '"';
            var item = ShortcutService.ImportPath(quotedEnvironmentPath);
            Check.Equal(shortcut, item.TargetPath);
            Check.Equal("My application", item.Name);
            Check.Equal(directory, item.WorkingDirectory);
            Check.Equal(shortcut, item.IconPath);
            Check.Equal(internetShortcut, ShortcutService.ImportPath(internetShortcut).TargetPath);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    });
});
suite.Run("Shell launch passes arguments and working directory to a real process", () =>
{
    WithTempDirectory(directory =>
    {
        string commandProcessor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string marker = Path.Combine(directory, "launch-working-directory.txt");
        ShortcutService.Launch(new ShortcutItem
        {
            Name = "Isolated launch probe", TargetPath = commandProcessor,
            Arguments = "/d /c cd>launch-working-directory.txt", WorkingDirectory = directory
        });
        Check.True(SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0,
            TimeSpan.FromSeconds(5)));
        Check.Equal(directory, File.ReadAllText(marker).Trim());
        // Allow cmd to release its current-directory handle before the temporary folder is removed.
        Thread.Sleep(100);
    });
});
suite.Run("Icon resource locations preserve explicit indices and literal comma filenames", () =>
{
    WithTempDirectory(directory =>
    {
        string asset = Path.Combine(directory, "原始 图标,theme.ico");
        File.WriteAllBytes(asset, new byte[] { 0 });
        Check.True(ShortcutIconMetadata.TryParse(asset, out var literal, out int index, out bool indexed));
        Check.Equal(asset, literal); Check.Equal(0, index); Check.False(indexed);
        foreach (int expected in new[] { 0, 3, -16769 })
        {
            Check.True(ShortcutIconMetadata.TryParse('\"' + asset + "\"," + expected,
                out var parsed, out index, out indexed));
            Check.Equal(asset, parsed); Check.Equal(expected, index); Check.True(indexed);
        }
        Check.False(ShortcutIconMetadata.TryParse(asset + ",not-an-index", out _, out _, out _));
        Check.False(ShortcutIconMetadata.TryParse(asset + "\0", out _, out _, out _));
    });
});
suite.Run("Internet shortcut import preserves Unicode icon path and negative resource ID", () =>
{
    WithTempDirectory(directory =>
    {
        string asset = Path.Combine(directory, "图标 图像.dll");
        string shortcut = Path.Combine(directory, "网页.url");
        File.WriteAllBytes(asset, new byte[] { 0 });
        File.WriteAllText(shortcut, "[InternetShortcut]\r\nURL=https://example.com/\r\nIconFile="
            + asset + "\r\nIconIndex=-16769\r\n");
        var item = ShortcutService.ImportPath(shortcut);
        Check.Equal(shortcut, item.TargetPath);
        Check.Equal(asset + ",-16769", item.IconPath);
    });
});
suite.Run("Shell shortcut import preserves original custom icon metadata without launch", () =>
{
    WithTempDirectory(directory =>
    {
        string asset = Path.Combine(directory, "shortcut icon.ico");
        string target = Path.Combine(directory, "harmless target.txt");
        string shortcut = Path.Combine(directory, "Original shortcut.lnk");
        File.WriteAllBytes(asset, new byte[] { 0 }); File.WriteAllText(target, "metadata test");
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic instance = shell!;
            link = instance.CreateShortcut(shortcut);
            dynamic entry = link; entry.TargetPath = target; entry.IconLocation = asset + ",4"; entry.Save();
            var item = ShortcutService.ImportPath(shortcut);
            Check.Equal(shortcut, item.TargetPath);
            Check.Equal(asset + ",4", item.IconPath);
        }
        finally
        {
            if (link is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    });
});
suite.Run("Name sorting is stable case insensitive and puts unknown names last in either direction", () =>
{
    var items = new List<ShortcutItem>
    {
        new() { Id = "b", Name = "Beta" },
        new() { Id = "unknown", Name = " " },
        new() { Id = "a1", Name = "alpha" },
        new() { Id = "a2", Name = "ALPHA" }
    };
    Check.Equal("a1,a2,b,unknown", SortIds(items, ShortcutSortKey.Name));
    Check.Equal("b,a1,a2,unknown", SortIds(items, ShortcutSortKey.Name, true));
    Check.Equal("b,unknown,a1,a2", string.Join(",", items.Select(item => item.Id)));
});
suite.Run("Added sorting uses saved dates with stable ties and unknown legacy dates last", () =>
{
    var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    var items = new List<ShortcutItem>
    {
        new() { Id = "unknown" },
        new() { Id = "new", AddedAtUtc = date.AddDays(1) },
        new() { Id = "old1", AddedAtUtc = date },
        new() { Id = "old2", AddedAtUtc = date }
    };
    Check.Equal("old1,old2,new,unknown", SortIds(items, ShortcutSortKey.Added));
    Check.Equal("new,old1,old2,unknown", SortIds(items, ShortcutSortKey.Added, true));
});
suite.Run("Manual sorting copies the collection while retaining item identity and order", () =>
{
    var items = new List<ShortcutItem> { new() { Id = "second" }, new() { Id = "first" } };
    var result = ShortcutSortService.SortAsync(items, ShortcutSortKey.Manual, true).GetAwaiter().GetResult();
    Check.False(ReferenceEquals(items, result));
    Check.True(ReferenceEquals(items[0], result[0]));
    Check.Equal("second,first", string.Join(",", result.Select(item => item.Id)));
    result.Clear();
    Check.Equal(2, items.Count);
    Check.Equal(0, ShortcutSortService.SortAsync(Array.Empty<ShortcutItem>(), ShortcutSortKey.Name, false).GetAwaiter().GetResult().Count);
});
suite.Run("Size and modified sorting read files without counting folders or treating missing paths as zero", () =>
{
    WithTempDirectory(directory =>
    {
        string small = Path.Combine(directory, "small.txt"), large = Path.Combine(directory, "large.txt");
        string zero = Path.Combine(directory, "zero.txt"), folder = Path.Combine(directory, "folder");
        File.WriteAllBytes(small, new byte[5]); File.WriteAllBytes(large, new byte[19]); File.WriteAllBytes(zero, Array.Empty<byte>());
        Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "nested.txt"), new byte[100]);
        var old = new DateTime(2024, 1, 1, 1, 2, 3, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(small, old); File.SetLastWriteTimeUtc(large, old.AddDays(1)); File.SetLastWriteTimeUtc(zero, old.AddDays(2));
        Directory.SetLastWriteTimeUtc(folder, old.AddDays(3));
        var items = new List<ShortcutItem>
        {
            new() { Id = "missing", TargetPath = Path.Combine(directory, "missing.txt") },
            new() { Id = "large", TargetPath = large }, new() { Id = "folder", TargetPath = folder },
            new() { Id = "web", TargetPath = "https://example.com/" },
            new() { Id = "small", TargetPath = small }, new() { Id = "zero", TargetPath = zero }
        };
        Check.Equal("zero,small,large,missing,folder,web", SortIds(items, ShortcutSortKey.Size));
        Check.Equal("large,small,zero,missing,folder,web", SortIds(items, ShortcutSortKey.Size, true));
        Check.Equal("small,large,zero,folder,missing,web", SortIds(items, ShortcutSortKey.Modified));
        Check.Equal("folder,zero,large,small,missing,web", SortIds(items, ShortcutSortKey.Modified, true));
    });
});
suite.Run("Type sorting recognizes extensions folders and websites with unknown types last", () =>
{
    WithTempDirectory(directory =>
    {
        string folder = Path.Combine(directory, "folder"), noExtension = Path.Combine(directory, "noextension");
        Directory.CreateDirectory(folder); File.WriteAllText(noExtension, "none");
        var entries = new[]
        {
            (new ShortcutItem { Id = "zzz", TargetPath = Path.Combine(directory, "missing.zzz") }, (string?)".zzz"),
            (new ShortcutItem { Id = "unknown", TargetPath = noExtension }, (string?)null),
            (new ShortcutItem { Id = "folder", TargetPath = folder }, (string?)"文件夹"),
            (new ShortcutItem { Id = "aaa", TargetPath = Path.Combine(directory, "missing.aaa") }, (string?)".aaa"),
            (new ShortcutItem { Id = "web", TargetPath = "https://example.com/" }, (string?)"网址")
        };
        var known = entries.Where(entry => entry.Item2 is not null);
        foreach (bool descending in new[] { false, true })
        {
            var sorted = descending
                ? known.OrderByDescending(entry => entry.Item2, StringComparer.CurrentCultureIgnoreCase)
                : known.OrderBy(entry => entry.Item2, StringComparer.CurrentCultureIgnoreCase);
            string expected = string.Join(",", sorted.Select(entry => entry.Item1.Id).Append("unknown"));
            Check.Equal(expected, SortIds(entries.Select(entry => entry.Item1), ShortcutSortKey.Type, descending));
        }
    });
});
suite.Run("Sorting snapshots caller collections and validates cancellation and invalid keys", () =>
{
    var items = new List<ShortcutItem> { new() { Id = "z", Name = "Zulu" }, new() { Id = "a", Name = "Alpha" } };
    var sorting = ShortcutSortService.SortAsync(items, ShortcutSortKey.Name, false);
    items.Clear();
    Check.Equal("a,z", string.Join(",", sorting.GetAwaiter().GetResult().Select(item => item.Id)));
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    Check.Throws<OperationCanceledException>(() => ShortcutSortService.SortAsync(items, ShortcutSortKey.Size, false, cancellation.Token).GetAwaiter().GetResult());
    Check.Throws<ArgumentOutOfRangeException>(() => ShortcutSortService.SortAsync(items, (ShortcutSortKey)999, false));
    Check.Throws<ArgumentNullException>(() => ShortcutSortService.SortAsync(null!, ShortcutSortKey.Manual, false));
    Check.Throws<ArgumentNullException>(() => ShortcutSortService.SortAsync(new ShortcutItem[] { null! }, ShortcutSortKey.Manual, false));
});
suite.Run("Shortcut sorting follows the saved link target metadata without launching it", () =>
{
    WithTempDirectory(directory =>
    {
        string marker = Path.Combine(directory, "must-not-launch.txt");
        string target = Path.Combine(directory, "read-only-metadata.cmd");
        string reference = Path.Combine(directory, "large.txt");
        string shortcut = Path.Combine(directory, "original.lnk");
        File.WriteAllText(target, "@echo off\r\necho launched>must-not-launch.txt\r\n");
        File.WriteAllBytes(reference, new byte[2048]);
        var old = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, old); File.SetLastWriteTimeUtc(reference, old.AddDays(1));
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic instance = shell!; link = instance.CreateShortcut(shortcut);
            dynamic entry = link; entry.TargetPath = target; entry.WorkingDirectory = directory; entry.Save();
            var items = new[] { new ShortcutItem { Id = "reference", TargetPath = reference }, new ShortcutItem { Id = "link", TargetPath = shortcut } };
            Check.Equal("link,reference", SortIds(items, ShortcutSortKey.Size));
            Check.Equal("reference,link", SortIds(items, ShortcutSortKey.Modified, true));
            Check.Equal("link,reference", SortIds(items, ShortcutSortKey.Type));
            Check.False(File.Exists(marker));
            Check.Equal(shortcut, items[1].TargetPath);
        }
        finally
        {
            if (link is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    });
});
// Optional EFS integration: creates and removes only its own GUID child directory.
// The supplied parent must already use EFS; this test never encrypts/decrypts it.
if (args.Length == 2 && args[0] == "--efs-parent")
{
    SettingsStoreEfsTests.Register(suite, args[1]);
    suite.Run("Encrypted settings preserve ADS, access rules and prior data on failure", () =>
    {
        string directory = Path.Combine(Path.GetFullPath(args[1]),
            "edgedock-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(directory);
            var settings = new DockSettings();
            store.Save(settings);
            var original = new FileInfo(store.SettingsFilePath);
            Check.True((original.Attributes & FileAttributes.Encrypted) != 0);
            original.Attributes |= FileAttributes.Hidden | FileAttributes.NotContentIndexed;
            string streamPath = store.SettingsFilePath + ":edgedock-test-metadata";
            File.WriteAllText(streamPath, "preserve metadata");
            string accessRules = original.GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            DateTime creation = original.CreationTimeUtc;
            for (int i = 0; i < 30; i++)
            {
                settings.HideDelayMs = 650 + i;
                store.Save(settings);
                Check.Equal(settings.HideDelayMs, store.Load().HideDelayMs);
                Check.True(store.LastError is null);
                Check.Equal("preserve metadata", File.ReadAllText(streamPath));
            }
            var result = new FileInfo(store.SettingsFilePath);
            const FileAttributes expected = FileAttributes.Encrypted | FileAttributes.Hidden
                | FileAttributes.NotContentIndexed;
            Check.Equal(expected, result.Attributes & expected);
            Check.Equal(creation, result.CreationTimeUtc);
            Check.Equal(accessRules, result.GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            using (var blocker = new FileStream(store.SettingsFilePath, FileMode.Open,
                FileAccess.Read, FileShare.None))
            {
                Check.Throws<IOException>(() => store.Save(new DockSettings { HideDelayMs = 9999 }));
            }
            Check.Equal(679, store.Load().HideDelayMs);
            Check.Equal("preserve metadata", File.ReadAllText(streamPath));
            Check.Equal(1, Directory.EnumerateFiles(directory).Count());
        }
        finally { Directory.Delete(directory, recursive: true); }
    });
}
else if (args.Length != 0)
{
    Console.WriteLine("Usage: EDock.CoreTests [--efs-parent <existing encrypted directory>]");
    return 2;
}
suite.Run("Legacy arrays migrate to independent named lists without changing items or disk data", () =>
{
    WithTempDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        string json = """
            {"HeaderText":"旧标题","SeparateSideSettings":true,"RightSettings":{"HeaderText":"右边旧标题"},
             "LeftShortcuts":[{"Id":"one","Name":"原有","TargetPath":"C:\\original.lnk","ImageCrop":"Top"}],
             "RightShortcuts":[{"Id":"two","Name":"右边","TargetPath":"C:\\right.lnk"}]}
            """;
        File.WriteAllText(store.SettingsFilePath, json);
        var loaded = store.Load();
        Check.Equal("旧标题", loaded.GetActiveList(DockSide.Left).Name);
        Check.Equal("右边旧标题", loaded.GetActiveList(DockSide.Right).Name);
        Check.Equal("one", loaded.GetActiveList(DockSide.Left).Shortcuts.Single().Id);
        Check.Equal("Top", loaded.LeftShortcuts.Single().ImageCrop);
        Check.True(loaded.LeftShortcuts.Single().AddedAtUtc is null);
        Check.True(ReferenceEquals(loaded.LeftShortcuts, loaded.GetActiveList(DockSide.Left).Shortcuts));
        loaded.Normalize();
        Check.Equal(1, loaded.LeftLists.Count);
        Check.Equal(json, File.ReadAllText(store.SettingsFilePath));
    });
});
suite.Run("Lists isolate sides and synchronize legacy active references while switching", () =>
{
    var settings = new DockSettings();
    var first = settings.GetActiveList(DockSide.Left);
    var right = settings.GetActiveList(DockSide.Right);
    first.Shortcuts.Add(new ShortcutItem { Id = "left", TargetPath = @"C:\left.txt" });
    var added = settings.AddList(DockSide.Left, "  工作  ");
    Check.Equal("工作", added.Name);
    Check.Equal(2, settings.LeftLists.Count);
    Check.Equal(1, settings.RightLists.Count);
    Check.True(ReferenceEquals(right, settings.GetActiveList(DockSide.Right)));
    Check.Equal(0, settings.LeftShortcuts.Count);
    Check.False(settings.SelectList(DockSide.Left, "missing"));
    Check.True(settings.SelectList(DockSide.Left, first.Id));
    Check.True(ReferenceEquals(first.Shortcuts, settings.LeftShortcuts));
    Check.Equal("left", settings.LeftShortcuts.Single().Id);
    Check.True(settings.RenameList(DockSide.Left, first.Id, "  常用  "));
    Check.Equal("常用", first.Name);
    Check.True(settings.RenameList(DockSide.Left, first.Id, new string('中', 100)));
    Check.Equal(80, first.Name.Length);
});
suite.Run("Deleting lists merges every duplicate into the next or previous neighbor and protects last list", () =>
{
    var settings = new DockSettings();
    var first = settings.GetActiveList(DockSide.Left);
    first.Shortcuts.Add(new ShortcutItem { Id = "same", TargetPath = @"C:\same.txt", Name = "first" });
    var second = settings.AddList(DockSide.Left, "second");
    second.Shortcuts.Add(new ShortcutItem { Id = "same", TargetPath = @"C:\same.txt", Name = "second" });
    second.SortKey = ShortcutSortKey.Name;
    var third = settings.AddList(DockSide.Left, "third");
    third.Shortcuts.Add(new ShortcutItem { TargetPath = @"C:\third.txt", Name = "third" });
    settings.SelectList(DockSide.Left, first.Id);
    Check.True(settings.DeleteList(DockSide.Left, first.Id));
    Check.True(ReferenceEquals(second, settings.GetActiveList(DockSide.Left)));
    Check.Equal("second,first", string.Join(",", second.Shortcuts.Select(item => item.Name)));
    Check.Equal(2, second.Shortcuts.Select(item => item.Id).Distinct().Count());
    Check.Equal(ShortcutSortKey.Manual, second.SortKey);
    Check.True(settings.DeleteList(DockSide.Left, third.Id));
    Check.Equal("second,first,third", string.Join(",", second.Shortcuts.Select(item => item.Name)));
    Check.False(settings.DeleteList(DockSide.Left, second.Id));
    Check.Equal(3, settings.LeftShortcuts.Count);
});
suite.Run("Multi list persistence retains inactive items active IDs timestamps and sort preferences", () =>
{
    WithTempDirectory(directory =>
    {
        var settings = new DockSettings();
        var first = settings.GetActiveList(DockSide.Left);
        first.Shortcuts.Add(new ShortcutItem { TargetPath = @"C:\inactive.txt", Name = "inactive" });
        var second = settings.AddList(DockSide.Left, "游戏");
        second.SortKey = ShortcutSortKey.Size; second.SortDescending = true;
        var stamp = DateTimeOffset.Parse("2026-10-17T02:00:00Z");
        second.Shortcuts.Add(new ShortcutItem { TargetPath = @"C:\active.txt", AddedAtUtc = stamp });
        var right = settings.AddList(DockSide.Right, "独立右侧");
        right.Shortcuts.Add(new ShortcutItem { TargetPath = @"C:\right.txt" });
        var store = new SettingsStore(directory); store.Save(settings);
        var loaded = store.Load();
        Check.Equal(second.Id, loaded.ActiveLeftListId);
        Check.Equal(right.Id, loaded.ActiveRightListId);
        Check.Equal("inactive", loaded.LeftLists[0].Shortcuts.Single().Name);
        Check.Equal(ShortcutSortKey.Size, loaded.GetActiveList(DockSide.Left).SortKey);
        Check.True(loaded.GetActiveList(DockSide.Left).SortDescending);
        Check.Equal(stamp, loaded.LeftShortcuts.Single().AddedAtUtc!.Value);
        store.Save(loaded); loaded = store.Load();
        Check.Equal(2, loaded.LeftLists.Count);
        Check.Equal(2, loaded.RightLists.Count);
        Check.Equal(1, loaded.LeftLists[0].Shortcuts.Count);
    });
});
suite.Run("Malformed list identifiers and active IDs repair without replacing inactive data", () =>
{
    var settings = new DockSettings
    {
        ActiveLeftListId = "missing",
        LeftShortcuts = new() { new() { TargetPath = @"C:\stale-legacy.txt" } },
        LeftLists = new()
        {
            new() { Id = "duplicate", Name = " ", Shortcuts = new() { new() { TargetPath = @"C:\one.txt" } } },
            new() { Id = "duplicate", Name = "two", SortKey = (ShortcutSortKey)900, Shortcuts = new() { new() { TargetPath = @"C:\two.txt" } } }
        }
    };
    settings.Normalize();
    Check.Equal(2, settings.LeftLists.Count);
    Check.Equal(2, settings.LeftLists.Select(list => list.Id).Distinct().Count());
    Check.Equal("默认列表", settings.LeftLists[0].Name);
    Check.Equal(ShortcutSortKey.Manual, settings.LeftLists[1].SortKey);
    Check.Equal(@"C:\two.txt", settings.LeftLists[1].Shortcuts.Single().TargetPath);
    Check.Equal(@"C:\one.txt", settings.LeftShortcuts.Single().TargetPath);
    string ids = string.Join(",", settings.LeftLists.Select(list => list.Id));
    settings.Normalize();
    Check.Equal(ids, string.Join(",", settings.LeftLists.Select(list => list.Id)));
});
DesktopShortcutTransferTests.Register(suite);
ManagedShortcutStorageTests.Register(suite);
ConfigurationDirectoryMigrationTests.Register(suite, args.Length == 2 && args[0] == "--efs-parent" ? args[1] : null);
WindowClassReaderTests.Register(suite);
MonitorLookupTests.Register(suite);
return suite.Finish();

static string SortIds(IEnumerable<ShortcutItem> items, ShortcutSortKey key, bool descending = false) =>
    string.Join(",", ShortcutSortService.SortAsync(items, key, descending).GetAwaiter().GetResult().Select(item => item.Id));

static void WithTempDirectory(Action<string> test)
{
    string directory = Path.Combine(Path.GetTempPath(), "EDock.CoreTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(directory); }
    finally { Directory.Delete(directory, recursive: true); }
}

sealed class CoreSuite
{
    private int passed;
    private int failed;
    public void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
    }
    public int Finish()
    {
        Console.WriteLine($"{passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}

static class Check
{
    public static void Near(double expected, double actual, double tolerance)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected} within {tolerance}; actual {actual}.");
    }
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
    public static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }
    public static void False(bool value) => True(!value);
    public static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
