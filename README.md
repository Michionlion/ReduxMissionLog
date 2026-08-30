# Redux Mission Log

Redux Mission Log is an early KSP 2 Redux mod that quietly records flights as readable mission stories. Version 0.1 creates a record for the active vessel, captures important milestones and peak statistics, remembers crew and destinations, and presents the result in a simple in-game archive.

Press **F7** in KSP 2 to open the Mission Log window.

## Current scope

- Automatic per-vessel mission creation in flight.
- Launch, situation, body, orbit, landing, and splash milestones.
- Crew, visited bodies, maximum altitude, speed, and g-force.
- Editable mission title and notes plus manual completion.
- Local JSON persistence outside KSP save files.
- Automated in-game lifecycle coverage through ReduxTestHarness.

This is a first vertical slice, not a claim that every docking, split, merge, destruction, or recovery boundary is already solved. See [SPEC.md](SPEC.md) for the product direction and explicit boundaries.

## Build and install

Requirements:

- KSP 2 Redux 0.2.8.5 or newer with SpaceWarp2 2.x.
- Unity 6000.4.1f1 installed through Unity Hub.
- PowerShell 7.

```powershell
pwsh -NoProfile -File scripts/test.ps1
pwsh -NoProfile -File scripts/install.ps1
```

The archive is stored under Unity's KSP 2 persistent-data directory in `ReduxMissionLog/mission-log.json`. A malformed archive is preserved beside it with a timestamped `.corrupt-...json` name before Mission Log starts a clean archive.

## In-game end-to-end test

[ReduxTestHarness](https://github.com/Michionlion/ReduxTestHarness) remains the generic enabling stack; Mission Log owns its semantic API and its Lua tests here. With a sibling harness checkout and a local launchpad fixture available:

```powershell
pwsh -NoProfile -File scripts/install.ps1
pwsh -NoProfile -File scripts/run-e2e.ps1
```

The runner switches Mission Log to an isolated test archive, launches KSP 2, loads a real save fixture, stages a real vessel, verifies recorded launch data, completes and reloads the archive, and captures the actual in-game archive UI. It never resets the player's normal archive.

KSP save fixtures are intentionally not published because they can contain campaign and player data. To reproduce the test, save a controllable vessel named `Fly Safe-15` on Kerbin's launchpad in Flight, review the JSON for private data, and place it at `tests/fixtures/local/launchpad-fly-safe-15.json`. The runner also recognizes the same developer fixture under a sibling ReduxTestHarness checkout.

## Development

Read [AGENTS.md](AGENTS.md) before changing the mod. Tests live under `tests/`; no product-specific behavior belongs in ReduxTestHarness.

## License

MIT
