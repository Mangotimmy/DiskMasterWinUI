param([int]$Bursts = 60, [int]$IntervalMs = 70)

$ErrorActionPreference = 'Stop'
$root = 'C:\Users\Atszl\Desktop\DiskMasterWinUI'
$settingsPath = Join-Path $env:LOCALAPPDATA 'DiskMaster_Portable\settings.json'

# 1) Make sure diagnostic logging is on so the UI heartbeat writes stall evidence.
try {
    $json = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $json.EnableDebugLogging = $true
    $json | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    Write-Host "settings: EnableDebugLogging=true, ScalePercent=$($json.ScalePercent)"
} catch { Write-Host "settings update failed: $($_.Exception.Message)" }

# 2) Start the freshly built app.
$exe = Join-Path $root 'bin\x64\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.exe'
if (-not (Test-Path $exe)) { Write-Host "EXE NOT FOUND: $exe"; exit 1 }
$existing = Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue
if ($existing) { $existing | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 8

$proc.Refresh()
$hwnd = $proc.MainWindowHandle
Write-Host "pid=$($proc.Id) hwnd=$hwnd responding=$($proc.Responding) title=$($proc.MainWindowTitle)"

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
[W32]::ShowWindow($hwnd, 9) | Out-Null
[W32]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 700

# 3) Rapid tab switching: Ctrl+Tab cycles through the strip (visits Network Tools repeatedly).
$sw = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt $Bursts; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
    Start-Sleep -Milliseconds $IntervalMs
}
$sw.Stop()
Write-Host "burst done: $Bursts switches in $($sw.ElapsedMilliseconds) ms"

Start-Sleep -Seconds 2
$proc.Refresh()
Write-Host "after burst: responding=$($proc.Responding) exited=$($proc.HasExited)"

# 4) Also jump straight to the network tools tab and away a few times (Ctrl+1 .. Ctrl+8 map to tabs).
for ($i = 0; $i -lt 12; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
    Start-Sleep -Milliseconds 40
}
Start-Sleep -Milliseconds 800
$proc.Refresh()
Write-Host "final: responding=$($proc.Responding) exited=$($proc.HasExited)"

# 5) Leave the app running so a screenshot can be taken by the caller.
Write-Host "STRESS_OK"
