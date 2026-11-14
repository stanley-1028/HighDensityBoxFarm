// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Applies the localized HdbfIndoorPanelData read model onto the vanilla UIElements tree:
//          the action card, the one action-result line, the worker roster/mode group and the
//          scroller height. The card stack itself (and its per-card content diff) lives in
//          HdbfFarmPanelFragment.Cards.cs; this file no longer rebuilds the cards. No value
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

        private void RenderWorkerRoster(VisualElement body, HdbfIndoorPanelData data)
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

            body.Add(roster);

            var note = HdbfPanelControls.CreateLabel(data.WorkerRosterNote,
                HdbfIndoorPanelStyle.SubText);
            note.style.marginTop = 2f;
            body.Add(note);

            // Vanilla RadioToggle mode group (requirement F) + the one-day transition sentence.
            _modeToggle?.Update((int)data.Mode);
            if (_modeHost is not null)
            {
                body.Add(_modeHost);
            }

            if (_modeTransition is not null)
            {
                SetOptional(_modeTransition, data.ModeTransitionText);
                body.Add(_modeTransition);
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

            if (_autoMaintenanceButton is not null)
            {
                _autoMaintenanceButton.text = AutoMaintenanceText(data.AutoMaintenance);
                _autoMaintenanceButton.tooltip = data.AutoMaintenanceTooltip;
                _autoMaintenanceButton.style.unityFontStyleAndWeight =
                    data.AutoMaintenance ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        /// <summary>
        /// Localized "Automatic maintenance: On/Off" label of the WP-02 toggle. The read model only
        /// carries the boolean, so the renderer composes the two shipped short labels.
        /// </summary>
        /// <param name="enabled">Current automatic-maintenance choice.</param>
        /// <returns>One-line toggle label with the current state.</returns>
        private string AutoMaintenanceText(bool enabled)
        {
            return _loc.T(HdbfUiLocKeys.PanelAutoMaintenanceLabel) + ": " + _loc.T(enabled
                ? HdbfUiLocKeys.PanelAutoMaintenanceOn
                : HdbfUiLocKeys.PanelAutoMaintenanceOff);
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
