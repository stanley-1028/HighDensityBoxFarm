// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Fold state of the v2 indoor panel. Every card can be clamped with the game's own
//          collapse idiom (.extension-clamp): folding only switches the card body's display, it
//          never removes the card from the stack, never changes a card signature and never asks the
//          read model to refresh. The state lives here - not in the read model - because the 4 Hz
//          refresh rebuilds a card element whenever its content signature changes; a fragment-side
//          override survives that rebuild and is re-applied by BuildSectionCard.
// Requirements: REQ-V2-UI-01, REQ-V2-UI-04, REQ-V2-PERF-02
// ============================================================================
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    public sealed partial class HdbfFarmPanelFragment
    {
        /// <summary>
        /// Player fold overrides, keyed by card kind. Absence of a key means "use the default for
        /// this card"; the dictionary is cleared whenever the panel is shown for a new entity.
        /// </summary>
        private readonly Dictionary<HdbfPanelSectionKind, bool> _foldOverrides = new(8);

        /// <summary>
        /// Effective open state of one card: an explicit player choice wins, then cards that carry
        /// warnings are always open (a warning hidden by a default fold would be a lie), then the
        /// per-kind default. The interactive/live cards start open, the reference-only cards start
        /// clamped, which is what keeps the vanilla-width panel short enough to read.
        /// </summary>
        /// <param name="kind">Card kind.</param>
        /// <param name="hasWarnings">True when the card carries warning rows.</param>
        /// <returns>True when the body is visible.</returns>
        private bool IsFoldOpen(HdbfPanelSectionKind kind, bool hasWarnings)
        {
            if (_foldOverrides.TryGetValue(kind, out var open))
            {
                return open;
            }

            if (hasWarnings)
            {
                return true;
            }

            switch (kind)
            {
                case HdbfPanelSectionKind.Workers:
                case HdbfPanelSectionKind.Growing:
                case HdbfPanelSectionKind.Crops:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Flips one card and repaints the mounted body/clamp. Pure layout: no signature, no model,
        /// no rebuild (the crop dropdown stays attached and an open popup stays open).
        /// </summary>
        private void ToggleFold(HdbfPanelSectionKind kind, bool hasWarnings, VisualElement body,
            Button? clamp)
        {
            var open = !IsFoldOpen(kind, hasWarnings);
            _foldOverrides[kind] = open;
            ApplyFold(body, clamp, open);
        }

        /// <summary>Applies the vanilla clamp classes and the body visibility.</summary>
        private static void ApplyFold(VisualElement body, Button? clamp, bool open)
        {
            if (body is not null)
            {
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }

            HdbfPanelControls.ApplyClampState(clamp, open);
        }

        /// <summary>Restores the default fold layout (a newly selected entity never inherits folds).</summary>
        private void ResetFolds()
        {
            _foldOverrides.Clear();
        }
    }
}
