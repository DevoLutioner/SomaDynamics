$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$controllerSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\ThighController.cs') -Raw -Encoding utf8
$pluginSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ThighPhysicsController\ThighPhysicsControllerPlugin.cs') -Raw -Encoding utf8

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

# SetClothesState can replace or rewrite the body hierarchy. The callback must
# remove Soma's previous deformation and capture the settled hierarchy as the
# new rest pose before the next Spring/Chain solve.
$clothesMethod = [regex]::Match(
    $controllerSource,
    '(?s)internal\s+void\s+OnClothesStateChanged\(\)\s*\{(?<body>.*?)\n\s*internal\s+void\s+OnPushupBodyMappingStarted\(\)'
)
if (-not $clothesMethod.Success) {
    throw 'Could not locate ThighController.OnClothesStateChanged.'
}

$body = $clothesMethod.Groups['body'].Value
Assert-Contains $body 'PrepareForExternalShapeChange\s*\(\s*2\s*\)' 'Clothes-state changes must enter the external shape rebase path.'
Assert-Contains $body 'RequestNativeReapply\s*\(\s*3\s*\)' 'Clothes-state changes must delay native body reapply until after pose settling.'

$patch = [regex]::Match(
    $pluginSource,
    '(?s)\[HarmonyPatch\(typeof\(ChaControl\),\s*"SetClothesState"\)\].*?private\s+static\s+class\s+ClothesStateChangedPatch\s*\{(?<body>.*?)\n\s*\}\s*\n\s*\}',
    [Text.RegularExpressions.RegexOptions]::Singleline
)
if (-not $patch.Success) {
    throw 'Could not locate the ChaControl.SetClothesState Harmony patch.'
}
Assert-Contains $patch.Groups['body'].Value 'controller\.OnClothesStateChanged\s*\(\s*\)' 'SetClothesState must notify the matching Soma controller.'

Write-Host 'PASS Test-FleshClothesStateRegression'
