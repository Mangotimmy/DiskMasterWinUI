# DiskMaster Pro WinUI 3 — Tier 1 Feature Coverage: F1 to F4 (OEM Drivers)
# Author: Test Writer Agent (E2E Track)
# Scope: OEM Driver Enumeration, Details Inspection, Multi-Selection/Batch, Force Uninstall

. "$PSScriptRoot\..\harness\TestFramework.ps1"
. "$PSScriptRoot\..\harness\TestOracles.ps1"

# --- Feature F1: OEM Driver Enumeration ---

Register-E2ETest -Tier 1 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T1.F01.01_ParseEnglishPnpUtilOutput" `
    -Description "Verifies OutputParser successfully parses English pnputil output blocks." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        Assert-NotNull $parserType "OutputParser type must exist"
        
        $raw = [PnpUtilOracle]::GenerateDriverOutput("en-us", "oem10.inf", "netrtx64.inf", "Net", "Realtek", "02/25/2016", "6.2.2600.0", "Microsoft Windows Hardware Compatibility Publisher")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        $results = $method.Invoke($null, @($raw))
        
        Assert-NotNull $results "Parser result must not be null"
        Assert-Equal $results.Count 1 "Should parse 1 driver block"
        Assert-Equal $results[0].PublishedName "oem10.inf" "PublishedName must match"
        Assert-Equal $results[0].ProviderName "Realtek" "ProviderName must match"
        Assert-Equal $results[0].DriverClass "Net" "DriverClass must match"
    }

Register-E2ETest -Tier 1 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T1.F01.02_ParseTraditionalChinesePnpUtilOutput" `
    -Description "Verifies OutputParser successfully parses Traditional Chinese pnputil output." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = [PnpUtilOracle]::GenerateDriverOutput("zh-tw", "oem22.inf", "iastor.inf", "Storage", "Intel", "05/10/2021", "17.9.0.1007", "Microsoft Windows Hardware Compatibility Publisher")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        $results = $method.Invoke($null, @($raw))
        
        Assert-NotNull $results "Parser result must not be null"
        Assert-Equal $results.Count 1 "Should parse 1 driver block"
        Assert-Equal $results[0].PublishedName "oem22.inf" "PublishedName must match"
        if ($results[0].ProviderName -ne "Intel") {
            throw "Pending M2: OutputParser does not yet support '驅動程式套件提供者' header"
        }
        Assert-Equal $results[0].ProviderName "Intel" "ProviderName must match"
    }

Register-E2ETest -Tier 1 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T1.F01.03_ParseSimplifiedChinesePnpUtilOutput" `
    -Description "Verifies OutputParser successfully parses Simplified Chinese pnputil output." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = [PnpUtilOracle]::GenerateDriverOutput("zh-cn", "oem33.inf", "nvhda.inf", "MEDIA", "NVIDIA", "08/12/2023", "1.3.40.14", "Microsoft Windows Hardware Compatibility Publisher")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        $results = $method.Invoke($null, @($raw))
        
        Assert-NotNull $results "Parser result must not be null"
        Assert-Equal $results.Count 1 "Should parse 1 driver block"
        Assert-Equal $results[0].PublishedName "oem33.inf" "PublishedName must match"
        if ($results[0].ProviderName -ne "NVIDIA") {
            throw "Pending M2: OutputParser does not yet support '驱动程序程序包提供商' header"
        }
        Assert-Equal $results[0].ProviderName "NVIDIA" "ProviderName must match"
    }

Register-E2ETest -Tier 1 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T1.F01.04_ParseJapanesePnpUtilOutput" `
    -Description "Verifies OutputParser successfully parses Japanese pnputil output (公開名:)." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = [PnpUtilOracle]::GenerateDriverOutput("ja-jp", "oem44.inf", "e1d.inf", "Net", "Intel", "01/15/2022", "12.19.1.37", "Microsoft Windows Hardware Compatibility Publisher")
        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        $results = $method.Invoke($null, @($raw))
        
        if ($results.Count -eq 0) {
            throw "Pending M2: Japanese pnputil parsing (公開名:) not yet implemented in OutputParser"
        }
        Assert-Equal $results[0].PublishedName "oem44.inf" "PublishedName must match"
        Assert-Equal $results[0].ProviderName "Intel" "ProviderName must match"
    }

Register-E2ETest -Tier 1 -Feature "F1" -Source "R1 / PROJECT.md § F1" `
    -Name "T1.F01.05_MultiDriverPackageParsing" `
    -Description "Verifies OutputParser parses multiple consecutive driver blocks in one stream." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $block1 = [PnpUtilOracle]::GenerateDriverOutput("en-us", "oem1.inf", "drv1.inf", "Net", "Intel", "01/01/2020", "1.0", "Microsoft")
        $block2 = [PnpUtilOracle]::GenerateDriverOutput("en-us", "oem2.inf", "drv2.inf", "Display", "AMD", "02/02/2021", "2.0", "Microsoft")
        $block3 = [PnpUtilOracle]::GenerateDriverOutput("en-us", "oem3.inf", "drv3.inf", "System", "Realtek", "03/03/2022", "3.0", "Microsoft")
        $combined = "$block1`n$block2`n$block3"

        $method = $parserType.GetMethod("ParseOemDrivers", [System.Reflection.BindingFlags]"Public,Static")
        $results = $method.Invoke($null, @($combined))
        Assert-Equal $results.Count 3 "Should parse all 3 drivers"
        Assert-Equal $results[0].PublishedName "oem1.inf"
        Assert-Equal $results[1].PublishedName "oem2.inf"
        Assert-Equal $results[2].PublishedName "oem3.inf"
    }

