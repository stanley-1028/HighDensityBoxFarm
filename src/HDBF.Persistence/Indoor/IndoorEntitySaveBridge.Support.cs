// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-GAMEPLAY-01
// Purpose: Diagnostic/logging helpers of the entity save bridge plus the L-04 pre-repair
//          rollback copy. Split from the bridge payload path to keep both files small.
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

        /// <summary>
        /// Bounded "|"-joined repair notes of one pass (L-04: <c>save.write-repaired</c> must say what
        /// the write actually changed, not only the counters). The list is already bounded by
        /// <see cref="IndoorSaveLimits.MaxRepairNotes"/> and every note by
        /// <see cref="IndoorSaveLimits.MaxDiagnosticLength"/>, so the line stays bounded too.
        /// </summary>
        private static string DescribeNotes(IndoorRepairResult? result)
        {
            if (result is null || result.Notes.Count == 0)
            {
                return "none";
            }

            return string.Join("|", result.Notes);
        }

        /// <summary>
        /// L-04 rollback copy of a repaired write: encodes the pre-repair state so the values a repair
        /// had to clear/clamp are not lost silently. This is deliberately NOT a "last good snapshot":
        /// the game's <c>IEntitySaver</c> is write-only, so the previous payload cannot be read back,
        /// and the pre-repair text may not be loadable. A null result (encoder refuses or throws) means
        /// "write no <c>PreviousPayload</c> and log no previous-snapshot line".
        /// </summary>
        private static string? TryEncodeRollbackCopy(IndoorFarmRecord source)
        {
            try
            {
                var copy = Clone(source);
                var text = IndoorSaveCodec.Encode(copy);
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception)
            {
                // Diagnostics/rollback must never break the write (REQ-V2-SAFE-01).
                return null;
            }
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
