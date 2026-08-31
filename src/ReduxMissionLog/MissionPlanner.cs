using System;
using System.Collections.Generic;

namespace ReduxMissionLog
{
    // Domain-only planner. KSP event translation, UI, and persistence remain adapters.
    public sealed class MissionPlanner
    {
        private readonly Action<MissionPlanState> _persist;
        private readonly Func<string> _idFactory;
        private readonly Func<string> _utcNow;
        private MissionPlanState _state;

        public MissionPlanner(
            MissionPlanState state,
            Action<MissionPlanState> persist = null,
            Func<string> idFactory = null,
            Func<string> utcNow = null)
        {
            _persist = persist;
            _idFactory = idFactory ?? DefaultIdFactory;
            _utcNow = utcNow ?? DefaultUtcNow;
            _state = state ?? new MissionPlanState();
            NormalizeState(_state);
            EnsureValidState(_state);
        }

        public MissionPlanner(
            IMissionPlanStore store,
            Func<string> idFactory = null,
            Func<string> utcNow = null)
        {
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            _persist = store.Save;
            _idFactory = idFactory ?? DefaultIdFactory;
            _utcNow = utcNow ?? DefaultUtcNow;
            _state = store.Load() ?? new MissionPlanState();
            NormalizeState(_state);
            EnsureValidState(_state);
        }

        public MissionPlanState State
        {
            get { return _state; }
        }

        // UI adapters use this monotonic value to avoid rebuilding focused
        // controls and scroll views when the observable planner state did not
        // change. Persistence alone does not advance the revision.
        public long Revision { get; private set; }

        public void ReplaceState(MissionPlanState state, bool save)
        {
            MissionPlanState replacement = state ?? new MissionPlanState();
            NormalizeState(replacement);
            EnsureValidState(replacement);
            _state = replacement;
            Revision++;
            if (save)
            {
                SaveNow();
            }
        }

        public void SaveNow()
        {
            NormalizeState(_state);
            EnsureValidState(_state);
            if (_persist != null)
            {
                _persist(_state);
            }
        }

        public MissionPlan GetPlan(string planId)
        {
            return RequirePlan(planId);
        }

        public MissionPlan CreatePlan(string campaignId, string title, string notes = null)
        {
            RequireText(title, "title");
            string now = _utcNow();
            MissionPlan plan = new MissionPlan
            {
                PlanId = NewId("plan"),
                CampaignId = Clean(campaignId),
                Title = Clean(title),
                Notes = Clean(notes),
                Status = MissionPlanStatus.Draft,
                CreatedUtc = now,
                UpdatedUtc = now
            };

            _state.Plans.Add(plan);
            Commit(plan);
            return plan;
        }

        public void UpdatePlan(string planId, string title, string notes)
        {
            RequireText(title, "title");
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            plan.Title = Clean(title);
            plan.Notes = Clean(notes);
            Commit(plan);
        }

        // Plans are never hard-deleted. Abandoned plans retain their full history.
        public void AbandonPlan(string planId, string reason = null)
        {
            MissionPlan plan = RequirePlan(planId);
            if (plan.Status == MissionPlanStatus.Completed)
            {
                throw new InvalidOperationException("A completed plan cannot be abandoned.");
            }

            if (plan.Status == MissionPlanStatus.Abandoned)
            {
                return;
            }

            plan.Status = MissionPlanStatus.Abandoned;
            plan.EndedUtc = _utcNow();
            if (!String.IsNullOrWhiteSpace(reason))
            {
                plan.Notes = AppendNote(plan.Notes, reason);
            }

            Commit(plan);
        }

        public void SetPlanArchived(string planId, bool archived)
        {
            MissionPlan plan = RequirePlan(planId);
            if (archived && plan.Status == MissionPlanStatus.Active)
            {
                throw new InvalidOperationException("Abandon or complete an active plan before archiving it.");
            }

            plan.Archived = archived;
            Commit(plan);
        }

        public MissionPlanVesselSlot AddVesselSlot(
            string planId,
            string name,
            string role,
            bool required = true)
        {
            RequireText(name, "name");
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanVesselSlot slot = new MissionPlanVesselSlot
            {
                SlotId = NewId("slot"),
                Order = plan.VesselSlots.Count,
                Name = Clean(name),
                Role = Clean(role),
                Required = required
            };

            plan.VesselSlots.Add(slot);
            Commit(plan);
            return slot;
        }

        public void UpdateVesselSlot(
            string planId,
            string slotId,
            string name,
            string role,
            bool required)
        {
            RequireText(name, "name");
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            slot.Name = Clean(name);
            slot.Role = Clean(role);
            slot.Required = required;
            Commit(plan);
        }

        public void ReorderVesselSlot(string planId, string slotId, int newIndex)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            Reorder(plan.VesselSlots, slot, newIndex);
            Commit(plan);
        }

        public void SetVesselSlotArchived(string planId, string slotId, bool archived)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            if (archived)
            {
                for (int index = 0; index < plan.Objectives.Count; index++)
                {
                    MissionPlanObjective objective = plan.Objectives[index];
                    if (!objective.Archived &&
                        (Same(objective.VesselSlotId, slot.SlotId) ||
                         Same(objective.RelatedVesselSlotId, slot.SlotId)))
                    {
                        throw new InvalidOperationException(
                            "Archive or move objectives that use the vessel slot first.");
                    }
                }
            }

