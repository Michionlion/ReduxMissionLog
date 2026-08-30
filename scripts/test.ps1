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

Write-Host 'Compiling the in-game mod...'
& (Join-Path $PSScriptRoot 'build.ps1') -GameRoot $GameRoot -UnityRoot $UnityRoot
Assert-True ($LASTEXITCODE -eq 0) "build.ps1 exited with $LASTEXITCODE"
$builtDll = Join-Path $repoRoot 'build\ReduxMissionLog\ReduxMissionLog.dll'
Assert-True (Test-Path -LiteralPath $builtDll) "Built DLL is missing: $builtDll"

Write-Host 'PASS - static checks and compilation succeeded.'
