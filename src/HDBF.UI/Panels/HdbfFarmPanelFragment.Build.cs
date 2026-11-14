// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Construction of the v2 indoor panel UIElements skeleton. There is deliberately NO
//          second header: the vanilla EntityPanel already renders the building icon, name and
//          district, so the fragment starts with the full-width card scroller (first card = the
//          compact tier/faction/mode row + workers/efficiency). The fragment root stays a plain
//          container (the vanilla EntityPanel inserts provider fragments into a classless
//          `Fragments` container); every visible block is a shipped NineSliceVisualElement card
//          with a vanilla sub-box class. After the tree is built the vanilla
//          VisualElementInitializer runs once so the shipped initializers (click sound, nine-slice
//          scrollbars, localization) apply to it exactly as they do to a loaded UXML template.
// Requirements: REQ-V2-UI-01/03, REQ-V2-READ-04
// ============================================================================
using System;
using Timberborn.CoreUI;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        /// <summary>Initial scroller height in UI points; the render refresh re-measures it.</summary>
        private const float InitialScrollHeight = 420f;

        private VisualElement? _root;
        private ScrollView? _scroll;
        private VisualElement? _cards;
        private VisualElement? _modeHost;
        private RadioToggle? _modeToggle;
        private Label? _modeTransition;
        private VisualElement? _actionCard;
        private Button? _refillButton;
        private Label? _refillReason;
        private Button? _refillAllButton;
        private Button? _maintenanceButton;
        private Label? _maintenanceReason;
        private Button? _autoMaintenanceButton;
        private Label? _actionStatus;
        private Label? _actionTitle;
        private VisualElementInitializer? _visualElementInitializer;

        private VisualElement BuildRoot()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            // No margin/padding/background: the root is the transparent fragment container and the
            // vanilla NineSlice cards own every background (requirement C).
            root.style.marginTop = 0f;
            root.style.display = DisplayStyle.None;

            BuildCardScroller(root);
            BuildModeToggle();
            BuildCropDropdown();
            BuildActionCard(root);

            // Run the game's own initializers over the code-built tree (the same pass a loaded UXML
            // template gets): this wires the vanilla click sounds (--click-sound custom property),
            // the nine-slice scrollbar draggers of the game-scroll-view and element localization.
            // A failure here is not fatal: the panel keeps working without those extras.
            try
            {
                _visualElementInitializer?.InitializeVisualElement(root);
            }
            catch (Exception)
            {
                // Swallowed on purpose: a missing initializer must never hide the panel.
            }

            _root = root;
            return root;
        }

        private void BuildCardScroller(VisualElement root)
        {
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            HdbfPanelControls.AddClasses(_scroll, HdbfIndoorPanelStyle.Scroll);
            // Vanilla scroller: the game-scroll-view stylesheet draws the shipped nine-slice
            // draggers. Only the horizontal bar is suppressed (the cards are fixed-width).
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.style.maxHeight = InitialScrollHeight;
            _scroll.style.marginTop = 0f;
            _scroll.style.marginBottom = 0f;
            _scroll.style.flexGrow = 0f;
            _scroll.style.flexShrink = 0f;

            _cards = new VisualElement();
            _cards.style.flexDirection = FlexDirection.Column;
            _scroll.Add(_cards);
            root.Add(_scroll);
        }

        private void BuildModeToggle()
        {
            _modeHost = new VisualElement();
            _modeHost.AddToClassList(HdbfIndoorPanelStyle.Row);
            _modeHost.style.flexWrap = Wrap.Wrap;
            _modeHost.style.marginTop = 4f;
            _modeHost.style.marginBottom = 2f;

            var modeLabel = HdbfPanelControls.CreateLabel(_loc.T(
                HDBF.UI.Localization.HdbfUiLocKeys.PanelMode),
                HdbfIndoorPanelStyle.RowLabel);
            modeLabel.style.marginRight = 8f;
            modeLabel.style.flexGrow = 0f;
            _modeHost.Add(modeLabel);

            try
            {
                // Vanilla radio-button group (Core/RadioButton template + .radio-button USS classes):
                // normal / dense / fallow with the vanilla selected-state sprite (requirement F).
                _modeToggle = _radioToggleFactory.CreateLocalizable(
                    new[]
                    {
                        HDBF.UI.Localization.HdbfUiLocKeys.ModeNormal,
                        HDBF.UI.Localization.HdbfUiLocKeys.ModeDense,
                        HDBF.UI.Localization.HdbfUiLocKeys.ModeFallow,
                    }, _modeHost);
                _modeToggle.RadioButtonSelected += OnModeSelected;
            }
            catch (Exception)
            {
                // Without the vanilla radio assets the mode row degrades to the meta line; the
                // panel (and every other building's panel) keeps working.
                _modeToggle = null;
            }

            _modeTransition = HdbfPanelControls.CreateNoteLabel(string.Empty);
            _modeTransition.style.marginTop = 2f;
        }

        private void BuildActionCard(VisualElement root)
        {
            // Vanilla action fragments are ordinary green sub-panel cards; the purple-striped tone
            // belongs to the entity-panel header and is deliberately not reused here.
            _actionCard = HdbfPanelControls.CreateCard("green");
            var actionTitleRow = HdbfPanelControls.CreateTitleRow(
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.PanelSectionActions),
                "maintenance", _icons);
            _actionTitle = actionTitleRow.Q<Label>();
            _actionCard.Add(actionTitleRow);

            _refillButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.ActionRefill), OnRefillClicked);
            _tooltips.Register(_refillButton, () => _lastData?.RefillTooltip ?? string.Empty);
            _refillButton.style.alignSelf = Align.Stretch;
            _actionCard.Add(_refillButton);
            _refillReason = HdbfPanelControls.CreateNoteLabel(string.Empty);
            _refillReason.style.marginTop = 2f;
            _actionCard.Add(_refillReason);

            // WP-08: settlement-wide refill. Each farm pays its own 2 farm actions and needs its own
            // reachable source, so this is a convenience button, not a free-water shortcut.
            _refillAllButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.ActionRefillAll), OnRefillAllClicked);
            _tooltips.Register(_refillAllButton,
                () => _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.TooltipRefillAll));
            _refillAllButton.style.alignSelf = Align.Stretch;
            _refillAllButton.style.marginTop = 4f;
            _actionCard.Add(_refillAllButton);

            _maintenanceButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.ActionMaintenance), OnMaintenanceClicked);
            _tooltips.Register(_maintenanceButton, () => _lastData?.MaintenanceTooltip ?? string.Empty);
            _maintenanceButton.style.alignSelf = Align.Stretch;
            _maintenanceButton.style.marginTop = 4f;
            _actionCard.Add(_maintenanceButton);
            _maintenanceReason = HdbfPanelControls.CreateNoteLabel(string.Empty);
            _maintenanceReason.style.marginTop = 2f;
            _actionCard.Add(_maintenanceReason);

            // WP-02: the per-building automatic-maintenance toggle is a vanilla game button whose
            // text carries the current On/Off state; pressing it flips the persisted choice. The
            // maintenance card repeats the state as a read-only row, so the setting is visible even
            // with the action card scrolled away.
            _autoMaintenanceButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                string.Empty, OnAutoMaintenanceClicked);
            _tooltips.Register(_autoMaintenanceButton,
                () => _lastData?.AutoMaintenanceTooltip ?? string.Empty);
            _autoMaintenanceButton.style.alignSelf = Align.Stretch;
            _autoMaintenanceButton.style.marginTop = 4f;
            _actionCard.Add(_autoMaintenanceButton);

            // One readable line for the last action result (success or the specific failure).
            _actionStatus = HdbfPanelControls.CreateNoteLabel(string.Empty);
            _actionStatus.style.marginTop = 4f;
            _actionCard.Add(_actionStatus);

            root.Add(_actionCard);
        }

        // ------------------------------------------------------------------ entity helpers
        private static Guid ResolveEntityId(Timberborn.BaseComponentSystem.BaseComponent entity)
        {
            try
            {
                var components = entity.AllComponents;
                for (var index = 0; index < components.Count; index++)
                {
                    if (components[index] is Timberborn.EntitySystem.EntityComponent entityComponent)
                    {
                        return entityComponent.EntityId;
                    }
                }
            }
            catch (Exception)
            {
                // A half-destroyed entity is simply "not a farm" for the panel.
            }

            return Guid.Empty;
        }
    }
}
