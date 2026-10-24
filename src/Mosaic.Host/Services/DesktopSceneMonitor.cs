using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Mosaic.Host.Services;

public readonly record struct DesktopScene(bool DesktopIsForeground, bool HasMaximizedApplication)
{
    public bool AllowsAutoHide => !DesktopIsForeground && HasMaximizedApplication;
}

public sealed class DesktopSceneMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
    public DesktopScene Current { get; private set; }
    public event Action<DesktopScene>? Changed;
    public DesktopSceneMonitor() => _timer.Tick += (_, _) => Refresh();
    public void SetEnabled(bool enabled)
    {
        if (enabled) { Refresh(); _timer.Start(); }
        else _timer.Stop();
    }
    public void Refresh()
    {
        var next = Capture(Current);
        if (next == Current) return;
        Current = next; Changed?.Invoke(next);
    }
    public static DesktopScene Capture(DesktopScene? previous = null)
    {
        var foreground = NativeShell.CaptureForeground();
        var root = GetAncestor(foreground, 2);
        var desktop = IsDesktop(root == 0 ? foreground : root);
        GetWindowThreadProcessId(foreground, out var process);
        if (process == Environment.ProcessId && (GetWindowLongPtr(foreground, -20).ToInt64() & 0x80) != 0 && previous is { } last)
            desktop = last.DesktopIsForeground;
        var maximized = false;
        EnumWindows((window, _) =>
        {
            if (IsMaximizedApplication(window)) { maximized = true; return false; }
            return true;
        }, 0);
        return new(desktop, maximized);
    }
    public static bool IsMaximizedApplication(nint window)
    {
        if (!IsWindowVisible(window) || IsIconic(window) || IsDesktop(window)) return false;
        if ((GetWindowLongPtr(window, -20).ToInt64() & 0x80) != 0) return false;
        DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int));
        return cloaked == 0 && (IsZoomed(window) || IsFullscreen(window));
    }
    private static bool IsDesktop(nint window)
    {
        if (window == 0) return false;
        var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
    private static bool IsFullscreen(nint window)
    {
        if (!GetWindowRect(window, out var rect)) return false;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor) &&
            rect.Left <= monitor.Monitor.Left && rect.Top <= monitor.Monitor.Top && rect.Right >= monitor.Monitor.Right && rect.Bottom >= monitor.Monitor.Bottom;
    }
    public void Dispose() => _timer.Stop();
    private delegate bool EnumCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public ShellWorkArea.Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out ShellWorkArea.Rect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
