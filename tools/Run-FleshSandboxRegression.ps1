param(
    [string]$SandboxRoot = 'Z:\Koikatu\.sandbox_BepInEx_test_20260804_103142',

    [ValidateSet('Sweep', 'SpringSweep', 'FivePointSweep', 'SpringFivePointSweep', 'SpringSoft', 'Stable', 'Natural', 'Dance')]
    [string]$Scenario = 'Sweep',

    [ValidateRange(45, 120)]
    [int]$DurationSeconds = 90,

    [switch]$DumpSkeleton,

    [switch]$SkipMetricGate,

    [ValidateSet(-1, 30, 60, 120)]
    [int]$TargetFps = -1,

    [string]$CardPath = '',

    [string]$CardLabel = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$SandboxRoot = [IO.Path]::GetFullPath($SandboxRoot).TrimEnd('\')
$exe = Join-Path $SandboxRoot 'CharaStudio.exe'
$plugin = Join-Path $SandboxRoot 'BepInEx\plugins\ThighPhysicsController\ThighPhysicsController.dll'
$config = Join-Path $SandboxRoot 'BepInEx\config\codex.koikatumanager.thighphysicscontroller.cfg'
$log = Join-Path $SandboxRoot 'output_log.txt'
$builtPlugin = Join-Path $repoRoot 'src\ThighPhysicsController\bin\Release\ThighPhysicsController.dll'
$defaultCard = Join-Path $SandboxRoot 'UserData\chara\female\_CharacterExport_2026-08-04-12-19-35_00_Female.png'
$card = if ([string]::IsNullOrWhiteSpace($CardPath)) {
    $defaultCard
}
else {
    [IO.Path]::GetFullPath($CardPath)
}

foreach ($required in @($exe, $plugin, $config, $card)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Sandbox prerequisite missing: $required"
    }
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-FleshParameterModel.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Parameter model tests failed before sandbox run.' }
& dotnet build (Join-Path $repoRoot 'src\ThighPhysicsController\ThighPhysicsController.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed before sandbox run.' }

$existing = Get-CimInstance Win32_Process -Filter "Name='CharaStudio.exe'" | Where-Object {
    $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).Equals($exe, [StringComparison]::OrdinalIgnoreCase)
}
if ($existing) {
    throw "Sandbox CharaStudio is already running (PID $($existing.ProcessId -join ','))."
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('fpc-regression-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$backupPlugin = Join-Path $tempRoot 'ThighPhysicsController.dll'
$backupConfig = Join-Path $tempRoot 'plugin.cfg'
$backupLog = Join-Path $tempRoot 'output_log.txt'
Copy-Item -LiteralPath $plugin -Destination $backupPlugin
Copy-Item -LiteralPath $config -Destination $backupConfig
if (Test-Path -LiteralPath $log) {
    Copy-Item -LiteralPath $log -Destination $backupLog
    Remove-Item -LiteralPath $log -Force
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$fpsSuffix = if ($TargetFps -gt 0) { "_fps$TargetFps" } else { '' }
$safeCardLabel = if ([string]::IsNullOrWhiteSpace($CardLabel)) { '' } else {
    $candidate = ($CardLabel.Trim() -replace '[^A-Za-z0-9_-]', '_').Trim('_')
    if ($candidate.Length -gt 32) { $candidate.Substring(0, 32) } else { $candidate }
}
$cardSuffix = if ([string]::IsNullOrWhiteSpace($safeCardLabel)) { '' } else { "_$safeCardLabel" }
$artifactDir = Join-Path $repoRoot "artifacts\regression\${stamp}_$($Scenario.ToLowerInvariant())$fpsSuffix$cardSuffix"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
@{
    CardPath = $card
    CardLabel = $safeCardLabel
    Scenario = $Scenario
    TargetFps = $TargetFps
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactDir 'run.json') -Encoding UTF8
$process = $null
try {
    Copy-Item -LiteralPath $builtPlugin -Destination $plugin -Force
    $presetDir = Join-Path $SandboxRoot 'BepInEx\plugins\ThighPhysicsController\Presets'
    $isSweep = $Scenario.EndsWith('Sweep', [StringComparison]::Ordinal)
    $isFivePoint = $Scenario -eq 'FivePointSweep' -or $Scenario -eq 'SpringFivePointSweep'
    $sweep = if ($isSweep) { 'true' } else { 'false' }
    $isSpringSoft = $Scenario -eq 'SpringSoft'
    $feelPreset = if ($isSweep -or $isSpringSoft) { 'Default' } else { $Scenario }
    $solverMode = if ($Scenario.StartsWith('Spring', [StringComparison]::Ordinal)) { 'Spring' } else { 'Default' }
    $softnessSteps = if ($isFivePoint) { 5 } else { 3 }
    $stageSeconds = if ($isFivePoint) { 14 } else { 17 }
    $softnessOverride = if ($isSpringSoft) { 1 } else { -1 }
    $responseOverride = if ($isSpringSoft) { 1.5 } else { -1 }
    $dumpSkeletonValue = if ($DumpSkeleton) { 'true' } else { 'false' }
    $testConfig = @"
[Debug]
Auto load studio scene = $card
Run regression motion = true
Regression softness sweep = $sweep
Regression softness steps = $softnessSteps
Regression stage seconds = $stageSeconds
Regression target FPS = $TargetFps
Regression feel preset = $feelPreset
Regression solver mode = $solverMode
Regression strength = -1
Regression softness = $softnessOverride
Regression motion response = $responseOverride
Collect runtime metrics = true
Log flesh physics = false
Dump skeleton bones = $dumpSkeletonValue

[General]
Window key = F10
Auto apply on load = true
Force enable = true
Remember per-character settings = false
Auto fix spring drift = true

[Presets]
Preset directory = $presetDir
"@
    Set-Content -LiteralPath $config -Value $testConfig -Encoding ASCII

    $process = Start-Process -FilePath $exe -WorkingDirectory $SandboxRoot -WindowStyle Minimized -PassThru
    Write-Host "Sandbox regression started (scenario=$Scenario, PID $($process.Id), duration ${DurationSeconds}s)."
    $deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) {
        Start-Sleep -Seconds 1
        $process.Refresh()
    }
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
    if (-not (Test-Path -LiteralPath $log -PathType Leaf)) {
        throw 'Sandbox produced no output_log.txt.'
    }
    $artifactLog = Join-Path $artifactDir 'output_log.txt'
    Copy-Item -LiteralPath $log -Destination $artifactLog
    $summaryPath = Join-Path $artifactDir 'metrics.csv'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Summarize-FleshMetrics.ps1') -LogPath $artifactLog -CsvPath $summaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Metric summary failed.' }
    $performancePath = Join-Path $artifactDir 'performance.csv'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Summarize-FleshPerformance.ps1') -LogPath $artifactLog -CsvPath $performancePath
    if ($LASTEXITCODE -ne 0) { throw 'Performance summary failed.' }
    if (-not $SkipMetricGate) {
        if ($isSweep) {
            $expectedMode = if ($Scenario.StartsWith('Spring', [StringComparison]::Ordinal)) { 'Spring' } else { 'Chain' }
            & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-FleshMetricRegression.ps1') -CsvPath $summaryPath -Mode $expectedMode -Points $softnessSteps
        }
        elseif ($isSpringSoft) {
            & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-FleshSingleRegression.ps1') -CsvPath $summaryPath -Mode Spring -Softness 1 -Motion 1.5
        }
        else {
            & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-FleshPresetRegression.ps1') -CsvPath $summaryPath -Preset $Scenario
        }
        if ($LASTEXITCODE -ne 0) { throw 'Metric regression gate failed.' }
    }
    Write-Host "Regression artifacts: $artifactDir"
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Copy-Item -LiteralPath $backupPlugin -Destination $plugin -Force
    Copy-Item -LiteralPath $backupConfig -Destination $config -Force
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $backupLog) {
        Copy-Item -LiteralPath $backupLog -Destination $log
    }
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
