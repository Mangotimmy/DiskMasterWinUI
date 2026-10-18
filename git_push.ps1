<#
.SYNOPSIS
    Automated file change inspection, version bump, Git stage, commit, tag, and push script for DiskMaster Pro.

.DESCRIPTION
    1. Inspects Git status and displays changed files to be uploaded.
    2. Handles clean working trees gracefully (detects unpushed commits or untagged versions).
    3. Calculates or prompts for next semantic version (patch, minor, major, keep current, or explicit).
    4. Synchronizes Directory.Build.props, version.json, and DiskMasterSetup.iss.
    5. Stages changes with `git add -A`.
    6. Commits with a descriptive message.
    7. Creates annotated release tag (e.g., v1.5.10).
    8. Pushes branch and tag to GitHub (origin), triggering automated CI/CD builds.

.PARAMETER Version
    Explicit version to set (e.g. "1.6.0"). Overrides -Bump if specified.

.PARAMETER Bump
    Semantic increment type: 'auto' (default: prompts/chooses based on changes), 'patch', 'minor', 'major', or 'none'.

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
    Non-interactive mode: proceeds with recommended defaults without interactive prompts.

.EXAMPLE
    .\git_push.ps1
    # Inspects file changes, prompts for version option, commits, tags, and pushes.

.EXAMPLE
    .\git_push.ps1 -Bump patch -Force
    # Non-interactive patch bump and push.

.EXAMPLE
    .\git_push.ps1 -Bump minor -Message "feat: add disk benchmarking module"

.EXAMPLE
    .\git_push.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("auto", "patch", "minor", "major", "none")][string]$Bump = "auto",
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
Write-Host "[OK] Active Git branch   : $currentBranch" -ForegroundColor Green

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
Write-Host "[*] Current Suite Version: v$currentVersion" -ForegroundColor Yellow

# 4. Inspect working tree changes and unpushed commits
$rawStatus = @(git status --porcelain 2>$null)
$unpushedCommits = @(git log "origin/${currentBranch}..HEAD" --oneline 2>$null)
$currentTagExists = git tag -l "v$currentVersion"

# Parse porcelain status lines
$changedFiles = @()
foreach ($line in $rawStatus) {
    if ($line.Length -ge 3) {
        $statusCode = $line.Substring(0, 2)
        $filePath   = $line.Substring(3).Trim()
        
        $statusDesc = switch -Regex ($statusCode) {
            '^\?\?' { "Untracked" }
            '^A'    { "Added" }
            '^M| M' { "Modified" }
            '^D| D' { "Deleted" }
            '^R'    { "Renamed" }
            default { "Changed ($statusCode)" }
        }
        $changedFiles += [PSCustomObject]@{
            Status = $statusDesc
            Path   = $filePath
        }
    }
}

