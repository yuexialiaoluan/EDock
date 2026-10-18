using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;

namespace EDock.Services;

/// <summary>Copies the legacy profile once; its original directory remains an untouched backup.</summary>
public static class ConfigurationDirectoryMigration
{
    public sealed record Result(string DirectoryPath, bool Migrated, string? Error);

    public static Result Resolve(string? isolatedProfileDirectory = null)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ResolveFromDirectories(Path.Combine(local, "EdgeDock"), Path.Combine(local, "EDock"), isolatedProfileDirectory);
    }

    // Explicit paths and the copy seam keep tests away from the user's actual profile.
    internal static Result ResolveFromDirectories(string legacyDirectory, string destinationDirectory,
        string? isolatedProfileDirectory = null, Action<string, string>? copyFile = null)
    {
        if (isolatedProfileDirectory is not null)
            return new(Path.GetFullPath(isolatedProfileDirectory), false, null);
        string legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        string settings = Path.Combine(destination, "settings.json");
        if (File.Exists(settings)) return new(destination, false, null);
        string sourceSettings = Path.Combine(legacy, "settings.json");
        if (!File.Exists(sourceSettings)) return new(destination, false, null);
        string? stage = null;
        try
        {
            if (IsWithin(legacy, destination) || IsWithin(destination, legacy))
                throw new IOException("旧配置目录和新配置目录不能相同或相互包含。");
            RequireOrdinaryPath(legacy);
            RequireOrdinaryPath(destination);
            // Prevent the old process from replacing settings while its snapshot is copied.
            using var sourceLock = new FileStream(sourceSettings, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var content = new MemoryStream();
            sourceLock.CopyTo(content);
            byte[] rewritten = RewriteSettings(content.ToArray(), legacy, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            stage = destination + ".migration-" + Guid.NewGuid().ToString("N");
            CopyTree(legacy, stage, copyFile ?? ((source, target) => File.Copy(source, target, overwrite: false)));
            string stagedSettings = Path.Combine(stage, "settings.json");
            var attributes = File.GetAttributes(stagedSettings);
            File.SetAttributes(stagedSettings, attributes & ~FileAttributes.ReadOnly);
            try
            {
                // Truncating the native copy preserves its EFS recipients and alternate data streams.
                using var output = new FileStream(stagedSettings, FileMode.Truncate, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough);
                output.Write(rewritten);
                output.Flush(flushToDisk: true);
            }
            finally { File.SetAttributes(stagedSettings, attributes); }
            if (!Directory.Exists(destination))
            {
                try
                {
                    Directory.Move(stage, destination);
                    stage = null;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    && error.HResult == unchecked((int)0x80070005)
                    && Directory.Exists(stage) && !Directory.Exists(destination)
                    && (File.GetAttributes(stage) & FileAttributes.Encrypted) != 0)
                {
                    // Some EFS systems reject directory renames while allowing encrypted
                    // file renames. Keep the same settings-last commit and rollback policy.
                    CommitNewEncryptedDirectory(stage, destination);
                }
            }
            else
            {
                CommitIntoExistingDirectory(stage, destination);
            }
            return new(destination, true, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // Continue using the complete legacy profile; never replace it with default settings.
            return new(legacy, false, $"配置目录迁移未完成，已保留并继续使用原配置：{error.Message}");
        }
        finally
        {
            if (stage is not null) TryDeleteStage(stage);
        }
    }

    private static void CopyTree(string source, string destination, Action<string, string> copyFile)
    {
        RequireOrdinaryPath(source);
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("配置目录中包含链接或重解析点，原配置已保留。");
            string target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyTree(entry, target, copyFile);
            else
            {
                copyFile(entry, target); // Native File.Copy preserves all named streams and EFS metadata.
                if ((attributes & FileAttributes.Encrypted) != 0
                    && (File.GetAttributes(target) & FileAttributes.Encrypted) == 0)
                    throw new IOException("配置副本未保留 EFS 加密，未提交迁移。");
                using var original = File.OpenRead(entry);
                using var copied = File.OpenRead(target);
                if (!SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(copied)))
                    throw new IOException("配置副本内容校验失败，未提交迁移。");
                if (OperatingSystem.IsWindows())
                    new FileInfo(target).SetAccessControl(new FileInfo(entry).GetAccessControl(AccessControlSections.Access));
            }
        }
        CopyDirectoryMetadata(source, destination);
    }

    private static void CopyDirectoryMetadata(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
            new DirectoryInfo(destination).SetAccessControl(new DirectoryInfo(source).GetAccessControl(AccessControlSections.Access));
        Directory.SetCreationTimeUtc(destination, Directory.GetCreationTimeUtc(source));
        Directory.SetLastWriteTimeUtc(destination, Directory.GetLastWriteTimeUtc(source));
        // New directories inherit EFS from their destination parent. Do not call
        // Encrypt on an already encrypted directory (unsupported on some systems).
        // Every encrypted source file is independently checked above after native copy.
        var encryption = File.GetAttributes(destination) & FileAttributes.Encrypted;
        File.SetAttributes(destination, (File.GetAttributes(source) & ~FileAttributes.Encrypted) | encryption);
    }

    private static void CommitNewEncryptedDirectory(string stage, string destination)
    {
        Directory.CreateDirectory(destination);
        try
        {
            CopyDirectoryMetadata(stage, destination);
            CommitIntoExistingDirectory(stage, destination);
        }
        catch
        {
            // The inner commit removed only files it created. Preserve anything
            // another process might have added, and remove this root only if empty.
            try { Directory.Delete(destination, recursive: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void CommitIntoExistingDirectory(string stage, string destination)
    {
        var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
            .Select(path => (Source: path, Target: Path.Combine(destination, Path.GetRelativePath(stage, path))))
            .OrderBy(pair => Path.GetRelativePath(stage, pair.Source).Equals("settings.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();
        // Do not overwrite user data, even when the destination has no settings yet.
        foreach (var file in files)
            if (File.Exists(file.Target) || Directory.Exists(file.Target))
                throw new IOException($"新配置目录已有同名文件，未覆盖：{file.Target}");
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories)
                .OrderBy(path => path.Length))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(stage, directory));
                RequireOrdinaryPath(target);
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                    createdDirectories.Add(target);
                    CopyDirectoryMetadata(directory, target);
                }
            }
            foreach (var file in files)
            {
                RequireOrdinaryPath(file.Target);
                File.Move(file.Source, file.Target, overwrite: false);
                createdFiles.Add(file.Target);
            }
        }
        catch
        {
            foreach (string file in createdFiles.AsEnumerable().Reverse()) TryDeleteFile(file);
            foreach (string directory in createdDirectories.AsEnumerable().Reverse())
                try { Directory.Delete(directory, recursive: false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static byte[] RewriteSettings(byte[] original, string legacy, string destination)
    {
        int bom = original.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        var reader = new Utf8JsonReader(original.AsSpan(bom), new JsonReaderOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        using var result = new MemoryStream();
        string? property = null;
        int last = 0;
        bool rootObject = false;
        bool firstToken = true;
        while (reader.Read())
        {
            if (firstToken) { rootObject = reader.TokenType == JsonTokenType.StartObject; firstToken = false; }
            if (reader.TokenType == JsonTokenType.PropertyName) { property = reader.GetString(); continue; }
            if (reader.TokenType == JsonTokenType.String && IsPathProperty(property))
            {
                string value = reader.GetString()!;
                string rewritten = RewriteReference(value, legacy, destination);
                if (!string.Equals(value, rewritten, StringComparison.Ordinal))
                {
                    int start = checked((int)reader.TokenStartIndex + bom);
                    result.Write(original.AsSpan(last, start - last));
                    result.Write(JsonSerializer.SerializeToUtf8Bytes(rewritten));
                    last = checked((int)reader.BytesConsumed + bom);
                }
            }
            property = null;
        }
        if (!rootObject || reader.BytesConsumed != original.Length - bom)
            throw new JsonException("旧配置不是有效的设置对象。");
        result.Write(original.AsSpan(last));
        return result.ToArray();
    }

    private static bool IsPathProperty(string? property) => property is not null && new[]
    {
        "TargetPath", "ImportedShortcutPath", "ImportedDesktopShortcutPath", "WorkingDirectory",
        "IconPath", "CustomIconPath", "BackgroundImagePath"
    }.Contains(property, StringComparer.OrdinalIgnoreCase);

    private static string RewriteReference(string value, string legacy, string destination)
    {
        int start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
        if (start < value.Length && value[start] == '"') start++;
        string reference = value[start..].Replace('/', Path.DirectorySeparatorChar);
        if (!reference.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)) return value;
        int end = start + legacy.Length;
        if (end < value.Length && value[end] is not ('\\' or '/' or '"' or ',')
            && !string.IsNullOrWhiteSpace(value[end..])) return value;
        return value[..start] + destination + value[end..];
    }

    private static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RequireOrdinaryPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("配置迁移路径不能经过链接或重解析点。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void TryDeleteStage(string stage)
    {
        try
        {
            if (!Directory.Exists(stage)) return;
            foreach (string file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)) TryDeleteFile(file);
            Directory.Delete(stage, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string file)
    {
        try { File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
