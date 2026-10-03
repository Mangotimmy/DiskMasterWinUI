# Build True Single-File Standalone Portable Executable (DiskMaster_Portable.exe)
param(
    [string]$Architecture = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "═════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "   DiskMaster Pro True Single-File Portable Executable Builder   " -ForegroundColor Cyan
Write-Host "   Target Architecture: $Architecture                            " -ForegroundColor Cyan
Write-Host "═════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

$projectDir = $PSScriptRoot
Set-Location $projectDir

# Close any running instances
Stop-Process -Name "DiskMasterWinUI" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "DiskMaster_Portable" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "DiskMasterPortableLauncher" -Force -ErrorAction SilentlyContinue

$publishDir = "$projectDir\publish"
$launcherProj = "$projectDir\tools\DiskMasterPortableLauncher\DiskMasterPortableLauncher.csproj"
$payloadZip = "$projectDir\tools\DiskMasterPortableLauncher\payload.zip"
$outputDir = "$projectDir\installer_output"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# 1. Ensure publish directory is fresh
Write-Host "`n[1/4] Publishing main WinUI 3 project ($Architecture)..." -ForegroundColor Yellow
dotnet publish "$projectDir\DiskMasterWinUI.csproj" -c Release -r $Architecture --self-contained true -o $publishDir
Copy-Item -Recurse -Force "$projectDir\Scripts" "$publishDir\"

# 2. Compress publish directory into payload.zip
Write-Host "`n[2/4] Compacting payload archive (payload.zip)..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
Write-Host "✓ Compressed payload size: $zipSizeMb MB" -ForegroundColor Green

# 3. Compile Launcher into single-file executable
Write-Host "`n[3/4] Compiling Single-File Native Launcher ($Architecture)..." -ForegroundColor Yellow
$tempOut = "$projectDir\tools\DiskMasterPortableLauncher\bin\temp_out"
if (Test-Path $tempOut) { Remove-Item $tempOut -Recurse -Force }

dotnet publish $launcherProj `
    -c Release `
    -r $Architecture `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $tempOut

# 4. Finalize and move output
Write-Host "`n[4/4] Finalizing single-file portable executable..." -ForegroundColor Yellow
$archSuffix = if ($Architecture -eq "win-arm64") { "_arm64" } else { "" }
$finalExeName = "DiskMaster${archSuffix}_Portable.exe"
$finalExePath = Join-Path $outputDir $finalExeName

Copy-Item "$tempOut\DiskMasterPortableLauncher.exe" -Destination $finalExePath -Force

# Cleanup temp files
Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue

$exeSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
Write-Host "`n═════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  SUCCESS: True Single-File Standalone Portable EXE Generated!" -ForegroundColor Green
Write-Host "  Path: $finalExePath" -ForegroundColor Cyan
Write-Host "  File Size: $exeSizeMb MB" -ForegroundColor Cyan
Write-Host "═════════════════════════════════════════════════════════════════" -ForegroundColor Green
