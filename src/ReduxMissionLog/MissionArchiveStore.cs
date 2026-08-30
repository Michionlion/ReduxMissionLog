using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ReduxMissionLog
{
    internal sealed class MissionArchiveStore
    {
        private readonly Action<string> _info;
        private readonly Action<string> _error;

        public MissionArchiveStore(Action<string> info, Action<string> error)
        {
            _info = info;
            _error = error;
            ProductionArchivePath = Path.Combine(
                Application.persistentDataPath,
                "ReduxMissionLog",
                "mission-log.json");
            ArchivePath = ProductionArchivePath;
        }

        public string ArchivePath { get; private set; }
        public string ProductionArchivePath { get; private set; }

        public void UseIsolatedTestArchive()
        {
            ArchivePath = Path.Combine(
                Application.persistentDataPath,
                "ReduxMissionLog",
                "tests",
                "mission-lifecycle.json");
        }

        public void UseProductionArchive()
        {
            ArchivePath = ProductionArchivePath;
        }

        public MissionArchive Load()
        {
            if (!File.Exists(ArchivePath))
            {
                return new MissionArchive();
            }

            try
            {
                string json = File.ReadAllText(ArchivePath);
                MissionArchive archive = JsonConvert.DeserializeObject<MissionArchive>(json);
                if (archive == null)
                {
                    throw new InvalidDataException("The archive document was empty.");
                }
                Normalize(archive);
                _info("Loaded " + archive.Missions.Count + " mission record(s).");
                return archive;
            }
            catch (Exception error)
            {
                PreserveMalformedArchive(error);
                return new MissionArchive();
            }
        }

        public void Save(MissionArchive archive)
        {
            string directory = Path.GetDirectoryName(ArchivePath);
            Directory.CreateDirectory(directory);
            string temporary = ArchivePath + ".tmp";
            string backup = ArchivePath + ".bak";
            string json = JsonConvert.SerializeObject(archive, Formatting.Indented);
            File.WriteAllText(temporary, json);

            if (File.Exists(ArchivePath))
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
                File.Replace(temporary, ArchivePath, backup, true);
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
            }
            else
            {
                File.Move(temporary, ArchivePath);
            }
        }

        public void Reset()
        {
            Save(new MissionArchive());
        }

        private void PreserveMalformedArchive(Exception cause)
        {
            try
            {
                string preserved = Path.Combine(
                    Path.GetDirectoryName(ArchivePath),
                    "mission-log.corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Copy(ArchivePath, preserved, true);
                _error("Could not read the mission archive; preserved it at " + preserved +
                    ". " + cause.Message);
            }
            catch (Exception preservationError)
            {
                _error("Could not read or preserve the malformed mission archive: " +
                    preservationError.Message + ". Original error: " + cause.Message);
            }
        }

        private static void Normalize(MissionArchive archive)
        {
            if (archive.SchemaVersion > 2)
            {
                throw new InvalidDataException(
                    "Archive schema " + archive.SchemaVersion + " is newer than this mod supports.");
            }
            bool legacySchema = archive.SchemaVersion <= 1;
            archive.SchemaVersion = 2;
            if (archive.Missions == null)
            {
                archive.Missions = new System.Collections.Generic.List<MissionRecord>();
                return;
            }
            for (int index = archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = archive.Missions[index];
                if (mission == null)
                {
                    archive.Missions.RemoveAt(index);
                    continue;
                }
                mission.Crew = mission.Crew ?? new System.Collections.Generic.List<string>();
                mission.VesselIds = mission.VesselIds ??
                    new System.Collections.Generic.List<string>();
                mission.TravelObjectIds = mission.TravelObjectIds ??
                    new System.Collections.Generic.List<string>();
                mission.TrackedVesselIds = mission.TrackedVesselIds ??
                    new System.Collections.Generic.List<string>();
                mission.VisitedBodies = mission.VisitedBodies ??
                    new System.Collections.Generic.List<string>();
                mission.Events = mission.Events ??
                    new System.Collections.Generic.List<MissionEvent>();
                for (int eventIndex = mission.Events.Count - 1; eventIndex >= 0; eventIndex--)
                {
                    if (mission.Events[eventIndex] == null)
                    {
                        mission.Events.RemoveAt(eventIndex);
                        continue;
                    }
                    MissionEvent entry = mission.Events[eventIndex];
                    entry.EventId = string.IsNullOrWhiteSpace(entry.EventId)
                        ? Guid.NewGuid().ToString("N")
                        : entry.EventId;
                    entry.RelatedMissionIds = entry.RelatedMissionIds ??
                        new System.Collections.Generic.List<string>();
                    entry.VesselIds = entry.VesselIds ??
                        new System.Collections.Generic.List<string>();
                }
                mission.MissionId = string.IsNullOrWhiteSpace(mission.MissionId)
                    ? Guid.NewGuid().ToString("N")
                    : mission.MissionId;
                mission.MissionKind = string.IsNullOrWhiteSpace(mission.MissionKind)
                    ? MissionLineageResolver.KindFlight
                    : mission.MissionKind;
                mission.VesselName = string.IsNullOrWhiteSpace(mission.VesselName)
                    ? "Unnamed vessel"
                    : mission.VesselName;
                mission.Title = string.IsNullOrWhiteSpace(mission.Title)
                    ? mission.VesselName
                    : mission.Title;
                mission.Status = string.IsNullOrWhiteSpace(mission.Status)
                    ? "Active"
                    : mission.Status;
                mission.Notes = mission.Notes ?? string.Empty;
                AddUnique(mission.VesselIds, mission.VesselId);
                NormalizeStrings(mission.VesselIds);
                NormalizeStrings(mission.TravelObjectIds);
                NormalizeStrings(mission.TrackedVesselIds);
                NormalizeStrings(mission.Crew);
                NormalizeStrings(mission.VisitedBodies);
                if (legacySchema && mission.IsActive &&
                    mission.TrackedVesselIds.Count == 0 &&
                    !string.IsNullOrWhiteSpace(mission.VesselId))
                {
                    mission.TrackedVesselIds.Add(mission.VesselId);
                }
                if (!mission.IsActive)
                {
                    mission.TrackedVesselIds.Clear();
                    mission.TrackedTravelObjectId = null;
                }
                if (mission.TrackedVesselIds.Count > 1)
                {
                    mission.TrackedVesselIds.RemoveRange(
                        1, mission.TrackedVesselIds.Count - 1);
                }
                if (mission.TrackedVesselIds.Count == 1)
                {
                    AddUnique(mission.VesselIds, mission.TrackedVesselIds[0]);
                    mission.VesselId = mission.TrackedVesselIds[0];
                }
                AddUnique(mission.TravelObjectIds, mission.TrackedTravelObjectId);
            }

            RepairHierarchy(archive);
            RepairTrackedOwnership(archive);
        }

        private static void RepairHierarchy(MissionArchive archive)
        {
            var originalCounts = new System.Collections.Generic.Dictionary<string, int>(
                StringComparer.Ordinal);
            for (int index = 0; index < archive.Missions.Count; index++)
            {
                string missionId = archive.Missions[index].MissionId;
                int count;
                originalCounts.TryGetValue(missionId, out count);
                originalCounts[missionId] = count + 1;
            }
            var ambiguousIds = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            var byId = new System.Collections.Generic.Dictionary<string, MissionRecord>(
                StringComparer.Ordinal);
            for (int index = 0; index < archive.Missions.Count; index++)
            {
                MissionRecord mission = archive.Missions[index];
                if (originalCounts[mission.MissionId] > 1)
                {
                    ambiguousIds.Add(mission.MissionId);
                    mission.MissionId = Guid.NewGuid().ToString("N");
                    mission.NeedsReview = true;
                }
                byId[mission.MissionId] = mission;
            }
            for (int index = 0; index < archive.Missions.Count; index++)
            {
                MissionRecord mission = archive.Missions[index];
                if (string.IsNullOrWhiteSpace(mission.ParentMissionId))
                {
                    mission.ParentMissionId = null;
                    mission.ParentRelation = null;
                    continue;
                }
                MissionRecord parent;
                if (ambiguousIds.Contains(mission.ParentMissionId) ||
                    !byId.TryGetValue(mission.ParentMissionId, out parent) ||
                    ReferenceEquals(parent, mission) ||
                    !string.Equals(parent.CampaignId, mission.CampaignId, StringComparison.Ordinal))
                {
                    mission.ParentMissionId = null;
                    mission.ParentRelation = null;
                    mission.NeedsReview = true;
                    continue;
                }

                var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                MissionRecord current = mission;
                bool cycle = false;
                while (current != null && !string.IsNullOrWhiteSpace(current.ParentMissionId))
                {
                    if (!visited.Add(current.MissionId))
                    {
                        cycle = true;
                        break;
                    }
                    byId.TryGetValue(current.ParentMissionId, out current);
                }
                if (cycle)
                {
                    mission.ParentMissionId = null;
                    mission.ParentRelation = null;
                    mission.NeedsReview = true;
                }
                else if (string.IsNullOrWhiteSpace(mission.ParentRelation))
                {
                    mission.ParentRelation = MissionLineageResolver.RelationManual;
                }
            }
        }

        private static void RepairTrackedOwnership(MissionArchive archive)
        {
            var owners = new System.Collections.Generic.Dictionary<string, MissionRecord>(
                StringComparer.Ordinal);
            for (int index = archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = archive.Missions[index];
                if (!mission.IsActive || mission.TrackedVesselIds.Count == 0)
                {
                    continue;
                }
                string vesselId = mission.TrackedVesselIds[0];
                string key = mission.CampaignId + "|" + vesselId;
                MissionRecord existing;
                if (owners.TryGetValue(key, out existing))
                {
                    mission.TrackedVesselIds.Clear();
                    mission.TrackedTravelObjectId = null;
                    mission.NeedsReview = true;
                    existing.NeedsReview = true;
                }
                else
                {
                    owners[key] = mission;
                }
            }
        }

        private static void NormalizeStrings(System.Collections.Generic.List<string> values)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Count;)
            {
                string value = values[index];
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                {
                    values.RemoveAt(index);
                    continue;
                }
                index++;
            }
        }

        private static void AddUnique(
            System.Collections.Generic.List<string> values,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value) || values.Contains(value))
            {
                return;
            }
            values.Add(value);
        }
    }
}
