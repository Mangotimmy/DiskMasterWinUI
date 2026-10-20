# Phase 2: share permissions, details, ACL status and finish dialogs
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: share permission API contracts" -ForegroundColor Yellow
$shareType = $asm.GetType("DiskMasterWinUI.Services.NetShareService")
Assert-NotNull $shareType "NetShareService must exist"
foreach ($name in "GetShareDetailsAsync","GetShareAclAsync","GrantSharePermissionAsync","RevokeSharePermissionAsync","CreateShareWithPermissionAsync","GetPathAclAsync") {
  Assert-NotNull ($shareType.GetMethod($name)) "$name must exist"
}
$aclType = $asm.GetType("DiskMasterWinUI.Models.ShareAclItem")
Assert-NotNull $aclType "ShareAclItem must exist"
$detailsType = $asm.GetType("DiskMasterWinUI.Models.ShareDetailsItem")
Assert-NotNull $detailsType "ShareDetailsItem must exist"
$ntfsType = $asm.GetType("DiskMasterWinUI.Services.NtfsPermissionService")
Assert-NotNull ($ntfsType.GetMethod("InspectNtfsFeaturesAsync")) "NTFS inspection must exist"

Write-Host "Test 2: finish dialog and ACL status wiring" -ForegroundColor Yellow
$dialogCs = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Helpers\DialogHelper.cs"))
Assert-True ($dialogCs.Contains("ShowOperationResultAsync")) "Finish dialog helper must exist"
$netVm = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\NetworkToolsViewModel.cs"))
Assert-True ($netVm.Contains("ShowShareResultAsync")) "NetShare commands must show finish dialogs"
Assert-True ($netVm.Contains("GrantSharePermissionAsync")) "NetShare grant must be wired"
Assert-True ($netVm.Contains("RevokeSharePermissionAsync")) "NetShare revoke must be wired"
Assert-True ($netVm.Contains("CreateShareWithPermissionAsync")) "Create share must use the selected account"
$ntfsVm = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\ViewModels\NtfsPermissionsViewModel.cs"))
Assert-True ($ntfsVm.Contains("RefreshAclStatusAsync")) "ACL status refresh must exist"
Assert-True ($ntfsVm.Contains("AclStatusText")) "ACL status text must exist"
Assert-True ($ntfsVm.Contains("ShowResultAsync")) "icacls actions must show finish dialogs"
$netXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\NetworkToolsPage.xaml"))
Assert-True ($netXaml.Contains("Manage access and options")) "NetShare must expose the share editor"
Assert-True ($netXaml.Contains("Share details")) "NetShare details pane must exist"
$editorXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Controls\ShareEditorDialog.xaml"))
Assert-True ($editorXaml.Contains("People with access")) "Share editor must manage people and access"
$ntfsXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\Pages\NtfsPermissionsPage.xaml"))
Assert-True ($ntfsXaml.Contains("目前權限狀態")) "Current permission status card must exist"
Assert-True ($ntfsXaml.Contains("Current ACL entries")) "Current ACL list must exist"

Write-Host "Test 3: live share details and ACL status" -ForegroundColor Yellow
$share = [Activator]::CreateInstance($shareType)
$getShares = $shareType.GetMethod("GetActiveSharesAsync")
$sharesTask = $getShares.Invoke($share, @([Threading.CancellationToken]::None))
Assert-True $sharesTask.Wait(15000) "GetActiveSharesAsync must complete"
$shares = $sharesTask.Result
$normal = $null
foreach ($s in $shares) { if ($s.ShareName -notlike '*$') { $normal = $s; break } }
if ($normal) {
  $detailsTask = $shareType.GetMethod("GetShareDetailsAsync").Invoke($share, @($normal.ShareName, [Threading.CancellationToken]::None))
  Assert-True $detailsTask.Wait(15000) "GetShareDetailsAsync must complete"
  $details = $detailsTask.Result
  Assert-NotNull $details "Share details must be returned for a normal share"
  Assert-True ($details.Path.Length -gt 0) "Share details must include a path"
  Write-Host ("  -> Share details: " + $normal.ShareName + " -> " + $details.Path) -ForegroundColor DarkGray
} else {
  Write-Host "  -> No non-admin share available; details live test skipped." -ForegroundColor DarkYellow
}

$ntfs = [Activator]::CreateInstance($ntfsType)
$inspect = $ntfsType.GetMethod("InspectNtfsFeaturesAsync")
$inspectTask = $inspect.Invoke($ntfs, @("C:\", [Threading.CancellationToken]::None))
Assert-True $inspectTask.Wait(15000) "NTFS inspection must complete"
$summary = $inspectTask.Result
Assert-True ($summary.AclEntries.Count -gt 0) "Current ACL entries must be returned"
Write-Host ("  -> ACL entries: " + $summary.AclEntries.Count) -ForegroundColor DarkGray

Write-Host "Phase 2 share/ACL tests PASSED." -ForegroundColor Green
