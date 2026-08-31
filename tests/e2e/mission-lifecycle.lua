Test.name("Redux Mission Log lifecycle")
Test.report.fail_on_log_errors()

Test.assert.true_(Test.mod.is_loaded("ReduxMissionLog"), "ReduxMissionLog should be active")

local mission_log = Test.mod.extension("ReduxMissionLog")
Test.assert.not_equal(mission_log, nil, "ReduxMissionLog should expose its semantic test API")
mission_log.begin_test_session()

Test.game.load_save("local/launchpad-fly-safe-15")
Test.game.wait_for_state("Flight", 45)

local vessel = Test.flight.start("Fly Safe-15")
Test.assert.equal(vessel.situation, "PreLaunch", "fixture should begin on the launchpad")

Test.wait["until"](function()
    return mission_log.current() ~= nil
end, 10)

local started = mission_log.current()
Test.assert.equal(started.vessel, "Fly Safe-15", "mission should follow the active vessel")
Test.assert.equal(started.status, "Active", "new mission should be active")
Test.assert.greater(started.eventCount, 0, "new mission should contain its start event")

Test.flight.set_sas(true)
Test.flight.set_throttle(1.0)
Test.wait.frames(5)
Test.flight.stage()

Test.wait["until"](function()
    local active = Test.flight.active_vessel()
    return active ~= nil and active.situation ~= "PreLaunch" and active.altitude > 5
end, 45)
Test.wait["until"](function()
    return mission_log.current_has_event("launch")
end, 10)

local launched = mission_log.current()
Test.wait["until"](function()
    local active = mission_log.current()
    return active ~= nil and
        active.maximumAltitude > 20 and
        active.maximumSpeed > 1 and
        active.maximumGForce >= 1.05
end, 45)

launched = mission_log.current()
Test.assert.greater(launched.maximumAltitude, 20, "mission should record peak altitude")
Test.assert.greater(launched.maximumSpeed, 1, "mission should record peak speed")
Test.assert.greater(launched.maximumGForce, 1.04, "mission should record peak force")

local function kind_count(timeline, kind)
    local count = 0
    for _, item in ipairs(timeline) do
        if item.kind == kind then
            count = count + 1
        end
    end
    return count
end

local function find_kind(timeline, kind)
    for _, item in ipairs(timeline) do
        if item.kind == kind then
            return item
        end
    end
    return nil
end

local active_timeline = mission_log.mission_timeline(launched.missionId)
Test.assert.equal(kind_count(active_timeline, "launch"), 1, "launch should be one timeline moment")
Test.assert.equal(kind_count(active_timeline, "peak_altitude"), 1, "altitude record should be one timeline moment")
Test.assert.equal(kind_count(active_timeline, "peak_speed"), 1, "speed record should be one timeline moment")
Test.assert.equal(kind_count(active_timeline, "peak_g_force"), 1, "force record should be one timeline moment")
Test.assert.equal(kind_count(active_timeline, "landed"), 0, "pad departure must not create a false landing moment")
Test.assert.equal(kind_count(active_timeline, "splashed"), 0, "pad departure must not create a false splashdown moment")

local initial_altitude_record = find_kind(active_timeline, "peak_altitude")
Test.assert.not_equal(initial_altitude_record, nil, "altitude record should be inspectable")
Test.wait["until"](function()
    local candidate = find_kind(
        mission_log.mission_timeline(launched.missionId),
        "peak_altitude")
    return candidate ~= nil and
        candidate.eventId == initial_altitude_record.eventId and
        candidate.value > initial_altitude_record.value + 10 and
        candidate.recordedUtc > initial_altitude_record.recordedUtc
end, 20)

local advanced_altitude_record = find_kind(
    mission_log.mission_timeline(launched.missionId),
    "peak_altitude")
Test.assert.equal(
    advanced_altitude_record.eventId,
    initial_altitude_record.eventId,
    "a rolling record should update one event instead of appending duplicates")
