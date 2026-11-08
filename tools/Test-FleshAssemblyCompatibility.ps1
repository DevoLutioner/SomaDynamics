#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('KK', 'KKS')]
    [string]$Game,

    [Parameter(Mandatory = $true)]
    [string]$PluginDll
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$Game = $Game.ToUpperInvariant()
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'tests\AssemblyCompatibility.Tests\AssemblyCompatibility.Tests.csproj'
$pluginPath = (Resolve-Path -LiteralPath $PluginDll -ErrorAction Stop).Path
& dotnet run --project $project --configuration Release -- $Game $pluginPath
if ($LASTEXITCODE -ne 0) {
    throw "Assembly compatibility check failed for $Game (exit code $LASTEXITCODE)."
}
