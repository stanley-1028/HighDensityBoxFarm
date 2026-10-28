// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Continuous water drain (player request 2026-10-28): turns the game time elapsed since the
//          previous frame into a water slice and lets every finished farm consume it, so the tank
//          drains per second instead of one daily deduction. The slice comes from
//          IDayNightCycle.PartialDayNumber, so a paused game drains nothing and game speed scales the
//          drain; one game day always consumes exactly the daily demand.
// Requirements: REQ-V2-WATER-02, REQ-V2-PERF-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Logging;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Per-frame bridge between game time and the farms' water tanks. The server keeps a registry of
    /// finished farms (the controller registers/unregisters on the finished-state transitions) and
    /// hands each of them the elapsed game-day delta; the tank math itself stays pure in
    /// <c>HDBF.Core.Indoor.IndoorWaterMath</c> (no game types in Core, REQ-ARCH-01).
    ///
    /// Failure policy (REQ-V2-SAFE-01): one broken farm is isolated inside the controller; a broken
    /// day counter only skips the drained frame, never the day tick.
    /// </summary>
    public sealed class HdbfWaterFlowService : IUpdatableSingleton, IUnloadableSingleton
    {
        /// <summary>
        /// Largest accepted frame delta in game days. Anything bigger means the game just loaded or
        /// the machine stalled for a long time; draining it at once would empty a tank instantly, so
        /// the frame only re-synchronises the clock.
        /// </summary>
        private const float MaxElapsedGameDays = 0.25f;

        private const string LogKeyFlow = "water.flow";

        private readonly IDayNightCycle _dayNightCycle;
        private readonly IHdbfLog _log;
        private readonly List<HdbfBoxFarmController> _farms = new List<HdbfBoxFarmController>();

        private float _lastPartialDay;
        private bool _initialized;

        /// <summary>Creates the service from the game clock and the mod log.</summary>
        /// <param name="dayNightCycle">Game clock; <c>PartialDayNumber</c> advances with game speed.</param>
        /// <param name="log">Throttled mod log.</param>
        public HdbfWaterFlowService(IDayNightCycle dayNightCycle, IHdbfLog log)
        {
            _dayNightCycle = dayNightCycle ?? throw new ArgumentNullException(nameof(dayNightCycle));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Number of farms currently drained by this service (diagnostics/tests only).</summary>
        public int RegisteredFarmCount => _farms.Count;

        /// <summary>
        /// Starts draining one finished farm every frame. Idempotent, so a re-entered finished state
        /// (construction-mode toggle) cannot register the same farm twice.
        /// </summary>
        /// <param name="farm">Finished farm controller; never used after <see cref="Unregister"/>.</param>
        public void Register(HdbfBoxFarmController farm)
        {
            if (farm is null)
            {
                return;
            }

            for (var index = 0; index < _farms.Count; index++)
            {
                if (ReferenceEquals(_farms[index], farm))
                {
                    return;
                }
            }

            _farms.Add(farm);
        }

        /// <summary>Stops draining one farm (left the finished state or being deleted). Idempotent.</summary>
        /// <param name="farm">Farm controller to remove.</param>
        public void Unregister(HdbfBoxFarmController farm)
        {
            if (farm is null)
            {
                return;
            }

            for (var index = _farms.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(_farms[index], farm))
                {
                    _farms.RemoveAt(index);
                }
            }
        }

        /// <inheritdoc />
        public void UpdateSingleton()
        {
            if (_farms.Count == 0)
            {
                return;
            }

            try
            {
                var partialDay = _dayNightCycle.PartialDayNumber;
                if (float.IsNaN(partialDay) || float.IsInfinity(partialDay))
                {
                    return;
                }

                if (!_initialized)
                {
                    // First frame after a load/start: only take the clock reference, never a backlog.
                    _initialized = true;
                    _lastPartialDay = partialDay;
                    return;
                }

                var elapsedGameDays = partialDay - _lastPartialDay;
                _lastPartialDay = partialDay;
                if (!(elapsedGameDays > 0f) || elapsedGameDays > MaxElapsedGameDays)
                {
                    return;
                }

                for (var index = _farms.Count - 1; index >= 0; index--)
                {
                    var farm = _farms[index];
                    if (farm is null)
                    {
                        _farms.RemoveAt(index);
                        continue;
                    }

                    farm.ConsumeElapsedWater(elapsedGameDays);
                }
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a broken clock/registry only skips the drained frame.
                _log.Error(HdbfLogCategory.Warn, LogKeyFlow,
                    "continuous water drain pass failed; the daily tick keeps the tank consistent",
                    exception);
            }
        }

        /// <inheritdoc />
        public void Unload()
        {
            _farms.Clear();
            _initialized = false;
            _lastPartialDay = 0f;
        }
    }
}
