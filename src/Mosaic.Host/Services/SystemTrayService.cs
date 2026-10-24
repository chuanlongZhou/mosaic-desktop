using System.Runtime.InteropServices;
using System.Windows;
using ManagedShell.WindowsTray;
using ManagedShell.Interop;
using ManagedShell.AppBar;
using System.Collections.ObjectModel;

namespace Mosaic.Host.Services;

// ManagedShell receives Shell_NotifyIcon messages and preserves application
// callbacks, including icons that were in Explorer's overflow area.
public sealed class SystemTrayService : IDisposable
{
    private const string Marker = "Mosaic.TrayService";
    private readonly TrayService _listener = new();
    private readonly AppBarManager _appBars;
    public NotificationArea Area { get; }
    public SystemTrayService()
    {
        Area = new NotificationArea(NotificationArea.DEFAULT_PINNED, _listener, new ExplorerTrayService());
        // A collection per lifecycle avoids retaining old NotifyIcon callbacks
        // through the library's dependency-property default collection.
        Area.TrayIcons = new ObservableCollection<NotifyIcon>();
        _appBars = new AppBarManager(new ExplorerHelper(Area));
        Area.Initialize();
        if (Area.Handle != 0) SetProp(Area.Handle, Marker, new nint(1));
    }
    public void SetPlacement(FrameworkElement element)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null) return;
        var top = element.PointToScreen(new Point());
        var bottom = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        Area.SetTrayHostSizeData(new TrayHostSizeData
        {
            edge = NativeMethods.ABEdge.ABE_TOP,
            rc = new NativeMethods.Rect { Left = (int)top.X, Top = (int)top.Y, Right = (int)bottom.X, Bottom = (int)bottom.Y }
        });
    }
    public static bool IsServiceWindow(nint window) => GetProp(window, Marker) != 0;
    public static bool IsNativeMenuOpen()
    {
        nint window = 0;
        while ((window = FindWindowEx(0, window, "#32768", null)) != 0)
            if (IsWindowVisible(window)) return true;
        return false;
    }
    public static void ReannounceIcons() => SendNotifyMessage(new nint(0xffff), RegisterWindowMessage("TaskbarCreated"), 0, 0);
    public void Dispose()
    {
        if (Area.Handle != 0) RemoveProp(Area.Handle, Marker);
        _appBars.Dispose();
        if (Area.IsFailed) _listener.Dispose();
        else Area.Dispose();
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint window, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SendNotifyMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string name, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
}
