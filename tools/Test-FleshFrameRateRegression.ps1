param(
    [Parameter(Mandatory = $true)] [string]$Metrics30,
    [Parameter(Mandatory = $true)] [string]$Performance30,
    [Parameter(Mandatory = $true)] [string]$Metrics60,
    [Parameter(Mandatory = $true)] [string]$Performance60,
    [Parameter(Mandatory = $true)] [string]$MetricsHigh,
    [Parameter(Mandatory = $true)] [string]$PerformanceHigh,
    [ValidateSet('Chain', 'Spring')] [string]$Mode = 'Chain'
)

$ErrorActionPreference = 'Stop'
$assertions = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

$sets = @(
    @{ Name = '30'; Metrics = Import-Csv $Metrics30; Performance = Import-Csv $Performance30 },
    @{ Name = '60'; Metrics = Import-Csv $Metrics60; Performance = Import-Csv $Performance60 },
    @{ Name = 'High'; Metrics = Import-Csv $MetricsHigh; Performance = Import-Csv $PerformanceHigh }
)
$peakLimit = @{ Thigh = 0.02; Arm = 0.02; Belly = 0.01 }

foreach ($set in $sets) {
    $fpsRows = @($set.Performance | Where-Object { $_.Mode -eq $Mode })
    Assert-True ($fpsRows.Count -eq 3) "$($set.Name) FPS performance rows are incomplete."
    $fps = [double]$fpsRows[0].AverageFps
    if ($set.Name -eq '30') { Assert-True ($fps -ge 29 -and $fps -le 31) "30 FPS run measured $fps FPS." }
    elseif ($set.Name -eq '60') { Assert-True ($fps -ge 55 -and $fps -le 65) "60 FPS run measured $fps FPS." }
    else { Assert-True ($fps -ge 100) "High FPS run measured only $fps FPS." }
}

foreach ($part in @('Thigh', 'Arm', 'Belly')) {
    $rows = @()
    foreach ($set in $sets) {
        $matches = @($set.Metrics | Where-Object { $_.Part -eq $part -and $_.Mode -eq $Mode })
        Assert-True ($matches.Count -eq 1) "$($set.Name) FPS/$part metric group is missing."
        $row = $matches[0]
        Assert-True ([int]$row.Resets -eq 0 -and [int]$row.Reanchors -eq 0) "$($set.Name) FPS/$part recovered or re-anchored."
        Assert-True ([double]$row.Peak -le $peakLimit[$part]) "$($set.Name) FPS/$part exceeded peak limit."
        $rows += $row
    }
    $dynamic = @($rows | ForEach-Object { [double]$_.Dynamic })
    $dynamicMin = ($dynamic | Measure-Object -Minimum).Minimum
    $dynamicMax = ($dynamic | Measure-Object -Maximum).Maximum
    Assert-True ($dynamicMin -gt 0 -and $dynamicMax / $dynamicMin -le 1.25) "$part dynamic response varies by more than 25% across frame rates."
    if ($Mode -eq 'Chain' -and $part -ne 'Thigh') {
        $bias = @($rows | ForEach-Object { [double]$_.Bias })
        $biasMin = ($bias | Measure-Object -Minimum).Minimum
        $biasMax = ($bias | Measure-Object -Maximum).Maximum
        Assert-True ($biasMin -gt 0 -and $biasMax / $biasMin -le 1.15) "$part static bias varies by more than 15% across frame rates."
    }
}

Write-Host "PASS FleshFrameRateRegression ($assertions assertions)"
