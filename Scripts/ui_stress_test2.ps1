param([int]$Bursts = 60, [int]$IntervalMs = 60)

$ErrorActionPreference = 'Stop'
$root = 'C:\Users\Atszl\Desktop\DiskMasterWinUI'
$shotDir = Join-Path $root 'Scripts\shots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

$exe = Join-Path $root 'bin\x64\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.exe'
$existing = Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue
if ($existing) { $existing | Stop-Process -Force; Start-Sleep -Milliseconds 1200 }
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 9
$proc.Refresh()
$hwnd = $proc.MainWindowHandle
Write-Host "pid=$($proc.Id) hwnd=$hwnd responding=$($proc.Responding)"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[W32]::ShowWindow($hwnd, 9) | Out-Null
[W32]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800

function Save-Shot([string]$name) {
    $r = New-Object W32+RECT
    [W32]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { Write-Host "bad rect for $name"; return }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $path = Join-Path $shotDir ("$name.png")
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "shot: $path ($w x $h)"
}

# Burst 1: 60 rapid Ctrl+Tab switches (cycles the whole strip, revisits Network Tools many times)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt $Bursts; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
    Start-Sleep -Milliseconds $IntervalMs
}
$sw.Stop()
Write-Host "burst1: $Bursts switches in $($sw.ElapsedMilliseconds) ms"
Start-Sleep -Milliseconds 1200
$proc.Refresh(); Write-Host "after burst1 responding=$($proc.Responding)"

# Jump to Network Tools deterministically: Ctrl+1 -> index 0, then 11 x Ctrl+Tab -> index 11
[System.Windows.Forms.SendKeys]::SendWait('^1'); Start-Sleep -Milliseconds 250
for ($i = 0; $i -lt 11; $i++) { [System.Windows.Forms.SendKeys]::SendWait('^{TAB}'); Start-Sleep -Milliseconds 90 }
Start-Sleep -Seconds 2
Save-Shot 'network_tools'

# Rapid ping-pong: leave Network Tools and come back repeatedly (the reported scenario)
for ($i = 0; $i -lt 12; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')   # away
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')   # back? (net next index)
    Start-Sleep -Milliseconds 35
}
Start-Sleep -Seconds 1
$proc.Refresh(); Write-Host "after pingpong responding=$($proc.Responding)"

# Screenshots of representative pages
[System.Windows.Forms.SendKeys]::SendWait('^2'); Start-Sleep -Milliseconds 700; Save-Shot 'easy_mode'
[System.Windows.Forms.SendKeys]::SendWait('^4'); Start-Sleep -Milliseconds 700; Save-Shot 'disk_tools'
[System.Windows.Forms.SendKeys]::SendWait('^1'); Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait('^{TAB}'); [System.Windows.Forms.SendKeys]::SendWait('^{TAB}'); Start-Sleep -Milliseconds 700
Save-Shot 'wim_deploy'

$proc.Refresh()
Write-Host "final responding=$($proc.Responding) exited=$($proc.HasExited)"
Write-Host "STRESS_OK"
