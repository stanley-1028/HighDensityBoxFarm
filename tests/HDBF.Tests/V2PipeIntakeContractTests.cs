// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-INTEG-01
// Purpose: Contract of the 2026-11-07 farm-adjacent pipe intake: the additive Core footprint
//          contract + pure neighbour math (real behaviour, Core only) and the source-token
//          contract that Gameplay and Timber Pipes Integration publish/draw through verified
//          anchors only, bounded and read-only in the probe.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-05, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the farm-adjacent pipe intake (player report 2026-11-07): a Timber Pipes water
    /// pipe attached to the farm shell must supply the farm. The Core contract and its pure neighbour
    /// math run for real here; the HDBF.Gameplay/HDBF.Integration halves are read as sources because
    /// those assemblies reference Timberborn and cannot load in this test host.
    /// </summary>
    public static class V2PipeIntakeContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string CoreAnchorFile = "src/HDBF.Core/Indoor/IIndoorPipeWaterAnchor.cs";
        private const string GameplayIntakeFile = "src/HDBF.Gameplay/Components/HdbfBoxFarmController.PipeIntake.cs";
        private const string BindingIntakeFile = "src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Intake.cs";
        private const string BindingAnchorsFile = "src/HDBF.Integration/Adapters/TimberPipesWaterBinding.PipeAnchors.cs";
        private const string TankConnectionFile =
            "src/HDBF.Integration/Adapters/TimberPipesWaterBinding.TankConnection.cs";
        private const string SourceIntakeFile = "src/HDBF.Integration/Adapters/TimberPipesWaterSource.Intake.cs";
        private const string HostFile = "src/HDBF.Integration/Adapters/IndoorAdapterHost.cs";
        private const string ContextFile = "src/HDBF.Gameplay/Components/HdbfBoxFarmController.Context.cs";
        private const string WaterFlowFile = "src/HDBF.Gameplay/Components/HdbfBoxFarmController.WaterFlow.cs";

        /// <summary>
        /// The footprint companion is additive exactly like <c>IIndoorPipeProbeAnchor</c>: one method,
        /// a Core value-type list, not a new frozen host member and no game type in Core.
        /// </summary>
        public static void TestIndoorPipeWaterAnchorContractIsAdditiveAndCoreOnly()
        {
            var contract = typeof(IIndoorPipeWaterAnchor);
            TestAssert.Equal(1, contract.GetMethods().Length,
                "IIndoorPipeWaterAnchor has exactly one method");
            var method = contract.GetMethod("SetWaterAnchor");
            TestAssert.NotNull(method, "IIndoorPipeWaterAnchor must expose SetWaterAnchor");
            TestAssert.Equal(1, method!.GetParameters().Length, "SetWaterAnchor takes the footprint");
            TestAssert.True(method.GetParameters()[0].ParameterType == typeof(IReadOnlyList<PipeCell>),
                "the footprint must be a Core IReadOnlyList<PipeCell>, no game type");
            TestAssert.True(typeof(PipeCell).IsValueType, "PipeCell must stay a Core value type");
            foreach (var axis in new[] { "X", "Y", "Z" })
            {
                var property = typeof(PipeCell).GetProperty(axis);
                TestAssert.NotNull(property, "PipeCell must expose " + axis);
                TestAssert.True(property!.PropertyType == typeof(int), "PipeCell." + axis + " must be int");
            }

            TestAssert.False(typeof(IIndoorAdapterHost).IsAssignableFrom(contract),
                "the footprint stays a separate optional contract, not a new IIndoorAdapterHost member");
            var coreSource = ReadRepoFile(CoreAnchorFile);
            foreach (var forbidden in new[] { "Timberborn", "UnityEngine", "TimberPipes", "Harmony" })
            {
                TestAssert.False(coreSource.Contains(forbidden, StringComparison.Ordinal),
                    "the Core footprint contract must not mention " + forbidden);
            }
        }

        /// <summary>A 3x3x1 farm footprint has exactly 30 face neighbours (9 top + 9 bottom + 12 side).</summary>
        public static void TestFarmFootprintExpandsToThirtyNeighbourCells()
        {
            var footprint = FarmFootprint();
            var neighbors = PipeIntakeMath.NeighborCells(footprint);
            TestAssert.Equal(30, neighbors.Length, "a 3x3x1 farm has 30 unique face neighbours");
            AssertNoOverlap(footprint, neighbors);
            TestAssert.Equal(neighbors.Length, neighbors.Distinct().Count(), "neighbours must be unique");
            var sorted = neighbors.OrderBy(cell => cell.Z).ThenBy(cell => cell.Y).ThenBy(cell => cell.X);
            TestAssert.True(neighbors.SequenceEqual(sorted), "the neighbour order must be deterministic");

            var single = PipeIntakeMath.NeighborCells(new[] { new PipeCell(0, 0, 0) });
            TestAssert.Equal(6, single.Length, "one cell has its six faces as neighbours");

            var two = PipeIntakeMath.NeighborCells(new[] { new PipeCell(0, 0, 0), new PipeCell(1, 0, 0) });
            TestAssert.Equal(10, two.Length, "two adjacent cells share their mutual face");
        }

        /// <summary>Neighbour math answers are safe on null/empty input (never null, never throws).</summary>
        public static void TestFarmFootprintMathIsSafeOnEmptyInput()
        {
            TestAssert.NotNull(PipeIntakeMath.NeighborCells(null), "null footprint answers an empty array");
            TestAssert.Equal(0, PipeIntakeMath.NeighborCells(null).Length);
            TestAssert.Equal(0, PipeIntakeMath.NeighborCells(Array.Empty<PipeCell>()).Length);
        }

        /// <summary>
        /// Gameplay reads the game object's own positioned blocks (world coordinates, rotation/flip
        /// already applied), publishes the footprint before the pipe query, and caches it by origin.
        /// </summary>
        public static void TestGameplayPublisherPublishesThePositionedBlocksFootprint()
        {
            var gameplay = ReadRepoFile(GameplayIntakeFile);
            foreach (var token in new[]
                     {
                         "PositionedBlocks", "GetOccupiedCoordinates()", "IIndoorPipeWaterAnchor",
                         "SetWaterAnchor(ResolveWaterFootprint())", "_waterFootprintOrigin", "catch (Exception",
                     })
            {
                TestAssert.True(gameplay.Contains(token, StringComparison.Ordinal),
                    "Gameplay footprint publisher is missing: " + token);
            }

            var lines = gameplay.Replace("\r\n", "\n").Split('\n');
            TestAssert.True(lines.Length <= 301, "the Gameplay footprint publisher must stay <= 300 lines");
            TestAssert.True(gameplay.StartsWith(
                    "// ============================================================================", StringComparison.Ordinal),
                "the Gameplay footprint publisher carries the fixed v2 header banner");
            foreach (var forbidden in new[]
                     { "Time.deltaTime", "TickableComponent", "void Tick(", "void Update()", "static List<" })
            {
                TestAssert.False(gameplay.Contains(forbidden, StringComparison.Ordinal),
                    "the footprint publisher must not run per frame: " + forbidden);
            }

            var context = ReadRepoFile(ContextFile);
            var publish = context.IndexOf("PublishWaterAnchor();", StringComparison.Ordinal);
            var query = context.IndexOf("_adapterHost.IsPipeConnected", StringComparison.Ordinal);
            TestAssert.True(publish >= 0 && query >= 0 && publish < query,
                "ReadPipeWater must publish the footprint before IsPipeConnected");

            var waterFlow = ReadRepoFile(WaterFlowFile);
            TestAssert.True(MethodText(waterFlow, "private void PublishProbeAnchor()")
                    .Contains("PublishWaterAnchor();", StringComparison.Ordinal),
                "the panel snapshot must publish the footprint before the read-only probe");
        }

        /// <summary>The host forwards the footprint to the water source without drawing or failing.</summary>
        public static void TestAdapterHostForwardsTheWaterAnchorWithoutDrawing()
        {
            var host = ReadRepoFile(HostFile);
            TestAssert.True(host.Contains("IIndoorPipeWaterAnchor", StringComparison.Ordinal),
                "IndoorAdapterHost must implement the optional footprint companion");
            var body = MethodText(host, "public void SetWaterAnchor(IReadOnlyList<PipeCell>? footprint)");
            TestAssert.True(body.Contains(".SetIntakeFootprint(footprint)", StringComparison.Ordinal),
                "the host must forward the footprint to the Timber Pipes water source");
            TestAssert.True(body.Contains("catch (Exception", StringComparison.Ordinal),
                "the forwarding must never throw into the day tick/panel");
            foreach (var forbidden in new[] { "TryDraw", "ReportRuntimeFailure", "Fail(" })
            {
                TestAssert.False(body.Contains(forbidden, StringComparison.Ordinal),
                    "the footprint setter must not draw or report failures: " + forbidden);
            }
        }

        /// <summary>
        /// The intake draw is bounded by the request, removes real pipe fluid through the verified
        /// <c>RemoveFluid</c> only, accounts for the fluid really removed, is exception-isolated and
        /// its read-only census never draws. The tank network test walks ports to a transport pipe.
        /// </summary>
        public static void TestAdjacentPipeIntakeIsBoundedGuardedAndReadOnly()
        {
            var intake = ReadRepoFile(BindingIntakeFile);
            foreach (var token in new[]
                     {
                         "var takeM3 = requestedVolumeM3 - drawnVolumeM3;",
                         "if (takeM3 > beforeM3)",
                         "_removeFluid!.Invoke(pipe, new object[] { takeM3 });",
                         "var removedM3 = beforeM3 - afterM3;",
                         "catch (Exception",
                     })
            {
                TestAssert.True(intake.Contains(token, StringComparison.Ordinal),
                    "bounded adjacent-pipe draw is missing: " + token);
            }

            TestAssert.False(intake.Contains("ApplyVolume", StringComparison.Ordinal),
                "the adjacent-pipe path may not use the tank ApplyVolume path");
            var census = MethodText(intake, "internal bool TryReadIntake(");
            foreach (var forbidden in new[]
                     { "RemoveFluid", "_removeFluid", "ApplyVolume", "FailDraw", "ReportRuntimeFailure",
                         "TryDraw(" })
            {
                TestAssert.False(census.Contains(forbidden, StringComparison.Ordinal),
                    "the read-only intake census must not draw: " + forbidden);
            }

            var anchors = ReadRepoFile(BindingAnchorsFile);
            foreach (var anchor in new[]
                     {
                         "\"TryGetPipe\"", "\"get_IsTransportPipe\"", "\"get_NetworkGoodId\"",
                         "\"get_FluidHeight\"", "\"RemoveFluid\"", "\"TimberPipes.Models.PipePort\"",
                     })
            {
                TestAssert.True(anchors.Contains(anchor, StringComparison.Ordinal),
                    "verified intake anchor missing: " + anchor);
            }

            var sourceIntake = ReadRepoFile(SourceIntakeFile);
            foreach (var token in new[]
                     {
                         "internal void SetIntakeFootprint(IReadOnlyList<PipeCell>? footprint)",
                         "PipeIntakeMath.NeighborCells(footprint)", "HasIntakeConnection()", "HasIntakeWater()",
                         "TryDrawIntake(", "catch (Exception",
                     })
            {
                TestAssert.True(sourceIntake.Contains(token, StringComparison.Ordinal),
                    "the source intake half is missing: " + token);
            }

            var tankConnection = ReadRepoFile(TankConnectionFile);
            foreach (var token in new[]
                     {
                         "PipeHasTransportLink(", "_getIsTransportPipe", "catch (Exception",
                     })
            {
                TestAssert.True(tankConnection.Contains(token, StringComparison.Ordinal),
                    "the tank transport-link check is missing: " + token);
            }

            foreach (var token in new[] { "\"get_Ports\"", "\"get_IsConnected\"", "\"TryGetConnectedBuilding\"" })
            {
                TestAssert.True(anchors.Contains(token, StringComparison.Ordinal),
                    "the tank network anchors are missing: " + token);
            }

            var tank = ReadRepoFile("src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Tank.cs");
            TestAssert.True(tank.Contains("graph is not null || PipeHasTransportLink(pipe)", StringComparison.Ordinal),
                "the tank filter must accept a real transport-pipe link, not only Pipe.Graph");
        }

        /// <summary>
        /// Decision 2026-11-07: the farm-adjacent intake is reported as <c>Connected</c> (the frozen
        /// reason code), so the panel contract stays 0-8 and no new localization key is required.
        /// </summary>
        public static void TestPipeProbeReasonSetStaysFrozen()
        {
            TestAssert.Equal(0, (int)PipeProbeReason.Unknown);
            TestAssert.Equal(1, (int)PipeProbeReason.Connected);
            TestAssert.Equal(2, (int)PipeProbeReason.NoPipeNetwork);
            TestAssert.Equal(3, (int)PipeProbeReason.NoPipeTank);
            TestAssert.Equal(4, (int)PipeProbeReason.TankDry);
            TestAssert.Equal(5, (int)PipeProbeReason.AdapterUnavailable);
            TestAssert.Equal(6, (int)PipeProbeReason.BindingFailed);
            TestAssert.Equal(7, (int)PipeProbeReason.PortOff);
            TestAssert.Equal(8, (int)PipeProbeReason.GlobalSupply);
        }

        // ------------------------------------------------------------------ helpers

        private static IReadOnlyList<PipeCell> FarmFootprint()
        {
            var cells = new List<PipeCell>();
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    cells.Add(new PipeCell(x, y, 0));
                }
            }

            return cells;
        }

        private static void AssertNoOverlap(IReadOnlyList<PipeCell> footprint, IReadOnlyList<PipeCell> neighbors)
        {
            var occupied = new HashSet<PipeCell>(footprint);
            for (var index = 0; index < neighbors.Count; index++)
            {
                TestAssert.False(occupied.Contains(neighbors[index]),
                    "a neighbour cell may not be an occupied farm cell");
            }
        }

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
            var metadata = typeof(V2PipeIntakeContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException("assembly metadata \"" + RepoRootKey + "\" is missing");
            }

            return metadata.Value!;
        }
    }
}
