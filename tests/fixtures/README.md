# Local KSP save fixtures

The end-to-end suite needs `local/launchpad-fly-safe-15.json`: a KSP 2 save in Flight with a controllable vessel named `Fly Safe-15` sitting on Kerbin's launchpad in the `PreLaunch` situation.

Save fixtures are not published because they can contain campaign and player data. Create the fixture in KSP 2, review its JSON before copying it here, and treat it as an immutable local test input. `scripts/run-e2e.ps1` falls back to the same fixture under a sibling ReduxTestHarness checkout when this directory does not contain it.
