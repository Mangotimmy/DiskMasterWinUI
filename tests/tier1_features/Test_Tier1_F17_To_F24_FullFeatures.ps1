# DiskMaster Pro WinUI 3 — Tier 1 Feature Coverage: F17 to F24
# Author: Test Writer Agent (E2E Track)
# Scope: OneDrive Deep Governance, UPnP, Network Diagnostics, Hosts Editor, Starter Hub, Dual Mode, Advanced Tweaks, Tray & Feedback

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F17: OneDrive Deep Detection, Issue Repair & Uninstallation ---

Register-E2ETest -Tier 1 -Feature "F17" -Source "PROJECT.md § F17" `
    -Name "T1.F17.01_OneDriveServiceTypeAndMethodsContract" `
    -Description "Verifies OneDriveService exists and exposes all 5 core issue repair methods." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.OneDriveService")
        Assert-NotNull $type "OneDriveService must exist in assembly"
        
        $methods = $type.GetMethods() | ForEach-Object { $_.Name }
        Assert-Contains $methods "DetectOneDriveStatusAsync"
        Assert-Contains $methods "DeepUninstallOneDriveAsync"
        Assert-Contains $methods "RestoreUserShellFoldersAsync"
        Assert-Contains $methods "RemoveExplorerGhostIconAsync"
        Assert-Contains $methods "ResetOneDriveSyncEngineAsync"
        Assert-Contains $methods "ToggleOneDrivePolicyBlockAsync"
    }

Register-E2ETest -Tier 1 -Feature "F17" -Source "PROJECT.md § F17" `
    -Name "T1.F17.02_OneDriveStatusInfoModelContract" `
    -Description "Verifies OneDriveStatusInfo model contains all diagnostic state properties." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Models.OneDriveStatusInfo")
        Assert-NotNull $type "OneDriveStatusInfo must exist in assembly"

        $instance = [System.Activator]::CreateInstance($type)
        Assert-NotNull $instance
        Assert-Equal $instance.IsInstalled $false
        Assert-Equal $instance.IsRunning $false
        Assert-Equal $instance.IsFoldersRedirected $false
        Assert-Equal $instance.HasCloudOnlyFiles $false
        Assert-Equal $instance.IsFileExplorerPinned $false
        Assert-Equal $instance.IsPolicyBlocked $false
    }

Register-E2ETest -Tier 1 -Feature "F17" -Source "PROJECT.md § F17" `
    -Name "T1.F17.03_OneDriveDetectionExecutionSafety" `
    -Description "Verifies DetectOneDriveStatusAsync executes without throwing and returns valid status info." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.OneDriveService")
        $service = [System.Activator]::CreateInstance($type)
        $task = $service.DetectOneDriveStatusAsync()
        $status = $task.GetAwaiter().GetResult()
        Assert-NotNull $status "Status result must not be null"
    }

# --- Feature F18: UPnP Automatic Port Forwarding & Game Presets ---

Register-E2ETest -Tier 1 -Feature "F18" -Source "PROJECT.md § F18" `
    -Name "T1.F18.01_UpnpServiceGamePresetsCompleteness" `
    -Description "Verifies UpnpService provides built-in gaming and server presets." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.UpnpService")
        Assert-NotNull $type "UpnpService must exist in assembly"

        $getPresets = $type.GetMethod("GetGamePresets", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $getPresets "GetGamePresets static method must exist"
        
        $presets = $getPresets.Invoke($null, @())
        Assert-True ($presets.Count -ge 6) "Must provide at least 6 game presets (found $($presets.Count))"

        $descriptions = $presets | ForEach-Object { $_.Description }
        Assert-True ($descriptions -match "Minecraft") "Must include Minecraft preset"
        Assert-True ($descriptions -match "Steam") "Must include Steam preset"
        Assert-True ($descriptions -match "Palworld") "Must include Palworld preset"
    }

Register-E2ETest -Tier 1 -Feature "F18" -Source "PROJECT.md § F18" `
    -Name "T1.F18.02_UpnpPortMappingItemModelContract" `
    -Description "Verifies UpnpPortMappingItem model properties and formatting." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Models.UpnpPortMappingItem")
        Assert-NotNull $type "UpnpPortMappingItem must exist"

        $item = [System.Activator]::CreateInstance($type)
        $item.ExternalPort = 25565
        $item.InternalPort = 25565
        $item.Protocol = "TCP"
        $item.InternalClient = "192.168.1.100"
        $item.Description = "Minecraft Server"

        Assert-Equal $item.ExternalPort 25565
        Assert-Equal $item.DisplayTitle "Minecraft Server (TCP: 25565)"
    }

