# ============================================================================
#
# push.ps1 - Auto-push to GitHub with automatic version increment
# Copyright (c) NoID Softwork 2026-2026. All rights reserved.
#
# ============================================================================
#
# Versioning Algorithm:
#   Format: a.b.c.MMDD
#     a    = Frontend update (GUI) - resets b and c to 0
#     b    = Backend update        - resets c to 0
#     c    = Little fix / patch
#     MMDD = Month and day of push (auto-generated)
#
#   On "frontend": a++, b=0, c=0
#   On "backend":  b++, c=0
#   On "fix":      c++
#
# Usage:
#   .\push.ps1                                        # fix bump, default commit
#   .\push.ps1 -Type backend -CommitMessage "My msg"
#   .\push.ps1 -Type frontend -AttachAssets           # build zip + GitHub release
#   .\push.ps1 -SkipVersion                           # commit without bumping
#   .\push.ps1 -NoRelease                             # push without GitHub release
#   .\push.ps1 -PreRelease                            # mark GitHub release as pre-release; appends -Pre-release_N suffix
#   .\push.ps1 -Type none                             # update MMDD/CHANGELOG only, don't bump a/b/c
# ============================================================================

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("frontend", "backend", "fix", "none")]
    [string]$Type = "fix",

    [Parameter(Mandatory=$false)]
    [string]$CommitMessage = "Auto-commit: Version update",

    [Parameter(Mandatory=$false)]
    [string]$Branch = "main",

    [Parameter(Mandatory=$false)]
    [switch]$AttachAssets,

    [Parameter(Mandatory=$false)]
    [switch]$SkipVersion,

    [Parameter(Mandatory=$false)]
    [switch]$NoRelease,

    [Parameter(Mandatory=$false)]
    [switch]$PreRelease
)

$projectFilePath  = "PaDDY.csproj"
$audioProjectFilePath = "NoIDSoftwork.AudioProcessor/NoIDSoftwork.AudioProcessor.csproj"
$solutionPath     = "PaDDY.sln"
$assemblyInfoPath = "AssemblyInfo.cs"
$changelogPath    = "CHANGELOG.md"

# Will populate with changelog content to use as GitHub release notes
$releaseNotes = $null

# Verify we are in a git repo
git rev-parse --show-toplevel > $null 2>&1
if (-not $?) {
    Write-Host "❌ Error: Not in a Git repository!" -ForegroundColor Red
    exit 1
}

Write-Host "[START] Auto-push process..." -ForegroundColor Cyan
Write-Host "[TYPE]  Update type: $Type"   -ForegroundColor Yellow
Write-Host "[FLAGS] NoRelease: $NoRelease  AttachAssets: $AttachAssets  SkipVersion: $SkipVersion  PreRelease: $PreRelease" -ForegroundColor Yellow

$trackedGitPaths = @(git ls-files --full-name)

function Resolve-GitPath {
    param ([string]$Path)
    $normalizedPath = $Path -replace '\\', '/'
    $trackedPath = $trackedGitPaths | Where-Object {
        $_.ToLowerInvariant() -eq $normalizedPath.ToLowerInvariant()
    } | Select-Object -First 1
    if ($trackedPath) { return $trackedPath }
    return $normalizedPath
}

function Add-GitPath {
    param ([string]$Path)
    $gitPath = Resolve-GitPath -Path $Path
    git add -- $gitPath
    if (-not $?) {
        Write-Host "[ERROR] Failed to stage $Path (resolved to $gitPath)" -ForegroundColor Red
        exit 1
    }
}

