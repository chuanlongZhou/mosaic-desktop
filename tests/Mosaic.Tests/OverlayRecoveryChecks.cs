using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Mosaic.Host.Views;
using Mosaic.Host.Services;

internal static class OverlayRecoveryChecks
{
    public static async Task RunAsync()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !IsWindowVisible(taskbar)) throw new Exception("Run shell recovery verification from Windows mode with a visible taskbar.");
        var executable = Path.GetFullPath(@"src\Mosaic.Host\bin\Release\net10.0-windows\Mosaic.exe");
        var data = Path.Combine(Path.GetTempPath(), "Mosaic.Tests", Guid.NewGuid().ToString("N"));
        var originalAreas = ShellWorkArea.Capture();
        var originalTaskbarState = AppBarReservation.TaskbarState();
        Process Launch(params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
            foreach (var arg in arguments.Concat(new[] { "--data-dir", data })) start.ArgumentList.Add(arg);
            return Process.Start(start)!;
        }
        static async Task Until(Func<bool> condition, string error)
        {
            for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(100); }
            throw new Exception(error);
        }
        using (var normal = Launch("--overlay"))
        {
            try
            {
                await Until(() => !IsWindowVisible(taskbar), "Overlay did not hide the Windows taskbar.");
                await Until(() => !ShellWorkArea.Capture().SequenceEqual(originalAreas), "Overlay did not update screen work areas.");
                await Until(() => { var handle = FindWindow(null, "Mosaic · Dock"); return handle != 0 && (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0; }, "Dock did not initialize as a topmost window.");
                await Until(() => FindWindow(null, "Mosaic · 时间与菜单栏") != 0, "Top menu did not initialize.");
                nint DesktopCanvas()
                {
                    nint result = 0;
                    EnumChildWindows(DesktopSurface.FindDesktopHost(), (window, _) =>
                    {
                        var title = new StringBuilder(128); GetWindowText(window, title, title.Capacity);
                        if (title.ToString() == "Mosaic · 桌面控件层") result = window;
                        return true;
                    }, 0);
                    return result;
                }
                await Until(() => DesktopCanvas() != 0, "No child canvas was attached to the Explorer desktop.");
                var canvas = DesktopCanvas();
                var desktopView = DesktopSurface.FindDesktopHost();
                var hostClass = new StringBuilder(128); GetClassName(desktopView, hostClass, hostClass.Capacity);
                if (hostClass.ToString() != "SHELLDLL_DefView" || !IsWindowVisible(desktopView) || (GetWindowLongPtr(desktopView, -16).ToInt64() & 0x08000000) != 0)
                    throw new Exception("Desktop composition host is hidden, disabled or not the shell view.");
                var icons = FindWindowEx(desktopView, 0, "SysListView32", "FolderView");
                if (icons == 0 || IsWindowVisible(icons)) throw new Exception("Overlay did not hide only the original desktop icons.");
                if (GetParent(canvas) != DesktopSurface.FindDesktopHost() || (GetWindowLongPtr(canvas, -16).ToInt64() & 0x40000000) == 0 || (GetWindowLongPtr(canvas, -20).ToInt64() & 8) != 0)
                    throw new Exception("Desktop canvas is not a non-topmost WS_CHILD of Explorer.");
                if (FindWindow(null, "Mosaic · 桌面控件层") != 0 || FindWindow(null, "Mosaic · 随手记") != 0)
                    throw new Exception("Desktop controls leaked into top-level windows.");
                var probeStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                probeStart.ArgumentList.Add("--window-probe-max");
                using (var probe = Process.Start(probeStart)!)
                {
                    try
                    {
                        await Until(() => { probe.Refresh(); return probe.MainWindowHandle != 0; }, "Maximized probe did not open.");
                        await Until(() => IsZoomed(probe.MainWindowHandle), "Probe did not maximize.");
                        await Until(() => !IsWindowVisible(FindWindow(null, "Mosaic · 时间与菜单栏")), "Top menu did not auto-hide for the maximized app.");
                        DwmGetWindowAttribute(probe.MainWindowHandle, 9, out var frame, Marshal.SizeOf<ShellWorkArea.Rect>());
                        var area = ShellWorkArea.Capture().First(a => frame.Left >= a.Left - 2 && frame.Left < a.Right);
                        GetWindowRect(FindWindow(null, "Mosaic · 时间与菜单栏"), out var hiddenBar);
                        if (Math.Abs(frame.Top - hiddenBar.Top) > 2) throw new Exception("Hidden top menu still reserved screen space.");
                        if (Math.Abs(frame.Top - area.Top) > 2 || Math.Abs(frame.Bottom - area.Bottom) > 2)
                            throw new Exception($"Maximized app does not fit work area: top/bottom {frame.Top}/{frame.Bottom}, expected {area.Top}/{area.Bottom}.");
                        var dock = FindWindow(null, "Mosaic · Dock");
                        if (dock == 0 || (GetWindowLongPtr(dock, -20).ToInt64() & 8) == 0) throw new Exception("Dock lost topmost state while another application was maximized.");
                        var hit = WindowFromPoint(new NativePoint { X = frame.Left + 150, Y = frame.Top + 180 });
                        if (GetAncestor(hit, 2) != probe.MainWindowHandle) throw new Exception("A desktop control covers the maximized application.");
                        using var managementRequest = Launch("--dashboard");
                        await Until(() => { var handle = FindWindow(null, "Mosaic · 设置与插件"); return handle != 0 && IsWindowVisible(handle); }, "Management activation was blocked by shell layout updates.");
                        Console.WriteLine($"  Explorer child canvas stays behind maximized app; work area {area.Top}–{area.Bottom}; Dock retains topmost style.");
                    }
                    finally { probe.CloseMainWindow(); await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                }
                using var exit = Launch("--exit");
                await normal.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Until(() => IsWindowVisible(taskbar), "Normal exit did not restore the taskbar.");
                if (!IsWindowVisible(desktopView) || !IsWindowVisible(icons)) throw new Exception("Normal exit did not preserve the desktop view and restore its icons.");
                await Until(() => ShellWorkArea.Capture().SequenceEqual(originalAreas), "Normal exit did not restore work areas.");
                if (AppBarReservation.TaskbarState() != originalTaskbarState) throw new Exception("Normal exit did not restore the original taskbar auto-hide setting.");
            }
            finally { if (!normal.HasExited) { normal.Kill(); await normal.WaitForExitAsync(); } }
        }
        using (var crashed = Launch("--overlay"))
        {
            try
            {
                await Until(() => !IsWindowVisible(taskbar), "Overlay did not hide the taskbar before crash verification.");
                crashed.Kill(); await crashed.WaitForExitAsync();
                await Until(() => IsWindowVisible(taskbar), "Guard did not restore the taskbar after forced termination.");
                await Until(() => ShellWorkArea.Capture().SequenceEqual(originalAreas), "Guard did not restore work areas after termination.");
                if (AppBarReservation.TaskbarState() != originalTaskbarState) throw new Exception("Guard did not restore the original taskbar auto-hide setting.");
            }
            finally { if (!crashed.HasExited) { crashed.Kill(); await crashed.WaitForExitAsync(); } }
        }
        Console.WriteLine("  Windows taskbar restored after graceful exit and forced termination.");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    private delegate bool EnumCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out ShellWorkArea.Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out ShellWorkArea.Rect rect, int size);
}
