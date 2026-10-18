using EDock.Models;
using EDock.Services;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class DesktopShortcutTransferTests
{
    internal static void Register(CoreSuite suite)
    {
        suite.Run("Drag-out placement creates a desktop shortcut without moving its target and rolls back until committed", () =>
        {
            using var fixture = new Fixture();
            string target = fixture.Write(fixture.Root, "program.exe", "application fixture");
            var item = new ShortcutItem
            {
                Name = "Test App", TargetPath = target, Arguments = "--profile test",
                WorkingDirectory = fixture.Root, IconPath = Path.Combine(fixture.Root, "app.ico")
            };
            string? captured = null;
            using (var pending = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots, (shortcut, path) =>
            {
                captured = path;
                File.WriteAllText(path, $"{shortcut.TargetPath}|{shortcut.Arguments}|{shortcut.WorkingDirectory}|{shortcut.IconPath}");
            }))
            {
                Check.False(pending.AlreadyPresent);
                Check.True(pending.ExistingOrCreatedPath.StartsWith(fixture.Desktop, StringComparison.OrdinalIgnoreCase));
                Check.True(pending.ExistingOrCreatedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
                Check.True(File.Exists(pending.ExistingOrCreatedPath));
                Check.True(File.Exists(target));
                Check.True(captured is not null && captured.EndsWith(".tmp.lnk", StringComparison.OrdinalIgnoreCase));
                Check.True(File.ReadAllText(pending.ExistingOrCreatedPath).Contains("--profile test"));
            }
            Check.False(File.Exists(Path.Combine(fixture.Desktop, "Test App.lnk")));
            Check.True(File.Exists(target));

            using var committed = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots,
                (_, path) => File.WriteAllText(path, "desktop shortcut"));
            string committedPath = committed.ExistingOrCreatedPath;
            committed.Commit();
            committed.Dispose();
            Check.True(File.Exists(committedPath));
            Check.True(File.Exists(target));
        });

        suite.Run("Drag-out detects equivalent user or common desktop shortcut without deleting it", () =>
        {
            using var fixture = new Fixture();
            string existing = fixture.Write(fixture.CommonDesktop, "Existing.url", "[InternetShortcut]\r\nURL=https://example.com/\r\n");
            var item = new ShortcutItem { Name = "Example", TargetPath = "https://example.com" };
            int createCalls = 0;
            using var pending = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots, (_, _) => createCalls++);
            Check.True(pending.AlreadyPresent);
            Check.Equal(existing, pending.ExistingOrCreatedPath);
            Check.Equal(0, createCalls);
            pending.Dispose();
            Check.True(File.Exists(existing));
        });

        suite.Run("Drag-out does not reuse stale imported provenance after retargeting", () =>
        {
            using var fixture = new Fixture();
            string oldSource = fixture.Write(fixture.Desktop, "Old.url", "[InternetShortcut]\r\nURL=https://old.example/\r\n");
            string newSource = fixture.Write(fixture.Root, "new.url", "[InternetShortcut]\r\nURL=https://new.example/\r\n");
            var item = new ShortcutItem
            {
                Name = "Retargeted", TargetPath = newSource, ImportedDesktopShortcutPath = oldSource
            };
            using var pending = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots);
            Check.False(pending.AlreadyPresent);
            Check.False(string.Equals(oldSource, pending.ExistingOrCreatedPath, StringComparison.OrdinalIgnoreCase));
            Check.True(File.ReadAllText(pending.ExistingOrCreatedPath).Contains("URL=https://new.example/", StringComparison.OrdinalIgnoreCase));
            Check.True(File.Exists(oldSource));
        });

        suite.Run("Internet shortcut drag-out remains a valid url file with edited metadata", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Root, "Website.url", "[InternetShortcut]\r\nURL=https://example.com/path\r\n");
            var item = new ShortcutItem
            {
                Name = "Website copy", TargetPath = source, Arguments = "--ignored-by-url",
                WorkingDirectory = fixture.Root
            };
            using var pending = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots);
            Check.False(pending.AlreadyPresent);
            string content = File.ReadAllText(pending.ExistingOrCreatedPath);
            Check.True(content.Contains("[InternetShortcut]", StringComparison.Ordinal));
            Check.True(content.Contains("URL=https://example.com/path", StringComparison.Ordinal));
            Check.False(content.Contains("--ignored-by-url", StringComparison.Ordinal));
            Check.True(File.Exists(source));
            pending.Commit();
            using var repeated = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots);
            Check.True(repeated.AlreadyPresent);
            Check.Equal(pending.ExistingOrCreatedPath, repeated.ExistingOrCreatedPath);
        });

        suite.Run("Drag-out reuses an unchanged desktop lnk with its original working directory", () =>
        {
            using var fixture = new Fixture();
            string target = fixture.Write(fixture.Root, "program.exe", "application fixture");
            string workingDirectory = Path.Combine(fixture.Root, "working");
            Directory.CreateDirectory(workingDirectory);
            string source = Path.Combine(fixture.Desktop, "Original.lnk");
            object? shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                dynamic instance = shell!;
                link = instance.CreateShortcut(source);
                dynamic entry = link;
                entry.TargetPath = target;
                entry.WorkingDirectory = workingDirectory;
                entry.Arguments = "--from-link";
                entry.Save();

                var item = ShortcutService.ImportPath(source);
                Check.Equal(workingDirectory, item.WorkingDirectory);
                using var pending = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots,
                    (_, _) => throw new IOException("equivalent desktop link should be reused"));
                Check.True(pending.AlreadyPresent);
                Check.Equal(source, pending.ExistingOrCreatedPath);
            }
            finally
            {
                if (link is not null) Marshal.FinalReleaseComObject(link);
                if (shell is not null) Marshal.FinalReleaseComObject(shell);
            }
        });

        suite.Run("Shortcut arguments are merged with one separator", () =>
        {
            Check.Equal(string.Empty, DesktopShortcutRestore.MergeArguments(null, null));
            Check.Equal("--one", DesktopShortcutRestore.MergeArguments("", " --one "));
            Check.Equal("--one --two", DesktopShortcutRestore.MergeArguments("--one", "--two"));
            Check.Equal("--one --two", DesktopShortcutRestore.MergeArguments(" --one ", " --two "));
        });

        suite.Run("Drag-out reports placement collisions and creation failure without touching target files", () =>
        {
            using var fixture = new Fixture();
            string target = fixture.Write(fixture.Root, "different.url", "[InternetShortcut]\r\nURL=https://elsewhere.example/\r\n");
            fixture.Write(fixture.Desktop, "Link.url", "[InternetShortcut]\r\nURL=https://different.example/\r\n");
            var item = new ShortcutItem { Name = "Link", TargetPath = "https://example.org" };
            using var pending = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots,
                (_, path) => File.WriteAllText(path, "new shortcut"));
            Check.True(pending.ExistingOrCreatedPath.EndsWith("Link (2).url", StringComparison.OrdinalIgnoreCase));
            Check.True(File.Exists(Path.Combine(fixture.Desktop, "Link.url")));
            Check.True(File.Exists(target));
            pending.Rollback();
            Check.False(File.Exists(pending.ExistingOrCreatedPath));

            Check.Throws<IOException>(() => DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots,
                (_, _) => throw new IOException("fixture failure")));
            Check.True(File.Exists(target));
            Check.True(File.Exists(Path.Combine(fixture.Desktop, "Link.url")));
        });

        suite.Run("Desktop removal and quick mode default off and global options persist", () =>
        {
            using var fixture = new Fixture();
            var settings = new DockSettings();
            Check.False(settings.RemoveDesktopShortcutAfterImport);
            Check.False(settings.QuickModeEnabled);
            Check.Equal("Left", settings.QuickModeSide);
            var store = new SettingsStore(fixture.Profile);
            foreach (string side in new[] { "Left", "Right", "Both" })
            {
                settings.RemoveDesktopShortcutAfterImport = true;
                settings.QuickModeEnabled = true;
                settings.QuickModeSide = side;
                store.Save(settings);
                var loaded = store.Load();
                Check.True(loaded.RemoveDesktopShortcutAfterImport);
                Check.True(loaded.QuickModeEnabled);
                Check.Equal(side, loaded.QuickModeSide);
            }
            settings.QuickModeSide = "Unknown";
            settings.Normalize();
            Check.Equal("Left", settings.QuickModeSide);
            settings.QuickModeSide = null!;
            settings.Normalize();
            Check.Equal("Left", settings.QuickModeSide);
        });

        suite.Run("Only direct desktop lnk and url files can be staged", () =>
        {
            using var fixture = new Fixture();
            string nested = Path.Combine(fixture.Desktop, "subfolder");
            Directory.CreateDirectory(nested);
            foreach (string path in new[]
            {
                fixture.Write(fixture.Desktop, "ordinary.txt"),
                fixture.Write(fixture.Desktop, "program.exe"),
                fixture.Write(fixture.Desktop, "shortcut.lnk.exe"),
                fixture.Write(nested, "nested.lnk"),
                fixture.Write(fixture.Root, "outside.url"),
                Path.Combine(fixture.Desktop, "missing.lnk"),
                fixture.Desktop,
                "https://example.com/shortcut.url"
            })
            {
                var item = new ShortcutItem { TargetPath = path };
                Check.True(fixture.Prepare(item) is null);
                Check.Equal(path, item.TargetPath);
            }
            string directoryLink = Path.Combine(fixture.Desktop, "folder.lnk");
            Directory.CreateDirectory(directoryLink);
            Check.True(fixture.Prepare(new ShortcutItem { TargetPath = directoryLink }) is null);
            foreach (var pair in new[] { (fixture.Desktop, "one.LNK"), (fixture.CommonDesktop, "two.URL") })
            {
                string path = fixture.Write(pair.Item1, pair.Item2);
                using var pending = fixture.Prepare(new ShortcutItem { TargetPath = path });
                Check.True(pending is not null);
            }
            var temporaryDesktopItem = new ShortcutItem { TargetPath = fixture.Write(fixture.Desktop, "not-real-desktop.lnk") };
            Check.True(DesktopShortcutTransfer.Prepare(temporaryDesktopItem, fixture.Profile) is null);
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Staging verifies a private copy preserves metadata and keeps the desktop original", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "中文 快捷方式.lnk", "shortcut-content");
            var item = new ShortcutItem
            {
                TargetPath = source, Name = "保留名称", Arguments = "--example", WorkingDirectory = "C:\\example",
                IconPath = "C:\\icons\\app.ico", CustomIconPath = "C:\\images\\tile.png", ImageCrop = "Fit",
                AddedAtUtc = DateTimeOffset.Parse("2026-10-17T00:00:00Z")
            };
            using var pending = fixture.Prepare(item) ?? throw new InvalidOperationException("Expected staged shortcut.");
            Check.Equal(source, pending.OriginalPath);
            Check.Equal(source, item.ImportedDesktopShortcutPath);
            Check.Equal(Path.Combine(fixture.Profile, "Shortcuts"), Path.GetDirectoryName(pending.ManagedPath));
            Check.Equal(pending.ManagedPath, item.TargetPath);
            Check.Equal("shortcut-content", File.ReadAllText(pending.ManagedPath));
            Check.True(File.Exists(source));
            Check.Equal("保留名称", item.Name);
            Check.Equal("--example", item.Arguments);
            Check.Equal("C:\\example", item.WorkingDirectory);
            Check.Equal("C:\\icons\\app.ico", item.IconPath);
            Check.Equal("C:\\images\\tile.png", item.CustomIconPath);
            Check.Equal("Fit", item.ImageCrop);
            Check.Equal(DateTimeOffset.Parse("2026-10-17T00:00:00Z"), item.AddedAtUtc!.Value);
            Check.False(pending.IsCommitted);
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Uncommitted disposal rolls back target and owned copy without deleting the desktop entry", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "rollback.url");
            var item = new ShortcutItem { TargetPath = source };
            var pending = fixture.Prepare(item)!;
            string managed = pending.ManagedPath;
            pending.Rollback(); pending.Rollback(); pending.Dispose();
            Check.Equal(source, item.TargetPath);
            Check.True(item.ImportedDesktopShortcutPath is null);
            Check.True(File.Exists(source));
            Check.False(File.Exists(managed));
            Check.False(pending.CommitDelete());
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Shortcut-based icons follow managed copies and roll back with unsaved targets", () =>
        {
            using var fixture = new Fixture();
            foreach (string extension in new[] { ".lnk", ".url" })
            {
                string source = fixture.Write(fixture.Desktop, "icon" + extension);
                var item = new ShortcutItem { TargetPath = source, IconPath = source.ToUpperInvariant() };
                string originalIcon = item.IconPath;
                var pending = fixture.Prepare(item)!;
                Check.Equal(pending.ManagedPath, item.IconPath);
                pending.Dispose();
                Check.Equal(source, item.TargetPath);
                Check.Equal(originalIcon, item.IconPath);
                Check.True(File.Exists(source));
            }
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Rollback preserves an icon edited after staging", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "edited-icon.lnk");
            var item = new ShortcutItem { TargetPath = source, IconPath = source };
            var pending = fixture.Prepare(item)!;
            item.IconPath = "C:\\icons\\replacement.ico";
            pending.Dispose();
            Check.Equal(source, item.TargetPath);
            Check.Equal("C:\\icons\\replacement.ico", item.IconPath);
            Check.True(File.Exists(source));
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Uncommitted transfer restores existing source provenance", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "provenance.url");
            var item = new ShortcutItem { TargetPath = source, ImportedDesktopShortcutPath = "C:\\old\\source.url" };
            var pending = fixture.Prepare(item)!;
            Check.Equal(source, item.ImportedDesktopShortcutPath);
            pending.Dispose();
            Check.Equal("C:\\old\\source.url", item.ImportedDesktopShortcutPath);
            Check.Equal(source, item.TargetPath);
            Check.True(File.Exists(source));
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Successful settings persistence precedes recycling and disposal retains the managed target", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.CommonDesktop, "persisted.lnk", "saved shortcut");
            var item = new ShortcutItem { TargetPath = source, IconPath = source };
            var pending = fixture.Prepare(item)!;
            string managed = pending.ManagedPath;
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            var store = new SettingsStore(fixture.Profile);
            store.Save(settings);
            Check.Equal(source, store.Load().LeftShortcuts.Single().ImportedDesktopShortcutPath);
            Check.Equal(0, fixture.RecycleCalls);
            Check.True(File.Exists(source));
            Check.True(pending.CommitDelete());
            Check.True(pending.IsCommitted);
            pending.Dispose(); pending.Rollback();
            Check.False(pending.CommitDelete());
            Check.Equal(1, fixture.RecycleCalls);
            Check.False(File.Exists(source));
            Check.Equal("saved shortcut", File.ReadAllText(fixture.RecycledPath!));
            Check.Equal(managed, item.TargetPath);
            Check.Equal(managed, store.Load().LeftShortcuts.Single().TargetPath);
            Check.Equal(managed, item.IconPath);
            Check.Equal(managed, store.Load().LeftShortcuts.Single().IconPath);
            Check.Equal(source, item.ImportedDesktopShortcutPath);
            Check.Equal(source, store.Load().LeftShortcuts.Single().ImportedDesktopShortcutPath);
            Check.True(File.Exists(managed));
        });

        suite.Run("Save failure rolls back staging and never invokes desktop recycling", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "save-fails.url");
            var item = new ShortcutItem { TargetPath = source };
            string managed;
            using (var pending = fixture.Prepare(item)!)
            {
                managed = pending.ManagedPath;
                string blocked = fixture.Write(fixture.Root, "profile-is-a-file");
                var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
                Check.Throws<IOException>(() => new SettingsStore(blocked).Save(settings));
            }
            Check.Equal(source, item.TargetPath);
            Check.True(item.ImportedDesktopShortcutPath is null);
            Check.True(File.Exists(source));
            Check.False(File.Exists(managed));
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Recycle failure preserves both the saved managed target and desktop shortcut", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "permission-denied.lnk");
            var item = new ShortcutItem { TargetPath = source, IconPath = source };
            var pending = fixture.Prepare(item, _ => throw new UnauthorizedAccessException("test denial"))!;
            string managed = pending.ManagedPath;
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            var store = new SettingsStore(fixture.Profile); store.Save(settings);
            Check.False(pending.CommitDelete());
            Check.True(pending.IsCommitted);
            Check.True(pending.LastError!.Contains("test denial"));
            pending.Dispose(); pending.Rollback();
            Check.Equal(managed, item.TargetPath);
            Check.Equal(managed, store.Load().LeftShortcuts.Single().TargetPath);
            Check.Equal(managed, item.IconPath);
            Check.Equal(managed, store.Load().LeftShortcuts.Single().IconPath);
            Check.Equal(source, item.ImportedDesktopShortcutPath);
            Check.Equal(source, store.Load().LeftShortcuts.Single().ImportedDesktopShortcutPath);
            Check.True(File.Exists(source));
            Check.True(File.Exists(managed));
        });

        suite.Run("Retargeting a staged item suppresses deletion and keeps the saved managed copy", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "retargeted.lnk");
            var item = new ShortcutItem { TargetPath = source };
            using var pending = fixture.Prepare(item)!;
            item.TargetPath = fixture.Write(fixture.Root, "different.exe");
            Check.False(pending.CommitDelete());
            Check.True(File.Exists(source));
            Check.True(File.Exists(pending.ManagedPath));
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Managed copies are unique and cannot be stored beneath a desktop root", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "unique.lnk");
            using var first = fixture.Prepare(new ShortcutItem { TargetPath = source })!;
            using var second = fixture.Prepare(new ShortcutItem { TargetPath = source })!;
            Check.False(first.ManagedPath == second.ManagedPath);
            var item = new ShortcutItem { TargetPath = source };
            Check.Throws<IOException>(() => DesktopShortcutTransfer.Prepare(item,
                Path.Combine(fixture.Desktop, "profile"), fixture.DesktopRoots, fixture.Recycle));
            Check.Equal(source, item.TargetPath);
            Check.Equal(0, fixture.RecycleCalls);
        });

        suite.Run("Staging rejects reparse files roots and managed directories", () =>
        {
            using var fixture = new Fixture();
            string outside = fixture.Write(fixture.Root, "outside.lnk");
            string fileLink = Path.Combine(fixture.Desktop, "reparse.lnk");
            bool symbolicLinkCreated = false;
            try
            {
                try { File.CreateSymbolicLink(fileLink, outside); symbolicLinkCreated = true; }
                catch (Exception error) when (error is UnauthorizedAccessException or IOException) { }
                if (symbolicLinkCreated)
                    Check.True(fixture.Prepare(new ShortcutItem { TargetPath = fileLink }) is null);
                else
                {
                    // Directory junctions use the same ReparsePoint attribute and
                    // do not require the symbolic-link privilege on Windows.
                    CreateJunction(fileLink, fixture.Root);
                    Check.True(fixture.Prepare(new ShortcutItem { TargetPath = fileLink }) is null);
                }
                string linkedDesktop = Path.Combine(fixture.Root, "desktop-alias");
                CreateJunction(linkedDesktop, fixture.Desktop);
                try
                {
                    string source = fixture.Write(fixture.Desktop, "real.lnk");
                    Check.True(DesktopShortcutTransfer.Prepare(new ShortcutItem { TargetPath = Path.Combine(linkedDesktop, "real.lnk") },
                        fixture.Profile, new[] { linkedDesktop }, fixture.Recycle) is null);
                    Directory.CreateDirectory(fixture.Profile);
                    string managedLink = Path.Combine(fixture.Profile, "Shortcuts");
                    CreateJunction(managedLink, fixture.Root);
                    try { Check.Throws<IOException>(() => fixture.Prepare(new ShortcutItem { TargetPath = source })); }
                    finally { Directory.Delete(managedLink); }
                }
                finally { Directory.Delete(linkedDesktop); }
                Check.True(File.Exists(outside));
                Check.Equal(0, fixture.RecycleCalls);
            }
            finally
            {
                if (symbolicLinkCreated) File.Delete(fileLink);
                else if (Directory.Exists(fileLink)) Directory.Delete(fileLink);
            }
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "EDock.TransferTests", Guid.NewGuid().ToString("N"));
        internal string Desktop { get; }
        internal string CommonDesktop { get; }
        internal string Profile { get; }
        internal string[] DesktopRoots => new[] { Desktop, CommonDesktop };
        internal int RecycleCalls { get; private set; }
        internal string? RecycledPath { get; private set; }
        internal Fixture()
        {
            Desktop = Path.Combine(Root, "UserDesktop");
            CommonDesktop = Path.Combine(Root, "CommonDesktop");
            Profile = Path.Combine(Root, "Profile");
            Directory.CreateDirectory(Desktop); Directory.CreateDirectory(CommonDesktop);
        }
        internal string Write(string directory, string name, string content = "fixture shortcut")
        {
            string path = Path.Combine(directory, name); File.WriteAllText(path, content); return path;
        }
        internal DesktopShortcutTransfer.PendingTransfer? Prepare(ShortcutItem item, Action<string>? recycle = null) =>
            DesktopShortcutTransfer.Prepare(item, Profile, DesktopRoots, recycle ?? Recycle);
        internal void Recycle(string path)
        {
            RecycleCalls++;
            RecycledPath = Path.Combine(Root, "Recycled-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            File.Move(path, RecycledPath);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void CreateJunction(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(linkPath);
        string substitute = @"\??\" + Path.GetFullPath(targetPath);
        string display = Path.GetFullPath(targetPath);
        byte[] pathBuffer = Encoding.Unicode.GetBytes(substitute + "\0" + display + "\0");
        byte[] buffer = new byte[16 + pathBuffer.Length];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)(substitute.Length * 2)).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)((substitute.Length + 1) * 2)).CopyTo(buffer, 12);
        BitConverter.GetBytes((ushort)(display.Length * 2)).CopyTo(buffer, 14);
        pathBuffer.CopyTo(buffer, 16);
        using var handle = CreateFile(linkPath, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length,
            IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, int inputLength,
        IntPtr output, int outputLength, out int bytesReturned, IntPtr overlapped);
}
