Test.name("Redux Mission Log mission tree sequences")
Test.report.fail_on_log_errors()

Test.assert.true_(Test.mod.is_loaded("ReduxMissionLog"), "ReduxMissionLog should be active")
local mission_log = Test.mod.extension("ReduxMissionLog")
Test.assert.not_equal(mission_log, nil, "ReduxMissionLog should expose its semantic test API")

local function assert_valid(label, roots, nodes)
    local snapshot = mission_log.tree_snapshot()
    local errors = mission_log.validate_tree()
    Test.assert.equal(snapshot.rootCount, roots, label .. " root count")
    Test.assert.equal(snapshot.nodeCount, nodes, label .. " node count")
    Test.assert.equal(#errors, 0, label .. " should satisfy every tree invariant")
end

local function mission(id)
    local value = mission_log.mission(id)
    Test.assert.not_equal(value, nil, "mission should exist: " .. id)
    return value
end

-- Two launches become children of one overarching mission. A duplicate event is harmless.
mission_log.begin_test_session()
mission_log.scenario_launch("launch-a", "Kerbin Station Core", "v-a")
mission_log.scenario_launch("launch-b", "Habitat Flight", "v-b")
assert_valid("independent launches", 2, 2)

local ab = mission_log.scenario_dock("v-a", "v-b", "v-ab", "Kerbin Station", "dock-a-b", false)
Test.assert.equal(ab.kind, "Combined", "independent docking should create a combined mission")
Test.assert.equal(ab.childCount, 2, "combined mission should contain both launches")
Test.assert.equal(mission("launch-a").parentMissionId, ab.missionId, "launch A should be a sub-mission")
Test.assert.equal(mission("launch-b").parentMissionId, ab.missionId, "launch B should be a sub-mission")
Test.assert.equal(mission("launch-a").status, "Joined", "launch A should close into the combined mission")
Test.assert.equal(ab.trackedVesselId, "v-ab", "combined mission should own the docked vessel")
assert_valid("first docking", 1, 3)

local duplicate = mission_log.scenario_dock("v-a", "v-b", "v-ab", "Kerbin Station", "dock-a-b", false)
Test.assert.equal(duplicate.missionId, ab.missionId, "replayed docking should resolve to the same mission")
assert_valid("duplicate docking", 1, 3)

-- A later launch docks to the existing station and wraps both histories once.
mission_log.scenario_launch("launch-c", "Laboratory Flight", "v-c")
local abc = mission_log.scenario_dock("v-ab", "v-c", "v-abc", "Kerbin Research Station", "dock-ab-c", false)
Test.assert.equal(abc.kind, "Combined", "nested docking should create a new overarching mission")
Test.assert.equal(ab.parentMissionId, "", "returned Lua values are snapshots, not live records")
Test.assert.equal(mission(ab.missionId).parentMissionId, abc.missionId, "prior station history should nest under the new mission")
Test.assert.equal(mission("launch-c").parentMissionId, abc.missionId, "new launch should join the new mission")
assert_valid("nested docking", 1, 5)

-- A lander sortie is reused across repeated undock/re-dock cycles, even when KSP changes its vessel ID.
local lander = mission_log.scenario_split("v-abc", "v-carrier-1", "v-lander-1", "Mun Lander", "travel-lander", "split-lander-1")
Test.assert.equal(lander.kind, "Sortie", "first separation should create a sortie")
Test.assert.equal(lander.parentMissionId, abc.missionId, "sortie should belong to the carrier mission")
Test.assert.equal(lander.status, "Active", "separated lander should be active")
assert_valid("first lander separation", 1, 6)

local reunited = mission_log.scenario_dock("v-carrier-1", "v-lander-1", "v-reunited-1", "Mun Expedition", "dock-lander-1", false)
Test.assert.equal(reunited.missionId, abc.missionId, "re-docking a known sortie should preserve its parent mission")
Test.assert.equal(mission(lander.missionId).status, "Rejoined", "lander sortie should close as rejoined")
assert_valid("first lander reunion", 1, 6)

local lander_again = mission_log.scenario_split("v-reunited-1", "v-carrier-2", "v-lander-2", "Mun Lander", "travel-lander", "split-lander-2")
Test.assert.equal(lander_again.missionId, lander.missionId, "travel identity should resume the same sortie after vessel-ID churn")
Test.assert.equal(lander_again.trackedVesselId, "v-lander-2", "resumed sortie should own the new vessel ID")
Test.assert.equal(lander_again.status, "Active", "resumed sortie should be active again")
Test.assert.equal(#lander_again.vesselIds, 2, "sortie should retain both historical vessel IDs")
Test.assert.equal(#lander_again.travelObjectIds, 1, "stable travel identity should not be duplicated")
assert_valid("second lander separation", 1, 6)

mission_log.scenario_dock("v-carrier-2", "v-lander-2", "v-reunited-2", "Mun Expedition", "dock-lander-2", false)
Test.assert.equal(mission(lander.missionId).status, "Rejoined", "second reunion should close the same sortie")
assert_valid("second lander reunion", 1, 6)

mission_log.reload_archive()
Test.assert.equal(mission(lander.missionId).status, "Rejoined", "reload should retain child status")
Test.assert.equal(mission(ab.missionId).parentMissionId, abc.missionId, "reload should retain nested parentage")
assert_valid("complex tree reload", 1, 6)
mission_log.open_window()
Test.wait.frames(5)
Test.capture.screenshot("mission-tree-nested", {
    scale = 1,
    hideUI = false,
    waitFrames = 2
})

-- Two simultaneously detached craft can merge beneath their common carrier.
mission_log.begin_test_session()
mission_log.scenario_launch("carrier", "Duna Carrier", "v-carrier")
local scout = mission_log.scenario_split("v-carrier", "v-carrier-a", "v-scout", "Scout", "travel-scout", "split-scout")
local tanker = mission_log.scenario_split("v-carrier-a", "v-carrier-b", "v-tanker", "Tanker", "travel-tanker", "split-tanker")
local service_pair = mission_log.scenario_dock("v-scout", "v-tanker", "v-service-pair", "Surface Service Pair", "dock-siblings", false)
Test.assert.equal(service_pair.kind, "Combined", "sibling docking should create a combined sub-mission")
Test.assert.equal(service_pair.parentMissionId, "carrier", "sibling combination should remain under the common carrier")
Test.assert.equal(mission(scout.missionId).parentMissionId, service_pair.missionId, "scout should move under sibling combination")
Test.assert.equal(mission(tanker.missionId).parentMissionId, service_pair.missionId, "tanker should move under sibling combination")
Test.assert.equal(mission("carrier").trackedVesselId, "v-carrier-b", "carrier should remain independently active")
assert_valid("sibling docking", 1, 4)

-- Losing a detached craft closes only that branch.
mission_log.begin_test_session()
mission_log.scenario_launch("eve-carrier", "Eve Carrier", "v-eve-carrier")
local probe = mission_log.scenario_split("v-eve-carrier", "v-eve-carrier-2", "v-probe", "Eve Probe", "travel-probe", "split-probe")
mission_log.scenario_status("v-probe", "Lost")
Test.assert.equal(mission(probe.missionId).status, "Lost", "destroyed probe branch should be lost")
Test.assert.equal(mission("eve-carrier").status, "Active", "carrier branch should remain active")
Test.assert.equal(mission("eve-carrier").trackedVesselId, "v-eve-carrier-2", "carrier binding should remain intact")
assert_valid("lost sortie", 1, 2)

-- Manual adoption and unlinking use the same invariant-checked tree operations.
mission_log.begin_test_session()
mission_log.scenario_launch("manual-parent", "Jool Campaign", "v-jool")
mission_log.scenario_launch("manual-child", "Relay Deployment", "v-relay")
mission_log.scenario_adopt("manual-child", "manual-parent")
Test.assert.equal(mission("manual-child").parentMissionId, "manual-parent", "manual adoption should set the parent")
Test.assert.equal(mission("manual-child").parentRelation, "Manual", "manual adoption should record its relation")
assert_valid("manual adoption", 1, 2)

local cycle_ok = pcall(function()
    mission_log.scenario_adopt("manual-parent", "manual-child")
end)
Test.assert.false_(cycle_ok, "manual adoption should reject a cycle")
assert_valid("rejected manual cycle", 1, 2)

mission_log.scenario_unlink("manual-child")
Test.assert.equal(mission("manual-child").parentMissionId, "", "manual unlink should restore a root")
assert_valid("manual unlink", 2, 2)

mission_log.scenario_status("v-relay", "Lost")
local repaired = mission_log.scenario_track("manual-child", "v-jool")
Test.assert.equal(repaired.status, "Active", "manual binding repair should reactivate the selected mission")
Test.assert.equal(repaired.trackedVesselId, "v-jool", "manual binding repair should move the live vessel")
Test.assert.equal(mission("manual-parent").status, "Joined", "displaced owner should close instead of becoming an active ghost")
Test.assert.true_(mission("manual-parent").needsReview, "displaced owner should be marked for review")
assert_valid("manual binding repair", 2, 2)

mission_log.open_window()
Test.wait.frames(5)
Test.capture.screenshot("mission-tree-archive", {
    scale = 1,
    hideUI = false,
    waitFrames = 2
})
Test.report.value("validatedSequences", 15)
Test.report.value("finalArchivePath", mission_log.archive_path())
Test.report.attach(mission_log.archive_path())
Test.report.note("Verified exact mission trees for docking, nested merges, sorties, reunions, identity churn, losses, manual corrections, idempotency, and reload")
mission_log.end_test_session()
