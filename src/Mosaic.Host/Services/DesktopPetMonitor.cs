using System.Runtime.InteropServices;
using System.Windows.Threading;
using Mosaic.Core;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Services;

/// <summary>Passive raw input: never registers a hotkey or consumes a user's key.</summary>
public sealed class DesktopPetMonitor : Forms.NativeWindow, IDisposable
{
    private readonly PetKeyboardFilter _keys = new();
    private readonly HashSet<nint> _windows = [];
    private readonly WinEventCallback _callback;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private nint _eventHook;
    private bool _disposed;
    public bool KeyboardAvailable { get; }
    public bool WindowEventsAvailable => _eventHook != 0;
    public event Action<PetTrigger>? Triggered;

    public DesktopPetMonitor(bool keyboard, bool windowEvents)
    {
        CreateHandle(new Forms.CreateParams { Caption = "Mosaic pet input", Parent = new nint(-3) });
        _callback = OnWindowEvent;
        if (keyboard)
        {
            var devices = new[] { new RawDevice { UsagePage = 1, Usage = 6, Flags = 0x100, Target = Handle } };
            KeyboardAvailable = RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>());
        }
        if (windowEvents)
        {
            _eventHook = SetWinEventHook(0x8001, 0x8002, 0, _callback, 0, 0, 2);
            EnumWindows((window, _) => { if (IsApplication(window)) _windows.Add(window); return true; }, 0);
        }
    }

    protected override void WndProc(ref Forms.Message message)
    {
        if (!_disposed && message.Msg == 0xFF)
        {
            // Keyboard RAWINPUT is bounded: 24-byte x64 header + 16-byte RAWKEYBOARD.
            uint size = (uint)Marshal.SizeOf<RawKeyboardInput>();
            if (GetRawInputData(message.LParam, 0x10000003, out var input, ref size, (uint)Marshal.SizeOf<RawHeader>()) == size && input.Header.Type == 1)
            {
                var trigger = _keys.Process(input.Key.VirtualKey, (input.Key.Flags & 1) == 0);
                if (trigger is { } action) Triggered?.Invoke(action);
            }
        }
        base.WndProc(ref message);
    }

    private void OnWindowEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time)
    {
        if (_disposed || window == 0 || objectId != 0 || childId != 0) return;
        if (eventId == 0x8002 && IsApplication(window)) _windows.Add(window);
        else if (eventId == 0x8001 && _windows.Remove(window))
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_disposed) Triggered?.Invoke(PetTrigger.AppClosed); }));
    }

    public static bool IsApplication(nint window)
    {
        if (!IsWindowVisible(window) || GetAncestor(window, 2) != window || GetWindowTextLength(window) == 0) return false;
        GetWindowThreadProcessId(window, out var process);
        if (process == Environment.ProcessId || window == GetShellWindow()) return false;
        var style = GetWindowLongPtr(window, -20).ToInt64();
        return (style & 0x80) == 0 && ((style & 0x40000) != 0 || GetWindow(window, 4) == 0);
    }

    public static double IdleSeconds()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000d : 0;
    }

    public static bool TryCursor(out System.Windows.Point position)
    {
        var success = GetCursorPos(out var point); position = new(point.X, point.Y); return success;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_eventHook != 0) { UnhookWinEvent(_eventHook); _eventHook = 0; }
        if (KeyboardAvailable)
        {
            var devices = new[] { new RawDevice { UsagePage = 1, Usage = 6, Flags = 1 } };
            RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>());
        }
        _windows.Clear(); Triggered = null; DestroyHandle();
    }

    private delegate bool EnumCallback(nint window, nint data);
    private delegate void WinEventCallback(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public nint Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboardInput { public RawHeader Header; public RawKeyboard Key; }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, out RawKeyboardInput data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
}
