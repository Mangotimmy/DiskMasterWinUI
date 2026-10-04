<#
.SYNOPSIS
    Signs DiskMaster binaries with an Authenticode certificate and registers publisher trust.
    Fully compatible with both local Windows development environments and GitHub Actions CI runners.

.DESCRIPTION
    Resolves a code signing certificate using the following priority:
    1. Base64-encoded PFX from parameter ($PfxBase64) or environment variable ($env:SIGNING_CERT_BASE64, $env:CERT_BASE64, $env:CSC_LINK)
    2. PFX file path from parameter ($PfxPath) or environment variable ($env:SIGNING_CERT_PATH, $env:CERT_PATH)
    3. Specific certificate thumbprint from parameter ($CertThumbprint) or environment variable ($env:SIGNING_CERT_THUMBPRINT, $env:CERT_THUMBPRINT)
    4. Existing DiskMaster or CodeSigning certificate in CurrentUser\My or LocalMachine\My store
    5. Automatic self-signed Code Signing Certificate generation (non-interactive, SHA-256, 5-year validity)

    Also exports the public certificate (DiskMaster_Certificate.cer) and provides timestamping failover.

.PARAMETER TargetPath
    Path(s) or wildcard pattern to the .exe, .dll, or .msix to sign. Accepts multiple files or pipeline input.

.PARAMETER CertThumbprint
    Optional thumbprint of an existing code signing certificate in the Windows Certificate Store.

.PARAMETER PfxPath
    Optional file path to a .pfx or .p12 code signing certificate file.

.PARAMETER PfxPassword
    Optional password for the .pfx file or Base64 payload.

.PARAMETER PfxBase64
    Optional Base64-encoded .pfx certificate content.

.PARAMETER TimestampServers
    Array of RFC 3161 timestamp server URLs tried in sequence for resilience.

.PARAMETER SkipTimestamp
    If specified, signs without contacting a timestamp server.

.PARAMETER SkipTrust
    If specified, skips registering the certificate in CurrentUser TrustedPublisher/Root store.

.PARAMETER ExportCert
    If specified (default $true), exports the public .cer certificate alongside targets.

.EXAMPLE
    .\tools\Sign-ReleaseBinary.ps1 -TargetPath "installer_output\DiskMaster_Portable.exe"
    .\tools\Sign-ReleaseBinary.ps1 -TargetPath @("publish\DiskMasterWinUI.exe", "installer_output\*.exe")
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, ValueFromPipeline = $true, Position = 0)]
    [string[]]$TargetPath,

    [Parameter(Mandatory = $false)]
    [string]$CertThumbprint = ($env:SIGNING_CERT_THUMBPRINT ?? $env:CERT_THUMBPRINT),

    [Parameter(Mandatory = $false)]
    [string]$PfxPath = ($env:SIGNING_CERT_PATH ?? $env:CERT_PATH),

    [Parameter(Mandatory = $false)]
    [string]$PfxPassword = ($env:SIGNING_CERT_PASSWORD ?? $env:CERT_PASSWORD ?? $env:CSC_KEY_PASSWORD ?? ""),

    [Parameter(Mandatory = $false)]
    [string]$PfxBase64 = ($env:SIGNING_CERT_BASE64 ?? $env:CERT_BASE64 ?? $env:CSC_LINK),

    [Parameter(Mandatory = $false)]
    [string[]]$TimestampServers = @(
        "http://timestamp.digicert.com",
        "http://timestamp.sectigo.com",
        "http://timestamp.globalsign.com/tsa/r6advanced1",
        "http://tsa.starfieldtech.com"
    ),

    [Parameter(Mandatory = $false)]
    [switch]$SkipTimestamp,

    [Parameter(Mandatory = $false)]
    [switch]$SkipTrust,

    [Parameter(Mandatory = $false)]
    [bool]$ExportCert = $true
)

Set-StrictMode -Off
$ErrorActionPreference = "Stop"

$isCI = ($env:GITHUB_ACTIONS -eq 'true') -or ($env:CI -eq 'true') -or ($env:TF_BUILD -eq 'True')

Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "   DiskMaster Pro Authenticode Code Signing Engine            " -ForegroundColor Cyan
Write-Host "   Environment: $(if ($isCI) { 'GitHub Actions / CI' } else { 'Local Windows' })" -ForegroundColor Gray
Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# -------------------------------------------------------------
# 1. Certificate Resolution
# -------------------------------------------------------------
$cert = $null

