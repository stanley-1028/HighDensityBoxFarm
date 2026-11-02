// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GAMEPLAY-01
// Purpose: Source contract of the building-pause integration (player report 2026-10-29 "it crash
//          when you paused it"): a paused farm must stop its own daily tick / water drain / warning
//          icons and must move its beavers out of the blocked building, because the vanilla pause
//          only unassigns the workplace and leaves enterers inside. The controller references
//          Timberborn types and cannot run in the game-free L1 assembly, so it is pinned as text
//          exactly like V2GameplayContractTests does (REQ-V2-SAFE-01, REQ-V2-LABOR-03).
// Requirements: REQ-V2-SAFE-01, REQ-V2-LABOR-03, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2PauseContractTests
    {
        private const string ResourcePrefix = "HDBF.Tests.Source.";

        public static void TestControllerSubscribesToTheVanillaPauseToggle()
        {
            var source = ReadControllerSources();
            AssertContains(source, "PausableBuilding", "reads the vanilla pause component");
            AssertContains(source, "PausedChanged", "subscribes to the vanilla pause event");
            AssertContains(source, "BindPauseState", "binds the pause event on activation");
            AssertContains(source, "UnbindPauseState", "unbinds the pause event on deactivation");
            AssertContains(source, "SyncPauseStateOnActivate", "a paused save enters the paused state on activation");
        }

        public static void TestPausedFarmStopsAndResumesItsOwnSubsystems()
        {
            var source = ReadPauseSource();
            AssertContains(source, "_waterFlow.Unregister(this)", "pause stops the continuous water drain");
            AssertContains(source, "_waterFlow.Register(this)", "resume restarts the continuous water drain");
            AssertContains(source, "_dayTrigger?.Pause()", "pause stops the daily tick");
            AssertContains(source, "_dayTrigger?.Resume()", "resume restarts the daily tick");
            AssertContains(source, "_worldIcons.Deactivate(_entityId)", "pause deactivates the warning icons");
            AssertContains(source, "_worldIcons.Register(_entityId, this)", "resume re-registers the warning icons");
        }

        public static void TestPausedFarmMovesBeaversOutOfTheBlockedBuilding()
        {
            var source = ReadControllerSources();
            AssertContains(source, "EjectBeaversFromPausedFarm", "pause ejects the stranded enterers");
            AssertContains(source, "EnterersInside", "reads the vanilla enterer list");
            AssertContains(source, "UnreserveSlotAndExit", "clears the paused building reservation (vanilla path)");
            AssertContains(source, "enterer.IsInside", "skips half-state enterers (CurrentBuilding null)");
            AssertContains(source, "enterer.CurrentBuilding", "only exits enterers that are inside this enterable");
            AssertContains(source, "NumberOfEnterersInside", "skips the ejection when nobody is inside");
            AssertContains(source, "ForceRemoveEveryone", "documents the vanilla force-remove fallback it mirrors");
            AssertContains(source, "PausedEjectSummary", "reports a requested/ejected/skipped/remaining summary");
            AssertContains(source, "PausedEjectFailed", "reports a failed ejection");
            AssertContains(source, "try", "the ejection is failure-isolated (REQ-V2-SAFE-01)");
        }

        /// <summary>
        /// 2026-11-02 residue R1/R2: a paused save restores its enterers after the one-shot pause
        /// ejection, and a beaver can still walk into a blocked building. A 1 Hz session singleton
        /// must keep reconciling the paused farms and the EntererAdded event must trigger an
        /// immediate ejection; the summary log keys must stay in the indoor.* namespace.
        /// </summary>
        public static void TestPausedFarmReconcilesResidualEnterers()
        {
            var source = ReadControllerSources();
            AssertContains(source, "IndoorPauseReconcileService", "a session singleton reconciles paused farms");
            AssertContains(source, "ReconcilePausedEnterers", "a paused farm exposes a reconcile entry point");
            AssertContains(source, "UpdateSingleton", "the reconcile service runs once per update tick");
            AssertContains(source, "OnEntererAddedWhilePaused", "a walk-in during the pause is ejected immediately");
            AssertContains(source, "BindEntererAdded", "the enterer-added guard is bound with the pause integration");
            AssertContains(source, "PausedResidualEnterer", "residual enterers are reported");
            AssertContains(source, "PausedReconcile", "the reconcile pass is reported");

            TestAssert.True(IndoorLogKeys.PausedReconcile.StartsWith("indoor.", StringComparison.Ordinal),
                "indoor.paused.reconcile must keep the indoor.* log-key namespace");
            TestAssert.True(IndoorLogKeys.PausedEjectSummary.StartsWith("indoor.", StringComparison.Ordinal));
            TestAssert.True(IndoorLogKeys.PausedEjectFailed.StartsWith("indoor.", StringComparison.Ordinal));
            TestAssert.True(IndoorLogKeys.PausedResidualEnterer.StartsWith("indoor.", StringComparison.Ordinal));
        }

        public static void TestPauseIntegrationIsWiredIntoTheControllerLifecycle()
        {
            var activation = ReadSourceEnding("HdbfBoxFarmController.Activation.cs");
            AssertContains(activation, "BindPauseState();", "activation binds the pause event");
            AssertContains(activation, "SyncPauseStateOnActivate();", "activation applies a restored paused state");
            AssertContains(activation, "UnbindPauseState();", "deactivation unbinds the pause event");
            // L-13 (2026-11-02): losing the finished state must eject before the vanilla cleanup runs.
            AssertContains(activation, "_pauseReconcile.Unregister(this);",
                "deactivation stops the paused-farm reconcile");
            AssertContains(activation, "EjectBeaversFromPausedFarm(\"deactivate\")",
                "deactivation ejects a residual enterer (L-13)");

            var controller = ReadSourceEnding("HdbfBoxFarmController.cs");
            AssertContains(controller, "ResolvePauseComponents();", "Awake resolves the pause components");
            AssertContains(controller, "_pauseReconcile", "the controller holds the reconcile singleton");

            var configurator = ReadSourceEnding("HdbfGameConfigurator.cs");
            AssertContains(configurator, "Bind<IndoorPauseReconcileService>().AsSingleton();",
                "the reconcile singleton is bound in the game context");
        }

        public static void TestPauseLogKeysFollowTheIndoorNamespace()
        {
            TestAssert.True(IndoorLogKeys.Paused.StartsWith("indoor.", StringComparison.Ordinal),
                "indoor.paused must keep the indoor.* log-key namespace, got " + IndoorLogKeys.Paused);
            TestAssert.True(IndoorLogKeys.Resumed.StartsWith("indoor.", StringComparison.Ordinal),
                "indoor.resumed must keep the indoor.* log-key namespace, got " + IndoorLogKeys.Resumed);
        }

        public static void TestPauseIntegrationStaysHarmonyFree()
        {
            var source = ReadControllerSources();
            TestAssert.False(source.Contains("Harmony", StringComparison.Ordinal),
                "the pause integration is content-only: no Harmony patch may be added for it");
        }

        // ------------------------------------------------------------------ helpers

        private static string ReadPauseSource() => ReadSourceEnding("HdbfBoxFarmController.Pause.cs");

        private static string ReadControllerSources()
        {
            var builder = new StringBuilder();
            foreach (var resource in ResourceNames())
            {
                if (resource.StartsWith(ResourcePrefix + "Controller.", StringComparison.Ordinal))
                {
                    builder.Append(ReadResource(resource)).Append('\n');
                }
            }

            return builder.ToString();
        }

        private static string ReadSourceEnding(string suffix)
        {
            foreach (var resource in ResourceNames())
            {
                if (resource.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return ReadResource(resource);
                }
            }

            throw new TestFailureException("embedded source not found: " + suffix);
        }

        private static List<string> ResourceNames()
        {
            var result = new List<string>();
            foreach (var name in typeof(V2PauseContractTests).Assembly.GetManifestResourceNames())
            {
                if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        private static string ReadResource(string name)
        {
            using var stream = typeof(V2PauseContractTests).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                throw new TestFailureException("embedded resource not found: " + name);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void AssertContains(string source, string token, string what)
        {
            TestAssert.True(source.Contains(token, StringComparison.Ordinal),
                "pause contract: the controller must " + what + " (missing '" + token + "')");
        }
    }
}
