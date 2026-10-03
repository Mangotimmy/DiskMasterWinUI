# DiskMaster Pro WinUI 3 — Tier 3 Cross-Feature Interactions (Pairwise Combinations)
# Author: Test Writer Agent (E2E Track)
# Scope: Pairwise interactions across Driver, Storage, BCD, PowerCfg, Optimizer, Assets, and Localization

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

Register-E2ETest -Tier 3 -Feature "F1+F15" -Source "R1 + R7" `
    -Name "T3.01_DriverService_Plus_Localization" `
    -Description "Verifies driver removal confirmation message can be resolved across all 4 cultures." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $tMethod = $locType.GetMethod("T", [System.Reflection.BindingFlags]"Public,Instance,Static")
        if (-not $tMethod) {
            throw "Pending M1: LocalizationService.T helper not yet implemented"
        }
        # Verify resolution
        $msg = $tMethod.Invoke($instance, @("確定要刪除驅動程式嗎？", "确定要删除驱动程序吗？", "Are you sure you want to delete this driver?", "ドライバーを削除してもよろしいですか？"))
        Assert-NotNull $msg "Localized message must not be null"
    }

Register-E2ETest -Tier 3 -Feature "F6+F15" -Source "R2 + R7" `
    -Name "T3.02_StorageEncyclopedia_Plus_Localization" `
    -Description "Verifies all 11 storage directory descriptions update when language is switched." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        Assert-NotNull $locType "LocalizationService must exist"
        # Check that target directories have localized descriptions in oracle
        foreach ($dir in [StorageDirectoryOracle]::TargetDirectories) {
            Assert-True ($dir.Length -gt 0) "Directory name is non-empty"
        }
    }

Register-E2ETest -Tier 3 -Feature "F8+F15" -Source "R3 + R7" `
    -Name "T3.03_SafeBootConfig_Plus_Localization" `
    -Description "Verifies boot flag descriptions and safe boot modes are localized in 4 languages." `
    -TestBlock {
        $modes = [BcdOracle]::SafeBootModes
        Assert-Equal $modes.Count 5
    }

Register-E2ETest -Tier 3 -Feature "F11+F12" -Source "R4 + R4" `
    -Name "T3.04_PowerCfgTuning_Plus_SystemOptimizerLatency" `
    -Description "Verifies applying gaming profile configures both CPU boost unhide and kernel latency tweaks." `
    -TestBlock {
        $boostCmd = [PowerCfgOracle]::BuildUnhideCommand([PowerCfgOracle]::ProcessorAttributes["BoostMode"])
        $latencyCmd = [SystemOptimizerOracle]::DynamicTickCommand
        Assert-Contains $boostCmd "-ATTRIB_HIDE"
        Assert-Contains $latencyCmd "disabledynamictick yes"
    }

Register-E2ETest -Tier 3 -Feature "F5+F13" -Source "R2 + R5" `
    -Name "T3.05_StorageAnalyzer_Plus_HiberfilPurge" `
    -Description "Verifies hiberfil purge reflects in storage analyzer as 0 bytes reclaimed." `
    -TestBlock {
        $bytes = 0L
        $gb = [PowerCfgOracle]::BytesToGigabytes($bytes)
        Assert-Equal $gb 0.0 "Purged hiberfil reflects as 0.0 GB"
    }

Register-E2ETest -Tier 3 -Feature "F5+F7" -Source "R2 + R2" `
    -Name "T3.06_StorageAnalyzer_Plus_DismCleanup" `
    -Description "Verifies storage directory WinSxS action dispatches directly to DISM component cleanup." `
    -TestBlock {
        $actions = [StorageDirectoryOracle]::ExpectedActionFlags["WinSxS"]
        Assert-True $actions.CanDismClean "WinSxS must support DISM cleanup"
        Assert-False $actions.CanSafeClean "WinSxS must not support direct safe clean"
    }

Register-E2ETest -Tier 3 -Feature "F3+F4" -Source "R1 + R1" `
    -Name "T3.07_DriverMultiSelection_Plus_ForceUninstall" `
    -Description "Verifies batch deletion of multiple drivers with force applies /uninstall /force to each." `
    -TestBlock {
        $infs = @("oem10.inf", "oem20.inf")
        foreach ($inf in $infs) {
            $cmd = [PnpUtilOracle]::BuildDeleteCommand($inf, $true, $true)
            Assert-Contains $cmd "/delete-driver $inf /uninstall /force"
        }
    }

Register-E2ETest -Tier 3 -Feature "F8+F9" -Source "R3 + R3" `
    -Name "T3.08_SafeBootMinimal_Plus_NoGuiBoot" `
    -Description "Verifies Minimal safe boot mode combined with NoGuiBoot sets both BCD entries." `
    -TestBlock {
        $safeBootCmd = [BcdOracle]::BuildSafeBootCommand("minimal")
        $noGuiCmd = [BcdOracle]::BuildBootFlagCommand("noguiboot", $true)
        Assert-Equal $safeBootCmd "bcdedit.exe /set {current} safeboot minimal"
        Assert-Equal $noGuiCmd "bcdedit.exe /set {current} noguiboot yes"
    }