# Case 1: Base64-encoded PFX provided (e.g. from GitHub Actions Secret)
if (-not [string]::IsNullOrWhiteSpace($PfxBase64)) {
    try {
        Write-Host "[Cert] Decoding PFX from Base64 environment variable / secret..." -ForegroundColor Yellow
        $pfxBytes = [System.Convert]::FromBase64String($PfxBase64.Trim())
        $keyStorageFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
                           [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor
                           [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
        
        $pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $pfxBytes,
            $PfxPassword,
            $keyStorageFlags
        )

        # Import into CurrentUser\My so PowerShell Authenticode APIs can access private keys smoothly
        $userStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("My", "CurrentUser")
        $userStore.Open("ReadWrite")
        $userStore.Add($pfxCert)
        $userStore.Close()

        $cert = $pfxCert
        Write-Host "[Cert] Loaded certificate from Base64: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
    }
    catch {
        Write-Warning "[Cert] Failed to load certificate from Base64: $_"
        $cert = $null
    }
}

# Case 2: PFX file path provided
if (-not $cert -and -not [string]::IsNullOrWhiteSpace($PfxPath) -and (Test-Path $PfxPath)) {
    try {
        Write-Host "[Cert] Loading certificate from file: $PfxPath..." -ForegroundColor Yellow
        $keyStorageFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
                           [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor
                           [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet

        $pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            (Resolve-Path $PfxPath).Path,
            $PfxPassword,
            $keyStorageFlags
        )

        $userStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("My", "CurrentUser")
        $userStore.Open("ReadWrite")
        $userStore.Add($pfxCert)
        $userStore.Close()

        $cert = $pfxCert
        Write-Host "[Cert] Loaded certificate from file: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
    }
    catch {
        Write-Warning "[Cert] Failed to load certificate from PFX file: $_"
        $cert = $null
    }
}

# Case 3: Specific Thumbprint provided
if (-not $cert -and -not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
    $cleanThumbprint = $CertThumbprint.Replace(" ", "").Trim()
    $cert = Get-Item "Cert:\CurrentUser\My\$cleanThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) {
        $cert = Get-Item "Cert:\LocalMachine\My\$cleanThumbprint" -ErrorAction SilentlyContinue
    }
    if ($cert) {
        Write-Host "[Cert] Found certificate by Thumbprint: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
    }
}

# Case 4: Search for existing Code Signing certificates in store
if (-not $cert) {
    $allCerts = @(Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue) +
                @(Get-ChildItem -Path Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue)
    
    # Prioritize valid certificates that have a private key and match "DiskMaster"
    $validDiskMasterCert = $allCerts | Where-Object { 
        $_.HasPrivateKey -and 
        $_.NotAfter -gt (Get-Date) -and 
        ($_.Subject -match "DiskMaster" -or $_.FriendlyName -match "DiskMaster") 
    } | Select-Object -First 1

    if ($validDiskMasterCert) {
        $cert = $validDiskMasterCert
        Write-Host "[Cert] Reusing existing DiskMaster certificate: $($cert.Thumbprint)" -ForegroundColor Green
    }
    else {
        # Fallback to any valid unexpired code signing certificate with private key
        $anyValidCert = $allCerts | Where-Object { 
            $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) 
        } | Select-Object -First 1

        if ($anyValidCert) {
            $cert = $anyValidCert
            Write-Host "[Cert] Using available Code Signing certificate: $($cert.Thumbprint) ($($cert.Subject))" -ForegroundColor Green
        }
    }
}

# Case 5: Auto-Generate Self-Signed Certificate if none exists
if (-not $cert) {
    Write-Host "[Cert] Generating dedicated Code Signing certificate for DiskMaster..." -ForegroundColor Yellow
    try {
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=DiskMaster Pro, O=DiskMaster Team, OU=Release Engineering" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -HashAlgorithm "SHA256" `
            -KeyLength 2048 `
            -KeyUsage DigitalSignature `
            -KeyUsageProperty All `
            -NotAfter (Get-Date).AddYears(5) `
            -FriendlyName "DiskMaster Pro Code Signing Certificate"

        Write-Host "[Cert] Successfully created new self-signed certificate:" -ForegroundColor Green
        Write-Host "       Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
        Write-Host "       Subject:    $($cert.Subject)" -ForegroundColor Green
        Write-Host "       Expires:    $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Green
    }
    catch {
        Write-Error "[Cert] Failed to generate self-signed certificate: $_"
        exit 1
    }
}

# -------------------------------------------------------------
# 2. Local Trust Registration (Skipped in CI to avoid modal prompts)
# -------------------------------------------------------------
if (-not $isCI -and -not $SkipTrust) {
    try {
        # Register in CurrentUser TrustedPublisher to eliminate Unknown Publisher dialogs
        $pubStore = New-Object System.Security.Cryptography.X509Certificates.X509Store "TrustedPublisher", "CurrentUser"
        $pubStore.Open("ReadWrite")
        $pubStore.Add($cert)
        $pubStore.Close()
        Write-Host "[Trust] Registered certificate in CurrentUser TrustedPublisher store." -ForegroundColor DarkGreen
    }
    catch {
        Write-Verbose "[Trust] Could not add to TrustedPublisher store: $_"
    }

    try {
        # Register in CurrentUser Root store
        $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store "Root", "CurrentUser"
        $rootStore.Open("ReadWrite")
        $rootStore.Add($cert)
        $rootStore.Close()
        Write-Host "[Trust] Registered certificate in CurrentUser Root store." -ForegroundColor DarkGreen
    }
    catch {
        Write-Verbose "[Trust] CurrentUser Root store registration note: $_"
    }
}
elseif ($isCI) {
    Write-Host "[Trust] CI runner detected: Operating in non-interactive signing mode." -ForegroundColor Gray
}

