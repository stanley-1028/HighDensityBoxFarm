using System;
using HDBF.Core;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using Timberborn.Fields;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: BUILD-05 managed-crop visual hiding ("托管格上的农作物视觉完全隐藏").
    ///
    /// The real vanilla <c>Crop</c>/<c>Growable</c> entities are still planted, grown, harvested and
    /// yielded on this farm's managed plots; only their models are hidden so that just the
    /// building's internal slots stay visible from the ground (user design decision 2026-10-25).
    /// The renderer-only mechanism lives in <see cref="HdbfCropVisuals"/>.
    ///
    /// Workers plant and the vanilla harvest loop removes crops without notifying the mod, so this
    /// part polls the farm's <b>own</b> registered plot cells (<c>_record.PlotIds</c> only, never a
    /// map walk) on a low, configurable interval (<c>perf.cropVisibilityIntervalSeconds</c>) in
    /// per-frame slices of at most <c>perf.cropVisibilityCellsPerFrame</c> cells. The hide/restore
    /// decisions, the identity cache and the sharded cursor live in
    /// <see cref="ManagedCropVisibilityState"/> (HDBF.Core, game-free and unit-tested).
    ///
    /// Restore paths — a crop that must no longer be hidden becomes visible again:
    /// * plot dropped from the registration (rescan capacity cut, player exclusion, save load):
    ///   <see cref="SyncCropVisibilityPlots"/> immediately restores the ids the Core contract
    ///   returns for plots that left the managed set;
    /// * <c>ui.hideManagedCrops</c> turned off: <see cref="StepCropVisibility"/> restores all;
    /// * building leaves the finished state or is deleted: <see cref="RestoreAllCropVisuals"/> from
    ///   <c>Deactivate</c>/<c>DeleteEntity</c>.
    ///
    /// Failure shape: each cell is isolated in its own try/catch (one broken cell never aborts the
    /// sweep or the frame), failures are counted and reported through one throttled Warn per
    /// building, and one HDBF/Info line per day reports hidden/registered/failure figures (skipped
    /// while the farm is idle). Nothing here throws into the game loop (REQ-SAFE-01/02).
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        private const float MinCropVisibilityIntervalSeconds = 0.5f;
        private const float MaxCropVisibilityIntervalSeconds = 60f;

        /// <summary>Game-free decision/cache state machine of BUILD-05 (per-building instance state).</summary>
        private readonly ManagedCropVisibilityState _cropVisibility = new ManagedCropVisibilityState();

        /// <summary>Per-frame shard buffer for the refresh walk; reallocated only when the budget changes.</summary>
        private int[] _cropShard = Array.Empty<int>();

        private bool _cropCycleActive;
        private bool _cropRefreshRequested;
        private float _cropNextCycleAt;
        private int _cropHideFailures;
        private int _cropRestoreFailures;

        /// <summary>
        /// Controller-local synthetic crop identity (monotonic Guid) used when a crop entity has no
        /// readable <c>EntityComponent.EntityId</c>. Instance state only; never static.
        /// </summary>
        private Guid _syntheticCropIdentity;

        // ---------------------------------------------------------------- lifecycle

        /// <summary>
        /// Starts the visual hiding with the building (called from Activate, after the world-icon
        /// registration). When <c>ui.hideManagedCrops</c> is off the switch-off path restores every
        /// crop the farm may still hide and leaves the refresh disabled.
        /// </summary>
        private void StartCropVisibility()
        {
            try
            {
                if (_record is null)
                {
                    return;
                }

                if (!_configProvider.Config.Ui.HideManagedCrops)
                {
                    var restore = _cropVisibility.SetHideEnabled(false);
                    RestoreManagedCropPlots(restore);
                    _log.Write(HdbfLogCategory.Info, HdbfLogKeys.CropVisibility + "." + _entityId + ".start",
                        "crop visibility inactive (ui.hideManagedCrops=false), entityId=" + _entityId);
                    return;
                }

                _cropVisibility.SetHideEnabled(true);
                SyncCropVisibilityPlots();
                _cropRefreshRequested = true;
                _log.Write(HdbfLogCategory.Info, HdbfLogKeys.CropVisibility + "." + _entityId + ".start",
                    "crop visibility active, entityId=" + _entityId);
            }
            catch (Exception exception)
            {
                // Visual polish must never prevent the farm from activating (REQ-SAFE-01).
                WarnCropVisibilityFailed("start", 0, Vector3Int.zero, exception);
            }
        }

        /// <summary>
        /// Mirrors the managed plot set into the visibility cache and restores the crop visuals of
        /// plots that are no longer managed. Called after every registration change (a rescan, a
        /// player exclusion, a save load) and from <see cref="StartCropVisibility"/>; a cheap no-op
        /// before the first scan because the record's plot list is then empty.
        /// </summary>
        private void SyncCropVisibilityPlots()
        {
            try
            {
                if (_record is null)
                {
                    return;
                }

                var restore = _cropVisibility.SetRegisteredPlots(_record.PlotIds);
                RestoreManagedCropPlots(restore);
                _cropRefreshRequested = true;
            }
            catch (Exception exception)
            {
                // A registration sync failure must not break the scan that triggered it.
                WarnCropVisibilityFailed("sync", 0, Vector3Int.zero, exception);
            }
        }

        /// <summary>
        /// Drops the whole hide cache and restores every currently hidden crop visual (switch off,
        /// deactivate, delete). One-shot; stops any refresh cycle in progress.
        /// </summary>
        private void RestoreAllCropVisuals(string reason)
        {
            try
            {
                var restore = _cropVisibility.RestoreAll();
                RestoreManagedCropPlots(restore);
                _cropCycleActive = false;
                _cropRefreshRequested = false;

                if (restore.Length > 0)
                {
                    _log.Write(HdbfLogCategory.Info,
                        HdbfLogKeys.CropVisibility + "." + _entityId + ".restore-" + reason,
                        "crop visibility restored, reason=" + reason + ", plots=" + restore.Length
                        + ", entityId=" + _entityId);
                }
            }
            catch (Exception exception)
            {
                WarnCropVisibilityFailed("restore-all:" + reason, 0, Vector3Int.zero, exception);
            }
        }

        /// <summary>
        /// Enables the renderers of the current crop on each plot that is no longer supposed to be
        /// hidden. The plot is not managed any more, so visible is correct even when the crop entity
        /// has been replaced meanwhile. A plot whose crop is already gone (harvested/removed) is a
        /// no-op; each plot is isolated so one broken cell cannot block the other restores.
        /// </summary>
        private void RestoreManagedCropPlots(int[] plotIds)
        {
            if (plotIds is null)
            {
                return;
            }

            for (var i = 0; i < plotIds.Length; i++)
            {
                var plotId = plotIds[i];
                var cell = Vector3Int.zero;
                try
                {
                    cell = HdbfPlotCellCodec.Decode(plotId);
                    var crop = _blockService.GetFirstObjectWithComponentAt<Crop>(cell);
                    if (crop is null)
                    {
                        // Harvested/removed entity: nothing to make visible again.
                        continue;
                    }

                    if (!HdbfCropVisuals.TryRestore(crop, out var failure) && failure is not null)
                    {
                        _cropRestoreFailures++;
                        WarnCropVisibilityFailed("restore", plotId, cell, failure);
                    }
                }
                catch (Exception exception)
                {
                    _cropRestoreFailures++;
                    WarnCropVisibilityFailed("restore", plotId, cell, exception);
                }
            }
        }

        // ---------------------------------------------------------------- per-frame refresh

        /// <summary>
        /// One per-frame slice of the low-frequency managed-crop visibility refresh, called from
        /// <c>Tick</c> after the farm-path measurement step. Cheap in the steady state: it only
        /// checks the config switch and a wall-clock gate until an interval cycle is due.
        /// </summary>
        private void StepCropVisibility()
        {
            if (!_activated || _record is null)
            {
                return;
            }

            try
            {
                var config = _configProvider.Config;
                if (!config.Ui.HideManagedCrops)
                {
                    if (_cropVisibility.HiddenCount > 0)
                    {
                        RestoreAllCropVisuals("config-off");
                    }

                    return;
                }

                if (!_cropVisibility.HideEnabled)
                {
                    // Defensive: the switch is on but the cache still thinks it is off (e.g. a
                    // previous activation failed before enabling it). Re-enable and re-sweep.
                    _cropVisibility.SetHideEnabled(true);
                    _cropRefreshRequested = true;
                }

                var budget = CropVisibilityCellsPerFrame(config.Perf.CropVisibilityCellsPerFrame);
                if (_cropShard.Length != budget)
                {
                    _cropShard = new int[budget];
                }

                var now = Time.unscaledTime;
                if (!_cropCycleActive)
                {
                    // A registration change (new scan) restarts the walk immediately; otherwise the
                    // next cycle only starts after the configured interval (BUILD-05 latency bound:
                    // one interval + one sweep).
                    if (!_cropRefreshRequested && now < _cropNextCycleAt)
                    {
                        return;
                    }

                    _cropVisibility.BeginRefreshCycle();
                    _cropCycleActive = true;
                    _cropRefreshRequested = false;
                }

                var interval = ClampCropVisibilityInterval(config.Perf.CropVisibilityIntervalSeconds);
                var count = _cropVisibility.TakeRefreshShard(_cropShard, _cropShard.Length, out var completed);

                for (var i = 0; i < count; i++)
                {
                    ProcessCropPlot(_cropShard[i]);
                }

                if (completed)
                {
                    _cropCycleActive = false;
                    _cropNextCycleAt = now + interval;
                }
                else if (count == 0 && _cropVisibility.RegisteredCount == 0)
                {
                    // Nothing registered: complete the (empty) cycle so the interval gate applies
                    // instead of leaving the cycle flag stuck until a plot appears.
                    _cropCycleActive = false;
                    _cropNextCycleAt = now + interval;
                }
            }
            catch (Exception exception)
            {
                // A broken sweep must never abort Tick; the next frame retries the shard.
                _cropCycleActive = false;
                WarnCropVisibilityFailed("step", 0, Vector3Int.zero, exception);
            }
        }

        /// <summary>
        /// Observes one registered plot cell and applies the hide/restore decision of the Core state
        /// machine. Every step is isolated so one broken cell never aborts the sweep or the frame.
        /// </summary>
        private void ProcessCropPlot(int plotId)
        {
            try
            {
                var cell = HdbfPlotCellCodec.Decode(plotId);
                var crop = _blockService.GetFirstObjectWithComponentAt<Crop>(cell);
                var identity = ResolveCropIdentity(crop, plotId);
                var action = _cropVisibility.Observe(plotId, crop is not null, identity);

                if (action == ManagedCropVisualAction.None || crop is null)
                {
                    // A Restore for a harvested crop is already satisfied: the entity is gone and
                    // the cache entry was dropped by Observe.
                    return;
                }

                if (action == ManagedCropVisualAction.Hide)
                {
                    if (!HdbfCropVisuals.TryHide(crop, out var failure) && failure is not null)
                    {
                        // Roll the cache entry back so the next sweep retries this cell instead of
                        // reporting "already hidden" forever (a failed toggle must not leave a
                        // visible crop on a managed plot).
                        _cropVisibility.Forget(plotId);
                        _cropHideFailures++;
                        WarnCropVisibilityFailed("hide", plotId, cell, failure);
                    }
                }
                else if (action == ManagedCropVisualAction.Restore)
                {
                    if (!HdbfCropVisuals.TryRestore(crop, out var failure) && failure is not null)
                    {
                        _cropRestoreFailures++;
                        WarnCropVisibilityFailed("restore", plotId, cell, failure);
                    }
                }
            }
            catch (Exception exception)
            {
                // Per-cell isolation: count it, throttled Warn, keep sweeping (never rethrow).
                _cropHideFailures++;
                WarnCropVisibilityFailed("process", plotId, Vector3Int.zero, exception);
            }
        }

        /// <summary>
        /// Crop identity for the visibility cache: the vanilla <c>EntityComponent.EntityId</c>. A
        /// crop without a readable id falls back to a controller-local synthetic Guid, but reuses
        /// the already cached identity of that plot so repeated sweeps stay idempotent; a replanted
        /// crop gets a fresh id because the harvest observation drained the cache entry first.
        /// </summary>
        private Guid ResolveCropIdentity(Crop? crop, int plotId)
        {
            if (crop is not null && HdbfCropVisuals.TryGetCropIdentity(crop, out var entityId))
            {
                return entityId;
            }

            if (crop is not null && _cropVisibility.TryGetHiddenIdentity(plotId, out var cached)
                && cached != Guid.Empty)
            {
                return cached;
            }

            return NextSyntheticCropIdentity();
        }

        /// <summary>
        /// Monotonic controller-local Guid counter (instance field only, no static state): each new
        /// synthetic identity differs from every previous one, so a crop the game replaced without a
        /// readable entity id is not mistaken for the crop that is already hidden.
        /// </summary>
        private Guid NextSyntheticCropIdentity()
        {
            var bytes = _syntheticCropIdentity.ToByteArray();
            for (var i = 0; i < bytes.Length; i++)
            {
                if (++bytes[i] != 0)
                {
                    break;
                }
            }

            _syntheticCropIdentity = new Guid(bytes);
            return _syntheticCropIdentity;
        }

        // ---------------------------------------------------------------- observability

        /// <summary>
        /// One HDBF/Info line per game day with the hidden crop count, the registered plot count and
        /// the hide/restore failure counters. Skipped (and the counters reset) when the farm did
        /// nothing visible, so an idle farm never spams the log. Called from <c>RunDailyTick</c>
        /// right after the farm day tick.
        /// </summary>
        private void LogCropVisibilityDaily()
        {
            try
            {
                var hidden = _cropVisibility.HiddenCount;
                var hideFailures = _cropHideFailures;
                var restoreFailures = _cropRestoreFailures;
                _cropHideFailures = 0;
                _cropRestoreFailures = 0;

                if (hidden == 0 && hideFailures == 0 && restoreFailures == 0)
                {
                    return;
                }

                _log.Write(HdbfLogCategory.Info, HdbfLogKeys.CropVisibility + "." + _entityId,
                    "crop visibility hidden=" + hidden
                    + " registered=" + _cropVisibility.RegisteredCount
                    + " hideFailures=" + hideFailures
                    + " restoreFailures=" + restoreFailures
                    + ", entityId=" + _entityId);
            }
            catch (Exception exception)
            {
                // Diagnostics must never break the daily simulation step (REQ-SAFE-02).
                WarnCropVisibilityFailed("daily", 0, Vector3Int.zero, exception);
            }
        }

        /// <summary>
        /// One throttled Warn per building for any crop-visual operation that failed; the log itself
        /// owns the throttle (same key + entity id) so a broken cell cannot flood the log.
        /// </summary>
        private void WarnCropVisibilityFailed(string context, int plotId, Vector3Int cell, Exception exception)
        {
            try
            {
                _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.CropVisibilityFailed + "." + _entityId,
                    "crop visibility failed; context=" + context + " plotId=" + plotId + " cell=" + cell
                    + " exception=" + exception.GetType().Name + ": " + exception.Message);
            }
            catch (Exception)
            {
                // Never let logging break the sweep or the frame.
            }
        }

        private static float ClampCropVisibilityInterval(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < MinCropVisibilityIntervalSeconds)
            {
                return MinCropVisibilityIntervalSeconds;
            }

            return seconds > MaxCropVisibilityIntervalSeconds
                ? MaxCropVisibilityIntervalSeconds
                : seconds;
        }

        private static int CropVisibilityCellsPerFrame(int configured)
        {
            return configured < 1 ? 1 : configured;
        }
    }
}
