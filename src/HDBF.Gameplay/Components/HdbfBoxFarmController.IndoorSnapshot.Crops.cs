// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Live crop data of the panel snapshot (L-12): the faction crop table of the game-backed
//          catalog exposed as Core IndoorCropChoice rows (rebuilt only when the catalog returns a
//          new table) plus the rotation-sequence text. Split from .IndoorSnapshot so every file
//          stays inside the 300-line gate.
// Requirements: REQ-V2-EXT-01, REQ-V2-UI-03/05, REQ-V2-PERF-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Indoor;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        private IReadOnlyList<CropDefinition>? _cropChoicesSource;
        private IReadOnlyList<IndoorCropChoice> _cropChoices = Array.Empty<IndoorCropChoice>();

        /// <summary>
        /// Crop list the panel offers for the fixed-crop dropdown (L-12), read from the live
        /// game-backed catalog of the building faction. The list is rebuilt only when the catalog
        /// returns a different table (a crop mod (re)loaded its specs, or the catalog was reset), so
        /// a 4 Hz panel read does not re-map the table (REQ-V2-PERF-02).
        /// </summary>
        /// <returns>Live choices; empty when the catalog is broken or empty (panel falls back).</returns>
        private IReadOnlyList<IndoorCropChoice> LiveCropChoices()
        {
            try
            {
                var crops = _cropCatalog.ForFaction(_faction);
                if (crops is null || crops.Count == 0)
                {
                    _cropChoicesSource = null;
                    _cropChoices = Array.Empty<IndoorCropChoice>();
                    return _cropChoices;
                }

                if (ReferenceEquals(crops, _cropChoicesSource))
                {
                    return _cropChoices;
                }

                var choices = new List<IndoorCropChoice>(crops.Count);
                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    if (crop is null || string.IsNullOrEmpty(crop.Id))
                    {
                        continue;
                    }

                    choices.Add(new IndoorCropChoice
                    {
                        Id = crop.Id,
                        DisplayNameLocKey = crop.DisplayNameLocKey,
                    });
                }

                _cropChoicesSource = crops;
                _cropChoices = choices.Count == 0
                    ? Array.Empty<IndoorCropChoice>()
                    : choices.ToArray();
                return _cropChoices;
            }
            catch (Exception)
            {
                // A broken catalog degrades to "no live list": the panel then uses its fallback
                // table and the built-in names instead of hiding the card (REQ-V2-SAFE-01).
                _cropChoicesSource = null;
                _cropChoices = Array.Empty<IndoorCropChoice>();
                return _cropChoices;
            }
        }

        /// <summary>
        /// Crop rotation sequence shown by the panel: the faction crop table in order (fixed mode
        /// shows the fixed crop). The string is built once per farm and cached; this method does not
        /// allocate in the steady state (REQ-V2-PERF-02).
        /// </summary>
        private string RotationSequence()
        {
            var record = _record;
            if (record is null)
            {
                return string.Empty;
            }

            if (record.CropPolicy == CropPolicy.Fixed)
            {
                return record.FixedCropId;
            }

            if (!string.IsNullOrEmpty(_rotationSequence))
            {
                return _rotationSequence;
            }

            try
            {
                var crops = _cropCatalog.ForFaction(_faction);
                if (crops is null || crops.Count == 0)
                {
                    return string.Empty;
                }

                var builder = new System.Text.StringBuilder();
                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    if (crop is null || string.IsNullOrEmpty(crop.Id))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append(" > ");
                    }

                    builder.Append(crop.Id);
                    if (builder.Length > 160)
                    {
                        break;
                    }
                }

                _rotationSequence = builder.ToString();
                return _rotationSequence;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
