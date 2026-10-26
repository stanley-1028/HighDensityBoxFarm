using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Logging;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the verified soft-dependency contract (chapter 8.1/8.2, REQ-COMPAT-05/06/10,
    /// REQ-COMPAT-19).
    ///
    /// The five external mods are NOT loaded by this test assembly: every assertion runs against
    /// the immutable data table (<see cref="CompatAnchorTable"/>) and the pure matching helpers
    /// (<see cref="CompatMatching"/>) that the game-side detector uses. The real DLL metadata is
    /// re-proven separately by <c>tools/dev/ModAnchorCheck</c> against the installed Workshop
    /// folders (REQ-TEST-03: no game or third-party reference in tests).
    ///
    /// The "real identity" block below is the verified evidence snapshot from 2026-10-25:
    /// game 1.1.2.8, see docs/workstreams/compat-01-adapters.md for DLL paths and member signatures.
    /// </summary>
    public static class CompatAnchorTableTests
    {
        private sealed class RealModIdentity
        {
            public RealModIdentity(string adapterId, string assemblyName, string manifestName,
                string manifestId, int anchorCount)
            {
                AdapterId = adapterId;
                AssemblyName = assemblyName;
                ManifestName = manifestName;
                ManifestId = manifestId;
                AnchorCount = anchorCount;
            }

            public string AdapterId { get; }

            public string AssemblyName { get; }

            public string ManifestName { get; }

            public string ManifestId { get; }

            public int AnchorCount { get; }
        }

        /// <summary>
        /// Verified 2026-10-25 from each mod's <c>manifest.json</c> and DLL assembly identity under
        /// <c>Steam\steamapps\workshop\content\1062090\</c>.
        /// </summary>
        private static readonly RealModIdentity[] RealMods =
        {
            new RealModIdentity("HDBF.Adapter.TimberPipes", "TimberPipes", "Timber Pipes", "TimberPipes", 4),
            new RealModIdentity("HDBF.Adapter.PowerLines", "PowerLines", "Power Lines", "PowerLines", 4),
            new RealModIdentity("HDBF.Adapter.ConveyorBeltsV2", "ConveyorBelt", "Conveyor Belts v2", "ConveyorBelt", 4),
            new RealModIdentity("HDBF.Adapter.MoreMines", "More Mines", "More Mines [ModContest1]", "TonWolfe.MoreMines", 4),
            new RealModIdentity("HDBF.Adapter.HoomanStairs", "hoomanstairs", "Hooman Stairs", "Calloatti.HoomanStairs", 3),
        };

        /// <summary>Anchor type names that were invented before real metadata was available; none may survive.</summary>
        private static readonly string[] ObsoleteAnchorTypeNames =
        {
            "TimberPipes.TimberPipeNetwork",
            "TimberPipes.PipeNetwork",
            "TimberPipes.TimberPipe",
            "Timberborn.Pipes.TimberPipeNetwork",
            "TimberPipes.WaterPipeNetwork",
            "PowerLines.PowerLineNetwork",
            "PowerLines.PowerLine",
            "Timberborn.PowerLines.PowerLineNetwork",
            "PowerLines.PowerNetwork",
            "ConveyorBelts.ConveyorBelt",
            "ConveyorBeltsV2.ConveyorBelt",
            "ConveyorBelts.BeltNetwork",
            "ConveyorBeltsV2.BeltNetwork",
            "ConveyorBelts.LogisticsInput",
            "MoreMines.MoreMinesSpec",
            "MoreMines.MineSpec",
            "MoreMines.MoreMinesConfigurator",
            "HoomanStairs.HoomanStair",
            "HoomanStairs.HoomanStairs",
            "HoomanStairs.StairBlockObject",
        };

        public static void TestChapter81AdapterOrderAndIds()
        {
            var expected = new[]
            {
                "HDBF.Adapter.TimberPipes",
                "HDBF.Adapter.PowerLines",
                "HDBF.Adapter.ConveyorBeltsV2",
                "HDBF.Adapter.MoreMines",
                "HDBF.Adapter.HoomanStairs",
            };

            TestAssert.Equal(expected.Length, CompatAnchorTable.All.Length, "table 8-1 has five adapters");
            for (var i = 0; i < expected.Length; i++)
            {
                TestAssert.Equal(expected[i], CompatAnchorTable.All[i].AdapterId);
                TestAssert.NotNull(CompatAnchorTable.ById(expected[i]), "ById must find " + expected[i]);
            }

            TestAssert.Null(CompatAnchorTable.ById("HDBF.Adapter.Unknown"));
            TestAssert.Null(CompatAnchorTable.ById(null));
        }

        public static void TestAdapterIdConstantsMatchTable()
        {
            TestAssert.Equal(CompatAnchorTable.TimberPipesId, CompatAnchorTable.All[0].AdapterId);
            TestAssert.Equal(CompatAnchorTable.PowerLinesId, CompatAnchorTable.All[1].AdapterId);
            TestAssert.Equal(CompatAnchorTable.ConveyorBeltsV2Id, CompatAnchorTable.All[2].AdapterId);
            TestAssert.Equal(CompatAnchorTable.MoreMinesId, CompatAnchorTable.All[3].AdapterId);
            TestAssert.Equal(CompatAnchorTable.HoomanStairsId, CompatAnchorTable.All[4].AdapterId);
        }

        public static void TestDetectionStringsMatchTheRealAssembliesAndManifests()
        {
            foreach (var real in RealMods)
            {
                var spec = CompatAnchorTable.ById(real.AdapterId);
                TestAssert.NotNull(spec, "missing spec for " + real.AdapterId);
                var adapter = spec!;

                TestAssert.Equal(real.ManifestId, adapter.ModId, real.AdapterId + " manifest id");
                TestAssert.True(adapter.DisplayNameKey.StartsWith("HDBF.Compat.Adapter.", StringComparison.Ordinal),
                    "display name key must use the documented prefix: " + adapter.DisplayNameKey);

                var normalizedAssembly = CompatMatching.Normalize(real.AssemblyName);
                TestAssert.True(CompatMatching.MatchesAssemblyName(normalizedAssembly, adapter.AssemblyNameTokens),
                    real.AdapterId + " must match assembly " + real.AssemblyName);

                var byName = CompatMatching.MatchModName(new[] { real.ManifestName }, adapter.ModNameAliases);
                TestAssert.Equal(real.ManifestName, byName, real.AdapterId + " must match manifest Name");

                var byId = CompatMatching.MatchModName(new[] { real.ManifestId }, adapter.ModNameAliases);
                TestAssert.Equal(real.ManifestId, byId, real.AdapterId + " must match manifest Id");
            }
        }

        public static void TestConveyorBeltUsesTheSingularAssemblyToken()
        {
            var adapter = CompatAnchorTable.ConveyorBeltsV2;

            // The real assemblies are "ConveyorBelt" and "ConveyorBelt.Algorithm"; a plural-only
            // token ("conveyorbelts") would miss both. This was a real detection bug before the fix.
            TestAssert.True(CompatMatching.MatchesAssemblyName("conveyorbelt", adapter.AssemblyNameTokens));
            TestAssert.True(CompatMatching.MatchesAssemblyName("conveyorbeltalgorithm", adapter.AssemblyNameTokens));
        }

        public static void TestMoreMinesAssemblyNameHasASpaceAndStillMatches()
        {
            var adapter = CompatAnchorTable.MoreMines;
            TestAssert.True(CompatMatching.MatchesAssemblyName(CompatMatching.Normalize("More Mines"),
                adapter.AssemblyNameTokens));
            TestAssert.True(CompatMatching.MatchesAssemblyName(CompatMatching.Normalize("MoreMines"),
                adapter.AssemblyNameTokens));
        }

        public static void TestUnrelatedAssembliesAndModsNeverMatch()
        {
            var unrelatedAssemblies = new[] { "HDBF.Integration", "HDBF.Core", "SharpNoise", "UnityEngine", "Harmony" };
            var unrelatedModList = new[] { "UnityEngine", "Newtonsoft.Json", "Harmony", "ModdableTimberborn" };

            foreach (var adapter in CompatAnchorTable.All)
            {
                foreach (var assembly in unrelatedAssemblies)
                {
                    TestAssert.False(
                        CompatMatching.MatchesAssemblyName(CompatMatching.Normalize(assembly), adapter.AssemblyNameTokens),
                        adapter.AdapterId + " must not match assembly " + assembly);
                }

                TestAssert.Null(CompatMatching.MatchModName(unrelatedModList, adapter.ModNameAliases),
                    adapter.AdapterId + " must not match an unrelated mod list");
            }
        }

        public static void TestEveryAdapterKeepsAtLeastThreeVerifiedAnchors()
        {
            foreach (var real in RealMods)
            {
                var adapter = CompatAnchorTable.ById(real.AdapterId)!;
                TestAssert.Equal(real.AnchorCount, adapter.ApiAnchors.Count,
                    real.AdapterId + " anchor count changed; re-verify against the installed DLLs");
                TestAssert.True(adapter.ApiAnchors.Count >= 3, real.AdapterId + " needs at least three anchors");
            }

            var total = 0;
            foreach (var adapter in CompatAnchorTable.All)
            {
                total += adapter.ApiAnchors.Count;
            }

            TestAssert.Equal(19, total, "verified anchor total");
        }

        public static void TestAnchorNamesAreWellFormed()
        {
            foreach (var adapter in CompatAnchorTable.All)
            {
                var seenTypes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var anchor in adapter.ApiAnchors)
                {
                    TestAssert.True(anchor.AssemblyName.Length > 0, adapter.AdapterId + " anchor assembly required");
                    TestAssert.True(anchor.TypeName.IndexOf('.') > 0, adapter.AdapterId + " anchor type must be namespaced");
                    TestAssert.True(seenTypes.Add(anchor.TypeName), adapter.AdapterId + " duplicate anchor type " + anchor.TypeName);
                    TestAssert.False(anchor.TypeName.Contains("+", StringComparison.Ordinal),
                        "nested anchor types need a deliberate '+' handling: " + anchor.TypeName);

                    if (anchor.IsTypeOnly)
                    {
                        continue;
                    }

                    TestAssert.True(anchor.MemberName.Length > 0, "member name required for " + anchor.TypeName);
                    TestAssert.True(anchor.ReturnTypeName.Length > 0, "return type required for " + anchor.TypeName);
                    TestAssert.False(anchor.MemberName.StartsWith(".ctor", StringComparison.Ordinal),
                        "constructors are not anchorable members: " + anchor.TypeName);

                    foreach (var parameter in anchor.ParameterTypeNames)
                    {
                        TestAssert.True(parameter.Length > 0, "empty parameter type on " + anchor.TypeName);
                        TestAssert.False(parameter.Contains("<", StringComparison.Ordinal) ||
                                         parameter.Contains("`", StringComparison.Ordinal) ||
                                         parameter.Contains(",", StringComparison.Ordinal),
                            "anchor signatures must use concrete, non-generic type names: " + parameter);
                        TestAssert.False(parameter.EndsWith("&", StringComparison.Ordinal),
                            "ByRef must be recorded by element type: " + parameter);
                    }
                }
            }
        }

        public static void TestApiBindingIsRequiredOnlyForLevelAAdapters()
        {
            TestAssert.True(CompatAnchorTable.TimberPipes.RequiresApiBinding, "Timber Pipes is A level");
            TestAssert.True(CompatAnchorTable.PowerLines.RequiresApiBinding, "Power Lines is A level");
            TestAssert.True(CompatAnchorTable.ConveyorBeltsV2.RequiresApiBinding, "Conveyor Belts v2 is A level");
            TestAssert.False(CompatAnchorTable.MoreMines.RequiresApiBinding, "More Mines is read-only");
            TestAssert.False(CompatAnchorTable.HoomanStairs.RequiresApiBinding, "Hooman Stairs is B level");

            // A-level adapters need a signature-pinned anchor; read-only ones need at least one
            // verified type so the report can still name what was found.
            foreach (var adapter in CompatAnchorTable.All)
            {
                TestAssert.True(adapter.MemberAnchorCount >= 1,
                    adapter.AdapterId + " must pin at least one member signature");
                if (adapter.RequiresApiBinding)
                {
                    TestAssert.Equal(adapter.ApiAnchors.Count, adapter.MemberAnchorCount,
                        adapter.AdapterId + " must pin every A-level anchor's signature");
                }
            }
        }

        public static void TestObsoleteInventedAnchorsAreGone()
        {
            foreach (var adapter in CompatAnchorTable.All)
            {
                foreach (var anchor in adapter.ApiAnchors)
                {
                    foreach (var obsolete in ObsoleteAnchorTypeNames)
                    {
                        TestAssert.False(string.Equals(anchor.TypeName, obsolete, StringComparison.Ordinal),
                            adapter.AdapterId + " still carries the invented anchor " + obsolete);

                        foreach (var parameter in anchor.ParameterTypeNames)
                        {
                            TestAssert.False(string.Equals(parameter, obsolete, StringComparison.Ordinal),
                                adapter.AdapterId + " still references the invented anchor " + obsolete);
                        }

                        TestAssert.False(string.Equals(anchor.ReturnTypeName, obsolete, StringComparison.Ordinal),
                            adapter.AdapterId + " still returns the invented anchor " + obsolete);
                    }
                }
            }
        }

        public static void TestDocumentedSignaturesArePinned()
        {
            TestAssert.Equal(
                "TimberPipes.Services.PipeRegistry.TryGetGraph(UnityEngine.Vector3Int, TimberPipes.Models.PipeGraph) -> System.Boolean",
                CompatAnchorTable.TimberPipes.ApiAnchors[0].SignatureText);
            TestAssert.Equal(
                "PowerLines.Services.PowerLineConnectionService.CanConnect(PowerLines.Components.PowerLineComponent, PowerLines.Components.PowerLineComponent) -> System.String",
                CompatAnchorTable.PowerLines.ApiAnchors[0].SignatureText);
            TestAssert.Equal(
                "ConveyorBelt.Algorithm.BeltSimulation.Tick(System.Single) -> System.Void",
                CompatAnchorTable.ConveyorBeltsV2.ApiAnchors[3].SignatureText);
            TestAssert.Equal(
                "More Mines",
                CompatAnchorTable.MoreMines.ApiAnchors[0].AssemblyName);
            TestAssert.Equal(
                "hoomanstairs",
                CompatAnchorTable.HoomanStairs.ApiAnchors[0].AssemblyName);
            TestAssert.Equal(
                "Calloatti.HoomanStairs.HoomanStairsPathfinder",
                CompatAnchorTable.HoomanStairs.ApiAnchors[2].SignatureText);
        }

        public static void TestNormalizeIsCaseAndSeparatorInsensitive()
        {
            TestAssert.Equal("timpberpipes", CompatMatching.Normalize("TimPBer_Pipes"));
            TestAssert.Equal("conveyorbeltsv2", CompatMatching.Normalize("Conveyor Belts V2"));
            TestAssert.Equal(string.Empty, CompatMatching.Normalize(null));
            TestAssert.Equal(string.Empty, CompatMatching.Normalize("---"));
        }

        public static void TestMissingModIsANeutralNonFailureStateForEveryAdapter()
        {
            // REQ-COMPAT-09/12: absent mods are grey/Disabled, never errors, and stay retryable.
            foreach (var adapter in CompatAnchorTable.All)
            {
                var machine = new CompatStateMachine();
                machine.MarkMissing("not-installed");

                TestAssert.Equal(HdbfAdapterHealth.Disabled, machine.Health, adapter.AdapterId);
                TestAssert.Equal(0, machine.FailureCount, adapter.AdapterId + " missing is not a failure");
                TestAssert.False(machine.IsDetected, adapter.AdapterId);
                TestAssert.True(machine.CanRetry, adapter.AdapterId + " must allow a later detect");

                TestAssert.True(machine.Detect(), adapter.AdapterId + " must detect after the mod appears");
                TestAssert.Equal(HdbfAdapterHealth.Disabled, machine.Health, adapter.AdapterId);
            }
        }

        public static void TestThreeStrikesDisableEveryAdapterForTheSession()
        {
            // REQ-COMPAT-07: three consecutive failures -> Failed, no further retries this session.
            foreach (var adapter in CompatAnchorTable.All)
            {
                var machine = new CompatStateMachine();
                machine.Detect();
                machine.InitializeSucceeded();
                TestAssert.True(machine.IsActive, adapter.AdapterId + " prerequisite: active");

                machine.Fail("api-anchor-signature-changed");
                machine.Fail("api-anchor-signature-changed");
                TestAssert.Equal(HdbfAdapterHealth.Degraded, machine.Health, adapter.AdapterId);
                TestAssert.True(machine.CanRetry, adapter.AdapterId + " retry below the threshold");

                machine.Fail("api-anchor-signature-changed");
                TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health, adapter.AdapterId);
                TestAssert.Equal(CompatStateMachine.FailureThreshold, machine.FailureCount, adapter.AdapterId);
                TestAssert.False(machine.CanRetry, adapter.AdapterId + " no retry after the threshold");
                TestAssert.False(machine.Detect(), adapter.AdapterId + " failed adapters stay failed");
            }
        }

        public static void TestCompatLogKeysAreStableForTheMatrixParser()
        {
            // tools/dev/CompatMatrix.ps1 -CheckLog parses these exact prefixes; they are a contract.
            TestAssert.Equal("compat.missing", HdbfLogKeys.CompatMissing);
            TestAssert.Equal("compat.failed", HdbfLogKeys.CompatFailed);
            TestAssert.Equal("compat.enabled", HdbfLogKeys.CompatEnabled);
        }
    }
}
