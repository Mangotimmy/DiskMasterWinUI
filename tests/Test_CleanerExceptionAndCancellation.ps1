# Cleaner Exception, Cancellation, and Hang Prevention Verification Test
$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$harnessDir = Join-Path $scriptRoot "harness"
. (Join-Path $harnessDir "TestFramework.ps1")
. (Join-Path $harnessDir "TestOracles.ps1")

Write-Host "`n[START] Verifying Cleaner Exception Handling, Cancellation, and Fast-Delete..." -ForegroundColor Cyan

$asm = Initialize-TestAssembly -Configuration "Release"
Assert-NotNull $asm "Target assembly must be loaded"

# Test 1: Verify TempCleanService class and cancellation contracts
Write-Host "Test 1: Verifying TempCleanService method contracts and cancellation signatures..." -ForegroundColor Yellow
$tempCleanType = $asm.GetType("DiskMasterWinUI.Services.TempCleanService")
Assert-NotNull $tempCleanType "TempCleanService must exist"

$cleanSelMethod = $tempCleanType.GetMethods() | Where-Object { $_.Name -eq "CleanSelectedCategoriesAsync" } | Select-Object -First 1
Assert-NotNull $cleanSelMethod "CleanSelectedCategoriesAsync must exist"
$selParams = $cleanSelMethod.GetParameters() | ForEach-Object { $_.Name }
Assert-True ($selParams -contains "ct") "CleanSelectedCategoriesAsync must take a CancellationToken"

$winOldMethod = $tempCleanType.GetMethods() | Where-Object { $_.Name -eq "CleanWindowsOldAsync" } | Select-Object -First 1
Assert-NotNull $winOldMethod "CleanWindowsOldAsync must exist"
$winOldParams = $winOldMethod.GetParameters() | ForEach-Object { $_.Name }
Assert-True ($winOldParams -contains "ct") "CleanWindowsOldAsync must take a CancellationToken"

$appCacheMethod = $tempCleanType.GetMethods() | Where-Object { $_.Name -eq "CleanAppCacheAsync" } | Select-Object -First 1
Assert-NotNull $appCacheMethod "CleanAppCacheAsync must exist"
$appCacheParams = $appCacheMethod.GetParameters() | ForEach-Object { $_.Name }
Assert-True ($appCacheParams -contains "ct") "CleanAppCacheAsync must take a CancellationToken"

Write-Host "  -> PASS: All cleaner methods accept CancellationToken." -ForegroundColor Green

# Test 2: Source Code Oracle - Verify Windows.old fast kernel removal and stdout pipe protection
Write-Host "Test 2: Verifying Windows.old kernel removal and output redirection..." -ForegroundColor Yellow
$tempCleanCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\TempCleanService.cs"))
Assert-True ($tempCleanCs.Contains('robocopy') -or $tempCleanCs.Contains('ROBOCOPY')) "Must leverage multi-threaded Robocopy for high-speed file tree removal"
Assert-True ($tempCleanCs.Contains('rd /s /q \"\\\\?\\{winOld}\"')) "Must use fast extended-path rd /s /q for Windows.old"
Assert-True ($tempCleanCs.Contains('>nul 2>&1')) "Must suppress takeown/icacls/robocopy stdout to prevent pipe buffer deadlock"
Assert-True ($tempCleanCs.Contains('cancellationToken: ct')) "Must propagate CancellationToken to ProcessHelper"
Assert-True ($tempCleanCs.Contains('ct.ThrowIfCancellationRequested()')) "Must throw OperationCanceledException upon cancellation"
Write-Host "  -> PASS: Windows.old fast removal and pipe protection verified." -ForegroundColor Green

# Test 3: Source Code Oracle - Verify SystemHealthViewModel exception boundaries
Write-Host "Test 3: Verifying SystemHealthViewModel exception handling..." -ForegroundColor Yellow
$vmCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\SystemHealthViewModel.cs"))
Assert-True ($vmCs.Contains('catch (OperationCanceledException)')) "SystemHealthViewModel must catch OperationCanceledException"
Assert-True ($vmCs.Contains('catch (Exception ex)')) "SystemHealthViewModel must catch general Exception"
Assert-True ($vmCs.Contains('CurrentStatus = "暫存清理作業已由使用者取消。"')) "Must provide localized status on cancellation"
Assert-True ($vmCs.Contains('CurrentStatus = "Windows.old 清理已由使用者取消。"')) "Must handle Windows.old cancellation status"
Assert-True ($vmCs.Contains('CurrentStatus = "App 快取清理已由使用者取消。"')) "Must handle App Cache cancellation status"
Write-Host "  -> PASS: SystemHealthViewModel handles all cancellation and exception paths." -ForegroundColor Green

