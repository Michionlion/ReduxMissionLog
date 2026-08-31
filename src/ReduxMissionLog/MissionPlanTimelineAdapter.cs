using System;
using System.Collections.Generic;

namespace ReduxMissionLog
{
    internal static class MissionPlanTimelineAdapter
    {
        public static List<MissionPlanTimelineFact> BuildFacts(
            MissionTracker tracker,
            MissionPlan plan)
        {
            var result = new List<MissionPlanTimelineFact>();
            if (tracker == null || plan == null)
            {
                return result;
            }

            HashSet<string> related = FindRelatedMissionIds(tracker.Archive, plan);
            if (related.Count == 0)
            {
                return result;
            }
            string completionRootId = FindSingleRelatedRootId(tracker.Archive, related);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var structuralIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            var structuralPriorities = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int missionIndex = 0;
                missionIndex < tracker.Archive.Missions.Count;
                missionIndex++)
            {
                MissionRecord mission = tracker.Archive.Missions[missionIndex];
                if (mission == null || !related.Contains(mission.MissionId) ||
                    !MatchesCampaign(plan, mission))
                {
                    continue;
                }
                for (int eventIndex = 0; eventIndex < mission.Events.Count; eventIndex++)
                {
                    MissionEvent entry = mission.Events[eventIndex];
                    MissionObjectiveKind kind;
                    bool terminalLoss = entry != null &&
                        EqualsKind(entry.Kind, "mission_completed") &&
                        string.Equals(
                            mission.Status,
                            "Lost",
                            StringComparison.OrdinalIgnoreCase);
                    if (entry == null ||
                        (!TryMapKind(entry.Kind, mission.Status, out kind) &&
                         !terminalLoss))
                    {
                        continue;
                    }
                    if (terminalLoss)
                    {
                        // Preserve loss as an observed fact without allowing it
                        // to satisfy a planned successful completion.
                        kind = MissionObjectiveKind.Custom;
                    }
                    string factId = string.IsNullOrWhiteSpace(entry.EventId)
                        ? mission.MissionId + ":" + entry.Kind + ":" +
                            entry.FlightTimeSeconds + ":" + eventIndex
                        : entry.EventId;
                    if (!seen.Add(factId))
                    {
                        continue;
                    }

                    List<string> vesselIds = Copy(entry.VesselIds);
                    Add(vesselIds, mission.VesselId);
                    List<string> slotIds = FindSlotIds(
                        tracker.Archive,
                        plan,
                        mission,
                        entry,
                        vesselIds);
                    var fact = new MissionPlanTimelineFact
                    {
                        FactId = factId,
                        Kind = kind,
                        IsPlanScoped = true,
                        IsPlanCompletion = kind == MissionObjectiveKind.Complete &&
                            string.Equals(
                                mission.MissionId,
                                completionRootId,
                                StringComparison.Ordinal) &&
                            string.Equals(
                                mission.Status,
                                "Completed",
                                StringComparison.OrdinalIgnoreCase),
                        IsTerminalLoss = terminalLoss,
                        MissionId = mission.MissionId,
                        VesselId = First(vesselIds),
                        VesselSlotId = First(slotIds),
                        RelatedMissionIds = Copy(entry.RelatedMissionIds),
                        VesselIds = vesselIds,
                        VesselSlotIds = slotIds,
                        RecordedUtc = entry.RecordedUtc,
                        FlightTimeSeconds = entry.FlightTimeSeconds,
                        Body = entry.Body,
                        Situation = entry.Situation,
                        Value = BuildValue(entry),
                        Title = entry.Title
                    };
                    string structuralKey = StructuralKey(kind, entry.OperationId);
                    if (!string.IsNullOrWhiteSpace(structuralKey))
                    {
                        int existingIndex;
                        int priority = StructuralPriority(entry.Kind);
                        if (structuralIndices.TryGetValue(
                            structuralKey,
                            out existingIndex))
                        {
                            if (priority > structuralPriorities[structuralKey])
                            {
                                result[existingIndex] = fact;
                                structuralPriorities[structuralKey] = priority;
                            }
                            continue;
                        }
                        structuralIndices[structuralKey] = result.Count;
                        structuralPriorities[structuralKey] = priority;
                    }
                    result.Add(fact);
                }
            }

            result.Sort(CompareFacts);
            ApplyPlannedSeparationLineage(tracker.Archive, plan, result);
            return result;
        }

