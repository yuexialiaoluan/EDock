using EDock.Interop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace EDock;

// A dock is only 88 DIPs wide. A separate window keeps editors usable at all scales.
public sealed class ShortcutEditorWindow : Window
{
    private static readonly HashSet<ShortcutEditorWindow> OpenWindows = new();
    private readonly TaskCompletionSource<ContentDialogResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ContentDialogResult _result;
    private readonly UIElement _editor;
    private readonly Action? _onPrimary;
    private readonly Func<bool>? _validatePrimary;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private bool _closeRequested;
    private bool _closed;

    public ShortcutEditorWindow(Window owner, string title, UIElement content, string primary, ElementTheme theme, Action? onPrimary = null, Func<bool>? validatePrimary = null)
    {
        Title = $"EDock · {title}";
        App.SetWindowIcon(this);
        _editor = content;
        _onPrimary = onPrimary;
        _validatePrimary = validatePrimary;
        _dispatcher = DispatcherQueue;
        var root = new Grid { Padding = new Thickness(28), RowSpacing = 24, RequestedTheme = theme };
        root.ActualThemeChanged += (_, _) => root.Background = new SolidColorBrush(root.ActualTheme == ElementTheme.Dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252));
        root.Background = new SolidColorBrush(root.ActualTheme == ElementTheme.Dark ? ColorHelper.FromArgb(255, 25, 29, 39) : ColorHelper.FromArgb(255, 246, 248, 252));
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消" };
        var save = new Button { Content = primary, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        cancel.Click += (_, _) => RequestClose(ContentDialogResult.None);
        save.Click += (_, _) => RequestClose(ContentDialogResult.Primary);
        buttons.Children.Add(cancel); buttons.Children.Add(save); Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { e.Handled = true; RequestClose(ContentDialogResult.None); } };
        Content = root;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        if (appWindow.Presenter is OverlappedPresenter presenter) { presenter.IsMaximizable = false; presenter.IsMinimizable = false; }
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = Math.Max(1, NativeMethods.GetDpiForWindow(hwnd) / 96d);
        int width = Math.Min(area.Width, (int)(560 * scale));
        int height = Math.Min(area.Height, (int)((content is StackPanel ? 560 : 280) * scale));
        appWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        Closed += OnClosed;
        root.Loaded += (_, _) =>
        {
            if (_editor is Control control) control.Focus(FocusState.Programmatic);
            else if (_editor is Panel panel && panel.Children.FirstOrDefault() is Control first) first.Focus(FocusState.Programmatic);
        };
    }

    public Task<ContentDialogResult> ShowAsync()
    {
        // Native ownership alone does not keep the managed WinUI Window alive.
        OpenWindows.Add(this);
        try { Activate(); }
        catch { OpenWindows.Remove(this); throw; }
        return _completion.Task;
    }

    private void RequestClose(ContentDialogResult result)
    {
        if (_closeRequested || _closed) return;
        if (result == ContentDialogResult.Primary && _validatePrimary is not null && !_validatePrimary()) return;
        // Snapshot editor fields while their XAML tree is still alive. Callers
        // must not read controls after awaiting the closed window.
        if (result == ContentDialogResult.Primary) _onPrimary?.Invoke();
        _closeRequested = true;
        _result = result;
        // Let the button/key/automation event unwind before tearing down its XAML tree.
        if (!_dispatcher.TryEnqueue(CloseDeferred)) CloseDeferred();
    }

    private void CloseDeferred()
    {
        if (_closed) return;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        Closed -= OnClosed;
        // Callers rebuild the dock after awaiting this task. Run them only after
        // the native Closed callback and its XAML teardown have returned.
        if (!_dispatcher.TryEnqueue(Complete)) Complete();
    }

    private void Complete()
    {
        _completion.TrySetResult(_result);
        OpenWindows.Remove(this);
    }

}
