using System.Globalization;
using System.Text.Json;
using EDock.Models;
using EDock.Services;

int passed = 0, failed = 0;
void Check(bool value, string message = "Assertion failed")
{
    if (!value) throw new InvalidOperationException(message);
}
void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}

Run("Existing profiles enable window protections without changing saved pin or list preferences", () =>
{
    var settings = JsonSerializer.Deserialize<DockSettings>("""
        {"SeparateSideSettings":true,"LeftSettings":{"DockWidth":112},"RightSettings":{"DockWidth":160},
         "LeftAutoHideOverride":false,"RightAutoHideOverride":true,"QuickModeEnabled":true,
         "LeftLists":[{"Id":"keep-left","Name":"保留列表","Items":[]}],"ActiveLeftListId":"keep-left"}
        """)!;
    settings.Normalize();
    foreach (var options in new[] { new DockOptions(), settings, settings.GetSideOptions(DockSide.Left), settings.GetSideOptions(DockSide.Right) })
        Check(options.SuppressRevealWhenFullscreen && options.KeepPinnedBelowWindows);
    Check(!settings.GetSideOptions(DockSide.Left).AutoHide && settings.GetSideOptions(DockSide.Right).AutoHide);
    Check(settings.QuickModeEnabled && settings.GetActiveList(DockSide.Left).Name == "保留列表");
});

Run("Shortcut bindings keep defaults, normalize combinations, and report conflicts", () =>
{
    var settings = new DockSettings();
    settings.Normalize();
    Check(settings.ShortcutBindings[DockShortcutAction.PreviousRow].Key == "W");
    settings.ShortcutBindings[DockShortcutAction.PreviousRow] = new ShortcutBinding { Key = "q", Modifiers = "Shift+Ctrl" };
    settings.ShortcutBindings[DockShortcutAction.NextRow] = new ShortcutBinding { Key = "Q", Modifiers = "Ctrl+Shift" };
    settings.Normalize();
    Check(settings.ShortcutBindings[DockShortcutAction.PreviousRow].Key == "Q");
    Check(settings.ShortcutBindings[DockShortcutAction.PreviousRow].Modifiers == "Ctrl+Shift");
    Check(ShortcutBindingCatalog.FindConflicts(settings.ShortcutBindings).Count == 1);
    settings.ShortcutBindings[DockShortcutAction.NextRow].Enabled = false;
    Check(ShortcutBindingCatalog.FindConflicts(settings.ShortcutBindings).Count == 0);
    Check(ShortcutBindingCatalog.Display(new ShortcutBinding { Enabled = false, Key = "Q" }) == "已关闭");
});

Run("Explicitly disabled window protections survive copy, normalization and JSON reload", () =>
{
    var settings = new DockSettings { SuppressRevealWhenFullscreen = false, KeepPinnedBelowWindows = false };
    settings.Normalize();
    var copy = settings.Copy();
    var applied = new DockOptions();
    applied.CopyFrom(copy);
    var reloaded = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
    reloaded.Normalize();
    foreach (var options in new[] { copy, applied, reloaded, reloaded.GetSideOptions(DockSide.Left), reloaded.GetSideOptions(DockSide.Right) })
        Check(!options.SuppressRevealWhenFullscreen && !options.KeepPinnedBelowWindows);
});

Run("Window protection previews route to the selected side and preserve live pin overrides", () =>
{
    var settings = new DockSettings { LeftAutoHideOverride = false, RightAutoHideOverride = true };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    draft.SetSeparate(true);
    draft.Current.SuppressRevealWhenFullscreen = false;
    Check(!draft.Matches(settings));
    draft.ApplyTo(settings);
    draft.AcceptApplied();
    Check(draft.Matches(settings));
    Check(settings.GetSideOptions(DockSide.Left).SuppressRevealWhenFullscreen);
    Check(!settings.GetSideOptions(DockSide.Right).SuppressRevealWhenFullscreen);
    draft.SelectSide(DockSide.Left);
    draft.Current.KeepPinnedBelowWindows = false;
    Check(!draft.Matches(settings));
    draft.ApplyTo(settings);
    Check(!settings.GetSideOptions(DockSide.Left).KeepPinnedBelowWindows);
    Check(settings.GetSideOptions(DockSide.Right).KeepPinnedBelowWindows);
    Check(settings.LeftAutoHideOverride == false && settings.RightAutoHideOverride == true);
    var reloaded = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
    reloaded.Normalize();
    Check(reloaded.GetSideOptions(DockSide.Left).SuppressRevealWhenFullscreen && !reloaded.GetSideOptions(DockSide.Right).SuppressRevealWhenFullscreen);
    Check(!reloaded.GetSideOptions(DockSide.Left).KeepPinnedBelowWindows && reloaded.GetSideOptions(DockSide.Right).KeepPinnedBelowWindows);
});

Run("Unified window protections survive switching back to saved independent choices", () =>
{
    var settings = new DockSettings();
    var draft = new SettingsEditorDraft(settings);
    draft.Shared.SuppressRevealWhenFullscreen = false;
    draft.Shared.KeepPinnedBelowWindows = false;
    draft.SetSeparate(true);
    Check(!draft.Left.SuppressRevealWhenFullscreen && !draft.Right.KeepPinnedBelowWindows);
    draft.Right.SuppressRevealWhenFullscreen = true;
    draft.SetSeparate(false);
    draft.ApplyTo(settings);
    Check(!settings.GetSideOptions(DockSide.Left).SuppressRevealWhenFullscreen && !settings.GetSideOptions(DockSide.Right).SuppressRevealWhenFullscreen);
    draft.SetSeparate(true);
    draft.ApplyTo(settings);
    Check(!settings.GetSideOptions(DockSide.Left).SuppressRevealWhenFullscreen && settings.GetSideOptions(DockSide.Right).SuppressRevealWhenFullscreen);
});

