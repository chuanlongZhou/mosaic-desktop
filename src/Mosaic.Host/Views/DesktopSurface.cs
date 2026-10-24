using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Mosaic.Core.Models;
using Mosaic.Host.Controls;
using Mosaic.Host.Services;
using Mosaic.Host.ViewModels;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Views;

// One native child of Explorer's desktop, containing ordinary WPF controls.
// Never creates a top-level widget window, even when Explorer is unavailable.
public sealed class DesktopSurface : IDisposable
{
    private readonly AppServices _services;
    private readonly DashboardViewModel _vm;
    private readonly Grid _root = new();
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Border _editBar;
    private readonly TextBlock _editStatus;
    private readonly Dictionary<WidgetKind, DesktopWidget> _widgets = [];
    private readonly HashSet<WidgetKind> _dirty = [];
    private readonly Stack<Dictionary<WidgetKind, DesktopPluginPlacement>> _undo = new(), _redo = new();
    private readonly DispatcherTimer _attachmentTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private HwndSource? _source;
    private nint _parent;
    private bool _disposed, _saving;
    public bool IsEditing { get; private set; }
    public nint Handle => _source is { IsDisposed: false } ? _source.Handle : 0;
    public FrameworkElement Content => _root;
    public IReadOnlyDictionary<WidgetKind, DesktopWidget> Widgets => _widgets;

