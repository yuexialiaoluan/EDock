using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EDock.Models;

namespace EDock.Services;

/// <summary>Keeps shell shortcuts independent of the file originally dragged into the dock.</summary>
public static class ManagedShortcutStorage
{
    public static DesktopShortcutTransfer.PendingTransfer? Prepare(ShortcutItem item, string configurationDirectory) =>
        Prepare(item, configurationDirectory, DesktopRoots);

    internal static DesktopShortcutTransfer.PendingTransfer? Prepare(ShortcutItem item, string configurationDirectory,
        IEnumerable<string> desktopRoots) =>
        PrepareCore(item, configurationDirectory, desktopRoots, allowUnavailable: false, recoverDeleted: null);

    private static DesktopShortcutTransfer.PendingTransfer? PrepareCore(ShortcutItem item, string configurationDirectory,
        IEnumerable<string> desktopRoots, bool allowUnavailable, Func<string, string?>? recoverDeleted)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsShortcutPath(item.TargetPath)) return null;
        string? source = null;
        string? provenance = null;
        if (!DesktopShortcutTransfer.TryGetRegularShortcut(item.TargetPath, out _))
        {
            // Only explicitly recorded source files can recover a missing private copy.
            // An executable used as an icon cannot recover link arguments, PIDLs or app identities.
            source = new[] { item.ImportedShortcutPath, item.ImportedDesktopShortcutPath }
                .FirstOrDefault(path => Path.GetExtension(path ?? string.Empty)
                    .Equals(Path.GetExtension(item.TargetPath), StringComparison.OrdinalIgnoreCase)
                    && DesktopShortcutTransfer.TryGetRegularShortcut(path, out _));
            if (source is null && allowUnavailable && recoverDeleted is not null)
            {
                foreach (string? original in new[] { item.TargetPath, item.ImportedShortcutPath, item.ImportedDesktopShortcutPath }
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(original) || !IsShortcutPath(original)) continue;
                    string? recovered = recoverDeleted(original);
                    if (recovered is null || !Path.GetExtension(recovered)
                        .Equals(Path.GetExtension(item.TargetPath), StringComparison.OrdinalIgnoreCase)
                        || !DesktopShortcutTransfer.TryGetRegularShortcut(recovered, out _)) continue;
                    source = recovered;
                    provenance = original;
                    break;
                }
            }
            if (source is null)
            {
                if (allowUnavailable) return null;
                throw new IOException("无法为快捷方式保存独立副本，请确认原文件存在、可读取且未通过文件夹链接访问。");
            }
        }
        var pending = DesktopShortcutTransfer.PrepareCopy(item, configurationDirectory, desktopRoots, source, provenance);
        if (pending is null && !HasIndependentCopy(item, configurationDirectory))
        {
            if (allowUnavailable) return null;
            throw new IOException("快捷方式在导入期间发生变化，未添加到边栏，请重试。");
        }
        return pending;
    }

    public static bool RequiresIndependentCopy(ShortcutItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsShortcutPath(item.TargetPath);
    }

    public static bool HasIndependentCopy(ShortcutItem item, string configurationDirectory)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!DesktopShortcutTransfer.TryGetRegularShortcut(item.TargetPath, out string path)
            || string.IsNullOrWhiteSpace(configurationDirectory) || !Path.IsPathFullyQualified(configurationDirectory)) return false;
        try
        {
            return PathsEqual(Path.GetDirectoryName(path), Path.Combine(Path.GetFullPath(configurationDirectory), "Shortcuts"));
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        { return false; }
    }

    public static bool WasImportedFrom(ShortcutItem item, string path) =>
        PathsEqual(item.TargetPath, path) || PathsEqual(item.ImportedShortcutPath, path)
            || PathsEqual(item.ImportedDesktopShortcutPath, path);

    public static PendingMigration PrepareMigration(DockSettings settings, string configurationDirectory)
    {
        try
        {
            return PrepareMigration(settings, configurationDirectory, DesktopRoots, DeletedShortcutRecovery.TryFindLatest);
        }
        finally { DeletedShortcutRecovery.ClearCache(); }
    }

    internal static PendingMigration PrepareMigration(DockSettings settings, string configurationDirectory,
        IEnumerable<string> desktopRoots, Func<string, string?>? recoverDeleted = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var pending = new PendingMigration();
        var roots = desktopRoots.ToArray();
        var visited = new HashSet<ShortcutItem>(ReferenceEqualityComparer.Instance);
        var items = settings.LeftLists.Concat(settings.RightLists).SelectMany(list => list.Shortcuts)
            .Concat(settings.LeftShortcuts).Concat(settings.RightShortcuts);
        try
        {
            foreach (var item in items)
            {
                if (!visited.Add(item) || !IsShortcutPath(item.TargetPath)) continue;
                try
                {
                    var transfer = PrepareCore(item, configurationDirectory, roots, allowUnavailable: true, recoverDeleted);
                    if (transfer is not null) pending.Add(transfer);
                    else if (!DesktopShortcutTransfer.TryGetRegularShortcut(item.TargetPath, out _))
                        pending.UnavailableCount++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException)
                { pending.AddError($"{item.Name}：{error.Message}"); }
            }
            return pending;
        }
        catch
        {
            pending.Dispose();
            throw;
        }
    }

    private static IEnumerable<string> DesktopRoots => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
    };

    private static bool IsShortcutPath(string? path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return false;
        if (Directory.Exists(path)) return false;
        string extension = Path.GetExtension(path ?? string.Empty);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        { return false; }
    }

    public sealed class PendingMigration : IDisposable
    {
        private readonly List<DesktopShortcutTransfer.PendingTransfer> _transfers = new();
        private readonly List<string> _errors = new();
        public int UpdatedCount => _transfers.Count;
        public int UnavailableCount { get; internal set; }
        public IReadOnlyList<string> Errors => _errors;
        internal void Add(DesktopShortcutTransfer.PendingTransfer transfer) => _transfers.Add(transfer);
        internal void AddError(string error) => _errors.Add(error);

        /// <summary>Call after saving every migrated target succeeds.</summary>
        public void Commit()
        {
            foreach (var transfer in _transfers) transfer.CommitKeep();
        }

        public void Dispose()
        {
            foreach (var transfer in _transfers) transfer.Dispose();
        }
    }
}
