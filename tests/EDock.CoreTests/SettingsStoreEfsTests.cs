using System.Security.AccessControl;
using EDock.Models;
using EDock.Services;

static class SettingsStoreEfsTests
{
    private const int FileNotEncrypted = unchecked((int)0x80071777);

    public static void Register(CoreSuite suite, string encryptedParent)
    {
        suite.Run("EFS ERROR_FILE_NOT_ENCRYPTED uses atomic fallback and retains metadata", () =>
            WithFixture(encryptedParent, directory =>
            {
                var initial = new SettingsStore(directory);
                initial.Save(new DockSettings { HideDelayMs = 701 });
                string path = initial.SettingsFilePath;
                var original = new FileInfo(path);
                original.Attributes |= FileAttributes.NotContentIndexed;
                File.WriteAllText(path + ":edgedock-test-metadata", "keep this stream");
                var attributes = File.GetAttributes(path);
                DateTime created = original.CreationTimeUtc;
                string access = AccessRules(path);
                int replaceAttempts = 0;
                var store = new SettingsStore(directory, (temporary, destination) =>
                {
                    replaceAttempts++;
                    Check.Equal(path, destination);
                    RequireEncrypted(path);
                    RequireEncrypted(temporary);
                    Check.Equal("keep this stream", File.ReadAllText(temporary + ":edgedock-test-metadata"));
                    throw new IOException("Injected exact EFS metadata merge failure.", FileNotEncrypted);
                });
                for (int i = 0; i < 3; i++)
                {
                    store.Save(new DockSettings { HideDelayMs = 702 + i });
                    Check.True(store.LastError is null);
                    Check.Equal(702 + i, new SettingsStore(directory).Load().HideDelayMs);
                    RequireEncrypted(path);
                    Check.Equal(attributes, File.GetAttributes(path));
                    Check.Equal(created, File.GetCreationTimeUtc(path));
                    Check.Equal(access, AccessRules(path));
                    Check.Equal("keep this stream", File.ReadAllText(path + ":edgedock-test-metadata"));
                    Check.Equal(1, Directory.EnumerateFiles(directory).Count());
                }
                Check.Equal(3, replaceAttempts);
            }));

        suite.Run("EFS fallback rejects unrelated IO errors without replacing prior settings", () =>
            WithFixture(encryptedParent, directory =>
            {
                var initial = new SettingsStore(directory);
                initial.Save(new DockSettings { HideDelayMs = 801 });
                string path = initial.SettingsFilePath;
                RequireEncrypted(path);
                byte[] previous = File.ReadAllBytes(path);
                foreach (int error in new[]
                {
                    unchecked((int)0x80070020), // Sharing violation.
                    unchecked((int)0x80070070), // Disk full.
                    unchecked((int)0x80071776), // Another EFS error.
                    unchecked((int)0x80131777)  // Same low word, different HRESULT facility.
                })
                {
                    var failure = new IOException("Injected unrelated IO failure.", error);
                    var store = new SettingsStore(directory, (temporary, _) =>
                    {
                        RequireEncrypted(temporary);
                        throw failure;
                    });
                    ExpectFailure(store, failure);
                    Check.True(previous.SequenceEqual(File.ReadAllBytes(path)));
                    Check.Equal(1, Directory.EnumerateFiles(directory).Count());
                }
            }));

        suite.Run("Exact EFS error does not enable fallback for unencrypted settings", () =>
            WithFixture(Path.GetTempPath(), directory =>
            {
                var initial = new SettingsStore(directory);
                initial.Save(new DockSettings { HideDelayMs = 901 });
                string path = initial.SettingsFilePath;
                Check.False((File.GetAttributes(path) & FileAttributes.Encrypted) != 0);
                byte[] previous = File.ReadAllBytes(path);
                var failure = new IOException("Injected exact EFS error on unencrypted settings.", FileNotEncrypted);
                var store = new SettingsStore(directory, (temporary, _) =>
                {
                    Check.False((File.GetAttributes(temporary) & FileAttributes.Encrypted) != 0);
                    throw failure;
                });
                ExpectFailure(store, failure);
                Check.True(previous.SequenceEqual(File.ReadAllBytes(path)));
                Check.Equal(1, Directory.EnumerateFiles(directory).Count());
            }, requireEncryption: false));

        suite.Run("EFS exact error rejects a target made read only before commit", () =>
            WithFixture(encryptedParent, directory =>
            {
                var initial = new SettingsStore(directory);
                initial.Save(new DockSettings { HideDelayMs = 1001 });
                string path = initial.SettingsFilePath;
                byte[] previous = File.ReadAllBytes(path);
                var attributes = File.GetAttributes(path);
                var failure = new IOException("Injected exact EFS error with protected target.", FileNotEncrypted);
                try
                {
                    var store = new SettingsStore(directory, (temporary, _) =>
                    {
                        RequireEncrypted(temporary);
                        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                        throw failure;
                    });
                    ExpectFailure(store, failure);
                    Check.True(previous.SequenceEqual(File.ReadAllBytes(path)));
                    Check.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
                    Check.Equal(1, Directory.EnumerateFiles(directory).Count());
                }
                finally { File.SetAttributes(path, attributes); }
            }));
    }

    private static void ExpectFailure(SettingsStore store, IOException expected)
    {
        try { store.Save(new DockSettings { HideDelayMs = 9999 }); }
        catch (IOException error)
        {
            Check.True(ReferenceEquals(expected, error.InnerException));
            Check.True(store.LastError?.Contains($"0x{expected.HResult:X8}") == true);
            return;
        }
        throw new InvalidOperationException("Expected the original save failure.");
    }

    private static void RequireEncrypted(string path) =>
        Check.True((File.GetAttributes(path) & FileAttributes.Encrypted) != 0);

    private static string AccessRules(string path) => new FileInfo(path)
        .GetAccessControl(AccessControlSections.Access)
        .GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    private static void WithFixture(string fixtureParent, Action<string> test, bool requireEncryption = true)
    {
        string parent = Path.GetFullPath(fixtureParent);
        if (requireEncryption) RequireEncrypted(parent);
        string directory = Path.Combine(parent, "edgedock-test-" + Guid.NewGuid().ToString("N"));
        Check.True(Path.GetFullPath(directory).StartsWith(Path.TrimEndingDirectorySeparator(parent)
            + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
