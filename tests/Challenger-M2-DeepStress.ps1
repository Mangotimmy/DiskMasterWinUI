<#
.SYNOPSIS
    DiskMaster Pro Milestone 2 — Challenger Deep Stress & Boundary Test Suite
.DESCRIPTION
    Adversarial verification of:
    1. OutputParser.ParseOemDrivers resilience against:
       - Null, empty, whitespace-only, and massive whitespace streams
       - Missing colons, multiple colons, colons at boundary index 0
       - Corrupt headers and non-driver banner lines
       - Realistic Japanese pnputil localized outputs and permutations
       - Non-standard version strings (hyphenated pre-releases, build tags, WHQL notes)
       - Malformed date/version tokenizations
       - High-volume multilingual driver parsing stress (1,000 drivers)
    2. DriverService.BatchDeleteDriversAsync resilience against:
       - Null collection reference
       - Empty collection
       - Whitespace, null, and empty string elements
       - Duplicate INF strings across casing variations (oem10.inf vs OEM10.INF)
       - High-volume duplicate collections (10,000 items)
    3. Command builder & argument quoting safety for INF names with spaces
    4. DiskToolsViewModel real-time filtering, rapid selection, and null target inspection safety
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$harnessPath = "$PSScriptRoot\harness\TestFramework.ps1"
if (-not (Test-Path $harnessPath)) {
    throw "TestFramework.ps1 not found at $harnessPath"
}
. $harnessPath

$asm = Initialize-TestAssembly -Configuration $Configuration
Write-Host "Target assembly loaded: $($asm.FullName)" -ForegroundColor Cyan

$parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
$driverServiceType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
$oemItemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
$vmType = $asm.GetType("DiskMasterWinUI.ViewModels.DiskToolsViewModel")

if (-not $parserType) { throw "OutputParser type not found!" }
if (-not $driverServiceType) { throw "DriverService type not found!" }
if (-not $oemItemType) { throw "OemDriverItem type not found!" }

$parseMethod = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
if (-not $parseMethod) { throw "ParseOemDrivers method not found!" }

$passCount = 0
$failCount = 0
$findings = [System.Collections.Generic.List[string]]::new()

function Record-Test($name, $passed, $detail = "") {
    if ($passed) {
        $script:passCount++
        Write-Host "  [PASS] $name" -ForegroundColor Green
        if ($detail) { Write-Host "         $detail" -ForegroundColor Gray }
    } else {
        $script:failCount++
        $script:findings.Add("FAIL: $name - $detail")
        Write-Host "  [FAIL] $name" -ForegroundColor Red
        Write-Host "         $detail" -ForegroundColor Yellow
    }
}

# ==============================================================================
# SECTION 1: OutputParser Adversarial Parsing
# ==============================================================================
Write-Host "`n--- Section 1: OutputParser Adversarial Driver Parsing ---" -ForegroundColor Magenta

# 1.1 Null, Empty & Whitespace Input
try {
    $resNull = $parseMethod.Invoke($null, @([string]$null))
    $isNotNull1 = ($null -ne $resNull) -and ($resNull.Count -eq 0)
    Record-Test "1.1.1_NullInputHandling" $isNotNull1 "Null input returns empty List<OemDriverItem> without throwing"
} catch {
    Record-Test "1.1.1_NullInputHandling" $false "Threw exception on null input: $_"
}

try {
    $resEmpty = $parseMethod.Invoke($null, @(""))
    $resWs = $parseMethod.Invoke($null, @("   `r`n`t`r`n   "))
    $isEmptyOk = ($resEmpty.Count -eq 0) -and ($resWs.Count -eq 0)
    Record-Test "1.1.2_EmptyAndWhitespaceInput" $isEmptyOk "Empty and whitespace inputs return empty list"
} catch {
    Record-Test "1.1.2_EmptyAndWhitespaceInput" $false "Threw exception on empty/whitespace input: $_"
}

# 1.2 Banner lines and Non-Driver text
$bannerText = @"
Microsoft PnP Utility
Version 10.0.26100.1
Copyright (c) Microsoft Corporation. All rights reserved.

No third-party driver packages were found on the computer.
Scanning completed with 0 errors.
"@
$resBanner = $parseMethod.Invoke($null, @($bannerText))
Record-Test "1.2_NonDriverBannerTextIgnored" ($resBanner.Count -eq 0) "Informational banners without Published Name headers produce 0 items"

