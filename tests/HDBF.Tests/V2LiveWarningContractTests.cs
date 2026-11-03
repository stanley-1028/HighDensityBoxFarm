// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GAMEPLAY-02 / V2-UI-01 (round-3 L-11/L-17)
// Purpose: L-11 same-source warning contract (live-derived flags drive both the world icon and
//          every panel warning card), L-10 NoWorkers severity, L-17 fixed world-icon tooltip and
//          the L-14 icon-pass cost rule, plus the WP-05/L-02 GlobalSupply pipe port state. The
//          gameplay half is pinned as source text through the baked-in repo root (same pattern as
//          V2GameplayWiringTests); the Core/UI half runs as real linked production code.
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-UI-02, REQ-V2-READ-04, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;
using HDBF.UI.Panels;

namespace HDBF.Tests
{
    /// <summary>
    /// Contract of the L-11 live warning derivation: <see cref="IndoorWarningMap.LiveFlagsOf"/> and
    /// <see cref="IndoorWarningMap.EffectiveFlags"/> are the one source the world icon and every
    /// panel warning card read, so a refilled tank / hauled output clears the warning immediately
    /// instead of on the next daily tick.
    /// </summary>
    public static class V2LiveWarningContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string Components = "src/HDBF.Gameplay/Components/";
        private const string Panels = "src/HDBF.UI/Panels/";

        // ---------------------------------------------------------------- L-11 Core derivation

        public static void TestLiveFlagsReactToRefilledWaterAndClearedOutput()
        {
            var stale = Healthy() with
            {
                Warnings = IndoorWarningFlags.NoWater | IndoorWarningFlags.OutputFull
                           | IndoorWarningFlags.EntranceBlocked,
                Coverage = 1f,
                EntranceBlocked = false,
                OutputOccupied = 0,
            };
            // L-11: the daily cache is stale; the live values are clean -> no warning at all.
            TestAssert.Equal(IndoorWarningFlags.None, IndoorWarningMap.LiveFlagsOf(stale));

            // Without a derived set the effective flags stay the daily cache (old snapshots/tests).
            TestAssert.Equal(stale.Warnings, IndoorWarningMap.EffectiveFlags(stale));
            // With a derived set the live one wins.
            TestAssert.Equal(IndoorWarningFlags.None, IndoorWarningMap.EffectiveFlags(
                stale with { LiveWarnings = IndoorWarningFlags.None }));
            TestAssert.Equal(IndoorWarningFlags.None, IndoorWarningMap.EffectiveFlags(null),
                "a missing farm carries no warnings, never an exception");
            TestAssert.Equal(IndoorWarningFlags.None, IndoorWarningMap.LiveFlagsOf(null));
        }

