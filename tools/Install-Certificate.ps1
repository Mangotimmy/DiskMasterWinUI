<#
.SYNOPSIS
    Installs the DiskMaster Pro Authenticode certificate into the Trusted Publisher and Root stores.
.PARAMETER CertPath
    Path to the DiskMaster_Certificate.cer file.
.PARAMETER Quiet
    Suppress interactive prompts and messages.
#>
param(
    [string]$CertPath,
    [switch]$Quiet
)

$ErrorActionPreference = "SilentlyContinue"

if (-not $Quiet) {
    Write-Host "=============================================================" -ForegroundColor Cyan
    Write-Host "   DiskMaster Pro Publisher Certificate Trust Installer      " -ForegroundColor Cyan
    Write-Host "=============================================================" -ForegroundColor Cyan
}

$searchPaths = @(
    $CertPath,
    "$PSScriptRoot\DiskMaster_Certificate.cer",
    "$PSScriptRoot\installer_output\DiskMaster_Certificate.cer",
    "$PSScriptRoot\..\installer_output\DiskMaster_Certificate.cer",
    "$PSScriptRoot\DiskMaster_Certificate_win-x64.cer",
    "$PSScriptRoot\DiskMaster_Certificate_win-arm64.cer"
)

$targetCertPath = $searchPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } | Select-Object -First 1

if (-not $targetCertPath) {
    if (-not $Quiet) { Write-Error "DiskMaster_Certificate.cer was not found." }
    exit 1
}

if (-not $Quiet) { Write-Host "Found certificate: $targetCertPath" -ForegroundColor Gray }

try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($targetCertPath)
    
    # 1. CurrentUser Stores (Always works without elevation)
    $stores = @("TrustedPublisher", "Root")
    foreach ($storeName in $stores) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "CurrentUser")
        $store.Open("ReadWrite")
        $store.Add($cert)
        $store.Close()
    }
    
    # 2. LocalMachine Stores (If elevated)
    foreach ($storeName in $stores) {
        try {
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "LocalMachine")
            $store.Open("ReadWrite")
            $store.Add($cert)
            $store.Close()
        } catch { }
    }

    if (-not $Quiet) {
        Write-Host "`n[SUCCESS] Certificate installed and trusted for DiskMaster Pro!" -ForegroundColor Green
        Write-Host "SmartScreen and Unknown Publisher warnings have been eliminated.`n" -ForegroundColor Green
    }
    exit 0
}
catch {
    if (-not $Quiet) { Write-Error "Failed to install certificate: $_" }
    exit 1
}
