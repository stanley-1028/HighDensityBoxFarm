using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HDBF.Tools.Validate;

/// <summary>
/// Deliverable-specific checks that generic schema validation cannot express: manifest fields,
/// the eight v2 3x3x1 BoxFarm blueprints (footprint / mixed block occupancy / stackable roof /
/// entrance / costs / spec numbers / construction time / children), both template collections,
/// the icons, the shipped custom .timbermesh assets (existence + zlib payload) and the
/// art-placeholder notes.
/// </summary>
internal sealed class DataChecks
{
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly Regex GameVersionPattern = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly Regex ModIdPattern = new(@"^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    // Release under test: bump together with data/manifest.json on every release (warning only).
    private const string ExpectedReleaseVersion = "2.5.2";

    private static readonly string[] BlueprintSuffixes = { "Basic", "Intermediate", "Advanced", "Top" };

    // Faction-specific blueprints: vanilla materials are faction-scoped (MaterialRepository
    // only contains the running faction's materials), so a blueprint must reference a model of
    // the same faction or prefab creation throws "Material <name> not found in repository".
    private static readonly string[] Factions = { "Folktails", "IronTeeth" };

    // REQ-V2-BLD-01..04: the v2 footprint is 3 wide x 3 deep x 1 high with nine placeholder
    // blocks; all nine blocks are `Stackable: BlockObject` (the roof is the stacking surface) and
    // the root carries BlockObjectNavMeshSettingsSpec.GenerateFloorsOnStackable = true so walking
    // floors are generated on the stackable roof. Reference blueprints (vanilla 1.1.2.8):
    // Buildings/Paths/MetalPlatform3x3/*.blueprint.json (3x3x1, all Stackable BlockObject,
    // GenerateFloorsOnStackable true) and Buildings/Food/HydroponicGarden/*.blueprint.json
    // (3x3x1 workplace with entrance, nine solid Stackable BlockObject blocks).
    private const int FootprintX = 3;
    private const int FootprintY = 3;
    private const int FootprintZ = 1;
    private const string StackableBlockObject = "BlockObject";

    // REQ-V2-BLD-01 + docs/v2/INTERFACES.md §2: construction time is 1/1.5/2.5/4 workdays
    // (halved from the v2.1 2/3/5/8 on 2026-10-27). The ONLY 1.1.2.8 blueprint field that
    // controls construction duration is
    // ConstructionSiteBuildTimeSpec.ConstructionTimeInHours (IL: ConstructionSiteBuildTimeCalculator
    // .GetConstructionTimeInHours returns it verbatim; without the spec the game derives
    // ceil(constructionInventoryCapacity / 20) hours, or its 1-hour default). The field unit is
    // game hours, so a workday is converted with the documented 24 hours/workday factor.
    private const int BuildHoursPerWorkday = 24;

    // Chapter 7 tech gate. Timberborn 1.1.2.8 ships no research-node blueprints at all (the
    // official Modding/Blueprints.zip has no Research/Tech folder, and the ScienceSystem assembly
    // has no node spec type): a building is unlocked by BuildingUnlockingService when
    // BuildingSpec.ScienceCost == 0 or after the player paid that cost in the build menu. A
    // positive, tier-correct ScienceCost is therefore the only data-level "locked until
    // researched" gate, and these two specs would change the mechanism into something else.
    private static readonly string[] WrongTechSpecs =
    {
        "ScienceNeedingBuildingSpec", // building consumes science while operating (not an unlock)
        "UnlockableOnceSpec",         // one-per-player data unlock, shared by later saves (not per game)
    };

    // Top-tier on-grid power (chapters 6.5 / 8.3 / 11.1, REQ-IMPL-21). The vanilla consumer
    // composition is MechanicalBuildingSpec + MechanicalNodeSpec.PowerInput + TransputProviderSpec
    // + MechanicalConnectorTargetSpec; the other three tiers must stay purely off-grid.
    private static readonly string[] MechanicalSpecNames =
    {
        "MechanicalBuildingSpec", "MechanicalNodeSpec", "TransputProviderSpec", "MechanicalConnectorTargetSpec",
    };

    // Ground-row connection faces that keep the HDBF entrance (front; grid -Y / Unity -Z) and the
    // worker access points clear and that do not create a roof connection point
    // (REQ-IMPL-05, chapter 11.2 #4). Timberborn's grid has Z as the vertical axis, so the direction
    // tokens map to: Left/Right = -/+X, Down/Up = -/+Y (Up = the back face), Bottom/Top = -/+Z
    // (vertical faces). The farm may only connect towards the back and the two sides.
    private static readonly string[] AllowedTransputDirections = { "Up", "Left", "Right" };

    // Harvest output inventory (P1 crop loop). The vanilla farmhouses use
    // SimpleOutputInventorySpec { Capacity: 50, IgnorableCapacity: false }; the spec alone creates
    // SimpleOutputInventory + Inventory + InventoryNeedBehavior (verified decorators in
    // SimpleOutputBuildingsTemplateModuleProvider), so hauling works without extra specs.
    //
    // 2026-10-29 player bug fix: the capacity is NOT the vanilla 50 any more. One slot can yield at
    // most 4 items (max vanilla crop base yield 3 x dense 1.35, rounded half up), so each tier ships
    // the whole-box harvest bound PlotCapacity x 4 (240/600/1200/2400). With 50, a 600-slot farm
    // filled the buffer after ~25 slots and transferred only 50 items instead of the whole harvest.
    private const int OutputCapacityPerSlot = 4;

    // The vanilla field-farm marker specs must NOT be added: farm-loop drives the vanilla
    // worker-side planting/harvesting behaviors itself and does not want the building to become a
    // field farm (FarmHouseSpec), own a plantable group (PlanterBuildingSpec) or remove yields by
    // building (YieldRemovingBuildingSpec).
    private static readonly string[] ForbiddenFarmSpecs =
    {
        "FarmHouseSpec", "PlanterBuildingSpec", "YieldRemovingBuildingSpec",
    };

    private string _currentFaction = "Folktails";

    // ART-03: the blueprints no longer borrow a vanilla placeholder model; every tier/faction ships
    // its own .timbermesh pair (finished + ConstructionStage0). The expected paths are derived from
    // tier + faction in TierExpectation (a single source of truth for blueprints, the model-asset
    // check and the ART_PLACEHOLDER.md check) instead of repeating 16 strings here.
    private const string CustomModelDirectory = "Buildings/Food/BoxFarm";

    // ART-04 art contract (user instruction 2026-10-25): the roof is now ONE empty flat platform —
    // the crop troughs, the soil and the six #Slot#Crop_0..5 marker nodes are gone, so the mesh
    // payload must contain no "#Slot#" node name at all (nor any other marker starting with it).
    // The three rear port empties stay: #Port#Power, #Port#Pipe, #Port#Logistics, each exactly once
    // (the blueprint wiring / HdbfBoxFarmSpec port flags depend on them). Geometry (flat platform,
    // bounds, materials, UV patches, triangles) is enforced by art/validate_timbermesh.py; this tool
    // only owns the container-level node-name contract the data side can see.
    private static readonly string[] RequiredArtPortMarkers =
    {
        "#Port#Power", "#Port#Pipe", "#Port#Logistics",
    };

    private const string ForbiddenArtMarker = "#Slot#";

    private static readonly TierExpectation[] Tiers =
    {
        //                                                                    BuildWorkdays --.
        //                    ScienceCost --.  MaxWorkers --.  PlotCapacity --.                 |
        //                                  v               v                v                v
        new("Basic", new[] { ("Log", 40) }, 200, 2, 60, 38, 11, 11, 0.50, 0.020, 3, 1.00,
            0, 0.0, 0, false, 0, true, false, false, 95, 1.0),
        new("Intermediate", new[] { ("Plank", 40), ("Log", 20) }, 600, 4, 150, 38, 15, 15, 0.60, 0.024, 3, 1.10,
            1, 10.0, 8, false, 0, true, true, false, 96, 1.5),
        new("Advanced", new[] { ("Plank", 50), ("MetalBlock", 30) }, 1000, 8, 300, 38, 19, 19, 0.75, 0.028, 4, 1.25,
            2, 10.0, 6, false, 0, true, true, false, 97, 2.5),
        new("Top", new[] { ("MetalBlock", 100), ("Plank", 50) }, 3000, 16, 600, 38, 27, 27, 0.80, 0.032, 4, 1.50,
            3, 8.0, 15, true, 150, true, true, true, 98, 4.0),
    };

    private readonly Report _report;
    private readonly string _dataDir;
    private readonly string _modRoot;
    private readonly string _refRoot;
    private readonly Dictionary<string, List<string>> _collectionEntries = new(StringComparer.Ordinal);

    // ART-03: decompressed .timbermesh payloads keyed by TimbermeshSpec.Model path (null = the
    // model is missing/corrupt and already reported). CheckModelAndColliders verifies each model
    // once; CheckTransformSlots reuses the same bytes for its slot-keyword cross-check instead of
    // decompressing the file twice.
    private readonly Dictionary<string, byte[]?> _modelBytesByPath = new(StringComparer.Ordinal);
    private bool _referenceUnavailable;

    public DataChecks(string dataDir, string modRoot, string refRoot, Report report)
    {
        _dataDir = dataDir;
        _report = report;
        _modRoot = modRoot;
        _refRoot = refRoot;
    }

    public void Validate()
    {
        CheckManifest();
        CheckBlueprintInventory();
        CheckBoxFarmBlueprints();
        CheckTemplateCollections();
        CheckIcons();
        CheckArtPlaceholderNotes();
        CheckTechGating();
        CheckMechanicalPower();
        CheckOutputInventory();
    }

    // ------------------------------------------------------------------ blueprint inventory

    /// <summary>
    /// REQ-V2-BLD-01/06: exactly the eight v2 blueprints ship (four tiers x two factions), no more
    /// and no fewer. CheckBoxFarmBlueprints already fails a missing per-tier file; this check owns
    /// the "8/8" count so a half-migrated data folder (e.g. one faction deleted) cannot pass.
    /// </summary>
    private void CheckBlueprintInventory()
    {
        const string category = "blueprint";
        var folder = Path.Combine(_dataDir, "Buildings", "Food", "BoxFarm");
        if (!Directory.Exists(folder))
        {
            _report.Error(category, "data/Buildings/Food/BoxFarm/ is missing");
            return;
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tier in BlueprintSuffixes)
        {
            foreach (var faction in Factions)
            {
                expected.Add($"BoxFarm.{tier}.{faction}.blueprint.json");
            }
        }

        var actual = Directory
            .GetFiles(folder, "*.blueprint.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (actual.Count != expected.Count)
        {
            _report.Error(category, $"data/Buildings/Food/BoxFarm/ must contain exactly {expected.Count} "
                                     + $"BoxFarm blueprints for the v2 3x3x1 data set (found {actual.Count})");
        }

        foreach (var name in expected.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!actual.Contains(name, StringComparer.Ordinal))
            {
                _report.Error(category, $"expected v2 blueprint '{name}' is missing");
            }
        }

        foreach (var name in actual)
        {
            if (!expected.Contains(name))
            {
                _report.Error(category, $"unexpected blueprint '{name}' under Buildings/Food/BoxFarm/ "
                                         + "(the v2 data set is exactly eight tier/faction blueprints)");
            }
        }
    }

    // ------------------------------------------------------------------ manifest

    private void CheckManifest()
    {
        const string category = "manifest";
        var path = Path.Combine(_dataDir, "manifest.json");
        if (!File.Exists(path))
        {
            _report.Error(category, "data/manifest.json is missing");
            return;
        }

        using var document = ParseOrReport(path, "manifest.json");
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        if (!ExpectObject(root, category, "manifest.json"))
        {
            return;
        }

        var name = ExpectString(root, category, "manifest.json", "Name");
        // The Steam title is part of the release metadata ("High-Density Box Farm — 3x3x1
        // Indoor Stackable Farm" since the 2026-10-28 Workshop page rebuild), so the check pins
        // the brand prefix instead of one stale exact string; the mod identity is the Id field.
        if (name is not null
            && (!name.StartsWith("High-Density Box Farm", StringComparison.Ordinal) || name.Length > 120))
        {
            _report.Error(category, $"Name must start with 'High-Density Box Farm' and stay under 120 "
                                     + $"characters (found '{name}')");
        }

        var version = ExpectString(root, category, "manifest.json", "Version");
        if (version is not null && !VersionPattern.IsMatch(version))
        {
            _report.Error(category, $"Version '{version}' must look like 0.1.0");
        }
        else if (version is not null && version != ExpectedReleaseVersion)
        {
            _report.Warn(category, $"Version is '{version}', the pinned release under test is '{ExpectedReleaseVersion}'");
        }

        var id = ExpectString(root, category, "manifest.json", "Id");
        if (id is not null)
        {
            if (!ModIdPattern.IsMatch(id))
            {
                _report.Error(category, $"Id '{id}' must be alphanumeric (letters, digits, '.', '_' or '-')");
            }

            if (id != "HighDensityBoxFarm")
            {
                _report.Error(category, $"Id must be 'HighDensityBoxFarm' (found '{id}')");
            }
        }

        var minimumGameVersion = ExpectString(root, category, "manifest.json", "MinimumGameVersion");
        if (minimumGameVersion is not null)
        {
            if (!GameVersionPattern.IsMatch(minimumGameVersion))
            {
                _report.Error(category, $"MinimumGameVersion '{minimumGameVersion}' must look like 1.0.0.0");
            }
            else if (minimumGameVersion != "1.0.0.0")
            {
                _report.Warn(category, $"MinimumGameVersion is '{minimumGameVersion}', expected 1.0.0.0");
            }
        }

        var description = ExpectString(root, category, "manifest.json", "Description", allowEmpty: true);
        if (description is not null && description.Length == 0)
        {
            _report.Error(category, "Description must not be empty");
        }

        ExpectStringArray(root, category, "manifest.json", "RequiredMods");
        ExpectStringArray(root, category, "manifest.json", "OptionalMods");

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name", "Version", "Id", "MinimumGameVersion", "Description", "RequiredMods", "OptionalMods",
        };
        foreach (var property in root.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                _report.Warn(category, $"unknown manifest field '{property.Name}'");
            }
        }
    }

    // ------------------------------------------------------------------ box farm blueprints

    private void CheckBoxFarmBlueprints()
    {
        const string category = "blueprint";

        // REQ-V2-BLD-01: construction time is 1/1.5/2.5/4 workdays (halved from the v2.1
        // 2/3/5/8). The expectation table is the single source the per-blueprint hours are
        // compared against, so pin it here as well.
        var buildWorkdays = Tiers.Select(tier => tier.BuildWorkdays).ToArray();
        var requiredBuildWorkdays = new[] { 1.0, 1.5, 2.5, 4.0 };
        if (!buildWorkdays.SequenceEqual(requiredBuildWorkdays))
        {
            _report.Error(category, "construction workdays must be exactly 1/1.5/2.5/4 for Basic/Intermediate/"
                                     + $"Advanced/Top (halved from REQ-V2-BLD-01 v2.1 2/3/5/8; found {string.Join("/", buildWorkdays)})");
        }

        foreach (var expectation in Tiers)
        {
            foreach (var faction in Factions)
            {
            _currentFaction = faction;
            var relative = $"Buildings/Food/BoxFarm/BoxFarm.{expectation.Tier}.{faction}.blueprint.json";
            var path = Path.Combine(_dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _report.Error(category, $"{relative} is missing");
                continue;
            }

            using var document = ParseOrReport(path, relative);
            if (document is null)
            {
                continue;
            }

            var root = document.RootElement;
            if (!ExpectObject(root, category, relative))
            {
                continue;
            }

            foreach (var spec in new[]
                     {
                         "BuildingSpec", "BuildingModelSpec", "BlockObjectSpec",
                         "BlockObjectNavMeshSettingsSpec", "BuildingAccessibleSpec",
                         "WorkplaceSpec", "PlaceableBlockObjectSpec", "LabeledEntitySpec", "TemplateSpec",
                         "ConstructionSiteBuildTimeSpec", "ConstructionSiteProgressVisualizerSpec",
                         "HdbfBoxFarmSpec", "WorkplaceIlluminatorSpec", "WorkplaceWorkerOutfitSpec",
                     })
            {
                if (!TryGetObject(root, spec, out _))
                {
                    _report.Error(category, $"{relative}: missing required spec '{spec}'");
                }
            }

            CheckBlockObject(root, relative, category);
            CheckBuildTime(root, relative, expectation, category);
            CheckBuilding(root, relative, expectation, category);
            CheckWorkplace(root, relative, expectation, category);
            CheckPlaceable(root, relative, expectation, category);
            CheckLabeled(root, relative, expectation, category);
            CheckTemplate(root, relative, expectation, category);
            CheckCustomSpec(root, relative, expectation, category);
            CheckChildren(root, relative, expectation, category);
            CheckChildSpecPlacement(root, relative, category);
            CheckTransformSlots(root, relative, expectation.FinishedModelFor(faction), category);
            }
        }
    }

    /// <summary>
    /// REQ-V2-BLD-04: the roof must be a stacking surface. 1.1.2.8 realises that with
    /// <c>BlockObjectNavMeshSettingsSpec.GenerateFloorsOnStackable = true</c> (the nav-mesh updater
    /// creates walking floors on the cells above every block whose <c>Stackable</c> is
    /// <c>BlockObject</c>, IL: <c>BlockSystemNavigation.NavMeshObjectUpdater.AddEdgesAddedByStackable</c>).
    /// The vanilla references for the field shape and values are the 3x3x1
    /// <c>MetalPlatform3x3</c>/<c>Roof3x2</c> and the 3x3x1 <c>HydroponicGarden</c> blueprints.
    /// </summary>
    private void CheckStackableRoofNavSettings(JsonElement root, string relative, string category)
    {
        if (!TryGetObject(root, "BlockObjectNavMeshSettingsSpec", out var nav))
        {
            _report.Error(category, $"{relative}: missing BlockObjectNavMeshSettingsSpec; without "
                                     + "GenerateFloorsOnStackable the roof would not become a walkable/stackable floor "
                                     + "(REQ-V2-BLD-04)");
            return;
        }

        ExpectBool(nav, category, relative, "BlockObjectNavMeshSettingsSpec", "NoAutoWalls", false);
        ExpectBool(nav, category, relative, "BlockObjectNavMeshSettingsSpec", "GenerateFloorsOnStackable", true);

        foreach (var name in new[] { "EdgeGroups", "UnblockedCoordinates", "BlockedEdges" })
        {
            if (!nav.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                _report.Error(category, $"{relative}: BlockObjectNavMeshSettingsSpec.{name} must be an array");
            }
            else if (value.GetArrayLength() != 0)
            {
                _report.Error(category, $"{relative}: BlockObjectNavMeshSettingsSpec.{name} must be empty for the "
                                         + "plain flat roof (no manual edges/blocked edges; found "
                                         + $"{value.GetArrayLength()} entrie(s))");
            }
        }
    }

    private void CheckBlockObject(JsonElement root, string relative, string category)
    {
        if (!TryGetObject(root, "BlockObjectSpec", out var blockObject))
        {
            _report.Error(category, $"{relative}: BlockObjectSpec is missing");
            return;
        }

        var sizeX = FootprintX;
        var sizeY = FootprintY;   // grid Y = depth
        var sizeZ = FootprintZ;   // grid Z = height
        if (!TryGetObject(blockObject, "Size", out var size))
        {
            _report.Error(category, $"{relative}: BlockObjectSpec.Size is missing");
        }
        else
        {
            sizeX = ExpectInt(size, category, relative, "BlockObjectSpec.Size", "X", FootprintX) ?? sizeX;
            sizeY = ExpectInt(size, category, relative, "BlockObjectSpec.Size", "Y", FootprintY) ?? sizeY;
            sizeZ = ExpectInt(size, category, relative, "BlockObjectSpec.Size", "Z", FootprintZ) ?? sizeZ;
        }

        if (sizeX != FootprintX || sizeY != FootprintY || sizeZ != FootprintZ)
        {
            _report.Error(category, $"{relative}: BlockObjectSpec.Size must be exactly "
                                     + $"{{X:{FootprintX}, Y:{FootprintY}, Z:{FootprintZ}}} for the v2 BoxFarm "
                                     + $"(REQ-V2-BLD-01 3 wide x 3 deep x 1 high; found {sizeX}x{sizeY}x{sizeZ})");
        }

        var roofOccupations = new HashSet<string>(StringComparer.Ordinal);

        if (!blockObject.TryGetProperty("Blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            _report.Error(category, $"{relative}: BlockObjectSpec.Blocks must be an array");
        }
        else
        {
            var count = blocks.GetArrayLength();
            var expectedCount = FootprintX * FootprintY * FootprintZ;
            if (count != expectedCount)
            {
                _report.Error(category, $"{relative}: BlockObjectSpec.Blocks must have {expectedCount} entries "
                                         + $"for the {FootprintX}x{FootprintY}x{FootprintZ} footprint (found {count}); "
                                         + "the v2 BoxFarm covers all nine placeholder blocks (REQ-V2-BLD-01)");
            }

            // The game enumerates blocks row-major in GRID coordinates, where X = width,
            // Y = depth and Z = height (IL evidence: TerrainService.GetTerrainHeight uses
            // coordinates.z as the column height, and VectorExtensions.XY() takes X/Y as the
            // horizontal pair):
            //   index n -> (x = n % Size.X, y = (n / Size.X) % Size.Y, z = n / (Size.X * Size.Y))
            // A 3 wide x 3 deep x 1 high building therefore has 9 blocks: Blocks[0..2] are the
            // facade depth row (y = 0), Blocks[3..5] y = 1 and Blocks[6..8] y = 2, all on the only
            // height layer (z = 0). Vanilla keeps the facade row solid on the ground and the rows
            // behind it as open floors (API_FACTS.md §3.2), and the v1 BoxFarm already used that
            // pattern for the 3x2 footprint — the v2 change only adds the third depth row and the
            // stackable roof.
            var solidOccupations = "Bottom, Top, Corners, Path, Middle";
            var floorOccupations = "Floor, Bottom, Corners, Path, Middle";

            if (sizeZ != FootprintZ)
            {
                _report.Error(category, $"{relative}: BlockObjectSpec.Size.Z is the building HEIGHT in grid "
                                         + $"coordinates and must be {FootprintZ} for the 3x3x1 BoxFarm (found {sizeZ})");
            }

            var index = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                var location = $"BlockObjectSpec.Blocks[{index}]";
                if (block.ValueKind != JsonValueKind.Object)
                {
                    _report.Error(category, $"{relative}: {location} must be an object");
                }
                else
                {
                    var depthRow = sizeX > 0 ? (index / sizeX) % Math.Max(sizeY, 1) : 0;
                    var heightLayer = sizeX > 0 && sizeY > 0 ? index / (sizeX * sizeY) : 0;
                    var expectedMatterBelow = depthRow == 0 ? "GroundOrStackable" : "Any";
                    var expectedOccupations = depthRow == 0 ? solidOccupations : floorOccupations;

                    var matterBelow = ExpectString(block, category, relative, location, "MatterBelow");
                    if (matterBelow is not null && matterBelow != expectedMatterBelow)
                    {
                        _report.Error(category, $"{relative}: {location}.MatterBelow must be "
                                                 + $"'{expectedMatterBelow}' for the y={depthRow} depth row "
                                                 + $"(z={heightLayer}) of a 1-high building "
                                                 + $"(vanilla uses GroundOrStackable on the entrance row and Any behind it; found '{matterBelow}')");
                    }

                    var occupations = ExpectString(block, category, relative, location, "Occupations");
                    if (occupations is not null)
                    {
                        if (occupations != expectedOccupations)
                        {
                            _report.Error(category, $"{relative}: {location}.Occupations must be "
                                                     + $"'{expectedOccupations}' for the y={depthRow} depth row "
                                                     + $"(z={heightLayer}) of a 1-high building "
                                                     + $"(found '{occupations}')");
                        }

                        foreach (var token in occupations.Split(',',
                                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            roofOccupations.Add(token);
                        }
                    }

                    // REQ-V2-BLD-04: the top layer (Z is the only height layer here) must be
                    // stackable with the real BlockStackable member value used by vanilla
                    // platforms/platform roofs/housing and by the 3x3x1 HydroponicGarden.
                    var stackable = ExpectString(block, category, relative, location, "Stackable");
                    if (stackable is not null && stackable != StackableBlockObject)
                    {
                        _report.Error(category, $"{relative}: {location}.Stackable must be "
                                                 + $"'{StackableBlockObject}' (REQ-V2-BLD-04: the roof must support "
                                                 + $"an upper building; found '{stackable}')");
                    }

                    ExpectBool(block, category, relative, location, "OccupyAllBelow", false);
                    ExpectBool(block, category, relative, location, "Underground", false);
                }

                index++;
            }

            // The roof blocks must carry both occupation placeholders REQ-V2-BLD-04 asks for:
            // the entrance row is the vanilla solid stackable row (Bottom/Top/...), the depth rows
            // behind it are the vanilla open-floor rows (Floor/Bottom/...), so the union of the
            // nine top-layer blocks provides both `Top` and `Floor`.
            if (!roofOccupations.Contains("Top") || !roofOccupations.Contains("Floor"))
            {
                _report.Error(category, $"{relative}: the BlockObjectSpec.Blocks occupations must provide both "
                                         + "the 'Top' and the 'Floor' placeholder flags on the top layer "
                                         + $"(REQ-V2-BLD-04; found [{string.Join(", ", roofOccupations.OrderBy(t => t, StringComparer.Ordinal))}])");
            }
        }

        CheckStackableRoofNavSettings(root, relative, category);

        if (!TryGetObject(blockObject, "Entrance", out var entrance))
        {
            _report.Error(category, $"{relative}: BlockObjectSpec.Entrance is missing");
        }
        else
        {
            ExpectBool(entrance, category, relative, "BlockObjectSpec.Entrance", "HasEntrance", true);
            if (TryGetObject(entrance, "Coordinates", out var coordinates))
            {
                ExpectInt(coordinates, category, relative, "BlockObjectSpec.Entrance.Coordinates", "X", 1);
                ExpectInt(coordinates, category, relative, "BlockObjectSpec.Entrance.Coordinates", "Y", -1);
                ExpectInt(coordinates, category, relative, "BlockObjectSpec.Entrance.Coordinates", "Z", 0);
            }
            else
            {
                _report.Error(category, $"{relative}: BlockObjectSpec.Entrance.Coordinates is missing");
            }
        }
    }

    /// <summary>
    /// REQ-V2-BLD-01 + docs/v2/INTERFACES.md §2: construction time is 1/1.5/2.5/4 workdays
    /// (halved from the v2.1 2/3/5/8 on 2026-10-27). In 1.1.2.8 the only blueprint field that
    /// controls construction duration is
    /// <c>ConstructionSiteBuildTimeSpec.ConstructionTimeInHours</c> (float, game hours):
    /// <c>ConstructionSiteBuildTimeCalculator.GetConstructionTimeInHours</c> returns it verbatim
    /// when the construction site has the spec, otherwise it derives
    /// <c>ceil(inventoryCapacity / MaterialToHourRatio)</c> (ratio 20) or the 1-hour default.
    /// Vanilla users of the spec: <c>EarthRecultivator</c> / <c>EarthRepopulator</c>. The card
    /// converts workdays with the documented 24 h/workday factor, so 1/1.5/2.5/4 workdays are
    /// 24/36/60/96 hours.
    /// </summary>
    private void CheckBuildTime(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        var location = "ConstructionSiteBuildTimeSpec";
        if (!TryGetObject(root, location, out var spec))
        {
            return;
        }

        if (!spec.TryGetProperty("ConstructionTimeInHours", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            _report.Error(category, $"{relative}: {location}.ConstructionTimeInHours must be a number "
                                     + "(the game field is a float in game hours)");
            return;
        }

        var hours = value.GetDouble();
        var expectedHours = expectation.BuildTimeInHours;
        if (Math.Abs(hours - expectedHours) > 1e-3)
        {
            _report.Error(category, $"{relative}: {location}.ConstructionTimeInHours must be "
                                     + $"{expectedHours.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)} "
                                     + $"({expectation.BuildWorkdays.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)} "
                                     + $"workdays x {BuildHoursPerWorkday} h/workday, REQ-V2-BLD-01 halved to "
                                     + $"1/1.5/2.5/4) for the {expectation.Tier} tier "
                                     + $"(found {hours.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)})");
        }
    }

    private void CheckBuilding(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "BuildingSpec", out var building))
        {
            return;
        }

        ExpectInt(building, category, relative, "BuildingSpec", "ScienceCost", expectation.ScienceCost);

        if (!building.TryGetProperty("BuildingCost", out var cost) || cost.ValueKind != JsonValueKind.Array)
        {
            _report.Error(category, $"{relative}: BuildingSpec.BuildingCost must be an array");
            return;
        }

        var actual = new List<(string Id, int Amount)>();
        foreach (var item in cost.EnumerateArray())
        {
            var id = ExpectString(item, category, relative, "BuildingSpec.BuildingCost[]", "Id");
            var amount = ExpectInt(item, category, relative, "BuildingSpec.BuildingCost[]", "Amount");
            if (id is not null && amount is not null)
            {
                actual.Add((id, amount.Value));
            }
        }

        var expected = expectation.Costs();
        if (!actual.SequenceEqual(expected))
        {
            var expectedText = string.Join(", ", expected.Select(c => $"{c.Id}:{c.Amount}"));
            var actualText = string.Join(", ", actual.Select(c => $"{c.Id}:{c.Amount}"));
            _report.Error(category, $"{relative}: BuildingCost must be [{expectedText}] (found [{actualText}])");
        }
    }

    private void CheckWorkplace(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "WorkplaceSpec", out var workplace))
        {
            return;
        }

        ExpectInt(workplace, category, relative, "WorkplaceSpec", "MaxWorkers", expectation.MaxWorkers);
        ExpectInt(workplace, category, relative, "WorkplaceSpec", "DefaultWorkers", expectation.MaxWorkers);
    }

    private void CheckPlaceable(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "PlaceableBlockObjectSpec", out var placeable))
        {
            return;
        }

        var toolGroup = ExpectString(placeable, category, relative, "PlaceableBlockObjectSpec", "ToolGroupId");
        if (toolGroup is not null && toolGroup != "Food")
        {
            _report.Error(category, $"{relative}: ToolGroupId must be 'Food' (found '{toolGroup}')");
        }

        ExpectInt(placeable, category, relative, "PlaceableBlockObjectSpec", "ToolOrder", expectation.ToolOrder);
    }

    private void CheckLabeled(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "LabeledEntitySpec", out var labeled))
        {
            return;
        }

        var nameKey = ExpectString(labeled, category, relative, "LabeledEntitySpec", "DisplayNameLocKey");
        if (nameKey is not null && nameKey != $"HDBF.Building.BoxFarm.{expectation.Tier}.Name")
        {
            _report.Error(category, $"{relative}: DisplayNameLocKey must be 'HDBF.Building.BoxFarm.{expectation.Tier}.Name' (found '{nameKey}')");
        }

        var descriptionKey = ExpectString(labeled, category, relative, "LabeledEntitySpec", "DescriptionLocKey");
        if (descriptionKey is not null && descriptionKey != $"HDBF.Building.BoxFarm.{expectation.Tier}.Description")
        {
            _report.Error(category, $"{relative}: DescriptionLocKey must be 'HDBF.Building.BoxFarm.{expectation.Tier}.Description'");
        }

        var icon = ExpectString(labeled, category, relative, "LabeledEntitySpec", "Icon");
        if (icon is not null && icon != $"Icons/BoxFarm{expectation.Tier}")
        {
            _report.Error(category, $"{relative}: Icon must be 'Icons/BoxFarm{expectation.Tier}' (found '{icon}')");
        }
    }

    private void CheckTemplate(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "TemplateSpec", out var template))
        {
            return;
        }

        var templateName = ExpectString(template, category, relative, "TemplateSpec", "TemplateName");
        if (templateName is not null && templateName != $"BoxFarm.{expectation.Tier}.{_currentFaction}")
        {
            _report.Error(category, $"{relative}: TemplateName must be 'BoxFarm.{expectation.Tier}' (found '{templateName}')");
        }
    }

    private void CheckCustomSpec(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "HdbfBoxFarmSpec", out var spec))
        {
            return;
        }

        var prefix = "HdbfBoxFarmSpec";
        var tier = ExpectString(spec, category, relative, prefix, "Tier");
        if (tier is not null && tier != expectation.Tier)
        {
            _report.Error(category, $"{relative}: HdbfBoxFarmSpec.Tier must be '{expectation.Tier}' (found '{tier}')");
        }

        ExpectInt(spec, category, relative, prefix, "PlotCapacity", expectation.PlotCapacity);
        ExpectInt(spec, category, relative, prefix, "PlotsPerFarmer", expectation.PlotsPerFarmer);
        ExpectInt(spec, category, relative, prefix, "WorkRangeX", expectation.WorkRangeX);
        ExpectInt(spec, category, relative, prefix, "WorkRangeZ", expectation.WorkRangeZ);
        ExpectFloat(spec, category, relative, prefix, "MinIrrigationCoverage", expectation.MinIrrigationCoverage);
        ExpectFloat(spec, category, relative, prefix, "WaterPerPlotPerDay", expectation.WaterPerPlotPerDay);
        ExpectInt(spec, category, relative, prefix, "TankCapacityDays", expectation.TankCapacityDays);
        ExpectFloat(spec, category, relative, prefix, "SoilFatigueFactor", expectation.SoilFatigueFactor);
        ExpectInt(spec, category, relative, prefix, "MaintenanceKind", expectation.MaintenanceKind);
        ExpectFloat(spec, category, relative, prefix, "MaintenanceCycleDays", expectation.MaintenanceCycleDays);
        ExpectInt(spec, category, relative, prefix, "MaintenanceMaterialAmount", expectation.MaintenanceMaterialAmount);
        ExpectBool(spec, category, relative, prefix, "RequiresPower", expectation.RequiresPower);
        ExpectInt(spec, category, relative, prefix, "PowerDemand", expectation.PowerDemand);
        ExpectBool(spec, category, relative, prefix, "HasPipePort", expectation.HasPipePort);
        ExpectBool(spec, category, relative, prefix, "HasLogisticsPort", expectation.HasLogisticsPort);
        ExpectBool(spec, category, relative, prefix, "HasPowerPort", expectation.HasPowerPort);
    }

    private void CheckChildren(JsonElement root, string relative, TierExpectation expectation, string category)
    {
        if (!TryGetObject(root, "Children", out var children))
        {
            _report.Error(category, $"{relative}: Children is missing");
            return;
        }

        if (!TryGetObject(children, "#Finished", out var finished))
        {
            _report.Error(category, $"{relative}: Children.#Finished is missing");
        }
        else
        {
            CheckModelAndColliders(finished, relative, expectation.FinishedModelFor(_currentFaction),
                "#Finished", category);
        }

        if (!TryGetObject(children, "#Unfinished", out var unfinished))
        {
            _report.Error(category, $"{relative}: Children.#Unfinished is missing");
            return;
        }

        if (!TryGetObject(unfinished, "Children", out var unfinishedChildren))
        {
            _report.Error(category, $"{relative}: Children.#Unfinished.Children is missing");
            return;
        }

        // REQ-V2-BLD-01: the construction base must match the 3x3 footprint (vanilla HydroponicGarden,
        // also 3x3x1, nests ConstructionBase3x3). The v1 3x2 base is a release regression.
        if (!TryGetObject(unfinishedChildren, "ConstructionBase3x3#nested", out var constructionBase))
        {
            _report.Error(category, $"{relative}: Children.#Unfinished must nest ConstructionBase3x3#nested "
                                     + "(the 3x3 footprint needs the 3x3 construction base; the 3x2 base is the v1 shape)");
        }
        else
        {
            var blueprintPath = ExpectString(constructionBase, category, relative,
                "Children.#Unfinished.Children.ConstructionBase3x3#nested", "BlueprintPath");
            const string expectedPath = "ConstructionBases/ConstructionBase3x3/ConstructionBase3x3.blueprint";
            if (blueprintPath is not null && blueprintPath != expectedPath)
            {
                _report.Error(category, $"{relative}: construction base BlueprintPath must be '{expectedPath}' (found '{blueprintPath}')");
            }
        }

        if (!TryGetObject(unfinishedChildren, "ConstructionStage0", out var stage))
        {
            _report.Error(category, $"{relative}: Children.#Unfinished.Children.ConstructionStage0 is missing");
        }
        else
        {
            CheckModelAndColliders(stage, relative, expectation.ConstructionModelFor(_currentFaction),
                "#Unfinished.ConstructionStage0", category);
        }
    }

    private void CheckModelAndColliders(JsonElement scope, string relative, string expectedModel,
        string location, string category)
    {
        if (!TryGetObject(scope, "TimbermeshSpec", out var timbermesh))
        {
            _report.Error(category, $"{relative}: Children.{location}.TimbermeshSpec is missing");
        }
        else
        {
            var model = ExpectString(timbermesh, category, relative, $"Children.{location}.TimbermeshSpec", "Model");
            if (model is not null)
            {
                // ART-03: #Finished must reference the custom finished model and
                // #Unfinished.ConstructionStage0 the matching custom construction-stage model, byte for
                // byte (ordinal), so a half-migrated blueprint cannot slip through.
                if (!string.Equals(model, expectedModel, StringComparison.Ordinal))
                {
                    _report.Error(category, $"{relative}: Children.{location}.TimbermeshSpec.Model must point at the "
                                             + $"shipped custom model '{expectedModel}' (found '{model}'); "
                                             + "see data/ART_PLACEHOLDER.md");
                }

                CheckModelAsset(model, relative, $"Children.{location}.TimbermeshSpec", category);
            }
        }

        if (!TryGetObject(scope, "CollidersSpec", out var colliders))
        {
            _report.Error(category, $"{relative}: Children.{location}.CollidersSpec is missing");
            return;
        }

        if (!colliders.TryGetProperty("BoxColliders", out var boxColliders)
            || boxColliders.ValueKind != JsonValueKind.Array
            || boxColliders.GetArrayLength() == 0)
        {
            _report.Error(category, $"{relative}: Children.{location}.CollidersSpec.BoxColliders must be a non-empty array");
            return;
        }

        // REQ-V2-BLD-01: the collision box must match the 3x3x1 model envelope. Colliders use
        // Unity local space (Y = height, Z = depth), so the v2 box is 3.0 wide x 1.0 high x 3.0
        // deep with its centre at the footprint middle (1.5, 0.5, 1.5). Vanilla HydroponicGarden
        // (3x3x1) uses the same 3x3 collider span.
        var first = boxColliders[0];
        if (TryGetObject(first, "Center", out var center))
        {
            ExpectFloat(center, category, relative, $"Children.{location}.CollidersSpec.Center", "X", 1.5);
            ExpectFloat(center, category, relative, $"Children.{location}.CollidersSpec.Center", "Y", 0.5);
            ExpectFloat(center, category, relative, $"Children.{location}.CollidersSpec.Center", "Z", 1.5);
        }
        else
        {
            _report.Error(category, $"{relative}: Children.{location}.CollidersSpec.BoxColliders[0].Center is missing");
        }

        if (TryGetObject(first, "Size", out var size))
        {
            ExpectFloat(size, category, relative, $"Children.{location}.CollidersSpec.Size", "X", 3.0);
            ExpectFloat(size, category, relative, $"Children.{location}.CollidersSpec.Size", "Y", 1.0);
            ExpectFloat(size, category, relative, $"Children.{location}.CollidersSpec.Size", "Z", 3.0);
        }
        else
        {
            _report.Error(category, $"{relative}: Children.{location}.CollidersSpec.BoxColliders[0].Size is missing");
        }
    }

    // ------------------------------------------------------------------ timbermesh assets

    /// <summary>
    /// ART-03: a <c>TimbermeshSpec.Model</c> must reference a real mod asset. The shipped contract is
    /// "the <c>.timbermesh</c> exists, starts with a valid zlib header and decompresses to a
    /// non-empty payload", and the payload integrity is also verified: the zlib trailer (last 4 file
    /// bytes, big-endian Adler-32 of the decompressed payload) must match the computed checksum, so
    /// a mid-stream truncation cannot pass as a valid model. This is deliberately not a protobuf
    /// parse: the model schema stays out of this tool.
    ///
    /// ART-04 adds ONE container-level check on top of the zlib contract, for the custom models this
    /// mod ships: the decompressed payload must name each of the three port marker empties exactly
    /// once and must not name any <c>#Slot#</c> marker (the six crop trough slots were removed when
    /// the roof became one empty flat platform). Nothing about the zlib/Adler-32 checks changes.
    ///
    /// A path that data/ does not ship is a pre-ART-03 vanilla placeholder model. That fallback is
    /// kept reachable on purpose: the old reference behaviour (the vanilla model's own blueprint
    /// must exist under <c>&lt;refRoot&gt;/Blueprints</c>) still runs, and the blueprint is told to
    /// point at the shipped custom model instead.
    /// </summary>
    private byte[]? CheckModelAsset(string model, string relative, string location, string category)
    {
        if (_modelBytesByPath.TryGetValue(model, out var cached))
        {
            return cached;
        }

        var verified = CheckModelAssetCore(model, relative, location, category);
        _modelBytesByPath[model] = verified;
        return verified;
    }

    private byte[]? CheckModelAssetCore(string model, string relative, string location, string category)
    {
        if (Path.IsPathRooted(model) || HasParentDirectorySegment(model))
        {
            _report.Error(category, $"{relative}: {location}.Model '{model}' must be a relative path below data/ "
                                     + "without a drive/root prefix or a '..' segment");
            return null;
        }

        var fullPath = Path.Combine(_dataDir, model.Replace('/', Path.DirectorySeparatorChar) + ".timbermesh");
        if (!File.Exists(fullPath))
        {
            if (IsCustomModelPath(model))
            {
                _report.Error(category, $"{relative}: {location}.Model '{model}' -> custom model asset not found: "
                                         + $"{fullPath} (data/ must ship this .timbermesh)");
            }
            else
            {
                // Pre-ART-03 fallback: the model only exists inside the game, so the .ref tree is the
                // only place the old placeholder contract can still be verified.
                var referenceBlueprint = ReferenceBlueprintPathFor(model);
                if (!File.Exists(referenceBlueprint))
                {
                    _report.Error(category, $"{relative}: {location}.Model '{model}' is a vanilla placeholder path, "
                                             + "but its reference blueprint is missing: "
                                             + $"{referenceBlueprint} (regenerate .ref with tools/ApiIndex or pass --ref)");
                }
            }

            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _report.Error(category, $"{relative}: {location}.Model '{model}' could not be read: {fullPath}: {ex.Message}");
            return null;
        }

        if (bytes.Length <= 4)
        {
            _report.Error(category, $"{relative}: {location}.Model '{model}' is not a usable .timbermesh: "
                                     + $"{fullPath} is only {bytes.Length} byte(s)");
            return null;
        }

        // zlib header (RFC 1950): the low nibble of CMF is the deflate method (8), and the 16-bit
        // big-endian CMF/FLG pair must be a multiple of 31 (FCHECK carries the remainder).
        if ((bytes[0] & 0x0F) != 8 || ((bytes[0] << 8) | bytes[1]) % 31 != 0)
        {
            _report.Error(category, $"{relative}: {location}.Model '{model}' has an invalid zlib header "
                                     + $"(0x{bytes[0]:X2} 0x{bytes[1]:X2}) in {fullPath}; the asset is corrupt or "
                                     + "not a .timbermesh");
            return null;
        }

        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var decompressed = output.ToArray();
            if (decompressed.Length == 0)
            {
                _report.Error(category, $"{relative}: {location}.Model '{model}' decompresses to an empty payload: "
                                         + $"{fullPath}");
                return null;
            }

            // Payload integrity: the zlib trailer is the last 4 file bytes, big-endian Adler-32 of
            // the decompressed payload. System.IO.Compression.ZLibStream is lenient about a stream
            // that ends mid-deflate-block, so this trailer is what rejects a truncated .timbermesh.
            var declaredAdler = ((uint)bytes[^4] << 24) | ((uint)bytes[^3] << 16)
                                | ((uint)bytes[^2] << 8) | bytes[^1];
            var computedAdler = ComputeAdler32(decompressed);
            if (declaredAdler != computedAdler)
            {
                _report.Error(category, $"{relative}: {location}.Model '{model}' has a corrupt/truncated zlib "
                                         + $"stream: Adler-32 trailer mismatch in {fullPath} "
                                         + $"(declared 0x{declaredAdler:X8}, computed 0x{computedAdler:X8})");
                return null;
            }

            // ART-04: container-level marker contract, only for the models this mod ships. The
            // check reads the decompressed protobuf payload (node names are plain UTF-8 strings).
            // It runs after the zlib/Adler-32 checks so a corrupt file is still reported as
            // corrupt rather than as a marker-contract violation.
            if (IsCustomModelPath(model))
            {
                CheckCustomModelMarkerContract(model, relative, location, category, decompressed);
            }

            return decompressed;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            _report.Error(category, $"{relative}: {location}.Model '{model}' could not be decompressed "
                                     + $"(corrupt .timbermesh?): {fullPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ART-04 data-side marker contract for a custom model's decompressed payload: exactly the three
    /// rear port empties (<c>#Port#Power</c>, <c>#Port#Pipe</c>, <c>#Port#Logistics</c>), each once,
    /// and zero <c>#Slot#</c> marker nodes. The roof is one empty flat platform now, so a surviving
    /// <c>#Slot#Crop_0..5</c> node means an old / half-migrated mesh was copied into <c>data/</c>.
    /// </summary>
    private void CheckCustomModelMarkerContract(string model, string relative, string location,
        string category, byte[] payload)
    {
        foreach (var marker in RequiredArtPortMarkers)
        {
            var count = CountUtf8Occurrences(payload, marker);
            if (count == 0)
            {
                _report.Error(category, $"{relative}: {location}.Model '{model}' is missing the required port "
                                         + $"marker node '{marker}' in its decompressed payload (ART-04 contract: "
                                         + $"every custom model keeps exactly the three rear ports "
                                         + $"{string.Join(", ", RequiredArtPortMarkers)}; re-export with "
                                         + "art/export_timbermesh.py)");
            }
            else if (count > 1)
            {
                _report.Error(category, $"{relative}: {location}.Model '{model}' contains the port marker node "
                                         + $"'{marker}' {count} times in its decompressed payload; exactly one node "
                                         + "per port is required (ART-04 contract)");
            }
        }

        var slotMarkerCount = CountUtf8Occurrences(payload, ForbiddenArtMarker);
        if (slotMarkerCount > 0)
        {
            _report.Error(category, $"{relative}: {location}.Model '{model}' still contains {slotMarkerCount} "
                                     + $"'{ForbiddenArtMarker}' marker node(s) in its decompressed payload; ART-04 "
                                     + "removed the six crop slot markers (#Slot#Crop_0..5) because the roof is now "
                                     + "one empty flat platform with nothing above it — re-export with "
                                     + "art/export_timbermesh.py instead of copying an older .timbermesh");
        }
    }

    /// <summary>
    /// RFC 1950 Adler-32 over the decompressed payload: a starts at 1, b at 0, both reduced
    /// modulo 65521, and the result is <c>(b &lt;&lt; 16) | a</c>.
    /// </summary>
    private static uint ComputeAdler32(byte[] data)
    {
        const uint modulus = 65521;
        uint a = 1;
        uint b = 0;

        foreach (var value in data)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }

        return (b << 16) | a;
    }

    /// <summary>True when the path belongs to the 16 models this mod ships under data/.</summary>
    private static bool IsCustomModelPath(string model) =>
        model.StartsWith(CustomModelDirectory + "/", StringComparison.Ordinal);

    private static bool HasParentDirectorySegment(string model) =>
        model.Split(new[] { '/', '\\' }, StringSplitOptions.None).Any(segment => segment == "..");

    /// <summary>
    /// The vanilla blueprint a placeholder model path refers to, under the external reference tree
    /// (default <c>%USERPROFILE%\.hdbf\ref\Blueprints</c>, override with <c>--ref</c>). ART-03 keeps
    /// this path reachable so the pre-art placeholder check still works unchanged.
    /// </summary>
    private string ReferenceBlueprintPathFor(string model)
    {
        var blueprintPath = model.EndsWith(".Model", StringComparison.Ordinal)
            ? model[..^".Model".Length] + ".blueprint.json"
            : model + ".blueprint.json";
        return Path.Combine(_refRoot, "Blueprints", blueprintPath.Replace('/', Path.DirectorySeparatorChar));
    }

    // ------------------------------------------------------------------ slot / child-spec placement

    // Vanilla puts workplace and slot specs on the blueprint root only. Smoke-test round 4 found a
    // previous patch had duplicated three of them inside Children.#Unfinished: the copies are
    // instantiated on the unfinished prefab, and a slot spec whose keyword has no transform there
    // throws `InvalidOperationException: There are no "<keyword>" slots in <prefab>` at placement.
    private static readonly string[] RootOnlySpecs =
    {
        "TransformSlotInitializerSpec", "WorkplaceSlotManagerSpec",
        "WorkplaceIlluminatorSpec", "WorkplaceWorkerOutfitSpec",
    };

    private void CheckChildSpecPlacement(JsonElement root, string relative, string category)
    {
        if (!TryGetObject(root, "Children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateObject())
        {
            CheckChildSpecPlacementRecursive(child.Value, relative, category, "Children." + child.Name);
        }
    }

    private void CheckChildSpecPlacementRecursive(JsonElement blueprint, string relative, string category,
        string location)
    {
        if (blueprint.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in blueprint.EnumerateObject())
        {
            if (RootOnlySpecs.Contains(property.Name, StringComparer.Ordinal))
            {
                _report.Error(category, $"{relative}: {location}.{property.Name} must be on the blueprint root, not "
                                         + "inside a child blueprint (a duplicated slot spec is instantiated for the "
                                         + "child prefab and makes placement throw)");
                continue;
            }

            if (property.Name == "Children" && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var grandChild in property.Value.EnumerateObject())
                {
                    CheckChildSpecPlacementRecursive(grandChild.Value, relative, category,
                        location + ".Children." + grandChild.Name);
                }
            }
        }
    }

    /// <summary>
    /// Worker slot cross-check. For a blueprint that still points at a vanilla placeholder model,
    /// every declared transform slot must name a transform that exists in that model, otherwise the
    /// game throws when the entity is instantiated. The source of truth is the placeholder model's
    /// own vanilla blueprint under the external reference directory (default
    /// `%USERPROFILE%\.hdbf\ref\Blueprints`, override with `--ref`); when that copy is absent the
    /// check is skipped with one warning. For a shipped custom model the contract is different (see
    /// <see cref="CheckCustomModelSlotSpecs"/>).
    ///
    /// ART-03 resolves the model from the blueprint itself (not from the tier expectation) so an
    /// un-migrated blueprint that still points at a vanilla placeholder really does take the legacy
    /// reference path even while the expectation is already the custom model.
    /// </summary>
    private void CheckTransformSlots(JsonElement root, string relative, string expectedModelPath, string category)
    {
        var modelPath = ActualFinishedModelPath(root) ?? expectedModelPath;
        if (IsModAsset(modelPath))
        {
            CheckCustomModelSlotSpecs(root, relative, modelPath, category);
            return;
        }

        if (IsCustomModelPath(modelPath))
        {
            // A custom-model path whose file is missing/unsafe: CheckModelAsset already failed the
            // run. Do not fall through to the vanilla .ref keyword cross-check — no reference
            // blueprint can exist for a model this mod ships itself.
            return;
        }

        var referenceKeywords = ReadReferenceSlotKeywords(modelPath, category);
        if (referenceKeywords is null)
        {
            return;
        }

        if (!TryGetObject(root, "TransformSlotInitializerSpec", out var slotSpec))
        {
            if (referenceKeywords.Count > 0)
            {
                _report.Warn(category, $"{relative}: no TransformSlotInitializerSpec, but the placeholder model "
                                        + $"'{modelPath}' provides slots [{string.Join(", ", referenceKeywords)}]; "
                                        + "workers simply get no animation slots (allowed, matches 6 vanilla workplaces)");
            }

            return;
        }

        if (!slotSpec.TryGetProperty("Slots", out var slots) || slots.ValueKind != JsonValueKind.Array
            || slots.GetArrayLength() == 0)
        {
            _report.Error(category, $"{relative}: TransformSlotInitializerSpec.Slots must be a non-empty array");
            return;
        }

        if (referenceKeywords.Count == 0)
        {
            _report.Error(category, $"{relative}: the placeholder model '{modelPath}' has no transform slots "
                                     + "(its vanilla blueprint declares no TransformSlotInitializerSpec); remove the "
                                     + "spec or the game throws \"There are no ... slots in <prefab>\" at placement");
            return;
        }

        var index = 0;
        foreach (var slot in slots.EnumerateArray())
        {
            var location = $"TransformSlotInitializerSpec.Slots[{index}]";
            if (slot.ValueKind != JsonValueKind.Object)
            {
                _report.Error(category, $"{relative}: {location} must be an object");
            }
            else
            {
                var keyword = ExpectString(slot, category, relative, location, "SlotKeyword");
                if (keyword is not null && !referenceKeywords.Contains(keyword, StringComparer.Ordinal))
                {
                    _report.Error(category, $"{relative}: {location}.SlotKeyword '{keyword}' does not exist in the "
                                             + $"placeholder model '{modelPath}' (vanilla slot keywords: "
                                             + $"{string.Join(", ", referenceKeywords)})");
                }
            }

            index++;
        }
    }

    /// <summary>
    /// ART-03 slot contract for the shipped custom models. The models carry crop/port marker nodes
    /// only, i.e. zero worker slot transforms, so the two specs that bind workers to slot transforms
    /// must stay absent — the same contract smoke-test round 4 established for the vanilla
    /// Top/Folktails placeholder and for the 10-worker <c>Mine</c> (docs/VERIFICATION.md 禮12):
    /// an empty <c>WorkplaceSlotManager</c> turns "no slot" into "all slots taken" (the workplace
    /// reads as full), and a <c>TransformSlotInitializer</c> whose keyword matches no transform
    /// throws <c>InvalidOperationException: There are no "&lt;keyword&gt;" slots in &lt;prefab&gt;</c>
    /// as soon as the construction site is instantiated. If the spec is present anyway its keywords
    /// are still checked against the decompressed model bytes (protobuf node names are plain UTF-8),
    /// so the report names exactly which keyword cannot work.
    /// </summary>
    private void CheckCustomModelSlotSpecs(JsonElement root, string relative, string modelPath, string category)
    {
        if (root.TryGetProperty("WorkplaceSlotManagerSpec", out _))
        {
            _report.Error(category, $"{relative}: WorkplaceSlotManagerSpec must be absent for the custom model "
                                     + $"'{modelPath}': the model ships no worker slot transforms, so the manager would "
                                     + "report the workplace as permanently full (round-4 contract, same as the vanilla "
                                     + "Top/Folktails placeholder and the 10-worker Mine)");
        }

        if (!root.TryGetProperty("TransformSlotInitializerSpec", out var slotSpec))
        {
            return;
        }

        _report.Error(category, $"{relative}: TransformSlotInitializerSpec must be absent for the custom model "
                                 + $"'{modelPath}': the model ships no worker slot transforms and the initializer throws "
                                 + "\"There are no '<keyword>' slots in <prefab>\" when a declared keyword matches no "
                                 + "transform (docs/VERIFICATION.md 禮12)");

        if (!_modelBytesByPath.TryGetValue(modelPath, out var modelBytes) || modelBytes is null)
        {
            // Missing/corrupt asset: CheckModelAsset already failed the run, and the keyword bytes
            // cannot be checked without the decompressed model.
            return;
        }

        if (!TryGetObject(root, "TransformSlotInitializerSpec", out slotSpec)
            || !slotSpec.TryGetProperty("Slots", out var slots)
            || slots.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var slot in slots.EnumerateArray())
        {
            var location = $"TransformSlotInitializerSpec.Slots[{index}]";
            if (slot.ValueKind != JsonValueKind.Object)
            {
                _report.Error(category, $"{relative}: {location} must be an object");
            }
            else
            {
                var keyword = ExpectString(slot, category, relative, location, "SlotKeyword");
                if (keyword is not null && !ContainsUtf8(modelBytes, keyword))
                {
                    _report.Error(category, $"{relative}: {location}.SlotKeyword '{keyword}' does not occur in the "
                                             + $"custom model '{modelPath}' (the model has no such node; "
                                             + "TransformSlotInitializer would throw at placement)");
                }
            }

            index++;
        }
    }

    /// <summary>True when the model path resolves to a .timbermesh shipped under data/.</summary>
    private bool IsModAsset(string modelPath) =>
        !Path.IsPathRooted(modelPath)
        && !HasParentDirectorySegment(modelPath)
        && File.Exists(Path.Combine(_dataDir,
            modelPath.Replace('/', Path.DirectorySeparatorChar) + ".timbermesh"));

    /// <summary>
    /// The model string the blueprint's finished prefab actually references, or null when the
    /// property is missing/unreadable (the caller then falls back to the tier expectation).
    /// </summary>
    private static string? ActualFinishedModelPath(JsonElement root)
    {
        if (!TryGetObject(root, "Children", out var children)
            || !TryGetObject(children, "#Finished", out var finished)
            || !TryGetObject(finished, "TimbermeshSpec", out var timbermesh)
            || !timbermesh.TryGetProperty("Model", out var model)
            || model.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = model.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Ordinal byte search: protobuf strings (node names) are stored as plain UTF-8.</summary>
    private static bool ContainsUtf8(byte[] haystack, string text) =>
        text.Length > 0 && haystack.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;

    /// <summary>
    /// Counts non-overlapping ordinal UTF-8 occurrences, so a duplicated protobuf node name is
    /// distinguishable from a single one (ART-04 requires each port marker exactly once).
    /// </summary>
    private static int CountUtf8Occurrences(byte[] haystack, string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var needle = Encoding.UTF8.GetBytes(text);
        var count = 0;
        var offset = 0;
        while (offset <= haystack.Length - needle.Length)
        {
            var index = haystack.AsSpan(offset).IndexOf(needle);
            if (index < 0)
            {
                break;
            }

            count++;
            offset += index + needle.Length;
        }

        return count;
    }

    private List<string>? ReadReferenceSlotKeywords(string modelPath, string category)
    {
        if (_referenceUnavailable)
        {
            return null;
        }

        var fullPath = ReferenceBlueprintPathFor(modelPath);
        if (!File.Exists(fullPath))
        {
            _referenceUnavailable = true;
            _report.Warn(category, $"placeholder reference blueprints not found under '{_refRoot}'; "
                                   + "transform-slot cross-check skipped (regenerate them with tools/ApiIndex "
                                   + "or pass --ref <dir>)");
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        var referenceRoot = document.RootElement;
        if (!TryGetObject(referenceRoot, "TransformSlotInitializerSpec", out var spec)
            || !spec.TryGetProperty("Slots", out var slots)
            || slots.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        var keywords = new List<string>();
        foreach (var slot in slots.EnumerateArray())
        {
            if (slot.ValueKind == JsonValueKind.Object
                && slot.TryGetProperty("SlotKeyword", out var keyword)
                && keyword.ValueKind == JsonValueKind.String)
            {
                keywords.Add(keyword.GetString() ?? string.Empty);
            }
        }

        return keywords;
    }

    // ------------------------------------------------------------------ collections + icons

    private void CheckTemplateCollections()
    {
        const string category = "collection";

        foreach (var (collectionId, suffix) in new[]
                 {
                     ("Buildings.Folktails", "Folktails"),
                     ("Buildings.IronTeeth", "IronTeeth"),
                 })
        {
            var expectedPaths = BlueprintSuffixes
                .Select(tier => $"Buildings/Food/BoxFarm/BoxFarm.{tier}.{suffix}.blueprint")
                .ToArray();
            var relative = $"TemplateCollections/TemplateCollection.{collectionId}.blueprint.json";
            var path = Path.Combine(_dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _report.Error(category, $"{relative} is missing");
                continue;
            }

            using var document = ParseOrReport(path, relative);
            if (document is null)
            {
                continue;
            }

            if (!TryGetObject(document.RootElement, "TemplateCollectionSpec", out var spec))
            {
                _report.Error(category, $"{relative}: missing TemplateCollectionSpec");
                continue;
            }

            var actualCollectionId = ExpectString(spec, category, relative, "TemplateCollectionSpec", "CollectionId");
            if (actualCollectionId is not null && actualCollectionId != collectionId)
            {
                _report.Error(category, $"{relative}: CollectionId must be '{collectionId}' (found '{actualCollectionId}')");
            }

            if (!spec.TryGetProperty("Blueprints#append", out var append) || append.ValueKind != JsonValueKind.Array)
            {
                _report.Error(category, $"{relative}: missing 'Blueprints#append' array");
                continue;
            }

            var actualPaths = new List<string>();
            foreach (var item in append.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    actualPaths.Add(item.GetString() ?? string.Empty);
                }
            }

            if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            {
                _report.Error(category, $"{relative}: Blueprints#append must list the four BoxFarm blueprints "
                                         + $"in Basic..Top order (found: {string.Join(", ", actualPaths)})");
            }

            foreach (var blueprintPath in actualPaths)
            {
                var blueprintFile = Path.Combine(_dataDir,
                    (blueprintPath + ".json").Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(blueprintFile))
                {
                    _report.Error(category, $"{relative}: referenced blueprint '{blueprintPath}' does not exist under data/");
                }
            }
        }
    }

    private void CheckIcons()
    {
        const string category = "icon";
        var iconDir = Path.Combine(_dataDir, "Icons");
        if (!Directory.Exists(iconDir))
        {
            _report.Error(category, "data/Icons/ is missing");
            return;
        }

        foreach (var tier in BlueprintSuffixes)
        {
            var name = "BoxFarm" + tier;
            var pngPath = Path.Combine(iconDir, name + ".png");
            var metaPath = pngPath + ".meta.json";

            if (!File.Exists(pngPath))
            {
                _report.Error(category, $"Icons/{name}.png is missing");
                continue;
            }

            if (!File.Exists(metaPath))
            {
                _report.Error(category, $"Icons/{name}.png.meta.json is missing (the game needs it to convert the sprite)");
            }
            else
            {
                using var metaDocument = ParseOrReport(metaPath, $"Icons/{name}.png.meta.json");
                if (metaDocument is not null)
                {
                    var root = metaDocument.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("isSprite", out var isSprite)
                        || isSprite.ValueKind != JsonValueKind.True)
                    {
                        _report.Error(category, $"Icons/{name}.png.meta.json must contain {{\"isSprite\": true}}");
                    }
                }
            }

            CheckPngDimensions(pngPath, $"Icons/{name}.png", category);
        }

        var pngs = Directory.GetFiles(iconDir, "*.png");
        if (pngs.Length != BlueprintSuffixes.Length)
        {
            _report.Warn(category, $"data/Icons/ contains {pngs.Length} PNGs; expected exactly {BlueprintSuffixes.Length} tier icons");
        }
    }

    private void CheckPngDimensions(string path, string relative, string category)
    {
        try
        {
            var header = new byte[24];
            using var stream = File.OpenRead(path);
            var read = stream.Read(header, 0, header.Length);
            if (read < 24
                || header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
            {
                _report.Error(category, $"{relative} is not a PNG file");
                return;
            }

            var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

            if (width != 128 || height != 128)
            {
                _report.Error(category, $"{relative} must be 128x128 (found {width}x{height})");
            }
        }
        catch (IOException ex)
        {
            _report.Error(category, $"{relative} could not be read: {ex.Message}");
        }
    }

    private void CheckArtPlaceholderNotes()
    {
        const string category = "art";
        var path = Path.Combine(_dataDir, "ART_PLACEHOLDER.md");
        if (!File.Exists(path))
        {
            _report.Error(category, "data/ART_PLACEHOLDER.md is missing (the shipped custom models must be documented)");
            return;
        }

        // ART-03: the notes must document every custom model the mod ships — all 8 finished paths
        // AND all 8 ConstructionStage0 paths (the old vanilla placeholder strings are gone).
        var text = File.ReadAllText(path);
        foreach (var expectation in Tiers)
        {
            foreach (var faction in Factions)
            {
                var finished = expectation.FinishedModelFor(faction);
                if (!text.Contains(finished, StringComparison.Ordinal))
                {
                    _report.Error(category, $"ART_PLACEHOLDER.md does not document the {expectation.Tier}/{faction} "
                                             + $"finished model '{finished}'");
                }

                var construction = expectation.ConstructionModelFor(faction);
                if (!text.Contains(construction, StringComparison.Ordinal))
                {
                    _report.Error(category, $"ART_PLACEHOLDER.md does not document the {expectation.Tier}/{faction} "
                                             + $"ConstructionStage0 model '{construction}'");
                }
            }
        }
    }

    // ------------------------------------------------------------------ tech gating (chapter 7)

    /// <summary>
    /// Proves the four tiers are really locked behind research in the installed game version.
    ///
    /// Timberborn 1.1.2.8 has no research-node blueprint type (verified with ApiIndex against the
    /// installed assemblies and against the official StreamingAssets/Modding/Blueprints.zip): the
    /// gate is <c>BuildingSpec.ScienceCost</c> read by <c>BuildingUnlockingService.Unlocked</c>,
    /// which returns true only when the cost is zero or the player already paid it. So this check
    /// enforces the four chapter-7 costs, keeps every cost positive (a zero cost means the tier is
    /// buildable from the start), keeps both factions identical (REQ-TECH-06), rejects specs that
    /// would silently replace the gate, and requires each gated blueprint to be registered so the
    /// locked tool is visible in the build menu (REQ-TECH-02/04).
    /// </summary>
    private void CheckTechGating()
    {
        const string category = "tech";
        var costsByTier = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var expectation in Tiers)
        {
            var tierCosts = new Dictionary<string, int>(StringComparer.Ordinal);
            costsByTier[expectation.Tier] = tierCosts;

            foreach (var faction in Factions)
            {
                _currentFaction = faction;
                var relative = $"Buildings/Food/BoxFarm/BoxFarm.{expectation.Tier}.{faction}.blueprint.json";
                var path = Path.Combine(_dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    // CheckBoxFarmBlueprints already reports the missing file.
                    continue;
                }

                using var document = ParseOrReport(path, relative);
                if (document is null)
                {
                    continue;
                }

                var root = document.RootElement;
                if (!ExpectObject(root, category, relative))
                {
                    continue;
                }

                if (!TryGetObject(root, "BuildingSpec", out var building))
                {
                    _report.Error(category, $"{relative}: missing BuildingSpec (the unlock gate reads its ScienceCost)");
                    continue;
                }

                var cost = TryGetInt(building, "ScienceCost", out var costValue) ? costValue : (int?)null;
                if (cost is null)
                {
                    _report.Error(category, $"{relative}: BuildingSpec.ScienceCost must be an integer "
                                             + "(it is the chapter-7 unlock price and the only data-level lock in 1.1.2.8)");
                }
                else
                {
                    tierCosts[faction] = cost.Value;

                    if (cost.Value <= 0)
                    {
                        _report.Error(category, $"{relative}: BuildingSpec.ScienceCost must be > 0, otherwise "
                                                 + "the tier is buildable from the start (BuildingUnlockingService treats "
                                                 + "ScienceCost == 0 as always unlocked)");
                    }
                    else if (cost.Value != expectation.ScienceCost)
                    {
                        _report.Error(category, $"{relative}: BuildingSpec.ScienceCost must be the chapter-7 "
                                                 + $"{expectation.Tier} price {expectation.ScienceCost} (found {cost.Value})");
                    }
                }

                foreach (var wrongSpec in WrongTechSpecs)
                {
                    if (root.TryGetProperty(wrongSpec, out _))
                    {
                        _report.Error(category, $"{relative}: '{wrongSpec}' must not be used; it changes the unlock "
                                                 + "mechanism. The chapter-7 gate is BuildingSpec.ScienceCost only.");
                    }
                }

                var collectionEntries = ReadTemplateCollectionEntries(faction);
                var blueprintPath = relative[..^".json".Length];
                if (collectionEntries is not null
                    && !collectionEntries.Contains(blueprintPath, StringComparer.Ordinal))
                {
                    _report.Error(category, $"{relative}: blueprint '{blueprintPath}' is not registered in "
                                             + $"TemplateCollections/TemplateCollection.Buildings.{faction}.blueprint.json; "
                                             + "a locked tier must still appear (greyed/locked) in the build menu");
                }
            }

            if (tierCosts.Count == Factions.Length
                && tierCosts.Values.Distinct().Count() > 1)
            {
                var detail = string.Join(", ", tierCosts.Select(pair => $"{pair.Key}={pair.Value}"));
                _report.Error(category, $"BoxFarm.{expectation.Tier}: both factions must use the same ScienceCost "
                                         + $"(REQ-TECH-06; found {detail})");
            }
        }

        // Chapter 7 stages must be affordable in ascending order; a reordering would let the player
        // skip the vanilla farming / lumber / metal / power progression the requirement describes.
        var ordered = Tiers.Select(t => t.ScienceCost).ToList();
        if (!ordered.SequenceEqual(ordered.OrderBy(value => value)))
        {
            _report.Error(category, "tier ScienceCost values must strictly increase Basic -> Intermediate "
                                     + $"-> Advanced -> Top (found {string.Join(" -> ", ordered)})");
        }
    }

    private List<string>? ReadTemplateCollectionEntries(string faction)
    {
        if (_collectionEntries.TryGetValue(faction, out var cached))
        {
            return cached;
        }

        var relative = $"TemplateCollections/TemplateCollection.Buildings.{faction}.blueprint.json";
        var path = Path.Combine(_dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            // CheckTemplateCollections reports the missing file.
            return null;
        }

        using var document = ParseOrReport(path, relative);
        if (document is null)
        {
            return null;
        }

        if (!TryGetObject(document.RootElement, "TemplateCollectionSpec", out var spec)
            || !spec.TryGetProperty("Blueprints#append", out var append)
            || append.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = new List<string>();
        foreach (var item in append.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                entries.Add(item.GetString() ?? string.Empty);
            }
        }

        _collectionEntries[faction] = entries;
        return entries;
    }

    // ------------------------------------------------------------------ mechanical / on-grid power (chapters 6.5, 8.3, 11.1)

    /// <summary>
    /// Enforces the Top-only on-grid power geometry. Vanilla consumers declare
    /// <c>MechanicalNodeSpec.PowerInput</c> plus one or more <c>TransputProviderSpec</c> connection
    /// faces; the transput coordinates are footprint-local and each direction names the adjacent
    /// cell the connection is made towards, so the geometry can be proven from JSON alone.
    ///
    /// Only the two Top blueprints may carry mechanical specs; every Top transput must sit inside
    /// the 3x3x1 footprint, must sit on the rear depth row (y = Size.Y - 1 = 2) so the socket set
    /// mirrors the v1 "full rear row" contract on the 3-deep model, must not point through the front
    /// face (the HDBF entrance and worker access live there; front = grid -Y = direction "Down") and
    /// must not point up/down ("Top"/"Bottom", the vertical faces; REQ-IMPL-05 forbids roof
    /// connection points). The back face is grid +Y = direction "Up"; IL: Direction3DExtensions.ToOffset
    /// maps Up to +Y, Left/Right to -/+X and Top to +Z (grid Z is the vertical axis).
    /// The Top tier also has to keep the off-grid fallback fields (RequiresPower/PowerDemand), so
    /// the check ties PowerDemand to PowerInput instead of replacing it.
    /// </summary>
    private void CheckMechanicalPower()
    {
        const string category = "mechanical";
        var powerInputs = new Dictionary<string, int>(StringComparer.Ordinal);
        var transputSignatures = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var expectation in Tiers)
        {
            var isTop = expectation.Tier == "Top";

            foreach (var faction in Factions)
            {
                _currentFaction = faction;
                var relative = $"Buildings/Food/BoxFarm/BoxFarm.{expectation.Tier}.{faction}.blueprint.json";
                var path = Path.Combine(_dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    continue;
                }

                using var document = ParseOrReport(path, relative);
                if (document is null)
                {
                    continue;
                }

                var root = document.RootElement;
                if (!ExpectObject(root, category, relative))
                {
                    continue;
                }

                if (!isTop)
                {
                    foreach (var specName in MechanicalSpecNames)
                    {
                        if (root.TryGetProperty(specName, out _))
                        {
                            _report.Error(category, $"{relative}: '{specName}' is only allowed on the Top tier; "
                                                     + $"{expectation.Tier} is an off-grid building (REQ-IMPL-21)");
                        }
                    }

                    continue;
                }

                CheckTopMechanicalSpecs(root, relative, category, powerInputs, transputSignatures);
            }
        }

        if (powerInputs.Count == Factions.Length
            && powerInputs.Values.Distinct().Count() > 1)
        {
            var detail = string.Join(", ", powerInputs.Select(pair => $"{pair.Key}={pair.Value}"));
            _report.Error(category, $"Top power input must be identical for both factions (REQ-TECH-06; found {detail})");
        }

        if (transputSignatures.Count == Factions.Length
            && transputSignatures.Values.Distinct(StringComparer.Ordinal).Count() > 1)
        {
            _report.Error(category, "Top power connection geometry must be identical for both factions "
                                     + $"(REQ-TECH-06; found {string.Join(" vs ", transputSignatures.Select(pair => $"{pair.Key}: [{pair.Value}]"))})");
        }
    }

    private void CheckTopMechanicalSpecs(JsonElement root, string relative, string category,
        Dictionary<string, int> powerInputs, Dictionary<string, string> transputSignatures)
    {
        // 1. Vanilla consumer marker components.
        AssertEmptyMarkerSpec(root, relative, category, "MechanicalBuildingSpec");
        AssertEmptyMarkerSpec(root, relative, category, "MechanicalConnectorTargetSpec");

        // 2. The node itself: a 150-power consumer, never a generator or a shaft.
        if (!TryGetObject(root, "MechanicalNodeSpec", out var node))
        {
            _report.Error(category, $"{relative}: missing MechanicalNodeSpec (Top needs a mechanical node with "
                                     + "PowerInput 150 for the on-grid path)");
        }
        else
        {
            ExpectInt(node, category, relative, "MechanicalNodeSpec", "PowerOutput", 0);
            ExpectInt(node, category, relative, "MechanicalNodeSpec", "PowerInput", 150);
            ExpectBool(node, category, relative, "MechanicalNodeSpec", "IsShaft", false);

            if (TryGetInt(node, "PowerInput", out var powerInput))
            {
                powerInputs[_currentFaction] = powerInput;

                if (TryGetObject(root, "HdbfBoxFarmSpec", out var custom)
                    && TryGetInt(custom, "PowerDemand", out var powerDemand)
                    && powerDemand != powerInput)
                {
                    _report.Error(category, $"{relative}: HdbfBoxFarmSpec.PowerDemand ({powerDemand}) must equal "
                                             + $"MechanicalNodeSpec.PowerInput ({powerInput}); gameplay uses the custom "
                                             + "field for the off-grid fallback (REQ-IMPL-21)");
                }
            }
        }

        // 3. Footprint-local connection faces.
        var sizeX = FootprintX;
        var sizeY = FootprintY;   // grid Y = depth
        var sizeZ = FootprintZ;   // grid Z = height
        if (TryGetObject(root, "BlockObjectSpec", out var blockObject))
        {
            if (TryGetObject(blockObject, "Size", out var size))
            {
                sizeX = ExpectInt(size, category, relative, "BlockObjectSpec.Size", "X", FootprintX) ?? sizeX;
                sizeY = ExpectInt(size, category, relative, "BlockObjectSpec.Size", "Y", FootprintY) ?? sizeY;
                sizeZ = ExpectInt(size, category, relative, "BlockObjectSpec.Size", "Z", FootprintZ) ?? sizeZ;
            }
        }

        if (!TryGetObject(root, "TransputProviderSpec", out var provider))
        {
            _report.Error(category, $"{relative}: missing TransputProviderSpec (the mechanical node cannot connect "
                                     + "to a power shaft without at least one transput face)");
            return;
        }

        ExpectBool(provider, category, relative, "TransputProviderSpec", "IgnoreRotation", false);

        if (!provider.TryGetProperty("Transputs", out var transputs)
            || transputs.ValueKind != JsonValueKind.Array
            || transputs.GetArrayLength() == 0)
        {
            _report.Error(category, $"{relative}: TransputProviderSpec.Transputs must be a non-empty array");
            return;
        }

        // v1 contract carried as-is onto the 3-deep model: the socket set spans the full rear row,
        // one transput per rear cell (x = 0..2, y = Size.Y - 1).
        if (transputs.GetArrayLength() != sizeX)
        {
            _report.Error(category, $"{relative}: TransputProviderSpec.Transputs must have exactly {sizeX} "
                                     + "entries for the v1 rear-row socket contract (one per cell on the rear "
                                     + $"depth row; found {transputs.GetArrayLength()})");
        }

        var signature = new List<string>();
        var hasRearFace = false;
        var index = 0;
        foreach (var transput in transputs.EnumerateArray())
        {
            var location = $"TransputProviderSpec.Transputs[{index}]";
            index++;

            if (transput.ValueKind != JsonValueKind.Object)
            {
                _report.Error(category, $"{relative}: {location} must be an object");
                continue;
            }

            if (!TryGetObject(transput, "Coordinates", out var coordinates))
            {
                _report.Error(category, $"{relative}: {location}.Coordinates is missing");
                continue;
            }

            var x = ExpectInt(coordinates, category, relative, location + ".Coordinates", "X") ?? 0;
            var y = ExpectInt(coordinates, category, relative, location + ".Coordinates", "Y") ?? 0;
            var z = ExpectInt(coordinates, category, relative, location + ".Coordinates", "Z") ?? 0;

            if (x < 0 || x >= sizeX || y < 0 || y >= sizeY || z < 0 || z >= sizeZ)
            {
                _report.Error(category, $"{relative}: {location}.Coordinates ({x},{y},{z}) is outside the "
                                         + $"{sizeX}x{sizeY}x{sizeZ} footprint; power geometry must stay on-grid "
                                         + "inside the building's own blocks");
            }
            else if (y != sizeY - 1)
            {
                _report.Error(category, $"{relative}: {location}.Coordinates ({x},{y},{z}) must sit on the rear "
                                         + $"depth row (y = Size.Y - 1 = {sizeY - 1}) so the socket set mirrors the "
                                         + "v1 rear-row contract on the 3-deep model and stays away from the entrance "
                                         + "front and the worker approach");
            }

            var directions = ExpectString(transput, category, relative, location, "Directions");
            ExpectBool(transput, category, relative, location, "ReverseRotation", false);

            if (directions is null)
            {
                continue;
            }

            var tokens = directions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                _report.Error(category, $"{relative}: {location}.Directions must not be empty");
                continue;
            }

            foreach (var token in tokens)
            {
                if (!AllowedTransputDirections.Contains(token, StringComparer.Ordinal))
                {
                    var reason = token switch
                    {
                        "Down" => "points at the front face, where the HDBF entrance and worker access are",
                        "Top" or "Bottom" => "is a vertical face; Top may not offer a roof/below power connection point",
                        _ => "is not a known connection face",
                    };
                    _report.Error(category, $"{relative}: {location}.Directions '{token}' {reason} "
                                             + "(allowed: Up (rear), Left, Right)");
                    continue;
                }

                var (dx, dy, dz) = token switch
                {
                    "Up" => (0, 1, 0),      // +Y = the back face (grid Z is the vertical axis)
                    "Left" => (-1, 0, 0),
                    _ => (1, 0, 0),         // "Right"
                };

                var targetX = x + dx;
                var targetY = y + dy;
                var targetZ = z + dz;
                if (targetX >= 0 && targetX < sizeX && targetY >= 0 && targetY < sizeY
                    && targetZ >= 0 && targetZ < sizeZ)
                {
                    _report.Error(category, $"{relative}: {location} at ({x},{y},{z}) faces '{token}' towards "
                                             + $"({targetX},{targetY},{targetZ}), which is still inside the footprint; "
                                             + "a connection face must point outwards");
                }

                if (token == "Up")
                {
                    hasRearFace = true;
                }
            }

            signature.Add($"{x},{y},{z}:{string.Join("+", tokens.OrderBy(t => t, StringComparer.Ordinal))}");
        }

        if (!hasRearFace)
        {
            _report.Error(category, $"{relative}: Top needs at least one rear ('Up') power connection face; "
                                     + "side-only geometry would hide the on-grid path behind the entrance front");
        }

        transputSignatures[_currentFaction] = string.Join("|", signature.OrderBy(s => s, StringComparer.Ordinal));
    }

    private void AssertEmptyMarkerSpec(JsonElement root, string relative, string category, string specName)
    {
        if (!TryGetObject(root, specName, out _))
        {
            _report.Error(category, $"{relative}: missing {specName} (part of the vanilla mechanical consumer "
                                     + "composition)");
        }
    }

    private static bool TryGetInt(JsonElement parent, string name, out int value)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    // ------------------------------------------------------------------ harvest output inventory (P1 crop loop)

    /// <summary>
    /// Every tier needs a vanilla output inventory, otherwise the harvested goods have nowhere to go
    /// (farm-loop writes real grown yields into SimpleOutputInventory and vanilla hauling moves them
    /// out). The capacity is per tier the whole-box harvest bound
    /// <c>PlotCapacity x OutputCapacityPerSlot</c> (240/600/1200/2400): the farm harvests in daily
    /// batches, so the old fixed vanilla-farmhouse capacity 50 filled up after a few slots and then
    /// blocked every further harvest (2026-10-29 player report: a 600-slot farm transferred only 50
    /// corn). Sizing the buffer to one whole-box harvest keeps hauling as the only rate limit.
    /// IgnorableCapacity stays false and the vanilla field-farm marker specs are forbidden: the HDBF
    /// route uses the worker-side behaviors directly, not the field-farm building model.
    /// </summary>
    private void CheckOutputInventory()
    {
        const string category = "inventory";

        foreach (var expectation in Tiers)
        {
            foreach (var faction in Factions)
            {
                _currentFaction = faction;
                var relative = $"Buildings/Food/BoxFarm/BoxFarm.{expectation.Tier}.{faction}.blueprint.json";
                var path = Path.Combine(_dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    continue;
                }

                using var document = ParseOrReport(path, relative);
                if (document is null)
                {
                    continue;
                }

                var root = document.RootElement;
                if (!ExpectObject(root, category, relative))
                {
                    continue;
                }

                foreach (var forbidden in ForbiddenFarmSpecs)
                {
                    if (root.TryGetProperty(forbidden, out _))
                    {
                        _report.Error(category, $"{relative}: '{forbidden}' must not be added; the HDBF crop loop "
                                                 + "uses worker-side planting/harvest behaviors, not the vanilla "
                                                 + "field-farm building model");
                    }
                }

                if (!TryGetObject(root, "SimpleOutputInventorySpec", out var inventory))
                {
                    _report.Error(category, $"{relative}: missing SimpleOutputInventorySpec at the blueprint root; "
                                             + "harvest output would have no SimpleOutputInventory to write into "
                                             + "(all tiers ship the tier-scaled harvest buffer)");
                    continue;
                }

                var capacity = ExpectInt(inventory, category, relative, "SimpleOutputInventorySpec", "Capacity");
                ExpectBool(inventory, category, relative, "SimpleOutputInventorySpec", "IgnorableCapacity", false);

                if (capacity is null)
                {
                    continue;
                }

                var expectedCapacity = expectation.PlotCapacity * OutputCapacityPerSlot;
                if (capacity != expectedCapacity)
                {
                    _report.Error(category, $"{relative}: SimpleOutputInventorySpec.Capacity must be "
                                             + $"{expectedCapacity} (PlotCapacity {expectation.PlotCapacity} x "
                                             + $"{OutputCapacityPerSlot} max harvest items per slot; found {capacity})");
                }
            }
        }
    }

    // ------------------------------------------------------------------ JSON helpers

    private JsonDocument? ParseOrReport(string path, string relative)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            _report.Error("json", $"{relative}: invalid JSON: {ex.Message}");
            return null;
        }
    }

    private bool ExpectObject(JsonElement element, string category, string relative)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        _report.Error(category, $"{relative}: expected a JSON object");
        return false;
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private string? ExpectString(JsonElement parent, string category, string relative, string location,
        string? name = null, bool allowEmpty = false)
    {
        var propertyName = name ?? location;
        var path = name is null ? location : location + "." + name;

        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            _report.Error(category, $"{relative}: {path} must be a string");
            return null;
        }

        var text = value.GetString() ?? string.Empty;
        if (!allowEmpty && text.Length == 0)
        {
            _report.Error(category, $"{relative}: {path} must not be empty");
        }

        return text;
    }

    private int? ExpectInt(JsonElement parent, string category, string relative, string location, string name,
        int? expected = null)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var actual))
        {
            _report.Error(category, $"{relative}: {location}.{name} must be an integer");
            return null;
        }

        if (expected is not null && actual != expected.Value)
        {
            _report.Error(category, $"{relative}: {location}.{name} must be {expected.Value} (found {actual})");
        }

        return actual;
    }

    private void ExpectFloat(JsonElement parent, string category, string relative, string location, string name,
        double expected)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            _report.Error(category, $"{relative}: {location}.{name} must be a number");
            return;
        }

        var actual = value.GetDouble();
        if (Math.Abs(actual - expected) > 1e-6)
        {
            _report.Error(category, $"{relative}: {location}.{name} must be "
                                     + $"{expected.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)} "
                                     + $"(found {actual.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)})");
        }
    }

    private void ExpectBool(JsonElement parent, string category, string relative, string location, string name,
        bool expected)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            _report.Error(category, $"{relative}: {location}.{name} must be a boolean");
            return;
        }

        if (value.GetBoolean() != expected)
        {
            _report.Error(category, $"{relative}: {location}.{name} must be "
                                     + $"{expected.ToString().ToLowerInvariant()} (found {value.GetBoolean().ToString().ToLowerInvariant()})");
        }
    }

    private void ExpectStringArray(JsonElement parent, string category, string relative, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            _report.Error(category, $"{relative}: {name} must be an array");
        }
    }

    private sealed record TierExpectation(
        string Tier,
        (string Id, int Amount)[] Cost,
        int ScienceCost,
        int MaxWorkers,
        int PlotCapacity,
        int PlotsPerFarmer,
        int WorkRangeX,
        int WorkRangeZ,
        double MinIrrigationCoverage,
        double WaterPerPlotPerDay,
        int TankCapacityDays,
        double SoilFatigueFactor,
        int MaintenanceKind,
        double MaintenanceCycleDays,
        int MaintenanceMaterialAmount,
        bool RequiresPower,
        int PowerDemand,
        bool HasPipePort,
        bool HasLogisticsPort,
        bool HasPowerPort,
        int ToolOrder,
        double BuildWorkdays)
    {
        /// <summary>
        /// REQ-V2-BLD-01 build time in game hours. The blueprint field
        /// <c>ConstructionSiteBuildTimeSpec.ConstructionTimeInHours</c> is expressed in hours, so the
        /// documented workday value is converted with 24 hours/workday (see CheckBuildTime).
        /// </summary>
        public double BuildTimeInHours => BuildWorkdays * BuildHoursPerWorkday;

        /// <summary>
        /// ART-03: the custom finished model this tier/faction ships, data-relative and without the
        /// `.timbermesh` extension (the game resolves the extension itself).
        /// </summary>
        public string FinishedModelFor(string faction) =>
            $"{CustomModelDirectory}/BoxFarm.{Tier}.{faction}.Model";

        /// <summary>
        /// ART-03: the custom ConstructionStage0 model for the unfinished prefab, same path scheme.
        /// </summary>
        public string ConstructionModelFor(string faction) =>
            $"{CustomModelDirectory}/BoxFarm.{Tier}.{faction}.ConstructionStage0.Model";

        public List<(string Id, int Amount)> Costs() => Cost.ToList();
    }
}