        public static void TestLiveFlagsDeriveTheWaterThresholdsFromTheLiveCoverage()
        {
            TestAssert.Equal(IndoorWarningFlags.NoWater,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { Coverage = 0f }));
            TestAssert.Equal(IndoorWarningFlags.LowWater,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { Coverage = 0.3f, MinCoverage = 0.6f }));
            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { Coverage = 0.6f, MinCoverage = 0.6f }),
                "exactly C_min is not a shortage");
            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { Coverage = 0.1f, MinCoverage = 0f }),
                "a tier without a minimum coverage is never short of water");
            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { Coverage = float.NaN }),
                "a broken coverage number reports no warning instead of a fake shortage");
        }

        public static void TestLiveFlagsCoverCrewEntrancePowerAndOutput()
        {
            TestAssert.Equal(IndoorWarningFlags.NoWorkers,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { Workers = 0 }));
            TestAssert.Equal(IndoorWarningFlags.EntranceBlocked,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { EntranceBlocked = true }));

            var top = Healthy() with { Profile = TierCatalog.Get(HdbfTier.Top) };
            TestAssert.Equal(IndoorWarningFlags.NoPower,
                IndoorWarningMap.LiveFlagsOf(top with
                {
                    Power = PowerConnection.Grid,
                    PowerConnected = false,
                }));
            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(top with
                {
                    Power = PowerConnection.Grid,
                    PowerConnected = true,
                }));

            // OutputFull is live from the occupancy: 150 slots -> 600 output capacity.
            TestAssert.Equal(IndoorWarningFlags.OutputFull,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { OutputOccupied = 600 }));
            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(Healthy() with { OutputOccupied = 599 }),
                "one free slot in the output buffer is not full");
        }

        public static void TestLiveFlagsKeepOnlyTheMissingMaterialDailyBit()
        {
            var snapshot = Healthy() with
            {
                Warnings = IndoorWarningFlags.MissingMaterial | IndoorWarningFlags.NoWater
                           | IndoorWarningFlags.MaintenanceDue,
            };
            // No live source can re-judge the material bill, so that one daily verdict survives.
            TestAssert.Equal(IndoorWarningFlags.MissingMaterial, IndoorWarningMap.LiveFlagsOf(snapshot));
        }

        public static void TestLiveFlagsJudgeMaintenanceAgainstTheLiveDay()
        {
            var profile = TierCatalog.Get(HdbfTier.Intermediate); // 10-day material cycle
            var baseSnapshot = Healthy() with { Profile = profile, MaintenanceDueDay = 24.5f };

            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(baseSnapshot with { CurrentDay = 10f, MaintenanceCyclesMissed = 5 }),
                "the live day (not the stored counter) decides: before the deadline nothing is due");
            TestAssert.Equal(IndoorWarningFlags.MaintenanceDue,
                IndoorWarningMap.LiveFlagsOf(baseSnapshot with { CurrentDay = 25f }));
            TestAssert.Equal(IndoorWarningFlags.MaintenanceDue | IndoorWarningFlags.MaintenanceStalled,
                IndoorWarningMap.LiveFlagsOf(baseSnapshot with { CurrentDay = 35f }),
                "two missed cycles stall the farm's actions");
            TestAssert.Equal(IndoorWarningFlags.None,
                IndoorWarningMap.LiveFlagsOf(baseSnapshot with { CurrentDay = float.NaN }),
                "a broken clock falls back to the stored 0-cycle counter");
        }

        public static void TestStoredCounterIsTheFallbackWhenTheProfileIsMissing()
        {
            // Older snapshots/tests carry no profile: the stored counter still reports the problem.
            var snapshot = Healthy() with
            {
                Profile = null,
                MaintenanceCyclesMissed = 2,
                MaintenanceDueDay = 5f,
                CurrentDay = 99f,
            };
            TestAssert.Equal(
                IndoorWarningFlags.MaintenanceDue | IndoorWarningFlags.MaintenanceStalled,
                IndoorWarningMap.LiveFlagsOf(snapshot));
        }

        // ---------------------------------------------------------------- panel same-source

        public static void TestPanelWarningsReadTheLiveFlagSetInsteadOfTheDailyCache()
        {
            var stale = Healthy() with
            {
                Warnings = IndoorWarningFlags.NoWater | IndoorWarningFlags.OutputFull
                           | IndoorWarningFlags.NoPower | IndoorWarningFlags.MaintenanceDue,
                LiveWarnings = IndoorWarningFlags.None,
                Coverage = 1f,
                OutputOccupied = 0,
            };
            var clean = CreateData(stale);
            TestAssert.Equal(0, Counting(clean), "the panel must ignore the stale daily flag cache");

            TestAssert.True(Counting(CreateData(stale with
                        { LiveWarnings = IndoorWarningFlags.OutputFull })) > 0,
                "an output-full live set must raise the output warning");
            TestAssert.True(Counting(CreateData(stale with
                        { LiveWarnings = IndoorWarningFlags.MaintenanceDue })) > 0,
                "a live maintenance-due set must raise the maintenance warning");
            TestAssert.True(Counting(CreateData(stale with
                        { LiveWarnings = IndoorWarningFlags.NoPower })) > 0,
                "a live power-cut set must raise the power warning");
        }

        public static void TestPanelWorkerWarningUsesTheL10HighSeverity()
        {
            var data = CreateData(Healthy() with
            {
                Workers = 0,
                LiveWarnings = IndoorWarningFlags.NoWorkers,
            });
            var row = data.Sections.SelectMany(section => section.Rows).First(row => row.IsWarning);
            TestAssert.Equal(IndoorWarningSeverity.High, IndoorWarningMap.SeverityOf(IndoorWarningFlags.NoWorkers));
            // The marker is the second, colour-free encoding: "!" is the P1 (High) marker.
            TestAssert.Equal("!", row.Marker,
                "NoWorkers renders the P1 text marker, not the P0 triangle");
        }

        public static void TestPanelEntranceRowComesFromTheLiveFieldToo()
        {
            // The live EntranceBlocked field alone (no flag bit) must still raise the entrance row.
            var data = CreateData(Healthy() with
            {
                EntranceBlocked = true,
                LiveWarnings = IndoorWarningFlags.None,
            });
            TestAssert.True(Counting(data) > 0,
                "the live entrance state must reach the panel even when no flag is carried");
        }

        // ---------------------------------------------------------------- WP-05 GlobalSupply

        public static void TestGlobalSupplyProbeReasonRendersTheSettlementWideState()
        {
            // The numeric reason codes are a frozen panel/save contract.
            TestAssert.Equal(8, (int)PipeProbeReason.GlobalSupply);
            TestAssert.Equal(HdbfUiLocKeys.PortGlobalSupply, "HDBF.Port.GlobalSupply");

            var snapshot = Healthy() with
            {
                PipePort = true,
                WaterSupply = WaterSupplyKind.Pipe,
                PipeProbeState = new PipeProbe(PipeProbeReason.GlobalSupply, 1, 1, string.Empty),
            };
            TestAssert.Equal(HdbfUiLocKeys.PortGlobalSupply,
                HdbfIndoorPanelFactory.PipePortStateKey(snapshot),
                "a settlement-wide supply is credited honestly instead of 'not connected'");
            var data = CreateData(snapshot);
            TestAssert.True(data.Sections.SelectMany(section => section.Rows)
                    .Any(row => row.Value == HdbfUiLocKeys.PortGlobalSupply),
                "the pipe row renders the GlobalSupply state text");
        }

        // ---------------------------------------------------------------- L-17 / L-14 source

        public static void TestWorldIconTooltipIsFixedAndFreeOfTheDynamicDistance()
        {
            var update = RepoRead(Components + "HdbfWorldIconService.Update.cs");
            var start = update.IndexOf("Tooltip sentence of one icon class", StringComparison.Ordinal);
            TestAssert.True(start >= 0, "the world-icon tooltip builder must exist");
            var tooltip = update.Substring(start, Math.Min(1600, update.Length - start));
            TestAssert.True(tooltip.Contains("_loc.T(IndoorWarningMap.WarningKeyOf(kind))", StringComparison.Ordinal),
                "the tooltip is the localized warning sentence");
            TestAssert.False(tooltip.Contains("WorldIconMaxDistance", StringComparison.Ordinal),
                "L-17: the tooltip must not bake the configurable distance in");
            // The API conclusion is documented where the fixed sentence is chosen.
            TestAssert.True(tooltip.Contains("StatusSpecification", StringComparison.Ordinal)
                            && tooltip.Contains("no unregister", StringComparison.Ordinal),
                "the source explains why vanilla offers no writable status description");
        }

        public static void TestWorldIconPassSkipsSnapshotsForDisabledIcons()
        {
            var evaluate = RepoRead(Components + "HdbfWorldIconService.Evaluate.cs");
            var guard = evaluate.IndexOf("!iconsEnabled || handle.Subject is null", StringComparison.Ordinal);
            var snapshot = evaluate.IndexOf("_query.GetIndoorSnapshot", StringComparison.Ordinal);
            TestAssert.True(guard >= 0 && snapshot > guard,
                "L-14: a farm with icons off must not pay for a snapshot build");
            TestAssert.True(evaluate.Contains("IndoorWarningMap.EffectiveFlags", StringComparison.Ordinal),
                "L-11: the world icon reads the same effective flag set as the panel");
            TestAssert.True(evaluate.Contains("handle.AppliedKind", StringComparison.Ordinal),
                "L-14: the toggles are only touched when the icon class actually changes");
        }

        public static void TestControllerAndPanelWiringUseTheLiveMap()
        {
            var controller = RepoRead(Components + "HdbfBoxFarmController.IndoorSnapshot.cs");
            foreach (var token in new[]
                     {
                         "CurrentDay = currentDay", "EntranceBlocked = _entranceBlocked",
                         "IndoorWarningMap.LiveFlagsOf(snapshot)", "LiveWarnings = liveWarnings",
                     })
            {
                TestAssert.True(controller.Contains(token, StringComparison.Ordinal),
                    "the controller must fill the live warning snapshot: " + token);
            }

            foreach (var file in new[]
                     {
                         "HdbfIndoorPanelWarnings.cs", "HdbfIndoorPanelFactory.cs",
                         "HdbfIndoorPanelFactory.Cards.Maintenance.cs",
                     })
            {
                var text = RepoRead(Panels + file);
                TestAssert.True(text.Contains("IndoorWarningMap.EffectiveFlags", StringComparison.Ordinal),
                    file + " must read the effective (live) flag set");
                TestAssert.False(text.Contains("snapshot.Warnings", StringComparison.Ordinal),
                    file + " must not read the once-per-day cache any more (L-11)");
            }
        }

        // ---------------------------------------------------------------- helpers

        private static int Counting(HdbfIndoorPanelData data)
        {
            return data.Sections.SelectMany(section => section.Rows).Count(row => row.IsWarning);
        }

        private static HdbfIndoorPanelData CreateData(IndoorFarmSnapshot snapshot)
        {
            return HdbfIndoorPanelFactory.Create(snapshot, key => key);
        }

        private static IndoorFarmSnapshot Healthy()
        {
            return new IndoorFarmSnapshot(
                Tier: HdbfTier.Intermediate,
                Faction: HdbfFaction.Folktails,
                Workers: 2,
                MaxWorkers: 4,
                Mode: IndoorMode.Normal,
                ModeTransitioning: false,
                Capacity: 150,
                ConfiguredSlots: 150,
                EffectiveSlots: 76,
                EmptySlots: 100,
                GrowingSlots: 50,
                MatureSlots: 0,
                BacklogActions: 0,
                Tank: 10.8f,
                TankCapacity: 10.8f,
                TankDaysRemaining: 3f,
                Coverage: 1f,
                MinCoverage: 0.6f,
                WaterSupply: WaterSupplyKind.None,
                MaintenanceDueDay: 24.5f,
                MaintenanceCyclesMissed: 0,
                MaintenanceMaterialMissing: false,
                Power: PowerConnection.NotRequired,
                PowerConnected: false,
                CropPolicy: CropPolicy.Rotation,
                FixedCropId: string.Empty,
                RotationSequence: string.Empty,
                OutputOccupied: 0,
                OutputPortConnected: false,
                PipePort: false,
                LogisticsPort: false,
                PowerPort: false,
                Warnings: IndoorWarningFlags.None,
                OutputInventoryPortState: string.Empty)
            {
                AutoMaintenance = true,
                IdleReason = IndoorIdleReason.None,
                Profile = TierCatalog.Get(HdbfTier.Intermediate),
                CurrentDay = 10f,
            };
        }

        private static string RepoRead(string relativePath)
        {
            var metadata = typeof(V2LiveWarningContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException("assembly metadata \"" + RepoRootKey + "\" is missing");
            }

            var path = Path.Combine(metadata.Value!,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new TestFailureException("source file not found: " + path);
            }

            return File.ReadAllText(path);
        }
    }
}
