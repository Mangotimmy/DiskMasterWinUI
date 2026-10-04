# DiskMaster Pro All-in-One Packager: Single-File Portable, Native Setup, WinPE Portable, and Inno Setup
param(
    [string]$Architecture = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "   DiskMaster Pro All-in-One Packaging Tool        " -ForegroundColor Cyan
Write-Host "   Architecture: $Architecture                     " -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan

$projectDir = $PSScriptRoot
Set-Location $projectDir
$outputDir = "$projectDir\installer_output"
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# 1. Build Standalone Portable Executable (.EXE)
Write-Host "`n[1/4] Building Single-File Standalone Portable EXE..." -ForegroundColor Yellow
& "$projectDir\build_portable_singlefile.ps1" -Architecture $Architecture

# 2. Build Native Setup Installer (.EXE)
Write-Host "`n[2/4] Building Native Setup Installer EXE..." -ForegroundColor Yellow
& "$projectDir\build_installer.ps1" -Architecture $Architecture

# 3. Package WinPE Portable ZIP
Write-Host "`n[3/4] Creating WinPE Portable ZIP Archive..." -ForegroundColor Yellow
$zipPath = "$outputDir\DiskMaster_${Architecture}_WinPE_Portable.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$projectDir\publish\*" -DestinationPath $zipPath -Force
Write-Host "[OK] WinPE Portable ZIP created: $zipPath" -ForegroundColor Green

# 4. Optional: Compile Inno Setup Installer if compiler is present
Write-Host "`n[4/4] Checking Inno Setup Compiler (DiskMasterSetup.iss)..." -ForegroundColor Yellow
$iscc = "C:\Users\Atszl\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    & $iscc "$projectDir\DiskMasterSetup.iss"
    Write-Host "[OK] Inno Setup Installer compiled successfully in installer_output/" -ForegroundColor Green
} else {
    Write-Host "Note: Inno Setup compiler ISCC.exe not found. Native installer was generated above." -ForegroundColor Yellow
}

Write-Host "`n===================================================" -ForegroundColor Cyan
Write-Host "  All-in-One Packaging Complete!" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Cyan
