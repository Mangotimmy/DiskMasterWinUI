<#
.SYNOPSIS
    Interactive runtime smoke test for the HUD freeze/rendering fix and the Live Console mini-window.

.DESCRIPTION
    Launches the freshly built Release executable, drives it through UI Automation, and asserts:
      1. The HUD opens and exposes real metric text (not an empty DWM frame).
      2. Idling with the HUD open does not spam the debug log or rewrite settings.json (feedback loop).
      3. Click-through toggling logs exactly one action per toggle.
      4. The HUD settings window opens and renders its controls.
      5. The bottom-bar Console button opens the Live Console window and it renders rows.
      6. The app remains responsive throughout.

    Requires an interactive desktop. Skips cleanly when UI Automation cannot find the app.
    Backs up and restores %LOCALAPPDATA%\DiskMaster_Portable\settings.json.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptRoot
$exe = Join-Path $projectRoot "bin\$Configuration\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.exe"
$settingsPath = Join-Path $env:LOCALAPPDATA "DiskMaster_Portable\settings.json"
$logPath = Join-Path $env:LOCALAPPDATA "DiskMaster_Portable\Logs\diskmaster_debug.log"
$artifactDir = Join-Path $env:TEMP "DiskMaster_RuntimeTest"
$backupPath = Join-Path $artifactDir "settings.backup.json"

$script:failures = 0
$script:passes = 0
function Pass([string]$message) { $script:passes++; Write-Host "  [PASS] $message" -ForegroundColor Green }
function Fail([string]$message) { $script:failures++; Write-Host "  [FAIL] $message" -ForegroundColor Red }
function Assert-True([bool]$condition, [string]$message) { if ($condition) { Pass $message } else { Fail $message } }

if (-not (Test-Path $exe)) {
    Write-Host "Executable not found: $exe" -ForegroundColor Red
    exit 1
}

$existing = Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "DiskMasterWinUI is already running (PID $($existing.Id -join ',')). Close it before running this test." -ForegroundColor Yellow
    exit 0
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
if (Test-Path $settingsPath) { Copy-Item $settingsPath $backupPath -Force }
else { Set-Content -Path $backupPath -Value "" -Encoding UTF8 }

$process = $null
$mainWindow = $null
$logAllLines = @()
$loopBefore = 0
$settingsStampBefore = $null

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
} catch {
    Write-Host "UI Automation is unavailable; skipping interactive runtime test." -ForegroundColor Yellow
    exit 0
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
function Get-ProcessWindows {
    if (-not $script:process) { return @() }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:process.Id)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition))
}
function Find-WindowByTitle([string]$titleLike) {
    foreach ($window in Get-ProcessWindows) {
        if ($window.Current.Name -like $titleLike) { return $window }
    }
    return $null
}
function Find-ButtonById([System.Windows.Automation.AutomationElement]$rootElement, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    return $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-ElementById([System.Windows.Automation.AutomationElement]$rootElement, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-ForWindowLike([string]$titleLike, [int]$timeoutMs = 15000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        $window = Find-WindowByTitle $titleLike
        if ($window) { return $window }
        Start-Sleep -Milliseconds 250
    }
    return $null
}
function Get-WindowTexts([System.Windows.Automation.AutomationElement]$window) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $names = @()
    foreach ($text in $texts) {
        if ($text.Current.Name) { $names += $text.Current.Name }
    }
    return $names
}

Write-Host ""
Write-Host "[START] HUD + Live Console runtime smoke test" -ForegroundColor Cyan

