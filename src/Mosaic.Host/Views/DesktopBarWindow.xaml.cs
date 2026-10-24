using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
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
    private bool? _autoHide;
    private bool _autoHideAllowed;
    private bool CanAutoHide => _autoHide == true && _autoHideAllowed;
    public bool IsAutoHidden { get; private set; }
    public void SetAutoHideAllowed(bool allowed)
    {
        var changed = _autoHideAllowed != allowed;
        _autoHideAllowed = allowed;
        if (!IsLoaded) return;
        if (!CanAutoHide) { _hideTimer.Stop(); SetCollapsed(false); }
        else if (changed && !IsMouseOver && _menu?.IsOpen != true) { _hideTimer.Stop(); _hideTimer.Start(); }
    }
    public DesktopBarWindow(AppServices services, Func<CommandRequest, Task> execute, Forms.Screen screen)
    {
        _services = services; _execute = execute; _screen = screen; InitializeComponent();
        Loaded += (_, _) => { RefreshConfiguration(); UpdateTime(); };
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, true, true);
        _timer.Tick += (_, _) => UpdateTime(); _timer.Start();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (CanAutoHide && !IsMouseOver && _menu?.IsOpen != true) SetCollapsed(true);
        };
        _edgeTimer.Tick += (_, _) =>
        {
            if (!CanAutoHide || !IsAutoHidden) return;
            var cursor = Forms.Cursor.Position; var bounds = _screen.Bounds;
            if (cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Top + 6) SetCollapsed(false);
        };
        _edgeTimer.Start();
        Closed += (_, _) => { _timer.Stop(); _hideTimer.Stop(); _edgeTimer.Stop(); _reservation?.Dispose(); };
    }
    public void RefreshConfiguration()
    {
        if (!IsLoaded || _autoHide == _services.Settings.Desktop.MenuBarAutoHide) return;
        _autoHide = _services.Settings.Desktop.MenuBarAutoHide;
        _hideTimer.Stop(); _reservation?.Dispose(); _reservation = null;
        if (_autoHide == false)
        {
            SetCollapsed(false);
            _reservation = new AppBarReservation(new WindowInteropHelper(this).Handle, _screen.Bounds);
        }
        else SetCollapsed(CanAutoHide && !IsMouseOver && _menu?.IsOpen != true);
    }
    private void SetCollapsed(bool collapsed)
    {
        collapsed &= CanAutoHide;
        IsAutoHidden = collapsed;
        Height = ShellWorkArea.BarHeight;
        BarContent.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        var handle = new WindowInteropHelper(this).Handle;
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
        var bounds = _screen.Bounds;
        // Hide the HWND completely: even a transparent activation strip can retain a compositor border.
        // The edge timer detects the pointer independently of the window's visibility.
        if (collapsed)
        {
            SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top, bounds.Width, (int)Math.Ceiling(ShellWorkArea.BarHeight * scale), 0x10);
            Hide();
        }
        else
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!IsVisible) Show();
            SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top, bounds.Width, (int)Math.Ceiling(ShellWorkArea.BarHeight * scale), 0x50);
        }
        if (!collapsed && CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); }
    }
    private void OnMouseEnter(object sender, MouseEventArgs e) { _hideTimer.Stop(); if (IsAutoHidden) SetCollapsed(false); }
    private void OnMouseLeave(object sender, MouseEventArgs e) { if (CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); } }
    private void UpdateTime()
    {
        DateTimeLabel.Text = DateTime.Now.ToString("M 月 d 日  dddd   HH:mm:ss", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        WorkspaceLabel.Text = _services.Workspace.Name;
    }
    public void EnsureTopmost() { if (IsVisible) NativeShell.KeepAbove(new WindowInteropHelper(this).Handle); }
    private async void OnSearchClick(object sender, RoutedEventArgs e) => await _execute(new("palette.toggle"));
    private async void OnSettingsClick(object sender, RoutedEventArgs e) => await _execute(new("dashboard.open"));
    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        ((Button)sender).ContextMenu = menu;
        _menu = menu; _hideTimer.Stop();
        menu.Closed += (_, _) => { if (CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); } };
        foreach (var (title, command) in new[] { ("设置与桌面插件", "dashboard.open"), ("编辑桌面布局", "desktop.edit"), ("资源库", "library.open"), ("工作空间", "workspaces.open"), ("切回 Windows", "desktop.toggle"), ("退出 Mosaic", "app.exit") })
        {
            var item = new MenuItem { Header = title }; item.Click += async (_, _) => await _execute(new(command)); menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
