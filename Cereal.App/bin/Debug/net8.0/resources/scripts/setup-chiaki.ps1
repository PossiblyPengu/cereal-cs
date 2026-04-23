#Requires -Version 5.1
param(
    [switch]$Force,
    [string]$InstallDir = ''
)

$ErrorActionPreference = 'Stop'

# GitHub requires TLS 1.2; Windows PowerShell defaults to TLS 1.0 on older systems
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'streetpea/chiaki-ng'

# InstallDir is passed by the app (userData/chiaki-ng). Fall back to a local path for manual use.
if (-not $InstallDir) {
    $InstallDir = [System.IO.Path]::GetFullPath("$PSScriptRoot\..\resources\chiaki-ng")
}
$installDir  = $InstallDir
$versionFile = Join-Path $installDir '.version'

# Already installed?
$alreadyInstalled = (Test-Path $versionFile) -or (Test-Path (Join-Path $installDir 'chiaki.exe')) -or (Test-Path (Join-Path $installDir 'chiaki-ng.exe'))
if (-not $Force -and $alreadyInstalled) {
    $v = if (Test-Path $versionFile) { Get-Content $versionFile -Raw } else { 'unknown' }
    Write-Output "chiaki-ng already installed ($($v.Trim()))"
    exit 0
}

Write-Output 'Fetching latest chiaki-ng release...'

$headers = @{ 'User-Agent' = 'cereal-launcher' }
$releaseUrl = "https://api.github.com/repos/$repo/releases/latest"

try {
    $response = Invoke-WebRequest -Uri $releaseUrl -Headers $headers -UseBasicParsing
    if ($response.StatusCode -eq 403 -or $response.StatusCode -eq 429) {
        Write-Host "ERROR: GitHub API rate limit exceeded. Try again in a few minutes."
        exit 1
    }
    $release = $response.Content | ConvertFrom-Json
} catch {
    $msg = if ($_.Exception.Response.StatusCode.value__ -eq 403 -or $_.Exception.Response.StatusCode.value__ -eq 429) {
        'GitHub API rate limit exceeded. Try again in a few minutes.'
    } else {
        "Failed to fetch release info: $_"
    }
    Write-Host "ERROR: $msg"
    exit 1
}

# Prefer the portable zip (contains chiaki-ng.exe directly)
$asset = $release.assets |
    Where-Object { $_.name -match 'win' -and $_.name -match 'x64' -and $_.name -match 'portable' -and $_.name -match '\.zip$' } |
    Select-Object -First 1

if (-not $asset) {
    # Fallback: any Windows x64 zip that isn't an installer wrapper
    $asset = $release.assets |
        Where-Object { $_.name -match 'win' -and $_.name -match 'x64' -and $_.name -match '\.zip$' -and $_.name -notmatch 'installer' } |
        Select-Object -First 1
}

if (-not $asset) {
    Write-Host 'ERROR: No suitable Windows x64 portable zip found in the latest chiaki-ng release.'
    exit 1
}

Write-Output "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."

$tmpZip = Join-Path $env:TEMP "chiaki-ng-setup-$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()).zip"
try {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tmpZip -Headers $headers
} catch {
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    Write-Host "ERROR: Download failed: $_"
    exit 1
}

Write-Output 'Extracting...'

if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

try {
    Expand-Archive -Path $tmpZip -DestinationPath $installDir -Force
} catch {
    Write-Host "ERROR: Failed to extract archive: $_"
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    exit 1
}
Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue

# Flatten one level if everything extracted into a single subdirectory
$entries = Get-ChildItem -Path $installDir
if ($entries.Count -eq 1 -and $entries[0].PSIsContainer) {
    $sub = $entries[0].FullName
    Get-ChildItem -Path $sub | Move-Item -Destination $installDir
    Remove-Item $sub -Recurse -Force
}

# Write version marker
Set-Content -Path $versionFile -Value $release.tag_name -Encoding UTF8

Write-Output "chiaki-ng $($release.tag_name) installed to $installDir"
exit 0
