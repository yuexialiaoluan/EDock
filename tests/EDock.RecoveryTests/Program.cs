using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using EDock.Services;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;
    private static string _root = string.Empty;
    private static readonly DateTime Deleted = DateTime.UtcNow.AddHours(-1);

    private static int Main()
    {
        _root = Path.Combine(Directory.GetCurrentDirectory(), "work", "recovery-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            Run("Version 1 exact full path returns original bytes without restoring", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "v1", original, Deleted, version: 1);
                byte[] before = File.ReadAllBytes(source);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
                Require(!File.Exists(original) && before.SequenceEqual(File.ReadAllBytes(source)));
                Require(File.Exists(Metadata(source)));
            });
            Run("Version 2 Unicode Internet shortcut is found", () =>
            {
                var (folder, original) = Scenario("中文 游戏.url");
                string source = Add(folder, "unicode", original, Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
            });
            Run("Latest exact original wins across formats and enumeration order", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "z_old", original, Deleted.AddMinutes(-1), version: 1);
                string latest = Add(folder, "a_new", original, Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == latest);
            });
            Run("Same filename from another directory is never substituted", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "wrong", Path.Combine(_root, "Other", Path.GetFileName(original)), Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Matching is case-insensitive and compares the complete path", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "case", original.ToUpperInvariant(), Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
            });
            Run("Existing shortcut and directory are never recovered over", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "present", original, Deleted);
                File.WriteAllText(original, "current");
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
                File.Delete(original);
                Directory.CreateDirectory(original);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Unsupported extension, relative path and absent recycle directory return null", () =>
            {
                var (folder, original) = Scenario();
                Require(DeletedShortcutRecovery.TryFindLatest(Path.ChangeExtension(original, ".exe"), folder) is null);
                Require(DeletedShortcutRecovery.TryFindLatest("app.lnk", folder) is null);
                Require(DeletedShortcutRecovery.TryFindLatest(original, "relative") is null);
                Require(DeletedShortcutRecovery.TryFindLatest(original, Path.Combine(folder, "missing")) is null);
            });
            Run("Mismatched metadata and original extension are rejected", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "wrongext", original, Deleted, extension: ".url");
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Missing paired source and incorrect source length are rejected", () =>
            {
                var (folder, original) = Scenario();
                string missing = Add(folder, "missing", original, Deleted);
                File.Delete(missing);
                string wrongSize = Add(folder, "size", original, Deleted);
                File.AppendAllText(wrongSize, "changed");
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Newer corrupt record falls back to latest intact exact original", () =>
            {
                var (folder, original) = Scenario();
                string good = Add(folder, "good", original, Deleted.AddMinutes(-1));
                string bad = Add(folder, "bad", original, Deleted);
                File.WriteAllBytes(Metadata(bad), new byte[] { 2, 0, 0 });
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == good);
            });
            Run("Unknown version is rejected", () => RejectMetadata(bytes => BitConverter.GetBytes(3L).CopyTo(bytes, 0)));
            Run("Negative original length is rejected", () => RejectMetadata(bytes => BitConverter.GetBytes(-1L).CopyTo(bytes, 8)));
            Run("Future deletion time is rejected", () => RejectMetadata(bytes => BitConverter.GetBytes(DateTime.UtcNow.AddDays(1).ToFileTimeUtc()).CopyTo(bytes, 16)));
            Run("Zero deletion time is rejected", () => RejectMetadata(bytes => BitConverter.GetBytes(0L).CopyTo(bytes, 16)));
            Run("Out-of-range deletion time is rejected", () => RejectMetadata(bytes => BitConverter.GetBytes(long.MaxValue).CopyTo(bytes, 16)));
            Run("Version 2 negative and excessive path lengths are rejected", () =>
            {
                RejectMetadata(bytes => BitConverter.GetBytes(-1).CopyTo(bytes, 24));
                RejectMetadata(bytes => BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 24));
            });
            Run("Version 2 truncated and trailing metadata are rejected", () =>
            {
                RejectMetadata(_ => { }, truncate: true);
                var (folder, original) = Scenario();
                string source = Add(folder, "trailing", original, Deleted);
                using (var append = new FileStream(Metadata(source), FileMode.Append)) append.WriteByte(0);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Version 2 missing terminator and embedded null are rejected", () =>
            {
                RejectMetadata(bytes => { bytes[^2] = 65; bytes[^1] = 0; });
                RejectMetadata(bytes => { bytes[28 + 8] = 0; bytes[28 + 9] = 0; });
            });
            Run("Invalid Unicode path is rejected", () => RejectMetadata(bytes => { bytes[28 + 8] = 0; bytes[28 + 9] = 0xD8; }));
            Run("Version 1 missing terminator and nonzero padding are rejected", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "v1padding", original, Deleted, version: 1);
                var bytes = File.ReadAllBytes(Metadata(source));
                bytes[^2] = 65;
                File.WriteAllBytes(Metadata(source), bytes);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
                Array.Fill(bytes, (byte)65, 24, 520);
                File.WriteAllBytes(Metadata(source), bytes);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Relative original recorded in metadata is rejected", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "relative", "app.lnk", Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Source directory with shortcut extension is rejected", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "directory", original, Deleted);
                File.Delete(source);
                Directory.CreateDirectory(source);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Nested records are not scanned", () =>
            {
                var (folder, original) = Scenario();
                string nested = Path.Combine(folder, "nested");
                Directory.CreateDirectory(nested);
                Add(nested, "nested", original, Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Cached matches are revalidated after the source is deleted", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "cached", original, Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
                File.Delete(source);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Directory changes refresh cached index to include a newer record", () =>
            {
                var (folder, original) = Scenario();
                string old = Add(folder, "old", original, Deleted.AddMinutes(-1));
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == old);
                string latest = Add(folder, "new", original, Deleted);
                Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddMinutes(-2));
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == latest);
            });
            Run("Cached metadata path replacement cannot change the original target", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "replace", original, Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
                Add(folder, "replace", Path.Combine(_root, "Other", "app.lnk"), Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
            });
            Run("Reparse source is never followed", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "symlink", original, Deleted);
                string outside = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".lnk");
                File.Copy(source, outside);
                File.Delete(source);
                File.CreateSymbolicLink(source, outside);
                try { Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null); }
                finally { File.Delete(source); }
            });
            Run("Reparse metadata is never followed", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "metalink", original, Deleted);
                string metadata = Metadata(source);
                string outside = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                File.Move(metadata, outside);
                File.CreateSymbolicLink(metadata, outside);
                try { Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null); }
                finally { File.Delete(metadata); }
            });
            Run("Reparse recycle directory is never traversed", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "parent", original, Deleted);
                string alias = Path.Combine(_root, "alias-" + Guid.NewGuid().ToString("N"));
                CreateJunction(alias, folder);
                try { Require(DeletedShortcutRecovery.TryFindLatest(original, alias) is null); }
                finally { Directory.Delete(alias); }
            });
            Run("Existing dangling shortcut reparse point is not considered missing", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "dangling", original, Deleted);
                CreateJunction(original, original + ".missing");
                try { Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null); }
                finally { Directory.Delete(original); }
            });
            Run("Reparse ancestor above recycle directory is never traversed", () =>
            {
                var (folder, original) = Scenario();
                Add(folder, "ancestor", original, Deleted);
                string alias = Path.Combine(_root, "parent-" + Guid.NewGuid().ToString("N"));
                CreateJunction(alias, Path.GetDirectoryName(folder)!);
                try { Require(DeletedShortcutRecovery.TryFindLatest(original, Path.Combine(alias, "recycle")) is null); }
                finally { Directory.Delete(alias); }
            });
            Run("Cache release permits rebuilding an unchanged directory", () =>
            {
                var (folder, original) = Scenario();
                string source = Add(folder, "clear", original, Deleted);
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
                DeletedShortcutRecovery.ClearCache();
                Require(DeletedShortcutRecovery.TryFindLatest(original, folder) == source);
            });
        }
        finally { Directory.Delete(_root, recursive: true); }
        Console.WriteLine($"Passed: {_passed}; Failures: {_failed}; Skipped: {_skipped}");
        return _failed == 0 ? 0 : 1;
    }

    private static (string Folder, string Original) Scenario(string name = "app.lnk")
    {
        string scenario = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(scenario, "recycle");
        string desktop = Path.Combine(scenario, "desktop");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(desktop);
        return (folder, Path.Combine(desktop, name));
    }

    private static string Add(string folder, string id, string original, DateTime deleted, int version = 2, string? extension = null)
    {
        string suffix = id + (extension ?? Path.GetExtension(original));
        string source = Path.Combine(folder, "$R" + suffix);
        File.WriteAllText(source, "original shortcut bytes", new UTF8Encoding(false));
        using var stream = File.Create(Path.Combine(folder, "$I" + suffix));
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write((long)version);
        writer.Write(new FileInfo(source).Length);
        writer.Write(deleted.ToFileTimeUtc());
        if (version == 2)
        {
            writer.Write(original.Length + 1);
            writer.Write(Encoding.Unicode.GetBytes(original + '\0'));
        }
        else
        {
            var path = new byte[520];
            Encoding.Unicode.GetBytes(original + '\0').CopyTo(path, 0);
            writer.Write(path);
        }
        return source;
    }

    private static string Metadata(string source) => Path.Combine(Path.GetDirectoryName(source)!, "$I" + Path.GetFileName(source)[2..]);

    private static void RejectMetadata(Action<byte[]> mutate, bool truncate = false)
    {
        var (folder, original) = Scenario();
        string source = Add(folder, "corrupt", original, Deleted);
        var bytes = File.ReadAllBytes(Metadata(source));
        mutate(bytes);
        if (truncate) bytes = bytes[..^2];
        File.WriteAllBytes(Metadata(source), bytes);
        Require(DeletedShortcutRecovery.TryFindLatest(original, folder) is null);
    }

    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }

    private static void CreateJunction(string path, string target)
    {
        Directory.CreateDirectory(path);
        string fullTarget = Path.GetFullPath(target);
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + fullTarget);
        byte[] display = Encoding.Unicode.GetBytes(fullTarget);
        byte[] data = new byte[16 + substitute.Length + 2 + display.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)(data.Length - 8)).CopyTo(data, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(data, 10);
        BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(data, 12);
        BitConverter.GetBytes((ushort)display.Length).CopyTo(data, 14);
        substitute.CopyTo(data, 16);
        display.CopyTo(data, 16 + substitute.Length + 2);
        using var handle = Native.CreateFile(path, 0x40000000, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid || !Native.DeviceIoControl(handle, 0x000900A4, data, data.Length, 0, 0, out _, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void Run(string name, Action test)
    {
        try { test(); ++_passed; Console.WriteLine("PASS " + name); }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) == 1314)
        {
            ++_skipped;
            Console.WriteLine("SKIP " + name + ": Windows does not grant file symbolic-link creation; junction cases are tested separately.");
        }
        catch (Exception ex) { ++_failed; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength, nint output, int outputLength, out int returned, nint overlapped);
    }
}
