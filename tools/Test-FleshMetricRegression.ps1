param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [ValidateSet('Chain', 'Spring')]
    [string]$Mode = 'Chain',

    [ValidateSet(3, 5)]
    [int]$Points = 3
)

$ErrorActionPreference = 'Stop'
$CsvPath = [IO.Path]::GetFullPath($CsvPath)
if (-not (Test-Path -LiteralPath $CsvPath -PathType Leaf)) {
    throw "Metric summary not found: $CsvPath"
}

$rows = Import-Csv -LiteralPath $CsvPath
$expectedStrength = if ($Mode -eq 'Chain') {
    @{ Thigh = 0.9; Arm = 0.7; Belly = 0.7 }
}
else {
    @{ Thigh = 0.8; Arm = 0.7; Belly = 0.7 }
}
$peakLimit = @{ Thigh = 0.02; Arm = 0.02; Belly = 0.01 }
$expectedSoftness = if ($Points -eq 5) { @(0, 0.25, 0.5, 0.75, 1) } else { @(0, 0.5, 1) }
$assertions = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

foreach ($part in @('Thigh', 'Arm', 'Belly')) {
    $partRows = @($rows | Where-Object { $_.Part -eq $part -and $_.Mode -eq $Mode })
    Assert-True ($partRows.Count -eq $Points) "$part expected $Points $Mode softness points, got $($partRows.Count)."
    $ordered = @($partRows | Sort-Object { [double]$_.Softness })
    $softness = @($ordered | ForEach-Object { [double]$_.Softness })
    $softnessComplete = $true
    for ($i = 0; $i -lt $Points; $i++) {
        if ([Math]::Abs($softness[$i] - $expectedSoftness[$i]) -ge 0.001) { $softnessComplete = $false }
    }
    Assert-True $softnessComplete "$part softness points are incomplete."
    foreach ($row in $ordered) {
        Assert-True ([int]$row.Windows -ge 2) "$part softness $($row.Softness) has fewer than two metric windows."
        Assert-True ([int]$row.Resets -eq 0) "$part softness $($row.Softness) recorded safety resets."
        Assert-True ([int]$row.Reanchors -eq 0) "$part softness $($row.Softness) recorded reanchors."
        Assert-True ([double]$row.Peak -le $peakLimit[$part]) "$part softness $($row.Softness) exceeded peak limit."
        Assert-True ([Math]::Abs([double]$row.Strength - $expectedStrength[$part]) -lt 0.001) "$part baseline strength changed."
    }
    $dynamic = @($ordered | ForEach-Object { [double]$_.Dynamic })
    $bias = @($ordered | ForEach-Object { [double]$_.Bias })
    $dynamicMonotonic = $true
    for ($i = 1; $i -lt $Points; $i++) {
        if ($dynamic[$i - 1] -gt $dynamic[$i]) { $dynamicMonotonic = $false }
    }
    Assert-True $dynamicMonotonic "$part dynamic RMS is not monotonic with softness."
    if ($Mode -eq 'Chain') {
        $biasMonotonic = $true
        for ($i = 1; $i -lt $Points; $i++) {
            if ($bias[$i - 1] -gt $bias[$i]) { $biasMonotonic = $false }
        }
        Assert-True $biasMonotonic "$part static bias is not monotonic with softness."
    }
    else {
        # Spring metrics intentionally exclude its gravity equilibrium (sag) from
        # the applied deformation, so mean-vector bias is phase-dependent rather
        # than a softness measure. Dynamic RMS is the user-visible spring response.
        Assert-True ($dynamic[$Points - 1] -ge 0.000002) "$part soft spring response is below the measurable floor."
    }
    Assert-True ($dynamic[$Points - 1] -ge $dynamic[0] * 1.5) "$part softness range has insufficient dynamic separation."
}

Write-Host "PASS FleshMetricRegression $Mode ($assertions assertions)"
