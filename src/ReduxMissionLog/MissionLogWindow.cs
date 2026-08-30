using System;
using System.Collections.Generic;
using UitkForKsp2.API;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using UitkWindow = UitkForKsp2.API.Window;

namespace ReduxMissionLog
{
    internal sealed class MissionLogWindow : IDisposable
    {
        private const float DefaultWidth = 920f;
        private const float DefaultHeight = 680f;
        private const float MinimumWidth = 720f;
        private const float MinimumHeight = 520f;

        private readonly MissionTracker _tracker;
        private readonly Action<string> _logError;
        private readonly HashSet<string> _collapsedMissionIds =
            new HashSet<string>(StringComparer.Ordinal);

        private UIDocument _document;
        private AppShell _shell;
        private VisualElement _archiveTree;
        private VisualElement _emptySelection;
        private VisualElement _selectedDetails;
        private ScrollView _detailScroll;
        private TextField _titleField;
        private TextField _notesField;
        private Label _archiveHeading;
        private Label _typeAndStatus;
        private Label _parentMission;
        private Label _vessel;
        private Label _crew;
        private Label _visited;
        private Label _peakAltitude;
        private Label _peakSpeed;
        private Label _peakGForce;
        private Label _reviewWarning;
        private VisualElement _timeline;
        private Label _subMissionHeading;
        private VisualElement _subMissions;
        private Label _feedbackLabel;
        private VisualElement _confirmation;
        private Label _pendingPromptLabel;
        private VisualElement _manualControls;
        private Button _completeButton;
        private Button _combineButton;
        private Button _adoptButton;
        private Button _unlinkButton;
        private Button _trackButton;

        private bool _visible;
        private string _selectedMissionId;
        private string _pendingAction;
        private string _pendingSelectedMissionId;
        private string _pendingCurrentMissionId;
        private string _pendingVesselId;
        private string _pendingPrompt;
        private string _feedback;

        public MissionLogWindow(MissionTracker tracker, Action<string> logError)
        {
            _tracker = tracker;
            _logError = logError;
        }

        public bool Visible { get { return _visible; } }

        public string UiStack
        {
            get { return _shell == null ? "uninitialized" : _shell.GetType().FullName; }
        }

        public void Toggle()
        {
            SetVisible(!_visible);
        }

        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                _visible = false;
                if (_document != null)
                {
                    _document.Hide();
                }
                return;
            }

