using System;
using System.Collections.Generic;

namespace ReduxMissionLog
{
    internal sealed class MissionLineageResolver
    {
        public const string KindFlight = "Flight";
        public const string KindCombined = "Combined";
        public const string KindSortie = "Sortie";
        public const string RelationDockedComponent = "DockedComponent";
        public const string RelationSeparatedCraft = "SeparatedCraft";
        public const string RelationManual = "Manual";

        private readonly MissionArchive _archive;

        public MissionLineageResolver(MissionArchive archive)
        {
            _archive = archive ?? throw new ArgumentNullException("archive");
        }

        public MissionRecord FindById(string missionId)
        {
            if (string.IsNullOrWhiteSpace(missionId))
            {
                return null;
            }
            for (int index = 0; index < _archive.Missions.Count; index++)
            {
                MissionRecord mission = _archive.Missions[index];
                if (string.Equals(mission.MissionId, missionId, StringComparison.Ordinal))
                {
                    return mission;
                }
            }
            return null;
        }

        public MissionRecord FindTrackedVessel(string campaignId, string vesselId)
        {
            if (string.IsNullOrWhiteSpace(vesselId))
            {
                return null;
            }
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (SameCampaign(mission, campaignId) && mission.IsActive &&
                    Contains(mission.TrackedVesselIds, vesselId))
                {
                    return mission;
                }
            }
            return null;
        }

