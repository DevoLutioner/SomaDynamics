param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,

    [string]$CsvPath = ''
)

$ErrorActionPreference = 'Stop'
$LogPath = [IO.Path]::GetFullPath($LogPath)
if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "Metric log not found: $LogPath"
}

$culture = [Globalization.CultureInfo]::InvariantCulture
$rows = New-Object System.Collections.Generic.List[object]

foreach ($line in Get-Content -LiteralPath $LogPath) {
    if ($line -notmatch 'FPC_METRIC ') { continue }
    $values = @{}
    foreach ($pair in [regex]::Matches($line, '(?<key>\w+)=(?<value>[^\s]+)')) {
        $values[$pair.Groups['key'].Value] = $pair.Groups['value'].Value
    }
    if (-not $values.ContainsKey('part') -or -not $values.ContainsKey('samples')) { continue }
    $rows.Add([PSCustomObject]@{
        Part = $values['part']
        Mode = $values['mode']
        Seconds = [double]::Parse($values['seconds'], $culture)
        Samples = [int]$values['samples']
        Mean = [double]::Parse($values['mean'], $culture)
        Rms = [double]::Parse($values['rms'], $culture)
        Bias = if ($values.ContainsKey('bias')) { [double]::Parse($values['bias'], $culture) } else { 0d }
        Dynamic = if ($values.ContainsKey('dynamic')) { [double]::Parse($values['dynamic'], $culture) } else { 0d }
        Peak = [double]::Parse($values['peak'], $culture)
        Resets = [int]$values['resets']
        Reanchors = [int]$values['reanchors']
        Strength = [double]::Parse($values['strength'], $culture)
        Softness = [double]::Parse($values['softness'], $culture)
        Motion = [double]::Parse($values['motion'], $culture)
    })
}

if ($rows.Count -eq 0) {
    throw "No FPC_METRIC rows found in $LogPath"
}

$summary = foreach ($group in $rows | Group-Object Part,Mode,Strength,Softness,Motion) {
    $samples = ($group.Group | Measure-Object Samples -Sum).Sum
    $weightedMean = ($group.Group | ForEach-Object { $_.Mean * $_.Samples } | Measure-Object -Sum).Sum / [Math]::Max(1, $samples)
    $weightedRmsSquared = ($group.Group | ForEach-Object { $_.Rms * $_.Rms * $_.Samples } | Measure-Object -Sum).Sum / [Math]::Max(1, $samples)
    $weightedBias = ($group.Group | ForEach-Object { $_.Bias * $_.Samples } | Measure-Object -Sum).Sum / [Math]::Max(1, $samples)
    $weightedDynamicSquared = ($group.Group | ForEach-Object { $_.Dynamic * $_.Dynamic * $_.Samples } | Measure-Object -Sum).Sum / [Math]::Max(1, $samples)
    [PSCustomObject]@{
        Part = $group.Group[0].Part
        Mode = $group.Group[0].Mode
        Windows = $group.Count
        Seconds = [Math]::Round(($group.Group | Measure-Object Seconds -Sum).Sum, 2)
        Samples = $samples
        Mean = [Math]::Round($weightedMean, 6)
        Rms = [Math]::Round([Math]::Sqrt($weightedRmsSquared), 6)
        Bias = [Math]::Round($weightedBias, 6)
        Dynamic = [Math]::Round([Math]::Sqrt($weightedDynamicSquared), 6)
        Peak = [Math]::Round(($group.Group | Measure-Object Peak -Maximum).Maximum, 6)
        Resets = ($group.Group | Measure-Object Resets -Sum).Sum
        Reanchors = ($group.Group | Measure-Object Reanchors -Sum).Sum
        Strength = $group.Group[0].Strength
        Softness = $group.Group[0].Softness
        Motion = $group.Group[0].Motion
    }
}

$summary = $summary | Sort-Object Part,Mode
if (-not [string]::IsNullOrWhiteSpace($CsvPath)) {
    $CsvPath = [IO.Path]::GetFullPath($CsvPath)
    $parent = Split-Path $CsvPath -Parent
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $summary | Export-Csv -LiteralPath $CsvPath -NoTypeInformation -Encoding UTF8
}
$summary
