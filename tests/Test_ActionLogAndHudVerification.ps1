# Action Log & Performance Overlay Window Verification Script
$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$harnessDir = Join-Path $scriptRoot "harness"
. (Join-Path $harnessDir "TestFramework.ps1")
. (Join-Path $harnessDir "TestOracles.ps1")

Write-Host "`n[START] Verifying Performance Overlay Window and Action Log System..." -ForegroundColor Cyan

$asm = Initialize-TestAssembly -Configuration "Release"
Assert-NotNull $asm "Target assembly must be loaded"

# Test 1: Verify ToggleSwitch attributes in PerformanceOverlayWindow.xaml
Write-Host "Test 1: Verifying zero IsOn='True' on ToggleSwitch in PerformanceOverlayWindow.xaml..." -ForegroundColor Yellow
$xamlPath = Join-Path $scriptRoot "..\Controls\PerformanceOverlayWindow.xaml"
$xamlContent = [System.IO.File]::ReadAllText($xamlPath)
$isOnMatches = [regex]::Matches($xamlContent, '<ToggleSwitch[^>]*\bIsOn\s*=\s*"True"')
Assert-Equal $isOnMatches.Count 0 "PerformanceOverlayWindow.xaml must contain 0 instances of IsOn='True' on ToggleSwitch"
Write-Host "  -> PASS: 0 premature IsOn='True' attributes found in XAML." -ForegroundColor Green

# Test 2: Verify PerformanceOverlayWindow class contract and fields
Write-Host "Test 2: Verifying PerformanceOverlayWindow initialization guards..." -ForegroundColor Yellow
$overlayType = $asm.GetType("DiskMasterWinUI.Controls.PerformanceOverlayWindow")
Assert-NotNull $overlayType "PerformanceOverlayWindow class must exist"

