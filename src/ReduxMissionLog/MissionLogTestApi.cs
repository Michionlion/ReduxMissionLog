using System;
using System.Reflection;
using MoonSharp.Interpreter;

namespace ReduxMissionLog
{
    internal sealed class MissionLogTestApi : IDisposable
    {
        private const string ModId = "ReduxMissionLog";
        private readonly MissionTracker _tracker;
        private readonly MissionLogWindow _window;
        private readonly Action<string> _info;
        private IDisposable _registration;
        private bool _registrationFailureLogged;

        public MissionLogTestApi(
            MissionTracker tracker,
            MissionLogWindow window,
            Action<string> info)
        {
            _tracker = tracker;
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
                return DynValue.Nil;
            }));
            api.Set("reload_archive", Callback("ReduxMissionLog.reload_archive", (context, args) =>
            {
                _tracker.ReloadArchive();
                return DynValue.Nil;
            }));
            api.Set("end_test_session", Callback("ReduxMissionLog.end_test_session", (context, args) =>
            {
                _tracker.EndIsolatedTestSession();
                return DynValue.Nil;
            }));
            api.Set("archive_count", Callback("ReduxMissionLog.archive_count", (context, args) =>
                DynValue.NewNumber(_tracker.Archive.Missions.Count)));
            api.Set("archive_path", Callback("ReduxMissionLog.archive_path", (context, args) =>
                DynValue.NewString(_tracker.ArchivePath)));
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
            api.Set("window_visible", Callback("ReduxMissionLog.window_visible", (context, args) =>
                DynValue.NewBoolean(_window.Visible)));
            api.Set("ui_stack", Callback("ReduxMissionLog.ui_stack", (context, args) =>
                DynValue.NewString(_window.UiStack)));
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
                    OptionalBoolean(args, 5, false));
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
                    RequiredString(args, 5, "operation ID"));
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
            api.Set("mission", Callback("ReduxMissionLog.mission", (context, args) =>
                MissionValue(script,
                    _tracker.FindById(RequiredString(args, 0, "mission ID")))));
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
            result.Set("body", StringValue(mission.LastBody));
            result.Set("situation", StringValue(mission.LastSituation));
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

            var events = new Table(script);
            for (int index = 0; index < mission.Events.Count; index++)
            {
                MissionEvent entry = mission.Events[index];
                var item = new Table(script);
                item.Set("kind", StringValue(entry.Kind));
                item.Set("title", StringValue(entry.Title));
                item.Set("body", StringValue(entry.Body));
                item.Set("situation", StringValue(entry.Situation));
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

        private static DynValue StringValue(string value)
        {
            return DynValue.NewString(value ?? string.Empty);
        }
    }
}
