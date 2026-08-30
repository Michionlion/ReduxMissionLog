using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Messages;
using KSP.Sim.impl;
using KSP.Sim.State;
using UnityEngine;

namespace ReduxMissionLog
{
    internal sealed class MissionTopologyCoordinator : IDisposable
    {
        private readonly MissionTracker _tracker;
        private readonly Action<string> _info;
        private readonly Action<string> _error;
        private readonly List<PendingDestruction> _pendingDestructions =
            new List<PendingDestruction>();
        private readonly List<PendingSplit> _pendingSplits = new List<PendingSplit>();
        private readonly Dictionary<string, float> _recentlyAdded =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _recentlyRemoved =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private MessageCenter _messages;
        private UniverseModel _universe;
        private string _campaignId;
        private VesselComponent _lastAddedVessel;
        private float _lastAddedRealtime;
        private SubscriptionHandle _docked;
        private SubscriptionHandle _split;
        private SubscriptionHandle _undocked;
        private SubscriptionHandle _recovered;
        private SubscriptionHandle _destroyed;
        private bool _subscribed;
        private bool _hasDocked;
        private bool _hasSplit;
        private bool _hasUndocked;
        private bool _hasRecovered;
        private bool _hasDestroyed;
        private bool _hasUniverseEvents;
        private bool _subscriptionFailureLogged;

        public MissionTopologyCoordinator(
            MissionTracker tracker,
            Action<string> info,
            Action<string> error)
        {
            _tracker = tracker;
            _info = info;
            _error = error;
        }

        public void Update(float realtime)
        {
            try
            {
                EnsureSubscriptions();
                _subscriptionFailureLogged = false;
            }
            catch (Exception error)
            {
                if (!_subscriptionFailureLogged)
                {
                    _subscriptionFailureLogged = true;
                    _error("Mission topology subscriptions are unavailable: " + error.Message);
                }
                return;
            }
            ResolvePendingSplits(realtime, false);
            ResolvePendingDestructions(realtime);
            PruneJournal(realtime);
        }

        public void Dispose()
        {
            ReleaseSubscriptions();
            _pendingDestructions.Clear();
            _pendingSplits.Clear();
            _recentlyAdded.Clear();
            _recentlyRemoved.Clear();
        }

        private void EnsureSubscriptions()
        {
            GameInstance game = CurrentGame();
            MessageCenter messages = game == null ? null : game.Messages;
            UniverseModel universe = game == null ? null : game.UniverseModel;
            if (messages == null || universe == null)
            {
                if (_messages != null || _universe != null || _subscribed)
                {
                    ReleaseSubscriptions();
                    ClearTransactionState();
                }
                return;
            }
            string campaignId = game.SessionGuidString;
            if (_subscribed && ReferenceEquals(messages, _messages) &&
                ReferenceEquals(universe, _universe) &&
                string.Equals(campaignId, _campaignId, StringComparison.Ordinal))
            {
                return;
            }

            ReleaseSubscriptions();
            ClearTransactionState();
            _messages = messages;
            _universe = universe;
            _campaignId = campaignId;
            try
            {
                _docked = messages.Subscribe<VesselDockedMessage>(OnVesselDocked);
                _hasDocked = true;
                _split = messages.Subscribe<VesselSplitMessage>(OnVesselSplit);
                _hasSplit = true;
                _undocked = messages.Subscribe<VesselUndockedMessage>(OnVesselUndocked);
                _hasUndocked = true;
                _recovered = messages.Subscribe<VesselRecoveredMessage>(OnVesselRecovered);
                _hasRecovered = true;
                _destroyed = messages.Subscribe<VesselDestroyedMessage>(OnVesselDestroyed);
                _hasDestroyed = true;
                universe.onVesselAdded += OnVesselAdded;
                universe.onVesselRemoved += OnVesselRemoved;
                _hasUniverseEvents = true;
                _subscribed = true;
            }
            catch
            {
                ReleaseSubscriptions();
                ClearTransactionState();
                throw;
            }
        }