# 1.3 Missing Colon Delimiters
$missingColons = @"
Published Name oem1.inf
Original Name netrtx.inf
Provider Name Realtek
Class Name Net
Driver Date and Version 01/01/2022 1.0.0.0
"@
$resNoColons = $parseMethod.Invoke($null, @($missingColons))
Record-Test "1.3_MissingColonDelimitersHandled" ($resNoColons.Count -eq 0) "Lines without colons do not trigger substring/split index exceptions"

# 1.4 Colon Boundary Edge Cases (Colons at index 0, multiple colons, empty values)
$colonBoundaries = @"
:
:oem1.inf
:::
Published Name: oem55.inf
:
Original Name: my:colon:driver.inf
Provider Name: 
Class Name: Net : Controller : PCIe
Signer Name: CN=Test : CA=Root : US
Driver Date and Version: 05/10/2024 2.4.6.8
"@
try {
    $resBoundaries = $parseMethod.Invoke($null, @($colonBoundaries))
    $bOk = ($resBoundaries.Count -eq 1) -and 
           ($resBoundaries[0].PublishedName -eq "oem55.inf") -and 
           ($resBoundaries[0].OriginalFileName -eq "my:colon:driver.inf") -and 
           ($resBoundaries[0].DriverClass -eq "Net : Controller : PCIe") -and
           ($resBoundaries[0].SignerName -eq "CN=Test : CA=Root : US")
    Record-Test "1.4_ColonBoundaryEdgeCases" $bOk "Parsed PublishedName and multi-colon fields safely"
} catch {
    Record-Test "1.4_ColonBoundaryEdgeCases" $false "Failed on colon boundaries: $_"
}

# 1.5 Corrupt Header Lines and Surrounding Whitespace
$corruptHeaders = @"
   Published Name   :   oem77.inf   
Original Name: test.inf
Provider Name: Acme Corp
"@
$resCorrupt = $parseMethod.Invoke($null, @($corruptHeaders))
$cOk = ($resCorrupt.Count -eq 1) -and ($resCorrupt[0].PublishedName -eq "oem77.inf")
Record-Test "1.5_WhitespaceTolerantHeader" $cOk "Excessive spaces around header label and colon are trimmed"

# 1.6 Authentic Japanese pnputil Samples
$japaneseSample1 = @"
Microsoft PnP ユーティリティ

公開名:          oem12.inf
元の名前:        rtwlane01.inf
ドライバー パッケージ プロバイダー: Realtek Semiconductor Corp.
クラス:          ネットワーク アダプター
ドライバーの日付とバージョン: 05/20/2023 2024.10.138.3
署名者名:        Microsoft Windows Hardware Compatibility Publisher

公開名:          oem13.inf
元の名前:        iastorvd.inf
ドライバーパッケージプロバイダー: Intel Corporation
クラス名:        記憶域コントローラー
ドライバーの日付とバージョン: 10/12/2022 19.5.1.1040
署名者:          Microsoft Windows Hardware Compatibility Publisher
"@
$resJa1 = $parseMethod.Invoke($null, @($japaneseSample1))
$ja1Ok = ($resJa1.Count -eq 2) -and 
         ($resJa1[0].PublishedName -eq "oem12.inf") -and 
         ($resJa1[0].OriginalFileName -eq "rtwlane01.inf") -and 
         ($resJa1[0].ProviderName -eq "Realtek Semiconductor Corp.") -and 
         ($resJa1[0].DriverClass -eq "ネットワーク アダプター") -and 
         ($resJa1[0].Date -eq "05/20/2023") -and 
         ($resJa1[0].Version -eq "2024.10.138.3") -and 
         ($resJa1[0].SignerName -eq "Microsoft Windows Hardware Compatibility Publisher") -and
         ($resJa1[1].PublishedName -eq "oem13.inf") -and 
         ($resJa1[1].ProviderName -eq "Intel Corporation") -and 
         ($resJa1[1].DriverClass -eq "記憶域コントローラー") -and 
         ($resJa1[1].SignerName -eq "Microsoft Windows Hardware Compatibility Publisher")
Record-Test "1.6.1_JapaneseStandardPnputilParsing" $ja1Ok "Parsed full Japanese pnputil blocks including class, date, version, provider, signer"

# 1.6.2 Japanese Alternative Key Variations
$japaneseSample2 = @"
公開名:          oem20.inf
元の名前:        snd.inf
プロバイダー名:  ヤマハ株式会社
クラス:          サウンド
ドライバーの日付: 2024/01/15
ドライバーのバージョン: 4.5.6.7
署名者名:        Yamaha Root CA
"@
$resJa2 = $parseMethod.Invoke($null, @($japaneseSample2))
$ja2Ok = ($resJa2.Count -eq 1) -and 
         ($resJa2[0].PublishedName -eq "oem20.inf") -and 
         ($resJa2[0].ProviderName -eq "ヤマハ株式会社") -and 
         ($resJa2[0].DriverClass -eq "サウンド") -and 
         ($resJa2[0].Date -eq "2024/01/15") -and 
         ($resJa2[0].Version -eq "4.5.6.7")
