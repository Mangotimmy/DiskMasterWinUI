
$ErrorActionPreference = "Stop"
$dll = "C:\Users\Atszl\Desktop\DiskMasterWinUI\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
Write-Host "Loaded: $($asm.FullName)"

$parserType = $asm.GetType("DiskMasterWinUI.Helpers.ChkdskProgressParser")
$parser = [Activator]::CreateInstance($parserType)
$processLine = $parserType.GetMethod("ProcessLine")
$reset = $parserType.GetMethod("Reset")

function P($line) { $processLine.Invoke($parser, @($line)) }

Write-Host "--- ChkdskProgressParser ---"
$reset.Invoke($parser, @())
$r1 = P("Stage 1: Examining basic file system structure ...")
$r2 = P("  25 percent complete. (123456 of 654321 MB processed)")
$r3 = P("Windows has scanned the file system and found no problems.")
Write-Host "stage1 line -> $r1 (expect ~8)"
Write-Host "25% line -> $r2 (expect ~16)"
Write-Host "completion line -> $r3 (expect 99)"
if ([double]$r1 -lt 7.9 -or [double]$r1 -gt 8.1) { throw "FAIL stage1" }
if ([double]$r2 -lt 15.9 -or [double]$r2 -gt 16.1) { throw "FAIL 25pct" }
if ([double]$r3 -ne 99) { throw "FAIL completion" }

$reset.Invoke($parser, @())
$r4 = P("(1234 of 10000 MB processed)")
Write-Host "ratio no-stage -> $r4 (expect ~12.34)"
if ($null -eq $r4 -or [double]$r4 -lt 12.2 -or [double]$r4 -gt 12.5) { throw "FAIL ratio" }
$r5 = P("  10 percent complete.")
Write-Host "10% after 12.34 -> $r5 (expect null, no regress)"
if ($null -ne $r5) { throw "FAIL regression" }

Write-Host "--- ProgressAndEtaTracker ---"
$trackerType = $asm.GetType("DiskMasterWinUI.Helpers.ProgressAndEtaTracker")
$tracker = [Activator]::CreateInstance($trackerType)
$trackerType.GetMethod("Start").Invoke($tracker, @("chkdsk C: /scan", [System.TimeSpan]::FromMinutes(10)))
Start-Sleep -Milliseconds 1500
$snap = $trackerType.GetMethod("GetLiveSnapshot").Invoke($tracker, @())
Write-Host ("live snapshot after 1.5s -> Percent={0} Elapsed={1} Eta={2}" -f $snap.Item1, $snap.Item2, $snap.Item3)
if ([double]$snap.Item1 -ne 0) { throw "FAIL: bar advanced without process output" }
$trackerType.GetMethod("ProcessLine").Invoke($tracker, @("Stage 1: Examining basic file system structure ..."))
$trackerType.GetMethod("ProcessLine").Invoke($tracker, @("  25 percent complete."))
$cur = $trackerType.GetProperty("CurrentPercentage").GetValue($tracker)
Write-Host "after stage1 + 25% -> CurrentPercentage=$cur (expect 16)"
if ([double]$cur -ne 16) { throw "FAIL: parser integration" }
Start-Sleep -Milliseconds 1200
$snap2 = $trackerType.GetMethod("GetLiveSnapshot").Invoke($tracker, @())
Write-Host ("live snapshot 1.2s later -> Percent={0} (must stay 16)" -f $snap2.Item1)
if ([double]$snap2.Item1 -ne 16) { throw "FAIL: timer moved the bar" }
Write-Host "ETA string: $($snap2.Item3)"

Write-Host "--- ChkdskDriveOption ---"
$optType = $asm.GetType("DiskMasterWinUI.Models.ChkdskDriveOption")
$fromDrive = $optType.GetMethod("FromDrive")
$di = [System.IO.DriveInfo]::new("C:\")
$argArray = [System.Array]::CreateInstance([System.Object], 1)
$argArray.SetValue($di.PSObject.BaseObject, 0)
$opt = $fromDrive.Invoke($null, $argArray)
Write-Host ("Letter={0} Display={1}" -f $opt.Letter, $opt.Display)
if ($opt.Letter -ne "C:") { throw "FAIL: letter" }
if (-not $opt.Display.Contains("C:")) { throw "FAIL: display" }

Write-Host "ALL SMOKE CHECKS PASSED"
