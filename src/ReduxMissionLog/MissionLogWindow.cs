using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReduxMissionLog
{
    internal sealed class MissionLogWindow
    {
        private readonly MissionTracker _tracker;
        private Rect _window = new Rect(70f, 70f, 660f, 560f);
        private Vector2 _archiveScroll;
        private Vector2 _detailScroll;
        private bool _visible;
        private string _selectedMissionId;
        private string _editTitle = string.Empty;
        private string _editNotes = string.Empty;

        public MissionLogWindow(MissionTracker tracker)
        {
            _tracker = tracker;
        }

        public bool Visible { get { return _visible; } }

        public void Toggle()
        {
            SetVisible(!_visible);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (visible && string.IsNullOrEmpty(_selectedMissionId))
            {
                Select(_tracker.GetCurrent() ?? _tracker.GetLatest());
            }
        }

        public void Draw()
        {
            if (!_visible)
            {
                return;
            }
            _window = GUILayout.Window(
                0x524D4C,
                _window,
                DrawContents,
                "Redux Mission Log");
        }

        private void DrawContents(int windowId)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Campaign history, recorded automatically");
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            DrawArchiveList();
            DrawMissionDetail();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("F7 toggles this window", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Close", GUILayout.Width(80f)))
            {
                _visible = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawArchiveList()
        {
            GUILayout.BeginVertical(GUILayout.Width(230f));
            GUILayout.Label("Missions (" + _tracker.Archive.Missions.Count + ")");
            _archiveScroll = GUILayout.BeginScrollView(_archiveScroll, GUI.skin.box);
            List<MissionRecord> missions = new List<MissionRecord>(_tracker.Archive.Missions);
            missions.Reverse();
            for (int index = 0; index < missions.Count; index++)
            {
                MissionRecord mission = missions[index];
                string label = mission.Title + "\n" + mission.Status + " - " + mission.LastBody;
                if (GUILayout.Button(label, GUILayout.Height(44f)))
                {
                    Select(mission);
                }
            }
            if (missions.Count == 0)
            {
                GUILayout.Label("No missions recorded yet.");
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawMissionDetail()
        {
            MissionRecord mission = FindSelected();
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (mission == null)
            {
                GUILayout.Label("Select a mission to see its debrief.");
                GUILayout.EndVertical();
                return;
            }

            _detailScroll = GUILayout.BeginScrollView(_detailScroll, GUI.skin.box);
            GUILayout.Label("Mission title");
            _editTitle = GUILayout.TextField(_editTitle);
            GUILayout.Label("Status: " + mission.Status);
            GUILayout.Label("Vessel: " + mission.VesselName);
            GUILayout.Label("Crew: " + JoinOrNone(mission.Crew));
            GUILayout.Label("Visited: " + JoinOrNone(mission.VisitedBodies));
            GUILayout.Label("Peak altitude: " + FormatDistance(mission.MaximumAltitudeMeters));
            GUILayout.Label("Peak speed: " + mission.MaximumSpeedMetersPerSecond.ToString("N1") + " m/s");
            GUILayout.Label("Peak g-force: " + mission.MaximumGForce.ToString("N2") + " g");
            GUILayout.Space(6f);
            GUILayout.Label("Notes");
            _editNotes = GUILayout.TextArea(_editNotes, GUILayout.MinHeight(56f));
            GUILayout.Space(6f);
            GUILayout.Label("Timeline");
            for (int index = 0; index < mission.Events.Count; index++)
            {
                MissionEvent entry = mission.Events[index];
                GUILayout.Label("T+" + FormatDuration(entry.FlightTimeSeconds) + "  " + entry.Title);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save edits"))
            {
                _tracker.SaveEdits(mission, _editTitle, _editNotes);
            }
            GUI.enabled = mission.IsActive && ReferenceEquals(mission, _tracker.GetCurrent());
            if (GUILayout.Button("Complete mission"))
            {
                _tracker.SaveEdits(mission, _editTitle, _editNotes);
                _tracker.CompleteMission(mission, "Completed");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void Select(MissionRecord mission)
        {
            _selectedMissionId = mission == null ? null : mission.MissionId;
            _editTitle = mission == null ? string.Empty : mission.Title;
            _editNotes = mission == null ? string.Empty : mission.Notes;
            _detailScroll = Vector2.zero;
        }

        private MissionRecord FindSelected()
        {
            for (int index = 0; index < _tracker.Archive.Missions.Count; index++)
            {
                MissionRecord mission = _tracker.Archive.Missions[index];
                if (string.Equals(mission.MissionId, _selectedMissionId,
                    StringComparison.Ordinal))
                {
                    return mission;
                }
            }
            MissionRecord fallback = _tracker.GetCurrent() ?? _tracker.GetLatest();
            if (fallback != null)
            {
                Select(fallback);
            }
            return fallback;
        }

        private static string JoinOrNone(List<string> values)
        {
            return values == null || values.Count == 0 ? "None" : string.Join(", ", values.ToArray());
        }

        private static string FormatDistance(double meters)
        {
            return meters >= 1000.0
                ? (meters / 1000.0).ToString("N1") + " km"
                : meters.ToString("N0") + " m";
        }

        private static string FormatDuration(double seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0.0, seconds));
            return duration.TotalHours >= 1.0
                ? ((int)duration.TotalHours).ToString("00") + ":" + duration.Minutes.ToString("00") + ":" + duration.Seconds.ToString("00")
                : duration.Minutes.ToString("00") + ":" + duration.Seconds.ToString("00");
        }
    }
}