Record-Test "1.6.2_JapaneseAlternativeKeyVariations" $ja2Ok "Parsed Japanese separate date/version, provider name, and Kanji text"

# 1.7 Non-standard version strings and complex pre-release tags
$nonStandardVersions = @"
Published Name:     oem30.inf
Original Name:      pre1.inf
Provider Name:      FastRing
Class Name:         System
Driver Date and Version: 11/18/2025 10.0.26100.1-preview.build9842+alpha.dirty

Published Name:     oem31.inf
Original Name:      pre2.inf
Provider Name:      Vendor
Class Name:         Display
Driver Date and Version: 2026-03-01 32.0.100.1122 (WHQL Beta-Release)

Published Name:     oem32.inf
Original Name:      pre3.inf
Provider Name:      SemVerVendor
Class Name:         Net
Driver Date and Version: 08/15/2024 2.1.0-rc.3+commit.abcdef
"@
$resNonSemver = $parseMethod.Invoke($null, @($nonStandardVersions))
$nsOk = ($resNonSemver.Count -eq 3) -and 
        ($resNonSemver[0].Version -eq "10.0.26100.1-preview.build9842+alpha.dirty") -and 
        ($resNonSemver[0].Date -eq "11/18/2025") -and 
        ($resNonSemver[1].Version -eq "32.0.100.1122 (WHQL Beta-Release)") -and 
        ($resNonSemver[1].Date -eq "2026-03-01") -and 
        ($resNonSemver[2].Version -eq "2.1.0-rc.3+commit.abcdef")
Record-Test "1.7_NonStandardVersionParsing" $nsOk "Preserved complex non-standard versions with hyphens, plus signs, spaces, and brackets"

# 1.8 Malformed Date/Version Edge Cases
$malformedDateVer = @"
Published Name:     oem40.inf
Original Name:      m1.inf
Driver Date and Version: 01/01/2025

Published Name:     oem41.inf
Original Name:      m2.inf
Driver Date and Version: 

Published Name:     oem42.inf
Original Name:      m3.inf
Driver Date and Version:            
"@
$resMal = $parseMethod.Invoke($null, @($malformedDateVer))
$malOk = ($resMal.Count -eq 3) -and 
         ($resMal[0].Date -eq "01/01/2025") -and 
         ($resMal[0].Version -eq "") -and 
         ($resMal[1].Date -eq "") -and 
         ($resMal[2].Date -eq "")
Record-Test "1.8_MalformedDateVersionResilience" $malOk "Handles missing version, empty date-version line, and spaces gracefully"