        private void ReleaseSubscriptions()
        {
            if (_hasDocked)
            {
                _docked.Release();
            }
            if (_hasSplit)
            {
                _split.Release();
            }
            if (_hasUndocked)
            {
                _undocked.Release();
            }
            if (_hasRecovered)
            {
                _recovered.Release();
            }
            if (_hasDestroyed)
            {
                _destroyed.Release();
            }
            if (_hasUniverseEvents && _universe != null)
            {
                _universe.onVesselAdded -= OnVesselAdded;
                _universe.onVesselRemoved -= OnVesselRemoved;
            }
            _docked = default(SubscriptionHandle);
            _split = default(SubscriptionHandle);
            _undocked = default(SubscriptionHandle);
            _recovered = default(SubscriptionHandle);
            _destroyed = default(SubscriptionHandle);
            _messages = null;
            _universe = null;
            _campaignId = null;
            _subscribed = false;
            _hasDocked = false;
            _hasSplit = false;
            _hasUndocked = false;
            _hasRecovered = false;
            _hasDestroyed = false;
            _hasUniverseEvents = false;
        }

        private void OnVesselDocked(MessageCenterMessage raw)
        {
            var message = raw as VesselDockedMessage;
            string operationId = "dock-" + (_campaignId ?? "unknown-campaign") + "-" +
                (message == null ? "unknown" : message.SentOn.ToString());
            var reviewIds = new List<string>();
            VesselComponent combined = message == null
                ? null
                : (message.VesselOne == null ? null : message.VesselOne.Model) ??
                  (message.VesselTwo == null ? null : message.VesselTwo.Model);
            if (combined == null && _lastAddedVessel != null &&
                Time.realtimeSinceStartup - _lastAddedRealtime <= 2f &&
                IsCorrelatedDockCandidate(_lastAddedVessel))
            {
                combined = _lastAddedVessel;
                _info("Docking lineage recovered the combined vessel from the lifecycle journal.");
            }
            if (combined == null)
            {
                reviewIds = QuarantineUnresolvedDockDestructions(Time.frameCount);
                _tracker.MarkLineageNeedsReview(
                    reviewIds,
                    "Docking was observed, but KSP did not expose the combined vessel.",
                    operationId + "-unresolved");
                _error("Docking lineage needs review: KSP did not expose the combined vessel.");
                return;
            }

            try
            {
                var state = (VesselState)combined.GetState();
                List<VesselComponent.SubVesselData> parents = state.SubVessels;
                if (parents == null || parents.Count != 2)
                {
                    throw new InvalidOperationException(
                        "the combined vessel did not expose exactly two direct parent records");
                }
                VesselComponent.SubVesselData left = parents[0];
                VesselComponent.SubVesselData right = parents[1];
                AddUnique(reviewIds, left.VesselId.ToString());
                AddUnique(reviewIds, right.VesselId.ToString());
                ConsumeDestruction(left.VesselId.ToString());
                ConsumeDestruction(right.VesselId.ToString());

                string resultId = combined.GlobalId.ToString();
                bool journalConfirmed = _recentlyAdded.ContainsKey(resultId) &&
                    _recentlyRemoved.ContainsKey(left.VesselId.ToString()) &&
                    _recentlyRemoved.ContainsKey(right.VesselId.ToString());
                if (!journalConfirmed)
                {
                    _info("Docking lineage used KSP SubVessels directly; the lifecycle journal was incomplete.");
                }
                _tracker.HandleDocking(
                    CurrentGame(),
                    combined,
                    parents,
                    operationId + "-" + resultId);
            }
            catch (Exception error)
            {
                if (reviewIds.Count == 0)
                {
                    reviewIds = QuarantineUnresolvedDockDestructions(Time.frameCount);
                }
                _tracker.MarkLineageNeedsReview(
                    reviewIds,
                    "Docking was observed, but its parent missions could not be resolved automatically.",
                    operationId + "-unresolved");
                _error("Docking lineage needs review: " + error.Message);
            }
        }

