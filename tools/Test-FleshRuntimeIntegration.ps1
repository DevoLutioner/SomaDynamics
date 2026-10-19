param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'tests\RuntimeIntegration.Tests\RuntimeIntegration.Tests.csproj'
& dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Runtime integration tests failed with exit code $LASTEXITCODE"
}