function Update-ProjectVersion {
    param(
        [string]$Path,
        [string]$UpdateType,
        [string]$NewVersion
    )

    if (-not (Test-Path $Path)) {
        Write-Host "[WARNING] Project not found at $Path" -ForegroundColor Yellow
        return $null
    }

    [xml]$proj = Get-Content $Path

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $ver = $proj.Project.PropertyGroup.Version
    if (-not $ver) { $ver = "0.1.0.0101" }

    if ($NewVersion) {
        $proj.Project.PropertyGroup.Version              = $NewVersion
        $proj.Project.PropertyGroup.AssemblyVersion      = $NewVersion
        $proj.Project.PropertyGroup.FileVersion          = $NewVersion
        if ($null -ne $proj.Project.PropertyGroup.InformationalVersion) {
            $proj.Project.PropertyGroup.InformationalVersion = $NewVersion
        }
        $proj.Save((Resolve-Path $Path).Path)
        Write-Host "[$projectName] Updated to: $NewVersion" -ForegroundColor Green
        return $NewVersion
    }

    Write-Host "[$projectName] Current version: $ver" -ForegroundColor White

    $parts = $ver -split '\.'
    while ($parts.Count -lt 3) { $parts += "0" }

    [int]$vA = $parts[0]
    [int]$vB = $parts[1]
    [int]$vC = $parts[2]

    switch ($UpdateType) {
        "frontend" {
            $vA++
            $vB = 0
            $vC = 0
            Write-Host "[$projectName] Frontend version incremented" -ForegroundColor Green
        }
        "backend" {
            $vB++
            $vC = 0
            Write-Host "[$projectName] Backend version incremented" -ForegroundColor Green
        }
        "fix" {
            $vC++
            Write-Host "[$projectName] Fix version incremented" -ForegroundColor Green
        }
        "none" {
            Write-Host "[$projectName] Type 'none': keeping a.b.c, updating MMDD only" -ForegroundColor DarkGray
        }
    }

    $today   = Get-Date
    $dateStr = $today.ToString("MMdd")
    $newVer  = "$vA.$vB.$vC.$dateStr"

    $proj.Project.PropertyGroup.Version         = $newVer
    $proj.Project.PropertyGroup.AssemblyVersion = $newVer
    $proj.Project.PropertyGroup.FileVersion     = $newVer

    $proj.Save((Resolve-Path $Path).Path)
    Write-Host "[$projectName] Updated to: $newVer" -ForegroundColor Green

    return $newVer
}

function Update-ReadmeVersionBadge {
    param ([string]$Version)

    $versionParts = $Version -split '\.'
    if ($versionParts.Count -ge 3) {
        $simplifiedVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2])"
    } else {
        $simplifiedVersion = $Version
    }

    $readmePath = "README.md"
    if (Test-Path $readmePath) {
        Write-Host "[README] Updating version badge to $simplifiedVersion" -ForegroundColor Cyan

        $readmeContent = Get-Content $readmePath -Raw -Encoding UTF8
        $pattern = '(?<=version-)\d+\.\d+\.\d+(?=-blue)'

        if ($readmeContent -match $pattern) {
            $readmeContent = $readmeContent -replace $pattern, $simplifiedVersion
            Set-Content $readmePath $readmeContent -Encoding UTF8
            Write-Host "[SUCCESS] README.md version badge updated to $simplifiedVersion" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] Version badge pattern not found in README.md; no changes made" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[WARNING] README.md not found" -ForegroundColor Yellow
    }
}

# ── Version Update ──────────────────────────────────────────────────────────
if (-not $SkipVersion) {
    $newVersion = Update-ProjectVersion -Path $projectFilePath -UpdateType $Type
    Update-ProjectVersion -Path $audioProjectFilePath -UpdateType $Type -NewVersion $newVersion | Out-Null
} else {
    Write-Host "[INFO] SkipVersion is set; not incrementing version" -ForegroundColor Yellow
    [xml]$p  = Get-Content $projectFilePath
    $newVersion = $p.Project.PropertyGroup.Version
}