Test.assert.greater(
    advanced_altitude_record.value,
    initial_altitude_record.value,
    "the rolling altitude record should advance during flight")
Test.assert.equal(advanced_altitude_record.body, "Kerbin", "rolling record should retain its body")
Test.assert.greater(#advanced_altitude_record.vesselIds, 0, "rolling record should retain its vessel identity")

-- No milestone or completion forces this save: the normal five-second cadence must persist it.
Test.wait.seconds(5.5)
mission_log.reload_archive()
local cadence_saved_record = find_kind(
    mission_log.mission_timeline(launched.missionId),
    "peak_altitude")
Test.assert.not_equal(cadence_saved_record, nil, "cadence-saved altitude record should reload")
Test.assert.equal(
    cadence_saved_record.eventId,
    initial_altitude_record.eventId,
    "cadence save should preserve the rolling event identity")
Test.assert.true_(
    cadence_saved_record.value >= advanced_altitude_record.value,
    "cadence save should retain the advanced altitude maximum")
Test.assert.true_(
    cadence_saved_record.recordedUtc >= advanced_altitude_record.recordedUtc,
    "cadence save should retain the advanced record time")
Test.assert.true_(
    cadence_saved_record.flightTime >= advanced_altitude_record.flightTime,
    "cadence save should retain the advanced flight clock")

mission_log.complete_current("Completed")
Test.assert.equal(mission_log.archive_count(), 1, "completing should retain exactly one mission")
local completed = mission_log.latest()
Test.assert.equal(completed.status, "Completed", "manual completion should persist the outcome")

mission_log.reload_archive()
Test.assert.equal(mission_log.archive_count(), 1, "archive should survive an on-disk reload")
local reloaded = mission_log.latest()
Test.assert.equal(reloaded.missionId, completed.missionId, "reload should retain mission identity")
Test.assert.equal(reloaded.status, "Completed", "reload should retain mission status")

local reloaded_timeline = mission_log.mission_timeline(reloaded.missionId)
Test.assert.equal(kind_count(reloaded_timeline, "launch"), 1, "reload should retain the launch moment")
Test.assert.equal(kind_count(reloaded_timeline, "peak_altitude"), 1, "reload should retain one altitude record")
Test.assert.equal(kind_count(reloaded_timeline, "peak_speed"), 1, "reload should retain one speed record")
Test.assert.equal(kind_count(reloaded_timeline, "peak_g_force"), 1, "reload should retain one force record")
Test.assert.equal(kind_count(reloaded_timeline, "mission_completed"), 1, "completion should close the story")
for index = 2, #reloaded_timeline do
    Test.assert.true_(
        reloaded_timeline[index - 1].recordedUtc <= reloaded_timeline[index].recordedUtc,
        "reloaded mission story should remain chronological at row " .. index)
end

mission_log.open_mission(reloaded.missionId)
Test.wait.frames(5)
Test.assert.true_(mission_log.window_visible(), "archive window should be visible")
Test.assert.equal(
    mission_log.selected_mission_id(),
    reloaded.missionId,
    "story view should open the completed mission")
Test.assert.equal(
    mission_log.rendered_timeline_count(),
    #reloaded_timeline,
    "story view should render the same timeline projection tested semantically")
Test.assert.equal(
    mission_log.ui_stack(),
    "UitkForKsp2.Controls.AppShell",
    "archive window should use Redux's KSP 2 AppShell")
Test.capture.screenshot("mission-log-debrief", {
    scale = 1,
    hideUI = false,
    waitFrames = 2
})

Test.report.value("missionId", reloaded.missionId)
Test.report.value("archivePath", mission_log.archive_path())
Test.report.metric("maximumAltitude", reloaded.maximumAltitude)
Test.report.metric("maximumSpeed", reloaded.maximumSpeed)
Test.report.metric("maximumGForce", reloaded.maximumGForce)
Test.report.attach(mission_log.archive_path())
Test.report.note("Verified mission creation, real launch and record moments, completion, disk reload, and timeline-first story UI")
mission_log.end_test_session()
