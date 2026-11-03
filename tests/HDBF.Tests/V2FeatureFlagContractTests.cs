// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-CONFIG-02 (L-18)
// Purpose: L-18 config feature-flag contract: every FeatureFlags property has a consumer in the
//          non-config source, the shipped defaults do not change v2.5.5 behaviour, and each flag's
//          "off" behaviour is pinned by a Core behaviour test (maintenance never expires, soil
//          fatigue never accumulates/reduces yield, rotation never cycles). Also pins the gameplay
//          wiring/log tokens for the port/maintenance switches.
// Requirements: REQ-V2-EXT-04, REQ-V2-BAL-01, REQ-V2-BAL-02, REQ-ARCH-09, L-18
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    public static class V2FeatureFlagContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string ConfigLayer = "src/HDBF.Core/Config/";

        // ---------------------------------------------------------------- defaults

        public static void TestFeatureFlagDefaultsKeepTheV255Behaviour()
        {
            var features = HdbfConfig.Default.Features;
            // Every flag is on except logisticsOutput (table 10-3). The five flags newly wired by
            // L-18 must not flip their shipped defaults: only the logistics port semantics changes
            // (logisticsOutput=false now really forces that port off).
            TestAssert.True(features.DensePlanting);
            TestAssert.True(features.Rotation);
            TestAssert.True(features.Maintenance);
            TestAssert.True(features.AutoMaintenance);
            TestAssert.True(features.PowerConnection);
            TestAssert.True(features.PipeConnection);
            TestAssert.False(features.LogisticsOutput);
            TestAssert.True(features.SoilFatigue);
            TestAssert.Equal(8, typeof(FeatureFlags).GetProperties().Length,
                "the documented feature set stays the eight chapter 9.3 flags");
        }

        public static void TestDefaultFeatureTogglesMeanEveryFeatureEnabled()
        {
            var toggles = default(IndoorFeatureToggles);
            TestAssert.False(toggles.MaintenanceDisabled);
            TestAssert.False(toggles.SoilFatigueDisabled);
            TestAssert.False(toggles.RotationDisabled);
        }

        // ---------------------------------------------------------------- consumer scan

        public static void TestEveryFeatureFlagHasAConsumerOutsideTheConfigLayer()
        {
            var root = RepoRoot();
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.StartsWith(ConfigLayer, StringComparison.Ordinal))
                {
                    // The config layer parses the flags; it is not a consumer of their behaviour.
                    continue;
                }

                sources[relative] = File.ReadAllText(path);
            }

            TestAssert.True(sources.Count > 50, "the src scan looks empty: " + sources.Count + " files");
            var missing = new List<string>();
            foreach (var property in typeof(FeatureFlags).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pattern = new Regex(@"Features\s*(?:\(\s*\))?\s*\.\s*" + Regex.Escape(property.Name) + @"\b");
                var consumer = sources.FirstOrDefault(pair => pattern.IsMatch(pair.Value));
                if (consumer.Key is null)
                {
                    missing.Add(property.Name);
                }
            }

            TestAssert.Equal(0, missing.Count,
                "FeatureFlags properties parsed but never wired to a behaviour (L-18): "
                + string.Join(", ", missing));
        }

        // ---------------------------------------------------------------- maintenance

        public static void TestMaintenanceDisabledNeverExpiresAndKeepsTheShippedTickWhenEnabled()
        {
            var disabledRecord = MaterialTierRecord();
            disabledRecord.MaintenanceDueDay = 0f;
            var disabled = Tick(disabledRecord, new IndoorFeatureToggles { MaintenanceDisabled = true }, 100f);
            TestAssert.False(disabled.Stalled, "features.maintenance=false must never stall the farm");
            TestAssert.Equal(1f, disabled.GrowthMultiplier, 1e-4f,
                "G_maint must stay 1 with the maintenance feature disabled");
            TestAssert.False(Has(disabled.Warnings, IndoorWarningFlags.MaintenanceDue),
                "maintenance can never become due with the feature disabled");
            TestAssert.False(Has(disabled.Warnings, IndoorWarningFlags.MissingMaterial));
            TestAssert.Equal(0, disabledRecord.MaintenanceCyclesMissed,
                "the disabled feature must not persist a missed-cycle countdown");
            TestAssert.Equal(0f, disabledRecord.MaintenanceDueDay,
                "the disabled feature must not schedule a new due day (REQ-ARCH-09)");

            // Negative control: the same record with the shipped defaults really is overdue/stalled,
            // so the assertions above observe the flag and not a vacuous record.
            var enabledRecord = MaterialTierRecord();
            enabledRecord.MaintenanceDueDay = 0f;
            var enabled = Tick(enabledRecord, default, 100f);
            TestAssert.True(enabled.Stalled, "the shipped defaults keep the overdue maintenance stall");
            TestAssert.True(Has(enabled.Warnings, IndoorWarningFlags.MaintenanceDue));
            TestAssert.True(Has(enabled.Warnings, IndoorWarningFlags.MissingMaterial));
            TestAssert.True(enabledRecord.MaintenanceCyclesMissed > 0);
            TestAssert.True(enabled.GrowthMultiplier < 1f, "the stall derates growth as before");
        }

        // ---------------------------------------------------------------- soil fatigue

        public static void TestSoilFatigueDisabledSkipsAccumulationAndPenalty()
        {
            var disabledRecord = BasicRecord();
            var mature = disabledRecord.Slots[0];
            mature.CropId = "Carrot";
            mature.LastCropId = "Carrot";
            mature.Stage = SlotStage.Mature;
            mature.Fatigue = 1f;
            var disabledSink = new RecordingSink();
            var disabled = IndoorLaborMath.RunActions(disabledRecord, BasicTier(), FallbackCropCatalog.Default,
                1, disabledSink, true, 0, 1f, true, soilFatigueDisabled: true, rotationDisabled: false);
            TestAssert.Equal(1, disabled.Harvested);
            TestAssert.Equal(3, disabledSink.Total,
                "G_fat must be 1 with features.soilFatigue=false even for a save that carries fatigue=1");
            TestAssert.Equal(1f, mature.Fatigue,
                "the disabled feature must not add fatigue (the saved value stays untouched)");

            // Negative control: the shipped behaviour still applies G_fat = 1 - 0.6 x F.
            var enabledRecord = BasicRecord();
            var tired = enabledRecord.Slots[0];
            tired.CropId = "Carrot";
            tired.LastCropId = "Carrot";
            tired.Stage = SlotStage.Mature;
            tired.Fatigue = 1f;
            var enabledSink = new RecordingSink();
            var enabled = IndoorLaborMath.RunActions(enabledRecord, BasicTier(), FallbackCropCatalog.Default,
                1, enabledSink, true, 0, 1f, true, soilFatigueDisabled: false, rotationDisabled: false);
            TestAssert.Equal(1, enabled.Harvested);
            TestAssert.Equal(1, enabledSink.Total, "fatigue=1 keeps the 0.4 yield penalty by default");

            // The panel multiplier must report G_fat = 1 as well; the formula has no division by fatigue.
            // A dry tank keeps the auto-fill planting pass out of the picture, so the only crop of the
            // snapshot is the tired one under test.
            var panelDisabled = BasicRecord();
            panelDisabled.Tank = 0f;
            panelDisabled.Slots[0].CropId = "Carrot";
            panelDisabled.Slots[0].Stage = SlotStage.Growing;
            panelDisabled.Slots[0].Fatigue = 1f;
            var disabledResult = Tick(panelDisabled, new IndoorFeatureToggles { SoilFatigueDisabled = true });
            TestAssert.Equal(1f, disabledResult.YieldMultiplier, 1e-4f);

            var panelEnabled = BasicRecord();
            panelEnabled.Tank = 0f;
            panelEnabled.Slots[0].CropId = "Carrot";
            panelEnabled.Slots[0].Stage = SlotStage.Growing;
            panelEnabled.Slots[0].Fatigue = 1f;
            var enabledResult = Tick(panelEnabled, default);
            TestAssert.Equal(0.4f, enabledResult.YieldMultiplier, 1e-4f);
            TestAssert.Equal(1f, IndoorGrowthMath.FatigueYieldMultiplier(0f));
            TestAssert.Equal(3f, IndoorGrowthMath.Yield(3, 0f, IndoorMode.Normal), 1e-5f,
                "the fatigue formula must stay finite at fatigue = 0 (no division by fatigue)");
        }

        // ---------------------------------------------------------------- rotation

        public static void TestRotationDisabledAlwaysPlantsTheFirstFactionCrop()
        {
            var disabledRecord = BasicRecord();
            IndoorLaborMath.RunActions(disabledRecord, BasicTier(), FallbackCropCatalog.Default, 1,
                new RecordingSink(), true, 0, 1f, true, soilFatigueDisabled: false, rotationDisabled: true);
            TestAssert.Equal("Carrot", disabledRecord.Slots[0].CropId,
                "the disabled rotation plants the first faction crop");
            for (var index = 0; index < 10; index++)
            {
                TestAssert.Equal("Carrot", disabledRecord.Slots[index].CropId);
            }

            // Second pass: with the previous crop recorded, a disabled rotation must still plant Carrot
            // (never Potato), while the shipped rotation policy moves to the next table entry.
            ResetSlot(disabledRecord, 0, "Carrot");
            IndoorLaborMath.RunActions(disabledRecord, BasicTier(), FallbackCropCatalog.Default, 1,
                new RecordingSink(), true, 0, 1f, true, soilFatigueDisabled: false, rotationDisabled: true);
            TestAssert.Equal("Carrot", disabledRecord.Slots[0].CropId);

            var enabledRecord = BasicRecord();
            IndoorLaborMath.RunActions(enabledRecord, BasicTier(), FallbackCropCatalog.Default, 1,
                new RecordingSink(), true, 0, 1f, true, soilFatigueDisabled: false, rotationDisabled: false);
            ResetSlot(enabledRecord, 0, "Carrot");
            IndoorLaborMath.RunActions(enabledRecord, BasicTier(), FallbackCropCatalog.Default, 1,
                new RecordingSink(), true, 0, 1f, true, soilFatigueDisabled: false, rotationDisabled: false);
            TestAssert.Equal("Potato", enabledRecord.Slots[0].CropId,
                "the shipped rotation keeps cycling Carrot -> Potato");
        }

        // ---------------------------------------------------------------- gameplay wiring

        public static void TestControllerWiresAndLogsTheDisabledFeatureFlags()
        {
            var components = "src/HDBF.Gameplay/Components/";
            var policy = Read(components + "HdbfBoxFarmController.Commands.CropPolicy.cs");
            var commands = Read(components + "HdbfBoxFarmController.Commands.cs");
            var context = Read(components + "HdbfBoxFarmController.Context.cs");
            var tick = Read(components + "HdbfBoxFarmController.Tick.cs");
            var activation = Read(components + "HdbfBoxFarmController.Activation.cs");
            var maintenance = Read(components + "HdbfBoxFarmController.Maintenance.cs");

            // One-time indoor.feature-disabled log per disabled flag, from one place.
            TestAssert.Equal("indoor.feature-disabled", IndoorLogKeys.FeatureDisabled);
            foreach (var flag in new[]
                     {
                         "features.maintenance", "features.pipeConnection", "features.logisticsOutput",
                         "features.rotation", "features.soilFatigue",
                     })
            {
                TestAssert.True(policy.Contains("\"" + flag + "\"", StringComparison.Ordinal),
                    "the L-18 activation must name the disabled flag in its log: " + flag);
            }

            TestAssert.True(policy.Contains("IndoorLogKeys.FeatureDisabled", StringComparison.Ordinal));
            TestAssert.True(policy.Contains("record.PipePort = false", StringComparison.Ordinal),
                "features.pipeConnection=false must force the pipe port off");
            TestAssert.True(policy.Contains("record.LogisticsPort = false", StringComparison.Ordinal),
                "features.logisticsOutput=false must force the logistics port off");
            TestAssert.True(policy.Contains("CropPolicy.Fixed", StringComparison.Ordinal)
                            && policy.Contains("FixedCropId = firstCrop", StringComparison.Ordinal),
                "features.rotation=false must convert a rotation record to a fixed crop at activation");
            TestAssert.True(policy.Contains("policy == CropPolicy.Rotation && !SafeFeatures().Rotation",
                    StringComparison.Ordinal)
                            && policy.Contains("failureKey = HdbfIndoorCommandFailures.InvalidMode;",
                                StringComparison.Ordinal),
                "features.rotation=false must reject TrySetCropPolicy(Rotation) with an existing failure key");
            TestAssert.True(activation.Contains("ApplyFeatureFlags();", StringComparison.Ordinal),
                "the activation path must apply the feature flags before the first daily tick");

            // Runtime consumers of the five newly wired flags.
            TestAssert.True(commands.Contains("SafeFeatures().PipeConnection", StringComparison.Ordinal));
            TestAssert.True(commands.Contains("SafeFeatures().LogisticsOutput", StringComparison.Ordinal));
            TestAssert.True(commands.Contains("SafeFeatures().Maintenance", StringComparison.Ordinal));
            TestAssert.True(context.Contains("SafeFeatures().PipeConnection", StringComparison.Ordinal),
                "the daily pipe draw must stop when the pipe feature is disabled");
            TestAssert.True(maintenance.Contains("SafeFeatures().Maintenance", StringComparison.Ordinal),
                "the automatic maintenance pass and the panel bill must stop when maintenance is disabled");
            TestAssert.True(tick.Contains("Features = new IndoorFeatureToggles", StringComparison.Ordinal),
                "the daily tick must pass the maintenance/soil-fatigue/rotation switches to Core");
        }

        // ---------------------------------------------------------------- helpers

        private sealed class RecordingSink : IInventorySink
        {
            public int Total;
            public bool TryAdd(string goodId, int amount)
            {
                Total += amount;
                return true;
            }
        }

        private static IndoorFarmRecord BasicRecord() =>
            IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);

        private static IndoorFarmRecord MaterialTierRecord()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Intermediate, HdbfFaction.Folktails);
            record.MaintenanceDueDay = 0f;
            return record;
        }

        private static TierProfile BasicTier() => TierCatalog.Get(HdbfTier.Basic);

        private static IndoorDayResult Tick(IndoorFarmRecord record, IndoorFeatureToggles features,
            float day = 1f)
        {
            var context = new IndoorDayContext
            {
                ActiveWorkers = 1,
                CurrentDay = day,
                Output = new RecordingSink(),
                Materials = null,
                Features = features,
            };
            return IndoorDayTick.Run(record, TierCatalog.Get(record.Tier), FallbackCropCatalog.Default,
                in context);
        }

        private static void ResetSlot(IndoorFarmRecord record, int index, string lastCropId)
        {
            var slot = record.Slots[index];
            slot.CropId = string.Empty;
            slot.LastCropId = lastCropId;
            slot.GrowthDays = 0f;
            slot.Stage = SlotStage.Empty;
        }

        private static bool Has(IndoorWarningFlags flags, IndoorWarningFlags expected) =>
            (flags & expected) == expected;

        private static string Read(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new TestFailureException("source file not found: " + path);
            }

            return File.ReadAllText(path);
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2FeatureFlagContractTests).Assembly
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
