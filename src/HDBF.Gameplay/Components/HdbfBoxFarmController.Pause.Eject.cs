// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Beaver ejection of the indoor-farm building pause (player report 2026-10-29 "it crash
//          when you paused it"). The vanilla pause only unassigns the workplace
//          (Workplace.OnObjectBlocked -> UnassignAllWorkers -> Worker.Unemploy), and Worker.Unemploy
//          does not call Enterer.Exit/Enterable.Remove; this farm is the only one that keeps its
//          farmers inside through WaitInsideIdlyWorkplaceBehavior, so a paused farm would otherwise
//          keep abandoned enterers inside a blocked building - the state the vanilla
//          Enterer/Enterable/Accessible invariants are not written for. This partial moves them out
//          through the vanilla Enterer.Exit path, isolated per beaver.
// Requirements: REQ-V2-SAFE-01, REQ-V2-LABOR-03
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
        /// Moves every beaver out of this farm through the vanilla Enterer.Exit path (the same
        /// Abandon+Remove pair Enterable uses for its own state changes). A paused farm is blocked, so
        /// a beaver left inside cannot be expected to walk out and the vanilla enterer/accessible
        /// invariants must not be fed that state. Every enterer is isolated: one broken enterer must
        /// never block the pause (REQ-V2-SAFE-01).
        /// </summary>
        private void EjectBeaversFromPausedFarm()
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

            var ejected = 0;
            for (var index = 0; index < inside.Count; index++)
            {
                try
                {
                    var enterer = inside[index];
                    if (enterer is null)
                    {
                        continue;
                    }

                    enterer.Exit();
                    ejected++;
                }
                catch (Exception exception)
                {
                    // One broken enterer must not abort the pause or the other ejections.
                    _log.Error(HdbfLogCategory.Warn, IndoorLogKeys.Paused,
                        "a beaver inside the paused farm could not be moved out", exception,
                        _entityId.ToString());
                }
            }

            if (ejected > 0)
            {
                _log.Write(HdbfLogCategory.Info, IndoorLogKeys.Paused,
                    "moved " + ejected + " beaver(s) out of the paused farm, entityId=" + _entityId);
            }
        }
    }
}
