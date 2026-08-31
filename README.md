# Redux Mission Log

Redux Mission Log is a KSP 2 Redux mod that turns flights and flight plans into readable mission stories. Version 0.5 adds a mission planner alongside compact visual timelines for launches, docked expeditions, lander sorties, reunions, destinations, milestones, deviations, and outcomes in a native KSP 2-styled window.

Press **F7** in KSP 2 to open the Mission Log window.

## Released scope (0.5)

- Automatic mission creation and vessel-identity continuity in flight.
- A timeline-first story view for launch, SOI entry, orbit, landing, splashdown, docking, separation, reunion, completion, and other meaningful moments.
- Compact single-line event rows keep chronology front and center; hover or keyboard focus expands the mission-leg and vessel details for one event.
- Peak altitude, speed, and g-force as rolling timeline records with no separate telemetry dashboard.
- Combined stories source-label moments from each launch and sortie while retaining that leg's own flight clock.
- Editable mission title and notes plus manual completion.
- Docking independent craft creates a `Combined` parent containing both launch histories.
- Splitting or undocking resumes a known child or creates a `Sortie` child.
- Re-docking a sortie closes it as rejoined without adding needless wrapper missions.
- Manual **Combine**, **Adopt under**, **Unlink**, and binding-repair controls correct uncertain cases.
- A mission planner for naming an expedition, choosing the bodies and flight states to visit, defining ordered objectives, and describing every vessel that will launch or dock.
- Saved-craft selection and an explicit **Launch** button for each planned vessel. This invokes KSP 2's normal launch flow; Mission Log does not steer, stage, execute manoeuvres, or automate the flight.
- Automatic comparison of the observed mission tree with the active plan, including current-step progress, out-of-order or missing-step deviations, and explicit manual match, skip, deviation, and correction controls.
- Redux's shared `AppShell`, panel controls, stock scaling, input blocking, UI sounds, dragging, and resizing make the archive behave like a KSP 2 window.
- Archive browsing, planning, editing, completion, and mission-tree repair as focused secondary views rather than permanent panels.
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

The archive and planner are stored under Unity's KSP 2 persistent-data directory in `ReduxMissionLog/mission-log.json` and `ReduxMissionLog/mission-plans.json`. A malformed sidecar is preserved beside itself with a timestamped `.corrupt-...json` name before Mission Log starts that data set cleanly. Neither sidecar modifies a KSP save.

## In-game end-to-end test

[ReduxTestHarness](https://github.com/Michionlion/ReduxTestHarness) remains the generic enabling stack; Mission Log owns its semantic API and its Lua tests here. With a sibling harness checkout and a local launchpad fixture available:

```powershell
pwsh -NoProfile -File scripts/install.ps1
pwsh -NoProfile -File scripts/run-e2e.ps1
```

The runner launches KSP 2 three times against isolated archive and plan sidecars. The mission-tree suite asserts exact results for independent and nested docking, splits, lander reunions, identity changes, sibling merges, loss, idempotency, manual corrections, timeline resolution, and persistence. The fixture-free mission-planner suite covers a realistic two-launch docked expedition, ordered progress, observed-state matching, deviations, manual correction, persistence, and compact timeline expansion. The lifecycle regression loads a real save, stages a real vessel, verifies launch and rolling record moments, completes and reloads the archive, and captures the in-game story UI. The suites never reset the player's normal data.

The automatic KSP topology adapter uses Redux's public docking, split, undock, recovery, destruction, and vessel-lifecycle APIs. A physical docking/undocking smoke still requires a reproducible local save fixture; the broad deterministic resolver coverage does not pretend to be that adapter smoke. All tests remain in this repository, while ReduxTestHarness stays the generic enabling stack.

The deterministic baseline is 49 compiled resolver, migration, timeline, and planner scenarios with 2,088 assertions. Installed-mod validation is split into mission-tree, mission-planner, and real-launch lifecycle suites so configured coverage is never confused with a completed in-game pass. The fixture-free planner suite uses semantic expansion and synthetic saved-craft selections; actual pointer/focus behavior and the player-confirmed launch button remain optional local UI smokes requiring a reviewed fixture.

KSP save fixtures are intentionally not published because they can contain campaign and player data. To reproduce the test, save a controllable vessel named `Fly Safe-15` on Kerbin's launchpad in Flight, review the JSON for private data, and place it at `tests/fixtures/local/launchpad-fly-safe-15.json`. The runner also recognizes the same developer fixture under a sibling ReduxTestHarness checkout.

## UI review gallery

Generate a fixture-free gallery with a sibling ReduxTestHarness checkout:

```powershell
pwsh -NoProfile -File scripts/run-gallery.ps1
```

The run installs the mod, launches KSP 2, and writes ordered real in-game screenshots under `.test-results/review-gallery` together with the test report and an attached copy of the isolated review archive.

## Development

Read [AGENTS.md](AGENTS.md) before changing the mod. Tests live under `tests/`; no product-specific behavior belongs in ReduxTestHarness.

## License

MIT
