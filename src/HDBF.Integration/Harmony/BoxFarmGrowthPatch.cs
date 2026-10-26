using System;
using HDBF.Integration.Harmony;
using Timberborn.Growing;
using Timberborn.TimeSystem;
using UnityEngine;

namespace HDBF.Patches
{
    /// <summary>
    /// The single approved Harmony Postfix (ADR-009, deviation D-2). It stretches the vanilla growth
    /// time of a crop that starts growing on a HDBF-managed plot by
    /// <c>1 / (G_irr × G_fat × G_maint × G_power)</c>.
    ///
    /// Why this method is the patch point (IL evidence in
    /// <c>docs/workstreams/bug-03-growth-multiplier-patch.md</c>):
    /// * <c>Growable.Awake</c> creates an <c>ITimeTrigger</c> with the blueprint's
    ///   <c>GrowthTimeInDays</c>; nothing else drives growth progress.
    /// * <c>Growable.InitializeEntity</c> calls the private <c>ResumeGrowing</c>, which calls
    ///   <c>ITimeTrigger.Resume()</c>; <c>TimeTrigger.Resume</c> registers the absolute completion
    ///   timestamp <c>now + delayLeft</c> with <c>TimeTriggerService</c>. Completion is therefore
    ///   wall-clock scheduled, not per-tick progress: a Postfix here can rewrite the trigger's
    ///   duration before the crop has grown at all.
    /// * Patched after <c>Growable.Grow</c> (HasGrown → <c>Cuttable</c> enables the <c>Yielder</c>)
    ///   would be too late: the crop would already be harvestable.
    /// * Patching the global <c>TimeTriggerService.Tick</c> is forbidden by chapter 8.4
    ///   ("不补全局 Tick"), and <c>Growable.get_GrowthProgress</c> is a pure getter.
    ///
    /// The body is intentionally trivial and Harmony-attribute-free: the installer binds it through
    /// reflection. Not referencing HarmonyLib here is what keeps HDBF.Integration loadable without
    /// the Harmony mod (REQ-COMPAT-02/05: no third-party assembly references).
    ///
    /// Both Harmony-injected parameters are resolved at patch time:
    /// <c>__instance</c> = the Growable whose growth starts, <c>____timeTrigger</c> = its private
    /// timer field <c>_timeTrigger</c> (Harmony strips exactly three underscores from a field
    /// injection name, so the fourth underscore is the field's own). Everything after that is
    /// delegated to <see cref="HdbfGrowthPatchBridge"/>.
    /// </summary>
    public static class BoxFarmGrowthPatch
    {
        private const string LogPrefix = "[HDBF/Error] " + HdbfPatchCatalog.GrowthPatchId + ": ";

        /// <summary>
        /// Postfix of <c>Timberborn.Growing.Growable.ResumeGrowing()</c>. Never throws into the game
        /// loop (REQ-COMPAT-18): any fault is reported and growth keeps vanilla time.
        /// </summary>
        public static void Postfix(Growable __instance, ITimeTrigger ____timeTrigger)
        {
            try
            {
                if (__instance is null || ____timeTrigger is null)
                {
                    return;
                }

                // The bridge is a DontDestroyOnLoad component (a component cache, not static
                // state); it owns the provider reference and the failure budget. One lookup per
                // crop growth start, never per frame.
                var bridge = UnityEngine.Object.FindAnyObjectByType<HdbfGrowthPatchBridge>();
                if (bridge is null)
                {
                    return;
                }

                bridge.Apply(__instance, ____timeTrigger);
            }
            catch (Exception exception)
            {
                // Last-resort report: the bridge itself may be unavailable, so go straight to the
                // Unity log with the stable HDBF/Error prefix.
                try
                {
                    Debug.LogError(LogPrefix + exception.GetType().Name + ": " + exception.Message);
                }
                catch
                {
                    // Unity logging itself is unavailable: nothing else can be done safely.
                }
            }
        }
    }
}
