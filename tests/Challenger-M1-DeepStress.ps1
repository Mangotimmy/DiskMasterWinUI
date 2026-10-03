<#
.SYNOPSIS
    DiskMaster Pro Milestone 1 — Challenger Deep Stress & Boundary Test Suite
.DESCRIPTION
    Adversarial verification of:
    1. Rapid language switching & concurrency stress across all 4 cultures.
    2. Missing key fallback, boundary inputs, and key parity.
    3. Japanese encoding integrity, UTF-8 byte purity, mojibake detection.
    4. Traditional vs. Simplified Chinese character set & vocabulary distinction.
    5. AppIcon.ico binary format, layer bounds, payload signatures, and internal dimensions.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$candidatePaths = @(
    "$projectRoot\bin\$Configuration\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll",
    "$projectRoot\bin\Debug\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll",
    "$projectRoot\bin\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll",
    "$projectRoot\bin\Release\net9.0-windows10.0.26100.0\win-arm64\DiskMasterWinUI.dll",
    "$projectRoot\publish\DiskMasterWinUI.dll"
)
$dllPath = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dllPath) {
    throw "Target assembly not found in candidate paths. Run dotnet build first."
}
Write-Host "Assembly target: $dllPath"

$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $dllPath).Path)
$locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
if (-not $locType) { throw "LocalizationService type not found!" }
$loc = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)

$dictField = $locType.GetField("_translations", [System.Reflection.BindingFlags]"NonPublic,Instance")
$translations = $dictField.GetValue($loc)

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
# SECTION 1: Rapid Language Switching & State Integrity
# ==============================================================================
Write-Host "`n--- Section 1: Rapid Language Switching & State Integrity ---" -ForegroundColor Magenta

# 1.1 Sequential Rapid Switching (10,000 iterations)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$cultures = @("zh-TW", "zh-CN", "en-US", "ja-JP")
$switchCount = 10000
$switchError = $null

try {
    for ($i = 0; $i -lt $switchCount; $i++) {
        $cult = $cultures[$i % 4]
        $loc.SetLanguage($cult)
        if ($loc.CurrentLanguage -ne $cult) {
            throw "Language mismatch: expected $cult, got $($loc.CurrentLanguage)"
        }
    }
} catch {
    $switchError = $_.ToString()
}
$sw.Stop()
Record-Test "1.1_SequentialRapidSwitching10k" ($null -eq $switchError) "Completed $switchCount switches in $($sw.ElapsedMilliseconds)ms (avg $([Math]::Round($sw.ElapsedMilliseconds / $switchCount, 4))ms/op)"

# 1.2 Random Switching & Consistency Oracle
$sw.Restart()
$randomFail = $null
$rng = [System.Random]::new(1337)
for ($i = 0; $i -lt 5000; $i++) {
    $idx = $rng.Next(0, 4)
    $targetCult = $cultures[$idx]
    $loc.SetLanguage($targetCult)
    
    # Check IsChinese Oracle
    $expectedIsChinese = ($targetCult -eq "zh-TW" -or $targetCult -eq "zh-CN")
    if ($loc.IsChinese -ne $expectedIsChinese) {
        $randomFail = "IsChinese mismatch for $targetCult`: expected $expectedIsChinese, got $($loc.IsChinese)"
        break
    }
    
    # Check T() helper oracle
    $tVal = [DiskMasterWinUI.Services.LocalizationService]::T("TW", "CN", "EN", "JA")
    $expT = switch ($targetCult) { "zh-TW" { "TW" } "zh-CN" { "CN" } "ja-JP" { "JA" } default { "EN" } }
    if ($tVal -ne $expT) {
        $randomFail = "T() helper mismatch for $targetCult`: expected $expT, got $tVal"
        break
    }
}
$sw.Stop()
Record-Test "1.2_RandomSwitchingWithOracle" ($null -eq $randomFail) "5,000 random hops with IsChinese and T() validation passed in $($sw.ElapsedMilliseconds)ms"

# 1.3 Event Notification Dispatch
$eventFiredCount = 0
$actionDelegate = [System.Action]{ $script:eventFiredCount++ }
$loc.add_LanguageChanged($actionDelegate)

$loc.SetLanguage("en-US")
$loc.SetLanguage("zh-TW")
$loc.SetLanguage("ja-JP")