Run("Opening the right dock preserves unified mode", () =>
{
    var settings = new DockSettings { DockWidth = 104 };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    Check(!draft.Separate && draft.Scope == 0 && draft.Side == DockSide.Right);
    draft.SelectSide(DockSide.Left);
    draft.SelectSide(DockSide.Right);
    Check(draft.Matches(settings));
    draft.Current.DockWidth = 128;
    draft.ApplyTo(settings);
    Check(!settings.SeparateSideSettings);
    Check(settings.GetSideOptions(DockSide.Left).DockWidth == 128);
    Check(settings.GetSideOptions(DockSide.Right).DockWidth == 128);
});

Run("The first split copies the latest unified values", () =>
{
    var draft = new SettingsEditorDraft(new DockSettings());
    draft.Shared.DockWidth = 152;
    draft.Shared.IconSize = 56;
    draft.SetSeparate(true);
    Check(draft.Left.DockWidth == 152 && draft.Right.DockWidth == 152);
    Check(draft.Left.IconSize == 56 && draft.Right.IconSize == 56);
    draft.Left.IconSize = 72;
    Check(draft.Shared.IconSize == 56 && draft.Right.IconSize == 56);
});

Run("Existing independent drafts remain isolated across mode changes", () =>
{
    var settings = new DockSettings
    {
        DockWidth = 88, SeparateSideSettings = true,
        LeftSettings = new DockOptions { DockWidth = 112 },
        RightSettings = new DockOptions { DockWidth = 160 }
    };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    draft.Current.DockWidth = 192;
    Check(settings.RightSettings.DockWidth == 160);
    draft.SelectSide(DockSide.Left);
    Check(draft.Current.DockWidth == 112);
    draft.Current.IconSpacing = 16;
    draft.SetSeparate(false);
    draft.Shared.DockWidth = 120;
    draft.SetSeparate(true);
    draft.ApplyTo(settings);
    Check(settings.DockWidth == 120 && settings.LeftSettings!.DockWidth == 112);
    Check(settings.LeftSettings.IconSpacing == 16 && settings.RightSettings!.DockWidth == 192);
});

Run("Stored independent settings survive editing unified appearance", () =>
{
    var settings = new DockSettings
    {
        LeftSettings = new DockOptions { IconSize = 48 },
        RightSettings = new DockOptions { IconSize = 64 }
    };
    var draft = new SettingsEditorDraft(settings);
    draft.Shared.IconSpacing = 12;
    draft.ApplyTo(settings);
    draft.SetSeparate(true);
    Check(draft.Left.IconSize == 48 && draft.Right.IconSize == 64);
});

Run("Unrelated appearance edits preserve pin overrides", () =>
{
    var settings = new DockSettings
    {
        AutoHide = true, SeparateSideSettings = true,
        LeftAutoHideOverride = false, RightAutoHideOverride = false
    };
    var draft = new SettingsEditorDraft(settings);
    Check(!draft.DisplayAutoHide(settings));
    draft.Current.DockWidth = 120;
    draft.ApplyTo(settings);
    Check(settings.LeftAutoHideOverride == false && settings.RightAutoHideOverride == false);
    Check(draft.Matches(settings));
});

Run("An explicit side auto hide edit clears only that pin", () =>
{
    var settings = new DockSettings
    {
        SeparateSideSettings = true,
        LeftAutoHideOverride = false, RightAutoHideOverride = false
    };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    draft.SetAutoHide(true);
    Check(!draft.Matches(settings));
    draft.ApplyTo(settings);
    Check(settings.LeftAutoHideOverride == false && settings.RightAutoHideOverride is null);
    Check(settings.GetSideOptions(DockSide.Right).AutoHide);
    draft.AcceptApplied();
    Check(draft.Matches(settings));
    settings.RightAutoHideOverride = false;
    Check(!draft.DisplayAutoHide(settings));
    draft.ApplyTo(settings);
    Check(settings.RightAutoHideOverride == false);
});

Run("A unified auto hide edit clears both pins", () =>
{
    var settings = new DockSettings { LeftAutoHideOverride = false, RightAutoHideOverride = true };
    var draft = new SettingsEditorDraft(settings);
    draft.SetAutoHide(false);
    draft.ApplyTo(settings);
    Check(!settings.AutoHide && settings.LeftAutoHideOverride is null && settings.RightAutoHideOverride is null);
});

Run("Live pin changes remain intact during subsequent previews", () =>
{
    var settings = new DockSettings { SeparateSideSettings = true };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    settings.LeftAutoHideOverride = false;
    draft.Current.IconSize = 52;
    draft.ApplyTo(settings);
    Check(settings.LeftAutoHideOverride == false);
    draft.SelectSide(DockSide.Left);
    Check(!draft.DisplayAutoHide(settings));
});

