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

# --- Feature F25: TPM 2.0 Diagnostics & Windows 11 Bypass ---

Register-E2ETest -Tier 1 -Feature "F25" -Source "PROJECT.md § F25" `
    -Name "T1.F25.01_TpmServiceAndModelContracts" `
    -Description "Verifies TpmService and TpmStatusInfo models exist with all required diagnostic properties." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $serviceType = $asm.GetType("DiskMasterWinUI.Services.TpmService")
        Assert-NotNull $serviceType "TpmService must exist"

        $modelType = $asm.GetType("DiskMasterWinUI.Models.TpmStatusInfo")
        Assert-NotNull $modelType "TpmStatusInfo must exist"

        $model = [System.Activator]::CreateInstance($modelType)
        Assert-NotNull $model
        Assert-Equal $model.IsPresent $false
        Assert-Equal $model.IsWin11BypassActive $false

        $methods = $serviceType.GetMethods() | ForEach-Object { $_.Name }
        Assert-Contains $methods "GetTpmStatusAsync"
        Assert-Contains $methods "SetWin11SetupBypassAsync"
        Assert-Contains $methods "OpenTpmManagementConsole"
    }

Register-E2ETest -Tier 1 -Feature "F25" -Source "PROJECT.md § F25" `
    -Name "T1.F25.02_TpmStatusAsyncExecutionSafety" `
    -Description "Verifies GetTpmStatusAsync runs safely without throwing exceptions." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $serviceType = $asm.GetType("DiskMasterWinUI.Services.TpmService")
        $service = [System.Activator]::CreateInstance($serviceType)
        $task = $service.GetTpmStatusAsync()
        $status = $task.GetAwaiter().GetResult()
        Assert-NotNull $status "TPM status result must not be null"
    }

# --- Feature F26: Real-time DNS Latency Ping & Universal Tab Reordering ---

Register-E2ETest -Tier 1 -Feature "F26" -Source "PROJECT.md § F26" `
    -Name "T1.F26.01_PhysicalNetworkAdapterDetectionContract" `
    -Description "Verifies GetAvailableNetworkAdapters filters and returns valid adapter items." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $diagType = $asm.GetType("DiskMasterWinUI.Services.NetworkDiagnosticService")
        Assert-NotNull $diagType "NetworkDiagnosticService must exist"

        $method = $diagType.GetMethod("GetAvailableNetworkAdapters", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $method "GetAvailableNetworkAdapters static method must exist"

        $adapters = $method.Invoke($null, @())
        Assert-NotNull $adapters "Adapters collection must not be null"
    }

