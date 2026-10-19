# chkdsk schedule-prompt handling and failure-state regression tests
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: prompt and busy detection" -ForegroundColor Yellow
$detectorType = $asm.GetType("DiskMasterWinUI.Helpers.ChkdskPromptDetector")
Assert-NotNull $detectorType "ChkdskPromptDetector must exist"
$isBusy = $detectorType.GetMethod("IsVolumeBusy")
$isPrompt = $detectorType.GetMethod("IsSchedulePrompt")
$getResponse = $detectorType.GetMethod("TryGetResponse")

Assert-True ($isBusy.Invoke($null, @("無法鎖定目前的磁碟機。"))) "Chinese lock line must be detected"
Assert-True ($isBusy.Invoke($null, @("Chkdsk cannot run because the volume is in use by another process."))) "English lock line must be detected"
Assert-True ($isPrompt.Invoke($null, @("是否要將此磁碟區排程在下次系統重新啟動時 (Y/N)?"))) "Chinese schedule prompt must be detected"
Assert-True ($isPrompt.Invoke($null, @("Would you like to schedule this volume to be checked the next time the system restarts? (Y/N)"))) "English schedule prompt must be detected"
Assert-False ($isPrompt.Invoke($null, @("Convert lost chains to files? (Y/N)"))) "Lost-chain prompt must not be auto-answered"
Assert-False ($isPrompt.Invoke($null, @("Do you want to convert lost chains to files?"))) "English lost-chain prompt must not be auto-answered"

$response = $null
$args = @("是否要將此磁碟區排程在下次系統重新啟動時 (Y/N)?", $response)
$handled = $getResponse.Invoke($null, $args)
Assert-True $handled "TryGetResponse must handle the schedule prompt"
Assert-Equal $args[1] "N" "Schedule prompt must be answered N"

Write-Host "Test 2: runner and state contracts" -ForegroundColor Yellow
$runnerType = $asm.GetType("DiskMasterWinUI.Helpers.StreamingProcessRunner")
Assert-NotNull $runnerType "StreamingProcessRunner must exist"
$run = $runnerType.GetMethod("RunAsync")
Assert-Equal $run.GetParameters().Count 7 "RunAsync must expose inputResponder and idleTimeout"
$statusType = $asm.GetType("DiskMasterWinUI.ViewModels.StatusBarViewModel")
Assert-NotNull ($statusType.GetMethod("UpdateOperationTitle")) "Status bar must expose UpdateOperationTitle"

Write-Host "Test 3: failed operation keeps current percent and stops ETA" -ForegroundColor Yellow
$status = $statusType.GetProperty("Instance").GetValue($null)
$status.StartOperation("test failure state", $null, [TimeSpan]::FromSeconds(10))
Start-Sleep -Milliseconds 700
$status.ProcessOutputLine("Stage 1: Examining basic file system structure ...")
$status.CompleteOperation("chkdsk failed: volume busy", $false, $false)
Start-Sleep -Milliseconds 200
Assert-True ($status.ProgressPercent -lt 100) "Failure must not jump to 100 percent"
Assert-True ($status.EtaText -match "Stopped|已停止") "Failure must show a stopped ETA"
Assert-True ($status.OperationTitle -match "volume busy") "Failure title must be the backend error message"

Write-Host "Test 4: source guards" -ForegroundColor Yellow
$runnerCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Helpers\StreamingProcessRunner.cs"))
Assert-True ($runnerCs.Contains("RedirectStandardInput")) "Runner must redirect stdin"
Assert-True ($runnerCs.Contains("inputResponder")) "Runner must accept an input responder"
Assert-True ($runnerCs.Contains("StandardInput.WriteLine")) "Runner must write prompt responses"
$chkdskCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\ChkdskService.cs"))
Assert-True ($chkdskCs.Contains("ChkdskPromptDetector")) "ChkdskService must use the prompt detector"
Assert-True ($chkdskCs.Contains("code = -2")) "ChkdskService must return the busy failure code"
$diskToolsCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\DiskToolsViewModel.cs"))
Assert-True ($diskToolsCs.Contains("exitCode == -2")) "DiskTools must handle the busy failure code"
Assert-True ($diskToolsCs.Contains("UpdateOperationTitle")) "DiskTools must connect busy lines to the status bar"
$liveCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\LiveConsoleWindow.xaml.cs"))
Assert-True ($liveCs.Contains("InitialItems = MaxVisibleItems")) "Live console must load the full buffer"

Write-Host "chkdsk prompt/state tests PASSED." -ForegroundColor Green