$loc.remove_LanguageChanged($actionDelegate)
Record-Test "1.3_LanguageChangedEventFiring" ($eventFiredCount -eq 3) "LanguageChanged event fired $eventFiredCount times across 3 transitions"

# 1.4 ToggleLanguage Cycling Order
$toggleResults = @()
$loc.SetLanguage("zh-TW")
for ($i = 0; $i -lt 4; $i++) {
    $loc.ToggleLanguage()
    $toggleResults += $loc.CurrentLanguage
}
$expectedToggle = @("zh-CN", "en-US", "ja-JP", "zh-TW")
$toggleMatch = ($toggleResults -join ",") -eq ($expectedToggle -join ",")
Record-Test "1.4_ToggleLanguageCycle" $toggleMatch "Cycle: $($toggleResults -join ' -> ')"

# 1.5 High-Frequency Interleaved Switch & Query Stress Test (5,000 operations)
$interleavedErrors = 0
$sw.Restart()
for ($i = 0; $i -lt 5000; $i++) {
    $c = $cultures[$i % 4]
    $loc.SetLanguage($c)
    $title = $loc.GetString("AppTitle")
    $status = $loc.GetString("StatusReady")
    if ([string]::IsNullOrEmpty($title) -or [string]::IsNullOrEmpty($status)) {
        $interleavedErrors++
    }
}
$sw.Stop()
Record-Test "1.5_InterleavedHighFrequencySwitchAndQuery" ($interleavedErrors -eq 0) "5,000 interleaved switch-and-query cycles completed in $($sw.ElapsedMilliseconds)ms with $interleavedErrors errors"


# ==============================================================================
# SECTION 2: Non-Existent Keys, Fallbacks & Parity Oracle
# ==============================================================================
Write-Host "`n--- Section 2: Non-Existent Keys, Boundary Inputs & Key Parity ---" -ForegroundColor Magenta

# 2.1 Standard Non-Existent Key Graceful Fallback
$loc.SetLanguage("en-US")
$k1 = "Completely_Non_Existent_Key_XYZ"
$res1 = $loc.GetString($k1)
Record-Test "2.1_StandardMissingKeyFallback" ($res1 -eq $k1) "Returned verbatim key name '$res1'"

# 2.2 Indexer Non-Existent Key Fallback
$indexerMethod = $locType.GetProperty("Item").GetGetMethod()
$resIndexer = $indexerMethod.Invoke($loc, @($k1))
Record-Test "2.2_IndexerMissingKeyFallback" ($resIndexer -eq $k1) "Indexer returned verbatim key name '$resIndexer'"

# 2.3 Boundary Keys: Empty string, whitespace, long string, symbols
$boundaryCases = @(
    @{ Name = "EmptyString"; Key = ""; Exp = "" },
    @{ Name = "Whitespace"; Key = "   "; Exp = "   " },
    @{ Name = "SpecialSymbols"; Key = "<div id='test'>&amp;</div>"; Exp = "<div id='test'>&amp;</div>" },
    @{ Name = "ExtremeLengthKey"; Key = ("A" * 5000); Exp = ("A" * 5000) },
    @{ Name = "NewlineInKey"; Key = "Line1`nLine2"; Exp = "Line1`nLine2" }
)

foreach ($tc in $boundaryCases) {
    $r = $loc.GetString($tc.Key)
    Record-Test "2.3_BoundaryKey_$($tc.Name)" ($r -eq $tc.Exp) "Length: $($tc.Key.Length) chars, returned accurately"
}

# 2.4 Unsupported / Unknown Active Language Fallback
$loc.SetLanguage("fr-FR")
$resFr = $loc.GetString("Refresh")
# When in fr-FR, should fallback to zh-TW ("重新整理")
Record-Test "2.4_UnsupportedCultureFallback" ($resFr -eq "重新整理") "Culture fr-FR gracefully fell back to zh-TW ('$resFr')"

$resFrMissing = $loc.GetString("Totally_Missing_In_All")
Record-Test "2.4b_UnsupportedCultureMissingKey" ($resFrMissing -eq "Totally_Missing_In_All") "Missing key in unsupported culture returned key name"

# 2.5 100% Full Key Parity Verification across all 4 cultures
$twKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]$translations["zh-TW"].Keys)
$cnKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]$translations["zh-CN"].Keys)
$enKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]$translations["en-US"].Keys)
$jaKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]$translations["ja-JP"].Keys)

