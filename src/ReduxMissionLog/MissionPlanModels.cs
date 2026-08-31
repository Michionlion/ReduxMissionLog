using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ReduxMissionLog
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MissionPlanStatus
    {
        Draft,
        Active,
        Completed,
        Abandoned
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MissionObjectiveStatus
    {
        Pending,
        Current,
        Achieved,
        Skipped,
        Deviated
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MissionObjectiveKind
    {
        Launch,
        Body,
        Situation,
        Orbit,
        Land,
        Dock,
        Separate,
        Recover,
        Complete,
        Custom
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MissionPlanDeviationKind
    {
        UnexpectedFact,
        OutOfOrder,
        MissingBeforeCompletion,
        Manual
    }

    public sealed class MissionPlanState
    {
        public int SchemaVersion = 1;
        public List<MissionPlan> Plans = new List<MissionPlan>();
    }

    public sealed class MissionPlan
    {
        public string PlanId;
        public string CampaignId;
        public string Title;
        public string Notes;
        public MissionPlanStatus Status = MissionPlanStatus.Draft;
        public bool Archived;
        public string CreatedUtc;
        public string UpdatedUtc;
        public string ActivatedUtc;
        public string EndedUtc;
        public List<MissionPlanVesselSlot> VesselSlots =
            new List<MissionPlanVesselSlot>();
        public List<MissionPlanObjective> Objectives =
            new List<MissionPlanObjective>();
        public List<MissionPlanDeviation> Deviations =
            new List<MissionPlanDeviation>();
    }

    public sealed class MissionPlanVesselSlot
    {
        public string SlotId;
        public int Order;
        public string Name;
        public string Role;
        public bool Required = true;
        public bool Archived;
        public string SavedVehicleId;
        public string SavedVehicleName;
        public string SavedVehiclePath;
        public string SavedVehicleLocation;
        public string LaunchRequestedUtc;
        public string LaunchState;
        public string LaunchError;
        public string BoundMissionId;
        public string BoundVesselId;
        public string BoundUtc;
        public List<string> MissionIds = new List<string>();
        public List<string> VesselIds = new List<string>();
    }

    public sealed class MissionPlanObjective
    {
        public string ObjectiveId;
        public int Order;
        public MissionObjectiveKind Kind;
        public MissionObjectiveStatus Status = MissionObjectiveStatus.Pending;
        public string Title;
        public string Notes;
        public string VesselSlotId;
        // Optional second participant for docking/merge objectives.
        public string RelatedVesselSlotId;
        public string TargetBody;
        public string TargetSituation;
        public string MatchValue;
        public bool Optional;
        public bool Archived;
        public string MatchedFactId;
        public string MatchedUtc;
        public bool HasManualResolution;
        public MissionObjectiveStatus ManualResolution;
        public string ManualFactId;
        public string ManualNote;
    }

    public sealed class MissionPlanDeviation
    {
        public string DeviationId;
        public MissionPlanDeviationKind Kind;
        public string ObjectiveId;
        public string FactId;
        public string RecordedUtc;
        public string Title;
        public string Detail;
        public bool Manual;
    }

    // A normalized, ordered observation supplied by the Mission Log timeline adapter.
    public sealed class MissionPlanTimelineFact
    {
        public string FactId;
        public MissionObjectiveKind Kind;
        // Set only by the mission-tree adapter after it has scoped the fact to
        // the connected mission forest represented by this plan.
        public bool IsPlanScoped;
        // Only true for the single overarching root outcome of the connected
        // mission forest. Child completion and vessel loss must not close a
        // multi-vessel plan.
        public bool IsPlanCompletion;
        // A lost mission is a consequential observed deviation, never a
        // successful completion objective.
        public bool IsTerminalLoss;
        public string MissionId;
        public string VesselId;
        public string VesselSlotId;
        public List<string> RelatedMissionIds = new List<string>();
        public List<string> VesselIds = new List<string>();
        public List<string> VesselSlotIds = new List<string>();
        public string RecordedUtc;
        public double FlightTimeSeconds;
        public string Body;
        public string Situation;
        public string Value;
        public string Title;
    }

    public sealed class MissionPlanObjectiveProgress
    {
        public string ObjectiveId;
        public MissionObjectiveStatus Status;
        public string MatchedFactId;
        public string MatchedUtc;
    }

    public sealed class MissionPlanEvaluation
    {
        public string PlanId;
        public MissionPlanStatus SuggestedStatus;
        public List<MissionPlanObjectiveProgress> Objectives =
            new List<MissionPlanObjectiveProgress>();
        public List<MissionPlanDeviation> Deviations =
            new List<MissionPlanDeviation>();
    }

    public interface IMissionPlanStore
    {
        MissionPlanState Load();
        void Save(MissionPlanState state);
    }
}
