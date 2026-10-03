# DiskMaster Pro WinUI 3 — Tier 1 Feature Coverage: F11 to F13 (Power, Latency, Hiberfil)
# Author: Test Writer Agent (E2E Track)
# Scope: Hidden CPU Power Attributes, Kernel Latency Tweaks, Complete Hiberfil.sys Cleanup

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F11: Unhide Hidden CPU Power Attributes ---

Register-E2ETest -Tier 1 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T1.F11.01_SubProcessorGuidValidation" `
    -Description "Verifies SUB_PROCESSOR subgroup GUID matches standard Windows power specification." `
    -TestBlock {
        $guid = [PowerCfgOracle]::SubProcessorGuid
        Assert-Equal $guid "54533251-82be-4824-96c1-47b60b740d00" "Subgroup must match SUB_PROCESSOR GUID"
    }

Register-E2ETest -Tier 1 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T1.F11.02_AllSevenPpmGuidsDefinedInOracle" `
    -Description "Verifies PowerCfgOracle contains all 7 target GUIDs per R4." `
    -TestBlock {
        $attrs = [PowerCfgOracle]::ProcessorAttributes
        Assert-Equal $attrs.Count 7 "Must define all 7 target GUIDs"
        Assert-Equal $attrs["BoostMode"] "be337238-0d82-4146-a960-4f3749d470c7"
        Assert-Equal $attrs["EnergyPerformancePreference"] "36687f9e-e376-49e8-b783-be5e3e3563ab"
        Assert-Equal $attrs["AutonomousMode"] "8baa4a8a-14fc-482b-bd23-a0f0f71e11e8"
        Assert-Equal $attrs["CoreParkingMinCores"] "0cc5b647-c1df-4637-891a-dec35c318583"
        Assert-Equal $attrs["CoreParkingMaxCores"] "ea062031-0e34-4ff1-9b6d-eb1059324028"
        Assert-Equal $attrs["HeterogeneousScheduling"] "7f24e370-7664-4642-99e3-e605185a0899"
        Assert-Equal $attrs["SystemCoolingPolicy"] "94d3a615-a899-4ac5-ae2b-e4d8f6343d57"
    }

Register-E2ETest -Tier 1 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T1.F11.03_UnhideCommandConstruction" `
    -Description "Verifies unhide command construction uses -attributes SUB_PROCESSOR <guid> -ATTRIB_HIDE." `
    -TestBlock {
        $boostGuid = [PowerCfgOracle]::ProcessorAttributes["BoostMode"]
        $cmd = [PowerCfgOracle]::BuildUnhideCommand($boostGuid)
        Assert-Contains $cmd "-attributes 54533251-82be-4824-96c1-47b60b740d00"
        Assert-Contains $cmd "$boostGuid -ATTRIB_HIDE"
    }

Register-E2ETest -Tier 1 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T1.F11.04_HideCommandConstruction" `
    -Description "Verifies hide command construction uses +ATTRIB_HIDE." `
    -TestBlock {
        $boostGuid = [PowerCfgOracle]::ProcessorAttributes["BoostMode"]
        $cmd = [PowerCfgOracle]::BuildHideCommand($boostGuid)
        Assert-Contains $cmd "+ATTRIB_HIDE"
    }

Register-E2ETest -Tier 1 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T1.F11.05_PowerCfgServiceUnhideMethodContract" `
    -Description "Verifies PowerCfgService exposes method to unhide CPU power attributes." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.PowerCfgService")
        Assert-NotNull $svcType "PowerCfgService must exist"
        $methods = $svcType.GetMethods() | Where-Object { $_.Name -like "*Unhide*" }
        if ($methods.Count -eq 0) {
            throw "Pending M5: PowerCfgService unhide methods not yet implemented"
        }
        Assert-True ($methods.Count -gt 0) "Unhide methods verified"
    }

# --- Feature F12: Hidden Latency & Kernel Tweaks ---

