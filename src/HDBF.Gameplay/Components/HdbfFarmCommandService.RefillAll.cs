// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Settlement-wide manual refill (WP-08) of the write-side command service: one click
//          asks every live indoor farm for its own refill trip. Split from
//          HdbfFarmCommandService.cs so both files stay inside the 300-line gate
//          (REQ-V2-MAINT-02); it is the same singleton class.
// Requirements: REQ-V2-UI-02, REQ-V2-WATER-04, REQ-V2-SAFE-01, REQ-V2-MAINT-02
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    public sealed partial class HdbfFarmCommandService
    {
        /// <inheritdoc />
        public bool TryRefillAll(out string? failureKey, out int refilled)
        {
            failureKey = null;
            refilled = 0;
            IReadOnlyList<Guid> ids;
            try
            {
                ids = _farms.LiveFarmIds();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyCommand,
                    "indoor refill-all could not enumerate the live farms", exception);
                failureKey = HdbfCommandFailures.Unknown;
                return false;
            }

            if (ids.Count == 0)
            {
                failureKey = HdbfCommandFailures.NoFarms;
                return false;
            }

            for (var index = 0; index < ids.Count; index++)
            {
                // WP-08: every farm keeps its own all-or-nothing rules; one rejected farm (no
                // worker, no source, empty action budget) must not stop the others.
                if (!TryGetController(ids[index], out var farm))
                {
                    continue;
                }

                try
                {
                    if (farm.TryRequestManualRefill(out _))
                    {
                        refilled++;
                    }
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn, LogKeyCommand,
                        "indoor refill-all skipped one farm after a fault", exception, ids[index].ToString());
                }
            }

            _log.Write(HdbfLogCategory.Info, IndoorLogKeys.RefillAll,
                "indoor refill-all finished, farms=" + ids.Count + ", refilled=" + refilled);
            return refilled > 0;
        }
    }
}
