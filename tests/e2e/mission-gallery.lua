Test.name("Redux Mission Log visual review gallery")
Test.report.fail_on_log_errors()

Test.assert.true_(Test.mod.is_loaded("ReduxMissionLog"), "ReduxMissionLog should be active")
local mission_log = Test.mod.extension("ReduxMissionLog")
Test.assert.not_equal(mission_log, nil, "ReduxMissionLog should expose its semantic test API")

local expected_ui_stack = "UitkForKsp2.Controls.AppShell"
local capture_count = 0

local function mission(id)
    local value = mission_log.mission(id)
    Test.assert.not_equal(value, nil, "mission should exist: " .. id)
    return value
end

local function assert_valid(label, roots, nodes)
    local snapshot = mission_log.tree_snapshot()
    local errors = mission_log.validate_tree()
    Test.assert.equal(snapshot.rootCount, roots, label .. " root count")
    Test.assert.equal(snapshot.nodeCount, nodes, label .. " node count")
    Test.assert.equal(#errors, 0, label .. " should satisfy every tree invariant")
end

local function capture(name)
    Test.capture.screenshot(name, {
        scale = 1,
        hideUI = false,
        waitFrames = 2
    })
    capture_count = capture_count + 1
end

local function assert_window_state(state, view, sheet, label)
    Test.assert.true_(state.visible, label .. " should keep the Mission Log visible")
    Test.assert.equal(state.view, view, label .. " view")
    Test.assert.equal(state.sheet, sheet, label .. " sheet")
    Test.assert.equal(mission_log.ui_stack(), expected_ui_stack, label .. " UI stack")
end

local function open_story(id, anchor, minimum_timeline_count, label)
    local projected = mission_log.mission_timeline(id)
    Test.assert.greater(
        #projected,
        minimum_timeline_count - 1,
        label .. " should have enough meaningful timeline moments")

    mission_log.open_mission(id)
    Test.wait.frames(5)
    mission_log.set_review_scroll("timeline", anchor)
    Test.wait.frames(3)

    local state = mission_log.review_ui_state()
    assert_window_state(state, "story", "none", label)
    Test.assert.equal(state.selectedMissionId, id, label .. " selected mission")
    Test.assert.equal(mission_log.selected_mission_id(), id, label .. " selected mission API")
    Test.assert.equal(
        state.renderedTimelineCount,
        #projected,
        label .. " should render the complete projected timeline")
    Test.assert.equal(
        mission_log.rendered_timeline_count(),
        #projected,
        label .. " rendered timeline API")
    if state.scrollMaximum > 0 then
        Test.assert.equal(state.scrollAnchor, anchor, label .. " scroll anchor")
    end
end

local function open_archive(anchor, label)
    mission_log.open_archive()
    Test.wait.frames(5)
    mission_log.set_review_scroll("archive", anchor)
    Test.wait.frames(3)

    local state = mission_log.review_ui_state()
    assert_window_state(state, "archive", "none", label)
    Test.assert.greater(state.archiveRenderedNodeCount, 0, label .. " should render archive nodes")
    if state.scrollMaximum > 0 then
        Test.assert.equal(state.scrollAnchor, anchor, label .. " scroll anchor")
    end
    return state
end

mission_log.begin_test_session()

-- Gallery 1: a clean, linear mission story with enough moments and records to scan.
mission_log.scenario_launch("mun-pathfinder", "Mun Pathfinder I", "v-pathfinder")
mission_log.scenario_crew("mun-pathfinder", "Valentina Kerman")
mission_log.scenario_note(
    "mun-pathfinder",
    "First crewed precision landing. Preserve the ascent stage for the return burn.")
mission_log.scenario_event(
    "mun-pathfinder", "launch", "Liftoff from Launchpad One", 5, "Kerbin", "Flying")
mission_log.scenario_event(
    "mun-pathfinder", "peak_g_force", "Peak ascent force — 4.20 g", 35, "Kerbin", "Flying")
mission_log.scenario_event(
    "mun-pathfinder", "orbit", "Parking orbit established", 480, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "mun-pathfinder", "peak_altitude", "Highest altitude — 120.0 km", 600, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "mun-pathfinder", "body_changed", "Entered the Mun's influence", 7200, "Mun", "Flying")
mission_log.scenario_event(
    "mun-pathfinder", "orbit", "Circularized over the East Crater", 8400, "Mun", "Orbiting")
mission_log.scenario_event(
    "mun-pathfinder", "peak_speed", "Top speed — 2,300 m/s", 8700, "Mun", "Flying")
mission_log.scenario_event(
    "mun-pathfinder", "landed", "Touchdown at East Crater", 9300, "Mun", "Landed")
mission_log.scenario_records("mun-pathfinder", 120000, 2300, 4.2)
mission_log.scenario_status("v-pathfinder", "Completed")

local pathfinder = mission("mun-pathfinder")
Test.assert.equal(pathfinder.status, "Completed", "Pathfinder should be a completed linear story")
Test.assert.equal(pathfinder.body, "Mun", "Pathfinder should finish on the Mun")
Test.assert.equal(pathfinder.crew[1], "Valentina Kerman", "Pathfinder should show its pilot")

-- Gallery 2: three launches form a nested station assembly tree.
mission_log.scenario_launch("gateway-core", "Gateway Core Launch", "v-gateway-core")
mission_log.scenario_crew("gateway-core", "Jebediah Kerman", "Bob Kerman")
mission_log.scenario_event(
    "gateway-core", "launch", "Core module launched", 6, "Kerbin", "Flying")
mission_log.scenario_event(
    "gateway-core", "orbit", "Core checkout orbit", 510, "Kerbin", "Orbiting")

mission_log.scenario_launch("gateway-hab", "Gateway Habitat Launch", "v-gateway-hab")
mission_log.scenario_crew("gateway-hab", "Valentina Kerman")
mission_log.scenario_event(
    "gateway-hab", "launch", "Habitat module launched", 7, "Kerbin", "Flying")
mission_log.scenario_event(
    "gateway-hab", "orbit", "Habitat rendezvous orbit", 550, "Kerbin", "Orbiting")

local gateway_phase = mission_log.scenario_dock(
    "v-gateway-core",
    "v-gateway-hab",
    "v-gateway-phase-one",
    "Gateway Phase I",
    "gallery-gateway-phase-one",
    false,
    660,
    "Kerbin",
    "Orbiting")

mission_log.scenario_launch("gateway-lab", "Gateway Laboratory Launch", "v-gateway-lab")
mission_log.scenario_crew("gateway-lab", "Bill Kerman")
mission_log.scenario_event(
    "gateway-lab", "launch", "Laboratory module launched", 8, "Kerbin", "Flying")
mission_log.scenario_event(
    "gateway-lab", "orbit", "Laboratory matched planes", 620, "Kerbin", "Orbiting")

local gateway = mission_log.scenario_dock(
    "v-gateway-phase-one",
    "v-gateway-lab",
    "v-kerbin-gateway",
    "Kerbin Gateway Assembly",
    "gallery-gateway-complete",
    false,
    720,
    "Kerbin",
    "Orbiting")
mission_log.scenario_crew(
    gateway.missionId,
    "Jebediah Kerman",
    "Bob Kerman",
    "Valentina Kerman",
    "Bill Kerman")
mission_log.scenario_note(
    gateway.missionId,
    "Permanent Kerbin-orbit staging hub assembled from three independent launches.")
mission_log.scenario_event(
    gateway.missionId, "peak_g_force", "Maximum docking load — 3.60 g", 680, "Kerbin", "Orbiting")
mission_log.scenario_event(
    gateway.missionId, "peak_altitude", "Assembly orbit — 104.0 km", 700, "Kerbin", "Orbiting")
mission_log.scenario_event(
    gateway.missionId, "peak_speed", "Combined orbital speed — 2,450 m/s", 720, "Kerbin", "Orbiting")
mission_log.scenario_records(gateway.missionId, 104000, 2450, 3.6)

Test.assert.equal(gateway.kind, "Combined", "Gateway should be an overarching combined mission")
Test.assert.equal(gateway.childCount, 2, "Gateway root should have Phase I and Laboratory children")
Test.assert.equal(mission(gateway_phase.missionId).childCount, 2, "Gateway Phase I should retain both launch children")
Test.assert.equal(mission(gateway_phase.missionId).parentMissionId, gateway.missionId, "Gateway nesting")

-- Gallery 3: a lander sortie leaves and then reunites with its carrier.
mission_log.scenario_launch("mun-expedition", "Mun Surface Expedition", "v-mun-stack")
mission_log.scenario_crew("mun-expedition", "Jebediah Kerman", "Bill Kerman")
mission_log.scenario_event(
    "mun-expedition", "launch", "Expedition departed Kerbin", 9, "Kerbin", "Flying")
mission_log.scenario_event(
    "mun-expedition", "peak_g_force", "Peak launch force — 5.10 g", 40, "Kerbin", "Flying")
mission_log.scenario_event(
    "mun-expedition", "orbit", "Trans-Mun injection checkout", 700, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "mun-expedition", "peak_altitude", "Highest orbit — 128.0 km", 820, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "mun-expedition", "peak_speed", "Top transfer speed — 2,510 m/s", 900, "Kerbin", "Flying")
mission_log.scenario_event(
    "mun-expedition", "body_changed", "Expedition arrived at the Mun", 10800, "Mun", "Flying")
mission_log.scenario_event(
    "mun-expedition", "orbit", "Polar science orbit established", 12000, "Mun", "Orbiting")

local peregrine = mission_log.scenario_split(
    "v-mun-stack",
    "v-mun-orbiter",
    "v-peregrine",
    "Peregrine Lander",
    "travel-peregrine",
    "gallery-peregrine-separation",
    15000,
    "Mun",
    "Orbiting")
mission_log.scenario_crew(peregrine.missionId, "Valentina Kerman", "Bob Kerman")
mission_log.scenario_event(
    peregrine.missionId, "peak_altitude", "Descent began at 18.0 km", 15400, "Mun", "Flying")
mission_log.scenario_event(
    peregrine.missionId, "peak_speed", "Peak descent speed — 820 m/s", 15600, "Mun", "Flying")
mission_log.scenario_event(
    peregrine.missionId, "peak_g_force", "Maximum landing force — 5.10 g", 16180, "Mun", "Flying")
mission_log.scenario_event(
    peregrine.missionId, "landed", "Peregrine landed near the arch", 16200, "Mun", "Landed")
mission_log.scenario_event(
    peregrine.missionId, "launch", "Peregrine launched from the Mun", 17400, "Mun", "Flying")
mission_log.scenario_records(peregrine.missionId, 18000, 820, 5.1)

local mun_reunion = mission_log.scenario_dock(
    "v-mun-orbiter",
    "v-peregrine",
    "v-mun-reunited",
    "Mun Surface Expedition",
    "gallery-peregrine-reunion",
    false,
    18000,
    "Mun",
    "Orbiting")
mission_log.scenario_note(
    "mun-expedition",
    "Peregrine returned samples and rejoined the orbiter after one surface sortie.")
mission_log.scenario_records("mun-expedition", 128000, 2510, 5.1)

Test.assert.equal(mun_reunion.missionId, "mun-expedition", "reunion should preserve the expedition root")
Test.assert.equal(mission(peregrine.missionId).status, "Rejoined", "Peregrine should close as rejoined")
Test.assert.equal(mission(peregrine.missionId).parentMissionId, "mun-expedition", "Peregrine should remain a child")

-- Gallery 4: one active Duna branch and one lost branch remain under their carrier.
mission_log.scenario_launch("duna-expedition", "Duna Relay Expedition", "v-duna-stack")
mission_log.scenario_crew("duna-expedition", "Valentina Kerman", "Bill Kerman")
mission_log.scenario_event(
    "duna-expedition", "launch", "Duna convoy departed Kerbin", 11, "Kerbin", "Flying")
mission_log.scenario_event(
    "duna-expedition", "peak_g_force", "Peak launch force — 6.70 g", 48, "Kerbin", "Flying")
mission_log.scenario_event(
    "duna-expedition", "orbit", "Interplanetary stage checked out", 760, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "duna-expedition", "peak_altitude", "Highest coast — 250.0 km", 980, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "duna-expedition", "body_changed", "Convoy entered the Duna system", 5840000, "Duna", "Flying")
mission_log.scenario_event(
    "duna-expedition", "peak_speed", "Peak Duna arrival speed — 4,100 m/s", 5850000, "Duna", "Flying")
mission_log.scenario_event(
    "duna-expedition", "orbit", "Carrier captured at Duna", 5860000, "Duna", "Orbiting")

local ike_mapper = mission_log.scenario_split(
    "v-duna-stack",
    "v-duna-carrier-one",
    "v-ike-mapper",
    "Ike Mapping Probe",
    "travel-ike-mapper",
    "gallery-ike-separation",
    5880000,
    "Duna",
    "Orbiting")
mission_log.scenario_event(
    ike_mapper.missionId, "body_changed", "Mapper entered Ike's influence", 5890000, "Ike", "Flying")
mission_log.scenario_event(
    ike_mapper.missionId, "peak_speed", "Mapper insertion speed — 720 m/s", 5892000, "Ike", "Flying")
mission_log.scenario_event(
    ike_mapper.missionId, "peak_g_force", "Mapper insertion force — 2.80 g", 5894000, "Ike", "Flying")
mission_log.scenario_event(
    ike_mapper.missionId, "orbit", "Ike polar survey began", 5900000, "Ike", "Orbiting")
mission_log.scenario_event(
    ike_mapper.missionId, "peak_altitude", "Survey apoapsis — 32.0 km", 5901000, "Ike", "Orbiting")
mission_log.scenario_records(ike_mapper.missionId, 32000, 720, 2.8)

local duna_relay = mission_log.scenario_split(
    "v-duna-carrier-one",
    "v-duna-carrier-two",
    "v-duna-relay",
    "Duna Atmospheric Relay",
    "travel-duna-relay",
    "gallery-duna-relay-separation",
    5910000,
    "Duna",
    "Orbiting")
mission_log.scenario_event(
    duna_relay.missionId, "aerobrake", "Relay began atmospheric capture", 5920000, "Duna", "Flying")
mission_log.scenario_status("v-duna-relay", "Lost")
mission_log.scenario_note(
    "duna-expedition",
    "Ike mapper remains healthy. Atmospheric relay was lost during capture and is retained in the history.")
mission_log.scenario_records("duna-expedition", 250000, 4100, 6.7)

Test.assert.equal(mission("duna-expedition").status, "Active", "Duna carrier should remain active")
Test.assert.equal(mission(ike_mapper.missionId).status, "Active", "Ike branch should remain active")
Test.assert.equal(mission(duna_relay.missionId).status, "Lost", "relay branch should show its loss")

-- Gallery 5: an ambiguous Eve/Gilly lineage explicitly asks for player review.
mission_log.scenario_launch("eve-demonstrator", "Eve Aerobrake Demonstrator", "v-eve-stack")
mission_log.scenario_crew("eve-demonstrator", "Jebediah Kerman")
mission_log.scenario_event(
    "eve-demonstrator", "launch", "Demonstrator departed Kerbin", 13, "Kerbin", "Flying")
mission_log.scenario_event(
    "eve-demonstrator", "peak_g_force", "Peak launch force — 7.40 g", 52, "Kerbin", "Flying")
mission_log.scenario_event(
    "eve-demonstrator", "orbit", "Eve transfer burn completed", 820, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "eve-demonstrator", "peak_altitude", "Transfer apoapsis — 310.0 km", 940, "Kerbin", "Orbiting")
mission_log.scenario_event(
    "eve-demonstrator", "body_changed", "Demonstrator arrived at Eve", 9350000, "Eve", "Flying")
mission_log.scenario_event(
    "eve-demonstrator", "peak_speed", "Peak Eve arrival speed — 4,750 m/s", 9360000, "Eve", "Flying")

local gilly_observer = mission_log.scenario_split(
    "v-eve-stack",
    "v-eve-carrier",
    "v-gilly-observer",
    "Gilly Observer",
    "travel-gilly-observer",
    "gallery-gilly-separation",
    9380000,
    "Eve",
    "Orbiting")
mission_log.scenario_event(
    gilly_observer.missionId, "body_changed", "Observer transferred to Gilly", 9390000, "Gilly", "Flying")
mission_log.scenario_event(
    gilly_observer.missionId, "orbit", "Gilly observation orbit established", 9400000, "Gilly", "Orbiting")
mission_log.scenario_note(
    "eve-demonstrator",
    "Identity telemetry disagreed after staging. Confirm whether the observer belongs to this campaign.")
mission_log.scenario_records("eve-demonstrator", 310000, 4750, 7.4)
mission_log.scenario_review(
    "eve-demonstrator",
    "Vessel identity changed during Eve staging; confirm the Gilly branch relationship.")

local eve = mission("eve-demonstrator")
Test.assert.true_(eve.needsReview, "Eve demonstrator should be marked for player review")
Test.assert.equal(eve.childCount, 1, "Eve demonstrator should retain the Gilly branch")
Test.assert.equal(mission(gilly_observer.missionId).parentMissionId, eve.missionId, "Gilly branch parent")

assert_valid("gallery fixture", 5, 13)

-- Ordered capture matrix: stories first, archive structure second, correction tools last.
open_story("mun-pathfinder", "top", 8, "Pathfinder story top")
capture("01-mun-pathfinder-story-top")

open_story("mun-pathfinder", "bottom", 8, "Pathfinder story bottom")
capture("02-mun-pathfinder-story-bottom")

open_story(gateway.missionId, "middle", 10, "Gateway combined story")
capture("03-kerbin-gateway-story-middle")

open_story("mun-expedition", "top", 8, "Mun expedition parent")
capture("04-mun-expedition-parent-top")

open_story(peregrine.missionId, "bottom", 5, "Peregrine child story")
capture("05-mun-lander-child-bottom")

open_story("duna-expedition", "middle", 10, "Duna mixed outcome story")
capture("06-duna-mixed-outcome-middle")

mission_log.set_archive_collapsed(gateway.missionId, false)
mission_log.set_archive_collapsed(gateway_phase.missionId, false)
mission_log.set_archive_collapsed("mun-expedition", false)
mission_log.set_archive_collapsed("duna-expedition", false)
mission_log.set_archive_collapsed("eve-demonstrator", false)
local archive_top = open_archive("top", "expanded archive top")
Test.assert.equal(archive_top.collapsedMissionCount, 0, "expanded archive should have no collapsed branches")
Test.assert.equal(archive_top.archiveRenderedNodeCount, 13, "expanded archive should render every mission")
capture("07-gallery-archive-expanded-top")

local archive_bottom = open_archive("bottom", "expanded archive bottom")
Test.assert.equal(archive_bottom.archiveRenderedNodeCount, 13, "expanded archive bottom should retain every mission")
capture("08-gallery-archive-expanded-bottom")

mission_log.set_archive_collapsed(gateway.missionId, true)
mission_log.set_archive_collapsed("mun-expedition", true)
mission_log.set_archive_collapsed("duna-expedition", true)
mission_log.set_archive_collapsed("eve-demonstrator", true)
local archive_collapsed = open_archive("top", "collapsed archive")
Test.assert.equal(archive_collapsed.collapsedMissionCount, 4, "four branching roots should be collapsed")
Test.assert.equal(archive_collapsed.archiveRenderedNodeCount, 5, "collapsed archive should show five roots")
capture("09-gallery-archive-collapsed")

open_story("eve-demonstrator", "top", 7, "Eve needs-review story")
Test.assert.true_(mission("eve-demonstrator").needsReview, "review flag should still be visible")
capture("10-eve-gilly-needs-review")

mission_log.open_editor("eve-demonstrator")
Test.wait.frames(5)
local editor_state = mission_log.review_ui_state()
assert_window_state(editor_state, "story", "editor", "Eve editor")
Test.assert.equal(editor_state.selectedMissionId, "eve-demonstrator", "editor should target Eve")
capture("11-eve-gilly-editor")

mission_log.open_organizer(gilly_observer.missionId)
Test.wait.frames(5)
local organizer_state = mission_log.review_ui_state()
assert_window_state(organizer_state, "story", "organizer", "Gilly child organizer")
Test.assert.equal(
    organizer_state.selectedMissionId,
    gilly_observer.missionId,
    "organizer should target the linked Gilly child")
capture("12-gilly-child-organizer")

assert_valid("gallery after UI review", 5, 13)
Test.assert.equal(capture_count, 12, "gallery should produce the full ordered capture matrix")
Test.report.value("galleryScreenshots", capture_count)
Test.report.value("galleryRoots", 5)
Test.report.value("galleryMissions", 13)
Test.report.value("galleryArchivePath", mission_log.archive_path())
Test.report.attach(mission_log.archive_path())
Test.report.note(
    "Captured linear, nested assembly, sortie reunion, mixed-outcome, needs-review, archive, editor, and organizer states")
mission_log.end_test_session()
