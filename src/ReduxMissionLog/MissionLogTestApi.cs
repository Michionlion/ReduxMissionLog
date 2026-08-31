using System;
using System.Reflection;
using MoonSharp.Interpreter;

namespace ReduxMissionLog
{
    internal sealed class MissionLogTestApi : IDisposable
    {
        private const string ModId = "ReduxMissionLog";
        private readonly MissionTracker _tracker;
        private readonly MissionPlanner _planner;
        private readonly MissionPlanStore _planStore;
        private readonly MissionPlannerCoordinator _plannerCoordinator;
        private readonly MissionLogWindow _window;
        private readonly Action<string> _info;
        private IDisposable _registration;
        private bool _registrationFailureLogged;

        public MissionLogTestApi(
            MissionTracker tracker,
            MissionPlanner planner,
            MissionPlanStore planStore,
            MissionPlannerCoordinator plannerCoordinator,
            MissionLogWindow window,
            Action<string> info)
        {
            _tracker = tracker;
            _planner = planner;
            _planStore = planStore;
            _plannerCoordinator = plannerCoordinator;
            _window = window;
            _info = info;
        }

        public bool TryRegister()
        {
            try
            {
                if (_registration != null)
                {
                    return true;
                }
                Type registry = Type.GetType(
                    "ReduxTestHarness.TestApiRegistry, ReduxTestHarness",
                    false);
                if (registry == null)
                {
                    return false;
                }
                MethodInfo register = registry.GetMethod(
                    "Register",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Action<Script, Table>) },
                    null);
                if (register == null)
                {
                    return false;
                }
                var builder = new Action<Script, Table>(Configure);
                _registration = register.Invoke(null, new object[] { ModId, builder }) as IDisposable;
                if (_registration != null)
                {
                    _info("Registered optional ReduxTestHarness semantic API.");
                }
                return _registration != null;
            }
            catch (Exception error)
            {
                if (!_registrationFailureLogged)
                {
                    _registrationFailureLogged = true;
                    Exception cause = error is TargetInvocationException && error.InnerException != null
                        ? error.InnerException
                        : error;
                    _info("Optional ReduxTestHarness API registration was unavailable: " +
                        cause.Message);
                }
                return false;
            }
        }

        public void Dispose()
        {
            if (_registration != null)
            {
                _registration.Dispose();
                _registration = null;
            }
        }

        private void Configure(Script script, Table api)
        {
            api.Set("begin_test_session", Callback("ReduxMissionLog.begin_test_session", (context, args) =>
            {
                _tracker.BeginIsolatedTestSession();
                _planStore.UseIsolatedTestState();
                _planStore.Reset();
                _planner.ReplaceState(_planStore.Load(), false);
                _plannerCoordinator.Invalidate();
                return DynValue.Nil;
            }));
            api.Set("reload_archive", Callback("ReduxMissionLog.reload_archive", (context, args) =>
            {
                _tracker.ReloadArchive();
                _planner.ReplaceState(_planStore.Load(), false);
                _plannerCoordinator.Invalidate();
                return DynValue.Nil;
            }));
            api.Set("end_test_session", Callback("ReduxMissionLog.end_test_session", (context, args) =>
            {
                _tracker.EndIsolatedTestSession();
                _planStore.UseProductionState();
                _planner.ReplaceState(_planStore.Load(), false);
                _plannerCoordinator.Invalidate();
                return DynValue.Nil;
            }));
            api.Set("archive_count", Callback("ReduxMissionLog.archive_count", (context, args) =>
                DynValue.NewNumber(_tracker.Archive.Missions.Count)));
            api.Set("archive_path", Callback("ReduxMissionLog.archive_path", (context, args) =>
                DynValue.NewString(_tracker.ArchivePath)));
            api.Set("plan_count", Callback("ReduxMissionLog.plan_count", (context, args) =>
                DynValue.NewNumber(_planner.State.Plans.Count)));
            api.Set("plan_path", Callback("ReduxMissionLog.plan_path", (context, args) =>
                DynValue.NewString(_planStore.Path)));
            api.Set("current", Callback("ReduxMissionLog.current", (context, args) =>
                MissionValue(script, _tracker.GetCurrent())));
            api.Set("latest", Callback("ReduxMissionLog.latest", (context, args) =>
                MissionValue(script, _tracker.GetLatest())));
            api.Set("current_has_event", Callback("ReduxMissionLog.current_has_event", (context, args) =>
            {
                string kind = RequiredString(args, 0, "event kind");
                return DynValue.NewBoolean(_tracker.CurrentHasEvent(kind));
            }));
            api.Set("complete_current", Callback("ReduxMissionLog.complete_current", (context, args) =>
            {
                string status = args.Count == 0 || args[0].IsNil()
                    ? "Completed"
                    : RequiredString(args, 0, "status");
                _tracker.CompleteCurrent(status);
                return DynValue.Nil;
            }));
            api.Set("open_window", Callback("ReduxMissionLog.open_window", (context, args) =>
            {
                _window.SetVisible(true);
                return DynValue.Nil;
            }));
            api.Set("open_mission", Callback("ReduxMissionLog.open_mission", (context, args) =>
            {
                string missionId = RequiredString(args, 0, "mission ID");
                MissionRecord mission = _tracker.FindById(missionId);
                if (mission == null)
                {
                    throw new ScriptRuntimeException("Mission does not exist: " + missionId);
                }
                _window.OpenMission(mission);
                return DynValue.Nil;
            }));
            api.Set("open_archive", Callback("ReduxMissionLog.open_archive", (context, args) =>
            {
                _window.OpenArchive();
                return DynValue.Nil;
            }));
            api.Set("open_planner", Callback("ReduxMissionLog.open_planner", (context, args) =>
            {
                string planId = OptionalText(args, 0);
                if (string.IsNullOrWhiteSpace(planId))
                {
                    _window.OpenPlanner();
                }
                else
                {
                    FindPlan(planId);
                    _window.OpenPlanner(planId);
                }
                return DynValue.Nil;
            }));
            api.Set("planner_ui_state", Callback(
                "ReduxMissionLog.planner_ui_state",
                (context, args) => PlannerUiState(script)));
            api.Set("refresh_saved_vehicles", Callback(
                "ReduxMissionLog.refresh_saved_vehicles",
                (context, args) =>
                {
                    _window.RefreshSavedVehicles();
                    return DynValue.NewNumber(_window.SavedVehicleCount);
                }));
            api.Set("open_editor", Callback("ReduxMissionLog.open_editor", (context, args) =>
            {
                _window.OpenEditorForReview(RequiredMission(args, 0));
                return DynValue.Nil;
            }));
            api.Set("open_organizer", Callback(
                "ReduxMissionLog.open_organizer",
                (context, args) =>
                {
                    _window.OpenOrganizerForReview(RequiredMission(args, 0));
                    return DynValue.Nil;
                }));
            api.Set("set_review_scroll", Callback(
                "ReduxMissionLog.set_review_scroll",
                (context, args) =>
                {
                    _window.SetReviewScroll(
                        RequiredString(args, 0, "review view"),
                        RequiredString(args, 1, "review scroll anchor"));
                    return DynValue.Nil;
                }));
            api.Set("set_archive_collapsed", Callback(
                "ReduxMissionLog.set_archive_collapsed",
                (context, args) =>
                {
                    _window.SetArchiveCollapsed(
                        RequiredString(args, 0, "mission ID"),
                        RequiredBoolean(args, 1, "collapsed state"));
                    return DynValue.Nil;
                }));
            api.Set("review_ui_state", Callback(
                "ReduxMissionLog.review_ui_state",
                (context, args) => ReviewUiState(script)));
            api.Set("window_visible", Callback("ReduxMissionLog.window_visible", (context, args) =>
                DynValue.NewBoolean(_window.Visible)));
            api.Set("ui_stack", Callback("ReduxMissionLog.ui_stack", (context, args) =>
                DynValue.NewString(_window.UiStack)));
            api.Set("selected_mission_id", Callback(
                "ReduxMissionLog.selected_mission_id",
                (context, args) => StringValue(_window.SelectedMissionId)));
            api.Set("rendered_timeline_count", Callback(
                "ReduxMissionLog.rendered_timeline_count",
                (context, args) => DynValue.NewNumber(_window.RenderedTimelineCount)));
            api.Set("set_timeline_event_expanded", Callback(
                "ReduxMissionLog.set_timeline_event_expanded",
                (context, args) =>
                {
                    _window.SetTimelineEventExpanded(
                        RequiredString(args, 0, "event ID"),
                        RequiredBoolean(args, 1, "expanded state"));
                    return DynValue.Nil;
                }));
            api.Set("expanded_timeline_event_count", Callback(
                "ReduxMissionLog.expanded_timeline_event_count",
                (context, args) =>
                    DynValue.NewNumber(_window.ExpandedTimelineEventCount)));
            api.Set("plan", Callback("ReduxMissionLog.plan", (context, args) =>
                PlanValue(script, FindPlan(RequiredString(args, 0, "plan ID")))));
            api.Set("create_plan", Callback("ReduxMissionLog.create_plan", (context, args) =>
                PlanValue(script, _planner.CreatePlan(
                    RequiredString(args, 0, "campaign ID"),
                    RequiredString(args, 1, "plan title"),
                    OptionalText(args, 2)))));
            api.Set("plan_add_vessel", Callback(
                "ReduxMissionLog.plan_add_vessel",
                (context, args) => SlotValue(script, _planner.AddVesselSlot(
                    RequiredString(args, 0, "plan ID"),
                    RequiredString(args, 1, "vessel-slot name"),
                    OptionalText(args, 2),
                    OptionalBoolean(args, 3, true)))));
            api.Set("plan_select_vehicle", Callback(
                "ReduxMissionLog.plan_select_vehicle",
                (context, args) =>
                {
                    _planner.SelectSavedVehicle(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "slot ID"),
                        RequiredString(args, 2, "saved vehicle ID"),
                        RequiredString(args, 3, "saved vehicle name"),
                        OptionalText(args, 4),
                        OptionalText(args, 5));
                    return DynValue.Nil;
                }));
            api.Set("plan_add_objective", Callback(
                "ReduxMissionLog.plan_add_objective",
                (context, args) => ObjectiveValue(script, _planner.AddObjective(
                    RequiredString(args, 0, "plan ID"),
                    RequiredObjectiveKind(args, 1),
                    RequiredString(args, 2, "objective title"),
                    OptionalText(args, 3),
                    OptionalText(args, 4),
                    OptionalText(args, 5),
                    OptionalText(args, 6),
                    OptionalBoolean(args, 7, false),
                    OptionalText(args, 8)))));
            api.Set("plan_activate", Callback(
                "ReduxMissionLog.plan_activate",
                (context, args) =>
                {
                    _planner.ActivatePlan(RequiredString(args, 0, "plan ID"));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_bind_vessel", Callback(
                "ReduxMissionLog.plan_bind_vessel",
                (context, args) =>
                {
                    _planner.BindLaunch(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "slot ID"),
                        RequiredString(args, 2, "mission ID"),
                        RequiredString(args, 3, "vessel ID"),
                        OptionalText(args, 4));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_clear_vessel_binding", Callback(
                "ReduxMissionLog.plan_clear_vessel_binding",
                (context, args) =>
                {
                    _planner.ClearVesselSlotBinding(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "slot ID"));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_skip_objective", Callback(
                "ReduxMissionLog.plan_skip_objective",
                (context, args) =>
                {
                    _planner.SkipObjective(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "objective ID"),
                        OptionalText(args, 2));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_match_objective", Callback(
                "ReduxMissionLog.plan_match_objective",
                (context, args) =>
                {
                    _planner.ManuallyMatchObjective(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "objective ID"),
                        OptionalText(args, 2),
                        OptionalText(args, 3));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_mark_deviated", Callback(
                "ReduxMissionLog.plan_mark_deviated",
                (context, args) =>
                {
                    _planner.MarkObjectiveDeviated(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "objective ID"),
                        OptionalText(args, 2),
                        OptionalText(args, 3));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_clear_resolution", Callback(
                "ReduxMissionLog.plan_clear_resolution",
                (context, args) =>
                {
                    _planner.ClearManualResolution(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "objective ID"));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_reorder_objective", Callback(
                "ReduxMissionLog.plan_reorder_objective",
                (context, args) =>
                {
                    _planner.ReorderObjective(
                        RequiredString(args, 0, "plan ID"),
                        RequiredString(args, 1, "objective ID"),
                        RequiredInteger(args, 2, "objective index"));
                    _plannerCoordinator.Invalidate();
                    return DynValue.Nil;
                }));
            api.Set("plan_recompute", Callback(
                "ReduxMissionLog.plan_recompute",
                (context, args) =>
                {
                    MissionPlan plan = FindPlan(RequiredString(args, 0, "plan ID"));
                    _planner.RecomputeProgress(
                        plan.PlanId,
                        MissionPlanTimelineAdapter.BuildFacts(_tracker, plan));
                    _plannerCoordinator.Invalidate();
                    return PlanValue(script, plan);
                }));
            api.Set("plan_facts", Callback(
                "ReduxMissionLog.plan_facts",
                (context, args) => PlanFactsValue(
                    script,
                    MissionPlanTimelineAdapter.BuildFacts(
                        _tracker,
                        FindPlan(RequiredString(args, 0, "plan ID"))))));
            api.Set("scenario_launch", Callback("ReduxMissionLog.scenario_launch", (context, args) =>
            {
                MissionRecord mission = _tracker.ScenarioLaunch(
                    RequiredString(args, 0, "mission ID"),
                    RequiredString(args, 1, "mission title"),
                    RequiredString(args, 2, "vessel ID"));
                return MissionValue(script, mission);
            }));
            api.Set("scenario_dock", Callback("ReduxMissionLog.scenario_dock", (context, args) =>
            {
                MissionRecord mission = _tracker.ScenarioDock(
                    RequiredString(args, 0, "left vessel ID"),
                    RequiredString(args, 1, "right vessel ID"),
                    RequiredString(args, 2, "result vessel ID"),
                    RequiredString(args, 3, "result name"),
                    RequiredString(args, 4, "operation ID"),
                    OptionalBoolean(args, 5, false),
                    OptionalNonNegativeNumber(args, 6, 10.0, "flight time"),
                    OptionalNonEmptyString(args, 7, "Kerbin", "body"),
                    OptionalNonEmptyString(args, 8, "Orbiting", "situation"));
                return MissionValue(script, mission);
            }));
            api.Set("scenario_split", Callback("ReduxMissionLog.scenario_split", (context, args) =>
            {
                MissionRecord mission = _tracker.ScenarioSplit(
                    RequiredString(args, 0, "source vessel ID"),
                    RequiredString(args, 1, "continuation vessel ID"),
                    RequiredString(args, 2, "detached vessel ID"),
                    RequiredString(args, 3, "detached name"),
                    RequiredString(args, 4, "detached travel ID"),
                    RequiredString(args, 5, "operation ID"),
                    OptionalNonNegativeNumber(args, 6, 20.0, "flight time"),
                    OptionalNonEmptyString(args, 7, "Mun", "body"),
                    OptionalNonEmptyString(args, 8, "Orbiting", "situation"));
                return MissionValue(script, mission);
            }));
            api.Set("scenario_adopt", Callback("ReduxMissionLog.scenario_adopt", (context, args) =>
            {
                _tracker.ScenarioAdopt(
                    RequiredString(args, 0, "child mission ID"),
                    RequiredString(args, 1, "parent mission ID"));
                return DynValue.Nil;
            }));
            api.Set("scenario_unlink", Callback("ReduxMissionLog.scenario_unlink", (context, args) =>
            {
                _tracker.ScenarioUnlink(RequiredString(args, 0, "mission ID"));
                return DynValue.Nil;
            }));
            api.Set("scenario_status", Callback("ReduxMissionLog.scenario_status", (context, args) =>
            {
                _tracker.ScenarioStatus(
                    RequiredString(args, 0, "vessel ID"),
                    RequiredString(args, 1, "status"));
                return DynValue.Nil;
            }));
            api.Set("scenario_track", Callback("ReduxMissionLog.scenario_track", (context, args) =>
                MissionValue(script, _tracker.ScenarioTrack(
                    RequiredString(args, 0, "mission ID"),
                    RequiredString(args, 1, "vessel ID")))));
            api.Set("scenario_event", Callback("ReduxMissionLog.scenario_event", (context, args) =>
                MissionValue(script, _tracker.ScenarioEvent(
                    RequiredString(args, 0, "mission ID"),
                    RequiredString(args, 1, "event kind"),
                    RequiredString(args, 2, "event title"),
                    RequiredNonNegativeNumber(args, 3, "flight time"),
                    RequiredString(args, 4, "body"),
                    RequiredString(args, 5, "situation"),
                    OptionalString(args, 6, "operation ID")))));
            api.Set("scenario_records", Callback(
                "ReduxMissionLog.scenario_records",
                (context, args) => MissionValue(script, _tracker.ScenarioRecords(
                    RequiredString(args, 0, "mission ID"),
                    RequiredNonNegativeNumber(args, 1, "altitude"),
                    RequiredNonNegativeNumber(args, 2, "speed"),
                    RequiredNonNegativeNumber(args, 3, "g-force")))));
            api.Set("scenario_note", Callback("ReduxMissionLog.scenario_note", (context, args) =>
                MissionValue(script, _tracker.ScenarioNote(
                    RequiredString(args, 0, "mission ID"),
                    RequiredText(args, 1, "mission note")))));
            api.Set("scenario_crew", Callback("ReduxMissionLog.scenario_crew", (context, args) =>
            {
                string missionId = RequiredString(args, 0, "mission ID");
                var crew = new System.Collections.Generic.List<string>();
                for (int index = 1; index < args.Count; index++)
                {
                    crew.Add(RequiredString(args, index, "crew name"));
                }
                return MissionValue(script, _tracker.ScenarioCrew(missionId, crew));
            }));
            api.Set("scenario_review", Callback("ReduxMissionLog.scenario_review", (context, args) =>
                MissionValue(script, _tracker.ScenarioReview(
                    RequiredString(args, 0, "mission ID"),
                    RequiredText(args, 1, "review reason")))));
            api.Set("mission", Callback("ReduxMissionLog.mission", (context, args) =>
                MissionValue(script,
                    _tracker.FindById(RequiredString(args, 0, "mission ID")))));
            api.Set("mission_timeline", Callback("ReduxMissionLog.mission_timeline", (context, args) =>
            {
                string missionId = RequiredString(args, 0, "mission ID");
                MissionRecord mission = _tracker.FindById(missionId);
                if (mission == null)
                {
                    throw new ScriptRuntimeException("Mission does not exist: " + missionId);
                }
                return TimelineValue(script, mission);
            }));
            api.Set("tree_snapshot", Callback("ReduxMissionLog.tree_snapshot", (context, args) =>
                TreeSnapshot(script)));
            api.Set("validate_tree", Callback("ReduxMissionLog.validate_tree", (context, args) =>
                StringListValue(script, _tracker.ValidateTree())));
        }

        private DynValue TreeSnapshot(Script script)
        {
            var result = new Table(script);
            result.Set("rootCount", DynValue.NewNumber(_tracker.GetRoots().Count));
            result.Set("nodeCount", DynValue.NewNumber(_tracker.Archive.Missions.Count));
            result.Set("errors", StringListValue(script, _tracker.ValidateTree()));
            var nodes = new Table(script);
            for (int index = 0; index < _tracker.Archive.Missions.Count; index++)
            {
                nodes.Set(index + 1,
                    MissionValue(script, _tracker.Archive.Missions[index]));
            }
            result.Set("nodes", DynValue.NewTable(nodes));
            return DynValue.NewTable(result);
        }

        private DynValue ReviewUiState(Script script)
        {
            var result = new Table(script);
            result.Set("visible", DynValue.NewBoolean(_window.Visible));
            result.Set("view", DynValue.NewString(_window.ReviewView));
            result.Set("sheet", DynValue.NewString(_window.ReviewSheet));
            result.Set("selectedMissionId", StringValue(_window.SelectedMissionId));
            result.Set("renderedTimelineCount",
                DynValue.NewNumber(_window.RenderedTimelineCount));
            result.Set("archiveRenderedNodeCount",
                DynValue.NewNumber(_window.ArchiveRenderedNodeCount));
            result.Set("collapsedMissionCount",
                DynValue.NewNumber(_window.CollapsedMissionCount));
            result.Set("scrollValue", DynValue.NewNumber(_window.ReviewScrollValue));
            result.Set("scrollMaximum", DynValue.NewNumber(_window.ReviewScrollMaximum));
            result.Set("scrollNormalized",
                DynValue.NewNumber(_window.ReviewScrollNormalized));
            result.Set("scrollAnchor", DynValue.NewString(_window.ReviewScrollAnchor));
            result.Set("windowWidth", DynValue.NewNumber(_window.ReviewWindowWidth));
            result.Set("windowHeight", DynValue.NewNumber(_window.ReviewWindowHeight));
            return DynValue.NewTable(result);
        }

        private DynValue PlannerUiState(Script script)
        {
            var result = new Table(script);
            result.Set("visible", DynValue.NewBoolean(_window.Visible));
            result.Set("view", StringValue(_window.ReviewView));
            result.Set("selectedPlanId", StringValue(_window.SelectedPlanId));
            result.Set("status", StringValue(_window.SelectedPlanStatus));
            result.Set("progress", StringValue(_window.SelectedPlanProgress));
            result.Set("deviationCount",
                DynValue.NewNumber(_window.SelectedPlanDeviationCount));
            result.Set("renderedPlanCount", DynValue.NewNumber(_window.RenderedPlanCount));
            result.Set("renderedVesselCount",
                DynValue.NewNumber(_window.RenderedPlanVesselCount));
            result.Set("renderedObjectiveCount",
                DynValue.NewNumber(_window.RenderedPlanObjectiveCount));
            result.Set("renderedDeviationCount",
                DynValue.NewNumber(_window.RenderedPlanDeviationCount));
            result.Set("savedVehicleCount", DynValue.NewNumber(_window.SavedVehicleCount));
            result.Set("createEditorVisible",
                DynValue.NewBoolean(_window.PlannerCreateEditorVisible));
            result.Set("planEditorVisible",
                DynValue.NewBoolean(_window.PlannerPlanEditorVisible));
            result.Set("vesselEditorVisible",
                DynValue.NewBoolean(_window.PlannerVesselEditorVisible));
            result.Set("objectiveEditorVisible",
                DynValue.NewBoolean(_window.PlannerObjectiveEditorVisible));
            result.Set("windowWidth", DynValue.NewNumber(_window.ReviewWindowWidth));
            result.Set("windowHeight", DynValue.NewNumber(_window.ReviewWindowHeight));
            return DynValue.NewTable(result);
        }

        private MissionPlan FindPlan(string planId)
        {
            for (int index = 0; index < _planner.State.Plans.Count; index++)
            {
                MissionPlan plan = _planner.State.Plans[index];
                if (plan != null && string.Equals(
                    plan.PlanId, planId, StringComparison.Ordinal))
                {
                    return plan;
                }
            }
            throw new ScriptRuntimeException("Mission plan does not exist: " + planId);
        }

        private DynValue PlanValue(Script script, MissionPlan plan)
        {
            if (plan == null)
            {
                return DynValue.Nil;
            }
            var result = new Table(script);
            result.Set("planId", StringValue(plan.PlanId));
            result.Set("campaignId", StringValue(plan.CampaignId));
            result.Set("title", StringValue(plan.Title));
            result.Set("notes", StringValue(plan.Notes));
            result.Set("status", StringValue(plan.Status.ToString()));
            result.Set("archived", DynValue.NewBoolean(plan.Archived));
            result.Set("createdUtc", StringValue(plan.CreatedUtc));
            result.Set("updatedUtc", StringValue(plan.UpdatedUtc));
            result.Set("activatedUtc", StringValue(plan.ActivatedUtc));
            result.Set("endedUtc", StringValue(plan.EndedUtc));

            var slots = new Table(script);
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                slots.Set(index + 1, SlotValue(script, plan.VesselSlots[index]));
            }
            result.Set("vessels", DynValue.NewTable(slots));

            var objectives = new Table(script);
            int achieved = 0;
            int resolved = 0;
            string currentObjectiveId = string.Empty;
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                objectives.Set(index + 1, ObjectiveValue(script, objective));
                if (!objective.Archived &&
                    objective.Status == MissionObjectiveStatus.Achieved)
                {
                    achieved++;
                }
                if (!objective.Archived &&
                    objective.Status != MissionObjectiveStatus.Pending &&
                    objective.Status != MissionObjectiveStatus.Current)
                {
                    resolved++;
                }
                if (!objective.Archived &&
                    objective.Status == MissionObjectiveStatus.Current)
                {
                    currentObjectiveId = objective.ObjectiveId;
                }
            }
            result.Set("objectives", DynValue.NewTable(objectives));
            result.Set("objectiveCount", DynValue.NewNumber(plan.Objectives.Count));
            result.Set("achievedCount", DynValue.NewNumber(achieved));
            result.Set("resolvedCount", DynValue.NewNumber(resolved));
            result.Set("currentObjectiveId", StringValue(currentObjectiveId));

            var deviations = new Table(script);
            for (int index = 0; index < plan.Deviations.Count; index++)
            {
                MissionPlanDeviation deviation = plan.Deviations[index];
                var item = new Table(script);
                item.Set("deviationId", StringValue(deviation.DeviationId));
                item.Set("kind", StringValue(deviation.Kind.ToString()));
                item.Set("objectiveId", StringValue(deviation.ObjectiveId));
                item.Set("factId", StringValue(deviation.FactId));
                item.Set("title", StringValue(deviation.Title));
                item.Set("detail", StringValue(deviation.Detail));
                item.Set("manual", DynValue.NewBoolean(deviation.Manual));
                deviations.Set(index + 1, DynValue.NewTable(item));
            }
            result.Set("deviations", DynValue.NewTable(deviations));
            result.Set("deviationCount", DynValue.NewNumber(plan.Deviations.Count));
            return DynValue.NewTable(result);
        }

        private static DynValue SlotValue(Script script, MissionPlanVesselSlot slot)
        {
            if (slot == null)
            {
                return DynValue.Nil;
            }
            var result = new Table(script);
            result.Set("slotId", StringValue(slot.SlotId));
            result.Set("order", DynValue.NewNumber(slot.Order));
            result.Set("name", StringValue(slot.Name));
            result.Set("role", StringValue(slot.Role));
            result.Set("required", DynValue.NewBoolean(slot.Required));
            result.Set("archived", DynValue.NewBoolean(slot.Archived));
            result.Set("savedVehicleId", StringValue(slot.SavedVehicleId));
            result.Set("savedVehicleName", StringValue(slot.SavedVehicleName));
            result.Set("savedVehiclePath", StringValue(slot.SavedVehiclePath));
            result.Set("savedVehicleLocation", StringValue(slot.SavedVehicleLocation));
            result.Set("launchRequestedUtc", StringValue(slot.LaunchRequestedUtc));
            result.Set("launchState", StringValue(slot.LaunchState));
            result.Set("launchError", StringValue(slot.LaunchError));
            result.Set("boundMissionId", StringValue(slot.BoundMissionId));
            result.Set("boundVesselId", StringValue(slot.BoundVesselId));
            result.Set("boundUtc", StringValue(slot.BoundUtc));
            result.Set("missionIds", StringListValue(script, slot.MissionIds));
            result.Set("vesselIds", StringListValue(script, slot.VesselIds));
            return DynValue.NewTable(result);
        }

        private static DynValue ObjectiveValue(Script script, MissionPlanObjective objective)
        {
            if (objective == null)
            {
                return DynValue.Nil;
            }
            var result = new Table(script);
            result.Set("objectiveId", StringValue(objective.ObjectiveId));
            result.Set("order", DynValue.NewNumber(objective.Order));
            result.Set("kind", StringValue(objective.Kind.ToString()));
            result.Set("status", StringValue(objective.Status.ToString()));
            result.Set("title", StringValue(objective.Title));
            result.Set("notes", StringValue(objective.Notes));
            result.Set("vesselSlotId", StringValue(objective.VesselSlotId));
            result.Set("relatedVesselSlotId", StringValue(
                objective.RelatedVesselSlotId));
            result.Set("targetBody", StringValue(objective.TargetBody));
            result.Set("targetSituation", StringValue(objective.TargetSituation));
            result.Set("matchValue", StringValue(objective.MatchValue));
            result.Set("optional", DynValue.NewBoolean(objective.Optional));
            result.Set("archived", DynValue.NewBoolean(objective.Archived));
            result.Set("matchedFactId", StringValue(objective.MatchedFactId));
            result.Set("matchedUtc", StringValue(objective.MatchedUtc));
            result.Set("manual", DynValue.NewBoolean(objective.HasManualResolution));
            return DynValue.NewTable(result);
        }

        private static DynValue PlanFactsValue(
            Script script,
            System.Collections.Generic.IList<MissionPlanTimelineFact> facts)
        {
            var result = new Table(script);
            for (int index = 0; index < facts.Count; index++)
            {
                MissionPlanTimelineFact fact = facts[index];
                var item = new Table(script);
                item.Set("factId", StringValue(fact.FactId));
                item.Set("kind", StringValue(fact.Kind.ToString()));
                item.Set("isPlanScoped", DynValue.NewBoolean(fact.IsPlanScoped));
                item.Set("isPlanCompletion", DynValue.NewBoolean(
                    fact.IsPlanCompletion));
                item.Set("isTerminalLoss", DynValue.NewBoolean(
                    fact.IsTerminalLoss));
                item.Set("missionId", StringValue(fact.MissionId));
                item.Set("vesselId", StringValue(fact.VesselId));
                item.Set("vesselSlotId", StringValue(fact.VesselSlotId));
                item.Set("relatedMissionIds", StringListValue(
                    script,
                    fact.RelatedMissionIds));
                item.Set("vesselIds", StringListValue(script, fact.VesselIds));
                item.Set("vesselSlotIds", StringListValue(
                    script,
                    fact.VesselSlotIds));
                item.Set("recordedUtc", StringValue(fact.RecordedUtc));
                item.Set("flightTime", DynValue.NewNumber(fact.FlightTimeSeconds));
                item.Set("body", StringValue(fact.Body));
                item.Set("situation", StringValue(fact.Situation));
                item.Set("value", StringValue(fact.Value));
                item.Set("title", StringValue(fact.Title));
                result.Set(index + 1, DynValue.NewTable(item));
            }
            return DynValue.NewTable(result);
        }

        private DynValue TimelineValue(Script script, MissionRecord mission)
        {
            var result = new Table(script);
            System.Collections.Generic.List<MissionTimelineItem> timeline =
                _tracker.GetTimeline(mission);
            for (int index = 0; index < timeline.Count; index++)
            {
                MissionTimelineItem timelineItem = timeline[index];
                MissionEvent entry = timelineItem.Event;
                var item = new Table(script);
                item.Set("eventId", StringValue(entry.EventId));
                item.Set("kind", StringValue(entry.Kind));
                item.Set("category", StringValue(timelineItem.Category));
                item.Set("categoryLabel", StringValue(timelineItem.CategoryLabel));
                item.Set("title", StringValue(entry.Title));
                item.Set("recordedUtc", StringValue(entry.RecordedUtc));
                item.Set("flightTime", DynValue.NewNumber(entry.FlightTimeSeconds));
                item.Set("body", StringValue(entry.Body));
                item.Set("situation", StringValue(entry.Situation));
                item.Set("vesselIds", StringListValue(script, entry.VesselIds));
                item.Set("operationId", StringValue(entry.OperationId));
                item.Set("sourceMissionId", StringValue(timelineItem.SourceMission.MissionId));
                item.Set("sourceTitle", StringValue(timelineItem.SourceMission.Title));
                item.Set("derived", DynValue.NewBoolean(timelineItem.IsDerived));
                item.Set("value", DynValue.NewNumber(timelineItem.Value));
                item.Set("unit", StringValue(timelineItem.Unit));
                result.Set(index + 1, DynValue.NewTable(item));
            }
            return DynValue.NewTable(result);
        }

        private DynValue MissionValue(Script script, MissionRecord mission)
        {
            if (mission == null)
            {
                return DynValue.Nil;
            }
            var result = new Table(script);
            result.Set("missionId", StringValue(mission.MissionId));
            result.Set("kind", StringValue(mission.MissionKind));
            result.Set("parentMissionId", StringValue(mission.ParentMissionId));
            result.Set("parentRelation", StringValue(mission.ParentRelation));
            result.Set("title", StringValue(mission.Title));
            result.Set("vessel", StringValue(mission.VesselName));
            result.Set("status", StringValue(mission.Status));
            result.Set("notes", StringValue(mission.Notes));
            result.Set("body", StringValue(mission.LastBody));
            result.Set("situation", StringValue(mission.LastSituation));
            result.Set("flightDuration", DynValue.NewNumber(mission.FlightDurationSeconds));
            result.Set("maximumAltitude", DynValue.NewNumber(mission.MaximumAltitudeMeters));
            result.Set("maximumSpeed", DynValue.NewNumber(mission.MaximumSpeedMetersPerSecond));
            result.Set("maximumGForce", DynValue.NewNumber(mission.MaximumGForce));
            result.Set("eventCount", DynValue.NewNumber(mission.Events.Count));
            result.Set("childCount", DynValue.NewNumber(_tracker.GetChildren(mission).Count));
            result.Set("needsReview", DynValue.NewBoolean(mission.NeedsReview));
            result.Set("trackedVesselId", mission.TrackedVesselIds.Count == 0
                ? DynValue.Nil
                : StringValue(mission.TrackedVesselIds[0]));
            result.Set("trackedTravelObjectId",
                string.IsNullOrWhiteSpace(mission.TrackedTravelObjectId)
                    ? DynValue.Nil
                    : StringValue(mission.TrackedTravelObjectId));
            result.Set("vesselIds", StringListValue(script, mission.VesselIds));
            result.Set("travelObjectIds", StringListValue(script, mission.TravelObjectIds));
            result.Set("trackedVesselIds", StringListValue(script, mission.TrackedVesselIds));
            result.Set("crew", StringListValue(script, mission.Crew));
            result.Set("visitedBodies", StringListValue(script, mission.VisitedBodies));

            var events = new Table(script);
            for (int index = 0; index < mission.Events.Count; index++)
            {
                MissionEvent entry = mission.Events[index];
                var item = new Table(script);
                item.Set("eventId", StringValue(entry.EventId));
                item.Set("kind", StringValue(entry.Kind));
                item.Set("title", StringValue(entry.Title));
                item.Set("body", StringValue(entry.Body));
                item.Set("situation", StringValue(entry.Situation));
                item.Set("recordedUtc", StringValue(entry.RecordedUtc));
                item.Set("flightTime", DynValue.NewNumber(entry.FlightTimeSeconds));
                item.Set("operationId", StringValue(entry.OperationId));
                events.Set(index + 1, DynValue.NewTable(item));
            }
            result.Set("events", DynValue.NewTable(events));
            return DynValue.NewTable(result);
        }

        private static DynValue StringListValue(Script script, System.Collections.Generic.IList<string> values)
        {
            var result = new Table(script);
            if (values != null)
            {
                for (int index = 0; index < values.Count; index++)
                {
                    result.Set(index + 1, StringValue(values[index]));
                }
            }
            return DynValue.NewTable(result);
        }

        private static DynValue Callback(
            string name,
            Func<ScriptExecutionContext, CallbackArguments, DynValue> callback)
        {
            return DynValue.NewCallback((context, arguments) =>
            {
                try
                {
                    return callback(context, arguments);
                }
                catch (ScriptRuntimeException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    throw new ScriptRuntimeException(error.Message);
                }
            }, name);
        }

        private static string RequiredString(
            CallbackArguments arguments,
            int index,
            string label)
        {
            if (arguments.Count <= index || arguments[index].Type != DataType.String ||
                string.IsNullOrWhiteSpace(arguments[index].String))
            {
                throw new ScriptRuntimeException("A non-empty " + label + " is required.");
            }
            return arguments[index].String;
        }

        private static bool OptionalBoolean(
            CallbackArguments arguments,
            int index,
            bool fallback)
        {
            if (arguments.Count <= index || arguments[index].IsNil())
            {
                return fallback;
            }
            if (arguments[index].Type != DataType.Boolean)
            {
                throw new ScriptRuntimeException("Argument " + (index + 1) + " must be a boolean.");
            }
            return arguments[index].Boolean;
        }

        private static bool RequiredBoolean(
            CallbackArguments arguments,
            int index,
            string label)
        {
            if (arguments.Count <= index || arguments[index].Type != DataType.Boolean)
            {
                throw new ScriptRuntimeException(label + " must be a boolean.");
            }
            return arguments[index].Boolean;
        }

        private MissionRecord RequiredMission(
            CallbackArguments arguments,
            int index)
        {
            string missionId = RequiredString(arguments, index, "mission ID");
            MissionRecord mission = _tracker.FindById(missionId);
            if (mission == null)
            {
                throw new ScriptRuntimeException("Mission does not exist: " + missionId);
            }
            return mission;
        }

        private static string RequiredText(
            CallbackArguments arguments,
            int index,
            string label)
        {
            if (arguments.Count <= index || arguments[index].Type != DataType.String)
            {
                throw new ScriptRuntimeException(label + " must be a string.");
            }
            return arguments[index].String;
        }

        private static double RequiredNonNegativeNumber(
            CallbackArguments arguments,
            int index,
            string label)
        {
            if (arguments.Count <= index || arguments[index].Type != DataType.Number)
            {
                throw new ScriptRuntimeException(label + " must be a number.");
            }
            double value = arguments[index].Number;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                throw new ScriptRuntimeException(
                    label + " must be a finite, non-negative number.");
            }
            return value;
        }

        private static double OptionalNonNegativeNumber(
            CallbackArguments arguments,
            int index,
            double fallback,
            string label)
        {
            return arguments.Count <= index || arguments[index].IsNil()
                ? fallback
                : RequiredNonNegativeNumber(arguments, index, label);
        }

        private static string OptionalNonEmptyString(
            CallbackArguments arguments,
            int index,
            string fallback,
            string label)
        {
            return arguments.Count <= index || arguments[index].IsNil()
                ? fallback
                : RequiredString(arguments, index, label);
        }

        private static string OptionalString(
            CallbackArguments arguments,
            int index,
            string label)
        {
            if (arguments.Count <= index || arguments[index].IsNil())
            {
                return null;
            }
            if (arguments[index].Type != DataType.String)
            {
                throw new ScriptRuntimeException(label + " must be a string when provided.");
            }
            return string.IsNullOrWhiteSpace(arguments[index].String)
                ? null
                : arguments[index].String;
        }

        private static string OptionalText(CallbackArguments arguments, int index)
        {
            if (arguments.Count <= index || arguments[index].IsNil())
            {
                return null;
            }
            if (arguments[index].Type != DataType.String)
            {
                throw new ScriptRuntimeException(
                    "Argument " + (index + 1) + " must be a string when provided.");
            }
            return arguments[index].String;
        }

        private static MissionObjectiveKind RequiredObjectiveKind(
            CallbackArguments arguments,
            int index)
        {
            string value = RequiredString(arguments, index, "objective kind");
            MissionObjectiveKind kind;
            if (!Enum.TryParse(value, true, out kind) ||
                !Enum.IsDefined(typeof(MissionObjectiveKind), kind))
            {
                throw new ScriptRuntimeException(
                    "Unknown objective kind '" + value + "'.");
            }
            return kind;
        }

        private static int RequiredInteger(
            CallbackArguments arguments,
            int index,
            string label)
        {
            if (arguments.Count <= index || arguments[index].Type != DataType.Number)
            {
                throw new ScriptRuntimeException(label + " must be an integer.");
            }
            double value = arguments[index].Number;
            int integer = (int)value;
            if (double.IsNaN(value) || double.IsInfinity(value) ||
                integer != value || integer < 0)
            {
                throw new ScriptRuntimeException(
                    label + " must be a non-negative integer.");
            }
            return integer;
        }

        private static DynValue StringValue(string value)
        {
            return DynValue.NewString(value ?? string.Empty);
        }
    }
}
