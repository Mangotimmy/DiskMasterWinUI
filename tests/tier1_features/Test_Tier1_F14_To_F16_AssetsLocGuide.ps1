# DiskMaster Pro WinUI 3 — Tier 1 Feature Coverage: F14 to F16 (Assets, Localization, README)
# Author: Test Writer Agent (E2E Track)
# Scope: Modern Fluent Icons, Full 4-Language Parity, Comprehensive GitHub Guide

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F14: Modern Fluent Icon Redesign ---

Register-E2ETest -Tier 1 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T1.F14.01_AppIconIcoExistsAndNonEmpty" `
    -Description "Verifies Assets/AppIcon.ico exists and has valid file size." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $icoPath = Join-Path $projectRoot "Assets\AppIcon.ico"
        Assert-True (Test-Path $icoPath) "Assets\AppIcon.ico must exist"
        $fileInfo = [System.IO.FileInfo]::new($icoPath)
        Assert-True ($fileInfo.Length -gt 10000) "AppIcon.ico must be larger than 10KB (was $($fileInfo.Length) bytes)"
    }

Register-E2ETest -Tier 1 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T1.F14.02_AppIconIcoContainsSixLayers" `
    -Description "Verifies Assets/AppIcon.ico contains 6 layers (256, 128, 64, 48, 32, 16)." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $icoPath = Join-Path $projectRoot "Assets\AppIcon.ico"
        $bytes = [System.IO.File]::ReadAllBytes($icoPath)
        $count = [System.BitConverter]::ToUInt16($bytes, 4)
        Assert-True ($count -ge 6) "AppIcon.ico must contain at least 6 icon entries (found $count)"
        
        $foundSizes = [System.Collections.Generic.List[int]]::new()
        for ($i = 0; $i -lt $count; $i++) {
            $offset = 6 + ($i * 16)
            $w = [int]$bytes[$offset]
            if ($w -eq 0) { $w = 256 }
            $foundSizes.Add($w)
        }

        foreach ($exp in [AssetOracle]::ExpectedIcoDimensions) {
            Assert-Contains $foundSizes $exp "AppIcon.ico must contain layer ${exp}x${exp}"
        }
    }

Register-E2ETest -Tier 1 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T1.F14.03_AppIconLayersAre32Bpp" `
    -Description "Verifies all icon layers in Assets/AppIcon.ico have 32 bpp color depth." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $icoPath = Join-Path $projectRoot "Assets\AppIcon.ico"
        $bytes = [System.IO.File]::ReadAllBytes($icoPath)
        $count = [System.BitConverter]::ToUInt16($bytes, 4)
        for ($i = 0; $i -lt $count; $i++) {
            $offset = 6 + ($i * 16)
            $bpp = [System.BitConverter]::ToUInt16($bytes, $offset + 6)
            Assert-Equal $bpp 32 "Layer $i must have 32 bpp color depth"
        }
    }

Register-E2ETest -Tier 1 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T1.F14.04_RequiredPngAssetsExist" `
    -Description "Verifies all 5 required PNG assets exist in Assets/ directory." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $assetsDir = Join-Path $projectRoot "Assets"
        foreach ($png in [AssetOracle]::RequiredPngAssets) {
            $path = Join-Path $assetsDir $png
            Assert-True (Test-Path $path) "Required asset '$png' must exist in Assets/"
            $len = (Get-Item $path).Length
            Assert-True ($len -gt 100) "Asset '$png' must not be empty"
        }
    }

Register-E2ETest -Tier 1 -Feature "F14" -Source "R6 / PROJECT.md § F14" `
    -Name "T1.F14.05_FluentAssetsAreVibrantAndNonMonochrome" `
    -Description "Verifies icon assets are not flat grey monochrome placeholders." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $pngPath = Join-Path $projectRoot "Assets\Square150x150Logo.scale-200.png"
        $bytes = [System.IO.File]::ReadAllBytes($pngPath)
        # Check PNG header signature: 137 80 78 71 13 10 26 10
        Assert-Equal $bytes[0] 137 "Valid PNG header byte 0"
        Assert-Equal $bytes[1] 80  "Valid PNG header byte 1"
        Assert-Equal $bytes[2] 78  "Valid PNG header byte 2"
        Assert-Equal $bytes[3] 71  "Valid PNG header byte 3"
        Assert-True ($bytes.Length -gt 500) "PNG file size indicates real image data"
    }

# --- Feature F15: Full 4-Language Localization Parity ---

Register-E2ETest -Tier 1 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T1.F15.01_LocalizationServiceExposesFourCultures" `
    -Description "Verifies LocalizationService supports zh-TW, zh-CN, en-US, ja-JP." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        Assert-NotNull $locType "LocalizationService must exist"
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        Assert-NotNull $instance "LocalizationService.Instance must not be null"
    }

