param(
    [string]$OutPath = "C:\Users\24790\WindowsProjects\monitor\shot.png",
    [int]$Pad = 16
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win32Shot {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> FindWindows(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

[Win32Shot]::SetProcessDPIAware() | Out-Null
$p = Get-Process DockGauge -ErrorAction Stop | Select-Object -First 1
if (-not $p) { throw "DockGauge process not found" }

$hwnds = [Win32Shot]::FindWindows([uint32]$p.Id)
if ($hwnds.Count -eq 0) { throw "no visible window for PID $($p.Id)" }

# 取面积最大的可见窗口
$best = $null; $bestArea = 0
foreach ($h in $hwnds) {
    $r = New-Object Win32Shot+RECT
    [Win32Shot]::GetWindowRect($h, [ref]$r) | Out-Null
    $area = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
    if ($area -gt $bestArea) { $bestArea = $area; $best = $r }
}

$w = ($best.Right - $best.Left) + 2 * $Pad
$h = ($best.Bottom - $best.Top) + 2 * $Pad
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($best.Left - $Pad, $best.Top - $Pad, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved: $OutPath ($w x $h)"
