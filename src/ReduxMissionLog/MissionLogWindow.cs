using System;
using System.Collections.Generic;
using System.Text;
using UitkForKsp2.API;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using UitkWindow = UitkForKsp2.API.Window;

namespace ReduxMissionLog
{
    internal sealed class MissionLogWindow : IDisposable
    {
        private const float DefaultWidth = 820f;
        private const float DefaultHeight = 700f;
        private const float MinimumWidth = 620f;
        private const float MinimumHeight = 500f;

        private readonly MissionTracker _tracker;
        private readonly Action<string> _logError;
        private readonly HashSet<string> _collapsedMissionIds =
            new HashSet<string>(StringComparer.Ordinal);

        private UIDocument _document;
        private AppShell _shell;
        private VisualElement _archiveView;
        private VisualElement _storyView;
        private ScrollView _archiveScroll;
        private ScrollView _timelineScroll;
        private VisualElement _archiveTree;
        private Label _archiveCount;
        private Button _backToStoryButton;
        private Label _missionTitle;
        private Label _statusChip;
        private Label _kindChip;
        private Label _missionMeta;
        private VisualElement _relationshipRow;
        private InvertedCornerBox _reviewBanner;
        private InvertedCornerBox _noteCard;
        private Label _noteText;
        private InvertedCornerBox _editorSheet;
        private TextField _titleField;
        private TextField _notesField;
        private Button _completeButton;
        private InvertedCornerBox _manageSheet;
        private Label _manageFeedback;
        private VisualElement _manageBody;
        private Label _timelineCount;
        private VisualElement _timeline;

        private bool _visible;
        private bool _showArchive;
        private bool _showEditor;
        private bool _showManage;
        private string _selectedMissionId;
        private string _timelineFingerprint;
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
        public string SelectedMissionId { get { return _selectedMissionId; } }
        public int RenderedTimelineCount { get; private set; }
        public string ReviewView { get { return _showArchive ? "archive" : "story"; } }
        public string ReviewSheet
        {
            get
            {
                if (_showEditor)
                {
                    return "editor";
                }
                return _showManage ? "organizer" : "none";
            }
        }
        public int ArchiveRenderedNodeCount
        {
            get { return _archiveTree == null ? 0 : _archiveTree.childCount; }
        }
        public int CollapsedMissionCount { get { return _collapsedMissionIds.Count; } }
        public float ReviewScrollValue { get { return CurrentReviewScrollValue(); } }
        public float ReviewScrollMaximum { get { return CurrentReviewScrollMaximum(); } }
        public float ReviewScrollNormalized
        {
            get
            {
                ScrollView scroll = CurrentReviewScroll();
                if (scroll == null)
                {
                    return 0f;
                }
                float low = scroll.verticalScroller.lowValue;
                float high = scroll.verticalScroller.highValue;
                return high <= low ? 0f : Mathf.Clamp01(
                    (scroll.verticalScroller.value - low) / (high - low));
            }
        }
        public string ReviewScrollAnchor
        {
            get
            {
                float normalized = ReviewScrollNormalized;
                if (normalized <= 0.05f)
                {
                    return "top";
                }
                return normalized >= 0.95f ? "bottom" : "middle";
            }
        }
        public float ReviewWindowWidth
        {
            get { return ResolvedDimension(true, DefaultWidth); }
        }
        public float ReviewWindowHeight
        {
            get { return ResolvedDimension(false, DefaultHeight); }
        }

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
                _showArchive = FindSelected() == null;
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

        public void OpenMission(MissionRecord mission)
        {
            Select(mission);
            _showArchive = false;
            _showEditor = false;
            _showManage = false;
            if (_visible)
            {
                Refresh();
            }
            else
            {
                SetVisible(true);
            }
        }

        public void OpenArchive()
        {
            if (!_visible)
            {
                SetVisible(true);
            }
            if (!_visible)
            {
                return;
            }
            _showArchive = true;
            _showEditor = false;
            _showManage = false;
            Refresh();
        }

        public void OpenEditorForReview(MissionRecord mission)
        {
            if (mission == null)
            {
                throw new InvalidOperationException("A mission is required to open the editor.");
            }
            OpenMission(mission);
            ShowEditor();
        }

        public void OpenOrganizerForReview(MissionRecord mission)
        {
            if (mission == null)
            {
                throw new InvalidOperationException("A mission is required to open the organizer.");
            }
            OpenMission(mission);
            ShowManage();
        }

        public void SetArchiveCollapsed(string missionId, bool collapsed)
        {
            if (_tracker.FindById(missionId) == null)
            {
                throw new InvalidOperationException("Mission does not exist: " + missionId);
            }
            if (collapsed)
            {
                _collapsedMissionIds.Add(missionId);
            }
            else
            {
                _collapsedMissionIds.Remove(missionId);
            }
            if (_visible && _showArchive)
            {
                PopulateArchive();
            }
        }

