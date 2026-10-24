using System.Runtime.InteropServices;
using System.Text;
using Mosaic.Core.Search;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Services;

public sealed class NativeShell : Forms.NativeWindow, IDisposable
{
    private const int HotKeyId = 0x4D53;
    private readonly WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private int _paletteId;
    private uint _paletteModifiers, _paletteKey;
    private bool? _lastFullscreen;
    public event Action? DashboardRequested;
    public event Action? DesktopModeRequested;
    private readonly bool _desktopHotkey;
    public event Action? PaletteRequested;
    public Func<nint, bool>? CopyDataReceiver { get; set; }
    public event Action<bool>? FullscreenChanged;
    public event Action? ShellLayoutChanged;
    public bool HotKeyRegistered { get; }
    public bool PaletteHotkeyRegistered => _paletteId != 0;
    public string? PaletteHotkeyError { get; private set; }

    public NativeShell(SearchSettings? search = null)
    {
        CreateHandle(new Forms.CreateParams { Caption = "Mosaic message window", Parent = new nint(-3) });
        _desktopHotkey = RegisterHotKey(Handle, 0x4D56, 0x4003, 0x4D);
        HotKeyRegistered = RegisterHotKey(Handle, HotKeyId, 0x4000 | 0x0001 | 0x0002, 0x44); // Ctrl+Alt+D; no repeat.
        if (search is not null)
        {
            try { using var reservation = ReservePaletteHotkey(search); reservation.Commit(); }
            catch (InvalidOperationException ex) { PaletteHotkeyError = ex.Message; }
        }
        _callback = OnWinEvent;
        foreach (var eventId in new uint[] { 3, 0x800B })
        {
            var hook = SetWinEventHook(eventId, eventId, 0, _callback, 0, 0, 0);
            if (hook != 0) _hooks.Add(hook);
        }
    }

    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == 0x4D56) DesktopModeRequested?.Invoke();
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == HotKeyId) DashboardRequested?.Invoke();
        if (message.Msg == 0x0312 && _paletteId != 0 && message.WParam.ToInt32() == _paletteId) PaletteRequested?.Invoke();
        if (message.Msg == 0x4A && CopyDataReceiver?.Invoke(message.LParam) == true) { message.Result = 1; return; }
        base.WndProc(ref message);
    }

    public HotkeyReservation ReservePaletteHotkey(SearchSettings settings)
    {
        if (!settings.Enabled)
            return new(() => { if (_paletteId != 0) UnregisterHotKey(Handle, _paletteId); _paletteId = 0; PaletteHotkeyError = null; }, () => { });
        if (_paletteId != 0 && _paletteModifiers == settings.HotkeyModifiers && _paletteKey == settings.HotkeyVirtualKey)
            return new(() => { }, () => { });
        var id = _paletteId == 0x4D54 ? 0x4D55 : 0x4D54;
        if (!RegisterHotKey(Handle, id, settings.HotkeyModifiers | 0x4000, settings.HotkeyVirtualKey))
            throw new InvalidOperationException("搜索快捷键已被其他程序占用，请选择另一组；原绑定保持可用。");
        return new(() =>
        {
            if (_paletteId != 0) UnregisterHotKey(Handle, _paletteId);
            _paletteId = id; _paletteModifiers = settings.HotkeyModifiers; _paletteKey = settings.HotkeyVirtualKey; PaletteHotkeyError = null;
        }, () => UnregisterHotKey(Handle, id));
    }

    public sealed class HotkeyReservation(Action commit, Action cancel) : IDisposable
    {
        private bool _finished;
        public void Commit() { if (_finished) return; commit(); _finished = true; }
        public void Dispose() { if (!_finished) { cancel(); _finished = true; } }
    }

    public static string DescribeHotkey(SearchSettings settings) =>
        ((settings.HotkeyModifiers & 2) != 0 ? "Ctrl + " : "") + ((settings.HotkeyModifiers & 1) != 0 ? "Alt + " : "") +
        ((settings.HotkeyModifiers & 4) != 0 ? "Shift + " : "") + (settings.HotkeyVirtualKey == 0x20 ? "Space" : settings.HotkeyVirtualKey is >= 0x70 and <= 0x7B ? "F" + (settings.HotkeyVirtualKey - 0x6F) : ((char)settings.HotkeyVirtualKey).ToString());
    public static nint CaptureForeground() => GetForegroundWindow();
    public static void TryRestoreForeground(nint window) { if (window != 0 && IsWindow(window)) SetForegroundWindow(window); }
    public static void PlacePalette(nint window, nint previous, double width, double height)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(previous, 2), ref info)) return;
        var dpi = previous != 0 ? GetDpiForWindow(previous) : 96;
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;
        var w = Math.Min((int)(width * scale), info.Work.Right - info.Work.Left - (int)(32 * scale));
        var h = Math.Min((int)(height * scale), info.Work.Bottom - info.Work.Top - (int)(32 * scale));
        SetWindowPos(window, 0, info.Work.Left + (info.Work.Right - info.Work.Left - w) / 2,
            info.Work.Top + (info.Work.Bottom - info.Work.Top - h) / 3, w, h, 0x14);
    }

    private void OnWinEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time)
    {
        var foreground = GetForegroundWindow();
        if (eventId == 0x800B && (window != foreground || objectId != 0)) return;
        ShellLayoutChanged?.Invoke();
        var full = false;
        if (foreground != 0 && IsWindowVisible(foreground) && !IsIconic(foreground))
        {
            var className = new StringBuilder(128);
            GetClassName(foreground, className, className.Capacity);
            // Explorer's desktop also fills the monitor; it must not hide the desktop modules.
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                PublishFullscreen(false);
                return;
            }
            GetWindowThreadProcessId(foreground, out var processId);
            if (processId != Environment.ProcessId && GetWindowRect(foreground, out var rect))
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(MonitorFromWindow(foreground, 2), ref info))
                    full = rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
            }
        }
        PublishFullscreen(full);
    }

    private void PublishFullscreen(bool full)
    {
        if (_lastFullscreen == full) return;
        _lastFullscreen = full; FullscreenChanged?.Invoke(full);
    }

    public static void KeepAbove(nint window) { if (window != 0) SetWindowPos(window, new nint(-1), 0, 0, 0, 0, 0x213); }
    public static void PlaceBar(nint window, System.Drawing.Rectangle bounds)
    {
        var scale = GetDpiForWindow(window) / 96.0;
        if (scale <= 0) scale = 1;
        SetWindowPos(window, new nint(-1), bounds.Left, bounds.Top, bounds.Width, (int)Math.Ceiling(ShellWorkArea.BarHeight * scale), 0x50);
    }

    public static void StyleWindow(nint window, bool toolWindow, bool noActivate)
    {
        var style = GetWindowLongPtr(window, -20).ToInt64();
        if (toolWindow) style |= 0x80;
        if (noActivate) style |= 0x08000000;
        SetWindowLongPtr(window, -20, new nint(style));
        var dark = 1;
        DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
        var corners = 2;
        DwmSetWindowAttribute(window, 33, ref corners, sizeof(int));
    }

    public void Dispose()
    {
        if (_desktopHotkey) UnregisterHotKey(Handle, 0x4D56);
        if (HotKeyRegistered) UnregisterHotKey(Handle, HotKeyId);
        if (_paletteId != 0) UnregisterHotKey(Handle, _paletteId);
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        DestroyHandle();
    }

    private delegate void WinEventDelegate(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventDelegate callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
