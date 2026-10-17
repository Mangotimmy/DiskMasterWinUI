<#
.SYNOPSIS
    DiskMaster Pro — Modular Anti-Forensics & Privacy Utility Function
    Inspects (Audit) and clears (Purge) eight critical Windows DFIR forensic artifacts.

.DESCRIPTION
    Comprehensive Windows privacy and forensic trace cleaning engine designed for systems
    administrators and DFIR security practitioners. Supports dry-run inspection, selective
    target cleaning, permission checks with automatic elevation, and detailed forensic reporting.

    Covered Forensic Artifacts:
    1. Thumbnail Cache: %LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db
    2. Prefetch Execution Logs: C:\Windows\Prefetch\*.pf
    3. USB Storage History (USBSTOR): HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR
    4. Plaintext Wi-Fi Profiles: netsh wlan stored SSIDs and cleartext keys
    5. Clipboard History: Windows Clipboard service / Win10/11 history buffer & registry toggle
    6. Disk Cleanup Automation: cleanmgr.exe SageSet/SageRun registry automation
    7. Run Dialog History (MRU): HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU
    8. Recent Files & Jump Lists: %APPDATA%\Recent, AutomaticDestinations, CustomDestinations & policies

.PARAMETER Mode
    Operational mode: 'Audit' (alias 'Inspect') to report without modifying,
    or 'Purge' (alias 'Clean') to perform safe deletion and configuration changes.

.PARAMETER Targets
    One or more targets to process: 'All', 'ThumbnailCache', 'Prefetch', 'UsbHistory',
    'WifiProfiles', 'ClipboardHistory', 'DiskCleanup', 'RunMru', 'RecentFiles'. Default: 'All'.

.PARAMETER DryRun
    When specified with Purge mode, simulates actions and logs intended changes without modifying the system.

.PARAMETER PassThru
    Outputs custom PSCustomObject result records to the PowerShell pipeline.

.EXAMPLE
    .\Invoke-WindowsPrivacyCleaner.ps1 -Mode Audit
    Scans and reports on all 8 forensic artifact categories.

.EXAMPLE
    .\Invoke-WindowsPrivacyCleaner.ps1 -Mode Purge -Targets Prefetch,RunMru -DryRun
    Simulates purge of Prefetch logs and Run dialog history.

.EXAMPLE
    .\Invoke-WindowsPrivacyCleaner.ps1 -Mode Purge -Targets All
    Cleans all 8 forensic artifact categories (elevates to Administrator if required).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [ValidateSet('Audit', 'Inspect', 'Purge', 'Clean')]
    [string]$Mode = 'Audit',

    [Parameter(Mandatory = $false)]
    [switch]$Inspect,

    [Parameter(Mandatory = $false)]
    [switch]$Clean,

    [Parameter(Mandatory = $false, ValueFromPipeline = $true)]
    [string[]]$Targets = @('All'),

    [Parameter(Mandatory = $false)]
    [switch]$DryRun,

    [Parameter(Mandatory = $false)]
    [switch]$NoRestartExplorer,

    [Parameter(Mandatory = $false)]
    [switch]$PassThru
)

# Handle switch flags for mode selection
if ($Clean) { $Mode = 'Purge' }
elseif ($Inspect) { $Mode = 'Audit' }

# Normalize operational mode aliases
$isPurge = ($Mode -eq 'Purge' -or $Mode -eq 'Clean')
$isAudit = -not $isPurge

# Check pipeline input stream safely without blocking console stdin
if ($PSCmdlet.MyInvocation.ExpectingInput) {
    $pipelineItems = @($input)
    if ($pipelineItems.Count -gt 0) {
        $Targets = $pipelineItems
    }
}

