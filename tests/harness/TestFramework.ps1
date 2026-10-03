# DiskMaster Pro WinUI 3 — E2E Test Framework Engine
# Author: Test Writer Agent (E2E Track)

if ($null -eq $global:E2EAllTests) {
    $global:E2EAllTests = [System.Collections.Generic.List[PSCustomObject]]::new()
}
if ($null -eq $global:E2ETestResults) {
    $global:E2ETestResults = [System.Collections.Generic.List[PSCustomObject]]::new()
}
$script:AllTests = $global:E2EAllTests
$script:TestResults = $global:E2ETestResults
if ($null -eq $script:LoadedAssembly) {
    $script:AssemblyLoaded = $false
    $script:LoadedAssembly = $null
}

function Initialize-TestAssembly {
    param([string]$Configuration = "Release")

    if ($script:AssemblyLoaded -and $script:LoadedAssembly -ne $null) {
        return $script:LoadedAssembly
    }

    $alreadyLoaded = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "DiskMasterWinUI" } | Select-Object -First 1
    if ($alreadyLoaded) {
        $script:LoadedAssembly = $alreadyLoaded
        $script:AssemblyLoaded = $true
        return $alreadyLoaded
    }

    $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $candidatePaths = @(
        "$projectRoot\bin\$Configuration\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll",
        "$projectRoot\bin\Debug\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll",
        "$projectRoot\bin\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll",
        "$projectRoot\bin\x64\$Configuration\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll"
    )

    $dllPath = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $dllPath) {
        throw "Could not locate compiled DiskMasterWinUI.dll in candidate paths: $($candidatePaths -join '; ')"
    }

    try {
        $assembly = [System.Reflection.Assembly]::LoadFrom($dllPath)
        $script:LoadedAssembly = $assembly
        $script:AssemblyLoaded = $true
        return $assembly
    }
    catch {
        throw "Failed to load assembly from '$dllPath': $_"
    }
}

function Register-E2ETest {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][int]$Tier,
        [Parameter(Mandatory=$true)][string]$Feature,
        [Parameter(Mandatory=$true)][string]$Source,
        [Parameter(Mandatory=$true)][string]$Description,
        [Parameter(Mandatory=$true)][scriptblock]$TestBlock
    )

    $test = [PSCustomObject]@{
        Name        = $Name
        Tier        = $Tier
        Feature     = $Feature
        Source      = $Source
        Description = $Description
        TestBlock   = $TestBlock
    }

    $global:E2EAllTests.Add($test)
}

# --- Assertion Helpers ---

function Assert-True {
    param($Condition, [string]$Message = "Expected condition to be True, but was False.")
    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Assert-False {
    param($Condition, [string]$Message = "Expected condition to be False, but was True.")
    if ($Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Message = "")
    if ($Actual -ne $Expected) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Expected: <$Expected>, Actual: <$Actual>"
    }
}

function Assert-NotEqual {
    param($Actual, $NotExpected, [string]$Message = "")
    if ($Actual -eq $NotExpected) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Expected NOT equal to: <$NotExpected>, but was identical."
    }
}

function Assert-Null {
    param($Actual, [string]$Message = "Expected value to be Null.")
    if ($null -ne $Actual) {
        throw "ASSERTION FAILED: $Message (Actual: <$Actual>)"
    }
}

function Assert-NotNull {
    param($Actual, [string]$Message = "Expected value NOT to be Null.")
    if ($null -eq $Actual) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Assert-Contains {
    param($Haystack, $Needle, [string]$Message = "")
    if ($null -eq $Haystack) {
        throw "ASSERTION FAILED: Haystack is null when searching for '$Needle'"
    }

    $contains = $false
    if ($Haystack -is [string]) {
        $contains = $Haystack.Contains($Needle)
    }
    elseif ($Haystack -is [System.Collections.IEnumerable]) {
        foreach ($item in $Haystack) {
            if ($item -eq $Needle) { $contains = $true; break }
        }
    }
    else {
        $contains = $Haystack -contains $Needle
    }

    if (-not $contains) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Expected container to contain: <$Needle>"
    }
}

