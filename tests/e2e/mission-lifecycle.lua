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
Test.assert.greater(launched.maximumAltitude, 5, "mission should record peak altitude")
Test.assert.greater(launched.maximumSpeed, 0, "mission should record peak speed")

mission_log.complete_current("Completed")
Test.assert.equal(mission_log.archive_count(), 1, "completing should retain exactly one mission")
local completed = mission_log.latest()
Test.assert.equal(completed.status, "Completed", "manual completion should persist the outcome")

mission_log.reload_archive()
Test.assert.equal(mission_log.archive_count(), 1, "archive should survive an on-disk reload")
local reloaded = mission_log.latest()
Test.assert.equal(reloaded.missionId, completed.missionId, "reload should retain mission identity")
Test.assert.equal(reloaded.status, "Completed", "reload should retain mission status")

mission_log.open_window()
Test.wait.frames(5)
Test.assert.true_(mission_log.window_visible(), "archive window should be visible")
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
Test.report.attach(mission_log.archive_path())
Test.report.note("Verified mission creation, real launch capture, completion, disk reload, and archive UI")
mission_log.end_test_session()
