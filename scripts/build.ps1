[CmdletBinding()]
param(
    [string] $GameRoot = $env:KSP2_ROOT,
    [string] $UnityRoot = $env:UNITY_6000_4_1F1_ROOT,
    [string] $Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $GameRoot) {
    $GameRoot = @(
        'G:\SteamLibrary\steamapps\common\Kerbal Space Program 2',
        'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program 2'
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'KSP2_x64.exe') } |
        Select-Object -First 1
}
if (-not $GameRoot) {
    throw 'KSP2 was not found. Pass -GameRoot or set KSP2_ROOT.'
}
$GameRoot = [IO.Path]::GetFullPath($GameRoot)
$managed = Join-Path $GameRoot 'KSP2_x64_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "KSP2 managed assemblies were not found under $managed"
}

if (-not $UnityRoot) {
    $UnityRoot = 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1'
}
$UnityRoot = [IO.Path]::GetFullPath($UnityRoot)
$compiler = Join-Path $UnityRoot 'Editor\Data\MonoBleedingEdge\lib\mono\4.5\csc.exe'
$mono = Join-Path $UnityRoot 'Editor\Data\MonoBleedingEdge\bin\mono.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Unity C# compiler was not found: $compiler"
}
if (-not (Test-Path -LiteralPath $mono)) {
    throw "Unity Mono runtime was not found: $mono"
}

$sourceRoot = Join-Path $repoRoot 'src\ReduxMissionLog'
$outputRoot = Join-Path $repoRoot 'build\ReduxMissionLog'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$references = @(
    'Assembly-CSharp.dll',
    'MoonSharp.Interpreter.dll',
    'netstandard.dll',
    'Newtonsoft.Json.dll',
    'ReduxLib.dll',
    'SpaceWarp2.dll',
    'UitkForKsp2.dll',
    'uitkforksp2.controls.Runtime.dll',
    'Unity.Addressables.dll',
    'Unity.ResourceManager.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.InputLegacyModule.dll',
    'UnityEngine.PropertiesModule.dll',
    'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.UIElementsModule.dll',
    'UnityEngine.UIModule.dll'
) | ForEach-Object { Join-Path $managed $_ }

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Required reference is missing: $reference"
    }
}

$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object FullName)
if ($sources.Count -eq 0) {
    throw "No C# sources found under $sourceRoot"
}

$arguments = [Collections.Generic.List[string]]::new()
$arguments.Add('/nologo')
$arguments.Add('/target:library')
$arguments.Add('/langversion:7.3')
$arguments.Add('/deterministic+')
$arguments.Add('/optimize' + $(if ($Configuration -eq 'Release') { '+' } else { '-' }))
$arguments.Add('/debug:portable')
$arguments.Add('/define:KSP2_X64')
$arguments.Add('/out:' + (Join-Path $outputRoot 'ReduxMissionLog.dll'))
foreach ($reference in $references) { $arguments.Add('/reference:' + $reference) }
foreach ($source in $sources) { $arguments.Add($source) }

& $mono $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $sourceRoot 'swinfo.json') `
    -Destination (Join-Path $outputRoot 'swinfo.json') -Force
Write-Host "Built: $outputRoot"
