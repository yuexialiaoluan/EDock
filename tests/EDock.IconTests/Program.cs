using System.Security.Cryptography;
using System.Runtime.InteropServices;
using EDock.Services;

internal static class Program
{
    private static int _failures;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "work", "EDock-IconTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Run("Nonexistent link returns no target or PIDL", () =>
            {
                string missing = Path.Combine(folder, "missing.lnk");
                Require(ShortcutIconMetadata.ReadLinkTarget(missing).Length == 0);
                Require(ShortcutIconMetadata.ReadLinkIconTarget(missing) == (string.Empty, string.Empty));
                Require(ShortcutIconMetadata.ReadLinkTargetIdList(missing) == IntPtr.Zero);
                Require(NativeShellIcon.Extract(missing, 64, true) is null);
            });
            Run("Internet shortcut retains Steam URL and original local icon", () =>
            {
                string icon = Path.Combine(folder, "中文,图标.ico");
                File.WriteAllBytes(icon, new byte[] { 0 });
                string path = Path.Combine(folder, "Game.url");
                File.WriteAllText(path, "[InternetShortcut]\nURL=steam://rungameid/12345\nIconFile=" + icon + "\nIconIndex=0\n", new System.Text.UTF8Encoding(false));
                Require(ShortcutIconMetadata.ReadInternetShortcutTarget(path) == "steam://rungameid/12345");
                Require(ShortcutIconMetadata.ResolveOriginal(path) == icon + ",0");
                var imported = ShortcutService.ImportPath(path);
                Require(imported.TargetPath == path);
            });
            Run("Unsafe or unknown protocol is never resolved", () =>
            {
                Require(ShortcutIconMetadata.ReadProtocolIconCandidates("file:///C:/Windows/notepad.exe").Count == 0);
                Require(ShortcutIconMetadata.ReadProtocolIconCandidates("https://example.com").Count == 0);
                Require(ShortcutIconMetadata.ReadProtocolIconCandidates("unknown-edgedock-test://123").Count == 0);
            });
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
            string desktopLink = Path.Combine(folder, "Ordinary desktop application.lnk");
            Run("Create isolated ordinary application shortcut", () =>
            {
                Require(File.Exists(executable));
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                object? shortcut = null;
                try
                {
                    shortcut = ((dynamic)shell).CreateShortcut(desktopLink);
                    ((dynamic)shortcut).TargetPath = executable;
                    ((dynamic)shortcut).Save();
                }
                finally
                {
                    if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
                    Marshal.FinalReleaseComObject(shell);
                }
                Require(File.Exists(desktopLink));
            });
            Run("Ordinary file shortcut keeps its original target and icon fallback order", () =>
            {
                string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(desktopLink)));
                var iconTarget = ShortcutIconMetadata.ReadLinkIconTarget(desktopLink);
                Require(iconTarget.Target == executable && iconTarget.ExecutablePath.Length == 0);
                Require(ShortcutIconMetadata.ReadLinkTarget(desktopLink) == iconTarget.Target);
                Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(desktopLink))) == before);
            });
            foreach ((string path, bool targetIdList) in new[] { (executable, false), (desktopLink, true) })
            {
                var reference = await Task.Run(() => ShellIconReference.Extract(path, 64, 0x4, targetIdList));
                var actual = await Task.Run(() => NativeShellIcon.Extract(path, 64, targetIdList));
                Run("Ordinary application keeps original Shell appearance: " + Path.GetFileName(path), () =>
                {
                    Require(reference is not null && actual is not null);
                    MatchReference(actual!, reference!);
                });
            }
            for (int argument = 0; argument < args.Length; argument++)
            {
                bool app = args[argument] == "--app-icon-shortcut";
                bool desktopApp = args[argument] == "--desktop-app-shortcut";
                if (!app && !desktopApp && args[argument] != "--installed-shortcut") continue;
                if (++argument >= args.Length) throw new ArgumentException("A shortcut path must follow the selected option.");
                string installedPath = Path.GetFullPath(args[argument]);
                if (desktopApp)
                {
                    Run("AppsFolder desktop identity exposes an existing executable without rewriting the shortcut", () =>
                    {
                        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installedPath)));
                        string originalTarget = ShortcutIconMetadata.ReadLinkTarget(installedPath);
                        var iconTarget = ShortcutIconMetadata.ReadLinkIconTarget(installedPath);
                        Require(iconTarget.Target == originalTarget);
                        Require(File.Exists(iconTarget.ExecutablePath) && Path.IsPathFullyQualified(iconTarget.ExecutablePath));
                        Require(Path.GetExtension(iconTarget.ExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase));
                        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installedPath))) == before);
                        Console.WriteLine("  identity=" + iconTarget.Target + " iconExe=" + iconTarget.ExecutablePath);
                    });
                }
                if (app)
                {
                    Run("Packaged app keeps its original identity for registered logo discovery", () =>
                    {
                        string originalTarget = ShortcutIconMetadata.ReadLinkTarget(installedPath);
                        var iconTarget = ShortcutIconMetadata.ReadLinkIconTarget(installedPath);
                        Require(originalTarget.Length > 0 && iconTarget.Target == originalTarget);
                    });
                }
                var sizes = app ? new[] { 40, 60, 64, 128, 256 } : new[] { 64, 256 };
                foreach (int size in sizes)
                {
                    var reference = await Task.Run(() => ShellIconReference.Extract(installedPath, size, 0x4, true));
                    var actual = await Task.Run(() => NativeShellIcon.Extract(installedPath, size, true));
                    Run("Installed target preserves original Shell pixels at " + size + "px: " + Path.GetFileName(installedPath), () =>
                    {
                        Require(File.Exists(installedPath));
                        Require(reference is not null && actual is not null);
                        MatchReference(actual!, reference!);
                    });
                }
                if (app)
                {
                    string appId = ShortcutIconMetadata.ReadLinkTarget(installedPath);
                    string appsFolderPath = "shell:AppsFolder\\" + appId;
                    var reference = await Task.Run(() => ShellIconReference.Extract(appsFolderPath, 64, 0x4, false));
                    foreach (string candidate in new[] { appId, appsFolderPath })
                    {
                        var actual = await Task.Run(() => NativeShellIcon.Extract(candidate, 64));
                        Run("Direct application target retains original Shell appearance: " + candidate, () =>
                        {
                            Require(appId.Length > 0 && reference is not null && actual is not null);
                            MatchReference(actual!, reference!);
                        });
                    }
                }
            }
            if (args.Contains("--installed-shortcuts"))
            {
                var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) };
                var names = new[] { "ChatGPT.lnk", "炉石传说.lnk", "TapTap.lnk", "Blender 5.2.lnk" };
                var hashes = new HashSet<string>();
                foreach (string name in names)
                {
                    string[] paths = roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, name,
                        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })).ToArray();
                    Run("Installed shortcut exists: " + name, () => Require(paths.Length > 0));
                    foreach (string path in paths)
                    {
                        // Run on the worker apartment used by the real icon loader.
                        var pixels = await Task.Run(() => NativeShellIcon.Extract(path, 256, true));
                        Console.WriteLine("  pixels=" + (pixels is null ? "null" : pixels.Width + "x" + pixels.Height
                            + " colors=" + pixels.Data.Where((_, index) => index % 4 != 3).Distinct().Count()
                            + " alpha=" + pixels.Data.Where((_, index) => index % 4 == 3).Distinct().Count()));
                        Run("Native target PIDL icon: " + path, () =>
                        {
                            Require(pixels is not null);
                            Require(pixels!.Width > 0 && pixels.Height > 0);
                            Require(pixels.Data.Length == pixels.Width * pixels.Height * 4);
                            Require(pixels.Data.Where((_, index) => index % 4 != 3).Distinct().Count() > 1);
                            Require(Enumerable.Range(0, pixels.Width * pixels.Height).All(i =>
                                pixels.Data[i * 4] <= pixels.Data[i * 4 + 3] && pixels.Data[i * 4 + 1] <= pixels.Data[i * 4 + 3]
                                && pixels.Data[i * 4 + 2] <= pixels.Data[i * 4 + 3]));
                            hashes.Add(Convert.ToHexString(SHA256.HashData(pixels.Data)));
                            var imported = ShortcutService.ImportPath(path);
                            Require(imported.TargetPath == path);
                            Console.WriteLine("  target=" + ShortcutIconMetadata.ReadLinkTarget(path) + " size=" + pixels.Width + "x" + pixels.Height);
                        });
                    }
                }
                Run("Four products produce distinct pixel buffers (visual identity checked separately)", () => Require(hashes.Count >= 4));
            }
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("Failures: " + _failures);
        return _failures == 0 ? 0 : 1;
    }

    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
    private static void MatchReference(NativeShellIcon.Pixels actual, ShellIconReference.Image expected)
    {
        Require(actual.Width == expected.Width && actual.Height == expected.Height);
        // Transparent Shell DIBs vary between straight and premultiplied RGB;
        // alpha and opaque colors are invariant under either representation.
        // Checking those independently catches unwanted native tile backgrounds
        // without duplicating the product's existing alpha-normalization rules.
        for (int pixel = 0; pixel < actual.Data.Length; pixel += 4)
        {
            Require(actual.Data[pixel + 3] == expected.Data[pixel + 3]);
            if (expected.Data[pixel + 3] != 255) continue;
            for (int channel = 0; channel < 3; channel++)
                Require(actual.Data[pixel + channel] == expected.Data[pixel + channel]);
        }
        Require(actual.Data.Where((_, i) => i % 4 != 3).Distinct().Count() > 1);
    }
    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { ++_failures; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
}
