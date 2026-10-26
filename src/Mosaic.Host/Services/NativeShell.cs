using System.Runtime.InteropServices;
using System.Text;
using Mosaic.Core.Search;
using Mosaic.Core.Models;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Services;

public sealed class NativeShell : Forms.NativeWindow, IDisposable
{
    private readonly WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private Dictionary<string, RegisteredShortcut> _registered = [];
    private Dictionary<int, string> _actions = [];
    private readonly Dictionary<string, string> _hotkeyErrors = [];
    private int _nextHotkeyId = 0x5000;
    private ShortcutSettings _shortcuts = new();
    private SearchSettings _searchOptions = new();
    private bool? _lastFullscreen;
    public event Action? DashboardRequested;
    public event Action? DesktopModeRequested;
    public event Action? LibraryRequested;
    public event Action? WorkspacesRequested;
    public event Action? EditLayoutRequested;
    public event Action? MenuBarPinRequested;
    public event Action? PaletteRequested;
    public Func<nint, bool>? CopyDataReceiver { get; set; }
    public event Action<bool>? FullscreenChanged;
    public event Action? ShellLayoutChanged;
    public bool HotKeyRegistered => _actions.ContainsValue("dashboard");
    public bool PaletteHotkeyRegistered => _actions.ContainsValue("palette");
    public string? PaletteHotkeyError => _hotkeyErrors.GetValueOrDefault("palette");
    public string ShortcutStatus => string.Join("\n", _hotkeyErrors.Values);
    public Func<ShortcutBinding, bool>? CaptureHotkey { get; set; }