# --- Feature F19: 5-Stage Smart Bottleneck Diagnostics & Recommended DNS ---

Register-E2ETest -Tier 1 -Feature "F19" -Source "PROJECT.md § F19" `
    -Name "T1.F19.01_DnsPresetsCompleteness" `
    -Description "Verifies NetworkDiagnosticService returns recommended DNS profiles." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.NetworkDiagnosticService")
        Assert-NotNull $type "NetworkDiagnosticService must exist"

        $getPresets = $type.GetMethod("GetRecommendedDnsPresets", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $getPresets "GetRecommendedDnsPresets must exist"

        $presets = $getPresets.Invoke($null, @())
        Assert-True ($presets.Count -ge 7) "Must include at least 7 DNS profiles"

        $names = $presets | ForEach-Object { $_.Name }
        Assert-True ($names -match "Cloudflare") "Must include Cloudflare DNS"
        Assert-True ($names -match "Google") "Must include Google DNS"
        Assert-True ($names -match "Quad9") "Must include Quad9 DNS"
    }

Register-E2ETest -Tier 1 -Feature "F19" -Source "PROJECT.md § F19" `
    -Name "T1.F19.02_DiagnosticStepResultModelContract" `
    -Description "Verifies DiagnosticStepResult latency display calculation." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.DiagnosticStepResult")
        Assert-NotNull $type "DiagnosticStepResult must exist"

        $res = [System.Activator]::CreateInstance($type)
        $res.Name = "Gateway Ping"
        $res.LatencyMs = 12
        Assert-Equal $res.LatencyDisplay "12 ms"

        $res.LatencyMs = 0
        Assert-Equal $res.LatencyDisplay "--"
    }

# --- Feature F20: Windows Hosts File Visual Editor ---

Register-E2ETest -Tier 1 -Feature "F20" -Source "PROJECT.md § F20" `
    -Name "T1.F20.01_HostsServiceParsingAndRoundtrip" `
    -Description "Verifies HostsService accurately parses lines, comments, disabled entries, and generates text." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.HostsService")
        Assert-NotNull $type "HostsService must exist"

        $svc = [System.Activator]::CreateInstance($type)
        $sampleText = @"
# Sample hosts file
127.0.0.1 localhost
# 0.0.0.0 telemetry.microsoft.com # Blocked
::1 localhost
"@
        $entries = $svc.ParseHosts($sampleText)
        Assert-True ($entries.Count -ge 3) "Must parse at least 3 valid entries"

        $disabled = $entries | Where-Object { $_.IsEnabled -eq $false }
        Assert-NotNull $disabled "Must identify disabled entries"
        Assert-Equal $disabled.HostName "telemetry.microsoft.com"

        $generated = $svc.GenerateHostsText($entries)
        Assert-Contains $generated "127.0.0.1"
        Assert-Contains $generated "localhost"
    }

# --- Feature F21: Beginner Starter Hub (StarterViewModel) ---

Register-E2ETest -Tier 1 -Feature "F21" -Source "PROJECT.md § F21" `
    -Name "T1.F21.01_StarterViewModelInitializationAndScoring" `
    -Description "Verifies StarterViewModel initializes health score and rating text." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.ViewModels.StarterViewModel")
        Assert-NotNull $type "StarterViewModel must exist"

        $vm = [System.Activator]::CreateInstance($type)
        Assert-NotNull $vm
        Assert-True ($vm.HealthScore -ge 0 -and $vm.HealthScore -le 100) "HealthScore must be between 0 and 100"
        Assert-False ([string]::IsNullOrWhiteSpace($vm.HealthRatingText)) "Health rating text must be set"
    }

# --- Feature F22: Dual Mode UI Switcher (Easy vs Advance) ---

Register-E2ETest -Tier 1 -Feature "F22" -Source "PROJECT.md § F22" `
    -Name "T1.F22.01_DualModeSettingPersistence" `
    -Description "Verifies AppSettings supports IsEasyMode boolean flag." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.AppSettings")
        Assert-NotNull $type "AppSettings must exist"

        $settings = [System.Activator]::CreateInstance($type)
        Assert-Equal $settings.IsEasyMode $false "Default mode should be Advance (false)"

        $settings.IsEasyMode = $true
        Assert-Equal $settings.IsEasyMode $true
    }

