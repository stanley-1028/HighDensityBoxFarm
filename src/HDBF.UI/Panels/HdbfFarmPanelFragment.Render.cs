// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Applies the localized HdbfIndoorPanelData read model onto the vanilla UIElements tree:
//          header card, the ordered cards (workers/efficiency, slots, water, maintenance, power,
//          crop strategy, output), the action card and the one-line action result. No value
//          formatting happens here: every string was produced by the pure factory from the Core
//          snapshot, and state is always shown as text, never by colour alone.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-WATER-03
// ============================================================================
using System.Collections.Generic;
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

            _lastData = data;
            RenderHeader(data);
            RenderCards(data);
            RenderActions(data);
            RenderActionMessage(_actionMessage);
            _root.style.display = DisplayStyle.Flex;
        }

        private void RenderHeader(HdbfIndoorPanelData data)
        {
            Set(_title, data.Title);
            Set(_subtitle, data.Subtitle);
            Set(_modeBadge, data.ModeBadge);
            SetOptional(_modeTransition, data.ModeTransitionText);
            _modeToggle?.Update((int)data.Mode);

            if (_headerIcon is not null)
            {
                // Null sprite (no badge/icon) keeps the vanilla avatar frame; nothing throws.
                _headerIcon.sprite = _avatarSprite;
            }

            if (_headerCard is not null)
            {
                HdbfPanelControls.SetTooltip(_headerCard, data.HeaderTooltip);
            }
        }

        private void RenderCards(HdbfIndoorPanelData data)
        {
            if (_cards is null)
            {
                return;
            }

            _cards.Clear();
            for (var index = 0; index < data.Sections.Count; index++)
            {
                var section = data.Sections[index];
                var card = HdbfPanelControls.CreateCard(section.Tone);
                card.Add(HdbfPanelControls.CreateSectionTitle(section, _icons));

                var critical = section.Kind == HdbfPanelSectionKind.Water && HasWarning(section.Rows);
                VisualElement? cropPolicyRow = null;
                for (var rowIndex = 0; rowIndex < section.Rows.Count; rowIndex++)
                {
                    var row = section.Rows[rowIndex];
                    if (row.IsWarning)
                    {
                        var warningRow = HdbfPanelControls.CreateWarningRow(row, _icons);
                        RegisterTooltip(warningRow, row.Tooltip);
                        card.Add(warningRow);
                        continue;
                    }

                    var rowElement = HdbfPanelControls.CreateRow(row, _icons, out _);
                    RegisterTooltip(rowElement, row.Tooltip);
                    card.Add(rowElement);
                    if (row.HasBar)
                    {
                        card.Add(HdbfPanelControls.CreateBar(row, critical));
                    }

                    if (section.Kind == HdbfPanelSectionKind.Crops && cropPolicyRow is null)
                    {
                        cropPolicyRow = rowElement;
                    }
                }

                if (section.Kind == HdbfPanelSectionKind.Workers)
                {
                    RenderWorkerRoster(card, data);
                }

                if (cropPolicyRow is not null && _cropHost is not null)
                {
                    // Requirement: one row = icon/label + current strategy text + the vanilla dropdown.
                    cropPolicyRow.Add(_cropHost);
                    RenderCrop(data);
                }

                _cards.Add(card);
            }
        }

        private void RenderWorkerRoster(VisualElement card, HdbfIndoorPanelData data)
        {
            var roster = new VisualElement();
            roster.AddToClassList(HdbfIndoorPanelStyle.WorkerRoster);
            roster.style.marginTop = 4f;
            for (var index = 0; index < data.WorkerChips.Count; index++)
            {
                var chip = HdbfPanelControls.CreateWorkerChip(_visualElementLoader, data.WorkerChips[index], _icons);
                RegisterTooltip(chip, data.WorkerChips[index].Tooltip);
                roster.Add(chip);
            }

            card.Add(roster);

            var note = new Label(data.WorkerRosterNote);
            note.AddToClassList(HdbfIndoorPanelStyle.SubText);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginTop = 2f;
            card.Add(note);

            if (_modeHost is not null)
            {
                card.Add(_modeHost);
            }

            if (_modeTransition is not null)
            {
                card.Add(_modeTransition);
            }
        }

        private void RenderActions(HdbfIndoorPanelData data)
        {
            if (_refillButton is not null)
            {
                _refillButton.text = _loc.T(HdbfUiLocKeys.ActionRefill);
                _refillButton.tooltip = data.RefillTooltip;
                _refillButton.SetEnabled(data.CanRefill);
            }

            SetOptional(_refillReason, data.RefillReasonText);

            if (_maintenanceButton is not null)
            {
                _maintenanceButton.text = _loc.T(HdbfUiLocKeys.ActionMaintenance);
                _maintenanceButton.tooltip = data.MaintenanceTooltip;
                _maintenanceButton.SetEnabled(data.CanPerformMaintenance);
                _maintenanceButton.style.unityFontStyleAndWeight =
                    data.MaintenanceUrgent && data.CanPerformMaintenance ? FontStyle.Bold : FontStyle.Normal;
            }

            SetOptional(_maintenanceReason, data.MaintenanceReasonText);
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

        private static bool HasWarning(IReadOnlyList<HdbfPanelRow> rows)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].IsWarning)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Registers the vanilla hover tooltip of a freshly created row/chip element.</summary>
        private void RegisterTooltip(VisualElement element, string? tooltip)
        {
            if (!string.IsNullOrEmpty(tooltip))
            {
                _tooltips.Register(element, tooltip!);
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
    }
}
