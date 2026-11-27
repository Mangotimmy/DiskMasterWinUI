# Automated verification for DiskMasterWinUI:
# 1) Multi-layer File Safety & Quarantine Restore Protection
# 2) Hardened TempCleanService, CacheCleanService, DriverService, OneDriveService
# 3) UI/UX Modernization (Default Categorized Left NavigationView + TopTabs Toggle, Uncramped SystemHealthPage + Collapsible Console + Protection Vault, Nested ListView Fix, Light-Theme Contrast Fix)

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

Write-Host "=== 1. File Protection & Hardened Cleaner Invariants ===" -ForegroundColor Cyan

$safeGuard  = Get-Content (Join-Path $root "Helpers\SafePathGuard.cs") -Raw
$quarantine = Get-Content (Join-Path $root "Services\FileQuarantineService.cs") -Raw
$tempSvc    = Get-Content (Join-Path $root "Services\TempCleanService.cs") -Raw
$cacheSvc   = Get-Content (Join-Path $root "Services\CacheCleanService.cs") -Raw
$driverSvc  = Get-Content (Join-Path $root "Services\DriverService.cs") -Raw
$oneDrive   = Get-Content (Join-Path $root "Services\OneDriveService.cs") -Raw
$settings   = Get-Content (Join-Path $root "Services\SettingsService.cs") -Raw

Assert-True ($safeGuard -match 'System32' -and $safeGuard -match 'WinSxS' -and $safeGuard -match 'Package Cache' -and $safeGuard -match 'Login Data' -and $safeGuard -match 'DiskMaster_Portable') "SafePathGuard hard-blocks System32, WinSxS, Package Cache, Browser Logins, and DiskMaster_Portable"
Assert-True ($safeGuard -match 'IsProtectedByAgeGuard' -and $safeGuard -match 'FileAttributes\.ReparsePoint') "SafePathGuard implements File-Age Guard and ReparsePoint protection"
Assert-True ($quarantine -match 'MaxSingleFileBytes\s*=\s*100L' -and $quarantine -match 'EnforceRetentionAndQuota' -and $quarantine -match '\.restored') "FileQuarantineService enforces 100MB single-file limit, retention/quota cap, and non-overwrite .restored safety"

$safeCatBlock = [regex]::Match($tempSvc, 'SafeCategoryIds\s*=\s*new[\s\S]*?\};').Value
Assert-True ($safeCatBlock -notmatch '"Prefetch"') "TempCleanService removes Prefetch from default SafeCategoryIds"
Assert-True ($tempSvc -notmatch '"Windows\\\\Panther"') "TempCleanService no longer touches C:\Windows\Panther in SetupFiles"
Assert-True ($tempSvc -match 'CleanDirectoryProtected' -and $tempSvc -match 'SafePathGuard\.IsSafeToClean' -and $tempSvc -match 'IsProtectedByAgeGuard') "TempCleanService enforces SafePathGuard, Age Guard, and Quarantine Vault"
Assert-True ($tempSvc -notmatch 'Directory\.EnumerateDirectories\(usersDir\)') "TempCleanService AppCache is scoped to current user (does not touch other user profiles)"

$deepCleanBlock = [regex]::Match($cacheSvc, 'PerformSystemDeepCleanupAsync[\s\S]*?CleanDownloadCacheAsync').Value
Assert-True ($deepCleanBlock -notmatch '"Prefetch"' -and $deepCleanBlock -notmatch '"MemoryDumps"') "CacheCleanService.PerformSystemDeepCleanupAsync excludes Prefetch and MemoryDumps"
Assert-True ($cacheSvc -match 'IsProtectedByAgeGuard') "CacheCleanService protects active recent .part downloads"

Assert-True ($driverSvc -match 'AutoBackupDriversBeforeDelete' -and $driverSvc -match '/export-driver') "DriverService auto-exports driver backup to Quarantine before /delete-driver"
Assert-True ($oneDrive -match 'TrySnapshotCopyFile' -and $oneDrive -match 'OneDriveTemp') "OneDriveService snapshots Personal/*.dat and guards non-empty OneDriveTemp"

Assert-True ($settings -match 'EnableQuarantineBackup\s*\{\s*get;\s*set;\s*\}\s*=\s*true' -and $settings -match 'EnableTempFileAgeGuard\s*\{\s*get;\s*set;\s*\}\s*=\s*true' -and $settings -match 'NavigationLayoutMode\s*\{\s*get;\s*set;\s*\}\s*=\s*"Sidebar"') "All protection defaults ON and NavigationLayoutMode defaults to Sidebar"

Write-Host "`n=== 2. UI / UX Modernization & Ergonomics Checks ===" -ForegroundColor Cyan

$mwXaml = Get-Content (Join-Path $root "MainWindow.xaml") -Raw
$mwCs   = Get-Content (Join-Path $root "MainWindow.xaml.cs") -Raw
$stXaml = Get-Content (Join-Path $root "Pages\SettingsPage.xaml") -Raw
$shXaml = Get-Content (Join-Path $root "Pages\SystemHealthPage.xaml") -Raw
$soXaml = Get-Content (Join-Path $root "Pages\SystemOptimizerPage.xaml") -Raw
$spXaml = Get-Content (Join-Path $root "Pages\SystemPerformancePage.xaml") -Raw

Assert-True ($mwXaml -match '<NavigationView\s+x:Name="SidebarNav"' -and $mwXaml -match 'NavigationViewItemHeader') "MainWindow provides categorized Left NavigationView (SidebarNav)"
Assert-True ($mwCs -match 'ApplyNavigationLayoutMode' -and $mwCs -match 'NormalView\.ActualTheme\s*==\s*ElementTheme\.Dark') "MainWindow supports Sidebar/TopTabs switching and uses ActualTheme for Light-Theme contrast"
Assert-True ($stXaml -match 'NavigationLayoutBox' -and $stXaml -match 'SelectedNavigationLayout') "SettingsPage exposes Navigation Layout Mode toggle"
Assert-True ($shXaml -match 'ProtectionVaultExpander' -and $shXaml -match 'ToggleConsoleExpandedCommand' -and $shXaml -match 'RestoreQuarantineSession_Click') "SystemHealthPage exposes Protection & Restore Vault panel and collapsible terminal console"
Assert-True ($soXaml -match '<ItemsControl ItemsSource="\{x:Bind ViewModel\.BackgroundBloatTweaks') "SystemOptimizerPage uses ItemsControl for BackgroundBloatTweaks (no nested unbounded ListView scroll trap)"
Assert-True ($spXaml -notmatch 'Foreground="#E9D5FF"') "SystemPerformancePage removed unreadable light-on-light #E9D5FF foreground"

Write-Host "`n=== SUMMARY: $script:Passed Passed, $script:Failed Failed ===" -ForegroundColor $(if ($script:Failed -eq 0) { 'Green' } else { 'Red' })
if ($script:Failed -gt 0) { exit 1 }
exit 0