$isInitField = $overlayType.GetField("_isInitialized", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
Assert-NotNull $isInitField "_isInitialized private field must exist on PerformanceOverlayWindow"

$toggleMethod = $overlayType.GetMethod("Toggle", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $toggleMethod "PerformanceOverlayWindow.Toggle() static method must exist"
Write-Host "  -> PASS: PerformanceOverlayWindow contract verified." -ForegroundColor Green

# Test 3: Verify ActionLogItem model properties
Write-Host "Test 3: Verifying ActionLogItem model..." -ForegroundColor Yellow
$actionItemType = $asm.GetType("DiskMasterWinUI.Models.ActionLogItem")
Assert-NotNull $actionItemType "ActionLogItem class must exist"

$item = [System.Activator]::CreateInstance($actionItemType)
Assert-NotNull $item
$item.Level = "ACTION"
$item.Category = "Navigation"
$item.Action = "切換分頁"
$item.Details = "Starter -> Performance"

Assert-NotNull $item.Timestamp
Assert-NotNull $item.FormattedTime
Assert-NotNull $item.FullExportLine
Assert-True ($item.FullExportLine.Contains("[ACTION]")) "FullExportLine must contain [ACTION]"
Assert-True ($item.FullExportLine.Contains("Starter -> Performance")) "FullExportLine must contain details"
Write-Host "  -> PASS: ActionLogItem verified." -ForegroundColor Green

# Test 4: Verify DebugLogService ring buffer and action logging
Write-Host "Test 4: Verifying DebugLogService ring buffer, capping at 2000, and events..." -ForegroundColor Yellow
$logServiceType = $asm.GetType("DiskMasterWinUI.Services.DebugLogService")
Assert-NotNull $logServiceType "DebugLogService class must exist"

$instanceProp = $logServiceType.GetProperty("Instance", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
$service = $instanceProp.GetValue($null)
Assert-NotNull $service "DebugLogService.Instance must not be null"

# Clear buffer
$service.ClearLogBuffer()
Assert-Equal $service.LogBufferCount 0 "Buffer count must be 0 after clear"

# Test event firing
$script:eventFiredCount = 0
$eventAction = [System.Action[DiskMasterWinUI.Models.ActionLogItem]]{
    param($logItem)
    $script:eventFiredCount++
}
$service.add_LogItemAdded($eventAction)

# Log actions
$service.LogAction("Navigation", "切換至分頁 [SystemPerformance]", "Test Details")
$service.LogRequest("ProcessHelper", "啟動呼叫: sfc.exe", "/scannow", $null, $true)
$service.LogRequest("ProcessHelper", "完成呼叫: sfc.exe", "/scannow", 1234, $true)

Assert-True ($service.LogBufferCount -ge 3) "Buffer must contain at least 3 items"
Assert-True ($eventFiredCount -ge 3) "LogItemAdded event must have fired at least 3 times"

# Test capping at 2,000 items
Write-Host "  -> Stress testing ring buffer capacity (2,500 rapid enqueues)..." -ForegroundColor Gray
for ($i = 0; $i -lt 2500; $i++) {
    $service.LogAction("TestCategory", "Action #$i", "Detail #$i")
}

Assert-True ($service.LogBufferCount -le 2000) "Memory log queue must be capped at 2,000 items (Actual: $($service.LogBufferCount))"
$recent = $service.GetRecentLogItems()
Assert-True ($recent.Count -le 2000) "GetRecentLogItems count must be <= 2,000"

# Remove event handler
$service.remove_LogItemAdded($eventAction)
Write-Host "  -> PASS: Ring buffer, capping at 2000, and event dispatch verified." -ForegroundColor Green

# Test 5: Verify ActionLogViewerDialog class
Write-Host "Test 5: Verifying ActionLogViewerDialog contract..." -ForegroundColor Yellow
$dialogType = $asm.GetType("DiskMasterWinUI.Controls.ActionLogViewerDialog")
Assert-NotNull $dialogType "ActionLogViewerDialog class must exist"
Write-Host "  -> PASS: ActionLogViewerDialog type verified." -ForegroundColor Green

# Test 6: Verify AppSettings properties for HudClickThrough and HudRemoveBorder
Write-Host "Test 6: Verifying AppSettings HudClickThrough and HudRemoveBorder..." -ForegroundColor Yellow
$settingsType = $asm.GetType("DiskMasterWinUI.Services.AppSettings")
Assert-NotNull $settingsType "AppSettings class must exist"
$clickThroughProp = $settingsType.GetProperty("HudClickThrough")
Assert-NotNull $clickThroughProp "HudClickThrough property must exist on AppSettings"
$removeBorderProp = $settingsType.GetProperty("HudRemoveBorder")
Assert-NotNull $removeBorderProp "HudRemoveBorder property must exist on AppSettings"

$testSettings = [System.Activator]::CreateInstance($settingsType)
Assert-Equal $clickThroughProp.GetValue($testSettings) $false "Default HudClickThrough must be false"
Assert-Equal $removeBorderProp.GetValue($testSettings) $false "Default HudRemoveBorder must be false"
Write-Host "  -> PASS: AppSettings click-through and remove-border verified." -ForegroundColor Green

# Test 7: Verify DebugLogService.OpenLiveConsole method exists
Write-Host "Test 7: Verifying DebugLogService.OpenLiveConsole..." -ForegroundColor Yellow
$openLiveConsoleMethod = $logServiceType.GetMethod("OpenLiveConsole")
Assert-NotNull $openLiveConsoleMethod "OpenLiveConsole method must exist on DebugLogService"
Write-Host "  -> PASS: DebugLogService.OpenLiveConsole verified." -ForegroundColor Green

# Test 8: Verify PerformanceOverlayWindow Click-Through and Border Removal methods
Write-Host "Test 8: Verifying PerformanceOverlayWindow Click-Through and Border Removal APIs..." -ForegroundColor Yellow
$applyCtMethod = $overlayType.GetMethod("ApplyClickThrough")
Assert-NotNull $applyCtMethod "ApplyClickThrough method must exist on PerformanceOverlayWindow"
$applyBorderMethod = $overlayType.GetMethod("ApplyBorderVisibility")
Assert-NotNull $applyBorderMethod "ApplyBorderVisibility method must exist on PerformanceOverlayWindow"
$toggleCtGlobalMethod = $overlayType.GetMethod("ToggleClickThroughGlobal", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $toggleCtGlobalMethod "ToggleClickThroughGlobal static method must exist on PerformanceOverlayWindow"
Write-Host "  -> PASS: PerformanceOverlayWindow APIs verified." -ForegroundColor Green

# Test 9: Verify HudSettingsWindow contract
Write-Host "Test 9: Verifying HudSettingsWindow contract..." -ForegroundColor Yellow
$hudSettingsType = $asm.GetType("DiskMasterWinUI.Controls.HudSettingsWindow")
Assert-NotNull $hudSettingsType "HudSettingsWindow class must exist"
$hudSettingsToggle = $hudSettingsType.GetMethod("Toggle", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $hudSettingsToggle "HudSettingsWindow.Toggle static method must exist"
Write-Host "  -> PASS: HudSettingsWindow contract verified." -ForegroundColor Green

# Test 10: HUD rendering must use a XAML backdrop and must never use the DWM frame-extension hack again
Write-Host "Test 10: Verifying HUD backdrop and removal of DwmExtendFrameIntoClientArea..." -ForegroundColor Yellow
$hudXamlPath = Join-Path $scriptRoot "..\Controls\PerformanceOverlayWindow.xaml"
$hudXaml = [System.IO.File]::ReadAllText($hudXamlPath)
Assert-True ($hudXaml.Contains("<Window.SystemBackdrop>")) "PerformanceOverlayWindow.xaml must define a SystemBackdrop"
Assert-True ($hudXaml.Contains("DesktopAcrylicBackdrop")) "PerformanceOverlayWindow.xaml must use DesktopAcrylicBackdrop"
$hudSettingsXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\HudSettingsWindow.xaml"))
Assert-True ($hudSettingsXaml.Contains("<Window.SystemBackdrop>")) "HudSettingsWindow.xaml must define a SystemBackdrop"
$hudCsPath = Join-Path $scriptRoot "..\Controls\PerformanceOverlayWindow.xaml.cs"
$hudCs = [System.IO.File]::ReadAllText($hudCsPath)
Assert-False ($hudCs -match 'DwmExtendFrameIntoClientArea\s*\(') "HUD code must not call DwmExtendFrameIntoClientArea"
Assert-False ($hudCs.Contains("private struct MARGINS")) "HUD code must not keep the DWM MARGINS struct"
Write-Host "  -> PASS: acrylic backdrop present and DWM frame-extension hack removed." -ForegroundColor Green

# Test 11: Settings application methods must not write settings (feedback-loop regression guard)
Write-Host "Test 11: Verifying HUD apply methods cannot re-enter SettingsService.Save..." -ForegroundColor Yellow
$ctStart = $hudCs.IndexOf("public void ApplyClickThrough(")
$borderStart = $hudCs.IndexOf("public void ApplyBorderVisibility(")
$themeStart = $hudCs.IndexOf("public void ApplyColorTheme(")
Assert-True ($ctStart -ge 0 -and $borderStart -gt $ctStart -and $themeStart -gt $borderStart) "HUD apply method order changed; update this regression test"
$ctBody = $hudCs.Substring($ctStart, $borderStart - $ctStart)
$borderBody = $hudCs.Substring($borderStart, $themeStart - $borderStart)
Assert-False ($ctBody.Contains("SettingsService.Instance.Save()")) "ApplyClickThrough must not call SettingsService.Save()"
Assert-False ($borderBody.Contains("SettingsService.Instance.Save()")) "ApplyBorderVisibility must not call SettingsService.Save()"
$isApplyingField = $overlayType.GetField("_isApplyingHudSettings", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
Assert-NotNull $isApplyingField "_isApplyingHudSettings guard field must exist"
$sigField = $overlayType.GetField("_lastAppliedHudSignature", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
Assert-NotNull $sigField "_lastAppliedHudSignature guard field must exist"
$sigMethod = $overlayType.GetMethod("ComputeHudSettingsSignature", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $sigMethod "ComputeHudSettingsSignature must exist"
Write-Host "  -> PASS: HUD apply methods are pure visual appliers and guarded by a signature." -ForegroundColor Green

# Test 12: Live console window + always-visible status bar + shortcut
Write-Host "Test 12: Verifying LiveConsoleWindow, persistent status bar and Ctrl+Shift+K..." -ForegroundColor Yellow
$liveConsoleType = $asm.GetType("DiskMasterWinUI.Controls.LiveConsoleWindow")
Assert-NotNull $liveConsoleType "LiveConsoleWindow class must exist"
Assert-True ($liveConsoleType.Name -eq "LiveConsoleWindow") "LiveConsoleWindow type name must be stable"
$liveConsoleToggle = $liveConsoleType.GetMethod("Toggle", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $liveConsoleToggle "LiveConsoleWindow.Toggle() static method must exist"
$liveConsoleInstance = $liveConsoleType.GetProperty("CurrentInstance", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $liveConsoleInstance "LiveConsoleWindow.CurrentInstance property must exist"
$statusBarXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\StatusBarControl.xaml"))
Assert-True ($statusBarXaml.Contains('x:Name="StatusBarRoot"')) "StatusBarControl root border must be named StatusBarRoot"
Assert-True ($statusBarXaml.Contains('x:Name="ConsoleBtn"')) "StatusBarControl must expose a ConsoleBtn"
Assert-True ($statusBarXaml.Contains('AutomationId="ConsoleButton"')) "Console button must have an automation id for the runtime test"
$statusRootMatch = [regex]::Match($statusBarXaml, '(?s)<Border x:Name="StatusBarRoot".*?>')
Assert-True $statusRootMatch.Success "StatusBarRoot border declaration must be parseable"
Assert-False ($statusRootMatch.Value.Contains('Visibility="{x:Bind ViewModel.IsActive')) "StatusBarRoot must stay visible when no operation is active"
$mainWindowCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\MainWindow.xaml.cs"))
Assert-True ($mainWindowCs.Contains("VirtualKey.K")) "MainWindow must register the Ctrl+Shift+K console accelerator"
Assert-True ($mainWindowCs.Contains("LiveConsoleWindow.Toggle()")) "MainWindow must toggle the live console from its accelerator"
Assert-True ($mainWindowCs.Contains("LiveConsoleWindow.CurrentInstance?.Close()")) "MainWindow must close the console on tray/exit paths"
Write-Host "  -> PASS: live console, persistent status bar and shortcut verified." -ForegroundColor Green

# Test 13: Live ETA tracker, heartbeat watchdog, and EasyMode change-only save
Write-Host "Test 13: Verifying live ETA snapshot, UI heartbeat and change-only EasyMode save..." -ForegroundColor Yellow
$trackerType = $asm.GetType("DiskMasterWinUI.Helpers.ProgressAndEtaTracker")
Assert-NotNull $trackerType "ProgressAndEtaTracker class must exist"
$liveSnapshot = $trackerType.GetMethod("GetLiveSnapshot")
Assert-NotNull $liveSnapshot "ProgressAndEtaTracker.GetLiveSnapshot() must exist"
$heartbeatType = $asm.GetType("DiskMasterWinUI.Helpers.UiHeartbeatMonitor")
Assert-NotNull $heartbeatType "UiHeartbeatMonitor class must exist"
$heartbeatStart = $heartbeatType.GetMethod("Start", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $heartbeatStart "UiHeartbeatMonitor.Start() must exist"
$easyModeCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\EasyModePage.xaml.cs"))
Assert-True ($easyModeCs.Contains("SetIfChanged")) "EasyModePage must only save splitter sizes when they actually changed"
Write-Host "  -> PASS: live ETA, heartbeat and change-only EasyMode persistence verified." -ForegroundColor Green

Write-Host "`n[COMPLETED] All Action Log, CMD Live Stream, HUD, Live Console and responsiveness verification tests PASSED with zero failures!" -ForegroundColor Green
