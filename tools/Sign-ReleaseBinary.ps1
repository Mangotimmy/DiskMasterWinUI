<#
.SYNOPSIS
    Signs DiskMaster binaries with an Authenticode certificate and registers publisher trust.
.PARAMETER TargetPath
    Path to the .exe or .dll to sign.
.PARAMETER CertThumbprint
    Optional thumbprint of an existing code signing certificate.
.PARAMETER TimestampServer
    Timestamp server URL (default: http://timestamp.digicert.com).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetPath,

    [Parameter(Mandatory = $false)]
    [string]$CertThumbprint,

    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $TargetPath)) {
    Write-Error "Target file not found: $TargetPath"
    exit 1
}

Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " DiskMaster Pro Authenticode Signing & Publisher Registration " -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Target: $TargetPath" -ForegroundColor Gray

$cert = $null

if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
    $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) {
        $cert = Get-Item "Cert:\LocalMachine\My\$CertThumbprint" -ErrorAction SilentlyContinue
    }
}
else {
    # Find any existing CodeSigning cert for DiskMaster Pro
    $allCerts = @(Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue) +
                @(Get-ChildItem -Path Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue)
    $cert = $allCerts | Where-Object { $_.Subject -match "DiskMaster" } | Select-Object -First 1
    if (-not $cert) {
        $cert = $allCerts | Select-Object -First 1
    }
}

if (-not $cert) {
    Write-Host "Creating Code Signing certificate for DiskMaster Team..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=DiskMaster Pro, O=DiskMaster Team" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -HashAlgorithm "SHA256" `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created certificate: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
}
else {
    Write-Host "Using certificate: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
}

# Trust certificate in current user root and trusted publisher to eliminate 'Unknown Publisher'
try {
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store "Root", "CurrentUser"
    $rootStore.Open("ReadWrite")
    $rootStore.Add($cert)
    $rootStore.Close()

    $pubStore = New-Object System.Security.Cryptography.X509Certificates.X509Store "TrustedPublisher", "CurrentUser"
    $pubStore.Open("ReadWrite")
    $pubStore.Add($cert)
    $pubStore.Close()
    Write-Host "Registered certificate in CurrentUser Root and TrustedPublisher stores." -ForegroundColor Green
}
catch {
    Write-Warning "Could not register certificate to TrustedPublisher: $_"
}

# Export public certificate (.cer)
$targetDir = Split-Path $TargetPath -Parent
$certPaths = @()
if ($targetDir -and (Test-Path $targetDir)) {
    $certPaths += (Join-Path $targetDir "DiskMaster_Certificate.cer")
}
$installerOutputDir = Join-Path (Split-Path $PSScriptRoot -Parent) "installer_output"
if (Test-Path $installerOutputDir) {
    $certPaths += (Join-Path $installerOutputDir "DiskMaster_Certificate.cer")
}

foreach ($cPath in ($certPaths | Select-Object -Unique)) {
    try {
        Export-Certificate -Cert $cert -FilePath $cPath -Force | Out-Null
        Write-Host "Exported public certificate to: $cPath" -ForegroundColor Gray
    }
    catch { }
}

# Sign binary
try {
    Write-Host "Signing $TargetPath with timestamp..." -ForegroundColor Cyan
    $sig = Set-AuthenticodeSignature -FilePath $TargetPath -Certificate $cert -TimestampServer $TimestampServer -HashAlgorithm SHA256
    if ($sig.Status -eq "Valid") {
        Write-Host "✅ Successfully signed: $TargetPath (Status: $($sig.Status))" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️ Signed with notice: $($sig.StatusMessage) (Status: $($sig.Status))" -ForegroundColor Yellow
    }
}
catch {
    Write-Warning "Set-AuthenticodeSignature failed with timestamp; trying without timestamp..."
    $sig = Set-AuthenticodeSignature -FilePath $TargetPath -Certificate $cert -HashAlgorithm SHA256
    Write-Host "Signed without timestamp: $($sig.Status)" -ForegroundColor Gray
}
