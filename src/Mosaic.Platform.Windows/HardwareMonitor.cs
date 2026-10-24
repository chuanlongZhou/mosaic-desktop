using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Mosaic.Platform.Windows;

public sealed record HardwareSensor(string Name, string Type, double Value, string Source);
public sealed record HardwareSnapshot(double? GpuPercent, double? GpuMemoryUsed, double? GpuMemoryTotal, string GpuName,
    IReadOnlyList<HardwareSensor> Sensors, string SensorStatus);

/// <summary>Driver counters and optional existing sensor providers. Never installs drivers or elevates.</summary>
public sealed class HardwareMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly GpuCounters _gpu = new();
    private readonly NvidiaReader _nvidia = new();
    private Task<(HardwareSensor[] Sensors, string Status)>? _sensorTask;
    private (HardwareSensor[] Sensors, string Status) _last = ([], "正在检测硬件传感器…");
    private DateTime _lastProbe;
    private bool _disposed;
    public HardwareSnapshot Read()
    {
        lock (_gate)
        {
            if (_disposed) return new(null, null, null, "GPU", [], "监控已停止");
            if (_sensorTask?.IsCompleted == true)
            {
                try { _last = _sensorTask.GetAwaiter().GetResult(); }
                catch (Exception ex) { _last = ([], "传感器读取不可用：" + ex.Message); }
                _sensorTask = null; _lastProbe = DateTime.UtcNow;
            }
            if (_sensorTask is null && (DateTime.UtcNow - _lastProbe).TotalSeconds >= (_last.Sensors.Length > 0 ? 5 : 30))
                _sensorTask = Task.Run(ReadSensors);
            var (load, memory) = _gpu.Read();
            var vendor = _nvidia.Read();
            var sensors = _last.Sensors.ToList();
            if (vendor.Temperature is double temperature) sensors.Add(new(vendor.Name + " 核心", "Temperature", temperature, "NVIDIA NVML"));
            var status = _sensorTask is { IsCompleted: false } && _lastProbe == default ? "正在检测传感器；基础监控可正常使用" : _last.Status;
            if (sensors.Count > 0)
                status = "已读取：" + string.Join("、", sensors.Select(x => x.Source).Distinct()) +
                    (sensors.Any(x => x.Type == "Fan") ? "" : "；未检测到风扇 RPM，需设备接口或已有硬件监控 WMI 提供者");
            return new(vendor.Load ?? load, vendor.Used ?? memory, vendor.Total, vendor.Name, sensors, status);
        }
    }
    private static (HardwareSensor[] Sensors, string Status) ReadSensors()
    {
        var readings = new List<HardwareSensor>();
        foreach (var provider in new[] { "LibreHardwareMonitor", "OpenHardwareMonitor" })
        {
            Query(@"root\" + provider, "SELECT Name,SensorType,Value FROM Sensor WHERE SensorType='Temperature' OR SensorType='Fan'", row =>
            {
                object? raw = row.Value; if (raw is null) return;
                var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                string type = row.SensorType; string name = row.Name;
                if (double.IsFinite(value) && (type == "Temperature" ? value is >= -50 and <= 250 : value is >= 0 and <= 100000))
                    readings.Add(new(name, type, value, provider));
            });
            if (readings.Count > 0) break;
        }
        if (!readings.Any(x => x.Type == "Temperature"))
            Query(@"root\WMI", "SELECT InstanceName,CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", row =>
            {
                var value = Convert.ToDouble(row.CurrentTemperature, CultureInfo.InvariantCulture) / 10 - 273.15;
                if (value is >= -50 and <= 200) readings.Add(new("ACPI 热区", "Temperature", value, "Windows ACPI"));
            });
        return (readings.ToArray(), readings.Count > 0 ? "已连接：" + string.Join("、", readings.Select(x => x.Source).Distinct()) :
            "设备未开放温度 / 风扇传感器。可读取正在运行的 Libre Hardware Monitor / Open Hardware Monitor 的 WMI 数据。");
    }
    private static void Query(string scope, string query, Action<dynamic> read)
    {
        object? locator = null, services = null, results = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            services = ((dynamic)locator!).ConnectServer(".", scope);
            results = ((dynamic)services).ExecQuery(query, "WQL", 48);
            foreach (var item in (System.Collections.IEnumerable)results)
            {
                try { read(item); }
                catch (Exception ex) when (ex is COMException or InvalidCastException or FormatException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { Release(item); }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally { Release(results); Release(services); Release(locator); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose() { lock (_gate) { _disposed = true; _gpu.Dispose(); _nvidia.Dispose(); } }

    private sealed class GpuCounters : IDisposable
    {
        private nint _query, _load, _memory;
        private bool _initialized;
        public (double? Load, double? Memory) Read()
        {
            if (!_initialized)
            {
                _initialized = true;
                if (PdhOpenQuery(null, 0, out _query) == 0)
                {
                    PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _load);
                    PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out _memory);
                    PdhCollectQueryData(_query);
                }
                return (null, null);
            }
            if (_query == 0 || PdhCollectQueryData(_query) != 0) return (null, null);
            var engines = Values(_load);
            // Sum processes sharing one physical engine, then take the busiest engine; engines run concurrently.
            var grouped = engines.Where(x => x.Name.Contains("luid_", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.Name[x.Name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase)..].Split('#')[0])
                .Select(x => x.Sum(v => v.Value)).ToArray();
            var memory = Values(_memory).Where(x => !x.Name.Equals("_Total", StringComparison.OrdinalIgnoreCase)).ToArray();
            return (grouped.Length > 0 ? Math.Clamp(grouped.Max(), 0, 100) : null, memory.Length > 0 ? memory.Sum(x => x.Value) : null);
        }
        private static List<(string Name, double Value)> Values(nint counter)
        {
            var values = new List<(string, double)>();
            if (counter == 0) return values;
            uint size = 0, count = 0;
            PdhGetFormattedCounterArray(counter, 0x8200, ref size, out count, 0);
            if (size == 0 || size > 8 * 1024 * 1024) return values;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, 0x8200, ref size, out count, buffer) != 0) return values;
                var stride = Marshal.SizeOf<CounterValue>();
                if ((long)count * stride > size) return values;
                for (var i = 0; i < count; i++)
                {
                    var entry = Marshal.PtrToStructure<CounterValue>(buffer + i * stride);
                    if (entry.Status is 0 or 1 && double.IsFinite(entry.Value) && entry.Value >= 0)
                        values.Add((Marshal.PtrToStringUni(entry.Name) ?? "", entry.Value));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return values;
        }
        public void Dispose() { if (_query != 0) { PdhCloseQuery(_query); _query = 0; } }
        [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public nint Name; public uint Status; public double Value; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] private static extern uint PdhOpenQuery(string? source, nuint user, out nint query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(nint query, string path, nuint user, out nint counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, out uint count, nint buffer);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
    }

    private sealed class NvidiaReader : IDisposable
    {
        private nint _library;
        private bool _initialized;
        private Init? _shutdown;
        private Count? _count;
        private Device? _device;
        private Load? _load;
        private Memory? _memory;
        private Temperature? _temperature;
        private Name? _name;
        private T? Export<T>(string name) where T : Delegate => NativeLibrary.TryGetExport(_library, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;
        public (double? Load, double? Used, double? Total, double? Temperature, string Name) Read()
        {
            if (!_initialized)
            {
                _initialized = true;
                foreach (var path in new[] { Path.Combine(Environment.SystemDirectory, "nvml.dll"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll") })
                    if (NativeLibrary.TryLoad(path, out _library)) break;
                if (_library != 0)
                {
                    if (Export<Init>("nvmlInit_v2") is not { } init || init() != 0) { NativeLibrary.Free(_library); _library = 0; }
                    else
                    {
                        _shutdown = Export<Init>("nvmlShutdown"); _count = Export<Count>("nvmlDeviceGetCount_v2"); _device = Export<Device>("nvmlDeviceGetHandleByIndex_v2");
                        _load = Export<Load>("nvmlDeviceGetUtilizationRates"); _memory = Export<Memory>("nvmlDeviceGetMemoryInfo");
                        _temperature = Export<Temperature>("nvmlDeviceGetTemperature"); _name = Export<Name>("nvmlDeviceGetName");
                    }
                }
            }
            (double? Load, double? Used, double? Total, double? Temperature, string Name) best = (null, null, null, null, "GPU");
            if (_library == 0 || _count is null || _device is null || _count(out var count) != 0) return best;
            for (uint i = 0; i < Math.Min(count, 16); i++)
            {
                if (_device(i, out var device) != 0) continue;
                double? load = _load is not null && _load(device, out var usage) == 0 ? usage.Gpu : null;
                double? used = null, total = null, temperature = null;
                if (_memory is not null && _memory(device, out var memory) == 0) { used = memory.Used; total = memory.Total; }
                if (_temperature is not null && _temperature(device, 0, out var degrees) == 0 && degrees <= 200) temperature = degrees;
                var name = new StringBuilder(128); if (_name is not null) _name(device, name, 128);
                if (i == 0 || (load ?? -1) > (best.Load ?? -1)) best = (load, used, total, temperature, name.Length > 0 ? name.ToString() : "NVIDIA GPU");
            }
            return best;
        }
        public void Dispose() { if (_library != 0) { _shutdown?.Invoke(); NativeLibrary.Free(_library); _library = 0; } }
        [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Total, Free, Used; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Device(uint index, out nint device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Load(nint device, out Utilization value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Memory(nint device, out MemoryInfo value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Temperature(nint device, uint sensor, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int Name(nint device, StringBuilder name, uint length);
    }
}
