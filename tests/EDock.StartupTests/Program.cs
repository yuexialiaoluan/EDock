using System.Diagnostics;
using System.Runtime.InteropServices;
using EDock.Services;
using Microsoft.Win32;

// This executable is also the scheduled child used to verify the real process
// priority. It cannot run tests or edit registry/task state on that child path.
if (args.Contains(StartupService.LaunchArgument))
{
    StartupService.ApplyLaunchPriority();
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"priority-result-{Environment.ProcessId}.txt"),
        Process.GetCurrentProcess().PriorityClass.ToString());
    return 0;
}

const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
const string approvalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
const string executable = @"C:\Program Files\EDock\EDock.exe";
var fixturePath = @"Software\EDock.Tests\Startup-" + Guid.NewGuid().ToString("N");
var taskName = "EDock.Tests.Startup-" + Guid.NewGuid().ToString("N");
int passed = 0, total = 0;
using var fixture = Registry.CurrentUser.CreateSubKey(fixturePath, writable: true)!;
var fake = new FakeTasks();
try
{
    Run("no registration remains off and upgrade does not opt in", () =>
    {
        Check(!StartupService.IsEnabled(fixture, fake));
        StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable);
        Check(fake.RegisterCount == 0);
    });
    Run("old enabled Run is only removed after new task verification", () =>
    {
        SetRun("EdgeDock", @"""C:\Old Dock\EdgeDock.exe""");
        fake.OnRegister = () => Check(ReadRun("EdgeDock") is not null);
        StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable);
        Check(fake.State is { Enabled: true, IsCurrent: true });
        Check(ReadRun("EdgeDock") is null);
        Check(StartupService.IsEnabled(fixture, fake));
        fake.OnRegister = null;
    });
    Run("already current tasks are never rewritten", () =>
    {
        int before = fake.RegisterCount;
        StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable);
        StartupService.SetEnabled(fixture, fake, true, executable);
        Check(fake.RegisterCount == before);
    });
    Run("an explicit disable is durable during later startup migration", () =>
    {
        StartupService.SetEnabled(fixture, fake, false, executable);
        Check(!StartupService.IsEnabled(fixture, fake));
        int before = fake.RegisterCount;
        StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable);
        Check(fake.State is { Enabled: false } && fake.RegisterCount == before);
    });
    Run("Task Manager disabled legacy entries migrate without re-enabling", () =>
    {
        foreach (byte state in new byte[] { 3, 7 })
        {
            fake.State = null;
            SetRun("EdgeDock", @"""C:\Old Dock\EdgeDock.exe""");
            using var approval = fixture.CreateSubKey(approvalPath, writable: true)!;
            approval.SetValue("EdgeDock", new byte[] { state, 0, 0, 0 }, RegistryValueKind.Binary);
            Check(!StartupService.IsEnabled(fixture, fake));
            StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable);
            Check(fake.State is { Enabled: false });
            Check(approval.GetValue("EdgeDock") is null);
        }
    });
    Run("unrelated startup entries and their approval data survive", () =>
    {
        SetRun("OtherApp", @"""C:\Other\Other.exe""");
        using var approval = fixture.CreateSubKey(approvalPath, writable: true)!;
        approval.SetValue("OtherApp", new byte[] { 3, 4, 5, 6 }, RegistryValueKind.Binary);
        StartupService.SetEnabled(fixture, fake, true, executable);
        Check(ReadRun("OtherApp") is string command && command == @"""C:\Other\Other.exe""");
        Check(approval.GetValue("OtherApp") is byte[] bytes && bytes.SequenceEqual(new byte[] { 3, 4, 5, 6 }));
    });
    Run("registration failure preserves legacy Run and disabled markers", () =>
    {
        fake.State = null;
        fake.ThrowRegister = true;
        SetRun("EdgeDock", @"""C:\Old Dock\EdgeDock.exe""");
        using var approval = fixture.CreateSubKey(approvalPath, writable: true)!;
        approval.SetValue("EdgeDock", new byte[] { 3, 0, 0, 0 }, RegistryValueKind.Binary);
        Throws(() => StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable));
        Check(ReadRun("EdgeDock") is not null && approval.GetValue("EdgeDock") is byte[] { Length: 4 });
        Check(fake.State is null);
        fake.ThrowRegister = false;
    });
    Run("read-back verification failure restores previous task and old Run", () =>
    {
        var previous = new StartupTaskRegistration(@"C:\Old Dock\EDock.exe", false, true, false, "previous-xml");
        fake.State = previous;
        fake.CorruptRegistration = true;
        Throws(() => StartupService.UpgradeRegistrationIfNeeded(fixture, fake, executable));
        Check(fake.State == previous && ReadRun("EdgeDock") is not null);
        fake.CorruptRegistration = false;
    });
    Run("foreign task names cannot be overwritten", () =>
    {
        var foreign = new StartupTaskRegistration(executable, true, false, false, "foreign-xml");
        fake.State = foreign;
        int before = fake.RegisterCount;
        Throws(() => StartupService.SetEnabled(fixture, fake, true, executable));
        Check(fake.State == foreign && fake.RegisterCount == before);
    });
    Run("invalid executable paths are rejected before any registration", () =>
    {
        fake.State = null;
        foreach (var path in new[] { "", "EDock.exe", "C:\\EDock\\bad\"path.exe", @"C:\EDock\file.txt" })
            Throws(() => StartupService.SetEnabled(fixture, fake, true, path));
        Check(fake.State is null);
    });
    Run("a same-name foreign Run command and its approval are not removed", () =>
    {
        SetRun("EdgeDock", @"""C:\Other\Other.exe""");
        using var approval = fixture.CreateSubKey(approvalPath, writable: true)!;
        approval.SetValue("EdgeDock", new byte[] { 3, 4, 5, 6 }, RegistryValueKind.Binary);
        StartupService.SetEnabled(fixture, fake, true, executable);
        Check(ReadRun("EdgeDock") is string command && command == @"""C:\Other\Other.exe""");
        Check(approval.GetValue("EdgeDock") is byte[] bytes && bytes.SequenceEqual(new byte[] { 3, 4, 5, 6 }));
    });
    Run("real interactive logon task validates without elevation", () =>
    {
        using var tasks = new ScheduledStartupTaskStore(taskName);
        Check(tasks.Read() is null);
        string appHost = Path.ChangeExtension(typeof(FakeTasks).Assembly.Location, ".exe");
        Check(File.Exists(appHost));
        tasks.Register(appHost, true);
        var registered = tasks.Read();
        if (registered is not { Enabled: true, IsCurrent: true, IsOwned: true })
            throw new InvalidOperationException("Task validation failed: " + registered);
        Check(registered!.Executable == appHost);
        Console.WriteLine("  verified logon trigger: current user, PT0S delay, interactive token, no elevation, battery allowed");
    });
    Run("scheduled child actually runs at High priority", () =>
    {
        var previous = Directory.GetFiles(AppContext.BaseDirectory, "priority-result-*.txt").ToHashSet();
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        dynamic? folder = null, task = null, running = null;
        try
        {
            service.Connect();
            folder = service.GetFolder(@"\");
            task = folder.GetTask(taskName);
            running = task.Run(null);
            var timer = Stopwatch.StartNew();
            string? result = null;
            while (timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                result = Directory.GetFiles(AppContext.BaseDirectory, "priority-result-*.txt").FirstOrDefault(path => !previous.Contains(path));
                if (result is not null && File.ReadAllText(result).Length > 0) break;
                Thread.Sleep(100);
            }
            Check(result is not null);
            string priority = File.ReadAllText(result!);
            File.Delete(result!);
            Check(priority == "High");
            Console.WriteLine($"  actual child PriorityClass={priority}");
        }
        finally
        {
            if (running is not null) Marshal.FinalReleaseComObject(running);
            if (task is not null) Marshal.FinalReleaseComObject(task);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(service);
        }
    });
    Run("real task disabling and re-enabling is reflected immediately", () =>
    {
        using var tasks = new ScheduledStartupTaskStore(taskName);
        string appHost = Path.ChangeExtension(typeof(FakeTasks).Assembly.Location, ".exe");
        tasks.Register(appHost, false);
        Check(tasks.Read() is { Enabled: false, IsCurrent: true });
        tasks.Register(appHost, true);
        Check(tasks.Read() is { Enabled: true, IsCurrent: true });
    });
    Run("real task can restore a prior disabled registration", () =>
    {
        using var tasks = new ScheduledStartupTaskStore(taskName);
        string appHost = Path.ChangeExtension(typeof(FakeTasks).Assembly.Location, ".exe");
        tasks.Register(appHost, false);
        var disabled = tasks.Read()!;
        tasks.Register(appHost, true);
        tasks.Restore(disabled);
        Check(tasks.Read() is { Enabled: false, IsCurrent: true });
    });
    Console.WriteLine($"{passed}/{total} startup tests passed. All registry and task writes were isolated fixtures.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(fixturePath, throwOnMissingSubKey: false);
    using var cleanup = new ScheduledStartupTaskStore(taskName);
    cleanup.Restore(null);
}

void Run(string name, Action test)
{
    total++;
    test();
    passed++;
    Console.WriteLine($"PASS {name}");
}

void SetRun(string name, string command)
{
    using var run = fixture.CreateSubKey(runPath, writable: true)!;
    run.SetValue(name, command);
}

object? ReadRun(string name)
{
    using var run = fixture.OpenSubKey(runPath);
    return run?.GetValue(name);
}

static void Throws(Action action)
{
    try { action(); } catch (IOException) { return; }
    throw new InvalidOperationException("Expected IOException.");
}

static void Check(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed.");
}

sealed class FakeTasks : IStartupTaskStore
{
    public StartupTaskRegistration? State;
    public int RegisterCount;
    public bool ThrowRegister, CorruptRegistration;
    public Action? OnRegister;
    public StartupTaskRegistration? Read() => State;
    public void Register(string executable, bool enabled)
    {
        OnRegister?.Invoke();
        RegisterCount++;
        if (ThrowRegister) throw new IOException("Injected registration failure.");
        State = new(executable, enabled, true, !CorruptRegistration, "new-xml");
    }
    public void Restore(StartupTaskRegistration? registration) => State = registration;
}
