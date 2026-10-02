using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DockGauge.Services;

public sealed class DriveUsage
{
    public string Letter = "";
    public string Label = "";
    public ulong Used;
    public ulong Total;
}

public sealed class MetricsSample
{
    public double CpuPercent;
    public double MemoryPercent;
    public ulong MemoryUsed;
    public ulong MemoryTotal;
    public double NetUploadBps;
    public double NetDownloadBps;
    public double DiskReadBps;
    public double DiskWriteBps;
    public TimeSpan Uptime;
    public List<DriveUsage> Drives = new();
}

/// <summary>
/// 每秒采样一次系统指标。
/// CPU 用 GetSystemTimes 差值；内存用 GlobalMemoryStatusEx；
/// 网络用各网卡 IPv4 统计差值；磁盘读写用 WMI 格式化性能计数（类名与系统语言无关）。
/// </summary>
public sealed class MetricsService
{
    private bool _cpuSeeded;
    private ulong _lastIdle, _lastKernel, _lastUser;

    private readonly Dictionary<string, (long Sent, long Recv)> _lastNet = new();
    private DateTime _netStamp;
    private bool _netSeeded;

    private long _lastDiskRead = -1, _lastDiskWrite = -1;
    private DateTime _diskStamp;
    private DateTime _diskRetryAfter = DateTime.MinValue;

    public MetricsSample Sample()
    {
        var s = new MetricsSample();
        SampleCpu(s);
        SampleMemory(s);
        SampleNetwork(s);
        SampleDisk(s);
        s.Uptime = TimeSpan.FromMilliseconds(GetTickCount64());
        SampleDrives(s);
        return s;
    }

    private void SampleCpu(MetricsSample s)
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        if (_cpuSeeded)
        {
            var totalDelta = (kernel - _lastKernel) + (user - _lastUser);
            var idleDelta = idle - _lastIdle;
            if (totalDelta > 0)
                s.CpuPercent = Math.Clamp(100.0 * (1.0 - (double)idleDelta / totalDelta), 0, 100);
        }
        _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
        _cpuSeeded = true;
    }

    private static void SampleMemory(MetricsSample s)
    {
        var ms = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(ms)) return;
        s.MemoryTotal = ms.ullTotalPhys;
        s.MemoryUsed = ms.ullTotalPhys - ms.ullAvailPhys;
        s.MemoryPercent = ms.ullTotalPhys > 0 ? 100.0 * s.MemoryUsed / ms.ullTotalPhys : 0;
    }

    private void SampleNetwork(MetricsSample s)
    {
        var now = DateTime.UtcNow;
        long up = 0, down = 0;
        var seen = new HashSet<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            var t = ni.NetworkInterfaceType;
            if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;
            if (ni.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;

            seen.Add(ni.Id);
            IPv4InterfaceStatistics? st = null;
            try { st = ni.GetIPv4Statistics(); } catch { continue; }
            if (st is null) continue;

            if (_lastNet.TryGetValue(ni.Id, out var prev))
            {
                up += Math.Max(0, st.BytesSent - prev.Sent);
                down += Math.Max(0, st.BytesReceived - prev.Recv);
            }
            _lastNet[ni.Id] = (st.BytesSent, st.BytesReceived);
        }

        foreach (var stale in _lastNet.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastNet.Remove(stale);

        if (_netSeeded)
        {
            var dt = (now - _netStamp).TotalSeconds;
            if (dt > 0.2)
            {
                s.NetUploadBps = up / dt;
                s.NetDownloadBps = down / dt;
            }
        }
        _netSeeded = true;
        _netStamp = now;
    }

    private void SampleDisk(MetricsSample s)
    {
        var now = DateTime.UtcNow;
        if (now < _diskRetryAfter) return;
        try
        {
            long read = 0, write = 0;
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name, DiskReadBytesPerSec, DiskWriteBytesPerSec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
            foreach (var mo in searcher.Get())
            {
                var name = Convert.ToString(mo["Name"]);
                if (string.Equals(name, "_Total", StringComparison.OrdinalIgnoreCase)) continue;
                read += Convert.ToInt64(mo["DiskReadBytesPerSec"] ?? 0L);
                write += Convert.ToInt64(mo["DiskWriteBytesPerSec"] ?? 0L);
            }

            if (_lastDiskRead >= 0)
            {
                var dt = (now - _diskStamp).TotalSeconds;
                if (dt > 0.2)
                {
                    s.DiskReadBps = Math.Max(0, (read - _lastDiskRead) / dt);
                    s.DiskWriteBps = Math.Max(0, (write - _lastDiskWrite) / dt);
                }
            }
            _lastDiskRead = read;
            _lastDiskWrite = write;
            _diskStamp = now;
        }
        catch
        {
            // 性能计数器可能被禁用，30 秒后再试，避免每秒刷异常
            _diskRetryAfter = now.AddSeconds(30);
        }
    }

    private static void SampleDrives(MetricsSample s)
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var total = (ulong)d.TotalSize;
                if (total == 0) continue;
                s.Drives.Add(new DriveUsage
                {
                    Letter = d.Name[..2],
                    Label = string.IsNullOrEmpty(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel,
                    Total = total,
                    Used = total - (ulong)d.AvailableFreeSpace,
                });
            }
            catch { /* 设备刚拔出等情况，跳过 */ }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
