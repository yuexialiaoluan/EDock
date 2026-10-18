using System.Runtime.InteropServices;
using EDock.Models;
using EDock.Services;

internal static class ManagedShortcutStorageTests
{
    internal static void Register(CoreSuite suite)
    {
        suite.Run("Every shortcut import gets an independent copy even when desktop removal is disabled", () =>
        {
            using var fixture = new Fixture();
            foreach (string directory in new[] { fixture.Desktop, fixture.Elsewhere })
            foreach (string extension in new[] { ".lnk", ".url" })
            {
                string source = fixture.Write(directory, Guid.NewGuid().ToString("N") + extension,
                    "[InternetShortcut]\r\nURL=https://example.com/\r\n");
                var item = ShortcutService.ImportPath(source);
                using var pending = fixture.Prepare(item) ?? throw new InvalidOperationException("Expected private copy.");
                string managed = item.TargetPath;
                Check.False(string.Equals(source, managed, StringComparison.OrdinalIgnoreCase));
                Check.Equal(Path.Combine(fixture.Profile, "Shortcuts"), Path.GetDirectoryName(managed));
                Check.Equal(source, item.ImportedShortcutPath);
                Check.Equal(directory == fixture.Desktop ? source : null, item.ImportedDesktopShortcutPath);
                var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
                var store = new SettingsStore(fixture.Profile); store.Save(settings);
                pending.CommitKeep();
                pending.Dispose();
                Check.True(File.Exists(source));
                File.Delete(source);
                var saved = store.Load().LeftShortcuts.Single();
                Check.Equal(managed, saved.TargetPath);
                Check.True(File.Exists(saved.TargetPath));
                Check.True(ManagedShortcutStorage.WasImportedFrom(saved, source));
                Check.True(fixture.Prepare(saved) is null);
            }
        });

        suite.Run("Private shell link retains its exact launch target arguments and directory after deleting the source", () =>
        {
            using var fixture = new Fixture();
            string target = fixture.Write(fixture.Elsewhere, "program.exe", "application fixture");
            string source = Path.Combine(fixture.Desktop, "Real shortcut.lnk");
            WriteShellLink(source, target, "--profile \"中文 空格\"", fixture.Elsewhere);
            byte[] original = File.ReadAllBytes(source);
            var item = ShortcutService.ImportPath(source);
            using var pending = fixture.Prepare(item)!;
            pending.CommitKeep();
            File.Delete(source);
            Check.True(original.SequenceEqual(File.ReadAllBytes(item.TargetPath)));
            Check.Equal(target, ShortcutIconMetadata.ReadLinkTarget(item.TargetPath));
            Check.Equal("--profile \"中文 空格\"", ShortcutIconMetadata.ReadLinkArguments(item.TargetPath));
            Check.Equal(fixture.Elsewhere, ShortcutIconMetadata.ReadLinkWorkingDirectory(item.TargetPath));
            using var restored = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots);
            Check.False(restored.AlreadyPresent);
            Check.Equal(target, ShortcutIconMetadata.ReadLinkTarget(restored.ExistingOrCreatedPath));
            Check.Equal("--profile \"中文 空格\"", ShortcutIconMetadata.ReadLinkArguments(restored.ExistingOrCreatedPath));
        });

