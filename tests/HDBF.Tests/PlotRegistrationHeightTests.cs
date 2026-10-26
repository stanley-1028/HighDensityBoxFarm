using System;
using System.Collections.Generic;
using HDBF.Core.Balance;
using HDBF.Core.Logging;
using HDBF.Core.Plots;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 regression tests for the P0 plot-registration / irrigation card.
    ///
    /// The card's root-cause question was the z semantics of
    /// <c>PlantingAreaValidator.CanPlant</c> vs <c>ISoilMoistureService.SoilIsMoist</c>.
    /// Reading the installed assembly IL showed both take the same cell — the terrain column
    /// ceiling (first air cell above the column, the cell the Fields tool paints). These tests pin
    /// that conclusion so a later "fix" cannot silently re-introduce a ±1 z offset, and cover the
    /// three other behaviours the card changed:
    ///
    /// * plot-id encode/decode bijection (negative horizontal coordinates, map boundaries);
    /// * coverage boundaries (0 registered plots, all dry, all moist, partial, excluded plot);
    /// * the empty/starved-registry warning rule and its stable log keys.
    ///
    /// No Timberborn or Unity assembly is referenced (REQ-TEST-03).
    /// </summary>
    public static class PlotRegistrationHeightTests
    {
        // ------------------------------------------------------------ plot-id codec bijection

        public static void TestPlotCellCodecRoundTripsMapBoundaries()
        {
            AssertRoundTrip(0, 0, 0);
            AssertRoundTrip(-1024, -1024, 0);
            AssertRoundTrip(1023, 1023, 1023);
            AssertRoundTrip(-1024, 1023, 512);
            AssertRoundTrip(1023, -1024, 1);
            AssertRoundTrip(-1, -1, 1023);
        }

        public static void TestPlotCellCodecRoundTripsSamplesAcrossTheWholeRange()
        {
            var coordinates = new[] { -1024, -1023, -513, -512, -511, -1, 0, 1, 511, 512, 513, 1022, 1023 };
            var heights = new[] { 0, 1, 2, 511, 1022, 1023 };
            foreach (var x in coordinates)
            {
                foreach (var y in coordinates)
                {
                    foreach (var z in heights)
                    {
                        AssertRoundTrip(x, y, z);
                    }
                }
            }
        }

        public static void TestPlotCellCodecIsBijectiveOnNegativeCoordinates()
        {
            // Negative horizontal cells are the sign-extension branch; every cell must map to a
            // distinct plot id (a collision would hand one farm another farm's plot).
            var ids = new HashSet<int>();
            for (var x = -64; x <= 0; x++)
            {
                for (var y = -64; y <= 0; y++)
                {
                    var id = PlotCellCodec.Encode(x, y, 4);
                    TestAssert.True(ids.Add(id),
                        "duplicate plot id for cell (" + x + "," + y + ",4)");
                }
            }

            TestAssert.Equal(65 * 65, ids.Count);
        }

        public static void TestPlotCellCodecDecodesTheSignBitsCorrectly()
        {
            var id = PlotCellCodec.Encode(-1, -2, 3);
            PlotCellCodec.Decode(id, out var x, out var y, out var z);
            TestAssert.Equal(-1, x);
            TestAssert.Equal(-2, y);
            TestAssert.Equal(3, z);
        }

        public static void TestPlotCellCodecMarksOutOfRangeCellsUnencodable()
        {
            TestAssert.True(PlotCellCodec.IsEncodable(0, 0, 0));
            TestAssert.True(PlotCellCodec.IsEncodable(-1024, 1023, 1023));
            TestAssert.False(PlotCellCodec.IsEncodable(-1025, 0, 0));
            TestAssert.False(PlotCellCodec.IsEncodable(0, 1024, 0));
            TestAssert.False(PlotCellCodec.IsEncodable(0, 0, -1));
            TestAssert.False(PlotCellCodec.IsEncodable(0, 0, 1024));
        }

        public static void TestPlotCellCodecKeepsTheDocumentedSaveLayout()
        {
            // Save-format contract (chapter 12): x in bits 21..31, y in bits 10..20, z in bits
            // 0..9. Existing saves store ids encoded with this layout, so it must never drift.
            TestAssert.Equal(0, PlotCellCodec.Encode(0, 0, 0));
            TestAssert.Equal(1, PlotCellCodec.Encode(0, 0, 1));
            TestAssert.Equal(1 << 10, PlotCellCodec.Encode(0, 1, 0));
            TestAssert.Equal(1 << 21, PlotCellCodec.Encode(1, 0, 0));
            TestAssert.Equal((0x7FF << 21) | (0x7FF << 10) | 0x3FF,
                PlotCellCodec.Encode(-1, -1, 1023));

            PlotCellCodec.Decode(0, out var x, out var y, out var z);
            TestAssert.Equal(0, x);
            TestAssert.Equal(0, y);
            TestAssert.Equal(0, z);
        }

        private static void AssertRoundTrip(int x, int y, int z)
        {
            var plotId = PlotCellCodec.Encode(x, y, z);
            PlotCellCodec.Decode(plotId, out var decodedX, out var decodedY, out var decodedZ);
            TestAssert.Equal(x, decodedX, "x round-trip for (" + x + "," + y + "," + z + ")");
            TestAssert.Equal(y, decodedY, "y round-trip for (" + x + "," + y + "," + z + ")");
            TestAssert.Equal(z, decodedZ, "z round-trip for (" + x + "," + y + "," + z + ")");
        }

        // ------------------------------------------------ plant cell == moisture sample cell

        public static void TestPlantCellHeightIsTheTerrainColumnCeiling()
        {
            // TerrainColumn.Ceiling is exclusive (solid voxels are Floor..Ceiling-1), so the
            // plant cell the Fields tool paints is the column ceiling itself. IL evidence:
            // TerrainService.GetTerrainHeight = coords.z + TerrainMap.TryGetRelativeHeight, and
            // PlantingSelectionService.InMapLeveledCoordinates yields pickedVoxel.z + 1.
            TestAssert.Equal(0, PlotCellHeights.ResolvePlantCellZ(0));
            TestAssert.Equal(1, PlotCellHeights.ResolvePlantCellZ(1));
            TestAssert.Equal(1023, PlotCellHeights.ResolvePlantCellZ(1023));
            for (var ceiling = 0; ceiling <= 1023; ceiling += 37)
            {
                TestAssert.Equal(ceiling, PlotCellHeights.ResolvePlantCellZ(ceiling));
            }
        }

        public static void TestMoistureSampleCellIsThePlantCell()
        {
            // SoilMoistureService.SoilIsMoist calls TryGetIndexAtCeiling(index2D, coordinates.z)
            // and that matches ReadOnlyTerrainColumn.Ceiling; PlantingSoilValidator passes the
            // planting coordinate itself. A non-zero offset here would make every plot dry.
            TestAssert.Equal(0, PlotCellHeights.PlantingToMoistureCellOffset,
                "IL: plant cell and moisture sample cell are the same terrain column ceiling");
            TestAssert.Equal(0, PlotCellHeights.ResolveMoistureSampleZ(0));
            TestAssert.Equal(7, PlotCellHeights.ResolveMoistureSampleZ(7));
            TestAssert.Equal(1023, PlotCellHeights.ResolveMoistureSampleZ(1023));
            for (var plantZ = 0; plantZ <= 1023; plantZ += 37)
            {
                TestAssert.Equal(plantZ, PlotCellHeights.ResolveMoistureSampleZ(plantZ));
                TestAssert.Equal(plantZ, PlotCellHeights.ResolveMoistureSampleZFromCeiling(plantZ));
            }
        }

        public static void TestStoredPlotIdSamplesTheSameCellItStores()
        {
            // Scan path: column ceiling -> ResolvePlantCellZ -> plot id. Sampler path: plot id ->
            // ResolveMoistureSampleZ. The two must land on the exact same cell for every height.
            var ceilings = new[] { 0, 1, 4, 10, 47, 900, 1023 };
            foreach (var ceiling in ceilings)
            {
                var plantZ = PlotCellHeights.ResolvePlantCellZ(ceiling);
                var plotId = PlotCellCodec.Encode(12, -34, plantZ);
                PlotCellCodec.Decode(plotId, out var x, out var y, out var storedZ);
                TestAssert.Equal(12, x);
                TestAssert.Equal(-34, y);
                TestAssert.Equal(ceiling, storedZ);

                var sampleZ = PlotCellHeights.ResolveMoistureSampleZ(storedZ);
                TestAssert.Equal(plantZ, sampleZ);
                TestAssert.Equal(ceiling, sampleZ);
            }
        }

        // ---------------------------------------------------------------- coverage boundaries

        public static void TestCoverageSampleEmptyRegistryIsZeroWithoutDivision()
        {
            var sample = IrrigationCoverage.Compute(0, 0, 0);
            TestAssert.Equal(0, sample.RegisteredPlots);
            TestAssert.Equal(0, sample.IrrigatedPlots);
            TestAssert.Equal(0f, sample.Coverage);

            // Untrusted counters must not create coverage when nothing is registered.
            var untrusted = IrrigationCoverage.Compute(0, 12, 5);
            TestAssert.Equal(0, untrusted.RegisteredPlots);
            TestAssert.Equal(0, untrusted.IrrigatedPlots);
            TestAssert.Equal(0f, untrusted.Coverage);

            TestAssert.Equal(0f, IrrigationCoverage.Compute(-3, 2, 2).Coverage);
        }

        public static void TestCoverageSampleAllDryIsZero()
        {
            var sample = IrrigationCoverage.Compute(60, 0, 0);
            TestAssert.Equal(60, sample.RegisteredPlots);
            TestAssert.Equal(0, sample.IrrigatedPlots);
            TestAssert.Equal(0f, sample.Coverage);
        }

        public static void TestCoverageSampleAllMoistIsOne()
        {
            var sample = IrrigationCoverage.Compute(60, 60, 0);
            TestAssert.Equal(60, sample.IrrigatedPlots);
            TestAssert.Equal(1f, sample.Coverage);
        }

        public static void TestCoverageSamplePartialMoisture()
        {
            var sample = IrrigationCoverage.Compute(100, 25, 0);
            TestAssert.Equal(25, sample.MoistPlots);
            TestAssert.Equal(25, sample.IrrigatedPlots);
            TestAssert.Equal(0.25f, sample.Coverage);
        }

        public static void TestCoverageSampleTankFedPlotsAddToMoisture()
        {
            // Chapter 6.3: coverage = natural moisture + the dry plots the tank watered today.
            var sample = IrrigationCoverage.Compute(100, 20, 35);
            TestAssert.Equal(20, sample.MoistPlots);
            TestAssert.Equal(35, sample.TankFedPlots);
            TestAssert.Equal(55, sample.IrrigatedPlots);
            TestAssert.Equal(0.55f, sample.Coverage);
        }

        public static void TestCoverageSampleClampsUntrustedCounters()
        {
            var sample = IrrigationCoverage.Compute(10, 20, -3);
            TestAssert.Equal(10, sample.MoistPlots, "moist count clamps to registered");
            TestAssert.Equal(0, sample.TankFedPlots, "negative tank-fed count clamps to 0");
            TestAssert.Equal(10, sample.IrrigatedPlots);
            TestAssert.Equal(1f, sample.Coverage);

            var combined = IrrigationCoverage.Compute(10, 8, 8);
            TestAssert.Equal(10, combined.IrrigatedPlots, "moist + tank-fed clamps to registered");
            TestAssert.Equal(1f, combined.Coverage);
        }

        public static void TestExcludedPlotIsNotCountedInCoverage()
        {
            // A player-excluded cell never becomes a candidate (REQ-IMPL-10), so it can be
            // neither a coverage denominator nor a numerator entry; only registered plots count.
            var plantable = new[] { 10, 20, 30, 40 };
            const int excludedPlotId = 30;
            var moistureByPlot = new Dictionary<int, bool>(plantable.Length);
            var candidates = new List<PlotCandidate>(plantable.Length);
            foreach (var plotId in plantable)
            {
                // Every plantable cell is moist, including the excluded one: the excluded cell
                // must still not raise coverage.
                moistureByPlot[plotId] = true;
                if (plotId != excludedPlotId)
                {
                    candidates.Add(new PlotCandidate(plotId, plotId));
                }
            }

            var registered = PlotSelection.Select(candidates, 60, null);
            TestAssert.Equal(3, registered.Length);
            foreach (var plotId in registered)
            {
                TestAssert.True(plotId != excludedPlotId, "excluded plot id must not be registered");
            }

            var moist = 0;
            foreach (var plotId in registered)
            {
                if (moistureByPlot[plotId])
                {
                    moist++;
                }
            }

            var sample = IrrigationCoverage.Compute(registered.Length, moist, 0);
            TestAssert.Equal(3, sample.RegisteredPlots);
            TestAssert.Equal(1f, sample.Coverage);
        }

        // ------------------------------------------------- empty / starved registry visibility

        public static void TestScanStatsResetClearsEveryCounter()
        {
            var stats = new PlotScanStats
            {
                Scanned = 121,
                Accepted = 60,
                TerrainRejects = 1,
                FieldRejects = 2,
                BlockRejects = 3,
                BlockerRejects = 4,
                ReachabilityRejects = 5,
                Excluded = 6,
                Claimed = 7,
            };

            stats.Reset();

            TestAssert.Equal(0, stats.Scanned);
            TestAssert.Equal(0, stats.Accepted);
            TestAssert.Equal(0, stats.TerrainRejects);
            TestAssert.Equal(0, stats.FieldRejects);
            TestAssert.Equal(0, stats.BlockRejects);
            TestAssert.Equal(0, stats.BlockerRejects);
            TestAssert.Equal(0, stats.ReachabilityRejects);
            TestAssert.Equal(0, stats.Excluded);
            TestAssert.Equal(0, stats.Claimed);
            TestAssert.Equal(0, stats.TotalRejects);
        }

        public static void TestEmptyRegistryRaisesTheEmptyWarning()
        {
            var stats = new PlotScanStats
            {
                Scanned = 121,
                Accepted = 0,
                FieldRejects = 121,
            };

            TestAssert.True(stats.ShouldWarnAboutRegistration(0));
            TestAssert.Equal(HdbfLogKeys.PlotScanEmpty, stats.WarningKey(0));

            var message = stats.Describe(0, "entity-guid");
            TestAssert.True(message.Contains("entityId=entity-guid", StringComparison.Ordinal), message);
            TestAssert.True(message.Contains("registered=0", StringComparison.Ordinal), message);
            TestAssert.True(message.Contains("scanned=121", StringComparison.Ordinal), message);
            TestAssert.True(message.Contains("accepted=0", StringComparison.Ordinal), message);
            TestAssert.True(message.Contains("field=121", StringComparison.Ordinal), message);
        }

        public static void TestZeroRegisteredPlotsWarnsEvenWhenCellsWereAccepted()
        {
            // Capacity 0 / registry refusal: cells were accepted but nothing is registered, so the
            // panel would show a healthy "effective plots" number with 0 % coverage again.
            var stats = new PlotScanStats { Scanned = 121, Accepted = 60 };
            TestAssert.True(stats.ShouldWarnAboutRegistration(0));
            TestAssert.Equal(HdbfLogKeys.PlotScanEmpty, stats.WarningKey(0));
        }

        public static void TestReachabilityDominatedScanRaisesTheStarvedWarning()
        {
            var stats = new PlotScanStats
            {
                Scanned = 121,
                Accepted = 5,
                ReachabilityRejects = 116,
            };

            TestAssert.True(stats.ShouldWarnAboutRegistration(5));
            TestAssert.Equal(HdbfLogKeys.PlotScanReachability, stats.WarningKey(5));
            var message = stats.Describe(5, "entity-guid");
            TestAssert.True(message.Contains("reach=116", StringComparison.Ordinal), message);
            TestAssert.True(message.Contains("accepted=5", StringComparison.Ordinal), message);
            TestAssert.True(message.Contains("registered=5", StringComparison.Ordinal), message);
        }

        public static void TestHealthyScanDoesNotWarn()
        {
            var stats = new PlotScanStats
            {
                Scanned = 121,
                Accepted = 60,
                ReachabilityRejects = 30,
            };

            TestAssert.False(stats.ShouldWarnAboutRegistration(60));
            TestAssert.Null(stats.WarningKey(60));

            var exactlyEqual = new PlotScanStats { Accepted = 30, ReachabilityRejects = 30 };
            TestAssert.False(exactlyEqual.ShouldWarnAboutRegistration(30),
                "reachability rejects equal to accepted plots is not a starvation signal");
        }

        public static void TestRegistrationWarningLogKeysAreStable()
        {
            // The log keys are the throttle identity and are grepped by the in-game verification
            // round; renaming them breaks the diagnostics contract (REQ-ARCH-15 / chapter 10.4).
            TestAssert.Equal("plot.scan-empty", HdbfLogKeys.PlotScanEmpty);
            TestAssert.Equal("plot.scan-reachability", HdbfLogKeys.PlotScanReachability);
            TestAssert.True(!string.Equals(HdbfLogKeys.PlotScanEmpty, HdbfLogKeys.PlotScanReachability,
                StringComparison.Ordinal), "the two warning outcomes must use different throttle keys");
        }
    }
}
