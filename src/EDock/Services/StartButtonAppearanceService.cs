using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

/// <summary>
/// Owns EDock's Start-button image session. The native component discovers the
/// actual taskbar buttons through XAML diagnostics; no overlay intercepts input.
/// Disabled instances own no timer, shared mapping, hook or ownership lock.
/// </summary>
public sealed class StartButtonAppearanceService : IDisposable
{
    private const string ChannelName = "Local\\EDock.StartButtonAppearance.v3";
    private const string WindowClass = "EDock.StartButtonAppearance.v3";
    private const string HelperName = "EDock.StartButton.0.16.18.1.dll";
    private const uint ApplyMessage = 0x8053;
    private const int Magic = 0x45445342;
    private const int MappingSize = 65536, PathCapacity = 4096;
    private const int ErrorMessageOffset = 32832, ErrorMessageCapacity = 2048;
    private const int ImageMappingNamesOffset = 36928, ImageMappingNameCapacity = 256;
    private const int ImageMappingLengthsOffset = 38976, MaximumImageBytes = 16 * 1024 * 1024;
    private const int MaximumRetiredImages = 4;
    private static readonly Guid DiagnosticsClass = new("2b309e31-d20e-45f2-a326-02ee74c63b93");
    private readonly DispatcherQueue _dispatcher;
    private DispatcherQueueTimer? _checkTimer;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private ImagePayload?[] _imagePayloads = new ImagePayload?[4];
    private readonly List<ImagePayload> _retiredImagePayloads = new();
    private Mutex? _owner;
    private bool _ownsMutex, _enabled, _disposed, _injecting, _refreshPending;
    private int _generation, _epoch, _attemptedEpoch = -1, _size = 32;
    private string _imagePath = "", _hoverImagePath = "", _pressedImagePath = "", _activatedImagePath = "";
    private uint _shellProcess, _attemptedShell;
    private long _deadline;
    private string? _injectionError;

    public string? LastError { get; private set; }
    public string StatusText { get; private set; } = "已关闭，使用系统开始按钮图标。";
    public event EventHandler? StatusChanged;

    public StartButtonAppearanceService(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    public void Configure(bool enabled, string imagePath, double size,
        string hoverImagePath = "", string pressedImagePath = "", string activatedImagePath = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!enabled)
        {
            if (_view is not null) Stop(failOnRestore: true);
            else SetStatus(null, "已关闭，使用系统开始按钮图标。");
            return;
        }

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            if (_view is not null) Stop(failOnRestore: true);
            SetStatus(null, "请选择普通状态的图片；当前保留系统开始按钮图标。");
            return;
        }

        string normal, hover, pressed, activated;
        try
        {
            normal = ValidateImagePath(imagePath);
            hover = ValidateImagePath(hoverImagePath, optional: true);
            pressed = ValidateImagePath(pressedImagePath, optional: true);
            activated = ValidateImagePath(activatedImagePath, optional: true);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetStatus(error.Message, error.Message);
            throw;
        }
        int selectedSize = (int)Math.Round(double.IsFinite(size) ? Math.Clamp(size, 8, 64) : 32);
        if (_view is not null && _enabled && _imagePath == normal && _hoverImagePath == hover
            && _pressedImagePath == pressed && _activatedImagePath == activated && _size == selectedSize) return;

        string helperPath = HelperPath;
        if (!File.Exists(helperPath))
            throw new FileNotFoundException("开始按钮组件不完整，请保留 EDock 的完整程序文件夹。", helperPath);

