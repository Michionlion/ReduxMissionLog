[CmdletBinding()]
param(
    [string] $GameRoot = $env:KSP2_ROOT,
    [string] $UnityRoot = $env:UNITY_6000_4_1F1_ROOT
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

$sources = @(
    (Join-Path $repoRoot 'src\ReduxMissionLog\MissionModels.cs'),
    (Join-Path $repoRoot 'src\ReduxMissionLog\MissionLineageResolver.cs'),
    (Join-Path $repoRoot 'src\ReduxMissionLog\MissionTimeline.cs'),
    (Join-Path $repoRoot 'src\ReduxMissionLog\MissionArchiveStore.cs'),
    (Join-Path $repoRoot 'tests\unit\MissionLineageScenarios.cs')
)
$plannerSources = @(
    (Join-Path $repoRoot 'src\ReduxMissionLog\MissionPlanModels.cs'),
    (Join-Path $repoRoot 'src\ReduxMissionLog\MissionPlanner.cs'),
    (Join-Path $repoRoot 'tests\unit\MissionPlannerScenarios.cs')
)
$references = @(
    (Join-Path $managed 'netstandard.dll'),
    (Join-Path $managed 'Newtonsoft.Json.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll')
)
foreach ($path in @($sources) + @($plannerSources) + @($references)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required lineage-test input is missing: $path"
    }
}

$outputRoot = Join-Path $repoRoot 'build\tests\lineage'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$executable = Join-Path $outputRoot 'ReduxMissionLog.LineageScenarios.exe'

$arguments = [Collections.Generic.List[string]]::new()
$arguments.Add('/nologo')
$arguments.Add('/target:exe')
$arguments.Add('/langversion:7.3')
$arguments.Add('/deterministic+')
$arguments.Add('/optimize+')
$arguments.Add('/debug:portable')
$arguments.Add('/out:' + $executable)
foreach ($reference in $references) { $arguments.Add('/reference:' + $reference) }
foreach ($source in $sources) { $arguments.Add($source) }

& $mono $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Lineage scenario compilation failed with exit code $LASTEXITCODE"
}

$previousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = $managed
    & $mono $executable
    if ($LASTEXITCODE -ne 0) {
        throw "Lineage scenarios failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:MONO_PATH = $previousMonoPath
}

$plannerExecutable = Join-Path $outputRoot 'ReduxMissionLog.PlannerScenarios.exe'
$arguments = [Collections.Generic.List[string]]::new()
$arguments.Add('/nologo')
$arguments.Add('/target:exe')
$arguments.Add('/langversion:7.3')
$arguments.Add('/deterministic+')
$arguments.Add('/optimize+')
$arguments.Add('/debug:portable')
$arguments.Add('/out:' + $plannerExecutable)
foreach ($reference in $references) { $arguments.Add('/reference:' + $reference) }
foreach ($source in $plannerSources) { $arguments.Add($source) }

& $mono $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Planner scenario compilation failed with exit code $LASTEXITCODE"
}

$previousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = $managed
    & $mono $plannerExecutable
    if ($LASTEXITCODE -ne 0) {
        throw "Planner scenarios failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:MONO_PATH = $previousMonoPath
}

Write-Host "Lineage scenarios passed: $executable"
Write-Host "Planner scenarios passed: $plannerExecutable"
