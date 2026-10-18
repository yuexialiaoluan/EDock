using System.Runtime.InteropServices;

namespace EDock.Services;

// StorageFile thumbnails do not reliably follow MSI, game-launcher or AppsFolder
// shortcuts. Ask Explorer's native icon provider without resolving or launching.
internal static class NativeShellIcon
{
    internal sealed record Pixels(byte[] Data, int Width, int Height);

    internal static Pixels? Extract(string path, int size, bool linkTarget = false)
    {
        size = Math.Clamp(size, 16, 512);
        object? item = null;
        IntPtr bitmap = IntPtr.Zero, idList = IntPtr.Zero;
        try
        {
            int hr;
            var iid = typeof(IShellItemImageFactory).GUID;
            if (linkTarget)
            {
                idList = ShortcutIconMetadata.ReadLinkTargetIdList(path);
                if (idList == IntPtr.Zero) return null;
                hr = SHCreateItemFromIDList(idList, ref iid, out item);
            }
            else
            {
                // An AppUserModelID obtained from a target PIDL needs AppsFolder.
                string parsingName = !File.Exists(path) && !Directory.Exists(path)
                    && !path.Contains('\\') && path.Contains('!')
                    ? "shell:AppsFolder\\" + path : path;
                hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out item);
            }
            if (hr < 0 || item is not IShellItemImageFactory factory) return null;
            // Leave BIGGERSIZEOK unset to keep decoded assets at tile resolution.
            var requestedSize = new NativeSize { Width = size, Height = size };
            if (factory.GetImage(requestedSize, 0x4, out bitmap) < 0 || bitmap == IntPtr.Zero) return null;
            return ReadBitmap(bitmap, size);
        }
        catch { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (idList != IntPtr.Zero) Marshal.FreeCoTaskMem(idList);
            if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }
    }

    private static Pixels? ReadBitmap(IntPtr bitmap, int maximumSize)
    {
        // Check before allocating pixels even if a third-party shell provider
        // ignores the requested dimensions. The caller can try its fallbacks.
        if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var native) == 0
            || native.Width <= 0 || native.Height == 0 || native.Width > maximumSize
            || Math.Abs((long)native.Height) > maximumSize) return null;
        int height = Math.Abs(native.Height);
        var info = new BitmapInfo { Header = new BitmapInfoHeader
        { Size = 40, Width = native.Width, Height = -height, Planes = 1, BitCount = 32 } };
        var pixels = new byte[checked(native.Width * height * 4)];
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) != height) return null;
        }
        finally { DeleteDC(dc); }
        // Old shell providers return opaque RGB without an alpha plane. Some
        // image factories return straight-alpha DIBs (notably AppsFolder), while
        // others return premultiplied BGRA. Normalize only a demonstrably straight
        // bitmap so the XAML renderer cannot create bright edge halos.
        bool hasAlpha = false;
        for (int p = 3; p < pixels.Length; p += 4) hasAlpha |= pixels[p] != 0;
        if (!hasAlpha) for (int p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
        bool straightAlpha = false;
        for (int p = 0; p < pixels.Length; p += 4)
            straightAlpha |= pixels[p] > pixels[p + 3] || pixels[p + 1] > pixels[p + 3] || pixels[p + 2] > pixels[p + 3];
        if (straightAlpha)
            for (int p = 0; p < pixels.Length; p += 4)
                for (int channel = 0; channel < 3; ++channel)
                    pixels[p + channel] = (byte)((pixels[p + channel] * pixels[p + 3] + 127) / 255);
        return new Pixels(pixels, native.Width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? item);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateItemFromIDList(IntPtr idList, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? item);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr item, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);
}
