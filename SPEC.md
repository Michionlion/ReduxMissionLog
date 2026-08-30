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

## Player experience

### In flight

Recording stays quiet. A lightweight control opens the current record, permits title and note edits, and allows manual completion when automatic inference is not appropriate.

### Debrief

At mission end, the player sees a concise summary: who flew, where they went, what happened, the important numbers, and the final outcome.

### Archive

The archive lists past and active missions newest-first. Selecting one reveals its summary and timeline without presenting raw telemetry.

### Kerbal record

Crew membership is stored per mission so the archive can later provide career histories and personal records without changing Kerbal abilities.

## First implementation slice

Version 0.1 delivers:

- Automatic mission creation for the active flight vessel.
- Launch, situation, celestial-body, orbit, landing, and splash milestones.
- Vessel, campaign, crew, visited-body, and peak-stat capture.
- Persistent local JSON archive with safe malformed-data recovery.
- An F7 archive/debrief window with editable title and notes.
- Manual completion and automatic recovery detection when observable.
- A mod-owned semantic ReduxTestHarness API and an automated in-game lifecycle test.

The first slice does not promise full continuity through docking, undocking, vessel merging, destruction, or every recovery path. Those behaviors require deliberate boundary rules informed by real play and tests.

## Later extensions

- Docking, separation, merge, recovery, and loss lineage.
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

A normal mission should leave behind an accurate story with little or no player effort, and a long-running campaign should gain a history worth revisiting.