    public DesktopSurface(AppServices services, DashboardViewModel vm)
    {
        _services = services; _vm = vm;
        _root.DataContext = vm; WidgetContext.SetOwner(_root, vm);
        _root.Children.Add(_canvas);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _editStatus = new TextBlock { Text = "编辑桌面布局", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        buttons.Children.Add(_editStatus);
        var undo = new Button { Content = "撤销", ToolTip = "Ctrl+Z", Margin = new Thickness(0, 0, 8, 0) }; undo.Click += (_, _) => Undo();
        var redo = new Button { Content = "重做", ToolTip = "Ctrl+Y", Margin = new Thickness(0, 0, 8, 0) }; redo.Click += (_, _) => Redo();
        var reset = new Button { Content = "恢复默认", Margin = new Thickness(0, 0, 8, 0) }; reset.Click += (_, _) => ResetLayout();
        buttons.Children.Add(undo); buttons.Children.Add(redo); buttons.Children.Add(reset);
        var save = new Button { Content = "完成", Margin = new Thickness(0, 0, 8, 0) };
        save.Click += async (_, _) => { try { await FlushLayoutAsync(); } catch (Exception ex) { _editStatus.Text = "保存失败，请重试：" + ex.Message; vm.SetStatus("桌面布局保存失败：" + ex.Message, true); } };
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => CancelEdit();
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        _editBar = new Border { Child = buttons, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(20, 44, 20, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        _editBar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        _root.Children.Add(_editBar);
        _root.PreviewKeyDown += (_, e) =>
        {
            if (!IsEditing) return;
            if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
            { if (e.Key == Key.Z) { Undo(); e.Handled = true; } else if (e.Key == Key.Y) { Redo(); e.Handled = true; } }
        };
        _canvas.SizeChanged += (_, _) => ClampWidgets();
        _attachmentTimer.Tick += (_, _) => RefreshAttachment();
        Refresh(); RefreshAttachment(); _attachmentTimer.Start();
    }

    public void Refresh()
    {
        var active = _services.Settings.Desktop.Widgets.Where(x => x.Enabled && (x.Kind != WidgetKind.Clock || _services.Settings.Modules.DesktopClock)).ToArray();
        foreach (var kind in _widgets.Keys.Except(active.Select(x => x.Kind)).ToArray())
        { _canvas.Children.Remove(_widgets[kind]); _widgets.Remove(kind); _dirty.Remove(kind); }
        foreach (var placement in active)
        {
            if (!_widgets.TryGetValue(placement.Kind, out var widget))
            {
                widget = new(_vm, placement); widget.SetEditing(IsEditing);
                widget.PlacementStarted += (_, _) => Checkpoint();
                widget.PlacementChanged += (_, _) => _dirty.Add(widget.Kind);
                _widgets.Add(placement.Kind, widget); _canvas.Children.Add(widget);
            }
            else if (!IsEditing && !_saving) Restore(widget, placement);
        }
        ClampWidgets();
        _vm.SetMonitoringFor(this, Handle != 0 && _widgets.Count > 0);
    }

    public void BeginEdit()
    {
        if (_saving) return;
        _editStatus.Text = "编辑桌面布局";
        IsEditing = true; _editBar.Visibility = Visibility.Visible;
        _undo.Clear(); _redo.Clear();
        foreach (var widget in _widgets.Values) widget.SetEditing(true);
    }
    public void CancelEdit()
    {
        EndEdit(); _dirty.Clear(); Refresh();
    }
    private void EndEdit()
    {
        IsEditing = false; _editBar.Visibility = Visibility.Collapsed;
        foreach (var widget in _widgets.Values) widget.SetEditing(false);
    }
    private Dictionary<WidgetKind, DesktopPluginPlacement> Capture() => _widgets.ToDictionary(x => x.Key, x => new DesktopPluginPlacement
        { Kind = x.Key, Left = Canvas.GetLeft(x.Value), Top = Canvas.GetTop(x.Value), Width = x.Value.Width, Height = x.Value.Height, Enabled = true });
    private void Checkpoint()
    {
        if (!IsEditing) return;
        if (_undo.Count >= 40) { var retained = _undo.Reverse().Skip(1).ToArray(); _undo.Clear(); foreach (var item in retained) _undo.Push(item); }
        _undo.Push(Capture()); _redo.Clear();
    }
    private void ApplySnapshot(Dictionary<WidgetKind, DesktopPluginPlacement> snapshot)
    {
        foreach (var (kind, placement) in snapshot) if (_widgets.TryGetValue(kind, out var widget)) { Restore(widget, placement); _dirty.Add(kind); }
        ClampWidgets();
    }
    public void Undo() { if (!IsEditing || _undo.Count == 0) return; _redo.Push(Capture()); ApplySnapshot(_undo.Pop()); }
    public void Redo() { if (!IsEditing || _redo.Count == 0) return; _undo.Push(Capture()); ApplySnapshot(_redo.Pop()); }
    public void ResetLayout()
    {
        if (!IsEditing) return; Checkpoint();
        ApplySnapshot(DesktopPluginPlacement.Defaults().Where(x => _widgets.ContainsKey(x.Kind)).ToDictionary(x => x.Kind));
    }
    private static void Restore(DesktopWidget widget, DesktopPluginPlacement placement)
    {
        widget.Width = placement.Width; widget.Height = placement.Height;
        Canvas.SetLeft(widget, placement.Left); Canvas.SetTop(widget, placement.Top);
    }
    private void ClampWidgets()
    {
        if (_canvas.ActualWidth < 1 || _canvas.ActualHeight < 1) return;
        foreach (var widget in _widgets.Values) widget.Clamp(_canvas.RenderSize);
    }
    public async Task FlushLayoutAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            if (_dirty.Count > 0)
            {
                var settings = _services.CopySettings();
                foreach (var kind in _dirty)
                {
                    if (!_widgets.TryGetValue(kind, out var widget)) continue;
                    var placement = settings.Desktop.Widgets.Single(x => x.Kind == kind);
                    placement.Left = Canvas.GetLeft(widget); placement.Top = Canvas.GetTop(widget); placement.Width = widget.Width; placement.Height = widget.Height;
                }
                _saving = true;
                _root.IsEnabled = false;
                try { await _services.SaveSettingsAsync(settings); _dirty.Clear(); }
                finally { _saving = false; _root.IsEnabled = true; }
            }
            EndEdit();
        }
        finally { _saveGate.Release(); }
    }

    public void RefreshAttachment()
    {
        if (_disposed) return;
        var parent = FindDesktopHost();
        if (parent == 0 || (Handle != 0 && _parent != parent) || (Handle != 0 && !IsWindow(Handle))) ReleaseSource();
        if (parent == 0) { _vm.SetMonitoringFor(this, false); return; }
        if (Handle == 0)
        {
            // Match Explorer's DPI context at creation, rather than reparenting a
            // top-level HWND and forcing a DPI-awareness reset for the process.
            var oldContext = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(parent));
            try
            {
                var parameters = new HwndSourceParameters("Mosaic · 桌面控件层")
                {
                    ParentWindow = parent, WindowStyle = 0x40000000 | 0x10000000 | 0x04000000,
                    ExtendedWindowStyle = 0x80, UsesPerPixelTransparency = true,
                    TreatAsInputRoot = true, Width = 1, Height = 1,
                };
                _source = new(parameters); _parent = parent;
                _source.CompositionTarget.BackgroundColor = Colors.Transparent;
                _source.RootVisual = _root;
            }
            finally { if (oldContext != 0) SetThreadDpiAwarenessContext(oldContext); }
        }
        // Placements are relative to the primary desktop in DIPs. Keep physical
        // screen/client mapping outside WPF, including a non-zero desktop origin.
        var bounds = Forms.Screen.PrimaryScreen!.Bounds;
        var point = new NativePoint { X = bounds.Left, Y = bounds.Top };
        MapWindowPoints(0, parent, ref point, 1);
        GetWindowRect(Handle, out var current);
        if (!IsWindowVisible(Handle) || current.Left != bounds.Left || current.Top != bounds.Top || current.Right - current.Left != bounds.Width || current.Bottom - current.Top != bounds.Height)
            SetWindowPos(Handle, 0, point.X, point.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040);
        _vm.SetMonitoringFor(this, _widgets.Count > 0);
    }

    public static nint FindDesktopHost()
    {
        nint host = 0;
        EnumWindows((window, _) =>
        {
            var view = FindWindowEx(window, 0, "SHELLDLL_DefView", null);
            if (view == 0) return true;
            // Keep the desktop composition view visible. Hiding this view and
            // placing a sibling on Progman bypasses the shell's visual layer
            // on modern Windows 11; wallpaper WorkerW is disabled for input.
            // DesktopOverlay hides only FolderView, leaving this host intact.
            host = view;
            return false;
        }, 0);
        return host;
    }
    private void ReleaseSource()
    {
        if (_source is not null) { if (!_source.IsDisposed) { _source.RootVisual = null; _source.Dispose(); } _source = null; }
        _parent = 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _attachmentTimer.Stop(); _vm.SetMonitoringFor(this, false); ReleaseSource();
    }
    private delegate bool EnumCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string name, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint window);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from, nint to, ref NativePoint point, uint count);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out ShellWorkArea.Rect rect);
}
