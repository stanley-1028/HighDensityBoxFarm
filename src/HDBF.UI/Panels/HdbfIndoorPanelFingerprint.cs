// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Cheap value fingerprint of the indoor panel snapshot (the 4 Hz dirty check). It mixes
//          every value the panel displays plus one translated string, so an unchanged farm never
//          re-formats a label and a localization switch still forces one refresh. Pure BCL + Core
//          DTO math: no game type, no allocation beyond the (empty) argument array.
// Requirements: REQ-V2-UI-01, REQ-V2-READ-04, REQ-V2-PERF-02
// ============================================================================
using System;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Value fingerprint behind the fragment's dirty check (REQ-V2-UI-01). A collision only costs
    /// one skipped refresh; an unchanged fingerprint never touches a label. Kept as a separate pure
    /// file so the panel mapping and the fingerprint can each be tested offline and every UI file
    /// stays inside the 300 line gate.
    /// </summary>
    public static class HdbfIndoorPanelFingerprint
    {
        /// <summary>
        /// Computes the fingerprint of one snapshot for the current language.
        /// </summary>
        /// <param name="snapshot">Read-only farm snapshot; null returns 0 (the panel hides anyway).</param>
        /// <param name="localize">Localization resolver, normally <c>ILoc.T</c>; null is tolerated.</param>
        /// <returns>Stable 64-bit value fingerprint (collisions are harmless).</returns>
        public static long Compute(IndoorFarmSnapshot? snapshot, Func<string, string>? localize)
        {
            if (snapshot is null)
            {
                return 0L;
            }

            unchecked
            {
                // A localization switch changes every rendered string without changing the snapshot;
                // probing one translated section title makes the dirty check catch it.
                var hash = 17L;
                try
                {
                    var title = localize?.Invoke(HdbfUiLocKeys.PanelSectionSlots);
                    hash += title is null ? 0L : title.GetHashCode();
                }
                catch (Exception)
                {
                    // A broken localization provider must not break rendering.
                }

                hash = hash * 31 + (int)snapshot.Tier;
                hash = hash * 31 + (int)snapshot.Faction;
                hash = hash * 31 + snapshot.Workers;
                hash = hash * 31 + snapshot.MaxWorkers;
                hash = hash * 31 + (int)snapshot.Mode;
                hash = hash * 31 + (snapshot.ModeTransitioning ? 1 : 0);
                hash = hash * 31 + (snapshot.Paused ? 1 : 0);
                hash = hash * 31 + snapshot.Capacity;
                hash = hash * 31 + snapshot.ConfiguredSlots;
                hash = hash * 31 + snapshot.EffectiveSlots;
                hash = hash * 31 + snapshot.EmptySlots;
                hash = hash * 31 + snapshot.GrowingSlots;
                hash = hash * 31 + snapshot.MatureSlots;
                hash = hash * 31 + snapshot.BacklogActions;
                hash = hash * 31 + Quantize(snapshot.Tank);
                hash = hash * 31 + Quantize(snapshot.TankCapacity);
                hash = hash * 31 + Quantize(snapshot.TankDaysRemaining);
                hash = hash * 31 + Quantize(snapshot.Coverage);
                hash = hash * 31 + Quantize(snapshot.MinCoverage);
                hash = hash * 31 + (int)snapshot.WaterSupply;
                hash = hash * 31 + Quantize(snapshot.MaintenanceDueDay);
                hash = hash * 31 + snapshot.MaintenanceCyclesMissed;
                hash = hash * 31 + (snapshot.MaintenanceMaterialMissing ? 1 : 0);
                // WP-01/WP-02: the automatic choice and the catch-up bill of the next click.
                hash = hash * 31 + (snapshot.AutoMaintenance ? 1 : 0);
                hash = hash * 31 + snapshot.MaintenanceCyclesToCatchUp;
                hash = hash * 31 + snapshot.MaintenanceCatchUpMaterialAmount;
                // WP-06: the status line is driven by the reason; the manual-source flag feeds the
                // water-refill hint of the snapshot contract.
                hash = hash * 31 + (int)snapshot.IdleReason;
                hash = hash * 31 + (snapshot.ManualRefillSourceAvailable ? 1 : 0);
                hash = hash * 31 + (int)snapshot.Power;
                hash = hash * 31 + (snapshot.PowerConnected ? 1 : 0);
                hash = hash * 31 + (int)snapshot.CropPolicy;
                hash = hash * 31 + (snapshot.FixedCropId is null ? 0 : snapshot.FixedCropId.GetHashCode());
                hash = hash * 31 + (snapshot.RotationSequence is null ? 0 : snapshot.RotationSequence.GetHashCode());
                hash = hash * 31 + snapshot.OutputOccupied;
                hash = hash * 31 + (snapshot.OutputPortConnected ? 1 : 0);
                hash = hash * 31 + (snapshot.PipePort ? 1 : 0);
                hash = hash * 31 + (snapshot.LogisticsPort ? 1 : 0);
                hash = hash * 31 + (snapshot.PowerPort ? 1 : 0);
                hash = hash * 31 + (int)snapshot.Warnings;
                hash = hash * 31 + (snapshot.OutputInventoryPortState is null
                    ? 0
                    : snapshot.OutputInventoryPortState.GetHashCode());
                // WP-05: the probe reason/counts are rendered by the pipe port row.
                hash = hash * 31 + (int)snapshot.PipeProbeState.Reason;
                hash = hash * 31 + snapshot.PipeProbeState.TankCount;
                hash = hash * 31 + snapshot.PipeProbeState.WaterTankCount;
                // REQ-V2-UI-05: the live "Growing now" rows change without any scalar above changing,
                // so the dirty check mixes each crop's identity, slot split and growth progress.
                var crops = snapshot.Crops;
                hash = hash * 31 + (crops is null ? 0 : crops.Count);
                for (var index = 0; crops is not null && index < crops.Count; index++)
                {
                    var crop = crops[index];
                    hash = hash * 31 + crop.CropId.GetHashCode();
                    hash = hash * 31 + crop.GrowingSlots;
                    hash = hash * 31 + crop.MatureSlots;
                    hash = hash * 31 + (long)(crop.AverageProgress * 100f);
                }

                return hash;
            }
        }

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

            return (long)(value * 1000f);
        }
    }
}
