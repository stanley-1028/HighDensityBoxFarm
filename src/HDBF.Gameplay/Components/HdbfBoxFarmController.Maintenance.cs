// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Automatic-maintenance wiring of one indoor farm (WP-02 / RE-01 / RE-07): the
//          persisted per-building on/off toggle, the config default for a record that never
//          stored a choice, the daily pass that pays one catch-up bill from the nearest
//          stockpile before the day is simulated, and the snapshot helpers (catch-up bill,
//          read-only pipe probe) the panel reads. Every action is all-or-nothing.
// Requirements: REQ-V2-BAL-02, REQ-V2-UI-02, REQ-V2-SAFE-01, REQ-V2-READ-03/04
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>Auto-maintenance toggle log key (plan section 4 step 4); indoor.* family.</summary>
        private const string LogKeyAutoMaintenance = "indoor.auto-maintenance";

        /// <summary>
        /// Stores one per-building automatic-maintenance choice (WP-02). From this point on the
        /// record no longer follows the config default, so the choice is persisted with the save and
        /// a later config change cannot silently flip this building. Rejected on an inactive farm.
        /// </summary>
        /// <param name="enabled">True to let the daily tick pay the maintenance automatically.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the choice was stored.</returns>
        public bool TrySetAutoMaintenance(bool enabled, out string? failureKey)
        {
            failureKey = null;
            var record = _record;
            if (record is null || !_activated)
            {
                failureKey = HdbfIndoorCommandFailures.NotActive;
                return false;
            }

            try
            {
                record.AutoMaintenance = enabled;
                record.AutoMaintenanceSpecified = true;
                _saveDirty = true;
                _log.Write(HdbfLogCategory.Info, LogKeyAutoMaintenance,
                    "indoor automatic maintenance set to " + (enabled ? "on" : "off") + ", entityId="
                    + _entityId);
                return true;
            }
            catch (Exception exception)
            {
                failureKey = HdbfCommandFailures.Unknown;
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.CommandFailed,
                    "setting the automatic maintenance choice failed; no state changed", exception,
                    _entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Phase 0b of the daily tick (WP-02/RE-01): before the day is simulated, a record that never
        /// stored a choice follows the config default and an enabled farm pays its whole catch-up
        /// bill from the nearest stockpile. The bill and the new due day are exactly the manual ones
        /// (no extra cost); a stockpile that cannot pay the whole bill consumes nothing and the due
        /// day stays, so the manual button and the MissingMaterial warning keep working.
        /// </summary>
        /// <param name="today">Current game day of the tick.</param>
        private void RunAutoMaintenance(float today)
        {
            var record = _record;
            if (record is null || !_activated)
            {
                return;
            }

            try
            {
                if (!record.AutoMaintenanceSpecified)
                {
                    // Old saves carry no choice: follow the config until the player decides once.
                    record.AutoMaintenance = SafeConfigAutoMaintenance();
                }

                if (!record.AutoMaintenance || !(record.MaintenanceDueDay <= today))
                {
                    return;
                }

                if (!IndoorMaintenanceMath.TryGetCatchUpBill(_profile, record, today, out var itemId,
                        out var totalAmount, out var cycles, out var cycleDays) || itemId is null)
                {
                    // Not due yet, basic tier or a grid-maintained top tier: nothing to pay.
                    return;
                }

                var materials = _materials;
                if (materials is null || !materials.TryConsume(itemId, totalAmount))
                {
                    var available = materials is null ? 0 : materials.Available(itemId);
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.MaintenanceAutoMissing,
                        "indoor automatic maintenance could not pay the bill: needed=" + totalAmount
                        + " " + itemId + ", available=" + available + ", entityId=" + _entityId);
                    return;
                }

                // The bill was validated from the same tier/record pair above, so the due-day advance
                // cannot be rejected after the materials were withdrawn.
                if (!TryApplyMaintenance(cycleDays, cycles, out _))
                {
                    return;
                }

                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.MaintenanceAuto,
                    "indoor automatic maintenance paid " + totalAmount + " " + itemId + " for " + cycles
                    + " catch-up cycle(s), dueDay=" + Format(record.MaintenanceDueDay) + ", entityId="
                    + _entityId);
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-01: a broken automatic pass keeps the due day and leaves the manual
                // command as the fallback; the rest of the daily tick still runs.
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.MaintenanceAutoMissing,
                    "automatic maintenance pass failed; the due day is kept", exception,
                    _entityId.ToString());
            }
        }

        /// <summary>
        /// Automatic-maintenance default of the session config (WP-02). A broken config provider
        /// falls back to enabled, matching the shipped <c>features.autoMaintenance</c> default.
        /// </summary>
        /// <returns>Config value of <c>features.autoMaintenance</c>; true when unreadable.</returns>
        private bool SafeConfigAutoMaintenance()
        {
            try
            {
                return _configProvider.Config.Features.AutoMaintenance;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// Catch-up numbers the panel shows for the next maintenance click (WP-01): the cycles that
        /// click would settle and the whole material bill. Zero when nothing is due or the tier has
        /// no material upkeep, so the panel can disable the action with
        /// <see cref="HdbfIndoorCommandFailures.MaintenanceNotDue"/>.
        /// </summary>
        /// <param name="today">Current game day read by the snapshot builder.</param>
        /// <returns>Cycles to catch up and the bill in items; (0, 0) when nothing is due.</returns>
        private (int Cycles, int Amount) CatchUpBillForPanel(float today)
        {
            try
            {
                if (!IndoorMaintenanceMath.TryGetCatchUpBill(_profile, _record, today, out _,
                        out var amount, out var cycles, out _))
                {
                    return (0, 0);
                }

                return (cycles, amount);
            }
            catch (Exception)
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// Read-only pipe probe for the panel (WP-05): the adapter reports why the pipe port is or is
        /// not usable. A port the player switched off (or a tier without a pipe port) is reported as
        /// <see cref="PipeProbeReason.PortOff"/> before the adapter is asked, so the panel never shows
        /// a connected pipe for a disabled port (L-06). A missing probe or a throwing adapter
        /// degrades to <see cref="PipeProbe.Unknown"/>, never into the panel/render path.
        /// </summary>
        /// <returns>Probe result, or <see cref="PipeProbe.Unknown"/> when no probe is available.</returns>
        private PipeProbe ProbePipeState()
        {
            try
            {
                var record = _record;
                if (record is null)
                {
                    return PipeProbe.Unknown;
                }

                if (!_spec.HasPipePort || !record.PipePort)
                {
                    return new PipeProbe(PipeProbeReason.PortOff, 0, 0, "port-off");
                }

                return (_adapterHost as IIndoorPipeProbe)?.ProbePipe() ?? PipeProbe.Unknown;
            }
            catch (Exception)
            {
                return PipeProbe.Unknown;
            }
        }
    }
}
