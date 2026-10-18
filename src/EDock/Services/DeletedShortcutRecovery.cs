using System.Collections.Concurrent;
using System.Security.Principal;
using System.Text;

namespace EDock.Services;

/// <summary>Finds an exact original shortcut in the current user's recycle bin without restoring it.</summary>
internal static class DeletedShortcutRecovery
{
    private const int MaximumPathCharacters = 32768;
    private const long IndexLifetimeMilliseconds = 2000;
    private static readonly UnicodeEncoding StrictUnicode = new(false, false, true);
    private static readonly ConcurrentDictionary<string, CachedIndex> Indexes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IndexGate = new();

    private sealed record Candidate(string OriginalPath, string MetadataPath, string SourcePath, long DeletionTime);
    private sealed record CachedIndex(DateTime DirectoryWrittenUtc, long IndexedAt, Dictionary<string, List<Candidate>> Candidates);

    internal static string? TryFindLatest(string originalPath)
    {
        if (!OperatingSystem.IsWindows() || !TryGetMissingShortcutPath(originalPath, out var normalized)) return null;
        try
        {
            string? root = Path.GetPathRoot(normalized);
            // Windows recycles a local file on its original volume. Network and
            // device namespace paths have no corresponding local recycle bin.
            if (root is not { Length: 3 } || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\') return null;
            using var identity = WindowsIdentity.GetCurrent();
            string? sid = identity.User?.Value;
            return sid is null ? null : TryFindLatest(normalized, Path.Combine(root, "$Recycle.Bin", sid));
        }
        catch (Exception ex) when (IsUnavailable(ex)) { return null; }
    }

    internal static void ClearCache() => Indexes.Clear();

    // The explicit directory keeps regression tests away from the user's recycle
    // bin. Both paths still undergo the production file and metadata checks.
    internal static string? TryFindLatest(string originalPath, string recycleDirectory)
    {
        if (!TryGetMissingShortcutPath(originalPath, out var normalized)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(recycleDirectory)) return null;
            string directory = Path.GetFullPath(recycleDirectory);
            if (!IsSafeDirectory(directory)) return null;
            var index = GetIndex(directory);
            if (!index.Candidates.TryGetValue(normalized, out var candidates)) return null;
            foreach (var candidate in candidates)
            {
                // Recheck selected records: a recycle entry may have been purged,
                // replaced or restored since this short-lived index was built.
                var current = ReadCandidate(candidate.MetadataPath, DateTime.UtcNow);
                if (current is not null && current.DeletionTime == candidate.DeletionTime
                    && string.Equals(current.OriginalPath, normalized, StringComparison.OrdinalIgnoreCase)
                    && TryGetMissingShortcutPath(normalized, out _) && IsSafeDirectory(directory))
                    return current.SourcePath;
            }
        }
        catch (Exception ex) when (IsUnavailable(ex)) { }
        return null;
    }

    private static bool TryGetMissingShortcutPath(string path, out string normalized)
    {
        normalized = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)
                || !Path.IsPathFullyQualified(path) || !IsShortcut(path)) return false;
            normalized = Path.GetFullPath(path);
            try
            {
                // Unlike File.Exists, this does not treat access denial or an
                // existing dangling reparse point as a missing shortcut.
                File.GetAttributes(normalized);
                return false;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return true; }
        }
        catch (Exception ex) when (IsUnavailable(ex)) { return false; }
    }

    private static CachedIndex GetIndex(string directory)
    {
        lock (IndexGate)
        {
            DateTime written = Directory.GetLastWriteTimeUtc(directory);
            long now = Environment.TickCount64;
            if (Indexes.TryGetValue(directory, out var cached) && cached.DirectoryWrittenUtc == written
                && now - cached.IndexedAt < IndexLifetimeMilliseconds) return cached;
            var candidates = new Dictionary<string, List<Candidate>>(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            foreach (string metadata in Directory.EnumerateFiles(directory, "$I*", options))
            {
                var candidate = ReadCandidate(metadata, DateTime.UtcNow);
                if (candidate is null) continue;
                if (!candidates.TryGetValue(candidate.OriginalPath, out var versions))
                    candidates[candidate.OriginalPath] = versions = new List<Candidate>();
                versions.Add(candidate);
            }
            foreach (var versions in candidates.Values)
                versions.Sort((left, right) => right.DeletionTime.CompareTo(left.DeletionTime));
            return Indexes[directory] = new CachedIndex(written, now, candidates);
        }
    }

    private static Candidate? ReadCandidate(string metadataPath, DateTime nowUtc)
    {
        try
        {
            string name = Path.GetFileName(metadataPath);
            if (!name.StartsWith("$I", StringComparison.OrdinalIgnoreCase) || name.Length <= 2
                || !IsShortcut(name) || !IsOrdinaryFile(metadataPath)) return null;
            using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length < 28 || stream.Length > 28 + MaximumPathCharacters * 2) return null;
            using var reader = new BinaryReader(stream, StrictUnicode, leaveOpen: true);
            long version = reader.ReadInt64();
            long originalSize = reader.ReadInt64();
            long deletionTime = reader.ReadInt64();
            if (originalSize < 0 || deletionTime <= 0) return null;
            DateTime deletedUtc = DateTime.FromFileTimeUtc(deletionTime);
            if (deletedUtc < DateTime.UnixEpoch || deletedUtc > nowUtc) return null;
            string original;
            if (version == 1)
            {
                if (stream.Length != 544) return null;
                string padded = StrictUnicode.GetString(reader.ReadBytes(520));
                int end = padded.IndexOf('\0');
                if (end < 0 || padded.AsSpan(end).ContainsAnyExcept('\0')) return null;
                original = padded[..end];
            }
            else if (version == 2)
            {
                int characters = reader.ReadInt32();
                if (characters < 2 || characters > MaximumPathCharacters || stream.Length != 28L + characters * 2L) return null;
                string terminated = StrictUnicode.GetString(reader.ReadBytes(characters * 2));
                if (terminated[^1] != '\0' || terminated.AsSpan(0, terminated.Length - 1).Contains('\0')) return null;
                original = terminated[..^1];
            }
            else return null;
            if (string.IsNullOrWhiteSpace(original) || original.Any(char.IsControl)
                || !Path.IsPathFullyQualified(original) || !IsShortcut(original)
                || !string.Equals(Path.GetExtension(original), Path.GetExtension(name), StringComparison.OrdinalIgnoreCase)) return null;
            string source = Path.Combine(Path.GetDirectoryName(metadataPath)!, "$R" + name[2..]);
            if (!IsOrdinaryFile(source) || new FileInfo(source).Length != originalSize) return null;
            return new Candidate(Path.GetFullPath(original), metadataPath, source, deletionTime);
        }
        catch (Exception ex) when (IsUnavailable(ex)) { return null; }
    }

    private static bool IsSafeDirectory(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            var attributes = File.GetAttributes(current.FullName);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }

    private static bool IsOrdinaryFile(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;
    }

    private static bool IsShortcut(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnavailable(Exception exception) => exception is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or System.Security.SecurityException or DecoderFallbackException;
}
