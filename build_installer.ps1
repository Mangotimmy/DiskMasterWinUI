# Build Native Windows Setup Installer (DiskMaster_Setup.exe)
[CmdletBinding()]
param(
    [string]$Architecture = "win-x64",
    [string]$CertThumbprint = "",
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [string]$PfxBase64 = "",
    [switch]$SkipTimestamp
)

if (-not $CertThumbprint) { $CertThumbprint = if ($env:SIGNING_CERT_THUMBPRINT) { $env:SIGNING_CERT_THUMBPRINT } else { $env:CERT_THUMBPRINT } }
if (-not $PfxPath) { $PfxPath = if ($env:SIGNING_CERT_PATH) { $env:SIGNING_CERT_PATH } else { $env:CERT_PATH } }
if (-not $PfxPassword) { $PfxPassword = if ($env:SIGNING_CERT_PASSWORD) { $env:SIGNING_CERT_PASSWORD } elseif ($env:CERT_PASSWORD) { $env:CERT_PASSWORD } else { $env:CSC_KEY_PASSWORD } }
if (-not $PfxBase64) { $PfxBase64 = if ($env:SIGNING_CERT_BASE64) { $env:SIGNING_CERT_BASE64 } elseif ($env:CERT_BASE64) { $env:CERT_BASE64 } else { $env:CSC_LINK } }

$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
Set-Location $projectDir

