[CmdletBinding()]
param(
    [string] $GameRoot = $env:KSP2_ROOT,
    [string] $TestHarnessRoot,
    [string] $Fixtures,
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
if (-not $Fixtures) {
    $ownedFixtures = Join-Path $repoRoot 'tests\fixtures'
    $ownedFixture = Join-Path $ownedFixtures 'local\launchpad-fly-safe-15.json'
    $Fixtures = if (Test-Path -LiteralPath $ownedFixture) {
        $ownedFixtures
    }
    else {
        Join-Path $TestHarnessRoot 'fixtures'
    }
}
$Fixtures = [IO.Path]::GetFullPath($Fixtures)
$runner = Join-Path $TestHarnessRoot 'redux-test.ps1'
$fixture = Join-Path $Fixtures 'local\launchpad-fly-safe-15.json'
if (-not (Test-Path -LiteralPath $runner)) {
    throw "ReduxTestHarness runner was not found: $runner"
}
if (-not (Test-Path -LiteralPath $fixture)) {
    throw "The required local launchpad fixture was not found: $fixture"
}

if (-not $SkipInstall) {
    & (Join-Path $PSScriptRoot 'install.ps1') -GameRoot $GameRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$arguments = @(
    '-NoProfile',
    '-File', $runner,
    'run', (Join-Path $repoRoot 'tests\e2e\mission-lifecycle.lua'),
    '-Launch',
    '-Timeout', $Timeout,
    '-Fixtures', $Fixtures,
    '-Results', (Join-Path $repoRoot '.test-results\e2e')
)
if ($GameRoot) { $arguments += @('-GameRoot', $GameRoot) }
if ($KeepOpen) { $arguments += '-KeepOpen' }

& (Get-Command pwsh -ErrorAction Stop).Source @arguments
exit $LASTEXITCODE
