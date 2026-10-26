using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Logging;
using HDBF.Integration.Detection;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// The one place that knows about the five optional mods (chapter 8.2, REQ-COMPAT-01/10).
    ///
    /// Responsibilities:
    /// * register the five adapters in the fixed order of table 8-1 and isolate every failure;
    /// * answer the only two questions gameplay/UI ask — "is this adapter active?" and "is this
    ///   port available?" — so mod detection never leaks into the simulation (chapter 8.2);
    /// * rebuild the session view on demand and export the chapter 10.4 compatibility report
    ///   (REQ-SAFE-16).
    ///
    /// Failure policy: detection/initialization is per-adapter try/catch, missing mods become
    /// <see cref="HdbfAdapterHealth.Disabled"/> without counting a failure, and three failures in
    /// one session move an adapter to <see cref="HdbfAdapterHealth.Failed"/> with no further
    /// retries (REQ-COMPAT-07). Nothing in this class throws out of a public member.
    /// </summary>
    public sealed partial class CompatAdapterRegistry : ICompatRegistry
    {
        /// <summary>HDBF mod version written to the compatibility report (chapter 9.4).</summary>
        public const string ModVersion = "0.1.0";

        /// <summary>Verified game version of this build (API_FACTS.md header).</summary>
        public const string GameVersion = "1.1.2.8";

        /// <summary>Save schema version known to this build (REQ-V2-SAVE-01).</summary>
        public const int SchemaVersion = 2;

        /// <summary>Config schema version known to this build (REQ-V2-EXT-04).</summary>
        public const int ConfigSchemaVersion = 2;

        private readonly IHdbfLog _log;
        private readonly CompatDetector _detector;
        private readonly List<ICompatAdapter> _adapters = new List<ICompatAdapter>(5);
        private readonly List<string> _registeredIds = new List<string>(5);

        private IReadOnlyList<string> _enabled = Array.Empty<string>();
        private IReadOnlyList<string> _failed = Array.Empty<string>();
        private string? _lastError;

        /// <summary>
        /// Creates the registry and registers the five built-in adapters. Bindito resolves this
        /// constructor for the <c>ICompatRegistry</c> singleton binding; it deliberately has no
        /// constructor dependencies (and is the only constructor) because Bindito rejects a type
        /// that has both a parameterless and a parameterful constructor (verified in
        /// <c>ConstructorRetriever</c>/<c>InstanceCreator</c> IL), and a broken integration layer
        /// must never fail container creation (REQ-COMPAT-01).
        /// </summary>
        public CompatAdapterRegistry()
        {
            _log = TestHooks.LogOverride ?? IntegrationLog.Create();
            _detector = new CompatDetector(_log, TestHooks.ModListProviderOverride, TestHooks.DetectionOverride);
            RegisterBuiltIns();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> EnabledAdapters => _enabled;

        /// <inheritdoc />
        public IReadOnlyList<string> FailedAdapters => _failed;

        /// <inheritdoc />
        public IReadOnlyList<string> DetectedMods => _detector.DetectedMods;

        /// <inheritdoc />
        public bool IsAdapterActive(string? adapterId)
        {
            if (string.IsNullOrEmpty(adapterId))
            {
                return false;
            }

            try
            {
                for (var i = 0; i < _adapters.Count; i++)
                {
                    var adapter = _adapters[i];
                    if (string.Equals(adapter.Id, adapterId, StringComparison.Ordinal))
                    {
                        return adapter.Health == HdbfAdapterHealth.Ok;
                    }
                }
            }
            catch (Exception exception)
            {
                RecordError("is-active-failed", exception);
            }

            return false;
        }

        /// <inheritdoc />
        public bool IsPortAvailable(HdbfPortKind port)
        {
            try
            {
                for (var i = 0; i < _adapters.Count; i++)
                {
                    var adapter = _adapters[i];
                    if (adapter.Health == HdbfAdapterHealth.Ok && ThirdPartyProbes.PortFor(adapter.Id) == port)
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                RecordError("port-query-failed", exception);
            }

            return false;
        }

        /// <inheritdoc />
        public void Register(ICompatAdapter? adapter)
        {
            if (adapter is null)
            {
                return;
            }

            try
            {
                var id = adapter.Id;
                if (string.IsNullOrEmpty(id) || _registeredIds.Contains(id!))
                {
                    return;
                }

                _registeredIds.Add(id!);
                _adapters.Add(adapter);

                try
                {
                    var detected = adapter.Detect();
                    if (!detected)
                    {
                        // Missing mod: neutral state, diagnostics already emitted by the adapter
                        // through the throttled HDBF/Compat key (REQ-COMPAT-09).
                        RebuildLists();
                        return;
                    }

                    if (adapter.Health == HdbfAdapterHealth.Failed)
                    {
                        _lastError = "adapter-failed:" + id;
                        RebuildLists();
                        return;
                    }

                    if (!adapter.Initialize())
                    {
                        _lastError = "init-failed:" + id + " (failures=" + adapter.FailureCount + ")";
                    }
                }
                catch (Exception exception)
                {
                    // Last line of defence for a third-party adapter implementation that throws
                    // despite the contract: isolate it and keep the rest of the registry running.
                    _lastError = "adapter-threw:" + id;
                    SafeLogError(id!, "adapter threw during detect/initialize", exception);
                }

                RebuildLists();
            }
            catch (Exception exception)
            {
                RecordError("register-failed", exception);
            }
        }

        /// <inheritdoc />
        public void RefreshDetection()
        {
            try
            {
                // Manual "re-detect" (chapter 8.2): clear the session cache, re-probe everything
                // that is not already active, but never force-unload a working adapter.
                _detector.Reset();

                for (var i = 0; i < _adapters.Count; i++)
                {
                    var adapter = _adapters[i];
                    try
                    {
                        if (adapter.Health == HdbfAdapterHealth.Ok || adapter.Health == HdbfAdapterHealth.Failed)
                        {
                            continue;
                        }

                        if (!adapter.Detect())
                        {
                            continue;
                        }

                        if (!adapter.Initialize())
                        {
                            _lastError = "refresh-init-failed:" + adapter.Id;
                        }
                    }
                    catch (Exception exception)
                    {
                        _lastError = "refresh-threw:" + adapter.Id;
                        SafeLogError(adapter.Id, "adapter threw during refresh", exception);
                    }
                }
            }
            catch (Exception exception)
            {
                RecordError("refresh-failed", exception);
            }
            finally
            {
                RebuildLists();
            }
        }


        private void RegisterBuiltIns()
        {
            try
            {
                // Fixed order of table 8-1; each registration is isolated (REQ-COMPAT-01).
                Register(Attach(new TimberPipesAdapter()));
                Register(Attach(new PowerLinesAdapter()));
                Register(Attach(new ConveyorBeltsV2Adapter()));
                Register(Attach(new MoreMinesAdapter()));
                Register(Attach(new HoomanStairsAdapter()));
            }
            catch (Exception exception)
            {
                RecordError("builtin-registration-failed", exception);
            }
        }

        private ICompatAdapter Attach(ReflectionCompatAdapter adapter)
        {
            adapter.AttachContext(_log, _detector);
            return adapter;
        }

        private void RebuildLists()
        {
            var enabled = new List<string>(_adapters.Count);
            var failed = new List<string>(_adapters.Count);
            for (var i = 0; i < _adapters.Count; i++)
            {
                var adapter = _adapters[i];
                switch (adapter.Health)
                {
                    case HdbfAdapterHealth.Ok:
                        enabled.Add(adapter.Id);
                        break;
                    case HdbfAdapterHealth.Failed:
                        failed.Add(adapter.Id);
                        break;
                }
            }

            _enabled = enabled;
            _failed = failed;
        }

        private void RecordError(string reason, Exception exception)
        {
            _lastError = reason + ":" + exception.GetType().Name;
            SafeLogError(reason, reason, exception);
        }

        private void SafeLogError(string keySuffix, string message, Exception exception)
        {
            try
            {
                _log.Error(HdbfLogCategory.Compat, HdbfLogKeys.CompatFailed + ":" + keySuffix, message, exception,
                    keySuffix);
            }
            catch (Exception)
            {
                // Diagnostics must never break a registry call (REQ-SAFE-01).
            }
        }
    }
}