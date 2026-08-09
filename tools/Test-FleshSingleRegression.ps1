param(
    [Parameter(Mandatory = $true)] [string]$CsvPath,
    [ValidateSet('Chain', 'Spring')] [string]$Mode,
    [double]$Softness,
    [double]$Motion
)

$ErrorActionPreference = 'Stop'
$rows = Import-Csv -LiteralPath ([IO.Path]::GetFullPath($CsvPath))
$strength = if ($Mode -eq 'Spring') {
    @{ Thigh = 0.8; Arm = 0.7; Belly = 0.7 }
} else {
    @{ Thigh = 0.9; Arm = 0.7; Belly = 0.7 }
}
$peakLimit = @{ Thigh = 0.02; Arm = 0.02; Belly = 0.01 }
$assertions = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

foreach ($part in @('Thigh', 'Arm', 'Belly')) {
    $matches = @($rows | Where-Object { $_.Part -eq $part -and $_.Mode -eq $Mode })
    Assert-True ($matches.Count -eq 1) "$Mode/$part expected one metric group."
    $row = $matches[0]
    Assert-True ([int]$row.Windows -ge 3) "$Mode/$part has fewer than three windows."
    Assert-True ([int]$row.Resets -eq 0 -and [int]$row.Reanchors -eq 0) "$Mode/$part recovered or re-anchored."
    Assert-True ([double]$row.Peak -le $peakLimit[$part]) "$Mode/$part exceeded peak limit."
    Assert-True ([Math]::Abs([double]$row.Strength - $strength[$part]) -lt 0.001) "$Mode/$part strength mismatch."
    Assert-True ([Math]::Abs([double]$row.Softness - $Softness) -lt 0.001) "$Mode/$part softness mismatch."
    Assert-True ([Math]::Abs([double]$row.Motion - $Motion) -lt 0.001) "$Mode/$part motion mismatch."
}
Write-Host "PASS FleshSingleRegression $Mode ($assertions assertions)"
