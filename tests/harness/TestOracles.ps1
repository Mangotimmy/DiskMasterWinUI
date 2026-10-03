# DiskMaster Pro WinUI 3 — Test Oracles & Domain Spec Helpers
# Author: Test Writer Agent (E2E Track)
# Scope: Authoritative oracle definitions and synthetic test data generators for Features F1 to F16.

class PnpUtilOracle {
    # Generates synthetic PnPUtil output for any of the 4 supported languages
    static [string] GenerateDriverOutput([string]$language, [string]$oemInf, [string]$origName, [string]$class, [string]$provider, [string]$date, [string]$version, [string]$signer) {
        $lang = $language.ToLower()
        if ($lang -eq "zh-tw") {
            return "發佈名稱:               $oemInf`n原始名稱:               $origName`n驅動程式套件提供者:     $provider`n類別名稱:               $class`n驅動程式日期和版本:     $date $version`n簽署者名稱:             $signer`n"
        }
        elseif ($lang -eq "zh-cn") {
            return "发布名称:               $oemInf`n原始名称:               $origName`n驱动程序程序包提供商:   $provider`n类名称:                 $class`n驱动程序日期和版本:     $date $version`n签名者名称:             $signer`n"
        }
        elseif ($lang -eq "ja-jp") {
            return "公開名:                 $oemInf`n元の名前:               $origName`nドライバー パッケージ プロバイダー: $provider`nクラス名:               $class`nドライバーの日付とバージョン: $date $version`n署名者名:               $signer`n"
        }
        else {
            return "Published Name:     $oemInf`nOriginal Name:      $origName`nProvider Name:      $provider`nClass Name:         $class`nDriver Date and Version: $date $version`nSigner Name:        $signer`n"
        }
    }

    # Reference command line constructor
    static [string] BuildDeleteCommand([string]$oemInf, [bool]$force, [bool]$uninstall) {
        $cmd = "/delete-driver $oemInf"
        if ($uninstall) { $cmd += " /uninstall" }
        if ($force) { $cmd += " /force" }
        return $cmd
    }
}

class StorageDirectoryOracle {
    static [string[]] $TargetDirectories
    static [hashtable] $ExpectedSafetyRatings
    static [hashtable] $ExpectedActionFlags
    static [string] $DismCommand = "dism.exe /online /cleanup-image /startcomponentcleanup"

    static StorageDirectoryOracle() {
        [StorageDirectoryOracle]::TargetDirectories = @(
            "WinSxS",
            "SoftwareDistribution",
            "Installer",
            "DriverStore\FileRepository",
            "Temp",
            "AppData",
            "System Volume Information",
            "hiberfil.sys",
            "pagefile.sys",
            "`$WINDOWS.~BT",
            "Windows.old"
        )

        [StorageDirectoryOracle]::ExpectedSafetyRatings = @{
            "WinSxS"                     = "CleanViaSystemTool"
            "SoftwareDistribution"      = "CleanViaSystemTool"
            "Installer"                 = "EssentialCore"
            "DriverStore\FileRepository" = "EssentialCore"
            "Temp"                      = "SafeToPurge"
            "AppData"                   = "SafeToPurge"
            "System Volume Information" = "EssentialCore"
            "hiberfil.sys"              = "SafeToPurge"
            "pagefile.sys"              = "EssentialCore"
            "`$WINDOWS.~BT"             = "SafeToPurge"
            "Windows.old"               = "SafeToPurge"
        }

        [StorageDirectoryOracle]::ExpectedActionFlags = @{
            "WinSxS"                     = @{ CanSafeClean = $false; CanDismClean = $true  }
            "SoftwareDistribution"      = @{ CanSafeClean = $true;  CanDismClean = $false }
            "Installer"                 = @{ CanSafeClean = $false; CanDismClean = $false }
            "DriverStore\FileRepository" = @{ CanSafeClean = $false; CanDismClean = $false }
            "Temp"                      = @{ CanSafeClean = $true;  CanDismClean = $false }
            "AppData"                   = @{ CanSafeClean = $true;  CanDismClean = $false }
            "System Volume Information" = @{ CanSafeClean = $false; CanDismClean = $false }
            "hiberfil.sys"              = @{ CanSafeClean = $true;  CanDismClean = $false }
            "pagefile.sys"              = @{ CanSafeClean = $false; CanDismClean = $false }
            "`$WINDOWS.~BT"             = @{ CanSafeClean = $true;  CanDismClean = $false }
            "Windows.old"               = @{ CanSafeClean = $true;  CanDismClean = $false }
        }
    }
}

class BcdOracle {
    static [string[]] $SafeBootModes
    static [string[]] $BootFlags

    static BcdOracle() {
        [BcdOracle]::SafeBootModes = @(
            "Normal",
            "Minimal",
            "AlternateShell",
            "Network",
            "DsRepair"
        )

        [BcdOracle]::BootFlags = @(
            "noguiboot",
            "bootlog",
            "basevideo",
            "sos",
            "testsigning",
            "nointegritychecks",
            "hypervisorlaunchtype"
        )
    }

    static [string] BuildSafeBootCommand([string]$mode) {
        return [BcdOracle]::BuildSafeBootCommand($mode, "{current}")
    }

