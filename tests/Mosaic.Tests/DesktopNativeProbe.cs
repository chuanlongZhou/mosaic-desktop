using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class DesktopNativeProbe
{
    public static void Print()
    {
        var windows = new List<object>();
        EnumWindows((window, _) =>
        {
            var name = Class(window); var title = Title(window);
            if (name is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow") && !title.StartsWith("Mosaic ·")) return true;
            windows.Add(Describe(window));
            EnumChildWindows(window, (child, _) => { windows.Add(Describe(child)); return true; }, 0);
            return true;
        }, 0);
        Console.WriteLine(JsonSerializer.Serialize(windows, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static object Describe(nint window)
    {
        GetWindowRect(window, out var rect);
        DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int));
        return new { Handle = window.ToInt64(), Parent = GetParent(window).ToInt64(), Class = Class(window), Title = Title(window), Visible = IsWindowVisible(window), Style = GetWindowLongPtr(window, -16).ToInt64().ToString("X"), ExStyle = GetWindowLongPtr(window, -20).ToInt64().ToString("X"), Rect = new[] {rect.Left, rect.Top, rect.Right, rect.Bottom}, Cloaked = cloaked };
    }
    private static string Class(nint window) { var text = new StringBuilder(128); GetClassName(window, text, text.Capacity); return text.ToString(); }
    private static string Title(nint window) { var text = new StringBuilder(128); GetWindowText(window, text, text.Capacity); return text.ToString(); }
    private delegate bool EnumCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