# --- Feature F23: Advanced Optimizer Hidden Registry Tweaks & CPU PowerCfg ---

Register-E2ETest -Tier 1 -Feature "F23" -Source "PROJECT.md § F23" `
    -Name "T1.F23.01_KernelAndNetworkOptimizerMethodsContract" `
    -Description "Verifies SystemOptimizerService exposes Nagle, MemoryManagement, and Group Policy tweaks." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.SystemOptimizerService")
        Assert-NotNull $type "SystemOptimizerService must exist"

        $methods = $type.GetMethods() | ForEach-Object { $_.Name }
        Assert-Contains $methods "ConfigureMemoryManagement"
        Assert-Contains $methods "ConfigureNagleAlgorithm"
        Assert-Contains $methods "ConfigureGroupPolicyPrivacy"
    }

Register-E2ETest -Tier 1 -Feature "F23" -Source "PROJECT.md § F23" `
    -Name "T1.F23.02_PowerCfgPpmSettingMethodsContract" `
    -Description "Verifies PowerCfgService exposes EPP, Core Parking, and Boost Mode active plan setters." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.PowerCfgService")
        Assert-NotNull $type "PowerCfgService must exist"

        $methods = $type.GetMethods() | ForEach-Object { $_.Name }
        Assert-Contains $methods "SetEnergyPerformancePreferenceAsync"
        Assert-Contains $methods "SetCoreParkingAsync"
        Assert-Contains $methods "SetProcessorBoostModeValueAsync"
    }

# --- Feature F24: Tray Minimization, FlashWindow, Audio Feedback & Auto Updater ---

Register-E2ETest -Tier 1 -Feature "F24" -Source "PROJECT.md § F24" `
    -Name "T1.F24.01_TaskbarFlashAndAudioFeedbackContracts" `
    -Description "Verifies TaskbarFlashService and AudioFeedbackService expose flash and sound methods." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $flashType = $asm.GetType("DiskMasterWinUI.Services.TaskbarFlashService")
        Assert-NotNull $flashType "TaskbarFlashService must exist"
        Assert-NotNull ($flashType.GetMethod("FlashWindow")) "FlashWindow must exist"
        Assert-NotNull ($flashType.GetMethod("StopFlash")) "StopFlash must exist"

        $audioType = $asm.GetType("DiskMasterWinUI.Services.AudioFeedbackService")
        Assert-NotNull $audioType "AudioFeedbackService must exist"
        Assert-NotNull ($audioType.GetMethod("PlaySuccess")) "PlaySuccess must exist"
        Assert-NotNull ($audioType.GetMethod("PlayWarning")) "PlayWarning must exist"
        Assert-NotNull ($audioType.GetMethod("PlayError")) "PlayError must exist"
    }

Register-E2ETest -Tier 1 -Feature "F24" -Source "PROJECT.md § F24" `
    -Name "T1.F24.02_UpdateServiceSingletonAndCheckContract" `
    -Description "Verifies UpdateService exposes singleton Instance and CheckForUpdatesAsync." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $updateType = $asm.GetType("DiskMasterWinUI.Services.UpdateService")
        Assert-NotNull $updateType "UpdateService must exist"

        $prop = $updateType.GetProperty("Instance", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $prop "UpdateService.Instance must exist"

        $method = $updateType.GetMethod("CheckForUpdatesAsync")
        Assert-NotNull $method "CheckForUpdatesAsync must exist"
    }

Register-E2ETest -Tier 1 -Feature "F24" -Source "PROJECT.md § F24" `
    -Name "T1.F24.03_TrayIconServiceContract" `
    -Description "Verifies TrayIconService defines Initialize, MinimizeToTray, and RestoreFromTray." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $trayType = $asm.GetType("DiskMasterWinUI.Services.TrayIconService")
        Assert-NotNull $trayType "TrayIconService must exist"

        $methods = $trayType.GetMethods() | ForEach-Object { $_.Name }
        Assert-Contains $methods "Initialize"
        Assert-Contains $methods "MinimizeToTray"
        Assert-Contains $methods "RestoreFromTray"
        Assert-Contains $methods "ShowNotification"
    }
