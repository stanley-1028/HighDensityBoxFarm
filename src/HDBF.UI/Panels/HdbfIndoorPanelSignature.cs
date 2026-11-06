// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Content signature (dirty check) of ONE card of the indoor entity panel. The fragment
//          re-renders the read model at most 4 times per second; rebuilding every card on every
//          tick would tear down live controls that live inside a card - most visibly the vanilla
//          crop-strategy Dropdown, whose DetachFromPanel callback (verified in the installed
//          Timberborn.DropdownSystem.dll) hides the open popup. The signature lets the fragment
//          rebuild only the cards whose rendered content actually changed, so an unchanged card
//          (and therefore the dropdown) stays attached and a click is never interrupted.
//          Game-free: HDBF.Core + the panel read model only, so L1 can drive it.
// Requirements: REQ-V2-UI-01 (dirty mark / 4 Hz), REQ-V2-UI-02/03, REQ-V2-PERF-02, REQ-V2-TEST-01
// ============================================================================
using System;
using HDBF.Core.Indoor;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Value signature of one <see cref="HdbfPanelSection"/> as the fragment renders it. Equal
    /// signatures mean "the built card would show the same model content", so the existing element
    /// tree can be kept instead of being cleared and rebuilt. The fields mixed here are the fields
    /// <c>BuildSectionCard</c> renders; the crop dropdown's own items/selected text/tooltip are
    /// deliberately out of band: <c>RenderCrop</c> re-syncs them on every refresh, so they can
    /// change without rebuilding the card (the whole point - the dropdown must stay attached).
    /// Collisions are harmless but not self-healing: the card is refreshed by the next non-colliding
    /// content change (the panel fingerprint only decides whether the read model is recomputed, it
    /// does not repair a skipped card by itself).
    /// </summary>
    public static class HdbfIndoorPanelSignature
    {
        /// <summary>
        /// Computes the signature of one card. Null arguments return 0 (the fragment hides the
        /// panel in that case), and a change of <paramref name="data"/> only matters for the extras
        /// the worker card renders (subtitle, roster, mode, transition).
        /// </summary>
        /// <param name="section">Card model to be rendered.</param>
        /// <param name="data">Whole panel read model (extras of the worker card).</param>
        /// <returns>Stable 64-bit signature; equal means an identical card.</returns>
        public static long ForSection(HdbfPanelSection? section, HdbfIndoorPanelData? data)
        {
            if (section is null || data is null)
            {
                return 0L;
            }

            unchecked
            {
                var hash = 17L;
                hash = hash * 31 + (int)section.Kind;
                hash = hash * 31 + HashOf(section.Title);
                hash = hash * 31 + HashOf(section.IconKey);
                hash = hash * 31 + HashOf(section.Tone);

                var rows = section.Rows;
                hash = hash * 31 + (rows is null ? 0 : rows.Count);
                for (var index = 0; rows is not null && index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row is null)
                    {
                        hash = hash * 31 + 1;
                        continue;
                    }

                    hash = hash * 31 + HashOf(row.IconKey);
                    hash = hash * 31 + HashOf(row.Label);
                    hash = hash * 31 + HashOf(row.Value);
                    hash = hash * 31 + HashOf(row.Tooltip);
                    hash = hash * 31 + (row.IsWarning ? 1 : 0);
                    hash = hash * 31 + HashOf(row.Marker);
                    hash = hash * 31 + (row.HasBar ? 1 : 0);
                    hash = hash * 31 + Quantize(row.Bar);
                    hash = hash * 31 + HashOf(row.BarText);
                    hash = hash * 31 + Quantize(row.BarMarker);
                }

                // The worker card renders more than its rows: the compact subtitle row, the worker
                // roster chips, the vanilla mode radio group and the one-day transition sentence.
                // A card that contains no interactive control other than the crop dropdown does not
                // need more than the rows above.
                if (section.Kind == HdbfPanelSectionKind.Workers)
                {
                    hash = hash * 31 + HashOf(data.MetaLine);
                    hash = hash * 31 + HashOf(data.MetaTooltip);
                    hash = hash * 31 + HashOf(data.WorkerRosterNote);
                    hash = hash * 31 + (int)data.Mode;
                    hash = hash * 31 + HashOf(data.ModeTransitionText);

                    var chips = data.WorkerChips;
                    hash = hash * 31 + (chips is null ? 0 : chips.Count);
                    for (var index = 0; chips is not null && index < chips.Count; index++)
                    {
                        var chip = chips[index];
                        if (chip is null)
                        {
                            hash = hash * 31 + 1;
                            continue;
                        }

                        hash = hash * 31 + HashOf(chip.Name);
                        hash = hash * 31 + (chip.Vacant ? 1 : 0);
                        hash = hash * 31 + HashOf(chip.Tooltip);
                    }
                }

                return hash;
            }
        }

        private static int HashOf(string? value)
        {
            return value is null ? 0 : value.GetHashCode();
        }

        /// <summary>Quantizes a bar value to 1/1000 so float jitter cannot force a rebuild.</summary>
        private static long Quantize(float value)
        {
            if (float.IsNaN(value))
            {
                return long.MinValue;
            }

            if (float.IsPositiveInfinity(value))
            {
                return long.MaxValue;
            }

            if (float.IsNegativeInfinity(value))
            {
                return long.MinValue + 1L;
            }

            return (long)(value * 1000f);
        }
    }
}