Run("Appearance previews preserve live lists and global controls", () =>
{
    var settings = new DockSettings
    {
        LeftEnabled = true, RightEnabled = false, OpenOnStartup = true,
        QuickModeEnabled = true, QuickModeSide = "Left", RemoveDesktopShortcutAfterImport = true,
        TaskbarTransparencyEnabled = true, TaskbarOpacity = .25
    };
    var left = settings.GetActiveList(DockSide.Left);
    var right = settings.GetActiveList(DockSide.Right);
    left.Shortcuts.Add(new ShortcutItem { Name = "one", TargetPath = "one.exe" });
    var originalLeftLists = settings.LeftLists;
    var originalRightLists = settings.RightLists;
    var originalLeftItems = settings.LeftShortcuts;
    var originalRightItems = settings.RightShortcuts;
    var draft = new SettingsEditorDraft(settings);
    // A live import while the settings window is open must survive its preview.
    left.Shortcuts.Add(new ShortcutItem { Name = "two", TargetPath = "two.exe" });
    draft.Shared.IconSize = 64;
    draft.ApplyTo(settings);
    Check(ReferenceEquals(originalLeftLists, settings.LeftLists));
    Check(ReferenceEquals(originalRightLists, settings.RightLists));
    Check(ReferenceEquals(originalLeftItems, settings.LeftShortcuts));
    Check(ReferenceEquals(originalRightItems, settings.RightShortcuts));
    Check(settings.LeftShortcuts.Count == 2 && ReferenceEquals(settings.GetActiveList(DockSide.Right), right));
    Check(settings.LeftEnabled && !settings.RightEnabled && settings.OpenOnStartup);
    Check(settings.QuickModeEnabled && settings.QuickModeSide == "Left" && settings.RemoveDesktopShortcutAfterImport);
    Check(settings.TaskbarTransparencyEnabled && settings.TaskbarOpacity == .25);
});

Run("Taskbar appearance is global, opt in, and repairs invalid opacity", () =>
{
    var settings = new DockSettings();
    Check(!settings.TaskbarTransparencyEnabled && settings.TaskbarOpacity == 0);
    settings.TaskbarTransparencyEnabled = true;
    foreach (var sample in new[] { (double.NaN, 0d), (double.PositiveInfinity, 0d), (-.5, 0d), (2d, 1d), (.4, .4) })
    {
        settings.TaskbarOpacity = sample.Item1;
        settings.Normalize();
        Check(settings.TaskbarTransparencyEnabled && settings.TaskbarOpacity == sample.Item2);
    }
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    draft.SetSeparate(true);
    draft.Right.IconSize = 64;
    draft.ApplyTo(settings);
    draft.SetSeparate(false);
    draft.ApplyTo(settings);
    Check(settings.TaskbarTransparencyEnabled && settings.TaskbarOpacity == .4);
    Check(typeof(DockOptions).GetProperty(nameof(DockSettings.TaskbarTransparencyEnabled)) is null);
});

Run("Start menu appearance is opt in for new and existing profiles", () =>
{
    foreach (var settings in new[] { new DockSettings(), JsonSerializer.Deserialize<DockSettings>("""
        {"TaskbarTransparencyEnabled":true,"TaskbarOpacity":0.35,"DockWidth":136,
         "LeftLists":[{"Id":"keep-list","Name":"保留列表","Shortcuts":[{"Id":"keep-item","Name":"保留图标","TargetPath":"app.exe"}]}],
         "ActiveLeftListId":"keep-list"}
        """)! })
    {
        settings.Normalize();
        Check(!settings.StartMenuTransparencyEnabled && settings.StartMenuOpacity == 0);
    }
    var old = JsonSerializer.Deserialize<DockSettings>("""
        {"TaskbarTransparencyEnabled":true,"TaskbarOpacity":0.35,"DockWidth":136,
         "LeftLists":[{"Id":"keep-list","Name":"保留列表","Shortcuts":[{"Id":"keep-item","Name":"保留图标","TargetPath":"app.exe"}]}],
         "ActiveLeftListId":"keep-list"}
        """)!;
    old.Normalize();
    Check(old.TaskbarTransparencyEnabled && old.TaskbarOpacity == .35 && old.DockWidth == 136);
    Check(old.GetActiveList(DockSide.Left).Id == "keep-list" && old.LeftShortcuts.Single().Id == "keep-item");
});

Run("Start menu opacity repairs nonfinite and out of range values", () =>
{
    var settings = new DockSettings { StartMenuTransparencyEnabled = true };
    foreach (var sample in new[] { (double.NaN, 0d), (double.PositiveInfinity, 0d),
        (double.NegativeInfinity, 0d), (-.5, 0d), (2d, 1d), (0d, 0d), (1d, 1d), (.4, .4) })
    {
        settings.StartMenuOpacity = sample.Item1;
        settings.Normalize();
        Check(settings.StartMenuTransparencyEnabled && settings.StartMenuOpacity == sample.Item2);
    }
});

Run("Start menu appearance persists independently and survives dock previews", () =>
{
    var settings = new DockSettings { StartMenuTransparencyEnabled = true, StartMenuOpacity = .42,
        TaskbarTransparencyEnabled = false, TaskbarOpacity = .18 };
    settings.Normalize();
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    draft.SetSeparate(true);
    draft.Right.IconSize = 64;
    draft.ApplyTo(settings);
    draft.SetSeparate(false);
    draft.ApplyTo(settings);
    var reloaded = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
    reloaded.Normalize();
    Check(reloaded.StartMenuTransparencyEnabled && reloaded.StartMenuOpacity == .42);
    Check(!reloaded.TaskbarTransparencyEnabled && reloaded.TaskbarOpacity == .18);
    Check(typeof(DockOptions).GetProperty(nameof(DockSettings.StartMenuTransparencyEnabled)) is null);
    settings.StartMenuTransparencyEnabled = false;
    reloaded = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
    reloaded.Normalize();
    Check(!reloaded.StartMenuTransparencyEnabled && reloaded.StartMenuOpacity == .42);
});