        public bool IsArchiveCollapsed(string missionId)
        {
            return !string.IsNullOrWhiteSpace(missionId) &&
                _collapsedMissionIds.Contains(missionId);
        }

        public void SetReviewScroll(string view, string anchor)
        {
            ScrollView scroll;
            if (string.Equals(view, "timeline", StringComparison.OrdinalIgnoreCase))
            {
                if (_showArchive)
                {
                    throw new InvalidOperationException(
                        "The timeline can only be scrolled while a mission story is open.");
                }
                scroll = _timelineScroll;
            }
            else if (string.Equals(view, "archive", StringComparison.OrdinalIgnoreCase))
            {
                if (!_showArchive)
                {
                    throw new InvalidOperationException(
                        "The archive can only be scrolled while the archive is open.");
                }
                scroll = _archiveScroll;
            }
            else
            {
                throw new ArgumentException(
                    "Review scroll view must be 'timeline' or 'archive'.", "view");
            }
            if (scroll == null)
            {
                throw new InvalidOperationException("The requested review view is not ready.");
            }

            float normalized;
            if (string.Equals(anchor, "top", StringComparison.OrdinalIgnoreCase))
            {
                normalized = 0f;
            }
            else if (string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase))
            {
                normalized = 0.5f;
            }
            else if (string.Equals(anchor, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                normalized = 1f;
            }
            else
            {
                throw new ArgumentException(
                    "Review scroll anchor must be 'top', 'middle', or 'bottom'.", "anchor");
            }

            float low = scroll.verticalScroller.lowValue;
            float high = scroll.verticalScroller.highValue;
            scroll.verticalScroller.value = low + ((high - low) * normalized);
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

            BuildArchiveView();
            BuildStoryView();
            body.Add(_archiveView);
            body.Add(_storyView);
            _shell.Add(body);
        }

        private void BuildArchiveView()
        {
            _archiveView = new VisualElement { name = "mission-archive-view" };
            _archiveView.style.flexGrow = 1f;
            _archiveView.style.minHeight = 0f;

            VisualElement toolbar = CreateActionRow();
            VisualElement heading = new VisualElement();
            heading.style.flexGrow = 1f;
            Label title = CreateHeading("MISSION ARCHIVE", 18f);
            _archiveCount = CreateMutedLabel(string.Empty);
            heading.Add(title);
            heading.Add(_archiveCount);
            toolbar.Add(heading);
            _backToStoryButton = CreateButton("Back to story", "Return to the selected mission", ShowStory);
            _backToStoryButton.style.flexGrow = 0f;
            _backToStoryButton.style.width = 140f;
            toolbar.Add(_backToStoryButton);
            _archiveView.Add(toolbar);

            Label help = CreateWrappedLabel(
                "Choose a mission to read its story. Docked flights and sorties remain nested beneath their shared mission.");
            help.style.marginTop = 5f;
            help.style.marginBottom = 8f;
            _archiveView.Add(help);

            InvertedCornerBox panel = CreatePanel("mission-archive-panel");
            panel.style.flexGrow = 1f;
            panel.style.minHeight = 0f;
            _archiveView.Add(panel);

            _archiveScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "mission-archive-scroll"
            };
            _archiveScroll.style.flexGrow = 1f;
            _archiveScroll.style.minHeight = 0f;
            _archiveScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.Add(_archiveScroll);
            _archiveTree = _archiveScroll.contentContainer;
        }

