# Redux Mission Log

## Vision

Redux Mission Log gives a KSP 2 campaign a memory. It quietly turns flights into readable mission stories, preserving the vessels, Kerbals, places, accomplishments, and outcomes that would otherwise disappear between play sessions.

## Goals

- Capture meaningful accomplishments automatically.
- Preserve continuity for vessels, crews, and campaigns.
- Provide a useful post-flight debrief and a browsable long-term archive.
- Stay observational: Mission Log records play but does not change physics, progression, or rewards.
- Remain dependable across scene changes, vessel switching, saves, reloads, and mod updates.

## Core model

A mission is a player-readable record containing:

- Identity: title, vessel, crew, campaign, start/end times, status, and notes.
- Journey: visited bodies and a concise chronological milestone timeline.
- Highlights: peak altitude, speed, and g-force plus notable flight states.
- Outcome: active, completed, recovered, or lost.

Missions begin automatically when a flight is first observed. Boundaries are inferred where reliable and remain player-correctable. The archive is local sidecar data and never owns or rewrites the KSP save.

Schema 2 stores a **mission forest**: independent missions are roots, and combined missions or sorties form trees beneath them. Each mission keeps historical vessel identifiers as aliases and a separate set of current vessel bindings. A KSP vessel identity change therefore continues the same mission instead of creating a duplicate. Switching the active vessel only selects its bound mission; it never merges, splits, completes, or creates history by itself.

### Topology rules

- **Dock independent missions:** create a new `Combined` parent, place both prior mission trees beneath it, close their directly tracked legs as merged, and bind the resulting vessel to the new parent.
- **Split or undock:** resume a known child when identity is clear; otherwise create a `Sortie` child for the detached vessel while the main craft continues its existing mission.
- **Re-dock within one tree:** close the returning sortie as rejoined and continue the common parent. Do not create another wrapper mission.
- **Dock a combined tree with an independent mission:** create one new `Combined` parent above both roots, preserving all earlier launches and sorties.
- **Uncertain identity:** preserve the known tree, record the unresolved relationship, and let the player correct it. A low-confidence guess must not silently rewrite mission history.

Tree relationships are structural; milestones remain on the mission leg that recorded them. Parent summaries roll up crew, destinations, and highlights without duplicating every child event into a single ambiguous timeline.

## Player experience

### In flight

Recording stays quiet. A lightweight control opens the current record, permits title and note edits, and allows manual completion when automatic inference is not appropriate. The current mission also offers four direct correction actions:

- **Combine with...** creates a combined parent for the current mission and another root.
- **Adopt under...** makes the selected mission a sub-mission or sortie of another mission.
- **Unlink** returns a mission to the forest root without deleting its history.
- **Track current vessel here** repairs an incorrect or missing current binding.

Every action previews its effect, is restricted to the current campaign, and rejects cycles.

### Debrief

At mission end, the player sees a concise summary: who flew, where they went, what happened, the important numbers, and the final outcome.

### Archive

The archive presents expandable mission trees. Selecting a combined mission, launch, or sortie reveals its summary, parent path, children, vessel history, and timeline without presenting raw telemetry.

### Kerbal record

Crew membership is stored per mission so the archive can later provide career histories and personal records without changing Kerbal abilities.

## Released foundation (0.1)

Version 0.1 delivers:

- Automatic mission creation for the active flight vessel.
- Launch, situation, celestial-body, orbit, landing, and splash milestones.
- Vessel, campaign, crew, visited-body, and peak-stat capture.
- Persistent local JSON archive with safe malformed-data recovery.
- An F7 archive/debrief window with editable title and notes.
- Manual completion and automatic recovery detection when observable.
- A mod-owned semantic ReduxTestHarness API and an automated in-game lifecycle test.

## Mission-tree release (0.2)

Version 0.2 delivers:

- Schema-1 migration into the schema-2 mission forest.
- Historical vessel aliases and explicit current bindings.
- Automatic docking, merging, split, undock, reunion, and vessel-identity resolution.
- Combined missions and lander-style sortie sub-missions.
- Tree browsing and the manual Combine, Adopt, Unlink, and binding-repair actions.
- Deterministic topology scenarios plus live KSP 2 docking and undocking coverage where reproducible fixtures are available.

The deterministic resolver suite and installed-mod semantic suite validate these rules. The existing real-launch test remains the end-to-end lifecycle regression. Physical live docking and undocking adapter smokes remain fixture-dependent and must be reported separately from resolver coverage.

## Native UI refresh (0.3)

Version 0.3 moves the archive to Redux's shared KSP 2 UI stack. The Mission Log uses the common app shell, panel skin, controls, scaling, input behavior, sounds, dragging, and resizing while keeping the information architecture and mission-tree actions simple.

## Later extensions

- Kerbal career pages, records, ribbons, and mission patches.
- Player-authored and automatic screenshots or postcards.
- Program-level statistics and campaign retrospectives.
- Science, EVA, colony, and interstellar milestones.
- Exportable mission cards and carefully bounded replay summaries.

## Non-goals

- Autopilot, maneuver planning, contracts, resources, life support, or economy.
- Gameplay bonuses, penalties, or competitive scoring.
- Continuous black-box telemetry recording.
- Cloud accounts, online services, or multiplayer synchronization in the initial product.
- Full physical replay in version 1.

## Design principles

- Automatic by default.
- Quiet during play.
- Human-readable and celebratory.
- Editable when inference is imperfect.
- Local, transparent, and recoverable.
- Reliable before elaborate.

## Success

A normal mission should leave behind an accurate story with little or no player effort. A docked expedition should preserve every launch, a lander should remain visibly part of its parent expedition, and switching vessels should reveal the right record without changing history.

## Test strategy

- Keep the existing real-launch lifecycle test as a regression.
- Exercise the installed resolver through Mission Log's optional TestHarness API with deterministic sequences: independent docking, nested docking, split, reunion, identity replacement, recovery, loss, and manual corrections.
- Assert the exact tree, status, aliases, current bindings, and persistence result after every transition.
- Reload schema-1 and schema-2 archives to verify migration and normalization, including missing parents, duplicate bindings, and cycle rejection.
- Add live docking and undocking smokes for KSP event integration; deterministic resolver scenarios remain the broad behavioral suite.
