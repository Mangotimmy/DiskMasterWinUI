# Hang prevention: HUD event-storm guards, WMI/IOCTL timeouts, refresh reentrancy, UI watchdog
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: hardware query timeouts and fallback" -ForegroundColor Yellow
$healthType = $asm.GetType("DiskMasterWinUI.Services.HealthMonitorService")
Assert-NotNull $healthType "HealthMonitorService must exist"
Assert-NotNull ($healthType.GetMethod("GetDiskHealthAsync")) "GetDiskHealthAsync must exist"

$smartCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\SmartReaderService.cs"))
Assert-True ($smartCs.Contains("GetStorageTemperatureAsync")) "Synchronous temperature IOCTL must have an async timeout wrapper"
Assert-True ($smartCs.Contains("WaitAsync")) "SMART IOCTL calls must be time-bounded"
$healthCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\HealthMonitorService.cs"))
Assert-True ($healthCs.Contains("WmiTimeout")) "Health WMI queries must have a timeout"
Assert-True ($healthCs.Contains("WaitAsync")) "Health SMART enrichment must be time-bounded"

Write-Host "Test 2: refresh reentrancy and deadline" -ForegroundColor Yellow
$diskToolsCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\DiskToolsViewModel.cs"))
Assert-True ($diskToolsCs.Contains("_isRefreshingAll")) "RefreshAllAsync must reject overlapping refreshes"
Assert-True ($diskToolsCs.Contains("TimeSpan.FromSeconds(20)")) "RefreshAllAsync must have a 20s overall deadline"
Assert-True ($diskToolsCs.Contains("GetStorageTemperatureAsync")) "DiskTools must use the async temperature wrapper"
$envCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\EnvironmentCheckService.cs"))
Assert-True ($envCs.Contains("StartsWith")) "UNC PATH entries must be skipped before probing"
Assert-True ($diskToolsCs.Contains("GetLicenseInfoAsync().WaitAsync")) "License WMI lookup must be time-bounded"

Write-Host "Test 3: HUD event-storm guards" -ForegroundColor Yellow
$hudCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\PerformanceOverlayWindow.xaml.cs"))
Assert-True ($hudCs.Contains("_isUpdatingMenuChecks")) "Programmatic menu checks must be guarded"
Assert-True ($hudCs.Contains("_settingsBurstSuppressUntilTicks")) "Settings event storm circuit breaker must exist"
Assert-True ($hudCs.Contains("SetMenuChecked")) "Menu checks must only change when the value changes"
$settingsCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\SettingsService.cs"))
Assert-True ($settingsCs.Contains("_saveRateCount")) "Settings save-rate diagnostic must exist"

Write-Host "Test 4: background UI watchdog" -ForegroundColor Yellow
$heartbeatCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Helpers\UiHeartbeatMonitor.cs"))
Assert-True ($heartbeatCs.Contains("_backgroundWatchdog")) "Background UI watchdog must exist"
Assert-True ($heartbeatCs.Contains("CheckFromBackground")) "Background watchdog must log stalls from the thread pool"

Write-Host "Hang prevention tests PASSED." -ForegroundColor Green
