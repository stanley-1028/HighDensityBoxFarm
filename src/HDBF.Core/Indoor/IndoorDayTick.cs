// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01 (day tick) / V2-GAMEPLAY-02 (wiring)
// Purpose: The fixed-order daily orchestration of one indoor farm (ARCHITECTURE
//          section 3). The DTOs live in IndoorDayContracts.cs; this file owns the
//          order, the per-slot isolation and the day result. The tank drain itself is
//          continuous and lives in the game layer (2026-10-28 player request).
// Requirements: REQ-V2-MAINT-03, REQ-V2-CORE-05, REQ-V2-SAFE-01, REQ-V2-BAL-01,
//               REQ-V2-PERF-02, REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-LABOR-01
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// The fixed-order daily tick of one indoor farm (ARCHITECTURE section 3): entrance gate,
    /// water refill, growth, farm actions, fatigue/rotation, maintenance/power, warnings and
    /// dirty flag. The tank drain is continuous in the game layer, so this tick only reads the live
    /// tank (2026-10-28 player request). The order is observable and pinned by
    /// <c>V2TickOrderTests</c> (REQ-V2-MAINT-03).
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
            var refillActions = 0;
            var refillOutcome = ManualRefillOutcome.NotRequested;
            var transitionScale = 1f;

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
            var manualWater = context is null ? null : context.ManualWater;
            var materials = context is null ? null : context.Materials;
            var hasPipeWater = context is not null && context.HasPipeWater;
            var manualRefillRequested = context is not null && context.ManualRefillRequested;
            var reservedActions = context is null ? 0 : context.ReservedActions;
            if (reservedActions < 0) reservedActions = 0;
            if (entranceBlocked) warnings |= IndoorWarningFlags.EntranceBlocked;
            if (activeWorkers <= 0) warnings |= IndoorWarningFlags.NoWorkers;

            var tankStart = record.Tank;
            try
            {
                // Phase 2: water refill (REQ-V2-WATER-02); the tank drain itself is continuous and
                // runs in the game layer (HdbfWaterFlowService -> IndoorWaterMath.Consume), so this
                // daily tick only repairs the tank, reads it for coverage/growth and tops it up here.
                IndoorWaterMath.SanitizeTank(record, tier);
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

                // REQ-V2-WATER-04: a manual trip needs an on-duty worker, an open entrance, a real
                // source and 2 free farm actions out of today's budget. It never moves water for free.
                if (manualRefillRequested)
                {
                    var source = manualWater ?? water;
                    var freeActions = IndoorLaborMath.RemainingActions(activeWorkers, reservedActions);
                    if (activeWorkers <= 0)
                    {
                        refillOutcome = ManualRefillOutcome.NoWorkers;
                    }
                    else if (entranceBlocked)
                    {
                        refillOutcome = ManualRefillOutcome.EntranceBlocked;
                    }
                    else if (freeActions < IndoorConstants.ManualRefillActions)
                    {
                        refillOutcome = ManualRefillOutcome.NoBudget;
                    }
                    else if (IndoorWaterMath.TryDrawSource(source,
                                 IndoorWaterMath.RefillAmount(tier.TankCapacity), out var drawn) && drawn > 0f)
                    {
                        IndoorWaterMath.TryRefill(record, drawn);
                        refillActions = IndoorConstants.ManualRefillActions;
                        refillOutcome = ManualRefillOutcome.Completed;
                    }
                    else
                    {
                        refillOutcome = ManualRefillOutcome.NoSource;
                    }

                    if (refillOutcome != ManualRefillOutcome.Completed)
                    {
                        warnings |= IndoorWarningFlags.NoWater;
                    }
                }

                // Continuous drain (player request 2026-10-28): the tank was already consumed by the
                // game-layer flow service slice by slice; price the day from the live tank only.
                coverage = IndoorWaterMath.Coverage(record.Tank, demand);
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
                // The manual refill trip is paid from the same daily budget (REQ-V2-LABOR-01); the
                // transition day settles harvests at the lower of the old/new mode yield (section 19.8).
                transitionScale = IndoorGrowthMath.ModeTransitionYieldScale(record.Mode,
                    context is null ? null : context.PreviousMode);
                var actionsAllowed = !entranceBlocked && !stalled && record.Mode != IndoorMode.Fallow;
                var budgetReserved = reservedActions + refillActions;
                // Player request 2026-10-28: when the tank holds water the planting pass fills every
                // empty worker-tended slot without spending the daily budget; an empty tank plants
                // nothing. Harvests keep the daily action budget and run strictly before planting.
                actions = IndoorLaborMath.RunActions(record, tier, catalog, activeWorkers, output!,
                    actionsAllowed, budgetReserved, transitionScale, waterAvailable: record.Tank > 0f);
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
                yieldMultiplier = IndoorGrowthMath.PanelYieldMultiplier(record, slots, limit, transitionScale);
            }
            catch
            {
                // REQ-V2-SAFE-01: no exception may escape into the game loop; gameplay logs the
                // throttled indoor.* warning, and the partial result keeps the farm alive.
            }

            if (refillActions > 0 || record.Tank != tankStart) changed = true;

            return new IndoorDayResult
            {
                Coverage = coverage,
                GrowthMultiplier = growthMultiplier,
                YieldMultiplier = yieldMultiplier,
                Actions = actions,
                Warnings = warnings,
                Changed = changed,
                Stalled = stalled,
                RefillActionsUsed = refillActions,
                ManualRefill = refillOutcome,
                TransitionYieldScale = transitionScale,
            };
        }
    }
}
