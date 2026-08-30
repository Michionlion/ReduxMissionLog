using SpaceWarp2.API.Mods;
using UnityEngine;

namespace ReduxMissionLog
{
    public sealed class ReduxMissionLogMod : MonoBehaviourMod
    {
        private MissionTracker _tracker;
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
            _window = new MissionLogWindow(_tracker);
            _topology = new MissionTopologyCoordinator(
                _tracker,
                message => SWLogger.LogInfo(message),
                message => SWLogger.LogError(message));
            _testApi = new MissionLogTestApi(
                _tracker,
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
            if (_topology != null)
            {
                _topology.Update(now);
            }
            if (now >= _nextObservation)
            {
                _nextObservation = now + 0.25f;
                _tracker.Observe(now);
            }
            if (_testApi != null && now >= _nextTestRegistration)
            {
                _nextTestRegistration = now + 1f;
                _testApi.TryRegister();
            }
        }

        private void OnGUI()
        {
            if (_window != null)
            {
                _window.Draw();
            }
        }

        private void OnDestroy()
        {
            if (_topology != null)
            {
                _topology.Dispose();
                _topology = null;
            }
            if (_tracker != null)
            {
                _tracker.Observe(Time.realtimeSinceStartup);
                _tracker.Flush();
            }
            if (_testApi != null)
            {
                _testApi.Dispose();
                _testApi = null;
            }
        }

    }
}
