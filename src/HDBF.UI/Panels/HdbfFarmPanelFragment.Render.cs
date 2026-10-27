// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Applies the localized HdbfIndoorPanelData read model onto the vanilla UIElements tree:
//          the compact tier/faction subtitle row, the ordered cards (workers/efficiency,
//          slots, water, maintenance, power, crop strategy, output) and the action card. No value
//          formatting happens here: every string was produced by the pure factory from the Core
//          snapshot. The scroller height is re-measured at most 4 times per second so the action
//          card stays reachable on every window size (requirement F), and state is always shown as
//          text, never by colour alone.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-WATER-03
// ============================================================================
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
            RenderCards(data);
            RenderActions(data);
            RenderActionMessage(_actionMessage);
            UpdateScrollHeight();
            if (_resetScrollOnNextRender && _scroll is not null)
            {
                // A newly selected farm always opens at the top of the card stack (the reported
                // "missing sections" screenshot was the previous selection's scroll position).
                _resetScrollOnNextRender = false;
                _scroll.scrollOffset = Vector2.zero;
            }

            _root.style.display = DisplayStyle.Flex;
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
                if (section.Kind == HdbfPanelSectionKind.Workers)
                {
                    // Requirement A: the compact tier/faction/mode line sits directly under the
                    // vanilla header as the first card row; the building name is never repeated.
                    card.Add(HdbfPanelControls.CreateMetaRow(data.MetaLine, data.MetaTooltip, _icons));
                }

                card.Add(HdbfPanelControls.CreateTitleRow(section.Title, section.IconKey, _icons));
                var critical = section.Tone == "red";
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

                    var rowElement = HdbfPanelControls.CreateRow(row, _icons, out var valueLabel);
                    RegisterTooltip(rowElement, row.Tooltip);
                    card.Add(rowElement);
                    if (row.HasBar)
                    {
                        card.Add(HdbfPanelControls.CreateBar(row, critical));
                    }

                    if (section.Kind == HdbfPanelSectionKind.Crops && cropPolicyRow is null)
                    {
                        cropPolicyRow = rowElement;
                        if (_cropDropdown is not null)
                        {
                            // The vanilla dropdown shows the current policy itself; hiding the
                            // static value avoids the duplicated "auto rotation [auto rotation]".
                            valueLabel.style.display = DisplayStyle.None;
                        }
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
                var chip = HdbfPanelControls.CreateWorkerChip(_visualElementLoader,
                    data.WorkerChips[index], _icons);
                RegisterTooltip(chip, data.WorkerChips[index].Tooltip);
                roster.Add(chip);
            }

            card.Add(roster);

            var note = HdbfPanelControls.CreateLabel(data.WorkerRosterNote,
                HdbfIndoorPanelStyle.SubText, 12f);
            note.style.marginTop = 2f;
            card.Add(note);

            // Vanilla RadioToggle mode group (requirement F) + the one-day transition sentence.
            _modeToggle?.Update((int)data.Mode);
            if (_modeHost is not null)
            {
                card.Add(_modeHost);
            }

            if (_modeTransition is not null)
            {
                SetOptional(_modeTransition, data.ModeTransitionText);
                card.Add(_modeTransition);
            }
        }

        private void RenderActions(HdbfIndoorPanelData data)
        {
            if (_actionTitle is not null)
            {
                _actionTitle.text = _loc.T(HdbfUiLocKeys.PanelSectionActions);
            }

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

        /// <summary>
        /// Re-measures the card scroller so the action card always fits the current window: the
        /// scroll view takes the space between the (vanilla) fragments above it and the action card,
        /// clamped to a sane range. Cheap and allocation free; called from the 4 Hz update gate and
        /// only writes the style when the value actually changed.
        /// </summary>
        private void UpdateScrollHeight()
        {
            if (_scroll is null || _root is null)
            {
                return;
            }

            float limit;
            try
            {
                var scale = _root.panel?.scaledPixelsPerPoint ?? 1f;
                if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                {
                    scale = 1f;
                }

                var screenPoints = Screen.height / scale;
                var top = _root.worldBound.y;
                if (float.IsNaN(top) || float.IsInfinity(top) || top <= 0f)
                {
                    // Not laid out yet: fall back to the vanilla panel top plus the header area.
                    top = 380f;
                }

                var actionHeight = _actionCard is null
                                   || float.IsNaN(_actionCard.resolvedStyle.height)
                                   || _actionCard.resolvedStyle.height < 40f
                    ? 150f
                    : _actionCard.resolvedStyle.height;
                limit = screenPoints - top - actionHeight - BottomReserve;
            }
            catch (System.Exception)
            {
                limit = InitialScrollHeight;
            }

            if (float.IsNaN(limit) || float.IsInfinity(limit))
            {
                limit = InitialScrollHeight;
            }

            limit = Mathf.Clamp(limit, MinScrollHeight, MaxScrollHeight);
            if (Mathf.Abs(limit - _lastScrollHeight) < 1f)
            {
                return;
            }

            _lastScrollHeight = limit;
            _scroll.style.maxHeight = limit;
        }

        /// <summary>Registers the vanilla hover tooltip of a freshly created row/chip element.</summary>
        private void RegisterTooltip(VisualElement element, string? tooltip)
        {
            if (!string.IsNullOrEmpty(tooltip))
            {
                _tooltips.Register(element, tooltip!);
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
