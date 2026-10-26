// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Applies the localized HdbfIndoorPanelData read-model onto the UIElements tree.
//          No value formatting happens here: every string was produced by the pure factory from
//          the Core snapshot. State is shown as text, never by colour alone.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-WATER-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        private void Render(HdbfIndoorPanelData data)
        {
            if (_root is null)
            {
                return;
            }

            if (!data.HasData)
            {
                Hide();
                return;
            }

            Set(_title, data.Title);
            RenderHeader(data);
            RenderStatus(data);
            RenderProblems(data);
            RenderSlots(data);
            RenderWater(data);
            RenderMaintenance(data);
            SetHeader(_powerHeader, HdbfUiLocKeys.PanelSectionPower);
            Set(_power, data.PowerText);
            SetTooltip(_power, data.PowerTooltip);
            RenderCrop(data);
            RenderOutput(data);
            RenderActionMessage(_actionMessage);

            _root.style.display = DisplayStyle.Flex;
        }

        private void RenderHeader(HdbfIndoorPanelData data)
        {
            Set(_tier, data.TierText);
            Set(_faction, data.FactionText);
            Set(_workers, data.WorkersText);
            SetOptional(_modeTransition, data.ModeTransitionText);
            RenderModeButtons(data);
        }

        private void RenderModeButtons(HdbfIndoorPanelData data)
        {
            RenderModeButton(_modeNormal, IndoorMode.Normal, data.ModeValue);
            RenderModeButton(_modeDense, IndoorMode.Dense, data.ModeValue);
            RenderModeButton(_modeFallow, IndoorMode.Fallow, data.ModeValue);
        }

        private void RenderModeButton(Button? button, IndoorMode mode, IndoorMode current)
        {
            if (button is null)
            {
                return;
            }

            button.text = _loc.T(HdbfIndoorPanelFormat.ModeKey(mode));
            button.tooltip = mode == current
                ? _loc.T(HdbfUiLocKeys.TooltipCurrentMode)
                : ModeTooltip(mode);
            // The active mode is the selected segment; the other two stay operable.
            button.SetEnabled(mode != current);
        }

        private string ModeTooltip(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Dense:
                    return _loc.T(HdbfUiLocKeys.TooltipModeDense);
                case IndoorMode.Fallow:
                    return _loc.T(HdbfUiLocKeys.TooltipModeFallow);
                default:
                    return _loc.T(HdbfUiLocKeys.TooltipModeNormal);
            }
        }

        private void RenderStatus(HdbfIndoorPanelData data)
        {
            var rows = data.StatusRows;
            var hasRows = rows is not null && rows.Count > 0;
            SetHeader(_statusHeader, HdbfUiLocKeys.PanelStatus);
            if (_statusHeader is not null)
            {
                _statusHeader.style.display = hasRows ? DisplayStyle.Flex : DisplayStyle.None;
            }

            for (var index = 0; index < _statusLabels.Count; index++)
            {
                var label = _statusLabels[index];
                if (rows is not null && index < rows.Count)
                {
                    // The text marker is the first carrier: colour is never the only signal.
                    label.text = rows[index].Marker + " " + rows[index].Text;
                    label.style.display = DisplayStyle.Flex;
                }
                else
                {
                    label.style.display = DisplayStyle.None;
                }
            }
        }

        private void RenderProblems(HdbfIndoorPanelData data)
        {
            var rows = data.ProblemRows;
            var hasRows = rows is not null && rows.Count > 0;
            SetHeader(_problemsHeader, HdbfUiLocKeys.PanelProblems);
            if (_problemsHeader is not null)
            {
                _problemsHeader.style.display = hasRows ? DisplayStyle.Flex : DisplayStyle.None;
            }

            for (var index = 0; index < _problemLabels.Count; index++)
            {
                var label = _problemLabels[index];
                if (hasRows && index < rows!.Count)
                {
                    label.text = "• " + rows[index];
                    label.style.display = DisplayStyle.Flex;
                }
                else
                {
                    label.style.display = DisplayStyle.None;
                }
            }
        }

        private void RenderSlots(HdbfIndoorPanelData data)
        {
            SetHeader(_slotsHeader, HdbfUiLocKeys.PanelSectionSlots);
            Set(_slots, data.SlotsText);
            Set(_effectiveSlots, data.EffectiveSlotsText);
            SetTooltip(_effectiveSlots, data.EffectiveSlotsTooltip);
            Set(_emptySlots, data.EmptySlotsText);
            Set(_growingSlots, data.GrowingSlotsText);
            Set(_matureSlots, data.MatureSlotsText);
            Set(_backlog, data.BacklogText);
        }

        private void RenderWater(HdbfIndoorPanelData data)
        {
            SetHeader(_waterHeader, HdbfUiLocKeys.PanelSectionWater);
            Set(_tank, data.TankText);
            SetTooltip(_tank, data.TankTooltip);
            Set(_coverage, data.CoverageText);
            SetTooltip(_coverage, data.CoverageTooltip);
            Set(_minCoverage, data.MinCoverageText);
            Set(_waterSupply, data.WaterSupplyText);
            SetOptional(_waterShortage, data.WaterShortageText);
            if (_refillButton is not null)
            {
                _refillButton.text = _loc.T(HdbfUiLocKeys.ActionRefill);
                _refillButton.tooltip = data.RefillTooltip;
                _refillButton.SetEnabled(data.CanRefill);
                _refillButton.style.unityFontStyleAndWeight =
                    data.CanRefill ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void RenderMaintenance(HdbfIndoorPanelData data)
        {
            SetHeader(_maintenanceHeader, HdbfUiLocKeys.PanelSectionMaintenance);
            Set(_maintenanceDue, data.MaintenanceDueText);
            Set(_maintenanceCycles, data.MaintenanceCyclesText);
            SetOptional(_maintenanceMaterials, data.MaintenanceMaterialsText);
            SetTooltip(_maintenanceDue, data.MaintenanceTooltip);
            if (_maintenanceButton is not null)
            {
                _maintenanceButton.text = _loc.T(HdbfUiLocKeys.ActionMaintenance);
                _maintenanceButton.tooltip = data.MaintenanceTooltip;
                _maintenanceButton.SetEnabled(data.CanPerformMaintenance);
                // Bold text (asset-free emphasis) marks the urgent action; the status rows above
                // already name the reason in words.
                _maintenanceButton.style.unityFontStyleAndWeight =
                    data.MaintenanceUrgent && data.CanPerformMaintenance ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void RenderOutput(HdbfIndoorPanelData data)
        {
            SetHeader(_outputHeader, HdbfUiLocKeys.PanelSectionOutput);
            Set(_outputStock, data.OutputStockText);
            SetTooltip(_outputStock, data.OutputTooltip);
            Set(_outputPort, data.OutputPortText);
            Set(_ports, data.PortsText);
        }

        private void RenderActionMessage(string message)
        {
            if (_actionStatus is null)
            {
                return;
            }

            _actionStatus.text = message;
            _actionStatus.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void SetActionMessage(string message)
        {
            _actionMessage = message ?? string.Empty;
            RenderActionMessage(_actionMessage);
        }

        private void SetHeader(Label? header, string key)
        {
            if (header is not null)
            {
                header.text = _loc.T(key);
            }
        }

        private static void Set(Label? label, string text)
        {
            if (label is not null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private static void SetOptional(Label? label, string text)
        {
            if (label is null)
            {
                return;
            }

            label.text = text ?? string.Empty;
            label.style.display = string.IsNullOrEmpty(label.text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private static void SetTooltip(VisualElement? element, string tooltip)
        {
            if (element is not null)
            {
                element.tooltip = tooltip ?? string.Empty;
            }
        }
    }
}
