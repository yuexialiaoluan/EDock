using EDock.Interop;

namespace EDock.Services;

internal static class DisplayMonitorService
{
    public static string[] GetChoices()
    {
        var names = NativeMethods.GetMonitorDeviceNames();
        return new[] { "主显示器" }.Concat(names.Skip(1)).ToArray();
    }

    public static int GetSelectedIndex(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return 0;
        var names = NativeMethods.GetMonitorDeviceNames();
        int index = Array.FindIndex(names, name => string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
    }

    public static string GetDeviceName(int selectedIndex)
    {
        if (selectedIndex <= 0) return string.Empty;
        var names = NativeMethods.GetMonitorDeviceNames();
        return selectedIndex < names.Length ? names[selectedIndex] : string.Empty;
    }
}
