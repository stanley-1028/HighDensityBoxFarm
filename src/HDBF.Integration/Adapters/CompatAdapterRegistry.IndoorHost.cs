// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: The v2 indoor-host half of CompatAdapterRegistry: one adapter health snapshot and
//          one runtime-failure report, both reusing the existing ICompatAdapter state machine.
//          Kept in its own partial file so the v1 registry file stays within its budget.
// Requirements: REQ-V2-COMPAT-01, REQ-V2-SAFE-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using HDBF.Core;
using HDBF.Core.Compat;

namespace HDBF.Integration.Adapters
{
    public sealed partial class CompatAdapterRegistry
    {
        /// <summary>
        /// Health snapshot of one adapter without leaking the adapter instance to Gameplay/UI
        /// (v2 <c>IIndoorAdapterHost.PipeHealth</c>). Reuses the same
        /// <see cref="CompatStateMachine"/> that produces <see cref="ICompatRegistry.EnabledAdapters"/>
        /// and the compatibility report, so there is exactly one state source (REQ-V2-COMPAT-01).
        /// </summary>
        /// <param name="adapterId">Stable adapter id, e.g. <c>HDBF.Adapter.TimberPipes</c>.</param>
        /// <returns>The adapter health, or <see cref="HdbfAdapterHealth.Unknown"/> when unknown.</returns>
        public HdbfAdapterHealth GetAdapterHealth(string? adapterId)
        {
            if (string.IsNullOrEmpty(adapterId))
            {
                return HdbfAdapterHealth.Unknown;
            }

            try
            {
                for (var i = 0; i < _adapters.Count; i++)
                {
                    var adapter = _adapters[i];
                    if (string.Equals(adapter.Id, adapterId, StringComparison.Ordinal))
                    {
                        return adapter.Health;
                    }
                }
            }
            catch (Exception exception)
            {
                RecordError("adapter-health-failed", exception);
            }

            return HdbfAdapterHealth.Unknown;
        }

        /// <summary>
        /// Counts one runtime failure of an already-initialized adapter (v2 water source: a
        /// reflection call into the external mod failed) against the v1 three-strike rule
        /// (REQ-V2-SAFE-02 via <see cref="CompatStateMachine.Fail"/>). Three failures disable only
        /// that adapter for the session; the farm keeps running on manual refill.
        /// </summary>
        /// <param name="adapterId">Stable adapter id, e.g. <c>HDBF.Adapter.TimberPipes</c>.</param>
        /// <param name="reason">Short stable reason recorded by the state machine.</param>
        public void ReportRuntimeFailure(string? adapterId, string reason)
        {
            if (string.IsNullOrEmpty(adapterId))
            {
                return;
            }

            try
            {
                for (var i = 0; i < _adapters.Count; i++)
                {
                    var adapter = _adapters[i];
                    if (!string.Equals(adapter.Id, adapterId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (adapter is ReflectionCompatAdapter reflection)
                    {
                        reflection.NoteRuntimeFailure(reason);
                    }

                    return;
                }
            }
            catch (Exception exception)
            {
                RecordError("runtime-failure-report-failed", exception);
            }
        }
    }
}