Run("Start button replacement is opt in and does not change existing appearance or lists", () =>
{
    foreach (var settings in new[] { new DockSettings(), JsonSerializer.Deserialize<DockSettings>("""
        {"TaskbarTransparencyEnabled":true,"TaskbarOpacity":0.35,"StartMenuTransparencyEnabled":true,"StartMenuOpacity":0.1,
         "LeftLists":[{"Id":"keep-list","Name":"保留列表","Shortcuts":[{"Id":"keep-item","Name":"保留图标","TargetPath":"app.exe"}]}],
         "ActiveLeftListId":"keep-list"}
        """)! })
    {
        settings.Normalize();
        Check(!settings.StartButtonReplacementEnabled && settings.StartButtonIconSize == 32);
        Check(settings.StartButtonImagePath == "" && settings.StartButtonHoverImagePath == "" && settings.StartButtonPressedImagePath == "" && settings.StartButtonActivatedImagePath == "");
        var reloaded = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
        reloaded.Normalize();
        Check(reloaded.TaskbarTransparencyEnabled == settings.TaskbarTransparencyEnabled && reloaded.TaskbarOpacity == settings.TaskbarOpacity);
        Check(reloaded.StartMenuTransparencyEnabled == settings.StartMenuTransparencyEnabled && reloaded.StartMenuOpacity == settings.StartMenuOpacity);
        Check(reloaded.GetActiveList(DockSide.Left).Id == settings.GetActiveList(DockSide.Left).Id);
        Check(reloaded.LeftShortcuts.Select(item => item.Id).SequenceEqual(settings.LeftShortcuts.Select(item => item.Id)));
    }
    var threeStateSettings = JsonSerializer.Deserialize<DockSettings>("""
        {"StartButtonReplacementEnabled":true,"StartButtonImagePath":"normal.png","StartButtonHoverImagePath":"hover.gif","StartButtonPressedImagePath":"pressed.jpg"}
        """)!;
    threeStateSettings.Normalize();
    Check(threeStateSettings.StartButtonReplacementEnabled && threeStateSettings.StartButtonImagePath == "normal.png"
        && threeStateSettings.StartButtonHoverImagePath == "hover.gif" && threeStateSettings.StartButtonPressedImagePath == "pressed.jpg"
        && threeStateSettings.StartButtonActivatedImagePath == "");
});

Run("Start button replacement normalizes editable paths and dimensions", () =>
{
    var settings = new DockSettings { StartButtonReplacementEnabled = true, StartButtonImagePath = null!,
        StartButtonHoverImagePath = "  C:\\图标\\hover.gif  ", StartButtonPressedImagePath = null!, StartButtonActivatedImagePath = "  C:\\图标\\active.png  " };
    foreach (var sample in new[] { (double.NaN, 32d), (double.PositiveInfinity, 32d), (double.NegativeInfinity, 32d),
        (-10d, 8d), (0d, 8d), (8d, 8d), (32d, 32d), (64d, 64d), (100d, 64d) })
    {
        settings.StartButtonIconSize = sample.Item1;
        settings.Normalize();
        Check(settings.StartButtonIconSize == sample.Item2 && settings.StartButtonReplacementEnabled);
        Check(settings.StartButtonImagePath == "" && settings.StartButtonHoverImagePath == "C:\\图标\\hover.gif" && settings.StartButtonPressedImagePath == "");
        Check(settings.StartButtonActivatedImagePath == "C:\\图标\\active.png");
    }
    settings.StartButtonActivatedImagePath = null!;
    settings.Normalize();
    Check(settings.StartButtonActivatedImagePath == "");
});

Run("Start button replacement persists independently through side previews and disabling", () =>
{
    var settings = new DockSettings { StartButtonReplacementEnabled = true, StartButtonIconSize = 44,
        StartButtonImagePath = "C:\\图片\\normal.png", StartButtonHoverImagePath = "C:\\图片\\hover.gif", StartButtonPressedImagePath = "C:\\图片\\pressed.jpg",
        StartButtonActivatedImagePath = "C:\\图片\\active.png",
        StartMenuTransparencyEnabled = true, StartMenuOpacity = .42, TaskbarTransparencyEnabled = true, TaskbarOpacity = .18 };
    settings.Normalize();
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    draft.SetSeparate(true);
    draft.Right.IconSize = 64;
    draft.ApplyTo(settings);
    draft.SetSeparate(false);
    draft.ApplyTo(settings);
    settings.StartButtonReplacementEnabled = false;
    var copy = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
    copy.Normalize();
    Check(!copy.StartButtonReplacementEnabled && copy.StartButtonIconSize == 44);
    Check(copy.StartButtonImagePath == settings.StartButtonImagePath && copy.StartButtonHoverImagePath == settings.StartButtonHoverImagePath
        && copy.StartButtonPressedImagePath == settings.StartButtonPressedImagePath && copy.StartButtonActivatedImagePath == settings.StartButtonActivatedImagePath);
    Check(copy.StartMenuTransparencyEnabled && copy.StartMenuOpacity == .42 && copy.TaskbarTransparencyEnabled && copy.TaskbarOpacity == .18);
    foreach (string property in new[] { nameof(DockSettings.StartButtonReplacementEnabled), nameof(DockSettings.StartButtonImagePath),
        nameof(DockSettings.StartButtonHoverImagePath), nameof(DockSettings.StartButtonPressedImagePath), nameof(DockSettings.StartButtonActivatedImagePath), nameof(DockSettings.StartButtonIconSize) })
        Check(typeof(DockOptions).GetProperty(property) is null);
});

