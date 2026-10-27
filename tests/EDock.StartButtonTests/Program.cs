using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using EDock.Services;
using Microsoft.UI.Dispatching;

static class Check
{
    static int passed;
    static readonly Type Type = typeof(StartButtonAppearanceService);
    static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Field(StartButtonAppearanceService service, string name, object value) => Type.GetField(name, Instance)!.SetValue(service, value);
    static T Value<T>(StartButtonAppearanceService service, string name) => (T)Type.GetField(name, Instance)!.GetValue(service)!;
    static object? Call(StartButtonAppearanceService service, string name, params object[] args) => Type.GetMethod(name, Instance)!.Invoke(service, args);
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
    static string ReadPath(MemoryMappedViewAccessor view, int offset)
    {
        var bytes = new byte[8192]; view.ReadArray(offset, bytes, 0, bytes.Length);
        return Encoding.Unicode.GetString(bytes).Split('\0')[0];
    }
    static (StartButtonAppearanceService, MemoryMappedViewAccessor) Create()
    {
        var service = new StartButtonAppearanceService(new DispatcherQueue());
        var mapping = MemoryMappedFile.CreateNew(null, 65536);
        var view = mapping.CreateViewAccessor();
        Field(service, "_mapping", mapping); Field(service, "_view", view);
        Field(service, "_enabled", true); Field(service, "_generation", 7);
        Field(service, "_injecting", true);
        return (service, view);
    }
    static readonly int[] MappingOffsets = { 36928, 37440, 37952, 38464 };
    static Array Prepare(StartButtonAppearanceService service, string[] paths) => (Array)Call(service, "PrepareImagePayloads", (object)paths)!;
    static void Commit(StartButtonAppearanceService service, Array payloads, string[] paths)
    {
        Call(service, "CommitImagePayloads", (object)payloads);
        Field(service, "_imagePath", paths[0]); Field(service, "_hoverImagePath", paths[1]);
        Field(service, "_pressedImagePath", paths[2]); Field(service, "_activatedImagePath", paths[3]);
        Call(service, "Publish", 0u);
    }
    static string[] MappingNames(MemoryMappedViewAccessor view) => MappingOffsets.Select(offset => ReadPath(view, offset)).ToArray();
    static bool Exists(string name)
    {
        try { using var mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read); return true; }
        catch (FileNotFoundException) { return false; }
    }
    static byte[] MappingBytes(string name, int length)
    {
        using var mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        using var view = mapping.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
        byte[] bytes = new byte[length]; view.ReadArray(0, bytes, 0, length); return bytes;
    }
    [DllImport("kernel32.dll")] static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool GetProcessHandleCount(nint process, out uint count);
    static uint HandleCount() { if (!GetProcessHandleCount(GetCurrentProcess(), out uint count)) throw new Exception("Cannot read process handle count"); return count; }
    public static void Main()
    {
        var (service, view) = Create();
        using (service)
        {
            int statusEvents = 0; service.StatusChanged += (_, _) => statusEvents++;
            Call(service, "BeginCheck");
            var timer = Value<DispatcherQueueTimer>(service, "_checkTimer");
            Assert(timer.IsRunning && timer.IsRepeating && timer.Interval.TotalMilliseconds == 200, "Acknowledgement checks repeat within the bounded window");
            var bytes = Encoding.Unicode.GetBytes("Image could not be loaded\0"); view.WriteArray(32832, bytes, 0, bytes.Length);
            view.Write(36, 7); view.Write(28, 0); view.Write(32, unchecked((int)0x8007000D));
            timer.Fire();
            Assert(!timer.IsRunning && service.LastError is not null && service.StatusText.Contains("8007000D") && service.StatusText.Contains("Image could not be loaded"), "Acknowledged image failure immediately stops applying and shows native detail");
            Assert(statusEvents == 2, "Applying and failure both notify the UI");
        }
        (service, view) = Create();
        using (service)
        {
            Call(service, "BeginCheck"); var timer = Value<DispatcherQueueTimer>(service, "_checkTimer");
            view.Write(36, 6); view.Write(32, unchecked((int)0x8007000D));
            timer.Fire();
            Assert(timer.IsRunning && service.LastError is null, "Previous-generation failures do not reject a new image");
            Field(service, "_deadline", Environment.TickCount64 - 1);
            timer.Fire();
            Assert(!timer.IsRunning && service.LastError is not null && !service.StatusText.Contains("8007000D"), "Missing acknowledgement reaches a terminal timeout without stale error text");
        }
        (service, view) = Create();
        using (service)
        {
            Call(service, "BeginCheck"); var timer = Value<DispatcherQueueTimer>(service, "_checkTimer");
            view.Write(36, 7); view.Write(28, 1); view.Write(32, 0); timer.Fire();
            Assert(!timer.IsRunning && service.LastError is null && service.StatusText.StartsWith("已应用"), "Successful replacement stops checking and reports success");
        }
        (service, view) = Create();
        using (service)
        {
            Field(service, "_imagePath", @"C:\normal.png"); Field(service, "_hoverImagePath", @"C:\hover.png");
            Field(service, "_pressedImagePath", @"C:\pressed.jpg"); Field(service, "_activatedImagePath", @"C:\activated.gif");
            Call(service, "Publish", 0u);
            Assert(ReadPath(view, 64) == @"C:\normal.png" && ReadPath(view, 8256) == @"C:\hover.png" && ReadPath(view, 16448) == @"C:\pressed.jpg" && ReadPath(view, 24640) == @"C:\activated.gif", "All four independent state paths reach their agreed ABI slots");
            Assert(view.ReadInt32(44) == 0 && view.ReadInt32(20) == 8, "Four-path publication finishes atomically with a new generation");
            service.Configure(false, "", 32);
            Assert(Value<MemoryMappedViewAccessor?>(service, "_view") is null && service.StatusText.StartsWith("已关闭"), "Disabling releases the mapping after restoring the published session");
        }
        string directory = Path.Combine(Path.GetTempPath(), "EDock.StartButtonServiceTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string[] paths = Enumerable.Range(0, 4).Select(i => Path.Combine(directory, $"state{i}.png")).ToArray();
            byte[][] images = Enumerable.Range(0, 4).Select(i => Enumerable.Range(0, 13 + i * 11).Select(j => (byte)(j + i)).ToArray()).ToArray();
            for (int i = 0; i < 4; i++) File.WriteAllBytes(paths[i], images[i]);
            (service, view) = Create();
            using (service)
            {
                var payloads = Prepare(service, paths); Commit(service, payloads, paths);
                string[] names = MappingNames(view);
                Assert(names.All(name => name.StartsWith($"Local\\EDock.StartButtonImage.v1.{Environment.ProcessId}.")) && names.Distinct().Count() == 4,
                    "Four image states publish independent process-owned unique mapping names at the native ABI offsets");
                Assert(Enumerable.Range(0, 4).All(i => view.ReadUInt32(38976 + 4 * i) == images[i].Length && MappingBytes(names[i], images[i].Length).SequenceEqual(images[i])),
                    "All four native ABI lengths and complete payload bytes match the source files");
                Field(service, "_size", 56);
                var resized = Prepare(service, paths); Commit(service, resized, paths);
                Assert(ReferenceEquals(payloads.GetValue(0), resized.GetValue(0)) && MappingNames(view).SequenceEqual(names) && Value<System.Collections.ICollection>(service, "_retiredImagePayloads").Count == 0,
                    "Changing icon size reuses image handles without creating or retiring image mappings");
                Call(service, "Publish", 0u);
                Assert(MappingNames(view).SequenceEqual(names), "Republishing for a refresh keeps the existing image mappings");
                Call(service, "BeginCheck"); var timer = Value<DispatcherQueueTimer>(service, "_checkTimer");
                view.Write(36, Value<int>(service, "_generation")); view.Write(28, 1); timer.Fire();
                Assert(names.All(Exists), "Successful acknowledgement retains current mappings for later taskbar button reconstruction");

                string replacement = Path.Combine(directory, "replacement.png"); File.WriteAllBytes(replacement, new byte[] { 13, 27, 42 });
                string[] replacedPaths = (string[])paths.Clone(); replacedPaths[0] = replacement;
                Commit(service, Prepare(service, replacedPaths), replacedPaths);
                string[] replacementNames = MappingNames(view);
                Assert(Exists(names[0]) && replacementNames[0] != names[0] && replacementNames.Skip(1).SequenceEqual(names.Skip(1)),
                    "Replacing one state keeps its old mapping alive while reusing the other three states");
                Call(service, "BeginCheck"); timer = Value<DispatcherQueueTimer>(service, "_checkTimer");
                view.Write(36, Value<int>(service, "_generation")); view.Write(28, 1); timer.Fire();
                Assert(!Exists(names[0]) && replacementNames.All(Exists), "New generation acknowledgement releases retired images while retaining all current images");

                // Configure must fail before publishing or connecting to Explorer.
                // This placeholder is used only by this isolated test binary.
                string helper = Path.Combine(AppContext.BaseDirectory, "Native", "EDock.StartButton.0.16.18.1.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(helper)!); File.WriteAllBytes(helper, new byte[] { 0 });
                string oversized = Path.Combine(directory, "oversized.png"); using (var file = File.Create(oversized)) file.SetLength(16 * 1024 * 1024 + 1);
                string firstValid = Path.Combine(directory, "first-valid.png"); File.WriteAllBytes(firstValid, new byte[] { 3, 4, 5 });
                int previousGeneration = view.ReadInt32(20);
                bool rejected = false;
                try { service.Configure(true, firstValid, 32, oversized, paths[2], paths[3]); }
                catch (InvalidDataException) { rejected = true; }
                Assert(rejected && view.ReadInt32(20) == previousGeneration && view.ReadInt32(44) == 0 && MappingNames(view).SequenceEqual(replacementNames) && replacementNames.All(Exists),
                    "A later image failing validation preserves the entire previously published configuration");
                _ = HandleCount();
                uint before = HandleCount();
                for (int attempt = 0; attempt < 64; attempt++)
                    try { service.Configure(true, firstValid, 32, oversized, paths[2], paths[3]); }
                    catch (InvalidDataException) { }
                Assert(HandleCount() <= before + 2 && Value<System.Collections.ICollection>(service, "_retiredImagePayloads").Count == 0,
                    "Repeated partial image preparation failures promptly release every new mapping handle");

                service.Configure(false, "", 32);
                Assert(replacementNames.All(name => !Exists(name)) && Value<MemoryMappedViewAccessor?>(service, "_view") is null,
                    "Disabling releases all current image mappings and the control channel");
            }

            (service, view) = Create();
            using (service)
            {
                string[] optional = { paths[0], "", "", "" };
                Commit(service, Prepare(service, optional), optional);
                string[] names = MappingNames(view);
                Assert(Exists(names[0]) && names.Skip(1).All(string.IsNullOrEmpty) && Enumerable.Range(1, 3).All(i => view.ReadUInt32(38976 + 4 * i) == 0),
                    "Empty optional states publish no mapping and zero length for native normal-image fallback");
                string empty = Path.Combine(directory, "empty.png"); File.WriteAllBytes(empty, Array.Empty<byte>());
                int previousGeneration = view.ReadInt32(20);
                bool rejected = false;
                try { service.Configure(true, empty, 32); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && view.ReadInt32(20) == previousGeneration && Exists(names[0]), "Empty image payloads are rejected without replacing the last valid image");
            }

            (service, view) = Create();
            using (service)
            {
                string[] currentPaths = { paths[0], "", "", "" };
                Commit(service, Prepare(service, currentPaths), currentPaths);
                var retiredNames = new List<string>();
                for (int change = 0; change < 4; change++)
                {
                    retiredNames.Add(MappingNames(view)[0]);
                    string next = Path.Combine(directory, $"unacknowledged{change}.png"); File.WriteAllBytes(next, new byte[] { (byte)change });
                    currentPaths[0] = next; Commit(service, Prepare(service, currentPaths), currentPaths);
                }
                string currentName = MappingNames(view)[0];
                int previousGeneration = view.ReadInt32(20);
                bool rejected = false;
                try { service.Configure(true, paths[0], 32); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected && view.ReadInt32(20) == previousGeneration && MappingNames(view)[0] == currentName
                    && retiredNames.All(Exists) && Value<System.Collections.ICollection>(service, "_retiredImagePayloads").Count == 4,
                    "Repeated changes without acknowledgement have a bounded retired backlog and refuse further publication safely");
                Call(service, "BeginCheck"); Field(service, "_deadline", Environment.TickCount64 - 1);
                Value<DispatcherQueueTimer>(service, "_checkTimer").Fire();
                Assert(retiredNames.All(name => !Exists(name)) && Exists(currentName)
                    && Value<System.Collections.ICollection>(service, "_retiredImagePayloads").Count == 0,
                    "A missing-acknowledgement timeout releases the retired backlog while retaining the current image");
                currentPaths[0] = paths[0]; Commit(service, Prepare(service, currentPaths), currentPaths);
                string newestName = MappingNames(view)[0];
                Call(service, "BeginCheck");
                view.Write(36, Value<int>(service, "_generation")); view.Write(28, 0); view.Write(32, unchecked((int)0x8007000D));
                Value<DispatcherQueueTimer>(service, "_checkTimer").Fire();
                Assert(!Exists(currentName) && Exists(newestName), "An acknowledged image failure also releases retired mappings without losing the current retry payload");
                currentPaths[0] = paths[1]; Commit(service, Prepare(service, currentPaths), currentPaths);
                string pendingName = MappingNames(view)[0];
                service.Configure(false, "", 32);
                Assert(!Exists(newestName) && !Exists(pendingName), "Disabling while a change is pending releases both current and retired image mappings");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine($"{passed} service checks passed.");
    }
}
