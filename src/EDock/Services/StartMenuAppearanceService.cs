using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

/// <summary>Changes only the Start menu's XAML background. Disabled instances own no
/// timers, hooks, mappings or locks. Start-menu show/foreground events reconnect a
/// replacement host; polling runs only for the bounded acknowledgement interval.</summary>
public sealed class StartMenuAppearanceService : IDisposable
{
    private const string ChannelName = "Local\\EDock.StartMenuAppearance.v1";
    private const string WindowClass = "EDock.StartMenuAppearance.v1";
    private const string HelperName = "EDock.StartMenu.0.16.15.dll";
    private const uint ApplyMessage = 0x8052;
    private const int Magic = 0x4544534D;
    private const uint ForegroundEvent = 3, ShowEvent = 0x8002;
    private static readonly Guid DiagnosticsClass = new("970b06c1-0b22-4f18-9f97-86f8c2e10848");
    private readonly DispatcherQueue _dispatcher;
    private readonly WinEventProcedure _windowEvent;
    private DispatcherQueueTimer? _checkTimer;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private Mutex? _owner;
    private bool _ownsMutex, _enabled, _disposed, _injecting;
    private int _generation, _epoch, _attemptedEpoch = -1, _queuedRefresh;
    private double _opacity;
    private uint _shellProcess, _attemptedShell;
    private nint _foregroundHook, _showHook;
    private DateTime _deadline;
    private string? _injectionError;

    public string? LastError { get; private set; }
    public string StatusText { get; private set; } = "已关闭，使用系统开始菜单外观。";
    public event EventHandler? StatusChanged;

