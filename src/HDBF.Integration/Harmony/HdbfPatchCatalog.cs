namespace HDBF.Patches
{
    /// <summary>
    /// Whitelist of the Harmony patches this mod is allowed to install (REQ-COMPAT-16/17,
    /// chapter 8.4 "补丁白名单 + 签名校验").
    ///
    /// There is deliberately exactly one entry: ADR-009 approved a single Postfix (deviation D-2).
    /// The installer validates this whitelist against the installed game assembly before patching;
    /// if the target type/method/signature no longer matches, the patch is skipped and the mod
    /// keeps its pre-patch D-2 behaviour (never a crash, never a silent different method).
    ///
    /// This type intentionally contains only constants: it is pure metadata, embeddable as a test
    /// resource, and it holds no mutable state (REQ-ARCH-06 / chapter 8.4).
    /// </summary>
    public static class HdbfPatchCatalog
    {
        /// <summary>The one Harmony instance id required by REQ-COMPAT-16.</summary>
        public const string HarmonyInstanceId = "hdbf.highdensityboxfarm";

        /// <summary>Patch class id used in logs and the compatibility report.</summary>
        public const string GrowthPatchId = "HDBF.Patches.BoxFarmGrowthPatch";

        /// <summary>Whitelisted target assembly type (full name).</summary>
        public const string GrowthPatchTargetType = "Timberborn.Growing.Growable";

        /// <summary>Whitelisted target method (private instance method of <c>Growable</c>).</summary>
        public const string GrowthPatchTargetMethod = "ResumeGrowing";

        /// <summary>
        /// Patch kind. The catalog must contain exactly one Postfix entry and no IL-rewriting
        /// patch kind (REQ-COMPAT-17: v1 does not rewrite method bodies).
        /// </summary>
        public const string GrowthPatchKind = "Postfix";

        /// <summary>
        /// Harmony priority used for the patch: <c>HarmonyLib.Priority.Normal</c>. The installer
        /// also reads the constant from the loaded Harmony assembly and prefers that value.
        /// </summary>
        public const int GrowthPatchPriorityNormal = 300;

        /// <summary>Private <c>Growable</c> field that owns the growth timer.</summary>
        public const string GrowableTimeTriggerField = "_timeTrigger";

        /// <summary>Private <c>TimeTrigger</c> field: the original growth duration in game days.</summary>
        public const string TimeTriggerFullDelayField = "_fullDelayInDays";

        /// <summary>Private <c>TimeTrigger</c> field: the time left until the trigger fires.</summary>
        public const string TimeTriggerDelayLeftField = "_delayLeftInDays";
    }
}
