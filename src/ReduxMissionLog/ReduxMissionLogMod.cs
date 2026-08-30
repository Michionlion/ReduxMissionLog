using SpaceWarp2.API.Mods;
using KSP.Game;
using KSP.Messages;
using UnityEngine;

namespace ReduxMissionLog
{
    public sealed class ReduxMissionLogMod : MonoBehaviourMod
    {
        private MissionTracker _tracker;
        private MissionLogWindow _window;
        private MissionLogTestApi _testApi;
        private float _nextObservation;
        private float _nextTestRegistration;
        private MessageCenter _messageCenter;
        private SubscriptionHandle _recoveredSubscription;
        private SubscriptionHandle _destroyedSubscription;
        private bool _messagesSubscribed;

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
            if (now >= _nextObservation)
            {
                _nextObservation = now + 0.25f;
                _tracker.Observe(now);
            }
            if (_testApi != null && now >= _nextTestRegistration)
            {
                _nextTestRegistration = now + 1f;
                _testApi.TryRegister();
                EnsureMessageSubscriptions();
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
            ReleaseMessageSubscriptions();
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

        private void EnsureMessageSubscriptions()
        {
            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            MessageCenter current = game == null ? null : game.Messages;
            if (current == null || ReferenceEquals(current, _messageCenter))
            {
                return;
            }
            ReleaseMessageSubscriptions();
            _messageCenter = current;
            _recoveredSubscription = current.Subscribe<VesselRecoveredMessage>(OnVesselRecovered);
            _destroyedSubscription = current.Subscribe<VesselDestroyedMessage>(OnVesselDestroyed);
            _messagesSubscribed = true;
        }

        private void ReleaseMessageSubscriptions()
        {
            if (_messagesSubscribed)
            {
                _recoveredSubscription.Release();
                _destroyedSubscription.Release();
                _recoveredSubscription = default(SubscriptionHandle);
                _destroyedSubscription = default(SubscriptionHandle);
                _messagesSubscribed = false;
            }
            _messageCenter = null;
        }

        private void OnVesselRecovered(MessageCenterMessage raw)
        {
            var message = raw as VesselRecoveredMessage;
            if (message != null && _tracker != null)
            {
                _tracker.CompleteVessel(message.VesselID.ToString(), "Recovered");
            }
        }

        private void OnVesselDestroyed(MessageCenterMessage raw)
        {
            var message = raw as VesselDestroyedMessage;
            if (message != null && _tracker != null)
            {
                string vesselId = message.Vessel == null
                    ? message.Guid.ToString()
                    : message.Vessel.GlobalId.ToString();
                _tracker.CompleteVessel(vesselId, "Lost");
            }
        }
    }
}
