using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Mosaic.Host.Services;

// The guard owns restoration independently of the WPF process, including forced termination.
public sealed class DesktopOverlay : IDisposable
{
    private List<ShellWorkArea.Rect> _workAreas = [];
    private int _dockHeight;
    private int _menuHeight;
    private long _lastRefresh;
    private uint? _taskbarState;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private EventWaitHandle? _stop;
    private Process? _guard;
    private readonly Dictionary<nint, bool> _original = [];
    private StartMenuKeyGuard? _startMenuKeys;
    public bool Enabled { get; private set; }
    public DesktopOverlay() => _timer.Tick += (_, _) => { HideShell(); ShellWorkArea.Apply(_dockHeight, false, _menuHeight); };

    public void Enable()
    {
        if (Enabled) return;
        var name = "Local\\MosaicShellGuard-" + Guid.NewGuid().ToString("N");
        _stop = new(false, EventResetMode.ManualReset, name);
        var readyName = name + "-ready";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--shell-guard", Environment.ProcessId.ToString(), name, readyName }) start.ArgumentList.Add(arg);
        _guard = Process.Start(start) ?? throw new InvalidOperationException("无法启动桌面恢复保护。");
        if (!ready.WaitOne(TimeSpan.FromSeconds(5))) { _stop.Set(); throw new InvalidOperationException("桌面恢复保护未就绪，保留 Windows 桌面。"); }
        try
        {
            _workAreas = ShellWorkArea.Capture();
            _taskbarState = AppBarReservation.TaskbarState();
            AppBarReservation.SetTaskbarState(_taskbarState.Value | 1);
            Enabled = true;
            _startMenuKeys = new(); _startMenuKeys.SetEnabled(true);
            HideShell(); _timer.Start();
        }
        catch { Dispose(); throw; }
    }

    internal static List<nint> ShellWindows()
    {
        var result = new List<nint>();
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(128); GetClassName(window, name, 128);
            if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") result.Add(window);
            var icons = FindWindowEx(window, 0, "SHELLDLL_DefView", null);
            if (icons != 0)
            {
                var folder = FindWindowEx(icons, 0, "SysListView32", "FolderView");
                if (folder != 0) result.Add(folder);
            }
            return true;
        }, 0);
        return result;
    }
    private void HideShell()
    {
        foreach (var window in ShellWindows())
        {
            _original.TryAdd(window, IsWindowVisible(window));
            if (IsWindowVisible(window)) ShowWindow(window, 0);
        }
    }
    public void ConfigureDock(bool visible, bool autoHide, int iconSize, bool menuBarAutoHide)
    {
        _dockHeight = visible && !autoHide ? (int)Math.Ceiling(60 * iconSize / 40.0 + 24) : 0;
        _menuHeight = menuBarAutoHide ? 0 : ShellWorkArea.BarHeight;
        if (Enabled) ShellWorkArea.Apply(_dockHeight, false, _menuHeight);
    }
    public void RefreshLayout()
    {
        if (!Enabled || Environment.TickCount64 - _lastRefresh < 150) return;
        _lastRefresh = Environment.TickCount64;
        ShellWorkArea.Apply(_dockHeight, true, _menuHeight);
    }
    public void Dispose()
    {
        _timer.Stop();
        _startMenuKeys?.Dispose(); _startMenuKeys = null;

        foreach (var (window, visible) in _original) if (visible) ShowWindow(window, 5);
        _original.Clear();
        if (_taskbarState is uint state) AppBarReservation.SetTaskbarState(state);
        _taskbarState = null;
        if (_workAreas.Count > 0) ShellWorkArea.Restore(_workAreas);
        _workAreas.Clear();
        _stop?.Set();
        if (_guard is not null) { _guard.WaitForExit(3000); _guard.Dispose(); _guard = null; }
        _stop?.Dispose(); _stop = null; Enabled = false;
    }
    public static void RunGuard(int ownerId, string eventName, string readyName)
    {
        using var stop = EventWaitHandle.OpenExisting(eventName);
        using var ready = EventWaitHandle.OpenExisting(readyName);
        using var owner = Process.GetProcessById(ownerId);
        var original = ShellWindows().ToDictionary(w => w, IsWindowVisible);
        var workAreas = ShellWorkArea.Capture();
        var taskbarState = AppBarReservation.TaskbarState();
        ready.Set();
        while (!stop.WaitOne(500) && !owner.HasExited)
            foreach (var window in ShellWindows()) original.TryAdd(window, IsWindowVisible(window));
        foreach (var (window, visible) in original) if (visible) ShowWindow(window, 5);
        AppBarReservation.SetTaskbarState(taskbarState);
        ShellWorkArea.Restore(workAreas);
    }
    private delegate bool EnumCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
}
