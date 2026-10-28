// ============================================================================
// HDBF v2 | Layer: Gameplay/Farming | Owner: V2-GAMEPLAY-01
// Purpose: Minimal workplace behavior for an indoor farm: keeps vanilla employment and the
//          building work slots, never dispatches vanilla planting/harvest/carry work and never
//          touches world cells. No plant/harvest API is called from here.
// Requirements: REQ-V2-LABOR-03, REQ-V2-LABOR-04, REQ-V2-CORE-03, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.WorkSystem;

namespace HDBF.Gameplay.Farming
{
    /// <summary>
    /// The replacement for the v1 farm work dispatcher. Vanilla has no generic "work at the
    /// workplace and play the work animation" behavior: <c>LaborWorkplaceBehavior</c> drives
    /// building-side <c>LaborBehavior</c> components that do not exist on an HDBF blueprint, and
    /// every other workplace behavior dispatches a concrete world task. The minimum safe behavior
    /// therefore keeps the worker employed through the vanilla <see cref="Workplace"/> (assignment
    /// and work slots are untouched by this class) and declines every action, so no vanilla
    /// planting, harvesting, navigation or field task can ever be started for indoor slots.
    ///
    /// This is an intentional approximation for V2-GAMEPLAY-01: farmers stay employed at the
    /// building and the vanilla <c>WaitInsideIdlyWorkplaceBehavior</c> keeps them waiting inside the
    /// building while on duty (2026-10-28 player request); the indoor planting/harvesting itself is
    /// simulated by the daily tick, so no bespoke indoor work animation is needed.
    /// </summary>
    public class IndoorFarmWorkplaceBehavior : WorkplaceBehavior, IAwakableComponent
    {
        private const string LogKey = IndoorLogKeys.Day;

        private readonly IHdbfLog _log;
        private bool _workplacePresent;

        /// <summary>Creates the behavior; the controller/log is injected by Bindito.</summary>
        /// <param name="log">Throttled log sink for a missing workplace component.</param>
        public IndoorFarmWorkplaceBehavior(IHdbfLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public void Awake()
        {
            try
            {
                _workplacePresent = HasComponent<Workplace>();
            }
            catch (Exception)
            {
                _workplacePresent = false;
            }

            if (_workplacePresent)
            {
                return;
            }

            try
            {
                _log.Write(HdbfLogCategory.Warn, LogKey,
                    "indoor farm workplace behavior has no Workplace component; workers stay assigned "
                    + "only when the blueprint provides employment");
            }
            catch (Exception)
            {
                // Diagnostics must never break component initialization (REQ-V2-SAFE-01).
            }
        }

        /// <inheritdoc />
        public override Decision Decide(BehaviorAgent agent)
        {
            try
            {
                // No world interaction: the daily tick owns all indoor farm actions. Making workers
                // available lets vanilla idling/need behaviors run without unlocking any crop task.
                return Decision.ReleaseNow();
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a behavior fault must never reach the game loop.
                _log.Error(HdbfLogCategory.Warn, LogKey,
                    "indoor workplace behavior failed; releasing the worker", exception);
                return Decision.ReleaseNow();
            }
        }
    }
}