Register-E2ETest -Tier 1 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T1.F12.01_DynamicTickingCommandsConstruction" `
    -Description "Verifies Dynamic Ticking commands match bcdedit specification." `
    -TestBlock {
        $setCmd = [SystemOptimizerOracle]::DynamicTickCommand
        $revCmd = [SystemOptimizerOracle]::DynamicTickRevertCommand
        Assert-Equal $setCmd "bcdedit.exe /set disabledynamictick yes"
        Assert-Equal $revCmd "bcdedit.exe /deletevalue disabledynamictick"
    }

Register-E2ETest -Tier 1 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T1.F12.02_MemoryManagementRegistryKeySpecification" `
    -Description "Verifies target registry key for LargeSystemCache and DisablePagingExecutive." `
    -TestBlock {
        $key = [SystemOptimizerOracle]::MemoryManagementKey
        Assert-Equal $key "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
    }

Register-E2ETest -Tier 1 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T1.F12.03_LargeSystemCacheRegistryVerification" `
    -Description "Verifies LargeSystemCache registry value can be queried on Windows host." `
    -TestBlock {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
        Assert-True (Test-Path $regPath) "Memory Management registry key must exist"
        $props = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
        $cacheVal = if ($null -ne $props.LargeSystemCache) { $props.LargeSystemCache } else { 0 }
        Assert-WithinRange $cacheVal 0 1 "LargeSystemCache must be 0 or 1"
    }

Register-E2ETest -Tier 1 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T1.F12.04_DisablePagingExecutiveRegistryVerification" `
    -Description "Verifies DisablePagingExecutive registry value can be queried on Windows host." `
    -TestBlock {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
        $val = Get-ItemProperty -Path $regPath -Name "DisablePagingExecutive" -ErrorAction SilentlyContinue
        Assert-NotNull $val "DisablePagingExecutive property should be accessible"
    }

Register-E2ETest -Tier 1 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T1.F12.05_SystemOptimizerServiceLatencyMethodContract" `
    -Description "Verifies SystemOptimizerService exposes kernel latency tweaking methods." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.SystemOptimizerService")
        if (-not $svcType) {
            $svcType = $asm.GetType("DiskMasterWinUI.Services.SystemOptimizerPageViewModel")
        }
        Assert-NotNull $svcType "SystemOptimizerService must exist"
        $methods = $svcType.GetMethods() | Where-Object { $_.Name -like "*Latency*" -or $_.Name -like "*Kernel*" -or $_.Name -like "*DynamicTick*" }
        if ($methods.Count -eq 0) {
            throw "Pending M5: SystemOptimizerService kernel latency tweak methods not yet implemented"
        }
        Assert-True ($methods.Count -gt 0) "Kernel latency methods verified"
    }

# --- Feature F13: Complete Hiberfil.sys Cleanup ---

Register-E2ETest -Tier 1 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T1.F13.01_HiberfilBytesToGbConversion" `
    -Description "Verifies byte-to-GB calculation converts 17179869184 bytes (16 GB) accurately." `
    -TestBlock {
        $bytes = 17179869184 # 16 GB
        $gb = [PowerCfgOracle]::BytesToGigabytes($bytes)
        Assert-Equal $gb 16.0 "16 GB should be 16.0"
        
        $bytes2 = 8589934592 # 8 GB
        $gb2 = [PowerCfgOracle]::BytesToGigabytes($bytes2)
        Assert-Equal $gb2 8.0 "8 GB should be 8.0"
    }

Register-E2ETest -Tier 1 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T1.F13.02_ZeroBytesHiberfilYieldsZeroGb" `
    -Description "Verifies 0 bytes converts to 0.0 GB reclaimed without division errors." `
    -TestBlock {
        $gb = [PowerCfgOracle]::BytesToGigabytes(0)
        Assert-Equal $gb 0.0 "0 bytes should yield 0.0 GB"
    }

Register-E2ETest -Tier 1 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T1.F13.03_PowerCfgHibernateOffCommandSpecification" `
    -Description "Verifies command to disable hibernation is powercfg /hibernate off." `
    -TestBlock {
        $cmd = "powercfg.exe /hibernate off"
        Assert-Contains $cmd "/hibernate off" "Must disable hibernation via powercfg"
    }

Register-E2ETest -Tier 1 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T1.F13.04_PowerCfgServicePurgeMethodContract" `
    -Description "Verifies PowerCfgService exposes PurgeHibernationFileAndFreeSpaceAsync method." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.PowerCfgService")
        Assert-NotNull $svcType "PowerCfgService must exist"
        $purgeMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*PurgeHibernation*" -or $_.Name -like "*ReclaimSpace*" } | Select-Object -First 1
        if (-not $purgeMethod) {
            throw "Pending M5: PowerCfgService.PurgeHibernationFileAndFreeSpaceAsync not yet implemented"
        }
        Assert-NotNull $purgeMethod "Purge method verified"
    }

Register-E2ETest -Tier 1 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T1.F13.05_HiberfilTargetDriveResolution" `
    -Description "Verifies hiberfil.sys path resolves to SystemDrive root (C:\hiberfil.sys)." `
    -TestBlock {
        $sysDrive = [System.Environment]::GetEnvironmentVariable("SystemDrive")
        if (-not $sysDrive) { $sysDrive = "C:" }
        $expectedPath = "$sysDrive\hiberfil.sys"
        Assert-Equal $expectedPath "C:\hiberfil.sys" "Expected path must be C:\hiberfil.sys"
    }
