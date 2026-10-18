using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using EDock.Models;
using Microsoft.Win32;

namespace EDock.Services;

public static class ShortcutService
{
    // Keep .lnk and .url intact: the Windows shell resolves their original metadata.
    // Callers stage a ManagedShortcutStorage copy before persisting the imported item.
    public static ShortcutItem ImportPath(string path)
    {
        var normalized = NormalizeTarget(path, out var webAddress);
        if (webAddress is not null)
        {
            return new ShortcutItem
            {
                Name = webAddress.IdnHost,
                TargetPath = normalized,
                AddedAtUtc = DateTimeOffset.UtcNow
            };
        }
        var isDirectory = Directory.Exists(normalized);
        var name = isDirectory
            ? new DirectoryInfo(normalized).Name
            : Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(name))
            name = normalized;
        string extension = Path.GetExtension(normalized);
        bool isLink = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        bool isInternetShortcut = extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
        string linkWorkingDirectory = isLink ? ShortcutIconMetadata.ReadLinkWorkingDirectory(normalized) : string.Empty;
        bool validLink = isLink && !string.IsNullOrWhiteSpace(ShortcutIconMetadata.ReadLinkTarget(normalized));
        return new ShortcutItem
        {
            Name = name,
            TargetPath = normalized,
            // A link's working directory belongs to the link metadata. Keeping
            // the desktop folder here makes drag-out comparison rebuild a link
            // with the wrong working directory and miss an equivalent desktop
            // shortcut that is still present.
            WorkingDirectory = isDirectory
                ? normalized
                : isLink
                    ? validLink ? linkWorkingDirectory : Path.GetDirectoryName(normalized) ?? string.Empty
                    : isInternetShortcut
                        ? string.Empty
                        : Path.GetDirectoryName(normalized) ?? string.Empty,
            IconPath = ShortcutIconMetadata.ResolveOriginal(normalized),
            AddedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public static void Launch(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var normalized = NormalizeTarget(shortcut.TargetPath, out _);
        var startInfo = new ProcessStartInfo
        {
            FileName = normalized,
            Arguments = shortcut.Arguments ?? string.Empty,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory))
            startInfo.WorkingDirectory = Environment.ExpandEnvironmentVariables(shortcut.WorkingDirectory);
        try
        {
            // A successful shell launch (for example opening a folder) may return null.
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            throw new InvalidOperationException($"无法打开“{shortcut.Name}”：{ex.Message}", ex);
        }
    }

    private static string NormalizeTarget(string path, out Uri? webAddress)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("请选择快捷方式、文件、文件夹或输入 http/https 网址。", nameof(path));
        if (path.Any(char.IsControl))
            throw new ArgumentException("目标路径或网址包含无效字符。", nameof(path));
        var normalized = Environment.ExpandEnvironmentVariables(path.Trim());
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
            normalized = normalized[1..^1];
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Any(char.IsControl))
            throw new ArgumentException("目标路径或网址包含无效字符。", nameof(path));

        webAddress = null;
        var colonIndex = normalized.IndexOf(':');
        var isDrivePath = colonIndex == 1 && char.IsLetter(normalized[0]);
        if (colonIndex >= 0 && !isDrivePath)
        {
            var scheme = normalized[..colonIndex];
            if (!scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("网址仅支持 http 或 https；文件和快捷方式请使用原始文件路径。", nameof(path));
            if (!normalized.StartsWith(scheme + "://", StringComparison.OrdinalIgnoreCase)
                || normalized.Any(char.IsWhiteSpace)
                || !Uri.TryCreate(normalized, UriKind.Absolute, out webAddress)
                || string.IsNullOrWhiteSpace(webAddress.Host)
                || !webAddress.IsWellFormedOriginalString())
                throw new ArgumentException("请输入完整有效的 http/https 网址。", nameof(path));
            return webAddress.AbsoluteUri;
        }

        normalized = Path.GetFullPath(normalized);
        if (!Directory.Exists(normalized) && !File.Exists(normalized))
            throw new FileNotFoundException("找不到要添加的文件或文件夹。", normalized);
        return normalized;
    }
}

// Kept in this file so the standalone core project also compiles the importer.
// Icon locations use the Windows convention: a local asset path followed by ,index.
internal static class ShortcutIconMetadata
{
    internal static string ReadInternetShortcutTarget(string path) =>
        ReadInternetShortcutValue(path, "URL");