        private static HashSet<string> FindRelatedMissionIds(
            MissionArchive archive,
            MissionPlan plan)
        {
            var related = new HashSet<string>(StringComparer.Ordinal);
            if (archive == null || archive.Missions == null)
            {
                return related;
            }
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                AddMissionIfCampaignMatches(
                    archive,
                    related,
                    slot.BoundMissionId,
                    plan.CampaignId);
                for (int missionIndex = 0;
                    missionIndex < slot.MissionIds.Count;
                    missionIndex++)
                {
                    AddMissionIfCampaignMatches(
                        archive,
                        related,
                        slot.MissionIds[missionIndex],
                        plan.CampaignId);
                }
            }

            bool changed;
            do
            {
                changed = false;
                for (int index = 0; index < archive.Missions.Count; index++)
                {
                    MissionRecord mission = archive.Missions[index];
                    if (mission == null)
                    {
                        continue;
                    }
                    if (!MatchesCampaign(plan, mission))
                    {
                        continue;
                    }
                    if (related.Contains(mission.MissionId))
                    {
                        if (Add(related, mission.ParentMissionId))
                        {
                            changed = true;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(mission.ParentMissionId) &&
                        related.Contains(mission.ParentMissionId))
                    {
                        related.Add(mission.MissionId);
                        changed = true;
                    }
                }
            }
            while (changed);
            return related;
        }

