// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GAMEPLAY-02
// Purpose: Wiring contract of the v2 gameplay closure (manual refill 2-action cost + no cross-day
//          overspend + real source, maintenance bills/no overdraw, power cut 0.60, stall stops
//          actions only, mode-transition lower yield, four world-icon classes) + source contract.
// Requirements: REQ-V2-WATER-02/04, REQ-V2-LABOR-01, REQ-V2-BAL-01/02/03, REQ-V2-UI-02,
//               REQ-V2-READ-03, REQ-V2-SAFE-01, REQ-V2-MAINT-02/03, REQ-V2-TEST-01
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2GameplayWiringTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string Components = "src/HDBF.Gameplay/Components/";

        public static void TestManualRefillTakesTwoActionsFromTodaysBudget()
        {
            var record = Record(HdbfTier.Basic);
            record.Tank = 0f;
            FillMature(record, 13);
            var result = Tick(record,
                Context(1, refill: true, manual: new TestWater(), output: new RecordingSink()));
            TestAssert.Equal(ManualRefillOutcome.Completed, result.ManualRefill);
            TestAssert.Equal(2, result.RefillActionsUsed, "the trip costs exactly 2 farm actions");
            TestAssert.Equal(11, result.Actions.Harvested, "13-action budget minus the 2-action refill");
            TestAssert.True(record.Tank > 0f, "a real source must fill the tank");
            TestAssert.Equal(0, IndoorLaborMath.ActionBudget(1, 99), "reserved actions never go negative");
        }

        public static void TestManualRefillNeverBorrowsTheNextDayBudget()
        {
            var record = Record(HdbfTier.Basic);
            FillMature(record, 2);
            var water = new TestWater();
            var dayOne = Tick(record, Context(1, day: 1f, reserved: 12, refill: true, manual: water));
            TestAssert.Equal(ManualRefillOutcome.NoBudget, dayOne.ManualRefill, "13 - 12 reserved < 2");
            TestAssert.Equal(0, dayOne.RefillActionsUsed);
            TestAssert.Equal(100f, water.Supply, 1e-4f, "a rejected trip draws no water");
            var dayTwo = Tick(record, Context(1, day: 2f, refill: true, manual: water));
            TestAssert.Equal(ManualRefillOutcome.Completed, dayTwo.ManualRefill, "the next day has a fresh budget");
            TestAssert.Equal(2, dayTwo.RefillActionsUsed);
        }

        public static void TestManualRefillFailuresAreSpecificAndNeverMoveWater()
        {
            var record = Record(HdbfTier.Basic);
            var noSource = Tick(record, Context(1, refill: true));
            TestAssert.Equal(ManualRefillOutcome.NoSource, noSource.ManualRefill, "never refill without a real source");
            TestAssert.True(Has(noSource.Warnings, IndoorWarningFlags.NoWater));
            var noWorkers = Tick(record, Context(0, refill: true, manual: new TestWater()));
            TestAssert.Equal(ManualRefillOutcome.NoWorkers, noWorkers.ManualRefill);
            TestAssert.True(Has(noWorkers.Warnings, IndoorWarningFlags.NoWorkers));
        }

        public static void TestMaintenanceBillsUseVerifiedItemsAndAmounts()
        {
            TestAssert.False(Bill(HdbfTier.Basic, PowerConnection.NotRequired, false, out _, out _),
                "the basic tier has no material upkeep");
            TestAssert.True(Bill(HdbfTier.Intermediate, PowerConnection.NotRequired, false, out var plank,
                out var planks));
            TestAssert.Equal(IndoorConstants.MaintenanceItemPlank, plank!);
            TestAssert.Equal(8, planks);
            TestAssert.True(Bill(HdbfTier.Advanced, PowerConnection.NotRequired, false, out _, out var metals));
            TestAssert.Equal(6, metals);
            TestAssert.True(Bill(HdbfTier.Top, PowerConnection.OffGrid, false, out _, out var off));
            TestAssert.Equal(15, off, "off-grid top tier: 15 metal / 8 days");
            TestAssert.True(Bill(HdbfTier.Top, PowerConnection.Grid, false, out _, out var cutMetal));
            TestAssert.Equal(30, cutMetal, "a power cut doubles the upkeep");
            TestAssert.False(Bill(HdbfTier.Top, PowerConnection.Grid, true, out _, out _),
                "a grid-connected top tier is maintained by the network");

            var materials = new Materials { Stock = 5 };
            TestAssert.True(IndoorMaintenanceMath.IsMaterialMissing(materials, Tier(HdbfTier.Intermediate),
                Record(HdbfTier.Intermediate)), "5 < 8 planks is a shortage");
            TestAssert.False(materials.TryConsume("Plank", 8), "all-or-nothing withdrawal must refuse");
            TestAssert.Equal(5, materials.Stock, "a refused withdrawal deducts nothing");
        }

        public static void TestPowerCutIsSixtyPercentAndStallOnlyStopsActions()
        {
            TestAssert.Equal(0.6f, IndoorPowerMath.Efficiency(true, false), 1e-6f);
            var cut = Record(HdbfTier.Top);
            cut.PowerMode = PowerConnection.Grid;
            cut.MaintenanceDueDay = 100f;
            cut.Tank = 76.8f;
            var cutResult = Tick(cut, Context(2, day: 10f));
            TestAssert.Equal(0.6f, cutResult.GrowthMultiplier, 1e-4f, "G_power = 0.60 on a power cut");
            TestAssert.True(Has(cutResult.Warnings, IndoorWarningFlags.NoPower));

            var stalled = Record(HdbfTier.Top);
            stalled.MaintenanceDueDay = 0f;
            stalled.Tank = 76.8f;
            var mature = stalled.Slots[0];
            mature.CropId = "Carrot";
            mature.GrowthDays = 4f;
            mature.Stage = SlotStage.Mature;
            var growing = stalled.Slots[1];
            growing.CropId = "Carrot";
            growing.Stage = SlotStage.Growing;
            var stallResult = Tick(stalled, Context(2, day: 10f));
            TestAssert.True(stallResult.Stalled);
            TestAssert.Equal(0, stallResult.Actions.Harvested, "a stall stops farm actions only");
            TestAssert.True(growing.GrowthDays > 0f, "crops keep growing during a stall");
        }

        public static void TestModeTransitionSettlesAtTheLowerYield()
        {
            var dense = Record(HdbfTier.Basic, IndoorMode.Dense);
            FillMature(dense, 1);
            var transitionSink = new RecordingSink();
            var transition = Tick(dense, Context(2, output: transitionSink, previous: IndoorMode.Normal));
            TestAssert.True(transition.TransitionYieldScale < 1f, "the transition day is derated");
            var reference = Record(HdbfTier.Basic, IndoorMode.Dense);
            FillMature(reference, 1);
            var referenceSink = new RecordingSink();
            TestAssert.Equal(1f, Tick(reference, Context(2, output: referenceSink)).TransitionYieldScale, 1e-6f);
            TestAssert.True(transitionSink.Total < referenceSink.Total, "the transition day uses the lower yield");
        }

        public static void TestWorldIconMapHasExactlyFourClassesAndWaterFacts()
        {
            var flags = new[]
            {
                IndoorWarningFlags.NoWater, IndoorWarningFlags.LowWater, IndoorWarningFlags.NoPower,
                IndoorWarningFlags.MaintenanceStalled, IndoorWarningFlags.MaintenanceDue,
                IndoorWarningFlags.EntranceBlocked,
            };
            var kinds = flags.Select(IndoorWarningMap.IconKindOf)
                .Where(kind => kind != IndoorWarningIconKind.None).Distinct().ToArray();
            TestAssert.Equal(4, kinds.Length, "world icons keep exactly four classes");
            TestAssert.Equal(IndoorWarningIconKind.Water, IndoorWarningMap.IconKindOf(IndoorWarningFlags.NoWater));
            TestAssert.Equal(IndoorWarningIconKind.None,
                IndoorWarningMap.IconKindOf(IndoorWarningFlags.OutputFull | IndoorWarningFlags.NoWorkers));
            TestAssert.Equal(IndoorWarningKeys.NoWater,
                IndoorWarningMap.WarningKeyOf(IndoorWarningIconKind.Water));
            TestAssert.Equal(IndoorWarningKeys.EntranceBlocked,
                IndoorWarningMap.WarningKeyOf(IndoorWarningIconKind.EntranceBlocked));
            TestAssert.Equal("Water", IndoorConstants.WaterGoodId);
            TestAssert.Equal(0.2f, IndoorConstants.WaterVolumeCubicMetersPerUnit, 1e-6f);
            TestAssert.Equal(12, IndoorConstants.ManualRefillRadiusCells);
            TestAssert.Equal(2, IndoorConstants.ManualRefillActions);
            TestAssert.True(IndoorLogKeys.MaintenanceMissing.StartsWith("indoor.", StringComparison.Ordinal)
                            && IndoorLogKeys.ManualRefill.StartsWith("water.", StringComparison.Ordinal)
                            && IndoorLogKeys.PowerCut.StartsWith("indoor.", StringComparison.Ordinal),
                "new log keys follow the ARCHITECTURE section-6 prefixes");
        }

        public static void TestGameplayWiringSourceContracts()
        {
            AssertTokens("HdbfBoxFarmController.ManualRefill.cs", "TryRequestManualRefill",
                "IndoorConstants.ManualRefillActions", "catch (Exception");
            AssertTokens("HdbfManualWaterSource.cs", "INavigationService", "ClosestPositionOnNavMesh", "catch (Exception");
            // BUG-09 fix (manual refill never found a walk start): a finished building owns two
            // Accessible decorators, so the ambiguous single lookup must never come back. The walk
            // start is enumerated from the access components, with the entrance doorstep and the
            // nearest nav mesh position as fallbacks.
            AssertTokens("HdbfManualWaterSource.WalkStart.cs", "GetComponents", "UnblockedSingleAccess",
                "PositionedEntrance", "DoorstepCoordinates", "ClosestPositionOnNavMesh", "catch (Exception");
            foreach (var walkStartFile in new[] { "HdbfManualWaterSource.cs", "HdbfManualWaterSource.WalkStart.cs" })
            {
                TestAssert.False(Read(Components + walkStartFile).Contains("GetComponent<Accessible>", StringComparison.Ordinal),
                    walkStartFile + " must not use the ambiguous GetComponent<Accessible>() lookup");
            }

            AssertTokens("HdbfManualWaterSource.Search.cs", "ManualRefillRadiusCells", "TryFindWaterInventory");
            AssertTokens("HdbfManualWaterSource.Sources.cs", "IThreadSafeWaterMap", "RemoveCleanWater", "TakeExisting",
                "catch (Exception");
            AssertTokens("HdbfFarmCommandService.cs", "TryPerformMaintenance", "TryRequestManualRefill",
                "TrySetCropPolicy", "catch (Exception");
            AssertTokens("HdbfWorldIconService.Evaluate.cs", "IndoorWarningMap.IconKindOf",
                "IndoorIconPolicy.ShouldBlink");
            AssertTokens("HdbfWorldIconService.Update.cs", "IndoorIconPolicy.IsWorldIconVisible",
                "IndoorIconPolicy.IsDue");
            AssertTokens("HdbfMaintenanceTransaction.cs", "IndoorLogKeys.MaintenanceMissing", "HasUnreservedStock",
                "catch (Exception");
            AssertTokens("HdbfBoxFarmController.Tick.cs", "IndoorDayTick.Run", "UpdatePower", "UpdatePorts",
                "ReservedActions", "PreviousMode", "catch (Exception");
            var tickSources = Read("src/HDBF.Core/Indoor/IndoorDayTick.cs");
            TestAssert.Equal(2, tickSources.Split(new[] { "TryDrawSource" }, StringSplitOptions.None).Length - 1,
                "water is drawn exactly twice per day (pipe + one manual trip), never per frame");
        }

        public static void TestNewGameplaySourcesStaySmallAndPerFrameFree()
        {
            foreach (var file in new[]
                     {
                         "HdbfBoxFarmController.ManualRefill.cs", "HdbfManualWaterSource.cs",
                         "HdbfManualWaterSource.Search.cs", "HdbfManualWaterSource.Sources.cs",
                         "HdbfManualWaterSource.WalkStart.cs",
                         "HdbfMaintenanceTransaction.cs", "HdbfWorldIconService.Evaluate.cs",
                     })
            {
                var text = Read(Components + file);
                var lines = text.Split('\n').Length - (text.EndsWith("\n", StringComparison.Ordinal) ? 1 : 0);
                TestAssert.True(lines <= 300, file + " must stay within the 300-line gate");
                TestAssert.True(text.StartsWith("// ============================================================================",
                    StringComparison.Ordinal), file + " is missing the fixed header");
                foreach (var forbidden in new[]
                         { "Time.deltaTime", "TickableComponent", "void Tick(", "void Update()", "static List<" })
                {
                    TestAssert.False(text.Contains(forbidden, StringComparison.Ordinal),
                        file + " must not run per frame / keep static mutable state: " + forbidden);
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static IndoorDayContext Context(int workers, float day = 1f, int reserved = 0,
            bool refill = false, IWaterSource? manual = null, IInventorySink? output = null,
            IndoorMode? previous = null) => new()
        {
            ActiveWorkers = workers, CurrentDay = day, ReservedActions = reserved,
            ManualRefillRequested = refill, ManualWater = manual, Output = output, PreviousMode = previous,
        };

        private static IndoorFarmRecord Record(HdbfTier tier, IndoorMode mode = IndoorMode.Normal)
        {
            var record = IndoorFarmRecord.Create(tier, HdbfFaction.Folktails);
            record.Mode = mode;
            return record;
        }

        private static bool Bill(HdbfTier tier, PowerConnection mode, bool connected, out string? item, out int amount)
        {
            var record = Record(tier);
            record.PowerMode = mode;
            record.PowerConnected = connected;
            return IndoorMaintenanceMath.TryGetMaterialBill(Tier(tier), record, out item, out amount);
        }

        private static void FillMature(IndoorFarmRecord record, int count)
        {
            for (var index = 0; index < count && index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = "Carrot";
                slot.LastCropId = "Carrot";
                slot.GrowthDays = 4f;
                slot.Stage = SlotStage.Mature;
            }
        }

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorDayContext context) =>
            IndoorDayTick.Run(record, Tier(record.Tier), FallbackCropCatalog.Default, in context);

        private static TierProfile Tier(HdbfTier tier) => TierCatalog.Get(tier);

        private static bool Has(IndoorWarningFlags flags, IndoorWarningFlags expected) => (flags & expected) == expected;

        private static void AssertTokens(string file, params string[] tokens)
        {
            var text = Read(Components + file);
            foreach (var token in tokens)
            {
                TestAssert.True(text.Contains(token, StringComparison.Ordinal), file + " must contain " + token);
            }
        }

        private static string Read(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new TestFailureException("source file not found: " + path);
            return File.ReadAllText(path);
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2GameplayWiringTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException("assembly metadata \"" + RepoRootKey + "\" is missing");
            }

            return metadata.Value!;
        }

        private sealed class RecordingSink : IInventorySink
        {
            public int Total;
            public bool TryAdd(string goodId, int amount) { Total += amount; return true; }
        }

        private sealed class TestWater : IWaterSource
        {
            public float Supply = 100f;
            public WaterSupplyKind Kind => WaterSupplyKind.Pipe;
            public bool IsAvailable => Supply > 0f;
            public bool TryDraw(float amount, out float actual)
            {
                actual = Math.Min(amount, Supply);
                Supply -= actual;
                return actual > 0f;
            }
        }

        private sealed class Materials : IMaintenanceMaterialSource
        {
            public int Stock;
            public int Available(string itemId) => Stock;
            public bool TryConsume(string itemId, int count)
            {
                if (count <= 0 || count > Stock) return false;
                Stock -= count;
                return true;
            }
        }
    }
}