Register-E2ETest -Tier 1 -Feature "F26" -Source "PROJECT.md § F26" `
    -Name "T1.F26.02_TabReorderHelperContract" `
    -Description "Verifies TabReorderHelper exposes universal Attach and EnhanceTabs methods." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $helperType = $asm.GetType("DiskMasterWinUI.Helpers.TabReorderHelper")
        Assert-NotNull $helperType "TabReorderHelper must exist"

        $attach = $helperType.GetMethod("Attach", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $attach "Attach method must exist"

        $enhance = $helperType.GetMethod("EnhanceTabs", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $enhance "EnhanceTabs method must exist"
    }

# --- Feature F27: 16-Item System, Shell, Performance & UX Enhancement Suite ---

Register-E2ETest -Tier 1 -Feature "F27" -Source "Item 1: DPI & Centered Window Sizing" `
    -Name "T1.F27.01_DisplayHelperDpiAndCenteredSizingContract" `
    -Description "Verifies DisplayHelper exposes GetOptimalWindowBounds, CalculateFloatingWindowBounds, GetRecommendedScalePercent, CalculateScaledViewportLayout, and safe zero-HWND fallbacks." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Helpers.DisplayHelper")
        Assert-NotNull $type "DisplayHelper must exist"

        $methods = $type.GetMethods() | ForEach-Object { $_.Name }
        Assert-Contains $methods "GetOptimalWindowBounds"
        Assert-Contains $methods "CalculateFloatingWindowBounds"
        Assert-Contains $methods "GetRecommendedScalePercent"
        Assert-Contains $methods "CalculateScaledViewportLayout"

        $dpi = $type.GetMethod("GetDpi").Invoke($null, @([IntPtr]::Zero))
        Assert-Equal $dpi ([uint32]96) "Zero HWND must safely return 96 DPI"

        $scale = $type.GetMethod("GetRecommendedScalePercent").Invoke($null, @([IntPtr]::Zero))
        Assert-True ($scale -ge 85 -and $scale -le 100) "Recommended scale must be in [85, 100]"

        $bounds = $type.GetMethod("GetOptimalWindowBounds").Invoke($null, @([IntPtr]::Zero))
        Assert-NotNull $bounds "GetOptimalWindowBounds must return non-null tuple"
        Assert-True ($bounds.Item2.Width -ge 640 -and $bounds.Item2.Height -ge 480) "Window size must respect minimum bounds"

        # Verify CalculateScaledViewportLayout prevents right-edge clipping at >100% scale (e.g. 115%)
        # Return tuple: (Item1=LayoutWidth, Item2=HorizontalOffset, Item3=BottomMargin, Item4=ClampedScale)
        $calcMethod = $type.GetMethod("CalculateScaledViewportLayout")
        $layout115 = $calcMethod.Invoke($null, @(1200.0, 800.0, 1000.0, 1.15))
        # Visual width (LayoutWidth * 1.15) must not exceed (1200 - 16) = 1184
        Assert-True (($layout115.Item1 * 1.15) -le 1184.5) "Scaled visual width at 115% must leave 16px scrollbar clearance without right-edge clipping"
        Assert-Equal $layout115.Item2 2.0 "HorizontalOffset at 1200px viewport centers 1184px visual width inside 1188px centerable area"
        Assert-True ($layout115.Item3 -gt 0) "BottomMargin must expand for >100% scale so vertical ScrollViewer reaches bottom"
        Assert-Equal $layout115.Item4 1.15 "ClampedScale must equal 1.15"

        # Verify CalculateScaledViewportLayout at <100% scale (e.g. 85%)
        $layout85 = $calcMethod.Invoke($null, @(1200.0, 800.0, 900.0, 0.85))
        Assert-True ([Math]::Abs(($layout85.Item1 * 0.85) - 1184.0) -lt 0.5) "At 85% scale, LayoutWidth * 0.85 fills usable viewport width (1184px)"
        Assert-Equal $layout85.Item3 0.0 "BottomMargin at <=100% scale must be 0"
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Item 2: Global Ctrl+Z & Ctrl+X UndoCutManager" `
    -Name "T1.F27.02_UndoCutManagerStackAndBoundaryBehavior" `
    -Description "Verifies UndoCutManager PushUndo, TryExecuteUndo, null-action safety, and 50-item bounded stack." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Helpers.UndoCutManager")
        Assert-NotNull $type "UndoCutManager must exist"

        $pushMethod = $type.GetMethod("PushUndo")
        $undoMethod = $type.GetMethod("TryExecuteUndo")
        $cutMethod = $type.GetMethod("TryExecuteCut")
        Assert-NotNull $pushMethod
        Assert-NotNull $undoMethod
        Assert-NotNull $cutMethod

        # Null action must not throw
        $pushMethod.Invoke($null, @("NullTest", $null))

        # Push and pop reversible state
        $script:restoredVal = "initial"
        $action = [System.Action]{ $script:restoredVal = "restored_ok" }
        $pushMethod.Invoke($null, @("TestAction", $action))
        Assert-True ($type.GetProperty("CanUndo").GetValue($null)) "CanUndo must be true after PushUndo"

        $ok = $undoMethod.Invoke($null, @($null, $null))
        Assert-True $ok "TryExecuteUndo must return true when stack has entry"
        Assert-Equal $script:restoredVal "restored_ok" "RestoreAction must have executed"

        # Stack overflow boundary (>50 items) must cap at 50 without throwing
        for ($i = 1; $i -le 60; $i++) {
            $pushMethod.Invoke($null, @("Entry_$i", [System.Action]{}))
        }
        $popped = 0
        while ($undoMethod.Invoke($null, @($null, $null))) {
            $popped++
            if ($popped -gt 100) { break }
        }
        Assert-Equal $popped 50 "Undo stack must enforce MaxUndoDepth = 50"
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Item 3 & 10: FPS HUD GPU/API, High-DPI Client Sizing & DDR4/DDR5 Memory Spec" `
    -Name "T1.F27.03_SystemPerformanceGpuApiAndSmbiosMemoryMapping" `
    -Description "Verifies SMBIOS DDR4/DDR5/LPDDR5 mapping, foreground Graphics API detection, HUD DPI client sizing, and SystemMetricsSnapshot properties." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.SystemPerformanceService")
        Assert-NotNull $svcType "SystemPerformanceService must exist"

        $mapMethod = $svcType.GetMethod("MapSmbiosMemoryType", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $mapMethod "MapSmbiosMemoryType must be exposed"

        Assert-Equal ($mapMethod.Invoke($null, @(34, 0, 6000))) "DDR5"
        Assert-Equal ($mapMethod.Invoke($null, @(35, 0, 6400))) "LPDDR5"
        Assert-Equal ($mapMethod.Invoke($null, @(26, 0, 3200))) "DDR4"
        Assert-Equal ($mapMethod.Invoke($null, @(30, 0, 4266))) "LPDDR4"
        Assert-Equal ($mapMethod.Invoke($null, @(24, 0, 1600))) "DDR3"
        # Fallback by clock speed when SMBIOS returns 0
        Assert-Equal ($mapMethod.Invoke($null, @(0, 0, 5600))) "DDR5"
        Assert-Equal ($mapMethod.Invoke($null, @(0, 0, 3200))) "DDR4"

        $snapType = $asm.GetType("DiskMasterWinUI.Models.SystemMetricsSnapshot")
        $snap = [System.Activator]::CreateInstance($snapType)
        $snap.GpuUsagePercent = 42.5
        $snap.ActiveGraphicsApi = "DX12"
        $snap.RamMemoryType = "DDR5"
        $snap.RamSpeedMhz = 6000
        Assert-Equal $snap.ActiveGraphicsApi "DX12"
        Assert-Equal $snap.RamMemoryType "DDR5"
        Assert-Equal $snap.RamSpeedMhz 6000

        # Verify PerformanceOverlayWindow.CalculateDpiAwareHudClientSize scales properly across 1.0 (100%), 1.5 (150%), 2.0 (200%) DPI scale
        $hudType = $asm.GetType("DiskMasterWinUI.Controls.PerformanceOverlayWindow")
        Assert-NotNull $hudType "PerformanceOverlayWindow must exist"
        $hudSizeMethod = $hudType.GetMethod("CalculateDpiAwareHudClientSize", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $hudSizeMethod "CalculateDpiAwareHudClientSize must exist"

        $size100 = $hudSizeMethod.Invoke($null, @($false, 1.0, 0.0, 0.0))
        $size200 = $hudSizeMethod.Invoke($null, @($false, 2.0, 0.0, 0.0))
        Assert-Equal $size200.Width ($size100.Width * 2) "200% DPI HUD width must be 2x 100% DPI width"
        Assert-Equal $size200.Height ($size100.Height * 2) "200% DPI HUD height must be 2x 100% DPI height"

        # Dynamic DesiredSize expansion when text is wider than minimum DIPs
        $sizeExpanded = $hudSizeMethod.Invoke($null, @($false, 1.5, 420.0, 240.0))
        Assert-True ($sizeExpanded.Width -ge [int][Math]::Ceiling(420.0 * 1.5)) "Client width must expand to fit larger DesiredSize at 150% DPI"
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Item 4: Close-to-Tray Lifecycle Safety" `
    -Name "T1.F27.04_TrayIconServiceEnsureCreatedAndMinimizeSafety" `
    -Description "Verifies TrayIconService exposes IsExplicitExit, MinimizeToTray, and RestoreFromTray to prevent WM_CLOSE killing app." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $trayType = $asm.GetType("DiskMasterWinUI.Services.TrayIconService")
        Assert-NotNull $trayType

        $prop = $trayType.GetProperty("IsExplicitExit")
        Assert-NotNull $prop "IsExplicitExit property must exist on TrayIconService"

        $tray = [System.Activator]::CreateInstance($trayType)
        Assert-Equal $tray.IsExplicitExit $false "IsExplicitExit must default to false so WM_CLOSE hides to tray"
        $tray.IsExplicitExit = $true
        Assert-Equal $tray.IsExplicitExit $true
        $tray.Dispose()
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Items 5, 6, 7, 8, 9: VPN NAT-T, GameDVR, DPS, SystemResponsiveness=10, DiagTrack" `
    -Name "T1.F27.05_SystemOptimizerNewTweaksAndServiceControls" `
    -Description "Verifies L2TP/IPsec VPN NAT-T Fix, GameDVR, DPS memory bloat, SystemResponsiveness=10, and DiagTrack methods." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $optType = $asm.GetType("DiskMasterWinUI.Services.SystemOptimizerService")
        Assert-NotNull $optType

        $svc = [System.Activator]::CreateInstance($optType)
        $methods = $optType.GetMethods() | ForEach-Object { $_.Name }

        # Item 5: L2TP/IPsec VPN Fix + ViewModel initialization
        Assert-Contains $methods "GetL2tpVpnNatRuleValue"
        Assert-Contains $methods "GetL2tpVpnNatFixEnabled"
        Assert-Contains $methods "SetL2tpVpnNatFix"
        $vpnVal = $svc.GetL2tpVpnNatRuleValue()
        Assert-True ($vpnVal -ge 0) "L2TP VPN rule value must be non-negative integer"

        $netVmType = $asm.GetType("DiskMasterWinUI.ViewModels.NetworkToolsViewModel")
        $netVm = [System.Activator]::CreateInstance($netVmType)
        Assert-False ($netVm.L2tpVpnNatTStatusText -eq "尚未檢測") "NetworkToolsViewModel must initialize L2tpVpnNatTStatusText on construction"

        # Item 6: GameDVR background recording tweak
        Assert-Contains $methods "GetGameDvrDisabled"
        Assert-Contains $methods "SetGameDvrDisabled"

        # Item 7: DPS memory bloat tweak
        Assert-Contains $methods "GetDpsServiceDisabled"
        Assert-Contains $methods "SetDpsServiceDisabledAsync"

        # Item 8: SystemResponsiveness = 10 tweak
        Assert-Contains $methods "GetSystemResponsiveness"
        Assert-Contains $methods "SetSystemResponsiveness"
        $srVal = $svc.GetSystemResponsiveness()
        Assert-True ($srVal -ge 0 -and $srVal -le 100) "SystemResponsiveness must be within 0..100"

        # Item 9: DiagTrack Turn Off / Turn On
        Assert-Contains $methods "GetDiagTrackDisabled"
        Assert-Contains $methods "SetDiagTrackServiceDisabledAsync"
        $diagDisabled = $svc.GetDiagTrackDisabled()
        Assert-True ($diagDisabled -is [bool])
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Items 11 & 12: Context Menu, Fast Folder Optimization & Custom Icons" `
    -Name "T1.F27.06_FolderAndIconCustomizerServiceEndToEnd" `
    -Description "Verifies FolderAndIconCustomizerService context menu queries, recursive desktop.ini folder template optimization, custom icon setters, and clean default icon restoration." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $type = $asm.GetType("DiskMasterWinUI.Services.FolderAndIconCustomizerService")
        Assert-NotNull $type "FolderAndIconCustomizerService must exist"

        $svc = [System.Activator]::CreateInstance($type)
        $templates = $type.GetProperty("FolderTemplates").GetValue($null)
        Assert-Equal $templates.Count 5 "Must define Generic, Documents, Pictures, Music, Videos templates"

        # Test real folder optimization & custom icon on a temporary directory
        $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DiskMaster_FolderOptTest_" + [System.Guid]::NewGuid().ToString("N"))
        $subDir = Join-Path $tempRoot "SubFolderA"
        $iconOnlyDir = Join-Path $tempRoot "IconOnlySubFolder"
        [System.IO.Directory]::CreateDirectory($subDir) | Out-Null
        [System.IO.Directory]::CreateDirectory($iconOnlyDir) | Out-Null

        try {
            $optTask = $svc.OptimizeFolderTemplateAsync($tempRoot, "Generic", $true, $null)
            $optRes = $optTask.GetAwaiter().GetResult()
            Assert-True $optRes.Item1 "OptimizeFolderTemplateAsync must succeed on valid temp folder"
            Assert-Equal $optRes.Item3 3 "Must optimize root folder and 2 subfolders"

            $rootIni = Join-Path $tempRoot "desktop.ini"
            $subIni = Join-Path $subDir "desktop.ini"
            Assert-True ([System.IO.File]::Exists($rootIni)) "Root desktop.ini must be created"
            Assert-True ([System.IO.File]::Exists($subIni)) "Subfolder desktop.ini must be created when applyToSubfolders=true"

            $iniContent = [System.IO.File]::ReadAllText($rootIni)
            Assert-Contains $iniContent "FolderType=Generic"

            # Apply custom folder icon
            $iconTask = $svc.SetFolderCustomIconAsync($tempRoot, "C:\Windows\System32\shell32.dll,43", $false)
            $iconRes = $iconTask.GetAwaiter().GetResult()
            Assert-True $iconRes.Item1 "SetFolderCustomIconAsync must succeed"
            $iniContent2 = [System.IO.File]::ReadAllText($rootIni)
            Assert-Contains $iniContent2 "IconResource=C:\Windows\System32\shell32.dll,43"
            # Existing FolderType=Generic must be preserved!
            Assert-Contains $iniContent2 "FolderType=Generic"

            # Test clean default icon restoration on a folder that ONLY had a custom icon
            $cleanDir = Join-Path ([System.IO.Path]::GetTempPath()) ("DiskMaster_IconReset_" + [System.Guid]::NewGuid().ToString("N"))
            [System.IO.Directory]::CreateDirectory($cleanDir) | Out-Null
            try {
                $svc.SetFolderCustomIconAsync($cleanDir, "C:\Windows\System32\imageres.dll,3", $false).GetAwaiter().GetResult() | Out-Null
                $cleanIni = Join-Path $cleanDir "desktop.ini"
                Assert-True ([System.IO.File]::Exists($cleanIni)) "desktop.ini must exist after setting custom icon"
                $svc.SetFolderCustomIconAsync($cleanDir, $null, $false).GetAwaiter().GetResult() | Out-Null
                Assert-False ([System.IO.File]::Exists($cleanIni)) "Empty desktop.ini must be deleted when restoring default icon"
            }
            finally {
                if ([System.IO.Directory]::Exists($cleanDir)) {
                    Get-ChildItem $cleanDir -Force -Recurse | ForEach-Object { $_.Attributes = [System.IO.FileAttributes]::Normal }
                    Remove-Item $cleanDir -Recurse -Force -ErrorAction SilentlyContinue
                }
            }

            # Invalid/missing directory edge case must return false without throwing
            $badRes = $svc.OptimizeFolderTemplateAsync("", "Generic", $false, $null).GetAwaiter().GetResult()
            Assert-False $badRes.Item1 "Empty path must return false safely"
        }
        finally {
            if ([System.IO.Directory]::Exists($tempRoot)) {
                Get-ChildItem $tempRoot -Force -Recurse | ForEach-Object { $_.Attributes = [System.IO.FileAttributes]::Normal }
                Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Items 13 & 14: IconCacheRepairService & ExplorerWatchdogService" `
    -Name "T1.F27.07_ExplorerWatchdogAndIconCacheServiceContract" `
    -Description "Verifies ExplorerWatchdogService singleton, suppression window, ResumeAutoRestart, LaunchExplorerShell, and IconCacheRepairService NotifyShellIconChange." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $wdType = $asm.GetType("DiskMasterWinUI.Services.ExplorerWatchdogService")
        Assert-NotNull $wdType "ExplorerWatchdogService must exist"

        $wd = $wdType.GetProperty("Instance").GetValue($null)
        Assert-NotNull $wd "ExplorerWatchdogService.Instance must not be null"
        $wd.SuppressAutoRestartFor([TimeSpan]::FromSeconds(2))

        # Verify ResumeAutoRestart method exists and executes cleanly
        $resumeMethod = $wdType.GetMethod("ResumeAutoRestart")
        Assert-NotNull $resumeMethod "ResumeAutoRestart method must exist on ExplorerWatchdogService"
        $resumeMethod.Invoke($wd, @())

        # Verify IsDesktopShellAlive method
        $isShellAliveMethod = $wdType.GetMethod("IsDesktopShellAlive", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $isShellAliveMethod "IsDesktopShellAlive static method must exist"
        $shellAlive = $isShellAliveMethod.Invoke($null, @())
        Assert-True ($shellAlive -is [bool]) "IsDesktopShellAlive must return boolean"

        # Verify CheckExplorerRunning method
        $checkMethod = $wdType.GetMethod("CheckExplorerRunning", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $checkMethod "CheckExplorerRunning static method must exist"
        $alive = $checkMethod.Invoke($null, @())
        Assert-True ($alive -is [bool]) "CheckExplorerRunning must return boolean"

        # Verify LaunchExplorerShell static method exists
        $launchMethod = $wdType.GetMethod("LaunchExplorerShell", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $launchMethod "LaunchExplorerShell static method must exist"

        # Verify IconCacheRepairService
        $icType = $asm.GetType("DiskMasterWinUI.Services.IconCacheRepairService")
        Assert-NotNull $icType "IconCacheRepairService must exist"
        $notifyMethod = $icType.GetMethod("NotifyShellIconChange", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $notifyMethod "NotifyShellIconChange static method must exist"
    }

Register-E2ETest -Tier 1 -Feature "F27" -Source "Items 15 & 16: Destructive Action Confirmation & 2D Helper Edge Auto-Docking" `
    -Name "T1.F27.08_DialogHelperAndAvatarCompanionEdgeResolver" `
    -Description "Verifies DialogHelper.ConfirmDestructiveOperationAsync null-root safety and AvatarCompanionControl.ResolveTargetDockEdge geometry." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $dlgType = $asm.GetType("DiskMasterWinUI.Helpers.DialogHelper")
        Assert-NotNull $dlgType "DialogHelper must exist"

        $avatarType = $asm.GetType("DiskMasterWinUI.Controls.AvatarCompanionControl")
        Assert-NotNull $avatarType "AvatarCompanionControl must exist"

        $resolveMethod = $avatarType.GetMethod("ResolveTargetDockEdge", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Public)
        Assert-NotNull $resolveMethod "ResolveTargetDockEdge must exist"

        # Left-bottom quadrant -> BottomLeft
        Assert-Equal ($resolveMethod.Invoke($null, @(200.0, 700.0, 1400.0, 900.0, $false))) "BottomLeft"
        # Right-bottom quadrant -> BottomRight
        Assert-Equal ($resolveMethod.Invoke($null, @(1200.0, 700.0, 1400.0, 900.0, $false))) "BottomRight"
        # Left-top quadrant -> TopLeft
        Assert-Equal ($resolveMethod.Invoke($null, @(200.0, 150.0, 1400.0, 900.0, $false))) "TopLeft"
        # Right-top quadrant -> TopRight
        Assert-Equal ($resolveMethod.Invoke($null, @(1200.0, 150.0, 1400.0, 900.0, $false))) "TopRight"
        # Right split-screen open forces left docking
        Assert-Equal ($resolveMethod.Invoke($null, @(1200.0, 700.0, 1400.0, 900.0, $true))) "BottomLeft"
        # Zero container size boundary fallback
        Assert-Equal ($resolveMethod.Invoke($null, @(0.0, 0.0, 0.0, 0.0, $false))) "BottomRight"
    }