$rawTargetList = @()
foreach ($t in $Targets) {
    if (-not [string]::IsNullOrWhiteSpace($t)) {
        if ($t -match ',') {
            $rawTargetList += ($t -split ',' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        else {
            $rawTargetList += $t.Trim()
        }
    }
}

$allTargetNames = @('ThumbnailCache', 'Prefetch', 'UsbHistory', 'WifiProfiles', 'ClipboardHistory', 'DiskCleanup', 'RunMru', 'RecentFiles')
$normalizedTargets = @()
foreach ($item in $rawTargetList) {
    switch -Regex ($item) {
        '^(All|\*)$' {
            $normalizedTargets = $allTargetNames
            break
        }
        '^(ThumbnailCache|ThumbCache|Thumbs?)$' { $normalizedTargets += 'ThumbnailCache'; break }
        '^(Prefetch|PF)$' { $normalizedTargets += 'Prefetch'; break }
        '^(UsbHistory|UsbStor|USB)$' { $normalizedTargets += 'UsbHistory'; break }
        '^(WifiProfiles|WiFi|Wlan)$' { $normalizedTargets += 'WifiProfiles'; break }
        '^(ClipboardHistory|Clipboard)$' { $normalizedTargets += 'ClipboardHistory'; break }
        '^(DiskCleanup|SageSet|Cleanmgr)$' { $normalizedTargets += 'DiskCleanup'; break }
        '^(RunMru|RunDialog|RunDialogHistory|Run)$' { $normalizedTargets += 'RunMru'; break }
        '^(RecentFiles|Recent|JumpLists?|Destinations)$' { $normalizedTargets += 'RecentFiles'; break }
        Default {
            Write-Warning "Unknown target category: '$item'. Valid categories: $($allTargetNames -join ', ')"
        }
    }
}

$selectedTargets = if ($normalizedTargets.Count -eq 0) { $allTargetNames } else { $normalizedTargets | Select-Object -Unique }

# ── Helper: Administrator Verification & Elevation ──
function Test-IsAdministrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$currentIdentity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-ElevatedPrivileges {
    param([string]$Reason)
    if (-not (Test-IsAdministrator)) {
        Write-Warning "[Privilege Warning] Operation requires Administrative privileges: $Reason"
        Write-Host "Elevating process to Administrator..." -ForegroundColor Yellow
        $scriptPath = $MyInvocation.MyCommand.Definition
        if (-not $scriptPath -or -not (Test-Path $scriptPath)) {
            $scriptPath = $PSCommandPath
        }
        $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Mode $Mode -Targets $($selectedTargets -join ',')"
        if ($DryRun) { $argList += " -DryRun" }
        if ($NoRestartExplorer) { $argList += " -NoRestartExplorer" }
        Start-Process pwsh.exe -ArgumentList $argList -Verb RunAs -Wait
        exit $LASTEXITCODE
    }
}

# ── Diagnostic Log Helper ──
function Write-ReportLog {
    param(
        [string]$Category,
        [string]$Status,
        [string]$Details,
        [string]$Color = 'White'
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $formatted = "[{0}] [{1,-16}] [{2,-7}] {3}" -f $timestamp, $Category, $Status, $Details
    Write-Host $formatted -ForegroundColor $Color
}

$Global:ReportResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Add-AuditRecord {
    param(
        [string]$Target,
        [string]$ArtifactType,
        [int]$ItemCount,
        [long]$SizeBytes,
        [string]$RiskLevel,
        [string]$Status,
        [string]$Summary,
        [object]$RawData = $null
    )
    $rec = [PSCustomObject]@{
        Timestamp    = (Get-Date)
        Target       = $Target
        ArtifactType = $ArtifactType
        ItemCount    = $ItemCount
        SizeBytes    = $SizeBytes
        RiskLevel    = $RiskLevel
        Status       = $Status
        Summary      = $Summary
        RawData      = $RawData
    }
    $Global:ReportResults.Add($rec)
    return $rec
}

# ══════════════════════════════════════════════════════════════════════════
# 1. THUMBNAIL CACHE
# ══════════════════════════════════════════════════════════════════════════
function Invoke-ThumbnailCacheArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "ThumbnailCache"
    $explorerPath = Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Explorer"

    if (-not (Test-Path $explorerPath)) {
        Write-ReportLog $cat "SKIP" "Explorer thumbnail cache directory not found: $explorerPath" 'DarkGray'
        $null = Add-AuditRecord $cat "Filesystem" 0 0 "None" "Clean" "Thumbnail cache directory does not exist."
        return
    }

    $thumbFiles = Get-ChildItem -Path $explorerPath -Filter "thumbcache_*.db" -File -ErrorAction SilentlyContinue
    $iconFiles  = Get-ChildItem -Path $explorerPath -Filter "iconcache_*.db"  -File -ErrorAction SilentlyContinue
    $allFiles = @($thumbFiles) + @($iconFiles)

    $count = $allFiles.Count
    $totalBytes = ($allFiles | Measure-Object -Property Length -Sum).Sum
    if (-not $totalBytes) { $totalBytes = 0 }
    $sizeMb = [math]::Round($totalBytes / 1MB, 2)

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "Found $count cached DB files ($sizeMb MB) in $explorerPath" 'Cyan'
        foreach ($f in $allFiles) {
            Write-ReportLog $cat "DETAIL" ("  - {0} ({1:N0} bytes, Last modified: {2})" -f $f.Name, $f.Length, $f.LastWriteTime) 'DarkCyan'
        }
        $null = Add-AuditRecord $cat "Filesystem" $count $totalBytes "Medium" "TracesFound" "$count thumbnail/icon cache databases ($sizeMb MB)" $allFiles.Name
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would safely terminate explorer.exe, remove $count DB files ($sizeMb MB), and restart explorer.exe." 'Yellow'
        $null = Add-AuditRecord $cat "Filesystem" $count $totalBytes "Medium" "DryRun" "Simulated purge of $count cache files ($sizeMb MB)"
        return
    }

    Write-ReportLog $cat "PURGE" "Stopping explorer.exe to release file handles..." 'Magenta'
    Stop-Process -Name "explorer" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    $deletedCount = 0
    $deletedBytes = 0
    foreach ($f in $allFiles) {
        try {
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop
            $deletedCount++
            $deletedBytes += $f.Length
            Write-ReportLog $cat "DELETED" "Purged $($f.Name)" 'Green'
        }
        catch {
            Write-ReportLog $cat "WARNING" "Could not delete $($f.Name): $($_.Exception.Message)" 'DarkYellow'
        }
    }

    if (-not $NoRestartExplorer) {
        if (-not (Get-Process -Name "explorer" -ErrorAction SilentlyContinue)) {
            Write-ReportLog $cat "PURGE" "Restarting Windows Explorer shell..." 'Magenta'
            Start-Process explorer.exe
        }
        else {
            Write-ReportLog $cat "PURGE" "Windows Explorer shell already active (auto-recovered)." 'DarkCyan'
        }
    }

    $delMb = [math]::Round($deletedBytes / 1MB, 2)
    Write-ReportLog $cat "SUCCESS" "Thumbnail cache purge completed ($deletedCount/$count files cleared, $delMb MB reclaimed)." 'Green'
    $null = Add-AuditRecord $cat "Filesystem" $deletedCount $deletedBytes "None" "Purged" "Cleared $deletedCount thumbnail/icon cache files ($delMb MB)"
}

# ══════════════════════════════════════════════════════════════════════════
# 2. PREFETCH EXECUTION LOGS
# ══════════════════════════════════════════════════════════════════════════
function Invoke-PrefetchArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "Prefetch"
    $prefetchDir = Join-Path $env:SystemRoot "Prefetch"

    if (-not (Test-Path $prefetchDir)) {
        Write-ReportLog $cat "SKIP" "Prefetch directory not present: $prefetchDir" 'DarkGray'
        $null = Add-AuditRecord $cat "Filesystem" 0 0 "None" "Clean" "Prefetch directory not found."
        return
    }

    $pfFiles = Get-ChildItem -Path $prefetchDir -Filter "*.pf" -File -ErrorAction SilentlyContinue
    $count = $pfFiles.Count
    $totalBytes = ($pfFiles | Measure-Object -Property Length -Sum).Sum
    if (-not $totalBytes) { $totalBytes = 0 }
    $sizeMb = [math]::Round($totalBytes / 1MB, 2)

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "Detected $count execution trace logs (*.pf, $sizeMb MB)" 'Cyan'
        $topExecutables = $pfFiles | Select-Object -First 10 | ForEach-Object {
            $baseExe = $_.Name -replace '-[0-9A-Fa-f]{8}\.pf$', ''
            "{0} (Run time: {1})" -f $baseExe, $_.LastWriteTime
        }
        foreach ($exe in $topExecutables) {
            Write-ReportLog $cat "DETAIL" "  - Trace: $exe" 'DarkCyan'
        }
        if ($count -gt 10) {
            Write-ReportLog $cat "DETAIL" "  ... and $($count - 10) more execution traces." 'DarkCyan'
        }
        $null = Add-AuditRecord $cat "Filesystem" $count $totalBytes "High" "TracesFound" "$count execution trace logs recorded ($sizeMb MB)" ($pfFiles | Select-Object -ExpandProperty Name)
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would delete $count prefetch files ($sizeMb MB) from $prefetchDir." 'Yellow'
        $null = Add-AuditRecord $cat "Filesystem" $count $totalBytes "High" "DryRun" "Simulated purge of $count prefetch files ($sizeMb MB)"
        return
    }

    if (-not (Test-IsAdministrator)) {
        Ensure-ElevatedPrivileges "Prefetch log purging requires Administrator access"
    }

    $purged = 0
    $purgedBytes = 0
    foreach ($f in $pfFiles) {
        try {
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop
            $purged++
            $purgedBytes += $f.Length
        }
        catch {
            Write-ReportLog $cat "WARNING" "Unable to purge $($f.Name): $($_.Exception.Message)" 'DarkYellow'
        }
    }

    $pMb = [math]::Round($purgedBytes / 1MB, 2)
    Write-ReportLog $cat "SUCCESS" "Purged $purged of $count prefetch execution logs ($pMb MB reclaimed)." 'Green'
    $null = Add-AuditRecord $cat "Filesystem" $purged $purgedBytes "None" "Purged" "Purged $purged prefetch logs ($pMb MB)"
}

