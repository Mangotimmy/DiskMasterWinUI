# NetShare redesign: table, details pane, advanced share editor
$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "harness\\TestFramework.ps1")
$asm = Initialize-TestAssembly -Configuration "Release"

Write-Host "Test 1: advanced share model and service contracts" -ForegroundColor Yellow
$itemType = $asm.GetType("DiskMasterWinUI.Models.NetShareItem")
Assert-NotNull $itemType "NetShareItem must exist"
foreach ($p in "AccessSummary","ConcurrentUserLimit","CachingMode","EncryptData","FolderEnumerationMode","PermissionCount") {
  Assert-NotNull ($itemType.GetProperty($p)) "NetShareItem.$p must exist"
}
$shareType = $asm.GetType("DiskMasterWinUI.Services.NetShareService")
Assert-NotNull $shareType "NetShareService must exist"
foreach ($m in "GetActiveSharesAsync","CreateShareAdvancedAsync","UpdateShareAdvancedAsync","ApplyShareAclAsync") {
  Assert-NotNull ($shareType.GetMethod($m)) "NetShareService.$m must exist"
}
$grantType = $asm.GetType("DiskMasterWinUI.Models.ShareAclGrantItem")
Assert-NotNull $grantType "ShareAclGrantItem must exist"
$editorType = $asm.GetType("DiskMasterWinUI.ViewModels.ShareEditorViewModel")
Assert-NotNull $editorType "ShareEditorViewModel must exist"
Assert-NotNull ($editorType.GetProperty("Grants")) "Editor must expose grants"
Assert-NotNull ($editorType.GetMethod("AddGrant")) "Editor must add grants"

Write-Host "Test 2: advanced enumeration" -ForegroundColor Yellow
$service = [Activator]::CreateInstance($shareType)
$list = $shareType.GetMethod("GetActiveSharesAsync").Invoke($service, @([Threading.CancellationToken]::None)).GetAwaiter().GetResult()
Assert-NotNull $list "Share list must not be null"
Write-Host ("  -> shares: " + $list.Count) -ForegroundColor DarkGray
$normal = $null
foreach ($s in $list) { if (-not $s.IsAdminShare) { $normal = $s; break } }
if ($normal) {
  Assert-True ($normal.CachingMode.Length -gt 0) "Share list must include caching mode"
  Assert-True ($normal.FolderEnumerationMode.Length -gt 0) "Share list must include folder enumeration"
  Assert-True ($normal.PermissionCount -ge 0) "Share list must include permission count"
  Write-Host ("  -> " + $normal.ShareName + " cache=" + $normal.CachingMode + " permissions=" + $normal.PermissionCount) -ForegroundColor DarkGray
}

Write-Host "Test 3: validation without mutation" -ForegroundColor Yellow
$grants = [System.Collections.Generic.List[DiskMasterWinUI.Models.ShareAclGrantItem]]::new()
$grant = New-Object DiskMasterWinUI.Models.ShareAclGrantItem
$grant.AccountName = "Authenticated Users"
$grant.AccessRight = "Read"
$grants.Add($grant)
$badName = $shareType.GetMethod("CreateShareAdvancedAsync").Invoke($service, @(
  "bad/name", "C:\\", "", 0, "Manual", $false, "AccessBased",
  $grants, $false, [Threading.CancellationToken]::None))
$badNameResult = $badName.GetAwaiter().GetResult()
Assert-True (-not $badNameResult.Item1) "Invalid share name must fail"
Assert-True ($badNameResult.Item2 -like "*invalid*") "Invalid name must return a clear message"
$missing = $shareType.GetMethod("CreateShareAdvancedAsync").Invoke($service, @(
  "TestShare", "C:\\ThisFolderDoesNotExist_12345", "", 0, "Manual", $false, "AccessBased",
  $grants, $false, [Threading.CancellationToken]::None))
$missingResult = $missing.GetAwaiter().GetResult()
Assert-True (-not $missingResult.Item1) "Missing folder must fail"
Assert-True ($missingResult.Item2 -like "*does not exist*") "Missing folder must return a clear message"

Write-Host "Test 4: source layout is the simplified design" -ForegroundColor Yellow
$netXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\\Pages\\NetworkToolsPage.xaml"))
Assert-True ($netXaml.Contains("Network shares")) "Page must have the new title"
Assert-True ($netXaml.Contains("New share")) "Page must have New share"
Assert-True ($netXaml.Contains("Share details")) "Page must have selected-share details"
Assert-True ($netXaml.Contains("Troubleshoot sharing")) "Page must have Troubleshoot sharing"
Assert-True (-not $netXaml.Contains("建立新的網路共用資料夾")) "Old inline create card must be removed"
Assert-True (-not $netXaml.Contains("尋找共用帳號")) "Old page account picker must be removed"
Assert-True (-not $netXaml.Contains("管理選取的共用權限")) "Old duplicate permission manager must be removed"
$editorXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\\Controls\\ShareEditorDialog.xaml"))
Assert-True ($editorXaml.Contains("People with access")) "Editor must have People with access"
Assert-True ($editorXaml.Contains("Advanced options")) "Editor must have Advanced options"
Assert-True ($editorXaml.Contains("Offline availability")) "Editor must expose offline availability"
Assert-True ($editorXaml.Contains("Simultaneous users")) "Editor must expose user limit"
$troubleXaml = [System.IO.File]::ReadAllText((Join-Path $scriptRoot "..\\Controls\\ShareTroubleshootingDialog.xaml"))
Assert-True ($troubleXaml.Contains("Fix Microsoft account LAN policy")) "Troubleshooting must include policy fix"
Assert-True ($troubleXaml.Contains("File and Printer Sharing")) "Troubleshooting must include firewall fix"
Assert-True ($troubleXaml.Contains("Dedicated sharing account")) "Troubleshooting must include dedicated account"

Write-Host "NetShare redesign tests PASSED." -ForegroundColor Green
