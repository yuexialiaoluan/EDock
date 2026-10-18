using System.Runtime.InteropServices;
using EDock.Interop;

namespace EDock.Services;

/// <summary>Checks whether unmodified desktop navigation can safely receive dock commands.</summary>
internal static class DesktopInputContext
{
    private static readonly uint GuiThreadInfoSize = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>();

    internal static bool IsDesktopForeground()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || NativeMethods.IsIconic(foreground) || !NativeMethods.IsWindowEnabled(foreground)
            || WindowClassReader.Read(foreground) != BackgroundWindowKind.Desktop)
            return false;

        for (int key = 1; key <= 6; key++)
            if (key != 3 && (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) return false;

        var gui = new NativeMethods.GuiThreadInfo { Size = GuiThreadInfoSize };
        // Explorer stays the foreground window while renaming a desktop item.
        // Its edit control owns a caret, so leave those letters with Explorer.
        return NativeMethods.GetGUIThreadInfo(0, ref gui) && gui.Active == foreground
            && gui.Capture == 0 && gui.Caret == 0 && gui.MenuOwner == 0 && gui.MoveSize == 0
            && (gui.Flags & NativeMethods.GuiInteractionModes) == 0
            && NativeMethods.GetForegroundWindow() == foreground;
    }
}
