using System.Runtime.InteropServices;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _callbacks = new();
        public bool TryEnqueue(Action callback) { _callbacks.Enqueue(callback); return true; }
        public void Drain() { while (_callbacks.TryDequeue(out var action)) action(); }
    }
}

namespace EDock.Interop
{
    internal static class NativeMethods
    {
        internal const int WhKeyboardLowLevel = 13, WmKeyDown = 0x100, WmKeyUp = 0x101,
            WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;
        internal delegate nint KeyboardHookProc(int code, nint message, nint data);
        [StructLayout(LayoutKind.Sequential)] internal struct LowLevelKeyboardData
        {
            internal uint VirtualKey, ScanCode, Flags, Time;
            internal nuint ExtraInfo;
        }
        internal static KeyboardHookProc? Hook;
        internal static nint Foreground = 123;
        internal static bool InstallAvailable = true;
        internal static int InstallCount, ReleaseCount;
        internal static HashSet<int> Keys = new();
        internal static nint SetWindowsHookEx(int type, KeyboardHookProc callback, nint module, uint thread)
        {
            InstallCount++;
            if (!InstallAvailable) return 0;
            Hook = callback;
            return 456;
        }
        internal static bool UnhookWindowsHookEx(nint hook) { Hook = null; ReleaseCount++; return true; }
        internal static nint GetModuleHandle(string? value) => 1;
        internal static nint GetForegroundWindow() => Foreground;
        internal static nint CallNextHookEx(nint hook, int code, nint message, nint data) => 0;
        internal static short GetAsyncKeyState(int key) => Keys.Contains(key) ? unchecked((short)0x8000) : (short)0;
        internal static bool Send(int key, bool down, int code = 0)
        {
            nint data = Marshal.AllocHGlobal(Marshal.SizeOf<LowLevelKeyboardData>());
            try
            {
                Marshal.StructureToPtr(new LowLevelKeyboardData { VirtualKey = (uint)key }, data, false);
                bool result = Hook?.Invoke(code, down ? WmKeyDown : WmKeyUp, data) == 1;
                if (down) Keys.Add(key); else Keys.Remove(key);
                return result;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        internal static void Reset()
        {
            if (Hook != null) throw new Exception("Hook leaked from previous scenario.");
            Foreground = 123; InstallAvailable = true; InstallCount = ReleaseCount = 0; Keys.Clear();
        }
    }
}
