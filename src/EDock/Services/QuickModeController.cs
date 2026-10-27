using System.ComponentModel;
using System.Runtime.InteropServices;
using EDock.Interop;
using EDock.Models;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

public enum DockKeyboardAction { Previous, Next, PreviousList, NextList, ToggleExpanded, Launch, PreviousColumn, NextColumn, SwitchDock, TogglePinned }
public readonly record struct DockKeyboardRequest(DockSide Side, DockKeyboardAction Action);

/// <summary>Runs user-configured shortcuts on the desktop or the supplied companion window.</summary>
public sealed class QuickModeController : IDisposable
{
    private enum PendingAction { Enter, Leave, Previous, Next, PreviousList, NextList, ToggleExpanded, Launch, PreviousColumn, NextColumn, SwitchDock, PreviousRow, NextRow, TogglePinned }
    private readonly record struct PendingRequest(PendingAction Action, DockSide? PointerSide = null, int PointerGeneration = 0);
    private readonly record struct MovementSource(PendingRequest Request, int QuickGeneration);
    private Dictionary<DockShortcutAction, ShortcutBinding> _bindings = ShortcutBindingCatalog.CreateDefaults();
    private ShortcutBinding _activation = QuickModeActivation.Default();
    private static readonly uint GuiThreadInfoSize = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<bool> _isBusy;
    private readonly Func<nint>? _companionWindow;
    private readonly Func<DockSide?>? _pointerTarget;
    private readonly Predicate<int> _isReleasedKey;
    private readonly NativeMethods.KeyboardHookProc _keyboardHook;
    private readonly NativeMethods.MouseHookProc _mouseHook;
    private readonly Queue<PendingRequest> _pending = new();
    private readonly HashSet<int> _pressedKeys = new();
    // An async snapshot can contain stale keys injected before our hook existed.
    // Keep their held-input protection without treating them as an observed chord.
    private readonly HashSet<int> _initialKeys = new();
    private readonly HashSet<int> _controlKeys = new();
    private readonly HashSet<int> _activationKeys = new();
    private readonly HashSet<int> _suppressedKeys = new();
    private readonly Dictionary<int, MovementSource> _movementSources = new();
    // Hooks run on the installing UI thread; retain delegates if native release fails.
    private static readonly HashSet<QuickModeController> LiveHooks = new();
    private nint _keyboardHandle;
    private nint _mouseHandle;
    private bool _dispatchQueued;
    private bool _draining;
    private bool _enabled;
    private bool _gestureActive;
    private bool _tapCandidate;
    private bool _disposed;
    private bool _timerRunning;
    private bool _dispatchRecoveryNeeded;
    private DockSide? _pointerSide;
    private int _pointerGeneration;
    private int _quickGeneration;
    private int _wheelRemainder;
    private int _timerIntervalMs = 100;
    private int _activationReleaseModifiers;

    public bool IsActive { get; private set; }
    public bool IsEnabled => _enabled;
    public string? LastError { get; private set; }
    public string Side { get; private set; } = "Left";
    public DockSide PreferredSide => Side == "Right" ? DockSide.Right : DockSide.Left;
    public event EventHandler<bool>? ActiveChanged;
    public event EventHandler<int>? SelectionRequested;
    public event EventHandler<int>? VerticalSelectionRequested;
    public event EventHandler<int>? HorizontalSelectionRequested;
    public event EventHandler? DockSwitchRequested;
    public event EventHandler? LaunchRequested;
    public event EventHandler<int>? ListSwitchRequested;
    public event EventHandler? ExpansionRequested;
    public event EventHandler? PinRequested;
    public event EventHandler<DockKeyboardRequest>? DockCommandRequested;