# ══════════════════════════════════════════════════════════════════════════
# 3. USB STORAGE HISTORY (USBSTOR)
# ══════════════════════════════════════════════════════════════════════════
function Invoke-UsbStorageArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "UsbHistory"
    $usbstorKey = "HKLM:\SYSTEM\CurrentControlSet\Enum\USBSTOR"

    if (-not (Test-Path $usbstorKey)) {
        Write-ReportLog $cat "AUDIT" "USBSTOR registry key clean or not present." 'Green'
        $null = Add-AuditRecord $cat "Registry" 0 0 "None" "Clean" "No USBSTOR records found."
        return
    }

    $usbDevices = [System.Collections.Generic.List[PSCustomObject]]::new()
    try {
        $deviceGroups = Get-ChildItem -Path $usbstorKey -ErrorAction SilentlyContinue
        foreach ($grp in $deviceGroups) {
            $instances = Get-ChildItem -Path $grp.PSPath -ErrorAction SilentlyContinue
            foreach ($inst in $instances) {
                $prop = Get-ItemProperty -Path $inst.PSPath -ErrorAction SilentlyContinue
                $friendlyName = if ($prop.FriendlyName) { $prop.FriendlyName } else { $grp.PSChildName }
                $serialNumber = $inst.PSChildName
                $hardwareId   = if ($prop.HardwareID) { ($prop.HardwareID -join "; ") } else { "" }

                $usbDevices.Add([PSCustomObject]@{
                    DeviceGroup  = $grp.PSChildName
                    SerialNumber = $serialNumber
                    FriendlyName = $friendlyName
                    HardwareID   = $hardwareId
                    RegistryPath = $inst.PSPath
                })
            }
        }
    }
    catch {
        Write-ReportLog $cat "WARNING" "Failed to enumerate USBSTOR keys: $($_.Exception.Message)" 'DarkYellow'
    }

    $count = $usbDevices.Count

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "Found $count historical USB storage device record(s) in USBSTOR" 'Cyan'
        foreach ($dev in $usbDevices) {
            Write-ReportLog $cat "DETAIL" ("  - [{0}] Serial: {1} (Name: {2})" -f $dev.DeviceGroup, $dev.SerialNumber, $dev.FriendlyName) 'DarkCyan'
        }
        $null = Add-AuditRecord $cat "Registry" $count 0 "High" "TracesFound" "$count historical USB storage devices detected" $usbDevices
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would clean up $count USB device entries under $usbstorKey." 'Yellow'
        $null = Add-AuditRecord $cat "Registry" $count 0 "High" "DryRun" "Simulated purge of $count USBSTOR entries"
        return
    }

    if (-not (Test-IsAdministrator)) {
        Ensure-ElevatedPrivileges "USBSTOR registry cleanup requires Administrator rights"
    }

    $cleared = 0
    $deviceGroups = Get-ChildItem -Path $usbstorKey -ErrorAction SilentlyContinue
    foreach ($grp in $deviceGroups) {
        try {
            # Try to ensure Administrators have full control before deletion
            try {
                $acl = Get-Acl -LiteralPath $grp.PSPath -ErrorAction SilentlyContinue
                if ($acl) {
                    $admin = [System.Security.Principal.NTAccount]"Administrators"
                    $rule = [System.Security.AccessControl.RegistryAccessRule]::new($admin, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
                    $acl.SetAccessRule($rule)
                    Set-Acl -LiteralPath $grp.PSPath -AclObject $acl -ErrorAction SilentlyContinue
                }
            } catch { }

            Remove-Item -Path $grp.PSPath -Recurse -Force -ErrorAction Stop
            $cleared++
            Write-ReportLog $cat "CLEANED" "Removed USBSTOR record for $($grp.PSChildName)" 'Green'
        }
        catch {
            Write-ReportLog $cat "WARNING" "Enum permission lock on $($grp.PSChildName): $($_.Exception.Message)" 'DarkYellow'
        }
    }

    Write-ReportLog $cat "SUCCESS" "USBSTOR cleanup completed ($cleared device groups cleared)." 'Green'
    $null = Add-AuditRecord $cat "Registry" $cleared 0 "None" "Purged" "Cleared $cleared USBSTOR registry keys"
}

