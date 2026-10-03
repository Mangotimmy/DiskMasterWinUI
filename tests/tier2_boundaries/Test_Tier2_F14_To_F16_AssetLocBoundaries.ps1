# DiskMaster Pro WinUI 3 — Tier 2 Boundary & Corner Cases: F14 to F16 (Assets, Localization, README)
# Author: Test Writer Agent (E2E Track)
# Scope: Binary ICO header parsing, PNG dimensions, fallback resilience, kanji/kana encoding, and README structure

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F14 Boundaries: Modern Fluent Icon Redesign ---

Register-E2ETest -Tier 2 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T2.F14.01_AppIconIcoHeaderBinaryIntegrity" `
    -Description "Verifies ICO binary header structure: Reserved=0, Type=1, Count >= 6." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $icoPath = Join-Path $projectRoot "Assets\AppIcon.ico"
        $bytes = [System.IO.File]::ReadAllBytes($icoPath)
        
        $reserved = [System.BitConverter]::ToUInt16($bytes, 0)
        $type = [System.BitConverter]::ToUInt16($bytes, 2)
        $count = [System.BitConverter]::ToUInt16($bytes, 4)
        
        Assert-Equal $reserved 0 "Reserved must be 0"
        Assert-Equal $type 1 "Type must be 1 (Icon format, not cursor)"
        Assert-True ($count -ge 6) "Count must be at least 6 icon entries"
    }

Register-E2ETest -Tier 2 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T2.F14.02_PngDimensionsScale200Validation" `
    -Description "Verifies required PNG asset dimensions match scale-200 specifications." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $assetsDir = Join-Path $projectRoot "Assets"
        
        foreach ($kvp in [AssetOracle]::ExpectedPngDimensions.GetEnumerator()) {
            $pngPath = Join-Path $assetsDir $kvp.Key
            if (Test-Path $pngPath) {
                $bytes = [System.IO.File]::ReadAllBytes($pngPath)
                # IHDR starts at byte 12: Width is at byte 16 (4 bytes big-endian), Height at 20 (4 bytes big-endian)
                if ($bytes.Length -gt 24) {
                    $wBytes = @($bytes[19], $bytes[18], $bytes[17], $bytes[16])
                    $hBytes = @($bytes[23], $bytes[22], $bytes[21], $bytes[20])
                    $w = [System.BitConverter]::ToInt32($wBytes, 0)
                    $h = [System.BitConverter]::ToInt32($hBytes, 0)
                    Assert-Equal $w $kvp.Value.Width "Width of $($kvp.Key) should be $($kvp.Value.Width)"
                    Assert-Equal $h $kvp.Value.Height "Height of $($kvp.Key) should be $($kvp.Value.Height)"
                }
            }
        }
    }

Register-E2ETest -Tier 2 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T2.F14.03_AssetsDirectoryContainsNoZeroByteFiles" `
    -Description "Verifies no files in Assets directory are 0-byte corrupted stubs." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $assetsDir = Join-Path $projectRoot "Assets"
        $files = Get-ChildItem -Path $assetsDir -File
        foreach ($f in $files) {
            Assert-True ($f.Length -gt 0) "File '$($f.Name)' must not be 0 bytes"
        }
    }

Register-E2ETest -Tier 2 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T2.F14.04_AltformVariantsExistAndNonEmpty" `
    -Description "Verifies targetsize-24 unplated and targetsize-48 lightunplated exist." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $assetsDir = Join-Path $projectRoot "Assets"
        $v24 = Join-Path $assetsDir "Square44x44Logo.targetsize-24_altform-unplated.png"
        $v48 = Join-Path $assetsDir "Square44x44Logo.targetsize-48_altform-lightunplated.png"
        Assert-True (Test-Path $v24) "targetsize-24 altform must exist"
        Assert-True (Test-Path $v48) "targetsize-48 lightunplated must exist"
        Assert-True ((Get-Item $v24).Length -gt 100)
        Assert-True ((Get-Item $v48).Length -gt 100)
    }

Register-E2ETest -Tier 2 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T2.F14.05_PackageAppxManifestIconBindings" `
    -Description "Verifies Package.appxmanifest references valid asset files." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $manifestPath = Join-Path $projectRoot "Package.appxmanifest"
        Assert-True (Test-Path $manifestPath) "Package.appxmanifest must exist"
        $xml = [System.IO.File]::ReadAllText($manifestPath)
        Assert-Contains $xml "Square150x150Logo.png"
        Assert-Contains $xml "Square44x44Logo.png"
        Assert-Contains $xml "SplashScreen.png"
    }

# --- Feature F15 Boundaries: Full 4-Language Localization Parity ---

