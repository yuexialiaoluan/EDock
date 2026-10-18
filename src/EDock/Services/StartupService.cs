using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace EDock.Services;

public static class StartupService
{
    internal const string LaunchArgument = "--startup-high-priority";
    internal const string TaskDescription = "EDock current-user startup (managed by EDock)";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private static readonly string[] LegacyNames = ["EDock", "EdgeDock"];
    private static readonly object Sync = new();

    // High-priority Scheduler definitions require extra privileges. An ordinary
    // interactive task launches immediately; its application can raise its own
    // priority without elevation. Manual launches retain their normal priority.
    public static void ApplyLaunchPriority()
    {
        if (!Environment.GetCommandLineArgs().Contains(LaunchArgument, StringComparer.Ordinal)) return;
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.High;
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    public static bool IsEnabled()
    {
        lock (Sync)
        {
            using var tasks = new ScheduledStartupTaskStore();
            return IsEnabled(Registry.CurrentUser, tasks);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        lock (Sync)
        {
            using var tasks = new ScheduledStartupTaskStore();
            SetEnabled(Registry.CurrentUser, tasks, enabled, ExecutablePath());
        }
    }

    public static void UpgradeRegistrationIfNeeded()
    {
        lock (Sync)
        {
            using var tasks = new ScheduledStartupTaskStore();
            UpgradeRegistrationIfNeeded(Registry.CurrentUser, tasks, ExecutablePath());
        }
    }

    internal static bool IsEnabled(RegistryKey registryRoot, IStartupTaskStore tasks)
    {
        var task = tasks.Read();
        if (task is { IsOwned: true }) return task.Enabled;
        return ReadLegacyState(registryRoot).Enabled;
    }

    internal static void SetEnabled(RegistryKey registryRoot, IStartupTaskStore tasks, bool enabled, string executable)
    {
        ValidateExecutable(executable);
        UpdateRegistration(registryRoot, tasks, executable, enabled);
    }

    internal static void UpgradeRegistrationIfNeeded(RegistryKey registryRoot, IStartupTaskStore tasks, string executable)
    {
        ValidateExecutable(executable);
        var task = tasks.Read();
        var legacy = ReadLegacyState(registryRoot);
        if (task is null && !legacy.Exists) return;
        if (task is { IsOwned: true, IsCurrent: true } &&
            string.Equals(task.Executable, executable, StringComparison.OrdinalIgnoreCase) &&
            !legacy.Exists && legacy.Values.Count == 0) return;
        UpdateRegistration(registryRoot, tasks, executable, task is { IsOwned: true } ? task.Enabled : legacy.Enabled);
    }

    private static void UpdateRegistration(RegistryKey registryRoot, IStartupTaskStore tasks, string executable, bool enabled)
    {
        var previous = tasks.Read();
        if (previous is { IsOwned: false }) throw new IOException("系统中已有其他程序使用 EDock 启动任务名称，无法覆盖。");
        var legacy = ReadLegacyState(registryRoot);
        // Leave an unchanged task's registration time and Windows state intact.
        if (previous is { IsCurrent: true } && previous.Enabled == enabled &&
            string.Equals(previous.Executable, executable, StringComparison.OrdinalIgnoreCase))
        {
            RemoveLegacyValues(registryRoot, legacy);
            return;
        }
        try
        {
            tasks.Register(executable, enabled);
            var committed = tasks.Read();
            if (committed is not { IsOwned: true, IsCurrent: true } || committed.Enabled != enabled ||
                !string.Equals(committed.Executable, executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("新的 EDock 开机启动设置未能通过验证。");
            // Keep the original Run command until the replacement has been
            // registered and read back successfully.
            RemoveLegacyValues(registryRoot, legacy);
        }
        catch (Exception failure)
        {
            var recoveryErrors = new List<Exception>();
            try { RestoreLegacyValues(registryRoot, legacy); } catch (Exception ex) { recoveryErrors.Add(ex); }
            try { tasks.Restore(previous); } catch (Exception ex) { recoveryErrors.Add(ex); }
            if (recoveryErrors.Count > 0)
                throw new IOException("无法更新 EDock 的开机启动设置；恢复原设置时也发生了错误。", new AggregateException([failure, .. recoveryErrors]));
            throw new IOException("无法更新 EDock 的开机启动设置，原启动设置已保留。", failure);
        }
    }

    private static string ExecutablePath() => Environment.ProcessPath ?? throw new IOException("无法找到应用程序路径。");

    private static void ValidateExecutable(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("应用程序路径无效。");
    }

    private static LegacyStartupState ReadLegacyState(RegistryKey root)
    {
        var values = new List<LegacyValue>();
        bool exists = false, enabled = false;
        using var run = root.OpenSubKey(RunKey);
        using var approval = root.OpenSubKey(ApprovalKey);
        foreach (var name in LegacyNames)
        {
            if (run?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command && IsOurLegacyCommand(command))
            {
                var disabled = approval?.GetValue(name) is byte[] { Length: > 0 } state && state[0] is 3 or 7;
                enabled |= !disabled;
                exists = true;
                values.Add(new(RunKey, name, command, run.GetValueKind(name)));
                if (approval?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is { } approvalValue)
                    values.Add(new(ApprovalKey, name, approvalValue, approval.GetValueKind(name)));
            }
        }
        return new(exists, enabled, values);
    }

    private static bool IsOurLegacyCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var path = command.StartsWith('"')
            ? command[1..].Split('"', 2)[0]
            : command.Split(' ', 2)[0];
        return Path.GetFileName(path) is string file &&
            (file.Equals("EDock.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("EdgeDock.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static void RemoveLegacyValues(RegistryKey root, LegacyStartupState legacy)
    {
        try
        {
            foreach (var group in legacy.Values.GroupBy(value => value.Path))
            {
                using var key = root.OpenSubKey(group.Key, writable: true);
                foreach (var value in group) key?.DeleteValue(value.Name, throwOnMissingValue: false);
            }
        }
        catch
        {
            RestoreLegacyValues(root, legacy);
            throw;
        }
    }

    private static void RestoreLegacyValues(RegistryKey root, LegacyStartupState legacy)
    {
        foreach (var group in legacy.Values.GroupBy(value => value.Path))
        {
            using var key = root.CreateSubKey(group.Key, writable: true) ?? throw new IOException("无法恢复原开机启动设置。");
            foreach (var value in group) key.SetValue(value.Name, value.Value, value.Kind);
        }
    }

    private sealed record LegacyValue(string Path, string Name, object Value, RegistryValueKind Kind);
    private sealed record LegacyStartupState(bool Exists, bool Enabled, List<LegacyValue> Values);
}

internal sealed record StartupTaskRegistration(string Executable, bool Enabled, bool IsOwned, bool IsCurrent, string Xml);

internal interface IStartupTaskStore
{
    StartupTaskRegistration? Read();
    void Register(string executable, bool enabled);
    void Restore(StartupTaskRegistration? registration);
}

internal sealed class ScheduledStartupTaskStore : IStartupTaskStore, IDisposable
{
    private readonly List<object> _comObjects = [];
    private readonly string _name;
    private readonly dynamic _service;
    private readonly dynamic _folder;
    private readonly string _userId;
    private readonly string _userName;

    internal ScheduledStartupTaskStore(string name = "EDock")
    {
        _name = name;
        using var identity = WindowsIdentity.GetCurrent();
        _userId = identity.User!.Value;
        _userName = identity.Name;
        try
        {
            _service = Track(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!);
            _service.Connect();
            _folder = Track(_service.GetFolder(@"\"));
        }
        catch { Dispose(); throw; }
    }

    public StartupTaskRegistration? Read()
    {
        dynamic task;
        try { task = Track(_folder.GetTask(_name)); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
        dynamic definition = Track(task.Definition);
        dynamic registration = Track(definition.RegistrationInfo);
        bool owned = (string)registration.Description == StartupService.TaskDescription;
        dynamic actions = Track(definition.Actions);
        if ((int)actions.Count != 1) return new("", (bool)task.Enabled, owned, false, (string)task.Xml);
        dynamic action = Track(actions[1]);
        if ((int)action.Type != 0) return new("", (bool)task.Enabled, owned, false, (string)task.Xml);
        string path = (string)action.Path;
        dynamic settings = Track(definition.Settings);
        dynamic principal = Track(definition.Principal);
        dynamic triggers = Track(definition.Triggers);
        bool logonMatches = false;
        if ((int)triggers.Count == 1)
        {
            dynamic trigger = Track(triggers[1]);
            string delay = (string)trigger.Delay;
            logonMatches = (int)trigger.Type == 9 && MatchesCurrentUser((string)trigger.UserId) &&
                (string.IsNullOrEmpty(delay) || delay == "PT0S") && (bool)trigger.Enabled;
        }
        bool current = owned && (string)action.Arguments == StartupService.LaunchArgument &&
            string.Equals((string)action.WorkingDirectory, Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase) &&
            MatchesCurrentUser((string)principal.UserId) && (int)principal.LogonType == 3 && (int)principal.RunLevel == 0 &&
            (int)settings.Priority == 4 && !(bool)settings.DisallowStartIfOnBatteries && !(bool)settings.StopIfGoingOnBatteries &&
            !(bool)settings.RunOnlyIfIdle && !(bool)settings.RunOnlyIfNetworkAvailable && (bool)settings.AllowDemandStart &&
            (string)settings.ExecutionTimeLimit == "PT0S" && (int)settings.MultipleInstances == 2 && logonMatches;
        return new(path, (bool)task.Enabled, owned, current, (string)task.Xml);
    }

    public void Register(string executable, bool enabled)
    {
        dynamic definition = Track(_service.NewTask(0));
        dynamic registration = Track(definition.RegistrationInfo);
        registration.Description = StartupService.TaskDescription;
        dynamic principal = Track(definition.Principal);
        principal.UserId = _userId;
        principal.LogonType = 3;
        principal.RunLevel = 0;
        dynamic settings = Track(definition.Settings);
        settings.Enabled = enabled;
        settings.Priority = 4;
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.RunOnlyIfIdle = false;
        settings.RunOnlyIfNetworkAvailable = false;
        settings.AllowDemandStart = true;
        settings.StartWhenAvailable = true;
        settings.ExecutionTimeLimit = "PT0S";
        settings.MultipleInstances = 2;
        dynamic triggers = Track(definition.Triggers);
        dynamic trigger = Track(triggers.Create(9));
        trigger.UserId = _userId;
        trigger.Delay = "PT0S";
        trigger.Enabled = true;
        dynamic actions = Track(definition.Actions);
        dynamic action = Track(actions.Create(0));
        action.Path = executable;
        action.Arguments = StartupService.LaunchArgument;
        action.WorkingDirectory = Path.GetDirectoryName(executable);
        Track(_folder.RegisterTaskDefinition(_name, definition, 6, null, null, 3, null));
    }

    public void Restore(StartupTaskRegistration? registration)
    {
        if (registration is not null)
            Track(_folder.RegisterTask(_name, registration.Xml, 6, null, null, 3, null));
        else
        {
            try { _folder.DeleteTask(_name, 0); }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { }
        }
    }

    private object Track(object value)
    {
        if (!_comObjects.Contains(value, ReferenceEqualityComparer.Instance)) _comObjects.Add(value);
        return value;
    }

    private bool MatchesCurrentUser(string user)
    {
        if (user.Equals(_userId, StringComparison.OrdinalIgnoreCase) || user.Equals(_userName, StringComparison.OrdinalIgnoreCase)) return true;
        // Scheduler COM may return an unqualified local account name while its
        // XML contains the SID. Resolve it rather than comparing display names.
        try { return new NTAccount(user).Translate(typeof(SecurityIdentifier)).Value.Equals(_userId, StringComparison.OrdinalIgnoreCase); }
        catch (IdentityNotMappedException) { return false; }
        catch (SystemException) { return false; }
    }

    public void Dispose()
    {
        for (int i = _comObjects.Count - 1; i >= 0; i--)
            if (Marshal.IsComObject(_comObjects[i])) Marshal.FinalReleaseComObject(_comObjects[i]);
        _comObjects.Clear();
    }
}
