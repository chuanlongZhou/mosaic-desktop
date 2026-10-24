using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Mosaic.Platform.Windows;

public sealed record MetricsSnapshot(double? CpuPercent, double MemoryPercent, ulong UsedMemory, ulong TotalMemory,
    double DownloadBytesPerSecond, double UploadBytesPerSecond, long DiskUsed, long DiskTotal, string DiskName);

/// <summary>Called by one visible-workbench subscriber, never a resident polling service.</summary>
public sealed class SystemMetrics
{
    private ulong _idle, _kernel, _user;
    private long _received, _sent;
    private DateTime _previous;
    private DateTime _diskRead;
    private long _diskUsed, _diskTotal;
    private string _diskName = "系统磁盘";

    public MetricsSnapshot Read()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user - _kernel - _user;
            if (_previous != default && total > 0) cpu = Math.Clamp((1 - (double)(idle - _idle) / total) * 100, 0, 100);
            (_idle, _kernel, _user) = (idle, kernel, user);
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("无法读取系统内存。");
        long received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try { var stats = adapter.GetIPStatistics(); received += stats.BytesReceived; sent += stats.BytesSent; }
            catch (NetworkInformationException) { /* A disconnected adapter contributes no sample. */ }
        }
        var now = DateTime.UtcNow;
        var seconds = (now - _previous).TotalSeconds;
        var down = _previous == default || seconds <= 0 ? 0 : Math.Max(0, received - _received) / seconds;
        var up = _previous == default || seconds <= 0 ? 0 : Math.Max(0, sent - _sent) / seconds;
        (_previous, _received, _sent) = (now, received, sent);
        if ((now - _diskRead).TotalSeconds >= 60)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            if (drive.IsReady)
            {
                (_diskUsed, _diskTotal, _diskName) = (drive.TotalSize - drive.TotalFreeSpace, drive.TotalSize, drive.Name.TrimEnd('\\'));
                _diskRead = now;
            }
        }
        return new(cpu, memory.MemoryLoad, memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical,
            down, up, _diskUsed, _diskTotal, _diskName);
    }

    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
