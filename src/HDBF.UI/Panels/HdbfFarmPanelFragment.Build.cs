// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Construction of the v2 indoor panel UIElements tree (chapter 19.11 block order).
//          Code-only: no UXML/USS asset and no Unity prefab (the game extension point is the
//          code-built IEntityPanelFragment). Rendering is in .Render.cs.
// Requirements: REQ-V2-UI-01/03, REQ-V2-READ-04
// ============================================================================
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        private VisualElement? _root;
        private Label? _title;
        private Label? _statusHeader;
        private readonly List<Label> _statusLabels = new(MaxStatusRows);
        private Label? _problemsHeader;
        private readonly List<Label> _problemLabels = new(MaxProblemRows);
        private Label? _tier;
        private Label? _faction;
        private Label? _workers;
        private Label? _modeTransition;
        private Button? _modeNormal;
        private Button? _modeDense;
        private Button? _modeFallow;
        private Label? _slotsHeader;
        private Label? _slots;
        private Label? _effectiveSlots;
        private Label? _emptySlots;
        private Label? _growingSlots;
        private Label? _matureSlots;
        private Label? _backlog;
        private Label? _waterHeader;
        private Label? _tank;
        private Label? _coverage;
        private Label? _minCoverage;
        private Label? _waterSupply;
        private Label? _waterShortage;
        private Button? _refillButton;
        private Label? _maintenanceHeader;
        private Label? _maintenanceDue;
        private Label? _maintenanceCycles;
        private Label? _maintenanceMaterials;
        private Button? _maintenanceButton;
        private Label? _powerHeader;
        private Label? _power;
        private Label? _cropsHeader;
        private Label? _cropPolicy;
        private Label? _fixedCrop;
        private Label? _rotation;
        private DropdownField? _cropDropdown;
        private Label? _outputHeader;
        private Label? _outputStock;
        private Label? _outputPort;
        private Label? _ports;
        private Label? _actionStatus;
        private readonly List<string> _cropChoiceIds = new();

        private VisualElement BuildRoot()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.marginTop = 4f;
            root.style.display = DisplayStyle.None;

            // ----- title + status strip -------------------------------------------------------
            _title = AddLabel(root);
            _statusHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelStatus);
            AddIndentedLabels(root, MaxStatusRows, _statusLabels);
            _problemsHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelProblems);
            AddIndentedLabels(root, MaxProblemRows, _problemLabels);

            // ----- header block: tier / faction / workers / mode + transition ------------------
            _tier = AddLabel(root);
            _faction = AddLabel(root);
            _workers = AddLabel(root);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.alignItems = Align.Center;
            _modeNormal = AddModeButton(modeRow, IndoorMode.Normal);
            _modeDense = AddModeButton(modeRow, IndoorMode.Dense);
            _modeFallow = AddModeButton(modeRow, IndoorMode.Fallow);
            root.Add(modeRow);
            _modeTransition = AddLabel(root);
            _modeTransition.style.whiteSpace = WhiteSpace.Normal;

            // ----- slots -----------------------------------------------------------------------
            _slotsHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelSectionSlots);
            _slots = AddLabel(root);
            _effectiveSlots = AddLabel(root);
            _emptySlots = AddLabel(root);
            _growingSlots = AddLabel(root);
            _matureSlots = AddLabel(root);
            _backlog = AddLabel(root);

            // ----- water -----------------------------------------------------------------------
            _waterHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelSectionWater);
            _tank = AddLabel(root);
            _coverage = AddLabel(root);
            _minCoverage = AddLabel(root);
            _waterSupply = AddLabel(root);
            _waterShortage = AddLabel(root);
            _waterShortage.style.whiteSpace = WhiteSpace.Normal;
            _refillButton = AddActionButton(root, HdbfUiLocKeys.ActionRefill, OnRefillClicked);

            // ----- maintenance -----------------------------------------------------------------
            _maintenanceHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelSectionMaintenance);
            _maintenanceDue = AddLabel(root);
            _maintenanceCycles = AddLabel(root);
            _maintenanceMaterials = AddLabel(root);
            _maintenanceMaterials.style.whiteSpace = WhiteSpace.Normal;
            _maintenanceButton = AddActionButton(root, HdbfUiLocKeys.ActionMaintenance, OnMaintenanceClicked);

            // ----- power -----------------------------------------------------------------------
            _powerHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelSectionPower);
            _power = AddLabel(root);

            // ----- crop strategy ---------------------------------------------------------------
            _cropsHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelSectionCrops);
            _cropPolicy = AddLabel(root);
            _fixedCrop = AddLabel(root);
            _rotation = AddLabel(root);
            _cropDropdown = new DropdownField(_loc.T(HdbfUiLocKeys.PanelCropPolicy));
            _cropDropdown.style.marginTop = 2f;
            _cropDropdown.RegisterValueChangedCallback(_ => OnCropChoiceChanged());
            root.Add(_cropDropdown);

            // ----- output ----------------------------------------------------------------------
            _outputHeader = AddSectionHeader(root, HdbfUiLocKeys.PanelSectionOutput);
            _outputStock = AddLabel(root);
            _outputPort = AddLabel(root);
            _ports = AddLabel(root);
            _ports.style.whiteSpace = WhiteSpace.Normal;

            // ----- result line of the last action ----------------------------------------------
            _actionStatus = AddLabel(root);
            _actionStatus.style.display = DisplayStyle.None;
            _actionStatus.style.whiteSpace = WhiteSpace.Normal;

            _root = root;
            return root;
        }

        private Button AddModeButton(VisualElement parent, IndoorMode mode)
        {
            // Focusable + NavigationSubmit (Enter) are UIElements defaults; set explicitly so a
            // later refactor cannot drop keyboard access.
            var button = new Button(() => OnModeClicked(mode))
            {
                focusable = true,
            };
            button.style.marginRight = 4f;
            parent.Add(button);
            return button;
        }

        private Button AddActionButton(VisualElement parent, string labelKey,
            System.Action onClick)
        {
            var button = new Button(onClick)
            {
                text = _loc.T(labelKey),
                focusable = true,
            };
            button.style.marginTop = 2f;
            parent.Add(button);
            return button;
        }

        private static Label AddLabel(VisualElement parent)
        {
            var label = new Label();
            parent.Add(label);
            return label;
        }

        private static Label AddSectionHeader(VisualElement parent, string labelKey)
        {
            var label = AddLabel(parent);
            label.style.marginTop = 6f;
            return label;
        }

        private static void AddIndentedLabels(VisualElement parent, int count, List<Label> labels)
        {
            for (var index = 0; index < count; index++)
            {
                var label = AddLabel(parent);
                label.style.marginLeft = 8f;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.display = DisplayStyle.None;
                labels.Add(label);
            }
        }
    }
}
