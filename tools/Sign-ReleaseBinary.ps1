<#
.SYNOPSIS
    Signs DiskMaster binaries with an Authenticode certificate or a self-signed developer certificate.
.PARAMETER TargetPath
    Path to the .exe or .dll to sign.
.PARAMETER CertThumbprint
    Optional thumbprint of an existing code signing certificate in Cert:\CurrentUser\My or Cert:\LocalMachine\My.
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
Write-Host " DiskMaster Pro Authenticode Signing Utility" -ForegroundColor Cyan
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
    # Find any existing CodeSigning cert
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $cert) {
        $cert = Get-ChildItem -Path Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue | Select-Object -First 1
    }
}

if (-not $cert) {
    Write-Host "No CodeSigning certificate found. Creating temporary self-signed developer certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=DiskMaster Pro Developer, O=DiskMaster Team" -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created certificate: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
}
else {
    Write-Host "Using certificate: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
}

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
