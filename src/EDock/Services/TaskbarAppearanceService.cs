using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

/// <summary>
/// Controls the taskbar background through the system XAML diagnostics API.
/// No running timer, mapping or ownership lock while disabled. A previously
/// loaded native module remains dormant in Explorer until Explorer exits.
/// </summary>
public sealed class TaskbarAppearanceService : IDisposable
{
    private const string ChannelName = "Local\\EDock.TaskbarAppearance.v7";
    private const string WindowClass = "EDock.TaskbarAppearance.v7";
    private const uint ApplyMessage = 0x8051;
    private const string HelperName = "EDock.Taskbar.0.14.3.dll";
    private const int Magic = 0x45445442;
    private static readonly Guid DiagnosticsClass = new("5ede36d4-6324-476b-b183-2976362e5297");
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _checkTimer;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private Mutex? _owner;
    private bool _ownsMutex;
    private bool _enabled;
    private bool _disposed;
    private bool _injecting;
    private bool _refreshPending;
    private int _generation;
    private int _epoch;
    private double _opacity;
    private int _material;
    private uint _shellProcess;
    private uint _attemptedShell;
    private int _attemptedEpoch = -1;
    private DateTime _deadline;
    private string? _injectionError;

    public string? LastError { get; private set; }
    public event EventHandler? StatusChanged;