        private void OnVesselSplit(MessageCenterMessage raw)
        {
            var message = raw as VesselSplitMessage;
            if (message == null || message.remainingVessel == null || message.newVessel == null)
            {
                return;
            }
            try
            {
                _pendingSplits.Add(new PendingSplit
                {
                    Remaining = message.remainingVessel,
                    Detached = message.newVessel,
                    RemainingId = message.remainingVessel.GlobalId.ToString(),
                    DetachedId = message.newVessel.GlobalId.ToString(),
                    OperationId = "split-" + (_campaignId ?? "unknown-campaign") + "-" +
                        message.SentOn + "-" +
                        message.newVessel.GlobalId,
                    RestoredSubVessel = message.isNewVesselFromSubVessel,
                    ResolveAfter = Time.realtimeSinceStartup
                });
            }
            catch (Exception error)
            {
                _error("Split lineage needs review: " + error.Message);
            }
        }

        private void OnVesselUndocked(MessageCenterMessage raw)
        {
            var message = raw as VesselUndockedMessage;
            if (message != null)
            {
                ResolvePendingSplits(Time.realtimeSinceStartup, true);
                _info("Confirmed vessel undocking after split reconciliation.");
            }
        }

        private void OnVesselRecovered(MessageCenterMessage raw)
        {
            var message = raw as VesselRecoveredMessage;
            if (message == null)
            {
                return;
            }
            string vesselId = message.VesselID.ToString();
            ConsumeDestruction(vesselId);
            _tracker.CompleteVessel(vesselId, "Recovered");
        }

        private void OnVesselDestroyed(MessageCenterMessage raw)
        {
            var message = raw as VesselDestroyedMessage;
            if (message == null)
            {
                return;
            }
            string vesselId = message.Vessel == null
                ? message.Guid.ToString()
                : message.Vessel.GlobalId.ToString();
            GameInstance game = CurrentGame();
            _pendingDestructions.Add(new PendingDestruction
            {
                VesselId = vesselId,
                CampaignId = game == null ? null : game.SessionGuidString,
                OccurredAt = Time.realtimeSinceStartup,
                OccurredFrame = Time.frameCount,
                ResolveAfter = Time.realtimeSinceStartup + 0.75f
            });
        }

        private void OnVesselAdded(VesselComponent vessel)
        {
            if (vessel != null)
            {
                _recentlyAdded[vessel.GlobalId.ToString()] = Time.realtimeSinceStartup;
                _lastAddedVessel = vessel;
                _lastAddedRealtime = Time.realtimeSinceStartup;
            }
        }

        private void OnVesselRemoved(VesselComponent vessel)
        {
            if (vessel != null)
            {
                _recentlyRemoved[vessel.GlobalId.ToString()] = Time.realtimeSinceStartup;
            }
        }

        private void ResolvePendingDestructions(float realtime)
        {
            for (int index = _pendingDestructions.Count - 1; index >= 0; index--)
            {
                PendingDestruction pending = _pendingDestructions[index];
                if (realtime < pending.ResolveAfter)
                {
                    continue;
                }
                _pendingDestructions.RemoveAt(index);
                GameInstance game = CurrentGame();
                if (!IsSameFlight(game, pending.CampaignId))
                {
                    continue;
                }
                _tracker.CompleteVessel(pending.VesselId, "Lost");
            }
        }

        private void ResolvePendingSplits(float realtime, bool force)
        {
            for (int index = _pendingSplits.Count - 1; index >= 0; index--)
            {
                PendingSplit pending = _pendingSplits[index];
                if (!force && realtime <= pending.ResolveAfter)
                {
                    continue;
                }
                _pendingSplits.RemoveAt(index);
                try
                {
                    if (pending.Remaining == null || pending.Detached == null)
                    {
                        throw new InvalidOperationException("a split vessel was no longer available");
                    }
                    _tracker.HandleSplit(
                        CurrentGame(),
                        pending.Remaining,
                        pending.Detached,
                        pending.OperationId,
                        pending.RestoredSubVessel);
                }
                catch (Exception error)
                {
                    _tracker.MarkLineageNeedsReview(
                        new[] { pending.RemainingId, pending.DetachedId },
                        "A vessel split was observed, but its mission branches could not be resolved automatically.",
                        pending.OperationId + "-unresolved");
                    _error("Split lineage needs review: " + error.Message);
                }
            }
        }

