// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-SAVE-01
// Purpose: Recovery and diagnostics helpers of the keyed bridge: identity/load-day finalisation,
//          the total warning callback and the IHdbfLog adapter for the fallback read paths.
// Requirements: REQ-V2-SAVE-02, REQ-V2-SAVE-05, REQ-V2-SAVE-06, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Persistence.Indoor
{
    /// <summary>
    /// Recovery/identity/diagnostic helpers shared by the keyed save and load paths. All of them
    /// are total functions: a broken callback, log sink or date degrades to the documented safe
    /// value instead of throwing into the game save/load callback (REQ-V2-SAFE-01).
    /// </summary>
    public static partial class IndoorEntitySaveBridge
    {
        /// <summary>Persisted entity id text (the game serializer has no Int64; API_FACTS section 6).</summary>
        private static string FormatEntityId(long entityId)
        {
            return entityId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Applies the blueprint identity and the load-day maintenance rule to a loaded record:
        /// one cycle from <paramref name="today"/> only when the persisted date is missing/damaged,
        /// so a reload can never restart or stall the maintenance countdown (REQ-V2-SAVE-02/06).
        /// </summary>
        private static void FinalizeLoadedRecord(IndoorFarmRecord record, string identity, TierProfile tier,
            HdbfFaction faction, float today)
        {
            record.EntityId = identity;
            record.Tier = tier.Tier;
            record.Faction = faction;
            record.SchemaVersion = IndoorFarmRecord.CurrentSchemaVersion;
            record.MaintenanceDueDay = MaintenanceDefaults.ResolveDueDay(tier, today, record.MaintenanceDueDay);
        }

        /// <summary>Invokes the caller's warning sink with a <c>key: message</c> line; never throws.</summary>
        private static void Notify(Action<string>? warn, string key, string message)
        {
            if (warn is null)
            {
                return;
            }

            try
            {
                warn(key + ": " + message);
            }
            catch (Exception)
            {
                // Diagnostics must never break a save/load (REQ-V2-SAFE-01).
            }
        }

        /// <summary>Writes one entry through an optional throttled log; a broken sink is swallowed.</summary>
        private static void Log(IHdbfLog? log, HdbfLogCategory category, string key, string message)
        {
            try
            {
                log?.Write(category, key, message);
            }
            catch (Exception)
            {
                // Diagnostics must never break a save/load (REQ-V2-SAFE-01).
            }
        }

        /// <summary>
        /// Adapts an <see cref="Action{T}"/> warning callback to <see cref="IHdbfLog"/> so the
        /// untouched payload/v1 read path logs through the same sink as the keyed path. The sink
        /// owns throttling (a <see cref="ThrottledLog"/>-backed callback is the intended receiver),
        /// the adapter keeps no state (REQ-V2-MAINT-04).
        /// </summary>
        private sealed class WarnLog : IHdbfLog
        {
            private readonly Action<string> _warn;

            internal WarnLog(Action<string> warn)
            {
                _warn = warn;
            }

            public void Write(HdbfLogCategory category, string key, string message)
            {
                Emit(key, message);
            }

            public void Error(HdbfLogCategory category, string key, string message, Exception? exception,
                string? entityId = null)
            {
                Emit(key, message
                    + (exception is null ? string.Empty : "; exception=" + exception.GetType().Name)
                    + (string.IsNullOrEmpty(entityId) ? string.Empty : "; entityId=" + entityId));
            }

            public int SuppressedCount(string key)
            {
                return 0;
            }

            public void Reset()
            {
                // The sink owns its throttle state; the adapter has none.
            }

            private void Emit(string key, string message)
            {
                try
                {
                    _warn(key + ": " + message);
                }
                catch (Exception)
                {
                    // Diagnostics must never break a save/load (REQ-V2-SAFE-01).
                }
            }
        }
    }
}
