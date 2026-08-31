using System;
using System.Collections.Generic;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReduxMissionLog
{
    // Native UITK workspace embedded by MissionLogWindow. It deliberately owns
    // presentation only; MissionPlanner remains the authority for every edit.
    internal sealed class MissionPlannerPanel
    {
        private const string NoSavedVehicle = "Choose a saved vehicle";
        private const string AnyVessel = "Any planned vessel";

        private readonly MissionPlanner _planner;
        private readonly MissionTracker _tracker;
        private readonly MissionPlanLaunchService _launchService;
        private readonly Action<string> _logError;
        private readonly Action<string> _info;

        private readonly VisualElement _planList;
        private readonly Label _feedback;
        private readonly VisualElement _emptyState;
        private readonly ScrollView _detailScroll;
        private readonly VisualElement _detail;
        private readonly InvertedCornerBox _createEditor;
        private readonly TextField _createTitle;
        private readonly TextField _createNotes;

        private readonly InvertedCornerBox _planEditor;
        private readonly TextField _planTitle;
        private readonly TextField _planNotes;

        private readonly InvertedCornerBox _slotEditor;
        private readonly TextField _slotName;
        private readonly TextField _slotRole;
        private readonly Toggle _slotRequired;

        private readonly InvertedCornerBox _objectiveEditor;
        private readonly DropdownField _objectiveKind;
        private readonly TextField _objectiveTitle;
        private readonly TextField _objectiveNotes;
        private readonly DropdownField _objectiveSlot;
        private readonly DropdownField _objectiveRelatedSlot;
        private readonly TextField _objectiveBody;
        private readonly TextField _objectiveSituation;
        private readonly TextField _objectiveMatch;
        private readonly Toggle _objectiveOptional;

        private readonly List<SavedVehicleInfo> _savedVehicles =
            new List<SavedVehicleInfo>();
        private readonly Dictionary<string, string> _objectiveSlotIds =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private string _selectedPlanId;
        private string _editingSlotId;
        private string _editingObjectiveId;
        private bool _abandonArmed;
        private long _lastRenderedRevision = -1;
        private int _savedVehiclesRevision;
        private int _lastRenderedSavedVehiclesRevision = -1;
        private string _lastRenderedPlanId;
        private bool _lastRenderedAbandonArmed;

        public MissionPlannerPanel(
            MissionPlanner planner,
            MissionTracker tracker,
            MissionPlanLaunchService launchService,
            Action<string> logError,
            Action<string> info)
        {
            if (planner == null)
            {
                throw new ArgumentNullException("planner");
            }

            _planner = planner;
            _tracker = tracker;
            _launchService = launchService;
            _logError = logError;
            _info = info;

            Root = new VisualElement { name = "mission-planner-view" };
            Root.AddToClassList("oab-window-body");
            Root.style.flexGrow = 1f;
            Root.style.minHeight = 0f;
            Root.style.minWidth = 0f;
            Root.style.paddingLeft = 0f;
            Root.style.paddingRight = 0f;
            Root.style.paddingTop = 0f;
            Root.style.paddingBottom = 0f;
            Root.style.display = DisplayStyle.None;

            VisualElement toolbar = CreateActionRow();
            toolbar.style.alignItems = Align.Center;
            VisualElement heading = new VisualElement();
            heading.style.flexGrow = 1f;
            heading.style.minWidth = 0f;
            heading.Add(CreateHeading("MISSION PLANNER", 18f));
            Label subtitle = CreateMutedLabel(
                "Plan the route. Mission Log records what actually happens.");
            subtitle.style.whiteSpace = WhiteSpace.NoWrap;
            subtitle.style.overflow = Overflow.Hidden;
            subtitle.style.textOverflow = TextOverflow.Ellipsis;
            heading.Add(subtitle);
            toolbar.Add(heading);
            Button create = CreateButton("New plan", "Create a mission plan", ShowCreateEditor);
            create.style.flexGrow = 0f;
            create.style.width = 112f;
            toolbar.Add(create);
            Root.Add(toolbar);

            _feedback = CreateMutedLabel(string.Empty);
            _feedback.name = "mission-planner-feedback";
            _feedback.style.display = DisplayStyle.None;
            _feedback.style.marginTop = 4f;
            _feedback.style.paddingLeft = 7f;
            _feedback.style.paddingRight = 7f;
            _feedback.style.paddingTop = 4f;
            _feedback.style.paddingBottom = 4f;
            _feedback.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(_feedback);

            _createEditor = CreatePanel("mission-plan-create-editor");
            _createEditor.BorderColor = PlannerBlue;
            _createEditor.style.marginTop = 6f;
            _createEditor.Add(CreateHeading("NEW PLAN", 14f));
            _createTitle = CreateTextField("Plan name", false);
            _createNotes = CreateTextField("Short intent or constraints", true);
            _createEditor.Add(_createTitle);
            _createEditor.Add(_createNotes);
            VisualElement createActions = CreateActionRow();
            createActions.Add(CreateButton("Cancel", "Close without creating a plan", HideEditors));
            createActions.Add(CreateButton("Create", "Create this draft plan", CreatePlan));
            _createEditor.Add(createActions);
            Root.Add(_createEditor);

            VisualElement workspace = new VisualElement { name = "mission-planner-workspace" };
            workspace.style.flexDirection = FlexDirection.Row;
            workspace.style.flexGrow = 1f;
            workspace.style.minHeight = 0f;
            workspace.style.minWidth = 0f;
            workspace.style.marginTop = 6f;
            Root.Add(workspace);

            InvertedCornerBox indexPanel = CreatePanel("mission-plan-index");
            indexPanel.style.width = 222f;
            indexPanel.style.minWidth = 190f;
            indexPanel.style.flexShrink = 0f;
            indexPanel.style.marginRight = 7f;
            Label indexHeading = CreateMutedLabel("PLANS");
            indexHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
            indexPanel.Add(indexHeading);
            ScrollView indexScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "mission-plan-index-scroll"
            };
            indexScroll.style.flexGrow = 1f;
            indexScroll.style.minHeight = 0f;
            indexScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            indexPanel.Add(indexScroll);
            _planList = indexScroll.contentContainer;
            workspace.Add(indexPanel);

            InvertedCornerBox detailPanel = CreatePanel("mission-plan-detail-panel");
            detailPanel.style.flexGrow = 1f;
            detailPanel.style.minWidth = 0f;
            detailPanel.style.minHeight = 0f;
            workspace.Add(detailPanel);

            _emptyState = new VisualElement { name = "mission-plan-empty" };
            _emptyState.style.flexGrow = 1f;
            _emptyState.style.justifyContent = Justify.Center;
            _emptyState.style.alignItems = Align.Center;
            Label emptyTitle = CreateHeading("NO PLAN SELECTED", 16f);
            Label emptyHelp = CreateMutedLabel(
                "Create a plan, add its launches and milestones, then activate it before flight.");
            emptyHelp.style.whiteSpace = WhiteSpace.Normal;
            emptyHelp.style.unityTextAlign = TextAnchor.MiddleCenter;
            emptyHelp.style.maxWidth = 390f;
            _emptyState.Add(emptyTitle);
            _emptyState.Add(emptyHelp);
            detailPanel.Add(_emptyState);

            _detailScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "mission-plan-detail-scroll"
            };
            _detailScroll.style.flexGrow = 1f;
            _detailScroll.style.minHeight = 0f;
            _detailScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            detailPanel.Add(_detailScroll);
            _detail = _detailScroll.contentContainer;

            _planEditor = CreatePanel("mission-plan-editor");
            _planEditor.BorderColor = PlannerBlue;
            _planEditor.Add(CreateHeading("EDIT PLAN", 14f));
            _planTitle = CreateTextField("Plan name", false);
            _planNotes = CreateTextField("Intent or constraints", true);
            _planEditor.Add(_planTitle);
            _planEditor.Add(_planNotes);
            VisualElement planActions = CreateActionRow();
            planActions.Add(CreateButton("Cancel", "Discard these edits", HideEditors));
            planActions.Add(CreateButton("Save", "Save the plan summary", SavePlan));
            _planEditor.Add(planActions);

            _slotEditor = CreatePanel("mission-plan-slot-editor");
            _slotEditor.BorderColor = PlannerPurple;
            _slotEditor.Add(CreateHeading("PLANNED VESSEL", 14f));
            _slotName = CreateTextField("Callsign or slot name", false);
            _slotRole = CreateTextField("Role, for example orbiter or lander", false);
            _slotRequired = new Toggle("Required for this plan") { value = true };
            _slotRequired.AddToClassList("ui-sound-toggle");
            _slotEditor.Add(_slotName);
            _slotEditor.Add(_slotRole);
            _slotEditor.Add(_slotRequired);
            VisualElement slotActions = CreateActionRow();
            slotActions.Add(CreateButton("Cancel", "Discard this vessel edit", HideEditors));
            slotActions.Add(CreateButton("Save vessel", "Save this planned vessel", SaveSlot));
            _slotEditor.Add(slotActions);

            List<string> kinds = new List<string>(Enum.GetNames(typeof(MissionObjectiveKind)));
            _objectiveEditor = CreatePanel("mission-plan-objective-editor");
            _objectiveEditor.BorderColor = PlannerGold;
            _objectiveEditor.Add(CreateHeading("MISSION STEP", 14f));
            _objectiveKind = new DropdownField("Type (Body means an SOI/body visit)", kinds, 0);
            _objectiveKind.AddToClassList("oab-dropdown-field");
            _objectiveTitle = CreateTextField("What should happen", false);
            _objectiveNotes = CreateTextField("Optional note", true);
            _objectiveSlot = new DropdownField(
                "Vessel",
                new List<string> { AnyVessel },
                0);
            _objectiveSlot.AddToClassList("oab-dropdown-field");
            _objectiveRelatedSlot = new DropdownField(
                "Other vessel (for docking)",
                new List<string> { AnyVessel },
                0);
            _objectiveRelatedSlot.AddToClassList("oab-dropdown-field");
            _objectiveBody = CreateTextField("Target body, if relevant", false);
            _objectiveSituation = CreateTextField(
                "Target state, for example Orbiting or Landed",
                false);
            _objectiveMatch = CreateTextField(
                "Other vessel, destination, or custom match",
                false);
            _objectiveOptional = new Toggle("Optional step");
            _objectiveOptional.AddToClassList("ui-sound-toggle");
            _objectiveKind.RegisterValueChangedCallback(delegate(ChangeEvent<string> change)
            {
                if (!string.IsNullOrWhiteSpace(_editingObjectiveId))
                {
                    return;
                }
                MissionObjectiveKind previous;
                MissionObjectiveKind next;
                if (Enum.TryParse(change.previousValue, true, out previous) &&
                    Enum.TryParse(change.newValue, true, out next) &&
                    (string.IsNullOrWhiteSpace(_objectiveTitle.value) ||
                     string.Equals(
                         _objectiveTitle.value,
                         DefaultObjectiveTitle(previous),
                         StringComparison.Ordinal)))
                {
                    _objectiveTitle.SetValueWithoutNotify(DefaultObjectiveTitle(next));
                }
                if (Enum.TryParse(change.newValue, true, out next))
                {
                    UpdateObjectiveEditorVisibility(next);
                }
            });
            _objectiveEditor.Add(_objectiveKind);
            _objectiveEditor.Add(_objectiveTitle);
            _objectiveEditor.Add(_objectiveNotes);
            _objectiveEditor.Add(_objectiveSlot);
            _objectiveEditor.Add(_objectiveRelatedSlot);
            _objectiveEditor.Add(_objectiveBody);
            _objectiveEditor.Add(_objectiveSituation);
            _objectiveEditor.Add(_objectiveMatch);
            _objectiveEditor.Add(_objectiveOptional);
            VisualElement objectiveActions = CreateActionRow();
            objectiveActions.Add(CreateButton("Cancel", "Discard this step edit", HideEditors));
            objectiveActions.Add(CreateButton("Save step", "Save this planned step", SaveObjective));
            _objectiveEditor.Add(objectiveActions);
            UpdateObjectiveEditorVisibility(MissionObjectiveKind.Launch);

            HideEditors();
            Refresh();
        }

        public VisualElement Root { get; private set; }
        public string SelectedPlanId { get { return _selectedPlanId ?? string.Empty; } }
        public string SelectedPlanStatus { get; private set; }
        public string SelectedPlanProgress { get; private set; }
        public int SelectedPlanDeviationCount { get; private set; }
        public int RenderedPlanCount { get; private set; }
        public int SavedVehicleCount { get { return _savedVehicles.Count; } }
        public int RenderedVesselCount { get; private set; }
        public int RenderedObjectiveCount { get; private set; }
        public int RenderedDeviationCount { get; private set; }
        public bool CreateEditorVisible
        {
            get { return IsDisplayed(_createEditor); }
        }
        public bool PlanEditorVisible
        {
            get { return IsDisplayed(_planEditor); }
        }
        public bool VesselEditorVisible
        {
            get { return IsDisplayed(_slotEditor); }
        }
        public bool ObjectiveEditorVisible
        {
            get { return IsDisplayed(_objectiveEditor); }
        }

        public void Open()
        {
            if (_savedVehicles.Count == 0)
            {
                LoadSavedVehicles();
            }
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Open(string planId)
        {
            SelectPlan(planId);
            Open();
        }

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
        }

        public bool SelectPlan(string planId)
        {
            MissionPlan plan = FindPlan(planId);
            if (plan == null)
            {
                return false;
            }
            _selectedPlanId = plan.PlanId;
            _abandonArmed = false;
            HideEditors();
            Refresh();
            return true;
        }

        // Deterministic review helpers let TestHarness drive meaningful UI state
        // without depending on screen coordinates or UI Toolkit internals.
        public MissionPlan CreatePlanForReview(string title, string notes)
        {
            MissionRecord mission = _tracker == null
                ? null
                : (_tracker.GetCurrent() ?? _tracker.GetLatest());
            MissionPlan plan = _planner.CreatePlan(
                mission == null ? string.Empty : mission.CampaignId,
                title,
                notes);
            _selectedPlanId = plan.PlanId;
            HideEditors();
            Refresh();
            return plan;
        }

        public void ShowCreateEditorForReview()
        {
            ShowCreateEditor();
        }

        public bool ShowPlanEditorForReview()
        {
            if (SelectedPlan() == null)
            {
                return false;
            }
            ShowPlanEditor();
            return true;
        }

        public bool ShowVesselEditorForReview(string slotId)
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || (!string.IsNullOrWhiteSpace(slotId) &&
                FindSlot(plan, slotId) == null))
            {
                return false;
            }
            ShowSlotEditor(slotId);
            return true;
        }

        public bool ShowObjectiveEditorForReview(string objectiveId)
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || (!string.IsNullOrWhiteSpace(objectiveId) &&
                FindObjective(plan, objectiveId) == null))
            {
                return false;
            }
            ShowObjectiveEditor(objectiveId);
            return true;
        }

        public bool SelectSavedVehicleForReview(string slotId, string vehicleKey)
        {
            MissionPlan plan = SelectedPlan();
            MissionPlanVesselSlot slot = FindSlot(plan, slotId);
            SavedVehicleInfo vehicle = FindSavedVehicle(vehicleKey);
            if (plan == null || slot == null || vehicle == null)
            {
                return false;
            }
            SelectSavedVehicle(plan, slot, vehicle);
            return true;
        }

        public bool LaunchSlotForReview(string slotId)
        {
            MissionPlan plan = SelectedPlan();
            MissionPlanVesselSlot slot = FindSlot(plan, slotId);
            return plan != null && slot != null && LaunchSlot(plan, slot);
        }

        public bool BindSlotToCurrentForReview(string slotId)
        {
            MissionPlan plan = SelectedPlan();
            MissionPlanVesselSlot slot = FindSlot(plan, slotId);
            return plan != null && slot != null && BindCurrent(plan, slot);
        }

        public bool MoveObjectiveForReview(string objectiveId, int delta)
        {
            MissionPlan plan = SelectedPlan();
            MissionPlanObjective objective = FindObjective(plan, objectiveId);
            if (plan == null || objective == null)
            {
                return false;
            }
            int current = ActiveObjectiveIndex(plan, objective);
            int target = current + delta;
            if (current < 0 || target < 0 || target >= ActiveObjectiveCount(plan))
            {
                return false;
            }
            return TryMutation(
                delegate { _planner.ReorderObjective(plan.PlanId, objective.ObjectiveId, target); },
                "Could not reorder that mission step.");
        }

        public bool ResolveObjectiveForReview(string objectiveId, string resolution)
        {
            MissionPlan plan = SelectedPlan();
            MissionPlanObjective objective = FindObjective(plan, objectiveId);
            if (plan == null || objective == null || string.IsNullOrWhiteSpace(resolution))
            {
                return false;
            }
            if (string.Equals(resolution, "match", StringComparison.OrdinalIgnoreCase))
            {
                return TryMutation(
                    delegate { _planner.ManuallyMatchObjective(plan.PlanId, objective.ObjectiveId); },
                    "Could not match that mission step.");
            }
            if (string.Equals(resolution, "skip", StringComparison.OrdinalIgnoreCase))
            {
                return TryMutation(
                    delegate { _planner.SkipObjective(plan.PlanId, objective.ObjectiveId); },
                    "Could not skip that mission step.");
            }
            if (string.Equals(resolution, "deviation", StringComparison.OrdinalIgnoreCase))
            {
                return TryMutation(
                    delegate { _planner.MarkObjectiveDeviated(plan.PlanId, objective.ObjectiveId); },
                    "Could not mark that deviation.");
            }
            if (string.Equals(resolution, "clear", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resolution, "correct", StringComparison.OrdinalIgnoreCase))
            {
                return TryMutation(
                    delegate { _planner.ClearManualResolution(plan.PlanId, objective.ObjectiveId); },
                    "Could not clear that manual resolution.");
            }
            return false;
        }

        public void RefreshSavedVehicles()
        {
            _savedVehicles.Clear();
            LoadSavedVehicles();
            Refresh();
        }

        private void LoadSavedVehicles()
        {
            if (_launchService != null)
            {
                try
                {
                    _savedVehicles.AddRange(_launchService.GetSavedVehicles());
                }
                catch (Exception error)
                {
                    LogError("Could not refresh saved vehicles: " + error.Message);
                }
            }
            _savedVehiclesRevision++;
        }

        public void Refresh()
        {
            EnsureSelectedPlan();
            string selectedPlanId = _selectedPlanId ?? string.Empty;
            if (_lastRenderedRevision == _planner.Revision &&
                _lastRenderedSavedVehiclesRevision == _savedVehiclesRevision &&
                string.Equals(
                    _lastRenderedPlanId,
                    selectedPlanId,
                    StringComparison.Ordinal) &&
                _lastRenderedAbandonArmed == _abandonArmed)
            {
                return;
            }

            // Automatic observation can update progress while the player is
            // typing. Defer the structural rebuild until the editor closes so
            // focus and draft text are never discarded by the 4 Hz UI refresh.
            if (CreateEditorVisible || PlanEditorVisible ||
                VesselEditorVisible || ObjectiveEditorVisible)
            {
                return;
            }

            RenderPlanList();
            RenderSelectedPlan();
            _lastRenderedRevision = _planner.Revision;
            _lastRenderedSavedVehiclesRevision = _savedVehiclesRevision;
            _lastRenderedPlanId = selectedPlanId;
            _lastRenderedAbandonArmed = _abandonArmed;
        }

        private void EnsureSelectedPlan()
        {
            if (FindPlan(_selectedPlanId) != null)
            {
                return;
            }

            _selectedPlanId = string.Empty;
            List<MissionPlan> plans = OrderedPlans();
            for (int index = 0; index < plans.Count; index++)
            {
                if (!plans[index].Archived)
                {
                    _selectedPlanId = plans[index].PlanId;
                    break;
                }
            }
        }

        private void RenderPlanList()
        {
            _planList.Clear();
            RenderedPlanCount = 0;
            List<MissionPlan> plans = OrderedPlans();
            for (int index = 0; index < plans.Count; index++)
            {
                MissionPlan plan = plans[index];
                RenderedPlanCount++;
                int achieved;
                int resolved;
                int total;
                ProgressCounts(plan, out achieved, out resolved, out total);
                string progress = total == 0
                    ? "No steps"
                    : achieved + "/" + total + " achieved";
                Button item = new Button(delegate { SelectPlan(plan.PlanId); })
                {
                    name = "mission-plan-index-" + SafeName(plan.PlanId),
                    tooltip = FirstNonEmpty(plan.Notes, plan.Title)
                };
                item.AddToClassList("ui-sound-button");
                item.style.height = 56f;
                item.style.marginTop = 3f;
                item.style.paddingLeft = 10f;
                item.style.paddingRight = 8f;
                item.style.paddingTop = 5f;
                item.style.paddingBottom = 5f;
                item.style.flexDirection = FlexDirection.Column;
                item.style.alignItems = Align.FlexStart;
                item.style.justifyContent = Justify.Center;
                item.style.overflow = Overflow.Hidden;

                Label itemTitle = new Label(FirstNonEmpty(plan.Title, "Untitled plan"));
                itemTitle.pickingMode = PickingMode.Ignore;
                itemTitle.style.width = Length.Percent(100f);
                itemTitle.style.whiteSpace = WhiteSpace.NoWrap;
                itemTitle.style.overflow = Overflow.Hidden;
                itemTitle.style.textOverflow = TextOverflow.Ellipsis;
                itemTitle.style.unityFontStyleAndWeight =
                    string.Equals(plan.PlanId, _selectedPlanId, StringComparison.Ordinal)
                        ? FontStyle.Bold
                        : FontStyle.Normal;
                item.Add(itemTitle);

                Label itemSummary = CreateMutedLabel(
                    (plan.Archived ? "Archived  ·  " : string.Empty) +
                    plan.Status + "  ·  " + progress);
                itemSummary.pickingMode = PickingMode.Ignore;
                itemSummary.style.width = Length.Percent(100f);
                itemSummary.style.whiteSpace = WhiteSpace.NoWrap;
                itemSummary.style.overflow = Overflow.Hidden;
                itemSummary.style.textOverflow = TextOverflow.Ellipsis;
                item.Add(itemSummary);
                if (string.Equals(plan.PlanId, _selectedPlanId, StringComparison.Ordinal))
                {
                    item.style.borderLeftWidth = 3f;
                    item.style.borderLeftColor = new StyleColor(StatusColor(plan.Status));
                }
                _planList.Add(item);
            }

            if (RenderedPlanCount == 0)
            {
                Label none = CreateMutedLabel("No plans yet.");
                none.style.marginTop = 8f;
                _planList.Add(none);
            }
        }

        private void RenderSelectedPlan()
        {
            _detail.Clear();
            RenderedVesselCount = 0;
            RenderedObjectiveCount = 0;
            RenderedDeviationCount = 0;
            SelectedPlanStatus = "none";
            SelectedPlanProgress = "0 / 0";
            SelectedPlanDeviationCount = 0;

            MissionPlan plan = SelectedPlan();
            _emptyState.style.display = plan == null ? DisplayStyle.Flex : DisplayStyle.None;
            _detailScroll.style.display = plan == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (plan == null)
            {
                return;
            }

            SelectedPlanStatus = plan.Status.ToString();
            SelectedPlanDeviationCount = plan.Deviations == null ? 0 : plan.Deviations.Count;
            int achieved;
            int resolved;
            int total;
            ProgressCounts(plan, out achieved, out resolved, out total);
            SelectedPlanProgress = achieved + " / " + total + " achieved; " +
                resolved + " / " + total + " resolved";

            RenderHeader(plan, achieved, resolved, total);
            _detail.Add(_planEditor);
            _detail.Add(_slotEditor);
            _detail.Add(_objectiveEditor);
            if (SelectedPlanDeviationCount > 0)
            {
                RenderDeviations(plan);
            }
            RenderSlots(plan);
            RenderObjectives(plan);
            if (SelectedPlanDeviationCount == 0)
            {
                RenderDeviations(plan);
            }
        }

        private void RenderHeader(MissionPlan plan, int achieved, int resolved, int total)
        {
            InvertedCornerBox header = CreatePanel("mission-plan-summary");
            header.style.marginBottom = 6f;
            VisualElement titleRow = CreateActionRow();
            titleRow.style.alignItems = Align.Center;
            Label title = CreateHeading(FirstNonEmpty(plan.Title, "Untitled plan"), 18f);
            title.style.flexGrow = 1f;
            title.style.flexShrink = 1f;
            title.style.minWidth = 0f;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.tooltip = plan.Title;
            Label status = CreateChip(plan.Status.ToString(), StatusColor(plan.Status));
            titleRow.Add(title);
            titleRow.Add(status);
            header.Add(titleRow);

            Label notes = CreateMutedLabel(FirstNonEmpty(plan.Notes, "No plan note."));
            notes.style.marginTop = 2f;
            notes.style.whiteSpace = WhiteSpace.NoWrap;
            notes.style.overflow = Overflow.Hidden;
            notes.style.textOverflow = TextOverflow.Ellipsis;
            notes.tooltip = plan.Notes;
            header.Add(notes);

            VisualElement progressRow = CreateActionRow();
            progressRow.style.alignItems = Align.Center;
            Label progress = CreateWrappedLabel(total == 0
                ? "Add the first mission step to make this plan actionable."
                : achieved + " of " + total + " achieved  ·  " +
                    resolved + " resolved  ·  " +
                    SelectedPlanDeviationCount + " deviations");
            progress.style.flexGrow = 1f;
            progress.style.unityFontStyleAndWeight = FontStyle.Bold;
            progress.style.color = new StyleColor(total > 0 && achieved == total
                ? PlannerGreen
                : PlannerBlue);
            progressRow.Add(progress);
            header.Add(progressRow);

            MissionPlanObjective current = CurrentObjective(plan);
            Label next = CreateMutedLabel(current == null
                ? (plan.Status == MissionPlanStatus.Completed
                    ? "PLAN RESOLVED"
                    : "NEXT  ·  No unresolved step")
                : "NEXT  ·  " + FirstNonEmpty(current.Title, current.Kind.ToString()));
            next.style.marginTop = 2f;
            next.style.whiteSpace = WhiteSpace.NoWrap;
            next.style.overflow = Overflow.Hidden;
            next.style.textOverflow = TextOverflow.Ellipsis;
            next.tooltip = current == null ? next.text : ObjectiveSummary(plan, current);
            header.Add(next);

            VisualElement actions = CreateActionRow();
            bool ended = IsEnded(plan);
            bool launchLocked = IsLaunchLocked();
            if (!ended)
            {
                Button edit = CreateMiniButton("Edit", "Edit the plan name and note", ShowPlanEditor);
                edit.SetEnabled(!launchLocked);
                actions.Add(edit);
            }
            if (plan.Status == MissionPlanStatus.Draft)
            {
                Button activate = CreateMiniButton(
                    "Activate",
                    "Start comparing observed flight events with this plan",
                    delegate
                    {
                        TryMutation(
                            delegate { _planner.ActivatePlan(plan.PlanId); },
                            "Could not activate this plan.");
                    });
                activate.SetEnabled(ActiveObjectiveCount(plan) > 0 && !launchLocked);
                actions.Add(activate);
            }
            if (!ended)
            {
                Button abandon = CreateMiniButton(
                    _abandonArmed ? "Confirm abandon" : "Abandon",
                    _abandonArmed
                        ? "Confirm that this plan should end without completion"
                        : "Arm the abandon action",
                    delegate
                    {
                        if (!_abandonArmed)
                        {
                            _abandonArmed = true;
                            Refresh();
                            return;
                        }
                        TryMutation(
                            delegate { _planner.AbandonPlan(plan.PlanId); },
                            "Could not abandon this plan.");
                    });
                abandon.SetEnabled(!launchLocked);
                actions.Add(abandon);
            }
            else
            {
                Button archive = CreateMiniButton(
                    plan.Archived ? "Restore" : "Archive",
                    plan.Archived ? "Return this plan to the planner" : "Hide this ended plan",
                    delegate
                    {
                        TryMutation(
                            delegate { _planner.SetPlanArchived(plan.PlanId, !plan.Archived); },
                            "Could not change this plan's archive state.");
                    });
                archive.SetEnabled(!launchLocked);
                actions.Add(archive);
            }
            header.Add(actions);
            _detail.Add(header);
        }

        private void RenderSlots(MissionPlan plan)
        {
            bool ended = IsEnded(plan);
            bool launchLocked = IsLaunchLocked();
            VisualElement heading = CreateSectionHeading(
                "PLANNED VESSELS",
                "Saved craft launch through KSP's normal launch flow.");
            if (!ended)
            {
                Button add = CreateMiniButton(
                    "Add vessel",
                    "Add a launch, orbiter, lander, relay, or other vessel slot",
                    delegate { ShowSlotEditor(null); });
                add.SetEnabled(!launchLocked);
                heading.Add(add);
                Button refresh = CreateMiniButton(
                    "Refresh craft",
                    "Read the latest saved vehicles from the VAB and SPH",
                    RefreshSavedVehicles);
                refresh.SetEnabled(!launchLocked);
                heading.Add(refresh);
            }
            _detail.Add(heading);

            List<MissionPlanVesselSlot> slots = ActiveSlots(plan);
            for (int index = 0; index < slots.Count; index++)
            {
                MissionPlanVesselSlot slot = slots[index];
                int slotIndex = index;
                RenderedVesselCount++;
                InvertedCornerBox card = CreatePanel(
                    "mission-plan-slot-" + SafeName(slot.SlotId));
                card.style.paddingTop = 5f;
                card.style.paddingBottom = 5f;
                card.style.marginBottom = 4f;

                VisualElement top = CreateActionRow();
                top.style.alignItems = Align.Center;
                Label order = CreateMutedLabel((index + 1).ToString("00"));
                order.style.width = 28f;
                top.Add(order);
                Label name = CreateWrappedLabel(FirstNonEmpty(slot.Name, "Planned vessel"));
                name.style.flexGrow = 1f;
                name.style.flexShrink = 1f;
                name.style.minWidth = 0f;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                name.tooltip = FirstNonEmpty(slot.Role, slot.Name);
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                top.Add(name);
                if (!string.IsNullOrWhiteSpace(slot.Role))
                {
                    Label role = CreateChip(slot.Role, PlannerPurple);
                    top.Add(role);
                }
                string linkText = SlotState(slot);
                Label link = CreateChip(linkText, SlotStateColor(slot));
                top.Add(link);
                card.Add(top);

                Label summary = CreateMutedLabel(SlotSummary(slot));
                summary.style.whiteSpace = WhiteSpace.NoWrap;
                summary.style.overflow = Overflow.Hidden;
                summary.style.textOverflow = TextOverflow.Ellipsis;
                summary.tooltip = SlotSummary(slot);
                card.Add(summary);

                if (!ended)
                {
                    VisualElement controls = CreateActionRow();
                    DropdownField saved = BuildSavedVehicleField(plan, slot);
                    saved.style.flexGrow = 1f;
                    saved.style.minWidth = 150f;
                    controls.Add(saved);

                    Button launch = CreateMiniButton(
                        "Launch",
                        "Launch the selected saved vehicle through KSP",
                        delegate { LaunchSlot(plan, slot); });
                    launch.SetEnabled(plan.Status == MissionPlanStatus.Active &&
                        HasSavedVehicle(slot) && !HasBinding(slot) &&
                        _launchService != null && !launchLocked);
                    controls.Add(launch);
                    Button bind = CreateMiniButton(
                        "Bind current",
                        "Link or correct this slot using the current Mission Log vessel",
                        delegate { BindCurrent(plan, slot); });
                    bind.SetEnabled(plan.Status == MissionPlanStatus.Active &&
                        !launchLocked && !HasBinding(slot) && _tracker != null &&
                        _tracker.GetCurrent() != null);
                    controls.Add(bind);
                    card.Add(controls);

                    VisualElement editActions = CreateActionRow();
                    Button up = CreateMiniButton(
                        "↑",
                        "Move this vessel earlier",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.ReorderVesselSlot(
                                        plan.PlanId,
                                        slot.SlotId,
                                        slotIndex - 1);
                                },
                                "Could not reorder that vessel.");
                        });
                    up.SetEnabled(!launchLocked && slotIndex > 0);
                    editActions.Add(up);
                    Button down = CreateMiniButton(
                        "↓",
                        "Move this vessel later",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.ReorderVesselSlot(
                                        plan.PlanId,
                                        slot.SlotId,
                                        slotIndex + 1);
                                },
                                "Could not reorder that vessel.");
                        });
                    down.SetEnabled(!launchLocked && slotIndex < slots.Count - 1);
                    editActions.Add(down);
                    Button edit = CreateMiniButton(
                        "Edit",
                        "Edit this planned vessel",
                        delegate { ShowSlotEditor(slot.SlotId); });
                    edit.SetEnabled(!launchLocked);
                    editActions.Add(edit);
                    if (HasBinding(slot))
                    {
                        Button clear = CreateMiniButton(
                            "Clear link",
                            "Remove this mission link and all of its matching aliases",
                            delegate
                            {
                                TryMutation(
                                    delegate
                                    {
                                        _planner.ClearVesselSlotBinding(
                                            plan.PlanId,
                                            slot.SlotId);
                                    },
                                    "Could not clear that vessel link.");
                            });
                        clear.SetEnabled(!launchLocked);
                        editActions.Add(clear);
                    }
                    Button remove = CreateMiniButton(
                        "Remove",
                        "Remove this planned vessel (move its mission steps first)",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.SetVesselSlotArchived(
                                        plan.PlanId,
                                        slot.SlotId,
                                        true);
                                },
                                "Could not remove that planned vessel.");
                        });
                    remove.SetEnabled(!launchLocked);
                    editActions.Add(remove);
                    card.Add(editActions);
                }
                _detail.Add(card);
            }

            if (slots.Count == 0)
            {
                _detail.Add(CreateEmptyHint(
                    "No vessels planned. Add one for each launch, docked element, or sortie."));
            }
        }

        private void RenderObjectives(MissionPlan plan)
        {
            bool ended = IsEnded(plan);
            bool launchLocked = IsLaunchLocked();
            VisualElement heading = CreateSectionHeading(
                "MISSION STEPS",
                "Actual events match this order; corrections remain explicit.");
            if (!ended)
            {
                Button add = CreateMiniButton(
                    "Add step",
                    "Add a launch, encounter, state, docking, landing, or custom step",
                    delegate { ShowObjectiveEditor(null); });
                add.SetEnabled(!launchLocked);
                heading.Add(add);
            }
            _detail.Add(heading);

            List<MissionPlanObjective> objectives = ActiveObjectives(plan);
            for (int index = 0; index < objectives.Count; index++)
            {
                MissionPlanObjective objective = objectives[index];
                int objectiveIndex = index;
                RenderedObjectiveCount++;
                InvertedCornerBox row = CreatePanel(
                    "mission-plan-objective-" + SafeName(objective.ObjectiveId));
                row.style.paddingTop = 5f;
                row.style.paddingBottom = 5f;
                row.style.marginBottom = 3f;
                if (objective.Status == MissionObjectiveStatus.Current)
                {
                    row.BorderColor = PlannerBlue;
                }
                else if (objective.Status == MissionObjectiveStatus.Deviated)
                {
                    row.BorderColor = PlannerRed;
                }

                VisualElement line = CreateActionRow();
                line.style.alignItems = Align.Center;
                Label order = CreateMutedLabel((index + 1).ToString("00"));
                order.style.width = 28f;
                line.Add(order);
                Label kind = CreateChip(objective.Kind.ToString(), KindColor(objective.Kind));
                line.Add(kind);
                Label title = CreateWrappedLabel(FirstNonEmpty(objective.Title, objective.Kind.ToString()));
                title.style.flexGrow = 1f;
                title.style.flexShrink = 1f;
                title.style.minWidth = 0f;
                title.style.whiteSpace = WhiteSpace.NoWrap;
                title.style.overflow = Overflow.Hidden;
                title.style.textOverflow = TextOverflow.Ellipsis;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.tooltip = ObjectiveSummary(plan, objective);
                line.Add(title);
                Label status = CreateChip(
                    objective.Status.ToString(),
                    ObjectiveStatusColor(objective.Status));
                line.Add(status);
                if ((plan.Status == MissionPlanStatus.Active ||
                     plan.Status == MissionPlanStatus.Completed) &&
                    !launchLocked && !objective.HasManualResolution &&
                    objective.Status == MissionObjectiveStatus.Achieved)
                {
                    line.Add(CreateMiniButton(
                        "Override",
                        "Reject this automatic match and record a deviation",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.MarkObjectiveDeviated(
                                        plan.PlanId,
                                        objective.ObjectiveId,
                                        null,
                                        "Automatic match overridden by player");
                                },
                                "Could not override that automatic match.");
                        }));
                }
                row.Add(line);

                Label target = CreateMutedLabel(ObjectiveSummary(plan, objective));
                target.style.whiteSpace = WhiteSpace.NoWrap;
                target.style.overflow = Overflow.Hidden;
                target.style.textOverflow = TextOverflow.Ellipsis;
                target.tooltip = ObjectiveSummary(plan, objective);
                row.Add(target);

                VisualElement actions = CreateActionRow();
                if (!ended)
                {
                    Button up = CreateMiniButton(
                        "↑",
                        "Move this step earlier",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.ReorderObjective(
                                        plan.PlanId,
                                        objective.ObjectiveId,
                                        objectiveIndex - 1);
                                },
                                "Could not reorder that mission step.");
                        });
                    up.SetEnabled(!launchLocked && objectiveIndex > 0);
                    actions.Add(up);
                    Button down = CreateMiniButton(
                        "↓",
                        "Move this step later",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.ReorderObjective(
                                        plan.PlanId,
                                        objective.ObjectiveId,
                                        objectiveIndex + 1);
                                },
                                "Could not reorder that mission step.");
                        });
                    down.SetEnabled(!launchLocked &&
                        objectiveIndex < objectives.Count - 1);
                    actions.Add(down);
                    Button edit = CreateMiniButton(
                        "Edit",
                        "Edit this mission step",
                        delegate { ShowObjectiveEditor(objective.ObjectiveId); });
                    edit.SetEnabled(!launchLocked);
                    actions.Add(edit);
                    Button remove = CreateMiniButton(
                        "Remove",
                        "Remove this mission step",
                        delegate
                        {
                            TryMutation(
                                delegate
                                {
                                    _planner.SetObjectiveArchived(
                                        plan.PlanId,
                                        objective.ObjectiveId,
                                        true);
                                },
                                "Could not remove that mission step.");
                        });
                    remove.SetEnabled(!launchLocked);
                    actions.Add(remove);
                }

                if ((plan.Status == MissionPlanStatus.Active ||
                     plan.Status == MissionPlanStatus.Completed) &&
                    !launchLocked)
                {
                    if (objective.HasManualResolution)
                    {
                        actions.Add(CreateMiniButton(
                            "Correct",
                            "Clear the manual resolution and match this step again",
                            delegate
                            {
                                TryMutation(
                                    delegate
                                    {
                                        _planner.ClearManualResolution(
                                            plan.PlanId,
                                            objective.ObjectiveId);
                                    },
                                    "Could not clear that manual resolution.");
                            }));
                    }
                    else if (objective.Status == MissionObjectiveStatus.Pending ||
                        objective.Status == MissionObjectiveStatus.Current ||
                        objective.Status == MissionObjectiveStatus.Deviated ||
                        objective.Status == MissionObjectiveStatus.Skipped)
                    {
                        actions.Add(CreateMiniButton(
                            "Match",
                            "Confirm that this step happened",
                            delegate
                            {
                                TryMutation(
                                    delegate
                                    {
                                        _planner.ManuallyMatchObjective(
                                            plan.PlanId,
                                            objective.ObjectiveId,
                                            null,
                                            "Matched by player");
                                    },
                                    "Could not match that mission step.");
                            }));
                        actions.Add(CreateMiniButton(
                            "Skip",
                            "Intentionally skip this mission step",
                            delegate
                            {
                                TryMutation(
                                    delegate
                                    {
                                        _planner.SkipObjective(
                                            plan.PlanId,
                                            objective.ObjectiveId,
                                            "Skipped by player");
                                    },
                                    "Could not skip that mission step.");
                            }));
                        if (objective.Status != MissionObjectiveStatus.Deviated)
                        {
                            actions.Add(CreateMiniButton(
                                "Deviation",
                                "Mark this planned step as deviated",
                                delegate
                                {
                                    TryMutation(
                                        delegate
                                        {
                                            _planner.MarkObjectiveDeviated(
                                                plan.PlanId,
                                                objective.ObjectiveId,
                                                null,
                                                "Marked by player");
                                        },
                                        "Could not mark that deviation.");
                                }));
                        }
                    }
                }
                if (actions.childCount > 0)
                {
                    row.Add(actions);
                }
                _detail.Add(row);
            }

            if (objectives.Count == 0)
            {
                _detail.Add(CreateEmptyHint(
                    "No mission steps yet. Start with Launch, then add bodies, states, docking, landing, and completion."));
            }
        }

        private void RenderDeviations(MissionPlan plan)
        {
            VisualElement heading = CreateSectionHeading(
                "DEVIATIONS",
                "Differences between the plan and the observed mission.");
            _detail.Add(heading);

            List<MissionPlanDeviation> deviations = plan.Deviations ??
                new List<MissionPlanDeviation>();
            for (int index = 0; index < deviations.Count; index++)
            {
                MissionPlanDeviation deviation = deviations[index];
                if (deviation == null)
                {
                    continue;
                }
                RenderedDeviationCount++;
                VisualElement line = CreateActionRow();
                line.name = "mission-plan-deviation-" + SafeName(deviation.DeviationId);
                line.style.minHeight = 30f;
                line.style.alignItems = Align.Center;
                line.style.paddingLeft = 6f;
                line.style.paddingRight = 6f;
                line.style.marginBottom = 2f;
                Label marker = CreateChip("!", PlannerRed);
                marker.style.width = 25f;
                line.Add(marker);
                Label text = CreateWrappedLabel(
                    FirstNonEmpty(deviation.Title, "Plan deviation") +
                    (string.IsNullOrWhiteSpace(deviation.Detail)
                        ? string.Empty
                        : "  ·  " + deviation.Detail));
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                text.style.minWidth = 0f;
                text.style.whiteSpace = WhiteSpace.NoWrap;
                text.style.overflow = Overflow.Hidden;
                text.style.textOverflow = TextOverflow.Ellipsis;
                text.tooltip = FirstNonEmpty(deviation.Detail, deviation.Title);
                line.Add(text);
                _detail.Add(line);
            }

            if (RenderedDeviationCount == 0)
            {
                _detail.Add(CreateEmptyHint(plan.Status == MissionPlanStatus.Draft
                    ? "Deviations appear after this plan is activated and flight begins."
                    : "No deviations recorded."));
            }
        }

        private DropdownField BuildSavedVehicleField(
            MissionPlan plan,
            MissionPlanVesselSlot slot)
        {
            List<string> choices = new List<string> { NoSavedVehicle };
            Dictionary<string, SavedVehicleInfo> vehiclesByLabel =
                new Dictionary<string, SavedVehicleInfo>(StringComparer.Ordinal);
            int selectedIndex = 0;
            for (int index = 0; index < _savedVehicles.Count; index++)
            {
                SavedVehicleInfo vehicle = _savedVehicles[index];
                string label = SavedVehicleLabel(vehicle, index);
                choices.Add(label);
                vehiclesByLabel[label] = vehicle;
                if (SavedVehicleMatches(slot, vehicle))
                {
                    selectedIndex = choices.Count - 1;
                }
            }

            bool unavailableSelection = HasSavedVehicle(slot) && selectedIndex == 0;
            if (unavailableSelection)
            {
                choices.Add(FirstNonEmpty(
                    slot.SavedVehicleName,
                    slot.SavedVehicleId,
                    "Saved vehicle") + "  [unavailable]");
                selectedIndex = choices.Count - 1;
            }

            DropdownField field = new DropdownField(string.Empty, choices, selectedIndex)
            {
                name = "mission-plan-saved-vehicle-" + SafeName(slot.SlotId),
                tooltip = unavailableSelection
                    ? "The saved selection is unavailable. Refresh or choose another craft."
                    : _savedVehicles.Count == 0
                    ? "Refresh after saving a craft in the VAB or SPH"
                    : "Choose the craft for this planned launch"
            };
            field.AddToClassList("oab-dropdown-field");
            field.SetEnabled(!IsEnded(plan) && !HasBinding(slot) &&
                !IsLaunchLocked() &&
                (_savedVehicles.Count > 0 || unavailableSelection));
            field.RegisterValueChangedCallback(delegate(ChangeEvent<string> change)
            {
                if (string.Equals(change.newValue, NoSavedVehicle, StringComparison.Ordinal))
                {
                    TryMutation(
                        delegate { _planner.ClearSavedVehicle(plan.PlanId, slot.SlotId); },
                        "Could not clear that saved vehicle.");
                    return;
                }
                SavedVehicleInfo vehicle;
                if (vehiclesByLabel.TryGetValue(change.newValue, out vehicle))
                {
                    SelectSavedVehicle(plan, slot, vehicle);
                }
            });
            return field;
        }

        private void SelectSavedVehicle(
            MissionPlan plan,
            MissionPlanVesselSlot slot,
            SavedVehicleInfo vehicle)
        {
            TryMutation(
                delegate
                {
                    _planner.SelectSavedVehicle(
                        plan.PlanId,
                        slot.SlotId,
                        vehicle.Id,
                        vehicle.Name,
                        null,
                        vehicle.DataLocation);
                },
                "Could not select that saved vehicle.");
        }

        private bool LaunchSlot(MissionPlan plan, MissionPlanVesselSlot slot)
        {
            if (_launchService == null)
            {
                Info("Saved-vehicle launching is not available in this scene.");
                return false;
            }
            if (IsLaunchLocked())
            {
                Info("Wait for the current KSP launch handoff to finish.");
                return false;
            }
            if (HasBinding(slot))
            {
                Info("Clear this vessel's existing mission link before launching it again.");
                return false;
            }
            MissionRecord campaignMission = _tracker == null
                ? null
                : (_tracker.GetCurrent() ?? _tracker.GetLatest());
            if (campaignMission != null &&
                !string.IsNullOrWhiteSpace(plan.CampaignId) &&
                !string.Equals(
                    plan.CampaignId,
                    campaignMission.CampaignId,
                    StringComparison.Ordinal))
            {
                Info("Open this plan in its original KSP campaign before launching.");
                return false;
            }

            try
            {
                _planner.RecordLaunchRequest(plan.PlanId, slot.SlotId);
                string message;
                bool launched = _launchService.TryLaunch(plan, slot, out message);
                _planner.RecordLaunchResult(
                    plan.PlanId,
                    slot.SlotId,
                    launched ? "Handed to KSP" : "Failed",
                    launched ? null : message);
                Info(message);
                Refresh();
                return launched;
            }
            catch (Exception error)
            {
                try
                {
                    _planner.RecordLaunchResult(
                        plan.PlanId,
                        slot.SlotId,
                        "Failed",
                        error.Message);
                }
                catch (Exception recordError)
                {
                    LogError("Could not record the failed launch: " + recordError.Message);
                }
                LogError("Could not launch that saved vehicle: " + error.Message);
                Refresh();
                return false;
            }
        }

        private bool BindCurrent(MissionPlan plan, MissionPlanVesselSlot slot)
        {
            MissionRecord mission = _tracker == null ? null : _tracker.GetCurrent();
            if (mission == null)
            {
                Info("Mission Log does not have a current vessel to bind.");
                return false;
            }
            if (!string.IsNullOrWhiteSpace(plan.CampaignId) &&
                !string.Equals(
                    plan.CampaignId,
                    mission.CampaignId,
                    StringComparison.Ordinal))
            {
                Info("That vessel belongs to a different KSP campaign.");
                return false;
            }
            return TryMutation(
                delegate
                {
                    _planner.BindLaunch(
                        plan.PlanId,
                        slot.SlotId,
                        mission.MissionId,
                        mission.VesselId,
                        DateTime.UtcNow.ToString("o"));
                },
                "Could not bind the current vessel to that slot.");
        }

        private void ShowCreateEditor()
        {
            HideEditors();
            _createTitle.SetValueWithoutNotify(string.Empty);
            _createNotes.SetValueWithoutNotify(string.Empty);
            _createEditor.style.display = DisplayStyle.Flex;
            _createTitle.Focus();
        }

        private void CreatePlan()
        {
            if (string.IsNullOrWhiteSpace(_createTitle.value))
            {
                Info("Give the mission plan a name first.");
                return;
            }
            try
            {
                CreatePlanForReview(_createTitle.value, _createNotes.value);
            }
            catch (Exception error)
            {
                LogError("Could not create the plan: " + error.Message);
            }
        }

        private void ShowPlanEditor()
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || IsEnded(plan))
            {
                return;
            }
            HideEditors();
            _planTitle.SetValueWithoutNotify(plan.Title ?? string.Empty);
            _planNotes.SetValueWithoutNotify(plan.Notes ?? string.Empty);
            _planEditor.style.display = DisplayStyle.Flex;
            _planTitle.Focus();
        }

        private void SavePlan()
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || string.IsNullOrWhiteSpace(_planTitle.value))
            {
                Info("Give the mission plan a name first.");
                return;
            }
            if (TryMutation(
                delegate
                {
                    _planner.UpdatePlan(plan.PlanId, _planTitle.value, _planNotes.value);
                },
                "Could not save the plan."))
            {
                HideEditors();
                Refresh();
            }
        }

        private void ShowSlotEditor(string slotId)
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || IsEnded(plan))
            {
                return;
            }
            HideEditors();
            _editingSlotId = slotId ?? string.Empty;
            MissionPlanVesselSlot slot = FindSlot(plan, _editingSlotId);
            _slotName.SetValueWithoutNotify(slot == null ? string.Empty : slot.Name);
            _slotRole.SetValueWithoutNotify(slot == null ? string.Empty : slot.Role);
            _slotRequired.SetValueWithoutNotify(slot == null || slot.Required);
            _slotEditor.style.display = DisplayStyle.Flex;
            _slotName.Focus();
        }

        private void SaveSlot()
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || string.IsNullOrWhiteSpace(_slotName.value))
            {
                Info("Give the planned vessel a callsign or slot name first.");
                return;
            }
            bool saved = TryMutation(
                delegate
                {
                    if (string.IsNullOrWhiteSpace(_editingSlotId))
                    {
                        _planner.AddVesselSlot(
                            plan.PlanId,
                            _slotName.value,
                            _slotRole.value,
                            _slotRequired.value);
                    }
                    else
                    {
                        _planner.UpdateVesselSlot(
                            plan.PlanId,
                            _editingSlotId,
                            _slotName.value,
                            _slotRole.value,
                            _slotRequired.value);
                    }
                },
                "Could not save that planned vessel.");
            if (saved)
            {
                HideEditors();
                Refresh();
            }
        }

        private void ShowObjectiveEditor(string objectiveId)
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || IsEnded(plan))
            {
                return;
            }
            HideEditors();
            _editingObjectiveId = objectiveId ?? string.Empty;
            UpdateObjectiveSlotChoices(plan);
            MissionPlanObjective objective = FindObjective(plan, _editingObjectiveId);
            MissionObjectiveKind kind = objective == null
                ? MissionObjectiveKind.Launch
                : objective.Kind;
            _objectiveKind.SetValueWithoutNotify(kind.ToString());
            UpdateObjectiveEditorVisibility(kind);
            _objectiveTitle.SetValueWithoutNotify(objective == null
                ? DefaultObjectiveTitle(kind)
                : objective.Title);
            _objectiveNotes.SetValueWithoutNotify(objective == null
                ? string.Empty
                : objective.Notes);
            _objectiveSlot.SetValueWithoutNotify(ObjectiveSlotLabel(plan,
                objective == null ? string.Empty : objective.VesselSlotId));
            _objectiveRelatedSlot.SetValueWithoutNotify(ObjectiveSlotLabel(plan,
                objective == null ? string.Empty : objective.RelatedVesselSlotId));
            _objectiveBody.SetValueWithoutNotify(objective == null
                ? string.Empty
                : objective.TargetBody);
            _objectiveSituation.SetValueWithoutNotify(objective == null
                ? string.Empty
                : objective.TargetSituation);
            _objectiveMatch.SetValueWithoutNotify(objective == null
                ? string.Empty
                : objective.MatchValue);
            _objectiveOptional.SetValueWithoutNotify(objective != null && objective.Optional);
            _objectiveEditor.style.display = DisplayStyle.Flex;
            _objectiveTitle.Focus();
        }

        private void SaveObjective()
        {
            MissionPlan plan = SelectedPlan();
            if (plan == null || string.IsNullOrWhiteSpace(_objectiveTitle.value))
            {
                Info("Describe what should happen in this mission step first.");
                return;
            }
            MissionObjectiveKind kind;
            if (!Enum.TryParse(_objectiveKind.value, true, out kind))
            {
                Info("Choose a valid mission step type.");
                return;
            }
            string slotId;
            _objectiveSlotIds.TryGetValue(_objectiveSlot.value ?? string.Empty, out slotId);
            string relatedSlotId;
            _objectiveSlotIds.TryGetValue(
                _objectiveRelatedSlot.value ?? string.Empty,
                out relatedSlotId);
            if (kind != MissionObjectiveKind.Dock)
            {
                relatedSlotId = string.Empty;
            }
            string targetBody = kind == MissionObjectiveKind.Body ||
                kind == MissionObjectiveKind.Orbit ||
                kind == MissionObjectiveKind.Land
                    ? _objectiveBody.value
                    : string.Empty;
            string targetSituation = kind == MissionObjectiveKind.Situation
                ? _objectiveSituation.value
                : string.Empty;
            string matchValue = kind == MissionObjectiveKind.Custom
                ? _objectiveMatch.value
                : string.Empty;
            bool saved = TryMutation(
                delegate
                {
                    if (string.IsNullOrWhiteSpace(_editingObjectiveId))
                    {
                        MissionPlanObjective added = _planner.AddObjective(
                            plan.PlanId,
                            kind,
                            _objectiveTitle.value,
                            slotId,
                            targetBody,
                            targetSituation,
                            matchValue,
                            _objectiveOptional.value,
                            relatedSlotId);
                        if (!string.IsNullOrWhiteSpace(_objectiveNotes.value))
                        {
                            _planner.UpdateObjective(
                                plan.PlanId,
                                added.ObjectiveId,
                                kind,
                                _objectiveTitle.value,
                                _objectiveNotes.value,
                                slotId,
                                targetBody,
                                targetSituation,
                                matchValue,
                                _objectiveOptional.value,
                                relatedSlotId);
                        }
                    }
                    else
                    {
                        _planner.UpdateObjective(
                            plan.PlanId,
                            _editingObjectiveId,
                            kind,
                            _objectiveTitle.value,
                            _objectiveNotes.value,
                            slotId,
                            targetBody,
                            targetSituation,
                            matchValue,
                            _objectiveOptional.value,
                            relatedSlotId);
                    }
                },
                "Could not save that mission step.");
            if (saved)
            {
                HideEditors();
                Refresh();
            }
        }

        private void UpdateObjectiveSlotChoices(MissionPlan plan)
        {
            _objectiveSlotIds.Clear();
            List<string> choices = new List<string> { AnyVessel };
            _objectiveSlotIds[AnyVessel] = string.Empty;
            List<MissionPlanVesselSlot> slots = ActiveSlots(plan);
            for (int index = 0; index < slots.Count; index++)
            {
                MissionPlanVesselSlot slot = slots[index];
                string label = (index + 1) + ". " + FirstNonEmpty(slot.Name, "Planned vessel");
                choices.Add(label);
                _objectiveSlotIds[label] = slot.SlotId;
            }
            _objectiveSlot.choices = choices;
            _objectiveSlot.SetValueWithoutNotify(AnyVessel);
            _objectiveRelatedSlot.choices = new List<string>(choices);
            _objectiveRelatedSlot.SetValueWithoutNotify(AnyVessel);
        }

        private void UpdateObjectiveEditorVisibility(MissionObjectiveKind kind)
        {
            _objectiveSlot.style.display = kind == MissionObjectiveKind.Custom
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            _objectiveRelatedSlot.style.display = kind == MissionObjectiveKind.Dock
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _objectiveBody.style.display =
                kind == MissionObjectiveKind.Body ||
                kind == MissionObjectiveKind.Orbit ||
                kind == MissionObjectiveKind.Land
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            _objectiveSituation.style.display = kind == MissionObjectiveKind.Situation
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _objectiveMatch.style.display = kind == MissionObjectiveKind.Custom
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void HideEditors()
        {
            _createEditor.style.display = DisplayStyle.None;
            _planEditor.style.display = DisplayStyle.None;
            _slotEditor.style.display = DisplayStyle.None;
            _objectiveEditor.style.display = DisplayStyle.None;
            _editingSlotId = string.Empty;
            _editingObjectiveId = string.Empty;
        }

        private bool TryMutation(Action action, string context)
        {
            try
            {
                action();
                MissionPlan selected = SelectedPlan();
                if (_tracker != null && selected != null && !selected.Archived &&
                    (selected.Status == MissionPlanStatus.Active ||
                     selected.Status == MissionPlanStatus.Completed))
                {
                    _planner.RecomputeProgress(
                        selected.PlanId,
                        MissionPlanTimelineAdapter.BuildFacts(_tracker, selected));
                }
                _abandonArmed = false;
                SetFeedback(string.Empty, false);
                Refresh();
                return true;
            }
            catch (Exception error)
            {
                LogError(context + " " + error.Message);
                Refresh();
                return false;
            }
        }

        private List<MissionPlan> OrderedPlans()
        {
            List<MissionPlan> result = new List<MissionPlan>();
            if (_planner.State != null && _planner.State.Plans != null)
            {
                for (int index = 0; index < _planner.State.Plans.Count; index++)
                {
                    MissionPlan plan = _planner.State.Plans[index];
                    if (plan != null)
                    {
                        result.Add(plan);
                    }
                }
            }
            result.Sort(delegate(MissionPlan left, MissionPlan right)
            {
                int status = PlanStatusOrder(left.Status).CompareTo(PlanStatusOrder(right.Status));
                if (status != 0)
                {
                    return status;
                }
                int updated = string.Compare(
                    right.UpdatedUtc,
                    left.UpdatedUtc,
                    StringComparison.Ordinal);
                return updated != 0
                    ? updated
                    : string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private MissionPlan SelectedPlan()
        {
            return FindPlan(_selectedPlanId);
        }

        private MissionPlan FindPlan(string planId)
        {
            if (string.IsNullOrWhiteSpace(planId) || _planner.State == null ||
                _planner.State.Plans == null)
            {
                return null;
            }
            for (int index = 0; index < _planner.State.Plans.Count; index++)
            {
                MissionPlan plan = _planner.State.Plans[index];
                if (plan != null && string.Equals(
                    plan.PlanId,
                    planId,
                    StringComparison.Ordinal))
                {
                    return plan;
                }
            }
            return null;
        }

        private static MissionPlanVesselSlot FindSlot(MissionPlan plan, string slotId)
        {
            if (plan == null || string.IsNullOrWhiteSpace(slotId) || plan.VesselSlots == null)
            {
                return null;
            }
            for (int index = 0; index < plan.VesselSlots.Count; index++)
            {
                MissionPlanVesselSlot slot = plan.VesselSlots[index];
                if (slot != null && string.Equals(slot.SlotId, slotId, StringComparison.Ordinal))
                {
                    return slot;
                }
            }
            return null;
        }

        private static MissionPlanObjective FindObjective(
            MissionPlan plan,
            string objectiveId)
        {
            if (plan == null || string.IsNullOrWhiteSpace(objectiveId) ||
                plan.Objectives == null)
            {
                return null;
            }
            for (int index = 0; index < plan.Objectives.Count; index++)
            {
                MissionPlanObjective objective = plan.Objectives[index];
                if (objective != null && string.Equals(
                    objective.ObjectiveId,
                    objectiveId,
                    StringComparison.Ordinal))
                {
                    return objective;
                }
            }
            return null;
        }

        private SavedVehicleInfo FindSavedVehicle(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            for (int index = 0; index < _savedVehicles.Count; index++)
            {
                SavedVehicleInfo vehicle = _savedVehicles[index];
                if (vehicle != null &&
                    (string.Equals(vehicle.Key, key, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(vehicle.Id, key, StringComparison.OrdinalIgnoreCase)))
                {
                    return vehicle;
                }
            }
            return null;
        }

        private static List<MissionPlanVesselSlot> ActiveSlots(MissionPlan plan)
        {
            List<MissionPlanVesselSlot> result = new List<MissionPlanVesselSlot>();
            if (plan != null && plan.VesselSlots != null)
            {
                for (int index = 0; index < plan.VesselSlots.Count; index++)
                {
                    MissionPlanVesselSlot slot = plan.VesselSlots[index];
                    if (slot != null && !slot.Archived)
                    {
                        result.Add(slot);
                    }
                }
            }
            result.Sort(delegate(MissionPlanVesselSlot left, MissionPlanVesselSlot right)
            {
                return left.Order.CompareTo(right.Order);
            });
            return result;
        }

        private static List<MissionPlanObjective> ActiveObjectives(MissionPlan plan)
        {
            List<MissionPlanObjective> result = new List<MissionPlanObjective>();
            if (plan != null && plan.Objectives != null)
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
                return left.Order.CompareTo(right.Order);
            });
            return result;
        }

        private static int ActiveObjectiveIndex(
            MissionPlan plan,
            MissionPlanObjective objective)
        {
            List<MissionPlanObjective> objectives = ActiveObjectives(plan);
            return objectives.IndexOf(objective);
        }

        private static int ActiveObjectiveCount(MissionPlan plan)
        {
            return ActiveObjectives(plan).Count;
        }

        private static MissionPlanObjective CurrentObjective(MissionPlan plan)
        {
            List<MissionPlanObjective> objectives = ActiveObjectives(plan);
            for (int index = 0; index < objectives.Count; index++)
            {
                if (objectives[index].Status == MissionObjectiveStatus.Current)
                {
                    return objectives[index];
                }
            }
            for (int index = 0; index < objectives.Count; index++)
            {
                if (objectives[index].Status == MissionObjectiveStatus.Pending)
                {
                    return objectives[index];
                }
            }
            return null;
        }

        private static void ProgressCounts(
            MissionPlan plan,
            out int achieved,
            out int resolved,
            out int total)
        {
            achieved = 0;
            resolved = 0;
            List<MissionPlanObjective> objectives = ActiveObjectives(plan);
            total = objectives.Count;
            for (int index = 0; index < objectives.Count; index++)
            {
                MissionObjectiveStatus status = objectives[index].Status;
                if (status == MissionObjectiveStatus.Achieved)
                {
                    achieved++;
                    resolved++;
                }
                else if (status == MissionObjectiveStatus.Skipped ||
                    status == MissionObjectiveStatus.Deviated)
                {
                    resolved++;
                }
            }
        }

        private static string ObjectiveSummary(MissionPlan plan, MissionPlanObjective objective)
        {
            List<string> parts = new List<string>();
            MissionPlanVesselSlot slot = FindSlot(plan, objective.VesselSlotId);
            if (slot != null)
            {
                parts.Add(FirstNonEmpty(slot.Name, "Planned vessel"));
            }
            MissionPlanVesselSlot related = FindSlot(
                plan,
                objective.RelatedVesselSlotId);
            if (related != null)
            {
                parts.Add("with " + FirstNonEmpty(related.Name, "planned vessel"));
            }
            if (!string.IsNullOrWhiteSpace(objective.TargetBody))
            {
                parts.Add(objective.TargetBody);
            }
            if (!string.IsNullOrWhiteSpace(objective.TargetSituation))
            {
                parts.Add(objective.TargetSituation);
            }
            if (!string.IsNullOrWhiteSpace(objective.MatchValue))
            {
                parts.Add(objective.MatchValue);
            }
            if (objective.Optional)
            {
                parts.Add("optional");
            }
            if (objective.HasManualResolution)
            {
                parts.Add("player resolved");
            }
            return parts.Count == 0 ? "No additional match constraints" :
                string.Join("  ·  ", parts.ToArray());
        }

        private static string ObjectiveSlotLabel(MissionPlan plan, string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return AnyVessel;
            }
            List<MissionPlanVesselSlot> slots = ActiveSlots(plan);
            for (int index = 0; index < slots.Count; index++)
            {
                if (string.Equals(slots[index].SlotId, slotId, StringComparison.Ordinal))
                {
                    return (index + 1) + ". " + FirstNonEmpty(
                        slots[index].Name,
                        "Planned vessel");
                }
            }
            return AnyVessel;
        }

        private static string SlotSummary(MissionPlanVesselSlot slot)
        {
            List<string> parts = new List<string>();
            parts.Add(slot.Required ? "required" : "optional");
            parts.Add(HasSavedVehicle(slot)
                ? "craft: " + FirstNonEmpty(slot.SavedVehicleName, slot.SavedVehicleId)
                : "craft not selected");
            if (HasBinding(slot))
            {
                parts.Add("mission linked");
            }
            if (!string.IsNullOrWhiteSpace(slot.LaunchState))
            {
                parts.Add("launch: " + slot.LaunchState);
            }
            if (!string.IsNullOrWhiteSpace(slot.LaunchError))
            {
                parts.Add(slot.LaunchError);
            }
            return string.Join("  ·  ", parts.ToArray());
        }

        private static string SlotState(MissionPlanVesselSlot slot)
        {
            if (HasBinding(slot))
            {
                return "Linked";
            }
            if (!string.IsNullOrWhiteSpace(slot.LaunchState))
            {
                return slot.LaunchState;
            }
            return HasSavedVehicle(slot) ? "Ready" : "Unassigned";
        }

        private static Color32 SlotStateColor(MissionPlanVesselSlot slot)
        {
            if (HasBinding(slot))
            {
                return PlannerGreen;
            }
            if (string.Equals(slot.LaunchState, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return PlannerRed;
            }
            return HasSavedVehicle(slot) ? PlannerBlue : MutedColor;
        }

        private static bool HasSavedVehicle(MissionPlanVesselSlot slot)
        {
            return slot != null && (!string.IsNullOrWhiteSpace(slot.SavedVehicleId) ||
                !string.IsNullOrWhiteSpace(slot.SavedVehiclePath));
        }

        private static bool HasBinding(MissionPlanVesselSlot slot)
        {
            return slot != null && (!string.IsNullOrWhiteSpace(slot.BoundMissionId) ||
                !string.IsNullOrWhiteSpace(slot.BoundVesselId));
        }

        private bool IsLaunchLocked()
        {
            return _launchService != null && _launchService.HasPendingLaunch;
        }

        private static bool SavedVehicleMatches(
            MissionPlanVesselSlot slot,
            SavedVehicleInfo vehicle)
        {
            return slot != null && vehicle != null &&
                string.Equals(slot.SavedVehicleId, vehicle.Id, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(slot.SavedVehicleLocation) ||
                 string.Equals(
                     slot.SavedVehicleLocation,
                     vehicle.DataLocation,
                     StringComparison.OrdinalIgnoreCase));
        }

        private static string SavedVehicleLabel(SavedVehicleInfo vehicle, int index)
        {
            string orientation = string.IsNullOrWhiteSpace(vehicle.Orientation)
                ? string.Empty
                : " · " + vehicle.Orientation;
            return FirstNonEmpty(vehicle.Name, vehicle.WorkspaceName, vehicle.Id) +
                orientation + "  [" + (index + 1) + "]";
        }

        private static int PlanStatusOrder(MissionPlanStatus status)
        {
            if (status == MissionPlanStatus.Active)
            {
                return 0;
            }
            if (status == MissionPlanStatus.Draft)
            {
                return 1;
            }
            return status == MissionPlanStatus.Completed ? 2 : 3;
        }

        private static bool IsEnded(MissionPlan plan)
        {
            return plan.Status == MissionPlanStatus.Completed ||
                plan.Status == MissionPlanStatus.Abandoned;
        }

        private static string DefaultObjectiveTitle(MissionObjectiveKind kind)
        {
            switch (kind)
            {
                case MissionObjectiveKind.Launch: return "Launch planned vessel";
                case MissionObjectiveKind.Body: return "Enter target body's SOI";
                case MissionObjectiveKind.Situation: return "Reach planned flight state";
                case MissionObjectiveKind.Orbit: return "Establish stable orbit";
                case MissionObjectiveKind.Land: return "Land at target body";
                case MissionObjectiveKind.Dock: return "Dock planned vessels";
                case MissionObjectiveKind.Separate: return "Separate planned vessel";
                case MissionObjectiveKind.Recover: return "Recover planned vessel";
                case MissionObjectiveKind.Complete: return "Complete mission";
                default: return "Complete custom mission step";
            }
        }

        private static Color32 StatusColor(MissionPlanStatus status)
        {
            if (status == MissionPlanStatus.Active)
            {
                return PlannerGreen;
            }
            if (status == MissionPlanStatus.Completed)
            {
                return PlannerBlue;
            }
            if (status == MissionPlanStatus.Abandoned)
            {
                return PlannerRed;
            }
            return PlannerGold;
        }

        private static Color32 ObjectiveStatusColor(MissionObjectiveStatus status)
        {
            if (status == MissionObjectiveStatus.Achieved)
            {
                return PlannerGreen;
            }
            if (status == MissionObjectiveStatus.Current)
            {
                return PlannerBlue;
            }
            if (status == MissionObjectiveStatus.Deviated)
            {
                return PlannerRed;
            }
            if (status == MissionObjectiveStatus.Skipped)
            {
                return MutedColor;
            }
            return PlannerGold;
        }

        private static Color32 KindColor(MissionObjectiveKind kind)
        {
            if (kind == MissionObjectiveKind.Dock || kind == MissionObjectiveKind.Separate)
            {
                return PlannerPurple;
            }
            if (kind == MissionObjectiveKind.Land || kind == MissionObjectiveKind.Recover)
            {
                return PlannerGreen;
            }
            if (kind == MissionObjectiveKind.Body || kind == MissionObjectiveKind.Orbit ||
                kind == MissionObjectiveKind.Situation)
            {
                return PlannerBlue;
            }
            return PlannerGold;
        }

        private static VisualElement CreateSectionHeading(string title, string help)
        {
            VisualElement row = CreateActionRow();
            row.style.alignItems = Align.Center;
            row.style.marginTop = 7f;
            row.style.marginBottom = 3f;
            VisualElement copy = new VisualElement();
            copy.style.flexGrow = 1f;
            copy.style.minWidth = 0f;
            Label heading = CreateHeading(title, 14f);
            Label subtitle = CreateMutedLabel(help);
            subtitle.style.whiteSpace = WhiteSpace.NoWrap;
            subtitle.style.overflow = Overflow.Hidden;
            subtitle.style.textOverflow = TextOverflow.Ellipsis;
            subtitle.tooltip = help;
            copy.Add(heading);
            copy.Add(subtitle);
            row.Add(copy);
            return row;
        }

        private static InvertedCornerBox CreatePanel(string name)
        {
            InvertedCornerBox panel = new InvertedCornerBox { name = name };
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 7f;
            panel.style.paddingBottom = 7f;
            return panel;
        }

        private static VisualElement CreateActionRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("oab-window-actions");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0f;
            return row;
        }

        private static Label CreateHeading(string text, float size)
        {
            Label heading = CreateWrappedLabel(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = size;
            return heading;
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
            label.style.color = new StyleColor(MutedColor);
            label.style.fontSize = 11f;
            return label;
        }

        private static Label CreateChip(string text, Color32 color)
        {
            Label chip = new Label(text);
            chip.style.height = 23f;
            chip.style.paddingLeft = 7f;
            chip.style.paddingRight = 7f;
            chip.style.marginLeft = 3f;
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
            chip.style.fontSize = 10f;
            StyleColor style = new StyleColor(color);
            chip.style.color = style;
            chip.style.borderLeftColor = style;
            chip.style.borderRightColor = style;
            chip.style.borderTopColor = style;
            chip.style.borderBottomColor = style;
            return chip;
        }

        private static Button CreateButton(string text, string tooltip, Action clicked)
        {
            Button button = new Button(clicked) { text = text, tooltip = tooltip };
            button.AddToClassList("ui-sound-button");
            button.style.flexGrow = 1f;
            button.style.marginRight = 4f;
            return button;
        }

        private static Button CreateMiniButton(string text, string tooltip, Action clicked)
        {
            Button button = new Button(clicked) { text = text, tooltip = tooltip };
            button.AddToClassList("link");
            button.AddToClassList("ui-sound-button");
            button.style.flexGrow = 0f;
            button.style.minWidth = 32f;
            button.style.height = 27f;
            button.style.marginLeft = 3f;
            button.style.paddingLeft = 7f;
            button.style.paddingRight = 7f;
            return button;
        }

        private static TextField CreateTextField(string label, bool multiline)
        {
            TextField field = new TextField(label) { multiline = multiline };
            field.AddToClassList("oab-text-field");
            field.style.width = Length.Percent(100f);
            field.style.minWidth = 0f;
            field.style.marginTop = 4f;
            if (multiline)
            {
                field.style.height = 58f;
                field.style.whiteSpace = WhiteSpace.Normal;
                field.style.overflow = Overflow.Hidden;
                VisualElement input = field.Q<VisualElement>("unity-text-input");
                if (input != null)
                {
                    input.style.whiteSpace = WhiteSpace.Normal;
                    input.style.overflow = Overflow.Hidden;
                }
            }
            return field;
        }

        private static VisualElement CreateEmptyHint(string text)
        {
            Label hint = CreateMutedLabel(text);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.paddingLeft = 7f;
            hint.style.paddingRight = 7f;
            hint.style.paddingTop = 5f;
            hint.style.paddingBottom = 5f;
            return hint;
        }

        private static bool IsDisplayed(VisualElement element)
        {
            return element != null && element.style.display.value == DisplayStyle.Flex;
        }

        private void Info(string message)
        {
            SetFeedback(message, false);
            if (_info != null && !string.IsNullOrWhiteSpace(message))
            {
                _info(message);
            }
        }

        private void LogError(string message)
        {
            SetFeedback(message, true);
            if (_logError != null)
            {
                _logError(message);
            }
            else
            {
                Info(message);
            }
        }

        private void SetFeedback(string message, bool error)
        {
            if (_feedback == null)
            {
                return;
            }
            _feedback.text = message ?? string.Empty;
            _feedback.style.color = new StyleColor(error ? PlannerRed : PlannerBlue);
            _feedback.style.display = string.IsNullOrWhiteSpace(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                {
                    return values[index].Trim();
                }
            }
            return string.Empty;
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "none";
            }
            return value.Replace(" ", "-").Replace("/", "-").Replace("\\", "-");
        }

        private static readonly Color32 PlannerBlue = new Color32(119, 152, 204, 255);
        private static readonly Color32 PlannerGreen = new Color32(112, 204, 151, 255);
        private static readonly Color32 PlannerPurple = new Color32(188, 161, 255, 255);
        private static readonly Color32 PlannerGold = new Color32(221, 178, 80, 255);
        private static readonly Color32 PlannerRed = new Color32(209, 119, 119, 255);
        private static readonly Color32 MutedColor = new Color32(165, 171, 184, 255);
    }
}
