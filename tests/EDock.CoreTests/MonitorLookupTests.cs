using System.Runtime.InteropServices;
using EDock.Interop;

internal static class MonitorLookupTests
{
    internal static void Register(CoreSuite suite)
    {
        suite.Run("Monitor choices and lookup agree with the native extended monitor API", () =>
        {
            foreach (var (monitor, deviceName) in ReadMonitors())
            {
                Check.Equal(deviceName, NativeMethods.GetMonitorDeviceName(monitor));
                Check.True(NativeMethods.GetMonitorDeviceNames().Contains(deviceName));
                Check.Equal(monitor, NativeMethods.FindMonitor(deviceName));
                Check.Equal(monitor, NativeMethods.FindMonitor(deviceName.ToLowerInvariant()));
            }
            Check.Equal(string.Empty, NativeMethods.GetMonitorDeviceName(0));
            Check.Equal((nint)0, NativeMethods.FindMonitor(string.Empty));
            Check.Equal((nint)0, NativeMethods.FindMonitor(@"\\.\EDock.DisconnectedMonitor"));
        });
        suite.Run("Repeated selected-monitor polling allocates no managed memory", () =>
        {
            var (monitor, deviceName) = ReadMonitors()[0];
            for (int i = 0; i < 100; i++) _ = NativeMethods.FindMonitor(deviceName);
            long before = GC.GetAllocatedBytesForCurrentThread();
            nint actual = 0;
            for (int i = 0; i < 20_000; i++) actual = NativeMethods.FindMonitor(deviceName);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(monitor, actual);
            Check.Equal(0L, allocated);
        });
    }

    private static List<(nint Monitor, string DeviceName)> ReadMonitors()
    {
        var monitors = new List<(nint, string)>();
        EnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), DeviceName = string.Empty };
            if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Cannot read native monitor info.");
            monitors.Add((monitor, info.DeviceName));
            return true;
        };
        Check.True(EnumDisplayMonitors(0, 0, callback, 0));
        GC.KeepAlive(callback);
        Check.True(monitors.Count > 0);
        return monitors;
    }

    private delegate bool EnumProc(nint monitor, nint hdc, nint rect, nint data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeMethods.Rect Monitor;
        public NativeMethods.Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, EnumProc callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
