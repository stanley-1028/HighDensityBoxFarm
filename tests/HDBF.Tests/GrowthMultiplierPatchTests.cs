using System;
using System.IO;
using System.Text;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Compat;
using HDBF.Core.Config;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of ADR-009 / deviation D-2 (BUG-03):
    /// * the growth-rate product of the four constraint layers and its mapping to a finite timer
    ///   scale (the only math the approved Postfix applies);
    /// * the three-strike failure state machine that makes the Postfix uninstall itself
    ///   (REQ-COMPAT-18);
    /// * the <c>features.growthMultiplierPatch</c> feature switch (default on, off = D-2);
    /// * the single-patch / no-Transpiler / single-Harmony-instance source contract, checked against
    ///   the shipped HDBF.Integration sources embedded in this test assembly (they reference game
    ///   assemblies and cannot be loaded here).
    ///
    /// The patch body itself cannot be executed offline (it needs the running game's Growable and
    /// TimeTrigger instances); the report states that explicitly.
    /// </summary>
    public static class GrowthMultiplierPatchTests
    {
        // ------------------------------------------------------------------ balance math

        public static void TestGrowthMultiplierIsTheProductOfTheFourLayers()
        {
            // Chapter 6.4/6.5 values: F=1 -> G_fat=0.4, off-grid -> G_power=0.6, one missed
            // maintenance cycle -> G_maint=0.5. Irrigation 0.8 halfway between 0.25*Cmin and Cmin.
            var gFat = BalanceFormulas.FatigueMultiplier(1f);
            var gPower = BalanceFormulas.PowerMultiplier(false);
            var gMaint = BalanceFormulas.MaintenanceMultiplier(1);
            TestAssert.Equal(0.4f, gFat, 1e-6f, "G_fat at F=1");
            TestAssert.Equal(0.6f, gPower, 1e-6f, "G_power off-grid");
            TestAssert.Equal(0.5f, gMaint, 1e-6f, "G_maint one cycle overdue");

            var product = BalanceFormulas.GrowthMultiplier(0.5f, gFat, gMaint, gPower);
            TestAssert.Equal(0.5f * 0.4f * 0.5f * 0.6f, product, 1e-6f,
                "growth multiplier must be the plain product of the four layers");
        }

        public static void TestGrowthMultiplierIsZeroWhenAnyLayerIsZero()
        {
            TestAssert.Equal(0f, BalanceFormulas.GrowthMultiplier(0f, 1f, 1f, 1f), 1e-6f, "G_irr=0");
            TestAssert.Equal(0f, BalanceFormulas.GrowthMultiplier(1f, 0f, 1f, 1f), 1e-6f, "G_fat=0");
            TestAssert.Equal(0f, BalanceFormulas.GrowthMultiplier(1f, 1f, 0f, 1f), 1e-6f, "G_maint=0 (shutdown)");
            TestAssert.Equal(0f, BalanceFormulas.GrowthMultiplier(1f, 1f, 1f, 0f), 1e-6f, "G_power=0");
        }

        public static void TestGrowthMultiplierCannotExceedOneAndClampsBadInput()
        {
            TestAssert.Equal(1f, BalanceFormulas.GrowthMultiplier(2f, 2f, 2f, 2f), 1e-6f,
                "the four layers only reduce; out-of-range inputs are clamped");
            TestAssert.Equal(1f, BalanceFormulas.GrowthMultiplier(1f, 1f, 1f, 1f), 1e-6f);
            TestAssert.Equal(0f, BalanceFormulas.GrowthMultiplier(-1f, 1f, 1f, 1f), 1e-6f,
                "negative layers clamp to zero, never below");
            TestAssert.Equal(0f, BalanceFormulas.GrowthMultiplier(float.NaN, 1f, 1f, 1f), 1e-6f,
                "NaN must degrade to zero instead of leaking into the timer");
        }

        public static void TestGrowthRateMultiplierKeepsAFiniteFloor()
        {
            TestAssert.Equal(1f, BalanceFormulas.GrowthRateMultiplier(1f), 1e-6f);
            TestAssert.Equal(0.6f, BalanceFormulas.GrowthRateMultiplier(0.6f), 1e-6f);
            TestAssert.Equal(BalanceFormulas.MinimumGrowthRate,
                BalanceFormulas.GrowthRateMultiplier(0f), 1e-6f,
                "a zero multiplier must not freeze an already planted crop (REQ-IMPL-20)");
            TestAssert.Equal(BalanceFormulas.MinimumGrowthRate,
                BalanceFormulas.GrowthRateMultiplier(0.01f), 1e-6f);
            TestAssert.Equal(1f, BalanceFormulas.GrowthRateMultiplier(5f), 1e-6f,
                "the patch must never speed a crop up");
            TestAssert.Equal(BalanceFormulas.MinimumGrowthRate,
                BalanceFormulas.GrowthRateMultiplier(float.NaN), 1e-6f);
            TestAssert.Equal(0.05f, BalanceFormulas.MinimumGrowthRate, 1e-6f,
                "documented floor is 5% (20x slowdown at worst)");
        }

        public static void TestGrowthTimeScaleIsTheInverseRate()
        {
            TestAssert.Equal(1f, BalanceFormulas.GrowthTimeScale(1f), 1e-6f, "unconstrained = vanilla time");
            TestAssert.Equal(2f, BalanceFormulas.GrowthTimeScale(0.5f), 1e-6f, "half speed = double time");
            TestAssert.Equal(1f / 0.6f, BalanceFormulas.GrowthTimeScale(0.6f), 1e-6f);
            TestAssert.Equal(20f, BalanceFormulas.GrowthTimeScale(0f), 1e-6f,
                "zero multiplier maps to the finite 20x floor, not infinity");
            TestAssert.True(BalanceFormulas.GrowthTimeScale(0.4f) > BalanceFormulas.GrowthTimeScale(0.5f),
                "a smaller rate must produce a longer growth time");
        }

        public static void TestGrowthTimeScaleOfTheDefaultUnconstrainedFarmIsVanilla()
        {
            var product = BalanceFormulas.GrowthMultiplier(
                BalanceFormulas.IrrigationMultiplier(1f, 0.5f),
                BalanceFormulas.FatigueMultiplier(0f),
                BalanceFormulas.MaintenanceMultiplier(0),
                BalanceFormulas.PowerMultiplier(true));
            TestAssert.Equal(1f, product, 1e-6f);
            TestAssert.Equal(1f, BalanceFormulas.GrowthTimeScale(product), 1e-6f,
                "a fully satisfied farm keeps vanilla growth time (the patch is a no-op)");
        }

        // ------------------------------------------------------------------ failure state machine

        public static void TestPatchFailureStateMachineUnloadsOnThirdFailure()
        {
            var machine = new PatchFailureStateMachine("HDBF.Patches.BoxFarmGrowthPatch",
                "Timberborn.Growing.Growable.ResumeGrowing");
            TestAssert.Equal(0, machine.FailureCount);
            TestAssert.False(machine.IsUnloaded);
            TestAssert.False(machine.RecordFailure(), "first failure must not unload");
            TestAssert.Equal(1, machine.FailureCount);
            TestAssert.False(machine.RecordFailure(), "second failure must not unload");
            TestAssert.Equal(2, machine.FailureCount);
            TestAssert.True(machine.RecordFailure(), "third failure must request unload (REQ-COMPAT-18)");
            TestAssert.Equal(3, machine.FailureCount);
            TestAssert.True(machine.IsUnloaded);
            TestAssert.False(machine.RecordFailure(), "an unloaded patch must not request unload twice");
            TestAssert.Equal(3, machine.FailureCount, "failure count is capped at the threshold");
        }

        public static void TestPatchFailureStateMachineResetReArmsTheBudget()
        {
            var machine = new PatchFailureStateMachine("p", "t");
            machine.RecordFailure();
            machine.RecordFailure();
            machine.RecordFailure();
            TestAssert.True(machine.IsUnloaded);
            machine.Reset();
            TestAssert.False(machine.IsUnloaded);
            TestAssert.Equal(0, machine.FailureCount);
            TestAssert.False(machine.RecordFailure());
            TestAssert.Equal(1, machine.FailureCount);
        }

        public static void TestPatchFailureStateMachineClampsThresholdAndKeepsIdentity()
        {
            var zeroThreshold = new PatchFailureStateMachine("p", "t", 0);
            TestAssert.True(zeroThreshold.RecordFailure(), "a non-positive threshold is clamped to 1");

            var custom = new PatchFailureStateMachine("patch", "method", 3);
            TestAssert.Equal("patch", custom.PatchId);
            TestAssert.Equal("method", custom.TargetMethod);
            TestAssert.Equal(3, PatchFailureStateMachine.FailureThreshold,
                "REQ-COMPAT-18 fixes the budget at three failures");
        }

        // ------------------------------------------------------------------ feature switch

        public static void TestGrowthMultiplierPatchDefaultsOn()
        {
            TestAssert.True(new FeatureFlags().GrowthMultiplierPatch,
                "chapter 9.3 default table: the approved patch is on");
            TestAssert.True(HdbfConfig.Default.Features.GrowthMultiplierPatch);
        }

        public static void TestConfigCodecParsesGrowthMultiplierPatchSwitch()
        {
            var off = HdbfConfigCodec.Parse("{\"features\":{\"growthMultiplierPatch\":false}}");
            TestAssert.False(off.Features.GrowthMultiplierPatch,
                "features.growthMultiplierPatch=false must reach the config object");
            TestAssert.True(off.Features.DensePlanting, "other flags keep their defaults");

            var on = HdbfConfigCodec.Parse("{\"features\":{\"growthMultiplierPatch\":true}}");
            TestAssert.True(on.Features.GrowthMultiplierPatch);

            var missing = HdbfConfigCodec.Parse("{\"features\":{}}");
            TestAssert.True(missing.Features.GrowthMultiplierPatch, "missing key keeps the on default");
        }

        public static void TestConfigCodecSerializesGrowthMultiplierPatchSwitch()
        {
            var json = HdbfConfigCodec.Serialize(HdbfConfig.Default);
            TestAssert.True(json.Contains("\"growthMultiplierPatch\": true", StringComparison.Ordinal),
                "serialized config must contain the switch: " + json);
            var roundTrip = HdbfConfigCodec.Parse(json);
            TestAssert.True(roundTrip.Features.GrowthMultiplierPatch);

            var offConfig = HdbfConfig.Default with
            {
                Features = HdbfConfig.Default.Features with { GrowthMultiplierPatch = false },
            };
            var offJson = HdbfConfigCodec.Serialize(offConfig);
            TestAssert.True(offJson.Contains("\"growthMultiplierPatch\": false", StringComparison.Ordinal),
                "serialized config must carry an explicit off value: " + offJson);
            TestAssert.False(HdbfConfigCodec.Parse(offJson).Features.GrowthMultiplierPatch);
        }

        // ------------------------------------------------------------------ shipped source contract

        private const string CatalogResource = "HDBF.Tests.Source.GrowthPatchCatalog.cs";
        private const string PatchResource = "HDBF.Tests.Source.GrowthPatch.cs";
        private const string InstallerResource = "HDBF.Tests.Source.HarmonyInstaller.cs";
        private const string BootstrapResource = "HDBF.Tests.Source.HarmonyBootstrap.cs";
        private const string BridgeResource = "HDBF.Tests.Source.GrowthPatchBridge.cs";

        private static readonly string[] PatchSources =
        {
            CatalogResource, PatchResource, InstallerResource, BootstrapResource, BridgeResource,
        };

        public static void TestShippedPatchSourcesAreEmbedded()
        {
            foreach (var name in PatchSources)
            {
                TestAssert.True(ReadResource(name).Length > 0, "embedded source missing: " + name);
            }
        }

        public static void TestExactlyOneApprovedPostfixAndNoTranspiler()
        {
            var catalog = ReadResource(CatalogResource);
            TestAssert.True(catalog.Contains("namespace HDBF.Patches", StringComparison.Ordinal),
                "patch classes must live in the HDBF.Patches namespace (REQ-COMPAT-16)");
            TestAssert.True(catalog.Contains(
                    "public const string GrowthPatchKind = \"Postfix\";", StringComparison.Ordinal),
                "the one whitelisted patch must be a Postfix (ADR-009)");
            TestAssert.Equal(1, CountOccurrences(catalog, "GrowthPatchTargetMethod"),
                "the catalog must whitelist exactly one target method");
            TestAssert.True(catalog.Contains("public const string GrowthPatchTargetMethod = \"ResumeGrowing\";",
                    StringComparison.Ordinal),
                "the patched method is Growable.ResumeGrowing (IL evidence in the report)");
            TestAssert.True(catalog.Contains(
                    "public const string GrowthPatchTargetType = \"Timberborn.Growing.Growable\";",
                    StringComparison.Ordinal),
                "the target type is Growable");

            var all = new StringBuilder();
            for (var i = 0; i < PatchSources.Length; i++)
            {
                all.Append(ReadResource(PatchSources[i])).Append('\n');
            }

            TestAssert.False(all.ToString().Contains("Transpiler", StringComparison.Ordinal),
                "no Transpiler is allowed anywhere in the patch assembly (REQ-COMPAT-17)");
            TestAssert.Equal(1, CountOccurrences(all.ToString(), "public static void Postfix"),
                "exactly one patch body exists in the shipped sources");

            // Harmony 2.4.1 field injection: the parameter name must be exactly three underscores
            // plus the complete field name (the IL strips the first three and looks up the rest),
            // so the private Growable._timeTrigger field needs the four-underscore parameter name.
            var patch = ReadResource(PatchResource);
            TestAssert.True(patch.Contains("Growable __instance", StringComparison.Ordinal),
                "the Postfix must receive the patched instance as __instance");
            TestAssert.True(patch.Contains("ITimeTrigger ____timeTrigger", StringComparison.Ordinal),
                "the timer field must be injected as ____timeTrigger (_timeTrigger with the 3-underscore prefix)");
        }

        public static void TestSingleHarmonyInstanceId()
        {
            var catalog = ReadResource(CatalogResource);
            TestAssert.True(catalog.Contains(
                    "public const string HarmonyInstanceId = \"hdbf.highdensityboxfarm\";",
                    StringComparison.Ordinal),
                "REQ-COMPAT-16 fixes the single Harmony instance id");
            var all = new StringBuilder();
            for (var i = 0; i < PatchSources.Length; i++)
            {
                all.Append(ReadResource(PatchSources[i])).Append('\n');
            }

            TestAssert.Equal(1, CountOccurrences(all.ToString(), "\"hdbf.highdensityboxfarm\""),
                "the Harmony instance id is declared exactly once (in the catalog) and referenced by "
                + "constant elsewhere; no second Harmony instance exists");
            TestAssert.False(all.ToString().Contains("new Harmony(", StringComparison.Ordinal),
                "Harmony is created through reflection (no compile-time HarmonyLib reference)");
        }

        public static void TestFailureAndFeatureSwitchAreWired()
        {
            var bootstrap = ReadResource(BootstrapResource);
            TestAssert.True(bootstrap.Contains("IsPatchEnabled", StringComparison.Ordinal),
                "the bootstrap must consult features.growthMultiplierPatch");
            TestAssert.True(bootstrap.Contains("Unload()", StringComparison.Ordinal),
                "the bootstrap must unregister on unload (REQ-COMPAT-08 spirit)");

            var bridge = ReadResource(BridgeResource);
            TestAssert.True(bridge.Contains("PatchFailureStateMachine", StringComparison.Ordinal),
                "the bridge must use the L1-tested failure state machine");
            TestAssert.True(bridge.Contains("HarmonyPatchUnloaded", StringComparison.Ordinal),
                "reaching the failure budget must log the unload under HDBF/Compat");
            TestAssert.True(bridge.Contains("TryGetGrowthRate", StringComparison.Ordinal),
                "the bridge must use the Core growth lookup, not re-implement the formula");
        }

        // ------------------------------------------------------------------ helpers

        private static string ReadResource(string name)
        {
            var assembly = typeof(GrowthMultiplierPatchTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                throw new TestFailureException("embedded resource not found: " + name);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return count;
                }

                count++;
                index += needle.Length;
            }
        }
    }
}
