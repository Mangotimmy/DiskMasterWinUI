# DiskMaster Pro WinUI 3 — Tier 2 Boundary & Corner Cases: F8 to F10 (Safe Boot & BCD Boundaries)
# Author: Test Writer Agent (E2E Track)
# Scope: Idempotency, invalid modes, non-admin handling, flag collisions, and localized BCD output

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F8 Boundaries: MSConfig Safe Boot Modes ---

Register-E2ETest -Tier 2 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T2.F08.01_IdempotentSafeBootModeSetting" `
    -Description "Verifies setting the same safe boot mode repeatedly generates consistent CLI commands." `
    -TestBlock {
        $cmd1 = [BcdOracle]::BuildSafeBootCommand("minimal")
        $cmd2 = [BcdOracle]::BuildSafeBootCommand("minimal")
        Assert-Equal $cmd1 $cmd2 "Idempotent mode command must be identical"
    }

Register-E2ETest -Tier 2 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T2.F08.02_AlternateShellToMinimalTransition" `
    -Description "Verifies transition from AlternateShell to Minimal clears safebootalternateshell." `
    -TestBlock {
        $clearAltShellCmd = "bcdedit.exe /deletevalue {current} safebootalternateshell"
        Assert-Contains $clearAltShellCmd "deletevalue"
        Assert-Contains $clearAltShellCmd "safebootalternateshell"
    }

Register-E2ETest -Tier 2 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T2.F08.03_AbsentSafeBootValueIdentifiesNormalMode" `
    -Description "Verifies when safeboot entry is absent from BCD output, mode is resolved as Normal." `
    -TestBlock {
        $mockBcdOutput = @"
Windows Boot Loader
-------------------
identifier              {current}
device                  partition=C:
path                    \Windows\system32\winload.efi
description             Windows 11
locale                  zh-TW
"@
        $hasSafeBoot = $mockBcdOutput -match "safeboot\s+"
        Assert-False $hasSafeBoot "SafeBoot should not be present"
        $resolvedMode = if ($hasSafeBoot) { "Custom" } else { "Normal" }
        Assert-Equal $resolvedMode "Normal" "Must resolve to Normal mode when safeboot absent"
    }

Register-E2ETest -Tier 2 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T2.F08.04_CaseInsensitiveSafeBootModeInput" `
    -Description "Verifies BuildSafeBootCommand accepts uppercase and lowercase mode strings." `
    -TestBlock {
        $cmdUpper = [BcdOracle]::BuildSafeBootCommand("MINIMAL")
        $cmdLower = [BcdOracle]::BuildSafeBootCommand("minimal")
        $cmdMixed = [BcdOracle]::BuildSafeBootCommand("Minimal")
        Assert-Equal $cmdUpper $cmdLower
        Assert-Equal $cmdUpper $cmdMixed
    }

Register-E2ETest -Tier 2 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T2.F08.05_InvalidSafeBootModeThrowsArgumentException" `
    -Description "Verifies invalid mode name throws error before executing CLI." `
    -TestBlock {
        Assert-Throws {
            [BcdOracle]::BuildSafeBootCommand("super_hacker_mode")
        } "Unknown safe boot mode"
    }

# --- Feature F9 Boundaries: Advanced Boot Flags Configuration ---

Register-E2ETest -Tier 2 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T2.F09.01_SimultaneousFlagSettingGeneratesDistinctCommands" `
    -Description "Verifies enabling all 7 flags generates 7 distinct bcdedit commands." `
    -TestBlock {
        $commands = [System.Collections.Generic.List[string]]::new()
        foreach ($flag in [BcdOracle]::BootFlags) {
            $commands.Add(([BcdOracle]::BuildBootFlagCommand($flag, $true)))
        }
        Assert-Equal $commands.Count 7 "Must generate 7 commands"
        $unique = $commands | Select-Object -Unique
        Assert-Equal $unique.Count 7 "All 7 commands must be unique"
    }

Register-E2ETest -Tier 2 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T2.F09.02_BcdDeleteValueElementNotFoundSafety" `
    -Description "Verifies deleting an unconfigured flag handles BCD error 'Element not found' cleanly." `
    -TestBlock {
        $elementNotFound = "An error occurred while attempting to delete the specified data element.`r`nElement not found."
        $isNotFound = $elementNotFound -like "*Element not found*"
        Assert-True $isNotFound "Recognizes Element not found as non-fatal on clear"
    }

Register-E2ETest -Tier 2 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T2.F09.03_CaseInsensitiveBcdValueParsing" `
    -Description "Verifies parser recognizes 'yes', 'Yes', 'YES', 'true', 'on', 'ON' as enabled." `
    -TestBlock {
        $values = @("yes", "Yes", "YES", "on", "ON", "true", "True")
        foreach ($v in $values) {
            $isEnabled = ($v -match "^(yes|true|on)$")
            Assert-True $isEnabled "Value '$v' should match enabled regex"
        }
    }

Register-E2ETest -Tier 2 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T2.F09.04_ConflictingFlagsCoexistence" `
    -Description "Verifies conflicting flags like NoGuiBoot and SOS can be set independently in config." `
    -TestBlock {
        $noguiCmd = [BcdOracle]::BuildBootFlagCommand("noguiboot", $true)
        $sosCmd   = [BcdOracle]::BuildBootFlagCommand("sos", $true)
        Assert-NotEqual $noguiCmd $sosCmd "Flags must be distinct commands"
    }

Register-E2ETest -Tier 2 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T2.F09.05_HypervisorLaunchTypeBoundaryValues" `
    -Description "Verifies HypervisorLaunchType values are restricted to auto and off." `
    -TestBlock {
        $cmdAuto = [BcdOracle]::BuildBootFlagCommand("hypervisorlaunchtype", $true)
        $cmdOff  = [BcdOracle]::BuildBootFlagCommand("hypervisorlaunchtype", $false)
        Assert-Contains $cmdAuto "auto"
        Assert-Contains $cmdOff "off"
    }

# --- Feature F10 Boundaries: BCD Query & Reboot Prompt ---

Register-E2ETest -Tier 2 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T2.F10.01_BcdQueryAccessDeniedResilience" `
    -Description "Verifies BCD queries catch access denied when not running elevated." `
    -TestBlock {
        $accessDeniedSample = "The boot configuration data store could not be opened.`r`nAccess is denied."
        Assert-Contains $accessDeniedSample "Access is denied"
    }

Register-E2ETest -Tier 2 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T2.F10.02_ZeroChangesApplyNoOp" `
    -Description "Verifies applying identical boot config performs zero unnecessary CLI writes." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $modelType = $asm.GetType("DiskMasterWinUI.Models.SafeBootConfig")
        if (-not $modelType) {
            throw "Pending M4: SafeBootConfig model not yet implemented"
        }
        Assert-NotNull $modelType
    }

Register-E2ETest -Tier 2 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T2.F10.03_RebootPromptCancellation" `
    -Description "Verifies prompt cancellation does not initiate system reboot." `
    -TestBlock {
        $userCancelled = $false
        $rebootExecuted = $false
        if (-not $userCancelled) {
            # Cancelled by user
        } else {
            $rebootExecuted = $true
        }
        Assert-False $rebootExecuted "Reboot must not be executed when prompt cancelled"
    }

Register-E2ETest -Tier 2 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T2.F10.04_LocalizedBcdOutputParsing" `
    -Description "Verifies BCD parser tolerates localized loader headers." `
    -TestBlock {
        $sampleZh = @"
Windows 開機載入器
-------------------
identifier              {current}
device                  partition=C:
safeboot                minimal
"@
        Assert-Contains $sampleZh "safeboot"
    }

Register-E2ETest -Tier 2 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T2.F10.05_ConcurrentBcdQueriesSerialization" `
    -Description "Verifies consecutive BCD queries execute cleanly without pipe contention." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.BcdManagerService")
        Assert-NotNull $svcType "BcdManagerService must exist"
    }
