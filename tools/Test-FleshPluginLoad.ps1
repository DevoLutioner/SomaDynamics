param(
    [string]$GameRoot = 'H:\KKS\KKS',
    [Parameter(Mandatory = $true)][string]$PluginDll,
    [string]$OutputDirectory = '',
    [ValidateRange(5, 120)][int]$Timeout = 30
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$ProgressPreference = 'SilentlyContinue'
function Read-SharedLog {
    param([string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}
$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\issue15-load-smoke'
}
$GameRoot = [IO.Path]::GetFullPath($GameRoot)
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$runRoot = Join-Path $OutputDirectory ('run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

# Executables, all writable configuration and plugins are private copies. Only
# immutable game data and the Mono runtime are junctioned to the installation.
foreach ($name in @('CharaStudio.exe', 'UnityPlayer.dll', 'winhttp.dll', 'doorstop_config.ini')) {
    Copy-Item -LiteralPath (Join-Path $GameRoot $name) -Destination (Join-Path $runRoot $name)
}
foreach ($name in @('CharaStudio_Data', 'MonoBleedingEdge', 'abdata', 'DefaultData')) {
    $source = Join-Path $GameRoot $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing KKS runtime directory: $source" }
    New-Item -ItemType Junction -Path (Join-Path $runRoot $name) -Target $source | Out-Null
}
$bepRoot = Join-Path $runRoot 'BepInEx'
$pluginRoot = Join-Path $bepRoot 'plugins'
$somaRoot = Join-Path $pluginRoot 'ThighPhysicsController'
$configRoot = Join-Path $bepRoot 'config'
$patcherRoot = Join-Path $bepRoot 'patchers'
foreach ($directory in @($pluginRoot, $somaRoot, $configRoot, $patcherRoot, (Join-Path $runRoot 'UserData'))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $GameRoot 'BepInEx\core') -Destination (Join-Path $bepRoot 'core') -Recurse
Copy-Item -LiteralPath (Join-Path $GameRoot 'BepInEx\plugins\KKSAPI.dll') -Destination $pluginRoot
Copy-Item -LiteralPath (Join-Path $GameRoot 'BepInEx\plugins\KKS_BepisPlugins\KKS_ExtensibleSaveFormat.dll') -Destination $pluginRoot
Copy-Item -LiteralPath (Join-Path $GameRoot 'BepInEx\patchers\KKS_ExtensibleSaveFormat_Patcher.dll') -Destination $patcherRoot
Copy-Item -LiteralPath $PluginDll -Destination (Join-Path $somaRoot 'ThighPhysicsController.dll')
$setupPath = Join-Path $GameRoot 'UserData\setup.xml'
if (Test-Path -LiteralPath $setupPath) {
    Copy-Item -LiteralPath $setupPath -Destination (Join-Path $runRoot 'UserData\setup.xml')
}
@'
[Caching]
EnableAssemblyCache = false
[Logging]
UnityLogListening = true
[Logging.Console]
Enabled = false
[Logging.Disk]
Enabled = true
WriteUnityLog = true
AppendLog = false
LogLevels = All
InstantFlushing = true
'@ | Set-Content -LiteralPath (Join-Path $configRoot 'BepInEx.cfg') -Encoding utf8

$unityLog = Join-Path $runRoot 'unity.log'
$bepLog = Join-Path $bepRoot 'LogOutput.log'
$process = $null
$watch = [Diagnostics.Stopwatch]::StartNew()
$allText = ''
$verdict = 'timeout'
try {
    $process = Start-Process -FilePath (Join-Path $runRoot 'CharaStudio.exe') -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode', '-nographics', '-screen-width', '320', '-screen-height', '240', '-logFile', ('"' + $unityLog + '"'))
    while ($watch.Elapsed.TotalSeconds -lt $Timeout) {
        Start-Sleep -Milliseconds 250
        $allText = ''
        foreach ($logPath in @($bepLog, $unityLog)) {
            if (Test-Path -LiteralPath $logPath) { $allText += (Read-SharedLog $logPath) + "`n" }
        }
        if ($allText -match "The script 'ThighPhysicsController\.ThighPhysicsControllerPlugin' could not be instantiated") {
            $verdict = 'cannot-instantiate'
            break
        }
        if ($allText -match 'Soma Dynamics initialized' -and $allText -match 'Native breast and Studio pose-change patches installed') {
            $verdict = 'initialized'
            break
        }
        if ($process.HasExited) { $verdict = 'process-exited'; break }
    }
}
finally {
    # Only stop the exact process created by this run, never other game sessions.
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $watch.Stop()
}
$result = [ordered]@{
    verdict = $verdict
    plugin = $PluginDll
    pluginSha256 = (Get-FileHash -LiteralPath $PluginDll -Algorithm SHA256).Hash.ToLowerInvariant()
    gameRoot = $GameRoot
    runRoot = $runRoot
    processId = $(if ($null -ne $process) { $process.Id } else { $null })
    elapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
    unityLog = $unityLog
    bepinexLog = $bepLog
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 5 | Write-Output
foreach ($logPath in @($bepLog, $unityLog)) {
    if (Test-Path -LiteralPath $logPath) {
        Select-String -LiteralPath $logPath -Pattern 'Soma Dynamics|ThighPhysicsController|could not be instantiated|patches installed' | ForEach-Object { $_.Line }
    }
}
if ($verdict -ne 'initialized') { throw "Soma plugin load failed: $verdict. Logs: $runRoot" }
Write-Output 'PASS: Soma Awake initialized and Harmony registration completed in actual KKS Unity runtime.'
