namespace DockGauge.Services;

/// <summary>字节/速率/时长的展示格式化，尽量贴近设计稿的写法（20 KB/s、5.1 GB、1.51 TB…）。</summary>
public static class Fmt
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Speed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{Math.Round(bytesPerSec)} B/s";
        var (v, u) = Split(bytesPerSec, "0.0");
        return $"{v} {u}/s";
    }

    public static string Size(double bytes)
    {
        if (bytes < 1024) return $"{Math.Round(bytes)} B";
        var (v, u) = Split(bytes, "0.00");
        return $"{v} {u}";
    }

    public static string MemGb(ulong bytes) => (bytes / 1073741824.0).ToString("0.0") + " GB";

    public static string Uptime(TimeSpan t)
    {
        var parts = new List<string>();
        if (t.Days > 0) parts.Add($"{t.Days} 天");
        parts.Add($"{t.Hours} 时");
        parts.Add($"{t.Minutes} 分");
        parts.Add($"{t.Seconds} 秒");
        return string.Join(" ", parts);
    }

    private static (string v, string u) Split(double b, string decFormat)
    {
        var i = 0;
        while (b >= 1024 && i < Units.Length - 1) { b /= 1024; i++; }
        return (b.ToString(decFormat), Units[i]);
    }
}
