using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mosaic.Host.Services;

public enum StartMenuKeyAction { Pass, Block, MaskWindowsRelease }

// Tracks only the modifier sequence needed to distinguish bare Win from Win+D,
// Win+E, etc. It never stores text or reports keyboard input elsewhere.
public sealed class StartMenuKeyFilter
{
    private int _windowsKeys;
    private bool _usedChord, _blockedEscape;
    public StartMenuKeyAction Process(uint key, bool down, bool control = false, bool shift = false, bool alt = false)
    {
        var bit = key == 0x5B ? 1 : key == 0x5C ? 2 : 0;
        if (bit != 0)
        {
            if (down)
            {
                if (_windowsKeys == 0) _usedChord = false;
                _windowsKeys |= bit;
                return StartMenuKeyAction.Pass;
            }
            var wasPressed = (_windowsKeys & bit) != 0;
            _windowsKeys &= ~bit;
            return wasPressed && _windowsKeys == 0 && !_usedChord ? StartMenuKeyAction.MaskWindowsRelease : StartMenuKeyAction.Pass;
        }
        if (down && _windowsKeys != 0) _usedChord = true;
        if (key == 0x1B)
        {
            if (down && control && !shift && !alt) _blockedEscape = true;
            if (_blockedEscape)
            {
                if (!down) _blockedEscape = false;
                return StartMenuKeyAction.Block;
            }
        }
        return StartMenuKeyAction.Pass;
    }
}

public sealed class StartMenuKeyGuard : IDisposable
{
    private HookThread? _state;
    public bool IsEnabled => _state is { StopRequested: false };
    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        if (!enabled) { Dispose(); return; }
        var state = new HookThread();
        _state = state;
        state.Thread = new Thread(() => Run(state)) { IsBackground = true, Name = "Mosaic start-menu key guard" };
        state.Thread.Start();
        if (!state.Ready.Task.Wait(TimeSpan.FromSeconds(2))) { Dispose(); throw new InvalidOperationException("开始菜单按键拦截未能就绪。"); }
        var error = state.Ready.Task.Result;
        if (error != 0) { Dispose(); throw new Win32Exception(error, "无法启用开始菜单按键拦截。"); }
    }
    private static void Run(HookThread state)
    {
        state.Id = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0); // Ensure PostThreadMessage can wake this thread.
        state.Callback = (code, message, data) =>
        {
            if (code < 0 || state.StopRequested) return CallNextHookEx(0, code, message, data);
            var input = Marshal.PtrToStructure<KeyboardHook>(data);
            if (input.ExtraInfo == Marker) return CallNextHookEx(0, code, message, data);
            var down = message == 0x0100 || message == 0x0104;
            var up = message == 0x0101 || message == 0x0105;
            if (!down && !up) return CallNextHookEx(0, code, message, data);
            var action = state.Filter.Process(input.Key, down, IsDown(0x11), IsDown(0x10), IsDown(0x12));
            if (action == StartMenuKeyAction.Block) return 1;
            if (action == StartMenuKeyAction.MaskWindowsRelease)
            {
                // Deliver an unused menu-mask key before the Win release. This
                // cancels Start without swallowing a modifier's key-up event.
                var inputs = new[] { Key(0xE8, false), Key(0xE8, true), Key((ushort)input.Key, true) };
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>()) == inputs.Length) return 1;
            }
            return CallNextHookEx(0, code, message, data);
        };
        var hook = SetWindowsHookEx(13, state.Callback, GetModuleHandle(null), 0);
        state.Ready.TrySetResult(hook == 0 ? Marshal.GetLastWin32Error() : 0);
        if (hook == 0) return;
        try
        {
            while (!state.StopRequested && GetMessage(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        finally { UnhookWindowsHookEx(hook); }
    }
    private static readonly nuint Marker = 0x4D4F5341;
    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static NativeInput Key(ushort key, bool up) => new() { Type = 1, Data = new InputData { Keyboard = new() { Key = key, Flags = (up ? 2u : 0u) | (key is 0x5B or 0x5C ? 1u : 0u), ExtraInfo = Marker } } };
    public void Dispose()
    {
        var state = _state;
        if (state is null) return;
        state.StopRequested = true;
        if (state.Id != 0) PostThreadMessage(state.Id, 0x0012, 0, 0);
        state.Thread?.Join(1000);
        _state = null;
    }
    private sealed class HookThread
    {
        public readonly StartMenuKeyFilter Filter = new();
        public readonly TaskCompletionSource<int> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool StopRequested;
        public uint Id;
        public Thread? Thread;
        public HookCallback? Callback;
    }
    private delegate nint HookCallback(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardHook { public uint Key, Scan, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int hook, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
}