# ── Pre-release Suffix ───────────────────────────────────────────────────────
if ($PreRelease) {
    $existingPreTags = @(git tag --list "v$newVersion-Pre-release_*")
    $preReleaseNumber = $existingPreTags.Count + 1
    $preReleaseVersion = "$newVersion-Pre-release_$preReleaseNumber"
    Write-Host "[PRE-RELEASE] Suffix: -Pre-release_$preReleaseNumber (found $($existingPreTags.Count) existing tag(s))" -ForegroundColor Magenta
    # Patch <Version> in .csproj files; AssemblyVersion/FileVersion remain numeric
    # InformationalVersion gets the pre-release suffix
    if (Test-Path $audioProjectFilePath) {
        [xml]$audioCsproj = Get-Content $audioProjectFilePath
        $audioCsproj.Project.PropertyGroup.InformationalVersion = $preReleaseVersion
        $audioCsproj.Save((Resolve-Path $audioProjectFilePath).Path)
        Write-Host "[PRE-RELEASE] Patched InformationalVersion in $audioProjectFilePath" -ForegroundColor Magenta
    }
} else {
    $preReleaseVersion = $newVersion
}

# ── AssemblyInfo / README Sync ───────────────────────────────────────────────
if (-not $SkipVersion) {
    # Sync AssemblyInfo.cs
    if (Test-Path $assemblyInfoPath) {
        $assemblyInfoContent = Get-Content $assemblyInfoPath -Raw -Encoding UTF8

        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyVersion\("[^"]*"\)\]',               "[assembly: AssemblyVersion(""$newVersion"")]"
        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyFileVersion\("[^"]*"\)\]',           "[assembly: AssemblyFileVersion(""$newVersion"")]"
        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyInformationalVersion\("[^"]*"\)\]',  "[assembly: AssemblyInformationalVersion(""$preReleaseVersion"")]"

        # Update copyright year dynamically (keep start year, update end year to current)
        $currentYear = (Get-Date).Year
        $assemblyInfoContent = $assemblyInfoContent -replace '\[assembly: AssemblyCopyright\("Copyright \(c\) NoID Softwork \d{4}-\d{4}"\)\]',
            "[assembly: AssemblyCopyright(""Copyright (c) NoID Softwork 2020-$currentYear"")]"

        # Append any missing attributes
        if ($assemblyInfoContent -notmatch 'AssemblyVersion') {
            $assemblyInfoContent += "`r`n[assembly: AssemblyVersion(""$newVersion"")]"
        }
        if ($assemblyInfoContent -notmatch 'AssemblyFileVersion') {
            $assemblyInfoContent += "`r`n[assembly: AssemblyFileVersion(""$newVersion"")]"
        }
        if ($assemblyInfoContent -notmatch 'AssemblyInformationalVersion') {
            $assemblyInfoContent += "`r`n[assembly: AssemblyInformationalVersion(""$preReleaseVersion"")]"
        }

        Set-Content -Path $assemblyInfoPath -Value $assemblyInfoContent -Encoding UTF8
        Write-Host "[SUCCESS] AssemblyInfo.cs updated with version: $newVersion (informational: $preReleaseVersion)" -ForegroundColor Green
    }

    # Update README badge when AttachAssets is requested (always use numeric base version)
    if ($AttachAssets) {
        Update-ReadmeVersionBadge -Version $newVersion
    }
}

