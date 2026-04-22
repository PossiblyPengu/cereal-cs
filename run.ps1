#Requires -Version 5.1
<#
.SYNOPSIS
    Publish self-contained win-x64 and launch Cereal.
.PARAMETER Release
    Publish the Release configuration instead of Debug.
#>
param(
    [switch]$Release
)

$config = if ($Release) { 'Release' } else { 'Debug' }
$proj   = Join-Path $PSScriptRoot 'Cereal.App\Cereal.App.csproj'
$out    = Join-Path $PSScriptRoot 'out\win-x64'
$dotnet = 'D:\CODE\important files\dotnet-sdk-9.0.306-win-x64\dotnet.exe'

Write-Host "Publishing self-contained ($config)..." -ForegroundColor Cyan
& $dotnet publish $proj -c $config -r win-x64 --self-contained -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Launching..." -ForegroundColor Cyan
& (Join-Path $out 'Cereal.App.exe')
