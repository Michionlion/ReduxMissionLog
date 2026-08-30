using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Sim.impl;

namespace ReduxMissionLog
{
    internal sealed class MissionTracker
    {
        private const string ScenarioCampaignId = "redux-mission-log-scenarios";
        private readonly MissionArchiveStore _store;
        private readonly Action<string> _info;
        private readonly Action<string> _error;
        private MissionArchive _archive;
        private MissionLineageResolver _lineage;
        private string _activeCampaignId;
        private string _activeVesselId;
        private string _activeTravelObjectId;
        private string _activeVesselName;
        private float _lastSavedRealtime;

        public MissionTracker(MissionArchiveStore store, Action<string> info, Action<string> error)
        {
            _store = store;
            _info = info;
            _error = error;
            ReplaceArchive(_store.Load());
        }

        public MissionArchive Archive { get { return _archive; } }
        public string ArchivePath { get { return _store.ArchivePath; } }
        public bool HasActiveVessel { get { return !string.IsNullOrWhiteSpace(_activeVesselId); } }
        public string ActiveVesselId { get { return _activeVesselId; } }

        public void Observe(float realtime)
        {
            try
            {
                GameInstance game = CurrentGame();
                if (!IsFlightReady(game))
                {
                    ClearActiveSelection();
                    return;
                }
                VesselComponent vessel = game.ViewController.GetActiveSimVessel(true);
                if (vessel == null || !vessel.IsValidInSim)
                {
                    ClearActiveSelection();
                    return;
                }

                string campaignId = game.SessionGuidString;
                string vesselId = SafeVesselId(vessel);
                string travelObjectId = SafeTravelObjectId(vessel);
                RememberActive(campaignId, vessel);
                MissionRecord mission = _lineage.FindTrackedVessel(campaignId, vesselId);
                if (mission == null && !string.IsNullOrWhiteSpace(travelObjectId))
                {
                    MissionRecord travelAlias = _lineage.FindTravelAlias(
                        campaignId, travelObjectId);
                    if (travelAlias != null && travelAlias.IsActive)
                    {
                        if (travelAlias.TrackedVesselIds.Count == 0 ||
                            (travelAlias.TrackedVesselIds.Count == 1 &&
                             string.Equals(travelAlias.TrackedVesselIds[0], vesselId,
                                StringComparison.Ordinal)))
                        {
                            _lineage.RebindTravelIdentity(
                                travelAlias,
                                campaignId,
                                vesselId,
                                travelObjectId,
                                SafeVesselName(vessel),
                                Moment(vessel),
                                "observe-identity-" + campaignId + "-" +
                                    travelAlias.MissionId + "-" + vesselId);
                            mission = travelAlias;
                        }
                        else
                        {
                            _lineage.MarkNeedsReview(
                                travelAlias,
                                "Another live craft reported this mission's travel identity.",
                                Moment(vessel),
                                "observe-identity-conflict-" + campaignId + "-" + vesselId,
                                new[] { travelAlias.TrackedVesselIds[0], vesselId });
                            Save(realtime);
                            return;
                        }
                        Save(realtime);
                    }
                }
                if (mission == null &&
                    !IsCompletedFlightContinuation(campaignId, vesselId, vessel.TimeSinceLaunch))
                {
                    mission = CreateFlightMission(game, vessel, campaignId, vesselId, false);
                    Save(realtime);
                }
                if (mission == null)
                {
                    return;
                }

                bool changed = UpdateMission(mission, game, vessel);
                if (vessel.IsVesselBeingRecovered)
                {
                    Complete(mission, "Recovered", vessel.TimeSinceLaunch);
                    changed = true;
                }
                if (changed || realtime - _lastSavedRealtime >= 5f)
                {
                    Save(realtime);
                }
            }
            catch (Exception error)
            {
                _error("Mission observation failed safely: " + error.Message);
            }
        }

        public MissionRecord GetCurrent()
        {
            return string.IsNullOrWhiteSpace(_activeVesselId)
                ? null
                : _lineage.FindTrackedVessel(_activeCampaignId, _activeVesselId);
        }

        public MissionRecord GetLatest()
        {
            return _archive.Missions.Count == 0
                ? null
                : _archive.Missions[_archive.Missions.Count - 1];
        }