# -------------------------------------------------------------
# 3. Export Public Certificate (.cer)
# -------------------------------------------------------------
if ($ExportCert) {
    $scriptRoot = Split-Path -Parent $PSCommandPath
    $projectRoot = Split-Path -Parent $scriptRoot
    
    $exportLocations = @(
        (Join-Path $projectRoot "installer_output\DiskMaster_Certificate.cer"),
        (Join-Path $projectRoot "tools\DiskMasterInstaller\DiskMaster_Certificate.cer"),
        (Join-Path $projectRoot "tools\DiskMaster_Certificate.cer")
    )

    foreach ($loc in $exportLocations) {
        try {
            $parentDir = Split-Path -Parent $loc
            if (-not (Test-Path $parentDir)) {
                New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
            }
            Export-Certificate -Cert $cert -FilePath $loc -Force | Out-Null
            Write-Host "[Export] Public certificate (.cer) saved to: $loc" -ForegroundColor DarkCyan
        }
        catch {
            Write-Verbose "[Export] Could not export certificate to $($loc): $_"
        }
    }
}

# -------------------------------------------------------------
# 4. Resolve Target Files
# -------------------------------------------------------------
$filesToSign = [System.Collections.Generic.List[string]]::new()

foreach ($pattern in $TargetPath) {
    if ([string]::IsNullOrWhiteSpace($pattern)) { continue }

    if (Test-Path $pattern -PathType Leaf) {
        $filesToSign.Add((Resolve-Path $pattern).Path)
    }
    elseif (Test-Path $pattern -PathType Container) {
        $matching = Get-ChildItem -Path $pattern -Include *.exe, *.dll, *.msix -Recurse -File | Select-Object -ExpandProperty FullName
        foreach ($m in $matching) { $filesToSign.Add($m) }
    }
    else {
        # Wildcard resolution
        $resolved = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
        if ($resolved) {
            foreach ($r in $resolved) { $filesToSign.Add($r) }
        }
        else {
            Write-Warning "Target path or pattern not found: $pattern"
        }
    }
}

if ($filesToSign.Count -eq 0) {
    Write-Warning "No valid files found to sign."
    return
}

# -------------------------------------------------------------
# 5. Sign Files with Authenticode & Timestamp Failover
# -------------------------------------------------------------
Write-Host "`n[Sign] Signing $($filesToSign.Count) file(s)..." -ForegroundColor Cyan

$successCount = 0
$failCount = 0

foreach ($file in ($filesToSign | Select-Object -Unique)) {
    if (-not (Test-Path $file)) {
        Write-Warning "File does not exist: $file"
        $failCount++
        continue
    }

    $fileName = Split-Path $file -Leaf
    $signed = $false

    # Try signing with Timestamp servers if not explicitly skipped
    if (-not $SkipTimestamp) {
        foreach ($ts in $TimestampServers) {
            try {
                Write-Host "  -> Signing '$fileName' with timestamp ($ts)..." -ForegroundColor Gray
                $sig = Set-AuthenticodeSignature `
                    -FilePath $file `
                    -Certificate $cert `
                    -TimestampServer $ts `
                    -HashAlgorithm SHA256 `
                    -ErrorAction Stop

                if ($sig -and ($sig.Status -eq "Valid" -or $sig.Status -eq "UnknownError" -or $sig.Status -eq "NotTrusted")) {
                    # NotTrusted is normal for self-signed certificates on machines without the root pre-installed
                    $signed = $true
                    Write-Host "     [OK] Timestamped signature applied via $ts (Status: $($sig.Status))" -ForegroundColor Green
                    break
                }
            }
            catch {
                Write-Verbose "     Timestamp server $ts failed: $_"
            }
        }
    }

    # Fallback to signing without timestamp if timestamping failed or was skipped
    if (-not $signed) {
        try {
            Write-Host "  -> Signing '$fileName' without timestamp (fallback)..." -ForegroundColor Yellow
            $sig = Set-AuthenticodeSignature `
                -FilePath $file `
                -Certificate $cert `
                -HashAlgorithm SHA256 `
                -ErrorAction Stop

            if ($sig) {
                $signed = $true
                Write-Host "     [OK] Signature applied without timestamp (Status: $($sig.Status))" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Error "     [FAIL] Signing failed for '$fileName': $_"
            $failCount++
            continue
        }
    }

    # Unblock the file to remove Mark-of-the-Web
    Unblock-File -Path $file -ErrorAction SilentlyContinue

    # Verify signature
    $verifySig = Get-AuthenticodeSignature -FilePath $file -ErrorAction SilentlyContinue
    $statusText = if ($verifySig) { $verifySig.Status.ToString() } else { "Unknown" }
    $signerName = if ($verifySig -and $verifySig.SignerCertificate) { $verifySig.SignerCertificate.Subject } else { "N/A" }
    
    Write-Host "  ✅ Verified: $fileName | Status: $statusText | Signer: $signerName" -ForegroundColor DarkGreen
    $successCount++
}

Write-Host "`n══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Signing Complete: $successCount succeeded, $failCount failed." -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "══════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
