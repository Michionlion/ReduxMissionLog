[CmdletBinding()]
param(
    [string] $GameRoot = $env:KSP2_ROOT,
    [string] $TestHarnessRoot,
    [int] $Timeout = 300,
    [switch] $KeepOpen,
    [switch] $SkipInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $TestHarnessRoot) {
    $TestHarnessRoot = Join-Path (Split-Path -Parent $repoRoot) 'ReduxTestHarness'
}
$TestHarnessRoot = [IO.Path]::GetFullPath($TestHarnessRoot)
$runner = Join-Path $TestHarnessRoot 'redux-test.ps1'
$gallery = Join-Path $repoRoot 'tests\e2e\mission-gallery.lua'
if (-not (Test-Path -LiteralPath $runner)) {
    throw "ReduxTestHarness runner was not found: $runner"
}
if (-not (Test-Path -LiteralPath $gallery)) {
    throw "The Mission Log gallery test was not found: $gallery"
}

if (-not $SkipInstall) {
    & (Join-Path $PSScriptRoot 'install.ps1') -GameRoot $GameRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$arguments = @(
    '-NoProfile',
    '-File', $runner,
    'run', $gallery,
    '-Launch',
    '-Timeout', $Timeout,
    '-Results', (Join-Path $repoRoot '.test-results\review-gallery')
)
if ($GameRoot) { $arguments += @('-GameRoot', $GameRoot) }
if ($KeepOpen) { $arguments += '-KeepOpen' }

& (Get-Command pwsh -ErrorAction Stop).Source @arguments
exit $LASTEXITCODE