    static [string] BuildSafeBootCommand([string]$mode, [string]$identifier) {
        $m = $mode.ToLower()
        if ($m -eq "minimal") { return "bcdedit.exe /set $identifier safeboot minimal" }
        if ($m -eq "alternateshell") { return "bcdedit.exe /set $identifier safeboot alternateshell" }
        if ($m -eq "network") { return "bcdedit.exe /set $identifier safeboot network" }
        if ($m -eq "dsrepair") { return "bcdedit.exe /set $identifier safeboot dsrepair" }
        if ($m -eq "normal") { return "bcdedit.exe /deletevalue $identifier safeboot" }
        throw "Unknown safe boot mode: $mode"
    }

    static [string] BuildBootFlagCommand([string]$flag, [bool]$enable) {
        return [BcdOracle]::BuildBootFlagCommand($flag, $enable, "{current}")
    }

    static [string] BuildBootFlagCommand([string]$flag, [bool]$enable, [string]$identifier) {
        $f = $flag.ToLower()
        if ($f -eq "testsigning") {
            $val = if ($enable) { "on" } else { "off" }
            return "bcdedit.exe /set $identifier testsigning $val"
        }
        if ($f -eq "nointegritychecks") {
            $val = if ($enable) { "on" } else { "off" }
            return "bcdedit.exe /set $identifier nointegritychecks $val"
        }
        if ($f -eq "hypervisorlaunchtype") {
            $val = if ($enable) { "auto" } else { "off" }
            return "bcdedit.exe /set $identifier hypervisorlaunchtype $val"
        }
        if ($enable) {
            return "bcdedit.exe /set $identifier $flag yes"
        } else {
            return "bcdedit.exe /deletevalue $identifier $flag"
        }
    }
}

class PowerCfgOracle {
    static [string] $SubProcessorGuid = "54533251-82be-4824-96c1-47b60b740d00"
    static [hashtable] $ProcessorAttributes

    static PowerCfgOracle() {
        [PowerCfgOracle]::ProcessorAttributes = @{
            "BoostMode"                   = "be337238-0d82-4146-a960-4f3749d470c7"
            "EnergyPerformancePreference" = "36687f9e-e376-49e8-b783-be5e3e3563ab"
            "AutonomousMode"              = "8baa4a8a-14fc-482b-bd23-a0f0f71e11e8"
            "CoreParkingMinCores"         = "0cc5b647-c1df-4637-891a-dec35c318583"
            "CoreParkingMaxCores"         = "ea062031-0e34-4ff1-9b6d-eb1059324028"
            "HeterogeneousScheduling"     = "7f24e370-7664-4642-99e3-e605185a0899"
            "SystemCoolingPolicy"         = "94d3a615-a899-4ac5-ae2b-e4d8f6343d57"
        }
    }

    static [string] BuildUnhideCommand([string]$guid) {
        return "powercfg.exe -attributes " + [PowerCfgOracle]::SubProcessorGuid + " " + $guid + " -ATTRIB_HIDE"
    }

    static [string] BuildHideCommand([string]$guid) {
        return "powercfg.exe -attributes " + [PowerCfgOracle]::SubProcessorGuid + " " + $guid + " +ATTRIB_HIDE"
    }

    static [double] BytesToGigabytes([long]$bytes) {
        return [System.Math]::Round(($bytes / (1024.0 * 1024.0 * 1024.0)), 2)
    }
}

class SystemOptimizerOracle {
    static [string] $MemoryManagementKey = "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
    static [string] $DynamicTickCommand = "bcdedit.exe /set disabledynamictick yes"
    static [string] $DynamicTickRevertCommand = "bcdedit.exe /deletevalue disabledynamictick"
}

class AssetOracle {
    static [int[]] $ExpectedIcoDimensions = @(256, 128, 64, 48, 32, 16)
    static [string[]] $RequiredPngAssets
    static [hashtable] $ExpectedPngDimensions

    static AssetOracle() {
        [AssetOracle]::RequiredPngAssets = @(
            "Square150x150Logo.scale-200.png",
            "Square44x44Logo.scale-200.png",
            "StoreLogo.png",
            "SplashScreen.scale-200.png",
            "Wide310x150Logo.scale-200.png"
        )

        [AssetOracle]::ExpectedPngDimensions = @{
            "Square150x150Logo.scale-200.png" = @{ Width = 300; Height = 300 }
            "Square44x44Logo.scale-200.png"   = @{ Width = 88;  Height = 88  }
            "StoreLogo.png"                   = @{ Width = 50;  Height = 50  }
            "SplashScreen.scale-200.png"      = @{ Width = 1240; Height = 600 }
            "Wide310x150Logo.scale-200.png"   = @{ Width = 620; Height = 300 }
        }
    }
}

class LocalizationOracle {
    static [string[]] $SupportedCultures = @("zh-TW", "zh-CN", "en-US", "ja-JP")
    static [hashtable] $CultureHeaders

    static LocalizationOracle() {
        [LocalizationOracle]::CultureHeaders = @{
            "zh-TW" = "繁體中文"
            "zh-CN" = "简体中文"
            "en-US" = "English"
            "ja-JP" = "日本語"
        }
    }
}

class ReadmeOracle {
    static [string[]] $RequiredSections = @(
        "Features",
        "Architecture",
        "Driver",
        "Storage",
        "Safe Boot",
        "Power",
        "Benchmarks",
        "Safety Guardrails",
        "Build"
    )
}
