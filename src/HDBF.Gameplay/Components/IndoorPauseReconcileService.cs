// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Session singleton that keeps the paused (blocked) farms' enterable state clean
//          (player report 2026-11-02: a paused save loads its beavers back inside the blocked
//          farm, because the one-shot PausedChanged ejection runs before the enterers are
//          restored). Every ~1 s it asks each registered paused farm to reconcile its
//          residual enterers, so the window in which the vanilla Enterer/Enterable invariants
//          see "employed worker inside a blocked building" stays minimal.
// Requirements: REQ-V2-SAFE-01, REQ-V2-LABOR-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Holds the list of farms that are currently held by the vanilla building pause and lets each
    /// of them re-run the beaver ejection once per second (V2-GAMEPLAY-01 pause-residual fix,
    /// 2026-11-02). The controller registers on pause entry and unregisters on resume/deactivation,
    /// so a normal working farm never pays for this pass. A broken farm is isolated per entity and
    /// a broken registry only skips the pass (REQ-V2-SAFE-01).
    /// </summary>
    public sealed class IndoorPauseReconcileService : IUpdatableSingleton, IUnloadableSingleton
    {
        /// <summary>Seconds between two reconcile passes of the paused farms (about 1 Hz).</summary>
        private const float ReconcileIntervalSeconds = 1f;

        private readonly IHdbfLog _log;
        private readonly List<HdbfBoxFarmController> _pausedFarms = new List<HdbfBoxFarmController>();
        private float _nextReconcileTime;

        /// <summary>Creates the reconcile service from the throttled mod log.</summary>
        /// <param name="log">Throttled mod log; never null.</param>
        public IndoorPauseReconcileService(IHdbfLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Number of paused farms currently held by this service (diagnostics/tests only).</summary>
        public int RegisteredFarmCount => _pausedFarms.Count;

        /// <summary>Starts reconciling one paused farm. Idempotent.</summary>
        /// <param name="farm">Paused farm controller; never used after <see cref="Unregister"/>.</param>
        public void Register(HdbfBoxFarmController farm)
        {
            if (farm is null)
            {
                return;
            }

            for (var index = 0; index < _pausedFarms.Count; index++)
            {
                if (ReferenceEquals(_pausedFarms[index], farm))
                {
                    return;
                }
            }

            _pausedFarms.Add(farm);
            // Reconcile on the next pass instead of waiting a full second after a load.
            _nextReconcileTime = 0f;
        }

        /// <summary>Stops reconciling one farm (resume, lost finished state or deletion). Idempotent.</summary>
        /// <param name="farm">Farm controller to remove.</param>
        public void Unregister(HdbfBoxFarmController farm)
        {
            if (farm is null)
            {
                return;
            }

            for (var index = _pausedFarms.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(_pausedFarms[index], farm))
                {
                    _pausedFarms.RemoveAt(index);
                }
            }
        }

        /// <inheritdoc />
        public void UpdateSingleton()
        {
            if (_pausedFarms.Count == 0)
            {
                return;
            }

            try
            {
                var now = Time.unscaledTime;
                if (now < _nextReconcileTime)
                {
                    return;
                }

                _nextReconcileTime = now + ReconcileIntervalSeconds;
                for (var index = _pausedFarms.Count - 1; index >= 0; index--)
                {
                    var farm = _pausedFarms[index];
                    if (farm is null)
                    {
                        _pausedFarms.RemoveAt(index);
                        continue;
                    }

                    farm.ReconcilePausedEnterers();
                }
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: one broken pass only skips the reconcile, never the game loop.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PausedReconcile,
                    "paused farm reconcile pass failed; the next pass retries", exception);
            }
        }

        /// <inheritdoc />
        public void Unload()
        {
            _pausedFarms.Clear();
            _nextReconcileTime = 0f;
        }
    }
}
