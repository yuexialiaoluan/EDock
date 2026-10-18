using System.Runtime.InteropServices;
using EDock.Services;

// Independent Shell API reference: tests choose flags explicitly and obtain
// raw DIB section pixels without calling the product's GetDIBits normalizer.
internal static class ShellIconReference
{
    internal sealed record Image(byte[] Data, int Width, int Height);

    internal static Image? Extract(string path, int size, uint flags, bool targetIdList)
    {
        nint pidl = 0, bitmap = 0;
        object? factoryObject = null;
        try
        {
            Guid iid = typeof(IShellItemImageFactory).GUID;
            int result;
            if (targetIdList)
            {
                pidl = ShortcutIconMetadata.ReadLinkTargetIdList(path);
                if (pidl == 0) return null;
                result = SHCreateItemFromIDList(pidl, ref iid, out factoryObject);
            }
            else result = SHCreateItemFromParsingName(path, 0, ref iid, out factoryObject);
            if (result < 0 || factoryObject is not IShellItemImageFactory factory) return null;
            if (factory.GetImage(new Size { Width = size, Height = size }, flags, out bitmap) < 0 || bitmap == 0) return null;
            // FromHbitmap discards the alpha channel on transparent Shell DIBs.
            // Read the DIB section directly, preserving its original alpha.
            if (GetObject(bitmap, Marshal.SizeOf<DibSection>(), out var section) == 0
                || section.Bitmap.Bits == 0 || section.Bitmap.BitsPixel != 32) return null;
            int width = section.Bitmap.Width, height = Math.Abs(section.Bitmap.Height);
            var bytes = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                int nativeRow = section.Header.Height < 0 ? row : height - row - 1;
                Marshal.Copy(section.Bitmap.Bits + nativeRow * section.Bitmap.WidthBytes,
                    bytes, row * width * 4, width * 4);
            }
            return new(bytes, width, height);
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (factoryObject is not null && Marshal.IsComObject(factoryObject)) Marshal.FinalReleaseComObject(factoryObject);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DibSection
    { public BitmapInfo Bitmap; public BitmapHeader Header; public uint Mask1, Mask2, Mask3; public nint Section; public uint Offset; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out nint bitmap); }
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHCreateItemFromIDList(nint idList, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint item, int size, out DibSection bitmap);
}
