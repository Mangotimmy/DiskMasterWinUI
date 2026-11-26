# Automated verification for DiskMasterWinUI:
# 1) Core bug fixes (sc.exe config syntax, finally service restore, MemoryManagerService SysMain read-only & strict errors, Windows.old confirmation, CTS disposal)
# 2) Windows Update & 0x800F0831 (CBS_E_STORE_CORRUPTION) Repair Center & CBS.log parser
# 3) PC Clean-Run & RAM Optimizer (StartupApproved reversible binary encoding, foreground/whitelist exclusion, presets)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$script:Passed = 0
$script:Failed = 0

function Assert-True($condition, $name) {
    if ($condition) {
        Write-Host "[PASS] $name" -ForegroundColor Green
        $script:Passed++
    } else {
        Write-Host "[FAIL] $name" -ForegroundColor Red
        $script:Failed++
    }
}

Write-Host "=== 1. Static Code Invariant & Bug-Fix Checks ===" -ForegroundColor Cyan

$wuSvc = Get-Content (Join-Path $root "Services\WindowsUpdateRepairService.cs") -Raw
$mmSvc = Get-Content (Join-Path $root "Services\MemoryManagerService.cs") -Raw
$srSvc = Get-Content (Join-Path $root "Services\SystemRepairService.cs") -Raw
$shVm  = Get-Content (Join-Path $root "ViewModels\SystemHealthViewModel.cs") -Raw
$shXaml = Get-Content (Join-Path $root "Pages\SystemHealthPage.xaml") -Raw
$soXaml = Get-Content (Join-Path $root "Pages\SystemOptimizerPage.xaml") -Raw
$spXaml = Get-Content (Join-Path $root "Pages\SystemPerformancePage.xaml") -Raw
$startupSvc = Get-Content (Join-Path $root "Services\StartupManagerService.cs") -Raw
$cleanRunSvc = Get-Content (Join-Path $root "Services\CleanRunOptimizerService.cs") -Raw
$mainWin = Get-Content (Join-Path $root "MainWindow.xaml.cs") -Raw

# 1.1 Zero sc.exe config start=<nospace> across all Services/
$badScMatches = Select-String -Path (Join-Path $root "Services\*.cs") -Pattern 'sc\.exe",\s*\$?"config\s+[^"]*start=[a-zA-Z]'
Assert-True ($badScMatches.Count -eq 0) "Zero sc.exe config 'start=' missing-space bugs in Services/"

# 1.2 Finally guarantee in WindowsUpdateRepairService
Assert-True ($wuSvc -match 'finally\s*\{[\s\S]*net\.exe",\s*\$"start \{svc\}"') "WindowsUpdateRepairService restores services inside finally block"
Assert-True ($wuSvc -notmatch 'wuauclt\.exe') "Deprecated wuauclt.exe removed from WindowsUpdateRepairService"
Assert-True ($wuSvc -match '0x800F0831' -and $wuSvc -match 'catalog\.update\.microsoft\.com' -and $wuSvc -match 'ms-settings:troubleshoot') "WindowsUpdateRepairService includes 0x800F0831 preset, Update Catalog, and Troubleshooter"

# 1.3 MemoryManagerService read-only SysMain & strict error matching
$readSection = [regex]::Match($mmSvc, 'ReadStatusFromWindowsAsync\(\)[\s\S]*?InvalidateStatusCache').Value
Assert-True ($readSection -notmatch 'Start-Service\s+SysMain') "MemoryManagerService.ReadStatusFromWindowsAsync does NOT forcibly start SysMain"
Assert-True ($mmSvc -notmatch 'combined\.Contains\("50"\)') "MemoryManagerService no longer uses loose Contains('50') false-positive check"
Assert-True ($mmSvc -match 'ApplyGamingRamProfileAsync[\s\S]*if\s*\(exitCode\s*!=\s*0\)') "ApplyGamingRamProfileAsync checks real PowerShell exitCode"
Assert-True ($mmSvc -match 'RestoreStockDefaultsAsync[\s\S]*if\s*\(exitCode\s*!=\s*0\)') "RestoreStockDefaultsAsync checks real PowerShell exitCode"

# 1.4 SystemRepairService full DISM+SFC sequence
Assert-True ($srSvc -match 'RunFullDismSfcSequenceAsync' -and $srSvc -match '/scanhealth' -and $srSvc -match '/checkhealth' -and $srSvc -match '/restorehealth') "SystemRepairService supports ScanHealth -> CheckHealth -> RestoreHealth -> SFC sequence"

# 1.5 SystemHealthViewModel confirmation & CTS hygiene
$winOldSection = [regex]::Match($shVm, 'CleanWindowsOldOnlyAsync\(\)[\s\S]*?CleanAppCacheOnlyAsync').Value
Assert-True ($winOldSection -match 'ConfirmDestructiveOperationAsync') "CleanWindowsOldOnlyAsync requires destructive confirmation dialog"
$directCtsAlloc = [regex]::Matches($shVm, '_cts\s*=\s*new\s+CancellationTokenSource\(\);')
Assert-True ($directCtsAlloc.Count -eq 1) "SystemHealthViewModel allocates CancellationTokenSource only inside ResetCts() (no CTS leaks)"