    public StartMenuAppearanceService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _windowEvent = OnWindowEvent;
    }

    public void Configure(bool enabled, double opacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;
        if (!enabled)
        {
            if (_view is not null) Stop(failOnRestore: true);
            return;
        }
        if (_view is not null)
        {
            if (_enabled && _opacity == opacity) return;
            _enabled = true;
            _opacity = opacity;
            Publish();
            BeginCheck();
            TryConnect();
            return;
        }

        string helperPath = HelperPath;
        if (!File.Exists(helperPath))
            throw new FileNotFoundException("开始菜单组件不完整，请保留 EDock 的完整程序文件夹。", helperPath);
        try
        {
            _owner = new Mutex(false, "Local\\EDock.StartMenuAppearance.Owner.v1");
            try { _ownsMutex = _owner.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
            if (!_ownsMutex)
                throw new InvalidOperationException("另一个 EDock 实例正在调整开始菜单，请先关闭它的开始菜单透明功能。");

            _mapping = MemoryMappedFile.CreateOrOpen(ChannelName, 64, MemoryMappedFileAccess.ReadWrite);
            _view = _mapping.CreateViewAccessor(0, 64, MemoryMappedFileAccess.ReadWrite);
            _view.Write(0, 0);
            for (int offset = 4; offset < 64; offset += 4) _view.Write(offset, 0);
            _view.Write(4, 1);
            _view.Write(8, Environment.ProcessId);
            Thread.MemoryBarrier();
            _view.Write(0, Magic);
            _enabled = true;
            _opacity = opacity;
            _generation = 0;
            _epoch++;
            _injectionError = null;
            _shellProcess = StartMenuProcess();
            InstallEventHooks();
            Publish();
            BeginCheck();
            TryConnect();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    /// <summary>Refreshes after a system theme/display/tray change.</summary>
    public void Refresh()
    {
        if (_disposed || !_enabled) return;
        // Already connecting or awaiting XAML creation: keep the original deadline.
        if (_checkTimer?.IsRunning == true) return;
        _injectionError = null;
        _attemptedShell = 0;
        Publish();
        BeginCheck();
        TryConnect();
    }

    private static string HelperPath => Path.Combine(AppContext.BaseDirectory, "Native", HelperName);

    private void Publish(uint previousProcess = 0)
    {
        if (_view is null) return;
        _view.Write(12, _enabled ? 1 : 0);
        _view.Write(16, (int)Math.Round(_opacity * 1_000_000));
        // -1 means no host exists yet. Zero is reserved for the read-only probe.
        _view.Write(40, _shellProcess == 0 ? -1 : unchecked((int)_shellProcess));
        Thread.MemoryBarrier();
        _view.Write(20, ++_generation);
        foreach (nint window in HelperWindows()) PostMessage(window, ApplyMessage, 0, 0);
        if (previousProcess != 0 && previousProcess != _shellProcess)
            foreach (nint window in HelperWindows(previousProcess)) PostMessage(window, ApplyMessage, 0, 0);
    }

    private void BeginCheck()
    {
        if (_checkTimer is null)
        {
            _checkTimer = _dispatcher.CreateTimer();
            _checkTimer.Interval = TimeSpan.FromMilliseconds(200);
            _checkTimer.Tick += CheckResult;
        }
        _deadline = DateTime.UtcNow.AddSeconds(12);
        SetStatus(null, "正在应用；首次使用请打开一次开始菜单。此操作不会影响图标和文字透明度。");
        _checkTimer.Start();
    }

    private void TryConnect()
    {
        if (!_enabled || _injecting) return;
        uint process = StartMenuProcess();
        SelectTarget(process);
        if (process == 0 || HelperWindows().Count > 0) return;
        if (_attemptedShell == process && _attemptedEpoch == _epoch) return;
        _attemptedShell = process;
        _attemptedEpoch = _epoch;
        _injecting = true;
        int epoch = _epoch;
        _ = Task.Run(() =>
        {
            string? error = null;
            try { ConnectDiagnostics(process, HelperPath); }
            catch (Exception ex) { error = ex.Message; }
            _dispatcher.TryEnqueue(() =>
            {
                _injecting = false;
                if (_disposed || !_enabled) return;
                uint currentProcess = StartMenuProcess();
                SelectTarget(currentProcess);
                if (epoch == _epoch && currentProcess == process)
                    _injectionError = error;
                else
                {
                    _injectionError = null;
                    _attemptedShell = 0;
                }
                // The helper reads the current generation on attachment. No new
                // generation or deadline is needed merely because attach returned.
                foreach (nint window in HelperWindows()) PostMessage(window, ApplyMessage, 0, 0);
                if (epoch != _epoch || currentProcess != process) TryConnect();
            });
        });
    }

    private void SelectTarget(uint process)
    {
        if (_shellProcess == process) return;
        uint previous = _shellProcess;
        _shellProcess = process;
        _attemptedShell = 0;
        _injectionError = null;
        // A helper attached to the old host must restore before its next display.
        Publish(previous);
    }

    private uint StartMenuProcess()
    {
        var processes = Process.GetProcessesByName("StartMenuExperienceHost");
        try
        {
            using var current = Process.GetCurrentProcess();
            int session = current.SessionId;
            var candidates = new HashSet<uint>();
            foreach (var process in processes)
            {
                try { if (process.SessionId == session && !process.HasExited) candidates.Add((uint)process.Id); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
            if (candidates.Contains(foreground)) return foreground;
            foreach (uint candidate in candidates)
            {
                if (ProcessWindows(candidate).Any(IsWindowVisible)) return candidate;
            }
            // Once a concrete Start surface was identified, keep it while hidden.
            if (candidates.Contains(_shellProcess)) return _shellProcess;
            return candidates.FirstOrDefault();
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static void ConnectDiagnostics(uint process, string helperPath)
    {
        nint library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "Windows.UI.Xaml.dll"));
        try
        {
            var initialize = Marshal.GetDelegateForFunctionPointer<InitializeDiagnostics>(NativeLibrary.GetExport(library, "InitializeXamlDiagnosticsEx"));
            int result = unchecked((int)0x80004005);
            for (int endpoint = 1; endpoint <= 30; endpoint++)
            {
                result = initialize("VisualDiagConnection" + endpoint, process, null, helperPath, DiagnosticsClass, "");
                if (result >= 0) return;
            }
            throw new COMException("Windows 暂时无法连接开始菜单背景组件。", result);
        }
        finally { NativeLibrary.Free(library); }
    }

    private void CheckResult(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !_enabled || _view is null) { sender.Stop(); return; }
        int acknowledged = _view.ReadInt32(36);
        Thread.MemoryBarrier();
        int applied = _view.ReadInt32(28), error = _view.ReadInt32(32);
        if (acknowledged == _generation && applied > 0 && error >= 0)
        {
            sender.Stop();
            SetStatus(null, "已应用，文字和图标保持清晰。关闭功能或退出 EDock 会恢复系统外观。");
            return;
        }
        if (DateTime.UtcNow >= _deadline)
        {
            sender.Stop();
            if (_shellProcess == 0 || (error >= 0 && _injectionError is null && _view.ReadInt32(24) == 0))
            {
                SetStatus(null, "尚未找到可调整的开始菜单背景，请打开一次开始菜单；若已打开仍未生效，当前系统版本可能暂不兼容。");
                return;
            }
            string reason = _injectionError ?? (error < 0 ? $"Windows 返回 0x{error:X8}。" : "当前系统的开始菜单背景暂未完成调整。");
            string message = "开始菜单透明暂未生效：" + reason;
            SetStatus(message, message);
            return;
        }
        if (!_injecting && _injectionError is null) TryConnect();
    }

    private void InstallEventHooks()
    {
        const uint OutOfContextSkipOwnProcess = 2;
        _foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, 0, _windowEvent, 0, 0, OutOfContextSkipOwnProcess);
        _showHook = SetWinEventHook(ShowEvent, ShowEvent, 0, _windowEvent, 0, 0, OutOfContextSkipOwnProcess);
        if (_foregroundHook == 0 || _showHook == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法监听开始菜单的打开状态。");
    }

    private void OnWindowEvent(nint hook, uint kind, nint window, int objectId, int childId, uint eventThread, uint time)
    {
        if (_disposed || !_enabled || window == 0 || objectId != 0 || childId != 0) return;
        GetWindowThreadProcessId(window, out uint process);
        if (process == 0 || process == Environment.ProcessId) return;
        if (process != _shellProcess)
        {
            // Foreground events are infrequent. For root show events only the
            // system XAML window class can introduce a new Start-menu host.
            if (kind != ForegroundEvent)
            {
                var name = new StringBuilder(128);
                if (GetClassName(window, name, name.Capacity) == 0
                    || name.ToString() is not ("Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow")) return;
            }
            try
            {
                using var target = Process.GetProcessById((int)process);
                if (target.ProcessName != "StartMenuExperienceHost") return;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return; }
        }
        if (Interlocked.Exchange(ref _queuedRefresh, 1) != 0) return;
        if (!_dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _queuedRefresh, 0);
            if (_disposed || !_enabled) return;
            if (process != _shellProcess)
            {
                SelectTarget(process);
                BeginCheck();
                TryConnect();
            }
            else Refresh();
        })) Interlocked.Exchange(ref _queuedRefresh, 0);
    }

    private List<nint> HelperWindows(uint processId = 0)
    {
        var windows = new List<nint>();
        uint target = processId == 0 ? _shellProcess : processId;
        if (target == 0) return windows;
        foreach (nint window in ProcessWindows(target, WindowClass))
        {
            var name = new StringBuilder(128);
            if (GetClassName(window, name, name.Capacity) != 0 && name.ToString() == WindowClass) windows.Add(window);
        }
        return windows;
    }

    // EnumWindows and EnumThreadWindows can omit UWP host windows. FindWindowEx
    // includes the Start host and its hidden diagnostics helper on Windows 11.
    private static List<nint> ProcessWindows(uint processId, string? className = null)
    {
        var windows = new HashSet<nint>();
        var visited = new HashSet<nint>();
        nint window = 0;
        while ((window = FindWindowEx(0, window, className, null)) != 0 && visited.Add(window))
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == processId) windows.Add(window);
        }
        return windows.ToList();
    }

    private void Stop(bool failOnRestore = false)
    {
        _checkTimer?.Stop();
        _enabled = false;
        _epoch++;
        if (_view is not null)
        {
            Publish();
            var windows = HelperWindows();
            bool sent = true;
            foreach (nint window in windows)
                sent &= SendMessageTimeout(window, ApplyMessage, 0, 0, 0x0002 | 0x0020, 1000, out _) != 0;
            if (windows.Count > 0 && (!sent || _view.ReadInt32(36) != _generation
                || _view.ReadInt32(28) != 0 || _view.ReadInt32(32) < 0))
            {
                const string message = "Windows 暂未恢复开始菜单背景，请稍后再次关闭；退出 EDock 时也会尝试恢复。";
                SetStatus(message, message);
                if (failOnRestore) throw new InvalidOperationException(message);
            }
        }
        if (_foregroundHook != 0) { UnhookWinEvent(_foregroundHook); _foregroundHook = 0; }
        if (_showHook != 0) { UnhookWinEvent(_showHook); _showHook = 0; }
        if (_checkTimer is not null) { _checkTimer.Tick -= CheckResult; _checkTimer = null; }
        _view?.Dispose(); _view = null;
        _mapping?.Dispose(); _mapping = null;
        if (_ownsMutex) { _owner?.ReleaseMutex(); _ownsMutex = false; }
        _owner?.Dispose(); _owner = null;
        _shellProcess = 0;
        _injectionError = null;
        SetStatus(null, "已关闭，使用系统开始菜单外观。");
    }

    private void SetStatus(string? error, string text)
    {
        if (LastError == error && StatusText == text) return;
        LastError = error;
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.KeepAlive(_windowEvent);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int InitializeDiagnostics(string endpoint, uint process, string? diagnosticsDll, string helperDll, Guid clsid, string data);
    private delegate void WinEventProcedure(nint hook, uint kind, nint window, int objectId, int childId, uint eventThread, uint time);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int capacity);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProcedure callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
}
