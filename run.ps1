#Requires -Version 5.1
<#
.SYNOPSIS
    Run Cereal in Debug mode.
.PARAMETER Release
    Run the Release configuration instead of Debug.
#>
param(
    [switch]$Release
)

$config = if ($Release) { 'Release' } else { 'Debug' }
$proj   = Join-Path $PSScriptRoot 'Cereal.App\Cereal.App.csproj'

Write-Host "Starting Cereal ($config)..." -ForegroundColor Cyan
dotnet run --project $proj -c $config
