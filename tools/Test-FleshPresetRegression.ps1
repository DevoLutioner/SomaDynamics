param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Stable', 'Natural', 'Dance')]
    [string]$Preset
)

$ErrorActionPreference = 'Stop'
$CsvPath = [IO.Path]::GetFullPath($CsvPath)
if (-not (Test-Path -LiteralPath $CsvPath -PathType Leaf)) {
    throw "Metric summary not found: $CsvPath"
}

$expected = @{
    Stable = @{
        Thigh = @(0.75, 0.0, 0.75); Arm = @(0.60, 0.0, 0.75); Belly = @(0.55, 0.0, 0.75)
    }
    Natural = @{
        Thigh = @(0.90, 1.0, 1.0); Arm = @(0.70, 1.0, 1.0); Belly = @(0.70, 0.5, 1.0864)
    }
    Dance = @{
        Thigh = @(0.95, 1.0, 1.5); Arm = @(0.80, 1.0, 1.5); Belly = @(0.70, 1.0, 1.2)
    }
}
$peakLimit = @{ Thigh = 0.02; Arm = 0.02; Belly = 0.01 }
$rows = Import-Csv -LiteralPath $CsvPath
$assertions = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

foreach ($part in @('Thigh', 'Arm', 'Belly')) {
    $expectedMode = if ($Preset -eq 'Stable') { 'Spring' } else { 'Chain' }
    $partRows = @($rows | Where-Object { $_.Part -eq $part -and $_.Mode -eq $expectedMode })
    Assert-True ($partRows.Count -eq 1) "$Preset/$part expected one $expectedMode metric group, got $($partRows.Count)."
    $row = $partRows[0]
    $target = $expected[$Preset][$part]
    Assert-True ([int]$row.Windows -ge 3) "$Preset/$part has fewer than three metric windows."
    Assert-True ([int]$row.Resets -eq 0) "$Preset/$part recorded safety resets."
    Assert-True ([int]$row.Reanchors -eq 0) "$Preset/$part recorded reanchors."
    Assert-True ([double]$row.Peak -le $peakLimit[$part]) "$Preset/$part exceeded peak limit."
    Assert-True ([Math]::Abs([double]$row.Strength - $target[0]) -lt 0.001) "$Preset/$part strength mismatch."
    Assert-True ([Math]::Abs([double]$row.Softness - $target[1]) -lt 0.001) "$Preset/$part softness mismatch."
    Assert-True ([Math]::Abs([double]$row.Motion - $target[2]) -lt 0.001) "$Preset/$part motion mismatch."
    Assert-True ([double]$row.Dynamic -ge 0) "$Preset/$part dynamic RMS is invalid."
}

Write-Host "PASS FleshPresetRegression $Preset ($assertions assertions)"
