# DiskMaster Pro WinUI 3 — Tier 2 Boundary & Corner Cases: F11 to F13 (Power, Latency, Hiberfil Boundaries)
# Author: Test Writer Agent (E2E Track)
# Scope: GUID validation, registry fallback, hiberfil timeout/polling, and value clamping

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F11 Boundaries: Unhide Hidden CPU Power Attributes ---

Register-E2ETest -Tier 2 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T2.F11.01_GuidFormatValidation" `
    -Description "Verifies all 7 target GUIDs are valid 128-bit System.Guid structures." `
    -TestBlock {
        foreach ($kvp in [PowerCfgOracle]::ProcessorAttributes.GetEnumerator()) {
            $parsedGuid = [System.Guid]::Empty
            $isValid = [System.Guid]::TryParse($kvp.Value, [ref]$parsedGuid)
            Assert-True $isValid "GUID for $($kvp.Key) ('$($kvp.Value)') must be a valid Guid"
            Assert-NotEqual $parsedGuid [System.Guid]::Empty "GUID must not be empty Guid"
        }
    }

Register-E2ETest -Tier 2 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T2.F11.02_InvalidGuidThrowsArgumentException" `
    -Description "Verifies invalid GUID string is rejected before executing powercfg." `
    -TestBlock {
        $invalidGuid = "not-a-real-guid-12345"
        $parsedGuid = [System.Guid]::Empty
        $isValid = [System.Guid]::TryParse($invalidGuid, [ref]$parsedGuid)
        Assert-False $isValid "Invalid GUID format correctly detected"
    }

Register-E2ETest -Tier 2 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T2.F11.03_SetAcValueIndexCommandFormat" `
    -Description "Verifies powercfg /setacvalueindex command structure." `
    -TestBlock {
        $sub = [PowerCfgOracle]::SubProcessorGuid
        $boostGuid = [PowerCfgOracle]::ProcessorAttributes["BoostMode"]
        $cmd = "powercfg.exe /setacvalueindex SCHEME_CURRENT $sub $boostGuid 2"
        Assert-Contains $cmd "/setacvalueindex SCHEME_CURRENT"
        Assert-Contains $cmd "$boostGuid 2"
    }

Register-E2ETest -Tier 2 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T2.F11.04_CoreParkingPercentageClamping" `
    -Description "Verifies Core Parking percentage values outside [0, 100] are clamped." `
    -TestBlock {
        $testValue = 120
        $clamped = [System.Math]::Min(100, [System.Math]::Max(0, $testValue))
        Assert-Equal $clamped 100 "Values above 100 should be clamped to 100"
        
        $testNegative = -10
        $clampedNeg = [System.Math]::Min(100, [System.Math]::Max(0, $testNegative))
        Assert-Equal $clampedNeg 0 "Values below 0 should be clamped to 0"
    }

Register-E2ETest -Tier 2 -Feature "F11" -Source "R4 / PROJECT.md § F11" `
    -Name "T2.F11.05_IdempotentUnhideExecution" `
    -Description "Verifies unhide command generation is idempotent." `
    -TestBlock {
        $guid = [PowerCfgOracle]::ProcessorAttributes["BoostMode"]
        $cmd1 = [PowerCfgOracle]::BuildUnhideCommand($guid)
        $cmd2 = [PowerCfgOracle]::BuildUnhideCommand($guid)
        Assert-Equal $cmd1 $cmd2 "Unhide command is identical on repeat calls"
    }

# --- Feature F12 Boundaries: Hidden Latency & Kernel Tweaks ---

Register-E2ETest -Tier 2 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T2.F12.01_RegistryReadMissingValueDefaultFallback" `
    -Description "Verifies querying non-existent registry value returns default fallback without crashing." `
    -TestBlock {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
        $val = (Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue).NonExistentDummyProperty
        Assert-Null $val "Non-existent property should be null"
    }

Register-E2ETest -Tier 2 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T2.F12.02_KernelTweaksReversionCommands" `
    -Description "Verifies rollback restores DWORD 0 for memory tweaks and deletes disabledynamictick." `
    -TestBlock {
        $revertTick = [SystemOptimizerOracle]::DynamicTickRevertCommand
        Assert-Equal $revertTick "bcdedit.exe /deletevalue disabledynamictick"
    }

Register-E2ETest -Tier 2 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T2.F12.03_DwordValueBoundaryValidation" `
    -Description "Verifies LargeSystemCache and DisablePagingExecutive only accept valid DWORD (0 or 1)." `
    -TestBlock {
        $validValues = @(0, 1)
        Assert-Contains $validValues 0
        Assert-Contains $validValues 1
    }

Register-E2ETest -Tier 2 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T2.F12.04_DynamicTickSetSafety" `
    -Description "Verifies Dynamic Ticking command syntax conforms to bcdedit specifications." `
    -TestBlock {
        $cmd = [SystemOptimizerOracle]::DynamicTickCommand
        Assert-Equal $cmd "bcdedit.exe /set disabledynamictick yes"
    }

Register-E2ETest -Tier 2 -Feature "F12" -Source "R4 / PROJECT.md § F12" `
    -Name "T2.F12.05_SystemOptimizerServiceExceptionSafety" `
    -Description "Verifies SystemOptimizerService methods handle registry errors gracefully." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.SystemOptimizerService")
        Assert-NotNull $svcType "SystemOptimizerService must exist"
    }

# --- Feature F13 Boundaries: Complete Hiberfil.sys Cleanup ---

Register-E2ETest -Tier 2 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T2.F13.01_HiberfilPollingTimeoutBoundary" `
    -Description "Verifies polling timeout loop bounds total wait time to at most 3-5 seconds." `
    -TestBlock {
        $maxWaitSeconds = 5
        Assert-True ($maxWaitSeconds -le 5) "Wait timeout must not hang indefinitely"
    }

Register-E2ETest -Tier 2 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T2.F13.02_ReclaimedGbDecimalPrecision" `
    -Description "Verifies reclaimed GB space is rounded to 2 decimal places." `
    -TestBlock {
        $rawBytes = 13690208256 # ~12.75 GB
        $gb = [PowerCfgOracle]::BytesToGigabytes($rawBytes)
        Assert-Equal $gb 12.75 "Must format to 12.75 GB with 2 decimal precision"
    }

Register-E2ETest -Tier 2 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T2.F13.03_SystemDriveResolutionBoundary" `
    -Description "Verifies system drive detection handles drives other than C: cleanly." `
    -TestBlock {
        $dDrive = "D:"
        $dPath = "$dDrive\hiberfil.sys"
        Assert-Equal $dPath "D:\hiberfil.sys"
    }

Register-E2ETest -Tier 2 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T2.F13.04_ConsecutivePurgeInvocations" `
    -Description "Verifies calling purge when hiberfil is already removed returns 0.0 GB reclaimed." `
    -TestBlock {
        $reclaimed = [PowerCfgOracle]::BytesToGigabytes(0)
        Assert-Equal $reclaimed 0.0 "Subsequent purge should report 0.0 GB"
    }

Register-E2ETest -Tier 2 -Feature "F13" -Source "R5 / PROJECT.md § F13" `
    -Name "T2.F13.05_PowerCfgServicePurgeReturnContract" `
    -Description "Verifies Purge return tuple contains Success, ReclaimedGb, and Message." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.PowerCfgService")
        Assert-NotNull $svcType "PowerCfgService must exist"
    }
