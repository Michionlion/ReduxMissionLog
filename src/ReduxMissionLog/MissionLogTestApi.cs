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
        }

        private static DynValue MissionValue(Script script, MissionRecord mission)
        {
            if (mission == null)
            {
                return DynValue.Nil;
            }
            var result = new Table(script);
            result.Set("missionId", StringValue(mission.MissionId));
            result.Set("title", StringValue(mission.Title));
            result.Set("vessel", StringValue(mission.VesselName));
            result.Set("status", StringValue(mission.Status));
            result.Set("body", StringValue(mission.LastBody));
            result.Set("situation", StringValue(mission.LastSituation));
            result.Set("maximumAltitude", DynValue.NewNumber(mission.MaximumAltitudeMeters));
            result.Set("maximumSpeed", DynValue.NewNumber(mission.MaximumSpeedMetersPerSecond));
            result.Set("maximumGForce", DynValue.NewNumber(mission.MaximumGForce));
            result.Set("eventCount", DynValue.NewNumber(mission.Events.Count));

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
                events.Set(index + 1, DynValue.NewTable(item));
            }
            result.Set("events", DynValue.NewTable(events));
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

        private static DynValue StringValue(string value)
        {
            return DynValue.NewString(value ?? string.Empty);
        }
    }
}
