// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-INTEG-01
// Purpose: Source/text contract of the v2 indoor adaptor host and its Timber Pipes water
//          source: frozen Core interfaces, verified real anchors, guarded reflection and the
//          degradation paths (no Harmony, no v1 plot/soil calls, no per-frame water draw).
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01..03, REQ-V2-SAFE-05, REQ-V2-TEST-01
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the v2 adaptor host (INTERFACES.md section 6, section 19.13). HDBF.Integration
    /// references Timberborn and Bindito and cannot be loaded here, so the shipped sources are
    /// embedded as text by HDBF.Tests.csproj and pinned exactly like the ADR-009 patch contract.
    /// The test assembly references only HDBF.Core (REQ-V2-TEST-01, no game dependency).
    /// </summary>
    public static class V2AdapterHostContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string HostResource = "HDBF.Tests.Source.IndoorAdapterHost.cs";
        private const string SourceResource = "HDBF.Tests.Source.TimberPipesWaterSource.cs";
        private const string BindingResource = "HDBF.Tests.Source.TimberPipesWaterBinding.cs";
        private const string InterfaceResource = "HDBF.Tests.Source.IIndoorAdapterHost.cs";
        private const string TankFile = "src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Tank.cs";
        private const string RegistryFile = "src/HDBF.Integration/Adapters/CompatAdapterRegistry.IndoorHost.cs";

        private static readonly string[] HostSources = { HostResource, SourceResource, BindingResource, InterfaceResource };

        public static void TestAdapterHostSourcesAreEmbedded()
        {
            foreach (var resource in HostSources)
            {
                TestAssert.True(ReadResource(resource).Length > 0, "embedded source missing: " + resource);
            }
        }

        public static void TestIndoorAdapterHostImplementsTheFrozenContract()
        {
            var host = ReadResource(HostResource);
            TestAssert.True(host.Contains("public sealed class IndoorAdapterHost : IIndoorAdapterHost", StringComparison.Ordinal),
                "the host must implement HDBF.Core.Indoor.IIndoorAdapterHost");
            foreach (var member in new[] { "public IWaterSource? PipeWaterSource", "public bool IsPipeConnected",
                         "public bool IsLogisticsConnected", "public HdbfAdapterHealth PipeHealth" })
            {
                TestAssert.True(host.Contains(member, StringComparison.Ordinal),
                    "IIndoorAdapterHost member missing from the host: " + member);
            }

            var source = ReadResource(SourceResource);
            TestAssert.True(source.Contains("public sealed class TimberPipesWaterSource : IWaterSource", StringComparison.Ordinal),
                "the pipe water source must implement IWaterSource");
            TestAssert.True(source.Contains("public WaterSupplyKind Kind => WaterSupplyKind.Pipe;", StringComparison.Ordinal),
                "the pipe source kind must be WaterSupplyKind.Pipe");
            TestAssert.True(source.Contains("public bool IsAvailable", StringComparison.Ordinal));
            TestAssert.True(source.Contains("public bool TryDraw(float amount, out float actual)", StringComparison.Ordinal),
                "TryDraw must be the atomic draw operation");
        }

        public static void TestCoreInterfaceIsGameTypeFreeAndFrozen()
        {
            var contract = typeof(IIndoorAdapterHost);
            TestAssert.Equal(4, contract.GetProperties().Length, "the frozen interface has four members");
            TestAssert.True(contract.GetProperty("PipeWaterSource")!.PropertyType == typeof(IWaterSource),
                "PipeWaterSource must be the Core IWaterSource contract");
            TestAssert.True(contract.GetProperty("IsPipeConnected")!.PropertyType == typeof(bool));
            TestAssert.True(contract.GetProperty("IsLogisticsConnected")!.PropertyType == typeof(bool));
            TestAssert.True(contract.GetProperty("PipeHealth")!.PropertyType == typeof(HdbfAdapterHealth));

            foreach (var property in contract.GetProperties())
            {
                var assembly = property.PropertyType.Assembly;
                TestAssert.True(assembly == contract.Assembly || assembly == typeof(int).Assembly,
                    "Core adaptor contract must not expose a game/external type: " + property.PropertyType);
            }

            AssertLacks("the Core interface", ReadResource(InterfaceResource), "Timberborn", "UnityEngine", "TimberPipes", "Harmony");
        }

        public static void TestNoHarmonyAndNoV1PlotOrSoilCalls()
        {
            AssertLacks("v2 adaptor code", ReadAllSources(), "Harmony", "Transpiler", "PlantingService",
                "NaturalResourceFactory", "SetField", "PlotRegistry", "PlotId", "SoilMoisture", "ITerrainService", "PlantNew");
        }

        public static void TestEveryReflectedWaterCallIsGuarded()
        {
            var binding = ReadResource(BindingResource);
            TestAssert.True(CountOccurrences(binding, "catch (Exception") >= 5,
                "every reflection entry point of the binding must catch its own failures");
            TestAssert.True(Regex.Matches(binding, "(?m)^\\s*try\\s*$").Count >= 5,
                "the binding needs guarded entry points");
            var tank = ReadRepoFile(TankFile);
            foreach (var method in new[] { "TryBind()", "TryResolveRegistry()", "TryGetTanks(", "ReadWaterGoodId(",
                         "TryDescribeTank(", "TryDrawVolume(" })
            {
                TestAssert.True((binding + tank).Contains(method, StringComparison.Ordinal), "guarded step missing: " + method);
            }

            TestAssert.True(CountOccurrences(tank, "catch (Exception") >= 2, "tank read and drain must each be isolated");
        }

        public static void TestDegradedPathsAnswerNullAndFalse()
        {
            var host = ReadResource(HostResource);
            TestAssert.True(CountOccurrences(host, "return null;") >= 1,
                "a missing/disabled pipe adaptor must hand out no water source");
            TestAssert.True(host.Contains("HdbfAdapterHealth.Ok", StringComparison.Ordinal) &&
                            host.Contains("HdbfAdapterHealth.Degraded", StringComparison.Ordinal),
                "the host must only serve Ok/Degraded and degrade on Failed/Disabled/Unknown");

            var source = ReadResource(SourceResource);
            TestAssert.True(CountOccurrences(source, "return false;") >= 5, "every failed pipe query/draw must answer false");
            TestAssert.True(source.Contains("actual = 0f;", StringComparison.Ordinal), "a failed draw must move no water");
            TestAssert.True(source.Contains("manual refill", StringComparison.Ordinal),
                "the degraded path must document the manual-refill fallback");
            TestAssert.True(source.Contains("IndoorLogKeys.CompatDegraded", StringComparison.Ordinal),
                "failures must be reported under a throttled compat.* key");
        }

        public static void TestTryDrawIsRequestBoundedAndNotAPerFrameTicker()
        {
            var all = ReadAllSources() + ReadRepoFile(TankFile);
            AssertLacks("the water source", all, "void Tick", "void Update", "ITickable", "IUpdatable",
                "DeltaTime", "deltaTime", "Time.deltaTime");

            var source = ReadResource(SourceResource);
            TestAssert.True(source.Contains("var wantedM3 = amount * scale;", StringComparison.Ordinal),
                "the draw request must be the requested amount, not a hoarded buffer");
            TestAssert.True(source.Contains("var needM3 = wantedM3 - drawnM3;", StringComparison.Ordinal),
                "one request may not draw more than what is still requested");
            TestAssert.True(source.Contains("actual = drawnM3 / scale;", StringComparison.Ordinal),
                "actual must be the volume really removed from the pipe network");

            var tank = ReadRepoFile(TankFile);
            TestAssert.True(tank.Contains("var drawVolume = requestedVolumeM3 < before ? requestedVolumeM3 : before;",
                StringComparison.Ordinal), "one tank draw is clamped to the requested volume");
            TestAssert.True(tank.Contains("_applyVolume.Invoke(tank, new object[] { target, _waterGoodId });",
                StringComparison.Ordinal), "water may only be removed through the mod's public ApplyVolume");
        }

        public static void TestWaterSourceUsesOnlyVerifiedRealAnchors()
        {
            var binding = ReadResource(BindingResource);
            foreach (var anchor in new[] { "\"TimberPipes.Services.PipeRegistry\"", "\"TimberPipes.Components.PipeTank\"",
                         "\"TimberPipes.Components.BuildingPipe\"", "\"TimberPipes.Models.PipeFlowSolver\"",
                         "\"TimberPipes.Models.PipeFluids\"", "\"get_Tanks\"", "\"get_VolumeM3\"", "\"ApplyVolume\"",
                         "\"TakesGood\"", "\"get_Graph\"", "\"get_IsFinished\"", "\"get_FluidGoodId\"", "\"get_Pipe\"",
                         "\"GoodsToVolume\"", "\"WaterGoodId\"" })
            {
                TestAssert.True(binding.Contains(anchor, StringComparison.Ordinal), "verified anchor missing: " + anchor);
            }

            TestAssert.True(binding.Contains("new[] { \"System.Single\", \"System.String\" }, \"System.Void\"",
                StringComparison.Ordinal), "ApplyVolume(float,string) signature must be pinned");
            AssertLacks("the binding", binding, "TimberPipeNetwork", "WaterStorage", "PipeNetwork");
        }

        public static void TestAdapterHostIsBoundThroughDi()
        {
            var configurator = ReadRepoFile("src/HDBF.Integration/HdbfIntegrationConfigurator.cs");
            TestAssert.True(configurator.Contains("Bind<IndoorAdapterHost>().AsSingleton();", StringComparison.Ordinal),
                "the concrete host must be a session singleton");
            TestAssert.True(configurator.Contains("Bind<IIndoorAdapterHost>().ToExisting<IndoorAdapterHost>();",
                StringComparison.Ordinal), "Gameplay/UI must resolve the Core contract to the same host instance");
            TestAssert.True(configurator.Contains("using HDBF.Core.Indoor;", StringComparison.Ordinal),
                "the configurator binds the Core indoor contract, not an Integration type");
        }

        public static void TestRuntimeFailuresReuseTheThreeStrikeStateMachine()
        {
            var registry = ReadRepoFile(RegistryFile);
            TestAssert.True(registry.Contains("public HdbfAdapterHealth GetAdapterHealth(string? adapterId)",
                StringComparison.Ordinal), "health must come from the existing adapter state machine");
            TestAssert.True(registry.Contains("public void ReportRuntimeFailure(string? adapterId, string reason)",
                StringComparison.Ordinal), "runtime water failures must feed the same three-strike rule");
            TestAssert.True(registry.Contains("NoteRuntimeFailure(reason)", StringComparison.Ordinal),
                "the registry must delegate to the adapter's existing failure counter");

            var adapter = ReadRepoFile("src/HDBF.Integration/Adapters/ReflectionCompatAdapter.cs");
            TestAssert.True(adapter.Contains("internal void NoteRuntimeFailure(string reason)", StringComparison.Ordinal));
            TestAssert.True(adapter.Contains("RecordFailure(reason, null)", StringComparison.Ordinal),
                "runtime failures must reuse RecordFailure/CompatStateMachine.Fail");

            var host = ReadResource(HostResource);
            TestAssert.True(host.Contains("ReportRuntimeFailure(CompatAnchorTable.TimberPipesId", StringComparison.Ordinal),
                "the host must report water failures against the pipe adapter");
            AssertLacks("the host", host, "new CompatStateMachine");
            TestAssert.True(host.Contains("IsPortAvailable(HdbfPortKind.Logistics)", StringComparison.Ordinal),
                "the logistics port state must come from the existing port query");
        }

        public static void TestIntegrationAssemblyDoesNotReferenceGameplay()
        {
            var csproj = ReadRepoFile("src/HDBF.Integration/HDBF.Integration.csproj");
            var references = Regex.Matches(csproj, "<ProjectReference Include=\"([^\"]+)\"");
            TestAssert.Equal(1, references.Count, "the assembly graph allows at most one ProjectReference");
            TestAssert.True(references[0].Groups[1].Value.EndsWith("HDBF.Core.csproj", StringComparison.Ordinal),
                "HDBF.Integration must reference HDBF.Core only");
            AssertLacks("HDBF.Integration.csproj", csproj, "HDBF.Gameplay", "HDBF.Persistence", "HDBF.UI", "TimberPipes.dll");
        }

        public static void TestAdapterHostHasNoStaticMutableState()
        {
            var all = ReadAllSources() + ReadRepoFile(TankFile);
            var mutableStatic = Regex.Matches(all, @"(?m)^\s*(?:private|internal|public|protected)\s+static\s+(?!readonly|const)");
            TestAssert.Equal(0, mutableStatic.Count,
                "the adaptor host may only have const/readonly static fields (REQ-V2-MAINT-04)");
            TestAssert.True(ReadResource(BindingResource).Contains("private static readonly string[] NoParameters",
                StringComparison.Ordinal), "the only static field is the immutable empty parameter list");
        }

        // ------------------------------------------------------------------ helpers

        private static void AssertLacks(string what, string text, params string[] forbidden)
        {
            foreach (var token in forbidden)
            {
                TestAssert.False(text.Contains(token, StringComparison.Ordinal), what + " must not contain: " + token);
            }
        }

        private static string ReadAllSources()
        {
            var builder = new StringBuilder();
            foreach (var resource in HostSources)
            {
                builder.Append(ReadResource(resource)).Append('\n');
            }

            return builder.ToString();
        }

        private static string ReadResource(string name)
        {
            using var stream = typeof(V2AdapterHostContractTests).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                throw new TestFailureException("embedded resource not found: " + name);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static string ReadRepoFile(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new TestFailureException("repository file not found: " + path);
            }

            return File.ReadAllText(path);
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2AdapterHostContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootMetadataKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException(
                    "assembly metadata \"" + RepoRootMetadataKey + "\" is missing from HDBF.Tests.csproj");
            }

            return metadata.Value!;
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            for (var index = text.IndexOf(needle, StringComparison.Ordinal);
                 index >= 0;
                 index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
