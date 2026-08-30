using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReduxMissionLog
{
    internal sealed class MissionLogWindow
    {
        private readonly MissionTracker _tracker;
        private Rect _window = new Rect(70f, 70f, 780f, 640f);
        private Vector2 _archiveScroll;
        private Vector2 _detailScroll;
        private bool _visible;
        private string _selectedMissionId;
        private string _editTitle = string.Empty;
        private string _editNotes = string.Empty;
        private string _pendingAction;
        private string _pendingSelectedMissionId;
        private string _pendingCurrentMissionId;
        private string _pendingVesselId;
        private string _pendingPrompt;
        private string _feedback;
        private readonly HashSet<string> _collapsedMissionIds =
            new HashSet<string>(StringComparer.Ordinal);

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
            GUILayout.BeginVertical(GUILayout.Width(280f));
            GUILayout.Label("Mission trees (" + _tracker.Archive.Missions.Count + " records)");
            _archiveScroll = GUILayout.BeginScrollView(_archiveScroll, GUI.skin.box);
            List<MissionRecord> roots = _tracker.GetRoots();
            roots.Reverse();
            for (int index = 0; index < roots.Count; index++)
            {
                DrawTreeNode(roots[index], 0, new HashSet<string>(StringComparer.Ordinal));
            }
            if (roots.Count == 0)
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
            MissionRecord parent = _tracker.GetParent(mission);
            MissionAggregate aggregate = _tracker.GetAggregate(mission);
            GUILayout.Label("Type: " + mission.MissionKind + "    Status: " + mission.Status);
            if (parent != null)
            {
                GUILayout.Label("Part of: " + parent.Title + " (" + mission.ParentRelation + ")");
            }
            GUILayout.Label("Vessel: " + mission.VesselName);
            GUILayout.Label("Crew in tree: " + JoinOrNone(aggregate.Crew));
            GUILayout.Label("Visited in tree: " + JoinOrNone(aggregate.VisitedBodies));
            GUILayout.Label("Tree peak altitude: " + FormatDistance(aggregate.MaximumAltitudeMeters));
            GUILayout.Label("Tree peak speed: " + aggregate.MaximumSpeedMetersPerSecond.ToString("N1") + " m/s");
            GUILayout.Label("Tree peak g-force: " + aggregate.MaximumGForce.ToString("N2") + " g");
            if (mission.NeedsReview)
            {
                GUILayout.Label("Needs review: an ambiguous or repaired relationship was detected.");
            }
            GUILayout.Space(6f);
            GUILayout.Label("Notes");
            _editNotes = GUILayout.TextArea(_editNotes, GUILayout.MinHeight(56f));
            GUILayout.Space(6f);
            GUILayout.Label("Mission timeline");
            for (int index = 0; index < mission.Events.Count; index++)
            {
                MissionEvent entry = mission.Events[index];
                GUILayout.Label("T+" + FormatDuration(entry.FlightTimeSeconds) + "  " + entry.Title);
            }
            List<MissionRecord> children = _tracker.GetChildren(mission);
            if (children.Count > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Sub-missions");
                for (int index = 0; index < children.Count; index++)
                {
                    GUILayout.Label("• " + children[index].Title + " — " + children[index].Status);
                }
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
            DrawOrganizeControls(mission);
            GUILayout.EndVertical();
        }

        private void DrawTreeNode(
            MissionRecord mission,
            int depth,
            HashSet<string> visited)
        {
            if (mission == null || !visited.Add(mission.MissionId))
            {
                return;
            }
            List<MissionRecord> children = _tracker.GetChildren(mission);
            GUILayout.BeginHorizontal();
            GUILayout.Space(depth * 16f);
            if (children.Count > 0)
            {
                bool collapsed = _collapsedMissionIds.Contains(mission.MissionId);
                if (GUILayout.Button(collapsed ? "▶" : "▼", GUILayout.Width(24f), GUILayout.Height(44f)))
                {
                    if (collapsed)
                    {
                        _collapsedMissionIds.Remove(mission.MissionId);
                    }
                    else
                    {
                        _collapsedMissionIds.Add(mission.MissionId);
                    }
                }
            }
            else
            {
                GUILayout.Space(28f);
            }
            string marker = mission.IsActive ? "● " : "○ ";
            string label = marker + mission.Title + "\n" + mission.Status + " · " + mission.MissionKind;
            if (GUILayout.Button(label, GUILayout.Height(44f)))
            {
                Select(mission);
            }
            GUILayout.EndHorizontal();
            if (_collapsedMissionIds.Contains(mission.MissionId))
            {
                return;
            }
            for (int index = 0; index < children.Count; index++)
            {
                DrawTreeNode(children[index], depth + 1, visited);
            }
        }

        private void DrawOrganizeControls(MissionRecord selected)
        {
            MissionRecord current = _tracker.GetCurrent();
            GUILayout.Space(4f);
            GUILayout.Label("Organize mission tree");
            if (!string.IsNullOrWhiteSpace(_feedback))
            {
                GUILayout.Label(_feedback);
            }
            if (!string.IsNullOrWhiteSpace(_pendingAction))
            {
                GUILayout.Label(_pendingPrompt);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm"))
                {
                    ExecutePending(selected);
                }
                if (GUILayout.Button("Cancel"))
                {
                    ClearPending();
                }
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = current != null && selected != null &&
                !ReferenceEquals(current, selected) && selected.IsActive &&
                selected.TrackedVesselIds.Count == 1 &&
                string.Equals(current.CampaignId, selected.CampaignId, StringComparison.Ordinal);
            if (GUILayout.Button("Combine current + selected"))
            {
                BeginPending(
                    "combine",
                    selected,
                    current,
                    "Treat " + current.Title + " and " + selected.Title +
                        " as physically combined? Their histories remain as children.");
            }
            GUI.enabled = current != null && selected != null &&
                !ReferenceEquals(current, selected) &&
                string.Equals(current.CampaignId, selected.CampaignId, StringComparison.Ordinal);
            if (GUILayout.Button("Make current a sub-mission"))
            {
                BeginPending(
                    "adopt",
                    selected,
                    current,
                    "Make " + current.Title + " a sub-mission of " + selected.Title + "?");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = _tracker.GetParent(selected) != null;
            if (GUILayout.Button("Unlink selected"))
            {
                BeginPending(
                    "unlink",
                    selected,
                    current,
                    "Move " + selected.Title + " to the top level?");
            }
            GUI.enabled = _tracker.CanTrackCurrentAs(selected) &&
                (current == null || !ReferenceEquals(current, selected));
            if (GUILayout.Button("Track current vessel as selected"))
            {
                BeginPending(
                    "track",
                    selected,
                    current,
                    "Assign the current vessel to " + selected.Title +
                        "? Its previous mission binding will be marked for review.");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void BeginPending(
            string action,
            MissionRecord selected,
            MissionRecord current,
            string prompt)
        {
            _pendingAction = action;
            _pendingSelectedMissionId = selected == null ? null : selected.MissionId;
            _pendingCurrentMissionId = current == null ? null : current.MissionId;
            _pendingVesselId = _tracker.ActiveVesselId;
            _pendingPrompt = prompt;
        }

        private void ExecutePending(MissionRecord selected)
        {
            try
            {
                MissionRecord current = _tracker.GetCurrent();
                if (selected == null || !string.Equals(
                    selected.MissionId, _pendingSelectedMissionId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The selected mission changed while confirmation was open.");
                }
                if (_pendingAction != "unlink" &&
                    (!string.Equals(_tracker.ActiveVesselId, _pendingVesselId,
                        StringComparison.Ordinal) ||
                     !string.Equals(current == null ? null : current.MissionId,
                        _pendingCurrentMissionId, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "The active vessel changed while confirmation was open.");
                }
                if (_pendingAction == "combine")
                {
                    _tracker.ManualCombineCurrentWith(selected);
                }
                else if (_pendingAction == "adopt")
                {
                    _tracker.ManualAdoptCurrentUnder(selected);
                }
                else if (_pendingAction == "unlink")
                {
                    _tracker.ManualUnlink(selected);
                }
                else if (_pendingAction == "track")
                {
                    _tracker.ManualTrackCurrentAs(selected);
                }
                _feedback = "Mission tree updated.";
            }
            catch (Exception error)
            {
                _feedback = "Could not update the tree: " + error.Message;
            }
            ClearPending();
        }

        private void ClearPending()
        {
            _pendingAction = null;
            _pendingSelectedMissionId = null;
            _pendingCurrentMissionId = null;
            _pendingVesselId = null;
            _pendingPrompt = null;
        }

        private void Select(MissionRecord mission)
        {
            _selectedMissionId = mission == null ? null : mission.MissionId;
            _editTitle = mission == null ? string.Empty : mission.Title;
            _editNotes = mission == null ? string.Empty : mission.Notes;
            _detailScroll = Vector2.zero;
            ClearPending();
            _feedback = null;
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
