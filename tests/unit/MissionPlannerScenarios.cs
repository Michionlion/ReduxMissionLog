using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ReduxMissionLog
{
    internal static class MissionPlannerScenarios
    {
        private static int _assertions;
        private static int _passed;

        public static int Main()
        {
            try
            {
                Run("legacy planner state normalizes defensively", LegacyStateNormalizesDefensively);
                Run("draft creation and editing persist through both hooks", DraftEditingPersistsThroughBothHooks);
                Run("saved craft launch intent binds exactly one slot", SavedCraftLaunchBindsOneSlot);
                Run("Kerbin to Mun expedition completes in order", KerbinToMunExpeditionCompletesInOrder);
                Run("out-of-order and unexpected facts become deviations", DeviationsExplainChangedFlightPlan);
                Run("optional objectives can be reordered and skipped", OptionalObjectivesReorderAndSkip);
                Run("pending optional objective keeps plan active", PendingOptionalObjectiveKeepsPlanActive);
                Run("manual deviation can be corrected without residue", ManualDeviationCanBeCorrected);
                Run("completed plan accepts durable manual correction", CompletedPlanAcceptsDurableManualCorrection);
                Run("manual fact claim consumes completed-plan replay", ManualFactClaimConsumesCompletedPlanReplay);
                Run("duplicate timeline facts are pure and idempotent", DuplicateFactsArePureAndIdempotent);
                Run("multi-launch mission-tree facts respect vessel slots", MultiLaunchTreeRespectsSlots);
                Run("cleared vessel binding can be reassigned", ClearedVesselBindingCanBeReassigned);
                Run("two-vessel docking requires both planned slots", TwoVesselDockingRequiresBothPlannedSlots);
                Run("only overarching completion closes the plan", OnlyOverarchingCompletionClosesPlan);
                Run("terminal root loss remains a visible deviation", TerminalRootLossRemainsVisibleDeviation);
                Run("abandonment, archival, and validation preserve history", TerminalPlansAndValidationPreserveHistory);

                Console.WriteLine(
                    "PASS: " + _passed + " planner scenarios, " +
                    _assertions + " assertions.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.Message);
                Console.Error.WriteLine(error.StackTrace);
                return 1;
            }
        }

        private static void LegacyStateNormalizesDefensively()
        {
            Determinism deterministic = new Determinism();
            MissionPlanState state = new MissionPlanState
            {
                SchemaVersion = 0,
                Plans = new List<MissionPlan>
                {
                    null,
                    new MissionPlan
                    {
                        PlanId = " ",
                        CampaignId = " campaign-alpha ",
                        Title = "  Legacy Mun Survey  ",
                        Notes = null,
                        VesselSlots = new List<MissionPlanVesselSlot>
                        {
                            null,
                            new MissionPlanVesselSlot
                            {
                                SlotId = null,
                                Order = 4,
                                Name = "  Surveyor  ",
                                Role = "  Lander  ",
                                SavedVehicleId = "  craft-legacy  ",
                                SavedVehicleLocation = "  Campaign  ",
                                BoundMissionId = " mission-legacy ",
                                BoundVesselId = " vessel-legacy ",
                                MissionIds = new List<string>
                                {
                                    "mission-legacy",
                                    " MISSION-LEGACY ",
                                    null
                                },
                                VesselIds = null
                            }
                        },
                        Objectives = new List<MissionPlanObjective>
                        {
                            null,
                            new MissionPlanObjective
                            {
                                ObjectiveId = null,
                                Order = 8,
                                Kind = MissionObjectiveKind.Land,
                                Title = "  Land on Mun  ",
                                VesselSlotId = null,
                                TargetBody = "  Mun  "
                            }
                        },
                        Deviations = null
                    }
                }
            };

            MissionPlanner planner = new MissionPlanner(
                state,
                null,
                deterministic.NextId,
                deterministic.UtcNow);
            MissionPlan plan = state.Plans[0];
            MissionPlanVesselSlot slot = plan.VesselSlots[0];
            MissionPlanObjective objective = plan.Objectives[0];

            Equal(1, state.SchemaVersion, "schema zero should normalize to schema one");
            Equal(1, state.Plans.Count, "null plans should be removed");
            Equal("plan-id-001", plan.PlanId, "a legacy plan should receive a stable ID");
            Equal("campaign-alpha", plan.CampaignId, "campaign ID should be trimmed");
            Equal("Legacy Mun Survey", plan.Title, "plan title should be trimmed");
            Equal(string.Empty, plan.Notes, "null plan notes should normalize to empty");
            Equal(1, plan.VesselSlots.Count, "null vessel slots should be removed");
            Equal("slot-id-002", slot.SlotId, "a legacy vessel slot should receive an ID");
            Equal(0, slot.Order, "slot order should be compacted");
            Equal("Surveyor", slot.Name, "slot name should be trimmed");
            Equal("craft-legacy", slot.SavedVehicleId, "saved craft ID should be normalized");
            Equal("Campaign", slot.SavedVehicleLocation,
                "saved craft IO-provider location should be normalized");
            Equal(1, slot.MissionIds.Count, "mission aliases should deduplicate case-insensitively");
            Equal("vessel-legacy", slot.VesselIds[0],
                "the bound vessel should be added to normalized aliases");
            Equal(1, plan.Objectives.Count, "null objectives should be removed");
            Equal("objective-id-003", objective.ObjectiveId,
                "a legacy objective should receive an ID");
            Equal(0, objective.Order, "objective order should be compacted");
            Equal("Land on Mun", objective.Title, "objective title should be trimmed");
            Equal("Mun", objective.TargetBody, "objective target should be trimmed");
            Equal(0, plan.Deviations.Count, "missing deviations should normalize to an empty list");
            Equal(0, planner.ValidateState().Count, "normalized state should validate cleanly");
        }

        private static void DraftEditingPersistsThroughBothHooks()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                " campaign-beta ",
                "  Duna Relay Deployment  ",
                "  Establish communications  ");

            Equal("plan-id-001", plan.PlanId, "create should use the injected ID source");
            Equal("campaign-beta", plan.CampaignId, "create should normalize campaign ID");
            Equal("Duna Relay Deployment", plan.Title, "create should normalize title");
            Equal("Establish communications", plan.Notes, "create should normalize notes");
            Equal(MissionPlanStatus.Draft, plan.Status, "new plans should be drafts");
            Equal(1, fixture.SaveCount, "create should invoke the persistence callback once");
            True(!string.IsNullOrWhiteSpace(plan.CreatedUtc), "create should stamp creation time");
            True(!string.IsNullOrWhiteSpace(plan.UpdatedUtc), "create should stamp update time");

            fixture.Planner.UpdatePlan(
                plan.PlanId,
                "  Duna Relay and Ike Survey  ",
                "  Expanded before launch  ");
            Equal("Duna Relay and Ike Survey", plan.Title, "draft title should be editable");
            Equal("Expanded before launch", plan.Notes, "draft notes should be editable");
            Equal(2, fixture.SaveCount, "update should invoke persistence again");
            Equal(0, fixture.Planner.ValidateState().Count, "created and edited state should validate");
            Equal(Snapshot(fixture.State), fixture.Snapshots[fixture.Snapshots.Count - 1],
                "the callback should receive the complete current state");

            MemoryStore store = new MemoryStore(new MissionPlanState());
            Determinism deterministic = new Determinism();
            MissionPlanner storedPlanner = new MissionPlanner(
                store,
                deterministic.NextId,
                deterministic.UtcNow);
            MissionPlan stored = storedPlanner.CreatePlan(
                "campaign-store", "Store-backed plan", null);
            Equal(1, store.SaveCount, "the store abstraction should save mutations");
            Same(store.Loaded, storedPlanner.State,
                "the planner should use the state supplied by the store");
            Same(store.LastSaved, storedPlanner.State,
                "the store should receive the planner's authoritative state");
            Same(stored, store.LastSaved.Plans[0],
                "the saved state should contain the created plan instance");

            MissionPlanState replacement = new MissionPlanState();
            storedPlanner.ReplaceState(replacement, true);
            Same(replacement, storedPlanner.State, "replace should install the supplied valid state");
            Equal(2, store.SaveCount, "saved replacement should use the same persistence hook");
        }

        private static void SavedCraftLaunchBindsOneSlot()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-launch", "Launch Minmus Mapper", null);
            MissionPlanVesselSlot mapper = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Mapper", "Primary orbiter", true);
            MissionPlanVesselSlot relay = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Relay", "Secondary payload", false);
            MissionPlanObjective launch = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Launch,
                "Launch mapper",
                mapper.SlotId);

            fixture.Planner.SelectSavedVehicle(
                plan.PlanId,
                mapper.SlotId,
                "workspace-minmus-mapper",
                "Minmus Mapper Mk2",
                "Vehicles/VAB/Minmus Mapper Mk2.json",
                "Campaign");
            Equal("workspace-minmus-mapper", mapper.SavedVehicleId,
                "slot should preserve the selected workspace ID");
            Equal("Minmus Mapper Mk2", mapper.SavedVehicleName,
                "slot should preserve the display name");
            Equal("Vehicles/VAB/Minmus Mapper Mk2.json", mapper.SavedVehiclePath,
                "slot should preserve the saved craft path");
            Equal("Campaign", mapper.SavedVehicleLocation,
                "slot should preserve the IO-provider location");
            Equal(string.Empty, mapper.LaunchState,
                "selecting a craft should clear stale launch state");

            fixture.Planner.ActivatePlan(plan.PlanId);
            Equal(MissionPlanStatus.Active, plan.Status, "activation should make the plan active");
            True(!string.IsNullOrWhiteSpace(plan.ActivatedUtc),
                "activation should preserve an activation timestamp");
            Equal(MissionObjectiveStatus.Current,
                Progress(fixture.Planner.EvaluatePlan(plan.PlanId, EmptyFacts()), launch).Status,
                "the first objective should evaluate as current after activation");

            fixture.Planner.RecordLaunchRequest(plan.PlanId, mapper.SlotId, "Requested");
            Equal("Requested", mapper.LaunchState, "launch intent should be persisted on the slot");
            True(!string.IsNullOrWhiteSpace(mapper.LaunchRequestedUtc),
                "launch intent should include a request timestamp");
            Equal(string.Empty, mapper.LaunchError, "a new request should clear old launch errors");

            fixture.Planner.RecordLaunchResult(
                plan.PlanId, mapper.SlotId, "VehicleLoaded", null);
            Equal("VehicleLoaded", mapper.LaunchState,
                "the KSP adapter should be able to report a neutral launch state");

            fixture.Planner.BindLaunch(
                plan.PlanId,
                mapper.SlotId,
                "mission-minmus-mapper",
                "vessel-minmus-mapper",
                "2026-02-01T00:00:00.0000000Z");
            Equal("mission-minmus-mapper", mapper.BoundMissionId,
                "slot should bind the launched mission");
            Equal("vessel-minmus-mapper", mapper.BoundVesselId,
                "slot should bind the launched vessel");
            Equal("2026-02-01T00:00:00.0000000Z", mapper.BoundUtc,
                "binding should preserve the observed launch time");
            Equal("Bound", mapper.LaunchState, "a successful binding should become the launch state");
            True(Contains(mapper.MissionIds, "mission-minmus-mapper"),
                "bound mission should be retained as a slot alias");
            True(Contains(mapper.VesselIds, "vessel-minmus-mapper"),
                "bound vessel should be retained as a slot alias");

            Throws<InvalidOperationException>(
                delegate
                {
                    fixture.Planner.BindLaunch(
                        plan.PlanId,
                        relay.SlotId,
                        "mission-minmus-mapper",
                        "vessel-other");
                },
                "one live mission should not bind to two slots");
            Equal(string.Empty, relay.BoundMissionId,
                "a rejected duplicate binding should not partially mutate the other slot");
            Equal(0, fixture.Planner.ValidateState().Count,
                "saved craft and launch binding state should validate");
        }

        private static void KerbinToMunExpeditionCompletesInOrder()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-mun", "Mun Surface Expedition", null);
            MissionPlanObjective launch = Add(fixture, plan, MissionObjectiveKind.Launch,
                "Launch from Kerbin");
            MissionPlanObjective body = AddTargeted(fixture, plan, MissionObjectiveKind.Body,
                "Enter Mun sphere of influence", "Mun", null);
            MissionPlanObjective orbit = AddTargeted(fixture, plan, MissionObjectiveKind.Orbit,
                "Establish low Mun orbit", "Mun", "Orbiting");
            MissionPlanObjective situation = AddTargeted(fixture, plan,
                MissionObjectiveKind.Situation, "Confirm stable orbit", null, "LowOrbit");
            MissionPlanObjective land = AddTargeted(fixture, plan, MissionObjectiveKind.Land,
                "Land on Mun", "Mun", "Landed");
            MissionPlanObjective dock = Add(fixture, plan, MissionObjectiveKind.Dock,
                "Redock lander and command ship");
            MissionPlanObjective separate = Add(fixture, plan, MissionObjectiveKind.Separate,
                "Separate return capsule");
            MissionPlanObjective recover = Add(fixture, plan, MissionObjectiveKind.Recover,
                "Recover crew on Kerbin");
            MissionPlanObjective complete = Add(fixture, plan, MissionObjectiveKind.Complete,
                "Close expedition");
            fixture.Planner.ActivatePlan(plan.PlanId);

            List<MissionPlanTimelineFact> facts = new List<MissionPlanTimelineFact>
            {
                Fact("mun-01-launch", MissionObjectiveKind.Launch, "Kerbin", "Flying", null, 0),
                Fact("mun-02-soi", MissionObjectiveKind.Body, "Mun", "HighOrbit", "Mun", 100),
                Fact("mun-03-orbit", MissionObjectiveKind.Orbit, "Mun", "Orbiting", null, 200),
                Fact("mun-04-low-orbit", MissionObjectiveKind.Situation,
                    "Mun", "LowOrbit", "LowOrbit", 300),
                Fact("mun-05-land", MissionObjectiveKind.Land, "Mun", "Landed", null, 400),
                Fact("mun-06-dock", MissionObjectiveKind.Dock, "Mun", "Orbiting", null, 500),
                Fact("mun-07-separate", MissionObjectiveKind.Separate,
                    "Kerbin", "SubOrbital", null, 600),
                Fact("mun-08-recover", MissionObjectiveKind.Recover,
                    "Kerbin", "Landed", null, 700),
                Fact("mun-09-complete", MissionObjectiveKind.Complete,
                    "Kerbin", "Landed", null, 800)
            };

            MissionPlanEvaluation evaluation = fixture.Planner.EvaluatePlan(plan.PlanId, facts);
            Equal(9, evaluation.Objectives.Count,
                "the evaluation should retain every ordered objective");
            AssertAchieved(evaluation, launch, "mun-01-launch");
            AssertAchieved(evaluation, body, "mun-02-soi");
            AssertAchieved(evaluation, orbit, "mun-03-orbit");
            AssertAchieved(evaluation, situation, "mun-04-low-orbit");
            AssertAchieved(evaluation, land, "mun-05-land");
            AssertAchieved(evaluation, dock, "mun-06-dock");
            AssertAchieved(evaluation, separate, "mun-07-separate");
            AssertAchieved(evaluation, recover, "mun-08-recover");
            AssertAchieved(evaluation, complete, "mun-09-complete");
            Equal(0, evaluation.Deviations.Count,
                "an in-order expedition should not produce deviations");
            Equal(MissionPlanStatus.Completed, evaluation.SuggestedStatus,
                "a complete fact should close the evaluated plan");

            fixture.Planner.RecomputeProgress(plan.PlanId, facts);
            Equal(MissionPlanStatus.Completed, plan.Status,
                "recompute should apply the completed status");
            Equal(facts[8].RecordedUtc, plan.EndedUtc,
                "the complete fact time should become the plan end time");
            Equal(MissionObjectiveStatus.Achieved, complete.Status,
                "recompute should apply objective progress to persistent state");
            Equal(0, plan.Deviations.Count,
                "persistent state should retain the clean evaluation");
        }

        private static void DeviationsExplainChangedFlightPlan()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-diversion", "Mun Landing Attempt", null);
            MissionPlanObjective launch = Add(fixture, plan, MissionObjectiveKind.Launch,
                "Launch lander");
            MissionPlanObjective orbit = AddTargeted(fixture, plan, MissionObjectiveKind.Orbit,
                "Orbit Mun", "Mun", null);
            MissionPlanObjective land = AddTargeted(fixture, plan, MissionObjectiveKind.Land,
                "Land on Mun", "Mun", null);
            fixture.Planner.ActivatePlan(plan.PlanId);

            List<MissionPlanTimelineFact> facts = new List<MissionPlanTimelineFact>
            {
                Fact("changed-01-launch", MissionObjectiveKind.Launch,
                    "Kerbin", "Flying", null, 0),
                Fact("changed-02-land-early", MissionObjectiveKind.Land,
                    "Mun", "Landed", null, 100),
                Fact("changed-03-duna-orbit", MissionObjectiveKind.Orbit,
                    "Duna", "Orbiting", null, 200),
                Fact("changed-04-mun-orbit", MissionObjectiveKind.Orbit,
                    "Mun", "Orbiting", null, 300)
            };

            MissionPlanEvaluation evaluation = fixture.Planner.EvaluatePlan(plan.PlanId, facts);
            AssertAchieved(evaluation, launch, "changed-01-launch");
            AssertAchieved(evaluation, orbit, "changed-04-mun-orbit");
            Equal(MissionObjectiveStatus.Deviated, Progress(evaluation, land).Status,
                "landing before orbit should resolve the landing as deviated");
            Equal("changed-02-land-early", Progress(evaluation, land).MatchedFactId,
                "the early landing should remain attributable");
            Equal(2, evaluation.Deviations.Count,
                "the changed plan should explain both unusual facts");
            MissionPlanDeviation outOfOrder = Deviation(
                evaluation, MissionPlanDeviationKind.OutOfOrder);
            Equal(land.ObjectiveId, outOfOrder.ObjectiveId,
                "out-of-order deviation should identify the early objective");
            Equal("changed-02-land-early", outOfOrder.FactId,
                "out-of-order deviation should identify its fact");
            True(outOfOrder.Detail.IndexOf("Orbit Mun", StringComparison.Ordinal) >= 0,
                "out-of-order detail should name the objective that was expected first");
            MissionPlanDeviation unexpected = Deviation(
                evaluation, MissionPlanDeviationKind.UnexpectedFact);
            Equal(orbit.ObjectiveId, unexpected.ObjectiveId,
                "unexpected target should be attributed to the current orbit objective");
            Equal("changed-03-duna-orbit", unexpected.FactId,
                "unexpected deviation should preserve the Duna fact ID");
            Equal(MissionPlanStatus.Completed, evaluation.SuggestedStatus,
                "all required objectives are resolved even when one deviated");

            string first = Snapshot(evaluation);
            string second = Snapshot(fixture.Planner.EvaluatePlan(plan.PlanId, facts));
            Equal(first, second, "deviation identities and ordering should be deterministic");
        }

        private static void OptionalObjectivesReorderAndSkip()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-training", "Kerbin Training Flight", null);
            MissionPlanObjective recover = Add(fixture, plan, MissionObjectiveKind.Recover,
                "Recover capsule");
            MissionPlanObjective launch = Add(fixture, plan, MissionObjectiveKind.Launch,
                "Launch training capsule");
            MissionPlanObjective dock = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Dock,
                "Optional station docking",
                null,
                null,
                null,
                null,
                true);

            fixture.Planner.ReorderObjective(plan.PlanId, launch.ObjectiveId, 0);
            Equal(launch.ObjectiveId, plan.Objectives[0].ObjectiveId,
                "launch should move to the first position");
            Equal(recover.ObjectiveId, plan.Objectives[1].ObjectiveId,
                "recovery should move behind launch");
            Equal(dock.ObjectiveId, plan.Objectives[2].ObjectiveId,
                "optional dock should remain last");
            Equal(0, launch.Order, "reordered launch should have compact order zero");
            Equal(1, recover.Order, "reordered recovery should have compact order one");
            Equal(2, dock.Order, "optional dock should have compact order two");
            True(dock.Optional, "the docking objective should remain optional");

            fixture.Planner.ActivatePlan(plan.PlanId);
            fixture.Planner.SkipObjective(plan.PlanId, dock.ObjectiveId, "Station unavailable");
            Equal(MissionObjectiveStatus.Skipped, dock.Status,
                "manual skip should immediately update persistent status");
            True(dock.HasManualResolution, "manual skip should be durable across recomputation");
            Equal("Station unavailable", dock.ManualNote,
                "skip reason should be retained for review");

            List<MissionPlanTimelineFact> facts = new List<MissionPlanTimelineFact>
            {
                Fact("training-launch", MissionObjectiveKind.Launch,
                    "Kerbin", "Flying", null, 0),
                Fact("training-recover", MissionObjectiveKind.Recover,
                    "Kerbin", "Landed", null, 100)
            };
            MissionPlanEvaluation evaluation = fixture.Planner.RecomputeProgress(
                plan.PlanId, facts);
            AssertAchieved(evaluation, launch, "training-launch");
            AssertAchieved(evaluation, recover, "training-recover");
            Equal(MissionObjectiveStatus.Skipped, Progress(evaluation, dock).Status,
                "optional manual skip should survive automatic evaluation");
            Equal(0, evaluation.Deviations.Count,
                "skipping an optional objective should not be a deviation");
            Equal(MissionPlanStatus.Completed, plan.Status,
                "required launch and recovery should complete the plan");
        }

        private static void ManualDeviationCanBeCorrected()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-correction", "Mun Orbiter Review", null);
            MissionPlanObjective launch = Add(fixture, plan, MissionObjectiveKind.Launch,
                "Launch orbiter");
            MissionPlanObjective body = AddTargeted(fixture, plan, MissionObjectiveKind.Body,
                "Enter Mun SOI", "Mun", null);
            MissionPlanObjective orbit = AddTargeted(fixture, plan, MissionObjectiveKind.Orbit,
                "Orbit Mun", "Mun", null);
            fixture.Planner.ActivatePlan(plan.PlanId);

            fixture.Planner.ManuallyMatchObjective(
                plan.PlanId, launch.ObjectiveId, "manual-launch", "Imported old launch");
            fixture.Planner.MarkObjectiveDeviated(
                plan.PlanId, body.ObjectiveId, "bad-soi-import", "Incorrect import");
            MissionPlanEvaluation beforeCorrection = fixture.Planner.EvaluatePlan(
                plan.PlanId, EmptyFacts());
            Equal(MissionObjectiveStatus.Achieved, Progress(beforeCorrection, launch).Status,
                "manual launch match should be authoritative");
            Equal("manual-launch", Progress(beforeCorrection, launch).MatchedFactId,
                "manual fact attribution should survive evaluation");
            Equal(MissionObjectiveStatus.Deviated, Progress(beforeCorrection, body).Status,
                "manual deviation should be authoritative");
            Equal(MissionObjectiveStatus.Current, Progress(beforeCorrection, orbit).Status,
                "the next unresolved objective should become current");
            Equal(1, beforeCorrection.Deviations.Count,
                "manual deviation should project exactly one review item");
            Equal(MissionPlanDeviationKind.Manual, beforeCorrection.Deviations[0].Kind,
                "manual review item should retain its kind");
            Equal("Incorrect import", beforeCorrection.Deviations[0].Detail,
                "manual review item should retain the correction note");

            fixture.Planner.ClearManualResolution(plan.PlanId, body.ObjectiveId);
            Equal(false, body.HasManualResolution,
                "clearing the incorrect decision should restore automatic matching");
            Equal(MissionObjectiveStatus.Pending, body.Status,
                "cleared objective should return to pending");
            Equal(string.Empty, body.ManualFactId,
                "cleared decision should not retain stale fact attribution");

            List<MissionPlanTimelineFact> correctedFacts = new List<MissionPlanTimelineFact>
            {
                Fact("correct-soi", MissionObjectiveKind.Body,
                    "Mun", "HighOrbit", "Mun", 100),
                Fact("correct-orbit", MissionObjectiveKind.Orbit,
                    "Mun", "Orbiting", null, 200)
            };
            string planBeforePureEvaluation = Snapshot(plan);
            MissionPlanEvaluation corrected = fixture.Planner.EvaluatePlan(
                plan.PlanId, correctedFacts);
            Equal(planBeforePureEvaluation, Snapshot(plan),
                "pure evaluation should not mutate persistent objective state");
            AssertAchieved(corrected, launch, "manual-launch");
            AssertAchieved(corrected, body, "correct-soi");
            AssertAchieved(corrected, orbit, "correct-orbit");
            Equal(0, corrected.Deviations.Count,
                "correcting the source fact should remove the stale manual deviation");
            Equal(MissionPlanStatus.Completed, corrected.SuggestedStatus,
                "the corrected objective sequence should complete");
        }

        private static void PendingOptionalObjectiveKeepsPlanActive()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-optional", "Optional Station Visit", null);
            MissionPlanObjective launch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Launch crew ferry");
            MissionPlanObjective dock = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Dock,
                "Optionally visit the station",
                null,
                null,
                null,
                null,
                true);
            fixture.Planner.ActivatePlan(plan.PlanId);

            List<MissionPlanTimelineFact> launchOnly = new List<MissionPlanTimelineFact>
            {
                Fact("optional-launch", MissionObjectiveKind.Launch,
                    "Kerbin", "Flying", null, 0)
            };
            MissionPlanEvaluation active = fixture.Planner.RecomputeProgress(
                plan.PlanId, launchOnly);

            Equal(MissionPlanStatus.Active, active.SuggestedStatus,
                "an unresolved optional step should keep the evaluated plan active");
            Equal(MissionPlanStatus.Active, plan.Status,
                "an unresolved optional step should keep persistent state active");
            AssertAchieved(active, launch, "optional-launch");
            Equal(MissionObjectiveStatus.Current, Progress(active, dock).Status,
                "the pending optional step should remain current");
            Equal(MissionObjectiveStatus.Current, dock.Status,
                "persistent optional progress should remain current");

            fixture.Planner.SkipObjective(
                plan.PlanId, dock.ObjectiveId, "Station visit omitted");
            MissionPlanEvaluation resolved = fixture.Planner.RecomputeProgress(
                plan.PlanId, launchOnly);
            Equal(MissionPlanStatus.Completed, resolved.SuggestedStatus,
                "resolving the optional step should allow completion");
            Equal(MissionPlanStatus.Completed, plan.Status,
                "resolved optional progress should complete persistent state");
            Equal(MissionObjectiveStatus.Skipped, Progress(resolved, dock).Status,
                "the optional step should retain its explicit skip");
            Equal(0, resolved.Deviations.Count,
                "an explicitly skipped optional step should not create a deviation");
        }

        private static void CompletedPlanAcceptsDurableManualCorrection()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-completed-correction", "Recovered History Review", null);
            MissionPlanObjective launch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Confirm historical launch");
            MissionPlanObjective complete = Add(
                fixture, plan, MissionObjectiveKind.Complete, "Close historical mission");
            fixture.Planner.ActivatePlan(plan.PlanId);

            MissionPlanTimelineFact completion = Fact(
                "overall-completion", MissionObjectiveKind.Complete,
                "Kerbin", "Landed", null, 100);
            completion.IsPlanCompletion = true;
            MissionPlanEvaluation incomplete = fixture.Planner.RecomputeProgress(
                plan.PlanId,
                new List<MissionPlanTimelineFact> { completion });
            Equal(MissionPlanStatus.Completed, plan.Status,
                "the overarching completion fact should end the plan");
            Equal(MissionObjectiveStatus.Deviated, Progress(incomplete, launch).Status,
                "the missing launch should initially require correction");
            Equal(2, incomplete.Deviations.Count,
                "the early completion and missing launch should both remain reviewable");
            Equal(launch.ObjectiveId,
                Deviation(incomplete, MissionPlanDeviationKind.MissingBeforeCompletion).ObjectiveId,
                "the missing-step review item should identify the launch");

            fixture.Planner.ManuallyMatchObjective(
                plan.PlanId,
                launch.ObjectiveId,
                "manual-completed-launch",
                "Matched after reviewing the completed flight");
            string matchedUtc = launch.MatchedUtc;
            True(!string.IsNullOrWhiteSpace(matchedUtc),
                "manual correction on a completed plan should record a timestamp");
            Equal(MissionObjectiveStatus.Achieved, launch.Status,
                "a completed plan should accept the manual match");
            Equal("manual-completed-launch", launch.MatchedFactId,
                "the completed-plan correction should retain its fact ID");

            MissionPlanEvaluation corrected = fixture.Planner.RecomputeProgress(
                plan.PlanId,
                new List<MissionPlanTimelineFact> { completion });
            Equal(MissionPlanStatus.Completed, plan.Status,
                "correcting history should not reopen an already completed plan");
            AssertAchieved(corrected, launch, "manual-completed-launch");
            Equal(matchedUtc, Progress(corrected, launch).MatchedUtc,
                "pure reconciliation should preserve the manual match timestamp");
            Equal(matchedUtc, launch.MatchedUtc,
                "persistent reconciliation should preserve the manual match timestamp");
            AssertAchieved(corrected, complete, "overall-completion");
            Equal(0, corrected.Deviations.Count,
                "the corrected objective should remove its stale missing-step deviation");
        }

        private static void ManualFactClaimConsumesCompletedPlanReplay()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-manual-claim", "Mun Orbital Review", null);
            MissionPlanObjective launch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Launch Mun orbiter");
            MissionPlanObjective body = AddTargeted(
                fixture, plan, MissionObjectiveKind.Body, "Enter Mun SOI", "Mun", null);
            MissionPlanObjective orbit = AddTargeted(
                fixture, plan, MissionObjectiveKind.Orbit, "Establish Mun orbit", "Mun", null);
            MissionPlanObjective complete = Add(
                fixture, plan, MissionObjectiveKind.Complete, "Close Mun mission");
            fixture.Planner.ActivatePlan(plan.PlanId);

            List<MissionPlanTimelineFact> facts = new List<MissionPlanTimelineFact>
            {
                Fact("claimed-launch", MissionObjectiveKind.Launch,
                    "Kerbin", "Flying", null, 0),
                Fact("claimed-orbit-early", MissionObjectiveKind.Orbit,
                    "Mun", "Orbiting", null, 100),
                Fact("claimed-body-late", MissionObjectiveKind.Body,
                    "Mun", "Orbiting", "Mun", 200),
                Fact("claimed-completion", MissionObjectiveKind.Complete,
                    "Kerbin", "Landed", null, 300)
            };

            MissionPlanEvaluation initial = fixture.Planner.RecomputeProgress(
                plan.PlanId, facts);
            Equal(MissionPlanStatus.Completed, plan.Status,
                "the reviewed fact stream should initially complete the plan");
            AssertAchieved(initial, launch, "claimed-launch");
            AssertAchieved(initial, body, "claimed-body-late");
            Equal(MissionObjectiveStatus.Deviated, Progress(initial, orbit).Status,
                "the early orbit should initially be classified out of order");
            Equal("claimed-orbit-early", Progress(initial, orbit).MatchedFactId,
                "the initial deviation should retain the exact early fact");
            AssertAchieved(initial, complete, "claimed-completion");
            Equal(1, initial.Deviations.Count,
                "the first pass should contain only the out-of-order review item");
            Equal(MissionPlanDeviationKind.OutOfOrder, initial.Deviations[0].Kind,
                "the first pass should classify the early orbit as out of order");

            fixture.Planner.ManuallyMatchObjective(
                plan.PlanId,
                orbit.ObjectiveId,
                "claimed-orbit-early",
                "Accepted after completed-mission review");
            Equal(true, orbit.HasManualResolution,
                "accepting the completed-plan fact should create a manual resolution");
            Equal("claimed-orbit-early", orbit.ManualFactId,
                "the manual resolution should explicitly claim the early fact");

            MissionPlanEvaluation replay = fixture.Planner.EvaluatePlan(plan.PlanId, facts);
            AssertAchieved(replay, orbit, "claimed-orbit-early");
            Equal(0, replay.Deviations.Count,
                "a manually claimed fact must not reappear as UnexpectedFact during evaluation");
            Equal(MissionPlanStatus.Completed, replay.SuggestedStatus,
                "claiming the fact should retain completed-plan status");

            MissionPlanEvaluation persisted = fixture.Planner.RecomputeProgress(
                plan.PlanId, facts);
            AssertAchieved(persisted, orbit, "claimed-orbit-early");
            Equal(0, persisted.Deviations.Count,
                "a manually claimed fact must remain consumed during recomputation");
            Equal(0, plan.Deviations.Count,
                "persistent plan state should not restore the stale UnexpectedFact");
            Equal("claimed-orbit-early", orbit.ManualFactId,
                "recomputation should preserve the claimed fact identity");
        }

        private static void DuplicateFactsArePureAndIdempotent()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-dual-launch", "Two Launch Campaign", null);
            MissionPlanObjective firstLaunch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Launch station core");
            MissionPlanObjective secondLaunch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Launch crew ferry");
            fixture.Planner.ActivatePlan(plan.PlanId);

            MissionPlanTimelineFact duplicateOne = Fact(
                "same-launch-notification", MissionObjectiveKind.Launch,
                "Kerbin", "Flying", null, 0);
            MissionPlanTimelineFact duplicateTwo = Fact(
                "SAME-LAUNCH-NOTIFICATION", MissionObjectiveKind.Launch,
                "Kerbin", "Flying", null, 1);
            List<MissionPlanTimelineFact> duplicates = new List<MissionPlanTimelineFact>
            {
                duplicateOne,
                duplicateTwo
            };
            string planBefore = Snapshot(plan);
            string factsBefore = Snapshot(duplicates);
            MissionPlanEvaluation first = fixture.Planner.EvaluatePlan(plan.PlanId, duplicates);
            MissionPlanEvaluation repeated = fixture.Planner.EvaluatePlan(plan.PlanId, duplicates);

            AssertAchieved(first, firstLaunch, "same-launch-notification");
            Equal(MissionObjectiveStatus.Current, Progress(first, secondLaunch).Status,
                "a duplicate notification must not satisfy a second launch objective");
            Equal(string.Empty, Progress(first, secondLaunch).MatchedFactId,
                "the second launch should remain unmatched");
            Equal(MissionPlanStatus.Active, first.SuggestedStatus,
                "one unique launch should leave the campaign active");
            Equal(Snapshot(first), Snapshot(repeated),
                "repeated evaluation should produce byte-equivalent JSON");
            Equal(planBefore, Snapshot(plan), "evaluation should not mutate the plan");
            Equal(factsBefore, Snapshot(duplicates), "evaluation should not mutate timeline facts");

            List<MissionPlanTimelineFact> twoUniqueLaunches =
                new List<MissionPlanTimelineFact>(duplicates);
            twoUniqueLaunches[1] = Fact(
                "crew-ferry-launch", MissionObjectiveKind.Launch,
                "Kerbin", "Flying", null, 10);
            MissionPlanEvaluation complete = fixture.Planner.EvaluatePlan(
                plan.PlanId, twoUniqueLaunches);
            AssertAchieved(complete, firstLaunch, "same-launch-notification");
            AssertAchieved(complete, secondLaunch, "crew-ferry-launch");
            Equal(MissionPlanStatus.Completed, complete.SuggestedStatus,
                "two distinct facts should satisfy two ordered launches");
            Equal(0, complete.Deviations.Count,
                "two distinct in-order launches should not deviate");
        }

        private static void MultiLaunchTreeRespectsSlots()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-assembly", "Kerbin Gateway Assembly", null);
            MissionPlanVesselSlot core = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Gateway Core", "Station core", true);
            MissionPlanVesselSlot tug = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Crew Tug", "Docking and return craft", true);
            MissionPlanObjective launchCore = fixture.Planner.AddObjective(
                plan.PlanId, MissionObjectiveKind.Launch, "Launch gateway core", core.SlotId);
            MissionPlanObjective launchTug = fixture.Planner.AddObjective(
                plan.PlanId, MissionObjectiveKind.Launch, "Launch crew tug", tug.SlotId);
            MissionPlanObjective dock = Add(
                fixture, plan, MissionObjectiveKind.Dock, "Dock tug with gateway");
            MissionPlanObjective separate = Add(
                fixture, plan, MissionObjectiveKind.Separate, "Separate tug for return");
            MissionPlanObjective recover = fixture.Planner.AddObjective(
                plan.PlanId, MissionObjectiveKind.Recover, "Recover crew tug", tug.SlotId);
            MissionPlanObjective complete = Add(
                fixture, plan, MissionObjectiveKind.Complete, "Close assembly mission");
            fixture.Planner.ActivatePlan(plan.PlanId);
            fixture.Planner.BindLaunch(
                plan.PlanId, core.SlotId, "mission-core", "vessel-core", FactTime(0));
            fixture.Planner.BindLaunch(
                plan.PlanId, tug.SlotId, "mission-tug", "vessel-tug", FactTime(10));

            MissionPlanTimelineFact outsider = Fact(
                "outsider-dock", MissionObjectiveKind.Dock,
                "Kerbin", "Orbiting", null, 15);
            outsider.MissionId = "mission-unrelated";
            outsider.VesselId = "vessel-unrelated";
            List<MissionPlanTimelineFact> facts = new List<MissionPlanTimelineFact>
            {
                SlottedFact("tree-01-core-launch", MissionObjectiveKind.Launch,
                    core, "mission-core", "vessel-core", 0),
                SlottedFact("tree-02-tug-launch", MissionObjectiveKind.Launch,
                    tug, "mission-tug", "vessel-tug", 10),
                outsider,
                SlottedFact("tree-03-combined-dock", MissionObjectiveKind.Dock,
                    core, "mission-gateway-combined", "vessel-gateway-stack", 20),
                SlottedFact("tree-04-tug-sortie", MissionObjectiveKind.Separate,
                    tug, "mission-tug-sortie", "vessel-tug-return", 30),
                SlottedFact("tree-05-tug-recovery", MissionObjectiveKind.Recover,
                    tug, "mission-tug-sortie", "vessel-tug-return", 40),
                SlottedFact("tree-06-complete", MissionObjectiveKind.Complete,
                    core, "mission-gateway-combined", "vessel-gateway", 50)
            };

            MissionPlanEvaluation evaluation = fixture.Planner.EvaluatePlan(plan.PlanId, facts);
            AssertAchieved(evaluation, launchCore, "tree-01-core-launch");
            AssertAchieved(evaluation, launchTug, "tree-02-tug-launch");
            AssertAchieved(evaluation, dock, "tree-03-combined-dock");
            AssertAchieved(evaluation, separate, "tree-04-tug-sortie");
            AssertAchieved(evaluation, recover, "tree-05-tug-recovery");
            AssertAchieved(evaluation, complete, "tree-06-complete");
            Equal(0, evaluation.Deviations.Count,
                "an unrelated mission-tree dock should be ignored, not treated as a deviation");
            Equal(MissionPlanStatus.Completed, evaluation.SuggestedStatus,
                "the two-launch assembly tree should complete");
            Equal(6, evaluation.Objectives.Count,
                "the tree evaluation should preserve the planned objective count");
            Equal("mission-core", core.MissionIds[0],
                "core slot should retain its launch mission alias");
            Equal("mission-tug", tug.MissionIds[0],
                "tug slot should retain its launch mission alias");
        }

        private static void ClearedVesselBindingCanBeReassigned()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-reassignment", "Correct Launch Assignment", null);
            MissionPlanVesselSlot mistaken = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Mistaken slot", "Incorrect assignment", true);
            MissionPlanVesselSlot correct = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Correct slot", "Actual launch", true);
            Add(fixture, plan, MissionObjectiveKind.Launch, "Launch the assigned vessel");
            fixture.Planner.ActivatePlan(plan.PlanId);

            fixture.Planner.BindLaunch(
                plan.PlanId,
                mistaken.SlotId,
                "mission-reassigned",
                "vessel-reassigned",
                FactTime(0));
            True(Contains(mistaken.MissionIds, "mission-reassigned"),
                "the initial mission binding should create a historical alias");
            True(Contains(mistaken.VesselIds, "vessel-reassigned"),
                "the initial vessel binding should create a historical alias");

            fixture.Planner.ClearVesselSlotBinding(plan.PlanId, mistaken.SlotId);
            Equal(string.Empty, mistaken.BoundMissionId,
                "clearing a mistaken link should remove the current mission binding");
            Equal(string.Empty, mistaken.BoundVesselId,
                "clearing a mistaken link should remove the current vessel binding");
            Equal(0, mistaken.MissionIds.Count,
                "clearing a mistaken link should remove all mission aliases");
            Equal(0, mistaken.VesselIds.Count,
                "clearing a mistaken link should remove all vessel aliases");

            fixture.Planner.BindLaunch(
                plan.PlanId,
                correct.SlotId,
                "MISSION-REASSIGNED",
                "VESSEL-REASSIGNED",
                FactTime(1));
            Equal("MISSION-REASSIGNED", correct.BoundMissionId,
                "the cleared mission identity should be assignable to another slot");
            Equal("VESSEL-REASSIGNED", correct.BoundVesselId,
                "the cleared vessel identity should be assignable to another slot");
            True(Contains(correct.MissionIds, "mission-reassigned"),
                "the corrected slot should own the mission alias");
            True(Contains(correct.VesselIds, "vessel-reassigned"),
                "the corrected slot should own the vessel alias");
            Equal(0, fixture.Planner.ValidateState().Count,
                "the reassigned binding should leave planner state valid");
        }

        private static void TwoVesselDockingRequiresBothPlannedSlots()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-two-slot-dock", "Dock Lander and Carrier", null);
            MissionPlanVesselSlot carrier = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Carrier", "Orbital carrier", true);
            MissionPlanVesselSlot lander = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Lander", "Surface lander", true);
            MissionPlanObjective docking = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Dock,
                "Dock lander with carrier",
                carrier.SlotId,
                "Mun",
                "Orbiting",
                null,
                false,
                lander.SlotId);
            fixture.Planner.ActivatePlan(plan.PlanId);
            fixture.Planner.BindLaunch(
                plan.PlanId, carrier.SlotId, "mission-carrier", "vessel-carrier", FactTime(0));
            fixture.Planner.BindLaunch(
                plan.PlanId, lander.SlotId, "mission-lander", "vessel-lander", FactTime(1));

            MissionPlanTimelineFact carrierOnly = Fact(
                "dock-carrier-only", MissionObjectiveKind.Dock,
                "Mun", "Orbiting", null, 10);
            carrierOnly.IsPlanScoped = true;
            carrierOnly.VesselSlotIds.Add(carrier.SlotId);
            MissionPlanEvaluation partial = fixture.Planner.EvaluatePlan(
                plan.PlanId,
                new List<MissionPlanTimelineFact> { carrierOnly });
            Equal(MissionObjectiveStatus.Current, Progress(partial, docking).Status,
                "a docking fact with only one participant must not satisfy a two-slot objective");
            Equal(MissionPlanStatus.Active, partial.SuggestedStatus,
                "a one-participant docking fact should leave the plan active");
            Equal(1, partial.Deviations.Count,
                "the unmatched one-participant docking fact should remain reviewable");

            MissionPlanTimelineFact bothParticipants = Fact(
                "dock-both-slots", MissionObjectiveKind.Dock,
                "Mun", "Orbiting", null, 20);
            bothParticipants.IsPlanScoped = true;
            bothParticipants.VesselSlotIds.Add(carrier.SlotId);
            bothParticipants.VesselSlotIds.Add(lander.SlotId);
            MissionPlanEvaluation complete = fixture.Planner.EvaluatePlan(
                plan.PlanId,
                new List<MissionPlanTimelineFact> { bothParticipants });
            AssertAchieved(complete, docking, "dock-both-slots");
            Equal(MissionPlanStatus.Completed, complete.SuggestedStatus,
                "a fact carrying both planned participants should complete the docking plan");
            Equal(0, complete.Deviations.Count,
                "the two-participant docking match should be clean");
            Equal(lander.SlotId, docking.RelatedVesselSlotId,
                "the docking objective should retain its explicit second participant");
        }

        private static void OnlyOverarchingCompletionClosesPlan()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-root-completion", "Carrier and Sortie", null);
            MissionPlanObjective launch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Launch carrier");
            MissionPlanObjective land = AddTargeted(
                fixture, plan, MissionObjectiveKind.Land, "Land the sortie", "Mun", "Landed");
            fixture.Planner.ActivatePlan(plan.PlanId);

            MissionPlanTimelineFact launchFact = Fact(
                "root-launch", MissionObjectiveKind.Launch,
                "Kerbin", "Flying", null, 0);
            MissionPlanTimelineFact childCompletion = Fact(
                "child-completion", MissionObjectiveKind.Complete,
                "Mun", "Orbiting", null, 50);
            childCompletion.IsPlanScoped = true;
            childCompletion.IsPlanCompletion = false;
            MissionPlanEvaluation childOnly = fixture.Planner.RecomputeProgress(
                plan.PlanId,
                new List<MissionPlanTimelineFact> { launchFact, childCompletion });

            Equal(MissionPlanStatus.Active, childOnly.SuggestedStatus,
                "a child completion should not suggest global plan completion");
            Equal(MissionPlanStatus.Active, plan.Status,
                "a child completion should leave persistent plan state active");
            AssertAchieved(childOnly, launch, "root-launch");
            Equal(MissionObjectiveStatus.Current, Progress(childOnly, land).Status,
                "remaining objectives should stay current after child completion");
            Equal(0, childOnly.Deviations.Count,
                "child completion should not mark remaining objectives missing");

            MissionPlanTimelineFact rootCompletion = Fact(
                "root-completion", MissionObjectiveKind.Complete,
                "Kerbin", "Landed", null, 100);
            rootCompletion.IsPlanScoped = true;
            rootCompletion.IsPlanCompletion = true;
            MissionPlanEvaluation closed = fixture.Planner.RecomputeProgress(
                plan.PlanId,
                new List<MissionPlanTimelineFact>
                {
                    launchFact,
                    childCompletion,
                    rootCompletion
                });
            Equal(MissionPlanStatus.Completed, closed.SuggestedStatus,
                "the overarching completion should suggest plan completion");
            Equal(MissionPlanStatus.Completed, plan.Status,
                "the overarching completion should close persistent plan state");
            Equal(MissionObjectiveStatus.Deviated, Progress(closed, land).Status,
                "the root completion should mark a missing required objective deviated");
            Equal(1, closed.Deviations.Count,
                "the root completion should record exactly one missing-step deviation");
            Equal(MissionPlanDeviationKind.MissingBeforeCompletion,
                closed.Deviations[0].Kind,
                "the remaining objective should use the missing-before-completion classification");
            Equal(land.ObjectiveId, closed.Deviations[0].ObjectiveId,
                "the root completion deviation should identify the missing landing");
        }

        private static void TerminalRootLossRemainsVisibleDeviation()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-terminal-loss", "Uncrewed Mun Probe", null);
            MissionPlanVesselSlot probe = fixture.Planner.AddVesselSlot(
                plan.PlanId, "Mun Probe", "Primary probe", true);
            MissionPlanObjective launch = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Launch,
                "Launch Mun probe",
                probe.SlotId);
            MissionPlanObjective broadCustom = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Custom,
                "Perform optional mission-specific work");
            MissionPlanObjective complete = fixture.Planner.AddObjective(
                plan.PlanId,
                MissionObjectiveKind.Complete,
                "Complete probe mission");
            fixture.Planner.ActivatePlan(plan.PlanId);
            fixture.Planner.BindLaunch(
                plan.PlanId,
                probe.SlotId,
                "mission-mun-probe",
                "vessel-mun-probe",
                FactTime(0));

            MissionPlanTimelineFact launchFact = SlottedFact(
                "probe-launch",
                MissionObjectiveKind.Launch,
                probe,
                "mission-mun-probe",
                "vessel-mun-probe",
                0);
            MissionPlanTimelineFact lossFact = Fact(
                "probe-terminal-loss",
                MissionObjectiveKind.Custom,
                "Mun",
                "LowOrbit",
                "Lost",
                100);
            lossFact.IsPlanScoped = true;
            lossFact.IsTerminalLoss = true;
            lossFact.MissionId = "mission-mun-probe";
            lossFact.VesselId = "vessel-mun-probe";
            lossFact.VesselSlotId = probe.SlotId;
            lossFact.VesselSlotIds.Add(probe.SlotId);

            MissionPlanEvaluation evaluation = fixture.Planner.RecomputeProgress(
                plan.PlanId,
                new List<MissionPlanTimelineFact> { launchFact, lossFact });

            AssertAchieved(evaluation, launch, "probe-launch");
            Equal(MissionObjectiveStatus.Current, Progress(evaluation, broadCustom).Status,
                "terminal loss must not satisfy a broad current custom objective");
            Equal(string.Empty, Progress(evaluation, broadCustom).MatchedFactId,
                "terminal loss must not be attributed to the broad custom objective");
            Equal(MissionObjectiveStatus.Pending, Progress(evaluation, complete).Status,
                "terminal loss must leave the later completion objective pending");
            Equal(string.Empty, Progress(evaluation, complete).MatchedFactId,
                "terminal loss must not be attributed as successful completion");
            Equal(MissionPlanStatus.Active, evaluation.SuggestedStatus,
                "terminal loss alone must not suggest plan completion");
            Equal(MissionPlanStatus.Active, plan.Status,
                "terminal loss alone must leave persistent plan state active");
            Equal(1, evaluation.Deviations.Count,
                "terminal loss should create one visible deviation");
            Equal(MissionPlanDeviationKind.UnexpectedFact,
                evaluation.Deviations[0].Kind,
                "terminal loss should use the unexpected-fact classification");
            Equal("probe-terminal-loss", evaluation.Deviations[0].FactId,
                "terminal-loss deviation should retain its source fact");
            Equal(broadCustom.ObjectiveId, evaluation.Deviations[0].ObjectiveId,
                "terminal-loss deviation should point at the current custom objective");
            True(!string.IsNullOrWhiteSpace(evaluation.Deviations[0].Detail),
                "terminal-loss deviation should remain visible with explanatory detail");
        }

        private static void TerminalPlansAndValidationPreserveHistory()
        {
            PlannerFixture fixture = new PlannerFixture();
            MissionPlan plan = fixture.Planner.CreatePlan(
                "campaign-abandoned", "Cancelled Eve Probe", "Awaiting launch window");
            MissionPlanObjective launch = Add(
                fixture, plan, MissionObjectiveKind.Launch, "Launch Eve probe");
            fixture.Planner.AbandonPlan(plan.PlanId, "Launch vehicle unavailable");
            Equal(MissionPlanStatus.Abandoned, plan.Status,
                "abandon should end the plan without deleting it");
            True(plan.Notes.IndexOf("Launch vehicle unavailable", StringComparison.Ordinal) >= 0,
                "abandon reason should be retained in notes");
            True(!string.IsNullOrWhiteSpace(plan.EndedUtc),
                "abandon should record an end timestamp");
            Equal(1, fixture.State.Plans.Count,
                "abandon should preserve the full plan in state");
            Same(launch, fixture.State.Plans[0].Objectives[0],
                "abandon should preserve planned objectives");

            fixture.Planner.SetPlanArchived(plan.PlanId, true);
            True(plan.Archived, "an abandoned plan should be archivable");
            Equal(1, fixture.State.Plans.Count,
                "archive should be a flag, never a hard delete");
            Throws<InvalidOperationException>(
                delegate { fixture.Planner.UpdatePlan(plan.PlanId, "Rewrite", null); },
                "an abandoned plan should reject structural edits");

            MissionPlanState invalid = new MissionPlanState
            {
                Plans = new List<MissionPlan>
                {
                    new MissionPlan
                    {
                        PlanId = "invalid-plan",
                        Title = "Invalid bindings",
                        VesselSlots = new List<MissionPlanVesselSlot>
                        {
                            new MissionPlanVesselSlot
                            {
                                SlotId = "slot-a",
                                Name = "A",
                                BoundVesselId = "same-vessel"
                            },
                            new MissionPlanVesselSlot
                            {
                                SlotId = "slot-b",
                                Name = "B",
                                BoundVesselId = "SAME-VESSEL"
                            }
                        },
                        Objectives = new List<MissionPlanObjective>
                        {
                            new MissionPlanObjective
                            {
                                ObjectiveId = "objective-invalid",
                                Title = "Unknown slot objective",
                                Kind = MissionObjectiveKind.Launch,
                                VesselSlotId = "slot-missing"
                            }
                        }
                    }
                }
            };
            List<string> errors = MissionPlanner.ValidateState(invalid);
            True(HasError(errors, "binds vessel"),
                "validation should report duplicate live vessel ownership");
            True(HasError(errors, "unknown vessel slot"),
                "validation should report an objective's missing slot");

            MissionPlanState beforeReplacement = fixture.Planner.State;
            Throws<InvalidOperationException>(
                delegate { fixture.Planner.ReplaceState(invalid, false); },
                "replace should reject invalid persistent state");
            Same(beforeReplacement, fixture.Planner.State,
                "rejected replacement should leave authoritative state unchanged");
            Throws<ArgumentException>(
                delegate { fixture.Planner.CreatePlan("campaign", "  ", null); },
                "create should reject an empty title");
            Equal(1, fixture.State.Plans.Count,
                "rejected operations should not erase historical plans");
        }

        private static MissionPlanObjective Add(
            PlannerFixture fixture,
            MissionPlan plan,
            MissionObjectiveKind kind,
            string title)
        {
            return fixture.Planner.AddObjective(plan.PlanId, kind, title);
        }

        private static MissionPlanObjective AddTargeted(
            PlannerFixture fixture,
            MissionPlan plan,
            MissionObjectiveKind kind,
            string title,
            string body,
            string situation)
        {
            return fixture.Planner.AddObjective(
                plan.PlanId,
                kind,
                title,
                null,
                body,
                situation);
        }

        private static MissionPlanTimelineFact Fact(
            string id,
            MissionObjectiveKind kind,
            string body,
            string situation,
            string value,
            int seconds)
        {
            return new MissionPlanTimelineFact
            {
                FactId = id,
                Kind = kind,
                IsPlanCompletion = kind == MissionObjectiveKind.Complete,
                RecordedUtc = FactTime(seconds),
                FlightTimeSeconds = seconds,
                Body = body,
                Situation = situation,
                Value = value,
                Title = id
            };
        }

        private static MissionPlanTimelineFact SlottedFact(
            string id,
            MissionObjectiveKind kind,
            MissionPlanVesselSlot slot,
            string missionId,
            string vesselId,
            int seconds)
        {
            MissionPlanTimelineFact fact = Fact(
                id, kind, "Kerbin", "Orbiting", null, seconds);
            fact.VesselSlotId = slot.SlotId;
            fact.MissionId = missionId;
            fact.VesselId = vesselId;
            return fact;
        }

        private static string FactTime(int seconds)
        {
            return new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(seconds)
                .ToString("o");
        }

        private static IList<MissionPlanTimelineFact> EmptyFacts()
        {
            return new List<MissionPlanTimelineFact>();
        }

        private static MissionPlanObjectiveProgress Progress(
            MissionPlanEvaluation evaluation,
            MissionPlanObjective objective)
        {
            for (int index = 0; index < evaluation.Objectives.Count; index++)
            {
                if (string.Equals(
                    evaluation.Objectives[index].ObjectiveId,
                    objective.ObjectiveId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return evaluation.Objectives[index];
                }
            }

            throw new InvalidOperationException(
                "Evaluation omitted objective " + objective.ObjectiveId);
        }

        private static MissionPlanDeviation Deviation(
            MissionPlanEvaluation evaluation,
            MissionPlanDeviationKind kind)
        {
            for (int index = 0; index < evaluation.Deviations.Count; index++)
            {
                if (evaluation.Deviations[index].Kind == kind)
                {
                    return evaluation.Deviations[index];
                }
            }

            throw new InvalidOperationException("Evaluation omitted deviation kind " + kind);
        }

        private static void AssertAchieved(
            MissionPlanEvaluation evaluation,
            MissionPlanObjective objective,
            string factId)
        {
            MissionPlanObjectiveProgress progress = Progress(evaluation, objective);
            Equal(MissionObjectiveStatus.Achieved, progress.Status,
                objective.Title + " should be achieved");
            Equal(factId, progress.MatchedFactId,
                objective.Title + " should retain its matching fact");
            if (!string.IsNullOrWhiteSpace(factId) && !factId.StartsWith("manual-"))
            {
                True(!string.IsNullOrWhiteSpace(progress.MatchedUtc),
                    objective.Title + " should retain the fact timestamp");
            }
        }

        private static bool Contains(List<string> values, string expected)
        {
            if (values == null)
            {
                return false;
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasError(List<string> errors, string fragment)
        {
            for (int index = 0; index < errors.Count; index++)
            {
                if (errors[index].IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Snapshot(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None);
        }

        private static void Run(string name, Action scenario)
        {
            scenario();
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void True(bool condition, string message)
        {
            _assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            _assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + "; expected <" + expected + ">, actual <" + actual + ">");
            }
        }

        private static void Same(object expected, object actual, string message)
        {
            _assertions++;
            if (!ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            _assertions++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private sealed class PlannerFixture
        {
            public PlannerFixture()
            {
                State = new MissionPlanState();
                Deterministic = new Determinism();
                Snapshots = new List<string>();
                Planner = new MissionPlanner(
                    State,
                    delegate(MissionPlanState saved)
                    {
                        SaveCount++;
                        Snapshots.Add(Snapshot(saved));
                    },
                    Deterministic.NextId,
                    Deterministic.UtcNow);
            }

            public MissionPlanState State { get; private set; }
            public MissionPlanner Planner { get; private set; }
            public Determinism Deterministic { get; private set; }
            public List<string> Snapshots { get; private set; }
            public int SaveCount { get; private set; }
        }

        private sealed class MemoryStore : IMissionPlanStore
        {
            public MemoryStore(MissionPlanState loaded)
            {
                Loaded = loaded;
            }

            public MissionPlanState Loaded { get; private set; }
            public MissionPlanState LastSaved { get; private set; }
            public int SaveCount { get; private set; }

            public MissionPlanState Load()
            {
                return Loaded;
            }

            public void Save(MissionPlanState state)
            {
                LastSaved = state;
                SaveCount++;
            }
        }

        private sealed class Determinism
        {
            private int _id;
            private int _time;

            public string NextId()
            {
                _id++;
                return "id-" + _id.ToString("D3");
            }

            public string UtcNow()
            {
                int current = _time;
                _time++;
                return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(current)
                    .ToString("o");
            }
        }
    }
}
