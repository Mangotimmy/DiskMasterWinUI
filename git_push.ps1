<#
.SYNOPSIS
    Automated version bump, Git stage, commit, tag, and push script for DiskMaster Pro.

.DESCRIPTION
    1. Reads the current version from Directory.Build.props.
    2. Calculates the next semantic version (patch, minor, major, or explicit version).
    3. Updates Directory.Build.props, version.json, and DiskMasterSetup.iss.
    4. Stages changes with `git add -A`.
    5. Commits with a descriptive message.
    6. Tags the release (e.g., v1.5.10).
    7. Pushes branch and tag to GitHub (origin), which triggers automated CI/CD builds.

.PARAMETER Version
    Explicit version to set (e.g. "1.6.0"). Overrides -Bump if specified.

.PARAMETER Bump
    Semantic increment type: 'patch' (default), 'minor', 'major', or 'none' (leave version unchanged).

.PARAMETER Message
    Commit message. If omitted, defaults to "chore: release v{Version}" (or prompts in interactive sessions).

.PARAMETER ReleaseNotes
    Optional new release notes string to update in version.json.

.PARAMETER NoTag
    If specified, skips creating and pushing the Git tag.

.PARAMETER NoPush
    If specified, commits and tags locally without pushing to GitHub remote.

.PARAMETER DryRun
    Simulates all file updates and Git commands without writing files or mutating Git.

.PARAMETER Force
    Skips interactive confirmation prompts.

.EXAMPLE
    .\git_push.ps1
    # Auto-increments patch version (e.g. 1.5.9 -> 1.5.10), commits, tags, and pushes.

.EXAMPLE
    .\git_push.ps1 -Bump minor -Message "feat: add disk benchmarking module"

.EXAMPLE
    .\git_push.ps1 -Version "2.0.0" -Message "feat: major 2.0 release"

.EXAMPLE
    .\git_push.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("patch", "minor", "major", "none")][string]$Bump = "patch",
    [string]$Message = "",
    [string]$ReleaseNotes = "",
    [switch]$NoTag,
    [switch]$NoPush,
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
Set-Location $projectDir

# Helper to write UTF-8 without BOM across all PowerShell editions
function Set-Utf8NoBomContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "     DiskMaster Pro - Automated Version & Git Push Tool         " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. Verify Git is available and check repository status
try {
    $gitVersion = git --version
    Write-Host "[OK] Git runtime detected: $gitVersion" -ForegroundColor Green
}
catch {
    Write-Error "Git command not found in PATH. Please install Git or run from Git Bash."
    exit 1
}

$currentBranch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
if (-not $currentBranch) {
    Write-Error "Current directory is not a valid Git repository."
    exit 1
}
Write-Host "[OK] Active Git branch: $currentBranch" -ForegroundColor Green

# 2. Locate files to update
$propsPath = Join-Path $projectDir "Directory.Build.props"
$jsonPath  = Join-Path $projectDir "version.json"
$issPath   = Join-Path $projectDir "DiskMasterSetup.iss"

if (-not (Test-Path $propsPath)) {
    Write-Error "Directory.Build.props not found at: $propsPath"
    exit 1
}

# 3. Read current version from Directory.Build.props
$propsContent = Get-Content $propsPath -Raw -Encoding UTF8
if ($propsContent -match '<Version>(\d+\.\d+\.\d+)</Version>') {
    $currentVersion = $Matches[1]
}
elseif ($propsContent -match '<VersionPrefix>(\d+\.\d+\.\d+)</VersionPrefix>') {
    $currentVersion = $Matches[1]
}
else {
    Write-Error "Could not parse current semantic version (X.Y.Z) from Directory.Build.props"
    exit 1
}

Write-Host "`n[*] Current Project Version : v$currentVersion" -ForegroundColor Yellow

# 4. Compute next version
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Error "Invalid version format '$Version'. Expected semantic format X.Y.Z (e.g. 1.6.0)."
        exit 1
    }
    $newVersion = $Version
}
elseif ($Bump -eq "none") {
    $newVersion = $currentVersion
}
else {
    $parts = $currentVersion.Split('.')
    [int]$major = [int]$parts[0]
    [int]$minor = [int]$parts[1]
    [int]$patch = [int]$parts[2]

    switch ($Bump) {
        "patch" { $patch++ }
        "minor" { $minor++; $patch = 0 }
        "major" { $major++; $minor = 0; $patch = 0 }
    }
    $newVersion = "$major.$minor.$patch"
}