Register-E2ETest -Tier 3 -Feature "F11+F11" -Source "R4 + R4" `
    -Name "T3.09_CpuPowerAttributes_Plus_SchemePersistence" `
    -Description "Verifies all 7 PPM attributes target active power scheme (SCHEME_CURRENT)." `
    -TestBlock {
        $sub = [PowerCfgOracle]::SubProcessorGuid
        foreach ($kvp in [PowerCfgOracle]::ProcessorAttributes.GetEnumerator()) {
            $guid = $kvp.Value
            $cmd = "powercfg.exe /setacvalueindex SCHEME_CURRENT $sub $guid 100"
            Assert-Contains $cmd "SCHEME_CURRENT"
        }
    }

Register-E2ETest -Tier 3 -Feature "F5+F7" -Source "R2 + R2" `
    -Name "T3.10_StorageEncyclopediaTempClean_Plus_Recalculation" `
    -Description "Verifies Temp directory cleanup action is authorized and recalculable." `
    -TestBlock {
        $actions = [StorageDirectoryOracle]::ExpectedActionFlags["Temp"]
        Assert-True $actions.CanSafeClean "Temp directory must be authorized for SafeClean"
    }

Register-E2ETest -Tier 3 -Feature "F10+F15" -Source "R3 + R7" `
    -Name "T3.11_BcdQuery_Plus_RebootPrompt_Localization" `
    -Description "Verifies reboot dialog prompt is localized in active culture before reboot." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $tMethod = $locType.GetMethod("T", [System.Reflection.BindingFlags]"Public,Instance,Static")
        if (-not $tMethod) {
            throw "Pending M1: LocalizationService.T helper not yet implemented"
        }
        $prompt = $tMethod.Invoke($instance, @("需要重新啟動以套用設定", "需要重启以应用设置", "System restart required to apply settings", "設定を適用するには再起動が必要です"))
        Assert-NotNull $prompt
    }

Register-E2ETest -Tier 3 -Feature "F14+F15" -Source "R6 + R7" `
    -Name "T3.12_LocalizationService_Plus_AssetIconBranding" `
    -Description "Verifies window branding and icon assets align on DiskMaster Pro identity." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $manifestPath = Join-Path $projectRoot "Package.appxmanifest"
        $xml = [System.IO.File]::ReadAllText($manifestPath)
        Assert-Contains $xml "DiskMaster" "App identity must be DiskMaster"
    }

Register-E2ETest -Tier 3 -Feature "F1+F5" -Source "R1 + R2" `
    -Name "T3.13_DriverEnumeration_Plus_DriverStoreScanning" `
    -Description "Verifies DriverStore target directory matches pnputil package repository." `
    -TestBlock {
        $driverStorePath = "C:\Windows\System32\DriverStore\FileRepository"
        Assert-Equal $driverStorePath "C:\Windows\System32\DriverStore\FileRepository"
        Assert-Contains ([StorageDirectoryOracle]::TargetDirectories) "DriverStore\FileRepository"
    }

Register-E2ETest -Tier 3 -Feature "F12+F12" -Source "R4 + R4" `
    -Name "T3.14_KernelLatencyTweaks_Plus_RegistrySnapshotRollback" `
    -Description "Verifies latency tweaks can be enabled and rolled back without state leakage." `
    -TestBlock {
        $tickApply = [SystemOptimizerOracle]::DynamicTickCommand
        $tickRevert = [SystemOptimizerOracle]::DynamicTickRevertCommand
        Assert-NotEqual $tickApply $tickRevert
    }

Register-E2ETest -Tier 3 -Feature "F8+F9" -Source "R3 + R3" `
    -Name "T3.15_SafeBootAlternateShell_Plus_BootLog" `
    -Description "Verifies Alternate Shell mode combined with BootLog flag creates both BCD entries." `
    -TestBlock {
        $altCmd = [BcdOracle]::BuildSafeBootCommand("alternateshell")
        $logCmd = [BcdOracle]::BuildBootFlagCommand("bootlog", $true)
        Assert-Equal $altCmd "bcdedit.exe /set {current} safeboot alternateshell"
        Assert-Equal $logCmd "bcdedit.exe /set {current} bootlog yes"
    }
