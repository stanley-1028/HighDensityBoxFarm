using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Farming;
using HDBF.Core.Logging;
using HDBF.Gameplay.Farming;
using Timberborn.Fields;
using Timberborn.Growing;
using Timberborn.InventorySystem;
using Timberborn.Navigation;
using Timberborn.Planting;
using Timberborn.SimpleOutputBuildings;
using Timberborn.TemplateSystem;
using Timberborn.Yielding;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: the P1 crop loop (REQ-CORE-03 / REQ-IMPL-16/17 / AC-03).
    ///
    /// The controller owns the deterministic daily work plan; <see cref="HdbfFarmWorkplaceBehavior"/>
    /// executes one item at a time through the vanilla worker-side planting/harvest steps. This
    /// split keeps every balance rule in gameplay code and every animation/walk/removal step in
    /// vanilla code, with zero Harmony patches (REQ-IMPL-15).
    ///
    /// Rules implemented here:
    /// * only registered plots inside <c>P_eff = min(PlotCapacity, activeWorkers × 38)</c> are
    ///   considered (REQ-IMPL-08, REQ-BAL-05: no workers → no actions);
    /// * at most <c>activeWorkers × 13</c> actions per day, each costing
    ///   <c>0.08 × (1 + realPathLength / 20) × modeWork</c> workdays (REQ-IMPL-16);
    /// * mature-to-harvest before empty-to-plant, then real path length, then plot id;
    /// * no dispatch while maintenance shutdown is active and no dispatch in fallow mode
    ///   (crops keep growing — growth is vanilla behaviour, chapter 6.5);
    /// * harvest updates fatigue / last crop from <see cref="BalanceFormulas"/> (chapter 6.4).
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        private const string LogKeyFarm = "gameplay.farm";
        private const string FarmhouseCropGroup = "Farmhouse";
        private const int FarmFaultsBeforeDisable = 5;

        private readonly List<FarmWorkCandidate> _farmCandidates = new List<FarmWorkCandidate>();
        private readonly List<FarmWorkCandidate> _farmPlan = new List<FarmWorkCandidate>();
        private readonly Queue<int> _farmPathQueue = new Queue<int>();
        private readonly Dictionary<int, int> _farmManhattanByPlot = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _farmPathByPlot = new Dictionary<int, int>();
        private readonly HashSet<int> _farmClaims = new HashSet<int>();
        private readonly List<PathCorner> _farmPathCorners = new List<PathCorner>();
        private readonly List<string> _farmCropTemplates = new List<string>();

        private FarmDailyBudget? _farmBudget;
        private SimpleOutputInventory? _simpleOutputInventory;
        private Inventory? _farmOutputInventory;
        private int _farmDay = int.MinValue;
        private bool _farmDayInitialized;
        private int _farmFaults;
        private bool _farmDisabled;
        private bool _farmCropTemplatesResolved;

        // ------------------------------------------------------------------ public surface for the behaviour

        /// <summary>
        /// True when the farm may dispatch work right now. Deliberately cheap: the expensive plan is
        /// built lazily on the first dispatch attempt of the day.
        /// </summary>
        public bool IsFarmWorkAvailable
        {
            get
            {
                if (!_activated || _farmDisabled || _record is null || _isShutdown)
                {
                    return false;
                }

                if (_record.Mode == HdbfMode.Fallow)
                {
                    return false;
                }

                if (_workplace is null || _workplace.NumberOfAssignedWorkers <= 0)
                {
                    return false;
                }

                return TryGetFarmOutputInventory() is not null;
            }
        }

        /// <summary>
        /// Takes the next deterministic work item for today. The daily budget is consumed here, so
        /// the action can never exceed <c>workers × 13</c> per day or the workday pool.
        /// </summary>
        public bool TryTakeFarmWorkItem(out FarmWorkItem item)
        {
            item = default;
            try
            {
                if (!IsFarmWorkAvailable || _record is null)
                {
                    return false;
                }

                EnsureFarmDay(_dayNightCycle.PartialDayNumber);

                var workers = _workplace!.NumberOfAssignedWorkers;
                if (workers <= 0)
                {
                    return false;
                }

                if (!_farmDayInitialized)
                {
                    _farmBudget = FarmDailyBudget.Create(workers);
                    BuildFarmCandidates(workers);
                    _farmDayInitialized = true;
                    DiagFarmPlanProbe(_farmCandidates.Count, _farmPathQueue.Count);
                }

                if (_farmBudget is null || _farmBudget.ActionsRemaining <= 0)
                {
                    return false;
                }

                var mode = _record.Mode;
                var index = FarmWorkPlanner.FindNext(_farmPlan, _farmBudget, _farmClaims, mode, out var workCost);
                if (index < 0)
                {
                    return false;
                }

                var candidate = _farmPlan[index];
                if (!_farmBudget.TryConsume(workCost))
                {
                    return false;
                }

                _farmClaims.Add(candidate.PlotId);
                item = new FarmWorkItem(candidate.PlotId, HdbfPlotCellCodec.Decode(candidate.PlotId),
                    candidate.Kind, candidate.CropTemplate, candidate.PathLength, workCost);
                return true;
            }
            catch (Exception exception)
            {
                ReportFarmFault(exception, "work dispatch");
                return false;
            }
        }

        /// <summary>
        /// Makes sure the global vanilla planting map has a crop resource for a managed plot. An
        /// existing player choice is adopted; otherwise a deterministic farmhouse crop is selected.
        /// This is the only way a planted crop can appear: the vanilla planting executor reads the
        /// resource from this map (no HDBF item spawning, REQ-BAL-22).
        /// </summary>
        public bool EnsurePlantingCrop(int plotId, out string cropTemplate)
        {
            cropTemplate = string.Empty;
            try
            {
                var cell = HdbfPlotCellCodec.Decode(plotId);
                var existing = _plantingService.GetResourceAt(cell);
                if (!string.IsNullOrEmpty(existing))
                {
                    cropTemplate = existing;
                    return true;
                }

                ResolveFarmCropTemplates();
                if (_farmCropTemplates.Count == 0)
                {
                    return false;
                }

                var index = (int)((uint)plotId % (uint)_farmCropTemplates.Count);
                var template = _farmCropTemplates[index];
                _plantingService.SetPlantingCoordinates(cell, template);
                cropTemplate = template;
                return true;
            }
            catch (Exception exception)
            {
                ReportFarmFault(exception, "planting resource");
                return false;
            }
        }

        /// <summary>Receiving inventory of the harvest; null until the blueprint carries a
        /// <c>SimpleOutputInventorySpec</c> (data requirement, see the workstream report).</summary>
        public Inventory? TryGetFarmOutputInventory()
        {
            if (_farmOutputInventory is not null && _farmOutputInventory.Enabled)
            {
                return _farmOutputInventory;
            }

            if (_simpleOutputInventory is null)
            {
                _simpleOutputInventory = HasComponent<SimpleOutputInventory>()
                    ? GetComponent<SimpleOutputInventory>()
                    : null;
            }

            var inventory = _simpleOutputInventory?.Inventory;
            if (inventory is not null && inventory.Enabled)
            {
                _farmOutputInventory = inventory;
                return inventory;
            }

            return null;
        }

        /// <summary>Releases a plot claim so the same plot can be worked again today (partial harvest).</summary>
        public void ReleaseFarmClaim(int plotId)
        {
            _farmClaims.Remove(plotId);
        }

        /// <summary>Called by the behaviour when a dispatched action could not start.</summary>
        public void NotifyFarmActionFailed(FarmWorkItem item, string reason)
        {
            ReleaseFarmClaim(item.PlotId);
            _log.Write(HdbfLogCategory.Warn, LogKeyFarm,
                "farm action failed (" + reason + "), entityId=" + _entityId + ", plotId=" + item.PlotId);
        }

        /// <summary>One crop cycle finished on a plot: update soil fatigue and the last-crop marker.</summary>
        public void NotifyPlotHarvested(int plotId, string cropTemplate)
        {
            try
            {
                if (_record is null || string.IsNullOrEmpty(cropTemplate))
                {
                    return;
                }

                var ids = _record.PlotIds;
                var index = Array.IndexOf(ids, plotId);
                if (index < 0)
                {
                    return;
                }

                var crops = _record.CropIds;
                var fatigue = _record.Fatigue;
                if (crops is null || fatigue is null || index >= crops.Length || index >= fatigue.Length)
                {
                    return;
                }

                var config = _configProvider.Config;
                var lastCrop = crops[index] ?? string.Empty;
                var sameCrop = !string.IsNullOrEmpty(lastCrop)
                               && string.Equals(lastCrop, cropTemplate, StringComparison.Ordinal);
                var modeFatigue = BalanceFormulas.ModeFatigueMultiplier(_record.Mode);
                var tierFactor = Math.Max(0f, _profile.SoilFatigueFactor) * Math.Max(0f, modeFatigue);

                fatigue[index] = BalanceFormulas.ApplyHarvestFatigue(fatigue[index], sameCrop, tierFactor,
                    TierCatalog.FatiguePerHarvest, config.RotationFactor, config.RotationRecovery);
                crops[index] = cropTemplate;
                _saveDirty = true;

                _log.Write(HdbfLogCategory.Info, LogKeyFarm,
                    "harvested plot " + plotId + " crop " + cropTemplate + " rotation=" + !sameCrop
                    + " fatigue=" + fatigue[index].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                    + ", entityId=" + _entityId);
            }
            catch (Exception exception)
            {
                ReportFarmFault(exception, "harvest accounting");
            }
        }

        /// <summary>Counts a farming fault and disables the loop for this building after a few in a row.</summary>
        public void ReportFarmFault(Exception exception, string context)
        {
            _farmFaults++;
            _log.Error(HdbfLogCategory.Error, LogKeyFarm,
                "farm loop fault (" + context + "); entityId=" + _entityId, exception, _entityId.ToString());

            if (_farmFaults >= FarmFaultsBeforeDisable && !_farmDisabled)
            {
                _farmDisabled = true;
                _farmClaims.Clear();
                _log.Write(HdbfLogCategory.Warn, LogKeyFarm,
                    "farm loop disabled after repeated faults; crops keep growing, entityId=" + _entityId);
            }
        }

        // ------------------------------------------------------------------ daily reset + per-frame path measurement

        /// <summary>Called once per game day from the daily simulation step (REQ-IMPL-22 order).</summary>
        private void OnFarmDayTick(float today)
        {
            try
            {
                EnsureFarmDay(today);
                ApplyDenseGrowthBoost();
            }
            catch (Exception exception)
            {
                ReportFarmFault(exception, "farm day tick");
            }
        }

        private void EnsureFarmDay(float today)
        {
            var day = (int)Math.Floor(today);
            if (day == _farmDay)
            {
                return;
            }

            _farmDay = day;
            InvalidateFarmPlan();
        }

        /// <summary>Invalidates the cached plan (day change, plot re-registration, deactivation).</summary>
        private void InvalidateFarmPlan()
        {
            _farmDayInitialized = false;
            _farmBudget = null;
            _farmCandidates.Clear();
            _farmPlan.Clear();
            _farmPathQueue.Clear();
            _farmPathByPlot.Clear();
            _farmClaims.Clear();
        }

        /// <summary>
        /// Per-frame slice of the day plan: resolves the real entrance-to-plot path length for a few
        /// candidates so no frame ever performs hundreds of path queries (chapter 15.3 frame budget).
        /// </summary>
        private void StepFarmPathMeasurements()
        {
            if (!_activated || _farmPathQueue.Count == 0)
            {
                return;
            }

            try
            {
                var budget = FarmWorkPlanner.PathMeasurementsPerTick;
                while (budget-- > 0 && _farmPathQueue.Count > 0)
                {
                    var index = _farmPathQueue.Dequeue();
                    if (index < 0 || index >= _farmCandidates.Count)
                    {
                        continue;
                    }

                    var candidate = _farmCandidates[index];
                    var pathLength = ResolvePathLength(candidate.PlotId);
                    candidate = candidate.WithPathLength(pathLength);
                    _farmCandidates[index] = candidate;

                    // Only a really measured path length enters the plan: an unreachable plot
                    // (per-cell query failure or no route) stays out of the plan for the rest of
                    // this plan period, while every remaining plot is still measured and
                    // dispatched (BUG-04 per-cell tolerance).
                    if (FarmPathProbe.IsReachable(pathLength))
                    {
                        _farmPlan.Add(candidate);
                        FarmWorkPlanner.SortMeasured(_farmPlan);
                    }
                }
            }
            catch (Exception exception)
            {
                ReportFarmFault(exception, "path measurement");
            }
        }

        // ------------------------------------------------------------------ plan construction

        private void BuildFarmCandidates(int workers)
        {
            _farmCandidates.Clear();
            _farmPlan.Clear();
            _farmPathQueue.Clear();
            _farmManhattanByPlot.Clear();
            _farmClaims.Clear();
            _farmPathByPlot.Clear();

            if (_record is null || workers <= 0)
            {
                return;
            }

            var ids = _record.PlotIds;
            if (ids is null || ids.Length == 0)
            {
                return;
            }

            var effective = EffectiveFarmPlots(workers);
            if (effective <= 0)
            {
                return;
            }

            var origin = ResolveScanOrigin();
            var count = Math.Min(effective, ids.Length);
            for (var i = 0; i < count; i++)
            {
                var plotId = ids[i];
                var cell = HdbfPlotCellCodec.Decode(plotId);
                if (!TryClassifyPlot(cell, out var kind, out var cropTemplate))
                {
                    continue;
                }

                var candidate = new FarmWorkCandidate(plotId, kind, cropTemplate);
                _farmCandidates.Add(candidate);
                _farmManhattanByPlot[plotId] =
                    Math.Abs(cell.x - origin.x) + Math.Abs(cell.y - origin.y);
            }

            if (_farmCandidates.Count == 0)
            {
                return;
            }

            _farmCandidates.Sort((left, right) => FarmWorkPlanner.CompareEstimated(left, right,
                _farmManhattanByPlot[left.PlotId], _farmManhattanByPlot[right.PlotId]));

            // Only the first quota candidates can possibly be dispatched today; their real path is
            // measured over the next frames. This keeps ordering by genuine path length while
            // bounding the path-query cost.
            var actions = _farmBudget?.ActionsRemaining ?? 0;
            var quota = FarmWorkPlanner.MeasurementQuota(actions);
            var queued = Math.Min(quota, _farmCandidates.Count);
            for (var i = 0; i < queued; i++)
            {
                _farmPathQueue.Enqueue(i);
            }
        }

        private int EffectiveFarmPlots(int workers)
        {
            var perFarmer = _spec.PlotsPerFarmer > 0 ? _spec.PlotsPerFarmer : TierCatalog.PlotsPerFarmer;
            return BalanceFormulas.EffectivePlots(_profile.PlotCapacity, workers, perFarmer);
        }

        /// <summary>
        /// Classifies one managed plot: harvest when a mature, yielding vanilla crop exists, plant
        /// when the cell is empty, nothing while the crop is still growing.
        /// </summary>
        private bool TryClassifyPlot(Vector3Int cell, out FarmActionKind kind, out string cropTemplate)
        {
            kind = FarmActionKind.Plant;
            cropTemplate = string.Empty;

            var crop = _blockService.GetFirstObjectWithComponentAt<Crop>(cell);
            if (crop is null)
            {
                return true;
            }

            var yielder = crop.Yielder;
            if (yielder is null || !yielder.IsYielding)
            {
                // Still growing (or a dead crop the game will clean up): nothing to do.
                return false;
            }

            var growable = crop.GetComponent<Growable>();
            if (growable is not null && !growable.IsGrown)
            {
                return false;
            }

            kind = FarmActionKind.Harvest;
            cropTemplate = ResolveCropTemplate(crop) ?? yielder.Yield.GoodId ?? string.Empty;
            return !string.IsNullOrEmpty(cropTemplate);
        }

        private static string? ResolveCropTemplate(Crop crop)
        {
            try
            {
                var template = crop.GetComponent<TemplateSpec>();
                return template is not null ? template.TemplateName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ mode: dense growth boost

        /// <summary>
        /// Dense mode's yield multiplier is realised through real crop growth (chapter 6.1: a layer
        /// may show up as yield *or growth speed): each in-progress managed crop gets its growth
        /// advanced by <c>(G_mode − 1) / GrowthTimeInDays</c> per day, so more real harvests happen
        /// and HDBF never adds items. Fallow mode does not dispatch at all and is handled by the
        /// existing fallow fatigue recovery.
        /// </summary>
        private void ApplyDenseGrowthBoost()
        {
            if (_record is null || _record.Mode != HdbfMode.Dense)
            {
                return;
            }

            var config = _configProvider.Config;
            var modeYield = BalanceFormulas.ModeYieldMultiplier(HdbfMode.Dense, config.DenseYieldMultiplier);
            if (modeYield <= 1f)
            {
                return;
            }

            var workers = _workplace is not null ? _workplace.NumberOfAssignedWorkers : 0;
            var effective = EffectiveFarmPlots(workers);
            var ids = _record.PlotIds;
            if (effective <= 0 || ids is null)
            {
                return;
            }

            var count = Math.Min(effective, ids.Length);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var cell = HdbfPlotCellCodec.Decode(ids[i]);
                    var crop = _blockService.GetFirstObjectWithComponentAt<Crop>(cell);
                    if (crop is null)
                    {
                        continue;
                    }

                    var growable = crop.GetComponent<Growable>();
                    if (growable is null || growable.IsGrown || !growable.GrowthInProgress)
                    {
                        continue;
                    }

                    var growthDays = growable.GrowthTimeInDays;
                    if (growthDays <= 0f)
                    {
                        continue;
                    }

                    growable.IncreaseGrowthProgress((modeYield - 1f) / growthDays);
                }
                catch (Exception exception)
                {
                    ReportFarmFault(exception, "dense growth");
                    return;
                }
            }
        }

        /// <summary>
        /// Resolves one farmhouse crop template for the plot scan so the game's planting-area
        /// validator can decide whether a candidate cell is a legal planting coordinate.
        /// </summary>
        internal bool TryGetScanCropTemplate(out string cropTemplate)
        {
            cropTemplate = string.Empty;
            try
            {
                ResolveFarmCropTemplates();
                if (_farmCropTemplates.Count == 0)
                {
                    return false;
                }

                cropTemplate = _farmCropTemplates[0];
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyFarm,
                    "scan crop template resolution failed", exception, _entityId.ToString());
                return false;
            }
        }
        // ------------------------------------------------------------------ crop templates

        private void ResolveFarmCropTemplates()
        {
            if (_farmCropTemplatesResolved)
            {
                return;
            }

            _farmCropTemplatesResolved = true;
            try
            {
                foreach (var plantable in _templateService.GetAll<PlantableSpec>())
                {
                    if (plantable is null)
                    {
                        continue;
                    }

                    if (!string.Equals(plantable.ResourceGroup, FarmhouseCropGroup, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = plantable.TemplateName;
                    if (!string.IsNullOrEmpty(name) && !_farmCropTemplates.Contains(name))
                    {
                        _farmCropTemplates.Add(name);
                    }
                }

                // Deterministic order so the same plot always gets the same default crop.
                _farmCropTemplates.Sort(StringComparer.Ordinal);
            }
            catch (Exception exception)
            {
                _farmCropTemplates.Clear();
                ReportFarmFault(exception, "crop templates");
            }
        }

        // ------------------------------------------------------------------ real path length

        /// <summary>
        /// Real entrance-to-plot path length in cells (REQ-IMPL-16 forbids straight-line distance).
        /// Returns <see cref="FarmPathProbe.Unreachable"/> (-1) when the plot is not reachable for
        /// this plan; the candidate is then never dispatched while every other candidate keeps its
        /// plan entry.
        ///
        /// P0 (round-9 in-game log, 2026-10-25): a plot whose coordinate has no cached navigation
        /// flow field made <c>Accessible.FindTerrainPath</c> throw
        /// <c>InvalidOperationException: There's no cached flow field at 1187869</c>. The old code
        /// counted that as a farm-loop fault: <see cref="ReportFarmFault"/> wrote an
        /// <c>HDBF/Error</c> with a stack, incremented the fault counter and — after five such
        /// plots — disabled the whole crop loop for the building, so no plot was ever dispatched.
        /// A path query is a per-cell filter, not a plan-fatal error: <see cref="FarmPathProbe"/>
        /// converts any thrown query into "unreachable for this plan", this method logs it once per
        /// building through a throttled <c>HDBF/Warn</c> (entityId + plotId/cell + exception type,
        /// no stack, no fault counter), and the plan continues with the remaining plots.
        /// </summary>
        private int ResolvePathLength(int plotId)
        {
            if (_farmPathByPlot.TryGetValue(plotId, out var cached))
            {
                return cached;
            }

            var pathLength = FarmPathProbe.Measure(() => QueryPathLength(plotId), out var failure);

            // Memoise the unreachable result as well: one query per plot per plan, never one per
            // frame (the per-frame measurement slice must not retry a known-bad cell).
            _farmPathByPlot[plotId] = pathLength;
            if (failure is not null)
            {
                ReportPathUnreachable(plotId, failure);
            }

            return pathLength;
        }

        /// <summary>
        /// The game-side path queries; may throw the engine's
        /// <c>InvalidOperationException("There's no cached flow field ...")</c> for a coordinate
        /// without a navigation flow field. Every caller must go through
        /// <see cref="ResolvePathLength"/>, which owns the per-cell unreachable conversion.
        /// </summary>
        private int QueryPathLength(int plotId)
        {
            var world = NavigationCoordinateSystem.GridToWorld(HdbfPlotCellCodec.Decode(plotId));
            float distance;
            Exception? lastFailure = null;

            var accessible = ResolveAccessible();
            if (accessible is not null)
            {
                // Each query is isolated: the engine throws "There's no cached flow field" for a
                // coordinate with no navigation flow field, and one failing strategy must not hide
                // the next one (round-10 in-game log: every grass plot failed here).
                try
                {
                    if (accessible.FindRoadPath(world, out distance))
                    {
                        return FarmPathProbe.FromQuery(true, distance);
                    }
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                }

                try
                {
                    if (accessible.FindTerrainPath(world, out distance))
                    {
                        return FarmPathProbe.FromQuery(true, distance);
                    }
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                }
            }

            // Fallback while BuildingAccessible has not wired the navigation Accessible yet, or
            // when the Accessible-side queries have no flow field for that coordinate.
            var start = NavigationCoordinateSystem.GridToWorld(ResolveScanOrigin());
            _farmPathCorners.Clear();
            try
            {
                if (_navigationService.FindPathUnlimitedRange(start, world, _farmPathCorners, out distance))
                {
                    return FarmPathProbe.FromQuery(true, distance);
                }
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }

            if (lastFailure is not null)
            {
                // FarmPathProbe.Measure turns this into "unreachable for this plan" and reports the
                // exception type to the throttled per-building warning.
                throw lastFailure;
            }

            return FarmPathProbe.Unreachable;
        }

        /// <summary>
        /// Player-visible, throttled warning for one failed per-cell path query. The log key
        /// carries the entity id, so the 30 s throttle is per building: one broken farm can
        /// neither hide another farm's warning nor flood the log with one line per plot. The
        /// message has entityId, plotId/cell and the exception type (no stack: this is a
        /// recoverable per-cell condition). It is never counted as a farm fault, because the plan
        /// is still valid for every other plot (REQ-SAFE-01 isolation).
        /// </summary>
        private void ReportPathUnreachable(int plotId, Exception failure)
        {
            try
            {
                var cell = HdbfPlotCellCodec.Decode(plotId);
                _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.FarmPathUnreachable + "." + _entityId,
                    "farm path query failed; plot treated as unreachable for this plan"
                    + "; entityId=" + _entityId
                    + ", plotId=" + plotId
                    + ", cell=(" + cell.x + "," + cell.y + "," + cell.z + ")"
                    + ", exception=" + failure.GetType().FullName + ": " + failure.Message);
            }
            catch (Exception)
            {
                // Diagnostics must never break the farm plan (REQ-SAFE-02).
            }
        }
    }
}