    internal static IReadOnlyList<string> ReadProtocolIconCandidates(string target)
    {
        var candidates = new List<string>();
        if (!OperatingSystem.IsWindows() || !Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || uri.Scheme is "http" or "https" or "file") return candidates;
        try
        {
            using var protocol = Registry.ClassesRoot.OpenSubKey(uri.Scheme);
            if (protocol?.GetValue("URL Protocol") is null) return candidates;
            using var icon = protocol.OpenSubKey("DefaultIcon");
            if (icon?.GetValue(null) is string location && location.Length > 0) candidates.Add(location);
            // The registered launcher is a last resort, after the shortcut's own
            // game icon. Read only the executable token; never run the command.
            using var commandKey = protocol.OpenSubKey("shell\\open\\command");
            if (commandKey?.GetValue(null) is string command)
            {
                command = Environment.ExpandEnvironmentVariables(command.Trim());
                string executable = string.Empty;
                if (command.StartsWith('\"'))
                {
                    int end = command.IndexOf('\"', 1);
                    if (end > 1) executable = command[1..end];
                }
                else
                {
                    int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (end >= 0) executable = command[..(end + 4)];
                }
                if (File.Exists(executable)) candidates.Add(executable);
            }
        }
        catch { }
        return candidates;
    }

    // The returned PIDL belongs to the caller (FreeCoTaskMem), even after the COM
    // link object is released. This does not resolve, repair, or launch a link.
    internal static IntPtr ReadLinkTargetIdList(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)
            || !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        object? link = null;
        IntPtr idList = IntPtr.Zero;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            if (((IShellLinkW)link).GetIDList(out idList) >= 0) return idList;
        }
        catch { }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
        }
        if (idList != IntPtr.Zero) Marshal.FreeCoTaskMem(idList);
        return IntPtr.Zero;
    }

    // Reading a link target is for icon fallback only. Never replace TargetPath,
    // call Resolve, or execute an application while discovering its appearance.
    internal static string ReadLinkTarget(string path) => ReadLinkTargetCore(path, false).Target;

    // Icon discovery may additionally inspect a namespace item's registered
    // executable. Keep its original parsing identity for packaged-app logos and
    // the existing PIDL fallbacks; never replace the saved launch target.
    internal static (string Target, string ExecutablePath) ReadLinkIconTarget(string path) =>
        ReadLinkTargetCore(path, true);

    private static (string Target, string ExecutablePath) ReadLinkTargetCore(string path, bool readIconExecutable)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)
            || !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return (string.Empty, string.Empty);
        object? link = null;
        IntPtr idList = IntPtr.Zero, name = IntPtr.Zero;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            var shellLink = (IShellLinkW)link;
            var target = new StringBuilder(32768);
            if (shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0) >= 0
                && target.Length > 0 && File.Exists(target.ToString()))
            {
                string fileTarget = target.ToString();
                return (fileTarget, string.Empty);
            }
            if (shellLink.GetIDList(out idList) < 0 || idList == IntPtr.Zero) return (string.Empty, string.Empty);
            // Packaged applications have a namespace PIDL rather than a file path.
            if (SHGetNameFromIDList(idList, 0x80028000, out name) < 0 || name == IntPtr.Zero) return (string.Empty, string.Empty);
            string parsed = Marshal.PtrToStringUni(name) ?? string.Empty;
            if (parsed.Any(char.IsControl)) return (string.Empty, string.Empty);
            return (parsed, readIconExecutable ? ReadNamespaceExecutable(idList) : string.Empty);
        }
        catch { return (string.Empty, string.Empty); }
        finally
        {
            if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
            if (idList != IntPtr.Zero) Marshal.FreeCoTaskMem(idList);
            if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
        }
    }

    private static bool IsExistingExecutable(string path) =>
        !string.IsNullOrWhiteSpace(path) && !path.Any(char.IsControl)
        && Path.IsPathFullyQualified(path)
        && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    private static string ReadNamespaceExecutable(IntPtr idList)
    {
        object? item = null;
        IntPtr value = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItem2).GUID;
            if (SHCreateItemFromIDList(idList, ref iid, out item) < 0 || item is not IShellItem2 shellItem)
                return string.Empty;
            // System.Link.TargetParsingPath comes from the namespace item's
            // registered metadata, including unpackaged AppsFolder identities.
            var key = new PropertyKey { FormatId = new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), PropertyId = 2 };
            if (shellItem.GetString(ref key, out value) < 0 || value == IntPtr.Zero) return string.Empty;
            string executable = Marshal.PtrToStringUni(value) ?? string.Empty;
            return IsExistingExecutable(executable) ? executable : string.Empty;
        }
        catch { return string.Empty; }
        finally
        {
            if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value);
            if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }
    }

    internal static string ReadLinkArguments(string path) => ReadLinkString(path, static (link, value) => link.GetArguments(value, value.Capacity));

    internal static string ReadLinkWorkingDirectory(string path) => ReadLinkString(path, static (link, value) => link.GetWorkingDirectory(value, value.Capacity));

    private static string ReadLinkString(string path, Func<IShellLinkW, StringBuilder, int> reader)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)
            || !Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        object? linkObject = null;
        try
        {
            linkObject = new ShellLink();
            ((IPersistFile)linkObject).Load(path, 0);
            var value = new StringBuilder(32768);
            return reader((IShellLinkW)linkObject, value) >= 0 ? value.ToString() : string.Empty;
        }
        catch { return string.Empty; }
        finally
        {
            if (linkObject is not null && Marshal.IsComObject(linkObject)) Marshal.FinalReleaseComObject(linkObject);
        }
    }

    internal static string ResolveOriginal(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return path;
        try
        {
            string extension = Path.GetExtension(path);
            string location = string.Empty;
            int index = 0;
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                object? shellLink = null;
                try
                {
                    shellLink = new ShellLink();
                    // Read metadata only. Never call Resolve or execute the shortcut.
                    ((IPersistFile)shellLink).Load(path, 0);
                    var buffer = new StringBuilder(32768);
                    if (((IShellLinkW)shellLink).GetIconLocation(buffer, buffer.Capacity, out index) >= 0)
                        location = buffer.ToString();
                }
                finally
                {
                    if (shellLink is not null && Marshal.IsComObject(shellLink))
                        Marshal.FinalReleaseComObject(shellLink);
                }
            }
            else if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                ReadInternetShortcutIcon(path, out location, out index);
            }
            if (string.IsNullOrWhiteSpace(location) || location.Any(char.IsControl)) return path;
            location = Environment.ExpandEnvironmentVariables(location.Trim().Trim('\"'));
            if (!Path.IsPathFullyQualified(location))
                location = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, location);
            location = Path.GetFullPath(location);
            return File.Exists(location)
                ? location + "," + index.ToString(CultureInfo.InvariantCulture) : path;
        }
        catch
        {
            // Missing, malformed or inaccessible metadata falls back to the shell icon.
            return path;
        }
    }

    private static void ReadInternetShortcutIcon(string path, out string location, out int index)
    {
        location = ReadInternetShortcutValue(path, "IconFile");
        int.TryParse(ReadInternetShortcutValue(path, "IconIndex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static string ReadInternetShortcutValue(string path, string keyName)
    {
        try
        {
            // Browser/export tools commonly write UTF-8 without a BOM. The Windows
            // INI API treats that as ANSI and corrupts non-ASCII icon filenames.
            if (new FileInfo(path).Length > 1024 * 1024) return string.Empty;
            using var reader = new StreamReader(path, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true);
            bool inSection = false;
            while (reader.ReadLine() is { } raw)
            {
                string line = raw.Trim();
                if (line.StartsWith(';') || line.StartsWith('#') || line.Length == 0) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                int equals = line.IndexOf('=');
                if (equals < 0) continue;
                string key = line[..equals].Trim();
                string value = line[(equals + 1)..].Trim();
                if (key.Equals(keyName, StringComparison.OrdinalIgnoreCase)) return value;
            }
        }
        catch (DecoderFallbackException)
        {
            // Legacy ANSI .url files still follow the user's Windows code page.
            var buffer = new StringBuilder(32768);
            GetPrivateProfileString("InternetShortcut", keyName, string.Empty,
                buffer, (uint)buffer.Capacity, path);
            return buffer.ToString();
        }
        catch { }
        return string.Empty;
    }

    internal static bool TryParse(string value, out string path, out int index, out bool indexed)
    {
        path = string.Empty; index = 0; indexed = false;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) return false;
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            // A comma can be part of a real filename. Prefer the literal existing path.
            string literal = expanded.Trim('\"');
            if (File.Exists(literal) || Directory.Exists(literal))
            {
                path = Path.GetFullPath(literal);
                return true;
            }
            int comma = expanded.LastIndexOf(',');
            if (comma < 0 || !int.TryParse(expanded[(comma + 1)..].Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return false;
            path = Path.GetFullPath(expanded[..comma].Trim().Trim('\"'));
            indexed = true;
            return File.Exists(path);
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string defaultValue,
        StringBuilder returnedString, uint size, string fileName);

    [DllImport("shell32.dll")]
    private static extern int SHGetNameFromIDList(IntPtr idList, uint displayName, out IntPtr name);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateItemFromIDList(IntPtr idList, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? item);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    [ComImport, Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IntPtr other, uint hint, out int order);
        [PreserveSig] int GetPropertyStore(uint flags, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyStoreWithCreateObject(uint flags, IntPtr creator, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyStoreForKeys(IntPtr keys, uint count, uint flags, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyDescriptionList(ref PropertyKey key, ref Guid iid, out IntPtr list);
        [PreserveSig] int Update(IntPtr bindContext);
        [PreserveSig] int GetProperty(ref PropertyKey key, IntPtr value);
        [PreserveSig] int GetCLSID(ref PropertyKey key, out Guid value);
        [PreserveSig] int GetFileTime(ref PropertyKey key, out long value);
        [PreserveSig] int GetInt32(ref PropertyKey key, out int value);
        [PreserveSig] int GetString(ref PropertyKey key, out IntPtr value);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private sealed class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemIdList);
        [PreserveSig] int SetIDList(IntPtr itemIdList);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