        // Read images in EDock's process: Explorer may not be able to read a
        // user's encrypted configuration directory even under the same SID.
        // Complete every new payload before publishing any part of the change.
        ImagePayload?[] preparedImages;
        try { preparedImages = PrepareImagePayloads(normal, hover, pressed, activated); }
        catch (Exception error)
        {
            SetStatus(error.Message, error.Message);
            throw;
        }
        bool imagesCommitted = false;
        try
        {
            if (_view is null)
            {
                _owner = new Mutex(false, "Local\\EDock.StartButtonAppearance.Owner.v1");
                try { _ownsMutex = _owner.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
                if (!_ownsMutex)
                    throw new InvalidOperationException("另一个 EDock 实例正在调整开始按钮，请先关闭它的开始按钮替换功能。");

                _mapping = MemoryMappedFile.CreateOrOpen(ChannelName, MappingSize, MemoryMappedFileAccess.ReadWrite);
                _view = _mapping.CreateViewAccessor(0, MappingSize, MemoryMappedFileAccess.ReadWrite);
                _view.Write(0, 0);
                _view.WriteArray(4, new byte[MappingSize - 4], 0, MappingSize - 4);
                _view.Write(4, 2);
                _view.Write(8, Environment.ProcessId);
                Thread.MemoryBarrier();
                _view.Write(0, Magic);
                _generation = 0;
                _epoch++;
            }

            CommitImagePayloads(preparedImages);
            imagesCommitted = true;
            _enabled = true;
            _imagePath = normal;
            _hoverImagePath = hover;
            _pressedImagePath = pressed;
            _activatedImagePath = activated;
            _size = selectedSize;
            _injectionError = null;
            _attemptedShell = 0;
            uint previousProcess = _shellProcess;
            _shellProcess = ShellProcess();
            Publish(previousProcess);
            BeginCheck();
            TryConnect();
        }
        catch
        {
            if (!imagesCommitted) DisposeNewImagePayloads(preparedImages);
            Stop();
            throw;
        }
    }

    /// <summary>Called by EDock's existing tray on taskbar, theme and display changes.</summary>
    public void Refresh()
    {
        if (_disposed || !_enabled) return;
        // System events often arrive in bursts. They share one bounded check;
        // refreshing cannot perpetually extend its acknowledgement deadline.
        if (_checkTimer?.IsRunning == true) { _refreshPending = true; return; }
        _injectionError = null;
        _attemptedShell = 0;
        SelectTarget(ShellProcess());
        Publish();
        BeginCheck();
        TryConnect();
    }

    private static string HelperPath => Path.Combine(AppContext.BaseDirectory, "Native", HelperName);

    private static string ValidateImagePath(string? path, bool optional = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (optional) return "";
            throw new ArgumentException("请选择开始按钮图片。");
        }
        path = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
            || Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            throw new ArgumentException("开始按钮图片需要使用本地图片的完整路径。");
        path = Path.GetFullPath(path);
        if (path.Length >= PathCapacity)
            throw new ArgumentException("图片路径过长，请将图片放到路径较短的本地文件夹。");
        if (!new[] { ".png", ".jpg", ".jpeg", ".gif" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("开始按钮图片支持 PNG、JPG 和 GIF 格式。");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到开始按钮图片，请重新选择。", path);
        return path;
    }

    private sealed class ImagePayload : IDisposable
    {
        private readonly MemoryMappedFile _mapping;
        public string Name { get; }
        public uint Length { get; }

        private ImagePayload(MemoryMappedFile mapping, string name, uint length)
        {
            _mapping = mapping; Name = name; Length = length;
        }

        public static ImagePayload Create(string path)
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.SequentialScan);
            long length = input.Length;
            if (length < 1 || length > MaximumImageBytes)
                throw new InvalidDataException("开始按钮图片需要为 1 字节至 16 MB，请选择较小的有效图片。");
            string name = $"Local\\EDock.StartButtonImage.v1.{Environment.ProcessId}.{Guid.NewGuid():N}";
            var mapping = MemoryMappedFile.CreateNew(name, length, MemoryMappedFileAccess.ReadWrite);
            try
            {
                // Only the kernel mapping handle survives. Neither a mapped
                // view nor an image-sized managed byte[] stays in EDock.
                using (var output = mapping.CreateViewStream(0, length, MemoryMappedFileAccess.Write))
                {
                    input.CopyTo(output);
                    if (output.Position != length) throw new IOException("读取开始按钮图片时文件大小发生变化，请重新选择。");
                }
                return new ImagePayload(mapping, name, (uint)length);
            }
            catch { mapping.Dispose(); throw; }
        }

