using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Logging;
using HDBF.Integration.Detection;
using HDBF.Integration.Reflection;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Shared behaviour of the five soft-dependency adapters (chapter 8.2, REQ-IMPL-23/24).
    ///
    /// Every public member is a try/catch boundary: detection, initialization, tick and shutdown
    /// failures are counted through <see cref="CompatStateMachine"/>, logged under
    /// <c>HDBF/Compat</c> with a per-adapter throttle key, and never escape into the game loop
    /// (REQ-COMPAT-01/07, REQ-SAFE-13). Initialization rolls its bindings back on every failure
    /// path so a half-initialized adapter can never stay active (REQ-COMPAT-08).
    ///
    /// Bindings are probe-grade: an adapter resolves verified anchor types (see
    /// <see cref="ThirdPartyProbes"/>) and, for member anchors, asks
    /// <see cref="ExternalProxy.FindMethodBySignature"/> for the exact member signature. It never
    /// invokes a third-party member, so an external signature change can only turn into a logged
    /// degradation, never into a runtime exception (R-04).
    /// </summary>
    public abstract class ReflectionCompatAdapter : ICompatAdapter
    {
        private readonly CompatStateMachine _state = new CompatStateMachine();
        private readonly ThirdPartyProbe _probe;

        private IHdbfLog? _log;
        private CompatDetector? _detector;
        private Type[] _boundTypes = Array.Empty<Type>();

        internal ReflectionCompatAdapter(ThirdPartyProbe probe)
        {
            _probe = probe;
        }

        /// <inheritdoc />
        public string Id => _probe.AdapterId;

        /// <inheritdoc />
        public string DisplayNameKey => _probe.DisplayNameKey;

        /// <inheritdoc />
        public HdbfAdapterHealth Health => _state.Health;

        /// <inheritdoc />
        public int FailureCount => _state.FailureCount;

        /// <summary>Log sink; lazily created so adapters can be constructed standalone (tests).</summary>
        private IHdbfLog Log
        {
            get
            {
                if (_log is null)
                {
                    _log = TestHooks.LogOverride ?? IntegrationLog.Create();
                }

                return _log;
            }
        }

        /// <summary>Detector shared with the registry; lazily created for standalone construction.</summary>
        private CompatDetector Detector
        {
            get
            {
                if (_detector is null)
                {
                    _detector = new CompatDetector(Log, TestHooks.ModListProviderOverride,
                        TestHooks.DetectionOverride);
                }

                return _detector;
            }
        }

        /// <summary>
        /// Hands the registry's shared detector and log to this adapter so the session cache and
        /// the throttle table are shared instead of per-adapter (REQ-COMPAT-06).
        /// </summary>
        internal void AttachContext(IHdbfLog log, CompatDetector detector)
        {
            _log = log;
            _detector = detector;
        }

        /// <inheritdoc />
        public bool Detect()
        {
            try
            {
                if (_state.Health == HdbfAdapterHealth.Failed)
                {
                    // REQ-COMPAT-07: no retries for the rest of the session.
                    return false;
                }

                if (!Detector.TryDetect(Id, out var result) || !result.Detected)
                {
                    // Missing mods are a neutral state, never an error (REQ-COMPAT-09/12).
                    _state.MarkMissing(result.FailureReason ?? "not-installed");
                    LogMissing(result.FailureReason);
                    return false;
                }

                _state.Detect();
                return true;
            }
            catch (Exception exception)
            {
                RecordFailure("detect-exception", exception);
                return false;
            }
        }

        /// <inheritdoc />
        public bool Initialize()
        {
            try
            {
                if (_state.Health == HdbfAdapterHealth.Failed)
                {
                    return false;
                }

                if (!_state.IsDetected && !Detect())
                {
                    return false;
                }

                ReleaseBindings();

                var bound = BindCapability(out var signatureMismatch);
                if (_probe.RequiresApiBinding && bound.Count == 0)
                {
                    // The mod exists but the expected API surface was not found. Distinguish "type
                    // missing" from "type present, member signature changed" so a game log says
                    // which external update broke the adapter (chapter 8.2, R-04).
                    RecordFailure(signatureMismatch ? "api-anchor-signature-changed" : "api-anchor-not-found", null);
                    return false;
                }

                _boundTypes = bound.ToArray();
                if (!_state.InitializeSucceeded())
                {
                    ReleaseBindings();
                    return false;
                }

                LogInfo(bound.Count);
                return true;
            }
            catch (Exception exception)
            {
                // REQ-COMPAT-08: roll back before reporting the failure.
                ReleaseBindings();
                RecordFailure("init-exception", exception);
                return false;
            }
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            try
            {
                ReleaseBindings();
                _state.Shutdown();
            }
            catch (Exception exception)
            {
                // Idempotent teardown must never throw (REQ-COMPAT-08).
                LogError("shutdown-exception", exception);
            }
        }

        /// <inheritdoc />
        public void Tick(float gameHours)
        {
            try
            {
                if (!_state.IsActive)
                {
                    return;
                }

                var bound = _boundTypes;
                if (bound.Length == 0)
                {
                    // Read-only adapters (MoreMines, HoomanStairs) have nothing to keep alive.
                    return;
                }

                for (var i = 0; i < bound.Length; i++)
                {
                    var type = bound[i];
                    if (ExternalProxy.FindType(type.Assembly.GetName().Name, type.FullName) is null)
                    {
                        // REQ-COMPAT-07: an external API that disappeared degrades the adapter.
                        RecordFailure("api-anchor-lost", null);
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                RecordFailure("tick-exception", exception);
            }
        }

        /// <summary>
        /// Resolves the adapter's anchors against the loaded assemblies. An anchor counts only when
        /// its type resolves *and* (for member anchors) the cached signature lookup succeeds, so a
        /// renamed member cannot leave an A-level port advertised without a bound API
        /// (chapter 8.3, R-04). Returns at least one <see cref="Type"/> when the external API is
        /// present and compatible.
        /// </summary>
        /// <param name="signatureMismatch">
        /// True when at least one anchor type resolved but its member signature did not, so the
        /// caller can report <c>api-anchor-signature-changed</c> instead of <c>-not-found</c>.
        /// </param>
        private List<Type> BindCapability(out bool signatureMismatch)
        {
            signatureMismatch = false;
            var anchors = _probe.ApiAnchors;
            var bound = new List<Type>(anchors.Length);
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                Type? type = null;
                try
                {
                    type = ExternalProxy.FindType(anchor.AssemblyName, anchor.TypeName) ??
                           ExternalProxy.FindType(null, anchor.TypeName) ??
                           ExternalProxy.FindTypeBySimpleName(SimpleName(anchor.TypeName));
                }
                catch (Exception)
                {
                    type = null;
                }

                if (type is null)
                {
                    continue;
                }

                if (!anchor.IsTypeOnly && !SignatureMatches(type, anchor))
                {
                    signatureMismatch = true;
                    continue;
                }

                if (!bound.Contains(type))
                {
                    bound.Add(type);
                }
            }

            return bound;
        }

        private static bool SignatureMatches(Type type, ApiAnchor anchor)
        {
            try
            {
                return ExternalProxy.FindMethodBySignature(type, anchor.MemberName, anchor.ParameterTypeNames,
                    anchor.ReturnTypeName) is not null;
            }
            catch (Exception)
            {
                // A reflection failure is a signature miss, never an exception into the game loop.
                return false;
            }
        }

        private void ReleaseBindings()
        {
            _boundTypes = Array.Empty<Type>();
        }

        private void RecordFailure(string reason, Exception? exception)
        {
            _state.Fail(reason);
            if (exception is null)
            {
                LogError(reason, null);
            }
            else
            {
                LogError(reason, exception);
            }
        }

        private void LogMissing(string? reason)
        {
            try
            {
                Log.Write(HdbfLogCategory.Compat, MissingKey,
                    Id + " not detected (" + (reason ?? "unknown") + "); capability unavailable");
            }
            catch (Exception)
            {
                // REQ-SAFE-01: diagnostics never break the adapter.
            }
        }

        private void LogInfo(int boundTypeCount)
        {
            try
            {
                // Emitted under HDBF/Compat so the in-game compatibility matrix can read a single
                // category for enabled/missing/failed (tools/dev/CompatMatrix.ps1 -CheckLog).
                Log.Write(HdbfLogCategory.Compat, EnabledKey,
                    Id + " active; api-anchors=" + boundTypeCount);
            }
            catch (Exception)
            {
                // The log sink itself failed; there is nowhere left to report it (REQ-SAFE-01).
            }
        }

        private void LogError(string reason, Exception? exception)
        {
            try
            {
                if (exception is null)
                {
                    Log.Write(HdbfLogCategory.Compat, FailedKey, Id + " " + reason);
                }
                else
                {
                    Log.Error(HdbfLogCategory.Compat, FailedKey, Id + " " + reason, exception, Id);
                }
            }
            catch (Exception)
            {
                // The log sink itself failed; the state machine still counts the failure.
            }
        }

        /// <summary>
        /// Per-adapter throttle identity: chapter 10.4 throttles by key, so a suffix is required or
        /// one missing mod would hide the other four diagnostics for 30 s (REQ-SAFE-14).
        /// </summary>
        private string MissingKey => HdbfLogKeys.CompatMissing + ":" + Id;

        private string FailedKey => HdbfLogKeys.CompatFailed + ":" + Id;

        private string EnabledKey => HdbfLogKeys.CompatEnabled + ":" + Id;

        private static string SimpleName(string fullName)
        {
            var dot = fullName.LastIndexOf('.');
            return dot >= 0 && dot < fullName.Length - 1 ? fullName.Substring(dot + 1) : fullName;
        }
    }
}
