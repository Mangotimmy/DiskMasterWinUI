# Phase 3: asynchronous logging, lifecycle guards and memory probe
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: debug writer is asynchronous and bounded" -ForegroundColor Yellow
$debugCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\DebugLogService.cs"))
Assert-True ($debugCs.Contains("_fileWriteQueue")) "Debug log file writes must use a queue"
Assert-True ($debugCs.Contains("FileWriterLoopAsync")) "Debug log must have a background writer"
Assert-True ($debugCs.Contains("MaxQueuedFileLines")) "Debug log queue must be bounded"
Assert-True ($debugCs.Contains("GetLogStatsAsync")) "Exact log stats must be available off the UI thread"
$writeStart = $debugCs.IndexOf("private void WriteEntry")
$writeEnd = $debugCs.IndexOf("private async Task FileWriterLoopAsync")
Assert-True ($writeStart -gt 0 -and $writeEnd -gt $writeStart) "WriteEntry must be identifiable"
$writeBody = $debugCs.Substring($writeStart, $writeEnd - $writeStart)
Assert-True ($writeBody.Contains("_fileWriteQueue.Enqueue")) "WriteEntry must only enqueue"
Assert-True (-not $writeBody.Contains("File.AppendAllText")) "WriteEntry must not append on the caller thread"
$statsStart = $debugCs.IndexOf("public (long SizeBytes, int LineCount, string FormattedText) GetLogStats")
$statsEnd = $debugCs.IndexOf("public Task<(long SizeBytes, int LineCount, string FormattedText)> GetLogStatsAsync")
Assert-True ($statsStart -gt 0 -and $statsEnd -gt $statsStart) "GetLogStats must be identifiable"
$statsBody = $debugCs.Substring($statsStart, $statsEnd - $statsStart)
Assert-True (-not $statsBody.Contains("ReadLine")) "GetLogStats must not line-count the file on the UI thread"

Write-Host "Test 2: async logger throughput and output" -ForegroundColor Yellow
$type = $asm.GetType("DiskMasterWinUI.Services.DebugLogService")
Assert-NotNull $type "DebugLogService must exist"
$svc = $type.GetProperty("Instance").GetValue($null)
Assert-NotNull ($type.GetProperty("DroppedFileLineCount")) "Dropped-line counter must exist"
$logAction = $type.GetMethod("LogAction")
Assert-Equal $logAction.GetParameters().Count 4 "LogAction must keep its four parameters"
$type.GetProperty("IsEnabled").SetValue($svc, $true)
$sw = [Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 3000; $i++) {
    $logAction.Invoke($svc, [object[]]@("Phase3", ("phase3 line " + $i), "", "ACTION")) | Out-Null
}
$sw.Stop()
Write-Host ("  -> enqueue 3000 lines: " + $sw.ElapsedMilliseconds + "ms") -ForegroundColor DarkGray
Assert-True ($sw.ElapsedMilliseconds -lt 1500) "Enqueueing 3000 log lines must not block the caller"
$flush = $type.GetMethod("FlushAsync").Invoke($svc, [object[]]@(5000))
$flush.GetAwaiter().GetResult()
$stats = $type.GetMethod("GetLogStats").Invoke($svc, [object[]]@())
Assert-True ($stats.Item1 -gt 0) "Log file must have content after flush"
$path = $type.GetProperty("LogFilePath").GetValue($svc)
$tail = Get-Content $path -Tail 5
Assert-True (($tail -join " ").Contains("phase3 line 2999")) "Flushed log must contain the final queued line"
Assert-Equal ($type.GetProperty("DroppedFileLineCount").GetValue($svc)) 0 "A 3,000-line burst must not drop lines"
$type.GetProperty("IsEnabled").SetValue($svc, $false)

Write-Host "Test 3: startup crash loop guard" -ForegroundColor Yellow
$guardType = $asm.GetType("DiskMasterWinUI.Helpers.StartupCrashGuard")
Assert-NotNull $guardType "StartupCrashGuard must exist"
Assert-NotNull ($guardType.GetMethod("BeginStartup")) "BeginStartup must exist"
Assert-NotNull ($guardType.GetMethod("MarkStartupHealthy")) "MarkStartupHealthy must exist"
$appCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\App.xaml.cs"))
Assert-True ($appCs.Contains("StartupCrashGuard.BeginStartup")) "App startup must begin the crash guard"
Assert-True ($appCs.Contains("MarkStartupHealthyAfterDelayAsync")) "App must reset the crash guard after a healthy start"

Write-Host "Test 4: lifecycle/race guards" -ForegroundColor Yellow
$netVm = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\NetworkToolsViewModel.cs"))
Assert-True ($netVm.Contains("if (_isLoadingData) return;")) "NetworkTools initialization must be single-flight"
Assert-True ($netVm.Contains("_refreshSharesBusy")) "Share refresh must be single-flight"
$mainCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\MainWindow.xaml.cs"))
Assert-True ($mainCs.Contains("MemoryLeakProbe.RecordNavigation")) "Navigation must record memory samples"

Write-Host "Test 5: memory probe contract" -ForegroundColor Yellow
$probeType = $asm.GetType("DiskMasterWinUI.Helpers.MemoryLeakProbe")
Assert-NotNull $probeType "MemoryLeakProbe must exist"
$probeType.GetMethod("Reset").Invoke($null, [object[]]@())
for ($i = 0; $i -lt 6; $i++) { $probeType.GetMethod("RecordNavigation").Invoke($null, [object[]]@("TestPage")) }
Assert-Equal $probeType.GetProperty("NavigationCount").GetValue($null) 6 "Probe must count navigations"
Assert-True ($probeType.GetProperty("PeakBytes").GetValue($null) -gt 0) "Probe must record heap size"

Write-Host "Phase 3 stability tests PASSED." -ForegroundColor Green