Run("Older JSON keeps its dock appearance and receives safe new global defaults", () =>
{
    const string legacy = """
        { "DockWidth": 120, "BackgroundStyle": "Acrylic", "BackgroundOpacity": 0.6,
          "SeparateSideSettings": true, "LeftSettings": { "IconSize": 52 },
          "RightSettings": { "IconSize": 64 }, "TaskbarTransparencyEnabled": true,
          "TaskbarOpacity": 0.25 }
        """;
    var settings = JsonSerializer.Deserialize<DockSettings>(legacy)!;
    settings.Normalize();
    Check(settings.SettingsBackgroundStyle == "Solid" && settings.SettingsBackgroundOpacity == 1);
    Check(settings.SettingsBackgroundImagePath == "" && settings.SettingsBackgroundImageOverlayOpacity == .35);
    Check(settings.SettingsTheme == "Default" && settings.SettingsContentOverlayTheme == "Default");
    Check(settings.TaskbarMaterial == "Transparent" && settings.TaskbarOpacity == .25);
    Check(settings.BackgroundStyle == "Acrylic" && settings.BackgroundOpacity == .6 && settings.DockWidth == 120);
    Check(settings.LeftSettings!.IconSize == 52 && settings.RightSettings!.IconSize == 64);
});

Run("Window background and taskbar material normalize invalid values and preserve valid modes", () =>
{
    var settings = new DockSettings();
    foreach (string? invalid in new string?[] { null, "", "Mica", "image", "invalid" })
    {
        settings.SettingsBackgroundStyle = invalid!;
        settings.TaskbarMaterial = invalid!;
        settings.Normalize();
        Check(settings.SettingsBackgroundStyle == "Solid" && settings.TaskbarMaterial == "Transparent");
    }
    foreach (string valid in new[] { "Solid", "Transparent", "Acrylic", "Image" })
    {
        settings.SettingsBackgroundStyle = valid;
        settings.Normalize();
        Check(settings.SettingsBackgroundStyle == valid);
    }
    foreach (string valid in new[] { "Transparent", "Acrylic", "Blur" })
    {
        settings.TaskbarMaterial = valid;
        settings.Normalize();
        Check(settings.TaskbarMaterial == valid);
    }
    foreach (var sample in new[] { (double.NaN, 1d, .35), (double.PositiveInfinity, 1d, .35),
        (double.NegativeInfinity, 1d, .35), (-.5, 0d, 0d), (2d, 1d, 1d), (.4, .4, .4) })
    {
        settings.SettingsBackgroundOpacity = sample.Item1;
        settings.SettingsBackgroundImageOverlayOpacity = sample.Item1;
        settings.Normalize();
        Check(settings.SettingsBackgroundOpacity == sample.Item2);
        Check(settings.SettingsBackgroundImageOverlayOpacity == sample.Item3);
    }
    settings.SettingsBackgroundImagePath = null!;
    settings.Normalize();
    Check(settings.SettingsBackgroundImagePath == "");
    settings.SettingsBackgroundImagePath = "  C:\\Images\\设置背景.png  ";
    settings.Normalize();
    Check(settings.SettingsBackgroundImagePath == "C:\\Images\\设置背景.png");
});

Run("Side drafts and unified previews preserve the latest window and taskbar globals", () =>
{
    var settings = new DockSettings
    {
        SettingsBackgroundStyle = "Image", SettingsBackgroundOpacity = .65,
        SettingsBackgroundImagePath = "C:\\Images\\window.png", SettingsBackgroundImageOverlayOpacity = .2,
        TaskbarMaterial = "Acrylic", TaskbarTransparencyEnabled = true, TaskbarOpacity = .4
    };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    // These can change while the dock draft remains open. Its later preview
    // must never restore the older global values captured when it was created.
    settings.SettingsBackgroundStyle = "Transparent";
    settings.SettingsBackgroundOpacity = .35;
    settings.SettingsBackgroundImagePath = "C:\\Images\\new.png";
    settings.SettingsBackgroundImageOverlayOpacity = .45;
    settings.TaskbarMaterial = "Blur";
    settings.TaskbarOpacity = .7;
    draft.SetSeparate(true);
    draft.Right.BackgroundStyle = "Acrylic";
    draft.ApplyTo(settings);
    draft.SelectSide(DockSide.Left);
    draft.Left.BackgroundStyle = "Image";
    draft.ApplyTo(settings);
    draft.SetSeparate(false);
    draft.Shared.BackgroundStyle = "Solid";
    draft.ApplyTo(settings);
    Check(settings.SettingsBackgroundStyle == "Transparent" && settings.SettingsBackgroundOpacity == .35);
    Check(settings.SettingsBackgroundImagePath == "C:\\Images\\new.png" && settings.SettingsBackgroundImageOverlayOpacity == .45);
    Check(settings.TaskbarMaterial == "Blur" && settings.TaskbarTransparencyEnabled && settings.TaskbarOpacity == .7);
    foreach (string global in new[] { nameof(DockSettings.SettingsBackgroundStyle), nameof(DockSettings.SettingsBackgroundOpacity),
        nameof(DockSettings.SettingsBackgroundImagePath), nameof(DockSettings.SettingsBackgroundImageOverlayOpacity), nameof(DockSettings.TaskbarMaterial) })
        Check(typeof(DockOptions).GetProperty(global) is null, global + " belongs to global settings");
});

