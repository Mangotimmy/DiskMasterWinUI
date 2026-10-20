# Phase 1: OS-linked Administrator/User account picker
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: account model and service contracts" -ForegroundColor Yellow
$itemType = $asm.GetType("DiskMasterWinUI.Models.LocalAccountItem")
Assert-NotNull $itemType "LocalAccountItem must exist"
Assert-NotNull ($itemType.GetProperty("AccountName")) "AccountName property must exist"
Assert-NotNull ($itemType.GetProperty("AccountType")) "AccountType property must exist"
Assert-NotNull ($itemType.GetProperty("Sid")) "Sid property must exist"
Assert-NotNull ($itemType.GetProperty("IsAdministrator")) "IsAdministrator property must exist"

$serviceType = $asm.GetType("DiskMasterWinUI.Services.LocalAccountService")
Assert-NotNull $serviceType "LocalAccountService must exist"
$getGroup = $serviceType.GetMethod("GetGroupMembersAsync")
Assert-NotNull $getGroup "GetGroupMembersAsync must exist"
Assert-Equal $getGroup.GetParameters().Count 3 "GetGroupMembersAsync must accept group, search and cancellation"
Assert-NotNull ($serviceType.GetMethod("GetAccountsAsync")) "GetAccountsAsync must exist"
Assert-NotNull ($serviceType.GetMethod("GetAccountAsync")) "GetAccountAsync must exist"
Assert-NotNull ($serviceType.GetMethod("InvalidateCache")) "InvalidateCache must exist"

Write-Host "Test 2: account picker contracts" -ForegroundColor Yellow
$pickerType = $asm.GetType("DiskMasterWinUI.Controls.AccountPickerPanel")
Assert-NotNull $pickerType "AccountPickerPanel must exist"
$selected = $pickerType.GetProperty("SelectedAccount")
Assert-NotNull $selected "SelectedAccount property must exist"
$event = $pickerType.GetEvent("SelectedAccountChanged")
Assert-NotNull $event "SelectedAccountChanged event must exist"

Write-Host "Test 3: progressive columns and page wiring" -ForegroundColor Yellow
$pickerXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\AccountPickerPanel.xaml"))
Assert-True ($pickerXaml.Contains("AccountPickerModeAdmin")) "Administrator scope control must exist"
Assert-True ($pickerXaml.Contains("AccountPickerModeUser")) "User scope control must exist"
Assert-True ($pickerXaml.Contains("AccountPickerList")) "Account columns list must exist"
Assert-True ($pickerXaml.Contains('Visibility="Collapsed"')) "Columns and list must start hidden"
Assert-True ($pickerXaml.Contains("Account") -and $pickerXaml.Contains("Type") -and $pickerXaml.Contains("SID")) "Account columns must be present"
$pickerCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\AccountPickerPanel.xaml.cs"))
Assert-True ($pickerCs.Contains("ContainerContentChanging")) "Account items must expose accessibility names"
Assert-True ($pickerCs.Contains("HeaderRow.Visibility")) "Header visibility must follow the account state"
$editorXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\ShareEditorDialog.xaml"))
Assert-True ($editorXaml.Contains("EditorAccountPicker")) "Share editor must host AccountPickerPanel"
$netCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\NetworkToolsPage.xaml.cs"))
Assert-True ($netCs.Contains("ShareEditorDialog")) "NetShare must open the share editor"
$ntfsXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\NtfsPermissionsPage.xaml"))
Assert-True ($ntfsXaml.Contains("BuilderAccountPicker")) "icacls builder must host AccountPickerPanel"
$ntfsCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\NtfsPermissionsPage.xaml.cs"))
Assert-True ($ntfsCs.Contains("ApplyAccountSelection")) "icacls builder must receive account selection"

Write-Host "Test 4: live local account query" -ForegroundColor Yellow
$service = [Activator]::CreateInstance($serviceType)
$task = $getGroup.Invoke($service, @("Users", $null, [Threading.CancellationToken]::None))
$completed = $task.Wait(15000)
Assert-True $completed "GetGroupMembersAsync(Users) must complete within 15s"
$users = $task.Result
Assert-NotNull $users "Users result must not be null"
Write-Host ("  -> Users returned: " + $users.Count) -ForegroundColor DarkGray

Write-Host "Phase 1 account picker tests PASSED." -ForegroundColor Green
