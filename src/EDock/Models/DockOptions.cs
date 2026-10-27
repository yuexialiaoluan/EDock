namespace EDock.Models;

/// <summary>Appearance and behavior shared by one dock or both docks.</summary>
public class DockOptions
{
    // Empty means the primary display. Otherwise this is the stable Windows
    // monitor device name (for example \\.\DISPLAY2).
    public string MonitorDeviceName { get; set; } = string.Empty;
    public double DockWidth { get; set; } = 88;
    public bool AutoHide { get; set; } = true;
    public bool SuppressRevealWhenFullscreen { get; set; } = true;
    public bool KeepPinnedBelowWindows { get; set; } = true;
    public int HideDelayMs { get; set; } = 650;
    public int RevealDelayMs { get; set; } = 120;
    public int RevealDurationMs { get; set; } = 250;
    public int HideDurationMs { get; set; } = 250;
    public string AnimationStyle { get; set; } = "Slide";
    public bool LoopScrolling { get; set; } = true;
    public string Theme { get; set; } = "Default";
    public bool ShowShortcutNames { get; set; } = true;
    public string HeaderText { get; set; } = "ED";
    public string ShortcutDisplayMode { get; set; } = "Icon";
    public double IconSize { get; set; } = 40;
    public double IconSpacing { get; set; } = 8;
    public int NormalColumns { get; set; } = 1;
    public double FooterOffset { get; set; } = 0;
    public string DockFrameStyle { get; set; } = "Edge";
    public double FloatingTopInset { get; set; } = 8;
    public double FloatingBottomInset { get; set; } = 8;
    public string BackgroundStyle { get; set; } = "Solid";
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double BackgroundOpacity { get; set; } = .82;
    public double BackgroundImageOverlayOpacity { get; set; } = .35;

    public DockOptions Copy() => new()
    {
        MonitorDeviceName = MonitorDeviceName,
        DockWidth = DockWidth, AutoHide = AutoHide, HideDelayMs = HideDelayMs, RevealDelayMs = RevealDelayMs,
        SuppressRevealWhenFullscreen = SuppressRevealWhenFullscreen, KeepPinnedBelowWindows = KeepPinnedBelowWindows,
        RevealDurationMs = RevealDurationMs, HideDurationMs = HideDurationMs, AnimationStyle = AnimationStyle, LoopScrolling = LoopScrolling,
        Theme = Theme, ShowShortcutNames = ShowShortcutNames, HeaderText = HeaderText, ShortcutDisplayMode = ShortcutDisplayMode, DockFrameStyle = DockFrameStyle,
        IconSize = IconSize, IconSpacing = IconSpacing, NormalColumns = NormalColumns, FooterOffset = FooterOffset,
        FloatingTopInset = FloatingTopInset, FloatingBottomInset = FloatingBottomInset,
        BackgroundStyle = BackgroundStyle, BackgroundImagePath = BackgroundImagePath, BackgroundOpacity = BackgroundOpacity,
        BackgroundImageOverlayOpacity = BackgroundImageOverlayOpacity
    };

    public void CopyFrom(DockOptions source)
    {
        MonitorDeviceName = source.MonitorDeviceName;
        DockWidth = source.DockWidth; AutoHide = source.AutoHide; HideDelayMs = source.HideDelayMs; RevealDelayMs = source.RevealDelayMs;
        SuppressRevealWhenFullscreen = source.SuppressRevealWhenFullscreen; KeepPinnedBelowWindows = source.KeepPinnedBelowWindows;
        RevealDurationMs = source.RevealDurationMs; HideDurationMs = source.HideDurationMs; AnimationStyle = source.AnimationStyle; LoopScrolling = source.LoopScrolling;
        Theme = source.Theme; ShowShortcutNames = source.ShowShortcutNames; HeaderText = source.HeaderText; ShortcutDisplayMode = source.ShortcutDisplayMode; DockFrameStyle = source.DockFrameStyle;
        IconSize = source.IconSize; IconSpacing = source.IconSpacing; NormalColumns = source.NormalColumns; FooterOffset = source.FooterOffset;
        FloatingTopInset = source.FloatingTopInset; FloatingBottomInset = source.FloatingBottomInset;
        BackgroundStyle = source.BackgroundStyle; BackgroundImagePath = source.BackgroundImagePath; BackgroundOpacity = source.BackgroundOpacity;
        BackgroundImageOverlayOpacity = source.BackgroundImageOverlayOpacity;
        MonitorDeviceName = source.MonitorDeviceName;
    }

    internal void NormalizeOptions()
    {
        MonitorDeviceName = (MonitorDeviceName ?? string.Empty).Trim();
        if (MonitorDeviceName.Length > 128) MonitorDeviceName = string.Empty;
        DockWidth = double.IsFinite(DockWidth) ? Math.Clamp(DockWidth, 72, 320) : 88;
        if (HideDelayMs < 0 || HideDelayMs > 60_000) HideDelayMs = 650;
        if (RevealDelayMs < 0 || RevealDelayMs > 10_000) RevealDelayMs = 120;
        RevealDurationMs = Math.Clamp(RevealDurationMs, 80, 1500);
        HideDurationMs = Math.Clamp(HideDurationMs, 80, 1500);
        if (AnimationStyle is not ("Slide" or "Reveal" or "None")) AnimationStyle = "Slide";
        if (Theme is not ("Default" or "Light" or "Dark")) Theme = "Default";
        HeaderText = (HeaderText ?? "ED").Trim();
        if (HeaderText.Length > 32) HeaderText = HeaderText[..32];
        if (ShortcutDisplayMode is not ("Icon" or "Wide" or "Text")) ShortcutDisplayMode = "Icon";
        IconSize = double.IsFinite(IconSize) ? Math.Clamp(IconSize, 16, 128) : 40;
        IconSpacing = double.IsFinite(IconSpacing) ? Math.Clamp(IconSpacing, 0, 64) : 8;
        NormalColumns = Math.Clamp(NormalColumns, 1, 6);
        FooterOffset = double.IsFinite(FooterOffset) ? Math.Clamp(FooterOffset, -12, 600) : 0;
        if (DockFrameStyle is not ("Edge" or "Floating" or "FloatingSquare")) DockFrameStyle = "Edge";
        FloatingTopInset = double.IsFinite(FloatingTopInset) ? Math.Clamp(FloatingTopInset, 0, 200) : 8;
        FloatingBottomInset = double.IsFinite(FloatingBottomInset) ? Math.Clamp(FloatingBottomInset, 0, 200) : 8;
        if (BackgroundStyle is not ("Solid" or "Transparent" or "Acrylic" or "Image")) BackgroundStyle = "Solid";
        BackgroundImagePath = (BackgroundImagePath ?? string.Empty).Trim();
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0, 1) : .82;
        BackgroundImageOverlayOpacity = double.IsFinite(BackgroundImageOverlayOpacity) ? Math.Clamp(BackgroundImageOverlayOpacity, 0, 1) : .35;
        MonitorDeviceName = string.IsNullOrWhiteSpace(MonitorDeviceName) ? "Primary" : MonitorDeviceName.Trim();
    }
}
