using System.Globalization;
using EDock.Models;

namespace EDock.Services;

/// <summary>Edits appearance without copying or replacing the application's shortcut lists.</summary>
internal sealed class SettingsEditorDraft
{
    private readonly HashSet<int> _autoHideChanged = new();
    public DockOptions Shared { get; }
    public DockOptions Left { get; private set; }
    public DockOptions Right { get; private set; }
    public bool Separate { get; private set; }
    public bool HasIndependent { get; private set; }
    public DockSide Side { get; private set; }
    public int Scope => Separate ? Side == DockSide.Left ? 1 : 2 : 0;
    public DockOptions Current => Options(Scope);

    public SettingsEditorDraft(DockSettings settings, DockSide? initialSide = null)
    {
        Shared = settings.Copy();
        Left = settings.GetStoredSideOptions(DockSide.Left);
        Right = settings.GetStoredSideOptions(DockSide.Right);
        Separate = settings.SeparateSideSettings;
        HasIndependent = Separate || settings.LeftSettings is not null || settings.RightSettings is not null;
        Side = initialSide ?? DockSide.Left;
    }

    public DockOptions Options(int scope) => scope switch { 1 => Left, 2 => Right, _ => Shared };
    public void SelectSide(DockSide side) => Side = side;

    public void SetSeparate(bool separate)
    {
        if (separate && !HasIndependent)
        {
            Left = Shared.Copy();
            Right = Shared.Copy();
            HasIndependent = true;
        }
        Separate = separate;
    }

    public bool DisplayAutoHide(DockSettings settings)
    {
        if (Scope != 0 && !_autoHideChanged.Contains(Scope) && !_autoHideChanged.Contains(0))
        {
            bool? pinned = Side == DockSide.Left ? settings.LeftAutoHideOverride : settings.RightAutoHideOverride;
            if (pinned.HasValue) return pinned.Value;
        }
        return Current.AutoHide;
    }

    public void SetAutoHide(bool value)
    {
        Current.AutoHide = value;
        _autoHideChanged.Add(Scope);
    }

    public void ApplyTo(DockSettings settings)
    {
        settings.CopyFrom(Shared);
        settings.SeparateSideSettings = Separate;
        if (HasIndependent)
        {
            settings.LeftSettings = Left.Copy();
            settings.RightSettings = Right.Copy();
        }
        if (_autoHideChanged.Contains(0) || _autoHideChanged.Contains(1))
            settings.ClearSideAutoHideOverride(DockSide.Left);
        if (_autoHideChanged.Contains(0) || _autoHideChanged.Contains(2))
            settings.ClearSideAutoHideOverride(DockSide.Right);
    }

    public void AcceptApplied() => _autoHideChanged.Clear();

    public bool Matches(DockSettings settings) => Separate == settings.SeparateSideSettings
        && SameOptions(Shared, settings)
        && (!HasIndependent || settings.LeftSettings is not null && settings.RightSettings is not null
            && SameOptions(Left, settings.LeftSettings) && SameOptions(Right, settings.RightSettings))
        && (!(_autoHideChanged.Contains(0) || _autoHideChanged.Contains(1)) || !settings.LeftAutoHideOverride.HasValue)
        && (!(_autoHideChanged.Contains(0) || _autoHideChanged.Contains(2)) || !settings.RightAutoHideOverride.HasValue);

    internal static bool SameOptions(DockOptions a, DockOptions b) =>
        a.DockWidth == b.DockWidth && a.AutoHide == b.AutoHide && a.HideDelayMs == b.HideDelayMs
        && a.SuppressRevealWhenFullscreen == b.SuppressRevealWhenFullscreen && a.KeepPinnedBelowWindows == b.KeepPinnedBelowWindows
        && a.RevealDelayMs == b.RevealDelayMs && a.RevealDurationMs == b.RevealDurationMs
        && a.HideDurationMs == b.HideDurationMs && a.AnimationStyle == b.AnimationStyle
        && a.LoopScrolling == b.LoopScrolling && a.Theme == b.Theme
        && a.ShowShortcutNames == b.ShowShortcutNames && a.HeaderText == b.HeaderText
        && a.ShortcutDisplayMode == b.ShortcutDisplayMode && a.IconSize == b.IconSize
        && a.IconSpacing == b.IconSpacing && a.NormalColumns == b.NormalColumns
        && a.FooterOffset == b.FooterOffset && a.DockFrameStyle == b.DockFrameStyle
        && a.FloatingTopInset == b.FloatingTopInset && a.FloatingBottomInset == b.FloatingBottomInset
        && a.BackgroundStyle == b.BackgroundStyle && a.BackgroundImagePath == b.BackgroundImagePath
        && a.BackgroundOpacity == b.BackgroundOpacity && a.BackgroundImageOverlayOpacity == b.BackgroundImageOverlayOpacity;

    internal static bool TryNumber(string? text, double minimum, double maximum, out double value)
    {
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture, out value) && double.IsFinite(value)
            && value >= minimum && value <= maximum;
    }
}
