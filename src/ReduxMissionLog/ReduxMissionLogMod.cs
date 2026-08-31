using System;
using SpaceWarp2.API.Mods;
using UnityEngine;

namespace ReduxMissionLog
{
    public sealed class ReduxMissionLogMod : MonoBehaviourMod
    {
        private MissionTracker _tracker;
        private MissionPlanStore _planStore;
        private MissionPlanner _planner;
        private MissionPlanLaunchService _launchService;
        private MissionPlannerCoordinator _plannerCoordinator;
        private MissionLogWindow _window;
        private MissionLogTestApi _testApi;
        private MissionTopologyCoordinator _topology;
        private float _nextObservation;
        private float _nextTestRegistration;

        public override void OnInitialized()
        {
            var store = new MissionArchiveStore(
                message => SWLogger.LogInfo(message),
                message => SWLogger.LogError(message));
            _tracker = new MissionTracker(
                store,
                message => SWLogger.LogInfo(message),
                message => SWLogger.LogError(message));
            _planStore = new MissionPlanStore(
                message => SWLogger.LogInfo(message),
                message => SWLogger.LogError(message));
            _planner = new MissionPlanner(_planStore);
            _launchService = new MissionPlanLaunchService(
                message => SWLogger.LogInfo(message));
            _window = new MissionLogWindow(
                _tracker,
                _planner,
                _launchService,
                message => SWLogger.LogError(message),
                message => SWLogger.LogInfo(message));
            _plannerCoordinator = new MissionPlannerCoordinator(
                _planner,
                _tracker,
                _launchService,
                message => SWLogger.LogInfo(message));
            _topology = new MissionTopologyCoordinator(
                _tracker,
                message => SWLogger.LogInfo(message),
                message => SWLogger.LogError(message));
            _testApi = new MissionLogTestApi(
                _tracker,
                _planner,
                _planStore,
                _plannerCoordinator,
                _window,
                message => SWLogger.LogInfo(message));
            _testApi.TryRegister();
            SWLogger.LogInfo("Initialized. Press F7 to open the archive.");
        }

        private void Update()
        {
            if (_tracker == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _window.Toggle();
            }
            float now = Time.realtimeSinceStartup;
            if (_launchService != null)
            {
                _launchService.Update(now);
            }
            if (_topology != null)
            {
                _topology.Update(now);
            }
            if (now >= _nextObservation)
            {
                _nextObservation = now + 0.25f;
                _tracker.Observe(now);
                if (_plannerCoordinator != null)
                {
                    _plannerCoordinator.Observe(now);
                }
                _window.RefreshIfVisible();
            }
            if (_testApi != null && now >= _nextTestRegistration)
            {
                _nextTestRegistration = now + 1f;
                _testApi.TryRegister();
            }
        }

        private void OnDestroy()
        {
            if (_topology != null)
            {
                SafeCleanup(delegate { _topology.Dispose(); }, "topology coordinator");
                _topology = null;
            }
            if (_tracker != null)
            {
                SafeCleanup(delegate
                {
                    _tracker.Observe(Time.realtimeSinceStartup);
                    _tracker.Flush();
                }, "mission archive");
            }
            if (_launchService != null)
            {
                // Resolve an in-flight handoff while the coordinator is still
                // subscribed so the slot is persisted as needing review.
                SafeCleanup(delegate { _launchService.Dispose(); }, "launch handoff");
            }
            if (_plannerCoordinator != null)
            {
                SafeCleanup(
                    delegate { _plannerCoordinator.Observe(Time.realtimeSinceStartup); },
                    "planner reconciliation");
            }
            if (_planner != null)
            {
                SafeCleanup(delegate { _planner.SaveNow(); }, "mission plans");
            }
            if (_testApi != null)
            {
                SafeCleanup(delegate { _testApi.Dispose(); }, "test API");
                _testApi = null;
            }
            if (_window != null)
            {
                SafeCleanup(delegate { _window.Dispose(); }, "mission window");
                _window = null;
            }
            if (_plannerCoordinator != null)
            {
                SafeCleanup(
                    delegate { _plannerCoordinator.Dispose(); },
                    "planner coordinator");
                _plannerCoordinator = null;
            }
            if (_launchService != null)
            {
                _launchService = null;
            }
        }

        private void SafeCleanup(Action action, string component)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                SWLogger.LogError("Could not clean up " + component + ": " + error.Message);
            }
        }

    }
}