# --- Handle Clean Working Tree State ---
if ($changedFiles.Count -eq 0) {
    Write-Host "`n[*] Working Tree Status  : Clean (no uncommitted file modifications)" -ForegroundColor Gray

    # Case A: Unpushed commits exist
    if ($unpushedCommits.Count -gt 0) {
        Write-Host "`n--- Unpushed Commits Ready to Upload ($($unpushedCommits.Count)) ---" -ForegroundColor Yellow
        foreach ($c in $unpushedCommits) {
            Write-Host "  Commit: $c" -ForegroundColor Cyan
        }

        if ($DryRun) {
            Write-Host "`n[DRY-RUN] Would push $($unpushedCommits.Count) unpushed commit(s) to origin/${currentBranch}" -ForegroundColor Magenta
            if (-not $currentTagExists -and -not $NoTag) {
                Write-Host "[DRY-RUN] Would create & push tag 'v$currentVersion' to trigger GitHub Actions release" -ForegroundColor Magenta
            }
            Write-Host "[DRY-RUN COMPLETE] Validation successful." -ForegroundColor Green
            exit 0
        }

        if (-not $Force -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
            $confirmPush = Read-Host "`nPush $($unpushedCommits.Count) unpushed commit(s) to origin/${currentBranch}? [Y/n]"
            if ($confirmPush -and $confirmPush.Trim().ToLower() -notin @("y", "yes", "")) {
                Write-Host "Push cancelled by user." -ForegroundColor Yellow
                exit 0
            }
        }

        if (-not $NoPush) {
            Write-Host "`nPushing branch '$currentBranch' to origin..." -ForegroundColor Yellow
            git push origin "$currentBranch"
            if ($LASTEXITCODE -ne 0) { Write-Error "Failed to push to origin/${currentBranch}"; exit 1 }
            Write-Host "[OK] Branch pushed successfully." -ForegroundColor Green

            if (-not $currentTagExists -and -not $NoTag) {
                Write-Host "Creating and pushing release tag 'v$currentVersion'..." -ForegroundColor Yellow
                git tag -a "v$currentVersion" -m "DiskMaster Pro v$currentVersion"
                git push origin "v$currentVersion"
                Write-Host "[OK] Tag 'v$currentVersion' pushed to GitHub." -ForegroundColor Green
            }
        }
        Write-Host "`n[OK] Push complete." -ForegroundColor Green
        exit 0
    }

    # Case B: Untagged current release
    if (-not $currentTagExists -and -not $NoTag) {
        Write-Host "`n[!] Current version in Directory.Build.props is v$currentVersion, but Git tag 'v$currentVersion' does not exist." -ForegroundColor Yellow
        Write-Host "    Pushing tag 'v$currentVersion' will trigger the GitHub Actions release workflow." -ForegroundColor Gray

        if ($DryRun) {
            Write-Host "`n[DRY-RUN] Would create and push tag 'v$currentVersion' to origin." -ForegroundColor Magenta
            Write-Host "[DRY-RUN COMPLETE] Validation successful." -ForegroundColor Green
            exit 0
        }

        if (-not $Force -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
            $tagChoice = Read-Host "`nCreate and push tag 'v$currentVersion' to trigger GitHub Actions release? [Y/n]"
            if ($tagChoice -and $tagChoice.Trim().ToLower() -notin @("y", "yes", "")) {
                Write-Host "Operation cancelled by user." -ForegroundColor Yellow
                exit 0
            }
        }

        Write-Host "`nCreating annotated Git tag 'v$currentVersion'..." -ForegroundColor Yellow
        git tag -a "v$currentVersion" -m "DiskMaster Pro v$currentVersion"
        if (-not $NoPush) {
            Write-Host "Pushing tag 'v$currentVersion' to origin..." -ForegroundColor Yellow
            git push origin "v$currentVersion"
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not push tag to remote." }
            else {
                Write-Host "[OK] Tag 'v$currentVersion' pushed successfully." -ForegroundColor Green
                Write-Host "[*] GitHub Actions release triggered: https://github.com/Mangotimmy/DiskMasterWinUI/actions" -ForegroundColor Cyan
            }
        }
        exit 0
    }

    # Case C: Fully up-to-date and clean
    Write-Host "`n[OK] Working tree clean. All files and tags (v$currentVersion) are already up to date with origin/${currentBranch}." -ForegroundColor Green
    if (-not $Force) {
        Write-Host "No file changes detected to upload. Exiting safely." -ForegroundColor Gray
        exit 0
    }
}

# --- Display Changed Files to be Uploaded ---
Write-Host "`n--- Detected Files to be Committed & Uploaded ($($changedFiles.Count)) ---" -ForegroundColor Yellow
foreach ($file in $changedFiles) {
    $color = switch ($file.Status) {
        "Modified"  { "Yellow" }
        "Added"     { "Green" }
        "Deleted"   { "Red" }
        "Untracked" { "DarkYellow" }
        default     { "White" }
    }
    Write-Host "  [$($file.Status.PadRight(9))] $($file.Path)" -ForegroundColor $color
}

# 5. Compute or Select Target Version
$parts = $currentVersion.Split('.')
[int]$major = [int]$parts[0]
[int]$minor = [int]$parts[1]
[int]$patch = [int]$parts[2]
$nextPatch = "$major.$minor.$($patch + 1)"
$nextMinor = "$major.$($minor + 1).0"
$nextMajor = "$($major + 1).0.0"

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
elseif ($Bump -eq "patch") {
    $newVersion = $nextPatch
}
elseif ($Bump -eq "minor") {
    $newVersion = $nextMinor
}
elseif ($Bump -eq "major") {
    $newVersion = $nextMajor
}
else {
    # Bump mode is 'auto': Prompt interactively if possible, otherwise default to patch
    if (-not $Force -and -not $DryRun -and [Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        Write-Host "`nVersion Options for this Upload:" -ForegroundColor Cyan
        Write-Host "  [1] Increment patch  : v$currentVersion -> v$nextPatch (Recommended)" -ForegroundColor White
        Write-Host "  [2] Keep current     : v$currentVersion (push changes without changing version)" -ForegroundColor White
        Write-Host "  [3] Increment minor  : v$currentVersion -> v$nextMinor" -ForegroundColor White
        Write-Host "  [4] Custom version   : (enter custom X.Y.Z)" -ForegroundColor White

        $choice = Read-Host "`nSelect version option [1/2/3/4, press Enter for 1]"
        switch ($choice.Trim()) {
            "2" { $newVersion = $currentVersion }
            "3" { $newVersion = $nextMinor }
            "4" {
                $customInput = Read-Host "Enter custom version (X.Y.Z)"
                if ($customInput -notmatch '^\d+\.\d+\.\d+$') {
                    Write-Error "Invalid version format '$customInput'."
                    exit 1
                }
                $newVersion = $customInput.Trim()
            }
            default { $newVersion = $nextPatch }
        }
    }
    else {
        $newVersion = $nextPatch
    }
}

Write-Host "`n[*] Target Version for Upload: v$newVersion" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "`n>>> [DRY-RUN MODE ACTIVATED] - No files will be modified and no Git commands executed <<<" -ForegroundColor Magenta
}

