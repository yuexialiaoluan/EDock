using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using EDock.Models;

namespace EDock.Services;

/// <summary>Places a dock item on the desktop without moving or deleting its target.</summary>
public static class DesktopShortcutRestore
{
    public static PendingPlacement Prepare(ShortcutItem item)
    {
        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        return Prepare(item, new[] { userDesktop, commonDesktop }, CreateDesktopShortcut);
    }

    internal static PendingPlacement Prepare(ShortcutItem item, IEnumerable<string> desktopRoots,
        Action<ShortcutItem, string> createShortcut)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(desktopRoots);
        ArgumentNullException.ThrowIfNull(createShortcut);

        string[] roots = desktopRoots.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0) throw new IOException("找不到桌面文件夹。");
        foreach (string root in roots)
            if (!Directory.Exists(root) || HasReparsePoint(root))
                throw new IOException("无法安全访问桌面文件夹。");

        ShortcutDescriptor desired = Describe(item);
        foreach (string root in roots)
        {
            foreach (string path in Directory.EnumerateFiles(root)
                .Where(x => Path.GetExtension(x).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(x).Equals(".url", StringComparison.OrdinalIgnoreCase)))
            {
                if (!IsRegularFile(path)) continue;
                if (IsEquivalent(Describe(path), desired))
                    return new PendingPlacement(path, null);
            }
        }

        string destinationRoot = roots[0];
        bool webTarget = Uri.TryCreate(item.TargetPath, UriKind.Absolute, out var webUri)
            && webUri.Scheme is "http" or "https";
        string extension = webTarget || Path.GetExtension(item.TargetPath).Equals(".url", StringComparison.OrdinalIgnoreCase)
            ? ".url" : ".lnk";
        string stem = SanitizeName(item.Name);
        string destination = GetAvailablePath(destinationRoot, stem, extension);
        string temporary = Path.Combine(destinationRoot, $".EDock-{Guid.NewGuid():N}.tmp{extension}");
        try
        {
            createShortcut(item, temporary);
            if (!IsRegularFile(temporary)) throw new IOException("桌面快捷方式创建失败。");
            File.Move(temporary, destination);
            return new PendingPlacement(destination, Fingerprint.Read(destination));
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    internal static PendingPlacement Prepare(ShortcutItem item, IEnumerable<string> desktopRoots) =>
        Prepare(item, desktopRoots, CreateDesktopShortcut);

    private static string GetAvailablePath(string directory, string stem, string extension)
    {
        string path = Path.Combine(directory, stem + extension);
        for (int suffix = 2; File.Exists(path) || Directory.Exists(path); suffix++)
            path = Path.Combine(directory, $"{stem} ({suffix}){extension}");
        return path;
    }

    private static string SanitizeName(string? name)
    {
        string value = string.IsNullOrWhiteSpace(name) ? "EDock 快捷方式" : name.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        value = value.Trim().TrimEnd('.');
        if (value.Length > 120) value = value[..120].TrimEnd('.');
        return string.IsNullOrWhiteSpace(value) ? "EDock 快捷方式" : value;
    }

    private static bool IsRegularFile(string path)
    {
        try
        {
            if (HasReparsePoint(path)) return false;
            return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        return false;
    }

    private static void TryDelete(string path)
    {
        try { if (IsRegularFile(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static ShortcutDescriptor Describe(ShortcutItem item)
    {
        string path = item.TargetPath;
        string extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            string target = ShortcutIconMetadata.ReadLinkTarget(path);
            if (string.IsNullOrWhiteSpace(target)) target = path;
            return new ShortcutDescriptor(target, MergeArguments(ShortcutIconMetadata.ReadLinkArguments(path), item.Arguments),
                string.IsNullOrWhiteSpace(item.WorkingDirectory)
                    ? ShortcutIconMetadata.ReadLinkWorkingDirectory(path) : item.WorkingDirectory);
        }
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            string target = ShortcutIconMetadata.ReadInternetShortcutTarget(path);
            return new ShortcutDescriptor(string.IsNullOrWhiteSpace(target) ? path : target, string.Empty, string.Empty);
        }
        return new ShortcutDescriptor(path, item.Arguments, item.WorkingDirectory);
    }

    private static ShortcutDescriptor Describe(string path)
    {
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            string target = ShortcutIconMetadata.ReadLinkTarget(path);
            return new ShortcutDescriptor(string.IsNullOrWhiteSpace(target) ? path : target,
                ShortcutIconMetadata.ReadLinkArguments(path), ShortcutIconMetadata.ReadLinkWorkingDirectory(path));
        }
        string url = ShortcutIconMetadata.ReadInternetShortcutTarget(path);
        return new ShortcutDescriptor(string.IsNullOrWhiteSpace(url) ? path : url, string.Empty, string.Empty);
    }

    private static bool IsEquivalent(ShortcutDescriptor left, ShortcutDescriptor right) =>
        TargetsEqual(left.Target, right.Target)
        && string.Equals(left.Arguments.Trim(), right.Arguments.Trim(), StringComparison.OrdinalIgnoreCase)
        && PathsOrTextEqual(left.WorkingDirectory, right.WorkingDirectory);

    private static bool TargetsEqual(string left, string right)
    {
        if (Uri.TryCreate(left, UriKind.Absolute, out var leftUri) && leftUri.IsAbsoluteUri
            && Uri.TryCreate(right, UriKind.Absolute, out var rightUri) && rightUri.IsAbsoluteUri)
            return string.Equals(leftUri.AbsoluteUri.TrimEnd('/'), rightUri.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        return PathsOrTextEqual(left, right);
    }

    private static bool PathsOrTextEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        try { return string.Equals(Path.GetFullPath(left.Trim().Trim('"')), Path.GetFullPath(right.Trim().Trim('"')), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase); }
    }

    internal static string MergeArguments(string? original, string? additional) =>
        string.Join(" ", new[] { original?.Trim(), additional?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static void CreateDesktopShortcut(ShortcutItem item, string destination)
    {
        bool webTarget = Uri.TryCreate(item.TargetPath, UriKind.Absolute, out var webUri)
            && webUri.Scheme is "http" or "https";
        string extension = Path.GetExtension(item.TargetPath);
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && !webTarget)
        {
            if (!IsRegularFile(item.TargetPath)) throw new FileNotFoundException("找不到 Internet 快捷方式文件。", item.TargetPath);
            string url = ShortcutIconMetadata.ReadInternetShortcutTarget(item.TargetPath);
            if (string.IsNullOrWhiteSpace(url)) throw new IOException("Internet 快捷方式中没有有效网址。");
            WriteInternetShortcut(destination, url, item);
            return;
        }
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && !webTarget
            && string.IsNullOrWhiteSpace(item.Arguments) && string.IsNullOrWhiteSpace(item.WorkingDirectory))
        {
            if (!IsRegularFile(item.TargetPath)) throw new FileNotFoundException("找不到快捷方式文件。", item.TargetPath);
            File.Copy(item.TargetPath, destination, overwrite: true);
            return;
        }

        string target = item.TargetPath;
        string arguments = item.Arguments ?? string.Empty;
        string workingDirectory = item.WorkingDirectory ?? string.Empty;
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            string resolved = ShortcutIconMetadata.ReadLinkTarget(target);
            if (!string.IsNullOrWhiteSpace(resolved)) target = resolved;
            arguments = MergeArguments(ShortcutIconMetadata.ReadLinkArguments(item.TargetPath), arguments);
            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = ShortcutIconMetadata.ReadLinkWorkingDirectory(item.TargetPath);
        }
        if (webTarget)
        {
            WriteInternetShortcut(destination, webUri!.AbsoluteUri, item);
            return;
        }
        if (string.IsNullOrWhiteSpace(target) || target.Any(char.IsControl))
            throw new IOException("快捷方式目标无效。");

        object? shellLink = null;
        try
        {
            shellLink = new ShellLink();
            var link = (IShellLinkW)shellLink;
            Marshal.ThrowExceptionForHR(link.SetPath(target));
            Marshal.ThrowExceptionForHR(link.SetArguments(arguments));
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                Marshal.ThrowExceptionForHR(link.SetWorkingDirectory(workingDirectory));
            string icon = !string.IsNullOrWhiteSpace(item.CustomIconPath) ? item.CustomIconPath : item.IconPath;
            if (TryParseIcon(icon, out string iconPath, out int iconIndex))
                Marshal.ThrowExceptionForHR(link.SetIconLocation(iconPath, iconIndex));
            ((IPersistFile)shellLink).Save(destination, false);
        }
        finally
        {
            if (shellLink is not null && Marshal.IsComObject(shellLink)) Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static void WriteInternetShortcut(string destination, string url, ShortcutItem item)
    {
        var lines = new List<string> { "[InternetShortcut]", $"URL={url}" };
        string icon = !string.IsNullOrWhiteSpace(item.CustomIconPath) ? item.CustomIconPath : item.IconPath;
        if (TryParseIcon(icon, out string iconPath, out int iconIndex))
        {
            lines.Add($"IconFile={iconPath}");
            lines.Add($"IconIndex={iconIndex.ToString(CultureInfo.InvariantCulture)}");
        }
        File.WriteAllText(destination, string.Join("\r\n", lines) + "\r\n", Encoding.Unicode);
    }

    private static bool TryParseIcon(string? value, out string path, out int index)
    {
        path = string.Empty; index = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        int comma = expanded.LastIndexOf(',');
        if (comma > 1 && int.TryParse(expanded[(comma + 1)..], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int parsed))
        { path = expanded[..comma].Trim('"'); index = parsed; }
        else path = expanded;
        return Path.IsPathFullyQualified(path) && !path.Any(char.IsControl);
    }

    private readonly record struct ShortcutDescriptor(string Target, string Arguments, string WorkingDirectory);

    public sealed class PendingPlacement : IDisposable
    {
        private readonly Fingerprint? _createdFingerprint;
        private bool _finished;
        public string ExistingOrCreatedPath { get; }
        public bool AlreadyPresent => _createdFingerprint is null;

        internal PendingPlacement(string path, Fingerprint? createdFingerprint)
        { ExistingOrCreatedPath = path; _createdFingerprint = createdFingerprint; }

        public void Commit() => _finished = true;

        public void Rollback()
        {
            if (_finished) return;
            _finished = true;
            if (_createdFingerprint is not { } fingerprint || !IsRegularFile(ExistingOrCreatedPath)) return;
            try
            {
                if (fingerprint.Equals(Fingerprint.Read(ExistingOrCreatedPath))) File.Delete(ExistingOrCreatedPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        public void Dispose() => Rollback();
    }

    internal readonly record struct Fingerprint(long Length, string Hash, uint Volume, ulong FileId)
    {
        internal static Fingerprint Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            uint volume = 0; ulong fileId = 0;
            if (OperatingSystem.IsWindows() && GetFileInformationByHandle(stream.SafeFileHandle, out var info))
            { volume = info.VolumeSerialNumber; fileId = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow; }
            return new Fingerprint(stream.Length, hash, volume, fileId);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,
        out ByHandleFileInformation information);

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