try {
    $logAllLines = if (Test-Path $logPath) { [System.IO.File]::ReadAllLines($logPath) } else { @() }
    $loopBefore = @($logAllLines | Where-Object { $_ -match "已停用 HUD 滑鼠穿透模式" }).Count
    $settingsStampBefore = if (Test-Path $settingsPath) { (Get-Item $settingsPath).LastWriteTimeUtc } else { $null }

    $script:process = Start-Process -FilePath $exe -ArgumentList "--tab", "SystemPerformance" -PassThru
    $mainWindow = Wait-ForWindowLike "*DiskMaster Pro*" 20000
    if (-not $mainWindow) {
        Fail "Main window did not appear"
        throw "Main window not found"
    }
    Pass "Main window appeared"

    $hudButton = $null
    for ($i = 0; $i -lt 20 -and -not $hudButton; $i++) {
        $hudButton = Find-ButtonById $mainWindow "HudToggleButton"
        if (-not $hudButton) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $hudButton) {
        Fail "HudToggleButton was not found on the SystemPerformance page"
        throw "HUD button not found"
    }
    $hudButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $hud = Wait-ForWindowLike "*Performance HUD*" 8000
    Assert-True ($null -ne $hud) "HUD window exists"
    if ($hud) {
        $hudRect = $hud.Current.BoundingRectangle
        Write-Host ("  HUD size: {0}x{1}" -f [int]$hudRect.Width, [int]$hudRect.Height)
        Assert-True ($hudRect.Width -gt 200 -and $hudRect.Height -gt 60) "HUD window has a visible content-sized area"
        $hudTexts = Get-WindowTexts $hud
        Assert-True ($hudTexts.Count -ge 5) "HUD exposes metric text elements (count=$($hudTexts.Count))"
        Assert-True (($hudTexts -join " ") -match "CPU") "HUD shows CPU metric text"
        Assert-True (($hudTexts -join " ") -match "FPS") "HUD shows FPS metric text"
    }

    Start-Sleep -Seconds 5
    $logAllLines = [System.IO.File]::ReadAllLines($logPath)
    $loopAfter = @($logAllLines | Where-Object { $_ -match "已停用 HUD 滑鼠穿透模式" }).Count
    Assert-True (($loopAfter - $loopBefore) -eq 0) "Idle HUD does not spam the settings feedback loop"
    $settingsStampAfter = if (Test-Path $settingsPath) { (Get-Item $settingsPath).LastWriteTimeUtc } else { $null }
    Assert-True ($settingsStampBefore -eq $settingsStampAfter) "Idle HUD does not rewrite settings.json"
    Assert-True ((Get-Process -Id $process.Id).Responding) "App is responsive with the HUD open"

    $clickThroughButton = Find-ButtonById $mainWindow "HudClickThroughBtn"
    Assert-True ($null -ne $clickThroughButton) "Click-through button is available"
    if ($clickThroughButton) {
        $enableBefore = @([System.IO.File]::ReadAllLines($logPath) | Where-Object { $_ -match "已啟用 HUD 滑鼠穿透模式" }).Count
        $clickThroughButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 2
        $enableAfter = @([System.IO.File]::ReadAllLines($logPath) | Where-Object { $_ -match "已啟用 HUD 滑鼠穿透模式" }).Count
        Assert-True (($enableAfter - $enableBefore) -eq 1) "Enabling click-through logs exactly once (no loop)"

        $clickThroughButton = Find-ButtonById $mainWindow "HudClickThroughBtn"
        if ($clickThroughButton) {
            $disableBefore = @([System.IO.File]::ReadAllLines($logPath) | Where-Object { $_ -match "已停用 HUD 滑鼠穿透模式" }).Count
            $clickThroughButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Seconds 2
            $disableAfter = @([System.IO.File]::ReadAllLines($logPath) | Where-Object { $_ -match "已停用 HUD 滑鼠穿透模式" }).Count
            Assert-True (($disableAfter - $disableBefore) -eq 1) "Disabling click-through logs exactly once (no loop)"
        }
    }

    $styleMenuButton = Find-ElementById $mainWindow "HudStyleMenuButton"
    Assert-True ($null -ne $styleMenuButton) "HUD style menu button is available"
    if ($styleMenuButton) {
        $styleMenuButton.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 700
        $settingsMenuItem = $null
        for ($i = 0; $i -lt 8 -and -not $settingsMenuItem; $i++) {
            foreach ($window in Get-ProcessWindows) {
                $settingsMenuItem = Find-ElementById $window "OpenHudSettingsMenuItem"
                if ($settingsMenuItem) { break }
            }
            if (-not $settingsMenuItem) { Start-Sleep -Milliseconds 400 }
        }
        Assert-True ($null -ne $settingsMenuItem) "HUD settings menu item is available"
        if ($settingsMenuItem) {
            $settingsMenuItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $opacityBefore = if (Test-Path $settingsPath) { (Get-Content $settingsPath -Raw | ConvertFrom-Json).HudOpacity } else { $null }
            $settingsWindow = Wait-ForWindowLike "*HUD*設定*" 6000
            Assert-True ($null -ne $settingsWindow) "HUD settings window opened"
            if ($settingsWindow) {
                Start-Sleep -Milliseconds 500
                $opacityAfter = if (Test-Path $settingsPath) { (Get-Content $settingsPath -Raw | ConvertFrom-Json).HudOpacity } else { $null }
                Assert-True ($opacityBefore -eq $opacityAfter) "Opening HUD settings does not overwrite the saved opacity"
                $settingsTexts = Get-WindowTexts $settingsWindow
                Assert-True ($settingsTexts.Count -ge 3) "HUD settings window renders its controls (count=$($settingsTexts.Count))"
                try { $settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
            }
        }
    }

    $consoleButton = Find-ButtonById $mainWindow "ConsoleButton"
    Assert-True ($null -ne $consoleButton) "Bottom-bar Console button is present while idle"
    if ($consoleButton) {
        $consoleButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $consoleWindow = Wait-ForWindowLike "*Live Console*" 6000
        Assert-True ($null -ne $consoleWindow) "Live Console window opened"
        if ($consoleWindow) {
            $consoleTexts = Get-WindowTexts $consoleWindow
            Assert-True ($consoleTexts.Count -ge 2) "Live Console renders toolbar/footer text"
            try { $consoleWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
        }
    }

    # Navigation stress: Easy Mode <-> System Performance used to trigger the heavy settings handler
    # on every tab-away (the 03:04 freeze). Run several cycles and assert no multi-second UI stalls.
    $tabItemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    $tabItems = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabItemCondition)
    $easyTab = $null
    $performanceTab = $null
    foreach ($tab in $tabItems) {
        if ($tab.Current.Name -like "*簡易模式*") { $easyTab = $tab }
        if ($tab.Current.Name -like "*效能分析*") { $performanceTab = $tab }
    }
    Assert-True ($null -ne $easyTab -and $null -ne $performanceTab) "Easy Mode and System Performance tabs are available"
    if ($easyTab -and $performanceTab) {
        $stallsBefore = @([System.IO.File]::ReadAllLines($logPath) | Where-Object { $_ -match "UI thread stalled" }).Count
        for ($i = 0; $i -lt 5; $i++) {
            $easyTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 350
            $performanceTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 350
        }
        $stallsAfter = @([System.IO.File]::ReadAllLines($logPath) | Where-Object { $_ -match "UI thread stalled" }).Count
        Assert-True (($stallsAfter - $stallsBefore) -eq 0) "Repeated Easy Mode navigation causes no UI-thread stalls"
    }

    if ($hud) {
        try { $hud.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    }

    Assert-True ((Get-Process -Id $process.Id).Responding) "App remains responsive after all HUD/console actions"
}
catch {
    Fail ("Runtime test exception: " + $_.Exception.Message)
}
finally {
    try {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    } catch { }

    try {
        if (Test-Path $backupPath) {
            if ((Get-Item $backupPath).Length -gt 0) { Copy-Item $backupPath $settingsPath -Force }
            else { Remove-Item $settingsPath -Force -ErrorAction SilentlyContinue }
        }
    } catch { }
}

Write-Host ""
Write-Host ("[RESULT] Passed: {0}  Failed: {1}" -f $passes, $failures) -ForegroundColor Cyan
if ($failures -eq 0) {
    Write-Host "[COMPLETED] HUD and Live Console runtime smoke test PASSED." -ForegroundColor Green
    exit 0
}

Write-Host "[FAILED] HUD and Live Console runtime smoke test FAILED." -ForegroundColor Red
exit 1
