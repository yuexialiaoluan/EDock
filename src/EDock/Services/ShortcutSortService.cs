using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EDock.Models;

namespace EDock.Services;

/// <summary>Prepares read-only target metadata and returns a stable view of a shortcut list.</summary>
public static class ShortcutSortService
{
    /// <summary>Unknown keys sort last in either direction. Manual retains the supplied order.</summary>
    public static Task<List<ShortcutItem>> SortAsync(IEnumerable<ShortcutItem> items,
        ShortcutSortKey sortKey, bool descending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!Enum.IsDefined(sortKey)) throw new ArgumentOutOfRangeException(nameof(sortKey));
        cancellationToken.ThrowIfCancellationRequested();

        // Snapshot before background work: do not enumerate a UI-owned collection
        // after the caller has returned to its dispatcher.
        var entries = new List<SortEntry>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(item);
            entries.Add(new SortEntry(item, entries.Count, KnownText(item.Name),
                item.AddedAtUtc, item.TargetPath ?? string.Empty));
        }
        if (sortKey == ShortcutSortKey.Manual || entries.Count < 2)
            return Task.FromResult(entries.Select(entry => entry.Item).ToList());

        return Task.Run(() =>
        {
            if (sortKey is ShortcutSortKey.Modified or ShortcutSortKey.Size or ShortcutSortKey.Type)
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entry.Metadata = ReadMetadata(entry.TargetPath);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            entries.Sort((left, right) =>
            {
                int comparison = sortKey switch
                {
                    ShortcutSortKey.Name => CompareText(left.Name, right.Name, descending),
                    ShortcutSortKey.Added => CompareValue(left.AddedAtUtc, right.AddedAtUtc, descending),
                    ShortcutSortKey.Modified => CompareValue(left.Metadata.ModifiedAtUtc, right.Metadata.ModifiedAtUtc, descending),
                    ShortcutSortKey.Size => CompareValue(left.Metadata.SizeBytes, right.Metadata.SizeBytes, descending),
                    ShortcutSortKey.Type => CompareText(left.Metadata.Type, right.Metadata.Type, descending),
                    _ => 0
                };
                // List.Sort is not stable. Keep original order for all tied keys,
                // including ties when the requested key is descending.
                return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
            });
            cancellationToken.ThrowIfCancellationRequested();
            return entries.Select(entry => entry.Item).ToList();
        }, cancellationToken);
    }

    private static TargetMetadata ReadMetadata(string targetPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetPath) || targetPath.Any(char.IsControl)) return default;
            string path = Environment.ExpandEnvironmentVariables(targetPath.Trim().Trim('"'));
            if (Uri.TryCreate(path, UriKind.Absolute, out var address)
                && address.Scheme is "http" or "https")
                return new TargetMetadata(null, null, "网址");
            if (!Path.IsPathFullyQualified(path)) return default;

            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                // Read persisted path/PIDL metadata only. Never call Resolve,
                // repair a shortcut, or launch an application for sorting.
                string target = ShortcutIconMetadata.ReadLinkTarget(path);
                if (!string.IsNullOrWhiteSpace(target) && !target.Any(char.IsControl)
                    && Path.IsPathFullyQualified(target) && TryReadLocalMetadata(target, out var resolved))
                    return resolved;
            }
            if (TryReadLocalMetadata(path, out var metadata)) return metadata;

            // A recognizable extension remains known if a saved local path is
            // gone; its size and modification time remain unknown.
            return new TargetMetadata(null, null, KnownText(Path.GetExtension(path)));
        }
        catch
        {
            // Unavailable drives, denied access and malformed paths remain unknown.
            return default;
        }
    }

    private static bool TryReadLocalMetadata(string path, out TargetMetadata metadata)
    {
        metadata = default;
        try
        {
            var attributes = File.GetAttributes(path);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
            info.Refresh();
            if (!info.Exists) return false;
            DateTimeOffset? modified = null;
            long? size = null;
            try
            {
                DateTime value = info.LastWriteTimeUtc;
                // File APIs use the Windows epoch as their missing-value sentinel.
                if (value > DateTime.FromFileTimeUtc(0)) modified = new DateTimeOffset(value, TimeSpan.Zero);
            }
            catch { }
            if (!directory)
            {
                try { size = ((FileInfo)info).Length; }
                catch { }
            }
            // Directory sizes remain unknown: never recursively scan user folders.
            metadata = new TargetMetadata(modified, size, directory ? "文件夹" : KnownText(info.Extension));
            return true;
        }
        catch { return false; }
    }

    private static string? KnownText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int CompareText(string? left, string? right, bool descending)
    {
        if (left is null) return right is null ? 0 : 1;
        if (right is null) return -1;
        int comparison = StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
        return descending ? -Math.Sign(comparison) : Math.Sign(comparison);
    }

    private static int CompareValue<T>(T? left, T? right, bool descending) where T : struct, IComparable<T>
    {
        if (!left.HasValue) return right.HasValue ? 1 : 0;
        if (!right.HasValue) return -1;
        int comparison = left.Value.CompareTo(right.Value);
        return descending ? -Math.Sign(comparison) : Math.Sign(comparison);
    }

    private readonly record struct TargetMetadata(DateTimeOffset? ModifiedAtUtc, long? SizeBytes, string? Type);

    private sealed class SortEntry(ShortcutItem item, int index, string? name, DateTimeOffset? addedAtUtc, string targetPath)
    {
        internal ShortcutItem Item { get; } = item;
        internal int Index { get; } = index;
        internal string? Name { get; } = name;
        internal DateTimeOffset? AddedAtUtc { get; } = addedAtUtc;
        internal string TargetPath { get; } = targetPath;
        internal TargetMetadata Metadata { get; set; }
    }
}
