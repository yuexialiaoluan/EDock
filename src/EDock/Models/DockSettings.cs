using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EDock.Models;

public enum DockSide
{
    Left,
    Right
}

public sealed class DockSettings : DockOptions, IJsonOnDeserializing, IJsonOnDeserialized
{
    public bool LeftEnabled { get; set; } = true;
    public bool RightEnabled { get; set; } = true;
    public bool OpenOnStartup { get; set; }
    public bool RemoveDesktopShortcutAfterImport { get; set; }
    public bool QuickModeEnabled { get; set; }
    public string QuickModeSide { get; set; } = "Left";
    private Dictionary<DockShortcutAction, ShortcutBinding> _shortcutBindings = ShortcutBindingCatalog.CreateDefaults();
    private ShortcutBinding? _legacyActivation;
    private ShortcutBinding? _assignedBindingsActivation;
    private bool _isDeserializing;
    // Keep the historical JSON property readable while using the same binding as
    // the ordinary shortcut list. Some previous builds saved a custom activation
    // only here and an untouched Ctrl entry in ShortcutBindings.
    public ShortcutBinding QuickModeActivation
    {
        get => _shortcutBindings.TryGetValue(DockShortcutAction.ActivateQuickMode, out var binding) && binding is not null
            ? binding : EDock.Models.QuickModeActivation.Default();
        set
        {
            var normalized = EDock.Models.QuickModeActivation.Normalize(value);
            if (_isDeserializing) _legacyActivation = normalized.Copy();
            _shortcutBindings[DockShortcutAction.ActivateQuickMode] = normalized;
        }
    }
    public Dictionary<DockShortcutAction, ShortcutBinding> ShortcutBindings
    {
        get => _shortcutBindings;
        set
        {
            _shortcutBindings = value ?? ShortcutBindingCatalog.CreateDefaults();
            if (_isDeserializing)
                _assignedBindingsActivation = _shortcutBindings.TryGetValue(DockShortcutAction.ActivateQuickMode, out var configured)
                    ? configured?.Copy() : null;
        }
    }
    void IJsonOnDeserializing.OnDeserializing() => _isDeserializing = true;
    void IJsonOnDeserialized.OnDeserialized()
    {
        // JSON property order must not choose which binding wins. Prefer a
        // customized dictionary entry, then the historical standalone setting.
        if (_assignedBindingsActivation is not null &&
            (!ShortcutBindingCatalog.SameBinding(_assignedBindingsActivation, EDock.Models.QuickModeActivation.Default()) || _legacyActivation is null))
            _shortcutBindings[DockShortcutAction.ActivateQuickMode] = ShortcutBindingCatalog.NormalizeBinding(_assignedBindingsActivation);
        else if (_legacyActivation is not null)
            _shortcutBindings[DockShortcutAction.ActivateQuickMode] = _legacyActivation.Copy();
        _legacyActivation = null;
        _assignedBindingsActivation = null;
        _isDeserializing = false;
    }
    // Windows has one taskbar appearance for the session, shared by both docks.
    public bool TaskbarTransparencyEnabled { get; set; }
    public double TaskbarOpacity { get; set; }
    public string TaskbarMaterial { get; set; } = "Transparent";
    // Start menu appearance is global and remains opt in for existing profiles.
    public bool StartMenuTransparencyEnabled { get; set; }
    public double StartMenuOpacity { get; set; }
    public bool StartButtonReplacementEnabled { get; set; }
    public string StartButtonImagePath { get; set; } = string.Empty;
    public string StartButtonHoverImagePath { get; set; } = string.Empty;
    public string StartButtonPressedImagePath { get; set; } = string.Empty;
    public string StartButtonActivatedImagePath { get; set; } = string.Empty;
    public double StartButtonIconSize { get; set; } = 32;
    // The settings window has its own global appearance, separate from either dock.
    public string SettingsTheme { get; set; } = "Default";
    public string SettingsBackgroundStyle { get; set; } = "Solid";
    public double SettingsBackgroundOpacity { get; set; } = 1;
    public string SettingsBackgroundImagePath { get; set; } = string.Empty;
    public string SettingsContentOverlayTheme { get; set; } = "Default";
    // Applies to the content overlay for every window material. Keep the historic
    // JSON property name and value so existing image-mask preferences survive.
    public double SettingsBackgroundImageOverlayOpacity { get; set; } = .35;
    public bool SeparateSideSettings { get; set; }
    public DockOptions? LeftSettings { get; set; }
    public DockOptions? RightSettings { get; set; }
    public bool? LeftAutoHideOverride { get; set; }
    public bool? RightAutoHideOverride { get; set; }

