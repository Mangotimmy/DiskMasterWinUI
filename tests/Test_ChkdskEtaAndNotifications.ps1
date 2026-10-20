# chkdsk ETA, raw command logging, completion notifications, and HUD border regression tests
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: chkdsk progress parser" -ForegroundColor Yellow
$parserType = $asm.GetType("DiskMasterWinUI.Helpers.ChkdskProgressParser")
Assert-NotNull $parserType "ChkdskProgressParser must exist"
$parser = [Activator]::CreateInstance($parserType)
$process = $parserType.GetMethod("ProcessLine")
$stage1 = $process.Invoke($parser, @("Stage 1: Examining basic file system structure ..."))
$fileDone = $process.Invoke($parser, @("File verification completed."))
$stage2 = $process.Invoke($parser, @("Stage 2: Examining file name linkage ..."))
$stage3 = $process.Invoke($parser, @("Stage 3: Examining security descriptors ..."))
$cnStage = $process.Invoke($parser, @("階段 1: 檢查基本檔案系統結構 ..."))
Assert-True ($stage1 -gt 0) "Stage 1 must yield progress"
Assert-True ($fileDone -gt $stage1) "File verification must advance progress"
Assert-True ($stage2 -gt $fileDone) "Stage 2 must advance progress"
Assert-True ($stage3 -gt $stage2) "Stage 3 must advance progress"
Assert-True ($cnStage -le $stage3) "Localised stage line must parse without throwing"

Write-Host "Test 2: progress bar follows real process output, not wall-clock time" -ForegroundColor Yellow
$trackerType = $asm.GetType("DiskMasterWinUI.Helpers.ProgressAndEtaTracker")
$tracker = [Activator]::CreateInstance($trackerType)
$trackerType.GetMethod("Start").Invoke($tracker, @("test", [TimeSpan]::FromSeconds(2))) | Out-Null
Start-Sleep -Milliseconds 1200
$snap = $tracker.GetLiveSnapshot()
Assert-True ($snap.Item1 -eq 0) "Bar must NOT advance from elapsed time alone (tracks process output only)"
Assert-True ($snap.Item3 -match "remaining|Almost") "Live snapshot must expose a remaining time"
$trackerType.GetMethod("ProcessLine").Invoke($tracker, @("[================ 45.0% ================]")) | Out-Null
$snap2 = $tracker.GetLiveSnapshot()
Assert-True ($snap2.Item1 -ge 45) "Bar must advance when the process reports real progress"
Start-Sleep -Milliseconds 900
$snap3 = $tracker.GetLiveSnapshot()
Assert-True ($snap3.Item1 -eq $snap2.Item1) "Timer ticks must not move the bar past reported progress"

Write-Host "Test 3: raw command output logging" -ForegroundColor Yellow
$logService = $asm.GetType("DiskMasterWinUI.Services.DebugLogService").GetProperty("Instance").GetValue($null)
$logService.ClearLogBuffer()
$logService.LogCommandOutput("chkdsk", "raw chkdsk output")
$recent = $logService.GetRecentLogItems()
$outputs = @($recent | Where-Object { $_.Level -eq "OUTPUT" })
Assert-Equal $outputs.Count 1 "LogCommandOutput must add exactly one OUTPUT row"
Assert-True ($outputs[0].Action -match "raw chkdsk output") "OUTPUT row must keep the raw line"

Write-Host "Test 4: completion notification contract" -ForegroundColor Yellow
$statusType = $asm.GetType("DiskMasterWinUI.ViewModels.StatusBarViewModel")
$start = $statusType.GetMethod("StartOperation")
$complete = $statusType.GetMethod("CompleteOperation")
Assert-Equal $start.GetParameters().Count 3 "StartOperation must accept an expected duration"
Assert-Equal $complete.GetParameters().Count 3 "CompleteOperation must accept success/notify"
$notificationType = $asm.GetType("DiskMasterWinUI.Services.WindowsNotificationService")
$notify = $notificationType.GetMethod("ShowNotification")
Assert-Equal $notify.GetParameters().Count 4 "ShowNotification must accept playSound"
Assert-True $notify.GetParameters()[3].HasDefaultValue "playSound must be optional"

Write-Host "Test 5: source guards" -ForegroundColor Yellow
$hudCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\PerformanceOverlayWindow.xaml.cs"))
Assert-True ($hudCs.Contains("_isUpdatingMenuChecks")) "HUD must guard programmatic menu updates"
Assert-True ($hudCs.Contains("Math.Max((int)alpha, 140)")) "HUD border alpha must be decoupled from opacity"
Assert-True ($hudCs.Contains("_themedBorderBrush")) "HUD must keep the themed border brush"
Assert-True ($hudCs.Contains("ApplyDpiAwareWindowSize(repositionTopRight: false)")) "HUD border toggle must resize"
$liveConsole = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\LiveConsoleWindow.xaml.cs"))
Assert-True ($liveConsole.Contains("OUTPUT")) "Live Console must expose an OUTPUT filter"
$hudSettingsXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\\Controls\\HudSettingsWindow.xaml"))
Assert-True ($hudSettingsXaml.Contains("HudRemoveBorderToggle")) "HUD settings must expose the border toggle automation id"
$hudSettingsCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\\Controls\\HudSettingsWindow.xaml.cs"))
Assert-True ($hudSettingsCs.Contains("ApplyMetricSettings")) "HUD settings must apply metric changes immediately"
$statusCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\StatusBarViewModel.cs"))
Assert-True ($statusCs.Contains("ShowNotification")) "Status bar must dispatch completion notifications"

Write-Host "chkdsk ETA/logging/notification/HUD tests PASSED." -ForegroundColor Green