        public MissionRecord FindById(string missionId) { return _lineage.FindById(missionId); }
        public List<MissionRecord> GetRoots() { return _lineage.GetRoots(null); }
        public List<MissionRecord> GetChildren(MissionRecord parent) { return _lineage.GetChildren(parent); }
        public MissionRecord GetParent(MissionRecord mission) { return _lineage.GetParent(mission); }
        public MissionAggregate GetAggregate(MissionRecord mission) { return _lineage.Aggregate(mission); }
        public List<string> ValidateTree() { return _lineage.Validate(); }

        public bool CanTrackCurrentAs(MissionRecord mission)
        {
            if (mission == null || string.IsNullOrWhiteSpace(_activeVesselId) ||
                !string.Equals(mission.CampaignId, _activeCampaignId, StringComparison.Ordinal))
            {
                return false;
            }
            return mission.TrackedVesselIds.Count == 0 ||
                (mission.TrackedVesselIds.Count == 1 &&
                 string.Equals(mission.TrackedVesselIds[0], _activeVesselId,
                    StringComparison.Ordinal));
        }

        public bool CurrentHasEvent(string kind)
        {
            MissionRecord mission = GetCurrent();
            return mission != null && HasEvent(mission, kind);
        }

        public void CompleteCurrent(string status)
        {
            MissionRecord mission = GetCurrent();
            if (mission == null)
            {
                throw new InvalidOperationException("There is no active mission to complete.");
            }
            CompleteMission(mission, status);
        }

        public void CompleteMission(MissionRecord mission, string status)
        {
            if (mission == null || !_archive.Missions.Contains(mission) || !mission.IsActive)
            {
                throw new InvalidOperationException("The selected mission is not active.");
            }
            Complete(mission,
                string.IsNullOrWhiteSpace(status) ? "Completed" : status.Trim(),
                mission.FlightDurationSeconds);
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void CompleteVessel(string vesselId, string status)
        {
            if (string.IsNullOrWhiteSpace(vesselId))
            {
                return;
            }
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (mission.IsActive && Contains(mission.TrackedVesselIds, vesselId))
                {
                    Complete(mission, status, mission.FlightDurationSeconds);
                    Save(UnityEngine.Time.realtimeSinceStartup);
                    return;
                }
            }
        }

        public void HandleDocking(
            GameInstance game,
            VesselComponent combined,
            IList<VesselComponent.SubVesselData> directParents,
            string operationId)
        {
            if (game == null || combined == null || directParents == null ||
                directParents.Count < 2 || string.IsNullOrWhiteSpace(game.SessionGuidString))
            {
                throw new InvalidOperationException(
                    "Docking topology did not contain a combined vessel and two direct parents.");
            }
            string campaignId = game.SessionGuidString;
            VesselComponent.SubVesselData leftData = directParents[directParents.Count - 2];
            VesselComponent.SubVesselData rightData = directParents[directParents.Count - 1];
            string leftId = leftData.VesselId.ToString();
            string rightId = rightData.VesselId.ToString();
            EnsureDockingParticipant(game, combined, leftId,
                leftData.TravelObjectId.ToString(), leftData.VesselName, operationId);
            EnsureDockingParticipant(game, combined, rightId,
                rightData.TravelObjectId.ToString(), rightData.VesselName, operationId);

            MissionRecord result = _lineage.Dock(
                campaignId,
                leftId,
                rightId,
                SafeVesselId(combined),
                SafeTravelObjectId(combined),
                SafeVesselName(combined),
                Moment(combined),
                operationId,
                false);
            CaptureCrew(game, combined, result.Crew);
            RememberActive(campaignId, combined);
            Save(UnityEngine.Time.realtimeSinceStartup);
            _info("Resolved docking into mission tree '" + result.Title + "'.");
        }

