using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ReduxMissionLog
{
    internal sealed class MissionPlanStore : IMissionPlanStore
    {
        private readonly Action<string> _info;
        private readonly Action<string> _error;

        public MissionPlanStore(Action<string> info, Action<string> error)
        {
            _info = info;
            _error = error;
            ProductionPath = System.IO.Path.Combine(
                Application.persistentDataPath,
                "ReduxMissionLog",
                "mission-plans.json");
            Path = ProductionPath;
        }

        public string Path { get; private set; }
        public string ProductionPath { get; private set; }

        public void UseIsolatedTestState()
        {
            Path = System.IO.Path.Combine(
                Application.persistentDataPath,
                "ReduxMissionLog",
                "tests",
                "mission-plans.json");
        }

        public void UseProductionState()
        {
            Path = ProductionPath;
        }

        public MissionPlanState Load()
        {
            if (!File.Exists(Path))
            {
                return new MissionPlanState();
            }

            MissionPlanState state;
            try
            {
                state = JsonConvert.DeserializeObject<MissionPlanState>(
                    File.ReadAllText(Path));
                if (state == null)
                {
                    throw new InvalidDataException("The mission-plan document was empty.");
                }
                if (state.SchemaVersion > 1)
                {
                    throw new InvalidDataException(
                        "Mission-plan schema " + state.SchemaVersion +
                        " is newer than this mod supports.");
                }
                state.SchemaVersion = 1;
                // Reuse the domain normalizer so missing legacy collections and
                // identifiers are repaired before the live planner sees them.
                state = new MissionPlanner(state).State;
            }
            catch (Exception error)
            {
                PreserveMalformedState(error);
                return new MissionPlanState();
            }

            try
            {
                // Persist normalization once so generated legacy identifiers
                // remain stable across subsequent loads. A write failure must
                // not make a valid, readable plan document disappear in memory.
                Save(state);
            }
            catch (Exception error)
            {
                _error("Loaded mission plans but could not persist normalized data: " +
                    error.Message);
            }
            _info("Loaded " + (state.Plans == null ? 0 : state.Plans.Count) +
                " mission plan(s).");
            return state;
        }

        public void Save(MissionPlanState state)
        {
            string directory = System.IO.Path.GetDirectoryName(Path);
            Directory.CreateDirectory(directory);
            string temporary = Path + ".tmp";
            string backup = Path + ".bak";
            File.WriteAllText(
                temporary,
                JsonConvert.SerializeObject(state ?? new MissionPlanState(), Formatting.Indented));

            if (File.Exists(Path))
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
                File.Replace(temporary, Path, backup, true);
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
            }
            else
            {
                File.Move(temporary, Path);
            }
        }

        public void Reset()
        {
            Save(new MissionPlanState());
        }

        private void PreserveMalformedState(Exception cause)
        {
            try
            {
                string preserved = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Path),
                    "mission-plans.corrupt-" +
                        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Copy(Path, preserved, true);
                _error("Could not read mission plans; preserved them at " + preserved +
                    ". " + cause.Message);
            }
            catch (Exception preservationError)
            {
                _error("Could not read or preserve malformed mission plans: " +
                    preservationError.Message + ". Original error: " + cause.Message);
            }
        }
    }
}
