param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,

    [Parameter(Mandatory = $true)]
    [string]$CsvPath
)

$ErrorActionPreference = 'Stop'
$LogPath = [IO.Path]::GetFullPath($LogPath)
$CsvPath = [IO.Path]::GetFullPath($CsvPath)
if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "Runtime log not found: $LogPath"
}

$rows = foreach ($line in Get-Content -LiteralPath $LogPath) {
    if ($line -notmatch 'FPC_PERF ') { continue }
    $values = @{}
    foreach ($pair in [regex]::Matches($line, '(?<key>\w+)=(?<value>[^\s]+)')) {
        $values[$pair.Groups['key'].Value] = $pair.Groups['value'].Value
    }
    if (-not $values.ContainsKey('part') -or -not $values.ContainsKey('samples')) { continue }
    [PSCustomObject]@{
        Part = $values['part']
        Mode = $values['mode']
        Seconds = [double]$values['seconds']
        Samples = [int]$values['samples']
        MeanUs = [double]$values['mean_us']
        MaxUs = [double]$values['max_us']
        MemorySource = if ($values.ContainsKey('memory_source')) { $values['memory_source'] } else { 'unsupported' }
        MemoryBytesPerFrame = if ($values.ContainsKey('memory_bpf')) { [double]$values['memory_bpf'] } else { -1d }
        MaxMemoryBytes = if ($values.ContainsKey('max_memory_bytes')) { [long]$values['max_memory_bytes'] } else { -1L }
    }
}
if (@($rows).Count -eq 0) {
    throw "No FPC_PERF rows found in $LogPath"
}

$summary = foreach ($group in $rows | Group-Object Part,Mode) {
    $samples = ($group.Group | Measure-Object Samples -Sum).Sum
    $seconds = ($group.Group | Measure-Object Seconds -Sum).Sum
    $weighted = ($group.Group | ForEach-Object { $_.MeanUs * $_.Samples } |
        Measure-Object -Sum).Sum / [Math]::Max(1, $samples)
    $allocationRows = @($group.Group | Where-Object { $_.MemoryBytesPerFrame -ge 0 })
    $allocationSamples = ($allocationRows | Measure-Object Samples -Sum).Sum
    $weightedAlloc = if ($allocationRows.Count -eq 0) { -1d } else {
        ($allocationRows | ForEach-Object { $_.MemoryBytesPerFrame * $_.Samples } |
            Measure-Object -Sum).Sum / [Math]::Max(1, $allocationSamples)
    }
    [PSCustomObject]@{
        Part = $group.Group[0].Part
        Mode = $group.Group[0].Mode
        Windows = $group.Count
        Seconds = [Math]::Round($seconds, 2)
        Samples = $samples
        AverageFps = [Math]::Round($samples / [Math]::Max(0.001, $seconds), 2)
        MeanUs = [Math]::Round($weighted, 3)
        MaxUs = [Math]::Round(($group.Group | Measure-Object MaxUs -Maximum).Maximum, 3)
        MemorySource = $group.Group[0].MemorySource
        MemoryBytesPerFrame = [Math]::Round($weightedAlloc, 3)
        MaxMemoryBytes = if ($allocationRows.Count -eq 0) { -1L } else {
            ($allocationRows | Measure-Object MaxMemoryBytes -Maximum).Maximum
        }
    }
}
$parent = Split-Path $CsvPath -Parent
if (-not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}
$summary = $summary | Sort-Object Part,Mode
$summary | Export-Csv -LiteralPath $CsvPath -NoTypeInformation -Encoding UTF8
$summary
