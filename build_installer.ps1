# Build and Package DiskMaster Installer
$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Building DiskMaster Release & Setup   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $ScriptDir

# 1. Publish Release
Write-Host "`n[1/3] Publishing self-contained Release (win-x64)..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained -o "./publish"

# Verify critical files
$criticalFiles = @(
    "publish\DiskMasterWinUI.exe",
    "publish\resources.pri",
    "publish\MainWindow.xbf",
    "publish\App.xbf"
)
foreach ($f in $criticalFiles) {
    if (-not (Test-Path $f)) {
        Write-Error "Critical file missing in publish directory: $f"
    }
}
Write-Host "Publish verification succeeded! All XAML and PRI files verified." -ForegroundColor Green

# 2. Locate Inno Setup Compiler
Write-Host "`n[2/3] Locating Inno Setup Compiler..." -ForegroundColor Yellow
$isccCandidates = @(
    "C:\Users\Atszl\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
)

$iscc = $null
foreach ($path in $isccCandidates) {
    if ($path -and (Test-Path $path)) {
        $iscc = $path
        break
    }
}

if (-not $iscc) {
    Write-Error "Inno Setup Compiler (ISCC.exe) was not found."
}
Write-Host "Found ISCC at: $iscc" -ForegroundColor Green

# 3. Compile Installer
Write-Host "`n[3/3] Compiling Installer executable..." -ForegroundColor Yellow
if (-not (Test-Path "installer_output")) {
    New-Item -ItemType Directory -Path "installer_output" | Out-Null
}

& $iscc "DiskMasterSetup.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$installer = Get-Item "installer_output\DiskMaster-Setup-v1.0.0.exe"
Write-Host "`n========================================" -ForegroundColor Green
Write-Host " Installer Successfully Created!" -ForegroundColor Green
Write-Host " File: $($installer.FullName)" -ForegroundColor White
Write-Host " Size: $([math]::Round($installer.Length / 1MB, 2)) MB" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Green
