$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$timelineSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\TimelineConstraintBridge.cs') -Raw -Encoding utf8
$solverSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\FleshChainSolver.cs') -Raw -Encoding utf8
$jiggleSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\ThighFleshJiggle.cs') -Raw -Encoding utf8
$pluginSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\ThighPhysicsControllerPlugin.cs') -Raw -Encoding utf8
$metricsSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\ThighFleshJiggle.Metrics.cs') -Raw -Encoding utf8

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

# Timeline playback is a global flag, so reflection must be read once per frame,
# not once per Soma component. The idle path must also bypass per-character scans.
Assert-Contains $timelineSource '_timelinePlayingFrame' 'Timeline playback state is not frame-cached.'
Assert-Contains $timelineSource '(?s)_timelinePlayingFrame.*?GetValue\(null, null\)' 'Timeline reflection read is not behind the frame cache.'
Assert-Contains $timelineSource '(?s)if \(!IsTimelinePlaying\(\)\)\s*\{\s*return false;' 'Idle Timeline path still enters per-character work.'
Assert-Contains $jiggleSource 'SOMA_RUNTIME_STATUS' 'Runtime status probe is missing.'
Assert-Contains $pluginSource 'SOMA_RUNTIME_TICK' 'Runtime tick probe is missing.'
Assert-Contains $jiggleSource 'SOMA_RUNTIME_INIT' 'Runtime initialization probe is missing.'
Assert-Contains $metricsSource 'LogRuntime\("FPC_PERF' 'Performance metrics are not using the reliable runtime log channel.'
Assert-Contains $metricsSource 'LogRuntime\("FPC_METRIC' 'Motion metrics are not using the reliable runtime log channel.'

# These values are invariant across particles in one part/frame. Keep the expensive
# frame-rate math out of the particle loops so character count remains the dominant
# cost instead of repeated scalar setup.
Assert-Contains $solverSource 'ComputeVelocityRetention' 'Chain velocity retention is not precomputed.'
Assert-Contains $solverSource 'ComputeSegmentAxialStrength' 'Chain axial return strength is not precomputed.'
Assert-Contains $solverSource 'ComputeSegmentLateralStrength' 'Chain lateral return strength is not precomputed.'
Assert-Contains $jiggleSource 'float solverDt' 'Chain solver timestep cache is missing.'
Assert-Contains $jiggleSource 'segmentAxialStrength' 'Chain segment scalars are not passed into the particle loop.'

Write-Host 'PASS Test-FleshPerformanceRegression'
