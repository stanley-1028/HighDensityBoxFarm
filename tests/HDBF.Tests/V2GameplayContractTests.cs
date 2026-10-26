// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GAMEPLAY-01
// Purpose: Source and Core-API contract of the indoor controller: uses IndoorDayTick,
//          IndoorEntitySaveBridge and ICropCatalog; no v1 world-plot/planting/highlight
//          chain; one daily trigger; guarded save/load; every gameplay file <=300 lines.
// Requirements: REQ-V2-CORE-03, REQ-V2-MAINT-02/03, REQ-V2-LABOR-03/04, REQ-V2-SAFE-01,
//               REQ-V2-SAVE-04/06, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HDBF.Core;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract of V2-GAMEPLAY-01. HDBF.Gameplay references Timberborn and cannot be loaded in
    /// this net9.0 process; its shipped sources are embedded by HDBF.Tests.csproj and pinned as
    /// text, while the Core types they call are exercised directly.
    /// </summary>
    public static class V2GameplayContractTests
    {
        private const int MaxSourceLines = 300;
        private const string ResourcePrefix = "HDBF.Tests.Source.";

        public static void TestControllerUsesFrozenV2CoreContracts()
        {
            var source = ReadControllerSources();
            foreach (var token in new[]
                     {
                         "IndoorDayTick.Run", "IndoorFarmRecord", "IndoorEntitySaveBridge.Save",
                         "IndoorEntitySaveBridge.Load", "ICropCatalog", "IIndoorAdapterHost",
                         "IndoorDayContext", "SaveDirty", "public Guid EntityId", "public long SaveEntityId",
                         "public void Save(", "public void Load(",
                     })
            {
                TestAssert.True(source.Contains(token, StringComparison.Ordinal), "controller must use " + token);
            }

            TestAssert.True(ReadBridgeSources().Contains("IndoorSaveValidator.ValidateAndRepair", StringComparison.Ordinal),
                "the save bridge must validate before writing (REQ-V2-SAVE-04)");
        }

        public static void TestNoV1WorldPlotOrVanillaFarmingChain()
        {
            var all = ReadGameplaySources();
            var forbidden = new[]
            {
                "PlantingService", "NaturalResourceFactory", "PlantNew", "SetField", "ISoilMoistureService",
                "INavigationService", "ITerrainService", "SetPlantingCoordinates", "PlantingAreaValidator",
                "SoilIsMoist", "PlotRegistry", "HdbfPlotScanner", "HdbfPlotCellCodec", "HdbfPlotHighlight",
                "HdbfManagedCropVisibility", "HdbfGrowthRateProvider", "FarmWorkPlanner",
                "HdbfFarmWorkplaceBehavior", "BoxFarmSnapshot ", "BoxFarmSnapshot?", "BoxFarmSnapshot.", "FarmWarning", "HdbfWarningBoard",
                "IGrowthMultiplierSource", "ReadOnlyGrowthRateProvider", "HdbfFarmLocator",
                "IBoxFarmWarningQuery", "IBoxFarmLocator",
            };
            foreach (var token in forbidden)
            {
                TestAssert.False(all.Contains(token, StringComparison.Ordinal),
                    "v2 gameplay source must not contain the v1 token: " + token);
            }
        }

        public static void TestControllerHasOneDailyTriggerAndNoPerFrameSimulation()
        {
            var source = ReadControllerSources();
            TestAssert.True(source.Contains("ITimeTrigger", StringComparison.Ordinal));
            TestAssert.True(source.Contains("DayTriggerDelayDays = 1f", StringComparison.Ordinal),
                "the trigger delay must be exactly one game day");
            TestAssert.True(source.Contains("_timeTriggerFactory.Create(OnDayTick, DayTriggerDelayDays)",
                StringComparison.Ordinal), "the day tick must be driven by ITimeTrigger");
            TestAssert.True(source.Contains("trigger.Reset();", StringComparison.Ordinal)
                            && source.Contains("trigger.Resume();", StringComparison.Ordinal),
                "the daily trigger must be re-armed once per callback");
            foreach (var forbidden in new[] { "TickableComponent", "void Tick(", "override void Update",
                         "Time.deltaTime", "UnityEngine.Time" })
            {
                TestAssert.False(source.Contains(forbidden, StringComparison.Ordinal),
                    "the controller must not simulate per frame: " + forbidden);
            }
        }

        public static void TestSaveAndLoadAreGuardedAndFailSafe()
        {
            var source = ReadControllerSources() + ReadBridgeSources();
            TestAssert.True(source.Split(new[] { "catch (Exception" }, StringSplitOptions.None).Length - 1 >= 6,
                "every save/load game boundary needs its own catch");
            TestAssert.False(ReadBridgeSources().Contains("throw new", StringComparison.Ordinal),
                "the save bridge must not throw at a game boundary");
            TestAssert.True(source.Contains("_saveDirty = !saved", StringComparison.Ordinal),
                "a failed save must keep the dirty flag set");
            TestAssert.True(source.Contains("safe default", StringComparison.Ordinal)
                            || source.Contains("safe defaults", StringComparison.Ordinal),
                "a failed load must keep the documented safe default record");
        }

        public static void TestCropCatalogReadsRealFarmhouseSpecsWithFallback()
        {
            var source = ReadCatalogSources();
            foreach (var token in new[] { "PlantableSpec", "ResourceGroup", "Farmhouse", "GrowableSpec",
                         "GrowthTimeInDays", "CuttableSpec", "Yielder", "FallbackCropCatalog",
                         "IndoorLogKeys.CropSpecMissing", "ICropCatalog" })
            {
                TestAssert.True(source.Contains(token, StringComparison.Ordinal), "crop catalog must use " + token);
            }

            TestAssert.True(source.Contains("ResolveFaction", StringComparison.Ordinal),
                "the building faction must be resolved from the blueprint/template name");
        }

        public static void TestWorkplaceBehaviorDispatchesNoVanillaWork()
        {
            var source = ReadSourceEnding("IndoorFarmWorkplaceBehavior.cs");
            TestAssert.True(source.Contains("WorkplaceBehavior", StringComparison.Ordinal));
            TestAssert.True(source.Contains("Decision.ReleaseNow()", StringComparison.Ordinal));
            foreach (var forbidden in new[] { "Plant", "Harvest", "Yielder", "NaturalResource", "Planter" })
            {
                TestAssert.False(source.Contains(forbidden, StringComparison.Ordinal),
                    "the indoor workplace behavior must not dispatch vanilla work: " + forbidden);
            }
        }

        public static void TestConfiguratorRegistersV2ServicesAndNoPlotPipeline()
        {
            var configurator = ReadSourceEnding("HdbfGameConfigurator.cs");
            foreach (var token in new[] { "Bind<HdbfBoxFarmController>().AsTransient()",
                         "Bind<GameCropCatalog>().AsSingleton()", "Bind<ICropCatalog>().ToExisting<GameCropCatalog>()",
                         "AddDecorator<HdbfBoxFarmSpec, HdbfBoxFarmController>()",
                         "AddDecorator<HdbfBoxFarmSpec, IndoorFarmWorkplaceBehavior>()",
                         "Bind<HdbfWorldIconService>().AsSingleton()" })
            {
                TestAssert.True(configurator.Contains(token, StringComparison.Ordinal),
                    "configurator must contain: " + token);
            }

            TestAssert.False(configurator.Contains("HdbfPlot", StringComparison.Ordinal),
                "the plot highlight/scan services must not be registered in v2");
        }

        public static void TestQueryAndCommandServicesConsumeV2State()
        {
            var query = ReadSourceEnding("HdbfFarmQueryService.cs");
            TestAssert.True(query.Contains("IHdbfIndoorFarmSource", StringComparison.Ordinal));
            TestAssert.True(query.Contains("GetIndoorSnapshot", StringComparison.Ordinal),
                "the query singleton must expose the Core IndoorFarmSnapshot read model");
            foreach (var deleted in new[] { "ApplyPlotSelection", "_plotOwners", "FarmWarning",
                         "HdbWarningEvaluator", "HdbfWarningBoard" })
            {
                TestAssert.False(query.Contains(deleted, StringComparison.Ordinal),
                    "the deleted v1 warning/plot chain must not be in the query service: " + deleted);
            }

            var commands = ReadSourceEnding("HdbfFarmCommandService.cs");
            TestAssert.True(commands.Contains("HdbfBoxFarmController", StringComparison.Ordinal));
            TestAssert.True(commands.Contains("TryPerformMaintenance", StringComparison.Ordinal));
            TestAssert.True(commands.Contains("TryRequestManualRefill", StringComparison.Ordinal));
            TestAssert.True(commands.Contains("TrySetCropPolicy", StringComparison.Ordinal));
            foreach (var deleted in new[] { "Plot", "Highlight", "ExcludePlot", "IncludePlot" })
            {
                TestAssert.False(commands.Contains(deleted, StringComparison.Ordinal),
                    "the deleted v1 plot command must not remain: " + deleted);
            }
        }

        public static void TestEveryEmbeddedGameplaySourceKeepsTheHeaderAndLineLimit()
        {
            var resources = GameplayResourceNames();
            TestAssert.True(resources.Count >= 10, "the controller/bridge/catalog sources must all be embedded");
            foreach (var resource in resources)
            {
                var text = ReadResource(resource);
                var lines = text.Split('\n').Length - (text.EndsWith("\n", StringComparison.Ordinal) ? 1 : 0);
                TestAssert.True(lines <= MaxSourceLines,
                    resource + " must stay within the 300-line limit (REQ-V2-MAINT-02)");
                TestAssert.True(text.StartsWith("// ============================================================================",
                        StringComparison.Ordinal), resource + " is missing the fixed file header");
                TestAssert.True(text.Contains("HDBF v2 | Layer: Gameplay", StringComparison.Ordinal)
                                || text.Contains("HDBF v2 | Layer: Persistence", StringComparison.Ordinal),
                    resource + " header must name its layer/owner");
            }
        }

        public static void TestCoreTickAndSaveCodecAreUsableWithoutGameAssemblies()
        {
            var record = IndoorFarmRecord.Create(HdbfTier.Basic, HdbfFaction.Folktails);
            ICropCatalog catalog = FallbackCropCatalog.Default;
            var context = new IndoorDayContext
            {
                ActiveWorkers = 2,
                CurrentDay = 10f,
                Output = null,
                Water = null,
                Materials = null,
            };
            var result = IndoorDayTick.Run(record, HDBF.Core.Balance.TierCatalog.Get(HdbfTier.Basic),
                catalog, in context);
            TestAssert.Equal(0, result.Actions.Harvested);
            TestAssert.Equal(4, catalog.ForFaction(HdbfFaction.Folktails).Count,
                "the fallback table is still the frozen four-crop Folktails table");
            var text = IndoorSaveCodec.Encode(record);
            TestAssert.True(text.Length > 0, "the v2 codec must produce a payload");
            TestAssert.True(IndoorSaveCodec.TryDecode(text, out var decoded, out var error),
                "the v2 payload must round-trip: " + error);
            TestAssert.Equal(record.Slots.Length, decoded.Slots.Length);
        }

        // ------------------------------------------------------------------ helpers

        private static string ReadControllerSources() => ReadResourcesWithPrefix(ResourcePrefix + "Controller.");
        private static string ReadBridgeSources() => ReadResourcesWithPrefix(ResourcePrefix + "Bridge.");
        private static string ReadCatalogSources() => ReadResourcesWithPrefix(ResourcePrefix + "Catalog.");

        private static string ReadGameplaySources()
        {
            var builder = new StringBuilder();
            foreach (var resource in GameplayResourceNames())
            {
                builder.Append(ReadResource(resource)).Append('\n');
            }

            return builder.ToString();
        }

        private static string ReadResourcesWithPrefix(string prefix)
        {
            var builder = new StringBuilder();
            foreach (var resource in GameplayResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                builder.Append(ReadResource(resource)).Append('\n');
            }

            return builder.ToString();
        }

        private static string ReadSourceEnding(string suffix)
        {
            var name = GameplayResourceNames().FirstOrDefault(resource => resource.EndsWith(suffix, StringComparison.Ordinal));
            if (name is null)
            {
                throw new TestFailureException("embedded source not found: " + suffix);
            }

            return ReadResource(name);
        }

        private static List<string> GameplayResourceNames()
        {
            var result = new List<string>();
            foreach (var name in typeof(V2GameplayContractTests).Assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.StartsWith(ResourcePrefix + "Controller.", StringComparison.Ordinal)
                    || name.StartsWith(ResourcePrefix + "Catalog.", StringComparison.Ordinal)
                    || name.StartsWith(ResourcePrefix + "Bridge.", StringComparison.Ordinal)
                    || name.EndsWith("IndoorFarmWorkplaceBehavior.cs", StringComparison.Ordinal)
                    || name.EndsWith("IndoorInventorySink.cs", StringComparison.Ordinal)
                    || name.EndsWith("IndoorMaintenanceMaterialSource.cs", StringComparison.Ordinal)
                    || name.EndsWith("HdbfGameConfigurator.cs", StringComparison.Ordinal)
                    || name.EndsWith("HdbfFarmCommandService.cs", StringComparison.Ordinal)
                    || name.EndsWith("HdbfFarmQueryService.cs", StringComparison.Ordinal)
                    || name.EndsWith("HdbfGameplayContracts.cs", StringComparison.Ordinal))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        private static string ReadResource(string name)
        {
            using var stream = typeof(V2GameplayContractTests).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                throw new TestFailureException("embedded resource not found: " + name);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

    }
}
