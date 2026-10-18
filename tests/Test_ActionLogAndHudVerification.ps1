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

Write-Host "`n[COMPLETED] All Action Log and HUD verification tests PASSED with zero failures!" -ForegroundColor Green
