using System;
using System.Collections;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Growth;
using HDBF.Core.Logging;
using HDBF.Patches;
using Timberborn.Growing;
using Timberborn.TimeSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HDBF.Integration.Harmony
{
    /// <summary>
    /// Installs/uninstalls the one approved Postfix (ADR-009) through <b>reflection only</b>.
    ///
    /// Design constraints this class satisfies:
    /// * no compile-time HarmonyLib reference and no bundled 0Harmony.dll, matching the assembly
    ///   rule of ADR-002/REQ-COMPAT-02/05 ("Integration reaches third-party mods through
    ///   reflection"). It also keeps HDBF.Integration loadable when the Harmony mod is absent:
    ///   Bindito calls <c>Assembly.GetTypes()</c> on every mod assembly, and a type carrying a
    ///   HarmonyLib attribute would break container creation for the whole game;
    /// * the whitelist of <see cref="HdbfPatchCatalog"/> is validated signature-first: a renamed
    ///   type/method/field is skipped with one <c>HDBF/Warn</c>, never patched blindly
    ///   (chapter 8.4 / REQ-COMPAT-17);
    /// * an existing postfix on the same method is treated as a conflict and the patch is skipped
    ///   (chapter 8.4 "冲突检测"), so another mod's growth patch is never silently overridden;
    /// * exactly one Harmony instance exists per session, with the fixed id
    ///   <see cref="HdbfPatchCatalog.HarmonyInstanceId"/> (REQ-COMPAT-16);
    /// * uninstall reverses the install (unpatch + bridge shutdown + report count 0,
    ///   REQ-COMPAT-08 spirit).
    ///
    /// Every public entry point catches its own exceptions: a broken patch installation degrades to
    /// the pre-patch D-2 behaviour instead of faulting mod load (REQ-SAFE-02, REQ-COMPAT-18).
    /// </summary>
    internal sealed class HdbfHarmonyInstaller : IHdbfPatchSession
    {
        private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;
        private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string HarmonyTypeName = "HarmonyLib.Harmony";
        private const string HarmonyMethodTypeName = "HarmonyLib.HarmonyMethod";
        private const string HarmonyPriorityTypeName = "HarmonyLib.Priority";
        private const string PostfixMethodName = "Postfix";
        private const string TimeTriggerTypeName = "Timberborn.TimeSystem.TimeTrigger";

        private readonly IHdbfLog _log;
        private readonly ICompatRegistry _compat;
        private readonly HdbfGrowthPatchBridge _bridge;
        private readonly object _harmony;
        private readonly Type _harmonyType;
        private readonly MethodInfo _postfix;
        private readonly MethodBase _target;
        private readonly string _targetName;

        private bool _installed;
        private bool _shutDown;

        private HdbfHarmonyInstaller(IHdbfLog log, ICompatRegistry compat, HdbfGrowthPatchBridge bridge,
            object harmony, Type harmonyType, MethodInfo postfix, MethodBase target, string targetName)
        {
            _log = log;
            _compat = compat;
            _bridge = bridge;
            _harmony = harmony;
            _harmonyType = harmonyType;
            _postfix = postfix;
            _target = target;
            _targetName = targetName;
        }

        /// <inheritdoc />
        public string TargetMethod => _targetName;

        /// <inheritdoc />
        public bool IsInstalled => _installed;

        /// <summary>
        /// Tries to install the patch. Returns the session on success; null means "keep D-2
        /// behaviour" (Harmony missing, signature changed, conflict, or any fault) and is always
        /// logged exactly once. Never throws.
        /// </summary>
        public static IHdbfPatchSession? TryInstall(IHdbfLog? log, IGrowthMultiplierSource? growthSource,
            ICompatRegistry? compat)
        {
            if (log is null || growthSource is null || compat is null)
            {
                return null;
            }

            try
            {
                return Install(log, growthSource, compat);
            }
            catch (Exception exception)
            {
                SafeWarn(log, "growth patch installation threw; pre-patch D-2 behaviour is kept",
                    exception);
                return null;
            }
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            if (_shutDown)
            {
                return;
            }

            _shutDown = true;
            var wasInstalled = _installed;
            _installed = false;

            if (wasInstalled)
            {
                try
                {
                    Unpatch();
                }
                catch (Exception exception)
                {
                    SafeError(_log, "growth patch unpatch failed; vanilla behaviour will still be used",
                        exception);
                }
            }

            try
            {
                _bridge.Shutdown();
            }
            catch (Exception)
            {
                // Bridge shutdown is best effort.
            }

            try
            {
                _compat.ReportHarmonyPatchCount(0);
            }
            catch (Exception)
            {
                // Report bookkeeping must never throw out of unload.
            }

            if (wasInstalled)
            {
                _log.Write(HdbfLogCategory.Compat, HdbfLogKeys.HarmonyPatchUnloaded,
                    "uninstalled " + HdbfPatchCatalog.GrowthPatchId + " (target=" + _targetName
                    + "); vanilla crop growth time is restored");
            }
        }

        /// <summary>Callback the bridge invokes after exhausting the three-failure budget.</summary>
        public void RequestAutoUnload()
        {
            try
            {
                Shutdown();
            }
            catch (Exception exception)
            {
                SafeError(_log, "growth patch auto-unload failed", exception);
            }
        }

        // ------------------------------------------------------------------ install

        private static IHdbfPatchSession? Install(IHdbfLog log, IGrowthMultiplierSource growthSource,
            ICompatRegistry compat)
        {
            var harmonyType = FindHarmonyType();
            if (harmonyType is null)
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "Harmony (HarmonyLib.Harmony) is not loaded; " + HdbfPatchCatalog.GrowthPatchId
                    + " stays off and pre-patch D-2 behaviour is used");
                return null;
            }

            if (!ValidateTarget(log, out var fullDelayField, out var delayLeftField))
            {
                return null;
            }

            var target = typeof(Growable).GetMethod(HdbfPatchCatalog.GrowthPatchTargetMethod,
                InstancePublic | InstanceNonPublic);
            var postfix = typeof(BoxFarmGrowthPatch).GetMethod(PostfixMethodName, StaticPublic);
            if (target is null || postfix is null)
            {
                // ValidateTarget already proved both; this is a defensive second gate.
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "patch target or postfix method could not be resolved; patch skipped");
                return null;
            }

            if (HasConflictingPostfix(harmonyType, target, log))
            {
                return null;
            }

            var harmonyMethodType = harmonyType.Assembly.GetType(HarmonyMethodTypeName, false);
            if (harmonyMethodType is null)
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "HarmonyMethod type not found; patch skipped");
                return null;
            }

            var postfixMethod = CreateHarmonyMethod(harmonyMethodType, harmonyType, postfix, log);
            if (postfixMethod is null)
            {
                return null;
            }

            // One Harmony instance per process (REQ-COMPAT-16). The DontDestroyOnLoad bridge keeps
            // it across game sessions, so a load/reload reuses the same instance instead of
            // constructing a second one.
            var bridge = FindOrCreateBridge(out var createdBridgeObject);
            object? harmony = null;
            if (bridge.TryGetHarmony(out var storedHarmony, out var storedType)
                && storedHarmony is not null && storedType == harmonyType)
            {
                harmony = storedHarmony;
            }
            else
            {
                harmony = CreateHarmony(harmonyType);
                if (harmony is null)
                {
                    log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                        "Harmony instance '" + HdbfPatchCatalog.HarmonyInstanceId
                        + "' could not be created; patch skipped");
                    DestroyBridgeIfCreated(createdBridgeObject);
                    return null;
                }

                bridge.SetHarmony(harmony, harmonyType);
            }

            var targetName = target.DeclaringType is null
                ? target.Name
                : target.DeclaringType.FullName + "." + target.Name;
            var installer = new HdbfHarmonyInstaller(log, compat, bridge, harmony, harmonyType, postfix,
                target, targetName);

            try
            {
                bridge.Initialize(growthSource, log, targetName, installer.RequestAutoUnload,
                    fullDelayField, delayLeftField);

                var patchMethod = harmonyType.GetMethod("Patch", InstancePublic, null,
                    new[]
                    {
                        typeof(MethodBase), harmonyMethodType, harmonyMethodType, harmonyMethodType,
                        harmonyMethodType,
                    }, null);
                if (patchMethod is null)
                {
                    log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                        "Harmony.Patch(MethodBase, HarmonyMethod, HarmonyMethod, ...) not found; patch skipped");
                    bridge.Shutdown();
                    DestroyBridgeIfCreated(createdBridgeObject);
                    return null;
                }

                patchMethod.Invoke(harmony, new object?[] { target, null, postfixMethod, null, null });
                installer._installed = true;
            }
            catch (Exception exception)
            {
                installer._installed = false;
                SafeWarn(log, "growth patch could not be bound (" + targetName
                    + "); pre-patch D-2 behaviour is kept", exception);
                try
                {
                    bridge.Shutdown();
                }
                catch (Exception)
                {
                    // Already degraded.
                }

                DestroyBridgeIfCreated(createdBridgeObject);
                return null;
            }

            compat.ReportHarmonyPatchCount(1);
            log.Write(HdbfLogCategory.Compat, HdbfLogKeys.HarmonyPatchInstalled,
                "installed " + HdbfPatchCatalog.GrowthPatchId + " (" + HdbfPatchCatalog.GrowthPatchKind
                + ", priority=" + ReadNormalPriority(harmonyType) + ") target=" + targetName
                + " harmonyInstance=" + HdbfPatchCatalog.HarmonyInstanceId
                + "; managed crops grow at G_irr*G_fat*G_maint*G_power speed");
            return installer;
        }

        /// <summary>
        /// Whitelist + signature validation (chapter 8.4). The patch is only ever bound to this exact
        /// shape: a private parameterless void instance method named "ResumeGrowing" on
        /// <see cref="Growable"/>, whose private timer field is an <c>ITimeTrigger</c> backed by
        /// <c>Timberborn.TimeSystem.TimeTrigger</c> with the two private fields the bridge rewrites.
        /// Returns the cached reflection handles on success.
        /// </summary>
        private static bool ValidateTarget(IHdbfLog log, out FieldInfo? fullDelayField,
            out FieldInfo? delayLeftField)
        {
            fullDelayField = null;
            delayLeftField = null;

            var target = typeof(Growable).GetMethod(HdbfPatchCatalog.GrowthPatchTargetMethod,
                InstancePublic | InstanceNonPublic);
            if (target is null)
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "target method " + HdbfPatchCatalog.GrowthPatchTargetType + "."
                    + HdbfPatchCatalog.GrowthPatchTargetMethod
                    + " not found in this game build; growth patch skipped (D-2 behaviour kept)");
                return false;
            }

            if (target.ReturnType != typeof(void) || target.GetParameters().Length != 0
                || target.IsStatic)
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "target method signature changed (expected private instance void with no parameters); "
                    + "growth patch skipped");
                return false;
            }

            var timerField = typeof(Growable).GetField(HdbfPatchCatalog.GrowableTimeTriggerField,
                InstanceNonPublic);
            if (timerField is null || !typeof(ITimeTrigger).IsAssignableFrom(timerField.FieldType))
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "Growable timer field changed (expected ITimeTrigger "
                    + HdbfPatchCatalog.GrowableTimeTriggerField + "); growth patch skipped");
                return false;
            }

            var timeTriggerType = FindGameType(TimeTriggerTypeName);
            if (timeTriggerType is null)
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "type " + TimeTriggerTypeName + " not found; growth patch skipped");
                return false;
            }

            fullDelayField = timeTriggerType.GetField(HdbfPatchCatalog.TimeTriggerFullDelayField,
                InstanceNonPublic);
            delayLeftField = timeTriggerType.GetField(HdbfPatchCatalog.TimeTriggerDelayLeftField,
                InstanceNonPublic);
            if (fullDelayField is null || fullDelayField.FieldType != typeof(float)
                || delayLeftField is null || delayLeftField.FieldType != typeof(float))
            {
                fullDelayField = null;
                delayLeftField = null;
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "TimeTrigger internals changed (" + HdbfPatchCatalog.TimeTriggerFullDelayField
                    + "/" + HdbfPatchCatalog.TimeTriggerDelayLeftField
                    + " missing); patch skipped and vanilla growth kept");
                return false;
            }

            var postfix = typeof(BoxFarmGrowthPatch).GetMethod(PostfixMethodName, StaticPublic);
            if (postfix is null)
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    "patch class has no public static Postfix method; growth patch skipped");
                return false;
            }

            return true;
        }

        private static Type? FindHarmonyType()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    var type = assemblies[i].GetType(HarmonyTypeName, false);
                    if (type is not null)
                    {
                        return type;
                    }
                }
                catch (Exception)
                {
                    // A broken assembly must not stop the scan.
                }
            }

            return null;
        }

        /// <summary>
        /// Finds one game type by full name. Used for <c>TimeTrigger</c>, which is internal in
        /// <c>Timberborn.TimeSystem</c> and therefore cannot be named at compile time.
        /// </summary>
        private static Type? FindGameType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    var type = assemblies[i].GetType(fullName, false);
                    if (type is not null)
                    {
                        return type;
                    }
                }
                catch (Exception)
                {
                    // A broken assembly must not stop the scan.
                }
            }

            return null;
        }

        private static object? CreateHarmony(Type harmonyType)
        {
            try
            {
                return Activator.CreateInstance(harmonyType, new object[] { HdbfPatchCatalog.HarmonyInstanceId });
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object? CreateHarmonyMethod(Type harmonyMethodType, Type harmonyType,
            MethodInfo postfix, IHdbfLog log)
        {
            try
            {
                var method = Activator.CreateInstance(harmonyMethodType, new object[] { postfix });
                var priorityField = harmonyMethodType.GetField("priority", InstancePublic);
                if (priorityField is not null)
                {
                    priorityField.SetValue(method, ReadNormalPriority(harmonyType));
                }

                return method;
            }
            catch (Exception exception)
            {
                SafeWarn(log, "HarmonyMethod could not be created; growth patch skipped", exception);
                return null;
            }
        }

        private static int ReadNormalPriority(Type harmonyType)
        {
            try
            {
                var priorityType = harmonyType.Assembly.GetType(HarmonyPriorityTypeName, false);
                var normalField = priorityType?.GetField("Normal", StaticPublic);
                if (normalField is not null && normalField.GetValue(null) is int value)
                {
                    return value;
                }
            }
            catch (Exception)
            {
                // Fall through to the catalog constant.
            }

            return HdbfPatchCatalog.GrowthPatchPriorityNormal;
        }

        private static bool HasConflictingPostfix(Type harmonyType, MethodBase target, IHdbfLog log)
        {
            try
            {
                var getPatchInfo = harmonyType.GetMethod("GetPatchInfo", StaticPublic, null,
                    new[] { typeof(MethodBase) }, null);
                if (getPatchInfo is null)
                {
                    return false;
                }

                var patches = getPatchInfo.Invoke(null, new object[] { target });
                if (patches is null)
                {
                    return false;
                }

                var postfixesField = patches.GetType().GetField("Postfixes",
                    InstancePublic | InstanceNonPublic);
                if (postfixesField?.GetValue(patches) is ICollection postfixes && postfixes.Count > 0)
                {
                    log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                        "another mod already postfixes " + target.DeclaringType?.FullName + "."
                        + target.Name + " (" + postfixes.Count
                        + " postfix(es)); HDBF growth patch skipped, pre-patch D-2 behaviour kept");
                    return true;
                }
            }
            catch (Exception exception)
            {
                SafeWarn(log, "Harmony patch-info conflict check failed; assuming no conflict", exception);
            }

            return false;
        }

        private static HdbfGrowthPatchBridge FindOrCreateBridge(out GameObject? createdObject)
        {
            createdObject = null;
            var existing = Object.FindAnyObjectByType<HdbfGrowthPatchBridge>();
            if (existing is not null)
            {
                return existing;
            }

            var gameObject = new GameObject("HDBF.GrowthPatch");
            Object.DontDestroyOnLoad(gameObject);
            createdObject = gameObject;
            return gameObject.AddComponent<HdbfGrowthPatchBridge>();
        }

        private static void DestroyBridgeIfCreated(GameObject? createdObject)
        {
            if (createdObject is null)
            {
                return;
            }

            try
            {
                Object.Destroy(createdObject);
            }
            catch (Exception)
            {
                // Scene teardown best effort.
            }
        }

        private void Unpatch()
        {
            var unpatch = _harmonyType.GetMethod("Unpatch", InstancePublic, null,
                new[] { typeof(MethodBase), typeof(MethodInfo) }, null);
            if (unpatch is not null)
            {
                unpatch.Invoke(_harmony, new object[] { _target, _postfix });
                return;
            }

            var unpatchAll = _harmonyType.GetMethod("UnpatchAll", InstancePublic, null,
                new[] { typeof(string) }, null);
            if (unpatchAll is not null)
            {
                unpatchAll.Invoke(_harmony, new object?[] { HdbfPatchCatalog.HarmonyInstanceId });
            }
        }

        private static void SafeWarn(IHdbfLog log, string message, Exception exception)
        {
            try
            {
                log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                    message + " (" + exception.GetType().Name + ": " + exception.Message + ")");
            }
            catch (Exception)
            {
                // Logging must not throw out of the patch bootstrap.
            }
        }

        private static void SafeError(IHdbfLog log, string message, Exception exception)
        {
            try
            {
                log.Error(HdbfLogCategory.Error, HdbfLogKeys.HarmonyPatchFailed, message, exception);
            }
            catch (Exception)
            {
                // Logging must not throw out of the patch bootstrap.
            }
        }
    }
}
