// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Construction of the v2 indoor panel UIElements skeleton: a vanilla entity header card
//          (avatar + name + subtitle + mode badge), the scrollable card stack, the vanilla mode
//          radio group host, the crop-dropdown host and the action card (refill/maintenance
//          buttons with their inline reasons + the one-line result). Card content is filled by
//          .Render.cs; the code-only design needs no UXML/USS asset of its own.
// Requirements: REQ-V2-UI-01/03, REQ-V2-READ-04
// ============================================================================
using System;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.EntitySystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        private VisualElement? _root;
        private NineSliceVisualElement? _headerCard;
        private Image? _headerIcon;
        private Label? _title;
        private Label? _subtitle;
        private Label? _modeBadge;
        private ScrollView? _scroll;
        private VisualElement? _cards;
        private VisualElement? _modeHost;
        private RadioToggle? _modeToggle;
        private Label? _modeTransition;
        private Button? _refillButton;
        private Label? _refillReason;
        private Button? _maintenanceButton;
        private Label? _maintenanceReason;
        private Label? _actionStatus;

        private VisualElement BuildRoot()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.marginTop = 4f;
            root.style.display = DisplayStyle.None;

            BuildHeader(root);
            BuildCardScroller(root);
            BuildModeToggle();
            BuildCropDropdown();
            BuildActionCard(root);

            _root = root;
            return root;
        }

        private void BuildHeader(VisualElement root)
        {
            _headerCard = new NineSliceVisualElement();
            _headerCard.AddToClassList(HdbfIndoorPanelStyle.HeaderCard);
            // The vanilla header reserves 60 px on the right for the description-hider button, which
            // is not part of this sub-header; keep 4 px so the mode badge fits the 325 px panel.
            _headerCard.style.paddingRight = 4f;

            _headerIcon = new Image();
            _headerIcon.AddToClassList(HdbfIndoorPanelStyle.HeaderAvatar);
            _headerIcon.pickingMode = PickingMode.Ignore;
            _headerCard.Add(_headerIcon);

            var texts = new VisualElement();
            texts.AddToClassList(HdbfIndoorPanelStyle.HeaderTexts);
            _title = new Label();
            _title.AddToClassList(HdbfIndoorPanelStyle.HeaderName);
            texts.Add(_title);
            _subtitle = new Label();
            _subtitle.AddToClassList(HdbfIndoorPanelStyle.HeaderSubtitle);
            texts.Add(_subtitle);
            _headerCard.Add(texts);

            // Mode badge: the vanilla small green sub-box, never colour-only (it carries the text).
            var badge = HdbfPanelControls.CreateBadge(string.Empty, out var badgeLabel);
            _modeBadge = badgeLabel;
            _headerCard.Add(badge);
            _tooltips.Register(_headerCard, () => _lastData?.HeaderTooltip ?? string.Empty);

            root.Add(_headerCard);
        }

        private void BuildCardScroller(VisualElement root)
        {
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList(HdbfIndoorPanelStyle.Scroll);
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _scroll.style.maxHeight = ScrollHeightCap;
            _scroll.style.marginTop = 4f;

            _cards = new VisualElement();
            _cards.style.flexDirection = FlexDirection.Column;
            _scroll.Add(_cards);
            root.Add(_scroll);
        }

        private void BuildModeToggle()
        {
            _modeHost = new VisualElement();
            _modeHost.AddToClassList(HdbfIndoorPanelStyle.Row);
            _modeHost.style.marginTop = 4f;
            _modeHost.style.flexWrap = Wrap.Wrap;

            try
            {
                // Vanilla radio-button group (Core/RadioButton template + .radio-button USS classes).
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
                // Without the vanilla radio assets the mode row degrades to the header badge; the
                // panel (and every other building's panel) keeps working.
                _modeToggle = null;
            }

            _modeTransition = new Label();
            _modeTransition.AddToClassList(HdbfIndoorPanelStyle.SubText);
            _modeTransition.style.whiteSpace = WhiteSpace.Normal;
            _modeTransition.style.display = DisplayStyle.None;
        }

        private void BuildActionCard(VisualElement root)
        {
            var actions = HdbfPanelControls.CreateCard("purple");
            var actionsTitle = new Label(_loc.T(HDBF.UI.Localization.HdbfUiLocKeys.PanelSectionActions));
            actionsTitle.AddToClassList(HdbfIndoorPanelStyle.SectionTitle);
            actionsTitle.style.marginBottom = 4f;
            actions.Add(actionsTitle);

            _refillButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.ActionRefill), OnRefillClicked);
            _tooltips.Register(_refillButton, () => _lastData?.RefillTooltip ?? string.Empty);
            _refillButton.style.alignSelf = Align.Stretch;
            actions.Add(_refillButton);
            _refillReason = CreateReasonLabel();
            actions.Add(_refillReason);

            _maintenanceButton = HdbfPanelControls.CreateButton(_visualElementLoader,
                _loc.T(HDBF.UI.Localization.HdbfUiLocKeys.ActionMaintenance), OnMaintenanceClicked);
            _tooltips.Register(_maintenanceButton, () => _lastData?.MaintenanceTooltip ?? string.Empty);
            _maintenanceButton.style.alignSelf = Align.Stretch;
            _maintenanceButton.style.marginTop = 4f;
            actions.Add(_maintenanceButton);
            _maintenanceReason = CreateReasonLabel();
            actions.Add(_maintenanceReason);

            // One readable line for the last action result (success or the specific failure).
            _actionStatus = new Label();
            _actionStatus.AddToClassList(HdbfIndoorPanelStyle.SubText);
            _actionStatus.style.whiteSpace = WhiteSpace.Normal;
            _actionStatus.style.marginTop = 4f;
            _actionStatus.style.display = DisplayStyle.None;
            actions.Add(_actionStatus);

            root.Add(actions);
        }

        // ------------------------------------------------------------------ entity helpers
        private Sprite? ResolveAvatar(BaseComponent entity)
        {
            try
            {
                // The vanilla badge of the building already carries the tier icon of the blueprint
                // (Icons/BoxFarmBasic|Intermediate|Advanced|Top), so the header shows exactly the
                // build-menu icon. A missing badge/icon is a silent text-only fallback.
                return _badges.GetEntityAvatar(entity);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Guid ResolveEntityId(BaseComponent entity)
        {
            try
            {
                var components = entity.AllComponents;
                for (var index = 0; index < components.Count; index++)
                {
                    if (components[index] is EntityComponent entityComponent)
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


        private static Label CreateReasonLabel()
        {
            var label = new Label();
            label.AddToClassList(HdbfIndoorPanelStyle.SubText);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 2f;
            label.style.display = DisplayStyle.None;
            return label;
        }
    }
}
