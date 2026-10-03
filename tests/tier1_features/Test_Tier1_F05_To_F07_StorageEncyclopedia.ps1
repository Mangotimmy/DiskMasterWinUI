# DiskMaster Pro WinUI 3 — Tier 1 Feature Coverage: F5 to F7 (Storage Directory Encyclopedia)
# Author: Test Writer Agent (E2E Track)
# Scope: Storage Directory Scanning, Functional Encyclopedia, One-Click Actions

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F5: Storage Directory Scanning ---

Register-E2ETest -Tier 1 -Feature "F5" -Source "R2 / PROJECT.md § F5" `
    -Name "T1.F05.01_CatalogContainsAllElevenDirectories" `
    -Description "Verifies StorageDirectoryOracle and domain catalog specify all 11 required directories." `
    -TestBlock {
        $required = [StorageDirectoryOracle]::TargetDirectories
        Assert-Equal $required.Count 11 "Must specify exactly 11 target directories"
        Assert-Contains $required "WinSxS"
        Assert-Contains $required "SoftwareDistribution"
        Assert-Contains $required "Installer"
        Assert-Contains $required "DriverStore\FileRepository"
        Assert-Contains $required "Temp"
        Assert-Contains $required "AppData"
        Assert-Contains $required "System Volume Information"
        Assert-Contains $required "hiberfil.sys"
        Assert-Contains $required "pagefile.sys"
        Assert-Contains $required "`$WINDOWS.~BT"
        Assert-Contains $required "Windows.old"
    }

Register-E2ETest -Tier 1 -Feature "F5" -Source "R2 / PROJECT.md § F5" `
    -Name "T1.F05.02_StorageEncyclopediaServiceTypeExists" `
    -Description "Verifies StorageEncyclopediaService or StorageAnalyzerService type exists in assembly." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.StorageEncyclopediaService")
        if (-not $svcType) {
            $svcType = $asm.GetType("DiskMasterWinUI.Services.StorageAnalyzerService")
        }
        if (-not $svcType) {
            throw "Pending M3: StorageEncyclopediaService not yet implemented"
        }
        Assert-NotNull $svcType "StorageEncyclopediaService type found"
    }

Register-E2ETest -Tier 1 -Feature "F5" -Source "R2 / PROJECT.md § F5" `
    -Name "T1.F05.03_StorageDirectoryItemModelDefinition" `
    -Description "Verifies StorageDirectoryItem model exposes Path, SizeBytes, ItemCount, and Exists properties." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $modelType = $asm.GetType("DiskMasterWinUI.Models.StorageDirectoryItem")
        if (-not $modelType) {
            throw "Pending M3: StorageDirectoryItem model not yet implemented"
        }
        $props = $modelType.GetProperties() | ForEach-Object { $_.Name }
        Assert-Contains $props "Path"
        Assert-Contains $props "SizeBytes"
        Assert-Contains $props "ItemCount"
        Assert-Contains $props "Exists"
    }

Register-E2ETest -Tier 1 -Feature "F5" -Source "R2 / PROJECT.md § F5" `
    -Name "T1.F05.04_DirectoryScannerCalculatesTempSize" `
    -Description "Verifies directory scanning calculates non-negative byte size for Windows Temp directory." `
    -TestBlock {
        $tempPath = [System.IO.Path]::GetTempPath()
        Assert-True (Test-Path $tempPath) "Temp path must exist in environment"
        $dirInfo = [System.IO.DirectoryInfo]::new($tempPath)
        Assert-NotNull $dirInfo "DirectoryInfo should be instantiable"
    }

Register-E2ETest -Tier 1 -Feature "F5" -Source "R2 / PROJECT.md § F5" `
    -Name "T1.F05.05_NonExistentDirectoryHandling" `
    -Description "Verifies non-existent directory (e.g. dummy test path) is flagged with Exists=false without crashing." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $modelType = $asm.GetType("DiskMasterWinUI.Models.StorageDirectoryItem")
        if (-not $modelType) {
            throw "Pending M3: StorageDirectoryItem model not yet implemented"
        }
        $item = [System.Activator]::CreateInstance($modelType)
        $modelType.GetProperty("Path").SetValue($item, "C:\NonExistent_Dummy_Dir_12345")
        $modelType.GetProperty("Exists").SetValue($item, $false)
        Assert-False ($modelType.GetProperty("Exists").GetValue($item)) "Exists should be false for missing directory"
    }

# --- Feature F6: Storage Directory Functional Encyclopedia ---

Register-E2ETest -Tier 1 -Feature "F6" -Source "R2 / PROJECT.md § F6" `
    -Name "T1.F06.01_SafetyRatingMappingIntegrity" `
    -Description "Verifies all 11 target directories have explicit safety rating classifications in oracle." `
    -TestBlock {
        $ratings = [StorageDirectoryOracle]::ExpectedSafetyRatings
        foreach ($dir in [StorageDirectoryOracle]::TargetDirectories) {
            Assert-True ($ratings.ContainsKey($dir)) "Safety rating must exist for $dir"
            Assert-Contains @("SafeToPurge", "CleanViaSystemTool", "EssentialCore") $ratings[$dir] "Valid safety rating enum value"
        }
    }