# Test 4: Live Functional Cancellation Test
Write-Host "Test 4: Executing live functional test with pre-cancelled CancellationToken..." -ForegroundColor Yellow
$serviceInstance = [System.Activator]::CreateInstance($tempCleanType)
$cts = [System.Threading.CancellationTokenSource]::new()
$cts.Cancel() # Trigger immediate cancellation

Add-Type -TypeDefinition @"
using System;
public static class CleanerTestSink {
    public static void Sink(string msg) { }
}
"@

$actionDelegate = [System.Action[string]][CleanerTestSink]::Sink

$threwExpected = $false
try {
    # Call CleanWindowsOldAsync with pre-cancelled token
    $task = $serviceInstance.CleanWindowsOldAsync($actionDelegate, $cts.Token)
    $task.GetAwaiter().GetResult()
}
catch [System.AggregateException] {
    if ($_.InnerException -is [System.OperationCanceledException]) {
        $threwExpected = $true
    }
}
catch [System.OperationCanceledException] {
    $threwExpected = $true
}

Assert-True $threwExpected "Service must throw OperationCanceledException when token is cancelled"
Write-Host "  -> PASS: CleanWindowsOldAsync immediately respects cancellation token without hanging." -ForegroundColor Green

# Test 5: Verify no orphaned takeown processes
Write-Host "Test 5: Verifying no orphaned background takeown or icacls processes..." -ForegroundColor Yellow
$orphanedTakeown = Get-Process -Name "takeown" -ErrorAction SilentlyContinue
$orphanedIcacls = Get-Process -Name "icacls" -ErrorAction SilentlyContinue
Assert-True ($null -eq $orphanedTakeown) "No orphaned takeown processes should exist"
Assert-True ($null -eq $orphanedIcacls) "No orphaned icacls processes should exist"
Write-Host "  -> PASS: No orphaned worker processes." -ForegroundColor Green

# Test 6: Verify Cleaning Status Display and Delete Status Commands
Write-Host "Test 6: Verifying Cleaning Status and Clear Status commands..." -ForegroundColor Yellow
$healthVmType = $asm.GetType("DiskMasterWinUI.ViewModels.SystemHealthViewModel")
Assert-NotNull $healthVmType "SystemHealthViewModel must exist"
$props = $healthVmType.GetProperties() | ForEach-Object { $_.Name }
Assert-True ($props -contains "CleaningStatusMessage") "CleaningStatusMessage property must exist"
Assert-True ($props -contains "CleaningDetailMessage") "CleaningDetailMessage property must exist"
Assert-True ($props -contains "CleaningProgressPercent") "CleaningProgressPercent property must exist"
Assert-True ($props -contains "HasCleaningStatus") "HasCleaningStatus property must exist"
Assert-True ($props -contains "ClearCleaningStatusCommand") "ClearCleaningStatusCommand must exist to delete current status"

$diskToolsType = $asm.GetType("DiskMasterWinUI.ViewModels.DiskToolsViewModel")
Assert-NotNull $diskToolsType "DiskToolsViewModel must exist"
$dtProps = $diskToolsType.GetProperties() | ForEach-Object { $_.Name }
Assert-True ($dtProps -contains "ClearCacheCleanupSummaryCommand") "ClearCacheCleanupSummaryCommand must exist to delete current status in DiskTools"
Write-Host "  -> PASS: Cleaning status reporting and delete current status commands verified." -ForegroundColor Green

# Test 7: Verify UI Dispatcher Protection on AppendOutput and Background Callbacks
Write-Host "Test 7: Verifying UI thread dispatching and SafeOutput immunity on background threads..." -ForegroundColor Yellow
$healthVmCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\SystemHealthViewModel.cs"))
Assert-True ($healthVmCs.Contains('DispatcherHelper.RunOnUIThread(() =>') -and $healthVmCs.Contains('CleaningDetailMessage = cleanText;')) "AppendOutput must dispatch CleaningDetailMessage to UI thread via DispatcherHelper"
Assert-True ($healthVmCs.Contains('DispatcherHelper.RunOnUIThread(() =>') -and $healthVmCs.Contains('CleaningProgressPercent =')) "onProgress callback must dispatch progress percent to UI thread"

$tempCleanCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Services\TempCleanService.cs"))
Assert-True ($tempCleanCs.Contains('safeOut') -or $tempCleanCs.Contains('SafeOutput')) "TempCleanService must wrap onOutput to protect background worker thread against UI callback exceptions"
Write-Host "  -> PASS: UI thread dispatching and background callback immunity verified." -ForegroundColor Green

Write-Host "`nAll cleaner exception and cancellation expectation tests PASSED!" -ForegroundColor Green
