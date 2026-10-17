<#
.SYNOPSIS
    Compiles the clean Win32 C++ hostfxr bootstrapper (DiskMasterWinUI.exe) using MSVC,
    replacing the stock .NET SDK apphost.exe template that triggers Windows Defender ML false positives.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputExePath,

    [string]$Architecture = "win-x64"
)

$ErrorActionPreference = "Stop"

$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vcvarsPath = $null

if (Test-Path $vsWhere) {
    $vsInstallDir = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
    if ($vsInstallDir) {
        $candidate = Join-Path $vsInstallDir "VC\Auxiliary\Build\vcvarsall.bat"
        if (Test-Path $candidate) { $vcvarsPath = $candidate }
    }
}

if (-not $vcvarsPath) {
    $fallbacks = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvarsall.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
    )
    $vcvarsPath = $fallbacks | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $vcvarsPath) {
    Write-Warning "[NativeHost] Visual Studio C++ Build Tools (vcvarsall.bat) not found; keeping default .NET apphost.exe."
    return $false
}

$vcArch = if ($Architecture -eq "win-arm64") { "x64_arm64" } elseif ($Architecture -eq "win-x86") { "x86" } else { "x64" }
$projectRoot = Split-Path -Parent $PSScriptRoot
$hostDir = Join-Path $PSScriptRoot "DiskMasterNativeHost"
$buildTemp = Join-Path $hostDir "obj_$Architecture"
if (Test-Path $buildTemp) { Remove-Item $buildTemp -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $buildTemp -Force | Out-Null

# Resolve the Windows App SDK merged SxS manifest (contains <winrtv1:activatableClass> entries required by unpackaged WinUI 3)
$mergedManifestCandidates = @(
    (Join-Path $projectRoot "obj\Release\net9.0-windows10.0.26100.0\$Architecture\Manifests\app.manifest"),
    (Join-Path $projectRoot "obj\Debug\net9.0-windows10.0.26100.0\$Architecture\Manifests\app.manifest")
)
$sourceManifest = $mergedManifestCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sourceManifest) {
    $foundManifest = Get-ChildItem (Join-Path $projectRoot "obj") -Recurse -Filter "app.manifest" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "Manifests" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($foundManifest) { $sourceManifest = $foundManifest.FullName }
}
if (-not $sourceManifest) {
    $sourceManifest = Join-Path $projectRoot "app.manifest"
}
$mergedManifestDest = Join-Path $buildTemp "merged_app.manifest"
Copy-Item -Path $sourceManifest -Destination $mergedManifestDest -Force
Write-Host "[NativeHost] Embedding WinRT SxS manifest: $sourceManifest ($((Get-Item $mergedManifestDest).Length) bytes)" -ForegroundColor Cyan

$rcFile = Join-Path $hostDir "resource.rc"
$resFile = Join-Path $buildTemp "resource.res"
$cppFile = Join-Path $hostDir "main.cpp"
$objFile = Join-Path $buildTemp "main.obj"
$outExe = Join-Path $buildTemp "DiskMasterWinUI.exe"

$cmdScript = @"
@echo off
call "$vcvarsPath" $vcArch >nul
if errorlevel 1 exit /b 1
rc.exe /nologo /i "$buildTemp" /fo "$resFile" "$rcFile"
if errorlevel 1 exit /b 1
cl.exe /nologo /utf-8 /O2 /GL /EHsc /MT /DNDEBUG /W3 /Fo"$objFile" /Fe"$outExe" "$cppFile" "$resFile" /link /SUBSYSTEM:WINDOWS /OPT:REF /OPT:ICF /LTCG /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT /MANIFEST:NO
if errorlevel 1 exit /b 1
"@

$batFile = Join-Path $buildTemp "compile.cmd"
Set-Content -Path $batFile -Value $cmdScript -Encoding ASCII

Push-Location $hostDir
try {
    & cmd.exe /c "`"$batFile`""
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $outExe)) {
        throw "MSVC compilation failed with exit code $LASTEXITCODE"
    }
    Copy-Item -Path $outExe -Destination $OutputExePath -Force
    Write-Host "[NativeHost] Compiled clean native C++ hostfxr launcher -> $OutputExePath" -ForegroundColor Green
}
finally {
    Pop-Location
    Remove-Item $buildTemp -Recurse -Force -ErrorAction SilentlyContinue
}
return $true
