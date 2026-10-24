using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Mosaic.Host.Services;

// Register with Explorer so newly maximized windows immediately respect the top menu bar.
public sealed class AppBarReservation : IDisposable
{
    private readonly nint _window;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly uint _callback;
    private readonly int _height;
    private readonly bool _bottom, _moveWindow;
    private bool _updating;
    private readonly HwndSource? _source;
    public AppBarReservation(nint window, System.Drawing.Rectangle bounds, int height = ShellWorkArea.BarHeight, bool bottom = false, bool moveWindow = true)
    {
        _window = window; _bounds = bounds;
        _height = height; _bottom = bottom; _moveWindow = moveWindow;
        _callback = RegisterWindowMessage("Mosaic.AppBar." + window);
        _source = HwndSource.FromHwnd(window); _source?.AddHook(OnMessage);
        var data = Data(); data.Callback = _callback; SHAppBarMessage(0, ref data);
        Place();
    }
    private AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = _window, Edge = _bottom ? 3u : 1u };
    private void Place()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var height = (int)Math.Ceiling(_height * Math.Max(96, GetDpiForWindow(_window)) / 96.0);
            var data = Data(); data.Rect = new() { Left = _bounds.Left, Top = _bottom ? _bounds.Bottom - height : _bounds.Top, Right = _bounds.Right, Bottom = _bottom ? _bounds.Bottom : _bounds.Top + height };
            SHAppBarMessage(2, ref data);
            if (_bottom) data.Rect.Top = data.Rect.Bottom - height; else data.Rect.Bottom = data.Rect.Top + height;
            SHAppBarMessage(3, ref data);
            if (_moveWindow) SetWindowPos(_window, new nint(-1), data.Rect.Left, data.Rect.Top, data.Rect.Right - data.Rect.Left, height, 0x50);
        }
        finally { _updating = false; }
    }
    private nint OnMessage(nint window, int message, nint parameter, nint value, ref bool handled)
    {
        if ((uint)message == _callback && parameter.ToInt32() == 1) Place();
        return 0;
    }
    public void Dispose() { _source?.RemoveHook(OnMessage); var data = Data(); SHAppBarMessage(1, ref data); }
    public static uint TaskbarState() { var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() }; return (uint)SHAppBarMessage(4, ref data); }
    public static void SetTaskbarState(uint state)
    {
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = FindWindow("Shell_TrayWnd", null), Parameter = new nint(state) };
        SHAppBarMessage(10, ref data);
    }
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData
    { public uint Size; public nint Window; public uint Callback, Edge; public ShellWorkArea.Rect Rect; public nint Parameter; }
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
