// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: Lead (round-3 contract)
// Purpose: Source/contract pins for the second repair round's Lead-owned pieces: the
//          settlement-wide refill action (WP-08) and the read-only behavior probe (WP-07/RE-03).
//          The gameplay/UI halves cannot be loaded into this game-free assembly, so their shipped
//          sources are read through the baked-in repo root (same pattern as the save bridge tests).
// Requirements: REQ-V2-UI-02, REQ-V2-WATER-04, REQ-V2-READ-02, REQ-V2-SAFE-01, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the round-3 additions: the refill-all command/UI wiring (WP-08) and the
    /// read-only third-party patch probe (WP-07). No game assembly is referenced; the checks run on
    /// the real shipped sources plus the frozen Core command contract.
    /// </summary>
    public static class V2RefillAndProbeContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string CommandServicePath = "src/HDBF.Gameplay/Components/HdbfFarmCommandService.cs";
        private const string CommandServiceRefillAllPath =
            "src/HDBF.Gameplay/Components/HdbfFarmCommandService.RefillAll.cs";
        private const string QueryServicePath = "src/HDBF.Gameplay/Components/HdbfFarmQueryService.cs";
        private const string BridgePath = "src/HDBF.UI/Panels/HdbfIndoorCommandBridge.cs";
        private const string FragmentBuildPath = "src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs";
        private const string FragmentCommandsPath = "src/HDBF.UI/Panels/HdbfFarmPanelFragment.Commands.cs";
        private const string ProbePath = "src/HDBF.Gameplay/Components/HdbfBehaviorProbe.cs";
        private const string ConfiguratorPath = "src/HDBF.Gameplay/HdbfGameConfigurator.cs";

        /// <summary>WP-08: the Core command contract publishes the settlement-wide refill action.</summary>
        public static void TestRefillAllIsPublishedOnTheCoreCommandContract()
        {
            var method = typeof(IBoxFarmCommand).GetMethod("TryRefillAll");
            TestAssert.NotNull(method, "IBoxFarmCommand must publish TryRefillAll (WP-08)");
            var parameters = method!.GetParameters();
            TestAssert.Equal(2, parameters.Length, "TryRefillAll takes the failure key and the refilled count");
            TestAssert.True(parameters[0].IsOut && parameters[0].ParameterType == typeof(string).MakeByRefType(),
                "first parameter is the out failure key");
            TestAssert.True(parameters[1].IsOut && parameters[1].ParameterType == typeof(int).MakeByRefType(),
                "second parameter is the out refilled count");
            TestAssert.Equal("HDBF.Error.NoFarms", HdbfCommandFailures.NoFarms,
                "the empty-session rejection key is stable");
            Console.WriteLine("[v2-refill-all] command contract: TryRefillAll(out string, out int) + NoFarms key");
        }

        /// <summary>WP-08: gameplay iterates the live farms and keeps each farm's all-or-nothing rules.</summary>
        public static void TestRefillAllGameplayKeepsPerFarmRules()
        {
            var service = RepoRead(CommandServicePath);
            TestAssert.True(service.Contains("partial class HdbfFarmCommandService", StringComparison.Ordinal)
                            && service.Contains("TryGetController", StringComparison.Ordinal),
                "the command service must be a partial class so the refill-all pass keeps the line gate");
            var body = RepoRead(CommandServiceRefillAllPath);
            TestAssert.True(body.Contains("public bool TryRefillAll", StringComparison.Ordinal),
                "the refill-all implementation lives in its own partial file");
            TestAssert.True(body.Contains("LiveFarmIds()", StringComparison.Ordinal),
                "refill-all must enumerate the live farms (query service)");
            TestAssert.True(body.Contains("TryRequestManualRefill", StringComparison.Ordinal),
                "refill-all must reuse the manual refill command per farm (same cost/rules)");
            TestAssert.True(body.Contains("HdbfCommandFailures.NoFarms", StringComparison.Ordinal),
                "an empty session must report NoFarms");
            TestAssert.True(body.Contains("catch (Exception", StringComparison.Ordinal),
                "one broken farm must be isolated instead of aborting the whole pass");
            TestAssert.True(body.Contains("IndoorLogKeys.RefillAll", StringComparison.Ordinal),
                "the pass logs under the registered water.refill-all key");

            var query = RepoRead(QueryServicePath);
            TestAssert.True(query.Contains("LiveFarmIds()", StringComparison.Ordinal),
                "the query singleton exposes the live farm ids for the command");
            Console.WriteLine("[v2-refill-all] gameplay: per-farm all-or-nothing pass + isolation + log");
        }

        /// <summary>WP-08: the panel exposes a dedicated button and reports the refilled count.</summary>
        public static void TestRefillAllPanelControlsAreWired()
        {
            var build = RepoRead(FragmentBuildPath);
            TestAssert.True(build.Contains("_refillAllButton", StringComparison.Ordinal)
                            && build.Contains("ActionRefillAll", StringComparison.Ordinal)
                            && build.Contains("TooltipRefillAll", StringComparison.Ordinal),
                "the action card must create the refill-all button with its own tooltip");
            var commands = RepoRead(FragmentCommandsPath);
            TestAssert.True(commands.Contains("OnRefillAllClicked", StringComparison.Ordinal)
                            && commands.Contains("ActionRefillAllDone", StringComparison.Ordinal),
                "the click handler must report the number of refilled farms");
            var bridge = RepoRead(BridgePath);
            TestAssert.True(bridge.Contains("TryRefillAll", StringComparison.Ordinal)
                            && bridge.Contains("MakeByRefType", StringComparison.Ordinal),
                "the UI bridge resolves the gameplay TryRefillAll member by signature");
            Console.WriteLine("[v2-refill-all] panel: button + handler + bridge wiring present");
        }

        /// <summary>WP-08: the localized strings exist in every shipped CSV (key parity is pinned globally).</summary>
        public static void TestRefillAllLocalizationKeysExist()
        {
            var keys = new[]
            {
                HDBF.UI.Localization.HdbfUiLocKeys.ActionRefillAll,
                HDBF.UI.Localization.HdbfUiLocKeys.ActionRefillAllDone,
                HDBF.UI.Localization.HdbfUiLocKeys.TooltipRefillAll,
                HDBF.UI.Localization.HdbfUiLocKeys.ErrorNoFarms,
                HDBF.UI.Localization.HdbfUiLocKeys.PortGlobalSupply,
            };
            var languages = new[] { "enUS", "zhCN", "zhTW" };
            for (var index = 0; index < languages.Length; index++)
            {
                var csv = RepoRead("data/Localizations/" + languages[index] + ".csv");
                for (var key = 0; key < keys.Length; key++)
                {
                    TestAssert.True(csv.Contains(keys[key] + ",", StringComparison.Ordinal),
                        languages[index] + ".csv is missing " + keys[key]);
                }
            }

            Console.WriteLine("[v2-refill-all] localization: 5 keys present in all three CSVs");
        }

        /// <summary>WP-07: the probe is read-only diagnostics (no patch API, no patch token, no state change).</summary>
        public static void TestBehaviorProbeIsReadOnlyDiagnostics()
        {
            var probe = RepoRead(ProbePath);
            TestAssert.True(probe.Contains("IndoorLogKeys.BehaviorProbe", StringComparison.Ordinal)
                            && probe.Contains("IndoorLogKeys.BehaviorProbePatched", StringComparison.Ordinal),
                "the probe logs under the registered behavior-probe keys");
            foreach (var forbidden in new[]
                     {
                         "HarmonyLib", "HarmonyInstance", "[HarmonyPatch]", "ReportHarmonyPatchCount",
                         "Patch(", "AddPatch", "Transpiler",
                     })
            {
                TestAssert.False(probe.Contains(forbidden, StringComparison.Ordinal),
                    "the probe must not contain the patch token '" + forbidden + "'");
            }

            TestAssert.True(probe.Contains("WorkerRootBehavior", StringComparison.Ordinal)
                            && probe.Contains("\"Decide\"", StringComparison.Ordinal),
                "the probe resolves the vanilla decision method read-only");
            TestAssert.True(probe.Contains("catch (Exception", StringComparison.Ordinal),
                "the probe must isolate every reflection failure");
            TestAssert.Equal("indoor.behavior-probe", IndoorLogKeys.BehaviorProbe);
            TestAssert.Equal("indoor.behavior-probe-patched", IndoorLogKeys.BehaviorProbePatched);
            Console.WriteLine("[v2-behavior-probe] source: read-only reflection, no patch API, isolated");
        }

        /// <summary>WP-07: the probe is a game singleton, so it runs once per session load.</summary>
        public static void TestBehaviorProbeIsRegisteredAsAGameSingleton()
        {
            var configurator = RepoRead(ConfiguratorPath);
            TestAssert.True(configurator.Contains("Bind<HdbfBehaviorProbe>().AsSingleton()", StringComparison.Ordinal),
                "the probe must be bound as a game singleton in HdbfGameConfigurator");
            var probe = RepoRead(ProbePath);
            TestAssert.True(probe.Contains("ILoadableSingleton", StringComparison.Ordinal),
                "the probe runs on load, once per session");
            Console.WriteLine("[v2-behavior-probe] DI: singleton bound and load-time only");
        }

        private static string RepoRead(string relativePath)
        {
            var attribute = typeof(V2RefillAndProbeContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(entry => string.Equals(entry.Key, RepoRootKey, StringComparison.Ordinal));
            TestAssert.True(attribute is not null && !string.IsNullOrEmpty(attribute.Value),
                "assembly metadata \"" + RepoRootKey + "\" is missing");
            var path = Path.Combine(attribute!.Value!, relativePath);
            TestAssert.True(File.Exists(path), "source file missing: " + path);
            return File.ReadAllText(path);
        }
    }
}