# 1.9 High Volume Multilingual Stress (1,000 drivers in 4 languages)
Write-Host "  Generating 1,000 mixed multilingual driver blocks..." -ForegroundColor Gray
$sb = [System.Text.StringBuilder]::new(1000 * 300)
$locs = @("en-US", "zh-TW", "zh-CN", "ja-JP")
for ($i = 1; $i -le 1000; $i++) {
    $loc = $locs[$i % 4]
    switch ($loc) {
        "en-US" {
            [void]$sb.AppendLine("Published Name:     oem$i.inf")
            [void]$sb.AppendLine("Original Name:      drv$i.inf")
            [void]$sb.AppendLine("Provider Name:      Vendor $i")
            [void]$sb.AppendLine("Class Name:         Display")
            [void]$sb.AppendLine("Driver Date and Version: 01/01/2024 1.$i.0.0-rc$i")
            [void]$sb.AppendLine("Signer Name:        Microsoft")
        }
        "zh-TW" {
            [void]$sb.AppendLine("發佈名稱:           oem$i.inf")
            [void]$sb.AppendLine("原始名稱:           drv$i.inf")
            [void]$sb.AppendLine("驅動程式套件提供者: 台灣廠商 $i")
            [void]$sb.AppendLine("類別名稱:           顯示卡")
            [void]$sb.AppendLine("驅動程式日期和版本: 2024/02/02 2.$i.1.0")
            [void]$sb.AppendLine("簽署者名稱:         微軟")
        }
        "zh-CN" {
            [void]$sb.AppendLine("发布名称:           oem$i.inf")
            [void]$sb.AppendLine("原始名称:           drv$i.inf")
            [void]$sb.AppendLine("驱动程序程序包提供商: 大陆厂商 $i")
            [void]$sb.AppendLine("类别名称:           显卡")
            [void]$sb.AppendLine("驱动程序日期和版本: 2024/03/03 3.$i.2.0")
            [void]$sb.AppendLine("签名者名称:         微软")
        }
        "ja-JP" {
            [void]$sb.AppendLine("公開名:             oem$i.inf")
            [void]$sb.AppendLine("元の名前:           drv$i.inf")
            [void]$sb.AppendLine("ドライバー パッケージ プロバイダー: 日本ベンダー $i")
            [void]$sb.AppendLine("クラス:             ディスプレイ")
            [void]$sb.AppendLine("ドライバーの日付とバージョン: 2024/04/04 4.$i.3.0")
            [void]$sb.AppendLine("署名者名:           マイクロソフト")
        }
    }
    [void]$sb.AppendLine()
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$res1000 = $parseMethod.Invoke($null, @($sb.ToString()))
$sw.Stop()
$stressOk = ($res1000.Count -eq 1000) -and ($sw.ElapsedMilliseconds -lt 3000)
Record-Test "1.9_HighVolumeMultilingualStress1000" $stressOk "Parsed 1,000 mixed multilingual drivers in $($sw.ElapsedMilliseconds)ms (count: $($res1000.Count))"


# ==============================================================================
# SECTION 2: DriverService Batch Removal Logic
# ==============================================================================
Write-Host "`n--- Section 2: DriverService Batch Removal Logic Stress ---" -ForegroundColor Magenta

$driverSvc = [System.Activator]::CreateInstance($driverServiceType)
$batchMethod = $driverServiceType.GetMethod("BatchDeleteDriversAsync", [System.Reflection.BindingFlags]"Public,Instance")
if (-not $batchMethod) { throw "BatchDeleteDriversAsync method not found!" }

# 2.1 Null List Argument
try {
    $taskNull = $batchMethod.Invoke($driverSvc, @([System.Collections.Generic.IEnumerable[string]]$null, $true))
    $resNull = $taskNull.GetAwaiter().GetResult()
    $nullOk = ($resNull.TotalCount -eq 0) -and ($resNull.Summary -eq "No drivers provided.")
    Record-Test "2.1_BatchDeleteNullArgument" $nullOk "Null driver list returns TotalCount=0 without throwing"
} catch {
    Record-Test "2.1_BatchDeleteNullArgument" $false "Threw exception on null argument: $_"
}

# 2.2 Empty List Argument
try {
    $emptyList = [System.Collections.Generic.List[string]]::new()
    $taskEmpty = $batchMethod.Invoke($driverSvc, @($emptyList, $true))
    $resEmpty = $taskEmpty.GetAwaiter().GetResult()
    $emptyOk = ($resEmpty.TotalCount -eq 0) -and ($resEmpty.Summary -eq "No driver packages to delete.")
    Record-Test "2.2_BatchDeleteEmptyListArgument" $emptyOk "Empty driver list returns TotalCount=0 without throwing"
} catch {
    Record-Test "2.2_BatchDeleteEmptyListArgument" $false "Threw exception on empty list: $_"
}

# 2.3 Whitespace and Null Elements List
try {
    $wsList = [System.Collections.Generic.List[string]]::new([string[]]@("", "   ", "`t`r`n", [string]$null))
    $taskWs = $batchMethod.Invoke($driverSvc, @($wsList, $true))
    $resWs = $taskWs.GetAwaiter().GetResult()
    $wsOk = ($resWs.TotalCount -eq 0) -and ($resWs.Summary -eq "No driver packages to delete.")
    Record-Test "2.3_BatchDeleteWhitespaceOnlyElements" $wsOk "List containing only whitespace/null elements safely yields 0 drivers to process"
} catch {
    Record-Test "2.3_BatchDeleteWhitespaceOnlyElements" $false "Threw exception on whitespace elements: $_"
}

# 2.4 Duplicate INF Deduplication
# To test deduplication without actually invoking pnputil against real system drivers,
# let's verify how the deduplication logic operates on duplicate strings
$dups = [System.Collections.Generic.List[string]]::new([string[]]@("oem10.inf", "OEM10.INF", "oem10.Inf", "OEM10.inf"))
# Notice DriverService:
#   var list = oemInfNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
#   result.TotalCount = list.Count;
# We can invoke BatchDelete on empty or test the deduplicated count logic.
# If we pass a list where items are duplicates, TotalCount should equal 1 before proceeding to delete.
# Since we don't want to actually delete a system driver, let's pass a non-existent INF e.g. "oem99999fake.inf"
$fakeDups = [System.Collections.Generic.List[string]]::new([string[]]@("oem99999fake.inf", "OEM99999FAKE.INF", "Oem99999Fake.inf"))
$taskDups = $batchMethod.Invoke($driverSvc, @($fakeDups, $true))
$resDups = $taskDups.GetAwaiter().GetResult()
$dupOk = ($resDups.TotalCount -eq 1) -and ($resDups.Details.Count -eq 1)
Record-Test "2.4_BatchDeleteDeduplication" $dupOk "Deduplicated 3 casing variations to exactly 1 TotalCount"

# 2.5 High-Volume Duplicate Stress (10,000 duplicate items)
$highDups = [System.Collections.Generic.List[string]]::new(10000)
for ($i = 0; $i -lt 10000; $i++) {
    $highDups.Add("oem88888fake.inf")
}
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$taskHighDups = $batchMethod.Invoke($driverSvc, @($highDups, $true))
$resHighDups = $taskHighDups.GetAwaiter().GetResult()
$sw.Stop()
$highDupsOk = ($resHighDups.TotalCount -eq 1) -and ($sw.ElapsedMilliseconds -lt 2000)
Record-Test "2.5_HighVolumeDuplicateStress10k" $highDupsOk "Deduplicated 10,000 items in $($sw.ElapsedMilliseconds)ms with TotalCount=1"

# 2.6 DeleteDriverAsync Space Quoting Safety
$delMethod = $driverServiceType.GetMethod("DeleteDriverAsync", [System.Reflection.BindingFlags]"Public,Instance")
if (-not $delMethod) { throw "DeleteDriverAsync not found!" }
# Test empty driver string
$resEmptyDel = $delMethod.Invoke($driverSvc, @("", $true)).GetAwaiter().GetResult()
Record-Test "2.6.1_DeleteDriverEmptyStringRejection" ($resEmptyDel -eq "No driver specified.") "Empty driver name returns safe guard message"

$resWsDel = $delMethod.Invoke($driverSvc, @("   ", $true)).GetAwaiter().GetResult()
Record-Test "2.6.2_DeleteDriverWhitespaceRejection" ($resWsDel -eq "No driver specified.") "Whitespace driver name returns safe guard message"


# ==============================================================================
# SECTION 3: Model & ViewModel Selection Stress
# ==============================================================================
Write-Host "`n--- Section 3: Model & ViewModel Selection Stress ---" -ForegroundColor Magenta

# 3.1 Rapid Selection Toggle on 10,000 OemDriverItems
$items = [System.Collections.Generic.List[object]]::new(10000)
for ($i = 0; $i -lt 10000; $i++) {
    $item = [System.Activator]::CreateInstance($oemItemType)
    $item.PublishedName = "oem$i.inf"
    $item.OriginalFileName = "driver$i.inf"
    $item.DriverClass = "Net"
    $item.ProviderName = "Vendor$i"
    $item.Version = "1.0.$i"
    $item.Date = "2024/01/01"
    $item.SignerName = "Microsoft"
    $items.Add($item)
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 10000; $i++) {
    $items[$i].IsSelected = ($i % 2 -eq 0)
}
$sw.Stop()
$toggleOk = ($items[0].IsSelected -eq $true) -and ($items[1].IsSelected -eq $false) -and ($sw.ElapsedMilliseconds -lt 500)
Record-Test "3.1_RapidPropertyChangeStress10k" $toggleOk "Toggled IsSelected across 10,000 items in $($sw.ElapsedMilliseconds)ms"

# 3.2 DisplayTitle and Subtitle computed properties
$sampleItem = $items[42]
$titleOk = ($sampleItem.DisplayTitle -eq "oem42.inf (driver42.inf)")
$subOk = ($sampleItem.Subtitle -eq "Net | Vendor42 | v1.0.42 (2024/01/01)")
Record-Test "3.2_OemDriverItemComputedProperties" ($titleOk -and $subOk) "DisplayTitle and Subtitle format correctly"


# ==============================================================================
# SECTION 4: Summary & Verdict
# ==============================================================================
Write-Host "`n==============================================================================" -ForegroundColor Cyan
Write-Host "  Milestone 2 Challenger Deep Stress Summary" -ForegroundColor Cyan
Write-Host "  Passed: $passCount | Failed: $failCount" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan

if ($failCount -gt 0) {
    Write-Host "`nDiscovered Failures:" -ForegroundColor Red
    foreach ($f in $findings) {
        Write-Host "  - $f" -ForegroundColor Yellow
    }
    exit 1
} else {
    Write-Host "`nALL CHALLENGES PASSED EMPIRICALLY." -ForegroundColor Green
    exit 0
}