            try
            {
                EnsureCreated();
                if (string.IsNullOrEmpty(_selectedMissionId))
                {
                    Select(_tracker.GetCurrent() ?? _tracker.GetLatest());
                }
                Refresh();
                _document.Show();
                _shell.BringToFront();
                _visible = true;
            }
            catch (Exception error)
            {
                _visible = false;
                if (_logError != null)
                {
                    _logError("Could not create the Mission Log UI: " + error);
                }
            }
        }

        public void RefreshIfVisible()
        {
            if (_visible && _document != null)
            {
                Refresh();
            }
        }

        public void Dispose()
        {
            _visible = false;
            if (_shell != null)
            {
                _shell.CloseClicked -= Close;
            }
            if (_document != null)
            {
                UnityEngine.Object.Destroy(_document.gameObject);
            }
            _document = null;
            _shell = null;
        }

        private void EnsureCreated()
        {
            if (_document != null)
            {
                return;
            }

            _shell = new AppShell
            {
                name = "redux-mission-log-window",
                Title = "Redux Mission Log",
                UppercaseTitle = true,
                TitleSpacing = AppShellTitleSpacing.Compact,
                ShowCloseButton = true
            };
            _shell.style.width = DefaultWidth;
            _shell.style.height = DefaultHeight;
            _shell.style.minWidth = MinimumWidth;
            _shell.style.minHeight = MinimumHeight;
            _shell.style.flexShrink = 0f;
            _shell.style.translate = new Translate(70f, 70f, 0f);
            _shell.CloseClicked += Close;

            BuildContents();
            Select(_tracker.GetCurrent() ?? _tracker.GetLatest());
            Refresh();

            WindowOptions options = WindowOptions.Default;
            options.WindowId = "ReduxMissionLog.Window";
            options.IsHidingEnabled = true;
            options.UseStockScale = true;
            options.DisableGameInputForTextFields = true;
            options.BringToFrontOnPointerDown = true;
            options.BlockGameInput = true;

            MoveOptions move = MoveOptions.Default;
            move.IsMovingEnabled = true;
            move.CheckScreenBounds = true;
            move.HandleElementName = "header";
            options.MoveOptions = move;

            ResizeOptions resize = ResizeOptions.Default;
            resize.IsResizingEnabled = true;
            resize.CheckScreenBounds = true;
            resize.MinWidth = MinimumWidth;
            resize.MinHeight = MinimumHeight;
            options.ResizeOptions = resize;

            _document = UitkWindow.Create(options, _shell);
            _shell.EnableUiSounds();
            _document.Hide();
        }

        private void BuildContents()
        {
            VisualElement body = new VisualElement { name = "mission-log-body" };
            body.AddToClassList("oab-window-body");
            body.style.flexGrow = 1f;
            body.style.minHeight = 0f;
            body.style.paddingLeft = 0f;
            body.style.paddingRight = 0f;
            body.style.paddingTop = 0f;
            body.style.paddingBottom = 0f;

            Label subtitle = CreateWrappedLabel(
                "Campaign history, recorded automatically. Docked flights and sorties stay connected as mission trees.");
            subtitle.style.marginBottom = 8f;
            subtitle.style.fontSize = 13f;
            body.Add(subtitle);

            VisualElement columns = new VisualElement { name = "mission-log-columns" };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow = 1f;
            columns.style.minHeight = 0f;
            body.Add(columns);

            InvertedCornerBox archivePanel = CreatePanel("mission-log-archive");
            archivePanel.style.width = 300f;
            archivePanel.style.minWidth = 240f;
            archivePanel.style.flexShrink = 0f;
            archivePanel.style.marginRight = 8f;
            columns.Add(archivePanel);

            _archiveHeading = CreateSectionHeading("Mission trees");
            archivePanel.Add(_archiveHeading);

            ScrollView archiveScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "mission-log-archive-scroll"
            };
            archiveScroll.style.flexGrow = 1f;
            archiveScroll.style.minHeight = 0f;
            archiveScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            archivePanel.Add(archiveScroll);
            _archiveTree = archiveScroll.contentContainer;

            InvertedCornerBox detailPanel = CreatePanel("mission-log-detail");
            detailPanel.style.flexGrow = 1f;
            detailPanel.style.minWidth = 0f;
            detailPanel.style.minHeight = 0f;
            columns.Add(detailPanel);

            _emptySelection = CreateWrappedLabel("Select a mission to see its debrief.");
            _emptySelection.style.flexGrow = 1f;
            _emptySelection.style.unityTextAlign = TextAnchor.MiddleCenter;
            detailPanel.Add(_emptySelection);

            _selectedDetails = new VisualElement { name = "mission-log-selected-details" };
            _selectedDetails.style.flexGrow = 1f;
            _selectedDetails.style.minHeight = 0f;
            detailPanel.Add(_selectedDetails);

            _detailScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "mission-log-detail-scroll"
            };
            _detailScroll.style.flexGrow = 1f;
            _detailScroll.style.minHeight = 0f;
            _detailScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _selectedDetails.Add(_detailScroll);

            _detailScroll.Add(CreateSectionHeading("Mission title"));
            _titleField = new TextField { name = "mission-title" };
            _titleField.AddToClassList("oab-text-field");
            _titleField.style.marginBottom = 6f;
            _detailScroll.Add(_titleField);

            _typeAndStatus = CreateWrappedLabel(string.Empty);
            _parentMission = CreateWrappedLabel(string.Empty);
            _vessel = CreateWrappedLabel(string.Empty);
            _crew = CreateWrappedLabel(string.Empty);
            _visited = CreateWrappedLabel(string.Empty);
            _peakAltitude = CreateWrappedLabel(string.Empty);
            _peakSpeed = CreateWrappedLabel(string.Empty);
            _peakGForce = CreateWrappedLabel(string.Empty);
            _detailScroll.Add(_typeAndStatus);
            _detailScroll.Add(_parentMission);
            _detailScroll.Add(_vessel);
            _detailScroll.Add(_crew);
            _detailScroll.Add(_visited);
            _detailScroll.Add(_peakAltitude);
            _detailScroll.Add(_peakSpeed);
            _detailScroll.Add(_peakGForce);

            _reviewWarning = CreateWrappedLabel(
                "Needs review: an ambiguous or repaired relationship was detected.");
            _reviewWarning.style.marginTop = 4f;
            _reviewWarning.style.unityFontStyleAndWeight = FontStyle.Bold;
            _detailScroll.Add(_reviewWarning);

            Label notesHeading = CreateSectionHeading("Notes");
            notesHeading.style.marginTop = 8f;
            _detailScroll.Add(notesHeading);
            _notesField = new TextField { name = "mission-notes", multiline = true };
            _notesField.style.height = 84f;
            _notesField.style.marginBottom = 8f;
            _detailScroll.Add(_notesField);

            _detailScroll.Add(CreateSectionHeading("Mission timeline"));
            _timeline = new VisualElement { name = "mission-timeline" };
            _detailScroll.Add(_timeline);

            _subMissionHeading = CreateSectionHeading("Sub-missions");
            _subMissionHeading.style.marginTop = 8f;
            _detailScroll.Add(_subMissionHeading);
            _subMissions = new VisualElement { name = "mission-submissions" };
            _detailScroll.Add(_subMissions);

            VisualElement primaryActions = CreateActionRow();
            Button saveButton = CreateButton("Save edits", "Save this mission's title and notes", SaveEdits);
            _completeButton = CreateButton(
                "Complete mission",
                "Close the currently active mission as completed",
                CompleteMission);
            primaryActions.Add(saveButton);
            primaryActions.Add(_completeButton);
            _selectedDetails.Add(primaryActions);

            Label organizeHeading = CreateSectionHeading("Organize mission tree");
            organizeHeading.style.marginTop = 8f;
            _selectedDetails.Add(organizeHeading);

            _feedbackLabel = CreateWrappedLabel(string.Empty);
            _feedbackLabel.style.marginBottom = 4f;
            _selectedDetails.Add(_feedbackLabel);

            _confirmation = new VisualElement { name = "mission-log-confirmation" };
            _pendingPromptLabel = CreateWrappedLabel(string.Empty);
            _pendingPromptLabel.style.marginBottom = 4f;
            _confirmation.Add(_pendingPromptLabel);
            VisualElement confirmActions = CreateActionRow();
            confirmActions.Add(CreateButton("Confirm", "Apply this mission-tree change", ConfirmPending));
            confirmActions.Add(CreateButton("Cancel", "Leave the mission tree unchanged", CancelPending));
            _confirmation.Add(confirmActions);
            _selectedDetails.Add(_confirmation);

            _manualControls = new VisualElement { name = "mission-log-manual-controls" };
            VisualElement firstManualRow = CreateActionRow();
            _combineButton = CreateButton(
                "Combine current + selected",
                "Create one overarching mission containing both flight histories",
                BeginCombine);
            _adoptButton = CreateButton(
                "Make current a sub-mission",
                "Place the current mission beneath the selected mission",
                BeginAdopt);
            firstManualRow.Add(_combineButton);
            firstManualRow.Add(_adoptButton);
            _manualControls.Add(firstManualRow);

            VisualElement secondManualRow = CreateActionRow();
            _unlinkButton = CreateButton(
                "Unlink selected",
                "Move the selected mission to the top level",
                BeginUnlink);
            _trackButton = CreateButton(
                "Track current vessel as selected",
                "Repair the current vessel's mission binding",
                BeginTrack);
            secondManualRow.style.marginTop = 4f;
            secondManualRow.Add(_unlinkButton);
            secondManualRow.Add(_trackButton);
            _manualControls.Add(secondManualRow);
            _selectedDetails.Add(_manualControls);

            Label footer = CreateWrappedLabel("F7 toggles this window");
            footer.style.marginTop = 6f;
            footer.style.fontSize = 11f;
            footer.style.paddingRight = 18f;
            footer.style.unityTextAlign = TextAnchor.MiddleRight;
            body.Add(footer);

            _shell.Add(body);
        }

        private void Refresh()
        {
            MissionRecord selected = FindSelected();
            PopulateArchive();
            PopulateDetails(selected);
        }

        private void PopulateArchive()
        {
            _archiveTree.Clear();
            _archiveHeading.text = "Mission trees (" + _tracker.Archive.Missions.Count + " records)";

            List<MissionRecord> roots = _tracker.GetRoots();
            roots.Reverse();
            if (roots.Count == 0)
            {
                _archiveTree.Add(CreateWrappedLabel("No missions recorded yet."));
                return;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < roots.Count; index++)
            {
                AddTreeNode(roots[index], 0, visited);
            }
        }

        private void AddTreeNode(MissionRecord mission, int depth, HashSet<string> visited)
        {
            if (mission == null || !visited.Add(mission.MissionId))
            {
                return;
            }

            List<MissionRecord> children = _tracker.GetChildren(mission);
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginLeft = depth * 14f;
            row.style.marginBottom = 3f;

            if (children.Count > 0)
            {
                bool collapsed = _collapsedMissionIds.Contains(mission.MissionId);
                Button expander = CreateButton(
                    collapsed ? "▸" : "▾",
                    collapsed ? "Expand this mission" : "Collapse this mission",
                    delegate
                    {
                        if (_collapsedMissionIds.Contains(mission.MissionId))
                        {
                            _collapsedMissionIds.Remove(mission.MissionId);
                        }
                        else
                        {
                            _collapsedMissionIds.Add(mission.MissionId);
                        }
                        Refresh();
                    });
                expander.style.width = 28f;
                expander.style.minWidth = 28f;
                expander.style.marginRight = 4f;
                row.Add(expander);
            }
            else
            {
                VisualElement spacer = new VisualElement();
                spacer.style.width = 32f;
                spacer.style.flexShrink = 0f;
                row.Add(spacer);
            }

            string marker = mission.IsActive ? "● " : "○ ";
            Button missionButton = CreateButton(
                marker + mission.Title + "\n" + mission.Status + " · " + mission.MissionKind,
                "Open the debrief for " + mission.Title,
                delegate
                {
                    Select(mission);
                    Refresh();
                });
            missionButton.style.flexGrow = 1f;
            missionButton.style.height = 48f;
            missionButton.style.whiteSpace = WhiteSpace.Normal;
            missionButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (string.Equals(mission.MissionId, _selectedMissionId, StringComparison.Ordinal))
            {
                missionButton.AddToClassList("selected");
                missionButton.style.borderLeftWidth = 3f;
                missionButton.style.borderLeftColor =
                    new StyleColor(new Color32(188, 161, 255, 255));
            }
            row.Add(missionButton);
            _archiveTree.Add(row);

            if (_collapsedMissionIds.Contains(mission.MissionId))
            {
                return;
            }
            for (int index = 0; index < children.Count; index++)
            {
                AddTreeNode(children[index], depth + 1, visited);
            }
        }

        private void PopulateDetails(MissionRecord mission)
        {
            bool hasMission = mission != null;
            _emptySelection.style.display = hasMission ? DisplayStyle.None : DisplayStyle.Flex;
            _selectedDetails.style.display = hasMission ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasMission)
            {
                return;
            }

            MissionRecord parent = _tracker.GetParent(mission);
            MissionAggregate aggregate = _tracker.GetAggregate(mission);
            _typeAndStatus.text = "Type: " + mission.MissionKind + "    Status: " + mission.Status;
            _parentMission.text = parent == null
                ? string.Empty
                : "Part of: " + parent.Title + " (" + mission.ParentRelation + ")";
            _parentMission.style.display = parent == null ? DisplayStyle.None : DisplayStyle.Flex;
            _vessel.text = "Vessel: " + mission.VesselName;
            _crew.text = "Crew in tree: " + JoinOrNone(aggregate.Crew);
            _visited.text = "Visited in tree: " + JoinOrNone(aggregate.VisitedBodies);
            _peakAltitude.text = "Tree peak altitude: " + FormatDistance(aggregate.MaximumAltitudeMeters);
            _peakSpeed.text = "Tree peak speed: " +
                aggregate.MaximumSpeedMetersPerSecond.ToString("N1") + " m/s";
            _peakGForce.text = "Tree peak g-force: " + aggregate.MaximumGForce.ToString("N2") + " g";
            _reviewWarning.style.display = mission.NeedsReview ? DisplayStyle.Flex : DisplayStyle.None;

            _timeline.Clear();
            if (mission.Events.Count == 0)
            {
                _timeline.Add(CreateWrappedLabel("No events recorded yet."));
            }
            else
            {
                for (int index = 0; index < mission.Events.Count; index++)
                {
                    MissionEvent entry = mission.Events[index];
                    _timeline.Add(CreateWrappedLabel(
                        "T+" + FormatDuration(entry.FlightTimeSeconds) + "  " + entry.Title));
                }
            }

            _subMissions.Clear();
            List<MissionRecord> children = _tracker.GetChildren(mission);
            _subMissionHeading.style.display = children.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _subMissions.style.display = children.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int index = 0; index < children.Count; index++)
            {
                _subMissions.Add(CreateWrappedLabel(
                    "• " + children[index].Title + " — " + children[index].Status));
            }

            MissionRecord current = _tracker.GetCurrent();
            _completeButton.SetEnabled(mission.IsActive && ReferenceEquals(mission, current));

            bool hasFeedback = !string.IsNullOrWhiteSpace(_feedback);
            _feedbackLabel.text = hasFeedback ? _feedback : string.Empty;
            _feedbackLabel.style.display = hasFeedback ? DisplayStyle.Flex : DisplayStyle.None;

            bool awaitingConfirmation = !string.IsNullOrWhiteSpace(_pendingAction);
            _confirmation.style.display = awaitingConfirmation ? DisplayStyle.Flex : DisplayStyle.None;
            _manualControls.style.display = awaitingConfirmation ? DisplayStyle.None : DisplayStyle.Flex;
            _pendingPromptLabel.text = awaitingConfirmation ? _pendingPrompt : string.Empty;

            _combineButton.SetEnabled(
                current != null && !ReferenceEquals(current, mission) && mission.IsActive &&
                mission.TrackedVesselIds.Count == 1 &&
                string.Equals(current.CampaignId, mission.CampaignId, StringComparison.Ordinal));
            _adoptButton.SetEnabled(
                current != null && !ReferenceEquals(current, mission) &&
                string.Equals(current.CampaignId, mission.CampaignId, StringComparison.Ordinal));
            _unlinkButton.SetEnabled(parent != null);
            _trackButton.SetEnabled(
                _tracker.CanTrackCurrentAs(mission) &&
                (current == null || !ReferenceEquals(current, mission)));
        }

        private void SaveEdits()
        {
            MissionRecord mission = FindSelected();
            if (mission == null)
            {
                return;
            }
            _tracker.SaveEdits(mission, _titleField.value, _notesField.value);
            _feedback = "Edits saved.";
            Refresh();
        }

        private void CompleteMission()
        {
            MissionRecord mission = FindSelected();
            if (mission == null)
            {
                return;
            }
            _tracker.SaveEdits(mission, _titleField.value, _notesField.value);
            _tracker.CompleteMission(mission, "Completed");
            _feedback = "Mission completed.";
            Refresh();
        }

        private void BeginCombine()
        {
            MissionRecord selected = FindSelected();
            MissionRecord current = _tracker.GetCurrent();
            if (!CanBeginWithCurrent(selected, current))
            {
                return;
            }
            BeginPending(
                "combine",
                selected,
                current,
                "Treat " + current.Title + " and " + selected.Title +
                    " as physically combined? Their histories remain as children.");
        }

        private void BeginAdopt()
        {
            MissionRecord selected = FindSelected();
            MissionRecord current = _tracker.GetCurrent();
            if (!CanBeginWithCurrent(selected, current))
            {
                return;
            }
            BeginPending(
                "adopt",
                selected,
                current,
                "Make " + current.Title + " a sub-mission of " + selected.Title + "?");
        }

        private void BeginUnlink()
        {
            MissionRecord selected = FindSelected();
            if (selected == null)
            {
                return;
            }
            BeginPending(
                "unlink",
                selected,
                _tracker.GetCurrent(),
                "Move " + selected.Title + " to the top level?");
        }

        private void BeginTrack()
        {
            MissionRecord selected = FindSelected();
            if (selected == null)
            {
                return;
            }
            BeginPending(
                "track",
                selected,
                _tracker.GetCurrent(),
                "Assign the current vessel to " + selected.Title +
                    "? Its previous mission binding will be marked for review.");
        }

        private bool CanBeginWithCurrent(MissionRecord selected, MissionRecord current)
        {
            if (selected != null && current != null)
            {
                return true;
            }
            _feedback = "The active vessel no longer has a current mission.";
            Refresh();
            return false;
        }

        private void BeginPending(
            string action,
            MissionRecord selected,
            MissionRecord current,
            string prompt)
        {
            if (selected == null)
            {
                return;
            }
            _pendingAction = action;
            _pendingSelectedMissionId = selected.MissionId;
            _pendingCurrentMissionId = current == null ? null : current.MissionId;
            _pendingVesselId = _tracker.ActiveVesselId;
            _pendingPrompt = prompt;
            _feedback = null;
            Refresh();
        }

        private void ConfirmPending()
        {
            ExecutePending(FindSelected());
            Refresh();
        }

        private void CancelPending()
        {
            ClearPending();
            Refresh();
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
            if (_titleField != null)
            {
                _titleField.SetValueWithoutNotify(mission == null ? string.Empty : mission.Title);
            }
            if (_notesField != null)
            {
                _notesField.SetValueWithoutNotify(mission == null ? string.Empty : mission.Notes);
            }
            if (_detailScroll != null)
            {
                _detailScroll.scrollOffset = Vector2.zero;
            }
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

        private void Close()
        {
            SetVisible(false);
        }

        private static InvertedCornerBox CreatePanel(string name)
        {
            InvertedCornerBox panel = new InvertedCornerBox { name = name };
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 8f;
            panel.style.paddingBottom = 8f;
            return panel;
        }

        private static Label CreateSectionHeading(string text)
        {
            Label heading = CreateWrappedLabel(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 14f;
            heading.style.marginBottom = 4f;
            return heading;
        }

        private static Label CreateWrappedLabel(string text)
        {
            Label label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0f;
            return label;
        }

        private static VisualElement CreateActionRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("oab-window-actions");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0f;
            return row;
        }

        private static Button CreateButton(string text, string tooltip, Action clicked)
        {
            Button button = new Button(clicked)
            {
                text = text,
                tooltip = tooltip
            };
            button.AddToClassList("ui-sound-button");
            button.style.flexGrow = 1f;
            button.style.marginRight = 4f;
            return button;
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
                ? ((int)duration.TotalHours).ToString("00") + ":" +
                    duration.Minutes.ToString("00") + ":" + duration.Seconds.ToString("00")
                : duration.Minutes.ToString("00") + ":" + duration.Seconds.ToString("00");
        }
    }
}
