using System.Runtime.InteropServices;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _callbacks = new();
        public DispatcherQueueTimer Timer { get; } = new();
        public bool AcceptEnqueue { get; set; } = true;
        public DispatcherQueueTimer CreateTimer() => Timer;
        public bool TryEnqueue(Action callback) { if (!AcceptEnqueue) return false; _callbacks.Enqueue(callback); return true; }
        public void Drain() { while (_callbacks.TryDequeue(out var callback)) callback(); }
    }
    public sealed class DispatcherQueueTimer
    {
        private TimeSpan _interval;
        public int IntervalReads { get; private set; }
        public int IntervalWrites { get; private set; }
        public TimeSpan Interval { get { IntervalReads++; return _interval; } set { IntervalWrites++; _interval = value; } }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event Action<DispatcherQueueTimer, object>? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (IsRunning) Tick?.Invoke(this, EventArgs.Empty); }
    }
}
namespace EDock.Models { public enum DockSide { Left, Right } }
namespace EDock.Interop
{
    internal static class NativeMethods
    {
        internal const int VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkLeftWindows = 0x5B, VkRightWindows = 0x5C;
        internal const int VkW = 0x57, VkS = 0x53, VkSpace = 0x20;
        internal const int VkLeftControl = 0xA2, VkRightControl = 0xA3, VkLeftShift = 0xA0, VkRightShift = 0xA1, VkLeftMenu = 0xA4, VkRightMenu = 0xA5;
        internal const int WhKeyboardLowLevel = 13, WhMouseLowLevel = 14;
        internal const int WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;
        internal const int WmLeftButtonDown = 0x201, WmMouseWheel = 0x20A, WmMouseHorizontalWheel = 0x20E;
        internal const uint GuiInteractionModes = 0x1e;
        internal delegate nint KeyboardHookProc(int code, nint message, nint data);
        internal delegate nint MouseHookProc(int code, nint message, nint data);
        [StructLayout(LayoutKind.Sequential)] internal struct LowLevelKeyboardData
        {
            internal uint VirtualKey, ScanCode, Flags, Time;
            internal nuint ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
        {
            internal uint Size, Flags;
            internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal Rect CaretRect;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct LowLevelMouseData
        {
            internal Point Position;
            internal uint MouseData, Flags, Time;
            internal nuint ExtraInfo;
        }
        internal static KeyboardHookProc? KeyboardHook;
        internal static MouseHookProc? MouseHook;
        internal static bool KeyboardHookAvailable = true, MouseHookAvailable = true, ThrowInstall, InHook;
        internal static int UnhookCount;
        internal static List<(int Key, bool Down, bool Suppressed)> Events { get; } = new();
        private static HashSet<int> PhysicalKeys { get; } = new();
        internal static HashSet<int> Keys { get; } = new();
        internal static string ForegroundClass { get; set; } = "Progman";
        internal static bool Minimized { get; set; }
        internal static bool HasForeground { get; set; } = true;
        internal static nint ForegroundWindow { get; set; } = 123;
        internal static bool WindowEnabled { get; set; } = true;
        internal static bool GuiQuerySuccess { get; set; } = true;
        internal static uint GuiFlags { get; set; }
        internal static nint GuiActiveOverride { get; set; }
        internal static nint GuiCapture { get; set; }
        internal static nint GuiCaret { get; set; }
        internal static nint GuiMenuOwner { get; set; }
        internal static nint GuiMoveSize { get; set; }
        internal static nint ForegroundAfterGuiQuery { get; set; }
        internal static int GuiThreadQueries { get; private set; }
        internal static void Reset()
        {
            if (KeyboardHook is not null || MouseHook is not null) throw new Exception("Previous fixture leaked native hooks");
            Keys.Clear(); PhysicalKeys.Clear(); Events.Clear();
            KeyboardHookAvailable = MouseHookAvailable = HasForeground = true;
            ThrowInstall = InHook = Minimized = false; UnhookCount = 0; ForegroundClass = "Progman";
            ForegroundWindow = 123; WindowEnabled = GuiQuerySuccess = true;
            GuiFlags = 0; GuiActiveOverride = GuiCapture = GuiCaret = GuiMenuOwner = GuiMoveSize = ForegroundAfterGuiQuery = 0; GuiThreadQueries = 0;
        }
        internal static nint SetWindowsHookEx(int type, KeyboardHookProc hook, nint module, uint thread)
        {
            if (ThrowInstall) throw new EntryPointNotFoundException("fake installation failure");
            if (!KeyboardHookAvailable) return 0;
            if (type != WhKeyboardLowLevel) throw new Exception("Wrong keyboard hook type");
            KeyboardHook = hook; return 456;
        }
        internal static nint SetWindowsHookEx(int type, MouseHookProc hook, nint module, uint thread)
        {
            if (ThrowInstall) throw new EntryPointNotFoundException("fake installation failure");
            if (!MouseHookAvailable) return 0;
            if (type != WhMouseLowLevel) throw new Exception("Wrong mouse hook type");
            MouseHook = hook; return 789;
        }
        internal static bool UnhookWindowsHookEx(nint hook)
        {
            UnhookCount++;
            if (hook == 456) KeyboardHook = null;
            else if (hook == 789) MouseHook = null;
            else throw new Exception("Unknown hook handle");
            return true;
        }
        internal static nint CallNextHookEx(nint hook, int code, nint message, nint data) => 0;
        internal static nint GetModuleHandle(string? name) => 1;
        internal static void SetKeys(params int[] next)
        {
            foreach (int key in PhysicalKeys.Except(next).ToArray()) SendKey(key, false);
            foreach (int key in next.Except(PhysicalKeys).ToArray()) SendKey(key, true);
        }
        internal static bool SendKey(int key, bool down, int code = 0)
        {
            nint data = Marshal.AllocHGlobal(Marshal.SizeOf<LowLevelKeyboardData>());
            bool suppressed;
            try
            {
                Marshal.StructureToPtr(new LowLevelKeyboardData { VirtualKey = (uint)key }, data, false);
                InHook = true;
                suppressed = KeyboardHook?.Invoke(code, down ? WmKeyDown : WmKeyUp, data) == 1;
            }
            finally { InHook = false; Marshal.FreeHGlobal(data); }
            if (down) PhysicalKeys.Add(key); else PhysicalKeys.Remove(key);
            // Native low-level hooks precede asynchronous key-state updates.
            if (!suppressed) { if (down) Keys.Add(key); else Keys.Remove(key); }
            Events.Add((key, down, suppressed));
            return suppressed;
        }
        internal static bool SendWheel(int delta, int message = WmMouseWheel, int code = 0)
        {
            nint data = Marshal.AllocHGlobal(Marshal.SizeOf<LowLevelMouseData>());
            try
            {
                Marshal.StructureToPtr(new LowLevelMouseData { MouseData = unchecked((uint)(ushort)(short)delta << 16) }, data, false);
                InHook = true;
                return MouseHook?.Invoke(code, message, data) == 1;
            }
            finally { InHook = false; Marshal.FreeHGlobal(data); }
        }
        internal static short GetAsyncKeyState(int key)
        {
            bool pressed = Keys.Contains(key) || key switch
            {
                VkControl => Keys.Contains(VkLeftControl) || Keys.Contains(VkRightControl),
                VkShift => Keys.Contains(VkLeftShift) || Keys.Contains(VkRightShift),
                VkMenu => Keys.Contains(VkLeftMenu) || Keys.Contains(VkRightMenu),
                _ => false
            };
            return pressed ? unchecked((short)0x8000) : (short)0;
        }
        internal static nint GetForegroundWindow() => HasForeground ? ForegroundWindow : 0;
        internal static bool IsIconic(nint hwnd) => Minimized;
        internal static bool IsWindowEnabled(nint hwnd) => WindowEnabled;
        internal static bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info)
        {
            GuiThreadQueries++;
            if (threadId != 0 || info.Size != Marshal.SizeOf<GuiThreadInfo>()) throw new Exception("Invalid GUI thread query");
            info.Flags = GuiFlags;
            info.Active = GuiActiveOverride != 0 ? GuiActiveOverride : ForegroundWindow;
            info.Capture = GuiCapture;
            info.Caret = GuiCaret;
            info.MenuOwner = GuiMenuOwner;
            info.MoveSize = GuiMoveSize;
            if (ForegroundAfterGuiQuery != 0) ForegroundWindow = ForegroundAfterGuiQuery;
            return GuiQuerySuccess;
        }
    }
}
namespace EDock.Services
{
    internal enum BackgroundWindowKind { Other, Desktop, Taskbar }
    internal static class WindowClassReader
    {
        // Native class-name classification has separate production-helper tests.
        // Keep hook/timer fixtures independent of real foreground windows.
        internal static BackgroundWindowKind Read(nint window) => EDock.Interop.NativeMethods.ForegroundClass switch
        {
            "Progman" or "WorkerW" => BackgroundWindowKind.Desktop,
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" => BackgroundWindowKind.Taskbar,
            _ => BackgroundWindowKind.Other
        };
    }
}
