<#
.SYNOPSIS
    DiskMaster Pro WinUI 3 — Automated 4-Tier E2E Test Suite Runner
.DESCRIPTION
    Executes the comprehensive requirements-driven opaque-box E2E test suite covering
    Features F1 to F16 across Tiers 1 to 4.
.PARAMETER Tier
    Optional tier filter (1, 2, 3, 4). Default 0 runs all tiers.
.PARAMETER Feature
    Optional feature filter (e.g. "F1", "F14", "F15").
.PARAMETER StrictMode
    If specified, tests awaiting future milestone implementation will FAIL instead of report PENDING.
.PARAMETER Configuration
    Build configuration to load ("Release" or "Debug"). Default is "Release".
.PARAMETER OutputPath
    Optional file path to output structured JSON test execution results.
.EXAMPLE
    pwsh tests/Run-E2ETests.ps1
.EXAMPLE
    pwsh tests/Run-E2ETests.ps1 -Tier 1
.EXAMPLE
    pwsh tests/Run-E2ETests.ps1 -Feature F14
.EXAMPLE
    pwsh tests/Run-E2ETests.ps1 -StrictMode
#>
[CmdletBinding()]
param(
    [int]$Tier = 0,
    [string]$Feature = "",
    [switch]$StrictMode = $false,
    [string]$Configuration = "Release",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

$global:E2EAllTests = [System.Collections.Generic.List[PSCustomObject]]::new()
$global:E2ETestResults = [System.Collections.Generic.List[PSCustomObject]]::new()

Write-Host "`nInitializing DiskMaster Pro E2E Test Environment..." -ForegroundColor Cyan

# 1. Load Harness Engine & Oracles
$harnessDir = Join-Path $scriptRoot "harness"
. (Join-Path $harnessDir "TestFramework.ps1")
. (Join-Path $harnessDir "TestOracles.ps1")

# 2. Initialize Assembly Reference
try {
    $asm = Initialize-TestAssembly -Configuration $Configuration
    Write-Host "Target Assembly Loaded: $($asm.FullName)" -ForegroundColor Green
}
catch {
    Write-Warning "Assembly loading notification: $_"
}

# 3. Discover and Source All Test Suite Files
$testFiles = @(
    # Tier 1
    (Join-Path $scriptRoot "tier1_features\Test_Tier1_F01_To_F04_OemDrivers.ps1"),
    (Join-Path $scriptRoot "tier1_features\Test_Tier1_F05_To_F07_StorageEncyclopedia.ps1"),
    (Join-Path $scriptRoot "tier1_features\Test_Tier1_F08_To_F10_SafeBootAndBcd.ps1"),
    (Join-Path $scriptRoot "tier1_features\Test_Tier1_F11_To_F13_PowerAndLatency.ps1"),
    (Join-Path $scriptRoot "tier1_features\Test_Tier1_F14_To_F16_AssetsLocGuide.ps1"),
    # Tier 2
    (Join-Path $scriptRoot "tier2_boundaries\Test_Tier2_F01_To_F04_DriverBoundaries.ps1"),
    (Join-Path $scriptRoot "tier2_boundaries\Test_Tier2_F05_To_F07_StorageBoundaries.ps1"),
    (Join-Path $scriptRoot "tier2_boundaries\Test_Tier2_F08_To_F10_BootBoundaries.ps1"),
    (Join-Path $scriptRoot "tier2_boundaries\Test_Tier2_F11_To_F13_PowerBoundaries.ps1"),
    (Join-Path $scriptRoot "tier2_boundaries\Test_Tier2_F14_To_F16_AssetLocBoundaries.ps1"),
    # Tier 3
    (Join-Path $scriptRoot "tier3_combinations\Test_Tier3_CrossFeatureInteractions.ps1"),
    # Tier 4
    (Join-Path $scriptRoot "tier4_scenarios\Test_Tier4_RealWorldScenarios.ps1")
)

foreach ($file in $testFiles) {
    if (Test-Path $file) {
        . $file
    } else {
        Write-Warning "Test file not found: $file"
    }
}

Write-Host "Total Registered Tests: $($script:AllTests.Count)" -ForegroundColor Cyan

# 4. Execute Test Suite via Harness Engine
$exitCode = Invoke-E2ETestSuite `
    -FilterTier $Tier `
    -FilterFeature $Feature `
    -StrictMode:$StrictMode `
    -OutputPath $OutputPath

exit $exitCode
