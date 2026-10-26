using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Mosaic.Core.Commands;
using Mosaic.Core.Models;
using Mosaic.Host.Services;

namespace Mosaic.Host.Views;

public partial class DockWindow : Window
{
    private readonly AppServices _services;
    private readonly Func<CommandRequest, Task> _execute;
    private readonly DispatcherTimer _hideTimer;
    private readonly DockApplications _applications = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _refreshing, _closed;
    private string _signature = "";
    private AppBarReservation? _reservation;
    private int _reservedHeight;
    private Point _mouseDown;
    private bool _dragging, _collapsed, _busy;
    private bool _autoHideAllowed;
    private bool? _windowsKeyPinned;
    public void SetWindowsKeyPinned(bool pinned)
    {
        _windowsKeyPinned = pinned;
        _hideTimer.Stop();
        if (pinned) { if (!IsVisible) Show(); ExpandDock(); EnsureTopmost(); }
        else if (CanAutoHide && !IsMouseOver && !HasOpenMenu()) CollapseDock();
    }
    private bool CanAutoHide => _services.Settings.Dock.AutoHide && _autoHideAllowed && _windowsKeyPinned != true;
    public bool IsAutoHidden => _collapsed;

    public void SetAutoHideAllowed(bool allowed)
    {
        var changed = _autoHideAllowed != allowed;
        _autoHideAllowed = allowed;
        if (!CanAutoHide)
        {
            _hideTimer.Stop();
            if (IsLoaded && !IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            ExpandDock(); EnsureTopmost();
        }
        else if (changed && !IsMouseOver && !HasOpenMenu()) { _hideTimer.Stop(); _hideTimer.Start(); }
    }

    public DockWindow(AppServices services, Func<CommandRequest, Task> execute)
    {
        _services = services;
        _execute = execute;
        InitializeComponent();
        _hideTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
        _refreshTimer.Tick += (_, _) => RefreshItems();
        _refreshTimer.Start();
        _edgeTimer.Tick += (_, _) =>
        {
            if (!_collapsed || !IsVisible) return;
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen is null) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var half = Width * scale / 2;
            var center = screen.Bounds.Left + screen.Bounds.Width / 2.0;
            if (cursor.Y >= screen.Bounds.Bottom - 6 && cursor.Y < screen.Bounds.Bottom && Math.Abs(cursor.X - center) <= half) ExpandDock();
        };
        _edgeTimer.Start();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); if (!IsMouseOver && !_dragging && !HasOpenMenu() && CanAutoHide) CollapseDock(); };
        SourceInitialized += (_, _) => NativeShell.StyleWindow(new WindowInteropHelper(this).Handle, true, true, transparent: true);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) => { _closed = true; _refreshTimer.Stop(); _hideTimer.Stop(); _edgeTimer.Stop(); _reservation?.Dispose(); SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged; };
    }

    public async void RefreshItems()
    {
        if (_refreshing || _closed) return;
        _refreshing = true;
        try
        {
            var entries = await _applications.ReadAsync(_services.Settings.Dock.Items, _services.Settings.Dock.SyncTaskbarPins);
            if (_closed) return;
            var signature = string.Join("|", entries.Select(x => x.Identity + ":" + x.Title + ":" +
                string.Join(",", x.Windows.Zip(x.WindowTitles, (handle, title) => handle.ToString() + "=" + title))));
            DockItems.LayoutTransform = new ScaleTransform(_services.Settings.Dock.IconSize / 40.0, _services.Settings.Dock.IconSize / 40.0);
            Height = DockHeight;
            DockTimeLabel.Text = DateTime.Now.ToString("HH:mm");
            DockTimeLabel.ToolTip = DateTime.Now.ToString("yyyy-MM-dd dddd");
            Width = Math.Min(Math.Max(240, entries.Count * 58 * _services.Settings.Dock.IconSize / 40.0 + 140), SystemParameters.PrimaryScreenWidth - 32);
            if (_signature != signature && !_dragging && !HasOpenMenu())
            {
                _signature = signature;
                DockItems.ItemsSource = entries;
            }
            if (!CanAutoHide) { _hideTimer.Stop(); ExpandDock(); }
            else if (!IsMouseOver && !_hideTimer.IsEnabled && !_collapsed) _hideTimer.Start();
            Reposition(); EnsureTopmost();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Trace.WriteLine("Dock refresh: " + ex);
            try { File.WriteAllText(Path.Combine(_services.Store.Root, "dock-error.txt"), ex.ToString()); } catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
        }
        finally { _refreshing = false; }
    }
    public double DockHeight => Math.Ceiling(60 * _services.Settings.Dock.IconSize / 40.0 + 24);
    public void EnsureTopmost() { if (IsVisible && !HasOpenMenu()) NativeShell.KeepAbove(new WindowInteropHelper(this).Handle); }
    private bool HasOpenMenu() => _addMenu?.IsOpen == true || _windowMenu?.IsOpen == true || DockItems.Items.Cast<object>().Any(x =>
        (DockItems.ItemContainerGenerator.ContainerFromItem(x) is ContentPresenter presenter &&
        VisualTreeHelper.GetChildrenCount(presenter) > 0 && VisualTreeHelper.GetChild(presenter, 0) is Button button && button.ContextMenu?.IsOpen == true));
    private ContextMenu? _addMenu, _windowMenu;
    private static DockItem? ItemOf(object sender) => ((sender as FrameworkElement)?.DataContext as DockApplication)?.Item;

    private void OnDockWheel(object sender, MouseWheelEventArgs e) { DockScroll.ScrollToHorizontalOffset(DockScroll.HorizontalOffset - e.Delta); e.Handled = true; }
    private void OnLoaded(object sender, RoutedEventArgs e) { Reposition(); RefreshItems(); }
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => Reposition();
    private void Reposition()
    {
        var reserved = _services.Settings.Dock.AutoHide ? 0 : (int)DockHeight;
        if (_reservedHeight != reserved && IsLoaded)
        {
            _reservedHeight = reserved; _reservation?.Dispose(); _reservation = null;
            if (reserved > 0 && System.Windows.Forms.Screen.PrimaryScreen is { } screen)
                _reservation = new AppBarReservation(new WindowInteropHelper(this).Handle, screen.Bounds, reserved, true, false);
        }
        var bottom = _services.Settings.Desktop.OverlayEnabled ? SystemParameters.PrimaryScreenHeight : SystemParameters.WorkArea.Bottom;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = bottom - (_collapsed ? 2 : Height);
    }
    private void ExpandDock()
    {
        if (!_collapsed) return;
        _collapsed = false; Height = DockHeight; DockBorder.Visibility = Visibility.Visible; Reposition(); EnsureTopmost();
        DockBorder.RenderTransform = new TranslateTransform();
        DockBorder.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(72, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private void CollapseDock()
    {
        if (_collapsed || !CanAutoHide) return;
        _collapsed = true; DockBorder.Visibility = Visibility.Hidden; Reposition();
    }
    private void OnMouseEnter(object sender, MouseEventArgs e) { _hideTimer.Stop(); if (_collapsed) ExpandDock(); }
    private void OnMouseLeave(object sender, MouseEventArgs e) { if (CanAutoHide) { _hideTimer.Stop(); _hideTimer.Start(); } }

    private async void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (_dragging || _busy || (sender as FrameworkElement)?.DataContext is not DockApplication entry) return;
        if (entry.HasMultipleWindows) ShowRunningWindowMenu((Button)sender, entry);
        else if (entry.IsRunning) DockApplications.Activate(entry.Windows[0]);
        else await _execute(new(entry.Item.Command, entry.Item.Target));
    }
    private static string WindowLabel(DockApplication entry, int index)
    {
        var title = entry.WindowTitles[index];
        return title.Length > 72 ? title[..69] + "…" : title;
    }
    private void ShowRunningWindowMenu(Button button, DockApplication entry)
    {
        _hideTimer.Stop();
        if (_windowMenu is not null) _windowMenu.IsOpen = false;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        for (var index = 0; index < entry.Windows.Length; index++)
        {
            var handle = entry.Windows[index];
            var item = new MenuItem { Header = $"{index + 1}. {WindowLabel(entry, index)}", ToolTip = entry.WindowTitles[index] };
            item.Click += (_, _) => DockApplications.Activate(handle);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_windowMenu, menu)) _windowMenu = null;
            if (CanAutoHide) _hideTimer.Start();
        };
        _windowMenu = menu;
        menu.IsOpen = true;
    }
    private void OnItemMouseDown(object sender, MouseButtonEventArgs e) => _mouseDown = e.GetPosition(this);
    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragging || _busy || ItemOf(sender) is not DockItem item) return;
        if ((e.GetPosition(this) - _mouseDown).Length < 6) return;
        _dragging = true;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("MosaicDockItem", item.Id), DragDropEffects.Move); }
        finally { _dragging = false; }
    }
    private async void OnItemDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("MosaicDockItem") || ItemOf(sender) is not DockItem target) return;
        var id = e.Data.GetData("MosaicDockItem") as string;
        await ChangeAsync(settings =>
        {
            var source = settings.Dock.Items.FirstOrDefault(x => x.Id == id);
            if (source is null || source.Id == target.Id) return;
            var index = settings.Dock.Items.FindIndex(x => x.Id == target.Id);
            settings.Dock.Items.Remove(source);
            settings.Dock.Items.Insert(Math.Min(index, settings.Dock.Items.Count), source);
        });
        e.Handled = true;
    }

    private void OnItemContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DockApplication entry) return;
        var item = entry.Item;
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开" };
        open.Click += async (_, _) => await _execute(new(item.Command, item.Target));
        menu.Items.Add(open);
        for (var windowIndex = 0; windowIndex < entry.Windows.Length; windowIndex++)
        {
            var handle = entry.Windows[windowIndex];
            var switchTo = new MenuItem { Header = "切换到 · " + WindowLabel(entry, windowIndex), ToolTip = entry.WindowTitles[windowIndex] };
            switchTo.Click += (_, _) => DockApplications.Activate(handle); menu.Items.Add(switchTo);
        }
        if (!_services.Settings.Dock.Items.Any(x => x.Id == item.Id))
        {
            menu.Items.Add(new MenuItem { Header = entry.IsRunning ? "正在运行 · 关闭应用后自动移除未固定项" : "来自 Windows 任务栏固定应用", IsEnabled = false });
            button.ContextMenu = menu; e.Handled = true; menu.PlacementTarget = button; menu.IsOpen = true; return;
        }
        var index = _services.Settings.Dock.Items.FindIndex(x => x.Id == item.Id);
        AddMove(menu, item, "向左移动", -1, index > 0);
        AddMove(menu, item, "向右移动", 1, index < _services.Settings.Dock.Items.Count - 1);
        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = "从 Dock 移除" };
        remove.Click += async (_, _) => await ChangeAsync(settings => settings.Dock.Items.RemoveAll(x => x.Id == item.Id));
        menu.Items.Add(remove);
        button.ContextMenu = menu;
        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void AddMove(ContextMenu menu, DockItem item, string title, int delta, bool enabled)
    {
        var move = new MenuItem { Header = title, IsEnabled = enabled };
        move.Click += async (_, _) => await ChangeAsync(settings =>
        {
            var index = settings.Dock.Items.FindIndex(x => x.Id == item.Id);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= settings.Dock.Items.Count) return;
            (settings.Dock.Items[index], settings.Dock.Items[target]) = (settings.Dock.Items[target], settings.Dock.Items[index]);
        });
        menu.Items.Add(move);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        _addMenu = menu;
        var settings = new MenuItem { Header = "桌面模式与 Dock 设置" };
        settings.Click += async (_, _) => await _execute(new("settings.open"));
        menu.Items.Add(settings);
        var file = new MenuItem { Header = "添加应用或文件…" };
        file.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "添加到 Mosaic Dock", CheckFileExists = true, Filter = "应用和快捷方式|*.exe;*.lnk|所有文件|*.*" };
            if (picker.ShowDialog() == true) await AddPathAsync(picker.FileName, false);
        };
        var folder = new MenuItem { Header = "添加文件夹…" };
        folder.Click += async (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "添加文件夹到 Mosaic Dock" };
            if (picker.ShowDialog() == true) await AddPathAsync(picker.FolderName, true);
        };
        var dashboard = new MenuItem { Header = "添加工作台入口", IsEnabled = !_services.Settings.Dock.Items.Any(x => x.Command == "dashboard.toggle") };
        dashboard.Click += async (_, _) => await ChangeAsync(settings => settings.Dock.Items.Add(new() { Title = "工作台", Glyph = "\uECA5", Command = "dashboard.toggle" }));
        menu.Items.Add(file); menu.Items.Add(folder); menu.Items.Add(dashboard);
        var search = new MenuItem { Header = "添加全局搜索入口", IsEnabled = !_services.Settings.Dock.Items.Any(x => x.Command == "palette.toggle") };
        search.Click += async (_, _) => await ChangeAsync(settings => settings.Dock.Items.Add(new() { Title = "全局搜索", Glyph = "\uE721", Command = "palette.toggle" }));
        menu.Items.Add(search);
        foreach (var (title, glyph, command) in new[] { ("资源库", "\uE8A9", "library.open"), ("工作空间", "\uE81E", "workspaces.open") })
        {
            var entry = new MenuItem { Header = "添加" + title + "入口", IsEnabled = !_services.Settings.Dock.Items.Any(x => x.Command == command) };
            entry.Click += async (_, _) => await ChangeAsync(settings => settings.Dock.Items.Add(new() { Title = title, Glyph = glyph, Command = command }));
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    private Task AddPathAsync(string path, bool folder) => ChangeAsync(settings =>
    {
        if (settings.Dock.Items.Count >= 24) throw new InvalidOperationException("Dock 最多保留 24 个入口。");
        if (settings.Dock.Items.Any(x => string.Equals(x.Target, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("这个入口已经在 Dock 中。");
        settings.Dock.Items.Add(new() { Title = folder ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path),
            Target = path, Glyph = folder ? "\uE8B7" : "\uE8A5", Command = "resource.open" });
    });

    private async Task ChangeAsync(Action<AppSettings> change)
    {
        if (_busy) return;
        _busy = true;
        try { var candidate = _services.CopySettings(); change(candidate); await _services.SaveSettingsAsync(candidate); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Dock 未能保存", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _busy = false; }
    }
}
