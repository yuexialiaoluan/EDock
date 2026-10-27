using EDock.Services;
using EDock.Models;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using System.Globalization;
using System.Security.Cryptography;
using Windows.Graphics.Imaging;

namespace EDock;

public sealed class SettingsWindow : Window
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private readonly Grid _root = new();
    private readonly Dictionary<string, StackPanel> _pages = new();
    private readonly Dictionary<string, ScrollViewer> _pageScrolls = new();
    private readonly Grid _pageHost = new();
    private readonly ListView _navigation = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false };
    private readonly Grid _titleBar = new() { Height = 40, Padding = new Thickness(18, 0, 144, 0) };
    private readonly InfoBar _message = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(20, 0, 20, 12) };
    private readonly Border _leftStatus = new();
    private readonly Border _rightStatus = new();
    private readonly TextBlock _leftScopeStatus = Help("");
    private readonly TextBlock _rightScopeStatus = Help("");
    private string _buildingPage = "基本设置";
    private readonly ToggleSwitch _left;
    private readonly ToggleSwitch _right;
    private readonly ToggleSwitch _autoHide;
    private readonly ToggleSwitch _suppressRevealWhenFullscreen;
    private readonly ToggleSwitch _keepPinnedBelowWindows;
    private readonly ToggleSwitch _startup;
    private readonly ToggleSwitch _removeDesktopShortcut;
    private readonly ToggleSwitch _quickMode;
    private readonly ComboBox _quickModeSide;
    private sealed record ShortcutRow(Button Key, ToggleSwitch Enabled, StackPanel Container)
    {
        public ShortcutEditControls? Edit { get; set; }
    }
    private sealed record ShortcutEditControls(Border Panel, TextBlock Candidate, TextBlock Hint, Button Save);
    private readonly Dictionary<DockShortcutAction, ShortcutRow> _shortcutRows = new();
    private readonly StackPanel _shortcutEditorHost = new();
    private DockShortcutAction? _editingShortcut;
    private ShortcutBinding? _shortcutCandidate;
    private ShortcutKeyRecorder? _shortcutRecorder;
    private bool _refreshingShortcuts;
    private readonly TextBlock _shortcutConflictHint = Help("");
    private readonly ToggleSwitch _taskbarTransparency;
    private readonly Slider _taskbarOpacity;
    private readonly ComboBox _taskbarMaterial;
    private readonly TextBlock _taskbarOpacityLabel = new();
    private readonly TextBlock _taskbarMaterialHelp = Help("");
    private readonly TextBlock _taskbarStatus = Help("");
    private readonly ToggleSwitch _startMenuTransparency;
    private readonly Slider _startMenuOpacity;
    private readonly TextBlock _startMenuOpacityLabel = new();
    private readonly TextBlock _startMenuStatus = Help("");
    private readonly ToggleSwitch _startButtonReplacement;
    private readonly Slider _startButtonSize;
    private readonly TextBlock _startButtonSizeLabel = new();
    private readonly TextBlock _startButtonStatus = Help("");
    private readonly TextBlock[] _startButtonImageHints = { Help(""), Help(""), Help(""), Help("") };
    private readonly TextBox[] _startButtonImagePaths = new TextBox[4];
    private readonly Button[] _startButtonPickButtons = new Button[4];
    private readonly string[] _startButtonImageValues = new string[4];
    private readonly string[] _startButtonImageTexts = new string[4];
    private readonly long[] _startButtonImageVersions = new long[4];
    private readonly CancellationTokenSource?[] _startButtonImageCancellation = new CancellationTokenSource?[4];
    private readonly Task?[] _startButtonImageTasks = new Task?[4];
    private bool _startButtonEnabled;
    private double _startButtonSizeValue;
    private readonly ToggleSwitch _showNames;
    private readonly NumberBox _width;
    private readonly NumberBox _iconSize;
    private readonly NumberBox _iconSpacing;
    private readonly NumberBox _normalColumns;
    private readonly NumberBox _footerOffset;
    private readonly NumberBox _hideDelay;
    private readonly NumberBox _revealDelay;
    private readonly NumberBox _revealDuration;
    private readonly NumberBox _hideDuration;
    private readonly ComboBox _animationStyle;
    private readonly ToggleSwitch _loopScrolling;
    private readonly ComboBox _scope;
    private readonly ComboBox _sideChoice;
    private readonly SettingsEditorDraft _draft;
    private bool _loadingOptions = true;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _previewTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _persistTimer;
    private readonly AppWindow _appWindow;
    private readonly Dictionary<NumberBox, NumberBinding> _numbers = new();
    private readonly string[] _imageText = new string[3];
    private readonly int[] _imageChoices = new int[3];
    private readonly string[] _imageHints = new string[3];
    private readonly long[] _imageVersions = new long[3];
    private readonly CancellationTokenSource?[] _imageCancellation = new CancellationTokenSource?[3];
    private readonly Task?[] _imageValidationTasks = new Task?[3];
    private Task<bool>? _flushTask;
    private bool _closePending;
    private bool _closeApproved;
    private bool _previewPending;
    private bool _needsPersist;
    private bool _flushing;
    private bool _leftEnabled;
    private bool _rightEnabled;
    private bool _removeDesktop;
    private bool _quickEnabled;
    private string _quickSide;
    private bool _startupValue;
    private bool _startupActual;
    private bool _taskbarEnabled;
    private double _taskbarOpacityValue;
    private string _taskbarMaterialValue;
    private bool _startMenuEnabled;
    private double _startMenuOpacityValue;
    private readonly SettingsWindowBackdrop _settingsBackdrop;
    private readonly List<Border> _sectionCards = new();
    private readonly List<Border> _textSurfaces = new();
    private readonly List<Border> _navigationSurfaces = new();
    private readonly List<Control> _contentControls = new();
    private readonly List<Panel> _backgroundSurfaces = new();
    private readonly List<Border> _shellDividers = new();
    private readonly ComboBox _settingsBackground;
    private readonly ComboBox _settingsTheme;
    private readonly ComboBox _settingsOverlayTheme;
    private readonly Slider _settingsOpacity;
    private readonly TextBox _settingsImagePath;
    private readonly Slider _settingsContentOverlay;
    private readonly StackPanel _settingsImageOptions = new() { Spacing = 12 };
    private readonly TextBlock _settingsImageHint = Help("");
    private readonly Button _settingsPickImage;
    private string _settingsBackgroundValue;
    private string _settingsThemeValue;
    private string _settingsOverlayThemeValue;
    private double _settingsOpacityValue;
    private string _settingsImageValue;
    private double _settingsOverlayValue;
    private CancellationTokenSource? _settingsImageCancellation;
    private Task? _settingsImageTask;
    private long _settingsImageVersion;
    private string _settingsImageText;
    private readonly ComboBox _displayMode;
    private readonly ComboBox _theme;
    private readonly ComboBox _frameStyle;
    private readonly NumberBox _floatingTopInset;
    private readonly NumberBox _floatingBottomInset;
    private readonly ComboBox _backgroundStyle;
    private readonly TextBox _backgroundImagePath;
    private readonly Slider _backgroundOpacity;
    private readonly Slider _imageOverlayOpacity;
    private readonly StackPanel _opacityOptions = new() { Spacing = 8 };
    private readonly StackPanel _imageOptions = new() { Spacing = 12 };
    private readonly TextBlock _materialHelp = Help("");
    private readonly Button _pickImage;
    private readonly TextBlock _imageHint = Help("");
    private bool _closed;
    private nint _quickModeWindowHandle;
    private int _modalInteractions;
    private readonly HashSet<ComboBox> _openChoices = new();
    private readonly HashSet<TextBox> _menuInputs = new();
    private readonly HashSet<FlyoutBase> _guardedFlyouts = new();
    private readonly HashSet<FlyoutBase> _openFlyouts = new();
    private readonly HashSet<Popup> _guardedPopups = new();
    private readonly HashSet<Popup> _openPopups = new();
    internal nint QuickModeWindowHandle => _closed ? 0 : _quickModeWindowHandle;
    internal bool IsQuickModeBusy => _closed || _closePending || _modalInteractions > 0
        || _openChoices.Count > 0 || _openFlyouts.Count > 0 || _openPopups.Count > 0
        || _editingShortcut is not null;

    public SettingsWindow(DockSide? initialSide = null)
    {
        Title = "EDock 设置";
        App.SetWindowIcon(this);
        var settings = App.Current.Settings;
        _draft = new SettingsEditorDraft(settings, initialSide);
        _leftEnabled = settings.LeftEnabled; _rightEnabled = settings.RightEnabled;
        _removeDesktop = settings.RemoveDesktopShortcutAfterImport;
        _quickEnabled = settings.QuickModeEnabled; _quickSide = settings.QuickModeSide;
        _startupValue = settings.OpenOnStartup;
        _taskbarEnabled = settings.TaskbarTransparencyEnabled;
        _taskbarOpacityValue = settings.TaskbarOpacity;
        _taskbarMaterialValue = settings.TaskbarMaterial;
        _startMenuEnabled = settings.StartMenuTransparencyEnabled;
        _startMenuOpacityValue = settings.StartMenuOpacity;
        _startButtonEnabled = settings.StartButtonReplacementEnabled;
        _startButtonSizeValue = settings.StartButtonIconSize;
        _startButtonImageValues[0] = settings.StartButtonImagePath;
        _startButtonImageValues[1] = settings.StartButtonHoverImagePath;
        _startButtonImageValues[2] = settings.StartButtonPressedImagePath;
        _startButtonImageValues[3] = settings.StartButtonActivatedImagePath;
        Array.Copy(_startButtonImageValues, _startButtonImageTexts, _startButtonImageValues.Length);
        _settingsBackgroundValue = settings.SettingsBackgroundStyle;
        _settingsThemeValue = settings.SettingsTheme;
        _settingsOverlayThemeValue = settings.SettingsContentOverlayTheme;
        _settingsOpacityValue = settings.SettingsBackgroundOpacity;
        _settingsImageValue = settings.SettingsBackgroundImagePath;
        _settingsImageText = _settingsImageValue;
        _settingsOverlayValue = settings.SettingsBackgroundImageOverlayOpacity;
        _settingsBackdrop = new SettingsWindowBackdrop(this);
        for (int i = 0; i < 3; i++)
        {
            _imageText[i] = _draft.Options(i).BackgroundImagePath;
            _imageChoices[i] = BackgroundIndex(_draft.Options(i).BackgroundStyle);
            _imageHints[i] = "";
        }
        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.IsRepeating = false; _previewTimer.Interval = TimeSpan.FromMilliseconds(40);
        _previewTimer.Tick += OnPreviewTimerTick;
        _persistTimer = DispatcherQueue.CreateTimer();
        _persistTimer.IsRepeating = false; _persistTimer.Interval = TimeSpan.FromMilliseconds(500);
        _persistTimer.Tick += OnPersistTimerTick;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var windowLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        windowLabel.Children.Add(new Border { Width = 21, Height = 21, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 51, 121, 230)),
            Child = new TextBlock { Text = "E", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        windowLabel.Children.Add(new TextBlock { Text = "EDock", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        windowLabel.Children.Add(new TextBlock { Text = "设置", FontSize = 12, Opacity = .55, VerticalAlignment = VerticalAlignment.Center });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var brandSurface = new Border { Child = windowLabel, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        _textSurfaces.Add(brandSurface); _titleBar.Children.Add(brandSurface);
        var saveStatus = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        saveStatus.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ColorHelper.FromArgb(255, 64, 151, 116)), VerticalAlignment = VerticalAlignment.Center });
        saveStatus.Children.Add(new TextBlock { Text = "自动保存", FontSize = 11, Opacity = .6 });
        var saveSurface = new Border { Child = saveStatus, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _textSurfaces.Add(saveSurface); Grid.SetColumn(saveSurface, 1); _titleBar.Children.Add(saveSurface);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_titleBar, "EDock 设置标题栏");
        Grid.SetColumnSpan(_titleBar, 2); _root.Children.Add(_titleBar);
        var sidebar = new Grid { Padding = new Thickness(10, 14, 10, 12), RowSpacing = 16 };
        _backgroundSurfaces.Add(sidebar);
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var categories = new[] { ("基本设置", "\uE713"), ("图标与布局", "\uE80A"), ("背景与外观", "\uE790"), ("快捷模式", "\uE765"), ("附属功能", "\uEA86"), ("设置窗口", "\uE737") };
        foreach (var (name, glyph) in categories)
        {
            var item = new ListViewItem { Tag = name, Padding = new Thickness(0), Margin = new Thickness(0, 2, 0, 2), CornerRadius = new CornerRadius(7), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 11 };
            label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
            label.Children.Add(new TextBlock { Text = name, FontSize = 13 });
            // Keep the mask inside the item: its selection/hover template owns
            // the outer Background and may replace it during visual-state changes.
            var navigationSurface = new Border { Child = label, Padding = new Thickness(11, 9, 9, 9),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
            item.Content = navigationSurface;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, name);
            _navigation.Items.Add(item);
            _textSurfaces.Add(navigationSurface); _navigationSurfaces.Add(navigationSurface);
            var page = new StackPanel { Spacing = 12, Padding = new Thickness(20, 0, 20, 20) };
            _pages.Add(name, page);
            var scroll = new ScrollViewer { Content = page,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // Unvisited pages must not realize their control templates and native
            // text-input resources merely because the settings window is open.
            _pageScrolls.Add(name, scroll);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_navigation, "设置分类");
        sidebar.Children.Add(_navigation);
        var navigationFrame = new Border { Child = sidebar, BorderThickness = new Thickness(0, 0, 1, 0) };
        _shellDividers.Add(navigationFrame);
        Grid.SetRow(navigationFrame, 1); _root.Children.Add(navigationFrame);

        var workspace = new Grid();
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(workspace, 1); Grid.SetColumn(workspace, 1); _root.Children.Add(workspace);
        var heading = new Grid { ColumnSpacing = 8, Padding = new Thickness(20, 8, 20, 12) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _left = Toggle("左侧边栏", settings.LeftEnabled);
        _right = Toggle("右侧边栏", settings.RightEnabled);
        BuildStatusCard(_leftStatus, _left, "左侧");
        BuildStatusCard(_rightStatus, _right, "右侧");
        heading.Children.Add(_leftStatus);
        Grid.SetColumn(_rightStatus, 1); heading.Children.Add(_rightStatus);
        _scope = Choice("左右设置方式", new[] { "左右统一设置", "左右独立设置" }, settings.SeparateSideSettings ? 1 : 0);
        _sideChoice = Choice("正在编辑的边栏", new[] { "左侧边栏", "右侧边栏" }, _draft.Side == DockSide.Right ? 1 : 0);
        _contentControls.Add(_scope); _contentControls.Add(_sideChoice);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_scope, "左右设置方式");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_sideChoice, "正在编辑的边栏");
        _scope.Header = null; _sideChoice.Header = null;
        _scope.Width = 148; _sideChoice.Width = 118;
        _scope.MinWidth = 0; _sideChoice.MinWidth = 0;
        _scope.FontSize = 12; _sideChoice.FontSize = 12;
        _scope.VerticalAlignment = VerticalAlignment.Center; _sideChoice.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_scope, "选择左右边栏统一设置或独立设置");
        ToolTipService.SetToolTip(_sideChoice, "选择当前编辑的边栏");
        _sideChoice.Visibility = settings.SeparateSideSettings ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(_scope, 3); heading.Children.Add(_scope);
        Grid.SetColumn(_sideChoice, 4); heading.Children.Add(_sideChoice);
        workspace.Children.Add(heading);
        Grid.SetRow(_message, 1); workspace.Children.Add(_message);
        Grid.SetRow(_pageHost, 2); workspace.Children.Add(_pageHost);

        var behavior = AddSection("边栏与自动隐藏", "鼠标靠近屏幕边缘时展开，离开后收回。固定按钮只影响所在的一侧。左右均可关闭，之后可从系统托盘打开设置重新开启。");
        _autoHide = Toggle("自动隐藏", settings.AutoHide);
        behavior.Children.Add(_autoHide);
        _suppressRevealWhenFullscreen = Toggle("其他应用最大化或全屏时不呼出边栏", settings.SuppressRevealWhenFullscreen);
        _keepPinnedBelowWindows = Toggle("固定时不置于其他应用上方", settings.KeepPinnedBelowWindows);
        behavior.Children.Add(_suppressRevealWhenFullscreen);
        behavior.Children.Add(_keepPinnedBelowWindows);
        behavior.Children.Add(Help("以上两项默认开启。其他应用最大化或全屏时，暂停鼠标移到屏幕边缘呼出；固定边栏时允许其他应用遮挡，回到桌面仍可使用。关闭相应开关可恢复边缘呼出或固定置顶。"));
        _hideDelay = Number("移开后多久隐藏（毫秒）", settings.HideDelayMs, 100, 5000, 50);
        _revealDelay = Number("在边缘停留多久展开（毫秒）", settings.RevealDelayMs, 0, 2000, 20);
        behavior.Children.Add(Pair(_hideDelay, _revealDelay));
        _animationStyle = Choice("出现和隐藏方式", new[] { "整体滑入滑出（默认）", "边缘展开收拢", "立即显示或隐藏" }, 0);
        _revealDuration = Number("出现动画时长（毫秒）", settings.RevealDurationMs, 80, 1500, 50);
        _hideDuration = Number("隐藏动画时长（毫秒）", settings.HideDurationMs, 80, 1500, 50);
        behavior.Children.Add(_animationStyle); behavior.Children.Add(Pair(_revealDuration, _hideDuration));
        behavior.Children.Add(Help("默认以 Windows 任务栏的整体滑动为参考，时长为 250 毫秒。数值越大越慢；关闭 Windows 动画效果时会直接显示或隐藏。"));
        _animationStyle.SelectionChanged += (_, _) => UpdateDelayControls();
        _autoHide.Toggled += (_, _) =>
        {
            UpdateDelayControls();
            if (_loadingOptions || _closed) return;
            _draft.SetAutoHide(_autoHide.IsOn);
            QueuePreview();
        };
        UpdateDelayControls();

        _buildingPage = "图标与布局";
        var layout = AddSection("图标与文字", "选择经典图标、横向图片，或只显示名称的纯文本。");
        _showNames = Toggle("显示应用名称", settings.ShowShortcutNames);
        _showNames.OnContent = "显示"; _showNames.OffContent = "隐藏";
        _displayMode = Choice("快捷方式样式", new[] { "经典图标", "横向图片", "纯文本" }, settings.ShortcutDisplayMode switch { "Wide" => 1, "Text" => 2, _ => 0 });
        _width = Number("边栏宽度（逻辑像素）", settings.DockWidth, 72, 320, 8);
        _iconSize = Number("图标大小（逻辑像素）", settings.IconSize, 16, 128, 4);
        _iconSpacing = Number("图标间距（逻辑像素）", settings.IconSpacing, 0, 64, 2);
        _normalColumns = Number("常规显示列数", settings.NormalColumns, 1, 6, 1);
        _footerOffset = Number("底部按钮上下偏移（逻辑像素）", settings.FooterOffset, -12, 600, 4);
        layout.Children.Add(Help("顶部现在显示当前列表名称。点击边栏顶部按钮可新建、重命名、删除和切换列表；鼠标放在边栏上时，按 Q / E 切换上一 / 下一列表。"));
        layout.Children.Add(_showNames); layout.Children.Add(_displayMode); layout.Children.Add(_width);
        layout.Children.Add(Pair(_iconSize, _iconSpacing)); layout.Children.Add(_normalColumns);
        layout.Children.Add(Help("纯文本始终显示名称，不加载图标；图标大小和名称开关仅用于前两种样式，切回后保留原设置。"));
        layout.Children.Add(Help("默认为 40 像素图标、8 像素间距和 1 列。增大图标或列数时，边栏会按需要加宽；横向图片会随图标大小按比例调整高度。"));
        layout.Children.Add(_footerOffset);
        layout.Children.Add(Help("0 保持当前按钮位置；正数上移，负数下移。屏幕高度不足时会自动限制位置。"));
        _loopScrolling = Toggle("循环滚动快捷方式", settings.LoopScrolling);
        layout.Children.Add(_loopScrolling);
        layout.Children.Add(Help("长列表首尾连续衔接，短列表轻微回弹。鼠标靠近边栏中部内侧时，会出现临时展开全部图标的按钮。"));
        layout.Children.Add(Help("横向图片会以全宽矩形显示封面，建议边栏宽度为 180–320。右键快捷方式可以设置专属图标、图片和裁剪方式。"));

        _buildingPage = "背景与外观";
        var appearance = AddSection("边缘与背景", "为当前设置范围选择主题和背景。");
        _frameStyle = Choice("边栏风格", new[] { "贴边直角（默认）", "直角悬浮", "圆角悬浮" }, settings.DockFrameStyle switch { "FloatingSquare" => 1, "Floating" => 2, _ => 0 });
        appearance.Children.Add(_frameStyle);
        appearance.Children.Add(Help("悬浮风格可分别调整上、下边缘距屏幕边缘的距离；默认各为 8 逻辑像素。圆角悬浮的四角半径为 16 逻辑像素。"));
        _floatingTopInset = Number("悬浮上边缘距屏幕距离（逻辑像素）", settings.FloatingTopInset, 0, 200, 4);
        _floatingBottomInset = Number("悬浮下边缘距屏幕距离（逻辑像素）", settings.FloatingBottomInset, 0, 200, 4);
        appearance.Children.Add(Pair(_floatingTopInset, _floatingBottomInset));
        _frameStyle.SelectionChanged += (_, _) => UpdateFrameControls();
        _theme = Choice("边栏主题", new[] { "跟随系统", "浅色", "深色" }, CurrentDraft.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 });
        _backgroundStyle = Choice("背景材质", new[] { "纯色背景", "透明背景", "亚克力毛玻璃", "图片背景" }, settings.BackgroundStyle switch { "Transparent" => 1, "Acrylic" => 2, "Image" => 3, _ => 0 });
        appearance.Children.Add(Pair(_theme, _backgroundStyle)); appearance.Children.Add(_materialHelp);
        _backgroundOpacity = OpacityControl(_opacityOptions, "背景不透明度", settings.BackgroundOpacity);
        _opacityOptions.Children.Add(Help("数值越小，背景越透明；文字和快捷方式仍保持清晰。"));
        appearance.Children.Add(_opacityOptions);
        _backgroundImagePath = new TextBox { Header = "背景图片", Text = settings.BackgroundImagePath ?? "", PlaceholderText = "选择图片，或粘贴本地图片的完整路径", HorizontalAlignment = HorizontalAlignment.Stretch };
        _pickImage = new Button { Content = "选择图片…" };
        _pickImage.Click += async (_, _) => await PickImageAsync();
        _imageOptions.Children.Add(_backgroundImagePath); _imageOptions.Children.Add(_pickImage);
        _imageOptions.Children.Add(_imageHint);
        _imageOptions.Children.Add(Help("支持 PNG、JPG、JPEG、BMP、GIF 和 WebP。图片会铺满边栏，超出的部分自动裁剪。"));
        _imageOverlayOpacity = OpacityControl(_imageOptions, "图片遮罩浓度", settings.BackgroundImageOverlayOpacity);
        _imageOptions.Children.Add(Help("增加遮罩可让图片更柔和，文字更容易看清；遮罩颜色跟随浅色或深色主题。"));
        appearance.Children.Add(_imageOptions);
        _backgroundStyle.SelectionChanged += (_, _) => UpdateBackgroundControls();
        UpdateBackgroundControls();

        _buildingPage = "附属功能";
        var taskbar = AddSection("系统任务栏", "选择 Windows 任务栏的背景材质，保留图标和文字的清晰度。");
        _taskbarTransparency = Toggle("任务栏背景透明", settings.TaskbarTransparencyEnabled);
        taskbar.Children.Add(_taskbarTransparency);
        _taskbarMaterial = Choice("系统任务栏材质", new[] { "透明", "亚克力", "毛玻璃" }, settings.TaskbarMaterial switch { "Acrylic" => 1, "Blur" => 2, _ => 0 });
        taskbar.Children.Add(_taskbarMaterial);
        taskbar.Children.Add(_taskbarOpacityLabel);
        _taskbarOpacity = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Value = settings.TaskbarOpacity * 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        taskbar.Children.Add(_taskbarOpacity); taskbar.Children.Add(_taskbarMaterialHelp);
        taskbar.Children.Add(Help("修改自动生效；关闭此功能或退出 EDock 时恢复系统外观。请先退出其他任务栏外观工具。默认关闭。"));
        taskbar.Children.Add(_taskbarStatus);
        _taskbarTransparency.IsEnabled = !App.Current.IsIsolatedProfile;
        UpdateTaskbarControls();
        App.Current.TaskbarStatusChanged += UpdateTaskbarStatus;
        UpdateTaskbarStatus(null, EventArgs.Empty);

        var startMenu = AddSection("开始菜单", "调整 Windows 开始菜单的背景透明度，保留文字和应用图标的清晰度。");
        _startMenuTransparency = Toggle("开始菜单背景透明", settings.StartMenuTransparencyEnabled);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_startMenuTransparency, "StartMenuTransparency");
        startMenu.Children.Add(_startMenuTransparency);
        startMenu.Children.Add(_startMenuOpacityLabel);
        _startMenuOpacity = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1,
            Value = (1 - settings.StartMenuOpacity) * 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_startMenuOpacity, "StartMenuTransparencyAmount");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_startMenuOpacity, "开始菜单背景透明度");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_startMenuStatus, "StartMenuStatus");
        startMenu.Children.Add(_startMenuOpacity);
        startMenu.Children.Add(Help("100% 为完全透明，0% 为不透明。修改自动生效；关闭此功能或退出 EDock 时恢复原背景。默认关闭。"));
        startMenu.Children.Add(_startMenuStatus);
        _startMenuTransparency.IsEnabled = !App.Current.IsIsolatedProfile;
        UpdateStartMenuControls();
        App.Current.StartMenuStatusChanged += UpdateStartMenuStatus;
        UpdateStartMenuStatus(null, EventArgs.Empty);

        var startButton = AddSection("开始按钮", "用自己的图片替换任务栏上的 Windows 开始图标，保留原来的单击与右键操作。");
        _startButtonReplacement = Toggle("替换开始按钮图标", _startButtonEnabled);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_startButtonReplacement, "StartButtonReplacement");
        _startButtonReplacement.IsEnabled = !App.Current.IsIsolatedProfile;
        startButton.Children.Add(_startButtonReplacement);
        string[] imageLabels = { "普通图片", "悬停图片（可选）", "按下图片（可选）", "激活图片（打开开始菜单时，可选）" };
        for (int i = 0; i < imageLabels.Length; i++)
        {
            int slot = i;
            var path = _startButtonImagePaths[i] = new TextBox { Header = imageLabels[i], Text = _startButtonImageTexts[i],
                PlaceholderText = i == 0 ? "选择图片，或粘贴本地图片路径" : "留空时使用普通图片", HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(path, "StartButtonImagePath" + i);
            path.Loaded += (_, _) => ProtectTextMenus(path);
            var pick = _startButtonPickButtons[i] = new Button { Content = "选择图片…", VerticalAlignment = VerticalAlignment.Bottom };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(pick, "StartButtonPickImage" + i);
            pick.Click += async (_, _) => await PickStartButtonImageAsync(slot);
            path.TextChanged += (_, _) => { if (!_loadingOptions && !_closed) _ = RequestStartButtonImageValidation(slot, path.Text); };
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(path); row.Children.Add(pick); Grid.SetColumn(pick, 1);
            startButton.Children.Add(row); startButton.Children.Add(_startButtonImageHints[i]);
            SetStartButtonImageHint(i, "");
        }
        startButton.Children.Add(_startButtonSizeLabel);
        _startButtonSize = new Slider { Minimum = 8, Maximum = 64, StepFrequency = 1, Value = _startButtonSizeValue,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_startButtonSize, "StartButtonIconSize");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_startButtonSize, "开始按钮图标大小");
        startButton.Children.Add(_startButtonSize);
        var restoreStartButton = new Button { Content = "恢复系统图标" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(restoreStartButton, "StartButtonRestore");
        restoreStartButton.Click += (_, _) => { _startButtonReplacement.IsOn = false; StartButtonChanged(); };
        restoreStartButton.IsEnabled = !App.Current.IsIsolatedProfile;
        var resetStartButton = new Button { Content = "恢复本组默认" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(resetStartButton, "StartButtonReset");
        resetStartButton.Click += (_, _) => ResetStartButton();
        resetStartButton.IsEnabled = !App.Current.IsIsolatedProfile;
        var startButtonActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        startButtonActions.Children.Add(restoreStartButton); startButtonActions.Children.Add(resetStartButton);
        startButton.Children.Add(startButtonActions);
        startButton.Children.Add(Help("支持 PNG、JPG 和 GIF，可分别设置普通、悬停、按下和开始菜单打开时的图片；可选图片留空时使用普通图片。图片会保存到 EDock，原文件移动或删除后仍可使用。关闭功能或退出 EDock 会恢复系统图标，图片选择保留。默认关闭。"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_startButtonStatus, "StartButtonStatus");
        startButton.Children.Add(_startButtonStatus);
        UpdateStartButtonControls();
        App.Current.StartButtonStatusChanged += UpdateStartButtonStatus;
        UpdateStartButtonStatus(null, EventArgs.Empty);

        var desktop = AddSection("桌面快捷方式整理", "把桌面图标收进边栏，保持桌面整洁。此选项对左右边栏共同生效。");
        _removeDesktopShortcut = Toggle("桌面图标拖入后移除原快捷方式", settings.RemoveDesktopShortcutAfterImport);
        desktop.Children.Add(_removeDesktopShortcut);
        desktop.Children.Add(Help("仅处理从桌面拖入的 .lnk / .url 快捷方式：先保存快捷方式副本并保存边栏记录，再移除桌面原件。普通文件、文件夹和桌面以外的快捷方式不会移除。默认关闭。"));

        _buildingPage = "快捷模式";
        var quick = AddSection("快捷模式", "鼠标移入边栏可使用快捷键；桌面前台时，固定边栏无需移入鼠标也能操作。呼出动作也可以在下方统一自定义。");
        _quickMode = Toggle("启用快捷模式", settings.QuickModeEnabled);
        _quickModeSide = Choice("快捷模式显示的边栏", new[] { "左侧边栏", "右侧边栏", "左右两侧边栏" }, settings.QuickModeSide switch { "Right" => 1, "Both" => 2, _ => 0 });
        quick.Children.Add(Pair(_quickMode, _quickModeSide));
        quick.Children.Add(_shortcutEditorHost);
        quick.Children.Add(Help("呼出显示并保持所选边栏，再按一次退出。可关闭单项快捷键；关闭后会保留原按键，重新启用即可恢复。录入期间暂停边栏快捷键。"));
        _quickMode.Toggled += (_, _) => UpdateQuickModeControls();
        UpdateQuickModeControls();

        _buildingPage = "设置窗口";
        var windowAppearance = AddSection("窗口背景", "材质与图片覆盖整个窗口，包含标题栏和左侧导航；与边栏背景分开保存。");
        _settingsTheme = Choice("设置窗口主题", new[] { "跟随系统", "浅色", "深色" }, ThemeIndex(_settingsThemeValue));
        _settingsBackground = Choice("设置窗口背景材质", new[] { "纯色", "透明", "亚克力", "背景图片" }, BackgroundIndex(_settingsBackgroundValue));
        windowAppearance.Children.Add(Pair(_settingsTheme, _settingsBackground));
        windowAppearance.Children.Add(Help("窗口主题独立保存，修改边栏主题或切换左右边栏不会改变此处的选择。"));
        _settingsOpacity = OpacityControl(windowAppearance, "设置窗口背景不透明度", _settingsOpacityValue);
        windowAppearance.Children.Add(Help("数值越小背景越透明，文字与按钮保持清晰。亚克力会模糊窗口后方内容；系统关闭透明效果时使用纯色底板。"));
        _settingsImagePath = new TextBox { Header = "设置窗口背景图片", Text = _settingsImageText, PlaceholderText = "选择图片，或粘贴本地图片的完整路径" };
        _settingsPickImage = new Button { Content = "选择设置背景图片…" };
        _settingsImageOptions.Children.Add(_settingsImagePath); _settingsImageOptions.Children.Add(_settingsPickImage);
        _settingsImageOptions.Children.Add(_settingsImageHint);
        _settingsImageOptions.Children.Add(Help("图片居中铺满窗口；请保留原图片，以便下次打开时继续使用。"));
        windowAppearance.Children.Add(_settingsImageOptions);
        _settingsPickImage.Click += async (_, _) => await PickSettingsImageAsync();
        UpdateSettingsBackgroundControls();
        var contentOverlay = AddSection("内容遮罩", "只覆盖设置卡片、导航按钮等内容区域，保留空白处的原始背景。所有背景材质均可使用。");
        _settingsOverlayTheme = Choice("内容遮罩颜色", new[] { "跟随系统", "浅色", "深色" }, ThemeIndex(_settingsOverlayThemeValue));
        contentOverlay.Children.Add(_settingsOverlayTheme);
        _settingsContentOverlay = OpacityControl(contentOverlay, "内容遮罩浓度", _settingsOverlayValue);
        contentOverlay.Children.Add(Help("0% 去掉遮罩，100% 使用完整底色。浓度与窗口背景不透明度分别调整；文字和按钮保持清晰。"));

        _buildingPage = "基本设置";
        var startup = AddSection("启动与使用", "快捷方式和外观会在下次启动时保留。");
        string? startupError = null;
        bool startupEnabled = settings.OpenOnStartup;
        if (!App.Current.IsIsolatedProfile)
        {
            try { startupEnabled = StartupService.IsEnabled(); }
            catch (Exception ex) { startupError = $"暂时无法读取登录启动状态，已显示上次保存的选择。边栏可继续使用：{ex.Message}"; }
        }
        _startup = Toggle("登录 Windows 后自动启动", startupEnabled);
        _startupActual = _startup.IsOn;
        _startup.IsEnabled = !App.Current.IsIsolatedProfile;
        startup.Children.Add(_startup);
        startup.Children.Add(Help("• 拖入文件、文件夹或快捷方式，即可添加到边栏。\n• 点 + 可以选择文件，也可以添加网页链接。\n• 右键快捷方式可修改图片、名称、路径、排序或移到另一侧。\n• 别针按钮只切换所在边栏的自动隐藏，不影响另一侧。"));
        var footer = new StackPanel { Spacing = 4 };
        var folder = new Button { Content = "打开本地文件所在位置", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 8, 8),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7) };
        folder.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex.Message); } };
        var exit = new Button { Content = "退出 EDock", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 8, 8),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7) };
        exit.Click += (_, _) => App.Current.ExitApplication();
        footer.Children.Add(folder); footer.Children.Add(exit);
        _contentControls.Add(folder); _contentControls.Add(exit);
        var versionSurface = new Border { Child = new TextBlock { Text = $"版本 {typeof(App).Assembly.GetName().Version?.ToString(3)}", FontSize = 11, Opacity = .55 },
            Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(6, 6, 0, 0), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
        _textSurfaces.Add(versionSurface); footer.Children.Add(versionSurface);
        Grid.SetRow(footer, 1); sidebar.Children.Add(footer);
        _navigation.SelectionChanged += (_, _) => ShowCategory();
        _navigation.SelectedIndex = 0;
        _settingsBackdrop.AttachTo(_root);
        _settingsBackdrop.ImageStatusChanged += text => { if (!_closed && text is not null) SetSettingsImageHint(text); };
        _settingsBackdrop.SystemAppearanceChanged += ApplySettingsTheme;
        _root.ActualThemeChanged += OnActualThemeChanged;
        Content = _root;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_titleBar);
        }
        ApplySettingsTheme();
        LoadOptions(CurrentDraft);
        BindLiveControls();
        _backgroundImagePath.Loaded += (_, _) => ProtectTextMenus(_backgroundImagePath);
        _settingsImagePath.Loaded += (_, _) => ProtectTextMenus(_settingsImagePath);
        _scope.SelectionChanged += (_, _) => ChangeScope();
        _sideChoice.SelectionChanged += (_, _) => ChangeScope();
        Closed += OnClosed;
        Activated += OnSettingsActivated;
        var handle = _quickModeWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));
        appWindow.Closing += OnAppWindowClosing;
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = Math.Max(1, EDock.Interop.NativeMethods.GetDpiForWindow(handle) / 96d);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min(area.Width, (int)(820 * scale));
            presenter.PreferredMinimumHeight = Math.Min(area.Height, (int)(660 * scale));
        }
        int width = Math.Min(area.Width, (int)(1040 * scale));
        int height = Math.Min(area.Height, (int)(860 * scale));
        appWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (startupError is not null) ShowError(startupError);
        else if (!string.IsNullOrEmpty(App.Current.LastError)) ShowError(App.Current.LastError);
    }

    private void OnPreviewTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => PreviewPendingChanges();

    private void OnPersistTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => PersistPendingChanges();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (!_closed) ApplyBackgroundColor();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        EndShortcutEdit();
        _closed = true; _previewTimer.Stop(); _persistTimer.Stop();
        _previewTimer.Tick -= OnPreviewTimerTick;
        _persistTimer.Tick -= OnPersistTimerTick;
        _appWindow.Closing -= OnAppWindowClosing;
        Closed -= OnClosed;
        Activated -= OnSettingsActivated;
        _root.ActualThemeChanged -= OnActualThemeChanged;
        foreach (var input in _menuInputs) input.ContextMenuOpening -= OnTextContextMenuOpening;
        foreach (var flyout in _guardedFlyouts)
        {
            flyout.Opened -= OnGuardedFlyoutOpened;
            flyout.Closed -= OnGuardedFlyoutClosed;
        }
        foreach (var popup in _guardedPopups)
        {
            popup.Opened -= OnGuardedPopupOpened;
            popup.Closed -= OnGuardedPopupClosed;
        }
        _menuInputs.Clear(); _guardedFlyouts.Clear(); _openFlyouts.Clear();
        _guardedPopups.Clear(); _openPopups.Clear(); _openChoices.Clear();
        App.Current.TaskbarStatusChanged -= UpdateTaskbarStatus;
        App.Current.StartMenuStatusChanged -= UpdateStartMenuStatus;
        App.Current.StartButtonStatusChanged -= UpdateStartButtonStatus;
        foreach (var cancellation in _startButtonImageCancellation) { cancellation?.Cancel(); cancellation?.Dispose(); }
        foreach (var cancellation in _imageCancellation) { cancellation?.Cancel(); cancellation?.Dispose(); }
        _settingsImageCancellation?.Cancel(); _settingsImageCancellation?.Dispose();
        _settingsBackdrop.Dispose();
    }

    private void OnSettingsActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) EndShortcutEdit();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closed || _closeApproved) return;
        args.Cancel = true;
        if (_closePending) return;
        _closePending = true;
        try
        {
            // Let this native Closing event return before closing again.
            await Task.Yield();
            if (await FlushPendingChangesAsync() && !_closed)
            {
                // WinUI can retain native title-bar composition resources when
                // destroying a window with an extended title bar. Reset it while
                // the window is still alive, after all edits have been saved.
                if (ExtendsContentIntoTitleBar)
                {
                    ExtendsContentIntoTitleBar = false;
                    _appWindow.TitleBar.ResetToDefault();
                }
                _closeApproved = true;
                Close();
            }
        }
        catch (Exception ex) { if (!_closed) ShowError(ex.Message); }
        finally { _closePending = false; }
    }

    private StackPanel AddSection(string title, string description)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Help(description));
        var card = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = panel };
        _sectionCards.Add(card);
        _pages[_buildingPage].Children.Add(card);
        return panel;
    }

    private static Grid Pair(FrameworkElement first, FrameworkElement second)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(first); grid.Children.Add(second); Grid.SetColumn(second, 1);
        return grid;
    }

    private static void BuildStatusCard(Border card, ToggleSwitch toggle, string label)
    {
        card.CornerRadius = new CornerRadius(7);
        card.Padding = new Thickness(10, 2, 10, 2);
        card.BorderThickness = new Thickness(1);
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        toggle.Header = null; toggle.OnContent = ""; toggle.OffContent = ""; toggle.MinWidth = 40;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(toggle, 1); content.Children.Add(toggle); card.Child = content;
    }

    private void ShowCategory()
    {
        EndShortcutEdit();
        if (_navigation.SelectedItem is not ListViewItem { Tag: string name }) return;
        if (name == "快捷模式" && _shortcutEditorHost.Children.Count == 0)
            AddShortcutEditor(_shortcutEditorHost);
        // Only one page belongs to the visual tree. Other pages retain their
        // draft values and scroll position without loading invisible templates.
        var scroll = _pageScrolls[name];
        if (!ReferenceEquals(scroll.Parent, _pageHost))
        {
            _pageHost.Children.Clear();
            _pageHost.Children.Add(scroll);
        }
        int index = _navigation.SelectedIndex;
        for (int i = 0; i < _navigationSurfaces.Count; i++)
            _navigationSurfaces[i].BorderBrush = new SolidColorBrush(i == index
                ? ColorHelper.FromArgb(95, 92, 135, 207) : Colors.Transparent);
    }

    private void UpdateScopeStatus()
    {
        var mask = ContentOverlay();
        void Update(Border card, TextBlock detail, DockSide side, bool enabled)
        {
            bool editing = !_draft.Separate || _draft.Side == side;
            detail.Text = !enabled ? "已关闭 · 可继续调整设置" : editing ? (_draft.Separate ? "当前编辑对象" : "使用统一设置") : "使用独立设置";
            ToolTipService.SetToolTip(card, detail.Text);
            card.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb((byte)(editing ? 75 : 24), 92, 135, 207));
            card.Background = new SolidColorBrush(mask.Tint);
            card.RequestedTheme = mask.Theme;
        }
        Update(_leftStatus, _leftScopeStatus, DockSide.Left, _left.IsOn);
        Update(_rightStatus, _rightScopeStatus, DockSide.Right, _right.IsOn);
    }

    private DockOptions CurrentDraft => _draft.Current;

    public void SelectSide(DockSide side)
    {
        if (_closed) return;
        PreviewPendingChanges();
        _draft.SelectSide(side);
        _loadingOptions = true;
        try { _sideChoice.SelectedIndex = side == DockSide.Right ? 1 : 0; }
        finally { _loadingOptions = false; }
        LoadOptions(CurrentDraft);
    }

    private void ChangeScope()
    {
        if (_loadingOptions || _closed) return;
        bool separate = _scope.SelectedIndex == 1;
        bool modeChanged = separate != _draft.Separate;
        bool createIndependent = separate && !_draft.HasIndependent;
        _draft.SetSeparate(separate);
        _draft.SelectSide(_sideChoice.SelectedIndex == 1 ? DockSide.Right : DockSide.Left);
        if (createIndependent)
        {
            for (int i = 1; i < 3; i++)
            {
                _imageText[i] = _imageText[0]; _imageChoices[i] = _imageChoices[0];
                _imageHints[i] = _imageHints[0];
                if (_imageChoices[i] == 3) RequestImageValidation(i, _imageText[i]);
            }
        }
        _sideChoice.Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
        LoadOptions(CurrentDraft);
        if (modeChanged) QueuePreview();
    }

    private void CaptureChoiceOptions()
    {
        var options = CurrentDraft;
        options.SuppressRevealWhenFullscreen = _suppressRevealWhenFullscreen.IsOn;
        options.KeepPinnedBelowWindows = _keepPinnedBelowWindows.IsOn;
        options.AnimationStyle = _animationStyle.SelectedIndex switch { 1 => "Reveal", 2 => "None", _ => "Slide" };
        options.LoopScrolling = _loopScrolling.IsOn;
        options.ShowShortcutNames = _showNames.IsOn;
        options.ShortcutDisplayMode = _displayMode.SelectedIndex switch { 1 => "Wide", 2 => "Text", _ => "Icon" };
        options.DockFrameStyle = _frameStyle.SelectedIndex switch { 1 => "FloatingSquare", 2 => "Floating", _ => "Edge" };
        options.BackgroundOpacity = Math.Clamp(_backgroundOpacity.Value / 100, 0, 1);
        options.BackgroundImageOverlayOpacity = Math.Clamp(_imageOverlayOpacity.Value / 100, 0, 1);
        options.Theme = _theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" };
    }

    private void LoadOptions(DockOptions options)
    {
        _loadingOptions = true;
        try
        {
            _autoHide.IsOn = _draft.DisplayAutoHide(App.Current.Settings); _width.Value = options.DockWidth;
            _suppressRevealWhenFullscreen.IsOn = options.SuppressRevealWhenFullscreen;
            _keepPinnedBelowWindows.IsOn = options.KeepPinnedBelowWindows;
            _iconSize.Value = options.IconSize; _iconSpacing.Value = options.IconSpacing;
            _normalColumns.Value = options.NormalColumns; _footerOffset.Value = options.FooterOffset;
            _floatingTopInset.Value = options.FloatingTopInset; _floatingBottomInset.Value = options.FloatingBottomInset;
            _hideDelay.Value = options.HideDelayMs; _revealDelay.Value = options.RevealDelayMs;
            _revealDuration.Value = options.RevealDurationMs; _hideDuration.Value = options.HideDurationMs;
            _animationStyle.SelectedIndex = options.AnimationStyle switch { "Reveal" => 1, "None" => 2, _ => 0 };
            _loopScrolling.IsOn = options.LoopScrolling;
            _showNames.IsOn = options.ShowShortcutNames;
            _displayMode.SelectedIndex = options.ShortcutDisplayMode switch { "Wide" => 1, "Text" => 2, _ => 0 };
            _frameStyle.SelectedIndex = options.DockFrameStyle switch { "FloatingSquare" => 1, "Floating" => 2, _ => 0 };
            _theme.SelectedIndex = options.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            _backgroundStyle.SelectedIndex = _imageChoices[_draft.Scope];
            _backgroundImagePath.Text = _imageText[_draft.Scope];
            _imageHint.Text = _imageHints[_draft.Scope];
            _imageHint.Visibility = string.IsNullOrEmpty(_imageHint.Text) ? Visibility.Collapsed : Visibility.Visible;
            _backgroundOpacity.Value = options.BackgroundOpacity * 100;
            _imageOverlayOpacity.Value = options.BackgroundImageOverlayOpacity * 100;
            foreach (var pair in _numbers)
            {
                string text = pair.Value.Read().ToString(CultureInfo.CurrentCulture);
                pair.Key.Text = text;
                if (pair.Value.Input is not null) pair.Value.Input.Text = text;
            }
            UpdateDelayControls(); UpdateBackgroundControls(); UpdateFrameControls(); UpdateShortcutControls();
            RefreshShortcutRows();
            UpdateScopeStatus();
        }
        finally { _loadingOptions = false; }
    }

    private static TextBlock Help(string text) => new() { Text = text, FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
    private static ToggleSwitch Toggle(string title, bool value)
    {
        var toggle = new ToggleSwitch { Header = title, IsOn = value, OnContent = "开启", OffContent = "关闭" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, title);
        return toggle;
    }
    private static NumberBox Number(string title, double value, double min, double max, double step) => new() { Header = title, Value = value, Minimum = min, Maximum = max, SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch };
    private ComboBox Choice(string title, string[] items, int selected)
    {
        var choice = new ComboBox { Header = title, ItemsSource = items, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        choice.DropDownOpened += (_, _) => { _openChoices.Add(choice); App.Current.ExitQuickMode(); };
        choice.DropDownClosed += (_, _) => _openChoices.Remove(choice);
        return choice;
    }

    private void ProtectTextMenus(TextBox input)
    {
        if (_closed) return;
        if (_menuInputs.Add(input)) input.ContextMenuOpening += OnTextContextMenuOpening;
        ProtectFlyout(input.ContextFlyout);
        ProtectFlyout(input.SelectionFlyout);
    }

    private void OnTextContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        App.Current.ExitQuickMode();
        if (sender is TextBox input) ProtectTextMenus(input);
    }

    private void ProtectFlyout(FlyoutBase? flyout)
    {
        if (flyout is null || !_guardedFlyouts.Add(flyout)) return;
        flyout.Opened += OnGuardedFlyoutOpened;
        flyout.Closed += OnGuardedFlyoutClosed;
        if (flyout.IsOpen) _openFlyouts.Add(flyout);
    }

    private void OnGuardedFlyoutOpened(object? sender, object args)
    {
        if (sender is FlyoutBase flyout) _openFlyouts.Add(flyout);
        App.Current.ExitQuickMode();
    }

    private void OnGuardedFlyoutClosed(object? sender, object args)
    {
        if (sender is FlyoutBase flyout) _openFlyouts.Remove(flyout);
    }

    private void OnGuardedPopupOpened(object? sender, object args)
    {
        if (sender is Popup popup) _openPopups.Add(popup);
        App.Current.ExitQuickMode();
    }

    private void OnGuardedPopupClosed(object? sender, object args)
    {
        if (sender is Popup popup) _openPopups.Remove(popup);
    }

    private void ProtectNumberPopup(DependencyObject parent)
    {
        // NumberBox's compact spin controls live in a Popup, not a Flyout.
        // Inspect its template once when connecting the input, never on a timer.
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Popup popup && popup.Name == "UpDownPopup" && _guardedPopups.Add(popup))
            {
                popup.Opened += OnGuardedPopupOpened;
                popup.Closed += OnGuardedPopupClosed;
                if (popup.IsOpen) _openPopups.Add(popup);
            }
            ProtectNumberPopup(child);
        }
    }

    private static Slider OpacityControl(StackPanel panel, string title, double value)
    {
        var label = new TextBlock();
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Value = Math.Clamp(double.IsFinite(value) ? value : .82, 0, 1) * 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        void UpdateLabel() => label.Text = $"{title}：{slider.Value:0}%";
        slider.ValueChanged += (_, _) => UpdateLabel();
        UpdateLabel();
        panel.Children.Add(label); panel.Children.Add(slider);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, title);
        return slider;
    }

    private void UpdateDelayControls()
    {
        _hideDelay.IsEnabled = _autoHide.IsOn;
        _revealDelay.IsEnabled = _autoHide.IsOn;
        _revealDuration.IsEnabled = _autoHide.IsOn && _animationStyle.SelectedIndex != 2;
        _hideDuration.IsEnabled = _autoHide.IsOn && _animationStyle.SelectedIndex != 2;
    }

    private void UpdateBackgroundControls()
    {
        _opacityOptions.Visibility = _backgroundStyle.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        _imageOptions.Visibility = _backgroundStyle.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        _materialHelp.Text = _backgroundStyle.SelectedIndex switch
        {
            1 => "让桌面透过边栏显示，可用下方滑块调整透明程度。",
            2 => "模糊边栏后方的内容，形成磨砂玻璃效果。系统关闭透明效果或不支持该材质时，会自动使用柔和底色。",
            3 => "使用你选择的本地图片作为背景，并用遮罩提高文字可读性。请保留图片文件，以便下次启动时继续使用。",
            _ => "使用与主题一致的纯色底板，清晰、简洁。"
        };
    }

    private void UpdateFrameControls()
    {
        bool floating = _frameStyle.SelectedIndex is 1 or 2;
        _floatingTopInset.IsEnabled = floating;
        _floatingBottomInset.IsEnabled = floating;
    }

    private static int ThemeIndex(string theme) => theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
    private static string ThemeValue(int selected) => selected switch { 1 => "Light", 2 => "Dark", _ => "Default" };
    private void ApplySettingsTheme()
    {
        if (_closed) return;
        bool dark = _settingsThemeValue == "Dark" || _settingsThemeValue == "Default" && _settingsBackdrop.SystemUsesDarkTheme;
        _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ApplyBackgroundColor();
    }

    private (Windows.UI.Color Tint, ElementTheme Theme) ContentOverlay()
    {
        bool dark = _root.RequestedTheme == ElementTheme.Dark;
        bool maskDark = _settingsOverlayThemeValue == "Dark" || _settingsOverlayThemeValue == "Default" && _settingsBackdrop.SystemUsesDarkTheme;
        double opacity = Math.Clamp(double.IsFinite(_settingsOverlayValue) ? _settingsOverlayValue : .35, 0, 1);
        // Strong masks supply their own readable control palette. A zero or
        // low-opacity mask keeps the selected window theme, independent of the dock.
        bool highContrast = _settingsBackdrop.HighContrast;
        var theme = (!highContrast && opacity >= .5 ? maskDark : dark) ? ElementTheme.Dark : ElementTheme.Light;
        byte alpha = highContrast ? (byte)0 : (byte)Math.Round(opacity * 255);
        return (maskDark ? ColorHelper.FromArgb(alpha, 25, 29, 39) : ColorHelper.FromArgb(alpha, 246, 248, 252), theme);
    }
    private void ApplyBackgroundColor()
    {
        bool dark = _root.RequestedTheme == ElementTheme.Dark;
        _root.Background = new SolidColorBrush(Colors.Transparent);
        _settingsBackdrop.Apply(_settingsBackgroundValue, dark, _settingsOpacityValue, _settingsImageValue);
        var mask = ContentOverlay();
        var edge = dark ? ColorHelper.FromArgb(22, 255, 255, 255) : ColorHelper.FromArgb(20, 35, 48, 69);
        var maskBrush = new SolidColorBrush(mask.Tint);
        var edgeBrush = new SolidColorBrush(edge);
        var transparentBrush = new SolidColorBrush(Colors.Transparent);
        foreach (var card in _sectionCards)
        {
            card.Background = maskBrush;
            card.RequestedTheme = mask.Theme;
            card.BorderBrush = edgeBrush;
        }
        foreach (var surface in _textSurfaces)
        {
            surface.Background = maskBrush;
            surface.RequestedTheme = mask.Theme;
        }
        foreach (var control in _contentControls)
        {
            control.Background = maskBrush;
            control.RequestedTheme = mask.Theme;
        }
        foreach (var surface in _backgroundSurfaces)
            surface.Background = transparentBrush;
        foreach (var divider in _shellDividers)
            divider.BorderBrush = edgeBrush;
        UpdateScopeStatus();
    }
    private void UpdateQuickModeControls() => _quickModeSide.IsEnabled = _quickMode.IsOn;

    private void BindLiveControls()
    {
        foreach (var toggle in new[] { _left, _right, _removeDesktopShortcut, _quickMode })
            toggle.Toggled += (_, _) => GlobalChanged();
        _quickModeSide.SelectionChanged += (_, _) => GlobalChanged();
        _taskbarTransparency.Toggled += (_, _) => TaskbarChanged();
        _taskbarOpacity.ValueChanged += (_, _) => TaskbarChanged();
        _taskbarMaterial.SelectionChanged += (_, _) => TaskbarChanged();
        _startMenuTransparency.Toggled += (_, _) => StartMenuChanged();
        _startMenuOpacity.ValueChanged += (_, _) => StartMenuChanged();
        _startButtonReplacement.Toggled += (_, _) => StartButtonChanged();
        _startButtonSize.ValueChanged += (_, _) => StartButtonChanged();
        _settingsBackground.SelectionChanged += (_, _) =>
        {
            UpdateSettingsBackgroundControls();
            if (_loadingOptions || _closed) return;
            if (_settingsBackground.SelectedIndex == 3)
                _ = RequestSettingsImageValidation(_settingsImagePath.Text, immediate: true);
            else
            {
                _settingsBackgroundValue = _settingsBackground.SelectedIndex switch { 1 => "Transparent", 2 => "Acrylic", _ => "Solid" };
                ApplyBackgroundColor(); QueuePreview();
            }
        };
        _settingsOpacity.ValueChanged += (_, _) => SettingsAppearanceChanged();
        _settingsContentOverlay.ValueChanged += (_, _) => SettingsAppearanceChanged();
        _settingsTheme.SelectionChanged += (_, _) => SettingsAppearanceChanged();
        _settingsOverlayTheme.SelectionChanged += (_, _) => SettingsAppearanceChanged();
        _settingsImagePath.TextChanged += (_, _) =>
        {
            if (!_loadingOptions && !_closed) _ = RequestSettingsImageValidation(_settingsImagePath.Text);
        };
        _startup.Toggled += (_, _) => StartupChanged();
        foreach (var toggle in new[] { _showNames, _loopScrolling, _suppressRevealWhenFullscreen, _keepPinnedBelowWindows })
            toggle.Toggled += (_, _) => ChoicesChanged();
        foreach (var choice in new[] { _animationStyle, _displayMode, _theme, _frameStyle })
            choice.SelectionChanged += (_, _) => ChoicesChanged();
        _backgroundOpacity.ValueChanged += (_, _) => ChoicesChanged();
        _imageOverlayOpacity.ValueChanged += (_, _) => ChoicesChanged();
        _backgroundStyle.SelectionChanged += (_, _) =>
        {
            if (_loadingOptions || _closed) return;
            _imageChoices[_draft.Scope] = _backgroundStyle.SelectedIndex;
            if (_backgroundStyle.SelectedIndex == 3) RequestImageValidation(_draft.Scope, _imageText[_draft.Scope], immediate: true);
            else
            {
                CurrentDraft.BackgroundStyle = _backgroundStyle.SelectedIndex switch
                { 1 => "Transparent", 2 => "Acrylic", _ => "Solid" };
                QueuePreview();
            }
        };
        _backgroundImagePath.TextChanged += (_, _) =>
        {
            if (!_loadingOptions && !_closed) RequestImageValidation(_draft.Scope, _backgroundImagePath.Text);
        };
        BindNumber(_width, () => CurrentDraft.DockWidth, v => CurrentDraft.DockWidth = v);
        BindNumber(_iconSize, () => CurrentDraft.IconSize, v => CurrentDraft.IconSize = v);
        BindNumber(_iconSpacing, () => CurrentDraft.IconSpacing, v => CurrentDraft.IconSpacing = v);
        BindNumber(_normalColumns, () => CurrentDraft.NormalColumns,
            v => CurrentDraft.NormalColumns = (int)Math.Round(v, MidpointRounding.AwayFromZero));
        BindNumber(_footerOffset, () => CurrentDraft.FooterOffset, v => CurrentDraft.FooterOffset = v);
        BindNumber(_floatingTopInset, () => CurrentDraft.FloatingTopInset, v => CurrentDraft.FloatingTopInset = v);
        BindNumber(_floatingBottomInset, () => CurrentDraft.FloatingBottomInset, v => CurrentDraft.FloatingBottomInset = v);
        BindNumber(_hideDelay, () => CurrentDraft.HideDelayMs, v => CurrentDraft.HideDelayMs = (int)v);
        BindNumber(_revealDelay, () => CurrentDraft.RevealDelayMs, v => CurrentDraft.RevealDelayMs = (int)v);
        BindNumber(_revealDuration, () => CurrentDraft.RevealDurationMs, v => CurrentDraft.RevealDurationMs = (int)v);
        BindNumber(_hideDuration, () => CurrentDraft.HideDurationMs, v => CurrentDraft.HideDurationMs = (int)v);
    }

    private void ChoicesChanged()
    {
        if (_loadingOptions || _closed) return;
        UpdateShortcutControls();
        CaptureChoiceOptions();
        QueuePreview();
    }

    private void UpdateShortcutControls()
    {
        bool textOnly = _displayMode.SelectedIndex == 2;
        _showNames.IsEnabled = !textOnly;
        _iconSize.IsEnabled = !textOnly;
    }

    private void GlobalChanged()
    {
        if (_loadingOptions || _closed) return;
        string side = _quickModeSide.SelectedIndex switch { 1 => "Right", 2 => "Both", _ => "Left" };
        _leftEnabled = _left.IsOn; _rightEnabled = _right.IsOn;
        _removeDesktop = _removeDesktopShortcut.IsOn; _quickEnabled = _quickMode.IsOn; _quickSide = side;
        UpdateScopeStatus();
        QueuePreview();
    }

    private void AddShortcutEditor(StackPanel parent)
    {
        var section = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "快捷键自定义", FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var reset = new Button { Content = "恢复默认按键", FontSize = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reset, "ShortcutResetAll");
        reset.Click += (_, _) => ResetShortcutBindings();
        Grid.SetColumn(reset, 1); header.Children.Add(reset); section.Children.Add(header);
        section.Children.Add(Help("点击按键开始录入 → 按下新按键 → Enter 保存 / Esc 取消。也可点击保存、取消；保存后需再次点击才能修改。"));
        foreach (var action in ShortcutBindingCatalog.Actions)
        {
            string label = ShortcutBindingCatalog.Label(action);
            var container = new StackPanel { Spacing = 6 };
            var row = new Grid { ColumnSpacing = 12, MinHeight = 38 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var key = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 34,
                FontFamily = new FontFamily("Segoe UI"), CornerRadius = new CornerRadius(6) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(key, "修改" + label + "快捷键");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(key, "ShortcutKey_" + action);
            ToolTipService.SetToolTip(key, "点击录入新的按键");
            key.Click += (_, _) => BeginShortcutEdit(action);
            Grid.SetColumn(key, 1); row.Children.Add(key);
            var enabled = Toggle("启用" + label + "快捷键", true);
            enabled.Header = null; enabled.OnContent = ""; enabled.OffContent = "";
            enabled.MinWidth = 40; enabled.VerticalAlignment = VerticalAlignment.Center;
            enabled.Toggled += (_, _) => SetShortcutEnabled(action, enabled.IsOn);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(enabled, "ShortcutEnabled_" + action);
            Grid.SetColumn(enabled, 2); row.Children.Add(enabled); container.Children.Add(row);

            section.Children.Add(container);
            _shortcutRows[action] = new ShortcutRow(key, enabled, container);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_shortcutConflictHint, "ShortcutStatus");
        section.Children.Add(_shortcutConflictHint); parent.Children.Add(section);
        RefreshShortcutRows();
    }

    private ShortcutEditControls CreateShortcutEditControls(DockShortcutAction action, ShortcutRow row)
    {
        // Recording controls are needed for one action at a time, not for every
        // shortcut whenever settings opens. Release them again after recording.
        var content = new StackPanel { Spacing = 8 };
        var candidate = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(candidate, "ShortcutCandidate_" + action);
        var hint = Help("");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(hint, "ShortcutHint_" + action);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = "保存", IsEnabled = false };
        var cancel = new Button { Content = "取消" };
        var restore = new Button { Content = "使用默认" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(save, "ShortcutSave_" + action);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(cancel, "ShortcutCancel_" + action);
        save.Click += (_, _) => SaveShortcutEdit();
        cancel.Click += (_, _) => EndShortcutEdit();
        restore.Click += (_, _) => SetShortcutCandidate(ShortcutBindingCatalog.Default(action));
        actions.Children.Add(save); actions.Children.Add(cancel); actions.Children.Add(restore);
        content.Children.Add(candidate); content.Children.Add(hint); content.Children.Add(actions);
        var panel = new Border { Child = content, Padding = new Thickness(14),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(150, 75, 140, 230)) };
        var edit = new ShortcutEditControls(panel, candidate, hint, save);
        row.Edit = edit;
        row.Container.Children.Add(panel);
        return edit;
    }

    private void RefreshShortcutRows()
    {
        _refreshingShortcuts = true;
        try
        {
            foreach (var (action, row) in _shortcutRows)
            {
                var binding = _draft.ShortcutBindings[action];
                row.Key.Content = ShortcutBindingCatalog.DisplayKey(binding);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(row.Key, ShortcutBindingCatalog.DisplayKey(binding));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Key,
                    ShortcutBindingCatalog.Label(action) + "快捷键：" + ShortcutBindingCatalog.DisplayKey(binding));
                row.Key.Opacity = binding.Enabled ? 1 : .5;
                row.Enabled.IsOn = binding.Enabled;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(row.Key,
                    binding.Enabled ? "已启用，点击修改" : "已关闭，保留原按键；点击修改后启用");
            }
        }
        finally { _refreshingShortcuts = false; }
        var conflicts = ShortcutBindingCatalog.FindConflicts(_draft.ShortcutBindings);
        _shortcutConflictHint.Text = conflicts.Count == 0 ? "修改保存后立即生效。" : "已有按键冲突：" + string.Join("；", conflicts);
    }

    private void BeginShortcutEdit(DockShortcutAction action)
    {
        if (_closed || _editingShortcut == action) return;
        EndShortcutEdit();
        _editingShortcut = action;
        App.Current.ExitQuickMode();
        var row = CreateShortcutEditControls(action, _shortcutRows[action]);
        row.Candidate.Text = "请按下快捷键…";
        row.Hint.Text = "当前：" + ShortcutBindingCatalog.DisplayKey(_draft.ShortcutBindings[action]) + "。Enter 保存 · Esc 取消";
        row.Save.IsEnabled = false;
        row.Panel.StartBringIntoView();
        var recorder = new ShortcutKeyRecorder(DispatcherQueue, () => _quickModeWindowHandle,
            binding => { if (_editingShortcut == action) SetShortcutCandidate(binding); },
            save => { if (_editingShortcut == action) { if (save) SaveShortcutEdit(); else EndShortcutEdit(); } });
        _shortcutRecorder = recorder;
        if (!recorder.Start())
        {
            string error = recorder.LastError ?? "无法开始按键录入，请重试。";
            EndShortcutEdit(); ShowError(error);
        }
    }

    private void SetShortcutCandidate(ShortcutBinding binding)
    {
        if (_editingShortcut is not { } action) return;
        _shortcutCandidate = ShortcutBindingCatalog.NormalizeBinding(binding);
        _shortcutCandidate.Enabled = true;
        var row = _shortcutRows[action].Edit!;
        row.Candidate.Text = ShortcutBindingCatalog.DisplayKey(_shortcutCandidate);
        string? conflict = ShortcutConflict(action, _shortcutCandidate);
        row.Hint.Text = conflict ?? "Enter 保存 · Esc 取消。保存后立即生效。";
        row.Save.IsEnabled = conflict is null;
    }

    private string? ShortcutConflict(DockShortcutAction action, ShortcutBinding candidate)
    {
        if (ShortcutBindingCatalog.VirtualKeyCode(candidate.Key) == 0) return "此按键暂不支持，请换一个按键。";
        if (candidate.Key is "Enter" or "Escape") return "Enter 和 Esc 用于保存、取消，请选择其他按键。";
        if (!candidate.Enabled) return null;
        var conflicts = _draft.ShortcutBindings.Where(pair => pair.Key != action && pair.Value.Enabled
            && ShortcutBindingCatalog.DisplayKey(pair.Value) == ShortcutBindingCatalog.DisplayKey(candidate))
            .Select(pair => ShortcutBindingCatalog.Label(pair.Key)).ToArray();
        return conflicts.Length == 0 ? null : "与「" + string.Join("、", conflicts) + "」冲突，请换一个按键或先关闭对应快捷键。";
    }

    private void SaveShortcutEdit()
    {
        if (_editingShortcut is not { } action || _shortcutCandidate is null) return;
        string? conflict = ShortcutConflict(action, _shortcutCandidate);
        if (conflict is not null) { _shortcutRows[action].Edit!.Hint.Text = conflict; return; }
        var candidate = _shortcutCandidate.Copy();
        EndShortcutEdit();
        ApplyShortcutBinding(action, candidate);
    }

    private void EndShortcutEdit()
    {
        var action = _editingShortcut;
        _editingShortcut = null; _shortcutCandidate = null;
        _shortcutRecorder?.Dispose(); _shortcutRecorder = null;
        if (action is { } edited)
        {
            var row = _shortcutRows[edited];
            if (row.Edit is { } edit) row.Container.Children.Remove(edit.Panel);
            row.Edit = null;
            // The recording button must not stay armed for another Enter press.
            _navigation.Focus(FocusState.Programmatic);
        }
    }

    private void SetShortcutEnabled(DockShortcutAction action, bool enabled)
    {
        if (_loadingOptions || _refreshingShortcuts || _closed) return;
        EndShortcutEdit();
        var binding = _draft.ShortcutBindings[action].Copy(); binding.Enabled = enabled;
        string? conflict = ShortcutConflict(action, binding);
        if (enabled && conflict is not null)
        {
            RefreshShortcutRows(); _shortcutConflictHint.Text = conflict; return;
        }
        ApplyShortcutBinding(action, binding);
    }

    private void ApplyShortcutBinding(DockShortcutAction action, ShortcutBinding binding)
    {
        var previous = _draft.ShortcutBindings[action].Copy();
        _draft.SetShortcutBinding(action, binding);
        QueuePreview();
        if (!PreviewPendingChanges()) _draft.SetShortcutBinding(action, previous);
        RefreshShortcutRows();
    }

    private void ResetShortcutBindings()
    {
        EndShortcutEdit();
        var previous = _draft.ShortcutBindings.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        foreach (var action in ShortcutBindingCatalog.Actions) _draft.SetShortcutBinding(action, ShortcutBindingCatalog.Default(action));
        QueuePreview();
        if (!PreviewPendingChanges()) foreach (var pair in previous) _draft.SetShortcutBinding(pair.Key, pair.Value);
        RefreshShortcutRows();
    }

    private void TaskbarChanged()
    {
        if (_loadingOptions || _closed) return;
        _taskbarEnabled = _taskbarTransparency.IsOn;
        _taskbarOpacityValue = Math.Clamp(_taskbarOpacity.Value / 100, 0, 1);
        _taskbarMaterialValue = _taskbarMaterial.SelectedIndex switch { 1 => "Acrylic", 2 => "Blur", _ => "Transparent" };
        UpdateTaskbarControls();
        QueuePreview();
    }

    private void UpdateTaskbarControls()
    {
        bool enabled = _taskbarTransparency.IsOn && !App.Current.IsIsolatedProfile;
        _taskbarOpacity.IsEnabled = enabled; _taskbarMaterial.IsEnabled = enabled;
        bool material = _taskbarMaterial.SelectedIndex is 1 or 2;
        string label = material ? "材质浓度" : "任务栏背景不透明度";
        _taskbarOpacityLabel.Text = $"{label}：{_taskbarOpacity.Value:0}%";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_taskbarOpacity, label);
        _taskbarMaterialHelp.Text = material
            ? "低浓度保留更多背景细节，高浓度增加主题底色；0% 仍保留模糊效果。亚克力更柔和，毛玻璃更清透。"
            : "0% 为完全透明，100% 保留完整背景。";
    }

    private void UpdateSettingsBackgroundControls() => _settingsImageOptions.Visibility =
        _settingsBackground.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

    private void SettingsAppearanceChanged()
    {
        if (_loadingOptions || _closed) return;
        _settingsOpacityValue = Math.Clamp(_settingsOpacity.Value / 100, 0, 1);
        _settingsOverlayValue = Math.Clamp(_settingsContentOverlay.Value / 100, 0, 1);
        _settingsThemeValue = ThemeValue(_settingsTheme.SelectedIndex);
        _settingsOverlayThemeValue = ThemeValue(_settingsOverlayTheme.SelectedIndex);
        ApplySettingsTheme(); QueuePreview();
    }

    private void UpdateTaskbarStatus(object? sender, EventArgs args)
    {
        _taskbarStatus.Text = App.Current.IsIsolatedProfile ? "测试实例不会修改系统任务栏。" : App.Current.TaskbarError ?? "";
        _taskbarStatus.Visibility = string.IsNullOrEmpty(_taskbarStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartMenuChanged()
    {
        if (_loadingOptions || _closed) return;
        _startMenuEnabled = _startMenuTransparency.IsOn;
        _startMenuOpacityValue = Math.Clamp(1 - _startMenuOpacity.Value / 100, 0, 1);
        UpdateStartMenuControls();
        QueuePreview();
    }

    private void UpdateStartMenuControls()
    {
        _startMenuOpacity.IsEnabled = _startMenuTransparency.IsOn && !App.Current.IsIsolatedProfile;
        _startMenuOpacityLabel.Text = $"背景透明度：{_startMenuOpacity.Value:0}%";
    }

    private void UpdateStartMenuStatus(object? sender, EventArgs args)
    {
        _startMenuStatus.Text = App.Current.IsIsolatedProfile ? "测试实例不会修改系统开始菜单。" : App.Current.StartMenuStatus;
        _startMenuStatus.Visibility = string.IsNullOrEmpty(_startMenuStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartButtonChanged()
    {
        if (_loadingOptions || _closed) return;
        _startButtonEnabled = _startButtonReplacement.IsOn;
        _startButtonSizeValue = Math.Clamp(_startButtonSize.Value, 8, 64);
        UpdateStartButtonControls();
        QueuePreview();
    }

    private void UpdateStartButtonControls()
    {
        _startButtonSize.IsEnabled = !App.Current.IsIsolatedProfile;
        _startButtonSizeLabel.Text = $"图标大小：{_startButtonSize.Value:0}";
    }

    private void UpdateStartButtonStatus(object? sender, EventArgs args)
    {
        _startButtonStatus.Text = App.Current.IsIsolatedProfile ? "测试实例不会修改系统开始按钮。" : App.Current.StartButtonStatus;
        _startButtonStatus.Visibility = string.IsNullOrEmpty(_startButtonStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetStartButton()
    {
        if (_closed || App.Current.IsIsolatedProfile) return;
        _loadingOptions = true;
        try
        {
            _startButtonEnabled = false; _startButtonSizeValue = 32;
            _startButtonReplacement.IsOn = false; _startButtonSize.Value = 32;
            for (int i = 0; i < _startButtonImageValues.Length; i++)
            {
                _startButtonImageCancellation[i]?.Cancel();
                _startButtonImageVersions[i]++;
                _startButtonImagePaths[i].Text = _startButtonImageTexts[i] = _startButtonImageValues[i] = "";
                SetStartButtonImageHint(i, "");
            }
            UpdateStartButtonControls();
        }
        finally { _loadingOptions = false; }
        QueuePreview();
    }

    private void StartupChanged()
    {
        if (_loadingOptions || _closed) return;
        if (App.Current.IsIsolatedProfile)
        {
            _loadingOptions = true;
            try { _startup.IsOn = _startupActual; }
            finally { _loadingOptions = false; }
            return;
        }
        try
        {
            // Only this explicit user gesture changes Windows' startup registration.
            StartupService.SetEnabled(_startup.IsOn);
            _startupActual = _startupValue = _startup.IsOn;
            QueuePreview();
        }
        catch (Exception ex)
        {
            _loadingOptions = true;
            try { _startup.IsOn = _startupActual; }
            finally { _loadingOptions = false; }
            ShowError($"无法调整登录启动：{ex.Message}");
        }
    }

    private sealed class NumberBinding
    {
        public required Func<double> Read { get; init; }
        public required Action<double> Write { get; init; }
        public TextBox? Input { get; set; }
        public TextChangedEventHandler? Changed { get; set; }
    }

    private void BindNumber(NumberBox box, Func<double> read, Action<double> write)
    {
        var binding = new NumberBinding { Read = read, Write = write };
        _numbers.Add(box, binding);
        void Accept(double value)
        {
            if (_loadingOptions || _closed || !double.IsFinite(value)
                || value < box.Minimum || value > box.Maximum) return;
            double previous = read();
            write(value);
            if (read() != previous) QueuePreview();
        }
        void ConnectInput()
        {
            box.ApplyTemplate();
            var input = FindNumberInput(box);
            if (input == binding.Input || input is null) return;
            if (binding.Input is not null && binding.Changed is not null)
                binding.Input.TextChanged -= binding.Changed;
            binding.Input = input;
            ProtectTextMenus(input);
            ProtectNumberPopup(box);
            binding.Changed = (_, _) =>
            {
                if (SettingsEditorDraft.TryNumber(input.Text, box.Minimum, box.Maximum, out double value)) Accept(value);
            };
            input.TextChanged += binding.Changed;
        }
        box.Loaded += (_, _) => ConnectInput();
        box.ValueChanged += (_, _) => Accept(box.Value);
        box.LosingFocus += (_, _) =>
        {
            if (_loadingOptions || _closed) return;
            // NumberBox commits raw text on LostFocus. Its previous Value may lag
            // behind our live draft, so synchronize before its validation runs.
            _loadingOptions = true;
            try
            {
                box.Value = read();
                box.Text = read().ToString(CultureInfo.CurrentCulture);
                if (binding.Input is not null) binding.Input.Text = box.Text;
            }
            finally { _loadingOptions = false; }
        };
        if (box.IsLoaded) ConnectInput();
    }

    private static TextBox? FindNumberInput(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox input && input.Name == "InputBox") return input;
            if (FindNumberInput(child) is { } found) return found;
        }
        return null;
    }

    private void QueuePreview()
    {
        if (_loadingOptions || _closed) return;
        _previewPending = true;
        // Restarting a long debounce would make a continuously dragged slider lag.
        if (!_previewTimer.IsRunning) _previewTimer.Start();
    }

    private bool PreviewPendingChanges()
    {
        _previewTimer.Stop();
        if (!_previewPending || _closed) return true;
        var current = App.Current.Settings;
        bool globalsMatch = current.LeftEnabled == _leftEnabled && current.RightEnabled == _rightEnabled
            && current.RemoveDesktopShortcutAfterImport == _removeDesktop
            && current.QuickModeEnabled == _quickEnabled && current.QuickModeSide == _quickSide
            && current.OpenOnStartup == _startupValue;
        globalsMatch = globalsMatch && current.TaskbarTransparencyEnabled == _taskbarEnabled
            && current.TaskbarOpacity == _taskbarOpacityValue && current.TaskbarMaterial == _taskbarMaterialValue
            && current.StartMenuTransparencyEnabled == _startMenuEnabled && current.StartMenuOpacity == _startMenuOpacityValue
            && current.StartButtonReplacementEnabled == _startButtonEnabled && current.StartButtonIconSize == _startButtonSizeValue
            && current.StartButtonImagePath == _startButtonImageValues[0] && current.StartButtonHoverImagePath == _startButtonImageValues[1]
            && current.StartButtonPressedImagePath == _startButtonImageValues[2]
            && current.StartButtonActivatedImagePath == _startButtonImageValues[3]
            && current.SettingsBackgroundStyle == _settingsBackgroundValue && current.SettingsBackgroundOpacity == _settingsOpacityValue
            && current.SettingsBackgroundImagePath == _settingsImageValue && current.SettingsBackgroundImageOverlayOpacity == _settingsOverlayValue
            && current.SettingsTheme == _settingsThemeValue && current.SettingsContentOverlayTheme == _settingsOverlayThemeValue;
        if (_draft.Matches(current) && globalsMatch)
        {
            _previewPending = false;
            _draft.AcceptApplied();
            return true;
        }
        if (!App.Current.PreviewSettings(settings =>
        {
            _draft.ApplyTo(settings);
            settings.LeftEnabled = _leftEnabled; settings.RightEnabled = _rightEnabled;
            settings.RemoveDesktopShortcutAfterImport = _removeDesktop;
            settings.QuickModeEnabled = _quickEnabled; settings.QuickModeSide = _quickSide;
            settings.OpenOnStartup = _startupValue;
            settings.TaskbarTransparencyEnabled = _taskbarEnabled;
            settings.TaskbarOpacity = _taskbarOpacityValue;
            settings.TaskbarMaterial = _taskbarMaterialValue;
            settings.StartMenuTransparencyEnabled = _startMenuEnabled;
            settings.StartMenuOpacity = _startMenuOpacityValue;
            settings.StartButtonReplacementEnabled = _startButtonEnabled;
            settings.StartButtonIconSize = _startButtonSizeValue;
            settings.StartButtonImagePath = _startButtonImageValues[0];
            settings.StartButtonHoverImagePath = _startButtonImageValues[1];
            settings.StartButtonPressedImagePath = _startButtonImageValues[2];
            settings.StartButtonActivatedImagePath = _startButtonImageValues[3];
            settings.SettingsBackgroundStyle = _settingsBackgroundValue;
            settings.SettingsBackgroundOpacity = _settingsOpacityValue;
            settings.SettingsBackgroundImagePath = _settingsImageValue;
            settings.SettingsBackgroundImageOverlayOpacity = _settingsOverlayValue;
            settings.SettingsTheme = _settingsThemeValue;
            settings.SettingsContentOverlayTheme = _settingsOverlayThemeValue;
            settings.ShortcutBindings = _draft.ShortcutBindings.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
            settings.QuickModeActivation = _draft.QuickModeActivation.Copy();
        }))
        {
            // A refused system appearance change must not prevent closing settings
            // or saving later dock edits (for example while another tool owns it).
            if (current.TaskbarTransparencyEnabled != _taskbarEnabled || current.TaskbarOpacity != _taskbarOpacityValue || current.TaskbarMaterial != _taskbarMaterialValue)
            {
                _taskbarEnabled = current.TaskbarTransparencyEnabled;
                _taskbarOpacityValue = current.TaskbarOpacity;
                _taskbarMaterialValue = current.TaskbarMaterial;
                _loadingOptions = true;
                try
                {
                    _taskbarTransparency.IsOn = _taskbarEnabled;
                    _taskbarOpacity.Value = _taskbarOpacityValue * 100;
                    _taskbarMaterial.SelectedIndex = _taskbarMaterialValue switch { "Acrylic" => 1, "Blur" => 2, _ => 0 };
                    UpdateTaskbarControls();
                }
                finally { _loadingOptions = false; }
            }
            if (current.StartMenuTransparencyEnabled != _startMenuEnabled || current.StartMenuOpacity != _startMenuOpacityValue)
            {
                _startMenuEnabled = current.StartMenuTransparencyEnabled;
                _startMenuOpacityValue = current.StartMenuOpacity;
                _loadingOptions = true;
                try
                {
                    _startMenuTransparency.IsOn = _startMenuEnabled;
                    _startMenuOpacity.Value = (1 - _startMenuOpacityValue) * 100;
                    UpdateStartMenuControls();
                }
                finally { _loadingOptions = false; }
            }
            if (current.StartButtonReplacementEnabled != _startButtonEnabled || current.StartButtonIconSize != _startButtonSizeValue
                || current.StartButtonImagePath != _startButtonImageValues[0] || current.StartButtonHoverImagePath != _startButtonImageValues[1]
                || current.StartButtonPressedImagePath != _startButtonImageValues[2] || current.StartButtonActivatedImagePath != _startButtonImageValues[3])
            {
                _startButtonEnabled = current.StartButtonReplacementEnabled;
                _startButtonSizeValue = current.StartButtonIconSize;
                _startButtonImageValues[0] = current.StartButtonImagePath;
                _startButtonImageValues[1] = current.StartButtonHoverImagePath;
                _startButtonImageValues[2] = current.StartButtonPressedImagePath;
                _startButtonImageValues[3] = current.StartButtonActivatedImagePath;
                _loadingOptions = true;
                try
                {
                    _startButtonReplacement.IsOn = _startButtonEnabled;
                    _startButtonSize.Value = _startButtonSizeValue;
                    for (int i = 0; i < _startButtonImageValues.Length; i++)
                        _startButtonImagePaths[i].Text = _startButtonImageTexts[i] = _startButtonImageValues[i];
                    UpdateStartButtonControls();
                }
                finally { _loadingOptions = false; }
            }
            ShowError(App.Current.LastError ?? "这次修改暂时无法应用，请调整后重试。");
            return false;
        }
        _previewPending = false; _needsPersist = true; _draft.AcceptApplied();
        _persistTimer.Stop(); _persistTimer.Start();
        return true;
    }

    private bool PersistPendingChanges()
    {
        _persistTimer.Stop();
        if (!PreviewPendingChanges()) return false;
        if (!_needsPersist) return true;
        if (!App.Current.PersistSettingsPreview())
        {
            ShowError(App.Current.LastError ?? "修改已生效，但暂时无法写入本地配置；请稍后重试。");
            return false;
        }
        _needsPersist = false;
        return true;
    }

    public bool FlushPendingChanges()
    {
        if (_closed) return true;
        if (_flushing) return false;
        _flushing = true;
        try { return PersistPendingChanges(); }
        catch (Exception ex) { ShowError(ex.Message); return false; }
        finally { _flushing = false; }
    }

    public Task<bool> FlushPendingChangesAsync()
    {
        if (_closed) return Task.FromResult(true);
        if (_flushTask is { IsCompleted: false }) return _flushTask;
        return _flushTask = FlushPendingImagesAsync();
    }

    private async Task<bool> FlushPendingImagesAsync()
    {
        try
        {
            while (!_closed)
            {
                // TextChanged may still be queued when a close message follows
                // a paste immediately. Capture the control's current text first.
                int scope = _draft.Scope;
                if (_backgroundImagePath.Text != _imageText[scope])
                    _ = RequestImageValidation(scope, _backgroundImagePath.Text, immediate: true);
                var versions = (long[])_imageVersions.Clone();
                if (_settingsImagePath.Text != _settingsImageText)
                    _ = RequestSettingsImageValidation(_settingsImagePath.Text, immediate: true);
                long settingsVersion = _settingsImageVersion;
                for (int i = 0; i < _startButtonImageValues.Length; i++)
                    if (_startButtonImagePaths[i].Text != _startButtonImageTexts[i])
                        _ = RequestStartButtonImageValidation(i, _startButtonImagePaths[i].Text, immediate: true);
                var startButtonVersions = (long[])_startButtonImageVersions.Clone();
                var pending = _imageValidationTasks.Append(_settingsImageTask).Concat(_startButtonImageTasks).Where(task => task is { IsCompleted: false }).Cast<Task>().ToArray();
                if (pending.Length > 0) await Task.WhenAll(pending);
                // Typing or choosing another image while awaiting replaces its task.
                // Only flush once the latest request in every scope has settled.
                if (!versions.SequenceEqual(_imageVersions) || settingsVersion != _settingsImageVersion
                    || !startButtonVersions.SequenceEqual(_startButtonImageVersions)) continue;
                return FlushPendingChanges();
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!_closed) ShowError(ex.Message);
            return false;
        }
    }

    private async Task PickStartButtonImageAsync(int slot)
    {
        _startButtonPickButtons[slot].IsEnabled = false;
        _modalInteractions++;
        App.Current.ExitQuickMode();
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".gif" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || _closed) return;
            _loadingOptions = true;
            try { _startButtonImagePaths[slot].Text = file.Path; }
            finally { _loadingOptions = false; }
            await RequestStartButtonImageValidation(slot, file.Path, immediate: true);
        }
        catch (Exception ex) { if (!_closed) ShowError($"无法选择图片：{ex.Message}"); }
        finally { _modalInteractions--; if (!_closed) _startButtonPickButtons[slot].IsEnabled = true; }
    }

    private Task RequestStartButtonImageValidation(int slot, string text, bool immediate = false)
    {
        _startButtonImageTexts[slot] = text;
        _startButtonImageCancellation[slot]?.Cancel(); _startButtonImageCancellation[slot]?.Dispose();
        var cancellation = _startButtonImageCancellation[slot] = new CancellationTokenSource();
        long version = ++_startButtonImageVersions[slot];
        SetStartButtonImageHint(slot, string.IsNullOrWhiteSpace(text) ? "" : "正在检查并保存图片…");
        return _startButtonImageTasks[slot] = ValidateStartButtonImageAsync(slot, text, version, cancellation.Token, immediate);
    }

    private async Task ValidateStartButtonImageAsync(int slot, string text, long version, CancellationToken token, bool immediate)
    {
        bool Current() => !_closed && !token.IsCancellationRequested && version == _startButtonImageVersions[slot] && text == _startButtonImageTexts[slot];
        try
        {
            if (!immediate) await Task.Delay(250, token);
            if (!Current()) return;
            string path = text.Trim().Trim('"');
            if (path.Length == 0)
            {
                _startButtonImageValues[slot] = "";
                SetStartButtonImageHint(slot, slot == 0 ? "请选择普通图片；选择前保留系统图标。" : "将使用普通图片。");
                QueuePreview();
                return;
            }
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif"))
                throw new InvalidDataException("请选择 PNG、JPG 或 GIF 图片。");
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("找不到图片，请检查路径。");
            if (info.Length == 0 || info.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("请选择小于 16 MB 的有效图片。");
            string stored = await ImportStartButtonImageAsync(path, extension, token);
            if (!Current()) return;
            _startButtonImageValues[slot] = stored;
            _startButtonImageTexts[slot] = stored;
            _loadingOptions = true;
            try { _startButtonImagePaths[slot].Text = stored; }
            finally { _loadingOptions = false; }
            SetStartButtonImageHint(slot, "已保存图片副本，可移动或删除原文件。");
            QueuePreview();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (Current())
            {
                string message = ex switch
                {
                    InvalidDataException => ex.Message,
                    FileNotFoundException or DirectoryNotFoundException => "找不到图片，请检查路径。",
                    UnauthorizedAccessException => "没有权限读取图片或保存图片副本，请换一个可读取的文件。",
                    IOException => "图片暂时无法读取或保存，请检查文件是否正在被其他程序占用。",
                    ArgumentException or NotSupportedException => "图片路径无效，请选择本地图片文件。",
                    _ => "图片无法解码，请换一张有效的 PNG、JPG 或 GIF 图片。"
                };
                SetStartButtonImageHint(slot, message + " 已保留上次有效的图片。");
            }
        }
    }

    private static async Task<string> ImportStartButtonImageAsync(string path, string extension, CancellationToken token)
    {
        string directory = Path.Combine(App.Current.ConfigurationDirectory, "StartButtonImages");
        Directory.CreateDirectory(directory);
        // Validate the exact bytes that are stored, so replacing the original
        // during import cannot bypass decoding or size checks.
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp" + extension);
        try
        {
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                if (input.Length == 0 || input.Length > 16 * 1024 * 1024)
                    throw new InvalidDataException("请选择小于 16 MB 的有效图片。");
                await input.CopyToAsync(output, token);
            }
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(temporary);
            using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                ulong pixels = (ulong)decoder.PixelWidth * decoder.PixelHeight;
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 2048 || decoder.PixelHeight > 2048
                    || pixels > 4_194_304 || pixels * decoder.FrameCount > 16_777_216)
                    throw new InvalidDataException("图片尺寸或动画过大，请选择较小的图标图片。");
                // Decode only a thumbnail to reject damaged pixel data without
                // allocating the full source image or playing GIF frames.
                var frame = await decoder.GetFrameAsync(0);
                var transform = new BitmapTransform { ScaledWidth = Math.Min(64, decoder.PixelWidth), ScaledHeight = Math.Min(64, decoder.PixelHeight) };
                _ = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            }
            token.ThrowIfCancellationRequested();
            byte[] hash;
            await using (var stream = File.OpenRead(temporary)) hash = await SHA256.HashDataAsync(stream, token);
            string destination = Path.Combine(directory, Convert.ToHexString(hash).ToLowerInvariant() + extension);
            try { File.Move(temporary, destination, false); }
            catch (IOException) when (File.Exists(destination))
            {
                // Repair an existing cached copy if it was modified externally.
                byte[] existing;
                await using (var stream = File.OpenRead(destination)) existing = await SHA256.HashDataAsync(stream, token);
                if (!existing.SequenceEqual(hash)) File.Move(temporary, destination, true);
            }
            return destination;
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private void SetStartButtonImageHint(int slot, string text)
    {
        if (_closed) return;
        _startButtonImageHints[slot].Text = text;
        _startButtonImageHints[slot].Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task PickImageAsync()
    {
        int scope = _draft.Scope;
        _pickImage.IsEnabled = false;
        _modalInteractions++;
        App.Current.ExitQuickMode();
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (var extension in ImageExtensions) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is not null && !_closed)
            {
                if (_draft.Scope == scope)
                {
                    _loadingOptions = true;
                    try { _backgroundImagePath.Text = file.Path; }
                    finally { _loadingOptions = false; }
                }
                await RequestImageValidation(scope, file.Path, immediate: true);
            }
        }
        catch (Exception ex) { if (!_closed) ShowError($"无法选择图片：{ex.Message}"); }
        finally { _modalInteractions--; if (!_closed) _pickImage.IsEnabled = true; }
    }

    private static int BackgroundIndex(string style) => style switch
    { "Transparent" => 1, "Acrylic" => 2, "Image" => 3, _ => 0 };

    private Task RequestImageValidation(int scope, string text, bool immediate = false)
    {
        _imageText[scope] = text;
        _imageCancellation[scope]?.Cancel();
        _imageCancellation[scope]?.Dispose();
        var cancellation = new CancellationTokenSource();
        _imageCancellation[scope] = cancellation;
        long version = ++_imageVersions[scope];
        SetImageHint(scope, string.IsNullOrWhiteSpace(text) ? "请选择一张背景图片。其他设置仍会自动生效。" : "正在检查图片…");
        var task = ValidateImageAsync(scope, text, version, cancellation.Token, immediate);
        _imageValidationTasks[scope] = task;
        return task;
    }

    private bool IsCurrentImageRequest(int scope, string text, long version, CancellationToken token) =>
        !_closed && !token.IsCancellationRequested && _imageVersions[scope] == version && _imageText[scope] == text;

    private async Task ValidateImageAsync(int scope, string text, long version, CancellationToken token, bool immediate)
    {
        try
        {
            if (!immediate) await Task.Delay(250, token);
            if (!IsCurrentImageRequest(scope, text, version, token)) return;
            string path = text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                SetImageHint(scope, "请选择一张背景图片。其他设置仍会自动生效。");
                return;
            }
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            string? error = !ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                ? "请选择 PNG、JPG、JPEG、BMP、GIF 或 WebP 图片。"
                : !File.Exists(path) ? "找不到图片，请检查路径；已保留上次有效的背景。" : null;
            if (error is not null) { SetImageHint(scope, error); return; }
            var image = await ShellIconService.LoadImageAsync(path, 64);
            if (!IsCurrentImageRequest(scope, text, version, token)) return;
            if (image is null) { SetImageHint(scope, "图片无法读取，请换一张图片；已保留上次有效的背景。"); return; }
            var options = _draft.Options(scope);
            options.BackgroundImagePath = path;
            if (_imageChoices[scope] == 3) options.BackgroundStyle = "Image";
            SetImageHint(scope, "");
            QueuePreview();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsCurrentImageRequest(scope, text, version, token))
                SetImageHint(scope, "图片路径或文件无法读取；已保留上次有效的背景。其他设置仍会自动生效。");
        }
    }

    private void SetImageHint(int scope, string text)
    {
        _imageHints[scope] = text;
        if (_closed || _draft.Scope != scope) return;
        _imageHint.Text = text;
        _imageHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task PickSettingsImageAsync()
    {
        _settingsPickImage.IsEnabled = false;
        _modalInteractions++;
        App.Current.ExitQuickMode();
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (var extension in ImageExtensions) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || _closed) return;
            _loadingOptions = true;
            try { _settingsImagePath.Text = file.Path; }
            finally { _loadingOptions = false; }
            await RequestSettingsImageValidation(file.Path, immediate: true);
        }
        catch (Exception ex) { if (!_closed) ShowError($"无法选择图片：{ex.Message}"); }
        finally { _modalInteractions--; if (!_closed) _settingsPickImage.IsEnabled = true; }
    }

    private Task RequestSettingsImageValidation(string text, bool immediate = false)
    {
        _settingsImageText = text;
        _settingsImageCancellation?.Cancel(); _settingsImageCancellation?.Dispose();
        var cancellation = _settingsImageCancellation = new CancellationTokenSource();
        long version = ++_settingsImageVersion;
        SetSettingsImageHint(string.IsNullOrWhiteSpace(text) ? "请选择一张背景图片。其他设置仍会自动生效。" : "正在检查图片…");
        return _settingsImageTask = ValidateSettingsImageAsync(text, version, cancellation.Token, immediate);
    }

    private async Task ValidateSettingsImageAsync(string text, long version, CancellationToken token, bool immediate)
    {
        bool Current() => !_closed && !token.IsCancellationRequested && version == _settingsImageVersion && text == _settingsImageText;
        try
        {
            if (!immediate) await Task.Delay(250, token);
            if (!Current()) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetSettingsImageHint("请选择一张背景图片。其他设置仍会自动生效。"); return;
            }
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(text.Trim().Trim('"')));
            bool valid = await SettingsWindowBackdrop.ValidateImageAsync(path, token);
            if (!Current()) return;
            if (!valid)
            {
                SetSettingsImageHint("图片无法读取，请选择本地图片；已保留上次有效的背景。"); return;
            }
            _settingsImageValue = path;
            _settingsBackdrop.InvalidateImage(path);
            if (_settingsBackground.SelectedIndex == 3) _settingsBackgroundValue = "Image";
            SetSettingsImageHint(""); ApplyBackgroundColor(); QueuePreview();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            if (Current()) SetSettingsImageHint("图片路径或文件无法读取；已保留上次有效的背景。其他设置仍会自动生效。");
        }
    }

    private void SetSettingsImageHint(string text)
    {
        if (_closed) return;
        _settingsImageHint.Text = text;
        _settingsImageHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowError(string text) { _message.Title = "需要留意"; _message.Message = text; _message.Severity = InfoBarSeverity.Error; _message.IsOpen = true; }
}

