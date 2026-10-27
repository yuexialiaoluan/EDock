using System.ComponentModel;
using System.Runtime.InteropServices;
using EDock.Interop;
using EDock.Models;
using Microsoft.UI.Dispatching;

namespace EDock.Services;

/// <summary>Owns keyboard capture only while a shortcut row is being edited.</summary>
internal sealed class ShortcutKeyRecorder : IDisposable
{
    private static readonly HashSet<ShortcutKeyRecorder> LiveRecorders = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<nint> _ownerWindow;
    private readonly Action<ShortcutBinding> _captured;
    private readonly Action<bool> _finished;
    private readonly NativeMethods.KeyboardHookProc _keyboardHook;
    private readonly HashSet<int> _held = new();
    private readonly HashSet<int> _suppressed = new();
    private nint _hook;
    private int _generation;
    private bool _recording;
    private bool _finishing;
    private bool _disposed;

    public string? LastError { get; private set; }
    public bool IsRecording => _recording;

    public ShortcutKeyRecorder(DispatcherQueue dispatcher, Func<nint> ownerWindow,
        Action<ShortcutBinding> captured, Action<bool> finished)
    {
        _dispatcher = dispatcher;
        _ownerWindow = ownerWindow;
        _captured = captured;
        _finished = finished;
        _keyboardHook = OnKeyboardHook;
    }

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recording) return true;
        LastError = null;
        try
        {
            if (_hook == 0)
            {
                _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLowLevel,
                    _keyboardHook, NativeMethods.GetModuleHandle(null), 0);
                if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                LiveRecorders.Add(this);
            }
            _generation++;
            _held.Clear();
            foreach (int key in _suppressed) _held.Add(key);
            foreach (int key in new[] { 0xA2, 0xA3, 0xA0, 0xA1, 0xA4, 0xA5, 0x5B, 0x5C })
                if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) _held.Add(key);
            _recording = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"无法录入快捷键：{ex.Message}";
            ReleaseIfIdle();
            return false;
        }
    }

    /// <summary>Ends capture; swallowed key releases drain before the hook is removed.</summary>
    public void Stop()
    {
        _recording = false;
        _finishing = false;
        _generation++;
        ReleaseIfIdle();
    }

    private nint OnKeyboardHook(int code, nint message, nint data)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        bool down = message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown;
        bool up = message == NativeMethods.WmKeyUp || message == NativeMethods.WmSysKeyUp;
        if (!down && !up) return NativeMethods.CallNextHookEx(_hook, code, message, data);
        try
        {
            int key = (int)Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardData>(data).VirtualKey;
            if (up)
            {
                _held.Remove(key);
                bool swallowed = _suppressed.Remove(key);
                ReleaseIfIdle();
                if (swallowed) return 1;
                return NativeMethods.CallNextHookEx(_hook, code, message, data);
            }

            if (!_recording)
            {
                // Windows may repeat Enter before its release. Do not let that repeat
                // activate the focused row again after a successful confirmation.
                if (_suppressed.Contains(key)) return 1;
                return NativeMethods.CallNextHookEx(_hook, code, message, data);
            }

            nint owner = _ownerWindow();
            if (owner == 0 || NativeMethods.GetForegroundWindow() != owner)
            {
                Finish(false);
                return NativeMethods.CallNextHookEx(_hook, code, message, data);
            }

            _suppressed.Add(key);
            if (!_held.Add(key)) return 1;
            if (key is 0x0D or 0x1B)
            {
                Finish(key == 0x0D);
                return 1;
            }

            string name = KeyName(key);
            if (name.Length > 0)
            {
                var binding = ShortcutBindingCatalog.NormalizeBinding(new ShortcutBinding
                {
                    Enabled = true,
                    Key = name,
                    Modifiers = Modifiers()
                });
                if (!binding.Enabled) return 1;
                int generation = _generation;
                if (!_dispatcher.TryEnqueue(() =>
                {
                    if (!_disposed && generation == _generation) _captured(binding);
                })) Finish(false);
            }
            return 1;
        }
        catch
        {
            // A native hook must always return promptly, including on window teardown.
            Finish(false);
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }
    }

    private string Modifiers()
    {
        var modifiers = new List<string>(4);
        if (_held.Any(key => key is 0x11 or 0xA2 or 0xA3)) modifiers.Add("Ctrl");
        if (_held.Any(key => key is 0x10 or 0xA0 or 0xA1)) modifiers.Add("Shift");
        if (_held.Any(key => key is 0x12 or 0xA4 or 0xA5)) modifiers.Add("Alt");
        if (_held.Any(key => key is 0x5B or 0x5C)) modifiers.Add("Win");
        return string.Join('+', modifiers);
    }

    private void Finish(bool accept)
    {
        if (!_recording) return;
        _recording = false;
        _finishing = true;
        int generation = _generation;
        bool queued = _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || generation != _generation) return;
            try { _finished(accept); }
            finally
            {
                // A validation failure (for example a duplicate key) leaves the row
                // editing. Its owner ends an accepted edit with Stop/Dispose.
                if (!_disposed && generation == _generation)
                {
                    _finishing = false;
                    _recording = accept;
                    ReleaseIfIdle();
                }
            }
        });
        if (!queued) _finishing = false;
        ReleaseIfIdle();
    }

    private void ReleaseIfIdle()
    {
        if (_recording || _finishing || _suppressed.Count != 0 || _hook == 0) return;
        try
        {
            if (!NativeMethods.UnhookWindowsHookEx(_hook)) return;
            _hook = 0;
            _held.Clear();
            LiveRecorders.Remove(this);
        }
        catch { /* Retain the delegate if Windows has not released its callback. */ }
    }

    private static string KeyName(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x60 and <= 0x69 => "NumPad" + (key - 0x60),
        >= 0x70 and <= 0x87 => "F" + (key - 0x70 + 1),
        0x08 => "Backspace", 0x09 => "Tab", 0x13 => "Pause", 0x14 => "CapsLock",
        0x20 => "Space", 0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
        0x2C => "PrintScreen", 0x2D => "Insert", 0x2E => "Delete",
        0x11 or 0xA2 or 0xA3 => "Ctrl", 0x10 or 0xA0 or 0xA1 => "Shift",
        0x12 or 0xA4 or 0xA5 => "Alt", 0x5B or 0x5C => "Win", 0x5D => "Apps",
        0x6A => "Multiply", 0x6B => "Add", 0x6C => "Separator", 0x6D => "Subtract", 0x6E => "Decimal", 0x6F => "Divide",
        0x90 => "NumLock", 0x91 => "ScrollLock",
        0xBA => "OemSemicolon", 0xBB => "OemPlus", 0xBC => "OemComma", 0xBD => "OemMinus",
        0xBE => "OemPeriod", 0xBF => "OemQuestion", 0xC0 => "OemTilde",
        0xDB => "OemOpenBrackets", 0xDC => "OemPipe", 0xDD => "OemCloseBrackets", 0xDE => "OemQuotes",
        0xE2 => "Oem102",
        _ => string.Empty
    };

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
