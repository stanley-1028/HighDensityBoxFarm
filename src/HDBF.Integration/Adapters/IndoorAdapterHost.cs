// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: The v2 IIndoorAdapterHost implementation (INTERFACES section 6): exposes the
//          Timber Pipes water source plus pipe/logistics port health to Gameplay/UI while
//          reusing the existing CompatAdapterRegistry state machine. No game type leaves Core.
// Requirements: REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-COMPAT-03, REQ-V2-SAFE-05
// ============================================================================
using System;
using Bindito.Core;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Session-scoped adaptor host of the v2 indoor farms (INTERFACES section 6). It is the single
    /// place Gameplay/UI may ask for a pipe water source or a port state, so no simulation code
    /// ever sees a Timber Pipes type (REQ-V2-COMPAT-01).
    ///
    /// Reuse instead of a second state machine:
    /// * health comes from the existing <see cref="CompatStateMachine"/> of the Timber Pipes
    ///   adapter through <see cref="CompatAdapterRegistry.GetAdapterHealth"/>;
    /// * runtime failures of the water source are counted by the same three-strike machine through
    ///   <see cref="CompatAdapterRegistry.ReportRuntimeFailure"/>;
    /// * port availability is <see cref="ICompatRegistry.IsPortAvailable"/>, i.e. the port state
    ///   machine of chapter 8.3 that the compatibility report already exports.
    ///
    /// A missing/broken mod never throws here: every member answers <c>null</c>/<c>false</c>/
    /// <see cref="HdbfAdapterHealth.Unknown"/> and Gameplay degrades to manual refill and vanilla
    /// output storage (REQ-V2-COMPAT-02/03).
    /// </summary>
    public sealed class IndoorAdapterHost : IIndoorAdapterHost
    {
        private readonly CompatAdapterRegistry? _compat;
        private readonly IContainer? _container;
        private readonly IHdbfLog _log;

        private TimberPipesWaterSource? _pipeWaterSource;

        /// <summary>
        /// Bindito constructor: the compatibility registry (same session singleton Gameplay already
        /// consumes) and the game container, which is only used to look up the external mod's own
        /// registry instance at draw time. Both are nullable-guarded on purpose: a broken optional
        /// dependency must degrade, never fail container creation (REQ-V2-COMPAT-02).
        /// </summary>
        public IndoorAdapterHost(CompatAdapterRegistry compat, IContainer container)
        {
            _compat = compat;
            _container = container;
            _log = TestHooks.LogOverride ?? IntegrationLog.Create();
        }

        /// <inheritdoc />
        public HdbfAdapterHealth PipeHealth
        {
            get
            {
                try
                {
                    return _compat is null
                        ? HdbfAdapterHealth.Unknown
                        : _compat.GetAdapterHealth(CompatAnchorTable.TimberPipesId);
                }
                catch (Exception exception)
                {
                    LogError("pipe-health-query-failed", exception);
                    return HdbfAdapterHealth.Unknown;
                }
            }
        }

        /// <inheritdoc />
        public IWaterSource? PipeWaterSource
        {
            get
            {
                try
                {
                    // REQ-V2-COMPAT-02: Disabled/Failed/Unknown = missing mod or the three-strike
                    // rule tripped; Gameplay must fall back to manual refill.
                    var health = PipeHealth;
                    if (health != HdbfAdapterHealth.Ok && health != HdbfAdapterHealth.Degraded)
                    {
                        return null;
                    }

                    if (_pipeWaterSource is null)
                    {
                        _pipeWaterSource = new TimberPipesWaterSource(_log, _container, () => PipeHealth,
                            reason => ReportRuntimeFailure(reason));
                    }

                    return _pipeWaterSource;
                }
                catch (Exception exception)
                {
                    LogError("pipe-water-source-query-failed", exception);
                    return null;
                }
            }
        }

        /// <inheritdoc />
        public bool IsPipeConnected
        {
            get
            {
                try
                {
                    return PipeWaterSource is TimberPipesWaterSource pipes && pipes.HasNetwork;
                }
                catch (Exception exception)
                {
                    LogError("pipe-connected-query-failed", exception);
                    return false;
                }
            }
        }

        /// <inheritdoc />
        public bool IsLogisticsConnected
        {
            get
            {
                try
                {
                    return _compat is not null && _compat.IsPortAvailable(HdbfPortKind.Logistics);
                }
                catch (Exception exception)
                {
                    LogError("logistics-connected-query-failed", exception);
                    return false;
                }
            }
        }

        private void ReportRuntimeFailure(string reason)
        {
            try
            {
                _compat?.ReportRuntimeFailure(CompatAnchorTable.TimberPipesId, reason);
            }
            catch (Exception exception)
            {
                LogError("runtime-failure-report-failed", exception);
            }
        }

        private void LogError(string keySuffix, Exception exception)
        {
            try
            {
                _log.Error(HdbfLogCategory.Compat, HdbfLogKeys.CompatFailed + ":IndoorAdapterHost:" + keySuffix,
                    "indoor adaptor host degraded", exception, keySuffix);
            }
            catch (Exception)
            {
                // Diagnostics must never break a host query (REQ-V2-SAFE-01).
            }
        }
    }
}
