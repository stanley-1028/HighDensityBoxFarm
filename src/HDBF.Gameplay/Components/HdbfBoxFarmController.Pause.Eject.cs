// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Beaver ejection of the indoor-farm building pause (player report 2026-10-29 "it crash
//          when you paused it" and 2026-11-02 "crash on pause" residue). The vanilla pause only
//          unassigns the workplace (Workplace.OnObjectBlocked -> UnassignAllWorkers ->
//          Worker.Unemploy), and Worker.Unemploy does not call Enterer.Exit/Enterable.Remove; this
//          farm is the only one that keeps its farmers inside through WaitInsideIdlyWorkplaceBehavior,
//          so a paused farm would otherwise keep abandoned enterers inside a blocked building - the
//          state the vanilla Enterer/Enterable/Accessible invariants are not written for. This
//          partial moves them out through the vanilla UnreserveSlotAndExit path, isolates every
//          beaver, reconciles residual enterers on demand (loaded paused save / walk-in while
//          paused) and reports an eject summary the player log can be read from.
// Requirements: REQ-V2-SAFE-01, REQ-V2-LABOR-03
// Size: above the 300-line soft gate with a written reason (REQ-V2-MAINT-02): the eject/reconcile
//       state machine (one-shot pause ejection, 1 Hz residual reconcile, walk-in guard and the
//       ForceRemoveEveryone-equivalent fallback) is the crash-safety contract of the pause and is
//       reviewed as one unit.
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.EnterableSystem;

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        /// <summary>
        /// One reconcile pass of a paused farm (called about once per second by
        /// <see cref="IndoorPauseReconcileService"/>). A paused save can finish loading its
        /// enterers after the one-shot PausedChanged ejection, and a beaver that was already
        /// walking in can still enter a blocked building (CanEnter only checks Enabled/capacity),
        /// so the residual enterers are both reported and moved out here.
        /// </summary>
        internal void ReconcilePausedEnterers()
        {
            if (!_activated || !_paused)
            {
                return;
            }

            var enterable = _enterable;
            if (enterable is null)
            {
                return;
            }

            int inside;
            try
            {
                inside = enterable.NumberOfEnterersInside;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PausedReconcile,
                    "paused farm enterer count could not be read; reconcile skipped", exception,
                    _entityId.ToString());
                return;
            }

            if (inside <= 0)
            {
                return;
            }

            _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PausedReconcile,
                "indoor paused reconcile: " + inside + " residual enterer(s) inside a blocked farm, entityId="
                + _entityId);
            LogResidualEnterers(enterable, "loaded-inside");
            EjectBeaversFromPausedFarm("reconcile");
        }

        /// <summary>
        /// A beaver was added to the enterable while the farm is paused (walk-in during the pause,
        /// R2 of the 2026-11-02 crash review): move it straight back out so the blocked building
        /// never accumulates enterers between reconcile passes.
        /// </summary>
        private void OnEntererAddedWhilePaused(object? sender, EntererAddedEventArgs args)
        {
            if (!_activated || !_paused)
            {
                return;
            }

            try
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PausedResidualEnterer,
                    "a beaver entered the paused farm while it is blocked; moving it out, entityId="
                    + _entityId + ", enterer=" + DescribeEnterer(args?.Enterer));
            }
            catch (Exception)
            {
                // Diagnostics must never break the pause path (REQ-V2-SAFE-01).
            }

            EjectBeaversFromPausedFarm("added-while-paused");
        }

        /// <summary>Subscribes the "entered while paused" guard; idempotent and failure-isolated.</summary>
        private void BindEntererAdded()
        {
            var enterable = _enterable;
            if (enterable is null)
            {
                return;
            }

            try
            {
                enterable.EntererAdded -= OnEntererAddedWhilePaused;
                enterable.EntererAdded += OnEntererAddedWhilePaused;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "vanilla EntererAdded could not be bound; the 1 Hz reconcile stays the only guard",
                    exception, _entityId.ToString());
            }
        }

        /// <summary>Removes the "entered while paused" guard when the farm leaves the finished state.</summary>
        private void UnbindEntererAdded()
        {
            var enterable = _enterable;
            if (enterable is null)
            {
                return;
            }

            try
            {
                enterable.EntererAdded -= OnEntererAddedWhilePaused;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "vanilla EntererAdded could not be unbound", exception, _entityId.ToString());
            }
        }

        /// <summary>
        /// Moves every beaver out of this farm through the vanilla enterer path (the same
        /// UnreserveSlot+Exit pair the vanilla Enterable uses for its own state changes). A paused
        /// farm is blocked, so a beaver left inside cannot be expected to walk out and the vanilla
        /// enterer/accessible invariants must not be fed that state. Every enterer is isolated: one
        /// broken enterer must never block the pause or the other ejections (REQ-V2-SAFE-01).
        /// </summary>
        /// <param name="reason">Short cause for the eject summary (pause/resume/deactivate/reconcile/...).</param>
        private void EjectBeaversFromPausedFarm(string reason)
        {
            var enterable = _enterable;
            if (enterable is null)
            {
                return;
            }

            List<Enterer>? inside = null;
            try
            {
                if (enterable.NumberOfEnterersInside <= 0)
                {
                    return;
                }

                // Copy first: EnterersInside is a live collection and Exit mutates it.
                inside = new List<Enterer>(enterable.EnterersInside);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                    "beavers inside the paused farm could not be listed", exception, _entityId.ToString());
                return;
            }

            var requested = inside.Count;
            var ejected = 0;
            var skipped = 0;
            for (var index = 0; index < inside.Count; index++)
            {
                var enterer = inside[index];
                try
                {
                    if (enterer is null)
                    {
                        skipped++;
                        continue;
                    }

                    // A half-state enterer (CurrentBuilding already null) would make Exit a no-op
                    // and keep it stuck in the enterable set; skip it and let the fallback clean up.
                    if (!enterer.IsInside
                        || !ReferenceEquals(enterer.CurrentBuilding, enterable))
                    {
                        skipped++;
                        continue;
                    }

                    // UnreserveSlotAndExit (vanilla) also clears a reservation left on the paused
                    // building, which a bare Exit would keep.
                    enterer.UnreserveSlotAndExit();
                    ejected++;
                }
                catch (Exception exception)
                {
                    // One broken enterer must not abort the pause or the other ejections.
                    _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PausedEjectFailed,
                        "a beaver inside the paused farm could not be moved out, reason=" + reason
                        + ", enterer=" + DescribeEnterer(enterer), exception, _entityId.ToString());
                }
            }

            var remaining = CountEnterersInside(enterable);
            var forced = 0;
            if (remaining > 0)
            {
                // Equivalent of the vanilla (private) Enterable.ForceRemoveEveryone: Abandon first
                // (it clears CurrentBuilding even for a half-state enterer) and then Remove from
                // the enterable set. Only reached when the normal exit path could not clear them.
                forced = ForceRemoveResidualEnterers(enterable, reason);
                remaining = CountEnterersInside(enterable);
            }

            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.PausedEjectSummary,
                "eject-summary reason=" + reason + ", requested=" + requested + ", ejected=" + ejected
                + ", skipped=" + skipped + ", forced=" + forced + ", remaining=" + remaining
                + ", entityId=" + _entityId);

            if (remaining > 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PausedEjectFailed,
                    "eject-failed reason=" + reason + ", remaining=" + remaining
                    + " enterer(s) still inside the blocked farm, entityId=" + _entityId);
            }
        }

        /// <summary>
        /// Last-resort cleanup for enterers stuck in a half-state: Abandon (clears CurrentBuilding)
        /// followed by Remove from the enterable set, mirroring the vanilla private cleanup. Each
        /// step is isolated so one broken beaver cannot stop the others.
        /// </summary>
        private int ForceRemoveResidualEnterers(Enterable enterable, string reason)
        {
            var forced = 0;
            List<Enterer>? stuck;
            try
            {
                stuck = new List<Enterer>(enterable.EnterersInside);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PausedEjectFailed,
                    "residual enterers could not be listed, reason=" + reason, exception, _entityId.ToString());
                return 0;
            }

            for (var index = 0; index < stuck.Count; index++)
            {
                var enterer = stuck[index];
                if (enterer is null)
                {
                    continue;
                }

                try
                {
                    enterer.Abandon();
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PausedEjectFailed,
                        "residual enterer could not be abandoned, reason=" + reason + ", enterer="
                        + DescribeEnterer(enterer), exception, _entityId.ToString());
                }

                try
                {
                    enterable.Remove(enterer);
                    forced++;
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.PausedEjectFailed,
                        "residual enterer could not be removed from the enterable, reason=" + reason
                        + ", enterer=" + DescribeEnterer(enterer), exception, _entityId.ToString());
                }
            }

            if (forced > 0)
            {
                _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PausedForceRemove,
                    "force-remove fallback cleared " + forced + " residual enterer(s), reason=" + reason
                    + ", entityId=" + _entityId);
            }

            return forced;
        }

        /// <summary>Logs each residual enterer once so a support log names who was still inside.</summary>
        private void LogResidualEnterers(Enterable enterable, string reason)
        {
            try
            {
                var seen = 0;
                foreach (var enterer in enterable.EnterersInside)
                {
                    if (seen++ >= 16)
                    {
                        // Bound the diagnostic burst on a heavily loaded pause.
                        break;
                    }

                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.PausedResidualEnterer,
                        "residual-enterer reason=" + reason + ", enterer=" + DescribeEnterer(enterer)
                        + ", entityId=" + _entityId);
                }
            }
            catch (Exception)
            {
                // Diagnostics only; never break the reconcile (REQ-V2-SAFE-01).
            }
        }

        private int CountEnterersInside(Enterable enterable)
        {
            try
            {
                return enterable.NumberOfEnterersInside;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string DescribeEnterer(Enterer? enterer)
        {
            if (enterer is null)
            {
                return "<null>";
            }

            try
            {
                // Beavers are entities; the entity id is the stable identifier a support log can
                // match against the crash stack. There is no exposed display name for a mod.
                var entity = enterer.GetComponent<Timberborn.EntitySystem.EntityComponent>();
                return entity is null ? "<no-entity>" : entity.EntityId.ToString();
            }
            catch (Exception)
            {
                return "<unknown>";
            }
        }
    }
}