            slot.Archived = archived;
            Commit(plan);
        }

        public void SelectSavedVehicle(
            string planId,
            string slotId,
            string savedVehicleId,
            string savedVehicleName,
            string savedVehiclePath = null,
            string savedVehicleLocation = null)
        {
            if (String.IsNullOrWhiteSpace(savedVehicleId) &&
                String.IsNullOrWhiteSpace(savedVehiclePath))
            {
                throw new ArgumentException(
                    "A saved vehicle ID or path is required.",
                    "savedVehicleId");
            }

            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            EnsureActiveSlot(slot);
            if (SlotHasBinding(slot))
            {
                throw new InvalidOperationException(
                    "Clear the existing mission link before changing its saved vehicle.");
            }
            slot.SavedVehicleId = Clean(savedVehicleId);
            slot.SavedVehicleName = Clean(savedVehicleName);
            slot.SavedVehiclePath = Clean(savedVehiclePath);
            slot.SavedVehicleLocation = Clean(savedVehicleLocation);
            slot.LaunchRequestedUtc = String.Empty;
            slot.LaunchState = String.Empty;
            slot.LaunchError = String.Empty;
            Commit(plan);
        }

        public void ClearSavedVehicle(string planId, string slotId)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            if (SlotHasBinding(slot))
            {
                throw new InvalidOperationException(
                    "Clear the existing mission link before clearing its saved vehicle.");
            }
            slot.SavedVehicleId = String.Empty;
            slot.SavedVehicleName = String.Empty;
            slot.SavedVehiclePath = String.Empty;
            slot.SavedVehicleLocation = String.Empty;
            slot.LaunchRequestedUtc = String.Empty;
            slot.LaunchState = String.Empty;
            slot.LaunchError = String.Empty;
            Commit(plan);
        }

        // The UI/runtime owns launching. The domain records intent and its neutral state.
        public void RecordLaunchRequest(
            string planId,
            string slotId,
            string launchState = "Requested")
        {
            MissionPlan plan = RequirePlan(planId);
            if (plan.Status != MissionPlanStatus.Active)
            {
                throw new InvalidOperationException("Activate the plan before requesting a launch.");
            }

            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            EnsureActiveSlot(slot);
            if (SlotHasBinding(slot))
            {
                throw new InvalidOperationException(
                    "This planned vessel is already linked to a mission.");
            }
            if (String.IsNullOrWhiteSpace(slot.SavedVehicleId) &&
                String.IsNullOrWhiteSpace(slot.SavedVehiclePath))
            {
                throw new InvalidOperationException("Select a saved vehicle before requesting launch.");
            }

            slot.LaunchRequestedUtc = _utcNow();
            slot.LaunchState = Clean(launchState);
            slot.LaunchError = String.Empty;
            Commit(plan);
        }

        public void RecordLaunchResult(
            string planId,
            string slotId,
            string launchState,
            string launchError = null)
        {
            RequireText(launchState, "launchState");
            MissionPlan plan = RequirePlan(planId);
            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            EnsureActiveSlot(slot);
            slot.LaunchState = Clean(launchState);
            slot.LaunchError = Clean(launchError);
            Commit(plan);
        }

        public void BindLaunch(
            string planId,
            string slotId,
            string missionId,
            string vesselId,
            string recordedUtc = null)
        {
            if (String.IsNullOrWhiteSpace(missionId) && String.IsNullOrWhiteSpace(vesselId))
            {
                throw new ArgumentException("A mission ID or vessel ID is required.", "missionId");
            }

            MissionPlan plan = RequirePlan(planId);
            if (plan.Status != MissionPlanStatus.Active)
            {
                throw new InvalidOperationException("Activate the plan before binding a launch.");
            }

            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            EnsureActiveSlot(slot);
            if (SlotHasBinding(slot))
            {
                throw new InvalidOperationException(
                    "Clear the existing mission link before binding this planned vessel again.");
            }
            EnsureBindingAvailable(plan, slot, missionId, vesselId);
            slot.BoundMissionId = Clean(missionId);
            slot.BoundVesselId = Clean(vesselId);
            slot.BoundUtc = FirstNonEmpty(recordedUtc, _utcNow());
            AddUnique(slot.MissionIds, missionId);
            AddUnique(slot.VesselIds, vesselId);
            slot.LaunchState = "Bound";
            slot.LaunchError = String.Empty;
            Commit(plan);
        }

        public void ClearVesselSlotBinding(string planId, string slotId)
        {
            MissionPlan plan = RequirePlan(planId);
            if (plan.Status == MissionPlanStatus.Completed ||
                plan.Status == MissionPlanStatus.Abandoned)
            {
                throw new InvalidOperationException("An ended plan's launch binding is historical.");
            }

            MissionPlanVesselSlot slot = RequireSlot(plan, slotId);
            slot.BoundMissionId = String.Empty;
            slot.BoundVesselId = String.Empty;
            slot.BoundUtc = String.Empty;
            slot.MissionIds.Clear();
            slot.VesselIds.Clear();
            slot.LaunchState = HasSavedVehicle(slot) ? "Ready" : String.Empty;
            slot.LaunchError = String.Empty;
            Commit(plan);
        }

        public MissionPlanObjective AddObjective(
            string planId,
            MissionObjectiveKind kind,
            string title,
            string vesselSlotId = null,
            string targetBody = null,
            string targetSituation = null,
            string matchValue = null,
            bool optional = false,
            string relatedVesselSlotId = null)
        {
            RequireText(title, "title");
            EnsureEnum(kind, "kind");
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            if (!String.IsNullOrWhiteSpace(vesselSlotId))
            {
                EnsureActiveSlot(RequireSlot(plan, vesselSlotId));
            }
            if (!String.IsNullOrWhiteSpace(relatedVesselSlotId))
            {
                EnsureActiveSlot(RequireSlot(plan, relatedVesselSlotId));
                if (Same(vesselSlotId, relatedVesselSlotId))
                {
                    throw new InvalidOperationException(
                        "A docking objective needs two different vessel slots.");
                }
            }

            MissionPlanObjective objective = new MissionPlanObjective
            {
                ObjectiveId = NewId("objective"),
                Order = plan.Objectives.Count,
                Kind = kind,
                Title = Clean(title),
                VesselSlotId = Clean(vesselSlotId),
                RelatedVesselSlotId = Clean(relatedVesselSlotId),
                TargetBody = Clean(targetBody),
                TargetSituation = Clean(targetSituation),
                MatchValue = Clean(matchValue),
                Optional = optional,
                Status = MissionObjectiveStatus.Pending
            };

            plan.Objectives.Add(objective);
            Commit(plan);
            return objective;
        }

        public void UpdateObjective(
            string planId,
            string objectiveId,
            MissionObjectiveKind kind,
            string title,
            string notes,
            string vesselSlotId,
            string targetBody,
            string targetSituation,
            string matchValue,
            bool optional,
            string relatedVesselSlotId = null)
        {
            RequireText(title, "title");
            EnsureEnum(kind, "kind");
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            if (!String.IsNullOrWhiteSpace(vesselSlotId))
            {
                EnsureActiveSlot(RequireSlot(plan, vesselSlotId));
            }
            if (!String.IsNullOrWhiteSpace(relatedVesselSlotId))
            {
                EnsureActiveSlot(RequireSlot(plan, relatedVesselSlotId));
                if (Same(vesselSlotId, relatedVesselSlotId))
                {
                    throw new InvalidOperationException(
                        "A docking objective needs two different vessel slots.");
                }
            }

            MissionPlanObjective objective = RequireObjective(plan, objectiveId);
            objective.Kind = kind;
            objective.Title = Clean(title);
            objective.Notes = Clean(notes);
            objective.VesselSlotId = Clean(vesselSlotId);
            objective.RelatedVesselSlotId = Clean(relatedVesselSlotId);
            objective.TargetBody = Clean(targetBody);
            objective.TargetSituation = Clean(targetSituation);
            objective.MatchValue = Clean(matchValue);
            objective.Optional = optional;
            Commit(plan);
        }

        public void ReorderObjective(string planId, string objectiveId, int newIndex)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanObjective objective = RequireObjective(plan, objectiveId);
            Reorder(plan.Objectives, objective, newIndex);
            Commit(plan);
        }

        public void SetObjectiveArchived(string planId, string objectiveId, bool archived)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureStructurallyEditable(plan);
            MissionPlanObjective objective = RequireObjective(plan, objectiveId);
            objective.Archived = archived;
            if (archived)
            {
                objective.Status = MissionObjectiveStatus.Skipped;
            }
            else if (!objective.HasManualResolution)
            {
                objective.Status = MissionObjectiveStatus.Pending;
            }

            Commit(plan);
        }

        public void ActivatePlan(string planId)
        {
            MissionPlan plan = RequirePlan(planId);
            if (plan.Status == MissionPlanStatus.Active)
            {
                return;
            }

            if (plan.Status != MissionPlanStatus.Draft)
            {
                throw new InvalidOperationException("Only a draft plan can be activated.");
            }

            int activeObjectives = 0;
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                if (objective.Archived)
                {
                    continue;
                }

                activeObjectives++;
                objective.Status = objective.HasManualResolution
                    ? objective.ManualResolution
                    : MissionObjectiveStatus.Pending;
                objective.MatchedFactId = objective.HasManualResolution
                    ? Clean(objective.ManualFactId)
                    : String.Empty;
                objective.MatchedUtc = String.Empty;
            }

            if (activeObjectives == 0)
            {
                throw new InvalidOperationException("A plan needs at least one objective before activation.");
            }

            plan.Status = MissionPlanStatus.Active;
            plan.ActivatedUtc = _utcNow();
            plan.EndedUtc = String.Empty;
            plan.Deviations.Clear();
            Commit(plan);
        }

        public void ManuallyMatchObjective(
            string planId,
            string objectiveId,
            string factId = null,
            string note = null)
        {
            SetManualResolution(
                planId,
                objectiveId,
                MissionObjectiveStatus.Achieved,
                factId,
                note);
        }

        public void SkipObjective(string planId, string objectiveId, string note = null)
        {
            SetManualResolution(
                planId,
                objectiveId,
                MissionObjectiveStatus.Skipped,
                null,
                note);
        }

        public void MarkObjectiveDeviated(
            string planId,
            string objectiveId,
            string factId = null,
            string note = null)
        {
            SetManualResolution(
                planId,
                objectiveId,
                MissionObjectiveStatus.Deviated,
                factId,
                note);
        }

        public void ClearManualResolution(string planId, string objectiveId)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureProgressEditable(plan);
            MissionPlanObjective objective = RequireObjective(plan, objectiveId);
            objective.HasManualResolution = false;
            objective.ManualResolution = MissionObjectiveStatus.Pending;
            objective.ManualFactId = String.Empty;
            objective.ManualNote = String.Empty;
            objective.MatchedFactId = String.Empty;
            objective.MatchedUtc = String.Empty;
            objective.Status = MissionObjectiveStatus.Pending;
            Commit(plan);
        }

        public MissionPlanEvaluation EvaluatePlan(
            string planId,
            IList<MissionPlanTimelineFact> orderedFacts)
        {
            return Evaluate(RequirePlan(planId), orderedFacts);
        }

        // Pure and idempotent: no input object or fact is modified.
        public static MissionPlanEvaluation Evaluate(
            MissionPlan plan,
            IList<MissionPlanTimelineFact> orderedFacts)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            List<MissionPlanObjective> objectives = OrderedActiveObjectives(plan);
            EnsureEvaluationIdsAreUnique(objectives);
            MissionPlanEvaluation evaluation = new MissionPlanEvaluation
            {
                PlanId = Clean(plan.PlanId),
                SuggestedStatus = plan.Status
            };
            Dictionary<string, MissionPlanObjectiveProgress> progressById =
                new Dictionary<string, MissionPlanObjectiveProgress>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < objectives.Count; index++)
            {
                MissionPlanObjective objective = objectives[index];
                MissionPlanObjectiveProgress progress = new MissionPlanObjectiveProgress
                {
                    ObjectiveId = objective.ObjectiveId,
                    Status = objective.HasManualResolution
                        ? objective.ManualResolution
                        : MissionObjectiveStatus.Pending,
                    MatchedFactId = objective.HasManualResolution
                        ? Clean(objective.ManualFactId)
                        : String.Empty,
                    MatchedUtc = objective.HasManualResolution
                        ? Clean(objective.MatchedUtc)
                        : String.Empty
                };
                evaluation.Objectives.Add(progress);
                progressById.Add(objective.ObjectiveId, progress);

                if (objective.HasManualResolution &&
                    objective.ManualResolution == MissionObjectiveStatus.Deviated)
                {
                    AddDeviation(
                        evaluation.Deviations,
                        plan,
                        MissionPlanDeviationKind.Manual,
                        objective.ObjectiveId,
                        objective.ManualFactId,
                        String.Empty,
                        "Objective manually marked as deviated",
                        objective.ManualNote,
                        true);
                }
            }

            HashSet<string> seenFactIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool sawCompletion = false;
            int factCount = orderedFacts == null ? 0 : orderedFacts.Count;
            for (int factIndex = 0; factIndex < factCount; factIndex++)
            {
                MissionPlanTimelineFact fact = orderedFacts[factIndex];
                if (fact == null || !FactBelongsToPlan(plan, fact))
                {
                    continue;
                }

                string factKey = FactKey(fact, factIndex);
                if (!String.IsNullOrWhiteSpace(fact.FactId) &&
                    !seenFactIds.Add(fact.FactId.Trim()))
                {
                    continue;
                }

                if (fact.Kind == MissionObjectiveKind.Complete &&
                    fact.IsPlanCompletion)
                {
                    sawCompletion = true;
                }

                // A player's durable correction owns its reviewed fact. Do
                // not reinterpret that same observation as an unexpected
                // event during later reconciliation or after reload.
                if (IsClaimedByManualResolution(objectives, fact))
                {
                    continue;
                }

                int firstPending = FirstPendingIndex(objectives, progressById);
                int matchIndex = FindMatchIndex(
                    plan,
                    objectives,
                    progressById,
                    fact,
                    firstPending);
                if (matchIndex == firstPending && matchIndex >= 0)
                {
                    Achieve(progressById[objectives[matchIndex].ObjectiveId], fact);
                }
                else if (matchIndex > firstPending && firstPending >= 0)
                {
                    MissionPlanObjective matched = objectives[matchIndex];
                    MissionPlanObjectiveProgress matchedProgress = progressById[matched.ObjectiveId];
                    matchedProgress.Status = MissionObjectiveStatus.Deviated;
                    matchedProgress.MatchedFactId = Clean(fact.FactId);
                    matchedProgress.MatchedUtc = Clean(fact.RecordedUtc);
                    AddDeviation(
                        evaluation.Deviations,
                        plan,
                        MissionPlanDeviationKind.OutOfOrder,
                        matched.ObjectiveId,
                        factKey,
                        fact.RecordedUtc,
                        "Objective occurred out of order",
                        "Expected '" + objectives[firstPending].Title + "' first.",
                        false);
                }
                else if (matchIndex >= 0)
                {
                    Achieve(progressById[objectives[matchIndex].ObjectiveId], fact);
                }
                else if (ShouldReportUnexpectedFact(objectives, fact))
                {
                    string currentId = firstPending >= 0
                        ? objectives[firstPending].ObjectiveId
                        : String.Empty;
                    AddDeviation(
                        evaluation.Deviations,
                        plan,
                        MissionPlanDeviationKind.UnexpectedFact,
                        currentId,
                        factKey,
                        fact.RecordedUtc,
                        "Unexpected mission event",
                        FactDescription(fact),
                        false);
                }
            }

            if (sawCompletion)
            {
                for (int index = 0; index < objectives.Count; index++)
                {
                    MissionPlanObjective objective = objectives[index];
                    MissionPlanObjectiveProgress progress = progressById[objective.ObjectiveId];
                    if (!IsPending(progress.Status))
                    {
                        continue;
                    }

                    if (objective.Optional)
                    {
                        progress.Status = MissionObjectiveStatus.Skipped;
                    }
                    else
                    {
                        progress.Status = MissionObjectiveStatus.Deviated;
                        AddDeviation(
                            evaluation.Deviations,
                            plan,
                            MissionPlanDeviationKind.MissingBeforeCompletion,
                            objective.ObjectiveId,
                            "completion",
                            String.Empty,
                            "Required objective was not observed",
                            objective.Title,
                            false);
                    }
                }
            }

            if (plan.Status == MissionPlanStatus.Active && !sawCompletion)
            {
                int currentIndex = FirstPendingIndex(objectives, progressById);
                if (currentIndex >= 0)
                {
                    progressById[objectives[currentIndex].ObjectiveId].Status =
                        MissionObjectiveStatus.Current;
                }
            }

            evaluation.SuggestedStatus = SuggestedStatus(
                plan,
                objectives,
                progressById,
                sawCompletion);
            return evaluation;
        }

        public MissionPlanEvaluation RecomputeProgress(
            string planId,
            IList<MissionPlanTimelineFact> orderedFacts)
        {
            MissionPlan plan = RequirePlan(planId);
            MissionPlanEvaluation evaluation = Evaluate(plan, orderedFacts);
            Dictionary<string, MissionPlanObjectiveProgress> progressById =
                new Dictionary<string, MissionPlanObjectiveProgress>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < evaluation.Objectives.Count; index++)
            {
                MissionPlanObjectiveProgress progress = evaluation.Objectives[index];
                progressById[progress.ObjectiveId] = progress;
            }

            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                MissionPlanObjectiveProgress progress;
                if (objective.Archived ||
                    !progressById.TryGetValue(objective.ObjectiveId, out progress))
                {
                    continue;
                }

                objective.Status = progress.Status;
                objective.MatchedFactId = Clean(progress.MatchedFactId);
                objective.MatchedUtc = Clean(progress.MatchedUtc);
            }

            plan.Deviations = CloneDeviations(evaluation.Deviations);
            if (plan.Status == MissionPlanStatus.Active &&
                evaluation.SuggestedStatus == MissionPlanStatus.Completed)
            {
                plan.Status = MissionPlanStatus.Completed;
                plan.EndedUtc = CompletionUtc(orderedFacts, _utcNow());
            }

            Commit(plan);
            return evaluation;
        }

        public List<string> ValidateState()
        {
            return ValidateState(_state);
        }

        public static List<string> ValidateState(MissionPlanState state)
        {
            List<string> errors = new List<string>();
            if (state == null)
            {
                errors.Add("Planner state is missing.");
                return errors;
            }

            if (state.SchemaVersion != 1)
            {
                errors.Add("Planner schema version must be 1.");
            }

            if (state.Plans == null)
            {
                errors.Add("Planner plan collection is missing.");
                return errors;
            }

            HashSet<string> planIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < state.Plans.Count; index++)
            {
                MissionPlan plan = state.Plans[index];
                if (plan == null)
                {
                    errors.Add("Planner contains a null plan.");
                    continue;
                }

                if (!planIds.Add(Clean(plan.PlanId)))
                {
                    errors.Add("Duplicate plan ID: " + plan.PlanId);
                }

                List<string> planErrors = ValidatePlan(plan);
                errors.AddRange(planErrors);
            }

            return errors;
        }

        public static List<string> ValidatePlan(MissionPlan plan)
        {
            List<string> errors = new List<string>();
            if (plan == null)
            {
                errors.Add("Plan is missing.");
                return errors;
            }

            string prefix = String.IsNullOrWhiteSpace(plan.PlanId)
                ? "Plan"
                : "Plan " + plan.PlanId;
            if (String.IsNullOrWhiteSpace(plan.PlanId))
            {
                errors.Add(prefix + " has no ID.");
            }

            if (String.IsNullOrWhiteSpace(plan.Title))
            {
                errors.Add(prefix + " has no title.");
            }

            if (!Enum.IsDefined(typeof(MissionPlanStatus), plan.Status))
            {
                errors.Add(prefix + " has an invalid status.");
            }

            if (plan.VesselSlots == null || plan.Objectives == null || plan.Deviations == null)
            {
                errors.Add(prefix + " has a missing collection.");
                return errors;
            }

            HashSet<string> slotIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> boundMissionIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> boundVesselIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> missionAliases =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> vesselAliases =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot == null)
                {
                    errors.Add(prefix + " contains a null vessel slot.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(slot.SlotId) || !slotIds.Add(slot.SlotId))
                {
                    errors.Add(prefix + " has a missing or duplicate vessel slot ID: " + slot.SlotId);
                }

                if (String.IsNullOrWhiteSpace(slot.Name))
                {
                    errors.Add(prefix + " has an unnamed vessel slot.");
                }

                if (!String.IsNullOrWhiteSpace(slot.BoundMissionId) &&
                    !boundMissionIds.Add(slot.BoundMissionId))
                {
                    errors.Add(prefix + " binds mission " + slot.BoundMissionId + " more than once.");
                }

                if (!String.IsNullOrWhiteSpace(slot.BoundVesselId) &&
                    !boundVesselIds.Add(slot.BoundVesselId))
                {
                    errors.Add(prefix + " binds vessel " + slot.BoundVesselId + " more than once.");
                }

                for (int aliasIndex = 0;
                    aliasIndex < slot.MissionIds.Count;
                    aliasIndex++)
                {
                    string alias = slot.MissionIds[aliasIndex];
                    if (!String.IsNullOrWhiteSpace(alias) && !missionAliases.Add(alias))
                    {
                        errors.Add(prefix + " assigns mission alias " + alias +
                            " to more than one vessel slot.");
                    }
                }
                for (int aliasIndex = 0;
                    aliasIndex < slot.VesselIds.Count;
                    aliasIndex++)
                {
                    string alias = slot.VesselIds[aliasIndex];
                    if (!String.IsNullOrWhiteSpace(alias) && !vesselAliases.Add(alias))
                    {
                        errors.Add(prefix + " assigns vessel alias " + alias +
                            " to more than one vessel slot.");
                    }
                }
            }

            HashSet<string> objectiveIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                if (objective == null)
                {
                    errors.Add(prefix + " contains a null objective.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(objective.ObjectiveId) ||
                    !objectiveIds.Add(objective.ObjectiveId))
                {
                    errors.Add(prefix + " has a missing or duplicate objective ID: " +
                        objective.ObjectiveId);
                }

                if (String.IsNullOrWhiteSpace(objective.Title))
                {
                    errors.Add(prefix + " has an untitled objective.");
                }

                if (!Enum.IsDefined(typeof(MissionObjectiveKind), objective.Kind) ||
                    !Enum.IsDefined(typeof(MissionObjectiveStatus), objective.Status))
                {
                    errors.Add(prefix + " has an objective with an invalid kind or status.");
                }

                if (!String.IsNullOrWhiteSpace(objective.VesselSlotId) &&
                    !slotIds.Contains(objective.VesselSlotId))
                {
                    errors.Add(prefix + " objective " + objective.ObjectiveId +
                        " references an unknown vessel slot.");
                }

                if (!String.IsNullOrWhiteSpace(objective.RelatedVesselSlotId) &&
                    !slotIds.Contains(objective.RelatedVesselSlotId))
                {
                    errors.Add(prefix + " objective " + objective.ObjectiveId +
                        " references an unknown related vessel slot.");
                }
                if (!String.IsNullOrWhiteSpace(objective.RelatedVesselSlotId) &&
                    Same(objective.VesselSlotId, objective.RelatedVesselSlotId))
                {
                    errors.Add(prefix + " objective " + objective.ObjectiveId +
                        " references the same vessel slot twice.");
                }

                if (objective.HasManualResolution &&
                    objective.ManualResolution != MissionObjectiveStatus.Achieved &&
                    objective.ManualResolution != MissionObjectiveStatus.Skipped &&
                    objective.ManualResolution != MissionObjectiveStatus.Deviated)
                {
                    errors.Add(prefix + " objective " + objective.ObjectiveId +
                        " has an invalid manual resolution.");
                }
            }

            return errors;
        }

        private void SetManualResolution(
            string planId,
            string objectiveId,
            MissionObjectiveStatus resolution,
            string factId,
            string note)
        {
            MissionPlan plan = RequirePlan(planId);
            EnsureProgressEditable(plan);
            MissionPlanObjective objective = RequireObjective(plan, objectiveId);
            if (objective.Archived)
            {
                throw new InvalidOperationException("An archived objective cannot be resolved.");
            }

            objective.HasManualResolution = true;
            objective.ManualResolution = resolution;
            objective.ManualFactId = Clean(factId);
            objective.ManualNote = Clean(note);
            objective.Status = resolution;
            objective.MatchedFactId = Clean(factId);
            objective.MatchedUtc = _utcNow();
            RemoveDeviationsForObjective(plan.Deviations, objective.ObjectiveId);
            if (resolution == MissionObjectiveStatus.Deviated)
            {
                AddDeviation(
                    plan.Deviations,
                    plan,
                    MissionPlanDeviationKind.Manual,
                    objective.ObjectiveId,
                    objective.ManualFactId,
                    objective.MatchedUtc,
                    "Objective manually marked as deviated",
                    objective.ManualNote,
                    true);
            }
            Commit(plan);
        }

        private MissionPlan RequirePlan(string planId)
        {
            RequireText(planId, "planId");
            for (int index = 0; index < _state.Plans.Count; index++)
            {
                MissionPlan plan = _state.Plans[index];
                if (Same(plan.PlanId, planId))
                {
                    return plan;
                }
            }

            throw new KeyNotFoundException("Mission plan was not found: " + planId);
        }

        private static MissionPlanVesselSlot RequireSlot(MissionPlan plan, string slotId)
        {
            RequireText(slotId, "slotId");
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                if (Same(plan.VesselSlots[index].SlotId, slotId))
                {
                    return plan.VesselSlots[index];
                }
            }

            throw new KeyNotFoundException("Mission plan vessel slot was not found: " + slotId);
        }

        private static MissionPlanObjective RequireObjective(MissionPlan plan, string objectiveId)
        {
            RequireText(objectiveId, "objectiveId");
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                if (Same(plan.Objectives[index].ObjectiveId, objectiveId))
                {
                    return plan.Objectives[index];
                }
            }

            throw new KeyNotFoundException("Mission plan objective was not found: " + objectiveId);
        }

        private static MissionPlanVesselSlot FindSlot(MissionPlan plan, string slotId)
        {
            if (plan == null || plan.VesselSlots == null ||
                String.IsNullOrWhiteSpace(slotId))
            {
                return null;
            }
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot != null && Same(slot.SlotId, slotId))
                {
                    return slot;
                }
            }
            return null;
        }

        private void Commit(MissionPlan plan)
        {
            plan.UpdatedUtc = _utcNow();
            NormalizeState(_state);
            EnsureValidState(_state);
            Revision++;
            if (_persist != null)
            {
                _persist(_state);
            }
        }

        private void NormalizeState(MissionPlanState state)
        {
            if (state.SchemaVersion <= 0)
            {
                state.SchemaVersion = 1;
            }

            if (state.Plans == null)
            {
                state.Plans = new List<MissionPlan>();
            }

            RemoveNulls(state.Plans);
            for (int index = 0; index < state.Plans.Count; index++)
            {
                NormalizePlan(state.Plans[index]);
            }
        }

        private void NormalizePlan(MissionPlan plan)
        {
            plan.PlanId = EnsureId(plan.PlanId, "plan");
            plan.CampaignId = Clean(plan.CampaignId);
            plan.Title = Clean(plan.Title);
            plan.Notes = Clean(plan.Notes);
            plan.CreatedUtc = Clean(plan.CreatedUtc);
            plan.UpdatedUtc = Clean(plan.UpdatedUtc);
            plan.ActivatedUtc = Clean(plan.ActivatedUtc);
            plan.EndedUtc = Clean(plan.EndedUtc);
            plan.VesselSlots = plan.VesselSlots ?? new List<MissionPlanVesselSlot>();
            plan.Objectives = plan.Objectives ?? new List<MissionPlanObjective>();
            plan.Deviations = plan.Deviations ?? new List<MissionPlanDeviation>();
            RemoveNulls(plan.VesselSlots);
            RemoveNulls(plan.Objectives);
            RemoveNulls(plan.Deviations);

            SortByOrder(plan.VesselSlots);
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                slot.SlotId = EnsureId(slot.SlotId, "slot");
                slot.Order = index;
                slot.Name = Clean(slot.Name);
                slot.Role = Clean(slot.Role);
                slot.SavedVehicleId = Clean(slot.SavedVehicleId);
                slot.SavedVehicleName = Clean(slot.SavedVehicleName);
                slot.SavedVehiclePath = Clean(slot.SavedVehiclePath);
                slot.SavedVehicleLocation = Clean(slot.SavedVehicleLocation);
                slot.LaunchRequestedUtc = Clean(slot.LaunchRequestedUtc);
                slot.LaunchState = Clean(slot.LaunchState);
                slot.LaunchError = Clean(slot.LaunchError);
                slot.BoundMissionId = Clean(slot.BoundMissionId);
                slot.BoundVesselId = Clean(slot.BoundVesselId);
                slot.BoundUtc = Clean(slot.BoundUtc);
                slot.MissionIds = NormalizeAliases(slot.MissionIds);
                slot.VesselIds = NormalizeAliases(slot.VesselIds);
                AddUnique(slot.MissionIds, slot.BoundMissionId);
                AddUnique(slot.VesselIds, slot.BoundVesselId);
            }

            SortByOrder(plan.Objectives);
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                objective.ObjectiveId = EnsureId(objective.ObjectiveId, "objective");
                objective.Order = index;
                objective.Title = Clean(objective.Title);
                objective.Notes = Clean(objective.Notes);
                objective.VesselSlotId = Clean(objective.VesselSlotId);
                objective.RelatedVesselSlotId = Clean(objective.RelatedVesselSlotId);
                objective.TargetBody = Clean(objective.TargetBody);
                objective.TargetSituation = Clean(objective.TargetSituation);
                objective.MatchValue = Clean(objective.MatchValue);
                objective.MatchedFactId = Clean(objective.MatchedFactId);
                objective.MatchedUtc = Clean(objective.MatchedUtc);
                objective.ManualFactId = Clean(objective.ManualFactId);
                objective.ManualNote = Clean(objective.ManualNote);
            }

            for (int index = 0; index < plan.Deviations.Count; index++)
            {
                MissionPlanDeviation deviation = plan.Deviations[index];
                deviation.DeviationId = EnsureId(deviation.DeviationId, "deviation");
                deviation.ObjectiveId = Clean(deviation.ObjectiveId);
                deviation.FactId = Clean(deviation.FactId);
                deviation.RecordedUtc = Clean(deviation.RecordedUtc);
                deviation.Title = Clean(deviation.Title);
                deviation.Detail = Clean(deviation.Detail);
            }
        }

        private static List<MissionPlanObjective> OrderedActiveObjectives(MissionPlan plan)
        {
            List<MissionPlanObjective> result = new List<MissionPlanObjective>();
            if (plan.Objectives != null)
            {
                for (int index = 0; index < plan.Objectives.Count; index++)
                {
                    MissionPlanObjective objective = plan.Objectives[index];
                    if (objective != null && !objective.Archived)
                    {
                        result.Add(objective);
                    }
                }
            }

            result.Sort(delegate(MissionPlanObjective left, MissionPlanObjective right)
            {
                int byOrder = left.Order.CompareTo(right.Order);
                return byOrder != 0
                    ? byOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(left.ObjectiveId, right.ObjectiveId);
            });
            return result;
        }

        private static bool FactBelongsToPlan(MissionPlan plan, MissionPlanTimelineFact fact)
        {
            if (fact.IsPlanScoped)
            {
                return true;
            }
            if (plan.VesselSlots == null || plan.VesselSlots.Count == 0)
            {
                return true;
            }

            bool hasBinding = false;
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot == null || slot.Archived)
                {
                    continue;
                }

                if (Same(slot.SlotId, fact.VesselSlotId))
                {
                    return true;
                }

                if (SlotHasBinding(slot))
                {
                    hasBinding = true;
                    if (FactMatchesSlot(slot, fact))
                    {
                        return true;
                    }
                }
            }

            bool factHasIdentity = !String.IsNullOrWhiteSpace(fact.VesselSlotId) ||
                !String.IsNullOrWhiteSpace(fact.MissionId) ||
                !String.IsNullOrWhiteSpace(fact.VesselId);
            return !hasBinding || !factHasIdentity;
        }

        private static int FindMatchIndex(
            MissionPlan plan,
            List<MissionPlanObjective> objectives,
            Dictionary<string, MissionPlanObjectiveProgress> progressById,
            MissionPlanTimelineFact fact,
            int startIndex)
        {
            int first = Math.Max(0, startIndex);
            for (int index = first; index < objectives.Count; index++)
            {
                MissionPlanObjective objective = objectives[index];
                if (!IsPending(progressById[objective.ObjectiveId].Status))
                {
                    continue;
                }

                if (ObjectiveMatches(plan, objective, fact))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool ObjectiveMatches(
            MissionPlan plan,
            MissionPlanObjective objective,
            MissionPlanTimelineFact fact)
        {
            if (fact.IsTerminalLoss)
            {
                return false;
            }
            if (objective.Kind != fact.Kind)
            {
                return false;
            }

            if (objective.Kind == MissionObjectiveKind.Complete &&
                String.IsNullOrWhiteSpace(objective.VesselSlotId) &&
                String.IsNullOrWhiteSpace(objective.RelatedVesselSlotId) &&
                !fact.IsPlanCompletion)
            {
                return false;
            }

            if (!String.IsNullOrWhiteSpace(objective.VesselSlotId))
            {
                MissionPlanVesselSlot slot = null;
                if (plan.VesselSlots != null)
                {
                    for (int index = 0; index < plan.VesselSlots.Count; index++)
                    {
                        MissionPlanVesselSlot candidate = plan.VesselSlots[index];
                        if (candidate != null && Same(candidate.SlotId, objective.VesselSlotId))
                        {
                            slot = candidate;
                            break;
                        }
                    }
                }

                if (slot == null || !FactMatchesSlot(slot, fact))
                {
                    return false;
                }
            }

            if (!String.IsNullOrWhiteSpace(objective.RelatedVesselSlotId))
            {
                MissionPlanVesselSlot related = FindSlot(
                    plan,
                    objective.RelatedVesselSlotId);
                if (related == null || !FactMatchesSlot(related, fact))
                {
                    return false;
                }
            }

            if (!String.IsNullOrWhiteSpace(objective.TargetBody) &&
                !Same(objective.TargetBody, fact.Body) &&
                !Same(objective.TargetBody, fact.Value))
            {
                return false;
            }

            if (!String.IsNullOrWhiteSpace(objective.TargetSituation) &&
                !Same(objective.TargetSituation, fact.Situation) &&
                !Same(objective.TargetSituation, fact.Value))
            {
                return false;
            }

            if (!String.IsNullOrWhiteSpace(objective.MatchValue) &&
                !Same(objective.MatchValue, fact.Value))
            {
                return false;
            }

            return true;
        }

        private static bool FactMatchesSlot(
            MissionPlanVesselSlot slot,
            MissionPlanTimelineFact fact)
        {
            if (Same(slot.SlotId, fact.VesselSlotId) ||
                Contains(fact.VesselSlotIds, slot.SlotId) ||
                Same(slot.BoundMissionId, fact.MissionId) ||
                Same(slot.BoundVesselId, fact.VesselId) ||
                Contains(slot.MissionIds, fact.MissionId) ||
                Contains(slot.VesselIds, fact.VesselId) ||
                Intersects(slot.MissionIds, fact.RelatedMissionIds) ||
                Intersects(slot.VesselIds, fact.VesselIds))
            {
                return true;
            }

            // Before launch binding, an explicitly slotted fact can still match.
            return !SlotHasBinding(slot) && Same(slot.SlotId, fact.VesselSlotId);
        }

        private static MissionPlanStatus SuggestedStatus(
            MissionPlan plan,
            List<MissionPlanObjective> objectives,
            Dictionary<string, MissionPlanObjectiveProgress> progressById,
            bool sawCompletion)
        {
            if (plan.Status == MissionPlanStatus.Draft ||
                plan.Status == MissionPlanStatus.Abandoned ||
                plan.Status == MissionPlanStatus.Completed)
            {
                return plan.Status;
            }

            if (sawCompletion)
            {
                return MissionPlanStatus.Completed;
            }

            for (int index = 0; index < objectives.Count; index++)
            {
                MissionPlanObjective objective = objectives[index];
                MissionObjectiveStatus status = progressById[objective.ObjectiveId].Status;
                if (status == MissionObjectiveStatus.Pending ||
                    status == MissionObjectiveStatus.Current)
                {
                    return MissionPlanStatus.Active;
                }
            }

            return objectives.Count > 0
                ? MissionPlanStatus.Completed
                : MissionPlanStatus.Active;
        }

        private static void AddDeviation(
            List<MissionPlanDeviation> deviations,
            MissionPlan plan,
            MissionPlanDeviationKind kind,
            string objectiveId,
            string factId,
            string recordedUtc,
            string title,
            string detail,
            bool manual)
        {
            string id = "deviation|" + Clean(plan.PlanId) + "|" + kind + "|" +
                Clean(objectiveId) + "|" + Clean(factId);
            for (int index = 0; index < deviations.Count; index++)
            {
                if (Same(deviations[index].DeviationId, id))
                {
                    return;
                }
            }

            deviations.Add(new MissionPlanDeviation
            {
                DeviationId = id,
                Kind = kind,
                ObjectiveId = Clean(objectiveId),
                FactId = Clean(factId),
                RecordedUtc = Clean(recordedUtc),
                Title = Clean(title),
                Detail = Clean(detail),
                Manual = manual
            });
        }

        private static void Achieve(
            MissionPlanObjectiveProgress progress,
            MissionPlanTimelineFact fact)
        {
            progress.Status = MissionObjectiveStatus.Achieved;
            progress.MatchedFactId = Clean(fact.FactId);
            progress.MatchedUtc = Clean(fact.RecordedUtc);
        }

        private static int FirstPendingIndex(
            List<MissionPlanObjective> objectives,
            Dictionary<string, MissionPlanObjectiveProgress> progressById)
        {
            for (int index = 0; index < objectives.Count; index++)
            {
                if (IsPending(progressById[objectives[index].ObjectiveId].Status))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsPending(MissionObjectiveStatus status)
        {
            return status == MissionObjectiveStatus.Pending ||
                status == MissionObjectiveStatus.Current;
        }

        private static bool IsPlannedKind(
            List<MissionPlanObjective> objectives,
            MissionObjectiveKind kind)
        {
            for (int index = 0; index < objectives.Count; index++)
            {
                if (objectives[index].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsClaimedByManualResolution(
            List<MissionPlanObjective> objectives,
            MissionPlanTimelineFact fact)
        {
            if (fact == null || string.IsNullOrWhiteSpace(fact.FactId))
            {
                return false;
            }
            for (int index = 0; index < objectives.Count; index++)
            {
                MissionPlanObjective objective = objectives[index];
                if (objective.HasManualResolution &&
                    !string.IsNullOrWhiteSpace(objective.ManualFactId) &&
                    Same(objective.ManualFactId, fact.FactId))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ShouldReportUnexpectedFact(
            List<MissionPlanObjective> objectives,
            MissionPlanTimelineFact fact)
        {
            if (fact.IsTerminalLoss)
            {
                return true;
            }
            if (fact.Kind == MissionObjectiveKind.Complete)
            {
                // The overarching completion fact is a control boundary; any
                // missing required steps already receive precise deviations.
                // A child completion matters only when the plan explicitly has
                // a slot-scoped child completion objective.
                if (fact.IsPlanCompletion)
                {
                    return false;
                }
                for (int index = 0; index < objectives.Count; index++)
                {
                    if (objectives[index].Kind == MissionObjectiveKind.Complete &&
                        !String.IsNullOrWhiteSpace(objectives[index].VesselSlotId))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (IsPlannedKind(objectives, fact.Kind))
            {
                return true;
            }
            MissionObjectiveKind kind = fact.Kind;
            return kind == MissionObjectiveKind.Launch ||
                kind == MissionObjectiveKind.Body ||
                kind == MissionObjectiveKind.Land ||
                kind == MissionObjectiveKind.Dock ||
                kind == MissionObjectiveKind.Separate ||
                kind == MissionObjectiveKind.Recover;
        }

        private static string FactDescription(MissionPlanTimelineFact fact)
        {
            if (!String.IsNullOrWhiteSpace(fact.Title))
            {
                return fact.Title.Trim();
            }

            string value = FirstNonEmpty(fact.Value, fact.Body, fact.Situation);
            return String.IsNullOrWhiteSpace(value)
                ? fact.Kind.ToString()
                : fact.Kind + ": " + value;
        }

        private static string FactKey(MissionPlanTimelineFact fact, int factIndex)
        {
            return String.IsNullOrWhiteSpace(fact.FactId)
                ? "ordered-fact-" + factIndex
                : fact.FactId.Trim();
        }

        private static void EnsureEvaluationIdsAreUnique(List<MissionPlanObjective> objectives)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < objectives.Count; index++)
            {
                string id = Clean(objectives[index].ObjectiveId);
                if (String.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    throw new InvalidOperationException(
                        "Objective IDs must be present and unique before evaluation.");
                }
            }
        }

        private static List<MissionPlanDeviation> CloneDeviations(
            List<MissionPlanDeviation> source)
        {
            List<MissionPlanDeviation> result = new List<MissionPlanDeviation>();
            for (int index = 0; index < source.Count; index++)
            {
                MissionPlanDeviation item = source[index];
                result.Add(new MissionPlanDeviation
                {
                    DeviationId = item.DeviationId,
                    Kind = item.Kind,
                    ObjectiveId = item.ObjectiveId,
                    FactId = item.FactId,
                    RecordedUtc = item.RecordedUtc,
                    Title = item.Title,
                    Detail = item.Detail,
                    Manual = item.Manual
                });
            }

            return result;
        }

        private static string CompletionUtc(
            IList<MissionPlanTimelineFact> orderedFacts,
            string fallback)
        {
            if (orderedFacts != null)
            {
                for (int index = orderedFacts.Count - 1; index >= 0; index--)
                {
                    MissionPlanTimelineFact fact = orderedFacts[index];
                    if (fact != null && fact.Kind == MissionObjectiveKind.Complete &&
                        !String.IsNullOrWhiteSpace(fact.RecordedUtc))
                    {
                        return fact.RecordedUtc.Trim();
                    }
                }
            }

            return fallback;
        }

        private void EnsureBindingAvailable(
            MissionPlan plan,
            MissionPlanVesselSlot target,
            string missionId,
            string vesselId)
        {
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (ReferenceEquals(slot, target))
                {
                    continue;
                }

                if ((!String.IsNullOrWhiteSpace(missionId) &&
                        (Same(slot.BoundMissionId, missionId) || Contains(slot.MissionIds, missionId))) ||
                    (!String.IsNullOrWhiteSpace(vesselId) &&
                        (Same(slot.BoundVesselId, vesselId) || Contains(slot.VesselIds, vesselId))))
                {
                    throw new InvalidOperationException(
                        "The mission or vessel is already assigned to another vessel slot.");
                }
            }
        }

        private static void EnsureStructurallyEditable(MissionPlan plan)
        {
            if (plan.Status == MissionPlanStatus.Completed ||
                plan.Status == MissionPlanStatus.Abandoned)
            {
                throw new InvalidOperationException("An ended plan cannot be structurally edited.");
            }
        }

        private static void EnsureProgressEditable(MissionPlan plan)
        {
            if (plan.Status != MissionPlanStatus.Active &&
                plan.Status != MissionPlanStatus.Completed)
            {
                throw new InvalidOperationException(
                    "Only an active or completed plan's progress can be corrected.");
            }
        }

        private static void EnsureActiveSlot(MissionPlanVesselSlot slot)
        {
            if (slot.Archived)
            {
                throw new InvalidOperationException("An archived vessel slot cannot be used.");
            }
        }

        private static void EnsureEnum<T>(T value, string parameterName)
            where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequireText(string value, string parameterName)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A value is required.", parameterName);
            }
        }

        private static void EnsureValidState(MissionPlanState state)
        {
            List<string> errors = ValidateState(state);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(String.Join(" ", errors.ToArray()));
            }
        }

        private string EnsureId(string value, string prefix)
        {
            return String.IsNullOrWhiteSpace(value) ? NewId(prefix) : value.Trim();
        }

        private string NewId(string prefix)
        {
            string value = _idFactory();
            if (String.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("The mission planner ID factory returned no ID.");
            }

            return prefix + "-" + value.Trim();
        }

        private static string DefaultIdFactory()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static string DefaultUtcNow()
        {
            return DateTime.UtcNow.ToString("o");
        }

        private static string Clean(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? String.Empty : value.Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (!String.IsNullOrWhiteSpace(values[index]))
                {
                    return values[index].Trim();
                }
            }

            return String.Empty;
        }

        private static string AppendNote(string existing, string addition)
        {
            string left = Clean(existing);
            string right = Clean(addition);
            return String.IsNullOrEmpty(left) ? right : left + Environment.NewLine + right;
        }

        private static bool Same(string left, string right)
        {
            return !String.IsNullOrWhiteSpace(left) &&
                !String.IsNullOrWhiteSpace(right) &&
                String.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(List<string> values, string candidate)
        {
            if (values == null || String.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (Same(values[index], candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Intersects(List<string> left, List<string> right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            for (int index = 0; index < left.Count; index++)
            {
                if (Contains(right, left[index]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasSavedVehicle(MissionPlanVesselSlot slot)
        {
            return slot != null &&
                (!String.IsNullOrWhiteSpace(slot.SavedVehicleId) ||
                 !String.IsNullOrWhiteSpace(slot.SavedVehiclePath));
        }

        private static void RemoveDeviationsForObjective(
            List<MissionPlanDeviation> deviations,
            string objectiveId)
        {
            if (deviations == null)
            {
                return;
            }
            for (int index = deviations.Count - 1; index >= 0; index--)
            {
                MissionPlanDeviation deviation = deviations[index];
                if (deviation != null && Same(deviation.ObjectiveId, objectiveId))
                {
                    deviations.RemoveAt(index);
                }
            }
        }

        private static bool SlotHasBinding(MissionPlanVesselSlot slot)
        {
            return !String.IsNullOrWhiteSpace(slot.BoundMissionId) ||
                !String.IsNullOrWhiteSpace(slot.BoundVesselId) ||
                (slot.MissionIds != null && slot.MissionIds.Count > 0) ||
                (slot.VesselIds != null && slot.VesselIds.Count > 0);
        }

        private static void AddUnique(List<string> values, string candidate)
        {
            if (!String.IsNullOrWhiteSpace(candidate) && !Contains(values, candidate))
            {
                values.Add(candidate.Trim());
            }
        }

        private static List<string> NormalizeAliases(List<string> aliases)
        {
            List<string> result = new List<string>();
            if (aliases != null)
            {
                for (int index = 0; index < aliases.Count; index++)
                {
                    AddUnique(result, aliases[index]);
                }
            }

            return result;
        }

        private static void RemoveNulls<T>(List<T> items) where T : class
        {
            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index] == null)
                {
                    items.RemoveAt(index);
                }
            }
        }

        private static void SortByOrder(List<MissionPlanVesselSlot> slots)
        {
            slots.Sort(delegate(MissionPlanVesselSlot left, MissionPlanVesselSlot right)
            {
                int byOrder = left.Order.CompareTo(right.Order);
                return byOrder != 0
                    ? byOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(left.SlotId, right.SlotId);
            });
        }

        private static void SortByOrder(List<MissionPlanObjective> objectives)
        {
            objectives.Sort(delegate(MissionPlanObjective left, MissionPlanObjective right)
            {
                int byOrder = left.Order.CompareTo(right.Order);
                return byOrder != 0
                    ? byOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(left.ObjectiveId, right.ObjectiveId);
            });
        }

        private static void Reorder(
            List<MissionPlanVesselSlot> items,
            MissionPlanVesselSlot item,
            int newIndex)
        {
            if (newIndex < 0 || newIndex >= items.Count)
            {
                throw new ArgumentOutOfRangeException("newIndex");
            }

            items.Remove(item);
            items.Insert(newIndex, item);
            for (int index = 0; index < items.Count; index++)
            {
                items[index].Order = index;
            }
        }

        private static void Reorder(
            List<MissionPlanObjective> items,
            MissionPlanObjective item,
            int newIndex)
        {
            if (newIndex < 0 || newIndex >= items.Count)
            {
                throw new ArgumentOutOfRangeException("newIndex");
            }

            items.Remove(item);
            items.Insert(newIndex, item);
            for (int index = 0; index < items.Count; index++)
            {
                items[index].Order = index;
            }
        }
    }
}
