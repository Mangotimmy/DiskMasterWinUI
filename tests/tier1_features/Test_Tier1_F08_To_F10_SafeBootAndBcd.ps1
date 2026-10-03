# DiskMaster Pro WinUI 3 — Tier 1 Feature Coverage: F8 to F10 (Safe Boot & BCD)
# Author: Test Writer Agent (E2E Track)
# Scope: MSConfig Safe Boot Modes, Advanced Boot Flags, BCD Query & Reboot

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F8: MSConfig Safe Boot Modes ---

Register-E2ETest -Tier 1 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T1.F08.01_SafeBootModesDefinedInOracle" `
    -Description "Verifies BcdOracle specifies all 5 Safe Boot modes (Normal, Minimal, AlternateShell, Network, DsRepair)." `
    -TestBlock {
        $modes = [BcdOracle]::SafeBootModes
        Assert-Equal $modes.Count 5 "Must define exactly 5 modes"
        Assert-Contains $modes "Normal"
        Assert-Contains $modes "Minimal"
        Assert-Contains $modes "AlternateShell"
        Assert-Contains $modes "Network"
        Assert-Contains $modes "DsRepair"
    }

Register-E2ETest -Tier 1 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T1.F08.02_MinimalSafeBootCommandConstruction" `
    -Description "Verifies Minimal safe boot mode generates bcdedit /set {current} safeboot minimal." `
    -TestBlock {
        $cmd = [BcdOracle]::BuildSafeBootCommand("minimal")
        Assert-Equal $cmd "bcdedit.exe /set {current} safeboot minimal"
    }

Register-E2ETest -Tier 1 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T1.F08.03_NetworkSafeBootCommandConstruction" `
    -Description "Verifies Network safe boot mode generates bcdedit /set {current} safeboot network." `
    -TestBlock {
        $cmd = [BcdOracle]::BuildSafeBootCommand("network")
        Assert-Equal $cmd "bcdedit.exe /set {current} safeboot network"
    }

Register-E2ETest -Tier 1 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T1.F08.04_DsRepairSafeBootCommandConstruction" `
    -Description "Verifies DsRepair safe boot mode generates bcdedit /set {current} safeboot dsrepair." `
    -TestBlock {
        $cmd = [BcdOracle]::BuildSafeBootCommand("dsrepair")
        Assert-Equal $cmd "bcdedit.exe /set {current} safeboot dsrepair"
    }

Register-E2ETest -Tier 1 -Feature "F8" -Source "R3 / PROJECT.md § F8" `
    -Name "T1.F08.05_NormalBootClearsSafeBootValue" `
    -Description "Verifies Normal boot mode executes /deletevalue to clear safeboot." `
    -TestBlock {
        $cmd = [BcdOracle]::BuildSafeBootCommand("normal")
        Assert-Equal $cmd "bcdedit.exe /deletevalue {current} safeboot"
    }

# --- Feature F9: Advanced Boot Flags Configuration ---

