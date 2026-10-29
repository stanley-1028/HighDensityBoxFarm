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
            AssertContains(source, "enterer.Exit()", "uses the vanilla enterer exit path (Abandon + Remove)");
            AssertContains(source, "NumberOfEnterersInside", "skips the ejection when nobody is inside");
            AssertContains(source, "try", "the ejection is failure-isolated (REQ-V2-SAFE-01)");
        }

        public static void TestPauseIntegrationIsWiredIntoTheControllerLifecycle()
        {
            var activation = ReadSourceEnding("HdbfBoxFarmController.Activation.cs");
            AssertContains(activation, "BindPauseState();", "activation binds the pause event");
            AssertContains(activation, "SyncPauseStateOnActivate();", "activation applies a restored paused state");
            AssertContains(activation, "UnbindPauseState();", "deactivation unbinds the pause event");

            var controller = ReadSourceEnding("HdbfBoxFarmController.cs");
            AssertContains(controller, "ResolvePauseComponents();", "Awake resolves the pause components");
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
