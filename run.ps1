#Requires -Version 5.1
<#
.SYNOPSIS
    Publish self-contained win-x64 and launch Cereal.
.PARAMETER Release
    Publish the Release configuration instead of Debug.
.PARAMETER Wait
    Wait for Cereal.App to exit (useful for debugging).
.PARAMETER SkipPublish
    Skip the publish step and launch existing output.
.PARAMETER NoLaunch
    Publish only, do not launch the app.
.PARAMETER DevPlaceholders
    Seed placeholder games on startup using DevPlaceholderCount (default 80).
.PARAMETER DevPlaceholderCount
    Number of placeholder games to seed when DevPlaceholders is enabled.
.PARAMETER DevPlaceholdersForce
    Force-refresh placeholder games each run (sets CEREAL_DEV_PLACEHOLDERS_FORCE=1).
.PARAMETER ClearDevPlaceholders
    Remove all placeholder games on startup (sets CEREAL_DEV_PLACEHOLDERS_CLEAR=1).
#>
param(
    [switch]$Release,
    [switch]$Wait,
    [switch]$SkipPublish,
    [switch]$NoLaunch,
    [switch]$DevPlaceholders,
    [int]$DevPlaceholderCount = 80,
    [switch]$DevPlaceholdersForce,
    [switch]$ClearDevPlaceholders
)

$config = if ($Release) { 'Release' } else { 'Debug' }
$proj   = Join-Path $PSScriptRoot 'Cereal.App\Cereal.App.csproj'
$out    = Join-Path $PSScriptRoot 'out\win-x64'
$dotnetLocal = 'D:\CODE\important files\dotnet-sdk-9.0.306-win-x64\dotnet.exe'
$dotnet = if (Test-Path $dotnetLocal) { $dotnetLocal } else { 'dotnet' }

if (-not $SkipPublish) {
    Write-Host "Publishing self-contained ($config)..." -ForegroundColor Cyan
    & $dotnet publish $proj -c $config -r win-x64 --self-contained -o $out
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($NoLaunch) { exit 0 }

Write-Host "Launching..." -ForegroundColor Cyan
$exe = Join-Path $out 'Cereal.App.exe'
if (-not (Test-Path $exe)) {
    Write-Error "Could not find built executable at $exe"
    exit 1
}

$prevDevCount = $env:CEREAL_DEV_PLACEHOLDERS
$prevDevForce = $env:CEREAL_DEV_PLACEHOLDERS_FORCE
$prevDevClear = $env:CEREAL_DEV_PLACEHOLDERS_CLEAR
$restoreDevEnv = $DevPlaceholders -or $DevPlaceholdersForce -or $ClearDevPlaceholders

if ($DevPlaceholders) {
    if ($DevPlaceholderCount -le 0) {
        Write-Error "DevPlaceholderCount must be greater than 0 when -DevPlaceholders is used."
        exit 1
    }
    $env:CEREAL_DEV_PLACEHOLDERS = "$DevPlaceholderCount"
    Write-Host "Dev placeholders enabled: $DevPlaceholderCount" -ForegroundColor Yellow
}

if ($DevPlaceholdersForce) {
    $env:CEREAL_DEV_PLACEHOLDERS_FORCE = "1"
    Write-Host "Dev placeholders force-refresh enabled" -ForegroundColor Yellow
}

if ($ClearDevPlaceholders) {
    $env:CEREAL_DEV_PLACEHOLDERS_CLEAR = "1"
    Write-Host "Dev placeholders clear enabled" -ForegroundColor Yellow
}

if ($Wait) {
    & $exe
    $code = $LASTEXITCODE
    if ($restoreDevEnv) {
        if ($null -ne $prevDevCount) { $env:CEREAL_DEV_PLACEHOLDERS = $prevDevCount } else { Remove-Item Env:CEREAL_DEV_PLACEHOLDERS -ErrorAction Ignore }
        if ($null -ne $prevDevForce) { $env:CEREAL_DEV_PLACEHOLDERS_FORCE = $prevDevForce } else { Remove-Item Env:CEREAL_DEV_PLACEHOLDERS_FORCE -ErrorAction Ignore }
        if ($null -ne $prevDevClear) { $env:CEREAL_DEV_PLACEHOLDERS_CLEAR = $prevDevClear } else { Remove-Item Env:CEREAL_DEV_PLACEHOLDERS_CLEAR -ErrorAction Ignore }
    }
    exit $code
}

Start-Process -FilePath $exe -WorkingDirectory $out | Out-Null

if ($restoreDevEnv) {
    if ($null -ne $prevDevCount) { $env:CEREAL_DEV_PLACEHOLDERS = $prevDevCount } else { Remove-Item Env:CEREAL_DEV_PLACEHOLDERS -ErrorAction Ignore }
    if ($null -ne $prevDevForce) { $env:CEREAL_DEV_PLACEHOLDERS_FORCE = $prevDevForce } else { Remove-Item Env:CEREAL_DEV_PLACEHOLDERS_FORCE -ErrorAction Ignore }
    if ($null -ne $prevDevClear) { $env:CEREAL_DEV_PLACEHOLDERS_CLEAR = $prevDevClear } else { Remove-Item Env:CEREAL_DEV_PLACEHOLDERS_CLEAR -ErrorAction Ignore }
}
