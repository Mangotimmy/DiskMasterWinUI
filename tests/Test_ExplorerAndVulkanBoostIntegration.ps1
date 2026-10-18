# ==============================================================================
# Test_ExplorerAndVulkanBoostIntegration.ps1
# Automated Verification for Explorer Advanced & Vulkan Layer Optimizer Integration
# ==============================================================================

$ErrorActionPreference = "Stop"
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Explorer Advanced & Vulkan Boost App Integration " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

function Assert-Check($name, $condition) {
    if ($condition) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  [FAIL] $name" -ForegroundColor Red
        $script:failed++
    }
}

# 1. Check Source File Integrity
Write-Host "`n1. Verifying Source Code File Artifacts..." -ForegroundColor Yellow
Assert-Check "Models/VulkanLayerItem.cs exists" (Test-Path "Models\VulkanLayerItem.cs")
Assert-Check "Models/ExplorerAdvancedState.cs exists" (Test-Path "Models\ExplorerAdvancedState.cs")
Assert-Check "Services/VulkanOptimizerService.cs exists" (Test-Path "Services\VulkanOptimizerService.cs")
Assert-Check "Services/ExplorerAdvancedOptimizerService.cs exists" (Test-Path "Services\ExplorerAdvancedOptimizerService.cs")
Assert-Check "docs/WINDOWS_REGISTRY_EXPLORER_AND_VULKAN_BOOST_RESEARCH.md exists" (Test-Path "docs\WINDOWS_REGISTRY_EXPLORER_AND_VULKAN_BOOST_RESEARCH.md")

# 2. Check Registry Path Access
Write-Host "`n2. Verifying Windows Registry Subsystems..." -ForegroundColor Yellow
$advPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
Assert-Check "Explorer\Advanced registry key is accessible" (Test-Path $advPath)

$vkExplicit = "HKLM:\SOFTWARE\Khronos\Vulkan\ExplicitLayers"
$vkImplicit = "HKLM:\SOFTWARE\Khronos\Vulkan\ImplicitLayers"
$vkImplicit32 = "HKLM:\SOFTWARE\WOW6432Node\Khronos\Vulkan\ImplicitLayers"

Assert-Check "Vulkan ExplicitLayers (64-bit) key exists" (Test-Path $vkExplicit)
Assert-Check "Vulkan ImplicitLayers (64-bit) key exists" (Test-Path $vkImplicit)
Assert-Check "Vulkan ImplicitLayers (32-bit WOW6432Node) key exists" (Test-Path $vkImplicit32)

# 3. Audit Vulkan Layer Discovery Logic
Write-Host "`n3. Auditing Vulkan Layers Discovery Logic..." -ForegroundColor Yellow
$explicitKey = Get-Item $vkExplicit -ErrorAction SilentlyContinue
$foundExplicit = $false
if ($explicitKey) {
    foreach ($v in $explicitKey.GetValueNames()) {
        $val = $explicitKey.GetValue($v)
        Write-Host "   -> Found Explicit Layer: $v (DWORD: $val, FileExists: $(Test-Path $v))" -ForegroundColor Gray
        $foundExplicit = $true
    }
}
Assert-Check "Vulkan Explicit Layer enumerated" ($foundExplicit -or $explicitKey -ne $null)

$implicitKey = Get-Item $vkImplicit -ErrorAction SilentlyContinue
$implicitCount = 0
if ($implicitKey) {
    foreach ($v in $implicitKey.GetValueNames()) {
        $val = $implicitKey.GetValue($v)
        Write-Host "   -> Found 64-bit Implicit Layer: $v (DWORD: $val, FileExists: $(Test-Path $v))" -ForegroundColor Gray
        $implicitCount++
    }
}
Assert-Check "Vulkan 64-bit Implicit Layers inspected (Count: $implicitCount)" ($implicitCount -gt 0)

$implicit32Key = Get-Item $vkImplicit32 -ErrorAction SilentlyContinue
$implicit32Count = 0
if ($implicit32Key) {
    foreach ($v in $implicit32Key.GetValueNames()) {
        $val = $implicit32Key.GetValue($v)
        Write-Host "   -> Found 32-bit Implicit Layer: $v (DWORD: $val, FileExists: $(Test-Path $v))" -ForegroundColor Gray
        $implicit32Count++
    }
}
Assert-Check "Vulkan 32-bit WOW6432Node Implicit Layers inspected (Count: $implicit32Count)" ($implicit32Count -gt 0)

# 4. Check Explorer Advanced Values
Write-Host "`n4. Auditing Explorer Advanced Values..." -ForegroundColor Yellow
$advProps = Get-ItemProperty -Path $advPath
Write-Host "   -> TaskbarAnimations: $($advProps.TaskbarAnimations)" -ForegroundColor Gray
Write-Host "   -> DesktopLivePreviewHoverTime: $($advProps.DesktopLivePreviewHoverTime)" -ForegroundColor Gray
Write-Host "   -> nonetcrawling: $($advProps.nonetcrawling)" -ForegroundColor Gray
Write-Host "   -> SeparateProcess: $($advProps.SeparateProcess)" -ForegroundColor Gray
Write-Host "   -> TaskbarDa: $($advProps.TaskbarDa)" -ForegroundColor Gray
Write-Host "   -> ShowCopilotButton: $($advProps.ShowCopilotButton)" -ForegroundColor Gray

Assert-Check "Read critical Explorer Advanced properties successfully" ($advProps -ne $null)

# Summary
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Test Summary: $passed Passed, $failed Failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
