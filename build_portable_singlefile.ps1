# Build True Single-File Standalone Portable Executable (DiskMaster_Portable.exe)
[CmdletBinding()]
param(
    [string]$Architecture = "win-x64",
    [string]$CertThumbprint = ($env:SIGNING_CERT_THUMBPRINT ?? $env:CERT_THUMBPRINT),
    [string]$PfxPath = ($env:SIGNING_CERT_PATH ?? $env:CERT_PATH),
    [string]$PfxPassword = ($env:SIGNING_CERT_PASSWORD ?? $env:CERT_PASSWORD ?? $env:CSC_KEY_PASSWORD),
    [string]$PfxBase64 = ($env:SIGNING_CERT_BASE64 ?? $env:CERT_BASE64 ?? $env:CSC_LINK),
    [switch]$SkipTimestamp
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "   DiskMaster Pro True Single-File Portable Executable Builder   " -ForegroundColor Cyan
Write-Host "   Target Architecture: $Architecture                            " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

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
$signScript = "$projectDir\tools\Sign-ReleaseBinary.ps1"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 1. Ensure publish directory is fresh
Write-Host "`n[1/6] Publishing main WinUI 3 project ($Architecture)..." -ForegroundColor Yellow
dotnet publish "$projectDir\DiskMasterWinUI.csproj" -c Release -r $Architecture --self-contained true -o $publishDir
Copy-Item -Recurse -Force "$projectDir\Scripts" "$publishDir\"

# 1.0 Prune unused WindowsAppSDK satellite language folders (keeps en-us, zh-cn, zh-tw, ja-jp)
$supportedLangs = @('en-us', 'zh-cn', 'zh-tw', 'ja-jp')
Get-ChildItem -Path $publishDir -Directory | Where-Object {
    $n = $_.Name.ToLowerInvariant()
    $n -match '^[a-z]{2,3}(-[a-z0-9]+)*$' -and -not ($supportedLangs -contains $n)
} | Remove-Item -Recurse -Force

# 1.1 Ensure the Explorer "Recover with DiskMaster" shell launcher is in the payload.
# ShellIntegrationService.Install() looks for DiskMasterShellHost.exe next to the app exe and
# refuses to register the context menu when it is missing, so it must ship or the feature is dead.
$shellHostProj = "$projectDir\tools\DiskMasterShellHost\DiskMasterShellHost.csproj"
if (Test-Path $shellHostProj) {
    Write-Host "`n[1.1/6] Publishing Explorer shell launcher ($Architecture)..." -ForegroundColor Yellow
    $shellHostTempOut = "$projectDir\tools\DiskMasterShellHost\bin\temp_shellout_$(Get-Random)"
    if (Test-Path $shellHostTempOut) { Remove-Item $shellHostTempOut -Recurse -Force -ErrorAction SilentlyContinue }
    # Self-contained + single file: the launcher must run from a bare install directory with no
    # companion .dll / .runtimeconfig.json next to it (ShellIntegrationService launches it directly).
    dotnet publish $shellHostProj `
        -c Release `
        -r $Architecture `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $shellHostTempOut
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish Explorer shell launcher (exit code $LASTEXITCODE)"
    }
    Copy-Item "$shellHostTempOut\DiskMasterShellHost.exe" "$publishDir\DiskMasterShellHost.exe" -Force
    Remove-Item $shellHostTempOut -Recurse -Force -ErrorAction SilentlyContinue
}

# 2. Sign core internal binaries prior to compression
if (Test-Path $signScript) {
    Write-Host "`n[3/6] Signing core payload binaries with Authenticode..." -ForegroundColor Yellow
    $signArgs = @{
        TargetPath = @("$publishDir\DiskMasterWinUI.exe", "$publishDir\DiskMasterShellHost.exe")
    }
    if ($CertThumbprint) { $signArgs["CertThumbprint"] = $CertThumbprint }
    if ($PfxPath) { $signArgs["PfxPath"] = $PfxPath }
    if ($PfxPassword) { $signArgs["PfxPassword"] = $PfxPassword }
    if ($PfxBase64) { $signArgs["PfxBase64"] = $PfxBase64 }
    if ($SkipTimestamp) { $signArgs["SkipTimestamp"] = $true }

    try {
        & $signScript @signArgs
    } catch {
        Write-Warning "Inner binary signing note: $_"
    }
}

# 3. Compress publish directory into payload.zip
Write-Host "`n[4/6] Compacting payload archive (payload.zip)..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zipSizeMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 2)
Write-Host "[OK] Compressed payload size: $zipSizeMb MB" -ForegroundColor Green

# 4. Compile Launcher into single-file executable
Write-Host "`n[5/6] Compiling Single-File Native Launcher ($Architecture)..." -ForegroundColor Yellow
$tempOut = "$projectDir\tools\DiskMasterPortableLauncher\bin\temp_out_$(Get-Random)"
if (Test-Path $tempOut) { Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue }

dotnet publish $launcherProj `
    -c Release `
    -r $Architecture `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $tempOut

if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish portable launcher executable (exit code $LASTEXITCODE)"
}

# 5. Finalize and move output
$archSuffix = if ($Architecture -eq "win-arm64") { "_arm64" } else { "" }
$finalExeName = "DiskMaster${archSuffix}_Portable.exe"
$finalExePath = Join-Path $outputDir $finalExeName

Copy-Item "$tempOut\DiskMasterPortableLauncher.exe" -Destination $finalExePath -Force

# Cleanup temp files
Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue

# 6. Sign final standalone portable binary
if (Test-Path $signScript) {
    Write-Host "`n[6/6] Signing standalone portable binary with Authenticode..." -ForegroundColor Yellow
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
        Write-Warning "Portable EXE signing note: $_"
    }
}

# Copy installer helpers to output
Copy-Item "$projectDir\tools\Install-Certificate.cmd" "$outputDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "$projectDir\tools\Install-Certificate.ps1" "$outputDir\" -Force -ErrorAction SilentlyContinue

Unblock-File $finalExePath -ErrorAction SilentlyContinue

$exeSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
Write-Host "`n=================================================================" -ForegroundColor Green
Write-Host "  SUCCESS: True Single-File Standalone Portable EXE Generated!" -ForegroundColor Green
Write-Host "  Path: $finalExePath" -ForegroundColor Cyan
Write-Host "  File Size: $exeSizeMb MB" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Green
