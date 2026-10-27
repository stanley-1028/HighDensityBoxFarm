// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-UI-01
// Purpose: Source+math contract of the LIVE water block of the panel snapshot: a loaded farm with
//          a full tank must report C = 100 % before the first daily tick (the reported "coverage
//          0 % / shortage 0 %" defect), and the panel derives the irrigation derate from the live
//          coverage instead of the previous tick's flags.
// Requirements: REQ-V2-WATER-03, REQ-V2-UI-02, REQ-V2-READ-04, REQ-V2-TEST-01
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.UI.Panels;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of the live coverage patch: the controller snapshot builder recomputes demand and
    /// coverage from the current tank/crew/mode (no cached <c>_lastCoverage</c>), and the UI maps the
    /// resulting C into G_irr with the shipped Core formula.
    /// </summary>
    public static class V2UiLiveWaterContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";

        /// <summary>
        /// Full-tank Basic farm, two farmers, normal mode: P_eff = min(60, 2 x 38) = 60,
        /// demand = 60 x 0.020 x 1.0 = 1.2 units/day, coverage = 3.6 / 1.2 -> clamped to 100 %.
        /// The old code reported 0 % here because the daily tick had not run yet.
        /// </summary>
        public static void TestFullTankCoverageExampleIsHundredPercent()
        {
            var profile = HDBF.Core.Balance.TierCatalog.Get(HdbfTier.Basic);
            var effective = IndoorLaborMath.EffectiveSlots(profile, 2);
            TestAssert.Equal(60, effective, "P_eff of two Basic farmers");
            var demand = IndoorWaterMath.DemandPerDay(effective, profile.WaterPerSlotPerDay, IndoorMode.Normal);
            TestAssert.Equal(1.2f, demand, 1e-5f, "60 slots x 0.020 units/day");
            TestAssert.Equal(1f, IndoorWaterMath.Coverage(profile.TankCapacity, demand),
                "a full 3.6 tank reports 100 % coverage");
            TestAssert.Equal(1f, IndoorWaterMath.IrrigationMultiplier(1f, profile.MinIrrigationCoverage),
                "C >= C_min means full growth efficiency");

            // Half-full tank on the same day still covers the demand fully (C = 150 % -> clamp 1).
            TestAssert.Equal(1f, IndoorWaterMath.Coverage(1.8f, demand));
            // 0.6 units = exactly C_min (50 %) -> no derate; 0.3 units = 25 % -> G_irr = 1/3.
            TestAssert.Equal(1f, IndoorWaterMath.IrrigationMultiplier(
                IndoorWaterMath.Coverage(0.6f, demand), profile.MinIrrigationCoverage));
            var low = IndoorWaterMath.Coverage(0.3f, demand);
            TestAssert.Equal(0.25f, low, 1e-5f);
            TestAssert.Equal(1f / 3f, IndoorWaterMath.IrrigationMultiplier(low, profile.MinIrrigationCoverage),
                1e-5f);
        }

        /// <summary>
        /// The snapshot builder must compute demand/coverage from the current record; the cached
        /// per-day value is gone so a load cannot render the previous day's 0 %.
        /// </summary>
        public static void TestSnapshotBuilderComputesCoverageLive()
        {
            var source = ReadControllerSource("HdbfBoxFarmController.IndoorSnapshot.cs");
            TestAssert.True(source.Contains("IndoorWaterMath.DemandPerDay", StringComparison.Ordinal),
                "the snapshot must derive the daily demand from P_eff, the tier rate and the mode");
            TestAssert.True(source.Contains("IndoorWaterMath.Coverage(record.Tank, demand)", StringComparison.Ordinal),
                "the snapshot coverage must be computed from the live tank and demand");
            TestAssert.True(source.Contains("record.Tank", StringComparison.Ordinal),
                "the snapshot must read the live tank, not a daily cache");
            TestAssert.True(source.Contains("IndoorWaterMath.DaysRemaining(record.Tank, demand)", StringComparison.Ordinal),
                "tank days must use the same live demand");
            TestAssert.False(source.Contains("_lastCoverage", StringComparison.Ordinal),
                "the stale coverage cache must not feed the panel snapshot");
            TestAssert.False(source.Contains("SettleDay(", StringComparison.Ordinal),
                "the snapshot is read-only: it must never settle/consume water");
        }

        /// <summary>
        /// The panel derives the shortage row from the live C vs C_min and only below G_irr = 1; the
        /// read-model factory must keep using the shipped Core formula (no duplicated constants).
        /// </summary>
        public static void TestPanelDerivesIrrigationFromLiveCoverage()
        {
            // The water card builder moved to its own partial (300-line soft gate); read the whole
            // read-model family so the contract stays independent of how the factory is split.
            var source = ReadUiPanelSource("HdbfIndoorPanelFactory.cs")
                         + ReadUiPanelSource("HdbfIndoorPanelFactory.Cards.cs")
                         + ReadUiPanelSource("HdbfIndoorPanelFactory.Cards.Water.cs");
            TestAssert.True(source.Contains("IndoorWaterMath.IrrigationMultiplier", StringComparison.Ordinal),
                "the panel must use the Core G_irr formula instead of guessing a percentage");
            TestAssert.True(source.Contains("irrigation < 0.999f", StringComparison.Ordinal),
                "the shortage row must only appear when the irrigation multiplier is derated");
            TestAssert.True(source.Contains("PanelWaterShortage", StringComparison.Ordinal),
                "the shortage row must render the localized sentence with the derate percentage");
        }

        private static string ReadControllerSource(string fileName)
        {
            var resource = "HDBF.Tests.Source.Controller." + fileName;
            var assembly = typeof(V2UiLiveWaterContractTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                throw new TestFailureException("missing embedded controller source " + resource);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static string ReadUiPanelSource(string fileName)
        {
            var root = RepoRoot();
            return File.ReadAllText(Path.Combine(root, "src", "HDBF.UI", "Panels", fileName), Encoding.UTF8);
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2UiLiveWaterContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>();
            foreach (var attribute in metadata)
            {
                if (string.Equals(attribute.Key, RepoRootMetadataKey, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return attribute.Value!;
                }
            }

            throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
        }
    }
}