function Assert-NotContains {
    param($Haystack, $Needle, [string]$Message = "")
    if ($null -eq $Haystack) { return }

    $contains = $false
    if ($Haystack -is [string]) {
        $contains = $Haystack.Contains($Needle)
    }
    elseif ($Haystack -is [System.Collections.IEnumerable]) {
        foreach ($item in $Haystack) {
            if ($item -eq $Needle) { $contains = $true; break }
        }
    }
    else {
        $contains = $Haystack -contains $Needle
    }

    if ($contains) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Expected container NOT to contain: <$Needle>"
    }
}

function Assert-Matches {
    param([string]$String, [string]$RegexPattern, [string]$Message = "")
    if ($String -notmatch $RegexPattern) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}String does not match pattern '$RegexPattern'. Input was: <$String>"
    }
}

function Assert-Throws {
    param([scriptblock]$ScriptBlock, [string]$ExpectedExceptionType = "", [string]$Message = "")
    $threw = $false
    try {
        & $ScriptBlock
    }
    catch {
        $threw = $true
        $matched = (-not $ExpectedExceptionType) -or `
            ($_.Exception.GetType().FullName -like "*$ExpectedExceptionType*") -or `
            ($null -ne $_.Exception.InnerException -and $_.Exception.InnerException.GetType().FullName -like "*$ExpectedExceptionType*") -or `
            ($_.Exception.Message -like "*$ExpectedExceptionType*")
        if (-not $matched) {
            throw "ASSERTION FAILED: Threw $($_.Exception.GetType().FullName) with message '$($_.Exception.Message)' but expected '$ExpectedExceptionType'"
        }
    }
    if (-not $threw) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Expected exception to be thrown, but code executed without error."
    }
}

function Assert-DoesNotThrow {
    param([scriptblock]$ScriptBlock, [string]$Message = "")
    try {
        & $ScriptBlock
    }
    catch {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Expected no exception, but caught: $_"
    }
}

function Assert-WithinRange {
    param($Actual, $Min, $Max, [string]$Message = "")
    if ($Actual -lt $Min -or $Actual -gt $Max) {
        $msg = if ($Message) { "$Message - " } else { "" }
        throw "ASSERTION FAILED: ${msg}Value <$Actual> is outside expected range [$Min, $Max]"
    }
}

# --- Runner Engine ---

function Invoke-E2ETestSuite {
    param(
        [int]$FilterTier = 0,
        [string]$FilterFeature = "",
        [switch]$StrictMode = $false,
        [string]$OutputPath = ""
    )

    $testsToRun = $global:E2EAllTests
    if ($FilterTier -gt 0) {
        $testsToRun = $testsToRun | Where-Object { $_.Tier -eq $FilterTier }
    }
    if (-not [string]::IsNullOrWhiteSpace($FilterFeature)) {
        $testsToRun = $testsToRun | Where-Object { $_.Feature -eq $FilterFeature }
    }

    $global:E2ETestResults.Clear()
    $total = $testsToRun.Count
    $passed = 0
    $failed = 0
    $pending = 0
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    Write-Host "════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  DiskMaster Pro WinUI 3 — E2E Test Suite Execution Runner                      " -ForegroundColor Cyan
    Write-Host "  Total Tests Registered: $total  | Strict Mode: $StrictMode                    " -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

    $currentTier = -1

    foreach ($test in $testsToRun) {
        if ($test.Tier -ne $currentTier) {
            $currentTier = $test.Tier
            $tierName = switch ($currentTier) {
                1 { "Tier 1: Feature Coverage (R1-R5 Core Invariants)" }
                2 { "Tier 2: Boundary, Buffer & Corner Cases" }
                3 { "Tier 3: Cross-Feature Combinations & Integrations" }
                4 { "Tier 4: Real-World End-to-End Application Scenarios" }
                Default { "Tier $currentTier" }
            }
            Write-Host "`n────────────────────────────────────────────────────────────────────────" -ForegroundColor Yellow
            Write-Host "  ► $tierName" -ForegroundColor Yellow
            Write-Host "────────────────────────────────────────────────────────────────────────" -ForegroundColor Yellow
        }

        $testSw = [System.Diagnostics.Stopwatch]::StartNew()
        $status = "PASS"
        $errorMsg = ""
        $errorDetails = ""

        try {
            & $test.TestBlock
        }
        catch {
            $ex = $_.Exception
            $errorMsg = $ex.Message
            $errorDetails = $_.ScriptStackTrace

            if ($errorMsg -like "*PENDING*" -or $errorMsg -like "*Pending*") {
                if ($StrictMode) {
                    $status = "FAIL"
                } else {
                    $status = "PENDING"
                }
            }
            else {
                $status = "FAIL"
            }
        }
        $testSw.Stop()

        $resultObj = [PSCustomObject]@{
            Name          = $test.Name
            Tier          = $test.Tier
            Feature       = $test.Feature
            Source        = $test.Source
            Description   = $test.Description
            Status        = $status
            DurationMs    = $testSw.ElapsedMilliseconds
            ErrorMessage  = $errorMsg
            StackTrace    = $errorDetails
        }
        $global:E2ETestResults.Add($resultObj)

        switch ($status) {
            "PASS" {
                $passed++
                Write-Host "  [PASS] " -ForegroundColor Green -NoNewline
                Write-Host "$($test.Name) " -ForegroundColor White -NoNewline
                Write-Host "($($testSw.ElapsedMilliseconds)ms)" -ForegroundColor DarkGray
            }
            "PENDING" {
                $pending++
                Write-Host "  [PEND] " -ForegroundColor DarkYellow -NoNewline
                Write-Host "$($test.Name) - $errorMsg " -ForegroundColor DarkYellow -NoNewline
                Write-Host "($($testSw.ElapsedMilliseconds)ms)" -ForegroundColor DarkGray
            }
            "FAIL" {
                $failed++
                Write-Host "  [FAIL] " -ForegroundColor Red -NoNewline
                Write-Host "$($test.Name)" -ForegroundColor Red
                Write-Host "         Source: $($test.Source)" -ForegroundColor DarkGray
                Write-Host "         Error:  $errorMsg" -ForegroundColor Magenta
            }
        }
    }

    $stopwatch.Stop()

    # --- Summary Report ---
    Write-Host "`n════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  E2E Test Execution Summary (Duration: $($stopwatch.Elapsed.TotalSeconds.ToString('F2'))s)      " -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

    for ($t = 1; $t -le 4; $t++) {
        $tierTests = $global:E2ETestResults | Where-Object { $_.Tier -eq $t }
        $tTotal = $tierTests.Count
        $tPass = ($tierTests | Where-Object { $_.Status -eq 'PASS' }).Count
        $tFail = ($tierTests | Where-Object { $_.Status -eq 'FAIL' }).Count
        $tPend = ($tierTests | Where-Object { $_.Status -eq 'PENDING' }).Count

        $tierLabel = switch ($t) {
            1 { "Tier 1 (Feature Coverage)     " }
            2 { "Tier 2 (Boundary & Corner)    " }
            3 { "Tier 3 (Cross-Feature Combos) " }
            4 { "Tier 4 (Real-World Scenarios) " }
        }

        $color = if ($tFail -gt 0) { "Red" } elseif ($tPend -gt 0) { "Yellow" } else { "Green" }
        Write-Host "  $tierLabel : Total: $tTotal | Passed: $tPass | Failed: $tFail | Pending: $tPend" -ForegroundColor $color
    }

    Write-Host "────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
    $summaryColor = if ($failed -gt 0) { "Red" } elseif ($pending -gt 0) { "Yellow" } else { "Green" }
    Write-Host "  TOTAL: $total | PASSED: $passed | FAILED: $failed | PENDING: $pending" -ForegroundColor $summaryColor
    Write-Host "════════════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $json = $global:E2ETestResults | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.Encoding]::UTF8)
        Write-Host "  Test execution artifact saved to: $OutputPath" -ForegroundColor DarkCyan
    }

    # Exit code determination
    if ($failed -gt 0) {
        return 1
    }
    return 0
}