$versionFile = Join-Path $projectDir "Directory.Build.props"
$appVersion = "1.4.2"
if (Test-Path $versionFile) {
    [xml]$xmlProps = Get-Content $versionFile
    $appVersion = $xmlProps.Project.PropertyGroup.Version
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "     DiskMaster Pro Windows Setup Installer (.EXE) Builder       " -ForegroundColor Cyan
Write-Host "     Target Architecture: $Architecture                          " -ForegroundColor Cyan
Write-Host "     Suite Version:       v$appVersion                           " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# Close any running instances
Stop-Process -Name "DiskMasterWinUI" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "DiskMaster_Setup" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "DiskMasterInstaller" -Force -ErrorAction SilentlyContinue

$publishDir = "$projectDir\publish"
$installerProj = "$projectDir\tools\DiskMasterInstaller\DiskMasterInstaller.csproj"
$payloadZip = "$projectDir\tools\DiskMasterInstaller\payload.zip"
$installerCert = "$projectDir\tools\DiskMasterInstaller\DiskMaster_Certificate.cer"
$outputDir = "$projectDir\installer_output"
$signScript = "$projectDir\tools\Sign-ReleaseBinary.ps1"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 1. Ensure publish directory is fresh
Write-Host "`n[1/5] Publishing main WinUI 3 project ($Architecture)..." -ForegroundColor Yellow
dotnet publish "$projectDir\DiskMasterWinUI.csproj" -c Release -r $Architecture --self-contained true -o $publishDir
Copy-Item -Recurse -Force "$projectDir\Scripts" "$publishDir\"

# 1.0 Prune unused WindowsAppSDK satellite language folders (keeps en-us, zh-cn, zh-tw, ja-jp)
$supportedLangs = @('en-us', 'zh-cn', 'zh-tw', 'ja-jp')
Get-ChildItem -Path $publishDir -Directory | Where-Object {
    $n = $_.Name.ToLowerInvariant()
    $n -match '^[a-z]{2,3}(-[a-z0-9]+)*$' -and -not ($supportedLangs -contains $n)
} | Remove-Item -Recurse -Force

# 1.1 Ensure native uninstaller binary is compiled into publish directory
$uninstallerProj = "$projectDir\tools\DiskMasterUninstaller\DiskMasterUninstaller.csproj"
if (Test-Path $uninstallerProj) {
    Write-Host "`n[1.1/5] Compiling Native Uninstaller executable ($Architecture)..." -ForegroundColor Yellow
    $uninstTempOut = "$projectDir\tools\DiskMasterUninstaller\bin\temp_uninst_out_$(Get-Random)"
    if (Test-Path $uninstTempOut) { Remove-Item $uninstTempOut -Recurse -Force -ErrorAction SilentlyContinue }
    dotnet publish $uninstallerProj `
        -c Release `
        -r $Architecture `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $uninstTempOut
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish uninstaller executable (exit code $LASTEXITCODE)"
    }
    Copy-Item "$uninstTempOut\DiskMasterUninstaller.exe" "$publishDir\Uninstall.exe" -Force
    Remove-Item $uninstTempOut -Recurse -Force -ErrorAction SilentlyContinue
}

# 2. Sign inner binaries if not already signed
if (Test-Path $signScript) {
    Write-Host "`n[2/5] Verifying & signing payload binaries..." -ForegroundColor Yellow
    $targetsToSign = @("$publishDir\DiskMasterWinUI.exe")
    if (Test-Path "$publishDir\Uninstall.exe") {
        $targetsToSign += "$publishDir\Uninstall.exe"
    }
    $signArgs = @{
        TargetPath = $targetsToSign
    }
    if ($CertThumbprint) { $signArgs["CertThumbprint"] = $CertThumbprint }
    if ($PfxPath) { $signArgs["PfxPath"] = $PfxPath }
    if ($PfxPassword) { $signArgs["PfxPassword"] = $PfxPassword }
    if ($PfxBase64) { $signArgs["PfxBase64"] = $PfxBase64 }
    if ($SkipTimestamp) { $signArgs["SkipTimestamp"] = $true }

    try {
        & $signScript @signArgs
    } catch {
        Write-Warning "Payload binary signing note: $_"
    }
}

# 3. Compile Standard Setup Installer or Fallback Native Installer
$isccCandidates = @(
    "C:\Users\Atszl\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $isccPath = $cmd.Source }
}

$archSuffix = if ($Architecture -eq "win-arm64") { "_arm64" } else { "" }
$finalExeName = "DiskMaster${archSuffix}_Setup.exe"
$finalExePath = Join-Path $outputDir $finalExeName

$useInnoSetup = ($isccPath -and (Test-Path "$projectDir\DiskMasterSetup.iss") -and ($Architecture -eq "win-x64"))

if ($useInnoSetup) {
    Write-Host "`n[3/5] Compiling Standard Inno Setup Installer ($Architecture)..." -ForegroundColor Yellow
    Write-Host "Compiler: $isccPath" -ForegroundColor DarkGray
    & $isccPath "/DMyAppVersion=$appVersion" "$projectDir\DiskMasterSetup.iss"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile Inno Setup script (exit code $LASTEXITCODE)"
    }
    Write-Host "[OK] Standard Inno Setup Installer compiled successfully." -ForegroundColor Green
}
else {
    # 3. Compress publish directory into payload.zip
    Write-Host "`n[3/5] Compacting payload archive into installer..." -ForegroundColor Yellow
    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
    Write-Host "[OK] Compressed installer payload size: $zipSizeMb MB" -ForegroundColor Green

    # Ensure Certificate is copied for installer embedding
    $existingCert = "$outputDir\DiskMaster_Certificate.cer"
    if (Test-Path $existingCert) {
        Copy-Item -Force $existingCert $installerCert
    }

    # 4. Compile Installer into single-file executable
    Write-Host "`n[4/5] Compiling Single-File Setup Installer ($Architecture)..." -ForegroundColor Yellow
    $tempOut = "$projectDir\tools\DiskMasterInstaller\bin\temp_out_$(Get-Random)"
    if (Test-Path $tempOut) { Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue }

    dotnet publish $installerProj `
        -c Release `
        -r $Architecture `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $tempOut

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish installer executable (exit code $LASTEXITCODE)"
    }

    Copy-Item "$tempOut\DiskMasterInstaller.exe" -Destination $finalExePath -Force

    # Cleanup temp files
    Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
    Remove-Item $installerCert -Force -ErrorAction SilentlyContinue
    Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue
}

# 6. Sign final Setup installer binary
if (Test-Path $signScript) {
    Write-Host "`n[5/5] Signing setup installer with Authenticode..." -ForegroundColor Yellow
    $signArgs = @{
        TargetPath = @($finalExePath)
    }
    if ($CertThumbprint) { $signArgs["CertThumbprint"] = $CertThumbprint }
    if ($PfxPath) { $signArgs["PfxPath"] = $PfxPath }
    if ($PfxPassword) { $signArgs["PfxPassword"] = $PfxPassword }
    if ($PfxBase64) { $signArgs["PfxBase64"] = $PfxBase64 }
    if ($SkipTimestamp) { $signArgs["SkipTimestamp"] = $true }

    try {
        & $signScript @signArgs
    } catch {
        Write-Warning "Setup EXE signing note: $_"
    }
}

# Copy installer helpers to output
Copy-Item "$projectDir\tools\Install-Certificate.cmd" "$outputDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "$projectDir\tools\Install-Certificate.ps1" "$outputDir\" -Force -ErrorAction SilentlyContinue

Unblock-File $finalExePath -ErrorAction SilentlyContinue

$exeSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
Write-Host "`n=================================================================" -ForegroundColor Green
Write-Host "  SUCCESS: Windows Setup Installer EXE Generated!" -ForegroundColor Green
Write-Host "  Path: $finalExePath" -ForegroundColor Cyan
Write-Host "  File Size: $exeSizeMb MB" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Green