        private void ConsumeDestruction(string vesselId)
        {
            for (int index = _pendingDestructions.Count - 1; index >= 0; index--)
            {
                if (string.Equals(_pendingDestructions[index].VesselId, vesselId,
                    StringComparison.Ordinal))
                {
                    _pendingDestructions.RemoveAt(index);
                }
            }
        }

        private void PruneJournal(float realtime)
        {
            Prune(_recentlyAdded, realtime);
            Prune(_recentlyRemoved, realtime);
            if (_lastAddedVessel != null && realtime - _lastAddedRealtime > 5f)
            {
                _lastAddedVessel = null;
            }
        }

        private List<string> QuarantineUnresolvedDockDestructions(int frame)
        {
            var result = new List<string>();
            for (int index = _pendingDestructions.Count - 1; index >= 0; index--)
            {
                PendingDestruction pending = _pendingDestructions[index];
                int frameDelta = frame - pending.OccurredFrame;
                if (frameDelta >= 0 && frameDelta <= 1)
                {
                    AddUnique(result, pending.VesselId);
                    _pendingDestructions.RemoveAt(index);
                    if (result.Count == 2)
                    {
                        break;
                    }
                }
            }
            return result;
        }

        private bool IsCorrelatedDockCandidate(VesselComponent candidate)
        {
            if (candidate == null ||
                !_recentlyAdded.ContainsKey(candidate.GlobalId.ToString()))
            {
                return false;
            }
            try
            {
                var state = (VesselState)candidate.GetState();
                List<VesselComponent.SubVesselData> parents = state.SubVessels;
                return parents != null && parents.Count == 2 &&
                    _recentlyRemoved.ContainsKey(parents[0].VesselId.ToString()) &&
                    _recentlyRemoved.ContainsKey(parents[1].VesselId.ToString());
            }
            catch
            {
                return false;
            }
        }

        private void ClearTransactionState()
        {
            _pendingDestructions.Clear();
            _pendingSplits.Clear();
            _recentlyAdded.Clear();
            _recentlyRemoved.Clear();
            _lastAddedVessel = null;
            _lastAddedRealtime = 0f;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
            {
                values.Add(value);
            }
        }

        private static void Prune(Dictionary<string, float> journal, float realtime)
        {
            var expired = new List<string>();
            foreach (KeyValuePair<string, float> pair in journal)
            {
                if (realtime - pair.Value > 5f)
                {
                    expired.Add(pair.Key);
                }
            }
            for (int index = 0; index < expired.Count; index++)
            {
                journal.Remove(expired[index]);
            }
        }

        private static GameInstance CurrentGame()
        {
            return GameManager.Instance == null ? null : GameManager.Instance.Game;
        }

        private static bool IsSameFlight(GameInstance game, string campaignId)
        {
            if (game == null || game.GlobalGameState == null ||
                !string.Equals(game.SessionGuidString, campaignId, StringComparison.Ordinal))
            {
                return false;
            }
            GameState state = game.GlobalGameState.GetGameState().GameState;
            return state == GameState.FlightView || state == GameState.Map3DView;
        }

        private sealed class PendingDestruction
        {
            public string VesselId;
            public string CampaignId;
            public float OccurredAt;
            public int OccurredFrame;
            public float ResolveAfter;
        }

        private sealed class PendingSplit
        {
            public VesselComponent Remaining;
            public VesselComponent Detached;
            public string RemainingId;
            public string DetachedId;
            public string OperationId;
            public bool RestoredSubVessel;
            public float ResolveAfter;
        }
    }
}