# ══════════════════════════════════════════════════════════════════════════
# 4. PLAINTEXT WI-FI PROFILES
# ══════════════════════════════════════════════════════════════════════════
function Invoke-WifiProfilesArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "WifiProfiles"

    $profilesList = [System.Collections.Generic.List[PSCustomObject]]::new()
    $wlanOutput = netsh wlan show profiles 2>$null | Out-String

    $pattern = '(?i)(?:All User Profile|所有使用者設定檔|所有用户配置文件|すべてのユーザー プロファイル|Alle Benutzerprofile)\s*[:：]\s*(.*)$'
    $matches = [regex]::Matches($wlanOutput, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)

    foreach ($m in $matches) {
        $ssid = $m.Groups[1].Value.Trim()
        if (-not [string]::IsNullOrEmpty($ssid)) {
            $keyClear = "None (Open)"
            $auth = "Unknown"
            $detail = netsh wlan show profile name="$ssid" key=clear 2>$null | Out-String

            $keyMatch = [regex]::Match($detail, '(?i)(?:Key Content|金鑰內容|密钥内容|主要なコンテンツ|Schlüsselinhalt)\s*[:：]\s*(.*)$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
            if ($keyMatch.Success) {
                $keyClear = $keyMatch.Groups[1].Value.Trim()
            }

            $authMatch = [regex]::Match($detail, '(?i)(?:Authentication|驗證|身份验证|認証|Authentifizierung)\s*[:：]\s*(.*)$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
            if ($authMatch.Success) {
                $auth = $authMatch.Groups[1].Value.Trim()
            }

            $profilesList.Add([PSCustomObject]@{
                SSID           = $ssid
                Authentication = $auth
                KeyContent     = $keyClear
            })
        }
    }

    $count = $profilesList.Count

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "Found $count stored Wi-Fi profiles with cleartext key capability" 'Cyan'
        foreach ($p in $profilesList) {
            $maskedKey = if ($p.KeyContent -eq "None (Open)") { $p.KeyContent } else { $p.KeyContent.Substring(0, [math]::Min(2, $p.KeyContent.Length)) + '****' }
            Write-ReportLog $cat "DETAIL" ("  - SSID: {0,-20} Auth: {1,-12} Key: {2}" -f $p.SSID, $p.Authentication, $maskedKey) 'DarkCyan'
        }
        $null = Add-AuditRecord $cat "Network" $count 0 "Critical" "TracesFound" "$count saved Wi-Fi profiles with recoverably stored keys" $profilesList
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would delete $count Wi-Fi profile(s) via 'netsh wlan delete profile'." 'Yellow'
        $null = Add-AuditRecord $cat "Network" $count 0 "Critical" "DryRun" "Simulated deletion of $count Wi-Fi profiles"
        return
    }

    $deleted = 0
    foreach ($p in $profilesList) {
        try {
            $delRes = netsh wlan delete profile name="$($p.SSID)" 2>&1 | Out-String
            if ($LASTEXITCODE -eq 0 -or $delRes -match 'deleted') {
                $deleted++
                Write-ReportLog $cat "DELETED" "Forgot Wi-Fi network profile: $($p.SSID)" 'Green'
            }
            else {
                Write-ReportLog $cat "WARNING" "Could not delete $($p.SSID): $delRes" 'DarkYellow'
            }
        }
        catch {
            Write-ReportLog $cat "ERROR" "Failed to delete $($p.SSID): $($_.Exception.Message)" 'Red'
        }
    }

    Write-ReportLog $cat "SUCCESS" "Wi-Fi profile cleanup finished ($deleted/$count profiles deleted)." 'Green'
    $null = Add-AuditRecord $cat "Network" $deleted 0 "None" "Purged" "Deleted $deleted stored Wi-Fi profiles"
}

