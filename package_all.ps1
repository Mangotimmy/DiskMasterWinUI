# DiskMaster Pro All-in-One Packager: Self-Contained, WinPE Portable, and Setup Installer
$ErrorActionPreference = "Stop"

Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "   DiskMaster Pro All-in-One Packaging Tool        " -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan

$projectDir = $PSScriptRoot
Set-Location $projectDir

# 1. Build and Publish x64
Write-Host "`n[1/3] Publishing Self-Contained win-x64 Release..." -ForegroundColor Yellow
dotnet publish DiskMasterWinUI.csproj -c Release -r win-x64 --self-contained true -o "$projectDir\publish"

# 2. Package WinPE Portable ZIP
Write-Host "`n[2/3] Creating WinPE Portable ZIP..." -ForegroundColor Yellow
$zipPath = "$projectDir\installer_output\DiskMaster_WinPE_Portable.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
if (-not (Test-Path "$projectDir\installer_output")) { New-Item -ItemType Directory -Path "$projectDir\installer_output" | Out-Null }

Compress-Archive -Path "$projectDir\publish\*" -DestinationPath $zipPath -Force
Write-Host "✓ WinPE Portable ZIP created: $zipPath" -ForegroundColor Green

# 3. Compile Inno Setup Installer
Write-Host "`n[3/3] Compiling Setup Installer..." -ForegroundColor Yellow
$iscc = "C:\Users\Atszl\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    & $iscc "$projectDir\installer.iss"
    Write-Host "✓ Installer compiled successfully in installer_output/" -ForegroundColor Green
} else {
    Write-Host "Note: Inno Setup compiler ISCC.exe not found at default location. Skipping installer compilation." -ForegroundColor Yellow
}

Write-Host "`n═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Packaging Complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
