using System;
using System.Collections.Generic;
using System.Text;

namespace ReduxMissionLog
{
    internal sealed class MissionPlannerCoordinator : IDisposable
    {
        private const float BindingTimeoutSeconds = 30f;
        private readonly MissionPlanner _planner;
        private readonly MissionTracker _tracker;
        private readonly MissionPlanLaunchService _launchService;
        private readonly Action<string> _info;
        private readonly List<PlannedLaunchResult> _launchResults =
            new List<PlannedLaunchResult>();
        private string _fingerprint;

        public MissionPlannerCoordinator(
            MissionPlanner planner,
            MissionTracker tracker,
            MissionPlanLaunchService launchService,
            Action<string> info)
        {
            _planner = planner;
            _tracker = tracker;
            _launchService = launchService;
            _info = info;
            _launchService.LaunchResolved += OnLaunchResolved;
        }

        public void Observe(float realtime)
        {
            ResolveLaunchResults(realtime);
            string fingerprint = BuildFingerprint();
            if (string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal))
            {
                return;
            }
            _fingerprint = fingerprint;

            for (int index = 0; index < _planner.State.Plans.Count; index++)
            {
                MissionPlan plan = _planner.State.Plans[index];
                if (plan == null || plan.Archived ||
                    (plan.Status != MissionPlanStatus.Active &&
                     plan.Status != MissionPlanStatus.Completed))
                {
                    continue;
                }
                _planner.RecomputeProgress(
                    plan.PlanId,
                    MissionPlanTimelineAdapter.BuildFacts(_tracker, plan));
            }
        }

        public void Invalidate()
        {
            _fingerprint = null;
        }

        public void Dispose()
        {
            _launchService.LaunchResolved -= OnLaunchResolved;
            _launchResults.Clear();
        }

        private void OnLaunchResolved(PlannedLaunchResult result)
        {
            if (result != null)
            {
                _launchResults.Add(result);
            }
        }

        private void ResolveLaunchResults(float realtime)
        {
            for (int index = _launchResults.Count - 1; index >= 0; index--)
            {
                PlannedLaunchResult result = _launchResults[index];
                MissionPlan plan = FindPlan(result.PlanId);
                MissionPlanVesselSlot slot = FindSlot(plan, result.SlotId);
                if (plan == null || slot == null)
                {
                    _launchResults.RemoveAt(index);
                    continue;
                }
                if (!result.Success)
                {
                    TryRecordLaunchResult(
                        plan.PlanId,
                        slot.SlotId,
                        "Failed",
                        result.Error);
                    _launchResults.RemoveAt(index);
                    continue;
                }
                MissionRecord mission = FindMissionByIdentity(
                    result.CampaignId,
                    result.VesselId,
                    result.TravelObjectId);
                if (mission != null)
                {
                    try
                    {
                        _planner.BindLaunch(
                            plan.PlanId,
                            slot.SlotId,
                            mission.MissionId,
                            result.VesselId,
                            mission.StartedUtc);
                        _info("Linked planned vessel '" + slot.Name + "' to " +
                            mission.Title + " by exact KSP launch identity.");
                    }
                    catch (Exception error)
                    {
                        TryRecordLaunchResult(
                            plan.PlanId,
                            slot.SlotId,
                            "NeedsReview",
                            "KSP launched the vessel, but its plan link needs review: " +
                                error.Message);
                    }
                    _launchResults.RemoveAt(index);
                    continue;
                }
                if (realtime - result.ResolvedRealtime >= BindingTimeoutSeconds)
                {
                    TryRecordLaunchResult(
                        plan.PlanId,
                        slot.SlotId,
                        "NeedsReview",
                        "KSP launched the requested vessel, but Mission Log could not " +
                            "match its exact identity to a mission record.");
                    _launchResults.RemoveAt(index);
                }
            }
        }

        private void TryRecordLaunchResult(
            string planId,
            string slotId,
            string state,
            string error)
        {
            try
            {
                _planner.RecordLaunchResult(planId, slotId, state, error);
            }
            catch (Exception recordError)
            {
                _info("Could not record planned launch state '" + state + "': " +
                    recordError.Message);
            }
        }

