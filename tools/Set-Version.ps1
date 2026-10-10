<#
.SYNOPSIS
    Single-step version updater for DiskMaster Pro suite.
.DESCRIPTION
    Updates Directory.Build.props to change the suite version across all binaries,
    installers, launchers, uninstaller, and CI workflows in one single operation.
.PARAMETER Version
    The new version string (e.g. "1.4.2", "1.5.0", "2.0.0").
.EXAMPLE
    pwsh tools/Set-Version.ps1 -Version 1.5.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

# Clean and normalize version format
$cleanVersion = $Version.Trim().TrimStart('v', 'V')
if ($cleanVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    Write-Error "Invalid version format '$Version'. Expected version like '1.4.2' or '1.4.2.0'."
    return
}

# Compute 3-part SemVer and 4-part Assembly/File version
$parts = $cleanVersion.Split('.')
$major = $parts[0]
$minor = $parts[1]
$build = $parts[2]
$revision = if ($parts.Length -ge 4) { $parts[3] } else { "0" }

$semVer = "$major.$minor.$build"
$fourPartVer = "$major.$minor.$build.$revision"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$propsPath = Join-Path $projectRoot "Directory.Build.props"

if (-not (Test-Path $propsPath)) {
    Write-Error "Directory.Build.props not found at: $propsPath"
    return
}

Write-Host "Updating DiskMaster Pro Suite version to v$semVer (Assembly: $fourPartVer)..." -ForegroundColor Cyan

$content = @"
<Project>
  <!-- Single Source of Truth for DiskMaster Pro suite versioning and metadata -->
  <PropertyGroup>
    <VersionPrefix>$semVer</VersionPrefix>
    <Version>$semVer</Version>
    <AssemblyVersion>$fourPartVer</AssemblyVersion>
    <FileVersion>$fourPartVer</FileVersion>
    <InformationalVersion>$semVer</InformationalVersion>
    <Company>DiskMaster Team</Company>
    <Product>DiskMaster Pro</Product>
    <Authors>DiskMaster Team</Authors>
    <Copyright>Copyright (C) $([DateTime]::Now.Year) DiskMaster Team. All rights reserved.</Copyright>
  </PropertyGroup>
</Project>
"@

Set-Content -Path $propsPath -Value $content -Encoding UTF8

$issPath = Join-Path $projectRoot "DiskMasterSetup.iss"
if (Test-Path $issPath) {
    $issContent = Get-Content $issPath -Raw -Encoding UTF8
    $issContent = $issContent -replace '(?m)^#define MyAppVersion ".*"', "#define MyAppVersion ""$semVer"""
    Set-Content -Path $issPath -Value $issContent -Encoding UTF8
    Write-Host "[SUCCESS] DiskMasterSetup.iss updated to v$semVer!" -ForegroundColor Green
}

Write-Host "[SUCCESS] Directory.Build.props successfully updated to $semVer!" -ForegroundColor Green
Write-Host "All projects in the repository will now inherit version $semVer." -ForegroundColor Green
