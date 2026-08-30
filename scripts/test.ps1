[CmdletBinding()]
param(
    [string] $GameRoot = $env:KSP2_ROOT,
    [string] $UnityRoot = $env:UNITY_6000_4_1F1_ROOT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

Write-Host 'Checking PowerShell syntax...'
$powershellFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
    Where-Object Extension -In @('.ps1', '.psm1')
foreach ($file in $powershellFiles) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref]$tokens,
        [ref]$errors)
    Assert-True ($errors.Count -eq 0) "PowerShell parse errors in $($file.FullName): $errors"
}

Write-Host 'Checking JSON metadata...'
$metadata = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ReduxMissionLog\swinfo.json') -Raw |
    ConvertFrom-Json -AsHashtable
Assert-True ($metadata.mod_id -eq 'ReduxMissionLog') 'Unexpected SpaceWarp mod ID.'
Assert-True ($metadata.main_assembly -eq 'ReduxMissionLog.dll') 'Unexpected main assembly.'

Write-Host 'Checking owned specification and end-to-end contract...'
Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md')) 'AGENTS.md is missing.'
Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot 'SPEC.md')) 'SPEC.md is missing.'
$e2e = Get-Content -LiteralPath (Join-Path $repoRoot 'tests\e2e\mission-lifecycle.lua') -Raw
Assert-True ($e2e -match 'Test\.mod\.extension\("ReduxMissionLog"\)') 'E2E test does not use the mod-owned semantic API.'
Assert-True ($e2e -match 'Test\.flight\.stage\(\)') 'E2E test does not exercise a real launch.'
Assert-True ($e2e -match 'reload_archive') 'E2E test does not verify disk reload.'
$treeE2ePath = Join-Path $repoRoot 'tests\e2e\mission-tree-sequences.lua'
Assert-True (Test-Path -LiteralPath $treeE2ePath) 'Mission-tree E2E suite is missing.'
$treeE2e = Get-Content -LiteralPath $treeE2ePath -Raw
Assert-True ($treeE2e -match 'scenario_dock') 'Mission-tree E2E suite does not cover docking.'
Assert-True ($treeE2e -match 'scenario_split') 'Mission-tree E2E suite does not cover separation.'
Assert-True ($treeE2e -match 'validate_tree') 'Mission-tree E2E suite does not validate tree invariants.'
Assert-True ($e2e -match 'UitkForKsp2\.Controls\.AppShell') 'E2E test does not verify the native Redux UI stack.'

Write-Host 'Checking KSP 2 UI integration...'
$windowSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ReduxMissionLog\MissionLogWindow.cs') -Raw
Assert-True ($windowSource -match 'new AppShell') 'Mission Log does not use the Redux AppShell.'
Assert-True ($windowSource -match 'InvertedCornerBox') 'Mission Log does not use the shared KSP 2 panel control.'
Assert-True ($windowSource -match 'UseStockScale = true') 'Mission Log does not opt into stock UI scaling.'
Assert-True ($windowSource -match 'BlockGameInput = true') 'Mission Log does not block flight input beneath its window.'
Assert-True ($windowSource -match 'EnableUiSounds') 'Mission Log does not enable KSP 2 UI sounds.'
Assert-True ($windowSource -notmatch 'GUILayout|GUI\.Window') 'Legacy IMGUI window code is still present.'
$modSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ReduxMissionLog\ReduxMissionLogMod.cs') -Raw
Assert-True ($modSource -notmatch 'OnGUI') 'The mod still exposes an IMGUI render loop.'

$lineageTests = Join-Path $PSScriptRoot 'test-lineage.ps1'
if (Test-Path -LiteralPath $lineageTests) {
    Write-Host 'Running deterministic mission-lineage scenarios...'
    & $lineageTests -GameRoot $GameRoot -UnityRoot $UnityRoot
    Assert-True ($LASTEXITCODE -eq 0) "test-lineage.ps1 exited with $LASTEXITCODE"
}

Write-Host 'Compiling the in-game mod...'
& (Join-Path $PSScriptRoot 'build.ps1') -GameRoot $GameRoot -UnityRoot $UnityRoot
Assert-True ($LASTEXITCODE -eq 0) "build.ps1 exited with $LASTEXITCODE"
$builtDll = Join-Path $repoRoot 'build\ReduxMissionLog\ReduxMissionLog.dll'
Assert-True (Test-Path -LiteralPath $builtDll) "Built DLL is missing: $builtDll"

Write-Host 'PASS - static checks and compilation succeeded.'
