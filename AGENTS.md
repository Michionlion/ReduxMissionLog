# Redux Mission Log contributor guide

## Scope

- Keep product behavior aligned with `SPEC.md`; update the spec when product boundaries change.
- Keep ordinary work inside this repository. Change ReduxTestHarness only when a reusable, mod-neutral capability is genuinely missing.
- Preserve unrelated changes in neighboring repositories.

## Product constraints

- Mission Log is observational during flight. The sole gameplay-affecting action is an explicit player press of a planner vessel's **Launch** button, which must use KSP 2's normal saved-craft launch flow.
- The planner must not steer, stage, execute manoeuvres, simulate a flight, or otherwise alter vessel physics, progression, resources, contracts, or save ownership.
- Store the archive and mission plans as separate local sidecar data and recover safely from missing, malformed, or older data.
- Prefer concise, meaningful milestones over continuous telemetry.
- Keep the chronological mission story as the primary window surface. Render each event as a compact single-line row, with details revealed by hover, keyboard focus, or the equivalent semantic test control. Treat record values as timeline moments; keep archive browsing, editing, planning, and tree repair secondary.
- Automatically reconcile observed mission history with active plans and surface progress and deviations. Player edits, explicit skips or matches, correction, and manual completion remain available.
- Build player-facing windows with Redux's `UitkForKsp2` stack and shared KSP 2 controls; do not reintroduce an IMGUI render loop.
- The first implementation targets KSP 2 Redux 0.2.8.5, SpaceWarp2 2.x, and Unity 6000.4.1f1.

## Testing boundaries

- Keep Mission Log tests under `tests/` in this repository.
- Expose mod-specific semantic operations from Mission Log through ReduxTestHarness's extension registry.
- Do not add a hard runtime dependency on ReduxTestHarness; the test API must be optional when the harness is absent.
- Lua callbacks must turn CLR failures into catchable `ScriptRuntimeException` errors.
- End-to-end tests must exercise the installed mod in KSP 2 Redux, not a stand-in implementation.
- Keep mission-tree, mission-planner, and real-flight lifecycle coverage as separate in-game suites. Planner tests must use isolated plan sidecars just as archive tests use isolated mission-log sidecars.

## Verification

- Run `pwsh -NoProfile -File scripts/test.ps1` for static checks and compilation.
- Run `pwsh -NoProfile -File scripts/install.ps1` before live validation.
- Run `pwsh -NoProfile -File scripts/run-e2e.ps1` for the in-game suite when KSP 2 and ReduxTestHarness are available.
- Report which checks actually ran; never describe configured or mocked coverage as a completed in-game pass.