Run("Window theme and content overlay color are independent global preferences", () =>
{
    foreach (string windowTheme in new[] { "Default", "Light", "Dark" })
    foreach (string overlayTheme in new[] { "Default", "Light", "Dark" })
    {
        var settings = new DockSettings
        {
            Theme = "Dark", SettingsTheme = windowTheme, SettingsContentOverlayTheme = overlayTheme,
            SettingsBackgroundImageOverlayOpacity = .23, SeparateSideSettings = true,
            LeftSettings = new DockOptions { Theme = "Light" }, RightSettings = new DockOptions { Theme = "Dark" }
        };
        settings.Normalize();
        Check(settings.SettingsTheme == windowTheme && settings.SettingsContentOverlayTheme == overlayTheme);
        Check(settings.SettingsBackgroundImageOverlayOpacity == .23);
        Check(settings.Theme == "Dark" && settings.LeftSettings!.Theme == "Light" && settings.RightSettings!.Theme == "Dark");
    }
    Check(typeof(DockOptions).GetProperty(nameof(DockSettings.SettingsTheme)) is null);
    Check(typeof(DockOptions).GetProperty(nameof(DockSettings.SettingsContentOverlayTheme)) is null);
});

Run("Invalid window and overlay themes fall back independently without changing the mask", () =>
{
    foreach (string? invalid in new string?[] { null, "", "light", "System", "Acrylic", " Light ", "invalid" })
    {
        var settings = new DockSettings
        {
            SettingsTheme = invalid!, SettingsContentOverlayTheme = "Dark", SettingsBackgroundImageOverlayOpacity = .58
        };
        settings.Normalize();
        Check(settings.SettingsTheme == "Default" && settings.SettingsContentOverlayTheme == "Dark");
        settings.SettingsTheme = "Light";
        settings.SettingsContentOverlayTheme = invalid!;
        settings.Normalize();
        Check(settings.SettingsTheme == "Light" && settings.SettingsContentOverlayTheme == "Default");
        Check(settings.SettingsBackgroundImageOverlayOpacity == .58);
    }
});

Run("Left right and unified drafts preserve live window theme and overlay changes", () =>
{
    var settings = new DockSettings
    {
        SettingsTheme = "Light", SettingsContentOverlayTheme = "Dark", SettingsBackgroundImageOverlayOpacity = .27
    };
    var draft = new SettingsEditorDraft(settings, DockSide.Right);
    settings.SettingsTheme = "Dark";
    settings.SettingsContentOverlayTheme = "Light";
    settings.SettingsBackgroundImageOverlayOpacity = .61;
    foreach (bool separate in new[] { true, false, true })
    {
        draft.SetSeparate(separate);
        foreach (DockSide side in new[] { DockSide.Left, DockSide.Right })
        {
            draft.SelectSide(side);
            draft.Current.Theme = side == DockSide.Left ? "Light" : "Dark";
            draft.Current.IconSize = side == DockSide.Left ? 52 : 68;
            draft.ApplyTo(settings);
            Check(settings.SettingsTheme == "Dark" && settings.SettingsContentOverlayTheme == "Light");
            Check(settings.SettingsBackgroundImageOverlayOpacity == .61);
        }
    }
});

Run("Legacy mask values survive new global defaults and every material JSON roundtrip", () =>
{
    const string legacy = """
        { "Theme": "Dark", "SettingsBackgroundStyle": "Image", "SettingsBackgroundOpacity": 0.72,
          "SettingsBackgroundImagePath": "C:\\Images\\window.png", "SettingsBackgroundImageOverlayOpacity": 0.17 }
        """;
    var settings = JsonSerializer.Deserialize<DockSettings>(legacy)!;
    settings.Normalize();
    Check(settings.SettingsTheme == "Default" && settings.SettingsContentOverlayTheme == "Default");
    Check(settings.SettingsBackgroundImageOverlayOpacity == .17);
    foreach (string material in new[] { "Solid", "Transparent", "Acrylic", "Image" })
    {
        settings.SettingsBackgroundStyle = material;
        settings.SettingsTheme = "Light";
        settings.SettingsContentOverlayTheme = "Dark";
        string json = JsonSerializer.Serialize(settings);
        using var document = JsonDocument.Parse(json);
        Check(document.RootElement.GetProperty("SettingsBackgroundImageOverlayOpacity").GetDouble() == .17);
        Check(!document.RootElement.TryGetProperty("SettingsContentOverlayOpacity", out _));
        var copy = JsonSerializer.Deserialize<DockSettings>(json)!;
        copy.Normalize();
        Check(copy.SettingsTheme == "Light" && copy.SettingsContentOverlayTheme == "Dark");
        Check(copy.SettingsBackgroundStyle == material && copy.SettingsBackgroundImageOverlayOpacity == .17);
        Check(copy.SettingsBackgroundImagePath == settings.SettingsBackgroundImagePath && copy.Theme == "Dark");
    }
});

