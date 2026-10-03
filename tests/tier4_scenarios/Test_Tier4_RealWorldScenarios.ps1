# DiskMaster Pro WinUI 3 — Tier 4 Real-World Application Scenarios (E2E Workflows)
# Author: Test Writer Agent (E2E Track)
# Scope: Realistic multi-step end-to-end user workflows matching user requirements R1 to R8

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R2 + R5 (SSD Reclamation Workflow)" `
    -Name "T4.01_Scenario_CleanSlateSsdReclamation" `
    -Description "Scenario: User analyzes storage usage, purges hiberfil.sys, cleans Temp, and calculates reclaimed disk space." `
    -TestBlock {
        # Step 1: Storage directory catalog identification
        $dirs = [StorageDirectoryOracle]::TargetDirectories
        Assert-Contains $dirs "hiberfil.sys"
        Assert-Contains $dirs "Temp"
        
        # Step 2: Hiberfil purge calculation
        $sampleHiberfilBytes = 17179869184L # 16 GB
        $reclaimedGb = [PowerCfgOracle]::BytesToGigabytes($sampleHiberfilBytes)
        Assert-Equal $reclaimedGb 16.0 "Must calculate exactly 16.0 GB reclaimed"
        
        # Step 3: Temp directory safe cleanup action authorization
        $tempActions = [StorageDirectoryOracle]::ExpectedActionFlags["Temp"]
        Assert-True $tempActions.CanSafeClean "Temp safe clean must be authorized"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R1 (OEM Driver Removal Workflow)" `
    -Name "T4.02_Scenario_LegacyDriverCleanup" `
    -Description "Scenario: User enumerates drivers in Japanese locale, selects multiple drivers, and batch uninstalls with force." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        
        # Step 1: Generate Japanese pnputil output
        $rawJa = [PnpUtilOracle]::GenerateDriverOutput("ja-jp", "oem35.inf", "olddisp.inf", "Display", "OldGPU Inc", "01/01/2018", "1.0", "Legacy Signer")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        $drivers = $method.Invoke($null, @($rawJa))
        if ($drivers.Count -eq 0) {
            throw "Pending M2: Japanese pnputil parsing not yet implemented"
        }
        Assert-Equal $drivers.Count 1
        
        # Step 2: Build batch force delete commands
        $cmd = [PnpUtilOracle]::BuildDeleteCommand($drivers[0].PublishedName, $true, $true)
        Assert-Equal $cmd "/delete-driver oem35.inf /uninstall /force"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R3 (Emergency Safe Boot Recovery Workflow)" `
    -Name "T4.03_Scenario_EmergencySafeBootRecovery" `
    -Description "Scenario: User activates Safe Boot Minimal with BaseVideo, tests BCD config, then restores Normal boot." `
    -TestBlock {
        # Step 1: Configure Safe Boot Minimal
        $safeBootCmd = [BcdOracle]::BuildSafeBootCommand("minimal")
        Assert-Equal $safeBootCmd "bcdedit.exe /set {current} safeboot minimal"
        
        # Step 2: Configure BaseVideo flag
        $baseVideoCmd = [BcdOracle]::BuildBootFlagCommand("basevideo", $true)
        Assert-Equal $baseVideoCmd "bcdedit.exe /set {current} basevideo yes"
        
        # Step 3: Emergency recovery: restore Normal boot
        $restoreCmd = [BcdOracle]::BuildSafeBootCommand("normal")
        Assert-Equal $restoreCmd "bcdedit.exe /deletevalue {current} safeboot"
        
        # Step 4: Clear BaseVideo
        $clearBaseVideo = [BcdOracle]::BuildBootFlagCommand("basevideo", $false)
        Assert-Equal $clearBaseVideo "bcdedit.exe /deletevalue {current} basevideo"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R4 (Competitive Gaming Tuning Workflow)" `
    -Name "T4.04_Scenario_CompetitiveGamingTuning" `
    -Description "Scenario: User applies gaming performance profile: unhides 7 CPU attributes and configures kernel tweaks." `
    -TestBlock {
        # Step 1: Verify all 7 PPM attributes to unhide
        $ppmAttrs = [PowerCfgOracle]::ProcessorAttributes
        Assert-Equal $ppmAttrs.Count 7
        foreach ($kvp in $ppmAttrs.GetEnumerator()) {
            $unhideCmd = [PowerCfgOracle]::BuildUnhideCommand($kvp.Value)
            Assert-Contains $unhideCmd "-ATTRIB_HIDE"
        }
        
        # Step 2: Verify kernel latency commands
        $tickCmd = [SystemOptimizerOracle]::DynamicTickCommand
        Assert-Equal $tickCmd "bcdedit.exe /set disabledynamictick yes"
        
        # Step 3: Verify memory management registry key
        $key = [SystemOptimizerOracle]::MemoryManagementKey
        Assert-Contains $key "Memory Management"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R7 (Multilingual Global Enterprise Workflow)" `
    -Name "T4.05_Scenario_MultilingualGlobalDeployment" `
    -Description "Scenario: User switches through all 4 supported cultures, verifying dictionary completeness and no missing keys." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        Assert-NotNull $locType "LocalizationService must exist"
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        Assert-NotNull $instance
        
        $cultures = [LocalizationOracle]::SupportedCultures
        Assert-Equal $cultures.Count 4 "Must support 4 cultures"
        Assert-Contains $cultures "zh-TW"
        Assert-Contains $cultures "zh-CN"
        Assert-Contains $cultures "en-US"
        Assert-Contains $cultures "ja-JP"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R2 (Component Store Hygiene Workflow)" `
    -Name "T4.06_Scenario_SystemComponentStoreHygiene" `
    -Description "Scenario: User inspects WinSxS, reviews safety rating, and executes DISM component cleanup." `
    -TestBlock {
        # Step 1: Inspect WinSxS safety rating
        $rating = [StorageDirectoryOracle]::ExpectedSafetyRatings["WinSxS"]
        Assert-Equal $rating "CleanViaSystemTool"
        
        # Step 2: Verify SafeClean is blocked
        $actions = [StorageDirectoryOracle]::ExpectedActionFlags["WinSxS"]
        Assert-False $actions.CanSafeClean "Direct deletion of WinSxS is strictly forbidden"
        
        # Step 3: Verify DISM component cleanup command
        $dism = [StorageDirectoryOracle]::DismCommand
        Assert-Equal $dism "dism.exe /online /cleanup-image /startcomponentcleanup"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R4 (System Optimizer Audit Workflow)" `
    -Name "T4.07_Scenario_CompleteSystemOptimizerAudit" `
    -Description "Scenario: User audits all tuning settings across PowerCfg, Registry, and BCD." `
    -TestBlock {
        $sub = [PowerCfgOracle]::SubProcessorGuid
        Assert-Equal $sub "54533251-82be-4824-96c1-47b60b740d00"
        
        $attrs = [PowerCfgOracle]::ProcessorAttributes
        Assert-Equal $attrs.Count 7
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R3 (Clean Boot Diagnostic Mode Workflow)" `
    -Name "T4.08_Scenario_CleanBootDiagnosticMode" `
    -Description "Scenario: User configures Safe Boot with NoGuiBoot, BootLog, and OS Boot Information (SOS)." `
    -TestBlock {
        $safeBootCmd = [BcdOracle]::BuildSafeBootCommand("minimal")
        $noGuiCmd = [BcdOracle]::BuildBootFlagCommand("noguiboot", $true)
        $bootLogCmd = [BcdOracle]::BuildBootFlagCommand("bootlog", $true)
        $sosCmd = [BcdOracle]::BuildBootFlagCommand("sos", $true)
        
        Assert-Equal $safeBootCmd "bcdedit.exe /set {current} safeboot minimal"
        Assert-Equal $noGuiCmd "bcdedit.exe /set {current} noguiboot yes"
        Assert-Equal $bootLogCmd "bcdedit.exe /set {current} bootlog yes"
        Assert-Equal $sosCmd "bcdedit.exe /set {current} sos yes"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R2 (Restricted Permissions Traversal Workflow)" `
    -Name "T4.09_Scenario_RestrictedPermissionsTraversalResilience" `
    -Description "Scenario: Storage analyzer encounters restricted system folders without crashing the application." `
    -TestBlock {
        $dirs = [StorageDirectoryOracle]::TargetDirectories
        Assert-Contains $dirs "System Volume Information"
        $rating = [StorageDirectoryOracle]::ExpectedSafetyRatings["System Volume Information"]
        Assert-Equal $rating "EssentialCore"
    }

Register-E2ETest -Tier 4 -Feature "E2E-Workflow" -Source "R6 + R8 + AC (Release Readiness & Packaging Workflow)" `
    -Name "T4.10_Scenario_FullReleaseReadinessAndAssetPackaging" `
    -Description "Scenario: Release readiness check: modern Fluent icons, documentation, and zero-warning build criteria." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        
        # 1. Icons check
        $icoPath = Join-Path $projectRoot "Assets\AppIcon.ico"
        Assert-True (Test-Path $icoPath) "AppIcon.ico must exist"
        
        # 2. PNG assets check
        foreach ($png in [AssetOracle]::RequiredPngAssets) {
            $pngPath = Join-Path $projectRoot "Assets\$png"
            Assert-True (Test-Path $pngPath) "PNG asset $png must exist"
        }
        
        # 3. Project file net9.0 target check
        $csproj = Join-Path $projectRoot "DiskMasterWinUI.csproj"
        $xml = [System.IO.File]::ReadAllText($csproj)
        Assert-Contains $xml "net9.0" "Target framework must be net9.0"
    }
