param(
    [string]$SomaVersion = '1.0.3.6',
    [string]$StabilizerVersion = '1.2.1',
    [string]$StabilizerRoot = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$packageRoot = Join-Path $repoRoot 'packaging'
if ([string]::IsNullOrWhiteSpace($StabilizerRoot)) {
    $StabilizerRoot = Join-Path (Split-Path $repoRoot -Parent) 'MmdDynamicBoneStabilizer'
}
$StabilizerRoot = [IO.Path]::GetFullPath($StabilizerRoot)

$somaStage = Join-Path $packageRoot "SomaDynamics_$SomaVersion"
$stabilizerStage = Join-Path $StabilizerRoot "release\MmdDynamicBoneStabilizer-v$StabilizerVersion"
$bundleName = "SomaDynamics_$SomaVersion-with-MMD-Stabilizer-v$StabilizerVersion"
$bundleStage = Join-Path $packageRoot $bundleName
$zip = Join-Path $packageRoot "$bundleName.zip"

foreach ($required in @($somaStage, $stabilizerStage)) {
    if (-not (Test-Path -LiteralPath $required -PathType Container)) {
        throw "Required staged package not found: $required"
    }
}

if (Test-Path -LiteralPath $bundleStage) {
    if (-not $Force) {
        throw "Bundle staging directory already exists: $bundleStage (pass -Force to replace)"
    }
    Remove-Item -LiteralPath $bundleStage -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    if (-not $Force) {
        throw "Bundle archive already exists: $zip (pass -Force to replace)"
    }
    Remove-Item -LiteralPath $zip -Force
    Remove-Item -LiteralPath "$zip.sha256" -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $bundleStage -Force | Out-Null
Copy-Item -Path (Join-Path $somaStage '*') -Destination $bundleStage -Recurse -Force

$stabilizerPluginSource = Join-Path $stabilizerStage 'BepInEx\plugins\MmdDynamicBoneStabilizer'
$stabilizerPluginTarget = Join-Path $bundleStage 'BepInEx\plugins\MmdDynamicBoneStabilizer'
New-Item -ItemType Directory -Path $stabilizerPluginTarget -Force | Out-Null
Copy-Item -Path (Join-Path $stabilizerPluginSource '*') -Destination $stabilizerPluginTarget -Force
Copy-Item -LiteralPath (Join-Path $stabilizerStage 'README.zh-CN.md') `
    -Destination (Join-Path $bundleStage 'MMD-Stabilizer-README.zh-CN.md') -Force
Copy-Item -LiteralPath (Join-Path $stabilizerStage 'CHANGELOG.md') `
    -Destination (Join-Path $bundleStage 'MMD-Stabilizer-CHANGELOG.md') -Force

$somaDll = Join-Path $bundleStage 'BepInEx\plugins\ThighPhysicsController\ThighPhysicsController.dll'
$stabilizerDll = Join-Path $bundleStage 'BepInEx\plugins\MmdDynamicBoneStabilizer\MmdDynamicBoneStabilizer.dll'
foreach ($requiredDll in @($somaDll, $stabilizerDll)) {
    if (-not (Test-Path -LiteralPath $requiredDll -PathType Leaf)) {
        throw "Bundle DLL missing: $requiredDll"
    }
}

$manifest = @(
    'Soma Dynamics complete companion bundle'
    "Soma Dynamics: $SomaVersion"
    "MMD DynamicBone Stabilizer: $StabilizerVersion"
    "ThighPhysicsController.dll SHA256: $((Get-FileHash -LiteralPath $somaDll -Algorithm SHA256).Hash)"
    "MmdDynamicBoneStabilizer.dll SHA256: $((Get-FileHash -LiteralPath $stabilizerDll -Algorithm SHA256).Hash)"
    'Install only after closing Koikatu.exe and CharaStudio.exe.'
    'Read the bundled full Chinese user guide before migrating from BPC.'
)
Set-Content -LiteralPath (Join-Path $bundleStage 'PACKAGE-MANIFEST.txt') -Value $manifest -Encoding UTF8

Compress-Archive -Path (Join-Path $bundleStage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $([IO.Path]::GetFileName($zip))" -Encoding ASCII

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $dllEntries = @($archive.Entries | Where-Object { $_.FullName -match '\.dll$' })
    if ($dllEntries.Count -ne 2) {
        throw "Bundle must contain exactly two DLLs; found $($dllEntries.Count)."
    }
} finally {
    $archive.Dispose()
}

Write-Host "Bundle: $zip"
Write-Host "SHA256: $hash"