# ══════════════════════════════════════════════════════════════════════════
# 5. CLIPBOARD HISTORY
# ══════════════════════════════════════════════════════════════════════════
function Invoke-ClipboardHistoryArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "ClipboardHistory"
    $clipRegPath = "HKCU:\Software\Microsoft\Clipboard"

    $isEnabled = $false
    if (Test-Path $clipRegPath) {
        $val = (Get-ItemProperty -Path $clipRegPath -Name "EnableClipboardHistory" -ErrorAction SilentlyContinue).EnableClipboardHistory
        if ($val -eq 1) { $isEnabled = $true }
    }

    $itemCountVal = $(if ($isEnabled) { 1 } else { 0 })
    $riskVal = $(if ($isEnabled) { "Medium" } else { "Low" })

    if (-not $Purge) {
        $statusStr = if ($isEnabled) { "ENABLED (Win+V buffer active)" } else { "DISABLED" }
        Write-ReportLog $cat "AUDIT" "Clipboard history feature status: $statusStr" 'Cyan'
        $null = Add-AuditRecord $cat "SystemBuffer" $itemCountVal 0 $riskVal "TracesFound" "Clipboard history: $statusStr"
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would clear current clipboard, purge Win10/11 history buffer, and set EnableClipboardHistory=0." 'Yellow'
        $null = Add-AuditRecord $cat "SystemBuffer" 1 0 "Medium" "DryRun" "Simulated clipboard and history purge"
        return
    }

    # 1. Clear standard clipboard
    try {
        [System.Windows.Forms.Clipboard]::Clear()
    }
    catch {
        cmd.exe /c "echo off | clip" 2>$null
    }

    # 2. Clear WinRT Clipboard history buffer if supported
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction SilentlyContinue
        [Windows.ApplicationModel.DataTransfer.Clipboard, Windows.ApplicationModel.DataTransfer, ContentType = WindowsRuntime]::ClearHistory() | Out-Null
        Write-ReportLog $cat "PURGED" "WinRT Clipboard History buffer purged via API." 'Green'
    }
    catch {
        Write-ReportLog $cat "DETAIL" "WinRT ClearHistory fallback applied." 'DarkCyan'
    }

    # 3. Disable Clipboard History in Registry
    if (-not (Test-Path $clipRegPath)) {
        New-Item -Path $clipRegPath -Force | Out-Null
    }
    Set-ItemProperty -Path $clipRegPath -Name "EnableClipboardHistory" -Value 0 -Type DWord -Force
    Write-ReportLog $cat "SUCCESS" "Clipboard cleared and EnableClipboardHistory set to 0 (Disabled)." 'Green'
    $null = Add-AuditRecord $cat "SystemBuffer" 1 0 "None" "Purged" "Cleared clipboard buffer and disabled Clipboard History."
}