        private MissionRecord FindMissionByIdentity(
            string campaignId,
            string vesselId,
            string travelObjectId)
        {
            if (string.IsNullOrWhiteSpace(vesselId) &&
                string.IsNullOrWhiteSpace(travelObjectId))
            {
                return null;
            }
            MissionRecord travelMatch = null;
            for (int index = 0; index < _tracker.Archive.Missions.Count; index++)
            {
                MissionRecord mission = _tracker.Archive.Missions[index];
                if (mission == null ||
                    (!string.IsNullOrWhiteSpace(campaignId) &&
                     !string.Equals(
                        mission.CampaignId,
                        campaignId,
                        StringComparison.Ordinal)))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(vesselId) &&
                    (string.Equals(mission.VesselId, vesselId, StringComparison.Ordinal) ||
                     mission.VesselIds.Contains(vesselId) ||
                     mission.TrackedVesselIds.Contains(vesselId)))
                {
                    return mission;
                }
                if (travelMatch == null && !string.IsNullOrWhiteSpace(travelObjectId) &&
                    (string.Equals(
                        mission.TrackedTravelObjectId,
                        travelObjectId,
                        StringComparison.Ordinal) ||
                     mission.TravelObjectIds.Contains(travelObjectId)))
                {
                    travelMatch = mission;
                }
            }
            return travelMatch;
        }

        private MissionPlan FindPlan(string planId)
        {
            for (int index = 0; index < _planner.State.Plans.Count; index++)
            {
                MissionPlan plan = _planner.State.Plans[index];
                if (plan != null &&
                    string.Equals(plan.PlanId, planId, StringComparison.Ordinal))
                {
                    return plan;
                }
            }
            return null;
        }

        private static MissionPlanVesselSlot FindSlot(MissionPlan plan, string slotId)
        {
            if (plan == null)
            {
                return null;
            }
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot != null &&
                    string.Equals(slot.SlotId, slotId, StringComparison.Ordinal))
                {
                    return slot;
                }
            }
            return null;
        }

        private string BuildFingerprint()
        {
            var value = new StringBuilder();
            for (int index = 0; index < _tracker.Archive.Missions.Count; index++)
            {
                MissionRecord mission = _tracker.Archive.Missions[index];
                if (mission == null)
                {
                    continue;
                }
                value.Append(mission.MissionId).Append('|')
                    .Append(mission.ParentMissionId).Append('|')
                    .Append(mission.Status).Append('|')
                    .Append(mission.Events.Count).Append('|');
                if (mission.Events.Count > 0)
                {
                    MissionEvent last = mission.Events[mission.Events.Count - 1];
                    value.Append(last.EventId).Append('|').Append(last.Kind).Append('|');
                }
            }
            for (int planIndex = 0;
                planIndex < _planner.State.Plans.Count;
                planIndex++)
            {
                MissionPlan plan = _planner.State.Plans[planIndex];
                if (plan == null ||
                    (plan.Status != MissionPlanStatus.Active &&
                     plan.Status != MissionPlanStatus.Completed))
                {
                    continue;
                }
                value.Append(plan.PlanId).Append('|');
                for (int slotIndex = 0;
                    slotIndex < plan.VesselSlots.Count;
                    slotIndex++)
                {
                    MissionPlanVesselSlot slot = plan.VesselSlots[slotIndex];
                    value.Append(slot.SlotId).Append(':')
                        .Append(slot.BoundMissionId).Append(':')
                        .Append(slot.BoundVesselId).Append('|');
                }
                for (int objectiveIndex = 0;
                    objectiveIndex < plan.Objectives.Count;
                    objectiveIndex++)
                {
                    MissionPlanObjective objective = plan.Objectives[objectiveIndex];
                    value.Append(objective.ObjectiveId).Append(':')
                        .Append(objective.Order).Append(':')
                        .Append(objective.Kind).Append(':')
                        .Append(objective.VesselSlotId).Append(':')
                        .Append(objective.RelatedVesselSlotId).Append(':')
                        .Append(objective.TargetBody).Append(':')
                        .Append(objective.TargetSituation).Append(':')
                        .Append(objective.MatchValue).Append(':')
                        .Append(objective.HasManualResolution).Append(':')
                        .Append(objective.ManualResolution).Append(':')
                        .Append(objective.Archived).Append('|');
                }
            }
            return value.ToString();
        }
    }
}
