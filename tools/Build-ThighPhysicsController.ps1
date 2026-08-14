param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version = '1.0.3.6',

    [string]$GameRoot = '',

    [switch]$SkipArchive,

    [switch]$SkipTests,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'src\ThighPhysicsController\ThighPhysicsController.csproj'
$outputDll = Join-Path $repoRoot "src\ThighPhysicsController\bin\$Configuration\ThighPhysicsController.dll"

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = $env:KOIKATU_BUILD_GAME_ROOT
}
if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = 'Z:\Koikatu'
}
$GameRoot = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')

$staging = Join-Path $repoRoot "packaging\SomaDynamics_$Version"
$pluginDir = Join-Path $staging 'BepInEx\plugins\ThighPhysicsController'
$presetsDir = Join-Path $pluginDir 'Presets'
$sourcePresets = Join-Path $repoRoot 'src\ThighPhysicsController\Presets'
$pluginSource = Join-Path $repoRoot 'src\ThighPhysicsController\ThighPhysicsControllerPlugin.cs'
$nativeBridgeSource = Join-Path $repoRoot 'src\ThighPhysicsController\NativeDynamicBoneBridge.cs'
$readme = Join-Path $repoRoot 'README.md'
$changelog = Join-Path $repoRoot 'CHANGELOG.md'
$userGuide = Join-Path $repoRoot 'docs\USER_GUIDE.zh-CN.md'

Write-Host "Building Soma Dynamics $Version (game root: $GameRoot)"
$pluginText = Get-Content -LiteralPath $pluginSource -Raw
$nativeBridgeText = Get-Content -LiteralPath $nativeBridgeSource -Raw
if ($pluginText -notmatch '_harmony\.PatchAll\(Assembly\.GetExecutingAssembly\(\)\)') {
    throw 'Native BPC compatibility patches are declared but not registered with Harmony.'
}
if ($pluginText -notmatch 'blocked BustSoft\.ReCalc overwrite' -or
    $pluginText -notmatch 'blocked BustGravity\.ReCalc overwrite' -or
    $pluginText -notmatch 'FPC_PUSHUP_COMMIT') {
    throw 'PushUp compatibility must preserve Soma physics and commit the completed breast shape.'
}
if ($nativeBridgeText -notmatch '\btarget\.setPtn\s*\(0, true\)' -or
    $nativeBridgeText -notmatch 'ReSetupDynamicBoneBust\(0\)') {
    throw 'Native breast apply must use the BPC pattern commit and refresh the completed baseline.'
}
if ($nativeBridgeText -match '\btarget\.(SetWeight|ResetParticlesPosition)\s*\(') {
    throw 'Native live-apply path must not change DynamicBone weight or reset colliding particles.'
}
$applyStart = $nativeBridgeText.IndexOf('internal void Apply(')
$restoreStart = $nativeBridgeText.IndexOf('internal void RestoreAll()')
if ($applyStart -lt 0 -or $restoreStart -le $applyStart) {
    throw 'Could not isolate NativeDynamicBoneBridge.Apply for safety inspection.'
}
$nativeApplyText = $nativeBridgeText.Substring($applyStart, $restoreStart - $applyStart)
if ($nativeApplyText -match 'ResetPosition|ResetParticlesPosition|SetWeight') {
    throw 'Native live-apply path must not directly reset DynamicBone particles or weight.'
}
$bodySourceText = (Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\NativeBodyParams.cs') -Raw) +
    (Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\ThighController.cs') -Raw) +
    $pluginText
if ($bodySourceText -match '\bSpringMode\b|DrawNativeSpringEditor|BreastSpringPart|ButtSpringPart') {
    throw 'Removed breast/butt Spring implementation is still present in source.'
}
if ($pluginText -match 'DebugRegressionMotion|TryDebugLoadScene|UpdateRegressionMotion|FPC_REGRESSION_STAGE') {
    throw 'Removed sandbox/runtime motion driver is still present in the plugin.'
}
if (-not $SkipTests) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-FleshParameterModel.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Parameter model tests failed with exit code $LASTEXITCODE"
    }
}
# Always clean before building: MSBuild's incremental state has repeatedly
# skipped CoreCompile after source edits and shipped stale DLLs, so force a
# from-scratch rebuild for a deterministic release artifact.
$projectDir = Join-Path $repoRoot 'src\ThighPhysicsController'
Remove-Item -LiteralPath (Join-Path $projectDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $projectDir 'obj') -Recurse -Force -ErrorAction SilentlyContinue
& dotnet build $project -c $Configuration -p:KOIKATU_BUILD_GAME_ROOT=$GameRoot
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}
$builtVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($outputDll).FileVersion
if ($builtVersion -ne $Version) {
    throw "Built DLL file version '$builtVersion' does not match package version '$Version'."
}

