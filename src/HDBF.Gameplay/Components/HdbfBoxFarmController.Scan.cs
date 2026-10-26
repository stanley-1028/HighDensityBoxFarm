using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using Timberborn.Fields;
using Timberborn.Navigation;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: the incremental managed-plot scan (chapter 11.3). Kept apart from the
    /// lifecycle code so the frame-budgeted scan can be reviewed on its own.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        /// <summary>
        /// Production counters of the current scan. They feed the player-visible empty-registry
        /// warning and are deliberately independent of the temporary diagnostics switch in
        /// <c>HdbfBoxFarmController.Diagnostics.cs</c>.
        /// </summary>
        private readonly PlotScanStats _scanStats = new PlotScanStats();

        /// <summary>
        /// BUG-05: snapshot of the plot ids registered when the current scan begins. One HashSet
        /// lookup per scanned cell decides the retained-plot path (the record is never searched
        /// linearly and this set is not rebuilt per cell). A plot this farm already manages must
        /// survive every daily rescan even when its crop occupies the cell — in the round-11 log
        /// the registry shrank 60 -> 25 -> 0 exactly because that was not the case.
        /// </summary>
        private readonly HashSet<int> _registeredWhenScanBegan = new HashSet<int>();

        /// <summary>
        /// One visible warning per scan when the crop probe cannot classify an occupied cell; the
        /// plot is kept in that case (never drop a whole registry because one probe failed).
        /// </summary>
        private bool _retentionProbeWarned;

        // ------------------------------------------------------------------ per-frame hook (scan only)

        /// <inheritdoc />
        public override void Tick()
        {
            if (!_activated || _scanner is null)
            {
                return;
            }

            if (_scanner.Active)
            {
                try
                {
                    _scanner.Step(ScanBudgetPerFrame(), this);
                }
                catch (Exception exception)
                {
                    _scanner.Cancel();
                    _log.Error(HdbfLogCategory.Error, LogKeyScan,
                        "plot scan aborted; keeping the current registry", exception, _entityId.ToString());
                }
            }

            // The farm day plan resolves real entrance-to-plot paths in small per-frame slices; it
            // must run even when the plot scan is idle (chapter 15.3 frame budget).
            StepFarmPathMeasurements();

            // BUILD-05: one sharded slice of the managed-crop visual refresh (own registered cells
            // only, low interval, small per-frame budget).
            StepCropVisibility();
        }

        // ------------------------------------------------------------------ plot scan

        private void StartScan()
        {
            if (!_activated || _scanner is null || _blockObject is null)
            {
                return;
            }

            var origin = ResolveScanOrigin();
            _reachabilityFallbackBudget = MaxReachabilityFallbacksPerScan;

            // A running scan means the previous "可开垦格" count is stale: the panel must show the
            // unknown glyph ("—") until this scan completes, never the old count and never 0
            // (chapter 14.1 "数据未就绪显示 —").
            _tillablePlots = BoxFarmSnapshot.UnknownTillablePlots;
            ResetScanCounters();
            SnapshotRegisteredPlots();
            var side = Math.Max(_profile.WorkRange, 1);
            _scanner.Begin(origin, side);
            DiagScanBegin(origin, side);
        }

        private Vector3Int ResolveScanOrigin()
        {
            if (_blockObject.HasEntrance)
            {
                var entrance = _blockObject.PositionedEntrance;
                if (entrance is not null)
                {
                    return entrance.Coordinates;
                }
            }

            return _blockObject.Coordinates;
        }

        /// <summary>
        /// Copies the current registration into the per-scan set (BUG-05). The set is a snapshot:
        /// the record is only rewritten when the scan completes, so every cell of this scan sees
        /// the same previously-registered ids.
        /// </summary>
        private void SnapshotRegisteredPlots()
        {
            _registeredWhenScanBegan.Clear();
            var ids = _record?.PlotIds;
            if (ids is null)
            {
                return;
            }

            for (var i = 0; i < ids.Length; i++)
            {
                _registeredWhenScanBegan.Add(ids[i]);
            }
        }

        /// <inheritdoc />
        public Vector3Int ResolvePlotCell(Vector3Int horizontalCell)
        {
            var x = horizontalCell.x;
            var y = horizontalCell.y;
            if (!_terrainService.Contains(new Vector2Int(x, y)))
            {
                // Not a terrain column, so this horizontal position stands for no plot. Hand the
                // horizontal cell back unchanged instead of probing GetTerrainHeight outside the
                // map; TryAcceptPlot's own Contains check rejects it exactly as before BUG-04.
                return horizontalCell;
            }

            // z semantics (verified from the installed assembly IL; full evidence chain in
            // HDBF.Core.Plots.PlotCellHeights):
            // * Timberborn.TerrainSystem.TerrainService.GetTerrainHeight(coords) returns
            //   coords.z + TerrainMap.TryGetRelativeHeight(...); probed at z=0 on a normal
            //   column that is topSolidZ + 1 = the terrain column ceiling, i.e. the first air
            //   cell above the column (ColumnTerrainMap.GetColumn treats Ceiling as exclusive:
            //   solid voxels are Floor..Ceiling-1);
            // * Timberborn.PlantingUI.PlantingSelectionService.InMapLeveledCoordinates (the
            //   Fields-tool coordinate source) yields exactly (x, y, pickedTerrainVoxel.z + 1)
            //   for columns where OnGround holds, and that same cell is passed to CanPlant and to
            //   PlantingService.SetPlantingCoordinates;
            // * PlantingAreaValidator.CanPlant places the crop BlockObject at that cell and
            //   BlockValidator rejects terrain cells but needs ground below — so CanPlant wants
            //   the surface cell, not the solid voxel and not a cell higher up.
            // The old suspicion that the two APIs wanted different z values is disproved; the
            // mapping lives in PlotCellHeights so it stays single-sourced and testable.
            var columnCeiling = _terrainService.GetTerrainHeight(new Vector3Int(x, y, 0));
            return new Vector3Int(x, y, PlotCellHeights.ResolvePlantCellZ(columnCeiling));
        }

        /// <inheritdoc />
        public bool TryAcceptPlot(Vector3Int cell, int distanceSquared)
        {
            _scanStats.Scanned++;
            var x = cell.x;
            var y = cell.y;
            if (!_terrainService.Contains(new Vector2Int(x, y)))
            {
                _scanStats.TerrainRejects++;
                return false;
            }

            var id = HdbfPlotCellCodec.Encode(cell);

            // BUG-05 (P0, round-11 in-game log 2026-10-25): a rescan used to re-apply the *new
            // cell* filters to cells this farm already manages. As soon as a crop occupied its
            // plot cell, CanPlant said no and AnyObjectAt saw the crop, so the cell was dropped;
            // every daily rescan shrank the registry (allocated 60 -> 25 -> 0), the last scan
            // logged plot.scan-empty and the farm had no managed plot left to harvest. A
            // previously registered cell is therefore judged by the retention rule only: terrain
            // still there, still inside the work range (the scanner only visits in-range cells),
            // not player-excluded, not stolen by another farm and not taken over by a non-crop
            // road/building. The crop on its own cell is the expected state and must not
            // disqualify it.
            if (_registeredWhenScanBegan.Count > 0 && _registeredWhenScanBegan.Contains(id))
            {
                return TryAcceptRetainedPlot(cell, id);
            }

            // A managed plot is exactly a cell the game's own field-painting validator accepts
            // (PlantingAreaValidator is the singleton the Fields tool uses to decide where a
            // planting coordinate may be created). The earlier CellIsField probe was wrong:
            // CellIsField only sees cells that already carry a planting coordinate, which is
            // empty on a fresh map, so every scan rejected every cell and the registry stayed
            // empty (root cause found in the round-6 in-game run).
            if (!TryGetScanCropTemplate(out var scanCrop))
            {
                _scanStats.FieldRejects++;
                return false;
            }

            // Defense in depth for the z semantics: the Fields tool only offers coordinates for
            // which ITerrainService.OnGround is true (IL:
            // TerrainAreaService.InMapLeveledCoordinates). Enforcing the same predicate here
            // guarantees a stored plot id is a real terrain column ceiling, which is exactly what
            // SoilIsMoist's TryGetIndexAtCeiling matches later.
            if (!_terrainService.OnGround(cell))
            {
                _scanStats.TerrainRejects++;
                return false;
            }

            if (!_plantingAreaValidator.CanPlant(cell, scanCrop))
            {
                _scanStats.FieldRejects++;
                return false;
            }

            if (!_terrainService.Contains(cell))
            {
                _scanStats.TerrainRejects++;
                return false;
            }

            // Buildings, paths and other block objects cannot be managed plots.
            if (_blockService.AnyObjectAt(cell))
            {
                _scanStats.BlockRejects++;
                return false;
            }

            // A path object may sit on top of a field and blocks planting there.
            if (_plantingService.TryGetPlantingBlocker(cell, out var blocker) && blocker != null)
            {
                _scanStats.BlockerRejects++;
                return false;
            }

            if (_excluded.Contains(id))
            {
                _scanStats.Excluded++;
                return false;
            }

            if (_registry.IsPlotClaimedByOther(id, _entityId))
            {
                _scanStats.Claimed++;
                return false;
            }

            if (!IsReachable(cell))
            {
                _scanStats.ReachabilityRejects++;
                return false;
            }
            _scanStats.Accepted++;
            return true;
        }

        /// <summary>
        /// BUG-05 retention path for a cell this farm already registered. Deliberately skips the
        /// new-cell qualification (<c>CanPlant</c>, <c>AnyObjectAt</c>, planting blocker,
        /// crop-template resolution): a crop occupies exactly those predicates, so applying them
        /// to an already managed cell is what wiped the registry in the round-11 log. The
        /// keep/drop decision itself comes from <see cref="PlotRetention"/> in Core, so the L1
        /// tests and the in-game path share one rule.
        ///
        /// Reachability is deliberately not re-tested here: a crop is not a navigation blocker, so
        /// the registration-time reachability does not change, and re-running the navigation
        /// filter on already registered cells is the exact class of flaky wipe this card fixes.
        /// A plot that became walled off is skipped per cell by the BUG-04 farm-plan probe while
        /// its identity (and its moisture sampling) is preserved.
        /// </summary>
        private bool TryAcceptRetainedPlot(Vector3Int cell, int plotId)
        {
            // "Terrain still there": the column and the cell itself are inside the terrain map and
            // the stored cell is still the surface cell above solid terrain. ITerrainService.
            // OnGround is terrain-only (IL: IsTerrainVoxel(Below(coords)) && !IsTerrainVoxel(coords)),
            // so a crop standing on the cell does not affect it.
            var terrainMissing = !_terrainService.Contains(cell) || !_terrainService.OnGround(cell);
            var excluded = !terrainMissing && _excluded.Contains(plotId);
            var claimedByOther = !terrainMissing && !excluded
                                 && _registry.IsPlotClaimedByOther(plotId, _entityId);
            var occupiedByNonCrop = !terrainMissing && !excluded && !claimedByOther
                                    && IsOccupiedByNonCrop(cell);

            var state = new PlotRetentionState
            {
                TerrainMissing = terrainMissing,
                ExcludedByPlayer = excluded,
                ClaimedByOtherFarm = claimedByOther,
                OccupiedByNonCrop = occupiedByNonCrop,
            };

            if (PlotRetention.ShouldKeep(state))
            {
                _scanStats.Accepted++;
                return true;
            }

            if (terrainMissing)
            {
                _scanStats.TerrainRejects++;
            }
            else if (excluded)
            {
                _scanStats.Excluded++;
            }
            else if (claimedByOther)
            {
                _scanStats.Claimed++;
            }
            else
            {
                // A road/building replaced the crop: the managed plot is gone (never retain forever).
                _scanStats.BlockRejects++;
            }

            return false;
        }

        /// <summary>
        /// BUG-05: decides whether an occupied registered-plot cell now carries a non-crop object.
        /// A crop is a block object too, so <c>AnyObjectAt</c> alone would drop every crop-occupied
        /// plot — the round-11 regression. Only a block object without a crop component (road,
        /// building, stockpile) takes the plot away. If the probe throws, the plot is kept (dropping
        /// registered plots is the bug this card fixes) and one visible warning is logged per scan.
        /// </summary>
        private bool IsOccupiedByNonCrop(Vector3Int cell)
        {
            try
            {
                if (!_blockService.AnyObjectAt(cell))
                {
                    return false;
                }

                // The farm's crops are the only expected block objects on a managed plot cell;
                // Crop is the same component the farm plan classifies (Farming.cs TryClassifyPlot).
                return _blockService.GetFirstObjectWithComponentAt<Crop>(cell) is null;
            }
            catch (Exception exception)
            {
                if (!_retentionProbeWarned)
                {
                    _retentionProbeWarned = true;
                    // Same recoverable per-cell class as the reachability filter above: keep the
                    // plot, never a stack, and one per-building line per scan instead of the old
                    // generic gameplay.scan error carrying the whole trace.
                    _log.Write(HdbfLogCategory.Warn,
                        HdbfLogKeys.ScanRetentionProbe + "." + _entityId,
                        "plot retention crop probe failed; keeping previously registered plots for this scan"
                        + "; entityId=" + _entityId
                        + ", exception=" + exception.GetType().FullName + ": " + exception.Message);
                }

                return false;
            }
        }

        private bool IsReachable(Vector3Int cell)
        {
            try
            {
                var cellWorld = NavigationCoordinateSystem.GridToWorld(cell);
                var entrance = ResolveScanOrigin();
                var startWorld = NavigationCoordinateSystem.GridToWorld(entrance);

                // Cheap first: the public navigation service exposes the reachable-area graph test
                // (verified IL: NavigationService.DestinationIsReachableUnlimitedRange delegates to
                // the internal GlobalReachabilityService.AreaReachable). One dictionary lookup per
                // cell keeps a 729-cell scan inside the table 10-5 frame budget.
                if (_navigationService.DestinationIsReachableUnlimitedRange(startWorld, cellWorld))
                {
                    return true;
                }

                // The building's own Accessible component can still answer "yes" for a cell the
                // area test puts in a different district (e.g. a walked terrain route). These are
                // real path queries, so they are capped per scan to protect the frame budget.
                if (_reachabilityFallbackBudget <= 0)
                {
                    return false;
                }

                var accessible = ResolveAccessible();
                if (accessible is null)
                {
                    return false;
                }

                _reachabilityFallbackBudget--;
                if (accessible.IsReachableByTerrain(cellWorld))
                {
                    return true;
                }

                // FindRoadPath is the verified Vector3 overload; it yields the real path length as
                // an out parameter (the REQ-IMPL-16 labour-cost input for a later version).
                return accessible.FindRoadPath(cellWorld, out _);
            }
            catch (Exception exception)
            {
                // Pathing is a filter: a failed query excludes that cell for this scan and the
                // rest of the range keeps working (REQ-SAFE-01, REQ-IMPL-06). The decision stays
                // exactly "unreachable" (the caller counts it as a reachability reject), but the
                // throw is only *counted* here: round 28 wrote one compat.failed Warn *with the
                // full stack* per cell, so a 27x27 Advanced scan flooded the log with a signal
                // that read as a compatibility break. WarnIfScanQueriesFailed emits the single
                // per-scan summary under HdbfLogKeys.ScanReachability.
                _scanStats.RecordReachabilityFailure(cell.x, cell.y, cell.z,
                    exception.GetType().FullName);
                return false;
            }
        }

        /// <summary>
        /// Resolves the navigation Accessible component lazily: <see cref="BuildingAccessible"/>
        /// owns it and only wires the reference during its own Awake/finished-state callbacks,
        /// which may run after this component's Awake. Cached once found.
        /// </summary>
        private Accessible? ResolveAccessible()
        {
            if (_accessible is not null)
            {
                return _accessible;
            }

            var buildingAccessible = _buildingAccessible;
            if (buildingAccessible is not null)
            {
                _accessible = buildingAccessible.Accessible;
            }

            return _accessible;
        }

        /// <inheritdoc />
        public void OnPlotsScanned(IReadOnlyList<PlotCandidate> candidates, int deferredFrames)
        {
            try
            {
                // Chapter 11.3 "可开垦格": the accepted candidates of this completed scan, before
                // the capacity cut. TryAcceptPlot already rejected non-plantable, unreachable,
                // player-excluded and other-farm cells, so candidates.Count is the count of cells
                // the player can actually cultivate.
                _tillablePlots = candidates.Count;

                // BUG-05 observability: the kept/added/dropped delta is recorded from the same
                // previous-id array the selector uses, before ApplyPlotArrays rewrites the record.
                // A crop-occupied rescan must report kept=60 added=0 dropped=0 instead of the
                // round-11 signature registeredNow=0 / plot.scan-empty.
                var previousIds = _record.PlotIds;
                var selected = PlotSelection.Select(candidates, _profile.PlotCapacity, previousIds);
                _scanStats.RecordSelection(previousIds, selected);

                var granted = _registry.ApplyPlotSelection(_entityId, selected);
                ApplyPlotArrays(granted);

                // P0 fix: coverage used to be written only by the daily simulation step, so a
                // freshly registered (or freshly loaded) farm showed 0 % until the first game
                // day even when every plot was moist. Resample once here, through the same
                // SampleMoistPlots path the day tick uses (never per frame).
                RefreshCoverageFromRegisteredPlots();

                DiagScanEnd(candidates.Count, selected.Length, granted.Length);

                // P0 fix: an empty or reachability-starved registry is now visible in the log
                // without the temporary diagnostics switch.
                WarnIfRegistrationLooksBroken();

                // BUG-07: per-cell reachability query throws are reported once per completed
                // scan, never once per cell (round 28 flooded the log with full-stack
                // compat.failed lines for a condition that is not a compatibility event).
                WarnIfScanQueriesFailed();

                if (deferredFrames > 3)
                {
                    _log.Write(HdbfLogCategory.Info, HdbfLogKeys.PlotScanBudget,
                        "plot scan deferred across " + deferredFrames + " frames, entityId=" + _entityId);
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyScan,
                    "plot selection failed; keeping the previous registry", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Rebuilds the aligned plot arrays for a new id set, preserving crop/fatigue state of
        /// plots that survive the rescan (REQ-IMPL-07: identity never changes).
        /// </summary>
        private void ApplyPlotArrays(int[] plotIds)
        {
            var oldIds = _record.PlotIds;
            var oldCrops = _record.CropIds;
            var oldFatigue = _record.Fatigue;

            _oldIndexByPlot.Clear();
            for (var i = 0; i < oldIds.Length; i++)
            {
                _oldIndexByPlot[oldIds[i]] = i;
            }

            var crops = new string[plotIds.Length];
            var fatigue = new float[plotIds.Length];
            for (var i = 0; i < plotIds.Length; i++)
            {
                if (_oldIndexByPlot.TryGetValue(plotIds[i], out var oldIndex))
                {
                    crops[i] = oldCrops[oldIndex];
                    fatigue[i] = oldFatigue[oldIndex];
                }
                else
                {
                    crops[i] = string.Empty;
                }
            }

            _record.PlotIds = plotIds;
            _record.CropIds = crops;
            _record.Fatigue = fatigue;
            _saveDirty = true;

            // The managed plot set changed: today's farm plan and its path cache are stale.
            InvalidateFarmPlan();

            // Keep the cached coverage consistent with the new denominator. The rescan path
            // resamples immediately after this call (OnPlotsScanned); the saved-plot claim and
            // player-exclusion paths only clamp here, so removing plots can never leave coverage
            // above 100 % and an empty registry can never divide by zero. Before the P0 card this
            // block only ran when _coverage > 0, which is exactly why the panel stayed at 0 %
            // until the first day tick.
            ClampCoverageToRegisteredPlots();

            // BUILD-05: the managed plot set changed, so mirror it into the crop-visibility cache
            // and restore the crop models of plots that are no longer managed.
            SyncCropVisibilityPlots();
        }

        /// <summary>
        /// Clears the per-scan counters. These are the production counters that feed the
        /// empty-registry warning and the (dev-only) diagnostics line; they must never depend on
        /// the HDBF_DEV_DIAGNOSTICS switch, so the release build keeps exactly this state.
        /// </summary>
        private void ResetScanCounters()
        {
            _scanStats.Reset();
            _retentionProbeWarned = false;
        }

        /// <summary>
        /// Makes an empty or reachability-starved plot registry visible without the diagnostics
        /// switch (P0: "coverage is 0 %" used to be indistinguishable from "nothing registered").
        /// Runs once per completed scan; the log key is the throttle identity, so a farm that
        /// stays starved reports at most once per throttle window.
        /// </summary>
        private void WarnIfRegistrationLooksBroken()
        {
            try
            {
                var registered = _record is null ? 0 : _record.PlotIds.Length;
                var key = _scanStats.WarningKey(registered);
                if (key is null)
                {
                    return;
                }

                _log.Write(HdbfLogCategory.Warn, key,
                    _scanStats.Describe(registered, _entityId.ToString()));
            }
            catch (Exception exception)
            {
                // Diagnostics must never break the scan or the simulation (REQ-SAFE-02).
                _log.Error(HdbfLogCategory.Warn, LogKeyScan,
                    "plot registration warning failed", exception, _entityId.ToString());
            }
        }

        /// <summary>
        /// BUG-07: one throttled, stack-free warning per completed scan when per-cell reachability
        /// queries threw (round-28 in-game: <c>InvalidOperationException: There's no cached flow
        /// field</c>). Pathing is a filter, not a compatibility failure, so this uses its own
        /// <see cref="HdbfLogKeys.ScanReachability"/> key instead of <c>compat.failed</c>. The key
        /// carries the building entity id (per-building 30 s throttle) and the message carries the
        /// cumulative counters, the first failed cell and the exception type only. Emitting here,
        /// at scan end, is what turns the 27x27 Advanced scan from one warning per failed cell into
        /// at most one line; the failed cells themselves are still excluded as before.
        /// </summary>
        private void WarnIfScanQueriesFailed()
        {
            try
            {
                var key = _scanStats.ReachabilityFailureKey();
                if (key is null)
                {
                    return;
                }

                _log.Write(HdbfLogCategory.Warn, key + "." + _entityId,
                    _scanStats.DescribeReachabilityFailures(_entityId.ToString()));
            }
            catch (Exception exception)
            {
                // Diagnostics must never break the scan (REQ-SAFE-02).
                _log.Error(HdbfLogCategory.Warn, LogKeyScan,
                    "scan reachability failure warning failed", exception, _entityId.ToString());
            }
        }

        private int ScanBudgetPerFrame()
        {
            var budget = _configProvider.Config.Perf.ScanBudgetPerFrame;
            return budget < 1 ? 1 : budget;
        }

        private static int[] ToIntArray(HashSet<int> values)
        {
            var result = new int[values.Count];
            var index = 0;
            foreach (var value in values)
            {
                result[index++] = value;
            }

            return result;
        }
    }
}