        public void Dispose() => _mapping.Dispose();
    }

    private ImagePayload?[] PrepareImagePayloads(params string[] paths)
    {
        ReleaseAcknowledgedImagePayloads();
        string[] previousPaths = { _imagePath, _hoverImagePath, _pressedImagePath, _activatedImagePath };
        int retiring = Enumerable.Range(0, paths.Length).Count(i => _imagePayloads[i] is not null && paths[i] != previousPaths[i]);
        if (_retiredImagePayloads.Count + retiring > MaximumRetiredImages)
            throw new InvalidOperationException("上一次开始按钮图片仍在加载，请稍后再更换图片。");
        var prepared = new ImagePayload?[4];
        try
        {
            for (int i = 0; i < paths.Length; i++)
                if (paths[i].Length > 0)
                    prepared[i] = paths[i] == previousPaths[i] && _imagePayloads[i] is { } existing
                        ? existing : ImagePayload.Create(paths[i]);
            return prepared;
        }
        catch { DisposeNewImagePayloads(prepared); throw; }
    }

    private void DisposeNewImagePayloads(ImagePayload?[] prepared)
    {
        foreach (var image in prepared)
            if (image is not null && !_imagePayloads.Contains(image)) image.Dispose();
    }

    private void CommitImagePayloads(ImagePayload?[] prepared)
    {
        foreach (var image in _imagePayloads)
            if (image is not null && !prepared.Contains(image)) _retiredImagePayloads.Add(image);
        _imagePayloads = prepared;
    }

    private void ReleaseAcknowledgedImagePayloads()
    {
        if (_view is not null && _view.ReadInt32(36) == _generation)
        {
            Thread.MemoryBarrier();
            if (_view.ReadInt32(28) > 0 || _view.ReadInt32(32) < 0) ReleaseRetiredImagePayloads();
        }
    }

    private void ReleaseRetiredImagePayloads()
    {
        foreach (var image in _retiredImagePayloads) image.Dispose();
        _retiredImagePayloads.Clear();
    }

    private void ReleaseImagePayloads()
    {
        ReleaseRetiredImagePayloads();
        foreach (var image in _imagePayloads) image?.Dispose();
        Array.Clear(_imagePayloads);
    }

    private void Publish(uint previousProcess = 0)
    {
        if (_view is null) return;
        // The native reader checks this flag plus the generation before and
        // after taking a snapshot. A path cannot be observed half-written.
        _view.Write(44, 1);
        Thread.MemoryBarrier();
        _view.Write(12, _enabled ? 1 : 0);
        _view.Write(16, _size);
        _view.Write(40, _shellProcess == 0 ? -1 : unchecked((int)_shellProcess));
        WritePath(64, _imagePath);
        WritePath(8256, _hoverImagePath);
        WritePath(16448, _pressedImagePath);
        WritePath(24640, _activatedImagePath);
        for (int i = 0; i < _imagePayloads.Length; i++)
        {
            WritePath(ImageMappingNamesOffset + i * ImageMappingNameCapacity * sizeof(char), _imagePayloads[i]?.Name ?? "");
            _view.Write(ImageMappingLengthsOffset + i * sizeof(uint), _imagePayloads[i]?.Length ?? 0);
        }
        _view.Write(ErrorMessageOffset, (short)0);
        _view.Write(20, ++_generation);
        Thread.MemoryBarrier();
        _view.Write(44, 0);
        foreach (nint window in HelperWindows()) PostMessage(window, ApplyMessage, 0, 0);
        if (previousProcess != 0 && previousProcess != _shellProcess)
            foreach (nint window in HelperWindows(previousProcess)) PostMessage(window, ApplyMessage, 0, 0);
    }

    private void WritePath(int offset, string path)
    {
        byte[] data = Encoding.Unicode.GetBytes(path + '\0');
        _view!.WriteArray(offset, data, 0, data.Length);
    }

    private void BeginCheck()
    {
        if (_checkTimer is null)
        {
            _checkTimer = _dispatcher.CreateTimer();
            _checkTimer.Interval = TimeSpan.FromMilliseconds(200);
            _checkTimer.IsRepeating = true;
            _checkTimer.Tick += CheckResult;
        }
        _deadline = Environment.TickCount64 + 12000;
        SetStatus(null, "正在应用开始按钮图片…");
        _checkTimer.Start();
    }

    private void TryConnect()
    {
        if (!_enabled || _injecting) return;
        uint process = ShellProcess();
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
                uint currentProcess = ShellProcess();
                SelectTarget(currentProcess);
                if (epoch == _epoch && currentProcess == process) _injectionError = error;
                else { _injectionError = null; _attemptedShell = 0; }
                // Connection completion does not change the settings generation
                // or restart a check that has already acknowledged or timed out.
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
        Publish(previous);
    }

    private static uint ShellProcess()
    {
        nint shell = FindWindow("Shell_TrayWnd", null);
        if (shell == 0) return 0;
        GetWindowThreadProcessId(shell, out uint process);
        return process;
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
            throw new COMException("Windows 暂时无法连接开始按钮组件。", result);
        }
        finally { NativeLibrary.Free(library); }
    }

    private void CheckResult(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !_enabled || _view is null) { sender.Stop(); return; }
        if (_refreshPending)
        {
            _refreshPending = false;
            _attemptedShell = 0;
            _injectionError = null;
            SelectTarget(ShellProcess());
            Publish();
        }
        int acknowledged = _view.ReadInt32(36);
        Thread.MemoryBarrier();
        int applied = _view.ReadInt32(28), error = _view.ReadInt32(32);
        if (acknowledged == _generation && (applied > 0 || error < 0)) ReleaseRetiredImagePayloads();
        if (acknowledged == _generation && applied > 0 && error >= 0)
        {
            sender.Stop();
            SetStatus(null, "已应用。关闭功能或退出 EDock 会恢复系统开始按钮图标。");
            return;
        }
        // Native image failures arrive after the initial tree acknowledgement.
        // Report an acknowledged failure promptly instead of leaving the UI
        // at "applying" until the full connection timeout has elapsed.
        if (acknowledged == _generation && error < 0)
        {
            sender.Stop();
            SetFailureStatus(error, applied);
            return;
        }
        if (Environment.TickCount64 >= _deadline)
        {
            sender.Stop();
            ReleaseRetiredImagePayloads();
            SetFailureStatus(acknowledged == _generation ? error : 0, applied);
            return;
        }
        if (!_injecting && _injectionError is null) TryConnect();
    }

    private void SetFailureStatus(int error, int applied)
    {
        string detail = error < 0 ? ReadNativeError() : "";
        string reason = _injectionError ?? (error < 0
            ? (detail.Length > 0 ? detail + $"（0x{error:X8}）" : $"Windows 返回 0x{error:X8}。")
            : _shellProcess == 0 ? "尚未找到系统任务栏。" : "Windows 未在规定时间内确认图片替换。");
        string message = "开始按钮替换暂未完成：" + reason
            + (applied > 0 ? " 可以关闭功能恢复系统图标。" : " 当前保留系统图标。");
        SetStatus(message, message);
    }

    private string ReadNativeError()
    {
        if (_view is null) return "";
        byte[] bytes = new byte[ErrorMessageCapacity * sizeof(char)];
        _view.ReadArray(ErrorMessageOffset, bytes, 0, bytes.Length);
        string text = Encoding.Unicode.GetString(bytes);
        int terminator = text.IndexOf('\0');
        return (terminator >= 0 ? text[..terminator] : text).Trim();
    }

    private static List<nint> HelperWindows(uint process)
    {
        var windows = new List<nint>();
        if (process == 0) return windows;
        nint window = 0;
        var visited = new HashSet<nint>();
        while ((window = FindWindowEx(0, window, WindowClass, null)) != 0 && visited.Add(window))
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == process) windows.Add(window);
        }
        return windows;
    }

    private List<nint> HelperWindows() => HelperWindows(_shellProcess);

    private void Stop(bool failOnRestore = false)
    {
        _checkTimer?.Stop();
        _enabled = false;
        _epoch++;
        _refreshPending = false;
        // Restoration only needs the control channel. Even if Windows refuses
        // a restore, retaining that session must not retain old image payloads.
        ReleaseImagePayloads();
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
                const string message = "Windows 暂未恢复开始按钮图标，请稍后再次关闭；退出 EDock 时也会尝试恢复。";
                SetStatus(message, message);
                if (failOnRestore) throw new InvalidOperationException(message);
                return; // Retain the channel and ownership until a restore retry.
            }
        }
        if (_checkTimer is not null) { _checkTimer.Tick -= CheckResult; _checkTimer = null; }
        _view?.Dispose(); _view = null;
        _mapping?.Dispose(); _mapping = null;
        if (_ownsMutex) { _owner?.ReleaseMutex(); _ownsMutex = false; }
        _owner?.Dispose(); _owner = null;
        _shellProcess = 0;
        _injectionError = null;
        SetStatus(null, "已关闭，使用系统开始按钮图标。");
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
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int InitializeDiagnostics(string endpoint, uint process, string? diagnosticsDll, string helperDll, Guid clsid, string data);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? windowName);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
