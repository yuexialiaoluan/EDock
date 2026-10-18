using System.Runtime.InteropServices;
using System.Text;
using EDock.Services;

internal static class WindowClassReaderTests
{
    internal static void Register(CoreSuite suite)
    {
        suite.Run("Window-class polling preserves exact desktop and taskbar matches", () =>
        {
            foreach (string name in new[] { "Progman", "WorkerW" })
                Check.Equal(BackgroundWindowKind.Desktop, WindowClassReader.Classify(name));
            foreach (string name in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                Check.Equal(BackgroundWindowKind.Taskbar, WindowClassReader.Classify(name));
            foreach (string name in new[] { "", "progman", "WorkerW1", "Shell_TrayWndExtra", "Shell_SecondaryTrayWn", "ApplicationFrameWindow" })
                Check.Equal(BackgroundWindowKind.Other, WindowClassReader.Classify(name));
            // Reusing a larger buffer must honor only the returned class-name span.
            Check.Equal(BackgroundWindowKind.Desktop, WindowClassReader.Classify("WorkerW_STALE".AsSpan(0, 7)));
        });
        suite.Run("Native window-class reads agree with the previous string-based API", () =>
        {
            foreach (nint window in new[] { (nint)0, (nint)(-1), GetDesktopWindow(), GetShellWindow(), GetForegroundWindow() })
            {
                var className = new StringBuilder(128);
                int length = GetClassName(window, className, className.Capacity);
                var expected = length > 0 ? WindowClassReader.Classify(className.ToString()) : BackgroundWindowKind.Other;
                Check.Equal(expected, WindowClassReader.Read(window));
            }
        });
        suite.Run("Repeated native window-class polling allocates no managed memory", () =>
        {
            nint window = GetDesktopWindow();
            Check.True(window != 0);
            for (int i = 0; i < 100; i++) _ = WindowClassReader.Read(window);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int accumulator = 0;
            for (int i = 0; i < 20_000; i++) accumulator += (int)WindowClassReader.Read(window);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(accumulator);
            Check.Equal(0L, allocated);
        });
    }

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);
}
