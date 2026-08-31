Test.name("Redux Mission Log planner and flight reconciliation")
Test.report.fail_on_log_errors()

local assertion_count = 0
local mission_log

local function expect_equal(actual, expected, message)
    assertion_count = assertion_count + 1
    Test.assert.equal(actual, expected, message)
end

local function expect_not_equal(actual, expected, message)
    assertion_count = assertion_count + 1
    Test.assert.not_equal(actual, expected, message)
end

local function expect_true(actual, message)
    assertion_count = assertion_count + 1
    Test.assert.true_(actual, message)
end

local function expect_greater(actual, expected, message)
    assertion_count = assertion_count + 1
    Test.assert.greater(actual, expected, message)
end

local function objective_by_id(plan, objective_id)
    for _, objective in ipairs(plan.objectives) do
        if objective.objectiveId == objective_id then
            return objective
        end
    end
    return nil
end

local function slot_by_id(plan, slot_id)
    for _, slot in ipairs(plan.vessels) do
        if slot.slotId == slot_id then
            return slot
        end
    end
    return nil
end

local function fact_for_mission(facts, kind, mission_id)
    for _, fact in ipairs(facts) do
        if fact.kind == kind and fact.missionId == mission_id then
            return fact
        end
    end
    return nil
end

local function contains_value(values, expected)
    for _, value in ipairs(values) do
        if value == expected then
            return true
        end
    end
    return false
end

local function assert_objective_status(plan, objective_id, status, label)
    local objective = objective_by_id(plan, objective_id)
    expect_not_equal(objective, nil, label .. " should remain in the plan")
    expect_equal(objective.status, status, label .. " status")
    return objective
end

