using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using EDock.Models;

namespace EDock.Services;

/// <summary>Loads original shell resources and user-selected local images at tile resolution.</summary>
public static class ShellIconService
{
    /// <summary>Tries custom appearance, saved original icon, then the unmodified
    /// launch target. Missing saved assets never stop the remaining fallbacks.</summary>
    public static async Task<ImageSource?> LoadShortcutAsync(ShortcutItem item, int size = 256)
    {
        ArgumentNullException.ThrowIfNull(item);
        var candidates = new[] { item.CustomIconPath, item.IconPath, item.TargetPath };
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !tried.Add(candidate)) continue;
            var image = await LoadAsync(candidate, size);
            if (image is not null) return image;
        }
        return null;
    }

    /// <summary>Decodes a local background image, including installed Windows image codecs.</summary>
    public static Task<ImageSource?> LoadImageAsync(string path, int requestedSize = 2048)
    {
        if (!ShortcutIconMetadata.TryParse(path, out var asset, out _, out bool indexed) || indexed)
            return Task.FromResult<ImageSource?>(null);
        return TryLoadRasterAsync(asset, Math.Clamp(requestedSize, 64, 4096));
    }

    public static Task<ImageSource?> LoadAsync(string path, int requestedSize = 256) =>
        LoadAssetAsync(path, Math.Clamp(requestedSize, 16, 512), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static async Task<ImageSource?> LoadAssetAsync(string path, int size, HashSet<string> tried)
    {
        if (!tried.Add(path) || tried.Count > 12) return null;
        if (!ShortcutIconMetadata.TryParse(path, out var asset, out int index, out bool indexed)) return null;
        try
        {
            string extension = Path.GetExtension(asset);
            if (!indexed && (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)))
            {
                // Saved items may retain the original shortcut path. Read its icon
                // location here too; this never resolves or launches the target.
                string original = ShortcutIconMetadata.ResolveOriginal(asset);
                if (!original.Equals(asset, StringComparison.OrdinalIgnoreCase))
                {
                    var shortcutIcon = await LoadAssetAsync(original, size, tried);
                    if (shortcutIcon is not null) return shortcutIcon;
                }
                var linkTarget = ShortcutIconMetadata.ReadLinkIconTarget(asset);
                string target = linkTarget.Target;
                // Packaged links can return a successful generic file image from
                // the shell. Prefer their registered app logo before that fallback.
                var appLogo = await TryLoadPackagedAppLogoAsync(target, size);
                if (appLogo is not null) return appLogo;
                if (!string.IsNullOrEmpty(linkTarget.ExecutablePath))
                {
                    // AppsFolder can expose a desktop app through a namespace
                    // identity whose shell image is only a generic file. Its
                    // registered executable resource is an authoritative fallback.
                    try
                    {
                        byte[]? executablePixels = await Task.Run(() => ExtractIconPixels(linkTarget.ExecutablePath, 0, size));
                        if (executablePixels is not null) return await FromPixelsAsync(executablePixels, (uint)size, (uint)size);
                    }
                    catch
                    {
                        // A failed optional resource must not skip the original
                        // namespace PIDL and target image providers below.
                    }
                }
                if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    // AppsFolder, advertised MSI and launcher links can have no
                    // path/icon string at all. Their target PIDL still has an icon.
                    var targetIcon = await LoadNativeShellAsync(asset, size, true);
                    if (targetIcon is not null) return targetIcon;
                }
                if (!string.IsNullOrWhiteSpace(target))
                {
                    var targetIcon = File.Exists(target) || Directory.Exists(target)
                        ? await LoadAssetAsync(target, size, tried)
                        : await LoadNativeShellAsync(target, size, tryPackagedLogo: false);
                    if (targetIcon is not null) return targetIcon;
                }
                if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    // Steam and Battle.net Internet shortcuts may contain only a
                    // protocol URL. Try registered local icon resources, never URL
                    // downloads or protocol activation. Explicit game icons above
                    // always take priority over a registered launcher's icon.
                    string address = ShortcutIconMetadata.ReadInternetShortcutTarget(asset);
                    foreach (string candidate in ShortcutIconMetadata.ReadProtocolIconCandidates(address))
                    {
                        var protocolIcon = await LoadAssetAsync(candidate, size, tried);
                        if (protocolIcon is not null) return protocolIcon;
                    }
                }
                // A generic .lnk/.url shell image must not mask a readable target.
                var shellIcon = await LoadNativeShellAsync(asset, size);
                if (shellIcon is not null) return shellIcon;
            }
            if (IsRaster(extension))
            {
                // Failed decoding is a failed custom image, not a successful generic
                // shell file icon. The editor relies on null to reject bad assets.
                return await TryLoadRasterAsync(asset, size);
            }
            if (indexed || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? pixels = await Task.Run(() => ExtractIconPixels(asset, index, size));
                if (pixels is not null) return await FromPixelsAsync(pixels, (uint)size, (uint)size);
                // A specific resource index must never silently become a different icon.
                if (indexed || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)) return null;
            }
            return await LoadNativeShellAsync(asset, size) ?? await LoadShellThumbnailAsync(asset, size);
        }
        catch
        {
            // Missing codecs and unavailable assets use the caller's standard glyph.
            return null;
        }
    }

    private static async Task<ImageSource?> LoadNativeShellAsync(string path, int size,
        bool linkTarget = false, bool tryPackagedLogo = true)
    {
        if (!linkTarget && tryPackagedLogo)
        {
            var appLogo = await TryLoadPackagedAppLogoAsync(path, size);
            if (appLogo is not null) return appLogo;
        }
        var pixels = await Task.Run(() => NativeShellIcon.Extract(path, size, linkTarget));
        return pixels is null ? null : await FromPixelsAsync(pixels.Data, (uint)pixels.Width, (uint)pixels.Height);
    }

    private static bool TryGetPackagedAppId(string path, out string appId)
    {
        const string appsFolder = "shell:AppsFolder\\";
        const string appsNamespace = "::{4234d49b-0245-4df3-b780-3893943456e1}\\";
        appId = path.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase) ? path[appsFolder.Length..]
            : path.StartsWith(appsNamespace, StringComparison.OrdinalIgnoreCase) ? path[appsNamespace.Length..]
            : path;
        int separator = appId.IndexOf('!');
        if (separator <= 0 || separator == appId.Length - 1 || appId.LastIndexOf('!') != separator)
        {
            appId = string.Empty;
            return false;
        }
        foreach (char character in appId)
        {
            // Accept only an exact app identity, never a path, URL, control
            // character or shell command appended to an AppsFolder identity.
            if (char.IsControl(character) || char.IsWhiteSpace(character)
                || character is '\\' or '/' or ':' or '"' or '<' or '>' or '|' or '?' or '*')
            {
                appId = string.Empty;
                return false;
            }
        }
        return true;
    }

    private static async Task<ImageSource?> TryLoadPackagedAppLogoAsync(string path, int size)
    {
        if (!TryGetPackagedAppId(path, out string appId)) return null;
        try
        {
            var app = AppInfo.GetFromAppUserModelId(appId);
            var logo = app.DisplayInfo.GetLogo(new Windows.Foundation.Size(size, size));
            using var stream = await logo.OpenReadAsync();
            // GetLogo can select a large Start tile with substantial transparent
            // padding rather than the compact shell icon. Scan a bounded image
            // only for registered app logos; user-selected images keep their crop.
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;
            double scale = Math.Min(1d, 512d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
            byte[] pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
            bitmap.CopyToBuffer(pixels.AsBuffer());
            var tile = FitPackagedAppLogo(pixels, bitmap.PixelWidth, bitmap.PixelHeight, size);
            return tile is null ? null : await FromPixelsAsync(tile.Value.Pixels,
                (uint)tile.Value.Width, (uint)tile.Value.Height);
        }
        catch
        {
            return null;
        }
    }

    private static (byte[] Pixels, int Width, int Height)? FitPackagedAppLogo(
        byte[] pixels, int width, int height, int size)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] == 0) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        }
        if (right < left || bottom < top) return null;
        int cropWidth = right - left + 1, cropHeight = bottom - top + 1;
        // Preserve normal icon spacing. Only remove a clearly padded tile's
        // completely transparent outer area, leaving all nonzero-alpha pixels.
        if (cropWidth >= width * .8 && cropHeight >= height * .8)
        {
            left = top = 0;
            cropWidth = width; cropHeight = height;
        }
        double scale = (double)size / Math.Max(cropWidth, cropHeight);
        int outputWidth = Math.Clamp((int)Math.Round(cropWidth * scale), 1, size);
        int outputHeight = Math.Clamp((int)Math.Round(cropHeight * scale), 1, size);
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        // Interpolate premultiplied BGRA so transparent edges retain their color.
        for (int y = 0; y < outputHeight; y++)
        {
            double sourceY = Math.Clamp((y + .5) * cropHeight / outputHeight - .5, 0, cropHeight - 1);
            int y0 = (int)sourceY, y1 = Math.Min(y0 + 1, cropHeight - 1);
            double fractionY = sourceY - y0;
            for (int x = 0; x < outputWidth; x++)
            {
                double sourceX = Math.Clamp((x + .5) * cropWidth / outputWidth - .5, 0, cropWidth - 1);
                int x0 = (int)sourceX, x1 = Math.Min(x0 + 1, cropWidth - 1);
                double fractionX = sourceX - x0;
                int row0 = ((top + y0) * width + left) * 4;
                int row1 = ((top + y1) * width + left) * 4;
                int destination = (y * outputWidth + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    double upper = pixels[row0 + x0 * 4 + channel]
                        + (pixels[row0 + x1 * 4 + channel] - pixels[row0 + x0 * 4 + channel]) * fractionX;
                    double lower = pixels[row1 + x0 * 4 + channel]
                        + (pixels[row1 + x1 * 4 + channel] - pixels[row1 + x0 * 4 + channel]) * fractionX;
                    output[destination + channel] = (byte)Math.Clamp((int)Math.Round(upper + (lower - upper) * fractionY), 0, 255);
                }
            }
        }
        return (output, outputWidth, outputHeight);
    }

    private static bool IsRaster(string extension) => extension.ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";

    private static async Task<ImageSource?> TryLoadRasterAsync(string path, int size)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            // Read dimensions without materializing a full decoded image. XAML
            // can decode the original file straight to its render surface; a
            // WriteableBitmap would retain an additional CPU pixel buffer.
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;
            double scale = Math.Min(1d, (double)size / Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
            int width = Math.Max(1, (int)Math.Round(decoder.OrientedPixelWidth * scale));
            int height = Math.Max(1, (int)Math.Round(decoder.OrientedPixelHeight * scale));
            try
            {
                var direct = new BitmapImage { DecodePixelType = DecodePixelType.Physical,
                    DecodePixelWidth = width, DecodePixelHeight = height };
                stream.Seek(0);
                await direct.SetSourceAsync(stream);
                return direct;
            }
            catch
            {
                // Keep installed WIC codecs available if XAML cannot read one.
            }
            scale = Math.Min(1d, (double)size / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            { ScaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant };
            // SoftwareBitmap reports the final dimensions after EXIF rotation.
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
            // A repeated scrolling list shares each decoded asset across several
            // ImageBrush instances. Use the same reusable bitmap type as shell
            // icons so every copy keeps its pixels when the layout is rebuilt.
            var image = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
            bitmap.CopyToBuffer(image.PixelBuffer);
            image.Invalidate();
            return image;
        }
        catch { return null; }
    }

    private static Task<ImageSource> FromPixelsAsync(byte[] pixels, uint width, uint height)
    {
        var image = new WriteableBitmap(checked((int)width), checked((int)height));
        using (var buffer = image.PixelBuffer.AsStream()) buffer.Write(pixels, 0, pixels.Length);
        image.Invalidate();
        return Task.FromResult<ImageSource>(image);
    }

    private static async Task<ImageSource?> LoadShellThumbnailAsync(string path, int size)
    {
        StorageItemThumbnail? thumbnail;
        if (Directory.Exists(path))
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(path);
            thumbnail = await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)size,
                ThumbnailOptions.ResizeThumbnail);
        }
        else
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)size,
                ThumbnailOptions.ResizeThumbnail);
        }
        if (thumbnail is null) return null;
        using (thumbnail)
        {
            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            return image;
        }
    }

    private static byte[]? ExtractIconPixels(string path, int index, int size)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            // Low word is the requested large size. A negative index selects the
            // resource ID; a non-negative index selects its ordinal in the file.
            if (SHDefExtractIcon(path, index, 0, out icon, IntPtr.Zero, (uint)size) != 0
                || icon == IntPtr.Zero) return null;
            var black = DrawIconPixels(icon, size, false);
            var white = DrawIconPixels(icon, size, true);
            if (black is null || white is null) return null;
            for (int pixel = 0; pixel < black.Length; pixel += 4)
            {
                // Recover alpha for modern ARGB icons and older monochrome masks.
                // The black render already contains premultiplied color channels.
                int transparency = Math.Max(Math.Abs(white[pixel] - black[pixel]),
                    Math.Max(Math.Abs(white[pixel + 1] - black[pixel + 1]),
                        Math.Abs(white[pixel + 2] - black[pixel + 2])));
                black[pixel + 3] = (byte)(255 - transparency);
            }
            return black;
        }
        finally { if (icon != IntPtr.Zero) DestroyIcon(icon); }
    }

    private static byte[]? DrawIconPixels(IntPtr icon, int size, bool whiteBackground)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            var info = new BitmapInfo { Header = new BitmapInfoHeader
            { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 } };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = SelectObject(dc, bitmap);
            var pixels = new byte[checked(size * size * 4)];
            if (whiteBackground) Array.Fill(pixels, (byte)255);
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            if (!DrawIconEx(dc, 0, 0, icon, size, size, 0, IntPtr.Zero, 3)) return null;
            GdiFlush();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount;
        public uint Compression; public uint SizeImage; public int XPelsPerMeter; public int YPelsPerMeter;
        public uint ClrUsed; public uint ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHDefExtractIconW")]
    private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, IntPtr small, uint size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
        uint frame, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info,
        uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
}
