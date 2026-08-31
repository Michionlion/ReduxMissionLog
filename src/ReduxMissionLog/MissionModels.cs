using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ReduxMissionLog
{
    public sealed class MissionArchive
    {
        public int SchemaVersion = 2;
        public List<MissionRecord> Missions = new List<MissionRecord>();
    }

    public sealed class MissionRecord
    {
        public string MissionId;
        public string MissionKind;
        public string CampaignId;
        public string CampaignName;
        public string ParentMissionId;
        public string ParentRelation;
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
        public bool NeedsReview;
        public List<string> VesselIds = new List<string>();
        public List<string> TravelObjectIds = new List<string>();
        public List<string> TrackedVesselIds = new List<string>();
        public string TrackedTravelObjectId;
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
        public string EventId;
        public string OperationId;
        public string Kind;
        public string Title;
        public string RecordedUtc;
        public double FlightTimeSeconds;
        public string Body;
        public string Situation;
        public List<string> RelatedMissionIds = new List<string>();
        public List<string> VesselIds = new List<string>();
    }

    internal sealed class MissionMoment
    {
        public string RecordedUtc;
        public double FlightTimeSeconds;
        public string Body;
        public string Situation;

        public static MissionMoment Now(
            double flightTimeSeconds,
            string body,
            string situation)
        {
            return new MissionMoment
            {
                RecordedUtc = DateTime.UtcNow.ToString("o"),
                FlightTimeSeconds = double.IsNaN(flightTimeSeconds) ||
                    double.IsInfinity(flightTimeSeconds)
                        ? 0.0
                        : Math.Max(0.0, flightTimeSeconds),
                Body = body ?? string.Empty,
                Situation = situation ?? string.Empty
            };
        }
    }

    internal sealed class MissionAggregate
    {
        public double MaximumAltitudeMeters;
        public double MaximumSpeedMetersPerSecond;
        public double MaximumGForce;
        public List<string> Crew = new List<string>();
        public List<string> VisitedBodies = new List<string>();
        public List<MissionEvent> Events = new List<MissionEvent>();
    }
}
