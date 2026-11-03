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
using HDBF.Core.Compat;
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
        private const string ProbeFile = "src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Probe.cs";
        private const string FarmGraphFile = "src/HDBF.Integration/Adapters/TimberPipesWaterBinding.FarmGraph.cs";
        private const string ProbeSourceFile = "src/HDBF.Integration/Adapters/TimberPipesWaterSource.Probe.cs";
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
            TestAssert.True(source.Contains("public sealed partial class TimberPipesWaterSource : IWaterSource",
                    StringComparison.Ordinal),
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
            // L-02(c): the production caller supplies the monotonic timestamp, so the Core window is
            // really used in game (a single failed draw is Degraded, not session-wide Failed).
            TestAssert.True(adapter.Contains("_state.Fail(reason, timestampSeconds)", StringComparison.Ordinal),
                "the production failure path must pass the caller clock into the state machine");
            TestAssert.True(adapter.Contains("Stopwatch.GetTimestamp()", StringComparison.Ordinal),
                "the caller clock must be the process monotonic Stopwatch, not Unity render time");
            TestAssert.True(adapter.Contains("double.IsNaN(timestampSeconds)", StringComparison.Ordinal),
                "a missing clock must fall back to the legacy timestampless rule");

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

        // ------------------------------------------- WP-05/RE-05: read-only pipe probe

        public static void TestPipeProbeContractIsFrozenAndReadOnly()
        {
            var contract = typeof(IIndoorPipeProbe);
            TestAssert.Equal(1, contract.GetMethods().Length, "IIndoorPipeProbe has exactly one method");
            var probeMethod = contract.GetMethod("ProbePipe");
            TestAssert.NotNull(probeMethod, "IIndoorPipeProbe must expose ProbePipe()");
            TestAssert.True(probeMethod!.ReturnType == typeof(PipeProbe),
                "ProbePipe must return the immutable PipeProbe DTO");
            TestAssert.Equal(0, probeMethod.GetParameters().Length, "ProbePipe takes no arguments");
            TestAssert.False(typeof(IIndoorAdapterHost).IsAssignableFrom(typeof(IIndoorPipeProbe)),
                "the probe is a separate optional contract, not a new IIndoorAdapterHost member");

            TestAssert.True(typeof(PipeProbe).IsValueType,
                "PipeProbe must stay a value type (readonly record struct)");
            TestAssert.Equal(0, (int)PipeProbeReason.Unknown, "reason numeric values are the panel contract");
            TestAssert.Equal(1, (int)PipeProbeReason.Connected);
            TestAssert.Equal(2, (int)PipeProbeReason.NoPipeNetwork);
            TestAssert.Equal(3, (int)PipeProbeReason.NoPipeTank);
            TestAssert.Equal(4, (int)PipeProbeReason.TankDry);
            TestAssert.Equal(5, (int)PipeProbeReason.AdapterUnavailable);
            TestAssert.Equal(6, (int)PipeProbeReason.BindingFailed);
            TestAssert.Equal(7, (int)PipeProbeReason.PortOff);
            TestAssert.Equal(8, (int)PipeProbeReason.GlobalSupply,
                "WP-05/L-02(c): the settlement-wide supply reason comes after the frozen values");

            // WP-05/L-02(c): the farm position travels through a separate optional contract, so the
            // frozen one-method probe above stays unchanged for every existing implementer.
            var anchorContract = typeof(IIndoorPipeProbeAnchor);
            TestAssert.Equal(1, anchorContract.GetMethods().Length,
                "IIndoorPipeProbeAnchor has exactly one method");
            TestAssert.True(anchorContract.GetMethod("SetProbeAnchor") is not null,
                "IIndoorPipeProbeAnchor must expose SetProbeAnchor(PipeProbeAnchor?)");
            var anchorMethod = anchorContract.GetMethod("SetProbeAnchor")!;
            TestAssert.Equal(1, anchorMethod.GetParameters().Length, "SetProbeAnchor takes the position");
            TestAssert.True(anchorMethod.GetParameters()[0].ParameterType == typeof(PipeProbeAnchor?),
                "the anchor must be a nullable Core value type (no game type crosses the boundary)");
            TestAssert.True(typeof(PipeProbeAnchor).IsValueType,
                "PipeProbeAnchor must stay a Core value type");
            TestAssert.False(typeof(IIndoorAdapterHost).IsAssignableFrom(typeof(IIndoorPipeProbeAnchor)),
                "the anchor stays a separate optional contract, not a new IIndoorAdapterHost member");

            var unknown = PipeProbe.Unknown;
            TestAssert.Equal(PipeProbeReason.Unknown, unknown.Reason, "the default probe answers Unknown");
            TestAssert.Equal(0, unknown.TankCount);
            TestAssert.Equal(0, unknown.WaterTankCount);
            TestAssert.Equal(string.Empty, unknown.Detail, "a probed Detail is never null");
        }

        public static void TestTimberPipesWaterSourceReportsFourDistinctProbeStates()
        {
            var source = ReadResource(SourceResource);
            TestAssert.True(source.Contains(
                    "public sealed partial class TimberPipesWaterSource : IWaterSource, IIndoorPipeProbe",
                    StringComparison.Ordinal), "the pipe source must implement the read-only probe contract");
            var probeSource = ReadRepoFile(ProbeSourceFile);
            TestAssert.True(probeSource.Contains("public PipeProbe ProbePipe()", StringComparison.Ordinal),
                "the pipe source owns ProbePipe()");
            TestAssert.True(source.Contains("IIndoorPipeProbeAnchor", StringComparison.Ordinal),
                "the pipe source must accept the optional farm anchor (WP-05/L-02(c))");
            TestAssert.True(probeSource.Contains("IndoorLogKeys.PipeProbe", StringComparison.Ordinal),
                "the probe line must use the registered water.pipe-probe throttle key");

            var probeFile = ReadRepoFile(ProbeFile);
            var probeText = probeSource + "\n" + probeFile;
            // Census precedence inside ProbeTanks: binding failure -> no network tank -> no water
            // tank -> dry -> connected. AdapterUnavailable and GlobalSupply are decided separately
            // (before/after the census), so they are pinned by presence instead of text order.
            var censusReasons = new[]
            {
                "PipeProbeReason.NoPipeNetwork", "PipeProbeReason.NoPipeTank",
                "PipeProbeReason.TankDry", "PipeProbeReason.Connected",
            };
            var previous = probeFile.IndexOf("PipeProbeReason.BindingFailed", StringComparison.Ordinal);
            TestAssert.True(previous >= 0, "probe reason missing: PipeProbeReason.BindingFailed");
            foreach (var reason in censusReasons)
            {
                var index = probeFile.IndexOf(reason, StringComparison.Ordinal);
                TestAssert.True(index > previous,
                    "probe reason missing or out of precedence order: " + reason);
                previous = index;
            }

            TestAssert.True(probeSource.Contains("PipeProbeReason.AdapterUnavailable", StringComparison.Ordinal),
                "the probe must report an unavailable adaptor before the census runs");
            TestAssert.True(probeSource.Contains("PipeProbeReason.GlobalSupply", StringComparison.Ordinal),
                "the probe must report settlement-wide supply (WP-05/L-02(c))");

            foreach (var step in new[]
                     {
                         "if (!IsAdaptorServing())", "!state.IsFinished || !state.HasPipeGraph",
                         "if (pipeTanks == 0)", "if (waterTanks == 0)", "if (wateredTanks == 0)",
                         "state.VolumeM3 > 0f", "candidate.TakesWaterGood",
                     })
            {
                TestAssert.True(probeText.Contains(step, StringComparison.Ordinal),
                    "probe classification step missing: " + step);
            }
        }

        public static void TestPipeProbeNeverDrawsWaterOrFeedsTheFailureCounter()
        {
            var probeSource = ReadRepoFile(ProbeSourceFile);
            var probeFile = ReadRepoFile(ProbeFile);
            var farmGraph = ReadRepoFile(FarmGraphFile);
            var probeBody = MethodText(probeSource, "public PipeProbe ProbePipe()")
                            + MethodText(probeSource, "private PipeProbe ClassifyFarmGraph(")
                            + MethodText(probeFile, "internal PipeProbe ProbeTanks()")
                            + MethodText(farmGraph, "internal bool TryProbeFarmGraph(");
            AssertLacks("the pipe probe", probeBody, "TryDrawVolume", "ApplyVolume", "FailDraw",
                "ReportRuntimeFailure", "_reportFailure", "TryDraw(");
            AssertLacks("the probe file", probeFile, "void Tick", "void Update", "ITickable",
                "IUpdatable", "DeltaTime", "deltaTime", "Time.deltaTime");
            AssertLacks("the probe source", probeSource, "TryDraw(", "ApplyVolume", "FailDraw");
            TestAssert.True(MethodText(probeFile, "internal PipeProbe ProbeTanks()")
                    .Contains("catch (Exception", StringComparison.Ordinal),
                "the probe must be exception-isolated (REQ-V2-SAFE-01)");
            TestAssert.True(MethodText(farmGraph, "internal bool TryProbeFarmGraph(")
                    .Contains("catch (Exception", StringComparison.Ordinal),
                "the farm-graph probe must be exception-isolated (REQ-V2-SAFE-01)");
        }

        /// <summary>
        /// WP-05/L-02(c): the probe reports settlement-wide supply honestly. The farm anchor travels
        /// through the Core optional contract, the host forwards it, the binding asks the verified
        /// <c>PipeRegistry.TryGetGraph</c> anchor, and every failure keeps the census answer.
        /// </summary>
        public static void TestPipeProbeDistinguishesSettlementWideSupplyHonestly()
        {
            var probeSource = ReadRepoFile(ProbeSourceFile);
            TestAssert.True(probeSource.Contains("public void SetProbeAnchor(PipeProbeAnchor? anchor)",
                    StringComparison.Ordinal), "the source must accept the optional farm anchor");
            TestAssert.True(probeSource.Contains("PipeProbeReason.GlobalSupply", StringComparison.Ordinal),
                "the probe must report the settlement-wide reason instead of faking a local connection");
            TestAssert.True(probeSource.Contains("_binding.TryProbeFarmGraph(anchor.Value.X, anchor.Value.Y, anchor.Value.Z, out var hasGraph)",
                    StringComparison.Ordinal), "the probe must ask the farm-port graph question");
            TestAssert.True(probeSource.Contains("if (anchor is null)", StringComparison.Ordinal),
                "without a farm anchor the probe must keep the census answer");

            var binding = ReadResource(BindingResource);
            TestAssert.True(binding.Contains("\"TryGetGraph\"", StringComparison.Ordinal),
                "the binding must resolve the documented PipeRegistry.TryGetGraph anchor");
            TestAssert.True(binding.Contains("\"UnityEngine.Vector3Int\"", StringComparison.Ordinal)
                            && binding.Contains("\"TimberPipes.Models.PipeGraph\"", StringComparison.Ordinal),
                "the TryGetGraph signature must be pinned exactly like CompatAnchorTable");
            var farmGraph = ReadRepoFile(FarmGraphFile);
            TestAssert.True(farmGraph.Contains("_tryGetGraph.Invoke(_registry, arguments)",
                    StringComparison.Ordinal),
                "the farm-graph probe must invoke the pinned TryGetGraph anchor");
            TestAssert.True(farmGraph.Contains("SetFailure(\"water-farm-graph-exception\"",
                    StringComparison.Ordinal),
                "a reflection failure must degrade to the census answer and be recorded");
            TestAssert.True(farmGraph.Contains("return false;", StringComparison.Ordinal),
                "an unknown graph answer must tell the caller to fall back");

            var anchor = CompatAnchorTable.TimberPipes.ApiAnchors.FirstOrDefault(spec =>
                string.Equals(spec.MemberName, "TryGetGraph", StringComparison.Ordinal));
            TestAssert.True(anchor is not null,
                "CompatAnchorTable must keep documenting the TryGetGraph anchor the probe uses");
            TestAssert.True(anchor!.TypeName.Contains("PipeRegistry", StringComparison.Ordinal));
            Console.WriteLine("[v2-compat] WP-05: farm-graph anchor pinned; unknown answers fall back to the census");
        }

        /// <summary>WP-05/L-02(c): the host forwards the optional anchor without drawing or failing.</summary>
        public static void TestIndoorAdapterHostForwardsTheProbeAnchor()
        {
            var host = ReadResource(HostResource);
            TestAssert.True(host.Contains(
                    "public sealed class IndoorAdapterHost : IIndoorAdapterHost, IIndoorPipeProbe, IIndoorPipeProbeAnchor",
                    StringComparison.Ordinal),
                "the host must implement the optional anchor companion of the probe");
            var body = MethodText(host, "public void SetProbeAnchor(PipeProbeAnchor? anchor)");
            TestAssert.True(body.Contains(".SetProbeAnchor(anchor)", StringComparison.Ordinal),
                "the host must forward the anchor to the Timber Pipes water source");
            TestAssert.True(body.Contains("catch (Exception", StringComparison.Ordinal),
                "the anchor forwarding must never throw into the snapshot/render path");
            AssertLacks("the host anchor setter", body, "TryDraw", "ReportRuntimeFailure", "Fail(");
        }

        public static void TestIndoorAdapterHostDelegatesThePipeProbe()
        {
            var host = ReadResource(HostResource);
            TestAssert.True(host.Contains(
                    "public sealed class IndoorAdapterHost : IIndoorAdapterHost, IIndoorPipeProbe",
                    StringComparison.Ordinal),
                "the host must implement both the frozen host contract and the read-only probe");
            TestAssert.True(host.Contains("public PipeProbe ProbePipe()", StringComparison.Ordinal),
                "the host must expose ProbePipe() of the optional probe contract");

            var body = MethodText(host, "public PipeProbe ProbePipe()");
            TestAssert.True(body.Contains(".ProbePipe()", StringComparison.Ordinal),
                "the host probe must delegate to the Timber Pipes water source");
            TestAssert.True(body.Contains("PipeProbeReason.AdapterUnavailable", StringComparison.Ordinal),
                "a null source or a probe exception must answer AdapterUnavailable");
            TestAssert.True(body.Contains("catch (Exception", StringComparison.Ordinal),
                "the host probe must never throw into the panel/render path");
        }

        public static void TestDailyTickNeverOverwritesThePlayerPortChoice()
        {
            var context = ReadRepoFile("src/HDBF.Gameplay/Components/HdbfBoxFarmController.Context.cs");
            var updatePorts = MethodText(context, "private bool UpdatePorts()");
            AssertLacks("UpdatePorts (L-06)", updatePorts, "record.PipePort =", "record.LogisticsPort =");
            TestAssert.True(updatePorts.Contains("record.PowerPort = nextPower;", StringComparison.Ordinal),
                "the power port stays profile-derived (grid power is not a player toggle)");
            TestAssert.True(updatePorts.Contains("IndoorLogKeys.Port", StringComparison.Ordinal),
                "an adapter-state change still emits the once-per-day indoor.port line");

            var tick = ReadRepoFile("src/HDBF.Gameplay/Components/HdbfBoxFarmController.Tick.cs");
            TestAssert.True(tick.Contains("UpdatePorts()", StringComparison.Ordinal),
                "the daily tick still observes the live port/adapter state");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Extracts one method's source text (signature through its closing brace).</summary>
        private static string MethodText(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new TestFailureException("method not found: " + signature);
            }

            var open = source.IndexOf('{', start);
            if (open < 0)
            {
                throw new TestFailureException("method body not found: " + signature);
            }

            var depth = 0;
            for (var index = open; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(start, index - start + 1);
                    }
                }
            }

            throw new TestFailureException("unterminated method: " + signature);
        }

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