Run("New window appearance and all taskbar materials survive a JSON roundtrip", () =>
{
    foreach (string material in new[] { "Transparent", "Acrylic", "Blur" })
    {
        var original = new DockSettings
        {
            SettingsBackgroundStyle = "Image", SettingsBackgroundOpacity = .72,
            SettingsBackgroundImagePath = "C:\\壁纸\\背景 1.png", SettingsBackgroundImageOverlayOpacity = .31,
            TaskbarTransparencyEnabled = true, TaskbarOpacity = .42, TaskbarMaterial = material,
            SeparateSideSettings = true, LeftSettings = new DockOptions { BackgroundStyle = "Transparent" },
            RightSettings = new DockOptions { BackgroundStyle = "Acrylic" }
        };
        original.Normalize();
        var copy = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(original))!;
        copy.Normalize();
        Check(copy.SettingsBackgroundStyle == original.SettingsBackgroundStyle && copy.SettingsBackgroundOpacity == .72);
        Check(copy.SettingsBackgroundImagePath == original.SettingsBackgroundImagePath && copy.SettingsBackgroundImageOverlayOpacity == .31);
        Check(copy.TaskbarTransparencyEnabled && copy.TaskbarMaterial == material && copy.TaskbarOpacity == .42);
        Check(copy.LeftSettings!.BackgroundStyle == "Transparent" && copy.RightSettings!.BackgroundStyle == "Acrylic");
    }
});

Run("Invalid intermediate numeric text preserves the last valid value", () =>
{
    double lastValid = 112;
    foreach (string input in new[] { "", "-", ".", "NaN", "Infinity", "1e999", "71", "321", "x" })
    {
        if (SettingsEditorDraft.TryNumber(input, 72, 320, out double value)) lastValid = value;
        Check(lastValid == 112, $"Invalid input accepted: {input}");
    }
    Check(SettingsEditorDraft.TryNumber("128", 72, 320, out double result) && result == 128);
});

Run("Numeric input uses the user's decimal separator and permits negative offsets", () =>
{
    var previous = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        Check(SettingsEditorDraft.TryNumber("112,5", 72, 320, out double value) && value == 112.5);
        Check(SettingsEditorDraft.TryNumber("-8,5", -12, 600, out value) && value == -8.5);
        Check(!SettingsEditorDraft.TryNumber("-12,5", -12, 600, out _));
    }
    finally { CultureInfo.CurrentCulture = previous; }
});

Run("Preview equality covers every appearance property", () =>
{
    foreach (var property in typeof(DockOptions).GetProperties())
    {
        var original = new DockOptions { MonitorDeviceName = @"\\.\DISPLAY1" };
        var changed = original.Copy();
        object value = property.PropertyType == typeof(bool) ? !(bool)property.GetValue(original)!
            : property.PropertyType == typeof(int) ? (int)property.GetValue(original)! + 1
            : property.PropertyType == typeof(double) ? (double)property.GetValue(original)! + 1
            : property.Name == "MonitorDeviceName" ? @"\\.\DISPLAY2" : (string)property.GetValue(original)! + "_changed";
        property.SetValue(changed, value);
        Check(!SettingsEditorDraft.SameOptions(original, changed), property.Name);
        Check(SettingsEditorDraft.SameOptions(changed, changed.Copy()), property.Name + " copy");
    }
});

Run("Modifier aliases display once without changing their source binding", () =>
{
    foreach (string alias in new[] { "Control", "LeftControl", "RightCtrl", "Ctrl+Control", "Ctrl+Ctrl" })
    {
        var source = new ShortcutBinding { Key = alias, Modifiers = "Control+Ctrl" };
        Check(QuickModeActivation.Display(source) == "Ctrl", alias);
        var normalized = ShortcutBindingCatalog.NormalizeBinding(source);
        Check(normalized.Key == "Ctrl" && normalized.Modifiers == "" && normalized.Enabled);
        Check(source.Key == alias && source.Modifiers == "Control+Ctrl", "Display mutated source");
    }
    var withExtraModifier = new ShortcutBinding { Key = "Control", Modifiers = "Ctrl+Alt+Shift" };
    Check(ShortcutBindingCatalog.DisplayKey(withExtraModifier) == "Shift+Alt+Ctrl");
    Check(ShortcutBindingCatalog.SameBinding(new() { Key = "i", Modifiers = "Shift+Control" }, new() { Key = "I", Modifiers = "Ctrl+Shift" }));
});

Run("All captured keyboard families resolve to canonical virtual keys", () =>
{
    foreach (var sample in new[]
    {
        ("i", "I", 0x49), ("Number1", "1", 0x31), ("D9", "9", 0x39), ("NumPad2", "NumPad2", 0x62),
        ("Left", "Left", 0x25), ("Prior", "PageUp", 0x21), ("F24", "F24", 0x87), ("OemPlus", "=", 0xBB),
        ("OemComma", ",", 0xBC), ("OemQuotes", "'", 0xDE), ("LeftWindows", "Win", 0x5B), ("RightMenu", "Alt", 0x12)
    })
    {
        Check(ShortcutBindingCatalog.NormalizeKey(sample.Item1) == sample.Item2, sample.Item1);
        Check(ShortcutBindingCatalog.VirtualKeyCode(sample.Item1) == sample.Item3, sample.Item1 + " virtual key");
        Check(ShortcutBindingCatalog.NormalizeBinding(new() { Key = sample.Item1 }).Enabled);
    }
    foreach (string invalid in new[] { "", "已关闭", "NotAKey", "NumPadX", "F25" })
        Check(!ShortcutBindingCatalog.NormalizeBinding(new() { Key = invalid }).Enabled, invalid);
});

