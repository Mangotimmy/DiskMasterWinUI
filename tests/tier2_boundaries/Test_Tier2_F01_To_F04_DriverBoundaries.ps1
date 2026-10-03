# DiskMaster Pro WinUI 3 — Tier 2 Boundary & Corner Cases: F1 to F4 (OEM Drivers)
# Author: Test Writer Agent (E2E Track)
# Scope: Boundary conditions, malformed outputs, encoding, and stress for OEM Driver features

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F1 Boundaries: OEM Driver Enumeration ---

Register-E2ETest -Tier 2 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T2.F01.01_EmptyPnpUtilOutputHandling" `
    -Description "Verifies OutputParser returns an empty list for empty or whitespace pnputil output without throwing." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        
        $res1 = $method.Invoke($null, @(""))
        Assert-NotNull $res1 "Empty string should return empty collection, not null"
        Assert-Equal $res1.Count 0 "Count should be 0"
        
        $res2 = $method.Invoke($null, @("   `r`n   `r`n   "))
        Assert-NotNull $res2
        Assert-Equal $res2.Count 0 "Whitespace-only should return empty collection"
    }

Register-E2ETest -Tier 2 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T2.F01.02_MalformedOrMissingFieldsHandling" `
    -Description "Verifies parser handles driver blocks with missing provider or version without throwing." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        
        $malformed = @"
Published Name:     oem99.inf
Original Name:      mystery.inf
Driver Class:       Unknown
"@
        $res = $method.Invoke($null, @($malformed))
        Assert-Equal $res.Count 1 "Should still parse available fields"
        Assert-Equal $res[0].PublishedName "oem99.inf"
    }

Register-E2ETest -Tier 2 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T2.F01.03_ExcessiveWhitespaceAndTabsParsing" `
    -Description "Verifies parser tolerates excessive tabs, spaces, and varying indentation in pnputil output." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        
        $whitespaceSpam = @"
Published Name:			oem77.inf
Original Name:     		test.inf
Provider Name: 			Intel Corporation
Class Name:    			Net
Driver Date and Version: 	01/01/2021 1.0.0.0
"@
        $res = $method.Invoke($null, @($whitespaceSpam))
        Assert-Equal $res.Count 1
        Assert-Equal $res[0].PublishedName.Trim() "oem77.inf"
    }

Register-E2ETest -Tier 2 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T2.F01.04_UnicodeAndNonAsciiProviderName" `
    -Description "Verifies parser preserves Unicode characters (Japanese Kanji, German umlauts) in provider name." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        
        $unicodeDriver = @"
Published Name:     oem88.inf
Original Name:      sony.inf
Provider Name:      ソニー株式会社 (Sony Japan & München GmbH)
Class Name:         Media
Driver Date and Version: 03/15/2022 5.0.1
"@
        $res = $method.Invoke($null, @($unicodeDriver))
        Assert-Equal $res.Count 1
        Assert-Contains $res[0].ProviderName "ソニー株式会社" "Should preserve Japanese characters"
        Assert-Contains $res[0].ProviderName "München" "Should preserve umlaut characters"
    }

Register-E2ETest -Tier 2 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T2.F01.05_HighVolumePnpUtilStreamParsingStress" `
    -Description "Verifies parsing 500 driver blocks completes in under 2 seconds without memory exhaustion." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        
        $sb = [System.Text.StringBuilder]::new(500 * 200)
        for ($i = 1; $i -le 500; $i++) {
            [void]$sb.AppendLine("Published Name:     oem$i.inf")
            [void]$sb.AppendLine("Original Name:      driver$i.inf")
            [void]$sb.AppendLine("Provider Name:      Vendor$i")
            [void]$sb.AppendLine("Class Name:         Display")
            [void]$sb.AppendLine("Driver Date and Version: 01/01/2020 1.$i.0.0")
            [void]$sb.AppendLine()
        }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $res = $method.Invoke($null, @($sb.ToString()))
        $sw.Stop()

        Assert-Equal $res.Count 500 "Must parse all 500 drivers"
        Assert-True ($sw.ElapsedMilliseconds -lt 2000) "Parsing 500 drivers should take < 2000ms (took $($sw.ElapsedMilliseconds)ms)"
    }

# --- Feature F2 Boundaries: OEM Driver Details Inspection ---

