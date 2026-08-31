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
        private sealed class TimelineExpansionState
        {
            public VisualElement Row;
            public VisualElement Details;
            public Label Chevron;
            public bool Hovered;
            public bool Focused;
        }

        private const float DefaultWidth = 820f;
        private const float DefaultHeight = 700f;
        private const float MinimumWidth = 620f;
        private const float MinimumHeight = 500f;

        private readonly MissionTracker _tracker;
        private readonly MissionPlanner _planner;
        private readonly MissionPlannerPanel _plannerPanel;
        private readonly Action<string> _logError;
        private readonly HashSet<string> _collapsedMissionIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _expandedTimelineEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimelineExpansionState> _timelineExpansionStates =
            new Dictionary<string, TimelineExpansionState>(StringComparer.Ordinal);

        private UIDocument _document;
        private AppShell _shell;
        private VisualElement _archiveView;
        private VisualElement _storyView;
        private VisualElement _plannerView;
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
        private Label _reviewText;
        private InvertedCornerBox _noteCard;
        private Label _noteText;
        private InvertedCornerBox _planBanner;
        private Label _planText;
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
        private bool _showPlanner;
        private bool _showEditor;
        private bool _showManage;
        private bool _followCurrentMission = true;
        private string _selectedMissionId;
        private string _timelineFingerprint;
        private string _archiveFingerprint;
        private string _manageFingerprint;
        private string _linkedPlanId;
        private string _pendingAction;
        private string _pendingSelectedMissionId;
        private string _pendingCurrentMissionId;
        private string _pendingVesselId;
        private string _pendingPrompt;
        private string _feedback;

        public MissionLogWindow(
            MissionTracker tracker,
            MissionPlanner planner,
            MissionPlanLaunchService launchService,
            Action<string> logError,
            Action<string> info)
        {
            _tracker = tracker;
            _planner = planner;
            _logError = logError;
            _plannerPanel = new MissionPlannerPanel(
                planner,
                tracker,
                launchService,
                logError,
                info);
        }

        public bool Visible { get { return _visible; } }
        public string SelectedMissionId { get { return _selectedMissionId; } }
        public int RenderedTimelineCount { get; private set; }
        public string ReviewView
        {
            get { return _showPlanner ? "planner" : (_showArchive ? "archive" : "story"); }
        }
        public string SelectedPlanId { get { return _plannerPanel.SelectedPlanId; } }
        public string SelectedPlanStatus { get { return _plannerPanel.SelectedPlanStatus; } }
        public string SelectedPlanProgress { get { return _plannerPanel.SelectedPlanProgress; } }
        public int SelectedPlanDeviationCount
        {
            get { return _plannerPanel.SelectedPlanDeviationCount; }
        }
        public int RenderedPlanCount { get { return _plannerPanel.RenderedPlanCount; } }
        public int RenderedPlanVesselCount { get { return _plannerPanel.RenderedVesselCount; } }
        public int RenderedPlanObjectiveCount { get { return _plannerPanel.RenderedObjectiveCount; } }
        public int RenderedPlanDeviationCount { get { return _plannerPanel.RenderedDeviationCount; } }
        public int SavedVehicleCount { get { return _plannerPanel.SavedVehicleCount; } }
        public bool PlannerCreateEditorVisible
        {
            get { return _plannerPanel.CreateEditorVisible; }
        }
        public bool PlannerPlanEditorVisible
        {
            get { return _plannerPanel.PlanEditorVisible; }
        }
        public bool PlannerVesselEditorVisible
        {
            get { return _plannerPanel.VesselEditorVisible; }
        }
        public bool PlannerObjectiveEditorVisible
        {
            get { return _plannerPanel.ObjectiveEditorVisible; }
        }
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
        public int ExpandedTimelineEventCount
        {
            get { return _expandedTimelineEventIds.Count; }
        }
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
                bool wasVisible = _visible;
                EnsureCreated();
                if (!wasVisible)
                {
                    Select(_tracker.GetCurrent() ?? _tracker.GetLatest());
                    _followCurrentMission = true;
                }
                if (!_showPlanner)
                {
                    _showArchive = FindSelected() == null;
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

        public void OpenMission(MissionRecord mission)
        {
            if (!_visible)
            {
                SetVisible(true);
            }
            if (!_visible)
            {
                return;
            }
            Select(mission);
            _followCurrentMission = false;
            _showPlanner = false;
            _showArchive = false;
            _plannerPanel.Hide();
            _showEditor = false;
            _showManage = false;
            Refresh();
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
            _showPlanner = false;
            _showArchive = true;
            _plannerPanel.Hide();
            _showEditor = false;
            _showManage = false;
            Refresh();
        }

        public void OpenPlanner()
        {
            OpenPlanner(null);
        }

        public void OpenPlanner(string planId)
        {
            if (!_visible)
            {
                SetVisible(true);
            }
            if (!_visible)
            {
                return;
            }
            _showPlanner = true;
            _showArchive = false;
            _showEditor = false;
            _showManage = false;
            if (string.IsNullOrWhiteSpace(planId))
            {
                _plannerPanel.Open();
            }
            else
            {
                _plannerPanel.Open(planId);
            }
            Refresh();
        }

        public void RefreshSavedVehicles()
        {
            _plannerPanel.RefreshSavedVehicles();
            if (_visible && _showPlanner)
            {
                _plannerPanel.Refresh();
            }
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

        public void SetTimelineEventExpanded(string eventId, bool expanded)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                throw new ArgumentException("A timeline event id is required.", "eventId");
            }
            TimelineExpansionState state;
            if (!_timelineExpansionStates.TryGetValue(eventId, out state))
            {
                throw new InvalidOperationException(
                    "Timeline event is not rendered in the open mission story: " + eventId);
            }
            if (expanded)
            {
                _expandedTimelineEventIds.Add(eventId);
            }
            else
            {
                _expandedTimelineEventIds.Remove(eventId);
            }
            ApplyTimelineExpansion(eventId, state);
        }

        public bool IsTimelineEventExpanded(string eventId)
        {
            return !string.IsNullOrWhiteSpace(eventId) &&
                _expandedTimelineEventIds.Contains(eventId);
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
            BuildPlannerView();
            body.Add(_archiveView);
            body.Add(_storyView);
            body.Add(_plannerView);
            _shell.Add(body);
        }

        private void BuildPlannerView()
        {
            _plannerView = new VisualElement { name = "mission-planner-workspace-view" };
            _plannerView.style.flexGrow = 1f;
            _plannerView.style.minHeight = 0f;
            _plannerView.style.minWidth = 0f;

            VisualElement navigation = CreateActionRow();
            Button story = CreateButton(
                "Back to story",
                "Return to the selected mission story",
                ShowStory);
            story.style.flexGrow = 0f;
            story.style.width = 132f;
            navigation.Add(story);
            Button archive = CreateButton(
                "All missions",
                "Open the mission archive",
                ShowArchive);
            archive.style.flexGrow = 0f;
            archive.style.width = 120f;
            navigation.Add(archive);
            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            navigation.Add(spacer);
            Label mode = CreateMutedLabel("PLANNING WORKSPACE");
            mode.style.unityTextAlign = TextAnchor.MiddleRight;
            navigation.Add(mode);
            _plannerView.Add(navigation);
            _plannerView.Add(_plannerPanel.Root);
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
            Button plannerButton = CreateButton(
                "Mission planner",
                "Open planned missions",
                OpenPlanner);
            plannerButton.style.flexGrow = 0f;
            plannerButton.style.width = 140f;
            toolbar.Add(plannerButton);
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
            Button currentButton = CreateButton(
                "Current",
                "Follow the mission for the active vessel",
                ShowCurrentStory);
            currentButton.style.flexGrow = 0f;
            currentButton.style.width = 92f;
            navigation.Add(currentButton);
            Button plannerButton = CreateButton(
                "Plan mission",
                "Open the mission planner",
                OpenPlanner);
            plannerButton.style.flexGrow = 0f;
            plannerButton.style.width = 116f;
            navigation.Add(plannerButton);
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
            header.style.marginTop = 5f;
            header.style.marginBottom = 5f;
            header.style.paddingTop = 6f;
            header.style.paddingBottom = 6f;
            header.style.flexShrink = 0f;
            VisualElement titleRow = CreateActionRow();
            titleRow.style.alignItems = Align.Center;
            _missionTitle = CreateHeading(string.Empty, 19f);
            _missionTitle.style.flexGrow = 1f;
            _missionTitle.style.flexShrink = 1f;
            _missionTitle.style.minWidth = 0f;
            _missionTitle.style.whiteSpace = WhiteSpace.NoWrap;
            _missionTitle.style.overflow = Overflow.Hidden;
            _missionTitle.style.textOverflow = TextOverflow.Ellipsis;
            titleRow.Add(_missionTitle);

            _statusChip = CreateChip();
            _kindChip = CreateChip();
            titleRow.Add(_statusChip);
            titleRow.Add(_kindChip);
            header.Add(titleRow);

            _missionMeta = CreateMutedLabel(string.Empty);
            _missionMeta.style.marginTop = 3f;
            _missionMeta.style.flexShrink = 1f;
            _missionMeta.style.whiteSpace = WhiteSpace.NoWrap;
            _missionMeta.style.overflow = Overflow.Hidden;
            _missionMeta.style.textOverflow = TextOverflow.Ellipsis;
            header.Add(_missionMeta);
            _relationshipRow = new VisualElement { name = "mission-relationships" };
            _relationshipRow.style.flexDirection = FlexDirection.Row;
            _relationshipRow.style.flexWrap = Wrap.Wrap;
            _relationshipRow.style.marginTop = 2f;
            header.Add(_relationshipRow);
            _storyView.Add(header);

            _reviewBanner = CreatePanel("mission-review-banner");
            _reviewBanner.BorderColor = new Color32(221, 178, 80, 255);
            _reviewBanner.style.marginBottom = 5f;
            _reviewBanner.style.paddingTop = 5f;
            _reviewBanner.style.paddingBottom = 5f;
            VisualElement reviewRow = CreateActionRow();
            reviewRow.style.alignItems = Align.Center;
            _reviewText = CreateWrappedLabel(string.Empty);
            _reviewText.style.flexGrow = 1f;
            _reviewText.style.flexShrink = 1f;
            _reviewText.style.minWidth = 0f;
            _reviewText.style.whiteSpace = WhiteSpace.NoWrap;
            _reviewText.style.overflow = Overflow.Hidden;
            _reviewText.style.textOverflow = TextOverflow.Ellipsis;
            _reviewText.style.unityFontStyleAndWeight = FontStyle.Bold;
            reviewRow.Add(_reviewText);
            Button reviewButton = CreateButton("Review", "Open the mission organizer", ShowManage);
            reviewButton.style.flexGrow = 0f;
            reviewButton.style.width = 92f;
            reviewRow.Add(reviewButton);
            _reviewBanner.Add(reviewRow);
            _storyView.Add(_reviewBanner);

            _noteCard = CreatePanel("mission-note-card");
            _noteCard.style.marginBottom = 5f;
            _noteCard.style.paddingTop = 5f;
            _noteCard.style.paddingBottom = 5f;
            _noteCard.style.flexDirection = FlexDirection.Row;
            _noteCard.style.alignItems = Align.Center;
            Label noteHeading = CreateMutedLabel("NOTE");
            noteHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
            noteHeading.style.width = 48f;
            noteHeading.style.minWidth = 48f;
            _noteText = new Label();
            _noteText.style.flexGrow = 1f;
            _noteText.style.flexShrink = 1f;
            _noteText.style.minWidth = 0f;
            _noteText.style.whiteSpace = WhiteSpace.NoWrap;
            _noteText.style.overflow = Overflow.Hidden;
            _noteText.style.textOverflow = TextOverflow.Ellipsis;
            _noteCard.Add(noteHeading);
            _noteCard.Add(_noteText);
            _storyView.Add(_noteCard);

            _planBanner = CreatePanel("mission-plan-context");
            _planBanner.BorderColor = new Color32(188, 161, 255, 255);
            _planBanner.style.marginBottom = 5f;
            _planBanner.style.paddingTop = 5f;
            _planBanner.style.paddingBottom = 5f;
            VisualElement planRow = CreateActionRow();
            planRow.style.alignItems = Align.Center;
            _planText = CreateWrappedLabel(string.Empty);
            _planText.style.flexGrow = 1f;
            _planText.style.flexShrink = 1f;
            _planText.style.minWidth = 0f;
            _planText.style.whiteSpace = WhiteSpace.NoWrap;
            _planText.style.overflow = Overflow.Hidden;
            _planText.style.textOverflow = TextOverflow.Ellipsis;
            _planText.style.unityFontStyleAndWeight = FontStyle.Bold;
            planRow.Add(_planText);
            Button openPlan = CreateButton(
                "View plan",
                "Open this mission's plan and progress",
                delegate
                {
                    if (!string.IsNullOrWhiteSpace(_linkedPlanId))
                    {
                        OpenPlanner(_linkedPlanId);
                    }
                });
            openPlan.style.flexGrow = 0f;
            openPlan.style.width = 100f;
            planRow.Add(openPlan);
            _planBanner.Add(planRow);
            _storyView.Add(_planBanner);

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
            _notesField.AddToClassList("oab-text-field");
            _notesField.style.width = Length.Percent(100f);
            _notesField.style.maxWidth = Length.Percent(100f);
            _notesField.style.minWidth = 0f;
            _notesField.style.height = 82f;
            _notesField.style.whiteSpace = WhiteSpace.Normal;
            _notesField.style.overflow = Overflow.Hidden;
            VisualElement notesInput = _notesField.Q<VisualElement>("unity-text-input");
            if (notesInput != null)
            {
                notesInput.style.whiteSpace = WhiteSpace.Normal;
                notesInput.style.overflow = Overflow.Hidden;
            }
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
            if (!_showPlanner && !_showArchive && _followCurrentMission)
            {
                MissionRecord current = _tracker.GetCurrent();
                if (current != null && !string.Equals(
                    current.MissionId,
                    _selectedMissionId,
                    StringComparison.Ordinal))
                {
                    Select(current);
                }
            }
            MissionRecord selected = FindSelected();
            if (selected == null && !_showPlanner)
            {
                _showArchive = true;
            }
            _archiveView.style.display = !_showPlanner && _showArchive
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _storyView.style.display = !_showPlanner && !_showArchive
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _plannerView.style.display = _showPlanner
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (_showPlanner)
            {
                _plannerPanel.Refresh();
            }
            else if (_showArchive)
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
            string fingerprint = BuildArchiveFingerprint();
            if (string.Equals(
                fingerprint,
                _archiveFingerprint,
                StringComparison.Ordinal))
            {
                return;
            }
            _archiveFingerprint = fingerprint;
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

        private string BuildArchiveFingerprint()
        {
            var value = new StringBuilder();
            value.Append(_selectedMissionId).Append('|');
            for (int index = 0; index < _tracker.Archive.Missions.Count; index++)
            {
                MissionRecord mission = _tracker.Archive.Missions[index];
                if (mission == null)
                {
                    continue;
                }
                value.Append(mission.MissionId).Append(':')
                    .Append(mission.ParentMissionId).Append(':')
                    .Append(mission.Title).Append(':')
                    .Append(mission.Status).Append(':')
                    .Append(mission.MissionKind).Append(':')
                    .Append(mission.LastBody).Append(':')
                    .Append(mission.IsActive).Append(':')
                    .Append(_collapsedMissionIds.Contains(mission.MissionId))
                    .Append('|');
            }
            return value.ToString();
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
            _missionTitle.tooltip = mission.Title;
            _statusChip.text = mission.Status.ToUpperInvariant();
            _kindChip.text = mission.MissionKind.ToUpperInvariant();
            ApplyChipColor(_statusChip, StatusColor(mission.Status));
            ApplyChipColor(_kindChip, new Color32(119, 152, 204, 255));
            string missionMeta = BuildMissionMeta(mission, aggregate);
            _missionMeta.text = missionMeta;
            _missionMeta.tooltip = missionMeta;
            PopulateRelationships(mission, parent, children);

            _reviewBanner.style.display = mission.NeedsReview
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            string reviewReason = BuildReviewReason(mission);
            _reviewText.text = reviewReason;
            _reviewText.tooltip = reviewReason;
            bool hasNote = !string.IsNullOrWhiteSpace(mission.Notes);
            _noteCard.style.display = hasNote ? DisplayStyle.Flex : DisplayStyle.None;
            _noteText.text = hasNote ? CompactSingleLine(mission.Notes) : string.Empty;
            _noteText.tooltip = hasNote ? mission.Notes : string.Empty;
            PopulatePlanContext(mission);

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

        private void PopulatePlanContext(MissionRecord mission)
        {
            MissionPlan plan = FindLinkedPlan(mission);
            _linkedPlanId = plan == null ? null : plan.PlanId;
            _planBanner.style.display = plan == null
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            if (plan == null)
            {
                _planText.text = string.Empty;
                _planText.tooltip = string.Empty;
                return;
            }

            int total = 0;
            int resolved = 0;
            MissionPlanObjective next = null;
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                if (objective == null || objective.Archived)
                {
                    continue;
                }
                total++;
                if (objective.Status == MissionObjectiveStatus.Achieved ||
                    objective.Status == MissionObjectiveStatus.Skipped ||
                    objective.Status == MissionObjectiveStatus.Deviated)
                {
                    resolved++;
                }
                else if (next == null)
                {
                    next = objective;
                }
            }

            int deviations = plan.Deviations == null ? 0 : plan.Deviations.Count;
            string nextText = plan.Status == MissionPlanStatus.Completed
                ? "PLAN COMPLETE"
                : (next == null ? "PLAN RESOLVED" : "NEXT  " + next.Title);
            string text = "PLAN  ·  " + plan.Title + "  ·  " + resolved + "/" +
                total + " resolved  ·  " + nextText;
            if (deviations > 0)
            {
                text += "  ·  " + deviations +
                    (deviations == 1 ? " deviation" : " deviations");
            }
            _planText.text = text;
            _planText.tooltip = text;
        }

        private MissionPlan FindLinkedPlan(MissionRecord mission)
        {
            if (mission == null || _planner == null || _planner.State == null ||
                _planner.State.Plans == null)
            {
                return null;
            }

            MissionPlan best = null;
            for (int index = 0; index < _planner.State.Plans.Count; index++)
            {
                MissionPlan plan = _planner.State.Plans[index];
                if (plan == null || plan.Archived ||
                    plan.Status == MissionPlanStatus.Abandoned ||
                    !PlanReferencesMission(plan, mission))
                {
                    continue;
                }
                if (best == null ||
                    (plan.Status == MissionPlanStatus.Active &&
                     best.Status != MissionPlanStatus.Active) ||
                    (plan.Status == best.Status && string.CompareOrdinal(
                        plan.UpdatedUtc,
                        best.UpdatedUtc) > 0))
                {
                    best = plan;
                }
            }
            return best;
        }

        private bool PlanReferencesMission(MissionPlan plan, MissionRecord mission)
        {
            if (!string.IsNullOrWhiteSpace(plan.CampaignId) &&
                !string.Equals(
                    plan.CampaignId,
                    mission.CampaignId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            string missionRootId = RootMissionId(mission.MissionId);
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot == null || slot.Archived)
                {
                    continue;
                }
                if (MissionAliasMatches(
                    mission.MissionId,
                    missionRootId,
                    slot.BoundMissionId))
                {
                    return true;
                }
                for (int aliasIndex = 0;
                    aliasIndex < slot.MissionIds.Count;
                    aliasIndex++)
                {
                    if (MissionAliasMatches(
                        mission.MissionId,
                        missionRootId,
                        slot.MissionIds[aliasIndex]))
                    {
                        return true;
                    }
                }
                if (VesselAliasMatches(slot.BoundVesselId, mission) ||
                    AnyVesselAliasMatches(slot.VesselIds, mission))
                {
                    return true;
                }
            }
            return false;
        }

        private bool MissionAliasMatches(
            string missionId,
            string missionRootId,
            string aliasMissionId)
        {
            if (string.IsNullOrWhiteSpace(aliasMissionId))
            {
                return false;
            }
            if (string.Equals(missionId, aliasMissionId, StringComparison.Ordinal))
            {
                return true;
            }
            string aliasRootId = RootMissionId(aliasMissionId);
            return !string.IsNullOrWhiteSpace(missionRootId) &&
                string.Equals(missionRootId, aliasRootId, StringComparison.Ordinal);
        }

        private string RootMissionId(string missionId)
        {
            MissionRecord cursor = _tracker.FindById(missionId);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (cursor != null && seen.Add(cursor.MissionId))
            {
                if (string.IsNullOrWhiteSpace(cursor.ParentMissionId))
                {
                    return cursor.MissionId;
                }
                MissionRecord parent = _tracker.FindById(cursor.ParentMissionId);
                if (parent == null)
                {
                    return cursor.MissionId;
                }
                cursor = parent;
            }
            return cursor == null ? missionId : cursor.MissionId;
        }

        private static bool AnyVesselAliasMatches(
            List<string> aliases,
            MissionRecord mission)
        {
            if (aliases == null)
            {
                return false;
            }
            for (int index = 0; index < aliases.Count; index++)
            {
                if (VesselAliasMatches(aliases[index], mission))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool VesselAliasMatches(string vesselId, MissionRecord mission)
        {
            if (string.IsNullOrWhiteSpace(vesselId) || mission == null)
            {
                return false;
            }
            if (string.Equals(vesselId, mission.VesselId, StringComparison.Ordinal))
            {
                return true;
            }
            return ContainsOrdinal(mission.VesselIds, vesselId) ||
                ContainsOrdinal(mission.TrackedVesselIds, vesselId);
        }

        private static bool ContainsOrdinal(List<string> values, string expected)
        {
            if (values == null)
            {
                return false;
            }
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
            _timelineExpansionStates.Clear();

            if (items.Count == 0)
            {
                Label empty = CreateWrappedLabel("The first meaningful mission moment will appear here.");
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 24f;
                _timeline.Add(empty);
                return;
            }

            bool showSourceClocks = HasMultipleTimelineSources(items);
            var renderedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++)
            {
                string eventId = items[index].Event.EventId ?? string.Empty;
                renderedIds.Add(eventId);
                _timeline.Add(CreateTimelineRow(
                    items[index], mission, showSourceClocks, index + 1));
            }
            _expandedTimelineEventIds.RemoveWhere(
                eventId => !renderedIds.Contains(eventId));
        }

        private VisualElement CreateTimelineRow(
            MissionTimelineItem item,
            MissionRecord selected,
            bool showSourceClock,
            int position)
        {
            Color32 accent = CategoryColor(item.Category, item.SourceMission.Status);
            string eventId = item.Event.EventId ?? string.Empty;
            VisualElement row = new VisualElement
            {
                name = "timeline-row-" + eventId,
                focusable = true,
                tabIndex = 0,
                tooltip = "Hover or focus to show mission-leg details"
            };
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0f;
            row.style.marginBottom = 4f;

            Label order = CreateMutedLabel(position.ToString("00"));
            order.tooltip = "Chronological moment " + position;
            order.style.width = 28f;
            order.style.minWidth = 28f;
            order.style.unityTextAlign = TextAnchor.MiddleCenter;
            order.style.fontSize = 10f;
            row.Add(order);

            InvertedCornerBox card = CreatePanel("timeline-" + eventId);
            card.BorderColor = accent;
            card.style.flexGrow = 1f;
            card.style.minWidth = 0f;
            card.style.paddingLeft = 7f;
            card.style.paddingRight = 7f;
            card.style.paddingTop = 4f;
            card.style.paddingBottom = 4f;

            VisualElement summary = CreateActionRow();
            summary.style.height = 30f;
            summary.style.alignItems = Align.Center;

            string clockText = item.IsDerived
                ? "SUMMARY"
                : (showSourceClock ? "LEG T+" : "T+") +
                    FormatDuration(item.Event.FlightTimeSeconds);
            Label time = CreateMutedLabel(clockText);
            time.tooltip = item.IsDerived
                ? "Summary calculated from the archived mission"
                : (showSourceClock
                    ? "Flight clock for mission leg " + item.SourceMission.Title
                    : "Mission flight clock");
            time.style.width = showSourceClock ? 84f : 72f;
            time.style.minWidth = showSourceClock ? 84f : 72f;
            time.style.whiteSpace = WhiteSpace.NoWrap;
            time.style.unityTextAlign = TextAnchor.MiddleLeft;
            time.style.fontSize = item.IsDerived ? 9f : 10f;
            summary.Add(time);

            Label symbol = new Label(item.Symbol);
            symbol.style.width = 24f;
            symbol.style.minWidth = 24f;
            symbol.style.height = 24f;
            symbol.style.marginRight = 7f;
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
            symbol.style.fontSize = 14f;
            summary.Add(symbol);

            Label category = CreateMutedLabel(item.CategoryLabel);
            category.style.color = new StyleColor(accent);
            category.style.unityFontStyleAndWeight = FontStyle.Bold;
            category.style.width = 92f;
            category.style.minWidth = 92f;
            category.style.marginRight = 7f;
            category.style.fontSize = 10f;
            category.style.whiteSpace = WhiteSpace.NoWrap;
            summary.Add(category);

            Label title = new Label(item.Event.Title);
            title.tooltip = item.Event.Title;
            title.style.flexGrow = 1f;
            title.style.flexShrink = 1f;
            title.style.minWidth = 0f;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13f;
            summary.Add(title);

            Label chevron = CreateMutedLabel("▾");
            chevron.tooltip = "Show details";
            chevron.style.width = 18f;
            chevron.style.minWidth = 18f;
            chevron.style.unityTextAlign = TextAnchor.MiddleCenter;
            summary.Add(chevron);
            card.Add(summary);

            VisualElement details = new VisualElement
            {
                name = "timeline-details-" + eventId
            };
            details.style.marginLeft = showSourceClock ? 115f : 103f;
            details.style.marginTop = 2f;
            details.style.paddingTop = 4f;
            details.style.borderTopWidth = 1f;
            details.style.borderTopColor =
                new StyleColor(new Color32(80, 84, 96, 180));

            VisualElement sourceRow = new VisualElement();
            sourceRow.style.flexDirection = FlexDirection.Row;
            sourceRow.style.alignItems = Align.Center;
            Label sourcePrefix = CreateMutedLabel("MISSION LEG  ·  ");
            sourcePrefix.style.whiteSpace = WhiteSpace.NoWrap;
            sourceRow.Add(sourcePrefix);
            if (!ReferenceEquals(item.SourceMission, selected))
            {
                Button source = CreateLinkButton(
                    item.SourceMission.Title,
                    delegate { OpenMission(item.SourceMission); });
                source.tooltip = "Open this mission leg";
                source.style.flexGrow = 1f;
                source.style.flexShrink = 1f;
                source.style.minWidth = 0f;
                source.style.unityTextAlign = TextAnchor.MiddleLeft;
                sourceRow.Add(source);
            }
            else
            {
                Label sourceName = CreateWrappedLabel(item.SourceMission.Title);
                sourceName.style.flexGrow = 1f;
                sourceName.style.flexShrink = 1f;
                sourceName.style.minWidth = 0f;
                sourceName.style.unityFontStyleAndWeight = FontStyle.Bold;
                sourceRow.Add(sourceName);
            }
            details.Add(sourceRow);

            string context = BuildEventContext(item);
            var detailValues = new List<string>();
            detailValues.Add(item.IsDerived
                ? "Calculated archive summary"
                : (showSourceClock ? "Leg clock " : "Flight clock ") +
                    "T+" + FormatDuration(item.Event.FlightTimeSeconds));
            if (!string.IsNullOrWhiteSpace(context))
            {
                detailValues.Add(context);
            }
            if (item.Event.VesselIds != null && item.Event.VesselIds.Count > 1)
            {
                detailValues.Add(item.Event.VesselIds.Count + " vessels");
            }
            Label metadata = CreateMutedLabel(
                string.Join("  ·  ", detailValues.ToArray()));
            metadata.style.marginTop = 2f;
            details.Add(metadata);
            card.Add(details);
            row.Add(card);

            var state = new TimelineExpansionState
            {
                Row = row,
                Details = details,
                Chevron = chevron
            };
            _timelineExpansionStates[eventId] = state;
            row.RegisterCallback<PointerEnterEvent>(delegate(PointerEnterEvent unused)
            {
                state.Hovered = true;
                ApplyTimelineExpansion(eventId, state);
            });
            row.RegisterCallback<PointerLeaveEvent>(delegate(PointerLeaveEvent unused)
            {
                state.Hovered = false;
                ApplyTimelineExpansion(eventId, state);
            });
            row.RegisterCallback<FocusInEvent>(delegate(FocusInEvent unused)
            {
                state.Focused = true;
                ApplyTimelineExpansion(eventId, state);
            });
            row.RegisterCallback<FocusOutEvent>(delegate(FocusOutEvent unused)
            {
                state.Focused = false;
                ApplyTimelineExpansion(eventId, state);
            });
            ApplyTimelineExpansion(eventId, state);
            return row;
        }

        private void ApplyTimelineExpansion(string eventId, TimelineExpansionState state)
        {
            bool expanded = _expandedTimelineEventIds.Contains(eventId) ||
                state.Hovered || state.Focused;
            state.Details.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            state.Chevron.text = expanded ? "▴" : "▾";
            state.Chevron.tooltip = expanded ? "Hide details" : "Show details";
            state.Row.style.marginBottom = expanded ? 7f : 4f;
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
            string fingerprint = BuildManageFingerprint(selected, parent);
            if (string.Equals(
                fingerprint,
                _manageFingerprint,
                StringComparison.Ordinal))
            {
                return;
            }
            _manageFingerprint = fingerprint;
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

        private string BuildManageFingerprint(
            MissionRecord selected,
            MissionRecord parent)
        {
            MissionRecord current = _tracker.GetCurrent();
            var value = new StringBuilder();
            AppendManageMission(value, selected);
            AppendManageMission(value, parent);
            AppendManageMission(value, current);
            value.Append(_tracker.ActiveVesselId).Append('|')
                .Append(_pendingAction).Append('|')
                .Append(_pendingSelectedMissionId).Append('|')
                .Append(_pendingCurrentMissionId).Append('|')
                .Append(_pendingVesselId).Append('|')
                .Append(_pendingPrompt).Append('|')
                .Append(_feedback);
            return value.ToString();
        }

        private static void AppendManageMission(
            StringBuilder value,
            MissionRecord mission)
        {
            if (mission == null)
            {
                value.Append("<none>|");
                return;
            }
            value.Append(mission.MissionId).Append(':')
                .Append(mission.ParentMissionId).Append(':')
                .Append(mission.Status).Append(':')
                .Append(mission.CampaignId).Append(':')
                .Append(mission.VesselId).Append(':')
                .Append(mission.TrackedVesselIds.Count).Append(':')
                .Append(mission.NeedsReview).Append('|');
        }

        private void ShowArchive()
        {
            _showPlanner = false;
            _showArchive = true;
            _plannerPanel.Hide();
            _showEditor = false;
            _showManage = false;
            Refresh();
        }

        private void ShowStory()
        {
            _showPlanner = false;
            _showArchive = false;
            _plannerPanel.Hide();
            _showEditor = false;
            _showManage = false;
            Refresh();
        }

        private void ShowCurrentStory()
        {
            _followCurrentMission = true;
            Select(_tracker.GetCurrent() ?? _tracker.GetLatest());
            ShowStory();
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
            _manageFingerprint = null;
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
            _showEditor = false;
            _showManage = false;
            _manageFingerprint = null;
            _timelineFingerprint = null;
            _expandedTimelineEventIds.Clear();
            _timelineExpansionStates.Clear();
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
            if (!string.IsNullOrWhiteSpace(mission.VesselName) &&
                !string.Equals(
                    mission.VesselName.Trim(),
                    (mission.Title ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
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

        private static string BuildReviewReason(MissionRecord mission)
        {
            MissionEvent latest = null;
            for (int index = 0; index < mission.Events.Count; index++)
            {
                MissionEvent candidate = mission.Events[index];
                if (candidate == null || !string.Equals(
                    candidate.Kind, "lineage_needs_review", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (latest == null || string.Compare(
                    candidate.RecordedUtc ?? string.Empty,
                    latest.RecordedUtc ?? string.Empty,
                    StringComparison.Ordinal) > 0)
                {
                    latest = candidate;
                }
            }
            string reason = latest == null || string.IsNullOrWhiteSpace(latest.Title)
                ? "Mission relationship needs review"
                : latest.Title.Trim();
            return "REVIEW  ·  " + reason;
        }

        private static string CompactSingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            string compact = text.Replace('\r', ' ').Replace('\n', ' ');
            while (compact.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                compact = compact.Replace("  ", " ");
            }
            return compact.Trim();
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
