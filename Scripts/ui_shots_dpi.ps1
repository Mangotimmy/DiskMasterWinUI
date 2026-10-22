param()
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\Atszl\Desktop\DiskMasterWinUI'
$shotDir = Join-Path $root 'Scripts\shots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DpiAware {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
try { [DpiAware]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null } catch { [DpiAware]::SetProcessDPIAware() | Out-Null }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$proc = Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host 'app not running'; exit 1 }
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq 0) { Write-Host 'no window'; exit 1 }
[DpiAware]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 700
function Save-Shot([string]$name) {
    $r = New-Object DpiAware+RECT
    $hr = [DpiAware]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16)
    if ($hr -ne 0) { [DpiAware]::GetWindowRect($hwnd, [ref]$r) | Out-Null }
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    Write-Host ($name + ' rect=(' + $r.Left + ',' + $r.Top + ') ' + $w + 'x' + $h)
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $path = Join-Path $shotDir ($name + '.png')
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host ('saved ' + $path)
}
[System.Windows.Forms.SendKeys]::SendWait('^1'); Start-Sleep -Milliseconds 300
for ($i = 0; $i -lt 11; $i++) { [System.Windows.Forms.SendKeys]::SendWait('^{TAB}'); Start-Sleep -Milliseconds 80 }
Start-Sleep -Seconds 1
Save-Shot 'v2_network_tools'
[System.Windows.Forms.SendKeys]::SendWait('^4'); Start-Sleep -Milliseconds 800
Save-Shot 'v2_page_4'
[System.Windows.Forms.SendKeys]::SendWait('^2'); Start-Sleep -Milliseconds 800
Save-Shot 'v2_page_2'
Write-Host 'SHOTS_OK'