# ══════════════════════════════════════════════════════════════════════════
# 6. DISK CLEANUP AUTOMATION (SageSet / SageRun)
# ══════════════════════════════════════════════════════════════════════════
function Invoke-DiskCleanupArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "DiskCleanup"
    $sageSetNumber = 64
    $volumeCachesPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches"

    $configuredCaches = [System.Collections.Generic.List[string]]::new()
    if (Test-Path $volumeCachesPath) {
        $keys = Get-ChildItem -Path $volumeCachesPath -ErrorAction SilentlyContinue
        foreach ($k in $keys) {
            $flag = (Get-ItemProperty -Path $k.PSPath -Name "StateFlags00$sageSetNumber" -ErrorAction SilentlyContinue)."StateFlags00$sageSetNumber"
            if ($flag -eq 2) {
                $configuredCaches.Add($k.PSChildName)
            }
        }
    }

    $count = $configuredCaches.Count

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "VolumeCaches SageSet $sageSetNumber has $count active cleanup targets configured" 'Cyan'
        $null = Add-AuditRecord $cat "Automation" $count 0 "Low" "Audit" "SageSet $sageSetNumber configured for $count targets" $configuredCaches
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would configure SageSet 00$sageSetNumber for Thumbnails and Temp files, then execute 'cleanmgr.exe /sagerun:$sageSetNumber'." 'Yellow'
        $null = Add-AuditRecord $cat "Automation" 1 0 "Low" "DryRun" "Simulated silent cleanmgr /sagerun:$sageSetNumber execution"
        return
    }

    if (-not (Test-IsAdministrator)) {
        Ensure-ElevatedPrivileges "Configuring cleanmgr SageSet requires Administrator privileges"
    }

    # Automatically enable state flags for Thumbnail Cache & Temp files
    $targetCaches = @('Thumbnail Cache', 'Temporary Files', 'Temporary Setup Files', 'Active Setup Temp Folders')
    foreach ($cacheName in $targetCaches) {
        $cachePath = Join-Path $volumeCachesPath $cacheName
        if (Test-Path $cachePath) {
            Set-ItemProperty -Path $cachePath -Name "StateFlags00$sageSetNumber" -Value 2 -Type DWord -Force -ErrorAction SilentlyContinue
        }
    }

    Write-ReportLog $cat "PURGE" "Launching cleanmgr.exe /sagerun:$sageSetNumber silently..." 'Magenta'
    $cleanmgrProcess = Start-Process cleanmgr.exe -ArgumentList "/sagerun:$sageSetNumber" -PassThru -WindowStyle Hidden
    $cleanmgrProcess.WaitForExit(30000)

    Write-ReportLog $cat "SUCCESS" "Silent cleanmgr execution completed (SageSet $sageSetNumber)." 'Green'
    $null = Add-AuditRecord $cat "Automation" $targetCaches.Count 0 "None" "Purged" "Configured and ran cleanmgr /sagerun:$sageSetNumber"
}