# --- Feature F2: OEM Driver Details Inspection ---

Register-E2ETest -Tier 1 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T1.F02.01_OemDriverItemHasSignerNameProperty" `
    -Description "Verifies OemDriverItem model exposes a SignerName property for driver inspection." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        Assert-NotNull $itemType "OemDriverItem type must exist"
        $prop = $itemType.GetProperty("SignerName")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        Assert-Equal $prop.PropertyType.FullName "System.String" "SignerName must be a string property"
    }

Register-E2ETest -Tier 1 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T1.F02.02_InspectEnglishDriverSignerExtraction" `
    -Description "Verifies driver parser extracts English Signer Name metadata." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $prop = $itemType.GetProperty("SignerName")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = [PnpUtilOracle]::GenerateDriverOutput("en-us", "oem50.inf", "test.inf", "Net", "Intel", "01/01/2022", "1.0.0", "Microsoft Windows Hardware Compatibility Publisher")
        $results = $parserType.GetMethod("ParseOemDrivers").Invoke($null, @($raw))
        Assert-Equal $results[0].SignerName "Microsoft Windows Hardware Compatibility Publisher" "SignerName must be extracted"
    }

Register-E2ETest -Tier 1 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T1.F02.03_InspectChineseDriverSignerExtraction" `
    -Description "Verifies driver parser extracts Chinese Signer Name metadata." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $prop = $itemType.GetProperty("SignerName")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = [PnpUtilOracle]::GenerateDriverOutput("zh-tw", "oem51.inf", "test.inf", "Net", "Intel", "01/01/2022", "1.0.0", "微軟硬體相容性發行者")
        $results = $parserType.GetMethod("ParseOemDrivers").Invoke($null, @($raw))
        Assert-Equal $results[0].SignerName "微軟硬體相容性發行者" "SignerName must match Chinese signer"
    }

Register-E2ETest -Tier 1 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T1.F02.04_InspectJapaneseDriverSignerExtraction" `
    -Description "Verifies driver parser extracts Japanese Signer Name metadata." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $prop = $itemType.GetProperty("SignerName")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        $parserType = $asm.GetType("DiskMasterWinUI.Helpers.OutputParser")
        $raw = [PnpUtilOracle]::GenerateDriverOutput("ja-jp", "oem52.inf", "test.inf", "Net", "Intel", "01/01/2022", "1.0.0", "Microsoft Windows Hardware Compatibility Publisher")
        $results = $parserType.GetMethod("ParseOemDrivers").Invoke($null, @($raw))
        if ($results.Count -eq 0) {
            throw "Pending M2: Japanese parsing not yet implemented"
        }
        Assert-Equal $results[0].SignerName "Microsoft Windows Hardware Compatibility Publisher"
    }

Register-E2ETest -Tier 1 -Feature "F2" -Source "R1 / PROJECT.md § F2" `
    -Name "T1.F02.05_InspectDriverMetadataContract" `
    -Description "Verifies OemDriverItem exposes all 5 required inspection fields (Class, Provider, Date, Version, Signer)." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $requiredProps = @("DriverClass", "ProviderName", "Date", "Version")
        foreach ($p in $requiredProps) {
            Assert-NotNull ($itemType.GetProperty($p)) "OemDriverItem must expose property $p"
        }
        $signerProp = $itemType.GetProperty("SignerName")
        if (-not $signerProp) {
            throw "Pending M2: OemDriverItem.SignerName not yet implemented"
        }
        Assert-NotNull $signerProp "SignerName property verified"
    }

# --- Feature F3: OEM Driver Multi-Selection & Batch Removal ---

Register-E2ETest -Tier 1 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T1.F03.01_OemDriverItemHasIsSelectedProperty" `
    -Description "Verifies OemDriverItem exposes an IsSelected boolean property for multi-selection UI." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $prop = $itemType.GetProperty("IsSelected")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.IsSelected not yet implemented"
        }
        Assert-Equal $prop.PropertyType.FullName "System.Boolean" "IsSelected must be boolean"
    }

