// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: The fixed-order daily orchestration of one indoor farm (ARCHITECTURE
//          section 3) plus its context/result/action DTOs; no game objects.
// Requirements: REQ-V2-MAINT-03, REQ-V2-CORE-05, REQ-V2-SAFE-01, REQ-V2-BAL-01,
//               REQ-V2-PERF-02, REQ-V2-WATER-02, REQ-V2-LABOR-01
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>Inputs of one daily indoor farm tick (INTERFACES section 5).</summary>
    public sealed class IndoorDayContext
    {
        /// <summary>On-duty workers of the building; source of the daily action budget.</summary>
        public int ActiveWorkers { get; init; }
        /// <summary>Current game day used by the maintenance cycle countdown.</summary>
        public float CurrentDay { get; init; }
        /// <summary>True when the front entrance is blocked; pauses farm actions only.</summary>
        public bool EntranceBlocked { get; init; }
        /// <summary>Output inventory sink; may be null when the port/inventory is missing.</summary>
        public IInventorySink? Output { get; init; }
        /// <summary>Real water source for pipe/manual refill; may be null (no source).</summary>
        public IWaterSource? Water { get; init; }
        /// <summary>Nearest maintenance material stockpile; may be null.</summary>
        public IMaintenanceMaterialSource? Materials { get; init; }
        /// <summary>True when the pipe port is connected and water is expected from it.</summary>
        public bool HasPipeWater { get; init; }
        /// <summary>True when the player requested a manual refill trip this day.</summary>
        public bool ManualRefillRequested { get; init; }
    }

    /// <summary>Outcome of one daily indoor farm tick (INTERFACES section 5).</summary>
    public readonly struct IndoorDayResult
    {
        /// <summary>Tank coverage C after the daily settlement.</summary>
        public float Coverage { get; init; }
        /// <summary>G_growth = G_irr x G_maint x G_power used by the growth step.</summary>
        public float GrowthMultiplier { get; init; }
        /// <summary>Panel yield multiplier: G_mode x average G_fat of the slots holding a crop.</summary>
        public float YieldMultiplier { get; init; }
        /// <summary>Farm actions performed this day.</summary>
        public IndoorActionStats Actions { get; init; }
        /// <summary>All problems detected this day.</summary>
        public IndoorWarningFlags Warnings { get; init; }
        /// <summary>True when the tick changed persisted state (the save should be marked dirty).</summary>
        public bool Changed { get; init; }
        /// <summary>True when maintenance stalls farm actions (crops keep growing).</summary>
        public bool Stalled { get; init; }
    }

    /// <summary>Per-day action counters of <see cref="IndoorLaborMath.RunActions"/> (INTERFACES section 5).</summary>
    public struct IndoorActionStats
    {
        /// <summary>Mature slots harvested this day.</summary>
        public int Harvested;
        /// <summary>Empty slots planted this day.</summary>
        public int Planted;
        /// <summary>Actions consumed from the daily budget.</summary>
        public int ActionsUsed;
        /// <summary>Mature slots that could not be harvested because the output was full.</summary>
        public int BlockedByOutput;
        /// <summary>True when output-full blocked at least one harvest (panel backlog).</summary>
        public bool Backlog;
    }

    /// <summary>
    /// The fixed-order daily tick of one indoor farm (ARCHITECTURE section 3): entrance gate,
    /// water settle/refill, growth, farm actions, fatigue/rotation, maintenance/power, warnings and
    /// dirty flag. The order is observable and pinned by <c>V2TickOrderTests</c> (REQ-V2-MAINT-03).
    /// No exception escapes (REQ-V2-SAFE-01); the normal path allocates nothing and every loop is
    /// bounded by the slot capacity (REQ-V2-PERF-02, REQ-V2-SAFE-03).
    /// </summary>
    public static class IndoorDayTick
    {
        /// <summary>Runs one game day of the indoor farm (mutates <paramref name="record"/> in place).</summary>
        /// <param name="record">Farm record to simulate; null returns an empty result.</param>
        /// <param name="tier">Tier profile of the building; null returns an empty result.</param>
        /// <param name="catalog">Crop catalog; the built-in fallback covers missing specs.</param>
        /// <param name="context">Per-day world/services context; null means "nothing available".</param>
        /// <returns>The day result; never throws and never allocates on the normal path.</returns>
        public static IndoorDayResult Run(IndoorFarmRecord record, TierProfile tier, ICropCatalog catalog,
            in IndoorDayContext context)
        {
            if (record is null || tier is null) return new IndoorDayResult();
            var warnings = IndoorWarningFlags.None;
            var actions = new IndoorActionStats();
            var coverage = 0f;
            var growthMultiplier = 0f;
            var yieldMultiplier = 0f;
            var changed = false;
            var stalled = false;

            var capacity = tier.SlotCapacity;
            if (capacity < 0) capacity = 0;
            var slots = record.Slots;
            var limit = capacity;
            if (slots is null) limit = 0;
            else if (slots.Length < limit) limit = slots.Length;

            // Phase 1: entrance / on-duty gate (REQ-V2-BLD-02, REQ-V2-LABOR-03).
            var activeWorkers = context is null ? 0 : context.ActiveWorkers;
            if (activeWorkers < 0) activeWorkers = 0;
            var currentDay = context is null ? 0f : context.CurrentDay;
            if (!IndoorMath.IsFinite(currentDay)) currentDay = 0f;
            var entranceBlocked = context is not null && context.EntranceBlocked;
            var output = context is null ? null : context.Output;
            var water = context is null ? null : context.Water;
            var materials = context is null ? null : context.Materials;
            var hasPipeWater = context is not null && context.HasPipeWater;
            var manualRefillRequested = context is not null && context.ManualRefillRequested;
            if (entranceBlocked) warnings |= IndoorWarningFlags.EntranceBlocked;
            if (activeWorkers <= 0) warnings |= IndoorWarningFlags.NoWorkers;

            try
            {
                // Phase 2: water settle + refill (REQ-V2-WATER-02); pipe tops up before the deduction.
                var effectiveSlots = IndoorLaborMath.EffectiveSlots(tier, activeWorkers);
                var demand = IndoorWaterMath.DemandPerDay(effectiveSlots, tier.WaterPerSlotPerDay, record.Mode);
                if (hasPipeWater)
                {
                    var headroom = tier.TankCapacity - IndoorWaterMath.SanitizeTank(record.Tank, tier.TankCapacity);
                    var want = demand < headroom ? demand : headroom;
                    if (want > 0f && IndoorWaterMath.TryDrawSource(water, want, out var drawn))
                    {
                        IndoorWaterMath.TryRefill(record, drawn);
                    }
                    else if (want > 0f)
                    {
                        warnings |= IndoorWarningFlags.NoWater;
                    }
                }
                else if (manualRefillRequested)
                {
                    // REQ-V2-WATER-02/WATER-04: refill only from a real source, never without workers.
                    if (activeWorkers <= 0
                        || !IndoorWaterMath.TryDrawSource(water,
                            IndoorWaterMath.RefillAmount(tier.TankCapacity), out var drawn))
                    {
                        warnings |= IndoorWarningFlags.NoWater;
                    }
                    else
                    {
                        IndoorWaterMath.TryRefill(record, drawn);
                    }
                }

                var tankBefore = record.Tank;
                coverage = IndoorWaterMath.SettleDay(record, tier, effectiveSlots);
                if (record.Tank != tankBefore) changed = true;
                if (demand > 0f)
                {
                    if (coverage <= 0f) warnings |= IndoorWarningFlags.NoWater;
                    else if (coverage < tier.MinIrrigationCoverage) warnings |= IndoorWarningFlags.LowWater;
                }

                // Maintenance/power are pure reads of the persisted day; phase 6 persists them.
                var offGridMaintenance = tier.NeedsPower
                    && !(record.PowerMode == PowerConnection.Grid && record.PowerConnected);
                var cycleDays = MaintenanceDefaults.CycleDaysFor(tier, offGridMaintenance);
                var cycles = BalanceFormulas.MaintenanceCyclesMissed(record.MaintenanceDueDay, currentDay, cycleDays);
                stalled = IndoorMaintenanceMath.IsStalled(cycles);
                var needsGridPower = tier.NeedsPower && record.PowerMode == PowerConnection.Grid;

                // Phase 3: growth; G_growth is multiplied exactly once, in IndoorMultipliers.Growth.
                // REQ-V2-BAL-02: a maintenance stall keeps crops growing (v1 minimum growth rate),
                // it only disables farm actions below.
                var maintenanceForGrowth = stalled
                    ? IndoorConstants.StalledGrowthMultiplier
                    : IndoorMaintenanceMath.Efficiency(cycles);
                growthMultiplier = new IndoorMultipliers(
                    IndoorWaterMath.IrrigationMultiplier(coverage, tier.MinIrrigationCoverage),
                    maintenanceForGrowth,
                    IndoorPowerMath.Efficiency(needsGridPower, record.PowerConnected)).Growth;

                for (var index = 0; index < limit; index++)
                {
                    var slot = slots![index];
                    if (slot is null || slot.Stage != SlotStage.Growing || string.IsNullOrEmpty(slot.CropId))
                    {
                        continue;
                    }

                    try
                    {
                        if (!FallbackCropCatalog.TryResolve(catalog, record.Faction, slot.CropId, out var crop))
                        {
                            continue;
                        }

                        var before = slot.GrowthDays;
                        IndoorGrowthMath.Advance(slot, crop, IndoorConstants.DayTickGameDays, growthMultiplier);
                        if (slot.GrowthDays != before || slot.Stage == SlotStage.Mature) changed = true;
                    }
                    catch
                    {
                        // REQ-V2-SAFE-02: one broken slot must not stop the other slots.
                    }
                }

                // Phase 4: actions; entrance block and maintenance stall pause farm actions only.
                var actionsAllowed = !entranceBlocked && !stalled && record.Mode != IndoorMode.Fallow;
                actions = IndoorLaborMath.RunActions(record, tier, catalog, activeWorkers, output!, actionsAllowed);
                if (actions.Harvested > 0 || actions.Planted > 0) changed = true;
                if (actions.Backlog) warnings |= IndoorWarningFlags.OutputFull;

                // Phase 5: fatigue/rotation; fallow recovers fatigue, every slot is cleaned/clamped.
                var fallow = record.Mode == IndoorMode.Fallow;
                for (var index = 0; index < limit; index++)
                {
                    var slot = slots![index];
                    if (slot is null) continue;
                    try
                    {
                        var fatigue = fallow ? slot.Fatigue - IndoorConstants.FallowRecoveryPerDay : slot.Fatigue;
                        var cleaned = IndoorMath.Clamp01(fatigue);
                        if (cleaned != slot.Fatigue)
                        {
                            slot.Fatigue = cleaned;
                            changed = true;
                        }
                    }
                    catch
                    {
                        // REQ-V2-SAFE-02: isolate a broken slot.
                    }
                }

                // Phase 6: maintenance/power persistence + warnings (section 19.8).
                if (record.MaintenanceCyclesMissed != cycles)
                {
                    record.MaintenanceCyclesMissed = cycles;
                    changed = true;
                }

                if (needsGridPower && record.PowerConnected)
                {
                    // A grid-connected top tier is maintained by the network: keep its off-grid
                    // material countdown full so a later power cut does not stall it instantly.
                    var offGridCycle = MaintenanceDefaults.CycleDaysFor(tier, true);
                    var refreshed = currentDay + offGridCycle;
                    if (offGridCycle > 0f && IndoorMath.IsFinite(refreshed) && refreshed != record.MaintenanceDueDay)
                    {
                        record.MaintenanceDueDay = refreshed;
                        changed = true;
                    }
                }

                if (needsGridPower && !record.PowerConnected) warnings |= IndoorWarningFlags.NoPower;
                if (cycles > 0)
                {
                    warnings |= IndoorWarningFlags.MaintenanceDue;
                    if (stalled) warnings |= IndoorWarningFlags.MaintenanceStalled;
                    if (IndoorMaintenanceMath.IsMaterialMissing(materials, tier, record))
                    {
                        warnings |= IndoorWarningFlags.MissingMaterial;
                    }
                }

                // Phase 7: panel multipliers and dirty flag (Changed drives the save dirty bit).
                yieldMultiplier = IndoorGrowthMath.PanelYieldMultiplier(record, slots, limit);
            }
            catch
            {
                // REQ-V2-SAFE-01: no exception may escape into the game loop; gameplay logs the
                // throttled indoor.* warning, and the partial result keeps the farm alive.
            }

            return new IndoorDayResult
            {
                Coverage = coverage,
                GrowthMultiplier = growthMultiplier,
                YieldMultiplier = yieldMultiplier,
                Actions = actions,
                Warnings = warnings,
                Changed = changed,
                Stalled = stalled,
            };
        }
    }
}