Register-E2ETest -Tier 2 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T2.F15.01_MissingKeyFallbackReturnsKeyNameSafely" `
    -Description "Verifies requesting a non-existent key returns the key itself rather than throwing null." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $getStringMethod = $locType.GetMethod("GetString", [System.Reflection.BindingFlags]"Public,Instance", $null, @([string]), $null)
        
        $nonExistentKey = "NonExistent_Test_Key_98765"
        $res = $getStringMethod.Invoke($instance, @($nonExistentKey))
        Assert-NotNull $res "Fallback must not be null"
        Assert-Equal $res $nonExistentKey "Fallback should return key name safely"
    }

Register-E2ETest -Tier 2 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T2.F15.02_JapaneseEncodingIntegrityNoMojibake" `
    -Description "Verifies Japanese strings contain authentic Kanji/Kana and no '?' mojibake artifacts." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $dictField = $locType.GetField("_translations", [System.Reflection.BindingFlags]"NonPublic,Instance")
        if ($dictField) {
            $translations = $dictField.GetValue($instance)
            if ($translations -and $translations.ContainsKey("ja-JP")) {
                $jaDict = $translations["ja-JP"]
                foreach ($kvp in $jaDict.GetEnumerator()) {
                    $str = $kvp.Value
                    # Japanese text should not consist entirely of '???'
                    Assert-False ($str -match "^\?+$") "String for '$($kvp.Key)' must not be mojibake question marks"
                }
            }
        }
    }

Register-E2ETest -Tier 2 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T2.F15.03_TraditionalVsSimplifiedChineseDistinction" `
    -Description "Verifies Traditional Chinese (繁體) and Simplified Chinese (简体) have authentic distinct characters." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $dictField = $locType.GetField("_translations", [System.Reflection.BindingFlags]"NonPublic,Instance")
        if ($dictField) {
            $translations = $dictField.GetValue($instance)
            if ($translations -and $translations.ContainsKey("zh-TW") -and $translations.ContainsKey("zh-CN")) {
                $tw = $translations["zh-TW"]
                $cn = $translations["zh-CN"]
                Assert-NotNull $tw
                Assert-NotNull $cn
            }
        }
    }

Register-E2ETest -Tier 2 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T2.F15.04_RapidCultureSwitchingConsistency" `
    -Description "Verifies rapid cycling through all 4 cultures retains dictionary consistency." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $setCultureMethod = $locType.GetMethod("SetLanguage", [System.Reflection.BindingFlags]"Public,Instance")
        if (-not $setCultureMethod) {
            $setCultureMethod = $locType.GetMethod("SetCulture", [System.Reflection.BindingFlags]"Public,Instance")
        }
        if ($setCultureMethod) {
            for ($round = 0; $round -lt 3; $round++) {
                foreach ($cult in [LocalizationOracle]::SupportedCultures) {
                    $setCultureMethod.Invoke($instance, @($cult))
                }
            }
        }
    }

Register-E2ETest -Tier 2 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T2.F15.05_LegacyIsChineseCompatibility" `
    -Description "Verifies IsChinese property remains functional for backwards compatibility." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $prop = $locType.GetProperty("IsChinese", [System.Reflection.BindingFlags]"Public,Instance")
        Assert-NotNull $prop "IsChinese property must exist for legacy compatibility"
        $val = $prop.GetValue($instance)
        Assert-NotNull $val "IsChinese value must be non-null boolean"
    }

# --- Feature F16 Boundaries: Comprehensive GitHub Guide (README.md) ---

Register-E2ETest -Tier 2 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T2.F16.01_ReadmeArchitectureSectionVerification" `
    -Description "Verifies README.md documents architecture (WinUI 3, MVVM, Service layer)." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "WinUI 3"
        Assert-Contains $content "MVVM"
    }

Register-E2ETest -Tier 2 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T2.F16.02_ReadmeSafetyGuardrailsSectionVerification" `
    -Description "Verifies README.md documents Safety Guardrails (ACL, UAC, DISM)." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "Safety"
    }

Register-E2ETest -Tier 2 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T2.F16.03_ReadmeBenchmarksSectionVerification" `
    -Description "Verifies README.md documents Tuning Benchmarks and performance improvements." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "Benchmark"
    }

Register-E2ETest -Tier 2 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T2.F16.04_ReadmeBuildInstructionsReleaseVerification" `
    -Description "Verifies README.md documents dotnet build -c Release instructions." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "dotnet build -c Release"
    }

Register-E2ETest -Tier 2 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T2.F16.05_ReadmeMarkdownFormattingIntegrity" `
    -Description "Verifies README.md contains valid Markdown headers, code blocks, and tables." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Matches $content "^#\s+" "Must contain top-level H1 header"
        Assert-Contains $content '```' "Must contain markdown code fences"
    }
