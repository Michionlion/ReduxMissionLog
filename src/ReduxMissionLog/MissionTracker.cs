using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Sim.impl;

namespace ReduxMissionLog
{
    internal sealed class MissionTracker
    {
        private readonly MissionArchiveStore _store;
        private readonly Action<string> _info;
        private readonly Action<string> _error;
        private MissionArchive _archive;
        private string _activeVesselKey;
        private float _lastSavedRealtime;

        public MissionTracker(
            MissionArchiveStore store,
            Action<string> info,
            Action<string> error)
        {
            _store = store;
            _info = info;
            _error = error;
            _archive = _store.Load();
        }

        public MissionArchive Archive { get { return _archive; } }
        public string ArchivePath { get { return _store.ArchivePath; } }

        public void Observe(float realtime)
        {
            try
            {
                GameInstance game = GameManager.Instance == null
                    ? null
                    : GameManager.Instance.Game;
                if (game == null || game.ViewController == null)
                {
                    _activeVesselKey = null;
                    return;
                }
                if (game.GlobalGameState == null)
                {
                    _activeVesselKey = null;
                    return;
                }
                GameState state = game.GlobalGameState.GetGameState().GameState;
                if (state != GameState.FlightView && state != GameState.Map3DView)
                {
                    _activeVesselKey = null;
                    return;
                }
                if (string.IsNullOrWhiteSpace(game.SessionGuidString))
                {
                    _activeVesselKey = null;
                    return;
                }

                VesselComponent vessel = game.ViewController.GetActiveSimVessel(true);
                if (vessel == null || !vessel.IsValidInSim)
                {
                    _activeVesselKey = null;
                    return;
                }

                string campaignId = SafeCampaignId(game);
                string vesselId = SafeVesselId(vessel);
                string vesselKey = campaignId + "|" + vesselId;
                _activeVesselKey = vesselKey;

                MissionRecord mission = FindActive(campaignId, vesselId);
                if (mission == null &&
                    !IsCompletedFlightContinuation(campaignId, vesselId, vessel.TimeSinceLaunch))
                {
                    mission = CreateMission(game, vessel, campaignId, vesselId);
                    _archive.Missions.Add(mission);
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
            if (string.IsNullOrEmpty(_activeVesselKey))
            {
                return null;
            }
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (mission.IsActive &&
                    string.Equals(mission.CampaignId + "|" + mission.VesselId,
                        _activeVesselKey, StringComparison.Ordinal))
                {
                    return mission;
                }
            }
            return null;
        }

        public MissionRecord GetLatest()
        {
            if (_archive.Missions.Count == 0)
            {
                return null;
            }
            return _archive.Missions[_archive.Missions.Count - 1];
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
            if (string.IsNullOrWhiteSpace(status))
            {
                status = "Completed";
            }
            CompleteMission(mission, status);
        }

        public void CompleteMission(MissionRecord mission, string status)
        {
            if (mission == null || !_archive.Missions.Contains(mission) || !mission.IsActive)
            {
                throw new InvalidOperationException("The selected mission is not active.");
            }
            if (string.IsNullOrWhiteSpace(status))
            {
                status = "Completed";
            }
            Complete(mission, status.Trim(), mission.FlightDurationSeconds);
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
                if (mission.IsActive &&
                    string.Equals(mission.VesselId, vesselId, StringComparison.Ordinal))
                {
                    Complete(mission, status, mission.FlightDurationSeconds);
                    Save(UnityEngine.Time.realtimeSinceStartup);
                    return;
                }
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
            _archive = new MissionArchive();
            _activeVesselKey = null;
            _store.Reset();
            _lastSavedRealtime = UnityEngine.Time.realtimeSinceStartup;
            _info("Started an isolated semantic test archive at " + _store.ArchivePath + ".");
        }

        public void ReloadArchive()
        {
            _archive = _store.Load();
        }

        public void EndIsolatedTestSession()
        {
            _store.UseProductionArchive();
            _archive = _store.Load();
            _activeVesselKey = null;
            _info("Ended the isolated semantic test archive session.");
        }

        public void Flush()
        {
            Save(UnityEngine.Time.realtimeSinceStartup);
        }

        private MissionRecord CreateMission(
            GameInstance game,
            VesselComponent vessel,
            string campaignId,
            string vesselId)
        {
            string vesselName = SafeVesselName(vessel);
            string body = SafeBody(vessel);
            string situation = vessel.Situation.ToString();
            var mission = new MissionRecord
            {
                MissionId = Guid.NewGuid().ToString("N"),
                CampaignId = campaignId,
                CampaignName = SafeCampaignName(game),
                VesselId = vesselId,
                VesselName = vesselName,
                Title = vesselName,
                Status = "Active",
                StartedUtc = DateTime.UtcNow.ToString("o"),
                StartBody = body,
                LastBody = body,
                LastSituation = situation,
                Notes = string.Empty
            };
            if (!string.IsNullOrWhiteSpace(body))
            {
                mission.VisitedBodies.Add(body);
            }
            CaptureCrew(game, vessel, mission.Crew);
            AddEvent(mission, "mission_started", "Mission started", vessel, body, situation);
            _info("Started mission '" + vesselName + "' for vessel " + vesselId + ".");
            return mission;
        }

        private bool UpdateMission(
            MissionRecord mission,
            GameInstance game,
            VesselComponent vessel)
        {
            bool changed = false;
            double altitude = Math.Max(0.0, vessel.AltitudeFromSeaLevel);
            double speed = Math.Max(vessel.SrfSpeedMagnitude, vessel.OrbitalSpeed);
            double gForce = Math.Max(0.0, vessel.geeForce);
            mission.FlightDurationSeconds = Math.Max(mission.FlightDurationSeconds,
                Math.Max(0.0, vessel.TimeSinceLaunch));

            if (altitude > mission.MaximumAltitudeMeters)
            {
                mission.MaximumAltitudeMeters = altitude;
            }
            if (speed > mission.MaximumSpeedMetersPerSecond)
            {
                mission.MaximumSpeedMetersPerSecond = speed;
            }
            if (gForce > mission.MaximumGForce)
            {
                mission.MaximumGForce = gForce;
            }

            string body = SafeBody(vessel);
            string situation = vessel.Situation.ToString();
            if (!string.Equals(body, mission.LastBody, StringComparison.OrdinalIgnoreCase))
            {
                mission.LastBody = body;
                AddUnique(mission.VisitedBodies, body);
                AddEvent(mission, "body_changed", "Arrived at " + body,
                    vessel, body, situation);
                changed = true;
            }

            bool leftPreLaunch =
                string.Equals(mission.LastSituation, "PreLaunch", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(situation, "PreLaunch", StringComparison.OrdinalIgnoreCase);
            if ((vessel.HasLaunched || leftPreLaunch) && !HasEvent(mission, "launch"))
            {
                AddEvent(mission, "launch", "Launched from " + body,
                    vessel, body, situation);
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
                AddEvent(mission, "orbit", "Entered orbit of " + body,
                    vessel, body, situation);
                changed = true;
            }
            if (vessel.Landed && (vessel.HasLaunched || HasEvent(mission, "launch")) &&
                !HasEventOnBody(mission, "landed", body))
            {
                AddEvent(mission, "landed", "Landed on " + body,
                    vessel, body, situation);
                changed = true;
            }
            if (vessel.Splashed && (vessel.HasLaunched || HasEvent(mission, "launch")) &&
                !HasEventOnBody(mission, "splashed", body))
            {
                AddEvent(mission, "splashed", "Splashed down on " + body,
                    vessel, body, situation);
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
            mission.Status = status;
            mission.EndedUtc = DateTime.UtcNow.ToString("o");
            mission.FlightDurationSeconds = Math.Max(mission.FlightDurationSeconds, flightTime);
            mission.Events.Add(new MissionEvent
            {
                Kind = "mission_completed",
                Title = "Mission " + status.ToLowerInvariant(),
                RecordedUtc = DateTime.UtcNow.ToString("o"),
                FlightTimeSeconds = mission.FlightDurationSeconds,
                Body = mission.LastBody,
                Situation = mission.LastSituation
            });
            _info("Completed mission '" + mission.Title + "' as " + status + ".");
        }

        private MissionRecord FindActive(string campaignId, string vesselId)
        {
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (mission.IsActive &&
                    string.Equals(mission.CampaignId, campaignId, StringComparison.Ordinal) &&
                    string.Equals(mission.VesselId, vesselId, StringComparison.Ordinal))
                {
                    return mission;
                }
            }
            return null;
        }

        private bool IsCompletedFlightContinuation(
            string campaignId,
            string vesselId,
            double currentFlightTime)
        {
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (!mission.IsActive &&
                    string.Equals(mission.CampaignId, campaignId, StringComparison.Ordinal) &&
                    string.Equals(mission.VesselId, vesselId, StringComparison.Ordinal))
                {
                    return Math.Max(0.0, currentFlightTime) + 0.5 >=
                        Math.Max(0.0, mission.FlightDurationSeconds);
                }
            }
            return false;
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

        private static void CaptureCrew(
            GameInstance game,
            VesselComponent vessel,
            List<string> destination)
        {
            if (game.SessionManager == null ||
                game.SessionManager.KerbalRosterManager == null)
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
                Kind = kind,
                Title = title,
                RecordedUtc = DateTime.UtcNow.ToString("o"),
                FlightTimeSeconds = Math.Max(0.0, vessel.TimeSinceLaunch),
                Body = body,
                Situation = situation
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

        private static bool HasEventOnBody(
            MissionRecord mission,
            string kind,
            string body)
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

        private static string SafeBody(VesselComponent vessel)
        {
            return vessel.mainBody == null || string.IsNullOrWhiteSpace(vessel.mainBody.bodyName)
                ? "Unknown body"
                : vessel.mainBody.bodyName;
        }

        private static string SafeCampaignId(GameInstance game)
        {
            return string.IsNullOrWhiteSpace(game.SessionGuidString)
                ? "unknown-session"
                : game.SessionGuidString;
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
            if (string.IsNullOrWhiteSpace(situation))
            {
                return "Flight state changed";
            }
            return "Flight state: " + situation;
        }
    }
}
