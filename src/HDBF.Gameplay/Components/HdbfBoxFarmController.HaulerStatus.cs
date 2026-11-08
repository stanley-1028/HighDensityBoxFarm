// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Guarantees that the vanilla "Requires haulers to function." status
//          (Status.Hauling.NoHaulingPost) never appears on a Box Farm: the farm
//          works with zero haulers (buffered output, pipe/manual water), so the
//          vanilla status is suppressed through its public opt-out.
// Requirements: REQ-V2-WATER-02, REQ-V2-UI-02, REQ-V2-SAFE-01
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.Hauling;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Partial part: the vanilla "requires haulers" status guard (player report 2026-11-08:
    /// "even with no haulers, the farm must not display 'requires haulers to function'").
    ///
    /// Why the farm does not need that status: a Box Farm keeps working without haulers. The
    /// harvest first fills the in-farm output buffer (one full day of the tier's harvest, see
    /// <see cref="IndoorConstants.OutputCapacityFor"/>) and haulers only empty that buffer
    /// afterwards; the internal water tank is refilled by the pipe network or by the manual refill
    /// command. A warning that claims the building "requires haulers to function" is therefore
    /// misleading on this building.
    ///
    /// Vanilla attaches <c>NoHaulingPostStatus</c> only when a building's own configuration adds
    /// it: worker-free good-consuming buildings, obtainable stockpiles and firework launchers
    /// (verified from the game IL on 1.1.2.8), and the Box Farm adds none of those. This guard
    /// turns that into a hard guarantee for the player: if the component is ever present (a
    /// vanilla change, a third-party mod or a future port), the controller uses the public
    /// <c>NoHaulingPostStatus.Disable()</c> opt-out - the same call the vanilla initializers use
    /// themselves - and repeats it on every daily tick, so a re-activation (e.g. a district
    /// reassignment) cannot keep the status visible. No runtime patching, no reflection, no
    /// save-state change; the vanilla hauling chain is untouched, so haulers still empty the farm
    /// output.
    ///
    /// Cost: one component lookup per activation/post-load plus two null checks per day tick -
    /// never per frame. Failure policy (REQ-V2-SAFE-01): every vanilla call is exception-isolated,
    /// so a missing or throwing status component degrades to "the status may stay visible", never
    /// to a broken farm.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        /// <summary>Vanilla status component resolved on this farm; null when vanilla does not attach one.</summary>
        private NoHaulingPostStatus? _noHaulingPostStatus;

        /// <summary>True once the one-time presence probe ran for the current activation.</summary>
        private bool _haulerStatusProbed;

        /// <summary>
        /// Probes the vanilla "requires haulers" status once per activation and disables it whenever
        /// it is present. Called on activation, once more after the save finished loading (whether a
        /// third-party component was attached late, and to cover listener ordering against the
        /// vanilla initializer) and on every daily tick, which keeps a re-activated status down.
        /// Idempotent and never throws.
        /// </summary>
        private void SuppressHaulerRequirementStatus()
        {
            if (!_haulerStatusProbed)
            {
                _haulerStatusProbed = true;
                try
                {
                    if (HasComponent<NoHaulingPostStatus>())
                    {
                        _noHaulingPostStatus = GetComponent<NoHaulingPostStatus>();
                    }
                    else
                    {
                        _log.Write(HdbfLogCategory.Info, IndoorLogKeys.HaulerStatus,
                            "no vanilla 'requires haulers' status on this farm (the farm works without"
                            + " haulers; the status is suppressed if it is ever attached), entityId="
                            + _entityId);
                    }
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.HaulerStatus,
                        "hauler status probe failed; the farm keeps running", exception,
                        _entityId.ToString());
                }
            }

            if (_noHaulingPostStatus is null)
            {
                return;
            }

            try
            {
                // Public vanilla opt-out: deactivates the status toggle and drops its hauling-range
                // subscription, exactly like the vanilla initializers do on OnExitFinishedState.
                _noHaulingPostStatus.Disable();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.HaulerStatus,
                    "vanilla 'requires haulers' status could not be disabled", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Forgets the resolved status between activations, so a rebuilt/reloaded/re-activated farm
        /// probes and suppresses again instead of trusting a stale reference.
        /// </summary>
        private void ResetHaulerRequirementStatus()
        {
            _noHaulingPostStatus = null;
            _haulerStatusProbed = false;
        }
    }
}
