# Redux Mission Log

Redux Mission Log is a KSP 2 Redux mod that quietly records flights as readable mission stories. Version 0.4 preserves launches, docked expeditions, lander sorties, reunions, crew, destinations, milestones, and outcomes as visual timelines in a native KSP 2-styled window.

Press **F7** in KSP 2 to open the Mission Log window.

## Released scope (0.4)

- Automatic mission creation and vessel-identity continuity in flight.
- A timeline-first story view for launch, SOI entry, orbit, landing, splashdown, docking, separation, reunion, completion, and other meaningful moments.
- Peak altitude, speed, and g-force as rolling timeline records with no separate telemetry dashboard.
- Combined stories source-label moments from each launch and sortie while retaining that leg's own flight clock.
- Editable mission title and notes plus manual completion.
- Docking independent craft creates a `Combined` parent containing both launch histories.
- Splitting or undocking resumes a known child or creates a `Sortie` child.
- Re-docking a sortie closes it as rejoined without adding needless wrapper missions.
- Manual **Combine**, **Adopt under**, **Unlink**, and binding-repair controls correct uncertain cases.
- Redux's shared `AppShell`, panel controls, stock scaling, input blocking, UI sounds, dragging, and resizing make the archive behave like a KSP 2 window.
- Archive browsing, editing, completion, and mission-tree repair as focused secondary views rather than permanent panels.
- Schema-1 archives migrate to the schema-2 mission forest.
- Local JSON persistence outside KSP save files.
- Automated resolver and real-flight coverage through ReduxTestHarness.

See [SPEC.md](SPEC.md) for the exact topology-resolution rules.

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

The runner launches KSP 2 twice against isolated archives. The mission-tree suite asserts exact results for independent and nested docking, splits, lander reunions, identity changes, sibling merges, loss, idempotency, manual corrections, timeline resolution, and persistence. The lifecycle regression loads a real save, stages a real vessel, verifies launch and rolling record moments, completes and reloads the archive, and captures the in-game story UI. It never resets the player's normal archive.

The automatic KSP topology adapter uses Redux's public docking, split, undock, recovery, destruction, and vessel-lifecycle APIs. A physical docking/undocking smoke still requires a reproducible local save fixture; the broad deterministic resolver coverage does not pretend to be that adapter smoke. All tests remain in this repository, while ReduxTestHarness stays the generic enabling stack.

The current validated baseline is 32 compiled resolver/migration/timeline scenarios with 1,782 assertions, 127 installed-mod mission-tree and timeline assertions in KSP, and 44 installed-mod real-launch/story assertions.

KSP save fixtures are intentionally not published because they can contain campaign and player data. To reproduce the test, save a controllable vessel named `Fly Safe-15` on Kerbin's launchpad in Flight, review the JSON for private data, and place it at `tests/fixtures/local/launchpad-fly-safe-15.json`. The runner also recognizes the same developer fixture under a sibling ReduxTestHarness checkout.

## Development

Read [AGENTS.md](AGENTS.md) before changing the mod. Tests live under `tests/`; no product-specific behavior belongs in ReduxTestHarness.

## License

MIT
