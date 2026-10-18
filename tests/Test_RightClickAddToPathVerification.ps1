# Right-Click Add to PATH Environment Verification Script
$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$harnessDir = Join-Path $scriptRoot "harness"
. (Join-Path $harnessDir "TestFramework.ps1")

Write-Host "`n[START] Verifying Right-Click Add to PATH Environment Feature..." -ForegroundColor Cyan

$asm = Initialize-TestAssembly -Configuration "Debug"
Assert-NotNull $asm "Target assembly must be loaded"

# Test 1: Verify ShellIntegrationService class and constants
Write-Host "Test 1: Verifying ShellIntegrationService constants and properties..." -ForegroundColor Yellow
$shellServiceType = $asm.GetType("DiskMasterWinUI.Services.ShellIntegrationService")
Assert-NotNull $shellServiceType "ShellIntegrationService type must exist"

$verbKeyField = $shellServiceType.GetField("VerbAddToPathKeyName", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $verbKeyField "VerbAddToPathKeyName field must exist"
$verbKeyValue = $verbKeyField.GetValue($null)
Assert-Equal "DiskMasterAddToPath" $verbKeyValue "VerbAddToPathKeyName must equal 'DiskMasterAddToPath'"

$classPathsField = $shellServiceType.GetField("ExtendedAddToPathClassPaths", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $classPathsField "ExtendedAddToPathClassPaths field must exist"
$classPathsValue = $classPathsField.GetValue($null)
Assert-NotNull $classPathsValue "ExtendedAddToPathClassPaths must not be null"
Assert-Equal 3 $classPathsValue.Length "ExtendedAddToPathClassPaths must cover Directory, Directory\Background, and Drive"

Assert-True ($classPathsValue[0] -like "*Directory\shell\*") "Index 0 must be Directory\shell"
Assert-True ($classPathsValue[1] -like "*Directory\Background\shell\*") "Index 1 must be Directory\Background\shell"
Assert-True ($classPathsValue[2] -like "*Drive\shell\*") "Index 2 must be Drive\shell"
Write-Host "  -> PASS: ShellIntegrationService constants verified." -ForegroundColor Green

# Test 2: Verify InstallExtended method signature has enableAddToPath parameter
Write-Host "Test 2: Verifying InstallExtended method signature..." -ForegroundColor Yellow
$installMethod = $shellServiceType.GetMethod("InstallExtended")
Assert-NotNull $installMethod "InstallExtended method must exist"
$params = $installMethod.GetParameters()
Assert-Equal 7 $params.Length "InstallExtended must have 7 parameters"
Assert-Equal "enableAddToPath" $params[6].Name "7th parameter must be named 'enableAddToPath'"
Assert-Equal ([System.Boolean]) $params[6].ParameterType "enableAddToPath parameter must be boolean"
Assert-True $params[6].HasDefaultValue "enableAddToPath must have a default value"
Assert-True ([bool]$params[6].DefaultValue) "enableAddToPath default value must be true"
Write-Host "  -> PASS: InstallExtended signature verified." -ForegroundColor Green

# Test 3: Verify EnvironmentCheckService.AddPathDirectoryDirect logic
Write-Host "Test 3: Verifying EnvironmentCheckService.AddPathDirectoryDirect..." -ForegroundColor Yellow
$envServiceType = $asm.GetType("DiskMasterWinUI.Services.EnvironmentCheckService")
Assert-NotNull $envServiceType "EnvironmentCheckService type must exist"

$directMethod = $envServiceType.GetMethod("AddPathDirectoryDirect", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
Assert-NotNull $directMethod "AddPathDirectoryDirect static method must exist"

# 3a. Empty path test
$resEmpty = $directMethod.Invoke($null, @("", [System.EnvironmentVariableTarget]::User))
Assert-False $resEmpty.Item1 "Empty path must fail"

# 3b. Add a unique test folder to User PATH
$testGuid = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
$testFolder = "C:\DiskMaster_Test_Path_$testGuid"
$resAdd = $directMethod.Invoke($null, @($testFolder, [System.EnvironmentVariableTarget]::User))
Assert-True $resAdd.Item1 "Adding test folder to User PATH must succeed"

# 3c. Duplicate add test
$resDup = $directMethod.Invoke($null, @($testFolder, [System.EnvironmentVariableTarget]::User))
Assert-False $resDup.Item1 "Adding duplicate folder to User PATH must fail"

# 3d. File path normalization test: when a file path is passed, parent folder is extracted
$testFile = [System.IO.Path]::Combine($testFolder, "dummy.exe")
$resFileDup = $directMethod.Invoke($null, @($testFile, [System.EnvironmentVariableTarget]::User))
Assert-False $resFileDup.Item1 "Passing a file inside an already-added directory must recognize folder duplicate"

# Cleanup test directory from User PATH
$envInst = [System.Activator]::CreateInstance($envServiceType)
$removeMethod = $envServiceType.GetMethod("RemovePathDirectoryAsync")
$task = $removeMethod.Invoke($envInst, @($testFolder, [System.EnvironmentVariableTarget]::User))
$task.Wait()

# 3e. Verify cleanup succeeded
$userPath = [System.Environment]::GetEnvironmentVariable("Path", [System.EnvironmentVariableTarget]::User)
Assert-False ($userPath.Contains($testFolder)) "Test folder must be cleaned up from User PATH"
Write-Host "  -> PASS: AddPathDirectoryDirect normalization, validation, and deduplication verified." -ForegroundColor Green

# Test 4: Verify SettingsViewModel and SettingsService properties
Write-Host "Test 4: Verifying SettingsViewModel and SettingsService ShellEnableAddToPath..." -ForegroundColor Yellow
$settingsServiceType = $asm.GetType("DiskMasterWinUI.Services.SettingsService")
$settingsType = $asm.GetType("DiskMasterWinUI.Services.AppSettings")
$appSettingsProp = $settingsType.GetProperty("ShellEnableAddToPath")
Assert-NotNull $appSettingsProp "AppSettings.ShellEnableAddToPath must exist"

$settingsVmType = $asm.GetType("DiskMasterWinUI.ViewModels.SettingsViewModel")
$vmProp = $settingsVmType.GetProperty("ShellEnableAddToPath")
Assert-NotNull $vmProp "SettingsViewModel.ShellEnableAddToPath property must exist"

$vmAvailProp = $settingsVmType.GetProperty("IsShellIntegrationAvailable")
Assert-NotNull $vmAvailProp "SettingsViewModel.IsShellIntegrationAvailable must exist"
Write-Host "  -> PASS: SettingsViewModel and AppSettings properties verified." -ForegroundColor Green

# Test 5: Verify XAML bindings in SettingsPage.xaml and DiskToolsPage.xaml
Write-Host "Test 5: Verifying XAML bindings..." -ForegroundColor Yellow
$settingsXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\SettingsPage.xaml"))
Assert-True ($settingsXaml.Contains("ViewModel.ShellEnableAddToPath")) "SettingsPage.xaml must bind ViewModel.ShellEnableAddToPath"

$diskToolsXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\DiskToolsPage.xaml"))
Assert-True ($diskToolsXaml.Contains("AddPathToUserScope_Click")) "DiskToolsPage.xaml must have AddPathToUserScope_Click"
Assert-True ($diskToolsXaml.Contains("AddPathToMachineScope_Click")) "DiskToolsPage.xaml must have AddPathToMachineScope_Click"
Assert-True ($diskToolsXaml.Contains("LargeFileAddToUserPath_Click")) "DiskToolsPage.xaml must have LargeFileAddToUserPath_Click"
Assert-True ($diskToolsXaml.Contains("LargeFileAddToSystemPath_Click")) "DiskToolsPage.xaml must have LargeFileAddToSystemPath_Click"
Write-Host "  -> PASS: XAML context menus and bindings verified." -ForegroundColor Green

Write-Host "`n[SUCCESS] All Right-Click Add to PATH Environment tests passed!`n" -ForegroundColor Green
