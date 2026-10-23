#requires -version 7.0
<#
  Verifies that hiding to the tray triggers the background working-set trim.
  Launch with /minimized, sample working set before and after the 15 s trim window,
  and assert the hidden process is well below the normal ~400 MB baseline.
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\bin\x64\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.exe'),
    [int]$EarlySeconds = 8,
    [int]$LateSeconds = 40,
    [double]$MaxHiddenWorkingSetMb = 200,
    [double]$MaxRatioOfBaseline = 0.6
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { Write-Host "EXE NOT FOUND: $Exe"; exit 2 }

Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$proc = Start-Process -FilePath $Exe -ArgumentList @('/minimized') -PassThru
Start-Sleep -Seconds $EarlySeconds
$proc.Refresh()
if ($proc.HasExited) { Write-Host "app exited during startup"; exit 2 }
$earlyWs = [math]::Round($proc.WorkingSet64 / 1MB, 1)
$earlyPrivate = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
Write-Host "early: ws=$earlyWs MB private=$earlyPrivate MB"

Start-Sleep -Seconds ($LateSeconds - $EarlySeconds)
$proc.Refresh()
if ($proc.HasExited) { Write-Host "app exited while hidden"; exit 2 }
$lateWs = [math]::Round($proc.WorkingSet64 / 1MB, 1)
$latePrivate = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
Write-Host "late : ws=$lateWs MB private=$latePrivate MB"
try { $proc.Kill($true) } catch { }

$ratio = if ($earlyWs -gt 0) { $lateWs / $earlyWs } else { 1 }
$pass = ($lateWs -le $MaxHiddenWorkingSetMb) -and ($ratio -le $MaxRatioOfBaseline)
Write-Host ("ratio={0:N2} (target <= {1})" -f $ratio, $MaxRatioOfBaseline)
if ($pass) { Write-Host "BACKGROUND MEMORY: GREEN"; exit 0 } else { Write-Host "BACKGROUND MEMORY: RED"; exit 1 }