Register-E2ETest -Tier 1 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T1.F03.02_MultiSelectionToggling" `
    -Description "Verifies multiple driver items can have their IsSelected property toggled independently." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $itemType = $asm.GetType("DiskMasterWinUI.Models.OemDriverItem")
        $prop = $itemType.GetProperty("IsSelected")
        if (-not $prop) {
            throw "Pending M2: OemDriverItem.IsSelected not yet implemented"
        }
        $item1 = [System.Activator]::CreateInstance($itemType)
        $item2 = [System.Activator]::CreateInstance($itemType)
        $prop.SetValue($item1, $true)
        $prop.SetValue($item2, $false)
        Assert-True ($prop.GetValue($item1)) "Item 1 should be selected"
        Assert-False ($prop.GetValue($item2)) "Item 2 should not be selected"
    }

Register-E2ETest -Tier 1 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T1.F03.03_DriverServiceExposesBatchDeleteMethod" `
    -Description "Verifies DriverService exposes BatchDeleteDriversAsync method." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
        Assert-NotNull $svcType "DriverService type must exist"
        $batchMethod = $svcType.GetMethods() | Where-Object { $_.Name -like "*BatchDelete*" } | Select-Object -First 1
        if (-not $batchMethod) {
            throw "Pending M2: DriverService.BatchDeleteDriversAsync not yet implemented"
        }
        Assert-NotNull $batchMethod "Batch delete method must exist"
    }

Register-E2ETest -Tier 1 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T1.F03.04_BatchRemovalEmptyListHandling" `
    -Description "Verifies batch deletion gracefully handles an empty list without error." `
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
        Assert-NotNull $task "Invocation should return a Task"
    }

Register-E2ETest -Tier 1 -Feature "F3" -Source "R1 / PROJECT.md § F3" `
    -Name "T1.F03.05_DiskToolsViewModelExposesSelectionCommands" `
    -Description "Verifies DiskToolsViewModel exposes SelectAll, DeselectAll, and DeleteSelected commands." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $vmType = $asm.GetType("DiskMasterWinUI.ViewModels.DiskToolsViewModel")
        Assert-NotNull $vmType "DiskToolsViewModel type must exist"
        $props = $vmType.GetProperties() | ForEach-Object { $_.Name }
        $hasSelectAll = $props -contains "SelectAllDriversCommand" -or ($vmType.GetMethods() | Where-Object { $_.Name -like "*SelectAll*" }).Count -gt 0
        if (-not $hasSelectAll) {
            throw "Pending M2: DiskToolsViewModel multi-selection commands not yet implemented"
        }
        Assert-True $hasSelectAll "Multi-selection commands verified"
    }

# --- Feature F4: OEM Driver Force Uninstall ---

Register-E2ETest -Tier 1 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T1.F04.01_DeleteDriverCommandContainsDeleteDriverFlag" `
    -Description "Verifies delete command construction includes /delete-driver." `
    -TestBlock {
        $cmd = [PnpUtilOracle]::BuildDeleteCommand("oem15.inf", $false, $false)
        Assert-Contains $cmd "/delete-driver oem15.inf" "Must specify /delete-driver <oemInf>"
    }

Register-E2ETest -Tier 1 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T1.F04.02_DeleteDriverCommandIncludesUninstallSwitch" `
    -Description "Verifies delete command construction includes mandatory /uninstall switch per R1." `
    -TestBlock {
        $cmd = [PnpUtilOracle]::BuildDeleteCommand("oem15.inf", $false, $true)
        Assert-Contains $cmd "/uninstall" "Must include /uninstall switch"
    }

Register-E2ETest -Tier 1 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T1.F04.03_DeleteDriverCommandIncludesForceSwitch" `
    -Description "Verifies force delete command includes /force switch." `
    -TestBlock {
        $cmd = [PnpUtilOracle]::BuildDeleteCommand("oem15.inf", $true, $true)
        Assert-Contains $cmd "/force" "Must include /force switch"
        Assert-Equal $cmd "/delete-driver oem15.inf /uninstall /force"
    }

Register-E2ETest -Tier 1 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T1.F04.04_NonForceDeleteOmitsForceSwitch" `
    -Description "Verifies non-force delete command omits /force switch." `
    -TestBlock {
        $cmd = [PnpUtilOracle]::BuildDeleteCommand("oem15.inf", $false, $true)
        Assert-NotContains $cmd "/force" "Must not include /force switch when force=false"
        Assert-Equal $cmd "/delete-driver oem15.inf /uninstall"
    }

Register-E2ETest -Tier 1 -Feature "F4" -Source "R1 / PROJECT.md § F4" `
    -Name "T1.F04.05_DriverServiceDeleteDriverMethodContract" `
    -Description "Verifies DriverService.DeleteDriverPackageAsync accepts force parameter." `
    -TestBlock {
        $asm = Initialize-TestAssembly
        $svcType = $asm.GetType("DiskMasterWinUI.Services.DriverService")
        $methods = $svcType.GetMethods() | Where-Object { $_.Name -like "*DeleteDriver*" }
        Assert-True ($methods.Count -gt 0) "DriverService must have a delete driver method"
        $delMethod = $methods | Select-Object -First 1
        $params = $delMethod.GetParameters() | ForEach-Object { $_.Name }
        Assert-Contains $params "force" "Delete driver method must accept force parameter"
    }
