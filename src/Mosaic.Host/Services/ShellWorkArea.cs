using System.Runtime.InteropServices;

namespace Mosaic.Host.Services;

public static class ShellWorkArea
{
    public const int BarHeight = 32;
    [StructLayout(LayoutKind.Sequential)] public struct Rect : IEquatable<Rect>
    {
        public int Left, Top, Right, Bottom;
        public bool Equals(Rect other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        public override bool Equals(object? obj) => obj is Rect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    public static List<Rect> Capture()
    {
        var areas = new List<Rect>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) areas.Add(info.Work);
            return true;
        }, 0);
        return areas;
    }
    public static void Apply(int dockHeight, bool fitExisting = false, IReadOnlyDictionary<System.Drawing.Rectangle, double>? menuHeights = null)
    {
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            GetDpiForMonitor(monitor, 0, out var xDpi, out _);
            var scale = (xDpi == 0 ? 96 : xDpi) / 96.0;
            var desired = info.Monitor;
            var bounds = System.Drawing.Rectangle.FromLTRB(desired.Left, desired.Top, desired.Right, desired.Bottom);
            if (menuHeights?.TryGetValue(bounds, out var menuHeight) == true) desired.Top += (int)Math.Ceiling(menuHeight * scale);
            if ((info.Flags & 1) != 0) desired.Bottom -= (int)Math.Ceiling(dockHeight * scale);
            if (!desired.Equals(info.Work))
            {
                SetWorkArea(ref desired);
                fitExisting = true;
            }
            if (fitExisting) FitWindows(info.Monitor, desired);
            return true;
        }, 0);
    }
    public static void Restore(IEnumerable<Rect> areas)
    {
        foreach (var area in areas) { var copy = area; SetWorkArea(ref copy); }
    }
    private static void SetWorkArea(ref Rect area)
    {
        // SPIF_SENDCHANGE broadcasts synchronously. A WinForms SystemEvents
        // listener can then wait for this WPF dispatcher while SPI waits for the
        // listener, freezing the shell on an intermediate animation frame.
        if (SystemParametersInfo(0x2F, 0, ref area, 0))
            SendNotifyMessage(new nint(0xffff), 0x1A, new nint(0x2F), 0);
    }
    private static void FitWindows(Rect monitor, Rect work)
    {
        // Bring existing maximized windows into the new work area, and keep restored title bars reachable.
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != 0 || (GetWindowLongPtr(window, -20).ToInt64() & 0x80) != 0) return true;
            // Mosaic's ordinary settings windows need the same work-area fit as
            // other apps. Tool windows (menu, Dock and popups) were excluded above.
            if (!GetWindowRect(window, out var rect)) return true;
            DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int));
            if (cloaked != 0 || (!IsZoomed(window) && (GetWindowLongPtr(window, -16).ToInt64() & 0xC00000) == 0)) return true;
            if (rect.Left < monitor.Left - 16 || rect.Left >= monitor.Right || rect.Top < monitor.Top - 16 || rect.Top >= monitor.Bottom) return true;
            if (IsZoomed(window))
            {
                DwmGetWindowAttribute(window, 9, out Rect frame, Marshal.SizeOf<Rect>());
                var left = frame.Left - rect.Left; var right = rect.Right - frame.Right;
                var top = frame.Top - rect.Top; var bottom = rect.Bottom - frame.Bottom;
                var target = new Rect { Left = work.Left - left, Top = work.Top - top, Right = work.Right + right, Bottom = work.Bottom + bottom };
                if (!target.Equals(rect)) SetWindowPos(window, 0, target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top, 0x4234);
            }
            else if (rect.Top < work.Top)
            {
                var height = Math.Min(rect.Bottom - rect.Top, work.Bottom - work.Top);
                SetWindowPos(window, 0, rect.Left, work.Top, rect.Right - rect.Left, height, 0x4214);
            }
            return true;
        }, 0);
    }
    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect rect, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint parameter, ref Rect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SendNotifyMessage(nint window, uint message, nint parameter, nint value);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
}
