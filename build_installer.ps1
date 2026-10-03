# Build Native Windows Setup Installer (DiskMaster_Setup.exe)
param(
    [string]$Architecture = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "     DiskMaster Pro Windows Setup Installer (.EXE) Builder       " -ForegroundColor Cyan
Write-Host "     Target Architecture: $Architecture                          " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$projectDir = $PSScriptRoot
Set-Location $projectDir

# Close any running instances
Stop-Process -Name "DiskMasterWinUI" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "DiskMaster_Setup" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "DiskMasterInstaller" -Force -ErrorAction SilentlyContinue

$publishDir = "$projectDir\publish"
$installerProj = "$projectDir\tools\DiskMasterInstaller\DiskMasterInstaller.csproj"
$payloadZip = "$projectDir\tools\DiskMasterInstaller\payload.zip"
$installerCert = "$projectDir\tools\DiskMasterInstaller\DiskMaster_Certificate.cer"
$outputDir = "$projectDir\installer_output"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# 1. Ensure publish directory is fresh
if (-not (Test-Path "$publishDir\DiskMasterWinUI.exe")) {
    Write-Host "`n[1/4] Publishing main WinUI 3 project ($Architecture)..." -ForegroundColor Yellow
    dotnet publish "$projectDir\DiskMasterWinUI.csproj" -c Release -r $Architecture --self-contained true -o $publishDir
    Copy-Item -Recurse -Force "$projectDir\Scripts" "$publishDir\"
} else {
    Write-Host "`n[1/4] Using existing published binaries in $publishDir..." -ForegroundColor Yellow
}

# 2. Compress publish directory into payload.zip
Write-Host "`n[2/4] Compacting payload archive into installer..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
Write-Host "[OK] Compressed installer payload size: $zipSizeMb MB" -ForegroundColor Green

# Copy Certificate if available
$existingCert = "$outputDir\DiskMaster_Certificate.cer"
if (Test-Path $existingCert) {
    Copy-Item -Force $existingCert $installerCert
}

# 3. Compile Installer into single-file executable
Write-Host "`n[3/4] Compiling Single-File Setup Installer ($Architecture)..." -ForegroundColor Yellow
$tempOut = "$projectDir\tools\DiskMasterInstaller\bin\temp_out"
if (Test-Path $tempOut) { Remove-Item $tempOut -Recurse -Force }

dotnet publish $installerProj `
    -c Release `
    -r $Architecture `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $tempOut

# 4. Finalize and move output
Write-Host "`n[4/4] Finalizing setup installer executable..." -ForegroundColor Yellow
$archSuffix = if ($Architecture -eq "win-arm64") { "_arm64" } else { "" }
$finalExeName = "DiskMaster${archSuffix}_Setup.exe"
$finalExePath = Join-Path $outputDir $finalExeName

Copy-Item "$tempOut\DiskMasterInstaller.exe" -Destination $finalExePath -Force

# Cleanup temp files
Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
Remove-Item $installerCert -Force -ErrorAction SilentlyContinue
Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue

# 5. Sign binary with Authenticode certificate
$signScript = "$projectDir\tools\Sign-ReleaseBinary.ps1"
if (Test-Path $signScript) {
    Write-Host "`n[5/5] Signing setup installer with Authenticode..." -ForegroundColor Yellow
    try {
        & $signScript -TargetPath $finalExePath
    } catch {
        Write-Warning "Signing failed or skipped: $_"
    }
}

Unblock-File $finalExePath -ErrorAction SilentlyContinue

$exeSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
Write-Host "`n=================================================================" -ForegroundColor Green
Write-Host "  SUCCESS: Windows Setup Installer EXE Generated!" -ForegroundColor Green
Write-Host "  Path: $finalExePath" -ForegroundColor Cyan
Write-Host "  File Size: $exeSizeMb MB" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Green