# ── CHANGELOG Update ─────────────────────────────────────────────────────────
if (-not $SkipVersion) {
    Write-Host "[CHANGELOG] Updating CHANGELOG.md..." -ForegroundColor Cyan

    $date = Get-Date -Format "yyyy-MM-dd"

    if (Test-Path $changelogPath) {
        $content = Get-Content $changelogPath -Raw -Encoding UTF8

        $versionPattern = [regex]::Escape("## [$preReleaseVersion]")
        if ($content -match $versionPattern) {
            Write-Host "[INFO] Version $preReleaseVersion already exists in CHANGELOG.md; skipping update" -ForegroundColor Yellow
            $existingPattern = "(?s)(## \[$([regex]::Escape($preReleaseVersion))\].*?)(?=\n## \[|$)"
            if ($content -match $existingPattern) {
                $releaseNotes = $matches[1].Trim()
            }
        } else {
            # Gather commit list
            $commitList = @()

            if ($CommitMessage -and $CommitMessage -ne "Auto-commit: Version update") {
                $cleanMessage = $CommitMessage -replace "^Auto-commit:\s*", ""
                $commitList += "- $cleanMessage"
            }

            $lastTag = git describe --tags --abbrev=0 2>$null
            if ($lastTag) {
                $commits = git log "$lastTag..HEAD" --pretty=format:"%s" --no-merges 2>$null
                if ($commits) {
                    foreach ($commit in $commits) {
                        if ($commit -notmatch "^Auto-commit:|^Release |^Version update") {
                            $entry = "- $commit"
                            if ($commitList -notcontains $entry) {
                                $commitList += $entry
                            }
                        }
                    }
                }
            }

            if ($commitList.Count -eq 0) {
                $commitList = @("- Version bump")
            }

            $categorySection = ($commitList -join "`n")
            $newEntry = @"
## [$preReleaseVersion] - $date

$categorySection
"@

            $releaseNotes = $newEntry.Trim()

            $lines = $content -split "`n"
            $headerEndIndex = -1

            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = $lines[$i].Trim()
                if ($line -match '^\#\# \[[\d\.]+\]') {
                    $headerEndIndex = $i
                    break
                }
            }

            if ($headerEndIndex -gt 0) {
                $headerPart = ($lines[0..($headerEndIndex - 1)] -join "`n").TrimEnd()
                $restPart   = ($lines[$headerEndIndex..($lines.Count - 1)] -join "`n").TrimEnd()
                $newContent = "$headerPart`n`n$newEntry`n`n$restPart`n"
                $newContent = $newContent -replace '(\r?\n){3,}', "`n`n"
                Set-Content $changelogPath $newContent -Encoding UTF8 -NoNewline
                Write-Host "[SUCCESS] CHANGELOG.md updated with version $newVersion" -ForegroundColor Green
            } elseif ($headerEndIndex -eq -1) {
                $newContent = $content.TrimEnd() + "`n`n$newEntry`n"
                $newContent = $newContent -replace '(\r?\n){3,}', "`n`n"
                Set-Content $changelogPath $newContent -Encoding UTF8 -NoNewline
                Write-Host "[SUCCESS] CHANGELOG.md updated with version $newVersion (first entry)" -ForegroundColor Green
            } else {
                Write-Host "[WARNING] Could not find insertion point in CHANGELOG.md" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "[WARNING] CHANGELOG.md not found" -ForegroundColor Yellow
    }
}

# ── Stage ───────────────────────────────────────────────────────────────────
Write-Host "[STAGING] Changes..." -ForegroundColor Cyan
Add-GitPath -Path $projectFilePath
Add-GitPath -Path $audioProjectFilePath
if (Test-Path $solutionPath) { Add-GitPath -Path $solutionPath }
if (-not $SkipVersion) {
    Add-GitPath -Path $changelogPath
    Add-GitPath -Path $assemblyInfoPath
    if ($AttachAssets) {
        Add-GitPath -Path "README.md"
    }
}

# ── Commit ───────────────────────────────────────────────────────────────────
$finalCommitMessage = "$CommitMessage (v$newVersion)"
Write-Host "[COMMITTING] Changes..." -ForegroundColor Cyan
git commit -m $finalCommitMessage

if ($?) {
    Write-Host "[SUCCESS] Commit successful" -ForegroundColor Green
} else {
    Write-Host "[WARNING] No changes to commit (this might be fine)" -ForegroundColor Yellow
}

# ── Branch ───────────────────────────────────────────────────────────────────
$branchExists = git rev-parse --verify $Branch 2>$null
if (-not $branchExists) {
    Write-Host "[BRANCH] Creating branch: $Branch" -ForegroundColor Cyan
    git checkout -b $Branch
}

# ── Push ─────────────────────────────────────────────────────────────────────
Write-Host "[PUSHING] To GitHub ($Branch)..." -ForegroundColor Cyan
git push origin $Branch

if ($?) {
    Write-Host "[SUCCESS] Pushed to GitHub!" -ForegroundColor Green
    Write-Host "[VERSION] New version: $newVersion" -ForegroundColor Yellow
} else {
    Write-Host "[ERROR] Failed to push to GitHub!" -ForegroundColor Red
    exit 1
}

# ── Tag ──────────────────────────────────────────────────────────────────────
$tagName    = "v$preReleaseVersion"
$existingTag = git tag --list $tagName
if (-not $existingTag) {
    Write-Host "[TAG] Creating annotated tag: $tagName" -ForegroundColor Cyan
    $tagMessage = if ($PreRelease) { "Pre-Release $tagName" } else { "Release $tagName" }
    git tag -a $tagName -m $tagMessage
    if ($?) {
        Write-Host "[TAG] Pushing tag $tagName to origin" -ForegroundColor Cyan
        git push origin $tagName
        if ($?) {
            Write-Host "[SUCCESS] Tag pushed: $tagName" -ForegroundColor Green
        } else {
            Write-Host "[WARNING] Failed to push tag $tagName" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[WARNING] Failed to create tag $tagName" -ForegroundColor Yellow
    }
} else {
    Write-Host "[INFO] Tag $tagName already exists; skipping tag creation" -ForegroundColor Yellow
}

# ── Build Assets (before release creation, so zip can be attached inline) ────
$zipPath = $null
if ($AttachAssets -and -not $NoRelease) {
    Write-Host "[ASSETS] AttachAssets requested; building artifacts before release creation" -ForegroundColor Cyan

    $artifactRoot = Join-Path $PSScriptRoot "bin\artifacts"
    $publishDir   = Join-Path $artifactRoot "PaDDY-$newVersion"

    # Clean previous artifacts
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    Write-Host "[BUILD] dotnet publish -c Release -o $publishDir -p:DebugType=None" -ForegroundColor Cyan
    dotnet publish $projectFilePath -c Release -o $publishDir -p:DebugType=None

    if (-not $?) {
        Write-Host "[ERROR] dotnet publish failed; skipping artifact upload" -ForegroundColor Red
    } else {
        # Remove dev files
        Write-Host "[CLEANUP] Removing dev files (*.pdb, *.xml) from release" -ForegroundColor Cyan
        Get-ChildItem -Path $publishDir -Include *.pdb, *.xml -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

        # Remove appsettings.json (regenerated at runtime)
        $appSettingsInPublish = Join-Path $publishDir "appsettings.json"
        if (Test-Path $appSettingsInPublish) {
            Remove-Item $appSettingsInPublish -Force
            Write-Host "[CLEANUP] Removed appsettings.json from release" -ForegroundColor Green
        }

        # Create zip
        $zipName = "PaDDY-$newVersion.zip"
        $zipPath = Join-Path $artifactRoot $zipName
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Write-Host "[ZIP] Creating $zipPath" -ForegroundColor Cyan
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
        Write-Host "[ZIP] Created: $zipName" -ForegroundColor Green
    }
} elseif ($AttachAssets -and $NoRelease) {
    Write-Host "[INFO] AttachAssets was requested but NoRelease is set; skipping asset build" -ForegroundColor Yellow
}

# ── GitHub Release ───────────────────────────────────────────────────────────
if ($NoRelease) {
    Write-Host "[INFO] NoRelease flag is set; skipping GitHub release creation" -ForegroundColor Yellow
} else {
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        if (-not $releaseNotes) { $releaseNotes = "Automated release for $tagName" }

        # If AttachAssets, gather all changelog entries since the last GitHub release
        if ($AttachAssets) {
            Write-Host "[RELEASE NOTES] Gathering all changes since last release..." -ForegroundColor Cyan

            $lastReleaseTag = gh release list --limit 1 --json tagName --jq '.[0].tagName' 2>$null
            if ($lastReleaseTag) {
                Write-Host "[RELEASE NOTES] Last release was: $lastReleaseTag" -ForegroundColor Yellow

                if (Test-Path $changelogPath) {
                    $changelogContent    = Get-Content $changelogPath -Raw -Encoding UTF8
                    $lastReleaseVersion  = $lastReleaseTag -replace '^v', ''
                    $pattern = "(?s)(## \[$([regex]::Escape($newVersion))\].*?)(?=## \[$([regex]::Escape($lastReleaseVersion))\]|$)"

                    if ($changelogContent -match $pattern) {
                        $releaseNotes = $matches[1].Trim()
                        Write-Host "[RELEASE NOTES] Collected changelog entries since $lastReleaseTag" -ForegroundColor Green
                    } else {
                        Write-Host "[RELEASE NOTES] Parsing commits since $lastReleaseTag..." -ForegroundColor Yellow
                        $allCommits = git log "$lastReleaseTag..HEAD" --pretty=format:"- %s" --no-merges 2>$null
                        if ($allCommits) {
                            $filteredCommits = $allCommits | Where-Object { $_ -notmatch "^- Auto-commit:|^- Release |^- Version update" }
                            if ($filteredCommits) {
                                $releaseNotes = "## [$newVersion]`n`n### Changelog`n" + ($filteredCommits -join "`n")
                            }
                        }
                    }
                }
            } else {
                Write-Host "[RELEASE NOTES] No previous release found; using current changelog only" -ForegroundColor Yellow
            }
        }

        # Check if release already exists
        $prevEAP = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        gh release view $tagName > $null 2>&1
        $releaseExists = ($LASTEXITCODE -eq 0)
        $ErrorActionPreference = $prevEAP

        if (-not $releaseExists) {
            $releaseType = if ($PreRelease) { "pre-release" } else { "release" }
            Write-Host "[RELEASE] Creating GitHub $releaseType for $tagName" -ForegroundColor Cyan
            $ghReleaseArgs = @($tagName, '--title', $tagName, '--notes', $releaseNotes, '--target', $Branch)
            if ($PreRelease) { $ghReleaseArgs += '--prerelease' }
            # Attach zip inline during creation if available
            if ($zipPath -and (Test-Path $zipPath)) { $ghReleaseArgs += $zipPath }
            gh release create @ghReleaseArgs
            if ($?) {
                Write-Host "[SUCCESS] GitHub release created: $tagName" -ForegroundColor Green
                if ($zipPath -and (Test-Path $zipPath)) {
                    Write-Host "[SUCCESS] Asset attached during release creation" -ForegroundColor Green
                    $zipPath = $null  # mark as already uploaded
                }
            } else {
                Write-Host "[WARNING] Failed to create GitHub release via gh CLI" -ForegroundColor Yellow
            }
        } else {
            Write-Host "[INFO] GitHub release $tagName already exists; updating notes" -ForegroundColor Yellow
            $ghEditArgs = @($tagName, '--notes', $releaseNotes)
            if ($PreRelease) { $ghEditArgs += '--prerelease' }
            gh release edit @ghEditArgs
        }

        # Upload zip separately if it wasn't attached during creation (existing release case)
        if ($zipPath -and (Test-Path $zipPath)) {
            Write-Host "[UPLOAD] Uploading $(Split-Path $zipPath -Leaf) to release $tagName" -ForegroundColor Cyan
            gh release upload $tagName $zipPath --clobber
            if ($?) {
                Write-Host "[SUCCESS] Uploaded asset: $(Split-Path $zipPath -Leaf)" -ForegroundColor Green
            } else {
                Write-Host "[WARNING] Failed to upload asset $(Split-Path $zipPath -Leaf)" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "[INFO] 'gh' CLI not found; skipping GitHub release creation" -ForegroundColor Yellow
    }
}

Write-Host "`n[COMPLETE] Auto-push finished! Version: $newVersion" -ForegroundColor Cyan
