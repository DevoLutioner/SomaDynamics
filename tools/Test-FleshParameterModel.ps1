param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'tests\ParameterModel.Tests\ParameterModel.Tests.csproj'
$arguments = @('run', '--project', $project, '--configuration', 'Release')
if ($NoBuild) {
    $arguments += '--no-build'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Parameter model tests failed with exit code $LASTEXITCODE"
}
