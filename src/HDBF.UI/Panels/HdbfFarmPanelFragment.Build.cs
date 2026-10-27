// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Construction of the v2 indoor panel UIElements skeleton. There is deliberately NO
//          second header: the vanilla EntityPanel already renders the building icon, name and
//          district, so the fragment starts with the full-width card scroller (first card = the
//          compact tier/faction/mode row + workers/efficiency). Cards are contiguous and span the
//          whole 325 px panel, the scroller reserves no scrollbar gutter and the fragment root is
//          transparent, so the tan entity-panel background can no longer show through as a big
//          "brown box" (requirements A/B/C). Card content is filled by .Render.cs; the vanilla
//          mode radio group, crop dropdown and the action card are built here.
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
        private Button? _maintenanceButton;
        private Label? _maintenanceReason;
        private Label? _actionStatus;
        private Label? _actionTitle;

        private VisualElement BuildRoot()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            // No margin/padding/background: the root is transparent and the cards own the dark
            // backgrounds (requirement C). A margin here would expose the tan panel background.
            root.style.marginTop = 0f;
            root.style.display = DisplayStyle.None;
            // Inheritance safety net: every text in this subtree is warm white even if a vanilla
            // template forgets to set a colour (requirement D).
            HdbfPanelControls.ApplyLightText(root);

            BuildCardScroller(root);
            BuildModeToggle();
            BuildCropDropdown();
            BuildActionCard(root);

            _root = root;
            return root;
        }

        private void BuildCardScroller(VisualElement root)
        {
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            HdbfPanelControls.AddClasses(_scroll, HdbfIndoorPanelStyle.Scroll);
            // Requirement C: a visible scroller would reserve a transparent gutter on the right
            // where the tan panel background shows through. Hidden takes no space; wheel scrolling
            // still works because ScrollView handles the wheel event itself.
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
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
                HdbfIndoorPanelStyle.RowLabel, 13f);
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
                PaintRadioButtonText();
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

        /// <summary>Paints the vanilla radio-button labels with the one light panel text colour.</summary>
        private void PaintRadioButtonText()
        {
            if (_modeHost is null)
            {
                return;
            }

            for (var index = 0; index < _modeHost.childCount; index++)
            {
                var child = _modeHost[index];
                HdbfPanelControls.ApplyLightText(child);
                var label = child.Q<Label>();
                if (label is not null)
                {
                    HdbfPanelControls.ApplyLightText(label);
                }
            }
        }

        private void BuildActionCard(VisualElement root)
        {
            _actionCard = HdbfPanelControls.CreateCard("purple");
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

            _maintenanceButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.ActionMaintenance), OnMaintenanceClicked);
            _tooltips.Register(_maintenanceButton, () => _lastData?.MaintenanceTooltip ?? string.Empty);
            _maintenanceButton.style.alignSelf = Align.Stretch;
            _maintenanceButton.style.marginTop = 4f;
            _actionCard.Add(_maintenanceButton);
            _maintenanceReason = HdbfPanelControls.CreateNoteLabel(string.Empty);
            _maintenanceReason.style.marginTop = 2f;
            _actionCard.Add(_maintenanceReason);

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
