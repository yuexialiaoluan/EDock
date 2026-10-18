using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace EDock.Services;

/// <summary>
/// Owns only the settings window's background. The foreground controls retain
/// their original opacity, and each window keeps its own backdrop lifetime.
/// </summary>
internal sealed class SettingsWindowBackdrop : IDisposable
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private readonly Window _window;
    private readonly DispatcherQueue _dispatcher;
    private readonly Border _base = new();
    private readonly ImageBrush _imageBrush = new() { Stretch = Stretch.UniformToFill,
        AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
    private readonly Border _image;
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private DockBackdrop? _ownedBackdrop;
    private string? _backdropKey;
    private string _style = "Solid";
    private bool _dark;
    private double _opacity = 1;
    private string _requestedImagePath = "";
    private string? _loadedImagePath;
    private string? _failedImagePath;
    private int _imageVersion;
    private bool _loadingImage;
    private readonly bool _advancedEffectsSubscribed;
    private readonly bool _highContrastSubscribed;
    private readonly bool _colorValuesSubscribed;
    private string? _captionPalette;
    private volatile bool _disposed;

    public SettingsWindowBackdrop(Window window)
    {
        _window = window;
        _dispatcher = window.DispatcherQueue;
        Layer = new Grid
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _image = new Border { Background = _imageBrush, Visibility = Visibility.Collapsed };
        Layer.Children.Add(_base);
        Layer.Children.Add(_image);
        // Some unpackaged Windows sessions can read HighContrast but reject
        // HighContrastChanged with ERROR_NOT_FOUND. Each optional subscription
        // must stand alone so unsupported notifications cannot block settings.
        _advancedEffectsSubscribed = TrySubscribe(() => _uiSettings.AdvancedEffectsEnabledChanged += OnAdvancedEffectsChanged);
        _highContrastSubscribed = TrySubscribe(() => _accessibilitySettings.HighContrastChanged += OnHighContrastChanged);
        _colorValuesSubscribed = TrySubscribe(() => _uiSettings.ColorValuesChanged += OnColorValuesChanged);
        _window.Activated += OnWindowActivated;
    }

    /// <summary>Place behind the settings content, spanning the root grid.</summary>
    public Grid Layer { get; }

    /// <summary>
    /// Attach after the root grid's rows and columns are defined. The backdrop
    /// spans the title bar, navigation and settings rather than a content panel.
    /// </summary>
    public void AttachTo(Grid root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Layer.Parent is not null && !ReferenceEquals(Layer.Parent, root))
            throw new InvalidOperationException("The settings background already belongs to another layout.");
        Grid.SetRow(Layer, 0);
        Grid.SetColumn(Layer, 0);
        Grid.SetRowSpan(Layer, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(Layer, Math.Max(1, root.ColumnDefinitions.Count));
        if (Layer.Parent is null) root.Children.Insert(0, Layer);
    }

    /// <summary>Null on success; a readable explanation when an image cannot be loaded.</summary>
    public event Action<string?>? ImageStatusChanged;

    /// <summary>Raised on the UI thread after a system appearance refresh.</summary>
    public event Action? SystemAppearanceChanged;

    /// <summary>Windows uses a bright foreground when its app theme is dark.</summary>
    public bool SystemUsesDarkTheme
    {
        get
        {
            var foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
            return 2 * foreground.R + 5 * foreground.G + foreground.B > 8 * 128;
        }
    }

    public bool HighContrast => _accessibilitySettings.HighContrast;

    /// <summary>
    /// A successful explicit validation can retry a restored or replaced file at
    /// the same path. Keep the visible image until its replacement is decoded.
    /// </summary>
    public void InvalidateImage(string path)
    {
        if (_disposed || !string.Equals(NormalizeImagePath(path), _requestedImagePath, StringComparison.OrdinalIgnoreCase)) return;
        ++_imageVersion;
        _loadedImagePath = null;
        _failedImagePath = null;
        if (_style == "Image") StartImageLoad();
    }

    public void Apply(string style, bool dark, double opacity, string? imagePath)
    {
        if (_disposed) return;
        _style = style is "Transparent" or "Acrylic" or "Image" ? style : "Solid";
        _dark = dark;
        _opacity = ClampOpacity(opacity, 1);
        string path = NormalizeImagePath(imagePath);
        if (!string.Equals(path, _requestedImagePath, StringComparison.OrdinalIgnoreCase))
        {
            _requestedImagePath = path;
            _failedImagePath = null;
            ++_imageVersion;
        }
        if (_style != "Image")
        {
            // Backgrounds are not shared with the dock or retained after leaving
            // image mode. At most one decoded settings background remains alive.
            ++_imageVersion;
            _imageBrush.ImageSource = null;
            _loadedImagePath = null;
            _failedImagePath = null;
        }
        UpdateLayers();
        if (_style == "Image") StartImageLoad();
    }

    /// <summary>Checks a local image at thumbnail resolution before persisting its path.</summary>
    public static async Task<bool> ValidateImageAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string normalized = NormalizeImagePath(path);
        if (!IsSupportedLocalImage(normalized)) return false;
        var image = await ShellIconService.LoadImageAsync(normalized, 64);
        token.ThrowIfCancellationRequested();
        return image is not null;
    }

    private static double ClampOpacity(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;

    private static string NormalizeImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))); }
        catch { return path.Trim(); }
    }

    private static bool IsSupportedLocalImage(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && File.Exists(path);

    private void UpdateLayers()
    {
        if (_disposed) return;
        bool highContrast = HighContrast;
        bool effects = !highContrast && _uiSettings.AdvancedEffectsEnabled;
        UpdateCaptionColors(highContrast);
        var tint = highContrast ? _uiSettings.GetColorValue(UIColorType.Background)
            : _dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252);
        bool hasImage = _style == "Image" && _imageBrush.ImageSource is not null && !highContrast;
        bool acrylic = _style == "Acrylic" && effects;
        // Even a plain color needs a transparent HWND when its opacity is below
        // one. With Windows effects disabled, retain an opaque accessible base.
        bool transparent = effects && (_style != "Solid" || _opacity < 1);
        // Theme, tint and opacity can update in place. Reconnecting a transparent
        // composition target during a theme switch can race WinUI rendering.
        string key = $"{acrylic}:{transparent}";
        if (_backdropKey != key)
        {
            DockBackdrop? backdrop = transparent ? new DockBackdrop(acrylic, _dark, _opacity) : null;
            _window.SystemBackdrop = backdrop;
            _ownedBackdrop = backdrop;
            _backdropKey = key;
        }
        _ownedBackdrop?.Update(_dark, _opacity);
        _base.Background = new SolidColorBrush(tint);
        _base.Opacity = !effects ? 1 : _style switch
        {
            "Acrylic" => 0,
            "Image" => hasImage ? 0 : 1,
            _ => _opacity
        };
        _image.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        _image.Opacity = _opacity;
    }

    private void UpdateCaptionColors(bool highContrast)
    {
        // Native caption buttons otherwise retain an opaque system-colored
        // strip even when XAML extends the selected material into the title bar.
        // Keep their idle backgrounds clear; only hover/press need a surface.
        if (!_window.ExtendsContentIntoTitleBar || !AppWindowTitleBar.IsCustomizationSupported()) return;
        var foreground = highContrast ? _uiSettings.GetColorValue(UIColorType.Foreground)
            : _dark ? Colors.White : ColorHelper.FromArgb(255, 32, 36, 46);
        var background = highContrast ? _uiSettings.GetColorValue(UIColorType.Background) : Colors.Transparent;
        string palette = $"{highContrast}:{_dark}:{foreground}:{background}";
        if (_captionPalette == palette) return;
        var titleBar = _window.AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = highContrast ? null : foreground;
        titleBar.ButtonPressedForegroundColor = highContrast ? null : foreground;
        var inactive = foreground;
        if (!highContrast) inactive.A = 150;
        titleBar.InactiveForegroundColor = inactive;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = highContrast
            ? null
            : _dark ? ColorHelper.FromArgb(24, 255, 255, 255) : ColorHelper.FromArgb(18, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = highContrast
            ? null
            : _dark ? ColorHelper.FromArgb(38, 255, 255, 255) : ColorHelper.FromArgb(28, 0, 0, 0);
        _captionPalette = palette;
    }

    private void StartImageLoad()
    {
        if (_loadingImage || _disposed) return;
        _loadingImage = true;
        _ = LoadRequestedImageAsync();
    }

    private async Task LoadRequestedImageAsync()
    {
        try
        {
            // Coalesce edits while a decoder is running. Rapid changes never
            // create several full-sized background decodes in parallel.
            while (!_disposed && _style == "Image"
                && !string.Equals(_loadedImagePath, _requestedImagePath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_failedImagePath, _requestedImagePath, StringComparison.OrdinalIgnoreCase))
            {
                int version = _imageVersion;
                string path = _requestedImagePath;
                ImageSource? source = null;
                if (IsSupportedLocalImage(path))
                {
                    try
                    {
                        // XAML decodes straight to a bounded render surface,
                        // avoiding a retained original-resolution CPU bitmap.
                        source = await ShellIconService.LoadImageAsync(path, 2048);
                    }
                    catch { /* A bad image must not prevent access to settings. */ }
                }
                if (_disposed) return;
                if (_style != "Image" || version != _imageVersion) continue;
                if (source is null)
                {
                    _failedImagePath = path;
                    ImageStatusChanged?.Invoke(string.IsNullOrWhiteSpace(path) ? "请选择一张背景图片。"
                        : "图片无法读取，已保留上次有效的背景。请检查路径或选择其他图片。");
                    continue;
                }
                _imageBrush.ImageSource = source;
                _loadedImagePath = path;
                _failedImagePath = null;
                UpdateLayers();
                ImageStatusChanged?.Invoke(null);
            }
        }
        finally { _loadingImage = false; }
    }

    private void OnAdvancedEffectsChanged(UISettings sender, object args) => QueueRefresh();
    private void OnHighContrastChanged(AccessibilitySettings sender, object args) => QueueRefresh();
    private void OnColorValuesChanged(UISettings sender, object args) => QueueRefresh();
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // Re-read system settings when returning from Windows Settings even if
        // this session cannot deliver one of the optional WinRT notifications.
        RefreshSystemAppearance();
    }

    private void QueueRefresh()
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(RefreshSystemAppearance);
    }

    private void RefreshSystemAppearance()
    {
        if (_disposed) return;
        UpdateLayers();
        // Apply and image completion deliberately do not raise this event. A
        // settings owner may apply the resulting theme while handling it.
        SystemAppearanceChanged?.Invoke();
    }

    private static bool TrySubscribe(Action subscribe)
    {
        try { subscribe(); return true; }
        catch { return false; }
    }

    private static void TryUnsubscribe(bool subscribed, Action unsubscribe)
    {
        if (!subscribed) return;
        try { unsubscribe(); }
        catch { /* Optional system events must never interfere with closing. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_imageVersion;
        TryUnsubscribe(_advancedEffectsSubscribed, () => _uiSettings.AdvancedEffectsEnabledChanged -= OnAdvancedEffectsChanged);
        TryUnsubscribe(_highContrastSubscribed, () => _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged);
        TryUnsubscribe(_colorValuesSubscribed, () => _uiSettings.ColorValuesChanged -= OnColorValuesChanged);
        _window.Activated -= OnWindowActivated;
        _imageBrush.ImageSource = null;
        _loadedImagePath = null;
        ImageStatusChanged = null;
        SystemAppearanceChanged = null;
        if (ReferenceEquals(_window.SystemBackdrop, _ownedBackdrop)) _window.SystemBackdrop = null;
        _ownedBackdrop = null;
    }
}