    public QuickModeController(DispatcherQueue dispatcherQueue, Func<bool>? isBusy = null, Func<nint>? companionWindow = null, Func<DockSide?>? pointerTarget = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _dispatcher = dispatcherQueue;
        _isBusy = isBusy ?? (() => false);
        _companionWindow = companionWindow;
        _pointerTarget = pointerTarget;
        _isReleasedKey = IsReleasedKey;
        _keyboardHook = OnKeyboardHook;
        _mouseHook = OnMouseHook;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(_timerIntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        // Hovering an interactive dock temporarily needs only the keyboard hook.
    }

    public void Configure(bool enabled, string side, IDictionary<DockShortcutAction, ShortcutBinding>? bindings = null, ShortcutBinding? activation = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string normalizedSide = side?.ToUpperInvariant() switch
        {
            "RIGHT" => "Right", "BOTH" => "Both", _ => "Left"
        };
        var normalizedBindings = bindings is null ? ShortcutBindingCatalog.CreateDefaults()
            : bindings.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        ShortcutBindingCatalog.Normalize(normalizedBindings);
        var normalizedActivation = QuickModeActivation.Normalize(activation ?? normalizedBindings[DockShortcutAction.ActivateQuickMode]);
        normalizedBindings[DockShortcutAction.ActivateQuickMode] = normalizedActivation.Copy();
        if (_enabled == enabled && Side == normalizedSide && SameBindings(_bindings, normalizedBindings)
            && ShortcutBindingCatalog.SameBinding(_activation, normalizedActivation)) return;
        Exit();
        _pointerGeneration++;
        Side = normalizedSide;
        _bindings = normalizedBindings;
        _activation = normalizedActivation;
        LastError = null;
        ReadInitialKeys();
        _enabled = enabled && InstallHooks();
        UpdatePointerTarget();
        SynchronizeResources();
    }

    private static bool SameBindings(IDictionary<DockShortcutAction, ShortcutBinding> left,
        IDictionary<DockShortcutAction, ShortcutBinding> right) => left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value) && ShortcutBindingCatalog.SameBinding(pair.Value, value));

    /// <summary>Refreshes the eligible ordinary command target, including a caller-managed keyboard session.</summary>
    public void RefreshPointerTarget()
    {
        if (_disposed) return;
        UpdatePointerTarget();
        if (_pointerSide is not null && !CanUsePointerCommands()) _pointerGeneration++;
        SynchronizeResources();
    }

    /// <summary>Ends quick mode and cancels any in-progress activation gesture.</summary>
    public void Exit()
    {
        _gestureActive = false;
        _tapCandidate = false;
        _activationReleaseModifiers = 0;
        _wheelRemainder = 0;
        _pending.Clear();
        _pointerGeneration++;
        _quickGeneration++;
        _movementSources.Clear();
        SetActive(false);
        UpdateTimerCadence();
    }

    private bool InstallHooks()
    {
        try
        {
            InstallKeyboardHook();
            if (_mouseHandle == 0)
            {
                _mouseHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLowLevel, _mouseHook, NativeMethods.GetModuleHandle(null), 0);
                if (_mouseHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "滚轮监听安装失败");
                LiveHooks.Add(this);
            }
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"无法启用快捷模式：{ex.Message}";
            ReleaseHook(ref _mouseHandle, "滚轮");
            if (_pointerSide is null && _suppressedKeys.Count == 0) ReleaseHook(ref _keyboardHandle, "键盘");
            UpdateHookRoots();
            return false;
        }
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHandle != 0) return;
        ReadInitialKeys();
        _keyboardHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLowLevel, _keyboardHook, NativeMethods.GetModuleHandle(null), 0);
        if (_keyboardHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "键盘监听安装失败");
        LiveHooks.Add(this);
    }

    private void SynchronizeResources()
    {
        if (_disposed) return;
        if (!_enabled) ReleaseHook(ref _mouseHandle, "滚轮");
        if (_enabled || _pointerSide is not null || _suppressedKeys.Count != 0)
        {
            try { InstallKeyboardHook(); }
            catch (Exception ex) { LastError = $"无法启用边栏键盘操作：{ex.Message}"; }
        }
        else ReleaseHook(ref _keyboardHandle, "键盘");
        UpdateHookRoots();
        // A swallowed key retains only its matching release listener after hover ends.
        // No reconciliation timer is needed while waiting for that hook event.
        bool runTimer = _enabled || (_pointerSide is not null && _keyboardHandle != 0) || _dispatchRecoveryNeeded;
        UpdateTimerCadence();
        if (runTimer == _timerRunning) return;
        _timerRunning = runTimer;
        if (runTimer) _timer.Start(); else _timer.Stop();
    }

    private bool UpdatePointerTarget()
    {
        DockSide? target = null;
        if (_pointerTarget is not null && !_isBusy()) target = _pointerTarget();
        if (_pointerSide == target) return false;
        _pointerSide = target;
        _pointerGeneration++;
        return true;
    }

    private void ReleaseHooks()
    {
        ReleaseHook(ref _keyboardHandle, "键盘");
        ReleaseHook(ref _mouseHandle, "滚轮");
        UpdateHookRoots();
    }

    private void UpdateHookRoots()
    {
        if (_keyboardHandle != 0 || _mouseHandle != 0) return;
        _suppressedKeys.Clear();
        _movementSources.Clear();
        LiveHooks.Remove(this);
    }

    private void ReleaseHook(ref nint handle, string name)
    {
        if (handle == 0) return;
        try
        {
            if (NativeMethods.UnhookWindowsHookEx(handle)) { handle = 0; return; }
            LastError = $"无法释放快捷模式{name}监听：{new Win32Exception(Marshal.GetLastWin32Error()).Message}";
        }
        catch (Exception ex) { LastError = $"无法释放快捷模式{name}监听：{ex.Message}"; }
    }

    private void ReadInitialKeys()
    {
        int[] observedHeld = _pressedKeys.Where(key => !_initialKeys.Contains(key)
            && (_suppressedKeys.Contains(key) || IsKeyPressed(key))).ToArray();
        _pressedKeys.Clear();
        _initialKeys.Clear();
        _controlKeys.Clear();
        _activationKeys.Clear();
        for (int key = 8; key < 255; key++)
        {
            if (IsControl(key) || !IsKeyPressed(key)) continue;
            // Generic modifier aliases must not remain stuck after their sided key is released.
            if (key == NativeMethods.VkShift && (IsKeyPressed(NativeMethods.VkLeftShift) || IsKeyPressed(NativeMethods.VkRightShift))) continue;
            if (key == NativeMethods.VkMenu && (IsKeyPressed(NativeMethods.VkLeftMenu) || IsKeyPressed(NativeMethods.VkRightMenu))) continue;
            _pressedKeys.Add(key);
            if (!IsModifier(key)) _initialKeys.Add(key);
        }
        // Preserve confirmed downs across reconfiguration. Swallowed downs have
        // no async state, but still need their matching release and chord guard.
        foreach (int key in observedHeld)
        {
            _pressedKeys.Add(key);
            _initialKeys.Remove(key);
        }
        if (IsKeyPressed(NativeMethods.VkLeftControl)) _controlKeys.Add(NativeMethods.VkLeftControl);
        if (IsKeyPressed(NativeMethods.VkRightControl)) _controlKeys.Add(NativeMethods.VkRightControl);
        if (_controlKeys.Count == 0 && IsKeyPressed(NativeMethods.VkControl)) _controlKeys.Add(NativeMethods.VkControl);
        for (int key = 8; key < 255; key++)
        {
            if (!IsActivationKey(key) || !IsKeyPressed(key)) continue;
            if (key == NativeMethods.VkControl && (IsKeyPressed(NativeMethods.VkLeftControl) || IsKeyPressed(NativeMethods.VkRightControl))) continue;
            if (key == NativeMethods.VkShift && (IsKeyPressed(NativeMethods.VkLeftShift) || IsKeyPressed(NativeMethods.VkRightShift))) continue;
            if (key == NativeMethods.VkMenu && (IsKeyPressed(NativeMethods.VkLeftMenu) || IsKeyPressed(NativeMethods.VkRightMenu))) continue;
            _activationKeys.Add(key);
        }
        _tapCandidate = false;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed) return;
        UpdatePointerTarget();
        if (_pointerSide is not null && !CanUsePointerCommands()) _pointerGeneration++;
        // OLE drag loops can omit a key-up. Swallowed downs have no async state,
        // so retain those until their matching hook release arrives.
        _pressedKeys.RemoveWhere(_isReleasedKey);
        _initialKeys.RemoveWhere(_isReleasedKey);
        DrainPending();
        // Reconcile missed Ctrl events without treating polling as a new tap.
        if (IsKeyPressed(NativeMethods.VkControl) != (_controlKeys.Count > 0))
        {
            _tapCandidate = false;
            _controlKeys.Clear();
            if (IsKeyPressed(NativeMethods.VkLeftControl)) _controlKeys.Add(NativeMethods.VkLeftControl);
            if (IsKeyPressed(NativeMethods.VkRightControl)) _controlKeys.Add(NativeMethods.VkRightControl);
            if (_controlKeys.Count == 0 && IsKeyPressed(NativeMethods.VkControl)) _controlKeys.Add(NativeMethods.VkControl);
        }
        if (_activationKeys.RemoveWhere(key => !IsKeyPressed(key)) > 0) _tapCandidate = false;
        if ((CurrentModifiers() & _activationReleaseModifiers) == 0) _activationReleaseModifiers = 0;
        if (!CanUseQuickMode())
        {
            _tapCandidate = false;
            if (_gestureActive || IsActive) Exit();
        }
        DrainPending();
        SynchronizeResources();
    }

    private void ObserveActivation(int key, bool down)
    {
        if (down)
        {
            if (!_activationKeys.Add(key)) return; // A repeated down cannot start another tap.
            if (_activationKeys.Count > 1) { _tapCandidate = false; UpdateTimerCadence(); return; }
            _tapCandidate = _enabled && _activation.Enabled && ActivationModifiersMatch()
                && !HasActivationChordKeys() && !_isBusy() && IsAllowedForeground();
            if (!_tapCandidate && _gestureActive) CancelFromHook();
            UpdateTimerCadence();
            return;
        }
        bool wasHeld = _activationKeys.Remove(key);
        if (!wasHeld) _tapCandidate = false;
        if (_activationKeys.Count != 0) { UpdateTimerCadence(); return; }
        // Releasing a chord's modifiers before its main key is equally valid.
        // The full chord was already verified on that main key's initial down.
        bool toggle = _tapCandidate && _enabled && _activation.Enabled
            && (CurrentModifiers() & ~(ModifierMask(_activation.Modifiers) | ActivationOwnModifier)) == 0
            && !HasActivationChordKeys() && !_isBusy() && IsAllowedForeground();
        _tapCandidate = false;
        if (!toggle) { UpdateTimerCadence(); return; }
        _gestureActive = !_gestureActive;
        _activationReleaseModifiers = ModifierMask(_activation.Modifiers);
        _quickGeneration++;
        _wheelRemainder = 0;
        Enqueue(_gestureActive ? PendingAction.Enter : PendingAction.Leave);
    }

    private nint OnKeyboardHook(int code, nint message, nint data)
    {
        if (code < 0 || _disposed) return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
        try
        {
            int kind = (int)message;
            bool down = kind is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            bool up = kind is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
            if (!down && !up) return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
            int key = (int)Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardData>(data).VirtualKey;
            // OLE may emit VK0 sentinels, which do not represent a pressed key.
            if (key is <= 0 or >= 255) return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
            // The edge timer is intentionally slow while idle. Recheck the actual
            // hovered window here so input cannot reach a hidden or different dock.
            if (UpdatePointerTarget()) ScheduleDrain();
            if (IsControl(key))
            {
                if (down) _controlKeys.Add(key); else _controlKeys.Remove(key);
            }
            if (_activation.Enabled && IsActivationKey(key) && (ActivationModifiersMatch() || _activationKeys.Contains(key)))
            {
                if (down) _pointerGeneration++;
                ObserveActivation(key, down);
                return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
            }
            bool previouslyPressed = _pressedKeys.Contains(key);
            _initialKeys.Remove(key);
            if (down) _pressedKeys.Add(key); else _pressedKeys.Remove(key);
            // A required modifier may be released before the activation's main
            // key. Every other event invalidates that candidate permanently.
            if (!(up && _activationKeys.Count != 0 && IsModifier(key)
                && (KeyModifierMask(ModifierName(key)) & ModifierMask(_activation.Modifiers)) != 0))
                _tapCandidate = false;
            UpdateTimerCadence();
            if (IsModifier(key) && down) _pointerGeneration++;
            if (up && _suppressedKeys.Remove(key))
            {
                _movementSources.Remove(key);
                if (!_enabled && _pointerSide is null) ScheduleDrain();
                return 1;
            }
            if (down && _suppressedKeys.Contains(key))
            {
                if (_pointerSide is not null && !CanUsePointerCommands()) _pointerGeneration++;
                RepeatMovement(key);
                return 1;
            }
            if (_enabled && (_gestureActive || IsActive))
            {
                if (!CanUseQuickMode())
                {
                    CancelFromHook();
                    return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
                }
                if (!_gestureActive || !down)
                    return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
                if (!TryGetAction(key, out var quickAction))
                {
                    if (!IsModifier(key) && CurrentModifiers() != 0) CancelFromHook();
                    return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
                }
                // A down that predated quick mode keeps its original repeat/up
                // stream. Only a new press can become a quick-mode command.
                if (previouslyPressed)
                    return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
                _suppressedKeys.Add(key);
                if (quickAction == PendingAction.Launch)
                {
                    _gestureActive = false;
                    _wheelRemainder = 0;
                }
                if (HasOppositeKeys(quickAction)) CancelPendingGroup(quickAction);
                else
                {
                    if (IsMovement(quickAction)) _movementSources[key] = new(new(quickAction), _quickGeneration);
                    Enqueue(quickAction);
                }
                return 1;
            }
            // A held key that entered the dock keeps its original down/repeat/up
            // stream. Only a fresh unmodified press starts a normal dock command.
            if (!down || previouslyPressed || _pointerSide is null || !CanUsePointerCommands() || !TryGetAction(key, out var action))
                return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
            _suppressedKeys.Add(key);
            if (HasOppositeKeys(action)) CancelPendingGroup(action);
            else
            {
                if (IsMovement(action)) _movementSources[key] = new(new(action, _pointerSide, _pointerGeneration), 0);
                Enqueue(action, _pointerSide, _pointerGeneration);
            }
            return 1;
        }
        catch (Exception ex)
        {
            LastError = $"快捷模式键盘监听失败：{ex.Message}";
            CancelFromHook();
            return NativeMethods.CallNextHookEx(_keyboardHandle, code, message, data);
        }
    }

    private nint OnMouseHook(int code, nint message, nint data)
    {
        if (code < 0 || _disposed) return NativeMethods.CallNextHookEx(_mouseHandle, code, message, data);
        try
        {
            int kind = (int)message;
            if (kind is >= NativeMethods.WmLeftButtonDown and <= NativeMethods.WmMouseHorizontalWheel)
            {
                _tapCandidate = false;
                UpdateTimerCadence();
            }
            if (!_enabled || !_gestureActive || kind != NativeMethods.WmMouseWheel)
                return NativeMethods.CallNextHookEx(_mouseHandle, code, message, data);
            if (!CanUseQuickMode() || CurrentModifiers() != 0 || IsActivationHeld())
            {
                CancelFromHook();
                return NativeMethods.CallNextHookEx(_mouseHandle, code, message, data);
            }
            uint wheelData = Marshal.PtrToStructure<NativeMethods.LowLevelMouseData>(data).MouseData;
            _wheelRemainder += unchecked((short)(wheelData >> 16));
            while (_wheelRemainder >= 120) { _wheelRemainder -= 120; Enqueue(PendingAction.Previous); }
            while (_wheelRemainder <= -120) { _wheelRemainder += 120; Enqueue(PendingAction.Next); }
            return 1;
        }
        catch (Exception ex)
        {
            LastError = $"快捷模式滚轮监听失败：{ex.Message}";
            CancelFromHook();
            return NativeMethods.CallNextHookEx(_mouseHandle, code, message, data);
        }
    }

    private void CancelFromHook()
    {
        _gestureActive = false;
        _tapCandidate = false;
        _wheelRemainder = 0;
        _pending.Clear();
        _quickGeneration++;
        _movementSources.Clear();
        Enqueue(PendingAction.Leave);
    }

    private static bool IsMovement(PendingAction action) => action is PendingAction.PreviousRow or PendingAction.NextRow
        or PendingAction.PreviousColumn or PendingAction.NextColumn;

    private void RepeatMovement(int key)
    {
        if (!_movementSources.TryGetValue(key, out var source)) return;
        PendingRequest request = source.Request;
        if (HasOppositeKeys(request.Action)) { CancelPendingGroup(request.Action); return; }
        if (request.PointerSide is DockSide side)
        {
            if (_gestureActive || IsActive || request.PointerGeneration != _pointerGeneration
                || _pointerSide != side || !CanUsePointerCommands() || !BindingStillMatches(key))
            {
                _movementSources.Remove(key);
                CancelPendingGroup(request.Action);
                return;
            }
            Enqueue(request.Action, side, request.PointerGeneration);
        }
        else
        {
            if (!_enabled || !_gestureActive || source.QuickGeneration != _quickGeneration
                || !CanUseQuickMode() || !BindingStillMatches(key))
            {
                _movementSources.Remove(key);
                if (_gestureActive || IsActive) CancelFromHook();
                return;
            }
            Enqueue(request.Action);
        }
    }

    private bool TryGetAction(int key, out PendingAction action)
    {
        action = PendingAction.Leave;
        foreach (var pair in _bindings)
        {
            if (pair.Key == DockShortcutAction.ActivateQuickMode) continue;
            if (!pair.Value.Enabled || !MatchesKey(key, pair.Value)) continue;
            action = pair.Key switch
            {
                DockShortcutAction.PreviousRow => PendingAction.PreviousRow,
                DockShortcutAction.NextRow => PendingAction.NextRow,
                DockShortcutAction.PreviousColumn => PendingAction.PreviousColumn,
                DockShortcutAction.NextColumn => PendingAction.NextColumn,
                DockShortcutAction.PreviousList => PendingAction.PreviousList,
                DockShortcutAction.NextList => PendingAction.NextList,
                DockShortcutAction.ToggleExpanded => PendingAction.ToggleExpanded,
                DockShortcutAction.TogglePinned => PendingAction.TogglePinned,
                DockShortcutAction.SwitchDock => PendingAction.SwitchDock,
                DockShortcutAction.Launch => PendingAction.Launch,
                _ => PendingAction.Leave
            };
            return action != PendingAction.Leave;
        }
        return false;
    }

    private bool MatchesKey(int key, ShortcutBinding binding)
    {
        return KeyMatches(key, binding.Key) && ModifierMask(binding.Modifiers) == (CurrentModifiers() & ~KeyModifierMask(binding.Key));
    }

    private static bool KeyMatches(int key, string bindingKey) => bindingKey switch
    {
        "Ctrl" => IsControl(key),
        "Shift" => key is NativeMethods.VkShift or NativeMethods.VkLeftShift or NativeMethods.VkRightShift,
        "Alt" => key is NativeMethods.VkMenu or NativeMethods.VkLeftMenu or NativeMethods.VkRightMenu,
        "Win" => key is NativeMethods.VkLeftWindows or NativeMethods.VkRightWindows,
        _ => KeyCode(bindingKey) == key
    };

    private bool IsAnyPressed(int first, int second = 0, int third = 0) => IsHeld(first)
        || second != 0 && IsHeld(second) || third != 0 && IsHeld(third);
    private bool IsHeld(int key) => _pressedKeys.Contains(key) || IsKeyPressed(key);
    private static int KeyCode(string key) => ShortcutBindingCatalog.VirtualKeyCode(key);

    private bool BindingStillMatches(int key)
    {
        foreach (var pair in _bindings)
            if (pair.Key != DockShortcutAction.ActivateQuickMode && pair.Value.Enabled && MatchesKey(key, pair.Value)) return true;
        return false;
    }

    private bool HasOppositeKeys(PendingAction action)
    {
        var (first, second) = ActionGroup(action) switch
        {
            1 => (DockShortcutAction.PreviousRow, DockShortcutAction.NextRow),
            2 => (DockShortcutAction.PreviousColumn, DockShortcutAction.NextColumn),
            3 => (DockShortcutAction.PreviousList, DockShortcutAction.NextList),
            _ => (DockShortcutAction.ActivateQuickMode, DockShortcutAction.ActivateQuickMode)
        };
        return first != second && BindingHeld(first) && BindingHeld(second);
    }

    private bool BindingHeld(DockShortcutAction action)
    {
        if (!_bindings.TryGetValue(action, out var binding) || !binding.Enabled) return false;
        foreach (int key in _pressedKeys)
            if (MatchesKey(key, binding)) return true;
        return false;
    }

    private void CancelPendingGroup(PendingAction conflict)
    {
        int group = ActionGroup(conflict);
        foreach (var source in _movementSources.Where(pair => ActionGroup(pair.Value.Request.Action) == group).Select(pair => pair.Key).ToArray())
            _movementSources.Remove(source);
        // Remember a conflict when it happens, even if both keys are released
        // before the dispatcher runs. Preserve entry/exit and unrelated commands.
        int count = _pending.Count;
        for (int i = 0; i < count; i++)
        {
            PendingRequest request = _pending.Dequeue();
            bool matches = ActionGroup(request.Action) == group;
            if (!matches) _pending.Enqueue(request);
        }
    }

    private static int ActionGroup(PendingAction action) => action switch
    {
        PendingAction.PreviousRow or PendingAction.NextRow => 1,
        PendingAction.PreviousColumn or PendingAction.NextColumn => 2,
        PendingAction.PreviousList or PendingAction.NextList => 3,
        _ => 0
    };

    private void Enqueue(PendingAction action, DockSide? pointerSide = null, int generation = 0)
    {
        _pending.Enqueue(new(action, pointerSide, generation));
        UpdateTimerCadence();
        ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        if (_dispatchQueued || _draining) return;
        _dispatchQueued = true;
        if (_dispatcher.TryEnqueue(DrainPending)) return;
        _dispatchQueued = false;
        _pending.Clear();
        _gestureActive = false;
        _tapCandidate = false;
        _pointerGeneration++;
        _quickGeneration++;
        _movementSources.Clear();
        _pending.Enqueue(new(PendingAction.Leave)); // The timer can still retire published UI state.
        _dispatchRecoveryNeeded = true;
        // A failed enqueue can occur inside the native callback. Let the timer
        // retire hooks after that callback returns, rather than unhooking here.
        if (!_timerRunning) { _timerRunning = true; _timer.Start(); }
    }

    private void DrainPending()
    {
        if (_draining) return;
        _dispatchQueued = false;
        if (_disposed) { _pending.Clear(); return; }
        _draining = true;
        _dispatchRecoveryNeeded = false;
        try
        {
            UpdatePointerTarget();
            while (_pending.TryDequeue(out var request))
            {
                PendingAction action = request.Action;
                if (request.PointerSide is DockSide side)
                {
                    // Each command belongs to the side and hover session that
                    // swallowed its down. Never retarget a queued command.
                    UpdatePointerTarget();
                    if (!CanUsePointerCommands()) _pointerGeneration++;
                    if (!_gestureActive && !IsActive && request.PointerGeneration == _pointerGeneration
                        && _pointerSide == side && !HasOppositeKeys(action))
                        DockCommandRequested?.Invoke(this, new(side, ToDockAction(action)));
                    continue;
                }
                if (action != PendingAction.Leave && (!_enabled || !CanUseQuickMode()))
                {
                    Exit();
                    break;
                }
                switch (action)
                {
                    case PendingAction.Enter: SetActive(true); break;
                    case PendingAction.Leave: SetActive(false); break;
                    case PendingAction.Previous:
                    case PendingAction.Next:
                        if (IsActive)
                            SelectionRequested?.Invoke(this, action == PendingAction.Previous ? -1 : 1);
                        break;
                    case PendingAction.PreviousRow:
                    case PendingAction.NextRow:
                        if (IsActive && !HasOppositeKeys(action))
                            VerticalSelectionRequested?.Invoke(this, action == PendingAction.PreviousRow ? -1 : 1);
                        break;
                    case PendingAction.PreviousColumn:
                    case PendingAction.NextColumn:
                        if (IsActive && !HasOppositeKeys(action))
                            HorizontalSelectionRequested?.Invoke(this, action == PendingAction.PreviousColumn ? -1 : 1);
                        break;
                    case PendingAction.SwitchDock:
                        if (IsActive)
                        {
                            _quickGeneration++;
                            _movementSources.Clear();
                            CancelPendingGroup(PendingAction.PreviousRow);
                            CancelPendingGroup(PendingAction.PreviousColumn);
                            DockSwitchRequested?.Invoke(this, EventArgs.Empty);
                        }
                        break;
                    case PendingAction.PreviousList:
                    case PendingAction.NextList:
                        if (IsActive && !HasOppositeKeys(action)) ListSwitchRequested?.Invoke(this, action == PendingAction.PreviousList ? -1 : 1);
                        break;
                    case PendingAction.ToggleExpanded:
                        if (IsActive) ExpansionRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    case PendingAction.TogglePinned:
                        if (IsActive) PinRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    case PendingAction.Launch:
                        if (!IsActive) break;
                        // Preserve the selected tile until the launch callback has read it.
                        try { LaunchRequested?.Invoke(this, EventArgs.Empty); }
                        finally { SetActive(false); }
                        break;
                }
            }
        }
        finally { _draining = false; SynchronizeResources(); }
    }

    private static DockKeyboardAction ToDockAction(PendingAction action) => action switch
    {
        PendingAction.PreviousRow => DockKeyboardAction.Previous,
        PendingAction.NextRow => DockKeyboardAction.Next,
        PendingAction.PreviousColumn => DockKeyboardAction.PreviousColumn,
        PendingAction.NextColumn => DockKeyboardAction.NextColumn,
        PendingAction.SwitchDock => DockKeyboardAction.SwitchDock,
        PendingAction.PreviousList => DockKeyboardAction.PreviousList,
        PendingAction.NextList => DockKeyboardAction.NextList,
        PendingAction.ToggleExpanded => DockKeyboardAction.ToggleExpanded,
        PendingAction.TogglePinned => DockKeyboardAction.TogglePinned,
        _ => DockKeyboardAction.Launch
    };

    private void UpdateTimerCadence()
    {
        // Hooks deliver normal input immediately. Polling only reconciles lost
        // releases/foreground changes and backs up failed dispatcher enqueues.
        int intervalMs = IsActive || _gestureActive || _tapCandidate || _pending.Count > 0 ? 16 : 100;
        if (_timerIntervalMs == intervalMs) return;
        _timerIntervalMs = intervalMs;
        _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
    }

    private bool IsReleasedKey(int key) => !_suppressedKeys.Contains(key) && !IsKeyPressed(key);

    private bool CanUseQuickMode()
    {
        return !_isBusy() && ModifiersAllowed() && IsAllowedForeground();
    }

    private static int ModifierMask(string modifiers) => (modifiers.Contains("Ctrl", StringComparison.Ordinal) ? 1 : 0)
        | (modifiers.Contains("Shift", StringComparison.Ordinal) ? 2 : 0)
        | (modifiers.Contains("Alt", StringComparison.Ordinal) ? 4 : 0)
        | (modifiers.Contains("Win", StringComparison.Ordinal) ? 8 : 0);

    private int CurrentModifiers() => (HasControl() ? 1 : 0)
        | (IsAnyPressed(NativeMethods.VkShift, NativeMethods.VkLeftShift, NativeMethods.VkRightShift) ? 2 : 0)
        | (IsAnyPressed(NativeMethods.VkMenu, NativeMethods.VkLeftMenu, NativeMethods.VkRightMenu) ? 4 : 0)
        | (IsAnyPressed(NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows) ? 8 : 0);

    private static int KeyModifierMask(string key) => key switch { "Ctrl" => 1, "Shift" => 2, "Alt" => 4, "Win" => 8, _ => 0 };
    private static string ModifierName(int key) => IsControl(key) ? "Ctrl"
        : key is NativeMethods.VkShift or NativeMethods.VkLeftShift or NativeMethods.VkRightShift ? "Shift"
        : key is NativeMethods.VkMenu or NativeMethods.VkLeftMenu or NativeMethods.VkRightMenu ? "Alt"
        : key is NativeMethods.VkLeftWindows or NativeMethods.VkRightWindows ? "Win" : string.Empty;
    private int ActivationOwnModifier => KeyModifierMask(_activation.Key);

    private bool ActivationModifiersMatch() => (CurrentModifiers() & ~ActivationOwnModifier) == ModifierMask(_activation.Modifiers);

    private bool HasActivationChordKeys()
    {
        int required = ModifierMask(_activation.Modifiers);
        foreach (int key in _pressedKeys)
        {
            if (_initialKeys.Contains(key) || IsActivationKey(key)) continue;
            int bit = IsControl(key) ? 1 : key is NativeMethods.VkShift or NativeMethods.VkLeftShift or NativeMethods.VkRightShift ? 2
                : key is NativeMethods.VkMenu or NativeMethods.VkLeftMenu or NativeMethods.VkRightMenu ? 4
                : key is NativeMethods.VkLeftWindows or NativeMethods.VkRightWindows ? 8 : 0;
            if (bit != 0 && (required & bit) != 0) continue;
            return true;
        }
        return (CurrentModifiers() & ~(required | ActivationOwnModifier)) != 0;
    }

    private bool ModifiersAllowed()
    {
        int current = CurrentModifiers();
        if (current == 0 || _activationReleaseModifiers != 0 && (current & ~_activationReleaseModifiers) == 0) return true;
        if (_tapCandidate && _activation.Enabled && ActivationModifiersMatch()) return true;
        foreach (var pair in _bindings)
        {
            if (pair.Key == DockShortcutAction.ActivateQuickMode || !pair.Value.Enabled) continue;
            int required = ModifierMask(pair.Value.Modifiers) | KeyModifierMask(pair.Value.Key);
            if (required != 0 && (current & ~required) == 0) return true;
        }
        // The configured activation may begin with its modifiers, before its main key.
        int activationRequired = _activation.Enabled ? ModifierMask(_activation.Modifiers) : 0;
        return activationRequired != 0 && (current & ~activationRequired) == 0;
    }

    private bool HasControl() => _controlKeys.Count != 0 || _pressedKeys.Contains(NativeMethods.VkControl)
        || _pressedKeys.Contains(NativeMethods.VkLeftControl) || _pressedKeys.Contains(NativeMethods.VkRightControl)
        || IsKeyPressed(NativeMethods.VkControl);

    private bool IsActivationKey(int key) => QuickModeActivation.NormalizeKey(_activation.Key) switch
    {
        "Ctrl" => IsControl(key),
        "Alt" => key is NativeMethods.VkMenu or NativeMethods.VkLeftMenu or NativeMethods.VkRightMenu,
        "Shift" => key is NativeMethods.VkShift or NativeMethods.VkLeftShift or NativeMethods.VkRightShift,
        "Win" => key is NativeMethods.VkLeftWindows or NativeMethods.VkRightWindows,
        _ => KeyCode(_activation.Key) == key
    };

    private bool IsActivationHeld() => _activation.Enabled && (IsActivationKey(NativeMethods.VkControl) && _controlKeys.Count > 0
        || QuickModeActivation.NormalizeKey(_activation.Key) switch
        {
            "Ctrl" => IsKeyPressed(NativeMethods.VkControl),
            "Alt" => IsAnyPressed(NativeMethods.VkMenu, NativeMethods.VkLeftMenu, NativeMethods.VkRightMenu),
            "Shift" => IsAnyPressed(NativeMethods.VkShift, NativeMethods.VkLeftShift, NativeMethods.VkRightShift),
            "Win" => IsAnyPressed(NativeMethods.VkLeftWindows, NativeMethods.VkRightWindows),
            _ => IsAnyPressed(KeyCode(_activation.Key))
        });

    private bool CanUsePointerCommands()
    {
        if (_isBusy() || !ModifiersAllowed()) return false;
        var gui = new NativeMethods.GuiThreadInfo { Size = GuiThreadInfoSize };
        return NativeMethods.GetGUIThreadInfo(0, ref gui) && gui.Capture == 0
            && (gui.Flags & NativeMethods.GuiInteractionModes) == 0;
    }

    private static bool IsControl(int key) => key is NativeMethods.VkControl or NativeMethods.VkLeftControl or NativeMethods.VkRightControl;
    private static bool IsModifier(int key) => IsControl(key) || key is NativeMethods.VkShift or NativeMethods.VkLeftShift or NativeMethods.VkRightShift
        or NativeMethods.VkMenu or NativeMethods.VkLeftMenu or NativeMethods.VkRightMenu
        or NativeMethods.VkLeftWindows or NativeMethods.VkRightWindows;

    private bool IsAllowedForeground()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || NativeMethods.IsIconic(foreground)) return false;
        if (WindowClassReader.Read(foreground) == BackgroundWindowKind.Desktop) return true;
        // Match this instance's cached HWND, never a shared WinUI class or title.
        if (_companionWindow is null || foreground != _companionWindow() || !NativeMethods.IsWindowEnabled(foreground)) return false;
        var gui = new NativeMethods.GuiThreadInfo { Size = GuiThreadInfoSize };
        // A system menu or native move/resize loop can retain the owner's HWND.
        // This stack-only query is limited to the explicitly allowed window.
        return NativeMethods.GetGUIThreadInfo(0, ref gui) && gui.Active == foreground
            && gui.Capture == 0 && (gui.Flags & NativeMethods.GuiInteractionModes) == 0;
    }

    private static bool IsKeyPressed(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        UpdateTimerCadence();
        ActiveChanged?.Invoke(this, active);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
        ReleaseHooks();
        Exit();
    }
}