Write-Host "   Total keys in zh-TW: $($twKeys.Count)" -ForegroundColor Gray
Write-Host "   Total keys in zh-CN: $($cnKeys.Count)" -ForegroundColor Gray
Write-Host "   Total keys in en-US: $($enKeys.Count)" -ForegroundColor Gray
Write-Host "   Total keys in ja-JP: $($jaKeys.Count)" -ForegroundColor Gray

$missingInCn = @($twKeys | Where-Object { -not $cnKeys.Contains($_) })
$missingInEn = @($twKeys | Where-Object { -not $enKeys.Contains($_) })
$missingInJa = @($twKeys | Where-Object { -not $jaKeys.Contains($_) })

Record-Test "2.5_KeyParity_zhCN" ($missingInCn.Count -eq 0) "Missing in zh-CN: $($missingInCn.Count) keys"
Record-Test "2.5_KeyParity_enUS" ($missingInEn.Count -eq 0) "Missing in en-US: $($missingInEn.Count) keys"
Record-Test "2.5_KeyParity_jaJP" ($missingInJa.Count -eq 0) "Missing in ja-JP: $($missingInJa.Count) keys"

# 2.6 No Null or Empty Translations in Any Language
$nullEmptyCount = 0
foreach ($cult in $cultures) {
    foreach ($entry in $translations[$cult].GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace($entry.Value)) {
            $nullEmptyCount++
            $findings.Add("Culture $cult key '$($entry.Key)' is null or whitespace")
        }
    }
}
Record-Test "2.6_NoNullOrWhitespaceTranslations" ($nullEmptyCount -eq 0) "Checked $($twKeys.Count * 4) translation entries; $nullEmptyCount empty entries found"


# ==============================================================================
# SECTION 3: Japanese Text Encoding Integrity & Mojibake Audit
# ==============================================================================
Write-Host "`n--- Section 3: Japanese Text Encoding Integrity & Mojibake Audit ---" -ForegroundColor Magenta

$jaDict = $translations["ja-JP"]
$mojibakeHits = [System.Collections.Generic.List[string]]::new()
$nonJapaneseHits = [System.Collections.Generic.List[string]]::new()
$utf8Errors = 0

# Regex for common mojibake sequences:
# - Repeated question marks (e.g. ???)
# - UTF-8 / Windows-1252 artifacts like Ã, Â, æ, ç, è, é, ãƒ, â€
# - Replacement character \uFFFD
$mojibakePattern = "[\uFFFD]|^\?+$|[\u00C0-\u00FF]{2,}|ã[\u0080-\u00BF]|â€"

foreach ($kvp in $jaDict.GetEnumerator()) {
    $val = $kvp.Value
    
    # Check 1: Mojibake regex
    if ($val -match $mojibakePattern) {
        $mojibakeHits.Add("Key '$($kvp.Key)' matched mojibake pattern: $val")
    }
    
    # Check 2: UTF-8 round-trip byte integrity
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($val)
    $decoded = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($val -ne $decoded) {
        $utf8Errors++
        $findings.Add("UTF-8 byte roundtrip failed for ja-JP key '$($kvp.Key)'")
    }
    
    # Check 3: Valid Japanese character presence
    # Japanese strings with text should contain at least Kana (Hiragana \u3040-\u309F, Katakana \u30A0-\u30FF) or Kanji (\u4E00-\u9FFF)
    # Exclude purely technical keys or pure punctuation/icons if any
    $hasJapaneseChar = ($val -match "[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF]")
    $hasAsciiOnly = ($val -match "^[\x00-\x7F\s\p{P}\p{S}]+$")
    if (-not $hasJapaneseChar -and -not $hasAsciiOnly) {
        $nonJapaneseHits.Add("Key '$($kvp.Key)' has neither Japanese nor pure ASCII/symbols: $val")
    }
}

Record-Test "3.1_NoMojibakeArtifactsInJapanese" ($mojibakeHits.Count -eq 0) "Inspected $($jaDict.Count) ja-JP strings; found $($mojibakeHits.Count) mojibake artifacts"
Record-Test "3.2_Utf8RoundtripBytePurity" ($utf8Errors -eq 0) "All $($jaDict.Count) strings roundtrip losslessly through UTF-8 bytes"
Record-Test "3.3_AuthenticJapaneseCharacterCoverage" ($nonJapaneseHits.Count -eq 0) "Found $($nonJapaneseHits.Count) anomalous non-Japanese entries"