    public DockOptions GetStoredSideOptions(DockSide side) =>
        (side == DockSide.Left ? LeftSettings : RightSettings)?.Copy() ?? Copy();

    public DockOptions GetSideOptions(DockSide side)
    {
        var options = SeparateSideSettings ? GetStoredSideOptions(side) : Copy();
        bool? autoHide = side == DockSide.Left ? LeftAutoHideOverride : RightAutoHideOverride;
        if (autoHide.HasValue) options.AutoHide = autoHide.Value;
        return options;
    }

    public void SetSideAutoHide(DockSide side, bool autoHide)
    {
        if (side == DockSide.Left) LeftAutoHideOverride = autoHide;
        else RightAutoHideOverride = autoHide;
    }

    public void ClearSideAutoHideOverride(DockSide side)
    {
        if (side == DockSide.Left) LeftAutoHideOverride = null;
        else RightAutoHideOverride = null;
    }
    public List<ShortcutItem> LeftShortcuts { get; set; } = new();
    public List<ShortcutItem> RightShortcuts { get; set; } = new();
    public List<DockList> LeftLists { get; set; } = new();
    public List<DockList> RightLists { get; set; } = new();
    public string ActiveLeftListId { get; set; } = string.Empty;
    public string ActiveRightListId { get; set; } = string.Empty;

    public List<DockList> GetLists(DockSide side)
    {
        var lists = side == DockSide.Left ? LeftLists : RightLists;
        if (lists is null || lists.Count == 0 || lists.Exists(list => list is null))
        {
            NormalizeLists(side);
            lists = side == DockSide.Left ? LeftLists : RightLists;
        }
        return lists;
    }

    public DockList GetActiveList(DockSide side)
    {
        var lists = GetLists(side);
        string activeId = side == DockSide.Left ? ActiveLeftListId : ActiveRightListId;
        var active = lists.Find(list => list.Id == activeId) ?? lists[0];
        SetActiveList(side, active);
        return active;
    }

    public bool SelectList(DockSide side, string id)
    {
        var list = GetLists(side).Find(list => list.Id == id);
        if (list is null) return false;
        SetActiveList(side, list);
        return true;
    }

    public DockList AddList(DockSide side, string name)
    {
        var lists = GetLists(side);
        var list = new DockList { Name = NormalizeListName(name, "新列表") };
        lists.Add(list);
        SetActiveList(side, list);
        return list;
    }

    public bool RenameList(DockSide side, string id, string name)
    {
        var list = GetLists(side).Find(list => list.Id == id);
        if (list is null) return false;
        list.Name = NormalizeListName(name);
        return true;
    }

    // The UI confirms deletion. Keep every shortcut by appending it to a neighbor.
    public bool DeleteList(DockSide side, string id)
    {
        var lists = GetLists(side);
        if (lists.Count <= 1) return false;
        int index = lists.FindIndex(list => list.Id == id);
        if (index < 0) return false;
        var removed = lists[index];
        var recipient = lists[index + 1 < lists.Count ? index + 1 : index - 1];
        recipient.Shortcuts ??= new();
        if (removed.Shortcuts is not null) recipient.Shortcuts.AddRange(removed.Shortcuts);
        NormalizeShortcuts(recipient.Shortcuts);
        recipient.SortKey = ShortcutSortKey.Manual;
        string activeId = side == DockSide.Left ? ActiveLeftListId : ActiveRightListId;
        lists.RemoveAt(index);
        if (activeId == removed.Id) SetActiveList(side, recipient);
        else GetActiveList(side);
        return true;
    }

    private void SetActiveList(DockSide side, DockList list)
    {
        list.Shortcuts ??= new();
        if (side == DockSide.Left)
        {
            ActiveLeftListId = list.Id;
            LeftShortcuts = list.Shortcuts;
        }
        else
        {
            ActiveRightListId = list.Id;
            RightShortcuts = list.Shortcuts;
        }
    }

