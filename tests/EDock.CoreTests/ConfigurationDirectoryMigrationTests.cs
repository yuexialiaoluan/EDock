using System.Text;
using System.Text.Json;
using EDock.Services;

static class ConfigurationDirectoryMigrationTests
{
    internal static void Register(CoreSuite suite, string? encryptedParent)
    {
        suite.Run("Profile rename preserves lists, unknown settings and only redirects exact path references", () =>
        {
            using var fixture = new Fixture();
            string old = fixture.Legacy;
            string target = Path.Combine(old, "Shortcuts", "game.url");
            string icon = "\"" + Path.Combine(old, "Images", "icon.dll") + "\",-2";
            string sibling = old + "-other\\file.png";
            var document = new
            {
                LeftLists = new[] { new { Id = "same-id", Name = old, SortDescending = true, Shortcuts = new[]
                {
                    new { TargetPath = target, IconPath = icon, CustomIconPath = sibling,
                        ImportedShortcutPath = @"C:\Desktop\game.url", WorkingDirectory = old,
                        Arguments = "--read \"" + target + "\"", Name = target }
                } } },
                RightLists = Array.Empty<object>(),
                BackgroundImagePath = Path.Combine(old, "Images", "background.png"),
                FutureSetting = new { Text = target, ExactNumber = 0.123456789m, Enabled = false },
                HeaderText = "EdgeDock / EDock " + old
            };
            string json = " \r\n" + JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + "\r\n";
            fixture.Write("settings.json", json);
            fixture.Write("Shortcuts/game.url", "[InternetShortcut]\r\nURL=steam://rungameid/42");
            fixture.Write("Images/background.png", "background bytes");
            fixture.Write("Extra/user.data", "preserve unknown data");
            fixture.Write("settings.previous.json", json);
            byte[] original = File.ReadAllBytes(fixture.Settings);
            var result = fixture.Migrate();
            Check.True(result.Migrated); Check.True(result.Error is null);
            Check.Equal(fixture.Destination, result.DirectoryPath);
            Check.True(original.SequenceEqual(File.ReadAllBytes(fixture.Settings)));
            using var after = JsonDocument.Parse(File.ReadAllBytes(fixture.NewSettings));
            var root = after.RootElement;
            var list = root.GetProperty("LeftLists")[0];
            var item = list.GetProperty("Shortcuts")[0];
            Check.Equal(Path.Combine(fixture.Destination, "Shortcuts", "game.url"), item.GetProperty("TargetPath").GetString());
            Check.Equal("\"" + Path.Combine(fixture.Destination, "Images", "icon.dll") + "\",-2", item.GetProperty("IconPath").GetString());
            Check.Equal(sibling, item.GetProperty("CustomIconPath").GetString());
            Check.Equal(old, list.GetProperty("Name").GetString());
            Check.Equal(target, item.GetProperty("Name").GetString());
            Check.Equal("--read \"" + target + "\"", item.GetProperty("Arguments").GetString());
            Check.Equal(@"C:\Desktop\game.url", item.GetProperty("ImportedShortcutPath").GetString());
            Check.Equal(fixture.Destination, item.GetProperty("WorkingDirectory").GetString());
            Check.Equal(target, root.GetProperty("FutureSetting").GetProperty("Text").GetString());
            Check.Equal(0.123456789m, root.GetProperty("FutureSetting").GetProperty("ExactNumber").GetDecimal());
            Check.True(File.ReadAllText(fixture.NewSettings).StartsWith(" \r\n"));
            Check.Equal(json, File.ReadAllText(Path.Combine(fixture.Destination, "settings.previous.json")));
            foreach (string relative in new[] { "Shortcuts/game.url", "Images/background.png", "Extra/user.data" })
                Check.True(File.ReadAllBytes(Path.Combine(old, relative)).SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Destination, relative))));
            Check.False(fixture.Migrate().Migrated);
            fixture.NoStage();
        });

        suite.Run("Existing new settings always win without touching either profile", () =>
        {
            using var fixture = new Fixture();
            fixture.Write("settings.json", "{\"HeaderText\":\"legacy\"}");
            Directory.CreateDirectory(fixture.Destination);
            File.WriteAllText(fixture.NewSettings, "new profile even if malformed");
            var result = fixture.Migrate((_, _) => throw new InvalidOperationException("Must not copy."));
            Check.False(result.Migrated); Check.True(result.Error is null);
            Check.Equal(fixture.Destination, result.DirectoryPath);
            Check.Equal("new profile even if malformed", File.ReadAllText(fixture.NewSettings));
            Check.Equal("{\"HeaderText\":\"legacy\"}", File.ReadAllText(fixture.Settings));
            fixture.NoStage();
        });

        suite.Run("Isolated profiles and missing legacy settings never trigger a migration", () =>
        {
            using var fixture = new Fixture();
            var missing = fixture.Migrate();
            Check.Equal(fixture.Destination, missing.DirectoryPath);
            Check.False(missing.Migrated); Check.True(missing.Error is null);
            Check.False(Directory.Exists(fixture.Destination));
            fixture.Write("settings.json", "{}");
            string isolated = Path.Combine(fixture.Root, "isolated");
            var result = ConfigurationDirectoryMigration.ResolveFromDirectories(fixture.Legacy, fixture.Destination,
                isolated, (_, _) => throw new InvalidOperationException("Must not copy."));
            Check.Equal(isolated, result.DirectoryPath); Check.False(result.Migrated);
            Check.False(Directory.Exists(fixture.Destination));
            fixture.NoStage();
        });

        suite.Run("Copy failure leaves legacy complete and commits no partial new profile", () =>
        {
            using var fixture = new Fixture();
            fixture.Write("settings.json", "{\"HeaderText\":\"keep\"}");
            fixture.Write("Shortcuts/app.lnk", "original shortcut");
            var result = fixture.Migrate((source, target) =>
            {
                File.Copy(source, target);
                throw new IOException("Injected interruption after native copy.");
            });
            Check.Equal(fixture.Legacy, result.DirectoryPath); Check.False(result.Migrated);
            Check.True(result.Error?.Contains("Injected interruption") == true);
            Check.Equal("{\"HeaderText\":\"keep\"}", File.ReadAllText(fixture.Settings));
            Check.Equal("original shortcut", File.ReadAllText(Path.Combine(fixture.Legacy, "Shortcuts", "app.lnk")));
            Check.False(Directory.Exists(fixture.Destination));
            fixture.NoStage();
        });

        suite.Run("Malformed old JSON falls back without moving or repairing user data", () =>
        {
            using var fixture = new Fixture();
            foreach (string json in new[] { "broken-json", "[1,2]" })
            {
                fixture.Write("settings.json", json);
                var result = fixture.Migrate();
                Check.Equal(fixture.Legacy, result.DirectoryPath); Check.True(result.Error is not null);
                Check.Equal(json, File.ReadAllText(fixture.Settings));
                Check.False(Directory.Exists(fixture.Destination));
                fixture.NoStage();
            }
        });

        suite.Run("New directory user data remains and file collisions never overwrite either copy", () =>
        {
            using var fixture = new Fixture();
            fixture.Write("settings.json", "{}");
            fixture.Write("Shortcuts/app.lnk", "legacy application");
            Directory.CreateDirectory(Path.Combine(fixture.Destination, "Shortcuts"));
            string collision = Path.Combine(fixture.Destination, "Shortcuts", "app.lnk");
            File.WriteAllText(collision, "new unrelated application");
            var result = fixture.Migrate();
            Check.Equal(fixture.Legacy, result.DirectoryPath); Check.True(result.Error is not null);
            Check.Equal("new unrelated application", File.ReadAllText(collision));
            Check.False(File.Exists(fixture.NewSettings));
            File.Delete(collision);
            File.WriteAllText(Path.Combine(fixture.Destination, "unrelated.user"), "keep new data");
            result = fixture.Migrate();
            Check.True(result.Migrated);
            Check.Equal("keep new data", File.ReadAllText(Path.Combine(fixture.Destination, "unrelated.user")));
            Check.Equal("legacy application", File.ReadAllText(collision));
            fixture.NoStage();
        });

        suite.Run("Locked legacy settings return the old profile without creating partial data", () =>
        {
            using var fixture = new Fixture();
            fixture.Write("settings.json", "{}");
            using var locked = new FileStream(fixture.Settings, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = fixture.Migrate();
            Check.Equal(fixture.Legacy, result.DirectoryPath); Check.True(result.Error is not null);
            Check.False(Directory.Exists(fixture.Destination));
            fixture.NoStage();
        });

        suite.Run("Native profile copy retains named streams and readonly custom user files", () =>
        {
            using var fixture = new Fixture();
            fixture.Write("settings.json", "{}");
            fixture.Write("user.data", "ordinary data");
            File.WriteAllText(fixture.Settings + ":edock-migration-test", "settings alternate stream");
            string data = Path.Combine(fixture.Legacy, "user.data");
            File.WriteAllText(data + ":edock-migration-test", "custom alternate stream");
            File.SetAttributes(data, File.GetAttributes(data) | FileAttributes.ReadOnly);
            var result = fixture.Migrate();
            Check.True(result.Migrated); Check.True(result.Error is null);
            Check.Equal("settings alternate stream", File.ReadAllText(fixture.NewSettings + ":edock-migration-test"));
            string newData = Path.Combine(fixture.Destination, "user.data");
            Check.Equal("custom alternate stream", File.ReadAllText(newData + ":edock-migration-test"));
            Check.True((File.GetAttributes(newData) & FileAttributes.ReadOnly) != 0);
            Check.Equal("custom alternate stream", File.ReadAllText(data + ":edock-migration-test"));
        });

        if (encryptedParent is not null)
            suite.Run("EFS directory migration retains encrypted settings, shortcuts and ADS under the original encrypted parent", () =>
            {
                using var fixture = new Fixture(encryptedParent);
                string legacy = fixture.Legacy;
                Directory.CreateDirectory(legacy);
                try
                {
                    Check.True((File.GetAttributes(legacy) & FileAttributes.Encrypted) != 0);
                    string settings = Path.Combine(legacy, "settings.json");
                    string shortcut = Path.Combine(legacy, "Shortcuts", "app.lnk");
                    Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
                    File.WriteAllText(shortcut, "encrypted shell shortcut");
                    File.WriteAllText(settings, JsonSerializer.Serialize(new { TargetPath = shortcut }));
                    File.WriteAllText(settings + ":edock-migration-test", "encrypted settings stream");
                    File.WriteAllText(shortcut + ":edock-migration-test", "encrypted shortcut stream");
                    byte[] before = File.ReadAllBytes(settings);
                    Check.True((File.GetAttributes(fixture.Root) & FileAttributes.Encrypted) != 0);
                    var result = ConfigurationDirectoryMigration.ResolveFromDirectories(legacy, fixture.Destination);
                    if (!result.Migrated) throw new InvalidOperationException(result.Error);
                    Check.True(result.Migrated); Check.True(result.Error is null);
                    string newShortcut = Path.Combine(fixture.Destination, "Shortcuts", "app.lnk");
                    foreach (string path in new[] { fixture.NewSettings, newShortcut })
                        Check.True((File.GetAttributes(path) & FileAttributes.Encrypted) != 0);
                    Check.Equal("encrypted settings stream", File.ReadAllText(fixture.NewSettings + ":edock-migration-test"));
                    Check.Equal("encrypted shortcut stream", File.ReadAllText(newShortcut + ":edock-migration-test"));
                    Check.True(before.SequenceEqual(File.ReadAllBytes(settings)));
                    // Exercise the existing EFS save fallback after migration, too.
                    var store = new SettingsStore(fixture.Destination);
                    var loaded = store.Load(); loaded.HeaderText = "migrated settings still save";
                    store.Save(loaded);
                    Check.Equal("encrypted settings stream", File.ReadAllText(fixture.NewSettings + ":edock-migration-test"));
                    Check.True((File.GetAttributes(fixture.NewSettings) & FileAttributes.Encrypted) != 0);
                }
                finally { Directory.Delete(legacy, recursive: true); }
                fixture.NoStage();
            });
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; }
        internal string Legacy => Path.Combine(Root, "EdgeDock");
        internal string Destination => Path.Combine(Root, "EDock");
        internal string Settings => Path.Combine(Legacy, "settings.json");
        internal string NewSettings => Path.Combine(Destination, "settings.json");
        internal Fixture(string? parent = null)
        {
            Root = Path.Combine(parent ?? Path.GetTempPath(), "EDock.MigrationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        internal void Write(string relative, string content)
        {
            string path = Path.Combine(Legacy, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        internal ConfigurationDirectoryMigration.Result Migrate(Action<string, string>? copy = null) =>
            ConfigurationDirectoryMigration.ResolveFromDirectories(Legacy, Destination, copyFile: copy);
        internal void NoStage() => Check.False(Directory.EnumerateDirectories(Root, "EDock.migration-*").Any());
        public void Dispose()
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(Root, recursive: true);
        }
    }
}
