using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

/// <summary>Owns a native notification icon without adding a taskbar window.</summary>
public sealed class TrayIconService : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private const uint SettingsCommand = 1;
    private const uint ExitCommand = 2;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _retryTimer;
    private readonly Action _showSettings;
    private readonly Action _exit;
    private readonly WindowProcedure _windowProcedure;
    private readonly string _className = "EDock.Tray." + Guid.NewGuid().ToString("N");
    private readonly nint _instance;
    private readonly uint _taskbarCreated;
    private NotifyIconData _data;
    private nint _window;
    private nint _icon;
    private bool _classRegistered;
    private bool _registered;
    private bool _version4;
    private bool _disposed;

    public string? LastError { get; private set; }
    public event EventHandler? TaskbarCreated;
    public event EventHandler? AppearanceChanged;
    public bool IsMenuOpen { get; private set; }
    public event EventHandler? InteractionChanged;

    public TrayIconService(DispatcherQueue dispatcher, Action showSettings, Action exit)
    {
        _dispatcher = dispatcher;
        _showSettings = showSettings;
        _exit = exit;
        _windowProcedure = OnWindowMessage;
        _instance = GetModuleHandle(null);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _retryTimer = dispatcher.CreateTimer();
        _retryTimer.Interval = TimeSpan.FromSeconds(2);
        _retryTimer.Tick += RetryRegistration;
        try
        {
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(), Style = 0x0008,
                Procedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
                Instance = _instance, ClassName = _className
            };
            if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _classRegistered = true;
            // A hidden top-level window receives TaskbarCreated after Explorer restarts.
            // Message-only windows do not receive that broadcast.
            _window = CreateWindowEx(0x08000080, _className, "EDock 托盘", 0,
                0, 0, 0, 0, 0, 0, _instance, 0);
            if (_window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "EDock.ico"), 1, 32, 32, 0x10);
            if (_icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 EDock 图标。");
            _data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1,
                Flags = 0x1 | 0x2 | 0x4 | 0x80, Callback = CallbackMessage, Icon = _icon,
                Tip = "EDock · 右键设置或退出", Info = "", InfoTitle = ""
            };
            TryRegister();
        }
        catch { Dispose(); throw; }
    }

    private void RetryRegistration(DispatcherQueueTimer sender, object args) => TryRegister();

    private void TryRegister()
    {
        if (_disposed || _registered || _window == 0) return;
        if (!ShellNotifyIcon(0, ref _data))
        {
            LastError = "系统托盘暂时不可用，EDock 将自动重试。";
            _retryTimer.Start();
            return;
        }
        _registered = true;
        _data.Version = 4;
        _version4 = ShellNotifyIcon(4, ref _data);
        LastError = null;
        _retryTimer.Stop();
    }

    private nint OnWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (!_disposed && _taskbarCreated != 0 && message == _taskbarCreated)
            {
                _registered = false;
                TryRegister();
                TaskbarCreated?.Invoke(this, EventArgs.Empty);
                return 0;
            }
            if (!_disposed && message is 0x001A or 0x007E or 0x031A or 0x0320)
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
            if (!_disposed && message == CallbackMessage)
            {
                uint notification = _version4 ? (uint)((long)lParam & 0xffff) : (uint)lParam;
                if (notification is 0x007B or 0x0205) ShowMenu(); // WM_CONTEXTMENU / legacy right up
                else if (notification == 0x0203) QueueCommand(SettingsCommand); // double click
                return 0;
            }
        }
        catch (Exception ex) { LastError = $"托盘操作失败：{ex.Message}"; }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IsMenuOpen = true;
            InteractionChanged?.Invoke(this, EventArgs.Empty);
            AppendMenu(menu, 0, SettingsCommand, "设置…");
            AppendMenu(menu, 0x0800, 0, null);
            AppendMenu(menu, 0, ExitCommand, "退出 EDock");
            SetMenuDefaultItem(menu, SettingsCommand, false);
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            uint command = TrackPopupMenuEx(menu, 0x0002 | 0x0100 | 0x0080,
                point.X, point.Y, _window, 0);
            PostMessage(_window, 0, 0, 0);
            QueueCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
            IsMenuOpen = false;
            InteractionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void QueueCommand(uint command)
    {
        if (command is not (SettingsCommand or ExitCommand)) return;
        // Finish the native menu callback before opening XAML or destroying its owner.
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            if (command == SettingsCommand) _showSettings(); else _exit();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retryTimer.Stop();
        _retryTimer.Tick -= RetryRegistration;
        if (_window != 0)
        {
            ShellNotifyIcon(2, ref _data);
            DestroyWindow(_window);
            _window = 0;
        }
        if (_icon != 0) { DestroyIcon(_icon); _icon = 0; }
        if (_classRegistered) { UnregisterClass(_className, _instance); _classRegistered = false; }
        _registered = false;
        GC.KeepAlive(_windowProcedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadImage(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMenuDefaultItem(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
