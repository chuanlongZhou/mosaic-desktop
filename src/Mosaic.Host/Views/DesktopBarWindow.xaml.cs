using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ManagedShell.WindowsTray;
using Mosaic.Core.Commands;
using Mosaic.Host.Services;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Views;

public partial class DesktopBarWindow : Window
{
    private readonly AppServices _services;
    private readonly Func<CommandRequest, Task> _execute;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Forms.Screen _screen;
    private AppBarReservation? _reservation;
    private ContextMenu? _menu;
    private SystemTrayService? _systemTray;
    private Popup? _statusPopup;
    private nint _inputTarget;
    private bool HasOpenMenu => _menu?.IsOpen == true || _statusPopup?.IsOpen == true || SystemTrayService.IsNativeMenuOpen();
    private bool? _autoHide;
    private bool _autoHideAllowed;
    private bool? _targetCollapsed;
    private bool _closing;
    private int _transitionVersion;
    private readonly TranslateTransform _slide = new();
    private static readonly DependencyProperty RevealProperty = DependencyProperty.Register("Reveal", typeof(double), typeof(DesktopBarWindow),
        new PropertyMetadata(0.0, (target, args) => ((DesktopBarWindow)target).ApplyReveal((double)args.NewValue)));
    private double Reveal => (double)GetValue(RevealProperty);
    private bool? _windowsKeyPinned;
    public void SetWindowsKeyPinned(bool pinned)
    {
        _windowsKeyPinned = pinned;
        _hideTimer.Stop();
        SetCollapsed(!pinned && CanAutoHide);
        if (pinned) EnsureTopmost();
    }
    private bool CanAutoHide => _windowsKeyPinned is bool pinned ? !pinned : _autoHide == true && _autoHideAllowed;
    public bool IsAutoHidden { get; private set; }
    public bool IsTransitioning { get; private set; }
    public double ReservedHeight { get; private set; }
    public bool ReservesWorkArea => ReservedHeight > 0;
    public event Action? WorkAreaChanged;
    public void SetAutoHideAllowed(bool allowed)
    {
        var changed = _autoHideAllowed != allowed;
        _autoHideAllowed = allowed;
        if (!IsLoaded) return;
        if (!CanAutoHide) { _hideTimer.Stop(); SetCollapsed(false); }
        else if (changed && !IsMouseOver && !HasOpenMenu) { _hideTimer.Stop(); _hideTimer.Start(); }
    }
    public DesktopBarWindow(AppServices services, Func<CommandRequest, Task> execute, Forms.Screen screen, bool hostNotificationArea = false)
    {
        _services = services; _execute = execute; _screen = screen; InitializeComponent();
        BarContent.RenderTransform = _slide;
        UiEffects.MotionPreferenceChanged += OnMotionPreferenceChanged;
        Loaded += (_, _) =>
        {
            RefreshConfiguration(); UpdateTime();
            if (hostNotificationArea && _systemTray is null)
            {
                _systemTray = new SystemTrayService();
                PinnedTrayIcons.ItemsSource = _systemTray.Area.PinnedIcons;
                StatusArea.LayoutUpdated += (_, _) => _systemTray?.SetPlacement(StatusArea);
                UpdateTime();
            }
        };
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, true, true);
        _timer.Tick += (_, _) => UpdateTime(); _timer.Start();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (CanAutoHide && !IsMouseOver && !HasOpenMenu) SetCollapsed(true);
            else if (CanAutoHide) _hideTimer.Start();
        };
        _edgeTimer.Tick += (_, _) =>
        {
            if (!CanAutoHide || _targetCollapsed != true) return;
            var cursor = Forms.Cursor.Position; var bounds = _screen.Bounds;
            if (cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Top + 6) SetCollapsed(false);
        };
        _edgeTimer.Start();
        Closed += (_, _) =>
        {
            _closing = true; _transitionVersion++;
            UiEffects.MotionPreferenceChanged -= OnMotionPreferenceChanged;
            BeginAnimation(RevealProperty, null); IsTransitioning = false;
            if (_statusPopup is not null) _statusPopup.IsOpen = false;
            _systemTray?.Dispose(); _timer.Stop(); _hideTimer.Stop(); _edgeTimer.Stop();
            ReleaseWorkArea();
        };
    }
    public void RefreshConfiguration()
    {
        if (!IsLoaded) return;
        var pinned = !_services.Settings.Desktop.MenuBarAutoHide;
        PinMenuButton.Content = pinned ? "已固定" : "固定";
        PinMenuButton.SetResourceReference(ForegroundProperty, pinned ? "AccentBrush" : "MutedBrush");
        if (pinned) PinMenuButton.SetResourceReference(BackgroundProperty, "SelectionBrush"); else PinMenuButton.Background = Brushes.Transparent;
        PinMenuButton.ToolTip = (pinned ? "取消固定，恢复自动隐藏" : "固定顶部菜单，保持显示并预留空间") + " · " + NativeShell.DescribeHotkey(_services.Settings.Shortcuts.PinMenuBar);
        if (_autoHide == _services.Settings.Desktop.MenuBarAutoHide) return;
        _autoHide = _services.Settings.Desktop.MenuBarAutoHide;
        _hideTimer.Stop();
        SetCollapsed(CanAutoHide && !IsMouseOver && !HasOpenMenu);
    }
    private void ReleaseWorkArea()
    {
        if (_reservation is null) return;
        var reservation = _reservation; _reservation = null;
        ReservedHeight = 0;
        reservation.Dispose();
        WorkAreaChanged?.Invoke();
    }
    private void SetCollapsed(bool collapsed)
    {
        collapsed &= CanAutoHide;
        if (_closing) return;
        if (_targetCollapsed == collapsed)
        {
            if (!collapsed && (!IsVisible || WindowState == WindowState.Minimized)) ApplyReveal(Reveal);
            return;
        }
        _targetCollapsed = collapsed;
        var target = collapsed ? 0.0 : 1.0;
        // Preserve the current animated value before replacing its clock. A rapid
        // hover/desktop change reverses from this position instead of jumping.
        var current = Reveal;
        SetValue(RevealProperty, current);
        BeginAnimation(RevealProperty, null);
        var version = ++_transitionVersion;
        IsAutoHidden = false;
        if (!UiEffects.Enabled || Math.Abs(current - target) < .001) FinishReveal(target);
        else
        {
            IsTransitioning = true;
            ApplyReveal(current);
            var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds((collapsed ? 220 : 280) * Math.Abs(target - current)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            animation.Completed += (_, _) => { if (!_closing && version == _transitionVersion) FinishReveal(target); };
            BeginAnimation(RevealProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        if (!collapsed && CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); }
    }
    private void OnMotionPreferenceChanged()
    {
        if (!UiEffects.Enabled && IsTransitioning && !_closing)
        {
            _transitionVersion++;
            FinishReveal(_targetCollapsed == true ? 0 : 1);
        }
    }
    private void FinishReveal(double target)
    {
        SetValue(RevealProperty, target);
        BeginAnimation(RevealProperty, null);
        IsTransitioning = false;
        ApplyReveal(target);
        IsAutoHidden = target == 0;
    }
    private void ApplyReveal(double progress)
    {
        if (_closing || !IsLoaded) return;
        var height = ShellWorkArea.BarHeight * Math.Clamp(progress, 0, 1);
        var handle = new WindowInteropHelper(this).Handle;
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
        var bounds = _screen.Bounds;
        if (height == 0 && _targetCollapsed == true)
        {
            Hide();
            Height = 1 / scale;
            _slide.Y = -ShellWorkArea.BarHeight;
            SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top, bounds.Width, 1, 0x10);
            BarContent.Visibility = Visibility.Collapsed;
            ReleaseWorkArea();
            return;
        }
        // Clip a full-size bar while sliding it down from above the monitor. Its
        // visible bottom and the real Windows work-area boundary move together.
        Height = Math.Max(1 / scale, height);
        _slide.Y = height - ShellWorkArea.BarHeight;
        BarContent.Visibility = Visibility.Visible;
        if (_reservation is null) _reservation = new AppBarReservation(handle, bounds, height, moveWindow: false);
        else _reservation.SetHeight(height);
        if (ReservedHeight != height)
        {
            ReservedHeight = height;
            WorkAreaChanged?.Invoke();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();
        SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top, bounds.Width, Math.Max(1, (int)Math.Ceiling(height * scale)), 0x50);
    }
    private void OnMouseEnter(object sender, MouseEventArgs e) { _hideTimer.Stop(); if (_targetCollapsed == true) SetCollapsed(false); }
    private void OnMouseLeave(object sender, MouseEventArgs e) { if (CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); } }
    private void UpdateTime()
    {
        DateTimeLabel.Text = DateTime.Now.ToString("M月d日 ddd HH:mm", CultureInfo.GetCultureInfo("zh-CN"));
        WorkspaceLabel.Text = _services.Workspace.Name;
        var foreground = NativeShell.CaptureForeground();
        GetWindowThreadProcessId(foreground, out var process);
        if (process != Environment.ProcessId) _inputTarget = foreground;
        InputLanguageButton.Content = SystemStatus.InputLanguage(_inputTarget);
        InputLanguageButton.ToolTip = "当前输入语言；点击切换键盘布局";
        var status = SystemStatus.Read();
        NetworkButton.Content = status.NetworkGlyph; NetworkButton.ToolTip = status.NetworkDescription;
        VolumeButton.Content = status.VolumeGlyph; VolumeButton.ToolTip = status.VolumeDescription;
        BatteryButton.Content = status.BatteryText; BatteryButton.ToolTip = status.BatteryDescription;
        BatteryButton.Visibility = status.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        bool HasIcon(string id) => _systemTray?.Area.PinnedIcons?.Cast<NotifyIcon>().Any(icon => icon.GUID.ToString().Equals(id, StringComparison.OrdinalIgnoreCase)) == true;
        NetworkButton.Visibility = HasIcon(NotificationArea.NETWORK_GUID) ? Visibility.Collapsed : Visibility.Visible;
        VolumeButton.Visibility = HasIcon(NotificationArea.VOLUME_GUID) ? Visibility.Collapsed : Visibility.Visible;
        HiddenIconsButton.Visibility = _systemTray is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OpenStatusPopup(FrameworkElement target, UIElement content)
    {
        if (_statusPopup?.IsOpen == true) { _statusPopup.IsOpen = false; return; }
        SetCollapsed(false);
        _hideTimer.Stop();
        _statusPopup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = (System.Windows.Media.Brush)FindResource("PanelBrush"), BorderBrush = System.Windows.Media.Brushes.SlateGray,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = content } };
        _statusPopup.Closed += (_, _) => { if (CanAutoHide) _hideTimer.Start(); };
        _statusPopup.IsOpen = true;
    }
    private void OnHiddenIconsClick(object sender, RoutedEventArgs e)
    {
        if (_systemTray is null) return;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "后台应用", Margin = new Thickness(4, 0, 0, 8) });
        var icons = new ItemsControl { ItemsSource = _systemTray.Area.UnpinnedIcons, ItemTemplate = (DataTemplate)FindResource("TrayIconTemplate"),
            Width = 224, ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel))) };
        panel.Children.Add(new ScrollViewer { Content = icons, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var empty = new TextBlock { Text = _systemTray.Area.IsFailed ? "托盘服务未能启动，请切回 Windows 后重试。" : "暂无后台应用图标", Margin = new Thickness(4) };
        empty.Style = new Style(typeof(TextBlock))
        {
            Setters = { new Setter(VisibilityProperty, Visibility.Collapsed) },
            Triggers = { new DataTrigger { Binding = new System.Windows.Data.Binding("HasItems") { Source = icons }, Value = false,
                Setters = { new Setter(VisibilityProperty, Visibility.Visible) } } }
        };
        panel.Children.Add(empty);
        OpenStatusPopup(HiddenIconsButton, panel);
    }
    private static uint MousePosition()
    {
        var p = Forms.Cursor.Position;
        return unchecked((uint)((p.X & 0xffff) | ((p.Y & 0xffff) << 16)));
    }
    private static NotifyIcon? TrayIcon(object sender) => (sender as FrameworkElement)?.DataContext as NotifyIcon;
    private void OnTrayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TrayIcon(sender) is not { } icon) return;
        SetCollapsed(false); _hideTimer.Stop(); _systemTray?.SetPlacement((FrameworkElement)sender);
        var element = (FrameworkElement)sender;
        var r = element.PointToScreen(new Point());
        var bottom = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        icon.Placement = new ManagedShell.Interop.NativeMethods.Rect { Left = (int)r.X, Top = (int)r.Y, Right = (int)bottom.X, Bottom = (int)bottom.Y };
        icon.IconMouseDown(e.ChangedButton, MousePosition(), (int)GetDoubleClickTime()); e.Handled = true;
    }
    private void OnTrayMouseUp(object sender, MouseButtonEventArgs e)
    {
        TrayIcon(sender)?.IconMouseUp(e.ChangedButton, MousePosition(), (int)GetDoubleClickTime()); e.Handled = true;
    }
    private void OnTrayMouseEnter(object sender, MouseEventArgs e) => TrayIcon(sender)?.IconMouseEnter(MousePosition());
    private void OnTrayMouseLeave(object sender, MouseEventArgs e) => TrayIcon(sender)?.IconMouseLeave(MousePosition());
    private void OnTrayKeyboardClick(object sender, RoutedEventArgs e)
    {
        SetCollapsed(false); _hideTimer.Stop();
        TrayIcon(sender)?.IconMouseDown(MouseButton.Left, MousePosition(), (int)GetDoubleClickTime());
        TrayIcon(sender)?.IconMouseUp(MouseButton.Left, MousePosition(), (int)GetDoubleClickTime());
        if (CanAutoHide) _hideTimer.Start();
    }
    private void OnInputLanguageClick(object sender, RoutedEventArgs e)
    {
        SetCollapsed(false);
        var target = _inputTarget;
        var menu = new ContextMenu { PlacementTarget = InputLanguageButton };
        foreach (Forms.InputLanguage language in Forms.InputLanguage.InstalledInputLanguages)
        {
            var item = new MenuItem { Header = language.LayoutName };
            item.Click += (_, _) => PostMessage(target, 0x50, 0, language.Handle);
            menu.Items.Add(item);
        }
        _menu = menu; _hideTimer.Stop();
        menu.Closed += (_, _) => { if (CanAutoHide) _hideTimer.Start(); };
        menu.IsOpen = true;
    }
    private static void OpenSettings(string page)
    {
        try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show("无法打开 Windows 设置：" + ex.Message, "Mosaic"); }
    }
    private void OnNetworkClick(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:network-status");
    private void OnVolumeClick(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:sound");
    private void OnBatteryClick(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:batterysaver");
    private void OnClockClick(object sender, RoutedEventArgs e) => OpenStatusPopup(ClockButton, new System.Windows.Controls.Calendar { SelectedDate = DateTime.Today, DisplayDate = DateTime.Today });
    public void EnsureTopmost() { if (IsVisible) NativeShell.KeepAbove(new WindowInteropHelper(this).Handle); }
    private async void OnSearchClick(object sender, RoutedEventArgs e) => await _execute(new("palette.toggle"));
    private async void OnSettingsClick(object sender, RoutedEventArgs e) => await _execute(new("dashboard.open"));
    private async void OnPinMenuClick(object sender, RoutedEventArgs e)
    {
        PinMenuButton.IsEnabled = false;
        try { await _execute(new("menubar.pin.toggle")); }
        finally { PinMenuButton.IsEnabled = true; RefreshConfiguration(); }
    }
    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        SetCollapsed(false);
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        ((Button)sender).ContextMenu = menu;
        _menu = menu; _hideTimer.Stop();
        menu.Closed += (_, _) => { if (CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); } };
        var pin = new MenuItem { Header = "固定顶部菜单", IsCheckable = true, IsChecked = !_services.Settings.Desktop.MenuBarAutoHide,
            InputGestureText = NativeShell.DescribeHotkey(_services.Settings.Shortcuts.PinMenuBar) };
        pin.Click += async (_, _) => await _execute(new("menubar.pin.toggle"));
        menu.Items.Add(pin); menu.Items.Add(new Separator());
        foreach (var (title, command) in new[] { ("设置与桌面组件", "dashboard.open"), ("编辑桌面布局", "desktop.edit"), ("资源库", "library.open"), ("工作空间", "workspaces.open"), ("切回 Windows", "desktop.toggle"), ("退出 Mosaic", "app.exit") })
        {
            var item = new MenuItem { Header = title }; item.Click += async (_, _) => await _execute(new(command)); menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
