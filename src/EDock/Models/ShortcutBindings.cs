using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace EDock.Models;

public enum DockShortcutAction
{
    ActivateQuickMode,
    PreviousRow,
    NextRow,
    PreviousColumn,
    NextColumn,
    PreviousList,
    NextList,
    ToggleExpanded,
    TogglePinned,
    SwitchDock,
    Launch
}

public sealed class ShortcutBinding
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = string.Empty;
    public string Modifiers { get; set; } = string.Empty;

    public ShortcutBinding Copy() => new() { Enabled = Enabled, Key = Key, Modifiers = Modifiers };
}

public static class QuickModeActivation
{
    public static ShortcutBinding Default() => new() { Key = "Ctrl" };
    public static ShortcutBinding Normalize(ShortcutBinding? binding) => ShortcutBindingCatalog.NormalizeBinding(binding ?? Default());
    public static string NormalizeKey(string? key) => ShortcutBindingCatalog.NormalizeKey(key);
    public static string Display(ShortcutBinding binding) => ShortcutBindingCatalog.Display(binding);
}

public static class ShortcutBindingCatalog
{
    private static readonly IReadOnlyDictionary<DockShortcutAction, ShortcutBinding> Defaults =
        new ReadOnlyDictionary<DockShortcutAction, ShortcutBinding>(new Dictionary<DockShortcutAction, ShortcutBinding>
        {
            [DockShortcutAction.ActivateQuickMode] = new() { Key = "Ctrl" },
            [DockShortcutAction.PreviousRow] = new() { Key = "W" },
            [DockShortcutAction.NextRow] = new() { Key = "S" },
            [DockShortcutAction.PreviousColumn] = new() { Key = "A" },
            [DockShortcutAction.NextColumn] = new() { Key = "D" },
            [DockShortcutAction.PreviousList] = new() { Key = "Q" },
            [DockShortcutAction.NextList] = new() { Key = "E" },
            [DockShortcutAction.ToggleExpanded] = new() { Key = "F" },
            [DockShortcutAction.TogglePinned] = new() { Key = "G" },
            [DockShortcutAction.SwitchDock] = new() { Key = "Tab" },
            [DockShortcutAction.Launch] = new() { Key = "Space" }
        });

    public static IReadOnlyList<DockShortcutAction> Actions { get; } = Enum.GetValues<DockShortcutAction>();
    public static ShortcutBinding Default(DockShortcutAction action) => Defaults[action].Copy();
    public static Dictionary<DockShortcutAction, ShortcutBinding> CreateDefaults() => Actions.ToDictionary(action => action, Default);

    // Normalization returns a copy: comparing or displaying a draft must not edit live settings.
    public static ShortcutBinding NormalizeBinding(ShortcutBinding? binding)
    {
        var result = binding?.Copy() ?? new ShortcutBinding();
        result.Key = NormalizeKey(result.Key);
        result.Modifiers = string.Join('+', NormalizeModifiers(result.Modifiers).Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Where(modifier => modifier != result.Key));
        if (VirtualKeyCode(result.Key) == 0) result.Enabled = false;
        return result;
    }

    public static void Normalize(IDictionary<DockShortcutAction, ShortcutBinding>? bindings)
    {
        if (bindings is null) return;
        foreach (var action in Actions)
            bindings[action] = NormalizeBinding(bindings.TryGetValue(action, out var binding) && binding is not null ? binding : Default(action));
    }

    public static string NormalizeKey(string? key)
    {
        key = (key ?? string.Empty).Trim();
        string upper = key.ToUpperInvariant();
        if (key.Length == 1) return key == "+" ? "=" : upper;
        if (upper.StartsWith("NUMBER", StringComparison.Ordinal) && upper.Length == 7 && char.IsAsciiDigit(upper[6])) return upper[6].ToString();
        if (upper.StartsWith("D", StringComparison.Ordinal) && upper.Length == 2 && char.IsAsciiDigit(upper[1])) return upper[1].ToString();
        if (upper.StartsWith("NUMPAD", StringComparison.Ordinal) && upper.Length == 7 && char.IsAsciiDigit(upper[6])) return "NumPad" + upper[6];
        if (upper.StartsWith("F", StringComparison.Ordinal) && int.TryParse(upper.AsSpan(1), out int function) && function is >= 1 and <= 24) return "F" + function;
        return upper switch
        {
            "CTRL" or "CONTROL" or "LEFTCTRL" or "RIGHTCTRL" or "LEFTCONTROL" or "RIGHTCONTROL" or "LCONTROLKEY" or "RCONTROLKEY" or "CONTROLKEY" or "CTRL+CONTROL" or "CONTROL+CTRL" or "CTRL+CTRL" => "Ctrl",
            "ALT" or "MENU" or "LEFTMENU" or "RIGHTMENU" or "LEFTALT" or "RIGHTALT" or "LMENU" or "RMENU" => "Alt",
            "SHIFT" or "LEFTSHIFT" or "RIGHTSHIFT" or "SHIFTKEY" or "LSHIFTKEY" or "RSHIFTKEY" => "Shift",
            "WIN" or "WINDOWS" or "LEFTWINDOWS" or "RIGHTWINDOWS" or "LWIN" or "RWIN" => "Win",
            "SPACEBAR" or "SPACE" => "Space",
            "ESC" or "ESCAPE" => "Escape",
            "RETURN" or "ENTER" => "Enter",
            "TAB" => "Tab",
            "LEFT" => "Left", "RIGHT" => "Right", "UP" => "Up", "DOWN" => "Down",
            "HOME" => "Home", "END" => "End", "PAGEUP" or "PRIOR" => "PageUp", "PAGEDOWN" or "NEXT" => "PageDown",
            "INSERT" or "INS" => "Insert", "DELETE" or "DEL" => "Delete", "BACK" or "BACKSPACE" => "Backspace",
            "CAPITAL" or "CAPSLOCK" => "CapsLock", "NUMLOCK" or "NUMBERKEYSLOCK" => "NumLock", "SCROLL" or "SCROLLLOCK" => "ScrollLock",
            "PAUSE" => "Pause", "SNAPSHOT" or "PRINTSCREEN" => "PrintScreen", "APPS" or "APPLICATION" => "Apps",
            "MULTIPLY" => "Multiply", "ADD" => "Add", "SUBTRACT" => "Subtract", "DECIMAL" => "Decimal", "DIVIDE" => "Divide",
            "OEM1" or "OEMSEMICOLON" => ";", "OEMPLUS" => "=", "OEMCOMMA" => ",", "OEMMINUS" => "-", "OEMPERIOD" => ".",
            "OEM2" or "OEMQUESTION" => "/", "OEM3" or "OEMTILDE" => "`", "OEM4" or "OEMOPENBRACKETS" => "[",
            "OEM5" or "OEMPIPE" => "\\", "OEM6" or "OEMCLOSEBRACKETS" => "]", "OEM7" or "OEMQUOTES" => "'", "OEM102" or "OEMBACKSLASH" => "Oem102",
            // Keep unknown old values visible for recovery, but they are disabled by NormalizeBinding.
            _ => key.Length <= 32 ? key : string.Empty
        };
    }

