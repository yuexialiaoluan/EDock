using System.Runtime.InteropServices;

namespace EDock.Services;

internal enum BackgroundWindowKind { Other, Desktop, Taskbar }

/// <summary>Reads frequently polled window classes without allocating strings or retaining HWND state.</summary>
internal static class WindowClassReader
{
    internal static unsafe BackgroundWindowKind Read(nint window)
    {
        if (window == 0) return BackgroundWindowKind.Other;
        Span<char> buffer = stackalloc char[128];
        int length;
        fixed (char* pointer = buffer)
            length = GetClassName(window, pointer, buffer.Length);
        return length > 0 ? Classify(buffer[..length]) : BackgroundWindowKind.Other;
    }

    internal static BackgroundWindowKind Classify(ReadOnlySpan<char> className)
    {
        if (className.SequenceEqual("Progman") || className.SequenceEqual("WorkerW"))
            return BackgroundWindowKind.Desktop;
        if (className.SequenceEqual("Shell_TrayWnd") || className.SequenceEqual("Shell_SecondaryTrayWnd"))
            return BackgroundWindowKind.Taskbar;
        return BackgroundWindowKind.Other;
    }

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", ExactSpelling = true)]
    private static extern unsafe int GetClassName(nint window, char* className, int maxCount);
}
