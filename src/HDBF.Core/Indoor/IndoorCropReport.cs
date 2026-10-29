// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Builds the panel's per-crop live status list (REQ-V2-UI-05): one stable row per
//          crop that currently occupies slots, with the growing/mature split and the average
//          growth progress. Pure function over the record + crop catalog; no game type, no
//          allocation in the empty case, deterministic output order.
// Requirements: REQ-V2-UI-05, REQ-V2-CORE-04, REQ-V2-READ-04, REQ-V2-PERF-02
// ============================================================================
using System;
using System.Collections.Generic;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Aggregates the occupied slots of one indoor farm into <see cref="IndoorCropStatus"/> rows
    /// (REQ-V2-UI-05). The panel renders one row per crop from the first planted slot on, so a
    /// half-planted farm is visible instead of only a fully planted one.
    ///
    /// Ordering contract: the faction crop table order first (the rotation order the panel already
    /// shows), then any crop id the catalog does not know, sorted ordinally. The order never depends
    /// on dictionary iteration, so the same record always produces the same list.
    /// </summary>
    public static class IndoorCropReport
    {
        /// <summary>
        /// Upper bound of rows the panel card may receive. A faction table ships at most ten crops,
        /// so this only truncates corrupted/unknown data and keeps the card bounded (REQ-V2-SAFE-01).
        /// </summary>
        public const int MaxCrops = 12;

        private static readonly IReadOnlyList<IndoorCropStatus> NoCrops = Array.Empty<IndoorCropStatus>();
        private static readonly IReadOnlyList<CropDefinition> NoDefinitions = Array.Empty<CropDefinition>();

        /// <summary>
        /// Builds the live per-crop status list of one farm. Never throws and never returns null:
        /// a missing record, a missing catalog or a farm with nothing planted yields
        /// <see cref="Array.Empty{T}"/>.
        /// </summary>
        /// <param name="record">Farm record whose slots are aggregated; null means "not ready".</param>
        /// <param name="catalog">Crop catalog of the faction; null degrades to crop-id-only rows.</param>
        /// <returns>Ordered, immutable status rows (see the ordering contract on the type).</returns>
        public static IReadOnlyList<IndoorCropStatus> Build(IndoorFarmRecord? record, ICropCatalog? catalog)
        {
            var slots = record?.Slots;
            if (record is null || slots is null || slots.Length == 0)
            {
                return NoCrops;
            }

            var faction = record.Faction;
            Dictionary<string, Accumulator>? byCrop = null;
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (slot is null || string.IsNullOrEmpty(slot.CropId))
                {
                    continue;
                }

                // Empty/unknown stages are not "growing": CountSlots in the controller and this
                // aggregation must agree, otherwise the card and the slot counters drift apart.
                if (slot.Stage != SlotStage.Growing && slot.Stage != SlotStage.Mature)
                {
                    continue;
                }

                byCrop ??= new Dictionary<string, Accumulator>(StringComparer.Ordinal);
                if (!byCrop.TryGetValue(slot.CropId, out var accumulator))
                {
                    var definition = Lookup(catalog, faction, slot.CropId);
                    accumulator = new Accumulator(
                        slot.CropId,
                        definition is null ? string.Empty : definition.YieldItemId,
                        definition is null ? 0f : definition.GrowthTimeInDays);
                    byCrop.Add(slot.CropId, accumulator);
                }

                accumulator.Add(SlotProgress(slot, accumulator.GrowthTimeInDays));
            }

            if (byCrop is null || byCrop.Count == 0)
            {
                return NoCrops;
            }

            var rows = new List<IndoorCropStatus>(byCrop.Count);
            var crops = CatalogCrops(catalog, faction);
            for (var index = 0; index < crops.Count && rows.Count < MaxCrops; index++)
            {
                var crop = crops[index];
                if (crop is null || string.IsNullOrEmpty(crop.Id) || !byCrop.ContainsKey(crop.Id))
                {
                    continue;
                }

                rows.Add(byCrop[crop.Id].ToStatus());
                byCrop.Remove(crop.Id);
            }

            if (byCrop.Count > 0)
            {
                var unknownIds = new List<string>(byCrop.Keys);
                unknownIds.Sort(StringComparer.Ordinal);
                for (var index = 0; index < unknownIds.Count && rows.Count < MaxCrops; index++)
                {
                    rows.Add(byCrop[unknownIds[index]].ToStatus());
                }
            }

            return rows.Count == 0 ? NoCrops : rows.ToArray();
        }

        /// <summary>
        /// Growth progress of one slot in [0,1] against the crop growth time. A mature slot counts
        /// as 1 (it reached its growth time), an empty/invalid slot as 0. Never returns NaN/Infinity
        /// (REQ-V2-READ-04: unknown numbers must not reach the panel).
        /// </summary>
        /// <param name="slot">Slot to measure; null counts as 0.</param>
        /// <param name="growthTimeInDays">Growth time of the slot crop; non-positive counts as 0.</param>
        /// <returns>Progress in [0,1].</returns>
        public static float SlotProgress(IndoorSlot? slot, float growthTimeInDays)
        {
            if (slot is null || slot.Stage == SlotStage.Empty)
            {
                return 0f;
            }

            if (slot.Stage == SlotStage.Mature)
            {
                return 1f;
            }

            if (slot.Stage != SlotStage.Growing || !IndoorMath.IsPositiveFinite(growthTimeInDays))
            {
                return 0f;
            }

            if (!IndoorMath.IsPositiveFinite(slot.GrowthDays))
            {
                return 0f;
            }

            return IndoorMath.Clamp01(slot.GrowthDays / growthTimeInDays);
        }

        /// <summary>Crop table of the faction; a null or throwing catalog degrades to "no table".</summary>
        private static IReadOnlyList<CropDefinition> CatalogCrops(ICropCatalog? catalog, HdbfFaction faction)
        {
            if (catalog is null)
            {
                return NoDefinitions;
            }

            try
            {
                return catalog.ForFaction(faction) ?? NoDefinitions;
            }
            catch (Exception)
            {
                // A broken spec reader must not blank the whole card (REQ-V2-CORE-05).
                return NoDefinitions;
            }
        }

        /// <summary>One crop definition lookup; a missing/throwing catalog yields null.</summary>
        private static CropDefinition? Lookup(ICropCatalog? catalog, HdbfFaction faction, string cropId)
        {
            if (catalog is null)
            {
                return null;
            }

            try
            {
                return catalog.TryGet(faction, cropId, out var crop) ? crop : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Mutable per-crop sum while the slots are scanned once.</summary>
        private sealed class Accumulator
        {
            private float _progress;

            /// <summary>Creates the accumulator from the resolved crop definition (or unknown data).</summary>
            /// <param name="cropId">Crop template id of the row; never empty.</param>
            /// <param name="yieldItemId">Harvest good id, empty when the spec is unknown.</param>
            /// <param name="growthTimeInDays">Growth time in days, 0 when the spec is unknown.</param>
            public Accumulator(string cropId, string yieldItemId, float growthTimeInDays)
            {
                CropId = cropId;
                YieldItemId = yieldItemId;
                GrowthTimeInDays = growthTimeInDays;
            }

            /// <summary>Crop template id of the row.</summary>
            public string CropId { get; }

            /// <summary>Harvest good id; empty when the spec is unknown.</summary>
            public string YieldItemId { get; }

            /// <summary>Growth time in game days; 0 when the spec is unknown.</summary>
            public float GrowthTimeInDays { get; }

            private int GrowingSlots { get; set; }

            private int MatureSlots { get; set; }

            /// <summary>Adds one occupied slot with its already computed progress.</summary>
            /// <param name="progress">Progress in [0,1] of that slot.</param>
            public void Add(float progress)
            {
                if (progress >= 1f)
                {
                    MatureSlots++;
                }
                else
                {
                    GrowingSlots++;
                }

                _progress += progress;
            }

            /// <summary>Freezes the mutable sum into the immutable snapshot row.</summary>
            /// <returns>The row the panel renders.</returns>
            public IndoorCropStatus ToStatus()
            {
                var total = GrowingSlots + MatureSlots;
                var average = total <= 0 ? 0f : _progress / total;
                return new IndoorCropStatus(CropId, YieldItemId, GrowingSlots, MatureSlots,
                    IndoorMath.Clamp01(average), GrowthTimeInDays);
            }
        }
    }
}
