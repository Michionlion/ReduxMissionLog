# Redux Mission Log contributor guide

## Scope

- Keep product behavior aligned with `SPEC.md`; update the spec when product boundaries change.
- Keep ordinary work inside this repository. Change ReduxTestHarness only when a reusable, mod-neutral capability is genuinely missing.
- Preserve unrelated changes in neighboring repositories.

## Product constraints

- Mission Log is observational. It must not alter vessel physics, progression, resources, contracts, or save ownership.
- Store its archive as local sidecar data and recover safely from missing, malformed, or older data.
- Prefer concise, meaningful milestones over continuous telemetry.
- Automatic capture is the default; player edits and manual completion remain available.
- The first implementation targets KSP 2 Redux 0.2.8.5, SpaceWarp2 2.x, and Unity 6000.4.1f1.

## Testing boundaries

- Keep Mission Log tests under `tests/` in this repository.
- Expose mod-specific semantic operations from Mission Log through ReduxTestHarness's extension registry.
- Do not add a hard runtime dependency on ReduxTestHarness; the test API must be optional when the harness is absent.
- Lua callbacks must turn CLR failures into catchable `ScriptRuntimeException` errors.
- End-to-end tests must exercise the installed mod in KSP 2 Redux, not a stand-in implementation.

## Verification

- Run `pwsh -NoProfile -File scripts/test.ps1` for static checks and compilation.
- Run `pwsh -NoProfile -File scripts/install.ps1` before live validation.
- Run `pwsh -NoProfile -File scripts/run-e2e.ps1` for the in-game suite when KSP 2 and ReduxTestHarness are available.
- Report which checks actually ran; never describe configured or mocked coverage as a completed in-game pass.
