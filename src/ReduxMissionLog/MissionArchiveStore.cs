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
            if (archive.SchemaVersion > 1)
            {
                throw new InvalidDataException(
                    "Archive schema " + archive.SchemaVersion + " is newer than this mod supports.");
            }
            archive.SchemaVersion = 1;
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
                mission.VisitedBodies = mission.VisitedBodies ??
                    new System.Collections.Generic.List<string>();
                mission.Events = mission.Events ??
                    new System.Collections.Generic.List<MissionEvent>();
                for (int eventIndex = mission.Events.Count - 1; eventIndex >= 0; eventIndex--)
                {
                    if (mission.Events[eventIndex] == null)
                    {
                        mission.Events.RemoveAt(eventIndex);
                    }
                }
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
            }
        }
    }
}
