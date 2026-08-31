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
Assert-True ($metadata.version -eq '0.5.0') 'Unexpected Redux Mission Log release version.'

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
Assert-True ($treeE2e -match 'mission_timeline') 'Mission-tree E2E suite does not verify the resolved timeline.'
Assert-True ($treeE2e -match 'rendered_timeline_count') 'Mission-tree E2E suite does not compare the UI with its timeline projection.'
$plannerE2ePath = Join-Path $repoRoot 'tests\e2e\mission-planner.lua'
Assert-True (Test-Path -LiteralPath $plannerE2ePath) 'Mission-planner E2E suite is missing.'
$plannerE2e = Get-Content -LiteralPath $plannerE2ePath -Raw
Assert-True ($plannerE2e -match 'plan_add_vessel') 'Mission-planner E2E suite does not define planned vessels.'
Assert-True ($plannerE2e -match 'plan_add_objective') 'Mission-planner E2E suite does not define ordered objectives.'
Assert-True ($plannerE2e -match 'scenario_dock') 'Mission-planner E2E suite does not cover planned docking.'
Assert-True ($plannerE2e -match 'plan_recompute') 'Mission-planner E2E suite does not reconcile observed progress.'
Assert-True ($plannerE2e -match 'plan_skip_objective') 'Mission-planner E2E suite does not cover manual resolution.'
Assert-True ($plannerE2e -match 'reload_archive') 'Mission-planner E2E suite does not verify sidecar reload.'
Assert-True ($plannerE2e -match 'set_timeline_event_expanded') 'Mission-planner E2E suite does not verify compact timeline expansion.'
$plannerUnitPath = Join-Path $repoRoot 'tests\unit\MissionPlannerScenarios.cs'
Assert-True (Test-Path -LiteralPath $plannerUnitPath) 'Deterministic mission-planner scenarios are missing.'
$plannerOwnedFiles = @(
    'MissionPlanLaunchService.cs',
    'MissionPlanModels.cs',
    'MissionPlanner.cs',
    'MissionPlannerCoordinator.cs',
    'MissionPlannerPanel.cs',
    'MissionPlanStore.cs',
    'MissionPlanTimelineAdapter.cs'
)
foreach ($ownedFile in $plannerOwnedFiles) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot "src\ReduxMissionLog\$ownedFile")) "Mission planner source is missing: $ownedFile"
}
$galleryE2ePath = Join-Path $repoRoot 'tests\e2e\mission-gallery.lua'
Assert-True (Test-Path -LiteralPath $galleryE2ePath) 'Mission review gallery suite is missing.'
$galleryE2e = Get-Content -LiteralPath $galleryE2ePath -Raw
Assert-True ($galleryE2e -match 'scenario_event') 'Mission gallery does not author realistic mission moments.'
Assert-True ($galleryE2e -match 'set_review_scroll') 'Mission gallery does not cover the timeline at multiple scroll positions.'
Assert-True ($galleryE2e -match 'set_archive_collapsed') 'Mission gallery does not cover archive tree collapse state.'
Assert-True ($galleryE2e -match 'scenario_review') 'Mission gallery does not cover lineage review UI.'
Assert-True ($galleryE2e -match 'Test\.capture\.screenshot') 'Mission gallery does not capture in-game review images.'
Assert-True ($e2e -match 'peak_g_force') 'Lifecycle E2E does not verify peak force as a timeline event.'
Assert-True ($e2e -match 'cadence_saved_record') 'Lifecycle E2E does not verify rolling-record persistence.'
Assert-True ($e2e -match 'UitkForKsp2\.Controls\.AppShell') 'E2E test does not verify the native Redux UI stack.'

Write-Host 'Checking KSP 2 UI integration...'
$windowSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\ReduxMissionLog\MissionLogWindow.cs') -Raw
Assert-True ($windowSource -match 'new AppShell') 'Mission Log does not use the Redux AppShell.'
Assert-True ($windowSource -match 'InvertedCornerBox') 'Mission Log does not use the shared KSP 2 panel control.'
Assert-True ($windowSource -match 'UseStockScale = true') 'Mission Log does not opt into stock UI scaling.'
Assert-True ($windowSource -match 'BlockGameInput = true') 'Mission Log does not block flight input beneath its window.'
Assert-True ($windowSource -match 'EnableUiSounds') 'Mission Log does not enable KSP 2 UI sounds.'
Assert-True ($windowSource -match 'MISSION STORY') 'Mission Log does not put the mission story first.'
Assert-True ($windowSource -match '_tracker\.GetTimeline') 'Mission Log does not render the shared timeline projection.'
Assert-True ($windowSource -match 'PointerEnterEvent') 'Mission Log does not expand compact event details on hover.'
Assert-True ($windowSource -match 'FocusInEvent') 'Mission Log does not expand compact event details for keyboard focus.'
Assert-True ($windowSource -match 'OpenPlanner') 'Mission Log does not expose its mission-planner workspace.'
Assert-True ($windowSource -match 'SetReviewScroll') 'Mission Log does not expose semantic gallery scrolling.'
Assert-True ($windowSource -match 'SetArchiveCollapsed') 'Mission Log does not expose semantic archive collapse for review.'
Assert-True ($windowSource -notmatch 'Tree peak|Crew in tree|Permanent stats') 'Legacy dashboard-first summary copy remains in the story view.'
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
