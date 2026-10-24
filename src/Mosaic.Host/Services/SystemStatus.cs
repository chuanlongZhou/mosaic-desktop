using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Mosaic.Host.Services;

public sealed record SystemStatus(string NetworkGlyph, string NetworkDescription, string VolumeGlyph, string VolumeDescription,
    bool HasBattery, string BatteryText, string BatteryDescription)
{
    private static SystemStatus? _cached;
    private static long _sampled;
    public static SystemStatus Read()
    {
        if (_cached is not null && Environment.TickCount64 - _sampled < 3000) return _cached;
        var connected = Array.Empty<NetworkInterface>();
        try { connected = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToArray(); }
        catch (NetworkInformationException) { }
        var network = connected.Length == 0 ? "网络未连接" : string.Join(" · ", connected.Select(n => n.Name));
        var wireless = connected.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        var power = Forms.SystemInformation.PowerStatus;
        var hasBattery = (power.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) == 0 && power.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
        var percent = power.BatteryLifePercent is >= 0 and <= 1 ? $"{power.BatteryLifePercent:P0}" : "未知";
        var charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
        var (level, muted) = ReadVolume();
        _sampled = Environment.TickCount64;
        return _cached = new(connected.Length == 0 ? "\uEB55" : wireless ? "\uE701" : "\uE839", network,
            muted == true || level == 0 ? "\uE74F" : "\uE767", level is null ? "未检测到音频输出设备；点击打开声音设置" : muted == true ? "已静音" : $"音量 {level}%",
            hasBattery, (charging ? "⚡ " : "") + percent, (charging ? "已接通电源 · " : "电池供电 · ") + percent);
    }
    public static string InputLanguage(nint window)
    {
        var thread = GetWindowThreadProcessId(window, out _);
        var id = (int)(GetKeyboardLayout(thread).ToInt64() & 0xffff);
        try
        {
            var culture = CultureInfo.GetCultureInfo(id);
            if (culture.TwoLetterISOLanguageName == "zh")
            {
                var ime = ImmGetDefaultIMEWnd(window);
                if (ime != 0 && SendMessageTimeout(ime, 0x283, new nint(5), 0, 2, 30, out var open) != 0)
                    return open != 0 ? "中 · 拼" : "英 · 拼";
                return "中";
            }
            return culture.TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch (CultureNotFoundException) { return "输入"; }
    }
    private static (int? Level, bool? Muted) ReadVolume()
    {
        object? enumeratorObject = null; IDevice? device = null; object? endpoint = null;
        try
        {
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            ((IDeviceEnumerator)enumeratorObject!).GetDefaultAudioEndpoint(0, 1, out device);
            var iid = typeof(IEndpointVolume).GUID;
            device.Activate(ref iid, 23, 0, out endpoint);
            var volume = (IEndpointVolume)endpoint;
            volume.GetMasterVolumeLevelScalar(out var level); volume.GetMute(out var mute);
            return ((int)Math.Round(level * 100), mute);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return (null, null); }
        finally
        {
            if (endpoint is not null) Marshal.ReleaseComObject(endpoint);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumeratorObject is not null) Marshal.ReleaseComObject(enumeratorObject);
        }
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint mask, out nint devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice { void Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        void RegisterControlChangeNotify(nint callback); void UnregisterControlChangeNotify(nint callback); void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, nint context); void SetMasterVolumeLevelScalar(float level, nint context);
        void GetMasterVolumeLevel(out float level); void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, nint context); void SetChannelVolumeLevelScalar(uint channel, float level, nint context);
        void GetChannelVolumeLevel(uint channel, out float level); void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, nint context); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("imm32.dll")] private static extern nint ImmGetDefaultIMEWnd(nint window);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
