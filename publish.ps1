#Requires -Version 5.1
<#
.SYNOPSIS
    Tag a release and push to GitHub — triggers the release.yml CI workflow
    which builds win-x64 + linux-x64, packs with vpk, and publishes a GitHub Release.

.PARAMETER Version
    Semver string, e.g. "1.2.0". Required.

.PARAMETER Message
    Tag annotation / release title suffix. Defaults to "Release v<Version>".

.PARAMETER DryRun
    Show what would happen without making any changes.

.EXAMPLE
    .\publish.ps1 -Version 1.0.0
    .\publish.ps1 -Version 1.1.0 -Message "Beta: new orbit view"
    .\publish.ps1 -Version 1.0.1 -DryRun
#>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Message = '',

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$proj    = Join-Path $root 'Cereal.App\Cereal.App.csproj'
$tag     = "v$Version"
$tagMsg  = if ($Message) { $Message } else { "Release $tag" }

function Say  { param($t, $c = 'Cyan')   Write-Host $t -ForegroundColor $c }
function Step { param($t)                Say "`n==> $t" 'Yellow' }
function Bail { param($t)                Write-Error $t; exit 1 }
function Run  {
    param([string]$cmd, [string[]]$args)
    Say "  > $cmd $($args -join ' ')" 'DarkGray'
    if ($DryRun) { return }
    & $cmd @args
    if ($LASTEXITCODE -ne 0) { Bail "'$cmd $($args -join ' ')' failed (exit $LASTEXITCODE)." }
}

if ($DryRun) { Say '[DRY RUN — no changes will be made]' 'Magenta' }

# ── 1. Prerequisites ───────────────────────────────────────────────────────────
Step 'Checking prerequisites'

if (-not (Get-Command git  -ErrorAction SilentlyContinue)) { Bail 'git not found in PATH.' }
if (-not (Get-Command gh   -ErrorAction SilentlyContinue)) { Bail 'GitHub CLI (gh) not found. Install from https://cli.github.com/' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Bail 'dotnet not found in PATH.' }

# ── 2. Working tree must be clean ─────────────────────────────────────────────
Step 'Checking working tree'

Push-Location $root
try {
    $status = git status --porcelain 2>&1
    if (-not $DryRun -and $status) {
        Say $status 'DarkGray'
        Bail 'Working tree has uncommitted changes. Commit or stash them first.'
    }
    Say '  Working tree is clean.' 'Green'

    # ── 3. Tag must not already exist ─────────────────────────────────────────
    $existingTag = git tag --list $tag 2>&1
    if ($existingTag -eq $tag) { Bail "Tag $tag already exists." }

    # ── 4. Update <Version> in csproj ────────────────────────────────────────
    Step "Bumping csproj to $Version"

    $csprojContent = Get-Content $proj -Raw
    $newContent = $csprojContent -replace '<Version>.*?</Version>', "<Version>$Version</Version>"

    if ($csprojContent -eq $newContent) {
        Say '  Version unchanged (already set or tag not found in csproj).' 'DarkGray'
    } else {
        if (-not $DryRun) {
            Set-Content $proj $newContent -NoNewline
        }
        Say "  Set <Version>$Version</Version>" 'Green'
    }

    # ── 5. Commit version bump (if csproj changed) ───────────────────────────
    $dirty = git status --porcelain 2>&1
    if ($dirty) {
        Step 'Committing version bump'
        Run git @('add', $proj)
        Run git @('commit', '-m', "chore: bump version to $Version")
    }

    # ── 6. Create annotated tag ───────────────────────────────────────────────
    Step "Tagging $tag"
    Run git @('tag', '-a', $tag, '-m', $tagMsg)

    # ── 7. Push commit + tag ──────────────────────────────────────────────────
    Step 'Pushing to GitHub'
    Run git @('push', 'origin', 'HEAD')
    Run git @('push', 'origin', $tag)

    # ── 8. Report ─────────────────────────────────────────────────────────────
    Say "`nTag $tag pushed." 'Green'
    if (-not $DryRun) {
        $repoUrl = (gh repo view --json url -q '.url' 2>&1)
        if ($repoUrl) {
            Say "  GitHub Actions: $repoUrl/actions" 'White'
            Say "  Release (once built): $repoUrl/releases/tag/$tag" 'White'
        }
    }
} finally {
    Pop-Location
}
