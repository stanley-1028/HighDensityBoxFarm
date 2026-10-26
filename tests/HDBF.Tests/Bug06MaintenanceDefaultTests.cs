using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Save;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 regressions for BUG-06 (round-14 in-game evidence; docs/VERIFICATION.md §20.3).
    ///
    /// An injected top-tier off-grid farm with no HDBF persisted record loaded with
    /// <c>maintenance.dueDay = 0</c>, because the old default came from the top tier's on-grid
    /// <c>MaintenanceCycleDays = 0</c>. The first daily tick therefore already counted one missed
    /// cycle, so <c>G_maint = 0.5</c> and the measured growth rate was 0.198 =
    /// G_irr 0.663 x G_power 0.60 x G_maint 0.50. Requirement 12.1/12.2 "missing field safe
    /// defaults" requires a missing maintenance date to become
    /// <c>currentDay + maintenance cycle</c> with <c>cyclesMissed = 0</c>:
    /// top tier -> its off-grid cycle (8 days), intermediate/advanced -> 10 days, basic -> 0.
    ///
    /// The fix has a single Core source of truth (<see cref="MaintenanceDefaults"/>), used by
    /// <see cref="HdbfSaveCodec.CreateDefault"/>, <see cref="HdbfSaveCodec.TrySanitize"/> and
    /// <see cref="HdbfSaveCodec.Migrate"/>. These tests pin the resolver contract, the codec
    /// behaviour for v1 records, the v0 migration path, and prove the existing overdue math did not
    /// change. Game-free: System + HDBF.Core only (REQ-TEST-03).
    /// </summary>
    public static class Bug06MaintenanceDefaultTests
    {
        private static readonly TierProfile Basic = TierCatalog.Get(HdbfTier.Basic);
        private static readonly TierProfile Intermediate = TierCatalog.Get(HdbfTier.Intermediate);
        private static readonly TierProfile Advanced = TierCatalog.Get(HdbfTier.Advanced);
        private static readonly TierProfile Top = TierCatalog.Get(HdbfTier.Top);

        /// <summary>Round-14 load day: the injected farm appeared at this day with no record.</summary>
        private const float Round14LoadDay = 1.18f;

        /// <summary>First daily tick after the round-14 load (diag.day reported day=2.18).</summary>
        private const float Round14TickDay = 2.18f;

        private const float FloatTolerance = 1e-4f;

        // ---------------------------------------------------------------- single source of the cycle

        public static void TestDefaultCycleDaysMatchesTierTable()
        {
            TestAssert.Equal(0f, MaintenanceDefaults.DefaultCycleDays(Basic), FloatTolerance,
                "the irrigation-only basic tier has no material maintenance cycle");
            TestAssert.Equal(10f, MaintenanceDefaults.DefaultCycleDays(Intermediate), FloatTolerance,
                "intermediate uses its 10-day plank cycle");
            TestAssert.Equal(10f, MaintenanceDefaults.DefaultCycleDays(Advanced), FloatTolerance,
                "advanced uses its 10-day metal cycle");
            TestAssert.Equal(8f, MaintenanceDefaults.DefaultCycleDays(Top), FloatTolerance,
                "top uses its 8-day off-grid metal cycle because its on-grid cycle is 0");
        }

        public static void TestConfigOverrideDrivesDefaultCycle()
        {
            // Single source, no magic 8: a JSON/config override must move the load-time default.
            var topTwelve = Top with { OffGridMaintenanceCycleDays = 12f };
            TestAssert.Equal(12f, MaintenanceDefaults.DefaultCycleDays(topTwelve), FloatTolerance,
                "the load-time default must follow the resolved off-grid cycle, not a hard-coded 8");

            var topRecord = HdbfSaveCodec.CreateDefault(11, HdbfTier.Top, topTwelve, 100f);
            TestAssert.Equal(112f, topRecord.DueDay, FloatTolerance,
                "CreateDefault must schedule currentDay + the config-resolved cycle");

            var intermediateSeven = Intermediate with { MaintenanceCycleDays = 7f };
            TestAssert.Equal(7f, MaintenanceDefaults.DefaultCycleDays(intermediateSeven), FloatTolerance,
                "the load-time default must follow the config-resolved material cycle");

            var intermediateRecord = HdbfSaveCodec.CreateDefault(12, HdbfTier.Intermediate, intermediateSeven, 5f);
            TestAssert.Equal(12f, intermediateRecord.DueDay, FloatTolerance,
                "CreateDefault must schedule currentDay + the config-resolved cycle");
        }

        public static void TestCycleDaysForSelectsLivePowerState()
        {
            TestAssert.Equal(8f, MaintenanceDefaults.CycleDaysFor(Top, true), FloatTolerance,
                "an off-grid top farm runs its 8-day material cycle");
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(Top, false), FloatTolerance,
                "a connected top farm is maintained by power, so it has no material cycle");
            TestAssert.Equal(10f, MaintenanceDefaults.CycleDaysFor(Intermediate, false), FloatTolerance,
                "intermediate runs its 10-day cycle");
            TestAssert.Equal(10f, MaintenanceDefaults.CycleDaysFor(Intermediate, true), FloatTolerance,
                "intermediate has no separate off-grid cycle, so it keeps 10");
            TestAssert.Equal(10f, MaintenanceDefaults.CycleDaysFor(Advanced, false), FloatTolerance,
                "advanced runs its 10-day cycle");
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(Basic, false), FloatTolerance,
                "the irrigation-only basic tier has no maintenance cycle");
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(Basic, true), FloatTolerance,
                "the irrigation-only basic tier stays at 0 even off-grid");
        }

        public static void TestCycleDaysForNormalisesInvalidConfigCycle()
        {
            var negative = Intermediate with { MaintenanceCycleDays = -1f };
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(negative, false), FloatTolerance,
                "a negative configured cycle is normalised to 0");
            TestAssert.Equal(0f, MaintenanceDefaults.DefaultCycleDays(negative), FloatTolerance,
                "a negative configured cycle cannot become the load-time default");
            TestAssert.Equal(0f, MaintenanceDefaults.ResolveDueDay(negative, 10f, 0f), FloatTolerance,
                "a profile without a usable cycle keeps dueDay 0 instead of inventing one");

            var nan = Intermediate with { MaintenanceCycleDays = float.NaN };
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(nan, false), FloatTolerance,
                "a NaN configured cycle is normalised to 0");
            TestAssert.Equal(0f, MaintenanceDefaults.DefaultCycleDays(nan), FloatTolerance,
                "a NaN configured cycle cannot become the load-time default");
            TestAssert.Equal(0f, MaintenanceDefaults.ResolveDueDay(nan, 10f, 0f), FloatTolerance,
                "a NaN cycle means no due day");

            var infinite = Intermediate with { MaintenanceCycleDays = float.PositiveInfinity };
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(infinite, false), FloatTolerance,
                "an infinite configured cycle is normalised to 0");
            TestAssert.Equal(0f, MaintenanceDefaults.DefaultCycleDays(infinite), FloatTolerance,
                "an infinite configured cycle cannot become the load-time default");

            var badOffGrid = Top with { OffGridMaintenanceCycleDays = float.NaN };
            TestAssert.Equal(0f, MaintenanceDefaults.DefaultCycleDays(badOffGrid), FloatTolerance,
                "an invalid off-grid top cycle falls back to the on-grid value, which is 0");
            TestAssert.Equal(0f, MaintenanceDefaults.CycleDaysFor(badOffGrid, true), FloatTolerance,
                "an invalid off-grid top cycle is normalised to 0");
        }

        // ---------------------------------------------------------------- CreateDefault (round-14 defect)

        public static void TestCreateDefaultTopSchedulesOffGridCycle()
        {
            var record = HdbfSaveCodec.CreateDefault(0, HdbfTier.Top, Top, 100f);

            TestAssert.Equal(108f, record.DueDay, FloatTolerance,
                "a top-tier farm created at day 100 must be due one 8-day cycle later");
            TestAssert.Equal(0, record.CyclesMissed,
                "a freshly defaulted record has missed no maintenance cycle");
        }

        public static void TestCreateDefaultMaterialTiersScheduleMaterialCycle()
        {
            var intermediate = HdbfSaveCodec.CreateDefault(1, HdbfTier.Intermediate, Intermediate, 5f);
            TestAssert.Equal(15f, intermediate.DueDay, FloatTolerance,
                "intermediate created at day 5 -> due day 15");
            TestAssert.Equal(0, intermediate.CyclesMissed,
                "a freshly defaulted intermediate record has missed no cycle");

            var advanced = HdbfSaveCodec.CreateDefault(2, HdbfTier.Advanced, Advanced, 7.5f);
            TestAssert.Equal(17.5f, advanced.DueDay, FloatTolerance,
                "advanced created at day 7.5 -> due day 17.5");
            TestAssert.Equal(0, advanced.CyclesMissed,
                "a freshly defaulted advanced record has missed no cycle");
        }

        public static void TestCreateDefaultBasicHasNoMaintenanceDate()
        {
            var record = HdbfSaveCodec.CreateDefault(3, HdbfTier.Basic, Basic, 100f);

            TestAssert.Equal(0f, record.DueDay, FloatTolerance,
                "the irrigation-only basic tier has no material maintenance date");
            TestAssert.Equal(0, record.CyclesMissed,
                "the irrigation-only basic tier has no missed cycle");

            var missed = BalanceFormulas.MaintenanceCyclesMissed(
                record.DueDay, 100f, MaintenanceDefaults.DefaultCycleDays(Basic));
            TestAssert.Equal(0, missed,
                "the irrigation-only basic tier can never be counted as overdue");
            TestAssert.Equal(1f, BalanceFormulas.MaintenanceMultiplier(missed),
                "the basic tier always runs at G_maint 1.0");
        }

        public static void TestCreateDefaultTopAtLoadDayDoesNotScheduleToday()
        {
            var record = HdbfSaveCodec.CreateDefault(14, HdbfTier.Top, Top, Round14LoadDay);

            TestAssert.Equal(9.18f, record.DueDay, FloatTolerance,
                "top loaded at day 1.18 with no record must be due at 1.18 + 8 = 9.18");
            TestAssert.False(record.DueDay <= Round14LoadDay,
                "BUG-06: a top-tier record must never be due on its load day (round 14 loaded with dueDay=1.18)");
            TestAssert.Equal(0, record.CyclesMissed,
                "BUG-06: a newly loaded farm must start with cyclesMissed = 0");
        }

        // ---------------------------------------------------------------- round-14 replay

        public static void TestRound14TopLoadStartsOnSchedule()
        {
            var dueDay = MaintenanceDefaults.ResolveDueDay(Top, Round14LoadDay, 0f);

            TestAssert.Equal(9.18f, dueDay, FloatTolerance,
                "a missing top-tier maintenance date becomes load day + the 8-day off-grid cycle");

            var missed = BalanceFormulas.MaintenanceCyclesMissed(
                dueDay, Round14TickDay, MaintenanceDefaults.DefaultCycleDays(Top));

            TestAssert.Equal(0, missed,
                "round-14 regression: the first daily tick must not count a missed cycle");
            TestAssert.Equal(1f, BalanceFormulas.MaintenanceMultiplier(missed),
                "G_maint must be 1.0 after load (round 14 measured 0.5)");
        }

        public static void TestRound14IntermediateLoadStartsOnSchedule()
        {
            var dueDay = MaintenanceDefaults.ResolveDueDay(Intermediate, 5f, 0f);

            TestAssert.Equal(15f, dueDay, FloatTolerance,
                "a missing intermediate maintenance date becomes load day + the 10-day cycle");

            var missed = BalanceFormulas.MaintenanceCyclesMissed(
                dueDay, 6f, MaintenanceDefaults.DefaultCycleDays(Intermediate));

            TestAssert.Equal(0, missed,
                "the first daily tick after loading an intermediate farm must not count a missed cycle");
            TestAssert.Equal(1f, BalanceFormulas.MaintenanceMultiplier(missed),
                "G_maint must still be 1.0 after loading an intermediate farm");
        }

        public static void TestRound14GrowthRateIsNotDeratedByMaintenance()
        {
            // Round 14: growthRate 0.198 = G_irr 0.663 x G_power 0.60 x G_maint 0.50.
            // With BUG-06 fixed the load-day G_maint is 1.0, so the same farm runs at 0.3978.
            var record = HdbfSaveCodec.CreateDefault(14, HdbfTier.Top, Top, Round14LoadDay);
            var missed = BalanceFormulas.MaintenanceCyclesMissed(
                record.DueDay, Round14TickDay, MaintenanceDefaults.CycleDaysFor(Top, true));
            var maintenanceMultiplier = BalanceFormulas.MaintenanceMultiplier(missed);
            var growthRate = BalanceFormulas.GrowthMultiplier(0.663f, 1f, maintenanceMultiplier,
                BalanceFormulas.PowerMultiplier(false));

            TestAssert.Equal(1f, maintenanceMultiplier,
                "a freshly loaded off-grid top farm is on maintenance schedule (G_maint 1.0)");
            TestAssert.Equal(0.3978f, growthRate, FloatTolerance,
                "round-14 fixed growth rate = 0.663 x 0.60 x 1.0 (the log's 0.198 had G_maint = 0.5)");
            TestAssert.False(growthRate < 0.35f,
                "a newly loaded top farm must not be derated as if it had missed a cycle");
        }

        // ---------------------------------------------------------------- TrySanitize v1: missing/unusable dueDay

        public static void TestSanitizeReschedulesInvalidTopDueDay()
        {
            // Top needs no power knowledge in this Core path: the conservative load-time default is
            // the off-grid cycle (8 days), regardless of the live power state.
            AssertInvalidDueDayRescheduled(Top, Round14LoadDay, 9.18f);
        }

        public static void TestSanitizeReschedulesInvalidIntermediateDueDay()
        {
            AssertInvalidDueDayRescheduled(Intermediate, 5f, 15f);
        }

        public static void TestSanitizeKeepsLegalTopDueDayAndMissedCount()
        {
            var record = BuildMinimalRecord(HdbfTier.Top, HdbfBuildingRecord.CurrentSchemaVersion, 42.5f, 2);

            var clean = HdbfSaveCodec.TrySanitize(record, Top, 100f, out var repaired, out var reason);

            TestAssert.True(clean, "a legal v1 top record must sanitise clean; reason=" + reason);
            TestAssert.Null(reason, "no repair reason for a legal due date");
            TestAssert.Equal(42.5f, repaired.DueDay, 0f,
                "an existing valid dueDay must be preserved exactly (no free maintenance reset)");
            TestAssert.Equal(2, repaired.CyclesMissed,
                "a legal overdue counter must not be reset by a reload");
        }

        public static void TestSanitizeKeepsBasicZeroDueDay()
        {
            var record = BuildMinimalRecord(HdbfTier.Basic, HdbfBuildingRecord.CurrentSchemaVersion, 0f, 0);

            var clean = HdbfSaveCodec.TrySanitize(record, Basic, 100f, out var repaired, out var reason);

            TestAssert.True(clean, "the basic tier's documented dueDay 0 must sanitise clean; reason=" + reason);
            TestAssert.Null(reason, "no repair reason for the basic tier's zero due date");
            TestAssert.Equal(0f, repaired.DueDay, 0f,
                "the basic tier's zero due date must stay 0");
        }

        // ---------------------------------------------------------------- v0 migration fills the default once

        public static void TestMigratePrototypeIntermediateFillsDefaultOnce()
        {
            var record = BuildMinimalRecord(HdbfTier.Intermediate, 0, 0f, 0);

            var clean = HdbfSaveCodec.TrySanitize(record, Intermediate, 7f, out var repaired, out var reason);

            TestAssert.True(clean, "a structurally complete v0 record must migrate without degrading; reason=" + reason);
            TestAssert.Null(reason, "a structurally complete v0 record has no repair reason");
            TestAssert.Equal(17f, repaired.DueDay, FloatTolerance,
                "v0 record with no maintenance date -> currentDay 7 + 10-day cycle = 17");
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion,
                "v0 must migrate to the current schema version");
            TestAssert.Equal(0, repaired.CyclesMissed,
                "a migrated v0 record starts with cyclesMissed = 0");
        }

        public static void TestMigratePrototypeTopFillsOffGridDefault()
        {
            var record = BuildMinimalRecord(HdbfTier.Top, 0, 0f, 0);

            var clean = HdbfSaveCodec.TrySanitize(record, Top, 7f, out var repaired, out var reason);

            TestAssert.True(clean, "a structurally complete v0 top record must migrate cleanly; reason=" + reason);
            TestAssert.Equal(15f, repaired.DueDay, FloatTolerance,
                "v0 top record with no maintenance date -> currentDay 7 + 8-day off-grid cycle = 15");
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion,
                "v0 top must migrate to the current schema version");
        }

        public static void TestMigratePrototypeKeepsExistingDueDay()
        {
            var record = BuildMinimalRecord(HdbfTier.Intermediate, 0, 33.5f, 0);

            HdbfSaveCodec.TrySanitize(record, Intermediate, 7f, out var repaired, out _);

            TestAssert.Equal(33.5f, repaired.DueDay, 0f,
                "a v0 record that already carries a due date must keep it exactly");
            TestAssert.Equal(HdbfBuildingRecord.CurrentSchemaVersion, repaired.SchemaVersion,
                "the kept date must still migrate to the current schema version");
        }

        public static void TestMigratePrototypeDefaultIsFilledExactlyOnce()
        {
            var prototype = BuildMinimalRecord(HdbfTier.Intermediate, 0, 0f, 0);
            HdbfSaveCodec.TrySanitize(prototype, Intermediate, 7f, out var migrated, out _);
            TestAssert.Equal(17f, migrated.DueDay, FloatTolerance,
                "the v0 migration fills the default once (currentDay 7 + 10)");

            var cleanAgain = HdbfSaveCodec.TrySanitize(migrated, Intermediate, 8f, out var reloaded,
                out var reason);

            TestAssert.True(cleanAgain, "the migrated v1 record must reload clean; reason=" + reason);
            TestAssert.Equal(17f, reloaded.DueDay, FloatTolerance,
                "a reload one day later must not re-fill the default (that would move 17 to 18)");
            TestAssert.Equal(0, reloaded.CyclesMissed,
                "a reload must not invent missed cycles");
        }

        // ---------------------------------------------------------------- IsUsableDueDay / ResolveDueDay contract

        public static void TestIsUsableDueDayMatrix()
        {
            TestAssert.False(MaintenanceDefaults.IsUsableDueDay(Top, 0f),
                "a top-tier dueDay 0 means 'never scheduled' and is not usable");
            TestAssert.True(MaintenanceDefaults.IsUsableDueDay(Top, 8f),
                "a positive top-tier dueDay is usable");
            TestAssert.False(MaintenanceDefaults.IsUsableDueDay(Intermediate, 0f),
                "an intermediate dueDay 0 means 'never scheduled' and is not usable");
            TestAssert.True(MaintenanceDefaults.IsUsableDueDay(Intermediate, 10f),
                "a positive intermediate dueDay is usable");
            TestAssert.False(MaintenanceDefaults.IsUsableDueDay(Intermediate, float.NaN),
                "a NaN dueDay is never usable");
            TestAssert.True(MaintenanceDefaults.IsUsableDueDay(Basic, 0f),
                "the basic tier's documented zero due date is usable");
            TestAssert.False(MaintenanceDefaults.IsUsableDueDay(Basic, -1f),
                "a negative dueDay is never usable");
        }

        public static void TestResolveDueDayKeepsUsableSavedValue()
        {
            TestAssert.Equal(42.5f, MaintenanceDefaults.ResolveDueDay(Top, 50f, 42.5f), 0f,
                "a saved due date must be returned verbatim so a reload cannot restart the countdown");
            TestAssert.Equal(10f, MaintenanceDefaults.ResolveDueDay(Intermediate, 50f, 10f), 0f,
                "an intermediate saved due date must be returned verbatim");
            TestAssert.Equal(0f, MaintenanceDefaults.ResolveDueDay(Basic, 50f, 0f), 0f,
                "the basic tier keeps its documented zero due date");
            TestAssert.Equal(108f, MaintenanceDefaults.ResolveDueDay(Top, 100f, -3f), FloatTolerance,
                "a negative saved due date is unusable and must be rescheduled one cycle from now");
        }

        public static void TestResolveDueDayNonFiniteCurrentDayFallsBackToZero()
        {
            TestAssert.Equal(0f, MaintenanceDefaults.ResolveDueDay(Top, float.NaN, 0f), 0f,
                "a non-finite current day cannot produce a finite due day");
            TestAssert.Equal(0f, MaintenanceDefaults.ResolveDueDay(Top, float.PositiveInfinity, 0f), 0f,
                "an infinite current day cannot produce a finite due day");
            TestAssert.Equal(0f, MaintenanceDefaults.ResolveDueDay(Intermediate, float.NegativeInfinity, 0f), 0f,
                "a negative infinite current day cannot produce a finite due day");
            TestAssert.Equal(42.5f, MaintenanceDefaults.ResolveDueDay(Top, float.NaN, 42.5f), 0f,
                "a usable saved due date is still kept verbatim even when the current day is unusable");
        }

        // ---------------------------------------------------------------- existing overdue math is unchanged

        public static void TestOverdueMathStillReportsOneAndTwoCycles()
        {
            var oneCycle = BalanceFormulas.MaintenanceCyclesMissed(100f, 100f, 10f);
            TestAssert.Equal(1, oneCycle,
                "today == dueDay still counts as one missed cycle (chapter 11.6)");
            TestAssert.Equal(0.5f, BalanceFormulas.MaintenanceMultiplier(oneCycle),
                "one missed cycle is still G_maint 0.5");

            var twoCycles = BalanceFormulas.MaintenanceCyclesMissed(100f, 110f, 10f);
            TestAssert.Equal(2, twoCycles,
                "one full cycle overdue still counts as two missed cycles");
            TestAssert.Equal(0f, BalanceFormulas.MaintenanceMultiplier(twoCycles),
                "two missed cycles still shut the farm down (G_maint 0)");
        }

        public static void TestOverdueMathNotYetDueIsOnSchedule()
        {
            var missed = BalanceFormulas.MaintenanceCyclesMissed(100f, 99.99f, 10f);

            TestAssert.Equal(0, missed, "a tick before dueDay still counts zero missed cycles");
            TestAssert.Equal(1f, BalanceFormulas.MaintenanceMultiplier(missed),
                "a tick before dueDay still leaves G_maint at 1.0");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Every value a damaged/missing maintenance date can actually hold. All of them must be
        /// replaced by one full cycle from <paramref name="currentDay"/> and must reset the
        /// missed-cycle counter.
        /// </summary>
        private static void AssertInvalidDueDayRescheduled(TierProfile profile, float currentDay, float expectedDueDay)
        {
            var invalidDueDays = new[]
            {
                0f, -5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity,
            };

            foreach (var invalidDueDay in invalidDueDays)
            {
                var record = BuildMinimalRecord(profile.Tier, HdbfBuildingRecord.CurrentSchemaVersion,
                    invalidDueDay, cyclesMissed: 3);
                var label = profile.Tier + " dueDay=" + invalidDueDay;

                var clean = HdbfSaveCodec.TrySanitize(record, profile, currentDay, out var repaired, out var reason);

                TestAssert.False(clean, label + " must be reported as repaired");
                TestAssert.Equal("due-day-invalid", reason, label + " repair reason");
                TestAssert.Equal(expectedDueDay, repaired.DueDay, FloatTolerance,
                    label + " must be rescheduled to currentDay + the profile cycle");
                TestAssert.Equal(0, repaired.CyclesMissed, label + " must reset the missed-cycle counter");
                TestAssert.Equal(profile.Tier, repaired.Tier, label + " must keep the tier");
            }
        }

        /// <summary>
        /// Smallest structurally valid record: only the maintenance date is ever damaged, so any
        /// sanitise reason can only come from the due-day logic under test.
        /// </summary>
        private static HdbfBuildingRecord BuildMinimalRecord(HdbfTier tier, int schemaVersion, float dueDay,
            int cyclesMissed)
        {
            return new HdbfBuildingRecord
            {
                SchemaVersion = schemaVersion,
                EntityId = 14,
                Tier = tier,
                Mode = HdbfMode.Normal,
                PlotIds = Array.Empty<int>(),
                CropIds = Array.Empty<string>(),
                Fatigue = Array.Empty<float>(),
                ExcludedPlotIds = Array.Empty<int>(),
                DueDay = dueDay,
                CyclesMissed = cyclesMissed,
                TankWater = 0f,
                PortPower = false,
                PortPipe = false,
                PortLogistics = false,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal),
            };
        }
    }
}
