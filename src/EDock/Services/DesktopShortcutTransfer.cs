using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EDock.Models;
using Microsoft.Win32.SafeHandles;

namespace EDock.Services;

/// <summary>Stages a desktop shortcut before the caller saves its new managed target.</summary>
public static class DesktopShortcutTransfer
{
    public static PendingTransfer? Prepare(ShortcutItem item, string configurationDirectory) =>
        Prepare(item, configurationDirectory, new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }, Recycle);

    // Only tests substitute desktop roots and a recycle operation. Production callers
    // cannot widen the allowed locations or turn this into a generic file deletion API.
    internal static PendingTransfer? Prepare(ShortcutItem item, string configurationDirectory,
        IEnumerable<string> desktopRoots, Action<string> recycle)
        => PrepareCore(item, configurationDirectory, desktopRoots, recycle, desktopOnly: true, sourceOverride: null, provenanceOverride: null);

    internal static PendingTransfer? PrepareCopy(ShortcutItem item, string configurationDirectory,
        IEnumerable<string> desktopRoots, string? sourceOverride = null, string? provenanceOverride = null)
        => PrepareCore(item, configurationDirectory, desktopRoots, Recycle, desktopOnly: false, sourceOverride, provenanceOverride);

    private static PendingTransfer? PrepareCore(ShortcutItem item, string configurationDirectory,
        IEnumerable<string> desktopRoots, Action<string> recycle, bool desktopOnly, string? sourceOverride, string? provenanceOverride)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(desktopRoots);
        ArgumentNullException.ThrowIfNull(recycle);
        var roots = desktopRoots.Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string source;
        if (desktopOnly)
        {
            if (!TryGetDesktopShortcut(item.TargetPath, roots, out source)) return null;
        }
        else if (!TryGetRegularShortcut(sourceOverride ?? item.TargetPath, out source)) return null;
        if (string.IsNullOrWhiteSpace(configurationDirectory) || !Path.IsPathFullyQualified(configurationDirectory))
            throw new ArgumentException("配置目录必须是完整路径。", nameof(configurationDirectory));
        string directory = Path.Combine(Path.GetFullPath(configurationDirectory), "Shortcuts");
        if (!desktopOnly && sourceOverride is null && PathsEqual(Path.GetDirectoryName(source)!, directory)) return null;
        if (roots.Any(root => PathsEqual(root, directory)
            || directory.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)))
            throw new IOException("托管快捷方式必须保存在桌面之外的配置目录。");
        if (HasReparsePoint(directory)) throw new IOException("快捷方式存储目录不能经过链接或重解析点。");
        Directory.CreateDirectory(directory);
        if (HasReparsePoint(directory)) throw new IOException("快捷方式存储目录不能经过链接或重解析点。");
        string managed = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        string originalTarget = item.TargetPath;
        string originalIcon = item.IconPath;
        string originalCustomIcon = item.CustomIconPath;
        string? originalImportedPath = item.ImportedDesktopShortcutPath;
        string? originalGeneralImportedPath = item.ImportedShortcutPath;
        string importedPath = provenanceOverride ?? source;
        bool desktopSource = TryGetDesktopShortcut(source, roots, out _)
            || (provenanceOverride is not null && Path.IsPathFullyQualified(provenanceOverride)
                && roots.Any(root => PathsEqual(root, Path.GetDirectoryName(provenanceOverride) ?? string.Empty)));
        string managedIcon = RedirectReference(originalIcon, originalTarget, source, managed);
        string managedCustomIcon = RedirectReference(originalCustomIcon, originalTarget, source, managed);
        FileStream? sourceLock = null, managedLock = null;
        bool created = false;
        try
        {
            // Keep the desktop entry unchanged throughout preparation and saving.
            // A successful commit releases this lock just before asking the shell to recycle it.
            sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!(desktopOnly ? TryGetDesktopShortcut(source, roots, out _) : TryGetRegularShortcut(source, out _))) return null;
            var sourceFingerprint = Fingerprint.Read(sourceLock);
            using (var output = new FileStream(managed, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                created = true;
                sourceLock.Position = 0;
                sourceLock.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            if (HasReparsePoint(managed)) throw new IOException("托管快捷方式不能是链接或重解析点。");
            managedLock = new FileStream(managed, FileMode.Open, FileAccess.Read, FileShare.Read);
            var managedFingerprint = Fingerprint.Read(managedLock);
            if (!sourceFingerprint.ContentEquals(managedFingerprint)) throw new IOException("快捷方式副本验证失败，桌面原件已保留。");
            item.TargetPath = managed;
            item.IconPath = managedIcon;
            item.CustomIconPath = managedCustomIcon;
            item.ImportedShortcutPath = importedPath;
            if (desktopSource) item.ImportedDesktopShortcutPath = importedPath;
            var pending = new PendingTransfer(item, originalTarget, originalIcon, managedIcon,
                originalCustomIcon, managedCustomIcon, originalImportedPath, originalGeneralImportedPath,
                importedPath, source, managed, roots,
                recycle, sourceFingerprint, managedFingerprint, sourceLock, managedLock);
            sourceLock = managedLock = null;
            return pending;
        }
        finally
        {
            sourceLock?.Dispose();
            managedLock?.Dispose();
            if (created && !string.Equals(item.TargetPath, managed, StringComparison.OrdinalIgnoreCase))
                TryDeleteManagedCopy(managed);
        }
    }

    private static bool TryGetDesktopShortcut(string? target, string[] roots, out string source)
    {
        if (!TryGetRegularShortcut(target, out source)) return false;
        string? parent = Path.GetDirectoryName(source);
        return parent is not null && roots.Any(root => PathsEqual(root, parent));
    }

    internal static bool TryGetRegularShortcut(string? target, out string source)
    {
        source = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(target) || target.Any(char.IsControl)
                || target.StartsWith(@"\\?\", StringComparison.Ordinal) || target.StartsWith(@"\\.\", StringComparison.Ordinal)
                || !Path.IsPathFullyQualified(target)) return false;
            string path = Path.GetFullPath(target);
            if (Path.GetRelativePath(Path.GetPathRoot(path)!, path).Contains(':')) return false;
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return false;
            if (HasReparsePoint(path)) return false;
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
            source = path;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException) { return false; }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static string RedirectReference(string reference, string original, string source, string managed)
    {
        if (string.IsNullOrWhiteSpace(reference)) return reference;
        string path = reference.Trim().Trim('"');
        string suffix = string.Empty;
        int comma = path.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(path[(comma + 1)..].Trim(), out _))
        {
            suffix = path[comma..];
            path = path[..comma].Trim().Trim('"');
        }
        return string.Equals(path, original, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, source, StringComparison.OrdinalIgnoreCase) ? managed + suffix : reference;
    }

    private static bool HasReparsePoint(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return false;
    }

    private static void TryDeleteManagedCopy(string path)
    {
        try
        {
            if (!HasReparsePoint(path) && File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public sealed class PendingTransfer : IDisposable
    {
        private readonly string _originalTarget;
        private readonly string _originalIcon;
        private readonly string _managedIcon, _originalCustomIcon, _managedCustomIcon;
        private readonly string? _originalImportedPath;
        private readonly string? _originalGeneralImportedPath;
        private readonly string _assignedImportedPath;
        private readonly string[] _roots;
        private readonly Action<string> _recycle;
        private readonly Fingerprint _sourceFingerprint, _managedFingerprint;
        private FileStream? _sourceLock, _managedLock;
        private bool _finished;
        public ShortcutItem Item { get; }
        public string OriginalPath { get; }
        public string ManagedPath { get; }
        public string? LastError { get; private set; }
        // True means the saved managed target is retained, including when recycling fails.
        public bool IsCommitted { get; private set; }

        internal PendingTransfer(ShortcutItem item, string originalTarget, string originalIcon, string managedIcon,
            string originalCustomIcon, string managedCustomIcon, string? originalImportedPath,
            string? originalGeneralImportedPath, string assignedImportedPath, string source, string managed,
            string[] roots, Action<string> recycle, Fingerprint sourceFingerprint,
            Fingerprint managedFingerprint, FileStream sourceLock, FileStream managedLock)
        {
            Item = item; _originalTarget = originalTarget; OriginalPath = source; ManagedPath = managed;
            _originalIcon = originalIcon; _managedIcon = managedIcon;
            _originalCustomIcon = originalCustomIcon; _managedCustomIcon = managedCustomIcon;
            _originalImportedPath = originalImportedPath;
            _originalGeneralImportedPath = originalGeneralImportedPath;
            _assignedImportedPath = assignedImportedPath;
            _roots = roots; _recycle = recycle; _sourceFingerprint = sourceFingerprint;
            _managedFingerprint = managedFingerprint; _sourceLock = sourceLock; _managedLock = managedLock;
        }

        /// <summary>Retains the saved private copy and leaves the original untouched.</summary>
        public void CommitKeep()
        {
            if (_finished) return;
            _finished = IsCommitted = true;
            ReleaseLocks();
        }

        /// <summary>Call only after saving the new target succeeds. Failure retains both copies.</summary>
        public bool CommitDelete()
        {
            if (_finished) return false;
            _finished = IsCommitted = true;
            try
            {
                if (!PathsEqual(Item.TargetPath, ManagedPath) || HasReparsePoint(ManagedPath)
                    || !TryGetDesktopShortcut(OriginalPath, _roots, out _)
                    || !_sourceFingerprint.Equals(Fingerprint.Read(_sourceLock!))
                    || !_managedFingerprint.Equals(Fingerprint.Read(_managedLock!)))
                    throw new IOException("快捷方式在保存期间发生变化，桌面原件已保留。");
                // Validate path identity again: a replaced entry with identical bytes
                // must not be treated as the original desktop shortcut.
                using (var current = new FileStream(OriginalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    if (!_sourceFingerprint.Equals(Fingerprint.Read(current)))
                        throw new IOException("桌面快捷方式已被替换，原件已保留。");
                ReleaseLocks();
                if (!TryGetDesktopShortcut(OriginalPath, _roots, out _))
                    throw new IOException("桌面快捷方式已变化，原件已保留。");
                _recycle(OriginalPath);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"已添加到边栏，但未能回收桌面快捷方式：{ex.Message}";
                return false;
            }
            finally { ReleaseLocks(); }
        }

        public void Rollback()
        {
            if (_finished) return;
            _finished = true;
            ReleaseLocks();
            if (PathsEqual(Item.TargetPath, ManagedPath)) Item.TargetPath = _originalTarget;
            if (string.Equals(Item.IconPath, _managedIcon, StringComparison.OrdinalIgnoreCase))
                Item.IconPath = _originalIcon;
            if (string.Equals(Item.CustomIconPath, _managedCustomIcon, StringComparison.OrdinalIgnoreCase))
                Item.CustomIconPath = _originalCustomIcon;
            if (string.Equals(Item.ImportedDesktopShortcutPath, _assignedImportedPath, StringComparison.OrdinalIgnoreCase))
                Item.ImportedDesktopShortcutPath = _originalImportedPath;
            if (string.Equals(Item.ImportedShortcutPath, _assignedImportedPath, StringComparison.OrdinalIgnoreCase))
                Item.ImportedShortcutPath = _originalGeneralImportedPath;
            TryDeleteManagedCopy(ManagedPath);
        }

        public void Dispose() => Rollback();

        private void ReleaseLocks()
        {
            _sourceLock?.Dispose(); _sourceLock = null;
            _managedLock?.Dispose(); _managedLock = null;
        }
    }

    internal readonly record struct Fingerprint(long Length, string Hash, uint Volume, ulong FileId)
    {
        internal bool ContentEquals(Fingerprint other) => Length == other.Length && Hash == other.Hash;
        internal static Fingerprint Read(FileStream file)
        {
            file.Position = 0;
            string hash = Convert.ToHexString(SHA256.HashData(file));
            uint volume = 0; ulong fileId = 0;
            if (OperatingSystem.IsWindows())
            {
                if (!GetFileInformationByHandle(file.SafeFileHandle, out var information))
                    throw new IOException("无法验证快捷方式文件身份。", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
                volume = information.VolumeSerialNumber;
                fileId = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            }
            return new Fingerprint(file.Length, hash, volume, fileId);
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
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    private static void Recycle(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("回收站仅支持 Windows。");
        IFileOperation? operation = null;
        IShellItem? item = null;
        try
        {
            operation = (IFileOperation)new FileOperation();
            // RECYCLEONDELETE forbids the ordinary permanent-delete fallback.
            const uint flags = 0x4 | 0x400 | 0x80000 | 0x100000 | 0x20000000;
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(flags));
            Guid shellItemId = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref shellItemId, out item));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, IntPtr.Zero));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out bool aborted));
            if (aborted || File.Exists(path)) throw new IOException("回收操作未完成。");
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindingContext,
        ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), ClassInterface(ClassInterfaceType.None)]
    private class FileOperation { }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IntPtr sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