local function assert_valid_tree(label, expected_roots, expected_nodes)
    local snapshot = mission_log.tree_snapshot()
    local errors = mission_log.validate_tree()
    expect_equal(snapshot.rootCount, expected_roots, label .. " root count")
    expect_equal(snapshot.nodeCount, expected_nodes, label .. " node count")
    expect_equal(#errors, 0, label .. " tree invariants")
end

expect_true(Test.mod.is_loaded("ReduxMissionLog"), "ReduxMissionLog should be active")
mission_log = Test.mod.extension("ReduxMissionLog")
expect_not_equal(mission_log, nil, "ReduxMissionLog should expose its semantic test API")

-- This suite uses only synthetic mission observations and its own isolated sidecars.
-- It intentionally requires no save-game, craft, or flight fixture.
mission_log.begin_test_session()
expect_equal(mission_log.archive_count(), 0, "isolated mission archive should start empty")
expect_equal(mission_log.plan_count(), 0, "isolated planner store should start empty")

local archive_path = mission_log.archive_path()
local plan_path = mission_log.plan_path()
expect_not_equal(plan_path, archive_path, "plans and observed missions should use separate sidecars")
expect_true(
    string.find(plan_path, "mission-plans.json", 1, true) ~= nil,
    "isolated planner store should identify the mission-plans sidecar")

local scenario_campaign = "redux-mission-log-scenarios"

-- A realistic two-launch Mun expedition: assemble in Kerbin orbit, travel to
-- Mun, separate a lander, land, and rejoin the carrier before completion.
local expedition = mission_log.create_plan(
    scenario_campaign,
    "Mun Gateway Surface Expedition",
    "Assemble two launches in Kerbin orbit, land Peregrine at the Mun, and return to Gateway.")
expect_equal(expedition.status, "Draft", "new expedition should begin as a draft")
expect_equal(expedition.title, "Mun Gateway Surface Expedition", "plan title should round-trip")
expect_equal(expedition.campaignId, scenario_campaign, "plan should retain its campaign")
expect_equal(expedition.objectiveCount, 0, "new plan should have no implicit objectives")

local carrier_slot = mission_log.plan_add_vessel(
    expedition.planId,
    "Gateway Carrier",
    "Orbital carrier and return vehicle",
    true)
local lander_slot = mission_log.plan_add_vessel(
    expedition.planId,
    "Peregrine Lander",
    "Crewed surface lander",
    true)
expect_equal(carrier_slot.order, 0, "carrier should be the first launch slot")
expect_equal(lander_slot.order, 1, "lander should be the second launch slot")
expect_true(carrier_slot.required, "carrier should be required")
expect_true(lander_slot.required, "lander should be required")

mission_log.plan_select_vehicle(
    expedition.planId,
    carrier_slot.slotId,
    "saved-gateway-carrier",
    "Gateway Carrier",
    "Workspaces/Mun/Gateway Carrier.json",
    "Vehicle Assembly Building")
mission_log.plan_select_vehicle(
    expedition.planId,
    lander_slot.slotId,
    "saved-peregrine-lander",
    "Peregrine Lander",
    "Workspaces/Mun/Peregrine Lander.json",
    "Vehicle Assembly Building")

expedition = mission_log.plan(expedition.planId)
local saved_carrier = slot_by_id(expedition, carrier_slot.slotId)
local saved_lander = slot_by_id(expedition, lander_slot.slotId)
expect_not_equal(saved_carrier, nil, "carrier slot should be queryable")
expect_not_equal(saved_lander, nil, "lander slot should be queryable")
expect_equal(saved_carrier.savedVehicleId, "saved-gateway-carrier", "carrier saved-craft ID")
expect_equal(saved_carrier.savedVehicleName, "Gateway Carrier", "carrier saved-craft name")
expect_equal(
    saved_carrier.savedVehiclePath,
    "Workspaces/Mun/Gateway Carrier.json",
    "carrier saved-craft path")
expect_equal(
    saved_carrier.savedVehicleLocation,
    "Vehicle Assembly Building",
    "carrier launch location")
expect_equal(saved_carrier.launchState, "", "selected carrier should not pretend it was launched")
expect_equal(saved_lander.savedVehicleId, "saved-peregrine-lander", "lander saved-craft ID")
expect_equal(
    saved_lander.savedVehiclePath,
    "Workspaces/Mun/Peregrine Lander.json",
    "lander saved-craft path")

local objectives = {}
objectives.launch_carrier = mission_log.plan_add_objective(
    expedition.planId, "Launch", "Launch Gateway Carrier",
    carrier_slot.slotId, "Kerbin", nil, nil, false)
objectives.orbit_carrier = mission_log.plan_add_objective(
    expedition.planId, "Orbit", "Establish Gateway Carrier orbit",
    carrier_slot.slotId, "Kerbin", "Orbiting", nil, false)
objectives.launch_lander = mission_log.plan_add_objective(
    expedition.planId, "Launch", "Launch Peregrine Lander",
    lander_slot.slotId, "Kerbin", nil, nil, false)
objectives.orbit_lander = mission_log.plan_add_objective(
    expedition.planId, "Orbit", "Rendezvous Peregrine in Kerbin orbit",
    lander_slot.slotId, "Kerbin", "Orbiting", nil, false)
objectives.assemble = mission_log.plan_add_objective(
    expedition.planId, "Dock", "Dock Peregrine with Gateway",
    carrier_slot.slotId, "Kerbin", "Orbiting", nil, false, lander_slot.slotId)
objectives.enter_mun = mission_log.plan_add_objective(
    expedition.planId, "Body", "Enter the Mun sphere of influence",
    nil, "Mun", nil, nil, false)
objectives.orbit_mun = mission_log.plan_add_objective(
    expedition.planId, "Orbit", "Establish Mun orbit",
    nil, "Mun", "Orbiting", nil, false)
objectives.separate_lander = mission_log.plan_add_objective(
    expedition.planId, "Separate", "Separate Peregrine for descent",
    lander_slot.slotId, "Mun", "Orbiting", nil, false)
objectives.land_mun = mission_log.plan_add_objective(
    expedition.planId, "Land", "Land Peregrine on the Mun",
    lander_slot.slotId, "Mun", "Landed", nil, false)
objectives.rejoin = mission_log.plan_add_objective(
    expedition.planId, "Dock", "Rejoin Gateway in Mun orbit",
    carrier_slot.slotId, "Mun", "Orbiting", nil, false, lander_slot.slotId)
objectives.complete = mission_log.plan_add_objective(
    expedition.planId, "Complete", "Complete the expedition",
    nil, nil, nil, nil, false)

expedition = mission_log.plan(expedition.planId)
expect_equal(expedition.objectiveCount, 11, "expedition should retain all ordered objectives")
expect_equal(expedition.objectives[1].order, 0, "objective order should be zero-based and stable")
expect_equal(expedition.objectives[11].order, 10, "last objective order should be stable")
expect_equal(expedition.objectives[1].kind, "Launch", "first objective kind")
expect_equal(expedition.objectives[11].kind, "Complete", "last objective kind")
expect_equal(
    objective_by_id(expedition, objectives.assemble.objectiveId).relatedVesselSlotId,
    lander_slot.slotId,
    "assembly objective should name the lander participant")
expect_equal(
    objective_by_id(expedition, objectives.rejoin.objectiveId).relatedVesselSlotId,
    lander_slot.slotId,
    "rejoin objective should name the lander participant")

mission_log.plan_activate(expedition.planId)
expedition = mission_log.plan_recompute(expedition.planId)
expect_equal(expedition.status, "Active", "activated expedition should be active")
expect_equal(
    expedition.currentObjectiveId,
    objectives.launch_carrier.objectiveId,
    "first carrier launch should be current before observations")

local carrier_launch = mission_log.scenario_launch(
    "planner-gateway-launch", "Gateway Carrier", "v-planner-gateway")
mission_log.plan_bind_vessel(
    expedition.planId,
    carrier_slot.slotId,
    carrier_launch.missionId,
    "v-planner-gateway",
    carrier_launch.events[1].recordedUtc)
expect_equal(
    #mission_log.plan_facts(expedition.planId),
    0,
    "mission creation alone should not satisfy a planned launch")
mission_log.scenario_event(
    carrier_launch.missionId,
    "launch",
    "Gateway Carrier lifted off from Kerbin",
    12,
    "Kerbin",
    "Flying",
    "planner-carrier-liftoff")
Test.wait.frames(1)
mission_log.scenario_event(
    carrier_launch.missionId,
    "orbit",
    "Gateway Carrier established Kerbin orbit",
    620,
    "Kerbin",
    "Orbiting",
    "planner-carrier-kerbin-orbit")
Test.wait.frames(1)

expedition = mission_log.plan_recompute(expedition.planId)
expect_equal(expedition.achievedCount, 2, "carrier launch and orbit should advance two steps")
assert_objective_status(
    expedition, objectives.launch_carrier.objectiveId, "Achieved", "carrier launch")
assert_objective_status(
    expedition, objectives.orbit_carrier.objectiveId, "Achieved", "carrier Kerbin orbit")
expect_equal(
    expedition.currentObjectiveId,
    objectives.launch_lander.objectiveId,
    "lander launch should become current after the carrier reaches orbit")
expect_equal(expedition.deviationCount, 0, "nominal carrier launch should not create a deviation")

local lander_launch = mission_log.scenario_launch(
    "planner-peregrine-launch", "Peregrine Lander", "v-planner-peregrine")
mission_log.plan_bind_vessel(
    expedition.planId,
    lander_slot.slotId,
    lander_launch.missionId,
    "v-planner-peregrine",
    lander_launch.events[1].recordedUtc)
mission_log.scenario_event(
    lander_launch.missionId,
    "launch",
    "Peregrine Lander lifted off from Kerbin",
    12,
    "Kerbin",
    "Flying",
    "planner-lander-liftoff")
Test.wait.frames(1)
mission_log.scenario_event(
    lander_launch.missionId,
    "orbit",
    "Peregrine rendezvoused in Kerbin orbit",
    540,
    "Kerbin",
    "Orbiting",
    "planner-lander-kerbin-orbit")
Test.wait.frames(1)

expedition = mission_log.plan_recompute(expedition.planId)
expect_equal(expedition.achievedCount, 4, "both launches and parking orbits should be achieved")
expect_equal(
    expedition.currentObjectiveId,
    objectives.assemble.objectiveId,
    "assembly docking should become current")
expect_equal(expedition.deviationCount, 0, "two nominal launches should remain deviation-free")

local stack = mission_log.scenario_dock(
    "v-planner-gateway",
    "v-planner-peregrine",
    "v-planner-mun-stack",
    "Mun Expedition Stack",
    "planner-dock-assembly",
    false,
    900,
    "Kerbin",
    "Orbiting")
Test.wait.frames(1)
mission_log.scenario_event(
    stack.missionId,
    "body_changed",
    "Entered the Mun sphere of influence",
    24800,
    "Mun",
    "Orbiting",
    "planner-enter-mun")
Test.wait.frames(1)
mission_log.scenario_event(
    stack.missionId,
    "orbit",
    "Established a stable Mun orbit",
    27600,
    "Mun",
    "Orbiting",
    "planner-mun-orbit")
Test.wait.frames(1)

local sortie = mission_log.scenario_split(
    "v-planner-mun-stack",
    "v-planner-gateway-mun",
    "v-planner-peregrine-mun",
    "Peregrine Lander",
    "travel-planner-peregrine-sortie",
    "planner-separate-peregrine",
    28200,
    "Mun",
    "Orbiting")
Test.wait.frames(1)
mission_log.scenario_event(
    sortie.missionId,
    "landed",
    "Peregrine landed in the East Crater",
    30600,
    "Mun",
    "Landed",
    "planner-peregrine-landed")
Test.wait.frames(1)
mission_log.scenario_dock(
    "v-planner-gateway-mun",
    "v-planner-peregrine-mun",
    "v-planner-rejoined",
    "Mun Expedition Stack",
    "planner-dock-rejoin",
    false,
    35200,
    "Mun",
    "Orbiting")
Test.wait.frames(1)
mission_log.scenario_status("v-planner-rejoined", "Completed")

local expedition_facts = mission_log.plan_facts(expedition.planId)
local expected_fact_kinds = {
    "Launch", "Orbit", "Launch", "Orbit", "Dock", "Body",
    "Orbit", "Separate", "Land", "Dock", "Complete"
}
expect_equal(#expedition_facts, #expected_fact_kinds, "nominal expedition should yield one fact per step")
for index, expected_kind in ipairs(expected_fact_kinds) do
    expect_equal(
        expedition_facts[index].kind,
        expected_kind,
        "nominal fact kind at position " .. index)
end

expect_equal(
    expedition_facts[1].vesselSlotId,
    carrier_slot.slotId,
    "carrier launch fact should retain its planned vessel slot")
expect_equal(
    expedition_facts[1].missionId,
    carrier_launch.missionId,
    "carrier launch fact should link to the carrier launch mission")
expect_equal(
    expedition_facts[2].vesselSlotId,
    carrier_slot.slotId,
    "carrier orbit fact should retain its planned vessel slot")
expect_equal(
    expedition_facts[2].missionId,
    carrier_launch.missionId,
    "carrier orbit fact should link to the carrier launch mission")
expect_equal(
    expedition_facts[3].vesselSlotId,
    lander_slot.slotId,
    "lander launch fact should retain its planned vessel slot")
expect_equal(
    expedition_facts[3].missionId,
    lander_launch.missionId,
    "lander launch fact should link to the lander launch mission")
expect_equal(
    expedition_facts[4].vesselSlotId,
    lander_slot.slotId,
    "lander orbit fact should retain its planned vessel slot")
expect_equal(
    expedition_facts[4].missionId,
    lander_launch.missionId,
    "lander orbit fact should link to the lander launch mission")
expect_equal(
    expedition_facts[5].missionId,
    stack.missionId,
    "assembly fact should link to the overarching mission")
expect_true(
    contains_value(expedition_facts[5].vesselSlotIds, carrier_slot.slotId),
    "assembly fact should include the carrier slot")
expect_true(
    contains_value(expedition_facts[5].vesselSlotIds, lander_slot.slotId),
    "assembly fact should include the lander slot")
expect_equal(expedition_facts[6].missionId, stack.missionId, "Mun arrival should link to the stack")
expect_equal(expedition_facts[7].missionId, stack.missionId, "Mun orbit should link to the stack")
expect_equal(expedition_facts[8].missionId, stack.missionId, "separation should link to the carrier story")
expect_true(
    contains_value(expedition_facts[8].vesselSlotIds, lander_slot.slotId),
    "separation fact should include the planned lander slot")
expect_equal(expedition_facts[9].missionId, sortie.missionId, "landing should link to the lander sortie")
expect_equal(#expedition_facts[9].vesselSlotIds, 1, "landing should be scoped to one vessel slot")
expect_equal(
    expedition_facts[9].vesselSlotIds[1],
    lander_slot.slotId,
    "landing fact should inherit the planned lander slot")
expect_equal(expedition_facts[10].missionId, sortie.missionId, "rejoin should link to the returning sortie")
expect_true(
    contains_value(expedition_facts[10].vesselSlotIds, carrier_slot.slotId),
    "rejoin fact should include the carrier slot")
expect_true(
    contains_value(expedition_facts[10].vesselSlotIds, lander_slot.slotId),
    "rejoin fact should include the lander slot")
expect_equal(expedition_facts[11].missionId, stack.missionId, "completion should link to the overarching mission")

expedition = mission_log.plan_recompute(expedition.planId)
expect_equal(expedition.status, "Completed", "observed completion should close the nominal plan")
expect_equal(expedition.achievedCount, 11, "all nominal objectives should be achieved")
expect_equal(expedition.resolvedCount, 11, "all nominal objectives should be resolved")
expect_equal(expedition.currentObjectiveId, "", "completed plan should have no current objective")
expect_equal(expedition.deviationCount, 0, "nominal mission should remain deviation-free")
for _, objective in ipairs(expedition.objectives) do
    expect_equal(objective.status, "Achieved", objective.title .. " should be achieved")
    expect_not_equal(objective.matchedFactId, "", objective.title .. " should retain its matching fact")
end

local carrier_after_flight = slot_by_id(expedition, carrier_slot.slotId)
local lander_after_flight = slot_by_id(expedition, lander_slot.slotId)
expect_equal(
    carrier_after_flight.boundMissionId,
    carrier_launch.missionId,
    "carrier slot should retain its original launch mission")
expect_equal(
    lander_after_flight.boundMissionId,
    lander_launch.missionId,
    "lander slot should retain its original launch mission")
expect_equal(carrier_after_flight.launchState, "Bound", "carrier launch state should be bound")
expect_equal(lander_after_flight.launchState, "Bound", "lander launch state should be bound")

local stack_record = mission_log.mission(stack.missionId)
local carrier_record = mission_log.mission(carrier_launch.missionId)
local lander_record = mission_log.mission(lander_launch.missionId)
local sortie_record = mission_log.mission(sortie.missionId)
expect_equal(stack_record.kind, "Combined", "docking should form an overarching mission")
expect_equal(stack_record.childCount, 3, "overarching mission should contain both launches and the sortie")
expect_equal(carrier_record.parentMissionId, stack.missionId, "carrier launch should become a sub-mission")
expect_equal(lander_record.parentMissionId, stack.missionId, "lander launch should become a sub-mission")
expect_equal(sortie_record.parentMissionId, stack.missionId, "lander sortie should belong to the expedition")
expect_equal(carrier_record.status, "Joined", "carrier launch leg should close as joined")
expect_equal(lander_record.status, "Joined", "lander launch leg should close as joined")
expect_equal(sortie_record.status, "Rejoined", "lander sortie should close as rejoined")
expect_equal(stack_record.status, "Completed", "overarching mission should retain the final outcome")
assert_valid_tree("nominal expedition", 1, 4)

-- The dense event list stays one line per event until explicitly hovered/focused.
-- The semantic expansion hook tests that state without coordinate-based clicking.
local stack_timeline = mission_log.mission_timeline(stack.missionId)
mission_log.open_mission(stack.missionId)
Test.wait.frames(5)
expect_equal(
    mission_log.rendered_timeline_count(),
    #stack_timeline,
    "story UI should render the complete overarching timeline")
expect_equal(mission_log.expanded_timeline_event_count(), 0, "timeline should begin fully condensed")
expect_greater(#stack_timeline, 2, "expansion test needs multiple timeline rows")
mission_log.set_timeline_event_expanded(stack_timeline[1].eventId, true)
expect_equal(mission_log.expanded_timeline_event_count(), 1, "one timeline row should expand")
mission_log.set_timeline_event_expanded(stack_timeline[2].eventId, true)
expect_equal(mission_log.expanded_timeline_event_count(), 2, "two rows should expand independently")
mission_log.set_timeline_event_expanded(stack_timeline[1].eventId, false)
expect_equal(mission_log.expanded_timeline_event_count(), 1, "collapsing one row should preserve the other")
mission_log.set_timeline_event_expanded(stack_timeline[2].eventId, false)
expect_equal(mission_log.expanded_timeline_event_count(), 0, "timeline should return to its condensed state")

-- A second mission deliberately reaches Minmus orbit before its planned SOI
-- milestone. The event remains factual while the plan records exactly one
-- out-of-order deviation.
local deviation_plan = mission_log.create_plan(
    scenario_campaign,
    "Minmus Direct Sample Flight",
    "Exercise deterministic out-of-order reconciliation.")
local minmus_slot = mission_log.plan_add_vessel(
    deviation_plan.planId, "Minmus Hopper", "Direct ascent lander", true)
mission_log.plan_select_vehicle(
    deviation_plan.planId,
    minmus_slot.slotId,
    "saved-minmus-hopper",
    "Minmus Hopper",
    "Workspaces/Minmus/Minmus Hopper.json",
    "Vehicle Assembly Building")

local deviation_objectives = {}
deviation_objectives.launch = mission_log.plan_add_objective(
    deviation_plan.planId, "Launch", "Launch Minmus Hopper",
    minmus_slot.slotId, "Kerbin", nil, nil, false)
deviation_objectives.body = mission_log.plan_add_objective(
    deviation_plan.planId, "Body", "Enter Minmus sphere of influence",
    minmus_slot.slotId, "Minmus", nil, nil, false)
deviation_objectives.orbit = mission_log.plan_add_objective(
    deviation_plan.planId, "Orbit", "Establish Minmus orbit",
    minmus_slot.slotId, "Minmus", "Orbiting", nil, false)
deviation_objectives.land = mission_log.plan_add_objective(
    deviation_plan.planId, "Land", "Land on Minmus",
    minmus_slot.slotId, "Minmus", "Landed", nil, false)
deviation_objectives.complete = mission_log.plan_add_objective(
    deviation_plan.planId, "Complete", "Complete Minmus flight",
    minmus_slot.slotId, nil, nil, nil, false)
mission_log.plan_activate(deviation_plan.planId)
deviation_plan = mission_log.plan_recompute(deviation_plan.planId)
expect_equal(
    deviation_plan.currentObjectiveId,
    deviation_objectives.launch.objectiveId,
    "Minmus launch should initially be current")

local minmus_launch = mission_log.scenario_launch(
    "planner-minmus-launch", "Minmus Hopper", "v-planner-minmus")
mission_log.plan_bind_vessel(
    deviation_plan.planId,
    minmus_slot.slotId,
    minmus_launch.missionId,
    "v-planner-minmus",
    minmus_launch.events[1].recordedUtc)
mission_log.scenario_event(
    minmus_launch.missionId,
    "launch",
    "Minmus Hopper lifted off from Kerbin",
    12,
    "Kerbin",
    "Flying",
    "planner-minmus-liftoff")
Test.wait.frames(1)
mission_log.scenario_event(
    minmus_launch.missionId,
    "orbit",
    "Minmus orbit recorded before SOI milestone",
    22400,
    "Minmus",
    "Orbiting",
    "planner-minmus-orbit-early")
Test.wait.frames(1)
mission_log.scenario_event(
    minmus_launch.missionId,
    "body_changed",
    "Minmus sphere of influence confirmed late",
    22500,
    "Minmus",
    "Orbiting",
    "planner-minmus-body-late")
Test.wait.frames(1)
mission_log.scenario_event(
    minmus_launch.missionId,
    "landed",
    "Minmus Hopper landed on the Greater Flats",
    24600,
    "Minmus",
    "Landed",
    "planner-minmus-landed")
Test.wait.frames(1)
mission_log.scenario_status("v-planner-minmus", "Completed")

local deviation_facts = mission_log.plan_facts(deviation_plan.planId)
local expected_deviation_kinds = { "Launch", "Orbit", "Body", "Land", "Complete" }
expect_equal(#deviation_facts, 5, "deviation flight should produce five relevant facts")
for index, expected_kind in ipairs(expected_deviation_kinds) do
    expect_equal(
        deviation_facts[index].kind,
        expected_kind,
        "deviation fact order at position " .. index)
end

deviation_plan = mission_log.plan_recompute(deviation_plan.planId)
expect_equal(deviation_plan.status, "Completed", "deviated flight should still complete")
expect_equal(deviation_plan.achievedCount, 4, "four Minmus objectives should be achieved")
expect_equal(deviation_plan.resolvedCount, 5, "every Minmus objective should be resolved")
expect_equal(deviation_plan.deviationCount, 1, "out-of-order flight should have one deviation")
expect_equal(deviation_plan.deviations[1].kind, "OutOfOrder", "deviation classification")
expect_equal(
    deviation_plan.deviations[1].objectiveId,
    deviation_objectives.orbit.objectiveId,
    "deviation should point to the early orbit objective")
expect_equal(
    deviation_plan.deviations[1].factId,
    deviation_facts[2].factId,
    "deviation should retain the exact early orbit fact")
assert_objective_status(
    deviation_plan, deviation_objectives.launch.objectiveId, "Achieved", "Minmus launch")
assert_objective_status(
    deviation_plan, deviation_objectives.body.objectiveId, "Achieved", "late Minmus body event")
local early_orbit = assert_objective_status(
    deviation_plan, deviation_objectives.orbit.objectiveId, "Deviated", "early Minmus orbit")
expect_equal(early_orbit.matchedFactId, deviation_facts[2].factId, "early orbit fact should stay linked")
assert_objective_status(
    deviation_plan, deviation_objectives.land.objectiveId, "Achieved", "Minmus landing")
assert_objective_status(
    deviation_plan, deviation_objectives.complete.objectiveId, "Achieved", "Minmus completion")

-- Completed plans remain correctable: accepting the early orbit as the
-- intended observation resolves the deviation without reopening the plan.
mission_log.plan_match_objective(
    deviation_plan.planId,
    deviation_objectives.orbit.objectiveId,
    deviation_facts[2].factId,
    "Accepted the imported orbit milestone after review.")
deviation_plan = mission_log.plan(deviation_plan.planId)
expect_equal(deviation_plan.status, "Completed", "correcting a deviation should not reopen the plan")
expect_equal(deviation_plan.deviationCount, 0, "manual correction should clear the completed deviation")
local corrected_orbit = assert_objective_status(
    deviation_plan, deviation_objectives.orbit.objectiveId, "Achieved", "corrected Minmus orbit")
expect_true(corrected_orbit.manual, "completed deviation correction should be explicitly manual")
expect_equal(
    corrected_orbit.matchedFactId,
    deviation_facts[2].factId,
    "completed correction should retain the reviewed fact")

-- A capsule recovery exercises two adapter paths that are easy to lose in a
-- topology-focused story: situation changes and a Recovered terminal status.
local recovery_plan = mission_log.create_plan(
    scenario_campaign,
    "Kerbin Capsule Recovery",
    "Launch, splash down, and recover a crew capsule.")
local recovery_slot = mission_log.plan_add_vessel(
    recovery_plan.planId, "Recovery Capsule", "Crew return capsule", true)
mission_log.plan_select_vehicle(
    recovery_plan.planId,
    recovery_slot.slotId,
    "saved-recovery-capsule",
    "Recovery Capsule",
    "Workspaces/Kerbin/Recovery Capsule.json",
    "Vehicle Assembly Building")
local recovery_launch_objective = mission_log.plan_add_objective(
    recovery_plan.planId, "Launch", "Launch the recovery capsule",
    recovery_slot.slotId, "Kerbin", nil, nil, false)
local recovery_splash_objective = mission_log.plan_add_objective(
    recovery_plan.planId, "Situation", "Splash down near the KSC",
    recovery_slot.slotId, "Kerbin", "Splashed", nil, false)
local recovery_complete_objective = mission_log.plan_add_objective(
    recovery_plan.planId, "Recover", "Recover the capsule and crew",
    recovery_slot.slotId, "Kerbin", "Splashed", nil, false)
mission_log.plan_activate(recovery_plan.planId)
recovery_plan = mission_log.plan_recompute(recovery_plan.planId)
expect_equal(
    recovery_plan.currentObjectiveId,
    recovery_launch_objective.objectiveId,
    "capsule launch should initially be current")

local recovery_mission = mission_log.scenario_launch(
    "planner-recovery-launch", "Recovery Capsule", "v-planner-recovery")
mission_log.plan_bind_vessel(
    recovery_plan.planId,
    recovery_slot.slotId,
    recovery_mission.missionId,
    "v-planner-recovery",
    recovery_mission.events[1].recordedUtc)
mission_log.scenario_event(
    recovery_mission.missionId,
    "launch",
    "Recovery Capsule lifted off from Kerbin",
    10,
    "Kerbin",
    "Flying",
    "planner-recovery-liftoff")
Test.wait.frames(1)
mission_log.scenario_event(
    recovery_mission.missionId,
    "situation_changed",
    "Recovery Capsule splashed down near the KSC",
    780,
    "Kerbin",
    "Splashed",
    "planner-recovery-splashdown")
Test.wait.frames(1)
mission_log.scenario_status("v-planner-recovery", "Recovered")

local recovery_facts = mission_log.plan_facts(recovery_plan.planId)
expect_equal(#recovery_facts, 3, "recovery flight should produce three relevant facts")
expect_equal(recovery_facts[1].kind, "Launch", "recovery fact one should be launch")
expect_equal(recovery_facts[2].kind, "Situation", "recovery fact two should be situation")
expect_equal(recovery_facts[3].kind, "Recover", "recovery fact three should be recovery")
expect_equal(
    recovery_facts[1].missionId,
    recovery_mission.missionId,
    "recovery launch should link to the capsule mission")
expect_equal(
    recovery_facts[2].missionId,
    recovery_mission.missionId,
    "splashdown situation should link to the capsule mission")
expect_equal(
    recovery_facts[3].missionId,
    recovery_mission.missionId,
    "recovery outcome should link to the capsule mission")
expect_true(recovery_facts[1].isPlanScoped, "recovery launch should be plan-scoped")
expect_true(recovery_facts[2].isPlanScoped, "splashdown situation should be plan-scoped")
expect_true(recovery_facts[3].isPlanScoped, "recovery outcome should be plan-scoped")
expect_equal(recovery_facts[2].situation, "Splashed", "situation fact should retain splashdown state")

recovery_plan = mission_log.plan_recompute(recovery_plan.planId)
expect_equal(recovery_plan.status, "Completed", "recovery should resolve the plan")
expect_equal(recovery_plan.achievedCount, 3, "every recovery objective should be achieved")
expect_equal(recovery_plan.resolvedCount, 3, "every recovery objective should be resolved")
expect_equal(recovery_plan.deviationCount, 0, "nominal recovery should have no deviations")
assert_objective_status(
    recovery_plan, recovery_launch_objective.objectiveId, "Achieved", "capsule launch")
assert_objective_status(
    recovery_plan, recovery_splash_objective.objectiveId, "Achieved", "capsule splashdown")
local recovered_objective = assert_objective_status(
    recovery_plan, recovery_complete_objective.objectiveId, "Achieved", "capsule recovery")
expect_equal(
    recovered_objective.matchedFactId,
    recovery_facts[3].factId,
    "recovery objective should retain the terminal fact")
expect_equal(
    mission_log.mission(recovery_mission.missionId).status,
    "Recovered",
    "observed mission should retain the recovered outcome")
assert_valid_tree("capsule recovery", 3, 6)

-- Child outcomes are facts about their branches, not the completion of the
-- connected mission tree. Only the overarching root outcome may close this
-- unscoped Complete objective.
local guard_plan = mission_log.create_plan(
    scenario_campaign,
    "Overarching Completion Guard",
    "Two launched craft assemble before two disposable sorties separate.")
local guard_orbiter_slot = mission_log.plan_add_vessel(
    guard_plan.planId, "Guard Orbiter", "Overarching carrier", true)
local guard_payload_slot = mission_log.plan_add_vessel(
    guard_plan.planId, "Guard Payload", "Docked mission payload", true)
local guard_launch_orbiter = mission_log.plan_add_objective(
    guard_plan.planId, "Launch", "Launch Guard Orbiter",
    guard_orbiter_slot.slotId, "Kerbin", nil, nil, false)
local guard_launch_payload = mission_log.plan_add_objective(
    guard_plan.planId, "Launch", "Launch Guard Payload",
    guard_payload_slot.slotId, "Kerbin", nil, nil, false)
local guard_dock = mission_log.plan_add_objective(
    guard_plan.planId, "Dock", "Assemble the guard stack",
    guard_orbiter_slot.slotId, "Kerbin", "Orbiting", nil, false,
    guard_payload_slot.slotId)
local guard_complete = mission_log.plan_add_objective(
    guard_plan.planId, "Complete", "Complete the overarching guard mission",
    nil, nil, nil, nil, false)
mission_log.plan_activate(guard_plan.planId)

local guard_orbiter = mission_log.scenario_launch(
    "planner-guard-orbiter", "Guard Orbiter", "v-planner-guard-orbiter")
mission_log.plan_bind_vessel(
    guard_plan.planId,
    guard_orbiter_slot.slotId,
    guard_orbiter.missionId,
    "v-planner-guard-orbiter",
    guard_orbiter.events[1].recordedUtc)
mission_log.scenario_event(
    guard_orbiter.missionId,
    "launch",
    "Guard Orbiter lifted off",
    10,
    "Kerbin",
    "Flying",
    "planner-guard-orbiter-liftoff")
Test.wait.frames(1)

local guard_payload = mission_log.scenario_launch(
    "planner-guard-payload", "Guard Payload", "v-planner-guard-payload")
mission_log.plan_bind_vessel(
    guard_plan.planId,
    guard_payload_slot.slotId,
    guard_payload.missionId,
    "v-planner-guard-payload",
    guard_payload.events[1].recordedUtc)
mission_log.scenario_event(
    guard_payload.missionId,
    "launch",
    "Guard Payload lifted off",
    10,
    "Kerbin",
    "Flying",
    "planner-guard-payload-liftoff")
Test.wait.frames(1)

local guard_root = mission_log.scenario_dock(
    "v-planner-guard-orbiter",
    "v-planner-guard-payload",
    "v-planner-guard-stack",
    "Guard Mission Stack",
    "planner-guard-assemble",
    false,
    800,
    "Kerbin",
    "Orbiting")
Test.wait.frames(1)
guard_plan = mission_log.plan_recompute(guard_plan.planId)
expect_equal(guard_plan.status, "Active", "assembled guard plan should await root completion")
expect_equal(guard_plan.achievedCount, 3, "guard launches and docking should be achieved")
expect_equal(
    guard_plan.currentObjectiveId,
    guard_complete.objectiveId,
    "overarching completion should be current after assembly")

local completed_child = mission_log.scenario_split(
    "v-planner-guard-stack",
    "v-planner-guard-carrier-1",
    "v-planner-completed-child",
    "Completed Guard Sortie",
    "travel-planner-completed-child",
    "planner-guard-split-completed",
    1200,
    "Kerbin",
    "Orbiting")
Test.wait.frames(1)
mission_log.scenario_status("v-planner-completed-child", "Completed")
Test.wait.frames(1)

local lost_child = mission_log.scenario_split(
    "v-planner-guard-carrier-1",
    "v-planner-guard-carrier-2",
    "v-planner-lost-child",
    "Lost Guard Sortie",
    "travel-planner-lost-child",
    "planner-guard-split-lost",
    1600,
    "Kerbin",
    "Orbiting")
Test.wait.frames(1)
mission_log.scenario_status("v-planner-lost-child", "Lost")

local child_outcome_facts = mission_log.plan_facts(guard_plan.planId)
expect_equal(#child_outcome_facts, 7, "child outcomes should include completion and terminal loss facts")
local child_completion = fact_for_mission(
    child_outcome_facts, "Complete", completed_child.missionId)
expect_not_equal(child_completion, nil, "completed child should produce an inspectable completion fact")
expect_true(
    not child_completion.isPlanCompletion,
    "completed child must not be the overarching plan completion")
expect_equal(
    fact_for_mission(child_outcome_facts, "Complete", lost_child.missionId),
    nil,
    "lost child must not produce a plan-completion fact")
local child_loss = fact_for_mission(
    child_outcome_facts, "Custom", lost_child.missionId)
expect_not_equal(child_loss, nil, "lost child should produce an inspectable loss fact")
expect_true(child_loss.isTerminalLoss, "lost child fact should be marked as terminal loss")
expect_true(
    not child_loss.isPlanCompletion,
    "terminal loss must never be marked as plan completion")
expect_equal(
    mission_log.mission(completed_child.missionId).status,
    "Completed",
    "completed child should retain its branch outcome")
expect_equal(
    mission_log.mission(lost_child.missionId).status,
    "Lost",
    "lost child should retain its branch outcome")

guard_plan = mission_log.plan_recompute(guard_plan.planId)
expect_equal(guard_plan.status, "Active", "child outcomes must not close the overarching plan")
local guarded_completion = assert_objective_status(
    guard_plan, guard_complete.objectiveId, "Current", "guarded root completion")
expect_equal(
    guarded_completion.matchedFactId,
    "",
    "child completion must not satisfy the root completion objective")

mission_log.scenario_status("v-planner-guard-carrier-2", "Completed")
local guard_facts = mission_log.plan_facts(guard_plan.planId)
expect_equal(#guard_facts, 8, "root completion should add one final plan fact")
local root_completion = fact_for_mission(
    guard_facts, "Complete", guard_root.missionId)
expect_not_equal(root_completion, nil, "overarching root should produce a completion fact")
expect_true(root_completion.isPlanCompletion, "root completion should be marked as plan completion")
expect_true(
    not fact_for_mission(guard_facts, "Complete", completed_child.missionId).isPlanCompletion,
    "child completion should remain non-overarching after root completion")
guard_plan = mission_log.plan_recompute(guard_plan.planId)
expect_equal(guard_plan.status, "Completed", "root outcome should close the guard plan")
local achieved_root_completion = assert_objective_status(
    guard_plan, guard_complete.objectiveId, "Achieved", "overarching root completion")
expect_equal(
    achieved_root_completion.matchedFactId,
    root_completion.factId,
    "root objective should match the overarching completion fact")
assert_valid_tree("overarching completion guard", 4, 11)

-- A fifth plan exercises deliberate human resolution. Reordering is allowed
-- while drafting and remains available for an active plan; all resolutions
-- survive order changes and correction.
local manual_plan = mission_log.create_plan(
    scenario_campaign,
    "Manual Reconciliation Drill",
    "No flight facts are required; this plan exercises planner controls.")
local manual_launch = mission_log.plan_add_objective(
    manual_plan.planId, "Launch", "Launch the review vehicle",
    nil, "Kerbin", nil, nil, false)
local manual_custom = mission_log.plan_add_objective(
    manual_plan.planId, "Custom", "Photograph the launch tower",
    nil, nil, nil, nil, true)
local manual_orbit = mission_log.plan_add_objective(
    manual_plan.planId, "Orbit", "Confirm imported orbit record",
    nil, "Kerbin", "Orbiting", nil, false)
local manual_complete = mission_log.plan_add_objective(
    manual_plan.planId, "Complete", "Close the review drill",
    nil, nil, nil, nil, false)

mission_log.plan_reorder_objective(manual_plan.planId, manual_orbit.objectiveId, 1)
manual_plan = mission_log.plan(manual_plan.planId)
expect_equal(manual_plan.objectives[1].objectiveId, manual_launch.objectiveId, "draft launch order")
expect_equal(manual_plan.objectives[2].objectiveId, manual_orbit.objectiveId, "draft orbit reorder")
expect_equal(manual_plan.objectives[3].objectiveId, manual_custom.objectiveId, "draft custom reorder")
expect_equal(manual_plan.objectives[4].objectiveId, manual_complete.objectiveId, "draft completion order")

mission_log.plan_activate(manual_plan.planId)
mission_log.plan_mark_deviated(
    manual_plan.planId,
    manual_launch.objectiveId,
    "incorrect-launch-fact",
    "Initially linked to the wrong launch.")
mission_log.plan_skip_objective(
    manual_plan.planId,
    manual_custom.objectiveId,
    "Optional photograph was intentionally omitted.")
mission_log.plan_match_objective(
    manual_plan.planId,
    manual_orbit.objectiveId,
    "imported-orbit-fact",
    "Matched from a legacy mission entry.")
manual_plan = mission_log.plan_recompute(manual_plan.planId)
expect_equal(manual_plan.deviationCount, 1, "manual deviation should appear after reconciliation")
expect_equal(manual_plan.deviations[1].kind, "Manual", "manual deviation classification")
expect_true(manual_plan.deviations[1].manual, "manual deviation should be marked as human-authored")
assert_objective_status(
    manual_plan, manual_launch.objectiveId, "Deviated", "manually deviated launch")
assert_objective_status(
    manual_plan, manual_orbit.objectiveId, "Achieved", "manually matched orbit")
assert_objective_status(
    manual_plan, manual_custom.objectiveId, "Skipped", "manually skipped optional step")
expect_equal(
    manual_plan.currentObjectiveId,
    manual_complete.objectiveId,
    "completion should be current after the manual resolutions")

mission_log.plan_clear_resolution(manual_plan.planId, manual_launch.objectiveId)
mission_log.plan_match_objective(
    manual_plan.planId,
    manual_launch.objectiveId,
    "corrected-launch-fact",
    "Corrected to the intended vehicle launch.")
manual_plan = mission_log.plan_recompute(manual_plan.planId)
local corrected_launch = assert_objective_status(
    manual_plan, manual_launch.objectiveId, "Achieved", "corrected launch")
expect_equal(corrected_launch.matchedFactId, "corrected-launch-fact", "correction should replace the fact")
expect_true(corrected_launch.manual, "corrected launch should remain an explicit manual match")
expect_equal(manual_plan.deviationCount, 0, "correcting the launch should remove its manual deviation")

mission_log.plan_reorder_objective(manual_plan.planId, manual_complete.objectiveId, 2)
manual_plan = mission_log.plan_recompute(manual_plan.planId)
expect_equal(manual_plan.objectives[1].objectiveId, manual_launch.objectiveId, "active launch order")
expect_equal(manual_plan.objectives[2].objectiveId, manual_orbit.objectiveId, "active orbit order")
expect_equal(manual_plan.objectives[3].objectiveId, manual_complete.objectiveId, "active completion reorder")
expect_equal(manual_plan.objectives[4].objectiveId, manual_custom.objectiveId, "active optional-step reorder")
expect_equal(manual_plan.status, "Active", "manual drill should remain active until completion is resolved")
expect_equal(manual_plan.currentObjectiveId, manual_complete.objectiveId, "reordered completion should stay current")
assert_objective_status(
    manual_plan, manual_custom.objectiveId, "Skipped", "skipped step after active reorder")

-- Clearing a mistaken assignment must remove every historical alias before
-- the same observed mission can be assigned to the intended slot. This plan's
-- only objective is optional and pending, which must still keep it Active.
local binding_plan = mission_log.create_plan(
    scenario_campaign,
    "Vessel Binding Correction",
    "Correct a launch assigned to the wrong planned vessel slot.")
local mistaken_slot = mission_log.plan_add_vessel(
    binding_plan.planId, "Mistaken Slot", "Incorrect initial assignment", false)
local intended_slot = mission_log.plan_add_vessel(
    binding_plan.planId, "Survey Relay", "Intended relay assignment", true)
local pending_optional = mission_log.plan_add_objective(
    binding_plan.planId, "Custom", "Optional relay commissioning review",
    intended_slot.slotId, nil, nil, nil, true)
mission_log.plan_activate(binding_plan.planId)
binding_plan = mission_log.plan_recompute(binding_plan.planId)
expect_equal(binding_plan.status, "Active", "pending optional objective should keep its plan active")
expect_true(pending_optional.optional, "binding regression objective should be optional")
assert_objective_status(
    binding_plan, pending_optional.objectiveId, "Current", "pending optional objective")

local reassigned_mission = mission_log.scenario_launch(
    "planner-binding-reassignment", "Survey Relay", "v-planner-survey-relay")
mission_log.plan_bind_vessel(
    binding_plan.planId,
    mistaken_slot.slotId,
    reassigned_mission.missionId,
    "v-planner-survey-relay",
    reassigned_mission.events[1].recordedUtc)
binding_plan = mission_log.plan(binding_plan.planId)
local mistaken_bound = slot_by_id(binding_plan, mistaken_slot.slotId)
expect_equal(mistaken_bound.boundMissionId, reassigned_mission.missionId, "mistaken mission binding")
expect_equal(mistaken_bound.boundVesselId, "v-planner-survey-relay", "mistaken vessel binding")
expect_equal(#mistaken_bound.missionIds, 1, "mistaken slot should contain one mission alias")
expect_equal(#mistaken_bound.vesselIds, 1, "mistaken slot should contain one vessel alias")

mission_log.plan_clear_vessel_binding(binding_plan.planId, mistaken_slot.slotId)
binding_plan = mission_log.plan(binding_plan.planId)
local cleared_slot = slot_by_id(binding_plan, mistaken_slot.slotId)
expect_equal(cleared_slot.boundMissionId, "", "clearing should remove the bound mission")
expect_equal(cleared_slot.boundVesselId, "", "clearing should remove the bound vessel")
expect_equal(#cleared_slot.missionIds, 0, "clearing should remove every mission alias")
expect_equal(#cleared_slot.vesselIds, 0, "clearing should remove every vessel alias")

mission_log.plan_bind_vessel(
    binding_plan.planId,
    intended_slot.slotId,
    reassigned_mission.missionId,
    "v-planner-survey-relay",
    reassigned_mission.events[1].recordedUtc)
binding_plan = mission_log.plan_recompute(binding_plan.planId)
local intended_bound = slot_by_id(binding_plan, intended_slot.slotId)
expect_equal(
    intended_bound.boundMissionId,
    reassigned_mission.missionId,
    "cleared mission should be assignable to the intended slot")
expect_equal(
    intended_bound.boundVesselId,
    "v-planner-survey-relay",
    "cleared vessel should be assignable to the intended slot")
expect_equal(#intended_bound.missionIds, 1, "intended slot should receive one mission alias")
expect_equal(#intended_bound.vesselIds, 1, "intended slot should receive one vessel alias")
expect_equal(
    #slot_by_id(binding_plan, mistaken_slot.slotId).missionIds,
    0,
    "reassignment must not repopulate the cleared slot")
expect_equal(binding_plan.status, "Active", "unresolved optional objective should remain active")
assert_objective_status(
    binding_plan, pending_optional.objectiveId, "Current", "optional objective after reassignment")

-- Both the observed archive and planner sidecar must reload as one coherent
-- review state, without depending on object references retained by Lua.
mission_log.reload_archive()
expect_equal(mission_log.plan_count(), 6, "all six plans should reload from the isolated sidecar")
expect_equal(mission_log.archive_count(), 12, "all synthetic flights and mission trees should reload")
expect_equal(mission_log.plan_path(), plan_path, "planner reload should retain the isolated path")
assert_valid_tree("reloaded planner missions", 5, 12)

local reloaded_expedition = mission_log.plan(expedition.planId)
local reloaded_deviation = mission_log.plan(deviation_plan.planId)
local reloaded_recovery = mission_log.plan(recovery_plan.planId)
local reloaded_guard = mission_log.plan(guard_plan.planId)
local reloaded_manual = mission_log.plan(manual_plan.planId)
local reloaded_binding = mission_log.plan(binding_plan.planId)
expect_equal(reloaded_expedition.status, "Completed", "nominal plan status should persist")
expect_equal(reloaded_expedition.achievedCount, 11, "nominal plan progress should persist")
expect_equal(reloaded_expedition.deviationCount, 0, "nominal deviation state should persist")
expect_equal(
    slot_by_id(reloaded_expedition, carrier_slot.slotId).savedVehiclePath,
    "Workspaces/Mun/Gateway Carrier.json",
    "saved-craft path should persist")
expect_equal(
    slot_by_id(reloaded_expedition, lander_slot.slotId).boundMissionId,
    lander_launch.missionId,
    "launch binding should persist")
expect_equal(reloaded_deviation.status, "Completed", "deviated plan status should persist")
expect_equal(reloaded_deviation.deviationCount, 0, "completed deviation correction should persist")
local reloaded_corrected_orbit = objective_by_id(
    reloaded_deviation, deviation_objectives.orbit.objectiveId)
expect_equal(reloaded_corrected_orbit.status, "Achieved", "corrected orbit status should persist")
expect_true(reloaded_corrected_orbit.manual, "completed manual correction should persist")
expect_equal(reloaded_recovery.status, "Completed", "recovery plan status should persist")
expect_equal(reloaded_recovery.achievedCount, 3, "recovery plan progress should persist")
expect_equal(reloaded_recovery.deviationCount, 0, "recovery plan deviation state should persist")
expect_equal(reloaded_guard.status, "Completed", "overarching completion guard should persist")
expect_equal(
    objective_by_id(reloaded_guard, guard_complete.objectiveId).matchedFactId,
    root_completion.factId,
    "overarching root completion match should persist")
expect_equal(reloaded_manual.status, "Active", "manual plan status should persist")
expect_equal(reloaded_manual.deviationCount, 0, "manual correction should persist")
expect_equal(
    reloaded_manual.objectives[3].objectiveId,
    manual_complete.objectiveId,
    "active objective reorder should persist")
local persisted_correction = objective_by_id(reloaded_manual, manual_launch.objectiveId)
expect_equal(persisted_correction.status, "Achieved", "corrected manual status should persist")
expect_equal(
    persisted_correction.matchedFactId,
    "corrected-launch-fact",
    "corrected manual fact should persist")
expect_equal(reloaded_binding.status, "Active", "pending optional plan should persist as active")
expect_equal(
    #slot_by_id(reloaded_binding, mistaken_slot.slotId).missionIds,
    0,
    "cleared mission aliases should remain empty after reload")
expect_equal(
    slot_by_id(reloaded_binding, intended_slot.slotId).boundMissionId,
    reassigned_mission.missionId,
    "corrected binding should persist on the intended slot")
assert_objective_status(
    reloaded_binding, pending_optional.objectiveId, "Current", "reloaded pending optional objective")

Test.report.value("semanticAssertions", assertion_count)
Test.report.value("plannerPlans", mission_log.plan_count())
Test.report.value(
    "plannerFacts",
    #expedition_facts + #deviation_facts + #recovery_facts + #guard_facts)
Test.report.value("archiveMissions", mission_log.archive_count())
Test.report.value("plannerPath", plan_path)
Test.report.attach(plan_path)
Test.report.attach(archive_path)
Test.report.note(
    "Fixture-free planner coverage: participant-scoped topology, guarded child outcomes, situation and recovery facts, binding correction, optional progress, deviations, persistence, and condensed timeline expansion")
mission_log.end_test_session()
