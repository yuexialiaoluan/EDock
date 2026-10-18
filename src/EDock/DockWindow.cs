using EDock.Models;
using EDock.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Dispatching;
using EDock.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace EDock;

public sealed class DockWindow : Window
{
    private readonly DockSide _side;
    private readonly Grid _root = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly StackPanel _items = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Border _shell;
    private readonly Grid _surface = new();
    private readonly Image _backgroundImage = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly Border _backgroundOverlay = new() { IsHitTestVisible = false };
    private readonly TextBlock _header = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.None, TextWrapping = TextWrapping.NoWrap, MaxLines = 1 };
    private readonly Button _listButton;
    private readonly Button _sortButton;
    private readonly Button _expandButton;
    private readonly Grid _footer;
    private readonly RowDefinition _footerSpace = new() { Height = new GridLength(0) };
    private double _normalLayoutWidth;
    private double _tileColumnWidth = 76;
    private double _itemSpacing = 8;
    private bool _displayLayoutQueued;
    private bool _quickMode;
    private bool _quickSelectionTarget;
    private bool _ordinarySelection;
    private bool _ordinaryKeyboardHold;
    private Button? _selectedVisualButton;
    private string? _quickSelectedId;
    private string? _selectionListId;
    private readonly List<(Button Button, ShortcutItem Item)> _tileButtons = new();
    private const string InternalDragFormat = "EDock.Shortcut.v1";
    private static ShortcutDrag? _shortcutDrag;
    private long _suppressClickUntil;
    public bool IsInteractionBusy => _interactions > 0;
    internal bool IsReadyForIdleMemory => _resourcesSuspended && !_closing && !_edge.IsRevealed
        && !_quickMode && !_ordinaryKeyboardHold && _interactions == 0 && _shortcutDrag is null && !_expanded && !_sorting;
    private bool _expanded;
    private int _expandedRows = 1;
    private double _expandedLayoutWidth;
    private bool _expandedLayoutQueued;
    private int _sortVersion;
    private bool _sorting;
    private Storyboard? _handleAnimation;
    private readonly ScrollViewer _scroll;
    private readonly DispatcherQueueTimer _scrollTimer;
    private readonly DispatcherQueueTimer _resourceReleaseTimer;
    private bool _resourcesSuspended;
    private string? _suspendedListId;
    private double _suspendedScrollOffset;
    private bool _restoreScrollPending;
    private readonly TranslateTransform _scrollBounceTransform = new();
    private readonly DockScrollBounce _scrollBounce = new();
    private readonly Dictionary<string, Task<ImageSource?>> _tileImages = new();
    private readonly HashSet<string> _usedImageKeys = new();
    private int _imageVersion;
    private double _iconScale = 1;
    private MenuFlyout? _itemMenu;
    private StackPanel? _primaryCycle;
    private bool _repeatedCycles;
    private bool _metricsQueued;
    private double _cycleHeight;
    private double _scrollValue;
    private double _scrollTarget;
    private long _scrollTick;
    private string? _backdropKey;
    private DockBackdrop? _ownedBackdrop;
    private string? _backgroundPath;
    private int _backgroundVersion;
    private readonly Button _pinButton;
    private readonly EdgeController _edge;
    private int _interactions;
    private bool _closing;
    private readonly TextBlock _empty;

    public DockWindow(DockSide side)
    {
        _side = side;
        Title = $"EDock · {(side == DockSide.Left ? "左侧" : "右侧")}";
        App.SetWindowIcon(this);
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(_footerSpace);
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(4, 16, 4, 12), HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = new Grid { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(_header);
        var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 1); heading.Children.Add(chevron);
        _listButton = new Button { Content = heading, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(4), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_listButton, "管理和切换列表");
        top.Children.Add(_listButton);
        top.Children.Add(new Border { Width = 18, Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ColorHelper.FromArgb(255, 116, 155, 255)) });
        _root.Children.Add(top);
        var scrollContent = new Border { Child = _items, Padding = new Thickness(6, 0, 6, 0), Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Stretch };
        _scroll = new ScrollViewer { Content = scrollContent, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, IsTabStop = false };
        Grid.SetRow(_scroll, 1);
        _root.Children.Add(_scroll);
        _items.RenderTransform = _scrollBounceTransform;
        _scrollTimer = DispatcherQueue.CreateTimer();
        _scrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _scrollTimer.IsRepeating = true;
        _scrollTimer.Tick += OnScrollTick;
        _resourceReleaseTimer = DispatcherQueue.CreateTimer();
        _resourceReleaseTimer.IsRepeating = false;
        _resourceReleaseTimer.Tick += OnResourceReleaseTick;
        _scroll.SizeChanged += (_, _) => { QueueScrollMetrics(); QueueExpandedLayout(); };
        _items.SizeChanged += (_, _) => QueueScrollMetrics();
        _scroll.ViewChanged += (_, _) =>
        {
            if (_closing || _resourcesSuspended || _scrollTimer.IsRunning || !_repeatedCycles || _cycleHeight <= 0) return;
            double offset = _scroll.VerticalOffset;
            if (offset < _cycleHeight - .5 || offset >= 2 * _cycleHeight)
            {
                _scrollValue = _scrollTarget = DockScrollLogic.Recenter(offset, _cycleHeight);
                _scroll.ChangeView(null, _scrollValue, null, true);
            }
        };
        // Consume item-area wheel input before the ScrollViewer's default
        // handler, so it cannot apply a second movement after a loop wrap.
        _items.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        scrollContent.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        // Handle the wheel at the dock boundary as well as over the items, so
        // the header, empty space and footer scroll the shortcut list too.
        _root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        _empty = new TextBlock { Text = "拖入\n快捷方式", TextAlignment = TextAlignment.Center, FontSize = 11, Opacity = .6, Margin = new Thickness(0, 20, 0, 0) };
        var bottom = _footer = new Grid { RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(6, 10, 6, 12), HorizontalAlignment = HorizontalAlignment.Center };
        _sortButton = ToolButton("\uE8CB", "排序当前列表");
        bottom.Children.Add(_sortButton);
        var add = ToolButton("\uE710", "添加快捷方式");
        var addMenu = new MenuFlyout();
        AddMenuItem(addMenu, "选择文件或快捷方式…", async () => await PickFilesAsync());
        AddMenuItem(addMenu, "选择文件夹…", async () => await PickFolderAsync());
        AddMenuItem(addMenu, "输入路径或网址…", async () => await AddLocationAsync());
        GuardFlyout(addMenu);
        add.Flyout = addMenu;
        bottom.Children.Add(add);
        _pinButton = ToolButton("\uE718", "切换自动隐藏");
        _pinButton.Click += (_, _) =>
        {
            var current = App.Current.Settings.GetSideOptions(_side);
            App.Current.Settings.SetSideAutoHide(_side, !current.AutoHide);
            SaveOrReport();
        };
        bottom.Children.Add(_pinButton);
        var settings = ToolButton("\uE713", "设置");
        settings.Click += (_, _) => App.Current.ShowSettings(_side);
        bottom.Children.Add(settings);
        Grid.SetRow(bottom, 2);
        _root.Children.Add(bottom);
        _expandButton = ToolButton(_side == DockSide.Left ? "\uE76C" : "\uE76B", "展开当前列表全部图标");
        _expandButton.Width = 24; _expandButton.Height = 58;
        _expandButton.Padding = new Thickness(0);
        _expandButton.HorizontalAlignment = _side == DockSide.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _expandButton.VerticalAlignment = VerticalAlignment.Center;
        _expandButton.Background = new SolidColorBrush(ColorHelper.FromArgb(225, 60, 85, 145));
        _expandButton.Opacity = 0; _expandButton.IsHitTestVisible = false;
        _expandButton.Click += (_, _) => ToggleExpanded();
        Grid.SetRowSpan(_expandButton, 4);
        _root.Children.Add(_expandButton);
        _surface.Children.Add(_backgroundImage);
        _surface.Children.Add(_backgroundOverlay);
        _surface.Children.Add(_root);
        _shell = new Border { Child = _surface, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0) };
        Content = _shell;
        _root.AllowDrop = true;
        _root.DragOver += OnDragOver;
        _root.Drop += OnDrop;
        _edge = new EdgeController(this, side, () => _interactions > 0);
        _edge.AvailableSizeChanged += (_, _) =>
        {
            if (_closing || _displayLayoutQueued) return;
            _displayLayoutQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _displayLayoutQueued = false;
                if (!_closing) Refresh();
            });
        };
        _edge.KeyboardTargetChanged += (_, _) => App.Current.RefreshPointerKeyboardTarget();
        _edge.KeyboardHoldTick += (_, _) => App.Current.RefreshPointerKeyboardTarget();
        _edge.InnerEdgeHoverChanged += (_, visible) => SetExpansionHandleVisible(visible);
        _edge.RevealedChanged += (_, revealed) =>
        {
            App.Current.NotifyIdleMemoryStateChanged();
            if (revealed)
            {
                _resourceReleaseTimer.Stop();
                if (_resourcesSuspended) Refresh();
            }
            else
            {
                ClearOrdinarySelection();
                if (_expanded) { _expanded = false; _edge.SetExpandedWidth(null); Refresh(); }
                ScheduleResourceRelease();
            }
        };
        Closed += (_, _) =>
        {
            _closing = true; ++_imageVersion; ++_backgroundVersion;
            _resourceReleaseTimer.Stop();
            _resourceReleaseTimer.Tick -= OnResourceReleaseTick;
            _root.ActualThemeChanged -= OnThemeChanged;
            _itemMenu?.Hide(); _tileImages.Clear(); _backgroundImage.Source = null;
            EndShortcutDrag(); StopScrolling();
            _scrollTimer.Tick -= OnScrollTick;
            _handleAnimation?.Stop(); _handleAnimation = null;
            _edge.Dispose();
        };
        Refresh();
    }

    private static Button ToolButton(string glyph, string tooltip)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 16 }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 36, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(9) };
        ToolTipService.SetToolTip(b, tooltip);
        AutomationProperties.SetName(b, tooltip);
        return b;
    }

    public void Refresh()
    {
        if (_closing) return;
        var settings = App.Current.Settings.GetSideOptions(_side);
        var activeList = App.Current.Settings.GetActiveList(_side);
        if (_selectionListId != activeList.Id)
        {
            _selectionListId = activeList.Id;
            _quickSelectedId = activeList.Shortcuts.FirstOrDefault()?.Id;
        }
        else if (!activeList.Shortcuts.Any(item => item.Id == _quickSelectedId))
            _quickSelectedId = activeList.Shortcuts.FirstOrDefault()?.Id;
        if (_restoreScrollPending && (activeList.Id != _suspendedListId || activeList.Shortcuts.Count == 0)) _restoreScrollPending = false;
        if (_resourcesSuspended && !_edge.IsRevealed && settings.AutoHide && !_quickMode)
        {
            // Keep hidden geometry/settings current without decoding invisible assets.
            ConfigureEdge(settings);
            return;
        }
        _resourcesSuspended = false;
        _root.RequestedTheme = settings.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        _header.Text = activeList.Name;
        _header.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(_listButton, $"{activeList.Name} · {activeList.Shortcuts.Count} 项\n点击管理列表；鼠标在边栏上时按 Q / E 切换");
        RebuildListMenu();
        RebuildSortMenu();
        ApplyColors();
        _root.ActualThemeChanged -= OnThemeChanged;
        _root.ActualThemeChanged += OnThemeChanged;
        _pinButton.Content = new FontIcon { Glyph = settings.AutoHide ? "\uE718" : "\uE77A", FontSize = 16 };
        ToolTipService.SetToolTip(_pinButton, settings.AutoHide ? "自动隐藏中 · 点击保持展开" : "保持展开中 · 点击自动隐藏");
        _shell.CornerRadius = new CornerRadius(settings.DockFrameStyle == "Floating" ? 16 : 0);
        ConfigureEdge(settings);
        _normalLayoutWidth = DockLayoutLogic.NormalWidth(settings, _edge.AvailableWidthDip);
        _tileColumnWidth = DockLayoutLogic.ColumnWidth(settings, _edge.AvailableWidthDip);
        _itemSpacing = DockLayoutLogic.EffectiveSpacing(settings, _edge.AvailableWidthDip);
        _items.Spacing = _itemSpacing;
        if (_expanded) UpdateExpandedLayout();
        UpdateChromeLayout(_expanded ? _expandedLayoutWidth : _normalLayoutWidth);
        StopScrolling();
        _itemMenu?.Hide();
        ++_imageVersion;
        _iconScale = _root.XamlRoot?.RasterizationScale ?? Math.Max(1, NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d);
        _usedImageKeys.Clear();
        _repeatedCycles = false;
        _cycleHeight = 0;
        _scrollValue = _scrollTarget = 0;
        _items.Children.Clear();
        _tileButtons.Clear();
        _selectedVisualButton = null;
        var list = App.Current.Items(_side);
        if (list.Count == 0) _items.Children.Add(_empty);
        _primaryCycle = list.Count == 0 ? null : CreateCycle();
        if (_primaryCycle is not null)
        {
            _primaryCycle.SizeChanged += (_, _) => QueueScrollMetrics();
            _items.Children.Add(_primaryCycle);
        }
        foreach (string key in _tileImages.Keys.Where(key => !_usedImageKeys.Contains(key)).ToArray()) _tileImages.Remove(key);
        _scroll.ChangeView(null, 0, null, true);
        UpdateExpansionHandle();
        SetExpansionHandleVisible(_edge.IsPointerNearInnerEdge);
        QueueScrollMetrics();
        UpdateQuickSelection();
        if (_quickMode || _ordinarySelection) ScrollToQuickSelection();
        ScheduleResourceRelease();
    }

    private void ConfigureEdge(DockOptions settings) =>
        _edge.Configure(settings.AutoHide, settings.HideDelayMs, settings.RevealDelayMs, settings.DockWidth,
            settings.RevealDurationMs, settings.HideDurationMs, settings.AnimationStyle, settings.DockFrameStyle,
            DockLayoutLogic.PreferredWidth(settings), settings.FloatingTopInset, settings.FloatingBottomInset);

    private void ScheduleResourceRelease()
    {
        if (_closing || _resourcesSuspended || _edge.IsRevealed || _quickMode
            || !App.Current.Settings.GetSideOptions(_side).AutoHide)
        {
            _resourceReleaseTimer.Stop();
            return;
        }
        if (_resourceReleaseTimer.IsRunning) return;
        _resourceReleaseTimer.Interval = TimeSpan.FromSeconds(8);
        _resourceReleaseTimer.Start();
    }

    private void OnResourceReleaseTick(DispatcherQueueTimer sender, object args)
    {
        if (_closing || _resourcesSuspended || _edge.IsRevealed || _quickMode
            || !App.Current.Settings.GetSideOptions(_side).AutoHide) return;
        if (_interactions > 0 || _shortcutDrag is not null || _expanded || _sorting)
        {
            // A drag/menu may outlive the delay. Reuse the timer until it finishes.
            _resourceReleaseTimer.Interval = TimeSpan.FromSeconds(2);
            _resourceReleaseTimer.Start();
            return;
        }

        _suspendedListId = App.Current.Settings.GetActiveList(_side).Id;
        _suspendedScrollOffset = _repeatedCycles && _cycleHeight > 0
            ? DockScrollLogic.Recenter(_scroll.VerticalOffset, _cycleHeight) - _cycleHeight
            : _scroll.VerticalOffset;
        _restoreScrollPending = _primaryCycle is not null;
        _resourcesSuspended = true;
        StopScrolling();
        _handleAnimation?.Stop(); _handleAnimation = null;
        _expandButton.Opacity = 0; _expandButton.IsHitTestVisible = false;
        ++_imageVersion; ++_backgroundVersion;
        _itemMenu?.Hide(); _itemMenu = null;
        _repeatedCycles = false; _cycleHeight = 0;
        _scrollValue = _scrollTarget = 0;
        _items.Children.Clear(); _tileButtons.Clear(); _selectedVisualButton = null; _primaryCycle = null;
        _tileImages.Clear(); _usedImageKeys.Clear();
        _backgroundImage.Source = null; _backgroundImage.Visibility = Visibility.Collapsed;
        _backgroundPath = null;
        SystemBackdrop = null; _ownedBackdrop = null; _backdropKey = null;
        App.Current.NotifyIdleMemoryStateChanged();
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => ApplyColors();

    private void RebuildListMenu()
    {
        var menu = new MenuFlyout();
        var active = App.Current.Settings.GetActiveList(_side);
        foreach (var list in App.Current.Settings.GetLists(_side))
        {
            string id = list.Id;
            var choice = new ToggleMenuFlyoutItem { Text = $"{list.Name}（{list.Shortcuts.Count}）", IsChecked = id == active.Id };
            choice.Click += (_, _) => { if (App.Current.Settings.SelectList(_side, id)) { _expanded = false; _edge.SetExpandedWidth(null); SaveOrReport(); } };
            menu.Items.Add(choice);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "新建列表…", async () => await AddListAsync());
        AddMenuItem(menu, "重命名当前列表…", async () => await RenameListAsync());
        var remove = new MenuFlyoutItem { Text = "删除当前列表…", IsEnabled = App.Current.Settings.GetLists(_side).Count > 1 };
        remove.Click += async (_, _) => await DeleteListAsync();
        menu.Items.Add(remove);
        GuardFlyout(menu);
        _listButton.Flyout = menu;
    }

    private void SwitchList(int direction)
    {
        if (_closing || _interactions > 0) return;
        var lists = App.Current.Settings.GetLists(_side);
        if (lists.Count < 2) return;
        int index = lists.IndexOf(App.Current.Settings.GetActiveList(_side));
        int next = (index + (direction < 0 ? -1 : 1) + lists.Count) % lists.Count;
        if (App.Current.Settings.SelectList(_side, lists[next].Id))
        {
            _expanded = false; _edge.SetExpandedWidth(null); SaveOrReport();
        }
    }

    private async Task AddListAsync()
    {
        var field = new TextBox { Header = "列表名称", Text = "新列表", MaxLength = 80, MinWidth = 280 };
        AutomationProperties.SetName(field, "列表名称");
        string name = "";
        if (await DialogAsync("新建列表", field, "创建", () => name = field.Text) != ContentDialogResult.Primary) return;
        App.Current.Settings.AddList(_side, name);
        _expanded = false; _edge.SetExpandedWidth(null); SaveOrReport();
    }

    private async Task RenameListAsync()
    {
        var list = App.Current.Settings.GetActiveList(_side);
        var field = new TextBox { Header = "列表名称", Text = list.Name, MaxLength = 80, MinWidth = 280 };
        AutomationProperties.SetName(field, "列表名称");
        string name = "";
        if (await DialogAsync("重命名列表", field, "保存", () => name = field.Text) == ContentDialogResult.Primary
            && App.Current.Settings.RenameList(_side, list.Id, name)) SaveOrReport();
    }

    private async Task DeleteListAsync()
    {
        var lists = App.Current.Settings.GetLists(_side);
        if (lists.Count < 2) return;
        var list = App.Current.Settings.GetActiveList(_side);
        int index = lists.IndexOf(list);
        var destination = lists[index + 1 < lists.Count ? index + 1 : index - 1];
        var text = new TextBlock { Text = $"删除列表“{list.Name}”？\n其中 {list.Shortcuts.Count} 个快捷方式将移入“{destination.Name}”。\n电脑上的文件和应用不会被删除。", TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        if (await DialogAsync("删除列表", text, "删除列表") != ContentDialogResult.Primary) return;
        if (App.Current.Settings.DeleteList(_side, list.Id)) { _expanded = false; _edge.SetExpandedWidth(null); SaveOrReport(); }
    }

    private void RebuildSortMenu()
    {
        var menu = new MenuFlyout();
        var list = App.Current.Settings.GetActiveList(_side);
        foreach (var pair in new[] { (ShortcutSortKey.Name, "名称"), (ShortcutSortKey.Added, "添加日期"), (ShortcutSortKey.Modified, "修改日期"), (ShortcutSortKey.Size, "文件大小"), (ShortcutSortKey.Type, "文件类型") })
        {
            var group = new MenuFlyoutSubItem { Text = pair.Item2 };
            foreach (bool descending in new[] { false, true })
            {
                var key = pair.Item1; bool reverse = descending;
                string order = key is ShortcutSortKey.Added or ShortcutSortKey.Modified
                    ? (reverse ? "从新到旧" : "从旧到新") : key == ShortcutSortKey.Size
                    ? (reverse ? "从大到小" : "从小到大") : (reverse ? "降序 Z → A" : "升序 A → Z");
                var item = new ToggleMenuFlyoutItem { Text = order, IsChecked = list.SortKey == key && list.SortDescending == reverse };
                item.Click += async (_, _) => await SortCurrentAsync(key, reverse);
                group.Items.Add(item);
            }
            menu.Items.Add(group);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var manual = new ToggleMenuFlyoutItem { Text = "手动排序（保留当前顺序）", IsChecked = list.SortKey == ShortcutSortKey.Manual };
        manual.Click += (_, _) => { list.SortKey = ShortcutSortKey.Manual; SaveOrReport(); };
        menu.Items.Add(manual);
        GuardFlyout(menu); _sortButton.Flyout = menu;
        _sortButton.IsEnabled = !_sorting;
        ToolTipService.SetToolTip(_sortButton, "排序当前列表\n按名称、添加日期、修改日期、大小或类型排序");
    }

    private async Task SortCurrentAsync(ShortcutSortKey key, bool descending)
    {
        var list = App.Current.Settings.GetActiveList(_side);
        var snapshot = list.Shortcuts.ToArray();
        int version = ++_sortVersion;
        _sorting = true; _sortButton.IsEnabled = false; _interactions++;
        try
        {
            var sorted = await ShortcutSortService.SortAsync(snapshot, key, descending);
            if (_closing || version != _sortVersion || App.Current.Settings.GetActiveList(_side) != list
                || !list.Shortcuts.SequenceEqual(snapshot)) return;
            list.Shortcuts.Clear(); list.Shortcuts.AddRange(sorted);
            list.SortKey = key; list.SortDescending = descending; SaveOrReport();
        }
        catch (Exception ex) { if (!_closing) App.Current.ReportError($"排序失败：{ex.Message}"); }
        finally { _interactions = Math.Max(0, _interactions - 1); if (version == _sortVersion) { _sorting = false; _sortButton.IsEnabled = true; } }
    }

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        if (!_expanded) _edge.SetExpandedWidth(null);
        Refresh();
        _edge.Reveal();
    }

    private bool UpdateExpandedLayout()
    {
        var settings = App.Current.Settings.GetSideOptions(_side);
        int count = App.Current.Items(_side).Count;
        double columnWidth = _tileColumnWidth;
        double tileHeight = DockLayoutLogic.TileHeight(settings, columnWidth);
        int maxColumns = Math.Max(settings.NormalColumns, (int)Math.Floor((_edge.AvailableWidthDip - 12 + _itemSpacing) / (columnWidth + _itemSpacing)));
        int previousRows = _expandedRows;
        int columns = settings.NormalColumns;
        double width;
        // Calculate the footer and item capacity together. Using the old viewport
        // here can oscillate between column counts when the footer changes rows.
        do
        {
            width = Math.Min(_edge.AvailableWidthDip, Math.Max(_normalLayoutWidth, 12 + columns * columnWidth + (columns - 1) * _itemSpacing));
            double viewport = DockLayoutLogic.Footer(settings, width, _edge.AvailableHeightDip, tileHeight).ViewportHeight;
            _expandedRows = Math.Max(1, (int)Math.Floor((viewport + _itemSpacing) / (tileHeight + _itemSpacing)));
            if (count <= columns * _expandedRows || columns == maxColumns) break;
            columns++;
        } while (true);
        if (count > columns * _expandedRows) _expandedRows = (int)Math.Ceiling((double)count / columns);
        bool changed = previousRows != _expandedRows || Math.Abs(width - _expandedLayoutWidth) > .1;
        _expandedLayoutWidth = width;
        _edge.SetExpandedWidth(width);
        return changed;
    }

    private void UpdateChromeLayout(double width)
    {
        _header.MaxWidth = Math.Max(12, width - 38);
        _listButton.Width = Math.Max(32, width - 8);
        _footer.Width = Math.Max(48, width - 12);
        int columns = DockLayoutLogic.FooterColumns(width);
        var settings = App.Current.Settings.GetSideOptions(_side);
        var placement = DockLayoutLogic.Footer(settings, width, _edge.AvailableHeightDip, DockLayoutLogic.TileHeight(settings, _tileColumnWidth));
        _footer.Margin = new Thickness(6, 10, 6, placement.BottomMargin);
        _footerSpace.Height = new GridLength(placement.BottomSpace);
        _footer.ColumnDefinitions.Clear(); _footer.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4 / columns; i++) _footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < _footer.Children.Count; i++)
        {
            Grid.SetColumn((FrameworkElement)_footer.Children[i], i % columns);
            Grid.SetRow((FrameworkElement)_footer.Children[i], i / columns);
        }
    }

    public bool IsPointerOverDock
    {
        get
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            return _edge.IsRevealed && NativeMethods.GetCursorPos(out var point)
                && NativeMethods.IsPointerOverWindow(hwnd, point);
        }
    }

    internal bool IsPointerKeyboardTarget => !_closing && !_resourcesSuspended && !IsInteractionBusy
        && _edge.IsKeyboardTarget && IsPointerOverDock && !HasTextInputFocus();

    internal bool IsSessionKeyboardTarget => _ordinaryKeyboardHold && !_closing && !_resourcesSuspended
        && !IsInteractionBusy && _edge.IsRevealed && !HasTextInputFocus();

    private bool CanUseKeyboardNavigation => IsPointerKeyboardTarget || IsSessionKeyboardTarget;

    internal void SetOrdinaryKeyboardHold(bool enabled)
    {
        if (_ordinaryKeyboardHold == enabled) return;
        _ordinaryKeyboardHold = enabled;
        _edge.SetKeyboardHold(enabled);
        if (!enabled) ClearOrdinarySelection();
        App.Current.NotifyIdleMemoryStateChanged();
    }

    internal void BeginOrdinaryKeyboardSelection()
    {
        if (!IsSessionKeyboardTarget) return;
        _ordinarySelection = true;
        if (!App.Current.Items(_side).Any(item => item.Id == _quickSelectedId))
            _quickSelectedId = App.Current.Items(_side).FirstOrDefault()?.Id;
        UpdateQuickSelection();
        ScrollToQuickSelection();
    }

    private bool HasTextInputFocus() => _root.XamlRoot is { } root
        && FocusManager.GetFocusedElement(root) is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;

    internal void ClearOrdinarySelection()
    {
        if (!_ordinarySelection) return;
        _ordinarySelection = false;
        _selectedVisualButton = null;
        UpdateQuickSelection();
    }

    internal void HandleKeyboardAction(DockKeyboardAction action)
    {
        if (!CanUseKeyboardNavigation) return;
        switch (action)
        {
            case DockKeyboardAction.Previous: MoveKeyboardSelection(-1, horizontal: false); break;
            case DockKeyboardAction.Next: MoveKeyboardSelection(1, horizontal: false); break;
            case DockKeyboardAction.PreviousColumn: MoveKeyboardSelection(-1, horizontal: true); break;
            case DockKeyboardAction.NextColumn: MoveKeyboardSelection(1, horizontal: true); break;
            case DockKeyboardAction.PreviousList: SwitchList(-1); break;
            case DockKeyboardAction.NextList: SwitchList(1); break;
            case DockKeyboardAction.ToggleExpanded: ToggleExpanded(); break;
            case DockKeyboardAction.Launch:
                if (_ordinarySelection) LaunchQuickItem();
                break;
        }
    }

    internal void SwitchKeyboardList(int direction) => SwitchList(direction);
    internal void ToggleKeyboardExpansion() => ToggleExpanded();

    public void SetQuickMode(bool enabled, bool selectionTarget = true)
    {
        _ordinarySelection = false;
        _quickMode = enabled;
        _quickSelectionTarget = selectionTarget;
        if (enabled && !App.Current.Items(_side).Any(x => x.Id == _quickSelectedId))
            _quickSelectedId = App.Current.Items(_side).FirstOrDefault()?.Id;
        _edge.SetQuickMode(enabled);
        UpdateQuickSelection();
        if (enabled) ScrollToQuickSelection();
    }

    public void SetQuickSelectionTarget(bool enabled)
    {
        _quickSelectionTarget = enabled;
        UpdateQuickSelection();
    }

    public void SelectQuickItem(int direction)
    {
        var items = App.Current.Items(_side);
        if (items.Count == 0) return;
        bool firstOrdinarySelection = !_quickMode && !_ordinarySelection;
        if (!_quickMode)
        {
            if (!CanUseKeyboardNavigation) return;
            _ordinarySelection = true;
        }
        int index = items.FindIndex(x => x.Id == _quickSelectedId);
        index = firstOrdinarySelection || index < 0 ? (direction < 0 ? items.Count - 1 : 0)
            : (index + (direction < 0 ? -1 : 1) + items.Count) % items.Count;
        _quickSelectedId = items[index].Id;
        _selectedVisualButton = null;
        UpdateQuickSelection(); ScrollToQuickSelection();
    }

    public void MoveKeyboardSelection(int direction, bool horizontal)
    {
        if (_closing || _resourcesSuspended || (!_quickMode && !CanUseKeyboardNavigation)) return;
        var items = App.Current.Items(_side);
        if (items.Count == 0 || _tileButtons.Count == 0) return;
        if (!_quickMode && !_ordinarySelection)
        {
            SelectQuickItem(direction);
            return;
        }
        _items.UpdateLayout();
        var cells = new DockNavigationCell[_tileButtons.Count];
        int current = -1;
        double closest = double.PositiveInfinity;
        double center = _scroll.VerticalOffset + _scroll.ViewportHeight / 2;
        for (int i = 0; i < _tileButtons.Count; i++)
        {
            var (button, item) = _tileButtons[i];
            var point = button.TransformToVisual(_items).TransformPoint(default);
            cells[i] = new(point.X, point.Y);
            if (item.Id != _quickSelectedId) continue;
            if (button == _selectedVisualButton) { current = i; closest = -1; }
            else if (closest >= 0 && Math.Abs(point.Y + button.ActualHeight / 2 - center) < closest)
            {
                current = i;
                closest = Math.Abs(point.Y + button.ActualHeight / 2 - center);
            }
        }
        int next = DockNavigationLogic.Neighbor(cells, current, direction, horizontal);
        if (next < 0) return;
        _selectedVisualButton = _tileButtons[next].Button;
        _quickSelectedId = _tileButtons[next].Item.Id;
        UpdateQuickSelection();
        ScrollToQuickSelection();
    }

    public void LaunchQuickItem()
    {
        var item = App.Current.Items(_side).Find(x => x.Id == _quickSelectedId);
        if (item is not null) LaunchItem(item);
    }

    private void UpdateQuickSelection()
    {
        foreach (var (button, item) in _tileButtons)
        {
            bool selected = ((_quickMode && _quickSelectionTarget) || _ordinarySelection) && item.Id == _quickSelectedId;
            button.Background = new SolidColorBrush(selected ? ColorHelper.FromArgb(95, 94, 143, 255) : Colors.Transparent);
            button.BorderBrush = new SolidColorBrush(selected ? ColorHelper.FromArgb(255, 135, 176, 255) : Colors.Transparent);
            // Keep geometry stable when selection changes.
            button.BorderThickness = new Thickness(2);
            AutomationProperties.SetItemStatus(button, selected ? (_quickMode ? "Ctrl 模式已选中" : "键盘已选中") : "");
        }
    }

    private void ScrollToQuickSelection()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_closing || !(_quickMode || _ordinarySelection) || _primaryCycle is null) return;
            var match = _tileButtons.FirstOrDefault(x => x.Button == _selectedVisualButton && x.Item.Id == _quickSelectedId);
            if (match.Button is null) match = _tileButtons.Where(x => x.Item.Id == _quickSelectedId)
                .OrderBy(x => Math.Abs(x.Button.TransformToVisual(_items).TransformPoint(default).Y - (_scroll.VerticalOffset + _scroll.ViewportHeight / 2)))
                .FirstOrDefault();
            if (match.Button is null) return;
            StopScrolling();
            var origin = match.Button.TransformToVisual(_items).TransformPoint(default);
            double offset = _scroll.VerticalOffset;
            if (origin.Y < offset) offset = origin.Y;
            else if (origin.Y + match.Button.ActualHeight > offset + _scroll.ViewportHeight)
                offset = origin.Y + match.Button.ActualHeight - _scroll.ViewportHeight;
            _scrollValue = _scrollTarget = Math.Clamp(offset, 0, _scroll.ScrollableHeight);
            _scroll.ChangeView(null, _scrollValue, null, true);
        });
    }

    private void LaunchItem(ShortcutItem item)
    {
        App.Current.ExitQuickMode();
        try { ShortcutService.Launch(item); }
        catch (Exception ex) { App.Current.ReportError($"无法打开“{item.Name}”：{ex.Message}"); }
    }

    private void QueueExpandedLayout()
    {
        if (_closing || _resourcesSuspended || !_expanded || _expandedLayoutQueued) return;
        _expandedLayoutQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _expandedLayoutQueued = false;
            if (!_closing && !_resourcesSuspended && _expanded && UpdateExpandedLayout()) Refresh();
        });
    }

    private void UpdateExpansionHandle()
    {
        string label = _expanded ? "收回当前列表展开" : "展开当前列表全部图标";
        _expandButton.Content = new FontIcon { Glyph = (_side == DockSide.Left) ^ _expanded ? "\uE76C" : "\uE76B", FontSize = 12 };
        ToolTipService.SetToolTip(_expandButton, label);
        AutomationProperties.SetName(_expandButton, label);
    }

    private void SetExpansionHandleVisible(bool visible)
    {
        if (_closing || _resourcesSuspended) return;
        visible &= _edge.IsRevealed && App.Current.Items(_side).Count > 0;
        _expandButton.IsHitTestVisible = visible;
        _handleAnimation?.Stop();
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            _expandButton.Opacity = visible ? .94 : 0;
            return;
        }
        _handleAnimation = new Storyboard();
        var animation = new DoubleAnimation { To = visible ? .94 : 0, Duration = new Duration(TimeSpan.FromMilliseconds(140)), EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, _expandButton); Storyboard.SetTargetProperty(animation, "Opacity");
        _handleAnimation.Children.Add(animation); _handleAnimation.Begin();
    }

    private void ApplyColors()
    {
        if (_closing || _resourcesSuspended) return;
        bool dark = _root.ActualTheme == ElementTheme.Dark;
        var settings = App.Current.Settings.GetSideOptions(_side);
        var tint = dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252);
        // Tint and opacity update the connected material in place. Recreating
        // its compositor on every slider tick retains unnecessary render work.
        var key = settings.BackgroundStyle switch { "Solid" => "Solid", "Acrylic" => "Acrylic", _ => "Transparent" };
        if (_backdropKey != key)
        {
            var backdrop = key == "Solid" ? null : new DockBackdrop(key == "Acrylic", dark, settings.BackgroundOpacity);
            SystemBackdrop = backdrop;
            _ownedBackdrop = backdrop;
            _backdropKey = key;
        }
        _ownedBackdrop?.Update(dark, settings.BackgroundOpacity);
        _shell.Background = new SolidColorBrush(settings.BackgroundStyle == "Solid" || (settings.BackgroundStyle == "Image" && _backgroundImage.Source is null) ? tint : Colors.Transparent);
        _shell.BorderBrush = null;
        _backgroundImage.Visibility = settings.BackgroundStyle == "Image" ? Visibility.Visible : Visibility.Collapsed;
        if (settings.BackgroundStyle != "Image" && _backgroundPath is not null)
        {
            _backgroundPath = null; ++_backgroundVersion;
            _backgroundImage.Source = null;
        }
        _backgroundImage.Opacity = settings.BackgroundOpacity;
        _backgroundOverlay.Background = new SolidColorBrush(tint);
        _backgroundOverlay.Opacity = settings.BackgroundStyle switch
        {
            "Transparent" => settings.BackgroundOpacity,
            "Image" => settings.BackgroundImageOverlayOpacity,
            _ => 0
        };
        if (settings.BackgroundStyle == "Image" && _backgroundPath != settings.BackgroundImagePath)
        {
            _backgroundPath = settings.BackgroundImagePath;
            _ = LoadBackgroundAsync(_backgroundPath, ++_backgroundVersion);
        }
    }

    private async Task LoadBackgroundAsync(string path, int version)
    {
        ImageSource? source = null;
        try
        {
            source = await ShellIconService.LoadImageAsync(path, 2048);
        }
        catch { /* A removed background must not prevent access to shortcuts. */ }
        if (_closing || _resourcesSuspended || version != _backgroundVersion || App.Current.Settings.GetSideOptions(_side).BackgroundStyle != "Image") return;
        _backgroundImage.Source = source;
        if (source is null)
        {
            bool dark = _root.ActualTheme == ElementTheme.Dark;
            _shell.Background = new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252));
        }
        else _shell.Background = new SolidColorBrush(Colors.Transparent);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_closing || _resourcesSuspended || e.Handled) return;
        var point = e.GetCurrentPoint(_root);
        if (point.Properties.IsHorizontalMouseWheel) return;
        e.Handled = true;
        // The desktop quick-mode mouse hook selects items before XAML sees
        // the wheel. Do not also move the list underneath the selection.
        if (_quickMode) return;
        double delta = Math.Clamp(-point.Properties.MouseWheelDelta * .6, -432, 432);
        if (delta == 0) return;
        bool animate = !NativeMethods.SystemParametersInfo(NativeMethods.SpiGetClientAreaAnimation, 0, out bool enabled, 0) || enabled;
        if (_scroll.ScrollableHeight <= .5 || _primaryCycle is null)
        {
            if (animate) { _scrollBounce.Push(delta); StartScrolling(); }
            return;
        }
        if (!_scrollTimer.IsRunning) _scrollValue = _scrollTarget = _scroll.VerticalOffset;
        if (_repeatedCycles) _scrollTarget += delta;
        else
        {
            double target = _scrollTarget + delta;
            _scrollTarget = Math.Clamp(target, 0, _scroll.ScrollableHeight);
            if (target != _scrollTarget && animate) _scrollBounce.Push(delta * .55);
        }
        if (!animate)
        {
            _scrollValue = _scrollTarget = _repeatedCycles ? DockScrollLogic.Recenter(_scrollTarget, _cycleHeight) : _scrollTarget;
            _scroll.ChangeView(null, _scrollValue, null, true);
            return;
        }
        StartScrolling();
    }

    private StackPanel CreateCycle(bool repeating = false)
    {
        var cycle = new StackPanel { Spacing = _itemSpacing, HorizontalAlignment = HorizontalAlignment.Stretch };
        var list = App.Current.Items(_side);
        if (!_expanded)
        {
            int columns = App.Current.Settings.GetSideOptions(_side).NormalColumns;
            int count = DockLayoutLogic.CycleItemCount(list.Count, columns, repeating);
            for (int i = 0; i < count; i += columns)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = _itemSpacing, HorizontalAlignment = HorizontalAlignment.Left };
                for (int j = i; j < Math.Min(i + columns, count); j++) row.Children.Add(CreateTile(list[j % list.Count]));
                cycle.Children.Add(row);
            }
        }
        else
        {
            cycle.Orientation = Orientation.Horizontal;
            cycle.HorizontalAlignment = HorizontalAlignment.Left;
            for (int i = 0; i < list.Count; i += _expandedRows)
            {
                var column = new StackPanel { Spacing = _itemSpacing, Width = _tileColumnWidth, VerticalAlignment = VerticalAlignment.Top };
                for (int j = i; j < Math.Min(i + _expandedRows, list.Count); j++) column.Children.Add(CreateTile(list[j]));
                cycle.Children.Add(column);
            }
        }
        return cycle;
    }

    private void QueueScrollMetrics()
    {
        if (_closing || _resourcesSuspended || _metricsQueued) return;
        _metricsQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _metricsQueued = false;
            if (_closing || _resourcesSuspended || _primaryCycle is null) return;
            double height = _primaryCycle.ActualHeight;
            double viewport = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : _scroll.ActualHeight;
            if (height <= 0 || viewport <= 0) return;
            var settings = App.Current.Settings.GetSideOptions(_side);
            int count = App.Current.Items(_side).Count;
            int logicalRows = (int)Math.Ceiling((double)count / settings.NormalColumns);
            int visualRows = _primaryCycle.Children.Count;
            double logicalHeight = !_expanded && visualRows > 0 ? (height + _itemSpacing) * logicalRows / visualRows - _itemSpacing : height;
            bool repeat = !_expanded && settings.LoopScrolling && logicalHeight > viewport + .5;
            bool restore = _restoreScrollPending && App.Current.Settings.GetActiveList(_side).Id == _suspendedListId;
            if (!restore && repeat == _repeatedCycles && Math.Abs(_cycleHeight - (height + _itemSpacing)) < .1) return;
            double previousOffset = _scroll.VerticalOffset;
            double previousPhase = _repeatedCycles && _cycleHeight > 0
                ? (DockScrollLogic.Recenter(previousOffset, _cycleHeight) - _cycleHeight) / _cycleHeight
                : 0;
            bool previouslyRepeated = _repeatedCycles;
            StopScrolling();
            if (repeat != _repeatedCycles)
            {
                _items.Children.Clear();
                _tileButtons.Clear();
                _selectedVisualButton = null;
                _primaryCycle = CreateCycle(repeat);
                _primaryCycle.SizeChanged += (_, _) => QueueScrollMetrics();
                if (repeat)
                {
                    _items.Children.Add(CreateCycle(true));
                    _items.Children.Add(_primaryCycle);
                    _items.Children.Add(CreateCycle(true));
                }
                else _items.Children.Add(_primaryCycle);
                _repeatedCycles = repeat;
            }
            _scroll.UpdateLayout();
            _cycleHeight = _primaryCycle.ActualHeight + _itemSpacing;
            _scrollValue = _scrollTarget = repeat
                ? _cycleHeight * (1 + previousPhase)
                : Math.Clamp(previouslyRepeated ? previousPhase * _cycleHeight : previousOffset, 0, _scroll.ScrollableHeight);
            if (restore)
            {
                _scrollValue = _scrollTarget = repeat
                    ? DockScrollLogic.Recenter(_suspendedScrollOffset, _cycleHeight)
                    : Math.Clamp(_suspendedScrollOffset, 0, _scroll.ScrollableHeight);
                _restoreScrollPending = false;
            }
            _scroll.ChangeView(null, _scrollValue, null, true);
            UpdateQuickSelection();
            if (restore && _quickMode) ScrollToQuickSelection();
        });
    }

    private void StartScrolling()
    {
        if (_closing || _resourcesSuspended) return;
        if (_scrollTimer.IsRunning) return;
        _scrollTick = Environment.TickCount64;
        _scrollTimer.Start();
    }

    private void StopScrolling()
    {
        _scrollTimer.Stop();
        _scrollBounce.Reset();
        _scrollBounceTransform.Y = 0;
    }

    private void OnScrollTick(DispatcherQueueTimer sender, object args)
    {
        if (_closing || _resourcesSuspended || _interactions > 0) { StopScrolling(); return; }
        long now = Environment.TickCount64;
        double elapsed = Math.Clamp(now - _scrollTick, 1, 64);
        _scrollTick = now;
        if (!_repeatedCycles) _scrollTarget = Math.Clamp(_scrollTarget, 0, _scroll.ScrollableHeight);
        _scrollValue = DockScrollLogic.AnimateOffset(_scrollValue, _scrollTarget, elapsed);
        if (_repeatedCycles && _cycleHeight > 0)
        {
            double rebased = DockScrollLogic.Recenter(_scrollValue, _cycleHeight);
            double shift = _scrollValue - rebased;
            _scrollValue = rebased;
            _scrollTarget -= shift;
        }
        else _scrollValue = Math.Clamp(_scrollValue, 0, _scroll.ScrollableHeight);
        _scroll.ChangeView(null, _scrollValue, null, true);
        _scrollBounce.Update(elapsed);
        _scrollBounceTransform.Y = _scrollBounce.Value;
        if (_scrollValue == _scrollTarget && !_scrollBounce.IsRunning) StopScrolling();
    }

    private Button CreateTile(ShortcutItem item)
    {
        var settings = App.Current.Settings.GetSideOptions(_side);
        bool textOnly = settings.ShortcutDisplayMode == "Text";
        UIElement body = textOnly
            ? new TextBlock { Text = item.Name, FontSize = 12, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : CreateIconContent(item, settings);
        var button = new Button { Content = body, Width = _tileColumnWidth, MinWidth = 0, CanDrag = true, AllowDrop = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = textOnly ? new Thickness(6, 0, 6, 0) : new Thickness(1, 6, 1, 4), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Colors.Transparent), Background = new SolidColorBrush(Colors.Transparent) };
        if (textOnly) button.Height = DockLayoutLogic.TileHeight(settings, _tileColumnWidth);
        ToolTipService.SetToolTip(button, $"{item.Name}\n{item.TargetPath}");
        AutomationProperties.SetName(button, $"启动 {item.Name}");
        button.Click += (_, _) => { if (Environment.TickCount64 >= _suppressClickUntil && _shortcutDrag is null) LaunchItem(item); };
        _tileButtons.Add((button, item));
        button.DragStarting += (_, e) => StartShortcutDrag(item, e);
        button.DropCompleted += (_, e) => CompleteShortcutDrag(e);
        Windows.Foundation.Point pressedAt = default;
        bool pointerDown = false;
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            pointerDown = point.Properties.IsLeftButtonPressed;
            pressedAt = point.Position;
        }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pointerDown = false), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            if (!pointerDown || !point.Properties.IsLeftButtonPressed || _shortcutDrag is not null || _quickMode
                || Math.Abs(point.Position.X - pressedAt.X) + Math.Abs(point.Position.Y - pressedAt.Y) < 8) return;
            pointerDown = false;
            _suppressClickUntil = long.MaxValue;
            e.Handled = true;
            button.ReleasePointerCaptures();
            try { await button.StartDragAsync(point); }
            catch (Exception ex) { if (!_closing) App.Current.ReportError($"拖动失败：{ex.Message}"); }
            finally { EndShortcutDrag(); _suppressClickUntil = Environment.TickCount64 + 500; }
        }), true);
        button.DragOver += OnDragOver;
        button.Drop += (_, e) =>
        {
            if (!IsInternalDrag(e)) return;
            e.Handled = true; e.AcceptedOperation = DataPackageOperation.Move;
            DropShortcut(item);
        };
        button.ContextRequested += (_, e) =>
        {
            _itemMenu?.Hide();
            var menu = CreateItemMenu(item);
            _itemMenu = menu;
            menu.Closed += (_, _) => { if (ReferenceEquals(_itemMenu, menu)) _itemMenu = null; };
            if (e.TryGetPosition(button, out var position)) menu.ShowAt(button, new FlyoutShowOptions { Position = position });
            else menu.ShowAt(button);
            e.Handled = true;
        };
        return button;
    }

    private UIElement CreateIconContent(ShortcutItem item, DockOptions settings)
    {
        bool wide = settings.ShortcutDisplayMode == "Wide";
        var imageBrush = new ImageBrush { Stretch = wide && item.ImageCrop != "Fit" ? Stretch.UniformToFill : Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = item.ImageCrop switch { "Top" when wide => AlignmentY.Top, "Bottom" when wide => AlignmentY.Bottom, _ => AlignmentY.Center } };
        var icon = new Border { Background = imageBrush, CornerRadius = new CornerRadius(wide ? 8 : 0) };
        double iconHeight = DockLayoutLogic.IconHeight(settings, _tileColumnWidth);
        var fallback = new FontIcon { Glyph = Directory.Exists(item.TargetPath) ? "\uE8B7" : "\uE8A5", FontSize = Math.Min(26 * settings.IconSize / 40, iconHeight), Opacity = .8 };
        var iconGrid = new Grid { Height = iconHeight };
        if (!wide) { iconGrid.Width = iconHeight; iconGrid.HorizontalAlignment = HorizontalAlignment.Center; }
        iconGrid.Children.Add(fallback);
        iconGrid.Children.Add(icon);
        var body = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(iconGrid);
        if (settings.ShowShortcutNames) body.Children.Add(new TextBlock { Text = item.Name, FontSize = wide ? 12 : 10, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center });
        _ = LoadIconAsync();
        return body;

        async Task LoadIconAsync()
        {
            int version = _imageVersion;
            int size = Math.Clamp((int)Math.Ceiling(Math.Max(iconHeight, wide ? _tileColumnWidth - 8 : iconHeight) * _iconScale), 16, 512);
            string key = string.Join('\u001f', item.Id, size.ToString(), AssetStamp(item.CustomIconPath), AssetStamp(item.IconPath), AssetStamp(item.TargetPath));
            _usedImageKeys.Add(key);
            if (!_tileImages.TryGetValue(key, out var loading))
                _tileImages[key] = loading = ShellIconService.LoadShortcutAsync(item, size);
            var source = await loading;
            if (source is not null && !_closing && !_resourcesSuspended && version == _imageVersion) { imageBrush.ImageSource = source; fallback.Visibility = Visibility.Collapsed; }
        }
    }

    private static string AssetStamp(string path)
    {
        try
        {
            if (ShortcutIconMetadata.TryParse(path, out string asset, out _, out _))
                return path + ":" + File.GetLastWriteTimeUtc(asset).Ticks;
        }
        catch { }
        return path;
    }

    private MenuFlyout CreateItemMenu(ShortcutItem item)
    {
        var menu = new MenuFlyout();
        AddMenuItem(menu, "重命名…", async () => await RenameAsync(item));
        AddMenuItem(menu, "编辑路径和参数…", async () => await EditAsync(item));
        AddMenuItem(menu, "自定义图标与裁切…", async () => await EditAppearanceAsync(item));
        AddMenuItem(menu, "向上移动", () => Move(item, -1));
        AddMenuItem(menu, "向下移动", () => Move(item, 1));
        AddMenuItem(menu, _side == DockSide.Left ? "移到右侧边栏" : "移到左侧边栏", () =>
        {
            App.Current.Items(_side).Remove(item);
            App.Current.Items(_side == DockSide.Left ? DockSide.Right : DockSide.Left).Add(item);
            App.Current.Settings.GetActiveList(_side == DockSide.Left ? DockSide.Right : DockSide.Left).SortKey = ShortcutSortKey.Manual;
            if (_side == DockSide.Left) App.Current.Settings.RightEnabled = true; else App.Current.Settings.LeftEnabled = true;
            SaveOrReport(allSides: true);
        });
        var otherLists = App.Current.Settings.GetLists(_side).Where(x => x.Id != App.Current.Settings.GetActiveList(_side).Id).ToList();
        if (otherLists.Count > 0)
        {
            var moveList = new MenuFlyoutSubItem { Text = "移到其他列表" };
            foreach (var destination in otherLists)
            {
                var moveItem = new MenuFlyoutItem { Text = destination.Name };
                moveItem.Click += (_, _) => { if (App.Current.Items(_side).Remove(item)) destination.Shortcuts.Add(item); destination.SortKey = ShortcutSortKey.Manual; SaveOrReport(); };
                moveList.Items.Add(moveItem);
            }
            menu.Items.Add(moveList);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "从当前列表移除", () => { App.Current.Items(_side).Remove(item); SaveOrReport(); });
        GuardFlyout(menu);
        return menu;
    }

    private void GuardFlyout(FlyoutBase flyout)
    {
        flyout.Opened += (_, _) => _interactions++;
        flyout.Closed += (_, _) => _interactions = Math.Max(0, _interactions - 1);
    }

    private static void AddMenuItem(MenuFlyout flyout, string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        flyout.Items.Add(item);
    }

    private void Move(ShortcutItem item, int delta)
    {
        var list = App.Current.Items(_side);
        int index = list.IndexOf(item), next = index + delta;
        if (next < 0 || next >= list.Count) return;
        list.RemoveAt(index);
        list.Insert(next, item);
        App.Current.Settings.GetActiveList(_side).SortKey = ShortcutSortKey.Manual;
        SaveOrReport();
    }

    private sealed class ShortcutDrag
    {
        public required DockWindow Source { get; init; }
        public required DockList List { get; init; }
        public required ShortcutItem Item { get; init; }
        public required DispatcherQueueTimer Timer { get; init; }
        public bool Handled { get; set; }
        public bool Cancelled { get; set; }
    }

    private static bool IsInternalDrag(DragEventArgs e) => _shortcutDrag is not null && e.DataView.Contains(InternalDragFormat);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool internalDrag = IsInternalDrag(e);
        bool supported = internalDrag || e.DataView.Contains(StandardDataFormats.StorageItems)
            || e.DataView.Contains(StandardDataFormats.WebLink) || e.DataView.Contains(StandardDataFormats.Text);
        e.AcceptedOperation = supported ? (internalDrag ? DataPackageOperation.Move : DataPackageOperation.Copy) : DataPackageOperation.None;
        e.DragUIOverride.Caption = internalDrag ? "拖到图标上交换位置；拖出列表移到桌面（Esc 取消）" : "添加到当前列表";
        e.Handled = true;
    }

    private void StartShortcutDrag(ShortcutItem item, DragStartingEventArgs e)
    {
        if (_shortcutDrag is not null || _closing || _quickMode) { e.Cancel = true; return; }
        StopScrolling();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        var drag = new ShortcutDrag { Source = this, List = App.Current.Settings.GetActiveList(_side), Item = item, Timer = timer };
        _shortcutDrag = drag; _interactions++;
        _suppressClickUntil = long.MaxValue;
        // Clear old transitions before monitoring cancellation of this gesture.
        NativeMethods.GetAsyncKeyState(0x1B); NativeMethods.GetAsyncKeyState(0x02);
        timer.Tick += (_, _) => { if (NativeMethods.GetAsyncKeyState(0x1B) != 0 || NativeMethods.GetAsyncKeyState(0x02) != 0) drag.Cancelled = true; };
        timer.Start();
        try
        {
            e.Data.SetData(InternalDragFormat, item.Id);
            e.Data.RequestedOperation = DataPackageOperation.Move;
            e.AllowedOperations = DataPackageOperation.Move;
            ToolTipService.SetToolTip(_scroll, "拖到图标上交换位置；拖出列表移到桌面；Esc 取消");
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            EndShortcutDrag();
            App.Current.ReportError($"拖动失败：{ex.Message}");
        }
    }

    private void DropShortcut(ShortcutItem? target)
    {
        var drag = _shortcutDrag;
        if (drag is null || drag.Cancelled || drag.Handled) return;
        drag.Handled = true;
        var destination = App.Current.Settings.GetActiveList(_side);
        int sourceIndex = drag.List.Shortcuts.IndexOf(drag.Item);
        if (sourceIndex < 0) return;
        if (destination == drag.List)
        {
            int targetIndex = target is null ? -1 : destination.Shortcuts.IndexOf(target);
            if (targetIndex < 0 || targetIndex == sourceIndex) return;
            (destination.Shortcuts[sourceIndex], destination.Shortcuts[targetIndex]) = (destination.Shortcuts[targetIndex], destination.Shortcuts[sourceIndex]);
        }
        else
        {
            drag.List.Shortcuts.RemoveAt(sourceIndex);
            int index = target is null ? destination.Shortcuts.Count : destination.Shortcuts.IndexOf(target);
            destination.Shortcuts.Insert(Math.Max(0, index), drag.Item);
            drag.List.SortKey = ShortcutSortKey.Manual;
        }
        destination.SortKey = ShortcutSortKey.Manual;
        SaveOrReport(allSides: drag.Source._side != _side);
    }

    private static void RemoveDraggedShortcut()
    {
        var drag = _shortcutDrag;
        if (drag is null || drag.Cancelled || drag.Handled) return;
        drag.Handled = true;
        int index = drag.List.Shortcuts.IndexOf(drag.Item);
        if (index < 0) return;
        var oldSortKey = drag.List.SortKey;
        try
        {
            using var placement = DesktopShortcutRestore.Prepare(drag.Item);
            drag.List.Shortcuts.RemoveAt(index);
            drag.List.SortKey = ShortcutSortKey.Manual;
            if (drag.Source.SaveOrReport()) placement.Commit();
            else
            {
                drag.List.Shortcuts.Insert(Math.Min(index, drag.List.Shortcuts.Count), drag.Item);
                drag.List.SortKey = oldSortKey;
            }
        }
        catch (Exception ex)
        {
            App.Current.ReportError($"无法移到桌面，边栏快捷方式已保留：{ex.Message}");
        }
    }

    private bool IsPointerWithinList()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!NativeMethods.GetCursorPos(out var point) || !NativeMethods.GetWindowRect(hwnd, out var rect)) return true;
        double scale = Math.Max(96, NativeMethods.GetDpiForWindow(hwnd)) / 96d;
        var origin = _scroll.TransformToVisual(_root).TransformPoint(default);
        double x = (point.X - rect.Left) / scale - origin.X;
        double y = (point.Y - rect.Top) / scale - origin.Y;
        return x >= 0 && x < _scroll.ActualWidth && y >= 0 && y < _scroll.ActualHeight;
    }

    private void CompleteShortcutDrag(DropCompletedEventArgs e)
    {
        var drag = _shortcutDrag;
        if (drag is null || drag.Source != this) return;
        drag.Timer.Stop();
        drag.Cancelled |= NativeMethods.GetAsyncKeyState(0x1B) != 0 || NativeMethods.GetAsyncKeyState(0x02) != 0;
        try
        {
            if (!drag.Handled && !drag.Cancelled && e.DropResult == DataPackageOperation.None && !IsPointerWithinList()) RemoveDraggedShortcut();
        }
        finally { EndShortcutDrag(); }
    }

    private void EndShortcutDrag()
    {
        if (_shortcutDrag is not { } drag || drag.Source != this) return;
        drag.Timer.Stop();
        _shortcutDrag = null;
        _interactions = Math.Max(0, _interactions - 1);
        _suppressClickUntil = Environment.TickCount64 + 500;
        ToolTipService.SetToolTip(_scroll, null);
    }

    private bool SaveOrReport(bool allSides = false)
    {
        if (App.Current.SaveSettings(allSides ? null : _side)) return true;
        App.Current.ReportError(App.Current.LastError ?? "保存失败");
        return false;
    }

    private async Task AddPathsAsync(IEnumerable<string> paths, bool desktopDrop = false)
    {
        var errors = new List<string>();
        var active = App.Current.Settings.GetActiveList(_side);
        var list = active.Shortcuts;
        var transfers = new List<(DesktopShortcutTransfer.PendingTransfer Transfer, bool RemoveDesktop)>();
        var addedItems = new List<ShortcutItem>();
        foreach (var path in paths)
        {
            try
            {
                var item = ShortcutService.ImportPath(path);
                var existing = list.FirstOrDefault(x => ManagedShortcutStorage.WasImportedFrom(x, item.TargetPath)
                    && x.Arguments == item.Arguments);
                if (existing is not null)
                {
                    if (!ManagedShortcutStorage.RequiresIndependentCopy(existing)
                        || ManagedShortcutStorage.HasIndependentCopy(existing, App.Current.ConfigurationDirectory)) continue;
                    // Reimporting the same source repairs an older or missing link,
                    // preserving its position, name and custom appearance.
                    item = existing;
                }
                DesktopShortcutTransfer.PendingTransfer? transfer = null;
                bool removeDesktop = false;
                if (desktopDrop && App.Current.Settings.RemoveDesktopShortcutAfterImport)
                {
                    transfer = DesktopShortcutTransfer.Prepare(item, App.Current.ConfigurationDirectory);
                    removeDesktop = transfer is not null;
                }
                transfer ??= ManagedShortcutStorage.Prepare(item, App.Current.ConfigurationDirectory);
                if (transfer is not null) transfers.Add((transfer, removeDesktop));
                if (existing is null)
                {
                    list.Add(item);
                    addedItems.Add(item);
                }
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        bool saved = SaveOrReport();
        foreach (var (transfer, removeDesktop) in transfers)
        {
            if (saved)
            {
                if (removeDesktop && !transfer.CommitDelete()) errors.Add(transfer.LastError ?? "桌面原快捷方式未能移除，已保留原件。");
                else if (!removeDesktop) transfer.CommitKeep();
            }
            transfer.Dispose();
        }
        if (!saved)
        {
            foreach (var item in addedItems) list.Remove(item);
            Refresh();
            return;
        }
        if (active.SortKey != ShortcutSortKey.Manual && App.Current.Settings.GetActiveList(_side) == active)
            await SortCurrentAsync(active.SortKey, active.SortDescending);
        if (errors.Count > 0) App.Current.ReportError(string.Join("\n", errors));
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        _interactions++;
        try
        {
            e.Handled = true;
            if (IsInternalDrag(e))
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                if (!IsPointerWithinList()) RemoveDraggedShortcut();
                else DropShortcut(null);
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems)) await AddPathsAsync((await e.DataView.GetStorageItemsAsync()).Select(x => x.Path), desktopDrop: true);
            else if (e.DataView.Contains(StandardDataFormats.WebLink)) await AddPathsAsync(new[] { (await e.DataView.GetWebLinkAsync()).AbsoluteUri });
            else if (e.DataView.Contains(StandardDataFormats.Text)) await AddPathsAsync(new[] { (await e.DataView.GetTextAsync()).Trim() });
        }
        catch (Exception ex) { App.Current.ReportError($"添加失败：{ex.Message}"); }
        finally { _interactions--; deferral.Complete(); }
    }

    private async Task PickFilesAsync()
    {
        _interactions++;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop, ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            await AddPathsAsync((await picker.PickMultipleFilesAsync()).Select(x => x.Path));
        }
        catch (Exception ex) { App.Current.ReportError($"文件选择失败：{ex.Message}"); }
        finally { _interactions--; }
    }

    private async Task PickFolderAsync()
    {
        _interactions++;
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) await AddPathsAsync(new[] { folder.Path });
        }
        catch (Exception ex) { App.Current.ReportError($"文件夹选择失败：{ex.Message}"); }
        finally { _interactions--; }
    }

    private async Task AddLocationAsync()
    {
        var field = new TextBox { PlaceholderText = @"C:\Windows\notepad.exe 或 https://example.com", MinWidth = 320 };
        string location = "";
        var result = await DialogAsync("添加快捷方式", field, "添加", () => location = field.Text);
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(location)) await AddPathsAsync(new[] { location });
    }

    private async Task RenameAsync(ShortcutItem item)
    {
        var field = new TextBox { Text = item.Name, MinWidth = 280, MaxLength = 80 };
        string newName = "";
        if (await DialogAsync("重命名快捷方式", field, "保存", () => newName = field.Text) == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(newName)) { item.Name = newName.Trim(); SaveOrReport(); }
    }

    private async Task EditAsync(ShortcutItem item)
    {
        var name = new TextBox { Header = "名称", Text = item.Name, MaxLength = 80 };
        var path = new TextBox { Header = "目标路径或网址", Text = item.TargetPath };
        var args = new TextBox { Header = "启动参数", Text = item.Arguments ?? "" };
        var work = new TextBox { Header = "工作目录（可选）", Text = item.WorkingDirectory ?? "" };
        var fields = new StackPanel { Spacing = 12, MinWidth = 360 };
        fields.Children.Add(name); fields.Children.Add(path); fields.Children.Add(args); fields.Children.Add(work);
        string savedName = "", savedPath = "", savedArguments = "", savedWork = "";
        if (await DialogAsync("编辑快捷方式", fields, "保存", () =>
        {
            savedName = name.Text; savedPath = path.Text; savedArguments = args.Text; savedWork = work.Text;
        }) != ContentDialogResult.Primary) return;
        try
        {
            var validated = ShortcutService.ImportPath(savedPath);
            validated.Name = string.IsNullOrWhiteSpace(savedName) ? validated.Name : savedName.Trim();
            validated.Arguments = savedArguments; validated.WorkingDirectory = savedWork;
            validated.CustomIconPath = item.CustomIconPath;
            if (string.Equals(validated.TargetPath, item.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                validated.ImportedShortcutPath = item.ImportedShortcutPath;
                validated.ImportedDesktopShortcutPath = item.ImportedDesktopShortcutPath;
            }
            using var transfer = ManagedShortcutStorage.Prepare(validated, App.Current.ConfigurationDirectory);
            var old = new ShortcutItem { Name = item.Name, TargetPath = item.TargetPath, IconPath = item.IconPath,
                CustomIconPath = item.CustomIconPath, Arguments = item.Arguments ?? string.Empty, WorkingDirectory = item.WorkingDirectory ?? string.Empty,
                ImportedShortcutPath = item.ImportedShortcutPath, ImportedDesktopShortcutPath = item.ImportedDesktopShortcutPath };
            item.Name = validated.Name; item.TargetPath = validated.TargetPath; item.IconPath = validated.IconPath;
            item.CustomIconPath = validated.CustomIconPath; item.Arguments = validated.Arguments; item.WorkingDirectory = validated.WorkingDirectory;
            item.ImportedShortcutPath = validated.ImportedShortcutPath; item.ImportedDesktopShortcutPath = validated.ImportedDesktopShortcutPath;
            if (SaveOrReport()) transfer?.CommitKeep();
            else
            {
                item.Name = old.Name; item.TargetPath = old.TargetPath; item.IconPath = old.IconPath;
                item.CustomIconPath = old.CustomIconPath; item.Arguments = old.Arguments; item.WorkingDirectory = old.WorkingDirectory;
                item.ImportedShortcutPath = old.ImportedShortcutPath; item.ImportedDesktopShortcutPath = old.ImportedDesktopShortcutPath;
                Refresh();
            }
        }
        catch (Exception ex) { App.Current.ReportError(ex.Message); }
    }

    private async Task EditAppearanceAsync(ShortcutItem item)
    {
        _interactions++;
        ShortcutEditorWindow? editor = null;
        bool previewAlive = true;
        int previewVersion = 0;
        string validPath = string.Empty, savedPath = string.Empty, savedCrop = item.ImageCrop;
        var path = new TextBox { Header = "自定义图标或图片（留空使用原图标）", Text = item.CustomIconPath, PlaceholderText = "图片 / .ico / .exe / .dll / .lnk" };
        var crop = new ComboBox { Header = "矩形模式的裁切位置", ItemsSource = new[] { "上部", "居中（默认）", "下部", "完整显示，不裁切" }, SelectedIndex = item.ImageCrop switch { "Top" => 0, "Bottom" => 2, "Fit" => 3, _ => 1 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var brush = new ImageBrush { Stretch = Stretch.UniformToFill };
        var preview = new Border { Height = 126, Width = 210, Background = brush, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left };
        var error = new TextBlock { Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 196, 43, 50)), TextWrapping = TextWrapping.Wrap };
        var browse = new Button { Content = "选择图标或图片…" };
        var reset = new Button { Content = "恢复原图标" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(browse); actions.Children.Add(reset);
        var fields = new StackPanel { Spacing = 12, MinWidth = 360 };
        fields.Children.Add(path); fields.Children.Add(actions); fields.Children.Add(crop);
        fields.Children.Add(new TextBlock { Text = "矩形模式预览", Opacity = .7 }); fields.Children.Add(preview);
        fields.Children.Add(new TextBlock { Text = "原快捷方式图标会自动保留。矩形模式在设置中切换；每个项目可使用独立图片和裁切位置。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 });
        fields.Children.Add(error);

        void UpdateCrop()
        {
            brush.Stretch = crop.SelectedIndex == 3 ? Stretch.Uniform : Stretch.UniformToFill;
            brush.AlignmentX = AlignmentX.Center;
            brush.AlignmentY = crop.SelectedIndex switch { 0 => AlignmentY.Top, 2 => AlignmentY.Bottom, _ => AlignmentY.Center };
        }
        async Task UpdatePreviewAsync()
        {
            int version = ++previewVersion;
            string candidate = Environment.ExpandEnvironmentVariables(path.Text.Trim().Trim('"'));
            validPath = string.Empty;
            error.Text = candidate.Length == 0 ? string.Empty : "正在读取图标…";
            var source = candidate.Length == 0
                ? await ShellIconService.LoadShortcutAsync(new ShortcutItem { TargetPath = item.TargetPath, IconPath = item.IconPath }, 256)
                : await ShellIconService.LoadAsync(candidate, 256);
            if (!previewAlive || version != previewVersion) return;
            brush.ImageSource = source;
            validPath = source is not null ? candidate : string.Empty;
            error.Text = source is null && candidate.Length > 0 ? "无法读取该图标。请选择有效图片、ICO、EXE、DLL 或快捷方式。" : string.Empty;
        }
        path.TextChanged += async (_, _) => await UpdatePreviewAsync();
        crop.SelectionChanged += (_, _) => UpdateCrop();
        reset.Click += (_, _) => path.Text = string.Empty;
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
                foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".exe", ".dll", ".lnk", ".url" }) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(editor!));
                var file = await picker.PickSingleFileAsync();
                if (file is not null && previewAlive) path.Text = file.Path;
            }
            catch (Exception ex) { if (previewAlive) error.Text = $"选择失败：{ex.Message}"; }
        };
        try
        {
            editor = new ShortcutEditorWindow(this, "自定义图标与裁切", fields, "保存", _root.RequestedTheme, () =>
            {
                savedPath = Environment.ExpandEnvironmentVariables(path.Text.Trim().Trim('"'));
                savedCrop = crop.SelectedIndex switch { 1 => "Center", 2 => "Bottom", 3 => "Fit", _ => "Top" };
            }, () =>
            {
                string candidate = Environment.ExpandEnvironmentVariables(path.Text.Trim().Trim('"'));
                if (candidate.Length == 0 || candidate == validPath) return true;
                error.Text = "请等待预览成功，或选择可读取的图标。"; return false;
            });
            editor.Closed += (_, _) => { previewAlive = false; ++previewVersion; };
            UpdateCrop();
            _ = UpdatePreviewAsync();
            if (await editor.ShowAsync() == ContentDialogResult.Primary)
            {
                item.CustomIconPath = savedPath; item.ImageCrop = savedCrop; SaveOrReport();
            }
        }
        catch (Exception ex) { App.Current.ReportError($"无法编辑图标：{ex.Message}"); }
        finally { previewAlive = false; _interactions--; }
    }

    private async Task<ContentDialogResult> DialogAsync(string title, UIElement content, string primary, Action? onPrimary = null)
    {
        _interactions++;
        try
        {
            var editor = new ShortcutEditorWindow(this, title, content, primary, _root.RequestedTheme, onPrimary);
            var result = await editor.ShowAsync();
            return result;
        }
        catch (Exception ex) { App.Current.ReportError($"无法显示对话框：{ex.Message}"); return ContentDialogResult.None; }
        finally { _interactions--; }
    }

    public void Shutdown() { if (!_closing) { _closing = true; _edge.Dispose(); Close(); } }
}