# 6. Interactive confirmation of action and branch (with fixed string interpolation)
if (-not $Force -and -not $DryRun) {
    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        $confirm = Read-Host "`nProceed with staging $($changedFiles.Count) files, version v$newVersion, and pushing to origin/${currentBranch}? [Y/n]"
        if ($confirm -and $confirm.Trim().ToLower() -notin @("y", "yes", "")) {
            Write-Host "Operation cancelled by user." -ForegroundColor Yellow
            exit 0
        }
    }
}

# 7. Prepare and apply file updates (Directory.Build.props, version.json, DiskMasterSetup.iss)
$needsFileUpdate = ($newVersion -ne $currentVersion)

if ($needsFileUpdate) {
    Write-Host "`n--- Synchronizing Version to v$newVersion ---" -ForegroundColor Yellow

    # 7a. Directory.Build.props
    $updatedProps = $propsContent
    $updatedProps = [regex]::Replace($updatedProps, '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$newVersion</VersionPrefix>")
    $updatedProps = [regex]::Replace($updatedProps, '<Version>[^<]+</Version>', "<Version>$newVersion</Version>")
    $updatedProps = [regex]::Replace($updatedProps, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$newVersion.0</AssemblyVersion>")
    $updatedProps = [regex]::Replace($updatedProps, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$newVersion.0</FileVersion>")
    $updatedProps = [regex]::Replace($updatedProps, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$newVersion</InformationalVersion>")

    # 7b. version.json
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

    # 7c. DiskMasterSetup.iss
    $updatedIss = $null
    if (Test-Path $issPath) {
        $issRaw = Get-Content $issPath -Raw -Encoding UTF8
        $updatedIss = [regex]::Replace($issRaw, '(#define\s+MyAppVersion\s+)"[^"]+"', "`$1`"$newVersion`"")
    }

    if ($DryRun) {
        Write-Host "[DRY-RUN] Would update Directory.Build.props -> Version $newVersion ($newVersion.0)" -ForegroundColor Gray
        if ($updatedJson) { Write-Host "[DRY-RUN] Would update version.json -> tag_name v$newVersion" -ForegroundColor Gray }
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
}
else {
    Write-Host "`n[*] Version unchanged (v$currentVersion). Proceeding with code upload." -ForegroundColor Cyan
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

# 9. Git Operations
Write-Host "`n--- Git Operations ---" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "[DRY-RUN] Would execute: git add -A" -ForegroundColor Gray
    Write-Host "[DRY-RUN] Would execute: git commit -m `"$Message`"" -ForegroundColor Gray
    if (-not $NoTag) {
        Write-Host "[DRY-RUN] Would execute: git tag -a `"v$newVersion`" -m `"DiskMaster Pro v$newVersion`"" -ForegroundColor Gray
    }
    if (-not $NoPush) {
        Write-Host "[DRY-RUN] Would execute: git push origin $currentBranch" -ForegroundColor Gray
        if (-not $NoTag) {
            Write-Host "[DRY-RUN] Would execute: git push origin `"v$newVersion`"" -ForegroundColor Gray
        }
    }
    Write-Host "`n[DRY-RUN COMPLETE] Validation successful. Everything is ready." -ForegroundColor Green
    exit 0
}

# 9a. Stage all changes
Write-Host "[1/4] Staging changes (git add -A)..." -ForegroundColor Yellow
git add -A
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to stage changes with git add."
    exit 1
}

# Check staged changes
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
        Write-Error "Failed to push commits to origin/${currentBranch}."
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
Write-Host "  Success! DiskMaster Pro files uploaded and pushed to GitHub.  " -ForegroundColor Green
Write-Host "  Version: v$newVersion | Branch: $currentBranch                 " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Cyan
