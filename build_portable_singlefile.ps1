# Build True Single-File Standalone Portable Executable (DiskMaster_Portable.exe)
[CmdletBinding()]
param(
    [string]$Architecture = "win-x64",
    [string]$CertThumbprint = (@($env:SIGNING_CERT_THUMBPRINT, $env:CERT_THUMBPRINT) | Where-Object { $_ } | Select-Object -First 1),
    [string]$PfxPath = (@($env:SIGNING_CERT_PATH, $env:CERT_PATH) | Where-Object { $_ } | Select-Object -First 1),
    [string]$PfxPassword = (@($env:SIGNING_CERT_PASSWORD, $env:CERT_PASSWORD, $env:CSC_KEY_PASSWORD) | Where-Object { $_ } | Select-Object -First 1),
    [string]$PfxBase64 = (@($env:SIGNING_CERT_BASE64, $env:CERT_BASE64, $env:CSC_LINK) | Where-Object { $_ } | Select-Object -First 1),
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

# Normalize NTFS DACL and Mandatory Integrity Level (Medium) so sandboxed workspaces never
# stamp Low Mandatory Level onto built executables (which causes Error 5 Access Denied on launch).
& icacls $projectDir /setintegritylevel "(OI)(CI)M" /C 2>$null | Out-Null
& icacls $outputDir /reset /T /C 2>$null | Out-Null
& icacls $outputDir /setintegritylevel "(OI)(CI)M" /T /C 2>$null | Out-Null

# 1. Ensure publish directory is fresh
Write-Host "`n[1/6] Publishing main WinUI 3 project ($Architecture)..." -ForegroundColor Yellow
dotnet publish "$projectDir\DiskMasterWinUI.csproj" -c Release -r $Architecture --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$publishDir\DiskMasterWinUI.exe")) {
    throw "Failed to publish DiskMasterWinUI.csproj (exit code $LASTEXITCODE) or DiskMasterWinUI.exe is missing in $publishDir"
}

$nativeHostBuilder = "$projectDir\tools\Build-NativeHost.ps1"
if (Test-Path $nativeHostBuilder) {
    Write-Host "`n[2/6] Compiling custom Native C++ hostfxr launcher (DiskMasterWinUI.exe)..." -ForegroundColor Yellow
    & $nativeHostBuilder -OutputExePath "$publishDir\DiskMasterWinUI.exe" -Architecture $Architecture | Out-Null
}

Copy-Item -Recurse -Force "$projectDir\Scripts" "$publishDir\"
Copy-Item -Recurse -Force "$projectDir\Assets" "$publishDir\"

# 1.0 Prune unused WindowsAppSDK satellite language folders (keeps en-us, zh-cn, zh-tw, ja-jp)
$supportedLangs = @('en-us', 'zh-cn', 'zh-tw', 'ja-jp')
Get-ChildItem -Path $publishDir -Directory | Where-Object {
    $n = $_.Name.ToLowerInvariant()
    $n -match '^[a-z]{2,3}(-[a-z0-9]+)*$' -and -not ($supportedLangs -contains $n)
} | Remove-Item -Recurse -Force

# 1.1 The Explorer "Recover with DiskMaster" shell launcher (DiskMasterShellHost.exe) is intentionally
# NOT shipped in the portable build: right-click context-menu integration is installer-only.
# Make sure a stale copy from an earlier installer publish never ends up in the payload.
Remove-Item "$publishDir\DiskMasterShellHost.exe" -Force -ErrorAction SilentlyContinue
Remove-Item "$publishDir\Uninstall.exe" -Force -ErrorAction SilentlyContinue

# 2. Sign core internal binaries prior to compression
if (Test-Path $signScript) {
    Write-Host "`n[3/6] Signing core payload binaries with Authenticode..." -ForegroundColor Yellow
    $signArgs = @{
        TargetPath = @("$publishDir\DiskMasterWinUI.exe", "$publishDir\DiskMasterWinUI.dll")
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
& icacls $finalExePath /reset /C 2>$null | Out-Null
& icacls $finalExePath /setintegritylevel Medium /C 2>$null | Out-Null

$exeSizeMb = [math]::Round((Get-Item $finalExePath).Length / 1MB, 2)
Write-Host "`n=================================================================" -ForegroundColor Green
Write-Host "  SUCCESS: True Single-File Standalone Portable EXE Generated!" -ForegroundColor Green
Write-Host "  Path: $finalExePath" -ForegroundColor Cyan
Write-Host "  File Size: $exeSizeMb MB" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Green