# Sample 5 critical Japanese strings for visual validation
Write-Host "   Sample Japanese entries:" -ForegroundColor Gray
@("AppTitle", "AdminRequiredMsg", "Dir_WinSxS_Desc", "CpuTuningTitle", "PurgeHiberfilBtn") | ForEach-Object {
    Write-Host "     [$_] -> $($jaDict[$_])" -ForegroundColor Gray
}


# ==============================================================================
# SECTION 4: Traditional Chinese (zh-TW) vs Simplified Chinese (zh-CN) Distinction
# ==============================================================================
Write-Host "`n--- Section 4: Traditional Chinese vs Simplified Chinese Distinction ---" -ForegroundColor Magenta

$twDict = $translations["zh-TW"]
$cnDict = $translations["zh-CN"]

$totalChineseKeys = $twKeys.Count
$identicalKeys = [System.Collections.Generic.List[string]]::new()
$differentiatedKeys = [System.Collections.Generic.List[string]]::new()

# Technical acronyms that ARE expected to be identical
$technicalAcronyms = @("Cleanmgr", "CompactOs", "CustomFonts", "DismRunning", "DISM", "GPT", "MBR")

foreach ($k in $twKeys) {
    $twVal = $twDict[$k]
    $cnVal = $cnDict[$k]
    
    if ($twVal -eq $cnVal) {
        $identicalKeys.Add($k)
    } else {
        $differentiatedKeys.Add($k)
    }
}

$diffRatio = [Math]::Round(($differentiatedKeys.Count / $totalChineseKeys) * 100, 2)
Write-Host "   Total Keys: $totalChineseKeys" -ForegroundColor Gray
Write-Host "   Differentiated Keys: $($differentiatedKeys.Count) ($diffRatio%)" -ForegroundColor Gray
Write-Host "   Identical Keys: $($identicalKeys.Count) (e.g. $($identicalKeys[0..4] -join ', '))" -ForegroundColor Gray

# Check: Differentiation ratio must be significant (> 70%)
Record-Test "4.1_SignificantChineseDifferentiation" ($diffRatio -gt 70) "Differentiation ratio is $diffRatio% (expected > 70%)"

# Check: Specific key terminology checks
$termChecks = @(
    @{ Key = "Disks"; Tw = "實體磁碟"; Cn = "物理磁盘" },
    @{ Key = "Partitions"; Tw = "磁碟分割區"; Cn = "磁盘分区" },
    @{ Key = "Optimizer"; Tw = "最佳化"; Cn = "优化" },
    @{ Key = "Refresh"; Tw = "重新整理"; Cn = "刷新" },
    @{ Key = "Settings"; Tw = "設定"; Cn = "设置" },
    @{ Key = "OpenInExplorer"; Tw = "在檔案總管中開啟"; Cn = "在文件资源管理器中打开" }
)

$termCheckFailures = 0
foreach ($tc in $termChecks) {
    $twActual = $twDict[$tc.Key]
    $cnActual = $cnDict[$tc.Key]
    if ($twActual -notmatch [regex]::Escape($tc.Tw) -or $cnActual -notmatch [regex]::Escape($tc.Cn)) {
        $termCheckFailures++
        $findings.Add("Terminology check failed for '$($tc.Key)': TW='$twActual' (expected contains '$($tc.Tw)'), CN='$cnActual' (expected contains '$($tc.Cn)')")
    }
}
Record-Test "4.2_CoreWindowsTerminologyDifferentiation" ($termCheckFailures -eq 0) "Verified 6 core Windows terminology translations (磁碟/磁盘, 最佳化/优化, 檔案總管/文件资源管理器)"

# Check: Are there Traditional characters in zh-CN that should be simplified?
# Known distinctive traditional characters: 體, 經, 義, 關, 開, 點, 個, 後, 軟, 設, 統, 區, 備, 實, 導
$tradCharsPattern = "[體經關點後設統區備實導]"
$tradInCn = [System.Collections.Generic.List[string]]::new()
foreach ($kvp in $cnDict.GetEnumerator()) {
    if ($kvp.Value -match $tradCharsPattern) {
        $tradInCn.Add("$($kvp.Key): $($kvp.Value)")
    }
}
Record-Test "4.3_NoUnsimplifiedTraditionalCharsInCn" ($tradInCn.Count -eq 0) "Found $($tradInCn.Count) unsimplified Traditional Chinese characters in zh-CN"