if (Test-Path $staging) {
    if (-not $Force) {
        throw "Staging directory already exists: $staging (pass -Force to replace)"
    }
    Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Path $presetsDir -Force | Out-Null
Copy-Item -LiteralPath $outputDll -Destination (Join-Path $pluginDir 'ThighPhysicsController.dll')
$presetFiles = @(Get-ChildItem -Path $sourcePresets -Filter '*.xml' -File -ErrorAction SilentlyContinue)
if ($presetFiles.Count -gt 0) {
    $presetFiles | Copy-Item -Destination $presetsDir
}
Copy-Item -LiteralPath $readme -Destination (Join-Path $staging 'README.zh-CN.md')
Copy-Item -LiteralPath $changelog -Destination (Join-Path $staging 'CHANGELOG.md')
Copy-Item -LiteralPath $userGuide -Destination (Join-Path $staging 'USER-GUIDE.zh-CN.md')

# Smoke checks against the freshly built DLL. Metadata strings are UTF-8, user-facing
# string literals are UTF-16LE in the #US heap, so search both byte encodings.
function Test-DllMarker {
    param([byte[]]$Haystack, [string]$Marker)
    $ascii = [Text.Encoding]::ASCII.GetBytes($Marker)
    $utf16 = [Text.Encoding]::Unicode.GetBytes($Marker)
    for ($i = 0; $i -le $Haystack.Length - $ascii.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $ascii.Length; $j++) {
            if ($Haystack[$i + $j] -ne $ascii[$j]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    for ($i = 0; $i -le $Haystack.Length - $utf16.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $utf16.Length; $j++) {
            if ($Haystack[$i + $j] -ne $utf16[$j]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    return $false
}

$dllBytes = [IO.File]::ReadAllBytes((Join-Path $pluginDir 'ThighPhysicsController.dll'))
foreach ($marker in @($Version, 'Soma Dynamics', 'Global controls',
        'Native DynamicBone', 'Advanced', 'Low', 'Medium', 'High', 'MotionGain', 'EnsureXml',
        'RotCalc', 'Remember per-character settings', 'Auto fix spring drift', 'Breast', 'Butt',
        'FPC_NATIVE_GUARD', 'FPC_PRESET_APPLY', 'SOMA_POSE_REBASE', 'SOMA_SCENE_REBASE',
        'Timeline playback uses Spring fallback', 'SOMA_TIMELINE_SAFE')) {
    if (-not (Test-DllMarker $dllBytes $marker)) {
        throw "Built DLL is missing expected feature marker: $marker"
    }
}

Write-Host "Staged: $staging"

if (-not $SkipArchive) {
    $zip = Join-Path $repoRoot "packaging\SomaDynamics_$Version.zip"
    if (Test-Path $zip) {
        if (-not $Force) {
            throw "Archive already exists: $zip (pass -Force to replace)"
        }
        Remove-Item -LiteralPath $zip -Force
        Remove-Item -LiteralPath "$zip.sha256" -Force -ErrorAction SilentlyContinue
    }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$zip.sha256" -Value $hash -Encoding ASCII
    Write-Host "Archive: $zip"
    Write-Host "SHA256: $hash"
}
