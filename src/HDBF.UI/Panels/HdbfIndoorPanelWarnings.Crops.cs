// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Crop-list half of the v2 panel warnings: the fixed-crop dropdown choices built from the
//          live snapshot crop table (L-12, with the built-in ten-crop fallback) and the
//          known-crop check that keeps a live crop-mod crop out of the "invalid crop" warning.
//          Split from HdbfIndoorPanelWarnings.cs so both files stay inside the 300-line gate.
// Requirements: REQ-V2-EXT-01, REQ-V2-UI-02/03, REQ-V2-READ-04
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public static partial class HdbfIndoorPanelWarnings
    {
        /// <summary>
        /// True when the fixed crop id belongs to the faction's live crop list (L-12), so a crop
        /// accepted by the command layer is never reported as invalid by the panel. Only when the
        /// snapshot carries no live list (older snapshots/tests) is the built-in table consulted.
        /// </summary>
        private static bool IsKnownCrop(IndoorFarmSnapshot snapshot, string? cropId)
        {
            if (string.IsNullOrEmpty(cropId))
            {
                return false;
            }

            var live = snapshot.CropChoices;
            if (live is not null && live.Count > 0)
            {
                for (var index = 0; index < live.Count; index++)
                {
                    var choice = live[index];
                    if (choice is not null
                        && string.Equals(choice.Id, cropId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            return FallbackCropCatalog.Default.TryGet(snapshot.Faction, cropId!, out _);
        }

        /// <summary>
        /// Dropdown items: automatic rotation first, then the faction crop table. The live list of
        /// the snapshot wins (L-12: a crop-mod crop is a real choice with its own localized name);
        /// only a snapshot without a live list falls back to the built-in ten-crop table.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot.</param>
        /// <param name="localize">Localization resolver.</param>
        /// <returns>Ordered dropdown choices; item text is the localized display text.</returns>
        public static IReadOnlyList<HdbfCropChoice> BuildCropChoices(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var rotation = new HdbfCropChoice
            {
                Id = string.Empty,
                Text = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPolicyRotation),
            };
            var live = snapshot.CropChoices;
            if (live is not null && live.Count > 0)
            {
                var liveChoices = new List<HdbfCropChoice>(live.Count + 1) { rotation };
                for (var index = 0; index < live.Count; index++)
                {
                    var crop = live[index];
                    if (crop is null || string.IsNullOrEmpty(crop.Id))
                    {
                        continue;
                    }

                    liveChoices.Add(new HdbfCropChoice
                    {
                        Id = crop.Id,
                        Text = HdbfIndoorPanelFormat.CropName(localize, live, crop.Id),
                    });
                }

                return liveChoices;
            }

            var crops = FallbackCropCatalog.Default.ForFaction(snapshot.Faction);
            var choices = new List<HdbfCropChoice>(crops.Count + 1) { rotation };
            for (var index = 0; index < crops.Count; index++)
            {
                var crop = crops[index];
                if (crop is null || string.IsNullOrEmpty(crop.Id))
                {
                    continue;
                }

                choices.Add(new HdbfCropChoice
                {
                    Id = crop.Id,
                    Text = HdbfIndoorPanelFormat.CropName(localize, crop.Id),
                });
            }

            return choices;
        }
    }
}