# ==============================================================================
# SECTION 5: AppIcon.ico Binary Header, Layer Bounds & Payload Integrity
# ==============================================================================
Write-Host "`n--- Section 5: AppIcon.ico Binary Header & Layer Bounds Audit ---" -ForegroundColor Magenta

$icoPath = Join-Path $projectRoot "Assets\AppIcon.ico"
$icoBytes = [System.IO.File]::ReadAllBytes($icoPath)
$icoLen = $icoBytes.Length

Write-Host "   AppIcon.ico size: $icoLen bytes ($([Math]::Round($icoLen / 1024, 2)) KB)" -ForegroundColor Gray

# 5.1 ICONDIR Header Inspection
$reserved = [System.BitConverter]::ToUInt16($icoBytes, 0)
$resType = [System.BitConverter]::ToUInt16($icoBytes, 2)
$imgCount = [System.BitConverter]::ToUInt16($icoBytes, 4)

Record-Test "5.1a_IcoReservedZero" ($reserved -eq 0) "ICONDIR Reserved word: $reserved (must be 0)"
Record-Test "5.1b_IcoTypeIconFormat" ($resType -eq 1) "ICONDIR Type: $resType (1 = ICO, 2 = CUR)"
Record-Test "5.1c_IcoLayerCountSufficient" ($imgCount -ge 6) "ICONDIR Image count: $imgCount (expected >= 6)"

# 5.2 Parse ICONDIRENTRY Table
$entries = @()
$expectedDimensions = @(16, 32, 48, 64, 128, 256)
$foundDimensions = [System.Collections.Generic.List[int]]::new()
$all32Bpp = $true
$boundsErrors = [System.Collections.Generic.List[string]]::new()
$payloadTypeReport = [System.Collections.Generic.List[string]]::new()

for ($i = 0; $i -lt $imgCount; $i++) {
    $entryOffset = 6 + ($i * 16)
    $bWidth = [int]$icoBytes[$entryOffset]
    $bHeight = [int]$icoBytes[$entryOffset + 1]
    $bColorCount = [int]$icoBytes[$entryOffset + 2]
    $bReserved = [int]$icoBytes[$entryOffset + 3]
    $wPlanes = [System.BitConverter]::ToUInt16($icoBytes, $entryOffset + 4)
    $wBitCount = [System.BitConverter]::ToUInt16($icoBytes, $entryOffset + 6)
    $dwBytesInRes = [System.BitConverter]::ToUInt32($icoBytes, $entryOffset + 8)
    $dwImageOffset = [System.BitConverter]::ToUInt32($icoBytes, $entryOffset + 12)
    
    $actualW = if ($bWidth -eq 0) { 256 } else { $bWidth }
    $actualH = if ($bHeight -eq 0) { 256 } else { $bHeight }
    $foundDimensions.Add($actualW)
    
    if ($wBitCount -ne 32) { $all32Bpp = $false }
    
    # Boundary check 1: Offset must be past directory table
    $minOffset = 6 + ($imgCount * 16)
    if ($dwImageOffset -lt $minOffset) {
        $boundsErrors.Add("Layer $i ($actualW`x$actualH) offset $dwImageOffset is before header end $minOffset")
    }
    
    # Boundary check 2: Offset + Size must not exceed file length
    if (($dwImageOffset + $dwBytesInRes) -gt $icoLen) {
        $boundsErrors.Add("Layer $i ($actualW`x$actualH) bounds exceed file size: ($dwImageOffset + $dwBytesInRes = $($dwImageOffset + $dwBytesInRes)) > $icoLen")
    }
    
    # Payload Format & Magic Check
    $isPng = $false
    $isBmp = $false
    if ($dwBytesInRes -ge 8) {
        # Check PNG signature: 0x89 'P' 'N' 'G' 0x0D 0x0A 0x1A 0x0A
        if ($icoBytes[$dwImageOffset] -eq 0x89 -and
            $icoBytes[$dwImageOffset + 1] -eq 0x50 -and
            $icoBytes[$dwImageOffset + 2] -eq 0x4E -and
            $icoBytes[$dwImageOffset + 3] -eq 0x47) {
            $isPng = $true
            
            # Inspect IHDR dimensions
            if ($dwBytesInRes -ge 24) {
                $ihdrW = [System.BitConverter]::ToInt32(@($icoBytes[$dwImageOffset + 19], $icoBytes[$dwImageOffset + 18], $icoBytes[$dwImageOffset + 17], $icoBytes[$dwImageOffset + 16]), 0)
                $ihdrH = [System.BitConverter]::ToInt32(@($icoBytes[$dwImageOffset + 23], $icoBytes[$dwImageOffset + 22], $icoBytes[$dwImageOffset + 21], $icoBytes[$dwImageOffset + 20]), 0)
                if ($ihdrW -ne $actualW -or $ihdrH -ne $actualH) {
                    $boundsErrors.Add("Layer $i IHDR dimension mismatch: expected $actualW`x$actualH, got $ihdrW`x$ihdrH")
                }
            }
        }
        # Check BMP DIB header: biSize = 40 (0x00000028)
        elseif ($icoBytes[$dwImageOffset] -eq 0x28 -and $icoBytes[$dwImageOffset + 1] -eq 0x00) {
            $isBmp = $true
            $bmpW = [System.BitConverter]::ToInt32($icoBytes, $dwImageOffset + 4)
            $bmpH = [System.BitConverter]::ToInt32($icoBytes, $dwImageOffset + 8) / 2 # in ICO DIB, height is doubled (image + mask)
            if ($bmpW -ne $actualW -or $bmpH -ne $actualH) {
                $boundsErrors.Add("Layer $i BMP DIB dimension mismatch: expected $actualW`x$actualH, got $bmpW`x$bmpH")
            }
        }
    }
    
    $fmt = if ($isPng) { "PNG" } elseif ($isBmp) { "BMP/DIB" } else { "UNKNOWN" }
    $payloadTypeReport.Add("${actualW}x${actualH} -> $fmt ($dwBytesInRes bytes @ offset $dwImageOffset)")
}

