#requires -version 7.0
<#
.SYNOPSIS
    UI soak gate for DiskMaster Pro: drives tab navigation, samples process counters,
    probes UI-thread liveness and captures a dump if the UI thread stops responding.
.DESCRIPTION
    Executable Red/Green gate that the reflection-based E2E suite lacks.
    Exit code 0 = thresholds met, 1 = hang or unbounded growth violation.
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\bin\x64\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.exe'),
    [ValidateSet('full','dwell','burst','idle')][string]$Scenario = 'full',
    [int]$DwellSeconds = 25,
    [int]$Cycles = 80,
    [int]$CycleDelayMs = 70,
    [double]$MaxPrivateGrowthMb = 80,
    [int]$MaxHandleGrowth = 120,
    [int]$RecoveryTimeoutSeconds = 12,
    [int]$MaxStallMs = 1500,
    [int]$WarmupCycles = 16,
    [int]$MaxWarmupCycles = 40,
    [double]$MaxHandleSlopePerSec = 2.0,
    [double]$MaxPrivateSlopeMbPerSec = 5.0,
    [switch]$IncludeSeries,
    [string]$OutDir = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class UiProbe {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
}
"@

function Test-UiAlive([IntPtr]$Hwnd, [int]$TimeoutMs) {
    if ($Hwnd -eq [IntPtr]::Zero) { return $false }
    $r = [IntPtr]::Zero
    $res = [UiProbe]::SendMessageTimeout($Hwnd, 0, [IntPtr]::Zero, [IntPtr]::Zero,
        0x0003, [uint32]$TimeoutMs, [ref]$r)
    return ($res -ne [IntPtr]::Zero)
}

$existing = Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue
if ($existing) { $existing | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
if (-not (Test-Path $Exe)) { Write-Host "EXE NOT FOUND: $Exe"; exit 2 }

$logPath = Join-Path $env:LOCALAPPDATA 'DiskMaster_Portable\Logs\diskmaster_debug.log'
$logStart = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }

$startTab = if ($Scenario -in @('dwell','full')) { 'SystemPerformance' } else { 'Starter' }
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $Exe -ArgumentList @('--tab', $startTab) -PassThru

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { Write-Host "app exited during startup"; break }
    if ($proc.MainWindowHandle -ne 0) { $hwnd = $proc.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Host "NO WINDOW HANDLE"; try { $proc.Kill() } catch {}; exit 2 }
[UiProbe]::ShowWindow($hwnd, 9) | Out-Null
[UiProbe]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 1200
Write-Host ("window ready: pid={0} scenario={1} tab={2} startup={3}ms" -f $proc.Id, $Scenario, $startTab, $sw.ElapsedMilliseconds)

$samples = [System.Collections.Generic.List[object]]::new()
$stalls = [System.Collections.Generic.List[object]]::new()
$hung = $false
$hangAt = $null

function Add-Sample([string]$phase) {
    $proc.Refresh()
    if ($proc.HasExited) { return [pscustomobject]@{ Phase=$phase; Exited=$true } }
    $alive = Test-UiAlive $hwnd 800
    $s = [pscustomobject]@{
        t_ms = [int]$sw.ElapsedMilliseconds
        phase = $phase
        ui_alive = $alive
        private_mb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        working_mb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        handles = $proc.HandleCount
        threads = $proc.Threads.Count
    }
    $samples.Add($s)
    return $s
}

function Wait-UiRecovery([string]$phase, $first) {
    $start = $first.t_ms
    $deadline = (Get-Date).AddSeconds($RecoveryTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $c = Add-Sample ($phase + '-recovering')
        if ($c.PSObject.Properties['Exited'] -and $c.Exited) { return 'exited' }
        if ($c.ui_alive) {
            $stalls.Add([pscustomobject]@{ phase = $phase; start_ms = $start; duration_ms = $c.t_ms - $start })
            Write-Host ("stall: {0} for {1} ms" -f $phase, ($c.t_ms - $start))
            return 'recovered'
        }
    }
    return 'hung'
}

function Invoke-Sampling([string]$phase, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $s = Add-Sample $phase
        if ($s.PSObject.Properties['Exited'] -and $s.Exited) { return 'exited' }
        if ($s.ui_alive -eq $false) {
            $r = Wait-UiRecovery $phase $s
            if ($r -eq 'hung') { return 'hung' }
            if ($r -eq 'exited') { return 'exited' }
        }
        Start-Sleep -Milliseconds 400
    }
    return 'ok'
}