        public MissionRecord FindAlias(string campaignId, string vesselId)
        {
            if (string.IsNullOrWhiteSpace(vesselId))
            {
                return null;
            }
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (SameCampaign(mission, campaignId) &&
                    (Contains(mission.VesselIds, vesselId) ||
                     string.Equals(mission.VesselId, vesselId, StringComparison.Ordinal)))
                {
                    return mission;
                }
            }
            return null;
        }

        public MissionRecord FindTravelAlias(string campaignId, string travelObjectId)
        {
            if (string.IsNullOrWhiteSpace(travelObjectId))
            {
                return null;
            }
            for (int index = _archive.Missions.Count - 1; index >= 0; index--)
            {
                MissionRecord mission = _archive.Missions[index];
                if (SameCampaign(mission, campaignId) &&
                    Contains(mission.TravelObjectIds, travelObjectId))
                {
                    return mission;
                }
            }
            return null;
        }

        public List<MissionRecord> GetRoots(string campaignId)
        {
            var result = new List<MissionRecord>();
            for (int index = 0; index < _archive.Missions.Count; index++)
            {
                MissionRecord mission = _archive.Missions[index];
                if (string.IsNullOrWhiteSpace(mission.ParentMissionId) &&
                    (campaignId == null || SameCampaign(mission, campaignId)))
                {
                    result.Add(mission);
                }
            }
            return result;
        }

        public List<MissionRecord> GetChildren(MissionRecord parent)
        {
            var result = new List<MissionRecord>();
            if (parent == null)
            {
                return result;
            }
            for (int index = 0; index < _archive.Missions.Count; index++)
            {
                MissionRecord mission = _archive.Missions[index];
                if (string.Equals(mission.ParentMissionId, parent.MissionId,
                    StringComparison.Ordinal))
                {
                    result.Add(mission);
                }
            }
            return result;
        }

        public MissionRecord GetParent(MissionRecord mission)
        {
            return mission == null ? null : FindById(mission.ParentMissionId);
        }

        public MissionRecord GetRoot(MissionRecord mission)
        {
            if (mission == null)
            {
                return null;
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            MissionRecord current = mission;
            while (!string.IsNullOrWhiteSpace(current.ParentMissionId) &&
                visited.Add(current.MissionId))
            {
                MissionRecord parent = GetParent(current);
                if (parent == null)
                {
                    break;
                }
                current = parent;
            }
            return current;
        }

        public MissionRecord CreateMission(
            string missionId,
            string campaignId,
            string campaignName,
            string vesselId,
            string travelObjectId,
            string vesselName,
            string title,
            string kind,
            string parentMissionId,
            string parentRelation,
            string startBody,
            string startSituation,
            MissionMoment moment,
            IList<string> crew,
            bool observedAtDocking)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
            {
                throw new InvalidOperationException("A mission campaign ID is required.");
            }
            if (string.IsNullOrWhiteSpace(vesselId))
            {
                throw new InvalidOperationException("A mission vessel ID is required.");
            }
            if (string.IsNullOrWhiteSpace(missionId))
            {
                missionId = Guid.NewGuid().ToString("N");
            }
            if (FindById(missionId) != null)
            {
                throw new InvalidOperationException("Mission ID already exists: " + missionId);
            }

            var mission = new MissionRecord
            {
                MissionId = missionId,
                MissionKind = string.IsNullOrWhiteSpace(kind) ? KindFlight : kind,
                CampaignId = campaignId,
                CampaignName = campaignName ?? string.Empty,
                ParentMissionId = parentMissionId,
                ParentRelation = parentRelation,
                VesselId = vesselId,
                VesselName = string.IsNullOrWhiteSpace(vesselName) ? "Unnamed vessel" : vesselName,
                Title = string.IsNullOrWhiteSpace(title) ? vesselName : title,
                Status = "Active",
                StartedUtc = MomentUtc(moment),
                StartBody = startBody ?? string.Empty,
                LastBody = startBody ?? string.Empty,
                LastSituation = startSituation ?? string.Empty,
                Notes = string.Empty
            };
            AddUnique(mission.VesselIds, vesselId);
            AddUnique(mission.TravelObjectIds, travelObjectId);
            AddUnique(mission.TrackedVesselIds, vesselId);
            mission.TrackedTravelObjectId = travelObjectId;
            AddUnique(mission.VisitedBodies, startBody);
            CopyUnique(mission.Crew, crew);
            AddEvent(
                mission,
                observedAtDocking ? "observed_at_docking" : "mission_started",
                observedAtDocking ? "First observed at docking" : "Mission started",
                moment,
                null,
                new string[0],
                new[] { vesselId });
            _archive.Missions.Add(mission);
            return mission;
        }

        public MissionRecord Dock(
            string campaignId,
            string leftVesselId,
            string rightVesselId,
            string resultVesselId,
            string resultTravelObjectId,
            string resultVesselName,
            MissionMoment moment,
            string operationId,
            bool manual)
        {
            if (HasOperation(operationId))
            {
                return FindTrackedVessel(campaignId, resultVesselId) ??
                    FindAlias(campaignId, resultVesselId);
            }
            MissionRecord left = FindTrackedVessel(campaignId, leftVesselId);
            MissionRecord right = FindTrackedVessel(campaignId, rightVesselId);
            if (left == null || right == null)
            {
                throw new InvalidOperationException(
                    "Both docking participants must have active mission bindings.");
            }
            if (ReferenceEquals(left, right))
            {
                BindSingleVessel(left, resultVesselId, resultTravelObjectId, resultVesselName);
                AddEvent(left, "docking", "Docking completed within mission",
                    moment, operationId, new[] { left.MissionId },
                    new[] { leftVesselId, rightVesselId, resultVesselId });
                return left;
            }

            MissionRecord leftRoot = GetRoot(left);
            MissionRecord rightRoot = GetRoot(right);
            if (ReferenceEquals(leftRoot, rightRoot))
            {
                return RejoinSameTree(
                    left,
                    right,
                    leftRoot,
                    resultVesselId,
                    resultTravelObjectId,
                    resultVesselName,
                    moment,
                    operationId,
                    manual);
            }

            var combined = new MissionRecord
            {
                MissionId = Guid.NewGuid().ToString("N"),
                MissionKind = KindCombined,
                CampaignId = campaignId,
                CampaignName = left.CampaignName,
                VesselId = resultVesselId,
                VesselName = string.IsNullOrWhiteSpace(resultVesselName)
                    ? left.VesselName + " + " + right.VesselName
                    : resultVesselName,
                Title = string.IsNullOrWhiteSpace(resultVesselName)
                    ? left.Title + " + " + right.Title
                    : resultVesselName,
                Status = "Active",
                StartedUtc = MomentUtc(moment),
                StartBody = moment == null ? left.LastBody : moment.Body,
                LastBody = moment == null ? left.LastBody : moment.Body,
                LastSituation = moment == null ? left.LastSituation : moment.Situation,
                Notes = string.Empty
            };
            AddUnique(combined.VesselIds, resultVesselId);
            AddUnique(combined.TravelObjectIds, resultTravelObjectId);
            AddUnique(combined.TrackedVesselIds, resultVesselId);
            combined.TrackedTravelObjectId = resultTravelObjectId;
            leftRoot.ParentMissionId = combined.MissionId;
            leftRoot.ParentRelation = RelationDockedComponent;
            rightRoot.ParentMissionId = combined.MissionId;
            rightRoot.ParentRelation = RelationDockedComponent;
            JoinParticipant(left, combined, moment, operationId);
            JoinParticipant(right, combined, moment, operationId);
            AddEvent(
                combined,
                manual ? "missions_combined_manually" : "missions_combined",
                manual ? "Missions combined manually" : "Overarching mission formed by docking",
                moment,
                operationId,
                new[] { left.MissionId, right.MissionId },
                new[] { leftVesselId, rightVesselId, resultVesselId });
            _archive.Missions.Add(combined);
            return combined;
        }

        public MissionRecord Split(
            string campaignId,
            string sourceVesselId,
            string continuationVesselId,
            string continuationTravelObjectId,
            string continuationVesselName,
            string detachedVesselId,
            string detachedTravelObjectId,
            string detachedVesselName,
            MissionMoment moment,
            string operationId,
            IList<string> detachedCrew)
        {
            if (HasOperation(operationId))
            {
                return FindTrackedVessel(campaignId, detachedVesselId) ??
                    FindAlias(campaignId, detachedVesselId);
            }
            if (string.IsNullOrWhiteSpace(sourceVesselId) ||
                string.IsNullOrWhiteSpace(continuationVesselId) ||
                string.IsNullOrWhiteSpace(detachedVesselId))
            {
                throw new InvalidOperationException("Split vessel IDs must be non-empty.");
            }
            if (string.Equals(continuationVesselId, detachedVesselId,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The continuing and detached craft must have different vessel IDs.");
            }
            MissionRecord source = FindTrackedVessel(campaignId, sourceVesselId) ??
                FindTrackedVessel(campaignId, continuationVesselId) ??
                FindTrackedVessel(campaignId, detachedVesselId);
            if (source == null)
            {
                MissionRecord continuationAlias = FindTravelAlias(
                    campaignId, continuationTravelObjectId);
                MissionRecord detachedAlias = FindTravelAlias(
                    campaignId, detachedTravelObjectId);
                if (continuationAlias != null && continuationAlias.IsActive)
                {
                    source = continuationAlias;
                }
                if (detachedAlias != null && detachedAlias.IsActive)
                {
                    if (source != null && !ReferenceEquals(source, detachedAlias))
                    {
                        throw new InvalidOperationException(
                            "The split outputs match two different active mission aliases.");
                    }
                    source = detachedAlias;
                }
            }
            if (source == null)
            {
                throw new InvalidOperationException("The split source mission is not tracked.");
            }
            if (source.TrackedVesselIds.Count == 1 &&
                !string.Equals(source.TrackedVesselIds[0], sourceVesselId,
                    StringComparison.Ordinal) &&
                !string.Equals(source.TrackedVesselIds[0], continuationVesselId,
                    StringComparison.Ordinal) &&
                !string.Equals(source.TrackedVesselIds[0], detachedVesselId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The matched split mission still tracks a different live craft.");
            }
            MissionRecord continuationOwner = FindTrackedVessel(
                campaignId, continuationVesselId);
            if (continuationOwner != null && !ReferenceEquals(continuationOwner, source))
            {
                throw new InvalidOperationException(
                    "The continuing vessel is already owned by another mission.");
            }
            MissionRecord detachedOwner = FindTrackedVessel(campaignId, detachedVesselId);
            if (detachedOwner != null && !ReferenceEquals(detachedOwner, source))
            {
                throw new InvalidOperationException(
                    "The detached vessel is already owned by another mission.");
            }

            MissionRecord detached = FindReusableDescendant(
                source, detachedVesselId, detachedTravelObjectId);
            BindSingleVessel(source, continuationVesselId,
                continuationTravelObjectId, continuationVesselName);
            if (detached != null)
            {
                detached.Status = "Active";
                detached.EndedUtc = null;
                BindSingleVessel(detached, detachedVesselId,
                    detachedTravelObjectId, detachedVesselName);
                AddEvent(detached, "sub_mission_resumed", "Sub-mission separated again",
                    moment, operationId, new[] { source.MissionId },
                    new[] { continuationVesselId, detachedVesselId });
            }
            else
            {
                detached = CreateMission(
                    null,
                    campaignId,
                    source.CampaignName,
                    detachedVesselId,
                    detachedTravelObjectId,
                    detachedVesselName,
                    detachedVesselName + " sortie",
                    KindSortie,
                    source.MissionId,
                    RelationSeparatedCraft,
                    moment == null ? source.LastBody : moment.Body,
                    moment == null ? source.LastSituation : moment.Situation,
                    moment,
                    detachedCrew,
                    false);
                detached.Events[0].Kind = "sub_mission_started";
                detached.Events[0].Title = "Separated from " + source.Title;
                detached.Events[0].OperationId = operationId;
                AddUnique(detached.Events[0].RelatedMissionIds, source.MissionId);
            }
            AddEvent(source, "sub_mission_separated",
                detached.Title + " separated as a sub-mission",
                moment, operationId, new[] { detached.MissionId },
                new[] { continuationVesselId, detachedVesselId });
            return detached;
        }

        public void Reparent(
            MissionRecord child,
            MissionRecord parent,
            string relation,
            MissionMoment moment,
            string operationId)
        {
            if (HasOperation(operationId))
            {
                return;
            }
            RequireSameArchive(child, parent);
            if (!SameCampaign(child, parent.CampaignId))
            {
                throw new InvalidOperationException("Missions from different campaigns cannot be linked.");
            }
            if (ReferenceEquals(child, parent) || IsAncestor(child, parent))
            {
                throw new InvalidOperationException("That relationship would create a mission-tree cycle.");
            }
            if (string.Equals(child.ParentMissionId, parent.MissionId, StringComparison.Ordinal))
            {
                return;
            }
            child.ParentMissionId = parent.MissionId;
            child.ParentRelation = string.IsNullOrWhiteSpace(relation) ? RelationManual : relation;
            AddEvent(child, "mission_adopted", "Made a sub-mission of " + parent.Title,
                moment, operationId, new[] { parent.MissionId });
            AddEvent(parent, "sub_mission_adopted", "Adopted " + child.Title,
                moment, operationId, new[] { child.MissionId });
        }

        public void Unlink(MissionRecord mission, MissionMoment moment, string operationId)
        {
            if (HasOperation(operationId))
            {
                return;
            }
            if (mission == null || !_archive.Missions.Contains(mission))
            {
                throw new InvalidOperationException("The mission is not in this archive.");
            }
            MissionRecord parent = GetParent(mission);
            if (parent == null)
            {
                return;
            }
            mission.ParentMissionId = null;
            mission.ParentRelation = null;
            AddEvent(mission, "mission_unlinked", "Moved to the top level",
                moment, operationId, new[] { parent.MissionId });
            AddEvent(parent, "sub_mission_unlinked", "Unlinked " + mission.Title,
                moment, operationId, new[] { mission.MissionId });
        }

        public void TrackAsMission(
            MissionRecord mission,
            string campaignId,
            string vesselId,
            string travelObjectId,
            string vesselName,
            MissionMoment moment,
            string operationId)
        {
            if (HasOperation(operationId))
            {
                return;
            }
            if (mission == null || !SameCampaign(mission, campaignId))
            {
                throw new InvalidOperationException("The selected mission is not in the active campaign.");
            }
            if (mission.TrackedVesselIds.Count == 1 &&
                !string.Equals(mission.TrackedVesselIds[0], vesselId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The selected mission already tracks a different live craft.");
            }
            MissionRecord previous = FindTrackedVessel(campaignId, vesselId);
            if (previous != null && !ReferenceEquals(previous, mission))
            {
                previous.TrackedVesselIds.Remove(vesselId);
                if (previous.TrackedVesselIds.Count == 0)
                {
                    previous.TrackedTravelObjectId = null;
                    previous.Status = "Joined";
                    previous.EndedUtc = MomentUtc(moment);
                    previous.NeedsReview = true;
                    AddEvent(previous, "vessel_binding_reassigned",
                        "Live vessel reassigned manually to " + mission.Title,
                        moment, operationId, new[] { mission.MissionId }, new[] { vesselId });
                }
            }
            mission.Status = "Active";
            BindSingleVessel(mission, vesselId, travelObjectId, vesselName);
            AddEvent(mission, "vessel_binding_repaired", "Current vessel assigned manually",
                moment, operationId, new[] { mission.MissionId }, new[] { vesselId });
        }

        public void RebindTravelIdentity(
            MissionRecord mission,
            string campaignId,
            string vesselId,
            string travelObjectId,
            string vesselName,
            MissionMoment moment,
            string operationId)
        {
            if (HasOperation(operationId))
            {
                if (mission != null && mission.TrackedVesselIds.Count == 1 &&
                    string.Equals(mission.TrackedVesselIds[0], vesselId,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }
            if (mission == null || !SameCampaign(mission, campaignId) || !mission.IsActive ||
                string.IsNullOrWhiteSpace(travelObjectId) ||
                !Contains(mission.TravelObjectIds, travelObjectId))
            {
                throw new InvalidOperationException(
                    "The vessel identity does not match an active mission travel alias.");
            }
            MissionRecord owner = FindTrackedVessel(campaignId, vesselId);
            if (owner != null && !ReferenceEquals(owner, mission))
            {
                throw new InvalidOperationException(
                    "The replacement vessel ID is already owned by another mission.");
            }
            BindSingleVessel(mission, vesselId, travelObjectId, vesselName);
            AddEvent(mission, "vessel_identity_changed",
                "Vessel identity changed while mission lineage continued",
                moment, operationId, new[] { mission.MissionId }, new[] { vesselId });
        }

        public void MarkNeedsReview(
            MissionRecord mission,
            string reason,
            MissionMoment moment,
            string operationId,
            IList<string> vesselIds)
        {
            if (mission == null || HasOperation(operationId))
            {
                return;
            }
            mission.NeedsReview = true;
            AddEvent(mission, "lineage_needs_review",
                string.IsNullOrWhiteSpace(reason) ? "Mission lineage needs review" : reason,
                moment, operationId, new[] { mission.MissionId }, vesselIds);
        }

        public MissionAggregate Aggregate(MissionRecord root)
        {
            var result = new MissionAggregate();
            AggregateInto(root, result, new HashSet<string>(StringComparer.Ordinal));
            result.Events.Sort((left, right) =>
                string.Compare(left.RecordedUtc, right.RecordedUtc, StringComparison.Ordinal));
            return result;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < _archive.Missions.Count; index++)
            {
                MissionRecord mission = _archive.Missions[index];
                if (mission == null || string.IsNullOrWhiteSpace(mission.MissionId))
                {
                    errors.Add("Mission at index " + index + " has no ID.");
                    continue;
                }
                if (!ids.Add(mission.MissionId))
                {
                    errors.Add("Duplicate mission ID: " + mission.MissionId);
                }
                if (mission.TrackedVesselIds.Count > 1)
                {
                    errors.Add("Mission tracks more than one vessel: " + mission.MissionId);
                }
                if (!mission.IsActive && mission.TrackedVesselIds.Count > 0)
                {
                    errors.Add("Inactive mission tracks a live vessel: " + mission.MissionId);
                }
                if (mission.IsActive && mission.TrackedVesselIds.Count == 0 &&
                    !mission.NeedsReview)
                {
                    errors.Add("Active mission has no live vessel binding: " + mission.MissionId);
                }
                if (mission.TrackedVesselIds.Count == 0 &&
                    !string.IsNullOrWhiteSpace(mission.TrackedTravelObjectId))
                {
                    errors.Add("Mission has a travel binding without a live vessel: " +
                        mission.MissionId);
                }
                for (int vesselIndex = 0; vesselIndex < mission.TrackedVesselIds.Count; vesselIndex++)
                {
                    string key = mission.CampaignId + "|" + mission.TrackedVesselIds[vesselIndex];
                    string owner;
                    if (owners.TryGetValue(key, out owner) && owner != mission.MissionId)
                    {
                        errors.Add("Vessel is owned by two missions: " + key);
                    }
                    else
                    {
                        owners[key] = mission.MissionId;
                    }
                }
            }
            for (int index = 0; index < _archive.Missions.Count; index++)
            {
                MissionRecord mission = _archive.Missions[index];
                if (mission == null || string.IsNullOrWhiteSpace(mission.ParentMissionId))
                {
                    continue;
                }
                MissionRecord parent = FindById(mission.ParentMissionId);
                if (parent == null)
                {
                    errors.Add("Missing parent for mission: " + mission.MissionId);
                }
                else if (!SameCampaign(mission, parent.CampaignId))
                {
                    errors.Add("Cross-campaign parent for mission: " + mission.MissionId);
                }
                else if (HasParentCycle(mission))
                {
                    errors.Add("Parent cycle at mission: " + mission.MissionId);
                }
            }
            return errors;
        }

        private MissionRecord RejoinSameTree(
            MissionRecord left,
            MissionRecord right,
            MissionRecord root,
            string resultVesselId,
            string resultTravelObjectId,
            string resultVesselName,
            MissionMoment moment,
            string operationId,
            bool manual)
        {
            MissionRecord survivor;
            MissionRecord joined;
            if (IsAncestor(left, right))
            {
                survivor = left;
                joined = right;
            }
            else if (IsAncestor(right, left))
            {
                survivor = right;
                joined = left;
            }
            else
            {
                MissionRecord common = LowestCommonAncestor(left, right) ?? root;
                var combined = new MissionRecord
                {
                    MissionId = Guid.NewGuid().ToString("N"),
                    MissionKind = KindCombined,
                    CampaignId = root.CampaignId,
                    CampaignName = root.CampaignName,
                    ParentMissionId = common.MissionId,
                    ParentRelation = RelationDockedComponent,
                    VesselId = resultVesselId,
                    VesselName = resultVesselName,
                    Title = resultVesselName,
                    Status = "Active",
                    StartedUtc = MomentUtc(moment),
                    StartBody = moment == null ? root.LastBody : moment.Body,
                    LastBody = moment == null ? root.LastBody : moment.Body,
                    LastSituation = moment == null ? root.LastSituation : moment.Situation,
                    Notes = string.Empty
                };
                AddUnique(combined.VesselIds, resultVesselId);
                AddUnique(combined.TravelObjectIds, resultTravelObjectId);
                AddUnique(combined.TrackedVesselIds, resultVesselId);
                combined.TrackedTravelObjectId = resultTravelObjectId;
                left.ParentMissionId = combined.MissionId;
                left.ParentRelation = RelationDockedComponent;
                right.ParentMissionId = combined.MissionId;
                right.ParentRelation = RelationDockedComponent;
                JoinParticipant(left, combined, moment, operationId);
                JoinParticipant(right, combined, moment, operationId);
                AddEvent(combined,
                    manual ? "missions_combined_manually" : "sibling_missions_combined",
                    "Sub-missions combined",
                    moment,
                    operationId,
                    new[] { left.MissionId, right.MissionId },
                    new[] { resultVesselId });
                _archive.Missions.Add(combined);
                return combined;
            }

            joined.Status = "Rejoined";
            joined.EndedUtc = MomentUtc(moment);
            joined.TrackedVesselIds.Clear();
            joined.TrackedTravelObjectId = null;
            BindSingleVessel(survivor, resultVesselId,
                resultTravelObjectId, resultVesselName);
            AddEvent(joined, "sub_mission_rejoined", "Rejoined " + survivor.Title,
                moment, operationId, new[] { survivor.MissionId }, new[] { resultVesselId });
            AddEvent(survivor, "sub_mission_recovered", joined.Title + " rejoined",
                moment, operationId, new[] { joined.MissionId }, new[] { resultVesselId });
            return survivor;
        }

        private void JoinParticipant(
            MissionRecord participant,
            MissionRecord combined,
            MissionMoment moment,
            string operationId)
        {
            participant.Status = "Joined";
            participant.EndedUtc = MomentUtc(moment);
            participant.TrackedVesselIds.Clear();
            participant.TrackedTravelObjectId = null;
            AddEvent(participant, "joined_overarching_mission",
                "Joined " + combined.Title,
                moment, operationId, new[] { combined.MissionId });
        }

        private MissionRecord FindReusableDescendant(
            MissionRecord source,
            string vesselId,
            string travelObjectId)
        {
            MissionRecord root = GetRoot(source);
            MissionRecord alias = FindTravelAlias(source.CampaignId, travelObjectId) ??
                FindAlias(source.CampaignId, vesselId);
            if (alias == null || ReferenceEquals(alias, source) ||
                !ReferenceEquals(GetRoot(alias), root) || !IsAncestor(source, alias))
            {
                return null;
            }
            if (alias.IsActive ||
                (!string.Equals(alias.Status, "Joined", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(alias.Status, "Rejoined", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            return alias;
        }

        private void BindSingleVessel(
            MissionRecord mission,
            string vesselId,
            string travelObjectId,
            string vesselName)
        {
            if (string.IsNullOrWhiteSpace(vesselId))
            {
                throw new InvalidOperationException("A result vessel ID is required.");
            }
            mission.TrackedVesselIds.Clear();
            mission.TrackedVesselIds.Add(vesselId);
            mission.TrackedTravelObjectId = travelObjectId;
            AddUnique(mission.VesselIds, vesselId);
            AddUnique(mission.TravelObjectIds, travelObjectId);
            mission.VesselId = vesselId;
            if (!string.IsNullOrWhiteSpace(vesselName))
            {
                mission.VesselName = vesselName;
            }
            mission.Status = "Active";
            mission.EndedUtc = null;
        }

        private bool HasOperation(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return false;
            }
            for (int missionIndex = 0; missionIndex < _archive.Missions.Count; missionIndex++)
            {
                MissionRecord mission = _archive.Missions[missionIndex];
                for (int eventIndex = 0; eventIndex < mission.Events.Count; eventIndex++)
                {
                    if (string.Equals(mission.Events[eventIndex].OperationId, operationId,
                        StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsAncestor(MissionRecord ancestor, MissionRecord descendant)
        {
            MissionRecord current = GetParent(descendant);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current.MissionId))
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
                current = GetParent(current);
            }
            return false;
        }

        private MissionRecord LowestCommonAncestor(MissionRecord left, MissionRecord right)
        {
            var leftAncestors = new HashSet<string>(StringComparer.Ordinal);
            MissionRecord current = left;
            while (current != null && leftAncestors.Add(current.MissionId))
            {
                current = GetParent(current);
            }
            current = right;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current.MissionId))
            {
                if (leftAncestors.Contains(current.MissionId))
                {
                    return current;
                }
                current = GetParent(current);
            }
            return null;
        }

        private bool HasParentCycle(MissionRecord mission)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            MissionRecord current = mission;
            while (current != null)
            {
                if (!visited.Add(current.MissionId))
                {
                    return true;
                }
                current = GetParent(current);
            }
            return false;
        }

        private void AggregateInto(
            MissionRecord mission,
            MissionAggregate aggregate,
            HashSet<string> visited)
        {
            if (mission == null || !visited.Add(mission.MissionId))
            {
                return;
            }
            aggregate.MaximumAltitudeMeters = Math.Max(
                aggregate.MaximumAltitudeMeters, mission.MaximumAltitudeMeters);
            aggregate.MaximumSpeedMetersPerSecond = Math.Max(
                aggregate.MaximumSpeedMetersPerSecond, mission.MaximumSpeedMetersPerSecond);
            aggregate.MaximumGForce = Math.Max(aggregate.MaximumGForce, mission.MaximumGForce);
            CopyUnique(aggregate.Crew, mission.Crew);
            CopyUnique(aggregate.VisitedBodies, mission.VisitedBodies);
            aggregate.Events.AddRange(mission.Events);
            List<MissionRecord> children = GetChildren(mission);
            for (int index = 0; index < children.Count; index++)
            {
                AggregateInto(children[index], aggregate, visited);
            }
        }

        private static void AddEvent(
            MissionRecord mission,
            string kind,
            string title,
            MissionMoment moment,
            string operationId,
            IList<string> relatedMissionIds,
            IList<string> vesselIds = null)
        {
            var entry = new MissionEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                Kind = kind,
                Title = title,
                RecordedUtc = MomentUtc(moment),
                FlightTimeSeconds = moment == null ? 0.0 : moment.FlightTimeSeconds,
                Body = moment == null ? string.Empty : moment.Body,
                Situation = moment == null ? string.Empty : moment.Situation
            };
            CopyUnique(entry.RelatedMissionIds, relatedMissionIds);
            CopyUnique(entry.VesselIds, vesselIds);
            mission.Events.Add(entry);
        }

        private void RequireSameArchive(MissionRecord first, MissionRecord second)
        {
            if (first == null || second == null ||
                !_archive.Missions.Contains(first) || !_archive.Missions.Contains(second))
            {
                throw new InvalidOperationException("Both missions must belong to this archive.");
            }
        }

        private static bool SameCampaign(MissionRecord mission, string campaignId)
        {
            return mission != null && string.Equals(
                mission.CampaignId, campaignId, StringComparison.Ordinal);
        }

        private static string MomentUtc(MissionMoment moment)
        {
            return moment == null || string.IsNullOrWhiteSpace(moment.RecordedUtc)
                ? DateTime.UtcNow.ToString("o")
                : moment.RecordedUtc;
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

        internal static void AddUnique(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value) || Contains(values, value))
            {
                return;
            }
            values.Add(value);
        }

        private static void CopyUnique(List<string> destination, IList<string> source)
        {
            if (source == null)
            {
                return;
            }
            for (int index = 0; index < source.Count; index++)
            {
                AddUnique(destination, source[index]);
            }
        }
    }
}
