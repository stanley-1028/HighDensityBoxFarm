// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Growth, yield and fatigue-yield formulas of an indoor slot (INTERFACES
//          section 5, section 19.5). Pure functions, no game objects, no state.
// Requirements: REQ-V2-CORE-04, REQ-V2-CORE-05, REQ-V2-BAL-01
// ============================================================================
using System;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Indoor growth and yield mathematics (INTERFACES section 5). Every function is pure and
    /// allocation free so the same inputs always produce the same outputs (REQ-V2-CORE-04).
    /// </summary>
    public static class IndoorGrowthMath
    {
        /// <summary>G_fat = 1 - 0.60 x F with F clamped to [0,1] (section 19.5).</summary>
        /// <param name="fatigue">Slot fatigue; out-of-range values are clamped.</param>
        /// <returns>Yield multiplier in [0.40, 1.00].</returns>
        public static float FatigueYieldMultiplier(float fatigue)
        {
            return 1f - IndoorConstants.FatigueYieldPenalty * IndoorMath.Clamp01(fatigue);
        }

        /// <summary>G_mode: normal 1.00, dense 1.35, fallow 0 (fallow never harvests).</summary>
        /// <param name="mode">Operating mode of the farm.</param>
        /// <returns>Mode yield multiplier.</returns>
        public static float ModeYieldMultiplier(IndoorMode mode)
        {
            switch (mode)
            {
                case IndoorMode.Dense:
                    return IndoorConstants.DenseYieldMultiplier;
                case IndoorMode.Fallow:
                    return 0f;
                default:
                    return IndoorConstants.NormalModeMultiplier;
            }
        }

        /// <summary>
        /// Harvest yield formula: <c>BaseYield x G_fat x G_mode</c> (section 19.5). The raw float is
        /// returned so the panel can show fractions; inventory uses <see cref="HarvestItems"/>.
        /// </summary>
        /// <param name="baseYield">Crop base yield per harvest.</param>
        /// <param name="fatigue">Slot fatigue at harvest time.</param>
        /// <param name="mode">Operating mode at harvest time.</param>
        /// <returns>Yield in items, before integer rounding; 0 for fallow/invalid input.</returns>
        public static float Yield(int baseYield, float fatigue, IndoorMode mode)
        {
            return Yield(baseYield, fatigue, mode, 1f);
        }

        /// <summary>
        /// Harvest yield with an extra yield scale, used by the one-day mode transition to settle at
        /// the lower of the old/new mode yield (section 19.8). A scale outside [0,1] is clamped.
        /// </summary>
        /// <param name="baseYield">Crop base yield per harvest.</param>
        /// <param name="fatigue">Slot fatigue at harvest time.</param>
        /// <param name="mode">Operating mode at harvest time.</param>
        /// <param name="yieldScale">Extra scale in [0,1]; 1 means no transition.</param>
        /// <returns>Yield in items, before integer rounding; 0 for fallow/invalid input.</returns>
        public static float Yield(int baseYield, float fatigue, IndoorMode mode, float yieldScale)
        {
            if (baseYield <= 0)
            {
                return 0f;
            }

            var value = baseYield * FatigueYieldMultiplier(fatigue) * ModeYieldMultiplier(mode)
                        * IndoorMath.Clamp01(yieldScale);
            return IndoorMath.IsFinite(value) && value > 0f ? value : 0f;
        }

        /// <summary>
        /// Converts the float <see cref="Yield"/> into integer inventory items: round half up, and a
        /// positive simulated yield never becomes a wasted zero-item harvest (minimum 1).
        /// </summary>
        /// <param name="baseYield">Crop base yield per harvest.</param>
        /// <param name="fatigue">Slot fatigue at harvest time.</param>
        /// <param name="mode">Operating mode at harvest time.</param>
        /// <returns>Item count added to the output inventory; 0 for fallow/invalid input.</returns>
        public static int HarvestItems(int baseYield, float fatigue, IndoorMode mode)
        {
            return HarvestItems(baseYield, fatigue, mode, 1f);
        }

        /// <summary>Harvest items with the optional mode-transition yield scale (section 19.8).</summary>
        /// <param name="baseYield">Crop base yield per harvest.</param>
        /// <param name="fatigue">Slot fatigue at harvest time.</param>
        /// <param name="mode">Operating mode at harvest time.</param>
        /// <param name="yieldScale">Extra scale in [0,1]; 1 means no transition.</param>
        /// <returns>Item count added to the output inventory; 0 for fallow/invalid input.</returns>
        public static int HarvestItems(int baseYield, float fatigue, IndoorMode mode, float yieldScale)
        {
            var value = Yield(baseYield, fatigue, mode, yieldScale);
            if (!(value > 0f))
            {
                return 0;
            }

            var rounded = (int)Math.Floor(value + IndoorConstants.RoundHalfUp);
            return rounded < 1 ? 1 : rounded;
        }

        /// <summary>
        /// Yield scale of a one-game-day mode transition: settle the day at the lower of the old and
        /// the new mode yield (section 19.8). Returns 1 when there is no transition, when the mode is
        /// unchanged, or when the current mode has no yield (fallow never harvests anyway).
        /// </summary>
        /// <param name="current">Mode the farm switched to (the record mode).</param>
        /// <param name="previous">Mode before the switch, or null when no transition is pending.</param>
        /// <returns>Multiplier in [0,1] applied on top of the current G_mode.</returns>
        public static float ModeTransitionYieldScale(IndoorMode current, IndoorMode? previous)
        {
            if (previous is null || previous.Value == current)
            {
                return 1f;
            }

            var currentYield = ModeYieldMultiplier(current);
            if (!(currentYield > 0f))
            {
                return 1f;
            }

            var previousYield = ModeYieldMultiplier(previous.Value);
            var lower = previousYield < currentYield ? previousYield : currentYield;
            return IndoorMath.Clamp01(lower / currentYield);
        }

        /// <summary>
        /// Advances one growing slot: <c>progress += gameDays x G_growth</c> (section 19.5). Only
        /// non-empty, not-yet-mature slots move. A zero G_growth stops progress but never removes
        /// the crop, and reaching <c>GrowthTimeInDays</c> flips the slot to
        /// <see cref="SlotStage.Mature"/>, where it waits for a farmer.
        /// </summary>
        /// <param name="slot">Slot to advance; null is a no-op (defensive).</param>
        /// <param name="crop">Crop definition of the slot; null is a no-op (defensive).</param>
        /// <param name="gameDays">Game days elapsed this tick.</param>
        /// <param name="growthMultiplier">G_growth from <see cref="IndoorMultipliers.Growth"/>.</param>
        public static void Advance(IndoorSlot slot, CropDefinition crop, float gameDays, float growthMultiplier)
        {
            // REQ-V2-CORE-05: missing entity/spec is isolated, never fatal.
            if (slot is null || crop is null)
            {
                return;
            }

            if (slot.Stage != SlotStage.Growing || string.IsNullOrEmpty(slot.CropId))
            {
                return;
            }

            // G_growth = 0 (or an invalid step) stops progress; the crop stays in the slot.
            if (!IndoorMath.IsPositiveFinite(gameDays) || !IndoorMath.IsPositiveFinite(growthMultiplier))
            {
                return;
            }

            var growthTime = crop.GrowthTimeInDays;
            if (!IndoorMath.IsPositiveFinite(growthTime))
            {
                // Missing/invalid spec: mature immediately so the slot can be harvested instead of
                // being stuck forever. The load-time validator repairs the underlying data.
                slot.GrowthDays = 0f;
                slot.Stage = SlotStage.Mature;
                return;
            }

            var progress = IndoorMath.IsFinite(slot.GrowthDays) && slot.GrowthDays > 0f ? slot.GrowthDays : 0f;
            var next = progress + gameDays * growthMultiplier;
            if (!IndoorMath.IsFinite(next) || next <= progress)
            {
                return;
            }

            if (next >= growthTime)
            {
                slot.GrowthDays = growthTime;
                slot.Stage = SlotStage.Mature;
                return;
            }

            slot.GrowthDays = next;
        }

        /// <summary>
        /// Panel yield multiplier: G_mode x the average G_fat of the slots that currently hold a
        /// crop; with no crop there is no fatigue penalty, so only G_mode remains. Single pass, no
        /// allocation (REQ-V2-PERF-02). The optional scale reports the lower transition-day yield.
        /// </summary>
        internal static float PanelYieldMultiplier(IndoorFarmRecord record, IndoorSlot[]? slots, int limit)
        {
            return PanelYieldMultiplier(record, slots, limit, 1f);
        }

        /// <summary>Panel yield multiplier with the one-day mode-transition scale applied.</summary>
        internal static float PanelYieldMultiplier(IndoorFarmRecord record, IndoorSlot[]? slots, int limit,
            float transitionScale)
        {
            var modeMultiplier = ModeYieldMultiplier(record.Mode);
            if (slots is null || limit <= 0)
            {
                return modeMultiplier;
            }

            var sum = 0f;
            var count = 0;
            for (var index = 0; index < limit; index++)
            {
                var slot = slots[index];
                if (slot is null || string.IsNullOrEmpty(slot.CropId))
                {
                    continue;
                }

                sum += FatigueYieldMultiplier(slot.Fatigue);
                count++;
            }

            var fatigueAverage = count == 0 ? 1f : sum / count;
            return modeMultiplier * fatigueAverage * IndoorMath.Clamp01(transitionScale);
        }
    }
}