        suite.Run("Managed Internet shortcut preserves game protocols after original deletion and drag-out", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Elsewhere, "Game.url",
                "[InternetShortcut]\r\nURL=steam://rungameid/123456\r\n");
            var item = ShortcutService.ImportPath(source);
            using var pending = fixture.Prepare(item)!;
            pending.CommitKeep();
            File.Delete(source);
            Check.Equal("steam://rungameid/123456", ShortcutIconMetadata.ReadInternetShortcutTarget(item.TargetPath));
            using var restored = DesktopShortcutRestore.Prepare(item, fixture.DesktopRoots);
            Check.Equal("steam://rungameid/123456", ShortcutIconMetadata.ReadInternetShortcutTarget(restored.ExistingOrCreatedPath));
            Check.True(File.Exists(item.TargetPath));
        });

        suite.Run("A saved private shell shortcut still launches after its original desktop file is deleted", () =>
        {
            using var fixture = new Fixture();
            string commandProcessor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            string source = Path.Combine(fixture.Desktop, "Launch regression.lnk");
            string marker = Path.Combine(fixture.Elsewhere, "launch-working-directory.txt");
            WriteShellLink(source, commandProcessor, "/d /c cd>launch-working-directory.txt", fixture.Elsewhere);
            var item = ShortcutService.ImportPath(source);
            using var pending = fixture.Prepare(item)!;
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            var store = new SettingsStore(fixture.Profile); store.Save(settings);
            pending.CommitKeep();
            File.Delete(source);
            ShortcutService.Launch(store.Load().LeftShortcuts.Single());
            Check.True(SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0,
                TimeSpan.FromSeconds(5)));
            Check.Equal(fixture.Elsewhere, File.ReadAllText(marker).Trim());
            Thread.Sleep(100);
        });

        suite.Run("Private copy rollback restores all references and removes only the unsaved copy", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Elsewhere, "rollback.url", "URL fixture");
            var item = new ShortcutItem
            {
                TargetPath = source, IconPath = source + ",0", CustomIconPath = source,
                ImportedShortcutPath = "C:\\previous.url", ImportedDesktopShortcutPath = "C:\\previous-desktop.url"
            };
            string managed;
            using (var pending = fixture.Prepare(item)!)
            {
                managed = item.TargetPath;
                Check.Equal(managed + ",0", item.IconPath);
                Check.Equal(managed, item.CustomIconPath);
                var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
                string blocked = fixture.Write(fixture.Root, "blocked-profile", "file");
                Check.Throws<IOException>(() => new SettingsStore(blocked).Save(settings));
            }
            Check.Equal(source, item.TargetPath);
            Check.Equal(source + ",0", item.IconPath);
            Check.Equal(source, item.CustomIconPath);
            Check.Equal("C:\\previous.url", item.ImportedShortcutPath);
            Check.Equal("C:\\previous-desktop.url", item.ImportedDesktopShortcutPath);
            Check.True(File.Exists(source));
            Check.False(File.Exists(managed));
        });

        suite.Run("Existing active and inactive list shortcuts migrate once and remain independent of their sources", () =>
        {
            using var fixture = new Fixture();
            var settings = new DockSettings();
            var first = settings.GetActiveList(DockSide.Left);
            first.Shortcuts.Add(ShortcutService.ImportPath(fixture.Write(fixture.Desktop, "first.url", "[InternetShortcut]\r\nURL=https://one.example/")));
            var second = settings.AddList(DockSide.Left, "inactive");
            second.Shortcuts.Add(ShortcutService.ImportPath(fixture.Write(fixture.Elsewhere, "second.url", "[InternetShortcut]\r\nURL=https://two.example/")));
            settings.SelectList(DockSide.Left, first.Id);
            settings.GetActiveList(DockSide.Right).Shortcuts.Add(new ShortcutItem { TargetPath = "https://example.com/" });
            using (var migration = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots))
            {
                Check.Equal(2, migration.UpdatedCount);
                Check.Equal(0, migration.UnavailableCount);
                Check.Equal(0, migration.Errors.Count);
                new SettingsStore(fixture.Profile).Save(settings);
                migration.Commit();
            }
            foreach (var item in new[] { first.Shortcuts[0], second.Shortcuts[0] })
            {
                File.Delete(item.ImportedShortcutPath!);
                Check.True(File.Exists(item.TargetPath));
            }
            using var repeated = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots);
            Check.Equal(0, repeated.UpdatedCount);
            Check.Equal(0, repeated.UnavailableCount);
            Check.Equal(2, Directory.EnumerateFiles(Path.Combine(fixture.Profile, "Shortcuts")).Count());
        });

        suite.Run("Migration rollback preserves old configuration paths and source files", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "existing.url", "[InternetShortcut]\r\nURL=https://example.com/");
            var item = ShortcutService.ImportPath(source);
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            var store = new SettingsStore(fixture.Profile); store.Save(settings);
            string previousJson = File.ReadAllText(store.SettingsFilePath);
            string managed;
            using (var migration = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots))
            {
                Check.Equal(1, migration.UpdatedCount);
                managed = item.TargetPath;
                using var blocked = new FileStream(store.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                Check.Throws<IOException>(() => store.Save(settings));
            }
            Check.Equal(source, item.TargetPath);
            Check.True(item.ImportedShortcutPath is null);
            Check.Equal(previousJson, File.ReadAllText(store.SettingsFilePath));
            Check.True(File.Exists(source));
            Check.False(File.Exists(managed));
        });

        suite.Run("Migration recovers a missing private shortcut only from recorded shortcut sources", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "recover.url", "[InternetShortcut]\r\nURL=https://recovered.example/");
            string missing = Path.Combine(fixture.Profile, "Shortcuts", "missing.url");
            var recoverable = new ShortcutItem { TargetPath = missing, IconPath = missing, ImportedDesktopShortcutPath = source };
            string executable = fixture.Write(fixture.Elsewhere, "icon.exe", "not a launch fallback");
            var unrecoverable = new ShortcutItem { TargetPath = Path.Combine(fixture.Desktop, "deleted.lnk"), IconPath = executable };
            var settings = new DockSettings();
            settings.GetActiveList(DockSide.Left).Shortcuts.AddRange(new[] { recoverable, unrecoverable });
            using var migration = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots);
            Check.Equal(1, migration.UpdatedCount);
            Check.Equal(1, migration.UnavailableCount);
            Check.Equal(recoverable.TargetPath, recoverable.IconPath);
            Check.Equal("https://recovered.example/", ShortcutIconMetadata.ReadInternetShortcutTarget(recoverable.TargetPath));
            Check.Equal(Path.Combine(fixture.Desktop, "deleted.lnk"), unrecoverable.TargetPath);
            migration.Commit();
        });

        suite.Run("Desktop recycling and private copy staging compose without making duplicate copies", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "move.url", "[InternetShortcut]\r\nURL=https://example.com/");
            var item = ShortcutService.ImportPath(source);
            int recycled = 0;
            using var transferred = DesktopShortcutTransfer.Prepare(item, fixture.Profile, fixture.DesktopRoots,
                path => { recycled++; File.Delete(path); });
            Check.True(transferred is not null);
            Check.True(fixture.Prepare(item) is null);
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            new SettingsStore(fixture.Profile).Save(settings);
            Check.True(transferred!.CommitDelete());
            Check.Equal(1, recycled);
            Check.False(File.Exists(source));
            Check.True(File.Exists(item.TargetPath));
            Check.Equal(1, Directory.EnumerateFiles(Path.Combine(fixture.Profile, "Shortcuts")).Count());
        });

        suite.Run("Shortcut imports fail explicitly when no independent copy can be saved", () =>
        {
            using var fixture = new Fixture();
            var missing = new ShortcutItem { TargetPath = Path.Combine(fixture.Desktop, "missing.lnk") };
            Check.True(ManagedShortcutStorage.RequiresIndependentCopy(missing));
            Check.False(ManagedShortcutStorage.HasIndependentCopy(missing, fixture.Profile));
            Check.Throws<IOException>(() => fixture.Prepare(missing));
            Check.True(missing.ImportedShortcutPath is null);
            Check.False(Directory.Exists(fixture.Profile));

            foreach (string url in new[] { "https://example.com/shortcut.lnk", "https://example.com/shortcut.url" })
            {
                var web = ShortcutService.ImportPath(url);
                Check.False(ManagedShortcutStorage.RequiresIndependentCopy(web));
                Check.True(fixture.Prepare(web) is null);
                Check.True(ManagedShortcutStorage.WasImportedFrom(web, url));
            }
            string folder = Path.Combine(fixture.Elsewhere, "folder.lnk");
            Directory.CreateDirectory(folder);
            var directory = ShortcutService.ImportPath(folder);
            Check.False(ManagedShortcutStorage.RequiresIndependentCopy(directory));
            Check.True(fixture.Prepare(directory) is null);
        });

        suite.Run("Independent-copy checks distinguish source files from verified managed shortcuts", () =>
        {
            using var fixture = new Fixture();
            string source = fixture.Write(fixture.Desktop, "available.url", "[InternetShortcut]\r\nURL=https://example.com/");
            var item = ShortcutService.ImportPath(source);
            Check.False(ManagedShortcutStorage.HasIndependentCopy(item, fixture.Profile));
            using var pending = fixture.Prepare(item)!;
            Check.True(ManagedShortcutStorage.HasIndependentCopy(item, fixture.Profile));
            Check.True(fixture.Prepare(item) is null);
            pending.CommitKeep();
            File.Delete(item.TargetPath);
            Check.False(ManagedShortcutStorage.HasIndependentCopy(item, fixture.Profile));
            using var repaired = fixture.Prepare(item)!;
            Check.True(repaired is not null);
            Check.True(ManagedShortcutStorage.HasIndependentCopy(item, fixture.Profile));
        });

        suite.Run("Migration copies deleted original shortcuts without consuming recycle files or restoring desktop entries", () =>
        {
            using var fixture = new Fixture();
            string original = Path.Combine(fixture.Desktop, "Deleted.url");
            string recycled = fixture.Write(fixture.Elsewhere, "$R-isolated.url", "[InternetShortcut]\r\nURL=steam://rungameid/2468");
            var item = new ShortcutItem { Name = "Recovered", TargetPath = original, IconPath = original };
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            int recoveryCalls = 0;
            using (var migration = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots,
                path => { recoveryCalls++; return path == original ? recycled : null; }))
            {
                Check.Equal(1, migration.UpdatedCount);
                Check.Equal(0, migration.UnavailableCount);
                Check.Equal(1, recoveryCalls);
                Check.Equal(original, item.ImportedShortcutPath);
                Check.Equal(original, item.ImportedDesktopShortcutPath);
                Check.Equal(item.TargetPath, item.IconPath);
                Check.True(File.Exists(recycled));
                Check.False(File.Exists(original));
                new SettingsStore(fixture.Profile).Save(settings);
                migration.Commit();
            }
            Check.Equal("steam://rungameid/2468", ShortcutIconMetadata.ReadInternetShortcutTarget(item.TargetPath));
            Check.True(File.Exists(recycled));
            Check.False(File.Exists(original));
            using var repeated = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots,
                _ => throw new IOException("Available managed shortcuts must not scan deleted files."));
            Check.Equal(0, repeated.UpdatedCount);
        });

        suite.Run("Failed persistence rolls back deleted-shortcut recovery without modifying recycle files", () =>
        {
            using var fixture = new Fixture();
            string original = Path.Combine(fixture.Desktop, "Deleted.url");
            string recycled = fixture.Write(fixture.Elsewhere, "$R-rollback.url", "[InternetShortcut]\r\nURL=https://example.com/");
            var item = new ShortcutItem { TargetPath = original, IconPath = original + ",0", CustomIconPath = original };
            var settings = new DockSettings(); settings.GetActiveList(DockSide.Left).Shortcuts.Add(item);
            var store = new SettingsStore(fixture.Profile); store.Save(settings);
            string originalJson = File.ReadAllText(store.SettingsFilePath);
            string managed;
            using (var migration = ManagedShortcutStorage.PrepareMigration(settings, fixture.Profile, fixture.DesktopRoots,
                path => path == original ? recycled : null))
            {
                Check.Equal(1, migration.UpdatedCount);
                managed = item.TargetPath;
                using var blocked = new FileStream(store.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                Check.Throws<IOException>(() => store.Save(settings));
            }
            Check.Equal(originalJson, File.ReadAllText(store.SettingsFilePath));
            Check.Equal(original, item.TargetPath);
            Check.Equal(original + ",0", item.IconPath);
            Check.Equal(original, item.CustomIconPath);
            Check.True(item.ImportedShortcutPath is null);
            Check.True(item.ImportedDesktopShortcutPath is null);
            Check.True(File.Exists(recycled));
            Check.False(File.Exists(original));
            Check.False(File.Exists(managed));
        });
    }

    private static void WriteShellLink(string source, string target, string arguments, string workingDirectory)
    {
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic instance = shell!; link = instance.CreateShortcut(source);
            dynamic entry = link!; entry.TargetPath = target; entry.Arguments = arguments;
            entry.WorkingDirectory = workingDirectory; entry.WindowStyle = 7; entry.Save();
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "EDock.ManagedShortcutTests", Guid.NewGuid().ToString("N"));
        internal string Desktop { get; }
        internal string CommonDesktop { get; }
        internal string Elsewhere { get; }
        internal string Profile { get; }
        internal string[] DesktopRoots => new[] { Desktop, CommonDesktop };
        internal Fixture()
        {
            Desktop = Path.Combine(Root, "Desktop"); CommonDesktop = Path.Combine(Root, "CommonDesktop");
            Elsewhere = Path.Combine(Root, "Elsewhere"); Profile = Path.Combine(Root, "Profile");
            Directory.CreateDirectory(Desktop); Directory.CreateDirectory(CommonDesktop); Directory.CreateDirectory(Elsewhere);
        }
        internal string Write(string directory, string name, string content)
        { string path = Path.Combine(directory, name); File.WriteAllText(path, content); return path; }
        internal DesktopShortcutTransfer.PendingTransfer? Prepare(ShortcutItem item) =>
            ManagedShortcutStorage.Prepare(item, Profile, DesktopRoots);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