        private void BuildStoryView()
        {
            _storyView = new VisualElement { name = "mission-story-view" };
            _storyView.style.flexGrow = 1f;
            _storyView.style.minHeight = 0f;

            VisualElement navigation = CreateActionRow();
            Button archiveButton = CreateButton("All missions", "Open the mission archive", ShowArchive);
            archiveButton.style.flexGrow = 0f;
            archiveButton.style.width = 124f;
            navigation.Add(archiveButton);
            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            navigation.Add(spacer);
            Button editButton = CreateButton("Edit", "Edit the mission title and note", ShowEditor);
            editButton.style.flexGrow = 0f;
            editButton.style.width = 88f;
            navigation.Add(editButton);
            Button organizeButton = CreateButton("Organize", "Correct this mission's relationships", ShowManage);
            organizeButton.style.flexGrow = 0f;
            organizeButton.style.width = 112f;
            navigation.Add(organizeButton);
            _storyView.Add(navigation);

            InvertedCornerBox header = CreatePanel("mission-story-header");
            header.style.marginTop = 7f;
            header.style.marginBottom = 7f;
            header.style.flexShrink = 0f;
            _missionTitle = CreateHeading(string.Empty, 21f);
            _missionTitle.style.marginBottom = 5f;
            header.Add(_missionTitle);

            VisualElement chips = CreateActionRow();
            _statusChip = CreateChip();
            _kindChip = CreateChip();
            chips.Add(_statusChip);
            chips.Add(_kindChip);
            VisualElement chipSpacer = new VisualElement();
            chipSpacer.style.flexGrow = 1f;
            chips.Add(chipSpacer);
            header.Add(chips);

            _missionMeta = CreateWrappedLabel(string.Empty);
            _missionMeta.style.marginTop = 6f;
            header.Add(_missionMeta);
            _relationshipRow = new VisualElement { name = "mission-relationships" };
            _relationshipRow.style.flexDirection = FlexDirection.Row;
            _relationshipRow.style.flexWrap = Wrap.Wrap;
            _relationshipRow.style.marginTop = 4f;
            header.Add(_relationshipRow);
            _storyView.Add(header);

            _reviewBanner = CreatePanel("mission-review-banner");
            _reviewBanner.BorderColor = new Color32(221, 178, 80, 255);
            _reviewBanner.style.marginBottom = 7f;
            VisualElement reviewRow = CreateActionRow();
            Label reviewText = CreateWrappedLabel("This mission relationship needs review.");
            reviewText.style.flexGrow = 1f;
            reviewText.style.unityFontStyleAndWeight = FontStyle.Bold;
            reviewRow.Add(reviewText);
            Button reviewButton = CreateButton("Review", "Open the mission organizer", ShowManage);
            reviewButton.style.flexGrow = 0f;
            reviewButton.style.width = 92f;
            reviewRow.Add(reviewButton);
            _reviewBanner.Add(reviewRow);
            _storyView.Add(_reviewBanner);

            _noteCard = CreatePanel("mission-note-card");
            _noteCard.style.marginBottom = 7f;
            Label noteHeading = CreateMutedLabel("MISSION NOTE");
            noteHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
            _noteText = CreateWrappedLabel(string.Empty);
            _noteText.style.marginTop = 3f;
            _noteCard.Add(noteHeading);
            _noteCard.Add(_noteText);
            _storyView.Add(_noteCard);

            BuildEditorSheet();
            BuildManageSheet();
            _storyView.Add(_editorSheet);
            _storyView.Add(_manageSheet);

            VisualElement timelineHeader = CreateActionRow();
            Label storyHeading = CreateHeading("MISSION STORY", 16f);
            storyHeading.style.flexGrow = 1f;
            _timelineCount = CreateMutedLabel(string.Empty);
            _timelineCount.style.unityTextAlign = TextAnchor.MiddleRight;
            timelineHeader.Add(storyHeading);
            timelineHeader.Add(_timelineCount);
            _storyView.Add(timelineHeader);

            _timelineScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "mission-timeline-scroll"
            };
            _timelineScroll.style.flexGrow = 1f;
            _timelineScroll.style.minHeight = 0f;
            _timelineScroll.style.marginTop = 5f;
            _timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _storyView.Add(_timelineScroll);
            _timeline = _timelineScroll.contentContainer;
        }

        private ScrollView CurrentReviewScroll()
        {
            return _showArchive ? _archiveScroll : _timelineScroll;
        }

        private float CurrentReviewScrollValue()
        {
            ScrollView scroll = CurrentReviewScroll();
            return scroll == null ? 0f : scroll.verticalScroller.value;
        }

        private float CurrentReviewScrollMaximum()
        {
            ScrollView scroll = CurrentReviewScroll();
            return scroll == null ? 0f : scroll.verticalScroller.highValue;
        }

        private float ResolvedDimension(bool width, float fallback)
        {
            if (_shell == null)
            {
                return fallback;
            }
            float value = width ? _shell.resolvedStyle.width : _shell.resolvedStyle.height;
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
                ? fallback
                : value;
        }

        private void BuildEditorSheet()
        {
            _editorSheet = CreatePanel("mission-editor-sheet");
            _editorSheet.BorderColor = new Color32(119, 152, 204, 255);
            _editorSheet.style.marginBottom = 7f;
            _editorSheet.Add(CreateHeading("EDIT MISSION", 15f));

            Label titleLabel = CreateMutedLabel("TITLE");
            titleLabel.style.marginTop = 5f;
            _editorSheet.Add(titleLabel);
            _titleField = new TextField { name = "mission-title" };
            _titleField.AddToClassList("oab-text-field");
            _editorSheet.Add(_titleField);

            Label notesLabel = CreateMutedLabel("NOTE");
            notesLabel.style.marginTop = 5f;
            _editorSheet.Add(notesLabel);
            _notesField = new TextField { name = "mission-notes", multiline = true };
            _notesField.style.height = 72f;
            _editorSheet.Add(_notesField);

            VisualElement actions = CreateActionRow();
            actions.style.marginTop = 6f;
            Button cancel = CreateButton("Cancel", "Discard these edits", HideSheets);
            Button save = CreateButton("Save", "Save the mission title and note", SaveEdits);
            _completeButton = CreateButton(
                "Complete mission",
                "Close the current mission as completed",
                CompleteMission);
            actions.Add(cancel);
            actions.Add(save);
            actions.Add(_completeButton);
            _editorSheet.Add(actions);
        }

        private void BuildManageSheet()
        {
            _manageSheet = CreatePanel("mission-manage-sheet");
            _manageSheet.BorderColor = new Color32(188, 161, 255, 255);
            _manageSheet.style.marginBottom = 7f;
            _manageSheet.Add(CreateHeading("ORGANIZE MISSION", 15f));
            Label help = CreateWrappedLabel(
                "Use these only when the automatic mission relationship is not the story you intended.");
            help.style.marginTop = 4f;
            help.style.marginBottom = 5f;
            _manageSheet.Add(help);
            _manageFeedback = CreateWrappedLabel(string.Empty);
            _manageFeedback.style.marginBottom = 5f;
            _manageSheet.Add(_manageFeedback);
            _manageBody = new VisualElement { name = "mission-manage-actions" };
            _manageSheet.Add(_manageBody);
        }

        private void Refresh()
        {
            MissionRecord selected = FindSelected();
            if (selected == null)
            {
                _showArchive = true;
            }
            _archiveView.style.display = _showArchive ? DisplayStyle.Flex : DisplayStyle.None;
            _storyView.style.display = _showArchive ? DisplayStyle.None : DisplayStyle.Flex;
            if (_showArchive)
            {
                PopulateArchive();
            }
            else
            {
                PopulateStory(selected);
            }
        }

        private void PopulateArchive()
        {
            _archiveTree.Clear();
            int count = _tracker.Archive.Missions.Count;
            _archiveCount.text = count + (count == 1 ? " mission record" : " mission records");
            _backToStoryButton.style.display = FindSelected() == null
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            List<MissionRecord> roots = _tracker.GetRoots();
            roots.Reverse();
            if (roots.Count == 0)
            {
                Label empty = CreateWrappedLabel(
                    "Your first mission story will appear here when a vessel enters flight.");
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 24f;
                _archiveTree.Add(empty);
                return;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < roots.Count; index++)
            {
                AddArchiveNode(roots[index], 0, visited);
            }
        }

        private void AddArchiveNode(MissionRecord mission, int depth, HashSet<string> visited)
        {
            if (mission == null || !visited.Add(mission.MissionId))
            {
                return;
            }

            List<MissionRecord> children = _tracker.GetChildren(mission);
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginLeft = Math.Min(depth, 7) * 16f;
            row.style.marginBottom = 5f;

            if (children.Count > 0)
            {
                bool collapsed = _collapsedMissionIds.Contains(mission.MissionId);
                Button expander = CreateButton(
                    collapsed ? "▸" : "▾",
                    collapsed ? "Expand this mission" : "Collapse this mission",
                    delegate
                    {
                        if (!_collapsedMissionIds.Remove(mission.MissionId))
                        {
                            _collapsedMissionIds.Add(mission.MissionId);
                        }
                        PopulateArchive();
                    });
                expander.style.flexGrow = 0f;
                expander.style.width = 32f;
                expander.style.minWidth = 32f;
                row.Add(expander);
            }
            else
            {
                VisualElement spacer = new VisualElement();
                spacer.style.width = 36f;
                spacer.style.flexShrink = 0f;
                row.Add(spacer);
            }

            string marker = mission.IsActive ? "● " : "○ ";
            string context = mission.Status.ToUpperInvariant() + "  ·  " +
                mission.MissionKind.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(mission.LastBody))
            {
                context += "  ·  " + mission.LastBody;
            }
            Button open = CreateButton(
                marker + mission.Title + "\n" + context,
                "Open " + mission.Title,
                delegate { OpenMission(mission); });
            open.style.height = 52f;
            open.style.whiteSpace = WhiteSpace.Normal;
            open.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (string.Equals(mission.MissionId, _selectedMissionId, StringComparison.Ordinal))
            {
                open.AddToClassList("selected");
                open.style.borderLeftWidth = 3f;
                open.style.borderLeftColor =
                    new StyleColor(new Color32(188, 161, 255, 255));
            }
            row.Add(open);
            _archiveTree.Add(row);

            if (_collapsedMissionIds.Contains(mission.MissionId))
            {
                return;
            }
            for (int index = 0; index < children.Count; index++)
            {
                AddArchiveNode(children[index], depth + 1, visited);
            }
        }

        private void PopulateStory(MissionRecord mission)
        {
            if (mission == null)
            {
                ShowArchive();
                return;
            }

            MissionAggregate aggregate = _tracker.GetAggregate(mission);
            List<MissionRecord> children = _tracker.GetChildren(mission);
            MissionRecord parent = _tracker.GetParent(mission);

            _missionTitle.text = mission.Title;
            _statusChip.text = mission.Status.ToUpperInvariant();
            _kindChip.text = mission.MissionKind.ToUpperInvariant();
            ApplyChipColor(_statusChip, StatusColor(mission.Status));
            ApplyChipColor(_kindChip, new Color32(119, 152, 204, 255));
            _missionMeta.text = BuildMissionMeta(mission, aggregate);
            PopulateRelationships(mission, parent, children);

            _reviewBanner.style.display = mission.NeedsReview
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            bool hasNote = !string.IsNullOrWhiteSpace(mission.Notes);
            _noteCard.style.display = hasNote ? DisplayStyle.Flex : DisplayStyle.None;
            _noteText.text = hasNote ? mission.Notes : string.Empty;

            _editorSheet.style.display = _showEditor ? DisplayStyle.Flex : DisplayStyle.None;
            _manageSheet.style.display = _showManage ? DisplayStyle.Flex : DisplayStyle.None;
            _completeButton.style.display = mission.IsActive &&
                ReferenceEquals(mission, _tracker.GetCurrent())
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (_showManage)
            {
                PopulateManage(mission, parent);
            }
            PopulateTimeline(mission);
        }

        private void PopulateRelationships(
            MissionRecord mission,
            MissionRecord parent,
            List<MissionRecord> children)
        {
            _relationshipRow.Clear();
            if (parent != null)
            {
                Label prefix = CreateMutedLabel("PART OF  ");
                prefix.style.unityTextAlign = TextAnchor.MiddleLeft;
                _relationshipRow.Add(prefix);
                Button parentLink = CreateLinkButton(parent.Title, delegate { OpenMission(parent); });
                _relationshipRow.Add(parentLink);
            }
            if (children.Count > 0)
            {
                if (parent != null)
                {
                    Label separator = CreateMutedLabel("   ·   ");
                    _relationshipRow.Add(separator);
                }
                string label = children.Count + (children.Count == 1
                    ? " CONNECTED MISSION"
                    : " CONNECTED MISSIONS");
                Button branches = CreateLinkButton(label, ShowArchive);
                _relationshipRow.Add(branches);
            }
            _relationshipRow.style.display = parent == null && children.Count == 0
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void PopulateTimeline(MissionRecord mission)
        {
            List<MissionTimelineItem> items = _tracker.GetTimeline(mission);
            RenderedTimelineCount = items.Count;
            _timelineCount.text = items.Count + (items.Count == 1 ? " moment" : " moments");
            string fingerprint = TimelineFingerprint(mission, items);
            if (string.Equals(fingerprint, _timelineFingerprint, StringComparison.Ordinal))
            {
                return;
            }
            _timelineFingerprint = fingerprint;
            _timeline.Clear();

            if (items.Count == 0)
            {
                Label empty = CreateWrappedLabel("The first meaningful mission moment will appear here.");
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 24f;
                _timeline.Add(empty);
                return;
            }

            bool showSourceClocks = HasMultipleTimelineSources(items);
            for (int index = 0; index < items.Count; index++)
            {
                _timeline.Add(CreateTimelineRow(items[index], mission, showSourceClocks));
            }
        }

        private VisualElement CreateTimelineRow(
            MissionTimelineItem item,
            MissionRecord selected,
            bool showSourceClock)
        {
            Color32 accent = CategoryColor(item.Category, item.SourceMission.Status);
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0f;
            row.style.marginBottom = 7f;

            VisualElement marker = new VisualElement();
            float markerWidth = showSourceClock ? 112f : 86f;
            marker.style.width = markerWidth;
            marker.style.minWidth = markerWidth;
            marker.style.alignItems = Align.Center;
            marker.style.paddingTop = 4f;

            if (showSourceClock)
            {
                Label sourceClock = CreateMutedLabel(
                    (item.SourceMission.Title ?? "Mission leg").ToUpperInvariant());
                sourceClock.tooltip = "Flight clock for " + item.SourceMission.Title;
                sourceClock.style.width = markerWidth - 8f;
                sourceClock.style.fontSize = 9f;
                sourceClock.style.whiteSpace = WhiteSpace.NoWrap;
                sourceClock.style.overflow = Overflow.Hidden;
                sourceClock.style.textOverflow = TextOverflow.Ellipsis;
                sourceClock.style.unityTextAlign = TextAnchor.MiddleCenter;
                marker.Add(sourceClock);
            }

            Label time = CreateMutedLabel(item.IsDerived
                ? "ARCHIVE SUMMARY"
                : "T+" + FormatDuration(item.Event.FlightTimeSeconds));
            time.style.unityTextAlign = TextAnchor.MiddleCenter;
            time.style.fontSize = item.IsDerived ? 9f : 11f;
            marker.Add(time);

            Label symbol = new Label(item.Symbol);
            symbol.style.width = 30f;
            symbol.style.height = 30f;
            symbol.style.marginTop = 4f;
            symbol.style.borderLeftWidth = 1f;
            symbol.style.borderRightWidth = 1f;
            symbol.style.borderTopWidth = 1f;
            symbol.style.borderBottomWidth = 1f;
            symbol.style.borderLeftColor = new StyleColor(accent);
            symbol.style.borderRightColor = new StyleColor(accent);
            symbol.style.borderTopColor = new StyleColor(accent);
            symbol.style.borderBottomColor = new StyleColor(accent);
            symbol.style.borderTopLeftRadius = 5f;
            symbol.style.borderTopRightRadius = 5f;
            symbol.style.borderBottomLeftRadius = 5f;
            symbol.style.borderBottomRightRadius = 5f;
            symbol.style.color = new StyleColor(accent);
            symbol.style.unityTextAlign = TextAnchor.MiddleCenter;
            symbol.style.fontSize = 17f;
            marker.Add(symbol);
            row.Add(marker);

            InvertedCornerBox card = CreatePanel("timeline-" + item.Event.EventId);
            card.BorderColor = accent;
            card.style.flexGrow = 1f;
            card.style.minWidth = 0f;
            card.style.minHeight = 72f;

            VisualElement cardHeader = CreateActionRow();
            Label category = CreateMutedLabel(item.CategoryLabel);
            category.style.color = new StyleColor(accent);
            category.style.unityFontStyleAndWeight = FontStyle.Bold;
            category.style.flexGrow = 1f;
            cardHeader.Add(category);
            if (!ReferenceEquals(item.SourceMission, selected))
            {
                Button source = CreateLinkButton(
                    item.SourceMission.Title,
                    delegate { OpenMission(item.SourceMission); });
                source.tooltip = "Open this mission leg";
                source.style.maxWidth = 280f;
                source.style.unityTextAlign = TextAnchor.MiddleRight;
                cardHeader.Add(source);
            }
            card.Add(cardHeader);

            Label title = CreateWrappedLabel(item.Event.Title);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15f;
            title.style.marginTop = 3f;
            card.Add(title);

            string context = BuildEventContext(item);
            if (!string.IsNullOrWhiteSpace(context))
            {
                Label metadata = CreateMutedLabel(context);
                metadata.style.marginTop = 3f;
                card.Add(metadata);
            }
            row.Add(card);
            return row;
        }

        private static bool HasMultipleTimelineSources(List<MissionTimelineItem> items)
        {
            var sources = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++)
            {
                MissionRecord source = items[index].SourceMission;
                if (source != null)
                {
                    sources.Add(source.MissionId ?? string.Empty);
                }
                if (sources.Count > 1)
                {
                    return true;
                }
            }
            return false;
        }

        private void PopulateManage(MissionRecord selected, MissionRecord parent)
        {
            _manageBody.Clear();
            bool hasFeedback = !string.IsNullOrWhiteSpace(_feedback);
            _manageFeedback.text = hasFeedback ? _feedback : string.Empty;
            _manageFeedback.style.display = hasFeedback ? DisplayStyle.Flex : DisplayStyle.None;

            if (!string.IsNullOrWhiteSpace(_pendingAction))
            {
                Label prompt = CreateWrappedLabel(_pendingPrompt);
                prompt.style.marginBottom = 5f;
                _manageBody.Add(prompt);
                VisualElement confirmation = CreateActionRow();
                confirmation.Add(CreateButton("Cancel", "Leave the mission tree unchanged", CancelPending));
                confirmation.Add(CreateButton("Confirm", "Apply this mission-tree change", ConfirmPending));
                _manageBody.Add(confirmation);
                return;
            }

            MissionRecord current = _tracker.GetCurrent();
            int actionCount = 0;
            if (current != null && !ReferenceEquals(current, selected) && selected.IsActive &&
                selected.TrackedVesselIds.Count == 1 &&
                string.Equals(current.CampaignId, selected.CampaignId, StringComparison.Ordinal))
            {
                _manageBody.Add(CreateButton(
                    "Combine " + current.Title + " with this mission",
                    "Create one overarching mission containing both histories",
                    BeginCombine));
                actionCount++;
            }
            if (current != null && !ReferenceEquals(current, selected) &&
                string.Equals(current.CampaignId, selected.CampaignId, StringComparison.Ordinal))
            {
                _manageBody.Add(CreateButton(
                    "Place " + current.Title + " under this mission",
                    "Make the current mission a child of this mission",
                    BeginAdopt));
                actionCount++;
            }
            if (parent != null)
            {
                _manageBody.Add(CreateButton(
                    "Move this mission to the top level",
                    "Remove this mission from its current parent",
                    BeginUnlink));
                actionCount++;
            }
            if (_tracker.CanTrackCurrentAs(selected) &&
                (current == null || !ReferenceEquals(current, selected)))
            {
                _manageBody.Add(CreateButton(
                    "Assign the current vessel to this mission",
                    "Repair the current vessel's mission binding",
                    BeginTrack));
                actionCount++;
            }
            if (actionCount == 0)
            {
                _manageBody.Add(CreateWrappedLabel(
                    "No relationship changes are available for this mission right now."));
            }
            Button done = CreateButton("Done", "Close the organizer", HideSheets);
            done.style.marginTop = 6f;
            _manageBody.Add(done);
        }

        private void ShowArchive()
        {
            _showArchive = true;
            _showEditor = false;
            _showManage = false;
            Refresh();
        }

        private void ShowStory()
        {
            _showArchive = false;
            _showEditor = false;
            _showManage = false;
            Refresh();
        }

        private void ShowEditor()
        {
            MissionRecord mission = FindSelected();
            if (mission == null)
            {
                return;
            }
            _titleField.SetValueWithoutNotify(mission.Title ?? string.Empty);
            _notesField.SetValueWithoutNotify(mission.Notes ?? string.Empty);
            _showEditor = true;
            _showManage = false;
            _feedback = null;
            Refresh();
        }

        private void ShowManage()
        {
            _showManage = true;
            _showEditor = false;
            _feedback = null;
            ClearPending();
            Refresh();
        }

        private void HideSheets()
        {
            _showEditor = false;
            _showManage = false;
            _feedback = null;
            ClearPending();
            Refresh();
        }

        private void SaveEdits()
        {
            MissionRecord mission = FindSelected();
            if (mission == null)
            {
                return;
            }
            _tracker.SaveEdits(mission, _titleField.value, _notesField.value);
            _showEditor = false;
            _timelineFingerprint = null;
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
            _showEditor = false;
            _timelineFingerprint = null;
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
                "Combine " + current.Title + " and " + selected.Title +
                    " into one overarching mission? Both histories remain intact.");
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
                "Place " + current.Title + " beneath " + selected.Title + "?");
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
                    "? The previous binding will be marked for review.");
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
            _pendingAction = action;
            _pendingSelectedMissionId = selected == null ? null : selected.MissionId;
            _pendingCurrentMissionId = current == null ? null : current.MissionId;
            _pendingVesselId = _tracker.ActiveVesselId;
            _pendingPrompt = prompt;
            _feedback = null;
            Refresh();
        }

        private void ConfirmPending()
        {
            ExecutePending(FindSelected());
            _timelineFingerprint = null;
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
                _feedback = "Mission relationship updated.";
            }
            catch (Exception error)
            {
                _feedback = "Could not update the mission: " + error.Message;
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
            _timelineFingerprint = null;
            RenderedTimelineCount = 0;
            ClearPending();
            _feedback = null;
        }

        private MissionRecord FindSelected()
        {
            MissionRecord selected = _tracker.FindById(_selectedMissionId);
            if (selected != null)
            {
                return selected;
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

        private static string BuildMissionMeta(MissionRecord mission, MissionAggregate aggregate)
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(mission.VesselName))
            {
                values.Add(mission.VesselName);
            }
            values.Add(aggregate.Crew.Count == 0
                ? "Uncrewed"
                : string.Join(", ", aggregate.Crew.ToArray()));
            if (aggregate.VisitedBodies.Count > 0)
            {
                values.Add(string.Join(" · ", aggregate.VisitedBodies.ToArray()));
            }
            values.Add(FormatDuration(mission.FlightDurationSeconds));
            return string.Join("   ·   ", values.ToArray());
        }

        private static string BuildEventContext(MissionTimelineItem item)
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Event.Body))
            {
                values.Add(item.Event.Body);
            }
            if (!string.IsNullOrWhiteSpace(item.Event.Situation))
            {
                values.Add(FriendlySituation(item.Event.Situation));
            }
            if (item.IsDerived)
            {
                values.Add("Summary from an earlier archive");
            }
            return string.Join("  ·  ", values.ToArray());
        }

        private static string FriendlySituation(string situation)
        {
            if (string.IsNullOrWhiteSpace(situation))
            {
                return string.Empty;
            }
            var result = new StringBuilder();
            for (int index = 0; index < situation.Length; index++)
            {
                char current = situation[index];
                if (index > 0 && char.IsUpper(current) && char.IsLower(situation[index - 1]))
                {
                    result.Append(' ');
                }
                result.Append(current);
            }
            return result.ToString();
        }

        private static string TimelineFingerprint(
            MissionRecord mission,
            List<MissionTimelineItem> items)
        {
            var result = new StringBuilder();
            result.Append(mission.MissionId).Append('|').Append(mission.Title);
            for (int index = 0; index < items.Count; index++)
            {
                MissionTimelineItem item = items[index];
                result.Append('|')
                    .Append(item.Event.EventId)
                    .Append(':')
                    .Append(item.Event.RecordedUtc)
                    .Append(':')
                    .Append(item.Event.Title)
                    .Append(':')
                    .Append(item.SourceMission.Title);
            }
            return result.ToString();
        }

        private static InvertedCornerBox CreatePanel(string name)
        {
            InvertedCornerBox panel = new InvertedCornerBox { name = name };
            panel.style.paddingLeft = 9f;
            panel.style.paddingRight = 9f;
            panel.style.paddingTop = 8f;
            panel.style.paddingBottom = 8f;
            return panel;
        }

        private static Label CreateHeading(string text, float size)
        {
            Label heading = CreateWrappedLabel(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = size;
            return heading;
        }

        private static Label CreateChip()
        {
            Label chip = new Label();
            chip.style.height = 25f;
            chip.style.paddingLeft = 8f;
            chip.style.paddingRight = 8f;
            chip.style.marginRight = 5f;
            chip.style.borderLeftWidth = 1f;
            chip.style.borderRightWidth = 1f;
            chip.style.borderTopWidth = 1f;
            chip.style.borderBottomWidth = 1f;
            chip.style.borderTopLeftRadius = 5f;
            chip.style.borderTopRightRadius = 5f;
            chip.style.borderBottomLeftRadius = 5f;
            chip.style.borderBottomRightRadius = 5f;
            chip.style.unityTextAlign = TextAnchor.MiddleCenter;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.fontSize = 11f;
            return chip;
        }

        private static void ApplyChipColor(Label chip, Color32 color)
        {
            StyleColor style = new StyleColor(color);
            chip.style.color = style;
            chip.style.borderLeftColor = style;
            chip.style.borderRightColor = style;
            chip.style.borderTopColor = style;
            chip.style.borderBottomColor = style;
        }

        private static Label CreateWrappedLabel(string text)
        {
            Label label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0f;
            return label;
        }

        private static Label CreateMutedLabel(string text)
        {
            Label label = CreateWrappedLabel(text);
            label.style.color = new StyleColor(new Color32(165, 171, 184, 255));
            label.style.fontSize = 12f;
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

        private static Button CreateLinkButton(string text, Action clicked)
        {
            Button button = new Button(clicked) { text = text };
            button.AddToClassList("link");
            button.AddToClassList("ui-sound-button");
            button.style.flexGrow = 0f;
            button.style.height = StyleKeyword.Auto;
            button.style.whiteSpace = WhiteSpace.Normal;
            return button;
        }

        private static Color32 StatusColor(string status)
        {
            if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "Recovered", StringComparison.OrdinalIgnoreCase))
            {
                return new Color32(112, 204, 151, 255);
            }
            if (string.Equals(status, "Lost", StringComparison.OrdinalIgnoreCase))
            {
                return new Color32(209, 119, 119, 255);
            }
            if (string.Equals(status, "Joined", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "Rejoined", StringComparison.OrdinalIgnoreCase))
            {
                return new Color32(188, 161, 255, 255);
            }
            return new Color32(119, 152, 204, 255);
        }

        private static Color32 CategoryColor(string category, string status)
        {
            if (string.Equals(category, "launch", StringComparison.Ordinal))
            {
                return new Color32(188, 161, 255, 255);
            }
            if (string.Equals(category, "navigation", StringComparison.Ordinal))
            {
                return new Color32(119, 152, 204, 255);
            }
            if (string.Equals(category, "surface", StringComparison.Ordinal))
            {
                return new Color32(112, 204, 151, 255);
            }
            if (string.Equals(category, "topology", StringComparison.Ordinal))
            {
                return new Color32(202, 151, 232, 255);
            }
            if (string.Equals(category, "record", StringComparison.Ordinal))
            {
                return new Color32(221, 178, 80, 255);
            }
            if (string.Equals(category, "outcome", StringComparison.Ordinal))
            {
                return StatusColor(status);
            }
            return new Color32(116, 118, 128, 255);
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
