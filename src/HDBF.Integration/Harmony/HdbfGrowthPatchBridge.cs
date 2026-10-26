using System;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Growth;
using HDBF.Core.Logging;
using HDBF.Patches;
using Timberborn.BlockSystem;
using Timberborn.Growing;
using Timberborn.TimeSystem;
using UnityEngine;

namespace HDBF.Integration.Harmony
{
    /// <summary>
    /// Session bridge between the single approved Harmony Postfix (ADR-009) and the gameplay
    /// growth-rate provider. It is a <see cref="MonoBehaviour"/> component created once by
    /// <see cref="HarmonyBootstrap"/>, marked <c>DontDestroyOnLoad</c> and found by the patch with
    /// <c>FindAnyObjectByType</c>.
    ///
    /// Why a component instead of a static field: chapter 8.4 forbids patches from holding static
    /// mutable state ("数据一律从组件或服务获取"). Every piece of session state this feature needs
    /// lives here, on the bridge instance: the provider reference, the log, the failure budget, the
    /// cached reflection handles and the auto-unload callback. The patch class itself is stateless.
    ///
    /// Responsibilities:
    /// * map the patched crop to its managed plot (via <see cref="IGrowthMultiplierSource"/>) and
    ///   stretch the vanilla timer once, when a crop starts growing;
    /// * keep every operation inside try/catch, count failures through
    ///   <see cref="PatchFailureStateMachine"/> and request an uninstall after three failures
    ///   (REQ-COMPAT-18);
    /// * do nothing for vanilla farmland, other mods' crops and already-growing crops: in those
    ///   cases the query returns false / a 1.0 rate and vanilla behaviour is preserved.
    ///
    /// The only reflection left at runtime is reading/writing the two private
    /// <c>TimeTrigger</c> fields; the <see cref="FieldInfo"/> handles are resolved once by the
    /// installer and cached on this component (no per-call reflection, no static state).
    /// </summary>
    public sealed class HdbfGrowthPatchBridge : MonoBehaviour
    {
        /// <summary>
        /// Progress below which a crop counts as "just started". A crop resumed mid-cycle (dying
        /// recovery, or a save load where progress was applied before InitializeEntity) is left
        /// alone: rescaling it would compound the slowdown and corrupt the remaining time.
        /// </summary>
        private const float StartProgressEpsilon = 0.001f;

        private IGrowthMultiplierSource? _source;
        private IHdbfLog? _log;
        private PatchFailureStateMachine? _failures;
        private Action? _autoUnload;
        private FieldInfo? _fullDelayField;
        private FieldInfo? _delayLeftField;
        private string? _targetMethod;
        private object? _harmonyInstance;
        private Type? _harmonyType;
        private bool _quarantined;
        private int _applied;

        /// <summary>Crops whose timer was stretched since the patch was installed (diagnostics).</summary>
        public int AppliedCount => _applied;

        /// <summary>True after the failure budget was exhausted or the patch was shut down.</summary>
        public bool IsQuarantined => _quarantined;

        /// <summary>
        /// Returns the one Harmony instance this process uses (REQ-COMPAT-16: a single instance, ID
        /// <c>hdbf.highdensityboxfarm</c>). The instance lives on the DontDestroyOnLoad bridge, so a
        /// second game session reuses it instead of constructing a second Harmony object.
        /// </summary>
        public bool TryGetHarmony(out object? harmonyInstance, out Type? harmonyType)
        {
            harmonyInstance = _harmonyInstance;
            harmonyType = _harmonyType;
            return _harmonyInstance is not null && _harmonyType is not null;
        }

        /// <summary>Stores the process-wide Harmony instance created by the installer.</summary>
        public void SetHarmony(object harmonyInstance, Type harmonyType)
        {
            _harmonyInstance = harmonyInstance;
            _harmonyType = harmonyType;
        }

        /// <summary>
        /// Binds this bridge for one game session. Called by the installer right before patching;
        /// re-callable after a session unload/reload. Never throws.
        /// </summary>
        public void Initialize(IGrowthMultiplierSource source, IHdbfLog log, string targetMethod,
            Action autoUnload, FieldInfo? fullDelayField, FieldInfo? delayLeftField)
        {
            _source = source;
            _log = log;
            _targetMethod = targetMethod;
            _autoUnload = autoUnload;
            _failures = new PatchFailureStateMachine(HdbfPatchCatalog.GrowthPatchId, targetMethod);
            _fullDelayField = fullDelayField;
            _delayLeftField = delayLeftField;
            _quarantined = false;
            _applied = 0;
        }

        /// <summary>Detaches the provider and quarantines the bridge; called by the installer on unload.</summary>
        public void Shutdown()
        {
            _quarantined = true;
            _source = null;
            _autoUnload = null;
            _failures = null;
        }