    public NativeShell(SearchSettings? search = null, ShortcutSettings? shortcuts = null)
    {
        CreateHandle(new Forms.CreateParams { Caption = "Mosaic message window", Parent = new nint(-3) });
        _shortcuts = shortcuts ?? new(); _searchOptions = search ?? new() { Enabled = false };
        foreach (var (action, label, binding) in Desired(_shortcuts, _searchOptions))
        {
            var id = _nextHotkeyId++;
            if (RegisterHotKey(Handle, id, binding.Modifiers | 0x4000, binding.Key))
            { _registered[binding.Identity] = new(id, binding.Copy()); _actions[id] = action; }
            else _hotkeyErrors[action] = $"{label}：{DescribeHotkey(binding)} 已被占用，可在快捷键设置中修改。";
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
        if (message.Msg == 0x0312 && _actions.TryGetValue(message.WParam.ToInt32(), out var action))
        {
            var messageId = message.WParam.ToInt32();
            var binding = _registered.Values.FirstOrDefault(x => x.Id == messageId)?.Binding;
            if (binding is not null && CaptureHotkey?.Invoke(binding.Copy()) == true) return;
            switch (action)
            {
                case "desktop": DesktopModeRequested?.Invoke(); break;
                case "dashboard": DashboardRequested?.Invoke(); break;
                case "palette": PaletteRequested?.Invoke(); break;
                case "library": LibraryRequested?.Invoke(); break;
                case "workspaces": WorkspacesRequested?.Invoke(); break;
                case "edit": EditLayoutRequested?.Invoke(); break;
                case "menu-pin": MenuBarPinRequested?.Invoke(); break;
            }
        }
        if (message.Msg == 0x4A && CopyDataReceiver?.Invoke(message.LParam) == true) { message.Result = 1; return; }
        base.WndProc(ref message);
    }

    public HotkeyReservation ReservePaletteHotkey(SearchSettings settings)
    {
        return ReserveHotkeys(new AppSettings { Search = settings, Shortcuts = _shortcuts });
    }

    public HotkeyReservation ReserveHotkeys(AppSettings settings)
    {
        settings.Shortcuts.Validate(settings.Search.HotkeyModifiers, settings.Search.HotkeyVirtualKey, settings.Search.Enabled && settings.Search.HotkeyEnabled);
        var desired = Desired(settings.Shortcuts, settings.Search).ToArray();
        var next = new Dictionary<string, RegisteredShortcut>();
        var nextActions = new Dictionary<int, string>();
        var intended = Desired(_shortcuts, _searchOptions).ToDictionary(x => x.Action, x => x.Binding.Identity);
        var retainedErrors = new Dictionary<string, string>();
        var acquired = new List<int>();
        try
        {
            foreach (var (action, label, binding) in desired)
            {
                if (!_registered.TryGetValue(binding.Identity, out var registered))
                {
                    // An unrelated appearance save must not fail because a shortcut was already unavailable at startup.
                    if (intended.GetValueOrDefault(action) == binding.Identity && _hotkeyErrors.TryGetValue(action, out var error))
                    { retainedErrors[action] = error; continue; }
                    var id = _nextHotkeyId++;
                    if (!RegisterHotKey(Handle, id, binding.Modifiers | 0x4000, binding.Key))
                        throw new InvalidOperationException($"“{label}”的 {DescribeHotkey(binding)} 已被其他程序占用，原快捷键保持可用。");
                    acquired.Add(id); registered = new(id, binding.Copy());
                }
                next.Add(binding.Identity, registered); nextActions.Add(registered.Id, action);
            }
        }
        catch { foreach (var id in acquired) UnregisterHotKey(Handle, id); throw; }
        return new(() =>
        {
            foreach (var (identity, registered) in _registered)
                if (!next.ContainsKey(identity)) UnregisterHotKey(Handle, registered.Id);
            _registered = next; _actions = nextActions; _hotkeyErrors.Clear();
            foreach (var (action, error) in retainedErrors) _hotkeyErrors[action] = error;
            _shortcuts = settings.Shortcuts; _searchOptions = settings.Search.Copy();
        }, () => { foreach (var id in acquired) UnregisterHotKey(Handle, id); });
    }

    private static IEnumerable<(string Action, string Label, ShortcutBinding Binding)> Desired(ShortcutSettings settings, SearchSettings search)
    {
        foreach (var entry in settings.Entries()) if (entry.Binding.Enabled) yield return entry;
        if (search.Enabled && search.HotkeyEnabled) yield return ("palette", "全局搜索", new() { Modifiers = search.HotkeyModifiers, Key = search.HotkeyVirtualKey });
    }
    private sealed record RegisteredShortcut(int Id, ShortcutBinding Binding);

    public sealed class HotkeyReservation(Action commit, Action cancel) : IDisposable
    {
        private bool _finished;
        public void Commit() { if (_finished) return; commit(); _finished = true; }
        public void Dispose() { if (!_finished) { cancel(); _finished = true; } }
    }

    public static string DescribeHotkey(SearchSettings settings) => DescribeHotkey(new ShortcutBinding { Enabled = settings.HotkeyEnabled, Modifiers = settings.HotkeyModifiers, Key = settings.HotkeyVirtualKey });
    public static string DescribeHotkey(ShortcutBinding binding) => !binding.Enabled ? "未绑定" :
        ((binding.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((binding.Modifiers & 1) != 0 ? "Alt + " : "") +
        ((binding.Modifiers & 4) != 0 ? "Shift + " : "") + (binding.Key == 0x20 ? "Space" : binding.Key is >= 0x70 and <= 0x7B ? "F" + (binding.Key - 0x6F) : ((char)binding.Key).ToString());
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
        GetWindowThreadProcessId(foreground, out var processId);
        PublishFullscreen(processId != Environment.ProcessId && IsFullscreenApplication(foreground));
    }

    public static bool IsFullscreenApplication(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window)) return false;
        var className = new StringBuilder(128);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        // Maximized applications can cover the monitor when the taskbar is hidden.
        // A retained caption/resize frame distinguishes them from borderless fullscreen.
        var style = GetWindowLongPtr(window, -16).ToInt64();
        if (IsZoomed(window) && (style & 0x00C40000) != 0) return false;
        if (DwmGetWindowAttribute(window, 9, out var rect, Marshal.SizeOf<Rect>()) < 0 && !GetWindowRect(window, out rect)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, 2), ref info) &&
            rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
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

    public static void StyleWindow(nint window, bool toolWindow, bool noActivate, bool transparent = false)
    {
        var style = GetWindowLongPtr(window, -20).ToInt64();
        if (toolWindow) style |= 0x80;
        if (noActivate) style |= 0x08000000;
        SetWindowLongPtr(window, -20, new nint(style));
        var dark = 1;
        DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
        // Per-pixel transparent windows must not opt into a rectangular DWM frame.
        var corners = transparent ? 1 : 2; // DWMWCP_DONOTROUND / DWMWCP_ROUND
        DwmSetWindowAttribute(window, 33, ref corners, sizeof(int));
        if (transparent)
        {
            var disabled = 1; // DWMNCRP_DISABLED; DWMSBT_NONE
            var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(window, 2, ref disabled, sizeof(int));
            DwmSetWindowAttribute(window, 34, ref noBorder, sizeof(int));
            DwmSetWindowAttribute(window, 38, ref disabled, sizeof(int));
        }
    }

    public void Dispose()
    {
        foreach (var shortcut in _registered.Values) UnregisterHotKey(Handle, shortcut.Id);
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
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect rect, int size);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
}