    public static int VirtualKeyCode(string? key)
    {
        key = NormalizeKey(key);
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) return key[0];
        if (key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 24) return 0x70 + function - 1;
        if (key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 && char.IsAsciiDigit(key[6])) return 0x60 + key[6] - '0';
        return key switch
        {
            "Backspace" => 0x08, "Tab" => 0x09, "Enter" => 0x0D, "Shift" => 0x10, "Ctrl" => 0x11, "Alt" => 0x12,
            "Pause" => 0x13, "CapsLock" => 0x14, "Escape" => 0x1B, "Space" => 0x20, "PageUp" => 0x21, "PageDown" => 0x22,
            "End" => 0x23, "Home" => 0x24, "Left" => 0x25, "Up" => 0x26, "Right" => 0x27, "Down" => 0x28,
            "PrintScreen" => 0x2C, "Insert" => 0x2D, "Delete" => 0x2E, "Win" => 0x5B, "Apps" => 0x5D,
            "Multiply" => 0x6A, "Add" => 0x6B, "Subtract" => 0x6D, "Decimal" => 0x6E, "Divide" => 0x6F,
            "NumLock" => 0x90, "ScrollLock" => 0x91,
            ";" => 0xBA, "=" or "+" => 0xBB, "," => 0xBC, "-" => 0xBD, "." => 0xBE, "/" => 0xBF,
            "`" => 0xC0, "[" => 0xDB, "\\" => 0xDC, "]" => 0xDD, "'" => 0xDE, "Oem102" => 0xE2,
            _ => 0
        };
    }

    public static string NormalizeModifiers(string? modifiers)
    {
        var values = (modifiers ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeKey).Where(value => value is "Ctrl" or "Shift" or "Alt" or "Win")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value switch { "Ctrl" => 0, "Shift" => 1, "Alt" => 2, _ => 3 });
        return string.Join('+', values);
    }

    public static bool SameBinding(ShortcutBinding? left, ShortcutBinding? right)
    {
        var a = NormalizeBinding(left); var b = NormalizeBinding(right);
        return a.Enabled == b.Enabled && a.Key == b.Key && a.Modifiers == b.Modifiers;
    }

    public static string DisplayKey(ShortcutBinding binding)
    {
        var normalized = NormalizeBinding(binding);
        return string.Join('+', new[] { normalized.Modifiers, normalized.Key }.Where(value => value.Length > 0));
    }

    public static string Display(ShortcutBinding binding) => !binding.Enabled || VirtualKeyCode(binding.Key) == 0 ? "已关闭" : DisplayKey(binding);

    public static IReadOnlyList<string> FindConflicts(IDictionary<DockShortcutAction, ShortcutBinding> bindings) =>
        bindings.Select(pair => (pair.Key, Binding: NormalizeBinding(pair.Value)))
            .Where(pair => pair.Binding.Enabled)
            .GroupBy(pair => VirtualKeyCode(pair.Binding.Key) + "|" + pair.Binding.Modifiers, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join("、", group.Select(pair => Label(pair.Key))))
            .ToArray();

    public static string Label(DockShortcutAction action) => action switch
    {
        DockShortcutAction.ActivateQuickMode => "呼出",
        DockShortcutAction.PreviousRow => "上移",
        DockShortcutAction.NextRow => "下移",
        DockShortcutAction.PreviousColumn => "左移",
        DockShortcutAction.NextColumn => "右移",
        DockShortcutAction.PreviousList => "上一个列表",
        DockShortcutAction.NextList => "下一个列表",
        DockShortcutAction.ToggleExpanded => "展开/收回",
        DockShortcutAction.TogglePinned => "固定/取消固定",
        DockShortcutAction.SwitchDock => "切换边栏",
        DockShortcutAction.Launch => "打开选中项目",
        _ => action.ToString()
    };
}