    public TaskbarAppearanceService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _checkTimer = dispatcher.CreateTimer();
        _checkTimer.Interval = TimeSpan.FromMilliseconds(150);
        _checkTimer.Tick += CheckResult;
    }

    /// <summary>
    /// Transparent preserves the previous background-opacity behavior. Acrylic
    /// and Blur use the real Windows taskbar XAML backdrop blur; opacity controls tint
    /// concentration, so zero tint still retains the blur. Windows may use its
    /// standard fallback when system accessibility or power settings disable it.
    /// </summary>
    public void Configure(bool enabled, double opacity, string material = "Transparent")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;
        int selectedMaterial = material switch { "Acrylic" => 1, "Blur" => 2, _ => 0 };
        if (!enabled)
        {
            if (_view is not null) Stop(failOnRestore: true);
            return;
        }
        if (_view is not null)
        {
            if (_enabled && _opacity == opacity && _material == selectedMaterial) return;
            _enabled = true;
            _opacity = opacity;
            _material = selectedMaterial;
            Publish();
            BeginCheck();
            TryConnect(Path.Combine(AppContext.BaseDirectory, "Native", HelperName));
            return;
        }

        EnsureNoOtherCustomizer();
        string helperPath = Path.Combine(AppContext.BaseDirectory, "Native", HelperName);
        if (!File.Exists(helperPath)) throw new FileNotFoundException("任务栏组件不完整，请保留 EDock 的完整程序文件夹。", helperPath);
        try
        {
            _owner = new Mutex(false, "Local\\EDock.TaskbarAppearance.Owner.v1");
            try { _ownsMutex = _owner.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
            if (!_ownsMutex) throw new InvalidOperationException("另一个 EDock 实例正在调整系统任务栏，请先关闭它的任务栏透明功能。");

            _mapping = MemoryMappedFile.CreateOrOpen(ChannelName, 64, MemoryMappedFileAccess.ReadWrite);
            _view = _mapping.CreateViewAccessor(0, 64, MemoryMappedFileAccess.ReadWrite);
            // Invalidate any stale owner before publishing a new session.
            _view.Write(0, 0);
            for (int offset = 4; offset < 64; offset += 4) _view.Write(offset, 0);
            _view.Write(4, 2);
            _view.Write(8, Environment.ProcessId);
            Thread.MemoryBarrier();
            _view.Write(0, Magic);
            _enabled = true;
            _opacity = opacity;
            _material = selectedMaterial;
            _generation = 0;
            _epoch++;
            _injectionError = null;
            SetError(null);
            Publish();
            BeginCheck();
            TryConnect(helperPath);
        }
        catch
        {
            Stop();
            throw;
        }
    }

    /// <summary>Called by the existing tray window on taskbar/theme/display events.</summary>
    public void Refresh()
    {
        if (_disposed || !_enabled) return;
        // Coalesce bursts of Windows appearance messages. Polling stops after ack.
        if (_checkTimer.IsRunning) { _refreshPending = true; return; }
        _injectionError = null;
        _attemptedShell = 0;
        Publish();
        BeginCheck();
        TryConnect(Path.Combine(AppContext.BaseDirectory, "Native", HelperName));
    }

    private static void EnsureNoOtherCustomizer()
    {
        foreach (string name in new[] { "TranslucentTB", "TaskbarX", "RoundedTB", "TaskbarXI" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                    throw new InvalidOperationException($"请先退出 {name}，再开启 EDock 的任务栏外观功能，避免两个工具同时修改任务栏。");
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    private void Publish()
    {
        if (_view is null) return;
        _view.Write(12, _enabled ? 1 : 0);
        _view.Write(16, (int)Math.Round(_opacity * 1_000_000));
        _view.Write(40, _material);
        Thread.MemoryBarrier();
        _view.Write(20, ++_generation);
        foreach (nint window in HelperWindows()) PostMessage(window, ApplyMessage, 0, 0);
    }

    private void BeginCheck()
    {
        _deadline = DateTime.UtcNow.AddSeconds(12);
        _checkTimer.Start();
    }

    private void TryConnect(string helperPath)
    {
        if (!_enabled || _injecting) return;
        nint shell = FindWindow("Shell_TrayWnd", null);
        if (shell == 0) return; // TaskbarCreated or the bounded startup retry resumes this.
        GetWindowThreadProcessId(shell, out uint shellProcess);
        if (shellProcess == 0) return;
        _shellProcess = shellProcess;
        if (HelperWindows().Count > 0) return;
        // A successful diagnostics connection can take time to enumerate XAML,
        // or never find a supported taskbar. Do not keep attaching and extending
        // the deadline on every check. A new session/system refresh can retry.
        if (_attemptedShell == shellProcess && _attemptedEpoch == _epoch) return;
        _attemptedShell = shellProcess;
        _attemptedEpoch = _epoch;
        _injecting = true;
        int epoch = _epoch;
        _ = Task.Run(() =>
        {
            string? error = null;
            try { ConnectDiagnostics(shellProcess, helperPath); }
            catch (Exception ex) { error = ex.Message; }
            _dispatcher.TryEnqueue(() =>
            {
                _injecting = false;
                if (_disposed || !_enabled) return;
                _shellProcess = 0;
                nint currentShell = FindWindow("Shell_TrayWnd", null);
                if (currentShell != 0) GetWindowThreadProcessId(currentShell, out _shellProcess);
                _injectionError = epoch == _epoch && _shellProcess == shellProcess ? error : null;
                Publish();
                BeginCheck();
                if (epoch != _epoch || _shellProcess != shellProcess) TryConnect(helperPath);
            });
        });
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
            throw new COMException("Windows 暂时无法连接任务栏外观组件。", result);
        }
        finally { NativeLibrary.Free(library); }
    }

    private void CheckResult(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !_enabled || _view is null) { _checkTimer.Stop(); return; }
        if (_refreshPending)
        {
            _refreshPending = false;
            _injectionError = null;
            _attemptedShell = 0;
            _shellProcess = 0;
            Publish();
            BeginCheck();
        }
        int acknowledged = _view.ReadInt32(36);
        Thread.MemoryBarrier();
        int applied = _view.ReadInt32(28);
        int error = _view.ReadInt32(32);
        if (acknowledged == _generation && applied > 0 && error >= 0)
        {
            _checkTimer.Stop();
            SetError(null);
            return;
        }
        if (DateTime.UtcNow >= _deadline)
        {
            _checkTimer.Stop();
            string reason = _injectionError ?? (error < 0 ? $"Windows 返回 0x{error:X8}。" : "尚未找到可调整的任务栏背景。");
            SetError($"任务栏外观暂未生效：{reason} 可关闭后重新开启此功能；边栏仍可正常使用。");
            return;
        }
        if (!_injecting && _injectionError is null)
            TryConnect(Path.Combine(AppContext.BaseDirectory, "Native", HelperName));
    }

    private List<nint> HelperWindows()
    {
        var windows = new List<nint>();
        if (_shellProcess == 0)
        {
            nint shell = FindWindow("Shell_TrayWnd", null);
            if (shell != 0) GetWindowThreadProcessId(shell, out _shellProcess);
        }
        if (_shellProcess == 0) return windows;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint process);
            if (process == _shellProcess)
            {
                var name = new StringBuilder(128);
                if (GetClassName(window, name, name.Capacity) != 0 && name.ToString() == WindowClass) windows.Add(window);
            }
            return true;
        }, 0);
        return windows;
    }

    private void Stop(bool failOnRestore = false)
    {
        _checkTimer.Stop();
        _enabled = false;
        _epoch++;
        _refreshPending = false;
        if (_view is not null)
        {
            // Send synchronously, bounded per UI thread, so saved values are restored
            // before releasing ownership or closing the controlling process.
            Publish();
            var windows = HelperWindows();
            bool sent = true;
            foreach (nint window in windows)
                sent &= SendMessageTimeout(window, ApplyMessage, 0, 0, 0x0002 | 0x0020, 1000, out _) != 0;
            if (windows.Count > 0 && (!sent || _view.ReadInt32(36) != _generation
                || _view.ReadInt32(28) != 0 || _view.ReadInt32(32) < 0))
            {
                const string error = "Windows 暂时未能恢复任务栏外观，请稍后重试关闭；EDock 退出时也会再次尝试恢复。";
                SetError(error);
                if (failOnRestore) throw new InvalidOperationException(error);
                return; // Keep the channel and original values available for a retry.
            }
            _view.Dispose();
            _view = null;
        }
        _mapping?.Dispose(); _mapping = null;
        if (_ownsMutex) { _owner?.ReleaseMutex(); _ownsMutex = false; }
        _owner?.Dispose(); _owner = null;
        _shellProcess = 0;
        _injectionError = null;
        SetError(null);
    }

    private void SetError(string? error)
    {
        if (LastError == error) return;
        LastError = error;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _checkTimer.Tick -= CheckResult;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int InitializeDiagnostics(string endpoint, uint process, string? diagnosticsDll, string helperDll, Guid clsid, string data);
    private delegate bool EnumWindowProcedure(nint window, nint parameter);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowProcedure callback, nint parameter);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int capacity);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