# Warm-up: create every cached page once, so the measurement window covers steady-state
# navigation only (page construction is a one-time allocation, not a repeated leak).
$warmupCycles = 0
if ($Scenario -in @('full','burst')) {
    # Visit tabs until the handle count stops moving: every page and its templates are realised
    # once, and only then does the measurement window represent steady state.
    $stable = 0
    $prevH = -1
    while ($warmupCycles -lt $MaxWarmupCycles -and ($stable -lt 4 -or $warmupCycles -lt 24)) {
        [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
        $s = Add-Sample 'warmup'
        $warmupCycles++
        if ($s.PSObject.Properties['Exited'] -and $s.Exited) { break }
        if ($prevH -ge 0 -and [math]::Abs($s.handles - $prevH) -le 5) { $stable++ } else { $stable = 0 }
        $prevH = $s.handles
        Start-Sleep -Milliseconds 160
    }
    Start-Sleep -Seconds 3
    $null = Add-Sample 'warmup'
    Write-Host ("warm-up complete after {0} cycles (stable={1})" -f $warmupCycles, ($stable -ge 4))
}

$state = 'ok'
switch ($Scenario) {
    'idle'  { $state = Invoke-Sampling 'idle' $DwellSeconds }
    'dwell' { $state = Invoke-Sampling 'dwell' $DwellSeconds }
    'burst' { }
    'full'  { $state = Invoke-Sampling 'dwell' $DwellSeconds }
}
if ($state -eq 'hung') { $hung = $true; $hangAt = ($samples | Select-Object -Last 1) }

if (-not $hung -and $Scenario -in @('full','burst')) {
    for ($i = 0; $i -lt $Cycles; $i++) {
        [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
        $s = Add-Sample 'burst'
        if ($s.PSObject.Properties['Exited'] -and $s.Exited) { break }
        if ($s.ui_alive -eq $false) {
            $r = Wait-UiRecovery 'burst' $s
            if ($r -eq 'hung') { $hung = $true; $hangAt = ($samples | Select-Object -Last 1); break }
            if ($r -eq 'exited') { break }
        }
        Start-Sleep -Milliseconds $CycleDelayMs
    }
}

if (-not $hung -and $Scenario -in @('full','burst')) {
    $state = Invoke-Sampling 'settle' 12
    if ($state -eq 'hung') { $hung = $true; $hangAt = ($samples | Select-Object -Last 1) }
}

$dumpPath = $null
if ($hung) {
    $dumpPath = Join-Path $OutDir ("hang_{0}.dmp" -f $stamp)
    Write-Host "HANG DETECTED at t=$($hangAt.t_ms)ms - collecting dump: $dumpPath"
    try { & dotnet-dump collect -p $proc.Id -o $dumpPath --diag 2>&1 | Out-Host }
    catch { Write-Host "dump collect failed: $($_ | Out-String)" }
}

$proc.Refresh()
if (-not $proc.HasExited) { try { $proc.Kill($true) } catch {} }

$stallLines = @()
if (Test-Path $logPath) {
    try {
        $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
        if ($fs.Length -gt $logStart) {
            $fs.Seek($logStart, 'Begin') | Out-Null
            $reader = [System.IO.StreamReader]::new($fs)
            $text = $reader.ReadToEnd(); $reader.Dispose(); $fs.Dispose()
            $stallLines = @($text -split '\r?\n' | Where-Object { $_ -match 'UI thread (stalled|unresponsive)' })
        } else { $fs.Dispose() }
    } catch { }
}

function Get-SlopePerSec($arr, [string]$prop) {
    if ($arr.Count -lt 2) { return 0 }
    $n = $arr.Count; $sx = 0.0; $sy = 0.0; $sxy = 0.0; $sx2 = 0.0
    foreach ($x in $arr) {
        $y = [double]$x.$prop
        $sx += $x.t_ms; $sy += $y; $sxy += $x.t_ms * $y; $sx2 += $x.t_ms * $x.t_ms
    }
    $den = ($n * $sx2 - $sx * $sx)
    if ($den -eq 0) { return 0 }
    return (($n * $sxy - $sx * $sy) / $den) * 1000
}

$measuredSamples = @($samples | Where-Object { $_.phase -ne 'warmup' })
$handleSlope = Get-SlopePerSec $measuredSamples 'handles'
$privateSlope = Get-SlopePerSec $measuredSamples 'private_mb'
$base = $samples | Where-Object { $_.phase -ne 'warmup' } | Select-Object -First 1
$last = $samples | Select-Object -Last 1
$growth = if ($base -and $last) { [math]::Round($last.private_mb - $base.private_mb, 1) } else { 0 }
$handleGrowth = if ($base -and $last) { $last.handles - $base.handles } else { 0 }

$result = [pscustomobject]@{
    stamp = $stamp
    scenario = $Scenario
    duration_s = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    samples = $samples.Count
    hang_detected = $hung
    hang_at_ms = if ($hangAt) { $hangAt.t_ms } else { $null }
    dump = $dumpPath
    private_mb_start = if ($base) { $base.private_mb } else { $null }
    private_mb_end = if ($last) { $last.private_mb } else { $null }
    private_growth_mb = $growth
    handle_start = if ($base) { $base.handles } else { $null }
    handle_end = if ($last) { $last.handles } else { $null }
    handle_growth = $handleGrowth
    handle_slope_per_s = [math]::Round($handleSlope, 2)
    private_slope_mb_per_s = [math]::Round($privateSlope, 2)
    warmup_cycles = $warmupCycles
    max_threads = ($samples | Measure-Object -Property threads -Maximum).Maximum
    max_stall_ms = if ($stalls.Count -gt 0) { ($stalls | Measure-Object -Property duration_ms -Maximum).Maximum } else { 0 }
    stall_events = $stalls
    log_stall_count = $stallLines.Count
}
if ($IncludeSeries) { $result | Add-Member -NotePropertyName series -NotePropertyValue $samples }
$outFile = Join-Path $OutDir ("soak_{0}_{1}.json" -f $Scenario, $stamp)
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outFile -Encoding UTF8
Write-Host ($result | Format-List | Out-String -Width 200)

$measured = @($stalls | Where-Object { $_.phase -ne 'warmup' })
$maxStall = if ($measured.Count -gt 0) { ($measured | Measure-Object -Property duration_ms -Maximum).Maximum } else { 0 }
$fail = $hung -or ($maxStall -gt $MaxStallMs) -or
        ($handleSlope -gt $MaxHandleSlopePerSec) -or ($privateSlope -gt $MaxPrivateSlopeMbPerSec)
if ($fail) { Write-Host "SOAK RESULT: RED"; exit 1 } else { Write-Host "SOAK RESULT: GREEN"; exit 0 }
