#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads the SDK from global.json into .dotnet locally.
.PARAMETER Version
    Optional SDK version override. Defaults to global.json sdk.version.
#>
param(
    [string]$Version,
    [string]$InstallDir = 'D:\CODE\important files\.dotnet'
)

$repoRoot = $PSScriptRoot
$globalJsonPath = Join-Path $repoRoot 'global.json'

if (-not (Test-Path $globalJsonPath)) {
    Write-Error "global.json not found at $globalJsonPath"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        $globalJson = Get-Content -Path $globalJsonPath -Raw | ConvertFrom-Json
        $Version = $globalJson.sdk.version
    }
    catch {
        Write-Error "Failed to parse global.json: $($_.Exception.Message)"
        exit 1
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Error "SDK version was not provided and could not be read from global.json"
    exit 1
}

$installDir = $InstallDir
$installScript = Join-Path $repoRoot '.dotnet-install.ps1'

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Write-Host "Downloading dotnet-install.ps1..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript -ErrorAction Stop
}
catch {
    Write-Error "Failed to download dotnet-install.ps1: $($_.Exception.Message)"
    exit 1
}

Write-Host "Installing .NET SDK $Version to $installDir..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File $installScript -Version $Version -InstallDir $installDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dotnetExe = Join-Path $installDir 'dotnet.exe'
if (-not (Test-Path $dotnetExe)) {
    Write-Error "Installation did not produce $dotnetExe"
    exit 1
}

Write-Host "Local SDK installed: $dotnetExe" -ForegroundColor Green
& $dotnetExe --version