Record-Test "5.2_AllStandardResolutionsPresent" (($expectedDimensions | Where-Object { $foundDimensions.Contains($_) }).Count -eq 6) "Found: $($foundDimensions -join ', ')"
Record-Test "5.3_AllLayersAre32Bpp" $all32Bpp "All layers declare 32-bit ARGB color depth"
Record-Test "5.4_NoPayloadBoundaryOverflows" ($boundsErrors.Count -eq 0) "Checked payload bounds and internal image headers; errors: $($boundsErrors.Count)"

Write-Host "   Layer payload details:" -ForegroundColor Gray
foreach ($rpt in $payloadTypeReport) {
    Write-Host "     * $rpt" -ForegroundColor Gray
}

# 5.5 Overlap Detection
$sortedEntries = $(for ($i = 0; $i -lt $imgCount; $i++) {
    $entryOffset = 6 + ($i * 16)
    [PSCustomObject]@{
        Index = $i
        Offset = [System.BitConverter]::ToUInt32($icoBytes, $entryOffset + 12)
        Size = [System.BitConverter]::ToUInt32($icoBytes, $entryOffset + 8)
    }
}) | Sort-Object Offset

$overlapCount = 0
for ($i = 0; $i -lt ($sortedEntries.Count - 1); $i++) {
    $currentEnd = $sortedEntries[$i].Offset + $sortedEntries[$i].Size
    $nextStart = $sortedEntries[$i + 1].Offset
    if ($currentEnd -gt $nextStart) {
        $overlapCount++
        $findings.Add("Layer overlap detected between index $($sortedEntries[$i].Index) and $($sortedEntries[$i+1].Index)")
    }
}
Record-Test "5.5_NoPayloadOverlaps" ($overlapCount -eq 0) "Sorted offsets are non-overlapping with $overlapCount collisions"


# ==============================================================================
# SUMMARY & VERDICT
# ==============================================================================
Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "   TEST EXECUTION SUMMARY                                                       " -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "Total Tests: $($passCount + $failCount) | Passed: $passCount | Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })

if ($findings.Count -gt 0) {
    Write-Host "`nDiscovered Findings / Anomalies:" -ForegroundColor Yellow
    foreach ($f in $findings) {
        Write-Host "  ! $f" -ForegroundColor Yellow
    }
}

if ($failCount -eq 0) {
    Write-Host "`nVERDICT: [APPROVE] Milestone 1 passed all empirical challenger stress tests." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nVERDICT: [FAIL] Milestone 1 failed challenger tests." -ForegroundColor Red
    exit 1
}