# ══════════════════════════════════════════════════════════════════════════
# 7. RUN DIALOG HISTORY (RunMRU)
# ══════════════════════════════════════════════════════════════════════════
function Invoke-RunMruArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "RunMru"
    $runMruKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU"

    if (-not (Test-Path $runMruKey)) {
        Write-ReportLog $cat "AUDIT" "RunMRU history is clean or key not found." 'Green'
        $null = Add-AuditRecord $cat "Registry" 0 0 "None" "Clean" "RunMRU registry key empty or not present."
        return
    }

    $prop = Get-ItemProperty -Path $runMruKey -ErrorAction SilentlyContinue
    $mruList = $prop.MRUList
    $historyEntries = [System.Collections.Generic.List[PSCustomObject]]::new()

    if ($prop) {
        foreach ($p in $prop.PSObject.Properties) {
            if ($p.Name -notmatch '^(\(default\)|PSPath|PSParentPath|PSChildName|PSDrive|PSProvider|MRUList)$') {
                $historyEntries.Add([PSCustomObject]@{
                    Slot    = $p.Name
                    Command = $p.Value
                })
            }
        }
    }

    $count = $historyEntries.Count

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "Found $count Run dialog history command(s) (MRUList: '$mruList')" 'Cyan'
        foreach ($e in $historyEntries) {
            Write-ReportLog $cat "DETAIL" ("  - Slot {0}: {1}" -f $e.Slot, $e.Command) 'DarkCyan'
        }
        $null = Add-AuditRecord $cat "Registry" $count 0 "Medium" "TracesFound" "$count Run dialog commands stored in RunMRU" $historyEntries
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would delete $count command entries (a, b, c...) and MRUList while preserving (Default)." 'Yellow'
        $null = Add-AuditRecord $cat "Registry" $count 0 "Medium" "DryRun" "Simulated purge of $count RunMRU values"
        return
    }

    foreach ($e in $historyEntries) {
        try {
            Remove-ItemProperty -Path $runMruKey -Name $e.Slot -Force -ErrorAction SilentlyContinue
        } catch { }
    }
    Remove-ItemProperty -Path $runMruKey -Name "MRUList" -Force -ErrorAction SilentlyContinue

    Write-ReportLog $cat "SUCCESS" "RunMRU history purged ($count command entries removed, (Default) preserved)." 'Green'
    $null = Add-AuditRecord $cat "Registry" $count 0 "None" "Purged" "Purged $count RunMRU entries and MRUList"
}

