using EDock.Models;
using EDock.Services;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace EDock;

public partial class App : Application
{
    private Mutex? _instance;
    private DockWindow? _left;
    private DockWindow? _right;
    private SettingsWindow? _settingsWindow;
    private QuickModeController? _quickMode;
    private TrayIconService? _tray;
    private TaskbarAppearanceService? _taskbarAppearance;
    private IdleMemoryService? _idleMemory;
    private DockSide _quickTargetSide = DockSide.Left;
    private bool _quickDockSwitched;
    private DockSide? _ordinaryKeyboardSide;
    private DockSide? _ordinaryKeyboardSource;
    private EDock.Interop.NativeMethods.Point _keyboardPointerAnchor;
    private nint _keyboardForeground;
    private bool _changingKeyboardTarget;
    private SettingsStore _store = new();
    private static readonly System.Reflection.PropertyInfo[] SettingsProperties = typeof(DockSettings).GetProperties()
        .Where(property => property.CanRead && property.CanWrite).ToArray();
    private static string? _profileDirectory;
    private static string? _resolvedConfigurationDirectory;
    public DockSettings Settings { get; private set; } = new();
    public static new App Current => (App)Application.Current;
    public static void SetWindowIcon(Window window)
    {
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "EDock.ico"));
    }
    public string? LastError { get; private set; }
    public string? TaskbarError { get; private set; }
    public event EventHandler? TaskbarStatusChanged;
    public bool IsIsolatedProfile => _profileDirectory is not null;
    public string ConfigurationDirectory => SettingsStoreDirectory;
    private bool _exiting;
    private bool _exitPending;

    public App()
    {
        StartupService.ApplyLaunchPriority();
        InitializeComponent();
        // The tray and optional taskbar effects stay available with no XAML
        // windows. ExitApplication owns shutdown even after both docks close.
        DispatcherShutdownMode = Microsoft.UI.Xaml.DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(SettingsStoreDirectory); File.AppendAllText(Path.Combine(SettingsStoreDirectory, "error.log"), $"{DateTimeOffset.Now:u} {e.Exception}\n"); } catch { }
        };
    }

    private static string SettingsStoreDirectory => _profileDirectory ?? _resolvedConfigurationDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EDock");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        int profileArgument = Array.FindIndex(commandLine, x => x.Equals("--profile", StringComparison.OrdinalIgnoreCase));
        string mutexName = "Local\\EDock.Sidebar";
        if (profileArgument >= 0)
        {
            if (profileArgument + 1 >= commandLine.Length || !Path.IsPathFullyQualified(commandLine[profileArgument + 1])) { Exit(); return; }
            _profileDirectory = Path.GetFullPath(commandLine[profileArgument + 1]);
            _store = new SettingsStore(_profileDirectory);
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_profileDirectory.ToUpperInvariant()));
            mutexName += "." + Convert.ToHexString(hash);
        }
        _instance = new Mutex(true, mutexName, out bool created);
        if (!created) { Exit(); return; }
        var configuration = ConfigurationDirectoryMigration.Resolve(_profileDirectory);
        _resolvedConfigurationDirectory = configuration.DirectoryPath;
        _store = new SettingsStore(configuration.DirectoryPath);
        Settings = _store.Load();
        LastError = configuration.Error ?? _store.LastError;
        MigrateShortcutStorage();
        if (!IsIsolatedProfile)
        {
            _taskbarAppearance = new TaskbarAppearanceService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            _taskbarAppearance.StatusChanged += (_, _) =>
            {
                TaskbarError = _taskbarAppearance.LastError;
                TaskbarStatusChanged?.Invoke(this, EventArgs.Empty);
            };
        }
        ApplySettings();
        _quickMode = new QuickModeController(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
            () => _exiting || _exitPending || (_settingsWindow?.IsQuickModeBusy ?? false)
                || (_left?.IsInteractionBusy ?? false) || (_right?.IsInteractionBusy ?? false) || _tray?.IsMenuOpen == true,
            () => _settingsWindow?.QuickModeWindowHandle ?? 0,
            PointerKeyboardTarget);
        _quickMode.ActiveChanged += (_, active) =>
        {
            EndOrdinaryKeyboardSession();
            _quickDockSwitched = false;
            _quickTargetSide = _quickMode.PreferredSide;
            var target = QuickTarget();
            if (active && target is null) { _quickMode.Exit(); return; }
            ApplyQuickTargets(active, target);
        };
        _quickMode.SelectionRequested += (_, direction) => UseQuickTarget(dock => dock.SelectQuickItem(direction));
        _quickMode.VerticalSelectionRequested += (_, direction) => UseQuickTarget(dock => dock.MoveKeyboardSelection(direction, horizontal: false));
        _quickMode.HorizontalSelectionRequested += (_, direction) => UseQuickTarget(dock => dock.MoveKeyboardSelection(direction, horizontal: true));
        _quickMode.DockSwitchRequested += (_, _) => SwitchQuickDock();
        _quickMode.LaunchRequested += (_, _) => UseQuickTarget(dock => dock.LaunchQuickItem());
        _quickMode.ListSwitchRequested += (_, direction) => UseQuickTarget(dock => dock.SwitchKeyboardList(direction));
        _quickMode.ExpansionRequested += (_, _) => UseQuickTarget(dock => dock.ToggleKeyboardExpansion());
        _quickMode.DockCommandRequested += (_, request) =>
        {
            if (request.Action == DockKeyboardAction.SwitchDock) { SwitchOrdinaryDock(request.Side); return; }
            var dock = request.Side == DockSide.Left ? _left : _right;
            dock?.HandleKeyboardAction(request.Action);
        };
        ConfigureQuickMode();
        RefreshPointerKeyboardTarget();
        try
        {
            _tray = new TrayIconService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), () => ShowSettings(), ExitApplication);
            _tray.TaskbarCreated += (_, _) => _taskbarAppearance?.Refresh();
            _tray.AppearanceChanged += (_, _) => _taskbarAppearance?.Refresh();
            _tray.InteractionChanged += (_, _) => NotifyIdleMemoryStateChanged();
        }
        catch (Exception ex) { ReportError($"无法创建 EDock 托盘图标：{ex.Message}"); }
        _idleMemory = new IdleMemoryService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
            () => !_exiting && _settingsWindow is null && _quickMode?.IsActive != true
                && _tray?.IsMenuOpen != true
                && (_left is null || _left.IsReadyForIdleMemory)
                && (_right is null || _right.IsReadyForIdleMemory));
        NotifyIdleMemoryStateChanged();
        if (!IsIsolatedProfile)
        {
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _ = Task.Run(() =>
            {
                try { StartupService.UpgradeRegistrationIfNeeded(); }
                catch (Exception ex)
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        if (!_exiting) ReportError($"无法更新 EDock 开机启动设置：{ex.Message}");
                    });
                }
            });
        }
        if (args.Arguments.Contains("--settings", StringComparison.OrdinalIgnoreCase) || Environment.GetCommandLineArgs().Contains("--settings")) ShowSettings();
    }

    public bool SaveSettings(DockSide? changedSide = null)
    {
        try
        {
            _store.Save(Settings);
            LastError = _store.LastError;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
        // A UI refresh failure cannot undo a committed target path. Callers use
        // this result to decide whether a staged shortcut is safe to retain.
        try { ApplySettings(changedSide); }
        catch (Exception ex) { LastError = $"设置已保存，刷新边栏失败：{ex.Message}"; ReportError(LastError); }
        return true;
    }

    public bool PreviewSettings(Action<DockSettings> update, DockSide? changedSide = null)
    {
        // Keep list identities intact: an open settings window must not replace
        // shortcuts added, reordered or renamed elsewhere while it is open.
        var previous = SettingsProperties.Select(property =>
        {
            var value = property.GetValue(Settings);
            return value is DockOptions options ? options.Copy() : value;
        }).ToArray();
        var previousLeft = Settings.GetSideOptions(DockSide.Left);
        var previousRight = Settings.GetSideOptions(DockSide.Right);
        try
        {
            update(Settings);
            // Window-only appearance and taskbar edits do not rebuild dock tiles.
            ApplySettingsCore(changedSide, requireTaskbar: true,
                refreshLeft: !SettingsEditorDraft.SameOptions(previousLeft, Settings.GetSideOptions(DockSide.Left)),
                refreshRight: !SettingsEditorDraft.SameOptions(previousRight, Settings.GetSideOptions(DockSide.Right)));
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            for (int i = 0; i < SettingsProperties.Length; i++) SettingsProperties[i].SetValue(Settings, previous[i]);
            try { ApplySettings(changedSide); } catch { }
            LastError = $"无法应用设置：{ex.Message}";
            return false;
        }
    }

    public bool PersistSettingsPreview()
    {
        try { _store.Save(Settings); LastError = _store.LastError; return true; }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    private void MigrateShortcutStorage()
    {
        try
        {
            using var migration = ManagedShortcutStorage.PrepareMigration(Settings, ConfigurationDirectory);
            if (migration.UpdatedCount > 0)
            {
                _store.Save(Settings);
                migration.Commit();
            }
            if (migration.Errors.Count > 0)
                LastError = "部分快捷方式未能保存独立副本：\n" + string.Join("\n", migration.Errors);
            else if (migration.UnavailableCount > 0)
                LastError = $"有 {migration.UnavailableCount} 个旧快捷方式的原文件已经丢失，请重新拖入一次；新的导入会保存独立副本。";
        }
        catch (Exception ex) { LastError = $"保存快捷方式独立副本失败，已保留原配置：{ex.Message}"; }
    }

    public void ApplySettings(DockSide? changedSide = null, bool requireTaskbar = false) =>
        ApplySettingsCore(changedSide, requireTaskbar, refreshLeft: true, refreshRight: true);

    private void ApplySettingsCore(DockSide? changedSide, bool requireTaskbar, bool refreshLeft, bool refreshRight)
    {
        try
        {
            _taskbarAppearance?.Configure(Settings.TaskbarTransparencyEnabled, Settings.TaskbarOpacity, Settings.TaskbarMaterial);
            TaskbarError = _taskbarAppearance?.LastError;
        }
        catch (Exception ex)
        {
            TaskbarError = ex.Message;
            TaskbarStatusChanged?.Invoke(this, EventArgs.Empty);
            if (requireTaskbar) throw;
        }
        if ((_ordinaryKeyboardSide == DockSide.Left || _ordinaryKeyboardSource == DockSide.Left) && !Settings.LeftEnabled
            || (_ordinaryKeyboardSide == DockSide.Right || _ordinaryKeyboardSource == DockSide.Right) && !Settings.RightEnabled)
            EndOrdinaryKeyboardSession();
        bool leftCreated = SetDock(ref _left, DockSide.Left, Settings.LeftEnabled);
        bool rightCreated = SetDock(ref _right, DockSide.Right, Settings.RightEnabled);
        if (!leftCreated && refreshLeft && (changedSide is null or DockSide.Left)) _left?.Refresh();
        if (!rightCreated && refreshRight && (changedSide is null or DockSide.Right)) _right?.Refresh();
        ConfigureQuickMode();
        RefreshPointerKeyboardTarget();
        NotifyIdleMemoryStateChanged();
    }

    internal void NotifyIdleMemoryStateChanged() => _idleMemory?.Update();

    private void ConfigureQuickMode()
    {
        // Keep the user's preference, but only listen for Ctrl when its chosen
        // dock exists. Reopening that dock restores quick mode automatically.
        bool available = Settings.QuickModeSide switch
        {
            "Right" => _right is not null,
            "Both" => _left is not null || _right is not null,
            _ => _left is not null
        };
        bool enabled = Settings.QuickModeEnabled && available;
        if (_quickMode is not null && (_quickMode.IsEnabled != enabled || _quickMode.Side != Settings.QuickModeSide))
            EndOrdinaryKeyboardSession();
        _quickMode?.Configure(enabled, Settings.QuickModeSide);
        if (_quickMode?.LastError is { Length: > 0 } error) ReportError(error);
    }

    private DockSide? PointerKeyboardTarget()
    {
        if (_exiting || _exitPending) return null;
        // A dock never takes foreground focus on hover. Let ordinary editing in
        // the settings window keep its letters even with the pointer at an edge.
        if (_settingsWindow?.QuickModeWindowHandle is nint handle && handle != 0
            && EDock.Interop.NativeMethods.GetForegroundWindow() == handle
            && _settingsWindow.Content?.XamlRoot is { } root
            && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root)
                is Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.RichEditBox
                or Microsoft.UI.Xaml.Controls.PasswordBox or Microsoft.UI.Xaml.Controls.AutoSuggestBox)
        {
            EndOrdinaryKeyboardSession();
            return null;
        }
        if (_ordinaryKeyboardSide is DockSide sessionSide)
        {
            var target = sessionSide == DockSide.Left ? _left : _right;
            var source = _ordinaryKeyboardSource == DockSide.Left ? _left : _right;
            bool valid = _quickMode?.IsActive != true && target?.IsSessionKeyboardTarget == true && source is not null
                && !(_settingsWindow?.IsQuickModeBusy ?? false) && _tray?.IsMenuOpen != true
                && !(_left?.IsInteractionBusy ?? false) && !(_right?.IsInteractionBusy ?? false)
                && EDock.Interop.NativeMethods.GetForegroundWindow() == _keyboardForeground
                && EDock.Interop.NativeMethods.GetCursorPos(out var point)
                && Math.Abs((long)point.X - _keyboardPointerAnchor.X) <= 8
                && Math.Abs((long)point.Y - _keyboardPointerAnchor.Y) <= 8;
            for (int key = 1; valid && key <= 6; key++)
                if (key != 3 && (EDock.Interop.NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) valid = false;
            if (valid) return sessionSide;
            EndOrdinaryKeyboardSession();
        }
        if (_left?.IsPointerKeyboardTarget == true) return DockSide.Left;
        if (_right?.IsPointerKeyboardTarget == true) return DockSide.Right;
        return null;
    }

    internal void RefreshPointerKeyboardTarget()
    {
        if (_changingKeyboardTarget) return;
        var target = PointerKeyboardTarget();
        if (target != DockSide.Left) _left?.ClearOrdinarySelection();
        if (target != DockSide.Right) _right?.ClearOrdinarySelection();
        _quickMode?.RefreshPointerTarget();
    }

    private DockWindow? QuickTarget()
    {
        if (_quickDockSwitched) return _quickTargetSide == DockSide.Right ? _right ?? _left : _left ?? _right;
        if (_quickMode?.Side == "Right") return _right;
        if (_quickMode?.Side != "Both") return _left;
        if (_right?.IsPointerOverDock == true) _quickTargetSide = DockSide.Right;
        else if (_left?.IsPointerOverDock == true) _quickTargetSide = DockSide.Left;
        return _quickTargetSide == DockSide.Right ? _right ?? _left : _left ?? _right;
    }

    private void UseQuickTarget(Action<DockWindow> action)
    {
        var target = QuickTarget();
        _left?.SetQuickSelectionTarget(target == _left);
        _right?.SetQuickSelectionTarget(target == _right);
        if (target is not null) action(target);
    }

    private void ApplyQuickTargets(bool active, DockWindow? target)
    {
        bool both = _quickMode?.Side == "Both";
        _left?.SetQuickMode(active && (both || target == _left), target == _left);
        _right?.SetQuickMode(active && (both || target == _right), target == _right);
    }

    private void SwitchQuickDock()
    {
        var current = QuickTarget();
        var target = current == _left ? _right : _left;
        if (target is null) return;
        _quickDockSwitched = true;
        _quickTargetSide = target == _right ? DockSide.Right : DockSide.Left;
        ApplyQuickTargets(active: true, target);
    }

    private void SwitchOrdinaryDock(DockSide from)
    {
        if (PointerKeyboardTarget() != from) return;
        var target = from == DockSide.Left ? _right : _left;
        if (target is null || target.IsInteractionBusy) return;
        if (_ordinaryKeyboardSide is null)
        {
            if (!EDock.Interop.NativeMethods.GetCursorPos(out _keyboardPointerAnchor)) return;
            _keyboardForeground = EDock.Interop.NativeMethods.GetForegroundWindow();
            _ordinaryKeyboardSource = from;
        }
        _changingKeyboardTarget = true;
        try
        {
            _ordinaryKeyboardSide = from == DockSide.Left ? DockSide.Right : DockSide.Left;
            // Establish the new hold before releasing the old one so target changes
            // cannot invalidate a half-completed switch through an edge event.
            target.SetOrdinaryKeyboardHold(true);
            (from == DockSide.Left ? _left : _right)?.SetOrdinaryKeyboardHold(false);
            _left?.ClearOrdinarySelection();
            _right?.ClearOrdinarySelection();
            target.BeginOrdinaryKeyboardSelection();
        }
        finally { _changingKeyboardTarget = false; }
        RefreshPointerKeyboardTarget();
    }

    private void EndOrdinaryKeyboardSession()
    {
        if (_ordinaryKeyboardSide is null) return;
        bool wasChanging = _changingKeyboardTarget;
        _changingKeyboardTarget = true;
        _ordinaryKeyboardSide = null;
        _ordinaryKeyboardSource = null;
        try
        {
            _left?.SetOrdinaryKeyboardHold(false);
            _right?.SetOrdinaryKeyboardHold(false);
        }
        finally { _changingKeyboardTarget = wasChanging; }
    }

    public void ExitQuickMode()
    {
        EndOrdinaryKeyboardSession();
        _quickMode?.Exit();
    }

    private static bool SetDock(ref DockWindow? dock, DockSide side, bool enabled)
    {
        if (enabled && dock is null) { dock = new DockWindow(side); return true; }
        else if (!enabled && dock is not null) { dock.Shutdown(); dock = null; }
        return false;
    }

    public List<ShortcutItem> Items(DockSide side) => Settings.GetActiveList(side).Shortcuts;

    public void ReportError(string message)
    {
        LastError = message;
        ShowSettings();
        _settingsWindow?.ShowError(message);
    }

    public void ShowSettings(DockSide? side = null)
    {
        _idleMemory?.NotifyActivity();
        ExitQuickMode();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(side);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                NotifyIdleMemoryStateChanged();
            };
        }
        else if (side.HasValue) _settingsWindow.SelectSide(side.Value);
        _settingsWindow.Activate();
    }

    public async void ExitApplication()
    {
        if (_exiting || _exitPending) return;
        _exitPending = true;
        try
        {
            if (_settingsWindow is { } settingsWindow && !await settingsWindow.FlushPendingChangesAsync()) return;
            _exiting = true;
            EndOrdinaryKeyboardSession();
            _idleMemory?.Dispose();
            _taskbarAppearance?.Dispose();
            _tray?.Dispose();
            _quickMode?.Dispose();
            _left?.Shutdown();
            _right?.Shutdown();
            _settingsWindow?.Close();
            _instance?.Dispose();
            Exit();
        }
        finally { _exitPending = false; }
    }
}
