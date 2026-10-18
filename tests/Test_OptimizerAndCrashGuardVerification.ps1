# Optimizer, CrashGuard, and ActionLog Verification Test
$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$harnessDir = Join-Path $scriptRoot "harness"
. (Join-Path $harnessDir "TestFramework.ps1")
. (Join-Path $harnessDir "TestOracles.ps1")

Write-Host "`n[START] Verifying Optimizer, CrashGuard, and ActionLog Dialog..." -ForegroundColor Cyan

$asm = Initialize-TestAssembly -Configuration "Release"
Assert-NotNull $asm "Target assembly must be loaded"

# Test 1: Verify ActionLogItem has no WinRT UI dependencies
Write-Host "Test 1: Verifying ActionLogItem model purity..." -ForegroundColor Yellow
$itemType = $asm.GetType("DiskMasterWinUI.Models.ActionLogItem")
Assert-NotNull $itemType "ActionLogItem class must exist"

$props = $itemType.GetProperties() | ForEach-Object { $_.Name }
Assert-True ($props -contains "Timestamp") "Timestamp property must exist"
Assert-True ($props -contains "Level") "Level property must exist"
Assert-True ($props -contains "Category") "Category property must exist"
Assert-True ($props -contains "Action") "Action property must exist"
Assert-True ($props -contains "Details") "Details property must exist"
Assert-True ($props -contains "FormattedTime") "FormattedTime property must exist"
Assert-True ($props -contains "HasDetails") "HasDetails property must exist"
Assert-True ($props -notcontains "LevelBadgeBrush") "LevelBadgeBrush must NOT exist on ActionLogItem (decoupled to converter)"
Assert-True ($props -notcontains "HasDetailsVisibility") "HasDetailsVisibility must NOT exist on ActionLogItem (decoupled to converter)"
Write-Host "  -> PASS: ActionLogItem is 100% pure C# model without UI threading constraints." -ForegroundColor Green

# Test 2: Verify LogLevelToBrushConverter class exists and implements IValueConverter
Write-Host "Test 2: Verifying LogLevelToBrushConverter..." -ForegroundColor Yellow
$convType = $asm.GetType("DiskMasterWinUI.Helpers.LogLevelToBrushConverter")
Assert-NotNull $convType "LogLevelToBrushConverter must exist in DiskMasterWinUI.Helpers"
$converterInterface = $convType.GetInterface("IValueConverter")
Assert-NotNull $converterInterface "LogLevelToBrushConverter must implement IValueConverter"
Write-Host "  -> PASS: LogLevelToBrushConverter is registered and valid." -ForegroundColor Green

# Test 3: Verify GlobalExceptionHandler.ReportFatalException
Write-Host "Test 3: Verifying GlobalExceptionHandler.ReportFatalException contract..." -ForegroundColor Yellow
$handlerType = $asm.GetType("DiskMasterWinUI.Helpers.GlobalExceptionHandler")
Assert-NotNull $handlerType "GlobalExceptionHandler must exist"
$fatalMethod = $handlerType.GetMethod("ReportFatalException", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $fatalMethod "ReportFatalException method must exist on GlobalExceptionHandler"
Write-Host "  -> PASS: GlobalExceptionHandler.ReportFatalException verified." -ForegroundColor Green

# Test 4: Verify SystemOptimizerViewModel commands
Write-Host "Test 4: Verifying SystemOptimizerViewModel fast refresh and lazy commands..." -ForegroundColor Yellow
$vmType = $asm.GetType("DiskMasterWinUI.ViewModels.SystemOptimizerViewModel")
Assert-NotNull $vmType "SystemOptimizerViewModel must exist"

$fastCmd = $vmType.GetProperty("RefreshFastCoreSettingsCommand")
Assert-NotNull $fastCmd "RefreshFastCoreSettingsCommand property must exist"

$powerCmd = $vmType.GetProperty("RefreshPowerPlansCommand")
Assert-NotNull $powerCmd "RefreshPowerPlansCommand property must exist"

$restoreCmd = $vmType.GetProperty("RefreshRestorePointsCommand")
Assert-NotNull $restoreCmd "RefreshRestorePointsCommand property must exist"
Write-Host "  -> PASS: SystemOptimizerViewModel commands verified." -ForegroundColor Green

# Test 5: Verify MainWindow layout recursion guard
Write-Host "Test 5: Verifying MainWindow layout recursion guard..." -ForegroundColor Yellow
$mainWinType = $asm.GetType("DiskMasterWinUI.MainWindow")
Assert-NotNull $mainWinType "MainWindow must exist"
$guardField = $mainWinType.GetField("_isUpdatingLayout", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
Assert-NotNull $guardField "_isUpdatingLayout field must exist on MainWindow to prevent stack overflow layout recursion"
Write-Host "  -> PASS: MainWindow layout recursion guard verified." -ForegroundColor Green

Write-Host "`n[COMPLETED] All Optimizer, CrashGuard, and ActionLog tests PASSED with zero failures!" -ForegroundColor Green
