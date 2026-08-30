using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ReduxMissionLog
{
    public sealed class MissionArchive
    {
        public int SchemaVersion = 1;
        public List<MissionRecord> Missions = new List<MissionRecord>();
    }

    public sealed class MissionRecord
    {
        public string MissionId;
        public string CampaignId;
        public string CampaignName;
        public string VesselId;
        public string VesselName;
        public string Title;
        public string Status;
        public string StartedUtc;
        public string EndedUtc;
        public double FlightDurationSeconds;
        public string StartBody;
        public string LastBody;
        public string LastSituation;
        public double MaximumAltitudeMeters;
        public double MaximumSpeedMetersPerSecond;
        public double MaximumGForce;
        public string Notes;
        public List<string> Crew = new List<string>();
        public List<string> VisitedBodies = new List<string>();
        public List<MissionEvent> Events = new List<MissionEvent>();

        [JsonIgnore]
        public bool IsActive
        {
            get { return string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public sealed class MissionEvent
    {
        public string Kind;
        public string Title;
        public string RecordedUtc;
        public double FlightTimeSeconds;
        public string Body;
        public string Situation;
    }
}