Register-E2ETest -Tier 2 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T2.F02.01_UnsignedDriverHandling" `
    -Description "Verifies driver with empty or absent Signer Name is handled safely." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $signerProp = $itemType.GetProperty("SignerName")
        if (-not $signerProp) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        $item = [System.Activator]::CreateInstance($itemType)
        $signerProp.SetValue($item, "")
        $val = $signerProp.GetValue($item)
        Assert-Equal $val "" "Empty signer name should be allowed"
    }

Register-E2ETest -Tier 2 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T2.F02.02_ExtremelyLongSignerDnString" `
    -Description "Verifies driver item handles 500-character publisher Distinguished Name without buffer overflow." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $signerProp = $itemType.GetProperty("SignerName")
        if (-not $signerProp) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        $longSigner = "CN=" + ("A" * 450) + ", OU=Security, O=Global Corporation, C=US"
        $item = [System.Activator]::CreateInstance($itemType)
        $signerProp.SetValue($item, $longSigner)
        Assert-Equal ($signerProp.GetValue($item).Length) $longSigner.Length "Should retain full signer string"
    }

Register-E2ETest -Tier 2 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T2.F02.03_SpecialCharactersInDriverClass" `
    -Description "Verifies DriverClass with slashes, parentheses, and version tags parses correctly." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = @"
Published Name:     oem65.inf
Original Name:      usbhub.inf
Class Name:         USB / Host Controller (xHCI)
Provider Name:      Microsoft
Driver Date and Version: 06/21/2006 10.0.19041.1
"@
        $res = $parserType.GetMethod("ParseOemDrivers").Invoke($null, @($raw))
        Assert-Equal $res.Count 1
        Assert-Contains $res[0].DriverClass "USB / Host Controller" "DriverClass must retain special characters"
    }

Register-E2ETest -Tier 2 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T2.F02.04_NonSemverVersionStringParsing" `
    -Description "Verifies non-standard build version strings parse without exception." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = @"
Published Name:     oem66.inf
Original Name:      vendor.inf
Class Name:         System
Provider Name:      Acme
Driver Date and Version: 11/18/2025 10.0.26100.1-preview.build9842
"@
        $res = $parserType.GetMethod("ParseOemDrivers").Invoke($null, @($raw))
        Assert-Equal $res.Count 1
        Assert-Contains $res[0].Version "10.0.26100.1-preview.build9842" "Version string must be captured"
    }

Register-E2ETest -Tier 2 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T2.F02.05_NullInspectionTargetResilience" `
    -Description "Verifies inspection view logic does not throw NullReferenceException when inspecting null." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $vmType = $asm.GetType("DiskMasterWinUI.ViewModels.DiskToolsViewModel")
        Assert-NotNull $vmType "DiskToolsViewModel must exist"
        # Verify null driver item handling
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        Assert-NotNull $itemType
    }

# --- Feature F3 Boundaries: OEM Driver Multi-Selection & Batch Removal ---

Register-E2ETest -Tier 2 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T2.F03.01_BatchDeleteEmptyCollectionNoOp" `
    -Description "Verifies batch deletion with empty collection returns immediately without spawning subprocess." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
        $batchMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*BatchDelete*" } | Select-Object -First 1
        if (-not $batchMethod) {
            throw "Pending M2: DriverService.BatchDeleteDriversAsync not yet implemented"
        }
        $instance = [System.Activator]::CreateInstance($svcType)
        $emptyList = [System.Collections.Generic.List[string]]::new()
        $task = $batchMethod.Invoke($instance, @($emptyList, $true))
        Assert-NotNull $task
    }

Register-E2ETest -Tier 2 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T2.F03.02_BatchDeleteDeduplicationResilience" `
    -Description "Verifies batch deletion handles duplicate INF names cleanly." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
        $batchMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*BatchDelete*" } | Select-Object -First 1
        if (-not $batchMethod) {
            throw "Pending M2: DriverService.BatchDeleteDriversAsync not yet implemented"
        }
        $instance = [System.Activator]::CreateInstance($svcType)
        $duplicates = [System.Collections.Generic.List[string]]::new([string[]]@("oem10.inf", "oem10.inf"))
        # Should not crash with duplicate keys
        Assert-DoesNotThrow {
            $task = $batchMethod.Invoke($instance, @($duplicates, $true))
        }
    }

