param(
    [Parameter(Mandatory = $true)]
    [string[]]$MetricsPaths,

    [ValidateSet('Chain', 'Spring')]
    [string]$Mode = 'Chain',

    [ValidateRange(1.01, 3.0)]
    [double]$MaxDynamicRatio = 1.5
)

$ErrorActionPreference = 'Stop'
$assertions = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

Assert-True ($MetricsPaths.Count -ge 2) 'Body matrix needs at least two independent runs.'
$sets = foreach ($path in $MetricsPaths) {
    $fullPath = [IO.Path]::GetFullPath($path)
    Assert-True (Test-Path -LiteralPath $fullPath -PathType Leaf) "Metric file is missing: $fullPath"
    @{
        Name = Split-Path (Split-Path $fullPath -Parent) -Leaf
        Rows = @(Import-Csv -LiteralPath $fullPath | Where-Object { $_.Mode -eq $Mode })
    }
}

$peakLimit = @{ Thigh = 0.02; Arm = 0.02; Belly = 0.01 }
foreach ($part in @('Thigh', 'Arm', 'Belly')) {
    $dynamic = @()
    foreach ($set in $sets) {
        $rows = @($set.Rows | Where-Object { $_.Part -eq $part })
        Assert-True ($rows.Count -eq 1) "$($set.Name)/$part expected one $Mode metric group."
        $row = $rows[0]
        Assert-True ([int]$row.Windows -ge 2) "$($set.Name)/$part has insufficient metric windows."
        Assert-True ([int]$row.Resets -eq 0) "$($set.Name)/$part recorded safety resets."
        Assert-True ([int]$row.Reanchors -eq 0) "$($set.Name)/$part recorded reanchors."
        Assert-True ([double]$row.Peak -le $peakLimit[$part]) "$($set.Name)/$part exceeded its peak limit."
        Assert-True ([double]$row.Dynamic -gt 0) "$($set.Name)/$part has no measurable dynamic response."
        $dynamic += [double]$row.Dynamic
    }
    $minimum = ($dynamic | Measure-Object -Minimum).Minimum
    $maximum = ($dynamic | Measure-Object -Maximum).Maximum
    Assert-True ($maximum / $minimum -le $MaxDynamicRatio) "$part dynamic response varies by more than ${MaxDynamicRatio}x across body cards."
}

Write-Host "PASS FleshBodyMatrix $Mode ($assertions assertions, $($sets.Count) runs)"