        private static bool TryMapKind(
            string eventKind,
            string missionStatus,
            out MissionObjectiveKind kind)
        {
            kind = MissionObjectiveKind.Custom;
            if (string.IsNullOrWhiteSpace(eventKind))
            {
                return false;
            }
            if (EqualsKind(eventKind, "launch"))
            {
                kind = MissionObjectiveKind.Launch;
                return true;
            }
            if (EqualsKind(eventKind, "body_changed"))
            {
                kind = MissionObjectiveKind.Body;
                return true;
            }
            if (EqualsKind(eventKind, "situation_changed"))
            {
                kind = MissionObjectiveKind.Situation;
                return true;
            }
            if (EqualsKind(eventKind, "orbit"))
            {
                kind = MissionObjectiveKind.Orbit;
                return true;
            }
            if (EqualsKind(eventKind, "landed") || EqualsKind(eventKind, "splashed"))
            {
                kind = MissionObjectiveKind.Land;
                return true;
            }
            if (EqualsKind(eventKind, "mission_completed"))
            {
                if (string.Equals(
                    missionStatus,
                    "Recovered",
                    StringComparison.OrdinalIgnoreCase))
                {
                    kind = MissionObjectiveKind.Recover;
                    return true;
                }
                if (string.Equals(
                    missionStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    kind = MissionObjectiveKind.Complete;
                    return true;
                }
                return false;
            }
            if (eventKind.IndexOf("separat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                EqualsKind(eventKind, "sub_mission_resumed"))
            {
                kind = MissionObjectiveKind.Separate;
                return true;
            }
            if (eventKind.IndexOf("dock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                eventKind.IndexOf("combined", StringComparison.OrdinalIgnoreCase) >= 0 ||
                eventKind.IndexOf("rejoin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                EqualsKind(eventKind, "sub_mission_recovered") ||
                eventKind.IndexOf("joined_overarching", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = MissionObjectiveKind.Dock;
                return true;
            }
            if (eventKind.IndexOf("recover", StringComparison.OrdinalIgnoreCase) >= 0 &&
                eventKind.IndexOf("rejoined", StringComparison.OrdinalIgnoreCase) < 0)
            {
                kind = MissionObjectiveKind.Recover;
                return true;
            }
            return false;
        }

        private static string StructuralKey(
            MissionObjectiveKind kind,
            string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId) ||
                (kind != MissionObjectiveKind.Dock &&
                 kind != MissionObjectiveKind.Separate))
            {
                return null;
            }
            return kind + "|" + operationId;
        }

        private static int StructuralPriority(string eventKind)
        {
            if (EqualsKind(eventKind, "missions_combined") ||
                EqualsKind(eventKind, "missions_combined_manually") ||
                EqualsKind(eventKind, "sibling_missions_combined") ||
                EqualsKind(eventKind, "docking") ||
                EqualsKind(eventKind, "sub_mission_separated") ||
                EqualsKind(eventKind, "sub_mission_rejoined"))
            {
                return 3;
            }
            if (EqualsKind(eventKind, "joined_overarching_mission") ||
                EqualsKind(eventKind, "sub_mission_recovered"))
            {
                return 1;
            }
            return 2;
        }

        private static List<string> FindSlotIds(
            MissionArchive archive,
            MissionPlan plan,
            MissionRecord mission,
            MissionEvent entry,
            List<string> vesselIds)
        {
            var result = new List<string>();
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot == null || slot.Archived)
                {
                    continue;
                }
                if (Contains(slot.MissionIds, mission.MissionId) ||
                    Contains(slot.MissionIds, entry.RelatedMissionIds) ||
                    Contains(slot.VesselIds, vesselIds) ||
                    MissionTreeTouchesSlot(archive, mission, slot) ||
                    RelatedMissionsTouchSlot(
                        archive,
                        entry.RelatedMissionIds,
                        slot))
                {
                    Add(result, slot.SlotId);
                }
            }
            return result;
        }

        // KSP's split event identifies the detached mission, but not its
        // originating launch slot. A slotted planned separation provides that
        // missing intent. Carry the inferred slot through the later lander
        // sortie facts without mutating the persisted launch aliases.
        private static void ApplyPlannedSeparationLineage(
            MissionArchive archive,
            MissionPlan plan,
            List<MissionPlanTimelineFact> facts)
        {
            var missionSlots = new Dictionary<string, string>(StringComparer.Ordinal);
            var vesselSlots = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedObjectives = new HashSet<string>(StringComparer.Ordinal);
            List<MissionPlanObjective> objectives = new List<MissionPlanObjective>();
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                if (objective != null && !objective.Archived &&
                    objective.Kind == MissionObjectiveKind.Separate &&
                    !string.IsNullOrWhiteSpace(objective.VesselSlotId))
                {
                    objectives.Add(objective);
                }
            }
            objectives.Sort(delegate(MissionPlanObjective left, MissionPlanObjective right)
            {
                return left.Order.CompareTo(right.Order);
            });

            for (int factIndex = 0; factIndex < facts.Count; factIndex++)
            {
                MissionPlanTimelineFact fact = facts[factIndex];
                string inferred;
                if (missionSlots.TryGetValue(fact.MissionId ?? string.Empty, out inferred))
                {
                    Add(fact.VesselSlotIds, inferred);
                }
                for (int vesselIndex = 0;
                    vesselIndex < fact.VesselIds.Count;
                    vesselIndex++)
                {
                    if (vesselSlots.TryGetValue(fact.VesselIds[vesselIndex], out inferred))
                    {
                        Add(fact.VesselSlotIds, inferred);
                    }
                }

                if (fact.Kind == MissionObjectiveKind.Separate)
                {
                    MissionPlanObjective objective = null;
                    for (int objectiveIndex = 0;
                        objectiveIndex < objectives.Count;
                        objectiveIndex++)
                    {
                        MissionPlanObjective candidate = objectives[objectiveIndex];
                        if (!usedObjectives.Contains(candidate.ObjectiveId) &&
                            Contains(fact.VesselSlotIds, candidate.VesselSlotId) &&
                            (string.IsNullOrWhiteSpace(candidate.TargetBody) ||
                             string.Equals(
                                candidate.TargetBody,
                                fact.Body,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            objective = candidate;
                            break;
                        }
                    }
                    if (objective != null)
                    {
                        usedObjectives.Add(objective.ObjectiveId);
                        Add(fact.VesselSlotIds, objective.VesselSlotId);
                        for (int relatedIndex = 0;
                            relatedIndex < fact.RelatedMissionIds.Count;
                            relatedIndex++)
                        {
                            MissionRecord detached = FindMission(
                                archive,
                                fact.RelatedMissionIds[relatedIndex]);
                            if (detached == null)
                            {
                                continue;
                            }
                            missionSlots[detached.MissionId] = objective.VesselSlotId;
                            if (!string.IsNullOrWhiteSpace(detached.VesselId))
                            {
                                vesselSlots[detached.VesselId] = objective.VesselSlotId;
                            }
                        }
                    }
                }

                fact.VesselSlotId = First(fact.VesselSlotIds);
            }
        }

        private static bool RelatedMissionsTouchSlot(
            MissionArchive archive,
            List<string> relatedMissionIds,
            MissionPlanVesselSlot slot)
        {
            if (relatedMissionIds == null)
            {
                return false;
            }
            for (int index = 0; index < relatedMissionIds.Count; index++)
            {
                MissionRecord related = FindMission(archive, relatedMissionIds[index]);
                if (related != null && MissionTreeTouchesSlot(archive, related, slot))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool MissionTreeTouchesSlot(
            MissionArchive archive,
            MissionRecord mission,
            MissionPlanVesselSlot slot)
        {
            if (archive == null || mission == null || slot == null)
            {
                return false;
            }
            for (int index = 0; index < slot.MissionIds.Count; index++)
            {
                MissionRecord seed = FindMission(archive, slot.MissionIds[index]);
                if (seed != null &&
                    (IsAncestor(archive, mission, seed) ||
                     IsAncestor(archive, seed, mission)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAncestor(
            MissionArchive archive,
            MissionRecord ancestor,
            MissionRecord descendant)
        {
            if (archive == null || ancestor == null || descendant == null)
            {
                return false;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            MissionRecord current = descendant;
            while (current != null && seen.Add(current.MissionId))
            {
                if (string.Equals(
                    current.MissionId,
                    ancestor.MissionId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
                current = FindMission(archive, current.ParentMissionId);
            }
            return false;
        }

        private static MissionRecord FindMission(MissionArchive archive, string missionId)
        {
            if (archive == null || archive.Missions == null ||
                string.IsNullOrWhiteSpace(missionId))
            {
                return null;
            }
            for (int index = 0; index < archive.Missions.Count; index++)
            {
                MissionRecord mission = archive.Missions[index];
                if (mission != null && string.Equals(
                    mission.MissionId,
                    missionId,
                    StringComparison.Ordinal))
                {
                    return mission;
                }
            }
            return null;
        }

        private static string FindSingleRelatedRootId(
            MissionArchive archive,
            HashSet<string> related)
        {
            string root = null;
            for (int index = 0; index < archive.Missions.Count; index++)
            {
                MissionRecord mission = archive.Missions[index];
                if (mission == null || !related.Contains(mission.MissionId) ||
                    (!string.IsNullOrWhiteSpace(mission.ParentMissionId) &&
                     related.Contains(mission.ParentMissionId)))
                {
                    continue;
                }
                if (root != null)
                {
                    return null;
                }
                root = mission.MissionId;
            }
            return root;
        }

        private static void AddMissionIfCampaignMatches(
            MissionArchive archive,
            HashSet<string> related,
            string missionId,
            string campaignId)
        {
            MissionRecord mission = FindMission(archive, missionId);
            if (mission != null &&
                (string.IsNullOrWhiteSpace(campaignId) ||
                 string.Equals(
                    campaignId,
                    mission.CampaignId,
                    StringComparison.Ordinal)))
            {
                related.Add(mission.MissionId);
            }
        }

        private static bool MatchesCampaign(MissionPlan plan, MissionRecord mission)
        {
            return plan == null || mission == null ||
                string.IsNullOrWhiteSpace(plan.CampaignId) ||
                string.Equals(
                    plan.CampaignId,
                    mission.CampaignId,
                    StringComparison.Ordinal);
        }

        private static string BuildValue(MissionEvent entry)
        {
            if (entry.VesselIds != null && entry.VesselIds.Count > 1)
            {
                return string.Join("|", entry.VesselIds.ToArray());
            }
            return !string.IsNullOrWhiteSpace(entry.Situation)
                ? entry.Situation
                : entry.Body;
        }

        private static int CompareFacts(
            MissionPlanTimelineFact left,
            MissionPlanTimelineFact right)
        {
            DateTime leftUtc;
            DateTime rightUtc;
            bool hasLeft = DateTime.TryParse(left.RecordedUtc, out leftUtc);
            bool hasRight = DateTime.TryParse(right.RecordedUtc, out rightUtc);
            if (hasLeft && hasRight)
            {
                int utc = leftUtc.CompareTo(rightUtc);
                if (utc != 0)
                {
                    return utc;
                }
            }
            int flight = left.FlightTimeSeconds.CompareTo(right.FlightTimeSeconds);
            return flight != 0
                ? flight
                : string.Compare(left.FactId, right.FactId, StringComparison.Ordinal);
        }

        private static bool EqualsKind(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string First(List<string> values)
        {
            return values == null || values.Count == 0 ? null : values[0];
        }

        private static List<string> Copy(List<string> values)
        {
            var result = new List<string>();
            if (values != null)
            {
                for (int index = 0; index < values.Count; index++)
                {
                    Add(result, values[index]);
                }
            }
            return result;
        }

        private static bool Contains(List<string> left, List<string> right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            for (int index = 0; index < left.Count; index++)
            {
                if (right.Contains(left[index]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(List<string> values, string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                values != null && values.Contains(value);
        }

        private static bool Add(HashSet<string> values, string value)
        {
            return !string.IsNullOrWhiteSpace(value) && values.Add(value);
        }

        private static bool Add(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || values == null ||
                values.Contains(value))
            {
                return false;
            }
            values.Add(value);
            return true;
        }
    }
}