Write-Host "[*] Target Project Version  : v$newVersion" -ForegroundColor Cyan
Write-Host "[*] Version Bump Mode       : $Bump" -ForegroundColor DarkCyan

if ($DryRun) {
    Write-Host "`n>>> [DRY-RUN MODE ACTIVATED] - No files will be modified and no Git commands executed <<<" -ForegroundColor Magenta
}

# 5. Interactive confirmation (unless -Force or -DryRun)
if (-not $Force -and -not $DryRun) {
    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        $confirm = Read-Host "`nProceed with updating version to v$newVersion and pushing to origin/$currentBranch? [Y/n]"
        if ($confirm -and $confirm.Trim().ToLower() -notin @("y", "yes", "")) {
            Write-Host "Operation cancelled by user." -ForegroundColor Yellow
            exit 0
        }
    }
}

# 6. Prepare file updates
# --- 6a. Directory.Build.props ---
$updatedProps = $propsContent
$updatedProps = [regex]::Replace($updatedProps, '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$newVersion</VersionPrefix>")
$updatedProps = [regex]::Replace($updatedProps, '<Version>[^<]+</Version>', "<Version>$newVersion</Version>")
$updatedProps = [regex]::Replace($updatedProps, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$newVersion.0</AssemblyVersion>")
$updatedProps = [regex]::Replace($updatedProps, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$newVersion.0</FileVersion>")
$updatedProps = [regex]::Replace($updatedProps, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$newVersion</InformationalVersion>")

# --- 6b. version.json ---
$updatedJson = $null
if (Test-Path $jsonPath) {
    try {
        $jsonRaw = Get-Content $jsonPath -Raw -Encoding UTF8
        $jsonObj = $jsonRaw | ConvertFrom-Json
        $jsonObj.version = $newVersion
        $jsonObj.tag_name = "v$newVersion"
        $jsonObj.name = "DiskMaster Pro v$newVersion Flagship"
        $jsonObj.published_at = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        
        if ($ReleaseNotes) {
            $jsonObj.release_notes = $ReleaseNotes
        }

        # Update asset download URLs to match new tag
        if ($jsonObj.assets) {
            foreach ($asset in $jsonObj.assets) {
                if ($asset.browser_download_url) {
                    $asset.browser_download_url = [regex]::Replace($asset.browser_download_url, '/download/v[^/]+/', "/download/v$newVersion/")
                }
            }
        }
        $updatedJson = $jsonObj | ConvertTo-Json -Depth 10
    }
    catch {
        Write-Warning "Could not parse version.json: $_"
    }
}

# --- 6c. DiskMasterSetup.iss ---
$updatedIss = $null
if (Test-Path $issPath) {
    $issRaw = Get-Content $issPath -Raw -Encoding UTF8
    $updatedIss = [regex]::Replace($issRaw, '(#define\s+MyAppVersion\s+)"[^"]+"', "`$1`"$newVersion`"")
}

# 7. Apply or display file modifications
Write-Host "`n--- File Updates ---" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "[DRY-RUN] Would update Directory.Build.props -> Version $newVersion ($newVersion.0)" -ForegroundColor Gray
    if ($updatedJson) { Write-Host "[DRY-RUN] Would update version.json -> tag_name v$newVersion, published_at $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))" -ForegroundColor Gray }
    if ($updatedIss)  { Write-Host "[DRY-RUN] Would update DiskMasterSetup.iss -> MyAppVersion `"$newVersion`"" -ForegroundColor Gray }
}
else {
    Set-Utf8NoBomContent -Path $propsPath -Content $updatedProps
    Write-Host "[OK] Updated: Directory.Build.props" -ForegroundColor Green

    if ($updatedJson) {
        Set-Utf8NoBomContent -Path $jsonPath -Content $updatedJson
        Write-Host "[OK] Updated: version.json" -ForegroundColor Green
    }

    if ($updatedIss) {
        Set-Utf8NoBomContent -Path $issPath -Content $updatedIss
        Write-Host "[OK] Updated: DiskMasterSetup.iss" -ForegroundColor Green
    }
}

# 8. Commit message preparation
if (-not $Message) {
    if ($newVersion -ne $currentVersion) {
        $defaultMsg = "chore: bump version to $newVersion in Directory.Build.props and version.json"
    } else {
        $defaultMsg = "chore: update and push latest changes"
    }

    if (-not $Force -and -not $DryRun -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        $inputMsg = Read-Host "`nEnter commit message [press Enter for: `"$defaultMsg`"]"
        $Message = if ($inputMsg.Trim()) { $inputMsg.Trim() } else { $defaultMsg }
    } else {
        $Message = $defaultMsg
    }
}

Write-Host "`n[*] Commit Message: `"$Message`"" -ForegroundColor Cyan

# 9. Git Stage, Commit, Tag & Push
Write-Host "`n--- Git Operations ---" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "[DRY-RUN] Would execute: git add -A" -ForegroundColor Gray
    Write-Host "[DRY-RUN] Would execute: git commit -m `"$Message`"" -ForegroundColor Gray
    if (-not $NoTag -and ($newVersion -ne $currentVersion -or $Version)) {
        Write-Host "[DRY-RUN] Would execute: git tag -a `"v$newVersion`" -m `"DiskMaster Pro v$newVersion`"" -ForegroundColor Gray
    }
    if (-not $NoPush) {
        Write-Host "[DRY-RUN] Would execute: git push origin $currentBranch" -ForegroundColor Gray
        if (-not $NoTag -and ($newVersion -ne $currentVersion -or $Version)) {
            Write-Host "[DRY-RUN] Would execute: git push origin `"v$newVersion`"" -ForegroundColor Gray
        }
    }
    Write-Host "`n[DRY-RUN COMPLETE] Validation successful. Everything is ready." -ForegroundColor Green
    exit 0
}

# Real Git execution
# 9a. Stage all changes
Write-Host "[1/4] Staging working tree changes (git add -A)..." -ForegroundColor Yellow
git add -A
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to stage changes with git add."
    exit 1
}

# Check if there are staged changes to commit
$diffCheck = git diff --staged --name-only
if (-not $diffCheck) {
    Write-Host "[INFO] No staged changes to commit." -ForegroundColor Yellow
}
else {
    Write-Host "[2/4] Committing changes..." -ForegroundColor Yellow
    git commit -m "$Message"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Git commit failed."
        exit 1
    }
    Write-Host "[OK] Commit created successfully." -ForegroundColor Green
}

# 9b. Tagging
$tagName = "v$newVersion"
if (-not $NoTag) {
    # Check if tag already exists locally or remotely
    $tagExists = git tag -l $tagName
    if ($tagExists) {
        Write-Host "[SKIP] Git tag '$tagName' already exists locally." -ForegroundColor Yellow
    }
    else {
        Write-Host "[3/4] Creating annotated Git tag '$tagName'..." -ForegroundColor Yellow
        git tag -a "$tagName" -m "DiskMaster Pro $tagName"
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to create tag '$tagName'. Continuing with push..."
        }
        else {
            Write-Host "[OK] Tag '$tagName' created." -ForegroundColor Green
        }
    }
}
else {
    Write-Host "[SKIP] Git tagging skipped (-NoTag specified)." -ForegroundColor Gray
}

# 9c. Pushing to remote
if (-not $NoPush) {
    Write-Host "[4/4] Pushing branch '$currentBranch' to origin..." -ForegroundColor Yellow
    git push origin "$currentBranch"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to push commits to origin/$currentBranch."
        exit 1
    }
    Write-Host "[OK] Branch pushed successfully." -ForegroundColor Green

    if (-not $NoTag) {
        Write-Host "[*] Pushing tag '$tagName' to origin..." -ForegroundColor Yellow
        git push origin "$tagName"
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to push tag '$tagName' to origin. You may push it manually with: git push origin $tagName"
        }
        else {
            Write-Host "[OK] Tag '$tagName' pushed successfully." -ForegroundColor Green
            Write-Host "`n[*] GitHub Actions build triggered by tag: https://github.com/Mangotimmy/DiskMasterWinUI/actions" -ForegroundColor Cyan
        }
    }
}
else {
    Write-Host "[SKIP] Remote push skipped (-NoPush specified)." -ForegroundColor Gray
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "  Success! DiskMaster Pro version updated and pushed to GitHub. " -ForegroundColor Green
Write-Host "  Version: v$newVersion | Branch: $currentBranch                 " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Cyan
