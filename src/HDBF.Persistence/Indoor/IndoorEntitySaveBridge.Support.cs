// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-GAMEPLAY-01
// Purpose: Diagnostic/logging helpers of the entity save bridge. Split from the bridge
//          payload path to keep both files small and readable.
// Requirements: REQ-V2-READ-03, REQ-V2-SAFE-01, REQ-V2-MAINT-02
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Persistence.Indoor
{
    public static partial class IndoorEntitySaveBridge
    {
        private static string Describe(IndoorRepairResult? result)
        {
            return result is null ? "no-result" : result.ToString();
        }

        private static int SlotCount(IndoorFarmRecord record)
        {
            return record.Slots is null ? 0 : record.Slots.Length;
        }

        private static void Write(IHdbfLog? log, string key, string message)
        {
            if (log is null)
            {
                return;
            }

            try
            {
                log.Write(HdbfLogCategory.Info, key, message);
            }
            catch (Exception)
            {
                // Diagnostics must never break a save (REQ-V2-SAFE-01).
            }
        }

        private static void Warn(IHdbfLog? log, string key, string message, Exception? exception)
        {
            if (log is null)
            {
                return;
            }

            try
            {
                if (exception is null)
                {
                    log.Write(HdbfLogCategory.Warn, key, message);
                }
                else
                {
                    log.Error(HdbfLogCategory.Warn, key, message, exception);
                }
            }
            catch (Exception)
            {
                // Diagnostics must never break a save/load (REQ-V2-SAFE-01).
            }
        }
    }
}