Register-E2ETest -Tier 1 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T1.F09.01_BootFlagsDefinedInOracle" `
    -Description "Verifies BcdOracle specifies all 7 required boot flags." `
    -TestBlock {
        $flags = [BcdOracle]::BootFlags
        Assert-Equal $flags.Count 7 "Must define 7 boot flags"
        Assert-Contains $flags "noguiboot"
        Assert-Contains $flags "bootlog"
        Assert-Contains $flags "basevideo"
        Assert-Contains $flags "sos"
        Assert-Contains $flags "testsigning"
        Assert-Contains $flags "nointegritychecks"
        Assert-Contains $flags "hypervisorlaunchtype"
    }

Register-E2ETest -Tier 1 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T1.F09.02_NoGuiBootCommandConstruction" `
    -Description "Verifies NoGuiBoot toggle generates correct bcdedit set and deletevalue commands." `
    -TestBlock {
        $setCmd = [BcdOracle]::BuildBootFlagCommand("noguiboot", $true)
        $delCmd = [BcdOracle]::BuildBootFlagCommand("noguiboot", $false)
        Assert-Equal $setCmd "bcdedit.exe /set {current} noguiboot yes"
        Assert-Equal $delCmd "bcdedit.exe /deletevalue {current} noguiboot"
    }

Register-E2ETest -Tier 1 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T1.F09.03_BootLogCommandConstruction" `
    -Description "Verifies BootLog toggle generates correct bcdedit set and deletevalue commands." `
    -TestBlock {
        $setCmd = [BcdOracle]::BuildBootFlagCommand("bootlog", $true)
        $delCmd = [BcdOracle]::BuildBootFlagCommand("bootlog", $false)
        Assert-Equal $setCmd "bcdedit.exe /set {current} bootlog yes"
        Assert-Equal $delCmd "bcdedit.exe /deletevalue {current} bootlog"
    }

Register-E2ETest -Tier 1 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T1.F09.04_TestSigningAndNoIntegrityChecksCommands" `
    -Description "Verifies TestSigning and NoIntegrityChecks generate on/off values." `
    -TestBlock {
        $tsOn = [BcdOracle]::BuildBootFlagCommand("testsigning", $true)
        $tsOff = [BcdOracle]::BuildBootFlagCommand("testsigning", $false)
        Assert-Equal $tsOn "bcdedit.exe /set {current} testsigning on"
        Assert-Equal $tsOff "bcdedit.exe /set {current} testsigning off"

        $nicOn = [BcdOracle]::BuildBootFlagCommand("nointegritychecks", $true)
        $nicOff = [BcdOracle]::BuildBootFlagCommand("nointegritychecks", $false)
        Assert-Equal $nicOn "bcdedit.exe /set {current} nointegritychecks on"
        Assert-Equal $nicOff "bcdedit.exe /set {current} nointegritychecks off"
    }

Register-E2ETest -Tier 1 -Feature "F9" -Source "R3 / PROJECT.md § F9" `
    -Name "T1.F09.05_HypervisorLaunchTypeCommandConstruction" `
    -Description "Verifies HypervisorLaunchType flag generates auto/off values." `
    -TestBlock {
        $hypAuto = [BcdOracle]::BuildBootFlagCommand("hypervisorlaunchtype", $true)
        $hypOff = [BcdOracle]::BuildBootFlagCommand("hypervisorlaunchtype", $false)
        Assert-Equal $hypAuto "bcdedit.exe /set {current} hypervisorlaunchtype auto"
        Assert-Equal $hypOff "bcdedit.exe /set {current} hypervisorlaunchtype off"
    }

# --- Feature F10: BCD Query & Reboot Prompt ---

Register-E2ETest -Tier 1 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T1.F10.01_BcdManagerServiceExists" `
    -Description "Verifies BcdManagerService type exists in assembly." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.BcdManagerService")
        Assert-NotNull $svcType "BcdManagerService must exist"
    }

Register-E2ETest -Tier 1 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T1.F10.02_SafeBootConfigModelContract" `
    -Description "Verifies SafeBootConfig model exists or has defined structure." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $modelType = $asm.GetType("DiskMasterWinUI.Models.SafeBootConfig")
        if (-not $modelType) {
            throw "Pending M4: SafeBootConfig model not yet implemented"
        }
        Assert-NotNull $modelType "SafeBootConfig model found"
    }

Register-E2ETest -Tier 1 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T1.F10.03_BcdManagerServiceQueryMethodContract" `
    -Description "Verifies BcdManagerService exposes a query method for safe boot config." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.BcdManagerService")
        $queryMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*SafeBoot*" -or $_.Name -like "*BootOption*" } | Select-Object -First 1
        if (-not $queryMethod) {
            throw "Pending M4: BcdManagerService safe boot query method not yet implemented"
        }
        Assert-NotNull $queryMethod "Query method must exist"
    }

Register-E2ETest -Tier 1 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T1.F10.04_BcdManagerServiceSetModeMethodContract" `
    -Description "Verifies BcdManagerService exposes a method to set safe boot mode." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.BcdManagerService")
        $setMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*SetSafeBoot*" -or $_.Name -like "*ApplyBootOption*" } | Select-Object -First 1
        if (-not $setMethod) {
            throw "Pending M4: BcdManagerService SetSafeBootMode method not yet implemented"
        }
        Assert-NotNull $setMethod "Set mode method must exist"
    }

Register-E2ETest -Tier 1 -Feature "F10" -Source "R3 / PROJECT.md § F10" `
    -Name "T1.F10.05_BootManagerViewModelExposesBootCommands" `
    -Description "Verifies BootManagerViewModel exposes commands for safe boot operations." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $vmType = $asm.GetType("DiskMasterWinUI.ViewModels.BootManagerViewModel")
        Assert-NotNull $vmType "BootManagerViewModel must exist"
        $props = $vmType.GetProperties() | ForEach-Object { $_.Name }
        $hasSafeBoot = ($props | Where-Object { $_ -like "*SafeBoot*" -or $_ -like "*BootOption*" }).Count -gt 0
        if (-not $hasSafeBoot) {
            throw "Pending M4: BootManagerViewModel safe boot integration not yet implemented"
        }
        Assert-True $hasSafeBoot "Safe boot commands verified"
    }