    // Settings are editable JSON. Repair unusable values before the window uses them.
    internal void Normalize()
    {
        if (QuickModeSide is not ("Left" or "Right" or "Both")) QuickModeSide = "Left";
        ShortcutBindings ??= ShortcutBindingCatalog.CreateDefaults();
        ShortcutBindingCatalog.Normalize(ShortcutBindings);
        _legacyActivation = null;
        _assignedBindingsActivation = null;
        TaskbarOpacity = double.IsFinite(TaskbarOpacity) ? Math.Clamp(TaskbarOpacity, 0, 1) : 0;
        StartMenuOpacity = double.IsFinite(StartMenuOpacity) ? Math.Clamp(StartMenuOpacity, 0, 1) : 0;
        StartButtonImagePath = (StartButtonImagePath ?? string.Empty).Trim();
        StartButtonHoverImagePath = (StartButtonHoverImagePath ?? string.Empty).Trim();
        StartButtonPressedImagePath = (StartButtonPressedImagePath ?? string.Empty).Trim();
        StartButtonActivatedImagePath = (StartButtonActivatedImagePath ?? string.Empty).Trim();
        StartButtonIconSize = double.IsFinite(StartButtonIconSize) ? Math.Clamp(StartButtonIconSize, 8, 64) : 32;
        if (TaskbarMaterial is not ("Transparent" or "Acrylic" or "Blur")) TaskbarMaterial = "Transparent";
        if (SettingsTheme is not ("Default" or "Light" or "Dark")) SettingsTheme = "Default";
        if (SettingsContentOverlayTheme is not ("Default" or "Light" or "Dark")) SettingsContentOverlayTheme = "Default";
        if (SettingsBackgroundStyle is not ("Solid" or "Transparent" or "Acrylic" or "Image")) SettingsBackgroundStyle = "Solid";
        SettingsBackgroundOpacity = double.IsFinite(SettingsBackgroundOpacity) ? Math.Clamp(SettingsBackgroundOpacity, 0, 1) : 1;
        SettingsBackgroundImagePath = (SettingsBackgroundImagePath ?? string.Empty).Trim();
        SettingsBackgroundImageOverlayOpacity = double.IsFinite(SettingsBackgroundImageOverlayOpacity)
            ? Math.Clamp(SettingsBackgroundImageOverlayOpacity, 0, 1) : .35;
        NormalizeOptions();
        LeftSettings?.NormalizeOptions();
        RightSettings?.NormalizeOptions();
        NormalizeLists(DockSide.Left);
        NormalizeLists(DockSide.Right);
    }

    private void NormalizeLists(DockSide side)
    {
        var lists = side == DockSide.Left ? LeftLists : RightLists;
        lists ??= new();
        lists.RemoveAll(list => list is null);
        if (lists.Count == 0)
        {
            // Only old configurations use these arrays as the source of data.
            // Once lists exist, their inactive contents must never be overwritten.
            lists.Add(new DockList
            {
                Name = NormalizeListName(GetSideOptions(side).HeaderText),
                Shortcuts = (side == DockSide.Left ? LeftShortcuts : RightShortcuts) ?? new()
            });
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            if (string.IsNullOrWhiteSpace(list.Id) || !ids.Add(list.Id))
            {
                list.Id = Guid.NewGuid().ToString("N");
                ids.Add(list.Id);
            }
            list.Name = NormalizeListName(list.Name);
            list.Shortcuts = NormalizeShortcuts(list.Shortcuts);
            if (!Enum.IsDefined(list.SortKey)) list.SortKey = ShortcutSortKey.Manual;
        }
        if (side == DockSide.Left) LeftLists = lists;
        else RightLists = lists;
        GetActiveList(side);
    }

    private static string NormalizeListName(string? name, string fallback = "默认列表")
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) return fallback;
        return name.Length > 80 ? name[..80] : name;
    }

    private static List<ShortcutItem> NormalizeShortcuts(List<ShortcutItem>? items)
    {
        items ??= new();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item is null || string.IsNullOrWhiteSpace(item.TargetPath))
            {
                items.RemoveAt(index--);
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
                ids.Add(item.Id);
            }
            item.TargetPath = item.TargetPath.Trim();
            item.Name = string.IsNullOrWhiteSpace(item.Name) ? item.TargetPath : item.Name.Trim();
            item.Arguments ??= string.Empty;
            item.WorkingDirectory ??= string.Empty;
            item.IconPath ??= string.Empty;
            item.CustomIconPath = (item.CustomIconPath ?? string.Empty).Trim();
            if (item.ImageCrop is not ("Top" or "Center" or "Bottom" or "Fit")) item.ImageCrop = "Center";
        }
        return items;
    }
}