Run("Conflict checking includes activation and neither fills nor mutates the caller dictionary", () =>
{
    var bindings = new Dictionary<DockShortcutAction, ShortcutBinding>
    {
        [DockShortcutAction.ActivateQuickMode] = new() { Key = "LeftControl", Modifiers = "Ctrl" },
        [DockShortcutAction.PreviousRow] = new() { Key = "Control" },
        [DockShortcutAction.NextRow] = new() { Key = "W", Enabled = false }
    };
    string before = JsonSerializer.Serialize(bindings);
    var conflicts = ShortcutBindingCatalog.FindConflicts(bindings);
    Check(conflicts.Count == 1 && conflicts[0].Contains("呼出") && conflicts[0].Contains("上移"));
    Check(JsonSerializer.Serialize(bindings) == before && bindings.Count == 3);
    bindings[DockShortcutAction.PreviousRow].Enabled = false;
    Check(ShortcutBindingCatalog.FindConflicts(bindings).Count == 0);
    Check(ShortcutBindingCatalog.FindConflicts(new Dictionary<DockShortcutAction, ShortcutBinding>()).Count == 0);
});

Run("Disabled shortcuts retain their binding through draft application and JSON reload", () =>
{
    var settings = new DockSettings();
    var draft = new SettingsEditorDraft(settings);
    draft.SetShortcutBinding(DockShortcutAction.NextList, new() { Key = "i", Modifiers = "Control+Shift", Enabled = false });
    draft.SetShortcutBinding(DockShortcutAction.ActivateQuickMode, new() { Key = "Alt", Enabled = false });
    draft.ApplyTo(settings);
    settings.Normalize();
    var reloaded = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(settings))!;
    reloaded.Normalize();
    var binding = reloaded.ShortcutBindings[DockShortcutAction.NextList];
    Check(!binding.Enabled && binding.Key == "I" && binding.Modifiers == "Ctrl+Shift");
    Check(ShortcutBindingCatalog.Display(binding) == "已关闭" && ShortcutBindingCatalog.DisplayKey(binding) == "Ctrl+Shift+I");
    Check(!reloaded.QuickModeActivation.Enabled && reloaded.QuickModeActivation.Key == "Alt");
    Check(ReferenceEquals(reloaded.QuickModeActivation, reloaded.ShortcutBindings[DockShortcutAction.ActivateQuickMode]));
    binding.Enabled = true;
    Check(ShortcutBindingCatalog.Display(binding) == "Ctrl+Shift+I");
});

Run("Legacy and dictionary activation migrate independently of JSON property order", () =>
{
    const string legacy = "\"QuickModeActivation\":{\"Key\":\"I\",\"Modifiers\":\"Control\"}";
    foreach (string entry in new[] { "", "\"ActivateQuickMode\":{\"Key\":\"Control\"}," })
    {
        string dictionary = "\"ShortcutBindings\":{" + entry + "\"NextList\":{\"Key\":\"P\"}}";
        foreach (string json in new[] { "{" + legacy + "," + dictionary + "}", "{" + dictionary + "," + legacy + "}" })
        {
            var settings = JsonSerializer.Deserialize<DockSettings>(json)!;
            settings.Normalize();
            Check(settings.QuickModeActivation.Key == "I" && settings.QuickModeActivation.Modifiers == "Ctrl", json);
            Check(settings.ShortcutBindings[DockShortcutAction.ActivateQuickMode].Key == "I");
            Check(settings.ShortcutBindings[DockShortcutAction.NextList].Key == "P");
        }
    }
    const string customDictionary = "\"ShortcutBindings\":{\"ActivateQuickMode\":{\"Key\":\"J\"}}";
    foreach (string json in new[] { "{" + legacy + "," + customDictionary + "}", "{" + customDictionary + "," + legacy + "}" })
    {
        var settings = JsonSerializer.Deserialize<DockSettings>(json)!;
        settings.Normalize();
        Check(settings.QuickModeActivation.Key == "J", "Canonical custom entry must win");
    }
});

Run("Shortcut drafts can be abandoned and cannot normalize or alias live activation", () =>
{
    var settings = new DockSettings { QuickModeActivation = new() { Key = "Control", Modifiers = "Control" } };
    settings.Normalize();
    string before = JsonSerializer.Serialize(settings);
    var abandoned = new SettingsEditorDraft(settings);
    abandoned.QuickModeActivation.Key = "I";
    abandoned.QuickModeActivation.Modifiers = "Alt";
    abandoned.SetShortcutBinding(DockShortcutAction.NextRow, new() { Key = "J" });
    Check(!abandoned.Matches(settings));
    Check(JsonSerializer.Serialize(settings) == before);
    var draft = new SettingsEditorDraft(settings);
    draft.SetShortcutBinding(DockShortcutAction.ActivateQuickMode, new() { Key = "K", Modifiers = "Shift" });
    Check(draft.QuickModeActivation.Key == "K" && !draft.Matches(settings));
    draft.ApplyTo(settings);
    Check(draft.Matches(settings) && settings.QuickModeActivation.Key == "K");
    draft.QuickModeActivation.Key = "L";
    Check(settings.QuickModeActivation.Key == "K", "Applied draft must not alias live settings");
    draft.SetQuickModeActivation(new() { Key = "M" });
    Check(draft.ShortcutBindings[DockShortcutAction.ActivateQuickMode].Key == "M");
});

Run("Replacing shortcut bindings at runtime honors canonical activation resets", () =>
{
    var settings = new DockSettings { QuickModeActivation = new() { Key = "I" } };
    settings.ShortcutBindings = ShortcutBindingCatalog.CreateDefaults();
    Check(settings.QuickModeActivation.Key == "Ctrl");
    settings.QuickModeActivation = new() { Key = "J" };
    Check(settings.ShortcutBindings[DockShortcutAction.ActivateQuickMode].Key == "J");
    settings.ShortcutBindings[DockShortcutAction.ActivateQuickMode] = new() { Key = "K" };
    Check(settings.QuickModeActivation.Key == "K");
});

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