        public MissionRecord HandleSplit(
            GameInstance game,
            VesselComponent remaining,
            VesselComponent detached,
            string operationId,
            bool restoredSubVessel)
        {
            if (game == null || remaining == null || detached == null ||
                string.IsNullOrWhiteSpace(game.SessionGuidString))
            {
                return null;
            }
            if (!IsMissionWorthy(detached, game))
            {
                _info("Ignored a non-controllable split vessel: " + SafeVesselName(detached) + ".");
                return null;
            }
            var crew = new List<string>();
            CaptureCrew(game, detached, crew);
            MissionRecord result = _lineage.Split(
                game.SessionGuidString,
                SafeVesselId(remaining),
                SafeVesselId(remaining),
                SafeTravelObjectId(remaining),
                SafeVesselName(remaining),
                SafeVesselId(detached),
                SafeTravelObjectId(detached),
                SafeVesselName(detached),
                Moment(detached),
                operationId,
                crew);
            Save(UnityEngine.Time.realtimeSinceStartup);
            _info((restoredSubVessel ? "Resumed" : "Created") +
                " sub-mission '" + result.Title + "'.");
            return result;
        }

        public void ManualCombineCurrentWith(MissionRecord selected)
        {
            MissionRecord current = GetCurrent();
            if (current == null || selected == null || ReferenceEquals(current, selected) ||
                !selected.IsActive || selected.TrackedVesselIds.Count != 1)
            {
                throw new InvalidOperationException(
                    "Select a different active mission to combine with the current vessel.");
            }
            MissionRecord result = _lineage.Dock(
                current.CampaignId,
                current.TrackedVesselIds[0],
                selected.TrackedVesselIds[0],
                _activeVesselId,
                _activeTravelObjectId,
                _activeVesselName,
                CurrentMoment(),
                "manual-combine-" + Guid.NewGuid().ToString("N"),
                true);
            Save(UnityEngine.Time.realtimeSinceStartup);
            _info("Manually combined missions under '" + result.Title + "'.");
        }