# ══════════════════════════════════════════════════════════════════════════
# 8. RECENT FILES & JUMP LISTS
# ══════════════════════════════════════════════════════════════════════════
function Invoke-RecentFilesArtifact {
    param([bool]$Purge, [bool]$Simulate)
    $cat = "RecentFiles"
    $recentDir = Join-Path $env:APPDATA "Microsoft\Windows\Recent"
    $autoDestDir = Join-Path $recentDir "AutomaticDestinations"
    $customDestDir = Join-Path $recentDir "CustomDestinations"

    $recentFiles = if (Test-Path $recentDir) { Get-ChildItem -Path $recentDir -File -Filter "*.lnk" -ErrorAction SilentlyContinue } else { @() }
    $autoDestFiles = if (Test-Path $autoDestDir) { Get-ChildItem -Path $autoDestDir -File -ErrorAction SilentlyContinue } else { @() }
    $customDestFiles = if (Test-Path $customDestDir) { Get-ChildItem -Path $customDestDir -File -ErrorAction SilentlyContinue } else { @() }

    $allRecent = @($recentFiles) + @($autoDestFiles) + @($customDestFiles)
    $count = $allRecent.Count
    $totalBytes = ($allRecent | Measure-Object -Property Length -Sum).Sum
    if (-not $totalBytes) { $totalBytes = 0 }
    $sizeKb = [math]::Round($totalBytes / 1KB, 2)

    if (-not $Purge) {
        Write-ReportLog $cat "AUDIT" "Found $count recent items ($sizeKb KB): $($recentFiles.Count) LNK shortcuts, $($autoDestFiles.Count) JumpList auto-destinations, $($customDestFiles.Count) custom-destinations" 'Cyan'
        $null = Add-AuditRecord $cat "Filesystem" $count $totalBytes "High" "TracesFound" "$count recent file shortcuts & Jump Lists ($sizeKb KB)" $allRecent.Name
        return
    }

    if ($Simulate) {
        Write-ReportLog $cat "DRYRUN" "[Simulate] Would clear $count recent shortcuts and Jump Lists, and configure Explorer policies to disable recent files." 'Yellow'
        $null = Add-AuditRecord $cat "Filesystem" $count $totalBytes "High" "DryRun" "Simulated purge of $count recent items"
        return
    }

    $deleted = 0
    foreach ($f in $allRecent) {
        try {
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
            $deleted++
        } catch { }
    }

    # Configure File Explorer policies to disable Show recently used files & Show frequently used folders
    $explorerAdvancedKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
    $policiesExplorerKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"

    if (Test-Path $explorerAdvancedKey) {
        Set-ItemProperty -Path $explorerAdvancedKey -Name "Start_TrackDocs" -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $explorerAdvancedKey -Name "ShowRecent" -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $explorerAdvancedKey -Name "ShowFrequent" -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $policiesExplorerKey)) {
        New-Item -Path $policiesExplorerKey -Force | Out-Null
    }
    Set-ItemProperty -Path $policiesExplorerKey -Name "NoRecentDocsHistory" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue

    Write-ReportLog $cat "SUCCESS" "Recent files cleared ($deleted items removed), and Explorer policies updated to disable recent history." 'Green'
    $null = Add-AuditRecord $cat "Filesystem" $deleted $totalBytes "None" "Purged" "Cleared $deleted recent files & JumpLists; disabled history policies"
}

# ══════════════════════════════════════════════════════════════════════════
# DISPATCHER CONTROLLER
# ══════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "       DiskMaster Pro -- Modular Anti-Forensics Privacy Engine    " -ForegroundColor Cyan
Write-Host ("       Mode: {0} | DryRun: {1}" -f $Mode.ToUpper(), $DryRun) -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$targetMapping = @{
    'ThumbnailCache'   = ${function:Invoke-ThumbnailCacheArtifact}
    'Prefetch'         = ${function:Invoke-PrefetchArtifact}
    'UsbHistory'       = ${function:Invoke-UsbStorageArtifact}
    'WifiProfiles'     = ${function:Invoke-WifiProfilesArtifact}
    'ClipboardHistory' = ${function:Invoke-ClipboardHistoryArtifact}
    'DiskCleanup'      = ${function:Invoke-DiskCleanupArtifact}
    'RunMru'           = ${function:Invoke-RunMruArtifact}
    'RecentFiles'      = ${function:Invoke-RecentFilesArtifact}
}

foreach ($target in $selectedTargets) {
    if ($targetMapping.ContainsKey($target)) {
        & $targetMapping[$target] -Purge:$isPurge -Simulate:$DryRun
    }
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host (" Completed forensic operational pass: {0} ({1} target categories)" -f $Mode, $selectedTargets.Count) -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

if ($PassThru) {
    return $Global:ReportResults
}

# ── Exported Operational Functions & Aliases ──
function Audit {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string[]]$Targets = @('All'),
        [switch]$PassThru
    )
    $script = $PSCommandPath
    if (-not $script -or -not (Test-Path $script)) { $script = $MyInvocation.MyCommand.Definition }
    & $script -Mode Audit -Targets $Targets -PassThru:$PassThru
}
Set-Alias -Name Inspect -Value Audit -ErrorAction SilentlyContinue

function Purge {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string[]]$Targets = @('All'),
        [switch]$DryRun,
        [switch]$NoRestartExplorer,
        [switch]$PassThru
    )
    $script = $PSCommandPath
    if (-not $script -or -not (Test-Path $script)) { $script = $MyInvocation.MyCommand.Definition }
    & $script -Mode Purge -Targets $Targets -DryRun:$DryRun -NoRestartExplorer:$NoRestartExplorer -PassThru:$PassThru
}
Set-Alias -Name Clean -Value Purge -ErrorAction SilentlyContinue