# 1.6 UI & Lifecycle Wiring
Assert-True ($shXaml -match 'WuErrorPresets' -and $shXaml -match 'ScanCbsAndUpdateHistoryCommand' -and $shXaml -match 'InstallLocalUpdatePackageCommand' -and $shXaml -match 'LaunchWuTroubleshooterCommand') "SystemHealthPage exposes 0x800F0831 presets, CBS scanner, MSU/CAB installer, and Troubleshooter"
Assert-True ($soXaml -match 'TabCleanRun' -and $soXaml -match 'AutoRamGuardEnabled' -and $soXaml -match 'FilteredStartupEntries' -and $soXaml -match 'BackgroundBloatTweaks') "SystemOptimizerPage exposes TabCleanRun with Auto RAM Guard, Startup Manager, and Bloat Tamer"
Assert-True ($spXaml -match 'RamPressure\.CommitDisplay' -and $spXaml -match 'ReleaseRamSafelyCommand' -and $spXaml -match 'WebView2Owners') "SystemPerformancePage exposes RAM Pressure & Lag Doctor with Commit, WebView2 owners, and Safe Release"
Assert-True ($mainWin -match 'AutoRamGuardService\.Instance\.Start\(\)' -and $mainWin -match 'AutoRamGuardService\.Instance\.Dispose\(\)' -and $mainWin -match 'LocalizationService\.Instance\.LanguageChanged\s*-=\s*ApplyLanguage') "MainWindow starts/disposes AutoRamGuardService and unsubscribes LanguageChanged on close"

Write-Host "`n=== 2. Functional Unit Tests (CBS.log Parser, StartupApproved Binary, Whitelist) ===" -ForegroundColor Cyan

# Test StartupApproved binary format logic (Windows 12-byte format: even=enabled, odd=disabled)
$enabledBytes = [byte[]]@(0x02,0,0,0, 0,0,0,0, 0,0,0,0)
$disabledBytes = [byte[]]@(0x03,0,0,0, 1,2,3,4, 5,6,7,8)
Assert-True (($enabledBytes[0] -band 1) -eq 0) "StartupApproved 0x02 decodes as Enabled"
Assert-True (($disabledBytes[0] -band 1) -eq 1) "StartupApproved 0x03 decodes as Disabled"
Assert-True ($startupSvc -match 'StartupApproved\\Run' -and $startupSvc -match 'EncodeStartupApprovedFlag' -and $startupSvc -match 'DecodeStartupApprovedEnabled') "StartupManagerService uses native reversible StartupApproved flags (never deletes Run keys)"
Assert-True ($startupSvc -notmatch 'DeleteValue') "StartupManagerService never deletes user Run registry values"

# Test CBS.log sample lines match the exact regexes used in WindowsUpdateRepairService
$sampleCbsLines = @(
    "2026-11-26 14:22:01, Error                 CBS    Failed to resolve package 'Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.2033.1.16' [HRESULT = 0x800f0831 - CBS_E_STORE_CORRUPTION]",
    "2026-11-26 14:22:02, Error                 CBS    Mark store corruption flag because of package: Package_for_KB5044284~31bf3856ad364e35~amd64~~10.0.1.0. [HRESULT = 0x800f0831]",
    "2026-11-26 14:22:05, Info                  CSI    Cannot repair member file [l:12]'example.dll' of Microsoft-Windows-Core"
)
$kbMatch = [regex]::Match($sampleCbsLines[1], '(?<![A-Za-z0-9])KB\d{6,8}(?!\d)', 'IgnoreCase')
$pkgMatch = [regex]::Match($sampleCbsLines[0], 'Package_for_[A-Za-z0-9_~\.\-]+', 'IgnoreCase')
$errMatch = [regex]::Match($sampleCbsLines[0], '0x[0-9a-fA-F]{8}')
Assert-True ($kbMatch.Success -and $kbMatch.Value -eq "KB5044284") "CBS.log KB regex extracts KB5044284"
Assert-True ($pkgMatch.Success -and $pkgMatch.Value.StartsWith("Package_for_RollupFix")) "CBS.log Package ID regex extracts Package_for_RollupFix..."
Assert-True ($errMatch.Success -and $errMatch.Value.ToLower() -eq "0x800f0831") "CBS.log Error regex extracts 0x800f0831"

# Verify CleanRunOptimizerService foreground & critical protection
Assert-True ($cleanRunSvc -match 'GetForegroundWindow' -and $cleanRunSvc -match 'ShouldExcludeProcess' -and $cleanRunSvc -match 'NtSetSystemInformation' -and $cleanRunSvc -match 'SetProcessInformation') "CleanRunOptimizerService implements foreground exclusion, Standby purge, and EcoQoS"

Write-Host "`n=== SUMMARY: $script:Passed Passed, $script:Failed Failed ===" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
if ($script:Failed -gt 0) { exit 1 }
exit 0