        public void ManualAdoptCurrentUnder(MissionRecord parent)
        {
            MissionRecord current = GetCurrent();
            if (current == null || parent == null)
            {
                throw new InvalidOperationException("A current mission and parent mission are required.");
            }
            _lineage.Reparent(current, parent, MissionLineageResolver.RelationManual,
                CurrentMoment(), "manual-adopt-" + Guid.NewGuid().ToString("N"));
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void ManualUnlink(MissionRecord mission)
        {
            _lineage.Unlink(mission, CurrentMoment(),
                "manual-unlink-" + Guid.NewGuid().ToString("N"));
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void ManualTrackCurrentAs(MissionRecord mission)
        {
            if (string.IsNullOrWhiteSpace(_activeVesselId))
            {
                throw new InvalidOperationException("There is no active vessel to assign.");
            }
            _lineage.TrackAsMission(
                mission,
                _activeCampaignId,
                _activeVesselId,
                _activeTravelObjectId,
                _activeVesselName,
                CurrentMoment(),
                "manual-track-" + Guid.NewGuid().ToString("N"));
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void MarkLineageNeedsReview(
            IList<string> vesselIds,
            string reason,
            string operationId)
        {
            GameInstance game = CurrentGame();
            string campaignId = game == null ? _activeCampaignId : game.SessionGuidString;
            var marked = new HashSet<string>(StringComparer.Ordinal);
            if (vesselIds != null)
            {
                for (int index = 0; index < vesselIds.Count; index++)
                {
                    MissionRecord mission = _lineage.FindTrackedVessel(
                        campaignId, vesselIds[index]) ??
                        _lineage.FindAlias(campaignId, vesselIds[index]);
                    if (mission == null || !marked.Add(mission.MissionId))
                    {
                        continue;
                    }
                    _lineage.MarkNeedsReview(
                        mission,
                        reason,
                        CurrentMoment(),
                        operationId + "-" + mission.MissionId,
                        vesselIds);
                }
            }
            if (marked.Count > 0)
            {
                Save(UnityEngine.Time.realtimeSinceStartup);
            }
        }

        public void SaveEdits(MissionRecord mission, string title, string notes)
        {
            if (mission == null)
            {
                return;
            }
            mission.Title = string.IsNullOrWhiteSpace(title) ? mission.VesselName : title.Trim();
            mission.Notes = notes == null ? string.Empty : notes.Trim();
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void BeginIsolatedTestSession()
        {
            _store.UseIsolatedTestArchive();
            ReplaceArchive(new MissionArchive());
            ClearActiveSelection();
            _store.Reset();
            _lastSavedRealtime = UnityEngine.Time.realtimeSinceStartup;
            _info("Started an isolated semantic test archive at " + _store.ArchivePath + ".");
        }

        public void ReloadArchive() { ReplaceArchive(_store.Load()); }

        public void EndIsolatedTestSession()
        {
            _store.UseProductionArchive();
            ReplaceArchive(_store.Load());
            ClearActiveSelection();
            _info("Ended the isolated semantic test archive session.");
        }

        public void Flush() { Save(UnityEngine.Time.realtimeSinceStartup); }

        public MissionRecord ScenarioLaunch(string missionId, string title, string vesselId)
        {
            MissionRecord mission = _lineage.CreateMission(
                missionId,
                ScenarioCampaignId,
                "Lineage scenarios",
                vesselId,
                "travel-" + vesselId,
                title,
                title,
                MissionLineageResolver.KindFlight,
                null,
                null,
                "Kerbin",
                "Flying",
                MissionMoment.Now(1.0, "Kerbin", "Flying"),
                new string[0],
                false);
            Save(UnityEngine.Time.realtimeSinceStartup);
            return mission;
        }

        public MissionRecord ScenarioDock(
            string leftVesselId,
            string rightVesselId,
            string resultVesselId,
            string resultName,
            string operationId,
            bool manual)
        {
            MissionRecord mission = _lineage.Dock(
                ScenarioCampaignId,
                leftVesselId,
                rightVesselId,
                resultVesselId,
                "travel-" + resultVesselId,
                resultName,
                MissionMoment.Now(10.0, "Kerbin", "Orbiting"),
                operationId,
                manual);
            Save(UnityEngine.Time.realtimeSinceStartup);
            return mission;
        }

        public MissionRecord ScenarioSplit(
            string sourceVesselId,
            string continuationVesselId,
            string detachedVesselId,
            string detachedName,
            string detachedTravelObjectId,
            string operationId)
        {
            MissionRecord mission = _lineage.Split(
                ScenarioCampaignId,
                sourceVesselId,
                continuationVesselId,
                "travel-" + continuationVesselId,
                continuationVesselId,
                detachedVesselId,
                detachedTravelObjectId,
                detachedName,
                MissionMoment.Now(20.0, "Mun", "Orbiting"),
                operationId,
                new string[0]);
            Save(UnityEngine.Time.realtimeSinceStartup);
            return mission;
        }

        public void ScenarioAdopt(string childMissionId, string parentMissionId)
        {
            _lineage.Reparent(
                RequireMission(childMissionId),
                RequireMission(parentMissionId),
                MissionLineageResolver.RelationManual,
                MissionMoment.Now(30.0, "Mun", "Orbiting"),
                "scenario-adopt-" + childMissionId + "-" + parentMissionId);
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void ScenarioUnlink(string missionId)
        {
            _lineage.Unlink(
                RequireMission(missionId),
                MissionMoment.Now(31.0, "Mun", "Orbiting"),
                "scenario-unlink-" + missionId);
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        public void ScenarioStatus(string vesselId, string status) { CompleteVessel(vesselId, status); }

        public MissionRecord ScenarioTrack(string missionId, string vesselId)
        {
            MissionRecord mission = RequireMission(missionId);
            _lineage.TrackAsMission(
                mission,
                ScenarioCampaignId,
                vesselId,
                "travel-" + vesselId,
                vesselId,
                MissionMoment.Now(32.0, "Mun", "Orbiting"),
                "scenario-track-" + missionId + "-" + vesselId);
            Save(UnityEngine.Time.realtimeSinceStartup);
            return mission;
        }

        private MissionRecord CreateFlightMission(
            GameInstance game,
            VesselComponent vessel,
            string campaignId,
            string vesselId,
            bool observedAtDocking)
        {
            var crew = new List<string>();
            CaptureCrew(game, vessel, crew);
            MissionRecord mission = _lineage.CreateMission(
                null,
                campaignId,
                SafeCampaignName(game),
                vesselId,
                SafeTravelObjectId(vessel),
                SafeVesselName(vessel),
                SafeVesselName(vessel),
                MissionLineageResolver.KindFlight,
                null,
                null,
                SafeBody(vessel),
                vessel.Situation.ToString(),
                Moment(vessel),
                crew,
                observedAtDocking);
            _info("Started mission '" + mission.Title + "' for vessel " + vesselId + ".");
            return mission;
        }

        private void EnsureDockingParticipant(
            GameInstance game,
            VesselComponent combined,
            string vesselId,
            string travelObjectId,
            string vesselName,
            string operationId)
        {
            if (_lineage.FindTrackedVessel(game.SessionGuidString, vesselId) != null)
            {
                return;
            }
            MissionRecord travelAlias = _lineage.FindTravelAlias(
                game.SessionGuidString, travelObjectId);
            if (travelAlias != null && travelAlias.IsActive &&
                travelAlias.TrackedVesselIds.Count <= 1)
            {
                if (travelAlias.TrackedVesselIds.Count == 1 &&
                    !string.Equals(travelAlias.TrackedVesselIds[0], vesselId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "A docking parent travel alias still belongs to another live craft.");
                }
                _lineage.RebindTravelIdentity(
                    travelAlias,
                    game.SessionGuidString,
                    vesselId,
                    travelObjectId,
                    vesselName,
                    Moment(combined),
                    operationId + "-parent-identity-" + vesselId);
                return;
            }
            _lineage.CreateMission(
                null,
                game.SessionGuidString,
                SafeCampaignName(game),
                vesselId,
                travelObjectId,
                vesselName,
                vesselName,
                MissionLineageResolver.KindFlight,
                null,
                null,
                SafeBody(combined),
                combined.Situation.ToString(),
                Moment(combined),
                new string[0],
                true);
        }

        private bool UpdateMission(MissionRecord mission, GameInstance game, VesselComponent vessel)
        {
            bool changed = false;
            mission.FlightDurationSeconds = Math.Max(
                mission.FlightDurationSeconds, Math.Max(0.0, vessel.TimeSinceLaunch));
            mission.MaximumAltitudeMeters = Math.Max(
                mission.MaximumAltitudeMeters, Math.Max(0.0, vessel.AltitudeFromSeaLevel));
            mission.MaximumSpeedMetersPerSecond = Math.Max(
                mission.MaximumSpeedMetersPerSecond,
                Math.Max(vessel.SrfSpeedMagnitude, vessel.OrbitalSpeed));
            mission.MaximumGForce = Math.Max(mission.MaximumGForce, Math.Max(0.0, vessel.geeForce));

            string body = SafeBody(vessel);
            string situation = vessel.Situation.ToString();
            if (!string.Equals(body, mission.LastBody, StringComparison.OrdinalIgnoreCase))
            {
                mission.LastBody = body;
                AddUnique(mission.VisitedBodies, body);
                AddEvent(mission, "body_changed", "Arrived at " + body, vessel, body, situation);
                changed = true;
            }
            bool leftPreLaunch =
                string.Equals(mission.LastSituation, "PreLaunch", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(situation, "PreLaunch", StringComparison.OrdinalIgnoreCase);
            if ((vessel.HasLaunched || leftPreLaunch) && !HasEvent(mission, "launch"))
            {
                AddEvent(mission, "launch", "Launched from " + body, vessel, body, situation);
                changed = true;
            }
            if (!string.Equals(situation, mission.LastSituation, StringComparison.Ordinal))
            {
                mission.LastSituation = situation;
                AddEvent(mission, "situation_changed", FriendlySituation(situation),
                    vessel, body, situation);
                changed = true;
            }
            if (IsOrbitSituation(situation) && !HasEventOnBody(mission, "orbit", body))
            {
                AddEvent(mission, "orbit", "Entered orbit of " + body, vessel, body, situation);
                changed = true;
            }
            if (vessel.Landed && (vessel.HasLaunched || HasEvent(mission, "launch")) &&
                !HasEventOnBody(mission, "landed", body))
            {
                AddEvent(mission, "landed", "Landed on " + body, vessel, body, situation);
                changed = true;
            }
            if (vessel.Splashed && (vessel.HasLaunched || HasEvent(mission, "launch")) &&
                !HasEventOnBody(mission, "splashed", body))
            {
                AddEvent(mission, "splashed", "Splashed down on " + body, vessel, body, situation);
                changed = true;
            }
            int previousCrewCount = mission.Crew.Count;
            CaptureCrew(game, vessel, mission.Crew);
            return changed || mission.Crew.Count != previousCrewCount;
        }

        private void Complete(MissionRecord mission, string status, double flightTime)
        {
            if (!mission.IsActive)
            {
                return;
            }
            status = NormalizeTerminalStatus(status);
            mission.Status = status;
            mission.EndedUtc = DateTime.UtcNow.ToString("o");
            mission.FlightDurationSeconds = Math.Max(mission.FlightDurationSeconds, flightTime);
            mission.TrackedVesselIds.Clear();
            mission.TrackedTravelObjectId = null;
            mission.Events.Add(new MissionEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Kind = "mission_completed",
                Title = "Mission " + status.ToLowerInvariant(),
                RecordedUtc = DateTime.UtcNow.ToString("o"),
                FlightTimeSeconds = mission.FlightDurationSeconds,
                Body = mission.LastBody,
                Situation = mission.LastSituation,
                RelatedMissionIds = new List<string>(),
                VesselIds = new List<string>(mission.VesselIds)
            });
            _info("Completed mission '" + mission.Title + "' as " + status + ".");
        }

        private static string NormalizeTerminalStatus(string status)
        {
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return "Completed";
            }
            if (string.Equals(status, "Recovered", StringComparison.OrdinalIgnoreCase))
            {
                return "Recovered";
            }
            if (string.Equals(status, "Lost", StringComparison.OrdinalIgnoreCase))
            {
                return "Lost";
            }
            throw new InvalidOperationException(
                "Mission status must be Completed, Recovered, or Lost.");
        }

        private bool IsCompletedFlightContinuation(
            string campaignId, string vesselId, double currentFlightTime)
        {
            MissionRecord mission = _lineage.FindAlias(campaignId, vesselId);
            return mission != null && !mission.IsActive &&
                !string.Equals(mission.Status, "Joined", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mission.Status, "Rejoined", StringComparison.OrdinalIgnoreCase) &&
                Math.Max(0.0, currentFlightTime) + 0.5 >=
                    Math.Max(0.0, mission.FlightDurationSeconds);
        }

        private void Save(float realtime)
        {
            try
            {
                _store.Save(_archive);
                _lastSavedRealtime = realtime;
            }
            catch (Exception error)
            {
                _error("Could not save the mission archive: " + error.Message);
            }
        }

        private void ReplaceArchive(MissionArchive archive)
        {
            _archive = archive ?? new MissionArchive();
            _lineage = new MissionLineageResolver(_archive);
        }

        private MissionRecord RequireMission(string missionId)
        {
            MissionRecord mission = _lineage.FindById(missionId);
            if (mission == null)
            {
                throw new InvalidOperationException("Mission was not found: " + missionId);
            }
            return mission;
        }

        private void RememberActive(string campaignId, VesselComponent vessel)
        {
            _activeCampaignId = campaignId;
            _activeVesselId = SafeVesselId(vessel);
            _activeTravelObjectId = SafeTravelObjectId(vessel);
            _activeVesselName = SafeVesselName(vessel);
        }

        private void ClearActiveSelection()
        {
            _activeCampaignId = null;
            _activeVesselId = null;
            _activeTravelObjectId = null;
            _activeVesselName = null;
        }

        private MissionMoment CurrentMoment()
        {
            GameInstance game = CurrentGame();
            VesselComponent vessel = game == null || game.ViewController == null
                ? null
                : game.ViewController.GetActiveSimVessel(true);
            return vessel == null
                ? MissionMoment.Now(0.0, string.Empty, string.Empty)
                : Moment(vessel);
        }

        private static GameInstance CurrentGame()
        {
            return GameManager.Instance == null ? null : GameManager.Instance.Game;
        }

        private static bool IsFlightReady(GameInstance game)
        {
            if (game == null || game.ViewController == null || game.GlobalGameState == null ||
                string.IsNullOrWhiteSpace(game.SessionGuidString))
            {
                return false;
            }
            GameState state = game.GlobalGameState.GetGameState().GameState;
            return state == GameState.FlightView || state == GameState.Map3DView;
        }

        private static bool IsMissionWorthy(VesselComponent vessel, GameInstance game)
        {
            if (vessel.IsControllable || vessel.HasCommandModule || vessel.IsKerbalEVA ||
                vessel.TotalCommandCrewCapacity > 0)
            {
                return true;
            }
            if (game.SessionManager == null || game.SessionManager.KerbalRosterManager == null)
            {
                return false;
            }
            List<KerbalInfo> crew = game.SessionManager.KerbalRosterManager
                .GetAllKerbalsInVessel(vessel.GlobalId);
            return crew != null && crew.Count > 0;
        }

        private static void CaptureCrew(
            GameInstance game, VesselComponent vessel, List<string> destination)
        {
            if (game.SessionManager == null || game.SessionManager.KerbalRosterManager == null)
            {
                return;
            }
            List<KerbalInfo> crew = game.SessionManager.KerbalRosterManager
                .GetAllKerbalsInVessel(vessel.GlobalId);
            if (crew == null)
            {
                return;
            }
            for (int index = 0; index < crew.Count; index++)
            {
                KerbalInfo kerbal = crew[index];
                if (kerbal != null)
                {
                    string name = kerbal.Attributes.GetFullName();
                    AddUnique(destination,
                        string.IsNullOrWhiteSpace(name) ? kerbal.NameKey : name);
                }
            }
        }

        private static void AddEvent(
            MissionRecord mission,
            string kind,
            string title,
            VesselComponent vessel,
            string body,
            string situation)
        {
            mission.Events.Add(new MissionEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Kind = kind,
                Title = title,
                RecordedUtc = DateTime.UtcNow.ToString("o"),
                FlightTimeSeconds = Math.Max(0.0, vessel.TimeSinceLaunch),
                Body = body,
                Situation = situation,
                RelatedMissionIds = new List<string>(),
                VesselIds = new List<string> { SafeVesselId(vessel) }
            });
        }

        private static bool HasEvent(MissionRecord mission, string kind)
        {
            for (int index = 0; index < mission.Events.Count; index++)
            {
                if (string.Equals(mission.Events[index].Kind, kind,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasEventOnBody(MissionRecord mission, string kind, string body)
        {
            for (int index = 0; index < mission.Events.Count; index++)
            {
                MissionEvent entry = mission.Events[index];
                if (string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Body, body, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            values.Add(value);
        }

        private static bool Contains(List<string> values, string value)
        {
            if (values == null)
            {
                return false;
            }
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static MissionMoment Moment(VesselComponent vessel)
        {
            return MissionMoment.Now(
                vessel.TimeSinceLaunch,
                SafeBody(vessel),
                vessel.Situation.ToString());
        }

        private static string SafeVesselName(VesselComponent vessel)
        {
            string name = vessel.RevealDisplayName();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = vessel.RevealName();
            }
            return string.IsNullOrWhiteSpace(name) ? "Unnamed mission" : name;
        }

        private static string SafeVesselId(VesselComponent vessel)
        {
            string id = vessel.GlobalId.ToString();
            return string.IsNullOrWhiteSpace(id) ? vessel.Guid : id;
        }

        private static string SafeTravelObjectId(VesselComponent vessel)
        {
            return vessel.TravelObjectId.ToString();
        }

        private static string SafeBody(VesselComponent vessel)
        {
            return vessel.mainBody == null || string.IsNullOrWhiteSpace(vessel.mainBody.bodyName)
                ? "Unknown body"
                : vessel.mainBody.bodyName;
        }

        private static string SafeCampaignName(GameInstance game)
        {
            return game.SessionManager == null ||
                string.IsNullOrWhiteSpace(game.SessionManager.ActiveCampaignName)
                ? "Unknown campaign"
                : game.SessionManager.ActiveCampaignName;
        }

        private static bool IsOrbitSituation(string situation)
        {
            return situation.IndexOf("Orbit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FriendlySituation(string situation)
        {
            return string.IsNullOrWhiteSpace(situation)
                ? "Flight state changed"
                : "Flight state: " + situation;
        }
    }
}
