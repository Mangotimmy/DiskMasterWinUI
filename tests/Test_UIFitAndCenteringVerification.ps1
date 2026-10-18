# Test_UIFitAndCenteringVerification.ps1
$ErrorActionPreference = "Stop"
Write-Host "=== Testing UI Fit & Centering Across WinUI Pages ===" -ForegroundColor Cyan

$script:passed = 0
$script:failed = 0

function Assert-Contains($filePath, $pattern, $testName) {
    $fullPath = Join-Path $PSScriptRoot "..\$filePath"
    if (-not (Test-Path $fullPath)) {
        Write-Host "  [FAIL] $testName (File not found: $fullPath)" -ForegroundColor Red
        $script:failed++
        return
    }
    $content = Get-Content $fullPath -Raw
    if ($content -match $pattern) {
        Write-Host "  [PASS] $testName" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  [FAIL] $testName (Pattern: $pattern not matched in $filePath)" -ForegroundColor Red
        $script:failed++
    }
}

# 1. SettingsPage Centering & Stretch
Assert-Contains "Pages/SettingsPage.xaml" 'HorizontalContentAlignment="Stretch"' "SettingsPage ScrollViewer has HorizontalContentAlignment=Stretch"
Assert-Contains "Pages/SettingsPage.xaml" 'HorizontalAlignment="Center" MaxWidth="1200"' "SettingsPage StackPanel has HorizontalAlignment=Center MaxWidth=1200"

# 2. StarterPage Centering & Stretch
Assert-Contains "Pages/StarterPage.xaml" 'HorizontalContentAlignment="Stretch"' "StarterPage ScrollViewer has HorizontalContentAlignment=Stretch"
Assert-Contains "Pages/StarterPage.xaml" 'HorizontalAlignment="Center" MaxWidth="1200"' "StarterPage StackPanel has HorizontalAlignment=Center MaxWidth=1200"

# 3. GroupPolicyPage Centering & Stretch
Assert-Contains "Pages/GroupPolicyPage.xaml" 'HorizontalContentAlignment="Stretch"' "GroupPolicyPage ScrollViewer has HorizontalContentAlignment=Stretch"
Assert-Contains "Pages/GroupPolicyPage.xaml" 'HorizontalAlignment="Center" MaxWidth="1200"' "GroupPolicyPage StackPanel has HorizontalAlignment=Center MaxWidth=1200"

# 4. SystemOptimizerPage TabView TabWidthMode
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'TabWidthMode="SizeToContent"' "OptimizerTabView has TabWidthMode=SizeToContent"

# 5. SystemOptimizerPage Card 1.5 Responsive 3 Columns TextWrapping
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'nonetcrawling[^>]+TextWrapping="Wrap"' "Card 1.5 nonetcrawling caption has TextWrapping=Wrap"
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'SeparateProcess[^>]+TextWrapping="Wrap"' "Card 1.5 SeparateProcess caption has TextWrapping=Wrap"
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'TaskbarAnimations[^>]+TextWrapping="Wrap"' "Card 1.5 TaskbarAnimations caption has TextWrapping=Wrap"
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'ShowCopilotButton[^>]+TextWrapping="Wrap"' "Card 1.5 ShowCopilotButton caption has TextWrapping=Wrap"
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'Start_Iris[^>]+TextWrapping="Wrap"' "Card 1.5 Start_Iris caption has TextWrapping=Wrap"

# 6. SystemOptimizerPage Card 2 Right-Click Menu Wrap
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'SwitchToClassicContextMenuCommand[\s\S]*?TextWrapping="Wrap"' "Card 2 SwitchToClassicContextMenu button content has TextWrapping=Wrap"
Assert-Contains "Pages/SystemOptimizerPage.xaml" 'FixSlowRightClickMenuCommand[\s\S]*?TextWrapping="Wrap"' "Card 2 FixSlowRightClickMenu button content has TextWrapping=Wrap"

$color = if ($script:failed -eq 0) { "Green" } else { "Red" }
Write-Host "`nTest Summary: Passed: $($script:passed), Failed: $($script:failed)" -ForegroundColor $color
if ($script:failed -gt 0) {
    exit 1
}