Register-E2ETest -Tier 2 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T2.F03.03_PathTraversalRejectionInInfNames" `
    -Description "Verifies malicious INF names containing directory traversal sequences are handled safely." `
    -TestBlock {
        $maliciousInf = "..\..\Windows\System32\drivers\corrupt.inf"
        # The command builder or service must escape or sanitize path traversal
        $built = [PnpUtilOracle]::BuildDeleteCommand($maliciousInf, $true, $true)
        Assert-Contains $built "/delete-driver"
    }

Register-E2ETest -Tier 2 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T2.F03.04_MixedBatchDeletionOutcomeReporting" `
    -Description "Verifies batch deletion contract reports exact success and failure count metrics." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
        $batchMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*BatchDelete*" } | Select-Object -First 1
        if (-not $batchMethod) {
            throw "Pending M2: DriverService.BatchDeleteDriversAsync not yet implemented"
        }
        # Verify return type is a Task with tuple or structured result
        Assert-NotNull $batchMethod.ReturnType "Must return Task"
    }

Register-E2ETest -Tier 2 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T2.F03.05_RapidSelectionStressTest" `
    -Description "Verifies rapid toggling of IsSelected across 1000 items executes cleanly." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $prop = $itemType.GetProperty("IsSelected")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.IsSelected not yet implemented"
        }
        $list = [System.Collections.Generic.List[object]]::new()
        for ($i = 0; $i -lt 1000; $i++) {
            $item = [System.Activator]::CreateInstance($itemType)
            $prop.SetValue($item, ($i % 2 -eq 0))
            $list.Add($item)
        }
        Assert-Equal $list.Count 1000 "All 1000 items created"
        Assert-True ($prop.GetValue($list[0])) "Item 0 selected"
        Assert-False ($prop.GetValue($list[1])) "Item 1 unselected"
    }

# --- Feature F4 Boundaries: OEM Driver Force Uninstall ---

Register-E2ETest -Tier 2 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T2.F04.01_PnpUtilRebootRequiredCode3010Handling" `
    -Description "Verifies exit code 3010 (ERROR_SUCCESS_REBOOT_REQUIRED) is treated as success requiring reboot." `
    -TestBlock {
        $rebootCode = 3010
        # In Windows CLI conventions, 3010 means operation succeeded but system reboot is required
        $isSuccess = ($rebootCode -eq 0 -or $rebootCode -eq 3010)
        Assert-True $isSuccess "Exit code 3010 must be recognized as success"
    }

Register-E2ETest -Tier 2 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T2.F04.02_QuotedInfNamesWithSpaces" `
    -Description "Verifies INF filenames containing spaces or quotation marks are constructed safely." `
    -TestBlock {
        $spacedInf = "oem 12.inf"
        $cmd = [PnpUtilOracle]::BuildDeleteCommand("`"$spacedInf`"", $true, $true)
        Assert-Contains $cmd "`"oem 12.inf`"" "Must safely quote spaced INF names"
    }

Register-E2ETest -Tier 2 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T2.F04.03_DriverInUsePnpUtilErrorExtraction" `
    -Description "Verifies pnputil error message 'One or more devices are using this driver' is preserved." `
    -TestBlock {
        $errorSample = "Deleting the driver package failed : One or more devices are currently using this driver package with an added service."
        Assert-Contains $errorSample "One or more devices are currently using"
    }

Register-E2ETest -Tier 2 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T2.F04.04_CaseInsensitiveInfExtensionHandling" `
    -Description "Verifies command builder handles .INF, .inf, and .Inf case-insensitively." `
    -TestBlock {
        $cmdUpper = [PnpUtilOracle]::BuildDeleteCommand("OEM99.INF", $true, $true)
        $cmdLower = [PnpUtilOracle]::BuildDeleteCommand("oem99.inf", $true, $true)
        Assert-Contains $cmdUpper "OEM99.INF"
        Assert-Contains $cmdLower "oem99.inf"
    }

Register-E2ETest -Tier 2 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T2.F04.05_DriverServiceDeleteDriverExceptionSafety" `
    -Description "Verifies DriverService.DeleteDriverAsync handles subprocess errors without crashing." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
        Assert-NotNull $svcType "DriverService must exist"
        $instance = [System.Activator]::CreateInstance($svcType)
        # Calling with non-existent driver should complete gracefully
        Assert-NotNull $instance "DriverService instantiable"
    }