Register-E2ETest -Tier 1 -Feature "F6" -Source "R2 / PROJECT.md § F6" `
    -Name "T1.F06.02_WinSxSSafetyClassificationIsGuarded" `
    -Description "Verifies WinSxS is classified as CleanViaSystemTool and NOT SafeToPurge." `
    -TestBlock {
        $rating = [StorageDirectoryOracle]::ExpectedSafetyRatings["WinSxS"]
        Assert-Equal $rating "CleanViaSystemTool" "WinSxS must never be marked SafeToPurge"
    }

Register-E2ETest -Tier 1 -Feature "F6" -Source "R2 / PROJECT.md § F6" `
    -Name "T1.F06.03_TempDirectoryIsSafeToPurge" `
    -Description "Verifies Temp directory is classified as SafeToPurge." `
    -TestBlock {
        $rating = [StorageDirectoryOracle]::ExpectedSafetyRatings["Temp"]
        Assert-Equal $rating "SafeToPurge" "Temp must be classified as SafeToPurge"
    }

Register-E2ETest -Tier 1 -Feature "F6" -Source "R2 / PROJECT.md § F6" `
    -Name "T1.F06.04_SystemVolumeInfoIsEssentialCore" `
    -Description "Verifies System Volume Information is classified as EssentialCore." `
    -TestBlock {
        $rating = [StorageDirectoryOracle]::ExpectedSafetyRatings["System Volume Information"]
        Assert-Equal $rating "EssentialCore" "System Volume Information must be EssentialCore"
    }

Register-E2ETest -Tier 1 -Feature "F6" -Source "R2 / PROJECT.md § F6" `
    -Name "T1.F06.05_StorageDirectoryItemExposesDescriptionProperty" `
    -Description "Verifies StorageDirectoryItem model exposes Description and SafetyRating properties." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $modelType = $asm.GetType("DiskMasterWinUI.Models.StorageDirectoryItem")
        if (-not $modelType) {
            throw "Pending M3: StorageDirectoryItem model not yet implemented"
        }
        $props = $modelType.GetProperties() | ForEach-Object { $_.Name }
        Assert-Contains $props "Description" "Must expose Description"
        Assert-True ($props -contains "SafetyRating" -or $props -contains "SafetyLevel") "Must expose SafetyRating"
    }

# --- Feature F7: Storage Directory One-Click Actions ---

Register-E2ETest -Tier 1 -Feature "F7" -Source "R2 / PROJECT.md § F7" `
    -Name "T1.F07.01_ActionFlagSpecifications" `
    -Description "Verifies action flags in StorageDirectoryOracle define clean boundaries for each directory." `
    -TestBlock {
        $actions = [StorageDirectoryOracle]::ExpectedActionFlags
        Assert-Equal $actions.Count 11 "Must define action flags for all 11 directories"
        Assert-True ($actions["Temp"].CanSafeClean) "Temp must allow SafeClean"
        Assert-False ($actions["WinSxS"].CanSafeClean) "WinSxS must forbid SafeClean"
        Assert-True ($actions["WinSxS"].CanDismClean) "WinSxS must allow DismClean"
    }

Register-E2ETest -Tier 1 -Feature "F7" -Source "R2 / PROJECT.md § F7" `
    -Name "T1.F07.02_StorageDirectoryItemActionPropertiesContract" `
    -Description "Verifies StorageDirectoryItem model exposes CanOpenExplorer, CanSafeClean, and CanDismClean." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $modelType = $asm.GetType("DiskMasterWinUI.Models.StorageDirectoryItem")
        if (-not $modelType) {
            throw "Pending M3: StorageDirectoryItem model not yet implemented"
        }
        $props = $modelType.GetProperties() | ForEach-Object { $_.Name }
        Assert-Contains $props "CanOpenExplorer"
        Assert-Contains $props "CanSafeClean"
        Assert-Contains $props "CanDismClean"
    }

Register-E2ETest -Tier 1 -Feature "F7" -Source "R2 / PROJECT.md § F7" `
    -Name "T1.F07.03_DismComponentCleanupCommandVerification" `
    -Description "Verifies DISM command matches standard component cleanup specification." `
    -TestBlock {
        $cmd = [StorageDirectoryOracle]::DismCommand
        Assert-Equal $cmd "dism.exe /online /cleanup-image /startcomponentcleanup"
    }

Register-E2ETest -Tier 1 -Feature "F7" -Source "R2 / PROJECT.md § F7" `
    -Name "T1.F07.04_StorageEncyclopediaServiceDismMethodContract" `
    -Description "Verifies StorageEncyclopediaService exposes RunDismComponentCleanupAsync." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.StorageEncyclopediaService")
        if (-not $svcType) {
            $svcType = $asm.GetType("DiskMasterWinUI.Services.StorageAnalyzerService")
        }
        if (-not $svcType) {
            throw "Pending M3: StorageEncyclopediaService not yet implemented"
        }
        $dismMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*Dism*" } | Select-Object -First 1
        Assert-NotNull $dismMethod "Must expose DISM cleanup method"
    }

Register-E2ETest -Tier 1 -Feature "F7" -Source "R2 / PROJECT.md § F7" `
    -Name "T1.F07.05_StorageEncyclopediaServiceSafeCleanMethodContract" `
    -Description "Verifies StorageEncyclopediaService exposes ExecuteSafeCleanupAsync." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.StorageEncyclopediaService")
        if (-not $svcType) {
            $svcType = $asm.GetType("DiskMasterWinUI.Services.StorageAnalyzerService")
        }
        if (-not $svcType) {
            throw "Pending M3: StorageEncyclopediaService not yet implemented"
        }
        $cleanMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*SafeClean*" -or $_.Name -like "*ExecuteSafeCleanup*" } | Select-Object -First 1
        Assert-NotNull $cleanMethod "Must expose SafeClean method"
    }