        /// <summary>
        /// Applies the constrained growth rate to one just-started crop, or leaves it vanilla.
        /// This is the only method the patch calls; it is fully isolated (never throws).
        /// </summary>
        public void Apply(Growable growable, ITimeTrigger timeTrigger)
        {
            if (_quarantined || _source is null || _log is null)
            {
                return;
            }

            try
            {
                if (!_source.IsPatchEnabled)
                {
                    return;
                }

                // Only a fresh growth cycle is rescaled; see StartProgressEpsilon. A crop resumed
                // mid-cycle keeps its already-stretched remaining time.
                if (growable is null || growable.IsGrown || growable.GrowthProgress > StartProgressEpsilon)
                {
                    return;
                }

                var blockObject = growable.GetComponent<BlockObject>();
                if (blockObject is null)
                {
                    return;
                }

                var cell = blockObject.Coordinates;
                if (!_source.TryGetGrowthRate(cell.x, cell.y, cell.z, out var growthRate))
                {
                    // Not a HDBF-managed plot: vanilla farmland and other mods' crops keep vanilla
                    // growth time (ADR-009 constraint: the patch must not treat vanilla farmland).
                    return;
                }

                if (growthRate >= 0.999f)
                {
                    return;
                }

                if (timeTrigger is null || _fullDelayField is null || _delayLeftField is null)
                {
                    return;
                }

                var fullDelay = _fullDelayField.GetValue(timeTrigger);
                if (fullDelay is not float originalDays || originalDays <= 0f)
                {
                    return;
                }

                // A timer that no longer carries the blueprint's growth time was already rescaled
                // (e.g. ResumeGrowing fired twice before any day elapsed). Scaling it again would
                // square the slowdown, so only an untouched timer is rescaled.
                var specDays = growable.GrowthTimeInDays;
                if (specDays <= 0f || Math.Abs(originalDays - specDays) > 1e-4f)
                {
                    return;
                }

                // Growth rate r means the crop needs 1/r times the vanilla time. Both the duration
                // and the remaining delay are rewritten so that TimeTrigger.Progress (and therefore
                // Growable.GrowthProgress, save data and the UI) report *effective* growth progress:
                // saving/loading a stretched crop stays consistent instead of compounding.
                var scaledDays = originalDays / growthRate;
                timeTrigger.Pause();
                try
                {
                    _fullDelayField.SetValue(timeTrigger, scaledDays);
                    _delayLeftField.SetValue(timeTrigger, scaledDays);
                }
                catch (Exception)
                {
                    // Restore the vanilla timer before rethrowing so the crop never freezes.
                    _fullDelayField.SetValue(timeTrigger, originalDays);
                    _delayLeftField.SetValue(timeTrigger, originalDays);
                    timeTrigger.Resume();
                    throw;
                }

                timeTrigger.Resume();
                _applied++;

#if HDBF_DEV_DIAGNOSTICS
                // TEMPORARY ADR-009 in-game evidence, dev-diagnostics builds only (REL-01 / G-08):
                // the first few rescaled crops prove that a real managed plot got a real rate < 1
                // and a real stretched timer, not just that the Postfix could be installed. The
                // release build compiles this block (and the log key) out entirely.
                if (_applied <= 12)
                {
                    _log?.Write(HdbfLogCategory.Info, HdbfLogKeys.HarmonyPatchApplied + "." + _applied,
                        HdbfPatchCatalog.GrowthPatchId + " rescaled managed crop: cell=(" + cell.x + ","
                        + cell.y + "," + cell.z + ") growthRate="
                        + growthRate.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                        + " vanillaDays="
                        + specDays.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                        + " scaledDays="
                        + scaledDays.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                }
#endif
            }
            catch (Exception exception)
            {
                // The patch never surfaces an exception to the game loop: count it, log it and keep
                // vanilla growth for this crop (REQ-COMPAT-18).
                ReportFailure(exception);
            }
        }

        private void ReportFailure(Exception exception)
        {
            try
            {
                _log?.Error(HdbfLogCategory.Error, HdbfLogKeys.HarmonyPatchFailed,
                    HdbfPatchCatalog.GrowthPatchId + " faulted; crop keeps vanilla growth time (target="
                    + (_targetMethod ?? "?") + ")", exception);

                if (_failures is null || !_failures.RecordFailure())
                {
                    return;
                }

                _quarantined = true;
                _log?.Write(HdbfLogCategory.Compat, HdbfLogKeys.HarmonyPatchUnloaded,
                    HdbfPatchCatalog.GrowthPatchId + " reached " + PatchFailureStateMachine.FailureThreshold
                    + " failures and is being uninstalled; vanilla growth time is restored");
                _autoUnload?.Invoke();
            }
            catch (Exception)
            {
                // Failure reporting must never escalate into a second fault path.
            }
        }
    }
}