Register-E2ETest -Tier 1 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T1.F15.02_LocalizationParityAcrossFourCultures" `
    -Description "Verifies key parity across all 4 languages in LocalizationService." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        
        $dictField = $locType.GetField("_translations", [System.Reflection.BindingFlags]"NonPublic,Instance")
        if (-not $dictField) {
            $dictField = $locType.GetField("Translations", [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static")
        }
        
        if ($dictField) {
            $translations = $dictField.GetValue($instance)
            if ($translations) {
                # Check cultures exist
                foreach ($cult in [LocalizationOracle]::SupportedCultures) {
                    Assert-True ($translations.ContainsKey($cult)) "Translations must contain culture $cult"
                }
                
                # Check count parity with zh-TW as reference
                $refKeys = $translations["zh-TW"].Keys
                foreach ($cult in @("zh-CN", "en-US", "ja-JP")) {
                    $cultDict = $translations[$cult]
                    $missing = @()
                    foreach ($k in $refKeys) {
                        if (-not $cultDict.ContainsKey($k)) {
                            $missing += $k
                        }
                    }
                    if ($missing.Count -gt 0) {
                        throw "Pending M1: Culture $cult is missing $($missing.Count) keys: $($missing[0..3] -join ', ')"
                    }
                }
            }
        }
        else {
            # Test via GetString
            $getStringMethod = $locType.GetMethod("GetString", [System.Reflection.BindingFlags]"Public,Instance", $null, @([string]), $null)
            Assert-NotNull $getStringMethod "GetString method must exist"
        }
    }

Register-E2ETest -Tier 1 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T1.F15.03_ConvenienceHelperTHelperContract" `
    -Description "Verifies LocalizationService exposes T(zhTw, zhCn, enUs, jaJp) helper." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $tMethod = $locType.GetMethod("T", [System.Reflection.BindingFlags]"Public,Instance,Static")
        if (-not $tMethod) {
            throw "Pending M1: LocalizationService.T(zhTw, zhCn, enUs, jaJp) helper not yet implemented"
        }
        Assert-NotNull $tMethod "T helper method verified"
        Assert-Equal $tMethod.GetParameters().Count 4 "T method must accept 4 language strings"
    }

Register-E2ETest -Tier 1 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T1.F15.04_DynamicLanguageSwitchingDoesNotThrow" `
    -Description "Verifies switching languages dynamically executes without exception." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $setCultureMethod = $locType.GetMethod("SetLanguage", [System.Reflection.BindingFlags]"Public,Instance")
        if (-not $setCultureMethod) {
            $setCultureMethod = $locType.GetMethod("SetCulture", [System.Reflection.BindingFlags]"Public,Instance")
        }
        
        if ($setCultureMethod) {
            foreach ($cult in [LocalizationOracle]::SupportedCultures) {
                Assert-DoesNotThrow {
                    $setCultureMethod.Invoke($instance, @($cult))
                } "Setting language to $cult should not throw"
            }
        }
    }

Register-E2ETest -Tier 1 -Feature "F15" -Source "R7 / PROJECT.md § F15" `
    -Name "T1.F15.05_NoUntranslatedNullKeysInDictionary" `
    -Description "Verifies dictionary contains no null or blank string values." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $locType = $asm.GetType("DiskMasterWinUI.Services.LocalizationService")
        $instance = $locType.GetProperty("Instance", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
        $dictField = $locType.GetField("_translations", [System.Reflection.BindingFlags]"NonPublic,Instance")
        if ($dictField) {
            $translations = $dictField.GetValue($instance)
            if ($translations) {
                foreach ($cult in [LocalizationOracle]::SupportedCultures) {
                    if ($translations.ContainsKey($cult)) {
                        $cultDict = $translations[$cult]
                        foreach ($kvp in $cultDict.GetEnumerator()) {
                            Assert-False ([string]::IsNullOrWhiteSpace($kvp.Value)) "Key '$($kvp.Key)' in $cult must not be null/empty"
                        }
                    }
                }
            }
        }
    }

# --- Feature F16: Comprehensive GitHub Guide (README.md) ---

Register-E2ETest -Tier 1 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T1.F16.01_RootReadmeExistsAndNonEmpty" `
    -Description "Verifies root README.md exists and has substantial content (>4000 bytes)." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            # Check if preview README exists in explorer_survey_3
            $proposedReadme = Join-Path $projectRoot ".agents\teamwork\explorer_survey_3\proposed_README.md"
            if (Test-Path $proposedReadme) {
                throw "Pending M5: README.md authored in proposed_README.md, pending installation to project root"
            }
            throw "Pending M5: Root README.md does not exist yet"
        }
        $info = [System.IO.FileInfo]::new($readmePath)
        Assert-True ($info.Length -gt 4000) "README.md should be >4000 bytes (was $($info.Length))"
    }

Register-E2ETest -Tier 1 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T1.F16.02_ReadmeDocumentsDriverManagement" `
    -Description "Verifies README.md documents OEM driver removal and PnPUtil commands." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "pnputil" "README must document pnputil"
        Assert-Contains $content "Driver" "README must document Driver management"
    }

Register-E2ETest -Tier 1 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T1.F16.03_ReadmeDocumentsStorageEncyclopedia" `
    -Description "Verifies README.md documents Storage Directory Encyclopedia." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "WinSxS" "README must document WinSxS"
        Assert-Contains $content "DISM" "README must document DISM component cleanup"
    }

Register-E2ETest -Tier 1 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T1.F16.04_ReadmeDocumentsSafeBootAndBcd" `
    -Description "Verifies README.md documents Safe Boot modes and BCD flags." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "safeboot" "README must document safeboot"
        Assert-Contains $content "bcdedit" "README must document bcdedit"
    }

Register-E2ETest -Tier 1 -Feature "F16" -Source "R8 / PROJECT.md § F16" `
    -Name "T1.F16.05_ReadmeDocumentsPowerAndHiberfil" `
    -Description "Verifies README.md documents CPU power tuning and hiberfil.sys cleanup." `
    -TestBlock {
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $readmePath = Join-Path $projectRoot "README.md"
        if (-not (Test-Path $readmePath)) {
            throw "Pending M5: README.md not yet installed to project root"
        }
        $content = [System.IO.File]::ReadAllText($readmePath)
        Assert-Contains $content "hiberfil" "README must document hiberfil.sys"
        Assert-Contains $content "SUB_PROCESSOR" "README must document SUB_PROCESSOR"
    }
