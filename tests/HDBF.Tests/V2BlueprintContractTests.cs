// ============================================================================
// HDBF v2 | Layer: L1 data contract (offline, no game/Unity reference)
// Purpose: Pins the eight shipped BoxFarm blueprint JSON documents to the v2
//          3x3x1 stackable-building contract: footprint, nine blocks, stackable
//          roof (BlockObject + GenerateFloorsOnStackable + Top/Floor occupancy),
//          construction time 1/1.5/2.5/4 workdays (halved; hours in the real game field),
//          tier numbers, worker caps, maintenance, ports, output inventory,
//          construction base and colliders. It does not load any game assembly;
//          field/enum reality is proven by tools/validate against game metadata.
// Requirements: REQ-V2-BLD-01..07, REQ-V2-ART-02 (model paths), REQ-V2-BLD-06,
//               docs/v2/INTERFACES.md §2 (TierProfile numbers).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 contract for the v2 BoxFarm data set (REQ-V2-BLD-01/04/06, INTERFACES §2). Reads the REAL
    /// blueprints under <c>data/</c> via the <c>HdbfRepoRoot</c> assembly metadata baked in by
    /// HDBF.Tests.csproj, so it verifies exactly the JSON that ships with the mod. No Timberborn or
    /// Unity assembly is referenced or loaded (REQ-V2-TEST-01/REQ-TEST-03).
    ///
    /// The 3x3x1 / stackable / build-time facts this file pins are backed by real 1.1.2.8 evidence
    /// (docs/API_FACTS.md §3.2, docs/workstreams/bug-08-block-axis-size.md):
    /// * <c>BlockObjectSpec.Size</c> axes are X = width, Y = depth, Z = height;
    /// * <c>BlockStackable.BlockObject</c> + <c>BlockObjectNavMeshSettingsSpec.GenerateFloorsOnStackable</c>
    ///   are how vanilla 3x3x1 platforms/roofs/HydroponicGarden become stackable;
    /// * <c>ConstructionSiteBuildTimeSpec.ConstructionTimeInHours</c> is the only blueprint field
    ///   that controls construction duration (<c>ConstructionSiteBuildTimeCalculator</c> IL).
    /// </summary>
    public static class V2BlueprintContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string BoxFarmDataFolder = "Buildings/Food/BoxFarm";

        // REQ-V2-BLD-01: four tiers x two factions = the eight shipped blueprints.
        private static readonly string[] Tiers = { "Basic", "Intermediate", "Advanced", "Top" };
        private static readonly string[] Factions = { "Folktails", "IronTeeth" };

        /// <summary>REQ-V2-BLD-01 + docs/v2/INTERFACES.md §2; halved from the v2.1 2/3/5/8.</summary>
        private static readonly double[] BuildWorkdays = { 1.0, 1.5, 2.5, 4.0 };

        /// <summary>
        /// The game field <c>ConstructionSiteBuildTimeSpec.ConstructionTimeInHours</c> is in game
        /// hours; the card converts workdays with the documented 24 h/workday factor.
        /// </summary>
        private const int BuildHoursPerWorkday = 24;

        /// <summary>docs/v2/INTERFACES.md §2 <c>SlotCapacity</c>.</summary>
        private static readonly int[] SlotCapacities = { 60, 150, 300, 600 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>MaxWorkers</c> (unchanged from v1).</summary>
        private static readonly int[] MaxWorkers = { 2, 4, 8, 16 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>MinIrrigationCoverage</c> (C_min).</summary>
        private static readonly double[] MinIrrigationCoverage = { 0.50, 0.60, 0.75, 0.80 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>WaterPerSlotPerDay</c>.</summary>
        private static readonly double[] WaterPerSlotPerDay = { 0.020, 0.024, 0.028, 0.032 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>TankCapacityDays</c>.</summary>
        private static readonly int[] TankCapacityDays = { 3, 3, 4, 4 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>TankCapacity</c> (derived: water x slots x days).</summary>
        private static readonly double[] TankCapacity = { 3.6, 10.8, 33.6, 76.8 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>SoilFatigueFactor</c> (per-tier fatigue factor).</summary>
        private static readonly double[] SoilFatigueFactor = { 1.00, 1.10, 1.25, 1.50 };

        /// <summary>§19.3 maintenance kind: 0 = none/irrigation only, 1 = plank, 2 = metal, 3 = power/off-grid metal.</summary>
        private static readonly int[] MaintenanceKind = { 0, 1, 2, 3 };

        /// <summary>§19.3 maintenance cycle days.</summary>
        private static readonly double[] MaintenanceCycleDays = { 0.0, 10.0, 10.0, 8.0 };

        /// <summary>§19.3 maintenance material amount (mid = 8 plank, advanced = 6 metal, top off-grid = 15 metal).</summary>
        private static readonly int[] MaintenanceMaterialAmount = { 0, 8, 6, 15 };

        /// <summary>§19.3 research (science) cost.</summary>
        private static readonly int[] ScienceCosts = { 200, 600, 1000, 3000 };

        /// <summary>docs/v2/INTERFACES.md §2 <c>WorkRange</c> is 【作废】in v2 but the v1 field is kept in phase 1.</summary>
        private static readonly int[] WorkRangeX = { 11, 15, 19, 27 };
        private static readonly int[] WorkRangeZ = { 11, 15, 19, 27 };

        /// <summary>PlaceableBlockObjectSpec.ToolOrder (still the v1 food-tool ordering).</summary>
        private static readonly int[] ToolOrders = { 95, 96, 97, 98 };

        /// <summary>REQ-V2-BLD-06 port rules: water all tiers, logistics from Intermediate, power only Top.</summary>
        private static readonly bool[] HasPipePort = { true, true, true, true };
        private static readonly bool[] HasLogisticsPort = { false, true, true, true };
        private static readonly bool[] HasPowerPort = { false, false, false, true };

        /// <summary>REQ-V2-BLD-01 v1 build-materials (unchanged by v2.1).</summary>
        private static readonly (string Id, int Amount)[][] BuildingCosts =
        {
            new[] { ("Log", 40) },
            new[] { ("Plank", 40), ("Log", 20) },
            new[] { ("Plank", 50), ("MetalBlock", 30) },
            new[] { ("MetalBlock", 100), ("Plank", 50) },
        };

        /// <summary>The v1 3x2 solid entrance row occupations; the 3x3 facade row keeps them.</summary>
        private const string SolidRowOccupations = "Bottom, Top, Corners, Path, Middle";

        /// <summary>The v1 3x2 open-floor row occupations; both depth rows behind the facade keep them.</summary>
        private const string FloorRowOccupations = "Floor, Bottom, Corners, Path, Middle";

        /// <summary>
        /// v1/v2 rear-row power socket cells: x = 0..2 with side faces on the corners and the rear
        /// face on every cell. Kept as the v1 contract, moved from depth row y=1 (3x2) to y=2 (3x3).
        /// </summary>
        private static readonly string[] ExpectedTopTransputSignature =
        {
            "0,2,0:Left+Up",
            "1,2,0:Up",
            "2,2,0:Right+Up",
        };

        // ---------------------------------------------------------------- 8/8 inventory

        /// <summary>
        /// REQ-V2-BLD-01/06: exactly the eight tier/faction blueprints ship, each is strict JSON
        /// (no comments, no trailing commas, valid UTF-8) so the game's deserializer can load it.
        /// </summary>
        public static void TestAllEightV2BlueprintsExistAndAreStrictJson()
        {
            var folder = Path.Combine(DataRoot(), BoxFarmDataFolder.Replace('/', Path.DirectorySeparatorChar));
            TestAssert.True(Directory.Exists(folder), "BoxFarm data folder is missing: " + folder);

            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    expected.Add(BlueprintLabel(tier, faction));
                }
            }

            var actual = Directory
                .GetFiles(folder, "*.blueprint.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            TestAssert.Equal(8, actual.Count, "the v2 data set must ship exactly eight BoxFarm blueprints");
            TestAssert.True(expected.SetEquals(actual),
                "BoxFarm blueprints must be exactly the eight tier/faction files; found ["
                + string.Join(", ", actual) + "]");

            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var name = BlueprintLabel(tier, faction);
                    var path = Path.Combine(folder, name);
                    TestAssert.True(File.Exists(path), "missing v2 blueprint " + name);

                    var bytes = File.ReadAllBytes(path);
                    string text;
                    try
                    {
                        text = strictUtf8.GetString(bytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        throw new TestFailureException(name + ": blueprint is not valid UTF-8");
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
                        {
                            AllowTrailingCommas = false,
                            CommentHandling = JsonCommentHandling.Disallow,
                            MaxDepth = 64,
                        });
                        TestAssert.True(document.RootElement.ValueKind == JsonValueKind.Object,
                            name + ": blueprint root must be a JSON object");
                    }
                    catch (JsonException exception)
                    {
                        throw new TestFailureException(name + ": invalid JSON: " + exception.Message);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 3x3x1 footprint

        /// <summary>
        /// REQ-V2-BLD-01: every tier is strictly 3 wide x 3 deep x 1 high with nine placeholder
        /// blocks; the v1 3x2 mixed occupancy is extended consistently row by row: facade depth row
        /// (y = 0) solid on the ground, the two depth rows behind it (y = 1, 2) open floors.
        /// </summary>
        public static void TestEveryTierIsStrict3x3x1WithNineBlocks()
        {
            foreach (var (tier, faction, blueprint) in AllBlueprints())
            {
                var context = BlueprintLabel(tier, faction);
                var blockObject = ObjectMember(blueprint, "BlockObjectSpec", context);
                var size = ObjectMember(blockObject, "Size", context + " BlockObjectSpec");
                TestAssert.Equal(3, IntMember(size, "X", context + " Size"), context + ": Size.X (width)");
                TestAssert.Equal(3, IntMember(size, "Y", context + " Size"), context + ": Size.Y (depth)");
                TestAssert.Equal(1, IntMember(size, "Z", context + " Size"), context + ": Size.Z (height)");

                var blocks = ArrayMember(blockObject, "Blocks", context + " BlockObjectSpec");
                TestAssert.Equal(9, blocks.GetArrayLength(), context + ": 3x3x1 must have nine blocks");

                var index = 0;
                foreach (var block in blocks.EnumerateArray())
                {
                    var location = context + " Blocks[" + index + "]";
                    TestAssert.True(block.ValueKind == JsonValueKind.Object, location + " must be an object");

                    var depthRow = index / 3;
                    var expectedMatterBelow = depthRow == 0 ? "GroundOrStackable" : "Any";
                    var expectedOccupations = depthRow == 0 ? SolidRowOccupations : FloorRowOccupations;
                    TestAssert.Equal(expectedMatterBelow, StringMember(block, "MatterBelow", location),
                        location + ": MatterBelow for depth row y=" + depthRow);
                    TestAssert.Equal(expectedOccupations, StringMember(block, "Occupations", location),
                        location + ": Occupations for depth row y=" + depthRow);
                    TestAssert.Equal(false, BoolMember(block, "OccupyAllBelow", location),
                        location + ": OccupyAllBelow");
                    TestAssert.Equal(false, BoolMember(block, "Underground", location),
                        location + ": Underground");
                    index++;
                }

                var entrance = ObjectMember(blockObject, "Entrance", context + " BlockObjectSpec");
                TestAssert.Equal(true, BoolMember(entrance, "HasEntrance", context + " Entrance"),
                    context + ": Entrance.HasEntrance");
                var coordinates = ObjectMember(entrance, "Coordinates", context + " Entrance");
                TestAssert.Equal(1, IntMember(coordinates, "X", context + " Entrance"),
                    context + ": Entrance.X (facade centre)");
                TestAssert.Equal(-1, IntMember(coordinates, "Y", context + " Entrance"),
                    context + ": Entrance.Y (-1 = the cell in front of the facade)");
                TestAssert.Equal(0, IntMember(coordinates, "Z", context + " Entrance"),
                    context + ": Entrance.Z (ground level)");
            }
        }

        // ---------------------------------------------------------------- stackable roof

        /// <summary>
        /// REQ-V2-BLD-04: the top layer (the only height layer) declares the real
        /// <c>Stackable: BlockObject</c> enum value on all nine blocks, provides the `Top`/`Floor`
        /// occupancy placeholders, and the root carries the vanilla
        /// <c>BlockObjectNavMeshSettingsSpec</c> with <c>GenerateFloorsOnStackable = true</c> so the
        /// game generates walking floors on the stackable roof. This is the (data-side) precondition
        /// for the in-game stacking A/B required by REQ-V2-BLD-07; it does not claim the in-game
        /// validation itself.
        /// </summary>
        public static void TestTopLayerProvidesStackableRoofContract()
        {
            foreach (var (tier, faction, blueprint) in AllBlueprints())
            {
                var context = BlueprintLabel(tier, faction);
                var blockObject = ObjectMember(blueprint, "BlockObjectSpec", context);
                var blocks = ArrayMember(blockObject, "Blocks", context + " BlockObjectSpec");

                var occupationTokens = new HashSet<string>(StringComparer.Ordinal);
                var index = 0;
                foreach (var block in blocks.EnumerateArray())
                {
                    var location = context + " Blocks[" + index + "]";
                    TestAssert.Equal("BlockObject", StringMember(block, "Stackable", location),
                        location + ": Stackable must be the real BlockStackable.BlockObject member");
                    foreach (var token in StringMember(block, "Occupations", location)
                                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        occupationTokens.Add(token);
                    }

                    index++;
                }

                TestAssert.True(occupationTokens.Contains("Top"),
                    context + ": roof blocks must provide the 'Top' occupation placeholder (REQ-V2-BLD-04)");
                TestAssert.True(occupationTokens.Contains("Floor"),
                    context + ": roof blocks must provide the 'Floor' occupation placeholder (REQ-V2-BLD-04)");

                var nav = ObjectMember(blueprint, "BlockObjectNavMeshSettingsSpec", context);
                TestAssert.Equal(false, BoolMember(nav, "NoAutoWalls", context + " NavMeshSettings"),
                    context + ": NoAutoWalls (vanilla value)");
                TestAssert.Equal(true, BoolMember(nav, "GenerateFloorsOnStackable", context + " NavMeshSettings"),
                    context + ": GenerateFloorsOnStackable must be true (REQ-V2-BLD-04)");
                foreach (var name in new[] { "EdgeGroups", "UnblockedCoordinates", "BlockedEdges" })
                {
                    var array = ArrayMember(nav, name, context + " NavMeshSettings");
                    TestAssert.Equal(0, array.GetArrayLength(),
                        context + ": NavMeshSettings." + name + " must be empty for the plain flat roof");
                }
            }
        }

        // ---------------------------------------------------------------- construction time

        /// <summary>
        /// REQ-V2-BLD-01 (2026-10-27 halving): construction time is 1/1.5/2.5/4 workdays, half the
        /// v2.1 2/3/5/8. The real 1.1.2.8 field is
        /// <c>ConstructionSiteBuildTimeSpec.ConstructionTimeInHours</c> (float, game hours), so the
        /// expected values are 24/36/60/96 hours at the documented 24 h/workday factor. The
        /// building must also stay a construction site (<c>PlaceFinished = false</c>), otherwise the
        /// time would be irrelevant.
        /// </summary>
        public static void TestConstructionTimeIsHalvedFromV21()
        {
            for (var tierIndex = 0; tierIndex < Tiers.Length; tierIndex++)
            {
                var tier = Tiers[tierIndex];
                var workdays = BuildWorkdays[tierIndex];
                foreach (var faction in Factions)
                {
                    var context = BlueprintLabel(tier, faction);
                    var blueprint = LoadBlueprint(tier, faction);

                    var building = ObjectMember(blueprint, "BuildingSpec", context);
                    TestAssert.Equal(false, BoolMember(building, "PlaceFinished", context + " BuildingSpec"),
                        context + ": PlaceFinished must stay false so the construction site (and its build time) exists");

                    var buildTime = ObjectMember(blueprint, "ConstructionSiteBuildTimeSpec", context);
                    var hours = NumberMember(buildTime, "ConstructionTimeInHours",
                        context + " ConstructionSiteBuildTimeSpec");
                    var expectedHours = workdays * BuildHoursPerWorkday;
                    TestAssert.Equal(expectedHours, hours, 1e-6,
                        context + ": ConstructionTimeInHours must be " + workdays
                        + " workdays x " + BuildHoursPerWorkday + " h/workday");
                    TestAssert.Equal(workdays, hours / BuildHoursPerWorkday, 1e-6,
                        context + ": ConstructionTimeInHours must decode to " + workdays
                        + " workdays (REQ-V2-BLD-01, halved from v2.1 2/3/5/8)");
                }
            }
        }

        // ---------------------------------------------------------------- tier numbers

        /// <summary>
        /// docs/v2/INTERFACES.md §2 + §19.3: the <c>HdbfBoxFarmSpec</c> numbers v2 keeps (capacity,
        /// workers per farmer, C_min, water, tank days/capacity, maintenance, power, ports) and the
        /// four-slot capacity list 60/150/300/600.
        /// </summary>
        public static void TestTierSpecValuesMatchInterfaceSectionTwo()
        {
            for (var tierIndex = 0; tierIndex < Tiers.Length; tierIndex++)
            {
                var tier = Tiers[tierIndex];
                foreach (var faction in Factions)
                {
                    var context = BlueprintLabel(tier, faction);
                    var blueprint = LoadBlueprint(tier, faction);
                    var spec = ObjectMember(blueprint, "HdbfBoxFarmSpec", context);

                    TestAssert.Equal(tier, StringMember(spec, "Tier", context + " HdbfBoxFarmSpec"),
                        context + ": HdbfBoxFarmSpec.Tier");
                    TestAssert.Equal(SlotCapacities[tierIndex], IntMember(spec, "PlotCapacity", context + " HdbfBoxFarmSpec"),
                        context + ": slot capacity (docs/v2/INTERFACES.md §2 SlotCapacity)");
                    TestAssert.Equal(38, IntMember(spec, "PlotsPerFarmer", context + " HdbfBoxFarmSpec"),
                        context + ": SlotsPerFarmer = 38 (docs/v2/INTERFACES.md §1)");
                    TestAssert.Equal(WorkRangeX[tierIndex], IntMember(spec, "WorkRangeX", context + " HdbfBoxFarmSpec"),
                        context + ": WorkRangeX (v1 field kept in phase 1, 【作废】in v2)");
                    TestAssert.Equal(WorkRangeZ[tierIndex], IntMember(spec, "WorkRangeZ", context + " HdbfBoxFarmSpec"),
                        context + ": WorkRangeZ (v1 field kept in phase 1, 【作废】in v2)");
                    TestAssert.Equal(MinIrrigationCoverage[tierIndex],
                        NumberMember(spec, "MinIrrigationCoverage", context + " HdbfBoxFarmSpec"), 1e-6,
                        context + ": C_min (docs/v2/INTERFACES.md §2)");
                    TestAssert.Equal(WaterPerSlotPerDay[tierIndex],
                        NumberMember(spec, "WaterPerPlotPerDay", context + " HdbfBoxFarmSpec"), 1e-6,
                        context + ": WaterPerSlotPerDay (v1 field name kept in phase 1)");
                    TestAssert.Equal(TankCapacityDays[tierIndex],
                        IntMember(spec, "TankCapacityDays", context + " HdbfBoxFarmSpec"),
                        context + ": TankCapacityDays");

                    var derivedTank = WaterPerSlotPerDay[tierIndex] * SlotCapacities[tierIndex] * TankCapacityDays[tierIndex];
                    TestAssert.Equal(TankCapacity[tierIndex], derivedTank, 1e-6,
                        context + ": TankCapacity = WaterPerSlotPerDay x SlotCapacity x TankCapacityDays "
                        + "(docs/v2/INTERFACES.md §2)");

                    TestAssert.Equal(SoilFatigueFactor[tierIndex],
                        NumberMember(spec, "SoilFatigueFactor", context + " HdbfBoxFarmSpec"), 1e-6,
                        context + ": per-tier fatigue factor");
                    TestAssert.Equal(MaintenanceKind[tierIndex],
                        IntMember(spec, "MaintenanceKind", context + " HdbfBoxFarmSpec"),
                        context + ": MaintenanceKind (§19.3 maintenance row)");
                    TestAssert.Equal(MaintenanceCycleDays[tierIndex],
                        NumberMember(spec, "MaintenanceCycleDays", context + " HdbfBoxFarmSpec"), 1e-6,
                        context + ": MaintenanceCycleDays (§19.3 maintenance row)");
                    TestAssert.Equal(MaintenanceMaterialAmount[tierIndex],
                        IntMember(spec, "MaintenanceMaterialAmount", context + " HdbfBoxFarmSpec"),
                        context + ": MaintenanceMaterialAmount (§19.3 maintenance row)");

                    var requiresPower = tier == "Top";
                    TestAssert.Equal(requiresPower, BoolMember(spec, "RequiresPower", context + " HdbfBoxFarmSpec"),
                        context + ": RequiresPower (only Top has on-grid power)");
                    TestAssert.Equal(requiresPower ? 150 : 0, IntMember(spec, "PowerDemand", context + " HdbfBoxFarmSpec"),
                        context + ": PowerDemand (Top = 150, off-grid fallback keeps the field)");

                    TestAssert.Equal(HasPipePort[tierIndex], BoolMember(spec, "HasPipePort", context + " HdbfBoxFarmSpec"),
                        context + ": HasPipePort (water/irrigation on every tier)");
                    TestAssert.Equal(HasLogisticsPort[tierIndex],
                        BoolMember(spec, "HasLogisticsPort", context + " HdbfBoxFarmSpec"),
                        context + ": HasLogisticsPort (logistics from Intermediate)");
                    TestAssert.Equal(HasPowerPort[tierIndex], BoolMember(spec, "HasPowerPort", context + " HdbfBoxFarmSpec"),
                        context + ": HasPowerPort (Top only)");
                }
            }
        }

        // ---------------------------------------------------------------- workers / research / materials

        /// <summary>
        /// §19.3: worker caps 2/4/8/16, research costs 200/600/1000/3000 and the four unchanged v1
        /// build-material lists.
        /// </summary>
        public static void TestWorkerCapsScienceCostsAndBuildMaterials()
        {
            for (var tierIndex = 0; tierIndex < Tiers.Length; tierIndex++)
            {
                var tier = Tiers[tierIndex];
                foreach (var faction in Factions)
                {
                    var context = BlueprintLabel(tier, faction);
                    var blueprint = LoadBlueprint(tier, faction);

                    var workplace = ObjectMember(blueprint, "WorkplaceSpec", context);
                    TestAssert.Equal(MaxWorkers[tierIndex], IntMember(workplace, "MaxWorkers", context + " WorkplaceSpec"),
                        context + ": MaxWorkers (§19.3 worker cap)");
                    TestAssert.Equal(MaxWorkers[tierIndex],
                        IntMember(workplace, "DefaultWorkers", context + " WorkplaceSpec"),
                        context + ": DefaultWorkers (all worker slots filled by default)");

                    var building = ObjectMember(blueprint, "BuildingSpec", context);
                    TestAssert.Equal(ScienceCosts[tierIndex], IntMember(building, "ScienceCost", context + " BuildingSpec"),
                        context + ": ScienceCost (§19.3 research row)");

                    var costs = ArrayMember(building, "BuildingCost", context + " BuildingSpec");
                    var expected = BuildingCosts[tierIndex];
                    TestAssert.Equal(expected.Length, costs.GetArrayLength(),
                        context + ": BuildingCost entry count (§19.3 materials row)");
                    var costIndex = 0;
                    foreach (var cost in costs.EnumerateArray())
                    {
                        var location = context + " BuildingCost[" + costIndex + "]";
                        TestAssert.Equal(expected[costIndex].Id, StringMember(cost, "Id", location),
                            location + ": material id (§19.3 materials row)");
                        TestAssert.Equal(expected[costIndex].Amount, IntMember(cost, "Amount", location),
                            location + ": material amount (§19.3 materials row)");
                        costIndex++;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- output inventory

        /// <summary>
        /// §19.3 output inventory row (2026-10-29 bug fix): every tier ships the whole-box harvest
        /// bound <c>SlotCapacity x 4</c> (240/600/1200/2400) instead of the vanilla farmhouse
        /// capacity 50, so a full harvest of a 600-slot farm can be buffered and hauled out instead
        /// of stopping after ~50 items.
        /// </summary>
        public static void TestOutputInventoryMatchesHarvestCapacityPerTier()
        {
            foreach (var (tier, faction, blueprint) in AllBlueprints())
            {
                var context = BlueprintLabel(tier, faction);
                var tierIndex = Array.IndexOf(Tiers, tier);
                TestAssert.True(tierIndex >= 0, context + ": unknown tier");
                var inventory = ObjectMember(blueprint, "SimpleOutputInventorySpec", context);
                TestAssert.Equal(SlotCapacities[tierIndex] * IndoorConstants.MaxHarvestItemsPerSlot,
                    IntMember(inventory, "Capacity", context + " SimpleOutputInventorySpec"),
                    context + ": output inventory Capacity (whole-box harvest bound = slots x 4)");
                TestAssert.Equal(false, BoolMember(inventory, "IgnorableCapacity", context + " SimpleOutputInventorySpec"),
                    context + ": IgnorableCapacity (vanilla farmhouse value)");
            }
        }

        // ---------------------------------------------------------------- wiring: models / base / colliders

        /// <summary>
        /// REQ-V2-BLD-06 + REQ-V2-ART-02: the model paths stay the shipped custom 3x3x1 models, the
        /// unfinished prefab nests the vanilla <c>ConstructionBase3x3</c>, both prefabs carry a
        /// collision box covering the 3.0 x 1.0 x 3.0 envelope, and the placeable/label wiring is
        /// unchanged.
        /// </summary>
        public static void TestConstructionBaseCollidersAndModelWiringMatch3x3Envelope()
        {
            foreach (var (tier, faction, blueprint) in AllBlueprints())
            {
                var context = BlueprintLabel(tier, faction);
                var children = ObjectMember(blueprint, "Children", context);

                var finished = ObjectMember(children, "#Finished", context + " Children");
                var finishedMesh = ObjectMember(finished, "TimbermeshSpec", context + " #Finished");
                TestAssert.Equal(BoxFarmDataFolder + "/BoxFarm." + tier + "." + faction + ".Model",
                    StringMember(finishedMesh, "Model", context + " #Finished.TimbermeshSpec"),
                    context + ": finished model path (REQ-V2-BLD-06)");
                AssertBoxCollider(finished, context + " #Finished");

                var unfinished = ObjectMember(children, "#Unfinished", context + " Children");
                var unfinishedChildren = ObjectMember(unfinished, "Children", context + " #Unfinished");
                var stage = ObjectMember(unfinishedChildren, "ConstructionStage0", context + " #Unfinished.Children");
                var stageMesh = ObjectMember(stage, "TimbermeshSpec", context + " ConstructionStage0");
                TestAssert.Equal(BoxFarmDataFolder + "/BoxFarm." + tier + "." + faction + ".ConstructionStage0.Model",
                    StringMember(stageMesh, "Model", context + " ConstructionStage0.TimbermeshSpec"),
                    context + ": construction-stage model path (REQ-V2-BLD-06 / REQ-V2-ART-02)");
                AssertBoxCollider(stage, context + " ConstructionStage0");

                var constructionBase = ObjectMember(unfinishedChildren, "ConstructionBase3x3#nested",
                    context + " #Unfinished.Children");
                TestAssert.Equal("ConstructionBases/ConstructionBase3x3/ConstructionBase3x3.blueprint",
                    StringMember(constructionBase, "BlueprintPath", context + " ConstructionBase3x3#nested"),
                    context + ": the 3x3 footprint must nest the vanilla ConstructionBase3x3 (REQ-V2-BLD-01)");

                var template = ObjectMember(blueprint, "TemplateSpec", context);
                TestAssert.Equal("BoxFarm." + tier + "." + faction,
                    StringMember(template, "TemplateName", context + " TemplateSpec"),
                    context + ": TemplateName");
                var placeable = ObjectMember(blueprint, "PlaceableBlockObjectSpec", context);
                TestAssert.Equal("Food", StringMember(placeable, "ToolGroupId", context + " PlaceableBlockObjectSpec"),
                    context + ": ToolGroupId");
                TestAssert.Equal(ToolOrders[Tiers.ToList().IndexOf(tier)],
                    IntMember(placeable, "ToolOrder", context + " PlaceableBlockObjectSpec"),
                    context + ": ToolOrder");
                var labeled = ObjectMember(blueprint, "LabeledEntitySpec", context);
                TestAssert.Equal("Icons/BoxFarm" + tier,
                    StringMember(labeled, "Icon", context + " LabeledEntitySpec"),
                    context + ": build-menu icon path");
            }
        }

        // ---------------------------------------------------------------- ports / power

        /// <summary>
        /// REQ-V2-BLD-06 port rules: only Top may carry the vanilla mechanical consumer composition,
        /// its three v1 rear-row power sockets are moved to the 3-deep rear row (y = Size.Y - 1 = 2),
        /// every direction points outwards and the two factions stay identical (REQ-TECH-06).
        /// </summary>
        public static void TestPowerPortRulesAndTopTransputGeometry()
        {
            var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
            var powerInputs = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (tier, faction, blueprint) in AllBlueprints())
            {
                var context = BlueprintLabel(tier, faction);
                var mechanicalSpecs = new[]
                {
                    "MechanicalBuildingSpec", "MechanicalNodeSpec", "TransputProviderSpec",
                    "MechanicalConnectorTargetSpec",
                };

                if (tier != "Top")
                {
                    foreach (var specName in mechanicalSpecs)
                    {
                        TestAssert.False(blueprint.TryGetProperty(specName, out _),
                            context + ": '" + specName + "' is Top-only; the lower tiers are off-grid (REQ-V2-BLD-06)");
                    }

                    continue;
                }

                AssertEmptyObjectSpec(blueprint, "MechanicalBuildingSpec", context);
                AssertEmptyObjectSpec(blueprint, "MechanicalConnectorTargetSpec", context);

                var node = ObjectMember(blueprint, "MechanicalNodeSpec", context);
                TestAssert.Equal(0, IntMember(node, "PowerOutput", context + " MechanicalNodeSpec"),
                    context + ": MechanicalNodeSpec.PowerOutput");
                TestAssert.Equal(150, IntMember(node, "PowerInput", context + " MechanicalNodeSpec"),
                    context + ": MechanicalNodeSpec.PowerInput (Top = 150)");
                TestAssert.Equal(false, BoolMember(node, "IsShaft", context + " MechanicalNodeSpec"),
                    context + ": MechanicalNodeSpec.IsShaft");
                powerInputs[faction] = IntMember(node, "PowerInput", context + " MechanicalNodeSpec");

                var provider = ObjectMember(blueprint, "TransputProviderSpec", context);
                TestAssert.Equal(false, BoolMember(provider, "IgnoreRotation", context + " TransputProviderSpec"),
                    context + ": TransputProviderSpec.IgnoreRotation");
                var transputs = ArrayMember(provider, "Transputs", context + " TransputProviderSpec");
                TestAssert.Equal(3, transputs.GetArrayLength(),
                    context + ": the v1 rear-row socket contract keeps three transputs");

                var signature = new List<string>();
                var transputIndex = 0;
                foreach (var transput in transputs.EnumerateArray())
                {
                    var location = context + " Transputs[" + transputIndex + "]";
                    var coordinates = ObjectMember(transput, "Coordinates", location);
                    var x = IntMember(coordinates, "X", location + ".Coordinates");
                    var y = IntMember(coordinates, "Y", location + ".Coordinates");
                    var z = IntMember(coordinates, "Z", location + ".Coordinates");
                    TestAssert.True(x >= 0 && x < 3 && y >= 0 && y < 3 && z >= 0 && z < 1,
                        location + ": coordinates must stay inside the 3x3x1 footprint (found " + x + "," + y + "," + z + ")");
                    TestAssert.Equal(2, y,
                        location + ": power sockets must sit on the rear depth row y = Size.Y - 1 = 2");

                    TestAssert.Equal(false, BoolMember(transput, "ReverseRotation", location),
                        location + ": ReverseRotation (vanilla consumer value)");

                    var directions = StringMember(transput, "Directions", location)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    TestAssert.True(directions.Length > 0, location + ": Directions must not be empty");
                    foreach (var direction in directions)
                    {
                        var offset = DirectionOffset(direction);
                        var targetX = x + offset.Dx;
                        var targetY = y + offset.Dy;
                        var targetZ = z + offset.Dz;
                        TestAssert.True(targetX < 0 || targetX >= 3 || targetY < 0 || targetY >= 3
                                        || targetZ < 0 || targetZ >= 1,
                            location + ": direction '" + direction + "' must point out of the footprint");
                    }

                    signature.Add(x + "," + y + "," + z + ":"
                                  + string.Join("+", directions.OrderBy(token => token, StringComparer.Ordinal)));
                    transputIndex++;
                }

                signatures[faction] = string.Join("|", signature.OrderBy(entry => entry, StringComparer.Ordinal));
                var expected = string.Join("|", ExpectedTopTransputSignature.OrderBy(entry => entry, StringComparer.Ordinal));
                TestAssert.Equal(expected, signatures[faction],
                    context + ": Top transputs must be the v1 rear-row sockets " + string.Join(" ", ExpectedTopTransputSignature));

                var custom = ObjectMember(blueprint, "HdbfBoxFarmSpec", context);
                TestAssert.Equal(150, IntMember(custom, "PowerDemand", context + " HdbfBoxFarmSpec"),
                    context + ": HdbfBoxFarmSpec.PowerDemand must equal PowerInput 150 (off-grid fallback field)");
            }

            TestAssert.Equal(2, signatures.Count, "both Top factions must declare power geometry");
            TestAssert.Equal(2, powerInputs.Count, "both Top factions must declare a mechanical node");
            TestAssert.Equal(signatures["Folktails"], signatures["IronTeeth"],
                "Top power geometry must be identical for both factions (REQ-TECH-06)");
            TestAssert.Equal(powerInputs["Folktails"], powerInputs["IronTeeth"],
                "Top PowerInput must be identical for both factions (REQ-TECH-06)");
        }

        // ---------------------------------------------------------------- faction parity

        /// <summary>
        /// REQ-TECH-06: the two faction blueprints of a tier are mirror images — they may differ only
        /// in TemplateName and the two faction-scoped model paths (the 1.1.2.8 MaterialRepository is
        /// faction-scoped, docs/API_FACTS.md §8). Everything else (geometry, costs, balance, ports)
        /// must be byte-identical JSON.
        /// </summary>
        public static void TestFactionVariantsAreMirrorImages()
        {
            foreach (var tier in Tiers)
            {
                var folktails = LoadBlueprint(tier, "Folktails");
                var ironTeeth = LoadBlueprint(tier, "IronTeeth");
                var context = "BoxFarm." + tier;
                JsonAssertMirror(folktails, ironTeeth, string.Empty, context);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static readonly HashSet<string> FactionScopedJsonPaths = new(StringComparer.Ordinal)
        {
            "TemplateSpec.TemplateName",
            "Children.#Finished.TimbermeshSpec.Model",
            "Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model",
        };

        private static void JsonAssertMirror(JsonElement expected, JsonElement actual, string path, string context)
        {
            if (FactionScopedJsonPaths.Contains(path))
            {
                return;
            }

            var location = path.Length == 0 ? context : context + " " + path;
            if (expected.ValueKind != actual.ValueKind)
            {
                throw new TestFailureException(location + ": mirror mismatch ("
                    + expected.ValueKind + " vs " + actual.ValueKind + ")");
            }

            switch (expected.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var expectedNames = expected.EnumerateObject().Select(property => property.Name).ToList();
                    var actualNames = actual.EnumerateObject().Select(property => property.Name).ToList();
                    TestAssert.Equal(expectedNames.Count, actualNames.Count, location + ": mirror member count");
                    foreach (var name in expectedNames)
                    {
                        TestAssert.True(actual.TryGetProperty(name, out var actualValue),
                            location + ": mirror is missing member '" + name + "'");
                        JsonAssertMirror(expected.GetProperty(name), actualValue,
                            path.Length == 0 ? name : path + "." + name, context);
                    }

                    break;
                }

                case JsonValueKind.Array:
                {
                    TestAssert.Equal(expected.GetArrayLength(), actual.GetArrayLength(),
                        location + ": mirror array length");
                    for (var index = 0; index < expected.GetArrayLength(); index++)
                    {
                        JsonAssertMirror(expected[index], actual[index], path + "[" + index + "]", context);
                    }

                    break;
                }

                case JsonValueKind.String:
                    TestAssert.Equal(expected.GetString() ?? string.Empty, actual.GetString(),
                        location + ": mirror value");
                    break;

                case JsonValueKind.Number:
                    TestAssert.Equal(expected.GetRawText(), actual.GetRawText(), location + ": mirror number");
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    TestAssert.Equal(expected.GetBoolean(), actual.GetBoolean(), location + ": mirror boolean");
                    break;

                case JsonValueKind.Null:
                    break;

                default:
                    throw new TestFailureException(location + ": unexpected JSON kind " + expected.ValueKind);
            }
        }

        private static void AssertBoxCollider(JsonElement prefab, string context)
        {
            var colliders = ObjectMember(prefab, "CollidersSpec", context);
            var boxes = ArrayMember(colliders, "BoxColliders", context + ".CollidersSpec");
            TestAssert.True(boxes.GetArrayLength() > 0, context + ": BoxColliders must be a non-empty array");

            var box = boxes[0];
            var center = ObjectMember(box, "Center", context + " BoxColliders[0]");
            TestAssert.Equal(1.5, NumberMember(center, "X", context + " Center"), 1e-6, context + ": collider center X");
            TestAssert.Equal(0.5, NumberMember(center, "Y", context + " Center"), 1e-6, context + ": collider center Y");
            TestAssert.Equal(1.5, NumberMember(center, "Z", context + " Center"), 1e-6,
                context + ": collider center Z (3-deep footprint centre)");

            var size = ObjectMember(box, "Size", context + " BoxColliders[0]");
            TestAssert.Equal(3.0, NumberMember(size, "X", context + " Size"), 1e-6, context + ": collider size X");
            TestAssert.Equal(1.0, NumberMember(size, "Y", context + " Size"), 1e-6, context + ": collider size Y");
            TestAssert.Equal(3.0, NumberMember(size, "Z", context + " Size"), 1e-6,
                context + ": collider size Z (3-deep footprint)");
        }

        private static void AssertEmptyObjectSpec(JsonElement blueprint, string specName, string context)
        {
            var spec = ObjectMember(blueprint, specName, context);
            TestAssert.Equal(0, spec.EnumerateObject().Count(),
                context + ": '" + specName + "' must be the vanilla empty marker spec");
        }

        private static (int Dx, int Dy, int Dz) DirectionOffset(string direction) => direction switch
        {
            "Up" => (0, 1, 0),
            "Down" => (0, -1, 0),
            "Left" => (-1, 0, 0),
            "Right" => (1, 0, 0),
            "Top" => (0, 0, 1),
            "Bottom" => (0, 0, -1),
            _ => throw new TestFailureException("unknown Directions3D token '" + direction + "'"),
        };

        /// <summary>All eight blueprints as (tier, faction, parsed root) tuples.</summary>
        private static IEnumerable<(string Tier, string Faction, JsonElement Blueprint)> AllBlueprints()
        {
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    yield return (tier, faction, LoadBlueprint(tier, faction));
                }
            }
        }

        private static string BlueprintLabel(string tier, string faction)
            => "BoxFarm." + tier + "." + faction + ".blueprint.json";

        private static JsonElement LoadBlueprint(string tier, string faction)
            => LoadJson(Path.Combine(DataRoot(), BoxFarmDataFolder.Replace('/', Path.DirectorySeparatorChar),
                BlueprintLabel(tier, faction)), BlueprintLabel(tier, faction));

        /// <summary>Repository root baked into this assembly by HDBF.Tests.csproj.</summary>
        private static string RepoRoot()
        {
            var metadata = typeof(V2BlueprintContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, RepoRootMetadataKey, StringComparison.Ordinal));

            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException(
                    "assembly metadata \"" + RepoRootMetadataKey + "\" is missing: HDBF.Tests.csproj must declare "
                    + "<AssemblyMetadata Include=\"" + RepoRootMetadataKey + "\" .../> so the L1 data contract can "
                    + "find the shipped data/ tree (the build output lives outside the repository)");
            }

            return metadata.Value!;
        }

        private static string DataRoot()
        {
            var repoRoot = RepoRoot();
            var dataRoot = Path.Combine(repoRoot, "data");
            if (!Directory.Exists(dataRoot))
            {
                throw new TestFailureException(
                    "shipped data/ directory not found under " + RepoRootMetadataKey + "=\"" + repoRoot
                    + "\" (looked for " + dataRoot + "); the repo-root metadata or the build checkout is wrong");
            }

            return dataRoot;
        }

        private static JsonElement LoadJson(string path, string what)
        {
            TestAssert.True(File.Exists(path), "expected " + what + " at " + path);
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            return document.RootElement.Clone();
        }

        private static JsonElement Member(JsonElement element, string name, string context)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new TestFailureException(context + ": expected a JSON object, got " + element.ValueKind);
            }

            if (!element.TryGetProperty(name, out var value))
            {
                throw new TestFailureException(context + ": missing JSON member \"" + name + "\"");
            }

            return value;
        }

        private static JsonElement ObjectMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON object, got " + value.ValueKind);
            }

            return value;
        }

        private static JsonElement ArrayMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON array, got " + value.ValueKind);
            }

            return value;
        }

        private static string StringMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON string, got " + value.ValueKind);
            }

            return value.GetString()!;
        }

        private static int IntMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON integer");
            }

            return number;
        }

        private static double NumberMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind != JsonValueKind.Number)
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON number");
            }

            return value.GetDouble();
        }

        private static bool BoolMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON boolean");
            }

            return value.GetBoolean();
        }
    }
}
