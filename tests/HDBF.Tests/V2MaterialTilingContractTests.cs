// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: ART-07 (test-dev)
// Purpose: data contract of the ART-07 world-scale UV mapping:
//          * art/material_texture_map.json carries the frozen §4 additions
//            (mode / tile_meters / texture_size / wrap / uv_canvas / projection /
//            px_per_m / coverage_u_m / coverage_v_m / canvas_meters + the
//            top-level "uv_mapping" policy block) and every tile material stays
//            inside the REQ texel-density band (300-600 px/m);
//          * every shipped BoxFarm .timbermesh decodes as a real zlib+protobuf
//            model with finite data, its UVs inside the material canvas
//            (tile: 2-texel guard, patch: curated canvas rect) and a measured
//            world-projected texel density within ±3 % of the map px_per_m;
//          * art/material_slots.json records the per-model world_tiled mapping
//            and no longer declares the retired 32 px/m patch density as the
//            shipped density;
//          * in-memory negative controls prove those validators have teeth
//            (half/double UV scale, a 0..1 stretched patch face, a dropped map
//            material, bad policy records).
// Requirements: art/ART07_UV_TILING.md §3 (policy table), §4 (map schema),
//               §5 (density semantics + negative controls); ART-07 task-2.
//
// This layer is deliberately offline and dependency-free (JSON + zlib +
// protobuf byte reading only): the repository root is baked into the test
// assembly as AssemblyMetadata("HdbfRepoRoot", ...) because the build output
// lives outside the repository (see HDBF.Tests.csproj). The .timbermesh reader
// is a local copy of the minimal wire-format reader used by
// ArtModelContractTests / V2MaterialPatchContractTests so the ART-07 card stays
// independent of the other contract cards.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace HDBF.Tests
{
    public static class V2MaterialTilingContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string MapRelativePath = "art/material_texture_map.json";
        private const string SlotsRelativePath = "art/material_slots.json";
        private const string BoxFarmDataFolder = "Buildings/Food/BoxFarm";
        private const string ArtDocRelativePath = "data/ART_PLACEHOLDER.md";
        private const string LocalizationFolderRelativePath = "data/Localizations";

        private const string MapSchema = "hdbf.material_texture_map/v1";
        private const string UvMappingSchema = "hdbf.world_uv/v1";

        private const int ExpectedShippedModelCount = 16;

        /// <summary>2 texel guard on every canvas edge (ART07 §2/§4: usused canvas
        /// margin so the game's no-gutter auto-atlas never bleeds a neighbour material).</summary>
        private const int UvGuardTexels = 2;

        /// <summary>Aggregate per-material density tolerance from ART07 §5 C15 (±2 %) with
        /// the task-2 allowance (±3 %).</summary>
        private const double AggregateDensityTolerance = 0.03;

        /// <summary>Per-triangle density tolerance from ART07 §5 C15 for triangles with
        /// world area >= 0.05 m².</summary>
        private const double PerTriangleDensityTolerance = 0.25;
        private const double MinLargeTriangleWorldAreaM2 = 0.05;

        /// <summary>Triangles whose normal has two near-equal dominant |components| are
        /// "ambiguous plane" (45° bevel/chamfer faces) and are skipped by the per-triangle checks
        /// (ART07 §5 C15 frozen tie-break: rounded unit |components| top-two within 1e-3). They
        /// must stay below 15 % of triangles and 2 % of world area (measured on the shipped meshes:
        /// 8.1 % of triangles / 0.08 % of area).</summary>
        private const double AmbiguousNormalEpsilon = 1e-3;
        private const double MaxAmbiguousTriangleFraction = 0.15;
        private const double MaxAmbiguousAreaFraction = 0.02;

        /// <summary>REQ texel-density target band for tile materials (ART07 §3/§5).</summary>
        private const double TileDensityBandMin = 300.0;
        private const double TileDensityBandMax = 600.0;

        /// <summary>Regression floor for patch materials: the retired ART-06 shipped density
        /// was 32 px/m, so every patch canvas must stay clearly above double that.</summary>
        private const double PatchDensityRegressionFloor = 64.0;

        /// <summary>Tolerance when pinning UV rects at the 9-decimal JSON precision.</summary>
        private const double UvRectTolerance = 1e-6;

        /// <summary>Tolerance for canvas containment: the generator keeps a 2-texel guard and
        /// floors the density down, so a correct mesh has a visible margin; float32 rounding
        /// of the shipped UVs is far below this.</summary>
        private const double CanvasContainmentTolerance = 1e-5;

        /// <summary>The retired ART-06 shipped density (texels per 1 m grid). It must not be
        /// declared as the shipped density any more, only as legacy reference.</summary>
        private const int RetiredPatchTexelsPerGrid = 32;

        private static readonly string[] Tiers = { "Basic", "Intermediate", "Advanced", "Top" };
        private static readonly string[] Factions = { "Folktails", "IronTeeth" };
        private static readonly string[] Languages = { "enUS", "zhCN", "zhTW" };

        private const string ConstructionStageModelKey = "ConstructionStage0";
        private const string ConstructionStemMarker = ".ConstructionStage0.";

        /// <summary>ART-04 port marker empties; ART-07 changes UVs only, so the markers stay.</summary>
        private static readonly string[] RequiredPortMarkers =
        {
            "#Port#Power", "#Port#Pipe", "#Port#Logistics",
        };

        private const string ForbiddenSlotMarker = "#Slot#";

        /// <summary>Yellow/black hazard stripe texture family: banned on large faces (ART-05/06
        /// large_shell_policy) and still banned after the ART-07 UV change.</summary>
        private static readonly string[] ForbiddenLargeShellMaterials =
        {
            "PaintedMetal.Folktails", "PaintedMetal.IronTeeth",
        };

        /// <summary>
        /// Frozen ART07 §3 policy table: the mapping mode of every shipped vanilla material.
        /// A regression that flips a full-bleed tile texture to a low-density patch (or the
        /// other way round) must fail L1; the per-mode checks below then verify the rest.
        /// </summary>
        private static readonly Dictionary<string, string> ExpectedModeByMaterial = new(StringComparer.Ordinal)
        {
            ["BaseWood_Brown.Folktails"] = "patch",
            ["BaseWood_DarkBrown.IronTeeth"] = "patch",
            ["BaseWood_Grey.IronTeeth"] = "patch",
            ["BaseWood_LightBrown.Folktails"] = "patch",
            ["BaseWood_White.Folktails"] = "patch",
            ["BaseMetal.Folktails"] = "patch",
            ["BaseMetal.IronTeeth"] = "patch",
            ["Details.Folktails"] = "patch",
            ["Details.IronTeeth"] = "patch",
            ["WindowsAtlas.Folktails"] = "patch",
            ["WindowsAtlas.IronTeeth"] = "patch",
            ["Plaster_White.Folktails"] = "tile",
            ["Plaster_Orange.IronTeeth"] = "tile",
            ["Moss.Folktails"] = "tile",
            ["RoofPlanks.IronTeeth"] = "tile",
            ["IrregularPlanks_Brown.Folktails"] = "tile",
            ["IrregularPlanks_MetalForged.IronTeeth"] = "tile",
        };

        /// <summary>
        /// REQ chapter class -> allowed nominal tile_meters band (ART07 §3 "tile_meters (class)"
        /// column; mirrored from art/material_uv_policy.py TILE_METERS_BAND).
        /// </summary>
        private static readonly Dictionary<string, (double Min, double Max)> TileMetersBandByPatchClass =
            new(StringComparer.Ordinal)
            {
                ["wood"] = (2.2, 2.6),
                ["planks"] = (2.2, 2.6),
                ["forged_planks"] = (2.2, 2.6),
                ["plaster"] = (2.4, 2.8),
                ["metal"] = (1.8, 2.2),
                ["roof_planks"] = (2.0, 2.4),
                ["moss"] = (2.0, 2.4),
            };

        /// <summary>Frozen patch-class vocabulary of art/material_texture_map.json.</summary>
        private static readonly string[] KnownPatchClasses =
        {
            "wood", "metal", "glass", "plaster", "details", "moss", "roof_planks", "forged_planks",
        };

        /// <summary>ART07 §4 frozen dominant-axis projection table (Blender author space).</summary>
        private static readonly Dictionary<string, string[]> ExpectedProjectionAxes = new(StringComparer.Ordinal)
        {
            ["-X"] = new[] { "y", "z" },
            ["+X"] = new[] { "y", "z" },
            ["-Y"] = new[] { "x", "z" },
            ["+Y"] = new[] { "x", "z" },
            ["-Z"] = new[] { "x", "y" },
            ["+Z"] = new[] { "x", "y" },
        };

        /// <summary>ART07 §2 frozen projection centre in Blender author space.</summary>
        private static readonly double[] ExpectedWorldCenter = { 0.0, 0.0, 0.5 };

        private static readonly Lazy<TilingMap> LazyMap = new(LoadTilingMap);
        private static readonly Lazy<SlotsContract> LazySlots = new(LoadSlotsContract);
        private static readonly Lazy<Dictionary<string, ShippedModel>> LazyModels = new(LoadShippedModels);

        private static TilingMap Map => LazyMap.Value;
        private static SlotsContract Slots => LazySlots.Value;
        private static Dictionary<string, ShippedModel> Models => LazyModels.Value;

        // ==================================================================
        // 1. material_texture_map.json :: ART07 §4 policy schema
        // ==================================================================

        /// <summary>
        /// art/material_texture_map.json must carry the ART07 §4 additions for every used
        /// material and the top-level "uv_mapping" block. Checked per material:
        ///  * mode is the frozen §3 mode and wrap is "repeat" (tile) / "clamp" (patch);
        ///  * texture_size is the frozen 1024² class and the uv_canvas pixel/UV rects are a
        ///    non-empty rect inside it, translated with the frozen flipped-v convention;
        ///  * projection is the frozen axes/world-centre table with the per-material uv_center;
        ///  * px_per_m reproduces from (canvas_size - 2*guard) / coverage and is inside the
        ///    REQ bands (tile 300-600 px/m, patch clearly above the retired 32 px/m);
        ///  * coverage_u_m/coverage_v_m are positive and the full canvas covers them;
        ///  * the legacy per-material "patch" record is kept (canvas itself for patch mode,
        ///    small uniform probe window for tile mode) so the ART-05/06 C12/C13 checks keep
        ///    working unchanged;
        ///  * the top-level "uv_mapping" block declares schema hdbf.world_uv/v1,
        ///    atlas_cell_safe / no_uv_outside_canvas true and the 300-600 target band.
        /// </summary>
        public static void TestMaterialTextureMapDeclaresTheWorldUvPolicy()
        {
            var map = Map;
            const string context = MapRelativePath;

            TestAssert.Equal(MapSchema, StringMember(map.Root, "schema", context),
                context + " schema id (the ART-07 additions extend the same /v1 schema)");

            // ---- top-level uv_mapping policy block (ART07 §4) ----
            var uvMapping = map.UvMapping;
            var policyContext = context + " uv_mapping";
            TestAssert.Equal(UvMappingSchema, StringMember(uvMapping, "schema", policyContext),
                policyContext + " schema");
            TestAssert.True(StringMember(uvMapping, "generator", policyContext)
                    .Contains("material_uv_policy", StringComparison.Ordinal),
                policyContext + ".generator must name art/material_uv_policy.py (the single source of "
                + "truth for the ART-07 mapping)");
            TestAssert.True(BoolMember(uvMapping, "atlas_cell_safe", policyContext),
                policyContext + ".atlas_cell_safe must be true: the game's auto-atlas remaps every UV "
                + "into the material's own cell, so no shipped UV may leave the canvas");
            TestAssert.True(BoolMember(uvMapping, "no_uv_outside_canvas", policyContext),
                policyContext + ".no_uv_outside_canvas must be true");

            var targetBand = DoubleArray(Member(uvMapping, "texel_density_target_px_per_m", policyContext),
                2, policyContext + " texel_density_target_px_per_m");
            TestAssert.Equal(TileDensityBandMin, targetBand[0], 1e-9,
                policyContext + " texel_density_target_px_per_m[0] must be the REQ tile band floor");
            TestAssert.Equal(TileDensityBandMax, targetBand[1], 1e-9,
                policyContext + " texel_density_target_px_per_m[1] must be the REQ tile band ceiling");

            if (uvMapping.TryGetProperty("uv_guard_texels", out var guardElement))
            {
                TestAssert.Equal(UvGuardTexels, IntValue(guardElement, policyContext + " uv_guard_texels"),
                    policyContext + " uv_guard_texels must be the frozen 2-texel guard");
            }

            if (uvMapping.TryGetProperty("contract", out var contractElement))
            {
                TestAssert.True(contractElement.ValueKind == JsonValueKind.String
                        && contractElement.GetString()!.Contains("ART07", StringComparison.Ordinal),
                    policyContext + ".contract must point at the frozen ART07 contract document");
            }

            // ---- per-material §4 records ----
            var densities = Member(uvMapping, "densities", policyContext);
            var densityEntries = ObjectEntries(densities, policyContext + " densities").ToList();
            TestAssert.Equal(map.Materials.Count, densityEntries.Count,
                policyContext + ".densities must carry one entry per used material");

            var tileCount = 0;
            var patchCount = 0;
            var minDensity = double.PositiveInfinity;
            var maxDensity = double.NegativeInfinity;
            foreach (var pair in map.Materials.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var material = pair.Value;
                var entryContext = context + " materials[\"" + material.Name + "\"]";

                TestAssert.True(ExpectedModeByMaterial.ContainsKey(material.Name),
                    entryContext + ": material is not part of the frozen ART07 §3 policy table");
                TestAssert.Equal(ExpectedModeByMaterial[material.Name], material.Mode,
                    entryContext + ": mode must be the frozen ART07 §3 mode (a tile texture turned "
                    + "into a low-density patch would lose the round's resolution gain)");
                TestAssert.True(TryValidateTilingMaterial(material, map.HardStdCap, out var error),
                    entryContext + ": " + error);

                TestAssert.Equal(1024, material.Width, entryContext + ": texture_size width must be 1024");
                TestAssert.Equal(1024, material.Height, entryContext + ": texture_size height must be 1024");

                if (material.IsTile)
                {
                    tileCount++;
                    TestAssert.NotNull(material.TileMeters, entryContext + ": a tile material must declare tile_meters");
                    var band = TileMetersBandByPatchClass[material.PatchClass];
                    TestAssert.True(material.TileMeters!.Value >= band.Min - 1e-9
                            && material.TileMeters.Value <= band.Max + 1e-9,
                        entryContext + ": tile_meters " + material.TileMeters + " is outside the REQ class band ["
                        + band.Min + ", " + band.Max + "] of patch class \"" + material.PatchClass + "\"");
                    if (material.HasTileMetersBand)
                    {
                        TestAssert.Equal(band.Min, material.TileMetersBand[0], 1e-9,
                            entryContext + ": tile_meters_band[0] must match the REQ class band floor");
                        TestAssert.Equal(band.Max, material.TileMetersBand[1], 1e-9,
                            entryContext + ": tile_meters_band[1] must match the REQ class band ceiling");
                    }

                    TestAssert.True(material.PxPerM >= TileDensityBandMin && material.PxPerM <= TileDensityBandMax,
                        entryContext + ": tile px_per_m " + material.PxPerM
                        + " must be inside the REQ target band [" + TileDensityBandMin + ", "
                        + TileDensityBandMax + "]: 1024/3.0 = 341 px/m is the point of ART-07");
                }
                else
                {
                    patchCount++;
                    TestAssert.Null(material.TileMeters,
                        entryContext + ": a patch material must record tile_meters: null");
                    TestAssert.True(material.PxPerM > PatchDensityRegressionFloor,
                        entryContext + ": patch px_per_m " + material.PxPerM
                        + " collapsed back towards the retired ART-06 32 px/m density");

                    // The legacy probe record must now BE the curated canvas (ART07 §4).
                    TestAssert.True(material.HasPatchRecord,
                        entryContext + ": patch materials must keep the existing \"patch\" record "
                        + "(the canvas itself) so C12/C13 keep working");
                }

                minDensity = Math.Min(minDensity, material.PxPerM);
                maxDensity = Math.Max(maxDensity, material.PxPerM);

                // ---- uv_mapping.densities entry (ART07 §4 example) ----
                var densityElement = Member(densities, material.Name, policyContext + " densities");
                var densityContext = policyContext + " densities[\"" + material.Name + "\"]";
                TestAssert.Equal(material.Mode, StringMember(densityElement, "mode", densityContext),
                    densityContext + ".mode");
                TestAssert.Equal(material.PxPerM,
                    NumberMember(densityElement, "px_per_m", densityContext), 1e-9,
                    densityContext + ".px_per_m must equal the material record");
                if (densityElement.TryGetProperty("canvas_pixels", out var canvasPixelsElement))
                {
                    var canvasPixels = IntArray(canvasPixelsElement, 2, densityContext + " canvas_pixels");
                    TestAssert.Equal(material.CanvasSizePx[0], canvasPixels[0], densityContext + " canvas_pixels width");
                    TestAssert.Equal(material.CanvasSizePx[1], canvasPixels[1], densityContext + " canvas_pixels height");
                }

                if (densityElement.TryGetProperty("canvas_meters", out var canvasMetersElement))
                {
                    var canvasMeters = DoubleArray(canvasMetersElement, 2, densityContext + " canvas_meters");
                    TestAssert.Equal(material.CanvasMeters[0], canvasMeters[0], 1e-6,
                        densityContext + " canvas_meters[0]");
                    TestAssert.Equal(material.CanvasMeters[1], canvasMeters[1], 1e-6,
                        densityContext + " canvas_meters[1]");
                }
            }

            TestAssert.True(tileCount >= 5,
                "expected at least 5 full-bleed tile materials in the frozen policy table, found " + tileCount);
            TestAssert.True(patchCount >= 10,
                "expected at least 10 curated patch materials in the frozen policy table, found " + patchCount);

            Console.WriteLine("  ART-07 map: " + map.Materials.Count + " materials (" + tileCount
                + " tile + " + patchCount + " patch), px_per_m " + minDensity + ".." + maxDensity
                + ", uv_mapping schema " + UvMappingSchema);
        }

        // ==================================================================
        // 2. shipped .timbermesh container / node contract
        // ==================================================================

        /// <summary>
        /// data/Buildings/Food/BoxFarm must still ship exactly the 16 frozen models (8 tiers ×
        /// 2 factions × {finished, ConstructionStage0}), each a decodable zlib timbermesh with the
        /// frozen container: Model.name == file stem, 4 nodes (1 body + exactly the 3 #Port#
        /// empties), no #Slot# node, all vertex data finite, every mesh material present in the
        /// ART-07 map, no PaintedMetal hazard material, and every map material actually used by
        /// at least one shipped file. ART-07 changes the UV values only, so the ART-02/03 container
        /// contract must be untouched.
        /// </summary>
        public static void TestShippedModelsAreTheExpectedSixteenTimbermeshes()
        {
            var models = Models;
            var dataFolder = ShippedDataFolder();
            var actualFiles = Directory.GetFiles(dataFolder, "*.timbermesh")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var expectedFiles = ExpectedShippedStems().OrderBy(name => name, StringComparer.Ordinal).ToList();
            TestAssert.Equal(string.Join(", ", expectedFiles), string.Join(", ", actualFiles),
                "data/Buildings/Food/BoxFarm must ship exactly the " + ExpectedShippedModelCount
                + " BoxFarm .timbermesh models");
            TestAssert.Equal(ExpectedShippedModelCount, models.Count,
                "expected all " + ExpectedShippedModelCount + " shipped models to be readable");

            var usedMaterials = new SortedSet<string>(StringComparer.Ordinal);
            var forbidden = new HashSet<string>(ForbiddenLargeShellMaterials, StringComparer.Ordinal);
            foreach (var stem in ExpectedShippedStems())
            {
                var model = models[stem];
                var label = stem + ".timbermesh";

                TestAssert.Equal(stem, model.DecodedName,
                    label + ": decoded Model.name must equal the file stem (wrong asset copied)");
                TestAssert.Equal(4, model.Nodes.Count,
                    label + ": v2 model must have exactly 4 nodes (1 body + 3 #Port#; ART-07 changes UVs only)");

                var ports = model.Nodes
                    .Where(node => node.Name.StartsWith("#Port#", StringComparison.Ordinal))
                    .ToList();
                TestAssert.Equal(RequiredPortMarkers.Length, ports.Count,
                    label + ": decoded model must carry exactly the three #Port# empties");
                foreach (var marker in RequiredPortMarkers)
                {
                    TestAssert.True(ports.Any(port => string.Equals(port.Name, marker, StringComparison.Ordinal)),
                        label + ": missing port marker node \"" + marker + "\"");
                }

                TestAssert.Equal(0, CountOccurrences(model.PayloadText, ForbiddenSlotMarker),
                    label + ": the payload still contains \"" + ForbiddenSlotMarker
                    + "\" marker node(s); the v2 roof is one empty flat platform with no crop slots");
                foreach (var marker in RequiredPortMarkers)
                {
                    TestAssert.Equal(1, CountOccurrences(model.PayloadText, marker),
                        label + ": the payload must contain the port marker node \"" + marker
                        + "\" exactly once");
                }

                TestAssert.False(model.PayloadText.Contains("PaintedMetal", StringComparison.Ordinal),
                    label + ": the payload names a PaintedMetal hazard material (yellow/black stripe regression)");

                var body = model.Nodes.FirstOrDefault(node =>
                    string.Equals(node.Name, stem, StringComparison.Ordinal));
                TestAssert.NotNull(body, label + ": no root body node named \"" + stem + "\"");
                TestAssert.True(body!.VertexCount > 0, label + ": the body node carries no vertices");

                TestAssert.True(model.Materials.Count >= 1 && model.Materials.Count <= 4,
                    label + ": shipped model uses " + model.Materials.Count
                    + " distinct materials, expected 1..4");

                foreach (var node in model.Nodes)
                {
                    TestAssert.True(node.Positions.Count == node.Uvs.Count || node.Uvs.Count == 0,
                        label + ": node \"" + node.Name + "\" has " + node.Positions.Count
                        + " positions but " + node.Uvs.Count + " uv0 entries");
                    foreach (var (u, v) in node.Uvs)
                    {
                        TestAssert.True(double.IsFinite(u) && double.IsFinite(v),
                            label + ": node \"" + node.Name + "\" has a non-finite uv0 value");
                    }
                }

                foreach (var part in model.Nodes.SelectMany(node => node.Parts))
                {
                    TestAssert.True(part.Material.Length > 0,
                        label + ": a mesh part carries no material name (cannot be lit in game)");
                    TestAssert.False(forbidden.Contains(part.Material),
                        label + ": uses the forbidden large-shell hazard material \"" + part.Material
                        + "\" (PaintedMetal carries the yellow/black stripe texture)");
                    TestAssert.True(Map.Materials.ContainsKey(part.Material),
                        label + ": mesh material \"" + part.Material
                        + "\" has no entry in art/material_texture_map.json");
                    usedMaterials.Add(part.Material);
                }
            }

            // Ground truth in both directions: no un-mapped shipped material, no unused map entry.
            TestAssert.True(TryValidateMaterialCoverage(Map, models, out var coverageError),
                coverageError);

            Console.WriteLine("  shipped models: " + models.Count + " files, " + usedMaterials.Count
                + " distinct materials, all inside art/material_texture_map.json");
        }

        // ==================================================================
        // 3. shipped mesh UV containment (ART07 §1/§2/§5 C7/C11)
        // ==================================================================

        /// <summary>
        /// The hard in-game constraint of ART07 §1: the runtime auto-atlas remaps every mesh UV
        /// into the material's own atlas cell, so a UV component &gt; 1 (or &lt; 0) samples another
        /// material. Therefore every uv0 value of every shipped mesh must stay inside the
        /// material canvas from art/material_texture_map.json:
        ///  * tile material  -> [2/1024, 1 - 2/1024] (2-texel atlas guard on the whole texture);
        ///  * patch material -> the curated uv_canvas.uv_rect.
        /// Also requires every mesh to carry uv0 (a model without UVs cannot sample its material)
        /// and enough indexed UVs to prove the check ran over the real geometry.
        /// </summary>
        public static void TestShippedMeshUvsStayInsideTheirMaterialCanvas()
        {
            var map = Map;
            var totalUvs = 0;
            var totalMeshes = 0;
            foreach (var stem in ExpectedShippedStems())
            {
                var model = Models[stem];
                TestAssert.True(TryValidateUvContainment(map, model, out var checkedUvs, out var checkedMeshes,
                        out var error),
                    stem + ".timbermesh: " + error);
                totalUvs += checkedUvs;
                totalMeshes += checkedMeshes;
            }

            TestAssert.True(totalMeshes >= ExpectedShippedModelCount,
                "only " + totalMeshes + " material meshes were found across the shipped models; the UV "
                + "containment guard did not run over the real geometry");
            TestAssert.True(totalUvs >= 10000,
                "only " + totalUvs + " indexed UVs were checked across the shipped models; the UV "
                + "containment guard did not run over the real geometry");

            Console.WriteLine("  UV containment: " + totalUvs + " indexed uv0 values inside the material "
                + "canvas across " + totalMeshes + " material meshes (guard " + UvGuardTexels + " texels)");
        }

        // ==================================================================
        // 4. world-projected texel density (ART07 §2 formula, §5 C15)
        // ==================================================================

        /// <summary>
        /// The point of ART-07: the shipped UVs must be a uniform world-scale planar projection at
        /// the map's px_per_m. For every triangle of every shipped mesh the UV area is compared to
        /// the world area projected onto the dominant-axis plane; per material the aggregate
        /// density must be within ±2 % of art/material_texture_map.json px_per_m (the ART-06
        /// 32 px/m patch system fails this immediately). Tile materials must additionally land
        /// inside the REQ 300-600 px/m band, and every large triangle (world area &gt;= 0.05 m²)
        /// must be within ±25 %. Triangles whose normal has two near-equal dominant components
        /// (45° bevels) are ambiguous: they are covered by the aggregate density but must stay
        /// below 15 % of triangles / 2 % of world area (ART07 §5 C15).
        ///
        /// The ConstructionStage0 geometry is baked ONCE from the Folktails construction set and
        /// the IronTeeth file reuses that UV set with its own materials (ART07 §3), so its expected
        /// density is the Folktails source material's density (e.g. RoofPlanks.IronTeeth on the IT
        /// construction stage carries the BaseWood_Brown 145.3 px/m mapping). The expected map is
        /// derived from art/material_slots.json construction_faction_materials, not hard-coded.
        /// </summary>
        public static void TestShippedMeshUvDensityMatchesTheMaterialPolicy()
        {
            var map = Map;
            var slots = Slots;
            var totalTriangles = 0;
            var totalAmbiguous = 0;
            var totalDegenerate = 0;
            var totalLargeTriangles = 0;
            var totalWorldArea = 0.0;
            var totalAmbiguousArea = 0.0;
            var minMeasured = double.PositiveInfinity;
            var maxMeasured = double.NegativeInfinity;
            var worstRelativeError = 0.0;
            var worstMaterial = string.Empty;
            var measuredByMaterial = new SortedDictionary<string, (double Min, double Max)>(StringComparer.Ordinal);

            foreach (var stem in ExpectedShippedStems())
            {
                TestAssert.True(TryValidateDensities(map, slots, Models[stem], out var report, out var error),
                    stem + ".timbermesh: " + error);

                totalTriangles += report.Triangles;
                totalAmbiguous += report.AmbiguousTriangles;
                totalDegenerate += report.DegenerateTriangles;
                totalLargeTriangles += report.LargeTriangles;
                totalWorldArea += report.TotalWorldAreaM2;
                totalAmbiguousArea += report.AmbiguousWorldAreaM2;
                foreach (var pair in report.Materials)
                {
                    var density = pair.Value;
                    minMeasured = Math.Min(minMeasured, density.MeasuredPxPerM);
                    maxMeasured = Math.Max(maxMeasured, density.MeasuredPxPerM);
                    var relativeError = Math.Abs(density.MeasuredPxPerM - density.ExpectedPxPerM)
                        / density.ExpectedPxPerM;
                    if (relativeError > worstRelativeError)
                    {
                        worstRelativeError = relativeError;
                        worstMaterial = pair.Key;
                    }

                    if (measuredByMaterial.TryGetValue(pair.Key, out var range))
                    {
                        measuredByMaterial[pair.Key] = (Math.Min(range.Min, density.MeasuredPxPerM),
                            Math.Max(range.Max, density.MeasuredPxPerM));
                    }
                    else
                    {
                        measuredByMaterial[pair.Key] = (density.MeasuredPxPerM, density.MeasuredPxPerM);
                    }
                }
            }

            TestAssert.True(totalTriangles >= 3000,
                "only " + totalTriangles + " triangles were analysed across the 16 shipped "
                + "models; the world-UV density guard did not run over the real geometry");

            var ambiguousAreaFraction = totalWorldArea > 0.0 ? totalAmbiguousArea / totalWorldArea : 1.0;
            TestAssert.True(totalTriangles > 0
                    && totalAmbiguous <= MaxAmbiguousTriangleFraction * totalTriangles,
                "ambiguous-plane triangles (45° bevels: two near-equal dominant normal components) are "
                + totalAmbiguous + "/" + totalTriangles + " of the shipped triangles, over the "
                + (MaxAmbiguousTriangleFraction * 100.0) + " % budget (ART07 §5 C15)");
            TestAssert.True(ambiguousAreaFraction <= MaxAmbiguousAreaFraction,
                "ambiguous-plane triangles cover " + (ambiguousAreaFraction * 100.0).ToString("F3")
                + " % of the world area, over the " + (MaxAmbiguousAreaFraction * 100.0)
                + " % budget (ART07 §5 C15)");

            TestAssert.True(totalLargeTriangles > 0,
                "no large triangle (world area >= " + MinLargeTriangleWorldAreaM2
                + " m²) was found; the per-triangle density guard did not run");
            TestAssert.True(totalDegenerate <= totalTriangles * 0.01,
                totalDegenerate + " degenerate (zero-area) triangles were found in the shipped models");

            Console.WriteLine("  world-UV density: " + totalTriangles + " triangles over "
                + measuredByMaterial.Count + " materials, measured " + minMeasured + ".." + maxMeasured
                + " px/m, worst aggregate error " + (worstRelativeError * 100.0).ToString("F2")
                + " % (" + worstMaterial + "), ambiguous " + totalAmbiguous + "/" + totalTriangles
                + " (" + (100.0 * totalAmbiguous / Math.Max(1, totalTriangles)).ToString("F1")
                + " % / " + (ambiguousAreaFraction * 100.0).ToString("F2") + " % area), large "
                + totalLargeTriangles);
        }

        // ==================================================================
        // 5. art/material_slots.json per-model world_tiled mapping (ART07 §4)
        // ==================================================================

        /// <summary>
        /// art/material_slots.json must describe the shipped mapping instead of the retired flat
        /// patch density: the top-level uv_space block keeps the old 32 texels/grid only as
        /// "legacy_patch_texels_per_grid" and no longer declares it as the shipped density; every
        /// model LOD carries a "world_tiled" uv_mapping block whose per-slot material agrees with
        /// art/material_texture_map.json (mode / px_per_m / canvas rects), so the same material
        /// uses the SAME density and canvas in every model (no per-LOD / per-tier drift).
        /// The ConstructionStage0 block lists the Folktails source materials (one baked UV set).
        /// </summary>
        public static void TestMaterialSlotsRecordThePerModelWorldUvMapping()
        {
            var slots = Slots;
            var map = Map;
            const string context = SlotsRelativePath;

            var uvSpace = Member(slots.Root, "uv_space", context);
            TestAssert.False(uvSpace.TryGetProperty("patch_texels_per_grid", out _),
                context + " uv_space.patch_texels_per_grid must be retired: the shipped density is the "
                + "ART-07 world-scale projection, not the ART-06 32 px/grid patch");
            TestAssert.Equal(RetiredPatchTexelsPerGrid,
                NumberMember(uvSpace, "legacy_patch_texels_per_grid", context + " uv_space"), 1e-9,
                context + " uv_space.legacy_patch_texels_per_grid must keep the retired ART-06 density "
                + "as documented legacy reference");
            var uvSpaceModel = StringMember(uvSpace, "model", context + " uv_space");
            TestAssert.True(uvSpaceModel.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0,
                context + " uv_space.model must describe the ART-07 world-scale projection, got \""
                + uvSpaceModel + "\"");

            // Top-level uv_mapping summary (mirrors the map's block): positive policy flags.
            if (slots.Root.TryGetProperty("uv_mapping", out var slotsRootUvMapping)
                && slotsRootUvMapping.ValueKind == JsonValueKind.Object)
            {
                TestAssert.True(BoolMember(slotsRootUvMapping, "atlas_cell_safe", context + " uv_mapping"),
                    context + " uv_mapping.atlas_cell_safe must be true");
                if (slotsRootUvMapping.TryGetProperty("densities", out var slotsDensities)
                    && slotsDensities.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in map.Materials.OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        TestAssert.True(slotsDensities.TryGetProperty(pair.Key, out var densityElement),
                            context + " uv_mapping.densities is missing \"" + pair.Key + "\"");
                        TestAssert.Equal(pair.Value.PxPerM,
                            NumberMember(densityElement, "px_per_m",
                                context + " uv_mapping.densities[\"" + pair.Key + "\"]"), 1e-9,
                            context + " uv_mapping.densities[\"" + pair.Key + "\"].px_per_m must match the map");
                    }
                }
            }

            var models = Member(slots.Root, "models", context);
            var checkedLods = 0;
            foreach (var modelKey in ModelGroupKeys())
            {
                var groupContext = context + " models[\"" + modelKey + "\"]";
                var group = Member(models, modelKey, context + " models");
                var lodEntries = ObjectEntries(group, groupContext)
                    .Where(property => property.Name.StartsWith("LOD", StringComparison.Ordinal))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToList();
                TestAssert.True(lodEntries.Count >= 1,
                    groupContext + ": every model group must record at least the LOD0 mapping block");

                foreach (var lodEntry in lodEntries)
                {
                    var lodContext = groupContext + " " + lodEntry.Name;
                    var uvMapping = Member(lodEntry.Value, "uv_mapping", lodContext);
                    TestAssert.Equal("world_tiled", StringMember(uvMapping, "model", lodContext + " uv_mapping"),
                        lodContext + " uv_mapping.model must be \"world_tiled\"");
                    var materialEntries = ObjectEntries(
                        Member(uvMapping, "materials", lodContext + " uv_mapping"),
                        lodContext + " uv_mapping.materials").ToList();
                    TestAssert.True(materialEntries.Count >= 1,
                        lodContext + " uv_mapping.materials must map the model's material slots");
                    TestAssert.True(materialEntries.Count <= 4,
                        lodContext + " uv_mapping.materials maps " + materialEntries.Count
                        + " slots; a shipped model carries at most 4 material slots");

                    foreach (var materialEntry in materialEntries)
                    {
                        var slotContext = lodContext + " uv_mapping.materials[\""
                            + materialEntry.Name + "\"]";
                        var materialName = StringMember(materialEntry.Value, "material", slotContext);
                        TestAssert.True(map.Materials.TryGetValue(materialName, out var record),
                            slotContext + ": mapped material \"" + materialName
                            + "\" has no art/material_texture_map.json entry");
                        TestAssert.Equal(record!.Mode, StringMember(materialEntry.Value, "mode", slotContext),
                            slotContext + ".mode must match the material map");
                        TestAssert.Equal(record.PxPerM,
                            NumberMember(materialEntry.Value, "px_per_m", slotContext), 1e-9,
                            slotContext + ".px_per_m must match the material map (no per-LOD/per-tier drift)");

                        if (materialEntry.Value.TryGetProperty("canvas_px_rect", out var canvasPixelsElement))
                        {
                            var canvasPixels = IntArray(canvasPixelsElement, 4, slotContext + " canvas_px_rect");
                            TestAssert.Equal(string.Join(",", record.CanvasPixelRect),
                                string.Join(",", canvasPixels),
                                slotContext + ".canvas_px_rect must equal the material map uv_canvas.pixel_rect");
                        }

                        if (materialEntry.Value.TryGetProperty("uv_canvas_rect", out var uvCanvasElement))
                        {
                            var uvCanvas = DoubleArray(uvCanvasElement, 4, slotContext + " uv_canvas_rect");
                            TestAssert.True(UvRectsEqual(uvCanvas, record.CanvasUvRect, UvRectTolerance),
                                slotContext + ".uv_canvas_rect must equal the material map uv_canvas.uv_rect");
                        }

                        TestAssert.True(materialEntry.Name.StartsWith("M_HDBF_", StringComparison.Ordinal),
                            slotContext + ": uv_mapping materials must be keyed by material slot names, got \""
                            + materialEntry.Name + "\"");
                    }

                    checkedLods++;
                }
            }

            // The shared ConstructionStage0 geometry is baked from the Folktails source materials
            // (ART07 §3): its block must therefore list exactly those source materials.
            var constructionGroup = Member(models, ConstructionStageModelKey, context + " models");
            var constructionLod = ObjectEntries(constructionGroup, context + " models[\""
                    + ConstructionStageModelKey + "\"]")
                .First(property => property.Name.StartsWith("LOD", StringComparison.Ordinal));
            var constructionMapping = Member(constructionLod.Value,
                "uv_mapping", context + " models[\"" + ConstructionStageModelKey + "\"] " + constructionLod.Name);
            var constructionMaterials = ObjectEntries(
                    Member(constructionMapping, "materials",
                        context + " models[\"" + ConstructionStageModelKey + "\"] uv_mapping"),
                    context + " ConstructionStage0 uv_mapping.materials")
                .Select(property => StringMember(property.Value, "material",
                    context + " ConstructionStage0 uv_mapping.materials[\"" + property.Name + "\"]"))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var expectedConstructionSources = Slots.ConstructionFactionMaterials["Folktails"]
                .Values.OrderBy(name => name, StringComparer.Ordinal).ToList();
            TestAssert.Equal(string.Join(", ", expectedConstructionSources),
                string.Join(", ", constructionMaterials),
                context + " ConstructionStage0 uv_mapping must list the Folktails source materials "
                + "(the construction stage bakes ONE shared UV set for both factions)");
            foreach (var sourceMaterial in expectedConstructionSources)
            {
                TestAssert.True(map.Materials.TryGetValue(sourceMaterial, out var sourceRecord),
                    context + ": construction source material \"" + sourceMaterial + "\" has no map entry");
                TestAssert.Equal(sourceRecord!.PxPerM,
                    NumberMember(
                        constructionMapping.GetProperty("materials").GetProperty(
                            constructionMapping.GetProperty("materials").EnumerateObject()
                                .First(property => string.Equals(
                                    property.Value.GetProperty("material").GetString(),
                                    sourceMaterial, StringComparison.Ordinal)).Name),
                        "px_per_m",
                        context + " ConstructionStage0 uv_mapping.materials[\"" + sourceMaterial + "\"]"),
                    1e-9,
                    context + ": construction source material \"" + sourceMaterial
                    + "\" density must match the map");
            }

            Console.WriteLine("  material_slots uv mapping: " + ModelGroupKeys().Count + " model groups, "
                + checkedLods + " LOD blocks world_tiled, legacy patch density "
                + RetiredPatchTexelsPerGrid + " px/grid kept for reference only");
        }

        // ==================================================================
        // 6. untouched shipped data (ART-07 changes the UV values only)
        // ==================================================================

        /// <summary>
        /// ART-07 is a UV/mapping round: it must not remove the shipped art document or change the
        /// three language files. Cheap guards only (the deep key-set contract lives in
        /// V2LocalizationTests and ArtModelContractTests, so this is not duplicated here): the
        /// shipped data/ART_PLACEHOLDER.md still exists and still carries the frozen material
        /// contract tag, all three localization CSVs exist, are non-trivial and keep an identical
        /// key set.
        /// </summary>
        public static void TestArtAndLocalizationDataRemainShipped()
        {
            var artDocPath = RepoPath(ArtDocRelativePath);
            TestAssert.True(File.Exists(artDocPath),
                "the shipped art document is missing: " + artDocPath);
            var artDoc = File.ReadAllText(artDocPath, Encoding.UTF8);
            TestAssert.True(artDoc.Length > 1000,
                "data/ART_PLACEHOLDER.md shrank to " + artDoc.Length + " characters; ART-07 must not "
                + "remove the shipped art documentation");
            TestAssert.True(artDoc.Contains("ART_CONTRACT: material=flat-patch", StringComparison.Ordinal),
                "data/ART_PLACEHOLDER.md lost the frozen \"ART_CONTRACT: material=flat-patch\" line; "
                + "ART-07 upgrades the mapping behind that contract but must not silently drop the tag");

            var localizationFolder = RepoPath(LocalizationFolderRelativePath);
            TestAssert.True(Directory.Exists(localizationFolder),
                "the shipped localization folder is missing: " + localizationFolder);
            var languageKeySets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var language in Languages)
            {
                var csvPath = Path.Combine(localizationFolder, language + ".csv");
                TestAssert.True(File.Exists(csvPath), "the " + language + " localization file is missing: " + csvPath);
                var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
                TestAssert.True(lines.Length > 120,
                    language + ".csv shrank to " + lines.Length + " lines; ART-07 must not drop v2 UI keys");
                TestAssert.Equal("ID,Text,Comment", lines[0].Trim(),
                    language + ".csv header");

                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in lines.Skip(1))
                {
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    var comma = line.IndexOf(',');
                    TestAssert.True(comma > 0, language + ".csv contains a line without an ID column: " + line);
                    var key = line.Substring(0, comma).Trim();
                    TestAssert.True(keys.Add(key), language + ".csv contains the duplicate key \"" + key + "\"");
                }

                TestAssert.True(keys.Count > 120,
                    language + ".csv carries only " + keys.Count + " unique keys");
                languageKeySets[language] = keys;
            }

            foreach (var language in Languages.Skip(1))
            {
                var expected = languageKeySets["enUS"];
                var actual = languageKeySets[language];
                TestAssert.Equal(expected.Count, actual.Count,
                    language + ".csv key count must stay identical to enUS.csv");
                foreach (var key in expected)
                {
                    TestAssert.True(actual.Contains(key), language + ".csv is missing the key \"" + key + "\"");
                }
            }

            Console.WriteLine("  shipped data intact: ART_PLACEHOLDER.md (" + artDoc.Length
                + " chars), " + languageKeySets.Count + " localization files with identical key sets ("
                + languageKeySets["enUS"].Count + " keys)");
        }

        // ==================================================================
        // 7. negative controls: policy record validators
        // ==================================================================

        /// <summary>
        /// Pure in-memory negative controls for the §4 policy-record validator used by the map
        /// test (no file is written or touched): a wrong wrap, a tile_meters outside the REQ class
        /// band, a px_per_m inconsistent with the canvas/coverage formula, a canvas rect outside
        /// the texture, a tile material whose canvas is not the whole texture, and a patch material
        /// whose legacy patch record is not the canvas must all be rejected. The matching positive
        /// controls prove the validator is not simply always false.
        /// </summary>
        public static void TestMaterialTilingValidatorsRejectBadRecords()
        {
            var map = Map;
            var hardCap = map.HardStdCap;

            var goodTile = GoodTileMaterial();
            TestAssert.True(TryValidateTilingMaterial(goodTile, hardCap, out var goodTileError),
                "positive control: a well-formed tile policy record was rejected (" + goodTileError + ")");
            var goodPatch = GoodPatchMaterial();
            TestAssert.True(TryValidateTilingMaterial(goodPatch, hardCap, out var goodPatchError),
                "positive control: a well-formed patch policy record was rejected (" + goodPatchError + ")");

            var controls = 0;

            // 1. tile material with a clamping wrap (the game would clamp the full texture at the edge).
            var badWrap = goodTile.Clone();
            badWrap.Wrap = "clamp";
            TestAssert.False(TryValidateTilingMaterial(badWrap, hardCap, out var wrapError),
                "negative control 1: a tile record with wrap=\"clamp\" was accepted");
            TestAssert.True(wrapError.Contains("wrap", StringComparison.OrdinalIgnoreCase),
                "negative control 1: rejection should mention wrap, got \"" + wrapError + "\"");
            controls++;

            // 2. tile_meters outside the REQ class band.
            var badBand = goodTile.Clone();
            badBand.TileMeters = 3.2;
            TestAssert.False(TryValidateTilingMaterial(badBand, hardCap, out var bandError),
                "negative control 2: tile_meters outside the REQ class band was accepted");
            TestAssert.True(bandError.Contains("tile_meters", StringComparison.OrdinalIgnoreCase),
                "negative control 2: rejection should mention tile_meters, got \"" + bandError + "\"");
            controls++;

            // 3. px_per_m that does not reproduce from usable canvas / coverage.
            var badDensity = goodTile.Clone();
            badDensity.PxPerM = 350.0;
            TestAssert.False(TryValidateTilingMaterial(badDensity, hardCap, out var densityError),
                "negative control 3: a px_per_m inconsistent with canvas/coverage was accepted");
            TestAssert.True(densityError.Contains("px_per_m", StringComparison.OrdinalIgnoreCase),
                "negative control 3: rejection should mention px_per_m, got \"" + densityError + "\"");
            controls++;

            // 4. uv_canvas pixel rect beyond the texture.
            var badCanvas = goodTile.Clone();
            badCanvas.CanvasPixelRect = new[] { 900, 900, 1300, 1300 };
            badCanvas.CanvasSizePx = new[] { 400, 400 };
            badCanvas.CanvasUvRect = new[] { 900 / 1024.0, 1.0 - 1300 / 1024.0, 1300 / 1024.0, 1.0 - 900 / 1024.0 };
            TestAssert.False(TryValidateTilingMaterial(badCanvas, hardCap, out var canvasError),
                "negative control 4: a canvas rect outside the texture was accepted");
            TestAssert.True(canvasError.Contains("canvas", StringComparison.OrdinalIgnoreCase),
                "negative control 4: rejection should mention the canvas, got \"" + canvasError + "\"");
            controls++;

            // 5. tile material whose canvas is not the whole texture (would break the "no UV > 1" rule).
            var badTileCanvas = goodTile.Clone();
            badTileCanvas.CanvasPixelRect = new[] { 64, 64, 960, 960 };
            badTileCanvas.CanvasSizePx = new[] { 896, 896 };
            badTileCanvas.CanvasUvRect = new[] { 64 / 1024.0, 1.0 - 960 / 1024.0, 960 / 1024.0, 1.0 - 64 / 1024.0 };
            TestAssert.False(TryValidateTilingMaterial(badTileCanvas, hardCap, out var tileCanvasError),
                "negative control 5: a tile material whose uv_canvas is not the whole texture was accepted");
            TestAssert.True(tileCanvasError.Contains("tile", StringComparison.OrdinalIgnoreCase),
                "negative control 5: rejection should mention the tile canvas, got \"" + tileCanvasError + "\"");
            controls++;

            // 6. patch material whose legacy patch record is not the canvas (C12/C13 would check the
            //    wrong window and the validators would disagree with the generator).
            var badPatchRecord = goodPatch.Clone();
            badPatchRecord.PatchPixelRect = new[] { 64, 64, 320, 320 };
            badPatchRecord.PatchUvRect = new[]
            {
                64 / 1024.0, 1.0 - 320 / 1024.0, 320 / 1024.0, 1.0 - 64 / 1024.0,
            };
            TestAssert.False(TryValidateTilingMaterial(badPatchRecord, hardCap, out var patchRecordError),
                "negative control 6: a patch material whose patch record is not the curated canvas was accepted");
            TestAssert.True(patchRecordError.Contains("patch", StringComparison.OrdinalIgnoreCase),
                "negative control 6: rejection should mention the patch record, got \"" + patchRecordError + "\"");
            controls++;

            Console.WriteLine("  NEGATIVE CONTROLS (ART-07 policy records): PASS " + controls + "/" + controls);
        }

        // ==================================================================
        // 8. negative controls: tampered meshes / map coverage
        // ==================================================================

        /// <summary>
        /// In-memory negative controls for the shipped-geometry validators (the parsed models are
        /// deep-cloned, nothing on disk changes):
        ///  1. halve a tile material's UVs around the canvas centre -> measured density halves, the
        ///     aggregate density check must FAIL (the direct regression guard against the low-res
        ///     ART-06 sampling);
        ///  2. double them -> density doubles, must FAIL;
        ///  3. stretch one patch-material face to the full 0..1 square -> canvas containment must FAIL;
        ///  4. push one shipped tile UV to exactly 0.0 -> the 2-texel guard must FAIL;
        ///  5. drop a used material from the map -> material coverage must FAIL.
        /// Positive controls on the untouched parsed data prove the validators are not always false.
        /// </summary>
        public static void TestUvDensityAndContainmentDetectTamperedMeshes()
        {
            var map = Map;
            var slots = Slots;
            var models = Models;

            // ---- positive controls: the untouched shipped data passes ----
            ShippedModel? tileModel = null;
            string tileMaterial = string.Empty;
            ShippedModel? patchModel = null;
            string patchMaterial = string.Empty;
            foreach (var stem in ExpectedShippedStems().OrderBy(name => name, StringComparer.Ordinal))
            {
                if (stem.Contains(ConstructionStemMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                var model = models[stem];
                foreach (var material in model.Materials)
                {
                    var part = model.Nodes.SelectMany(node => node.Parts)
                        .FirstOrDefault(candidate => string.Equals(candidate.Material, material,
                            StringComparison.Ordinal) && candidate.Indices.Count >= 3);
                    if (part is null || !map.Materials.TryGetValue(material, out var record))
                    {
                        continue;
                    }

                    if (record.IsTile && tileModel is null)
                    {
                        tileModel = model;
                        tileMaterial = material;
                    }
                    else if (!record.IsTile && patchModel is null)
                    {
                        patchModel = model;
                        patchMaterial = material;
                    }
                }

                if (tileModel is not null && patchModel is not null)
                {
                    break;
                }
            }

            TestAssert.NotNull(tileModel, "no finished shipped model with a triangle on a tile material was found");
            TestAssert.NotNull(patchModel, "no finished shipped model with a triangle on a patch material was found");
            TestAssert.True(TryValidateMaterialCoverage(map, models, out var coverageError),
                "positive control: " + coverageError);
            TestAssert.True(TryValidateUvContainment(map, tileModel!, out _, out _, out var tileContainmentError),
                "positive control: " + tileContainmentError);
            TestAssert.True(TryValidateUvContainment(map, patchModel!, out _, out _, out var patchContainmentError),
                "positive control: " + patchContainmentError);
            TestAssert.True(TryValidateDensities(map, slots, tileModel!, out _, out var tileDensityError),
                "positive control: " + tileDensityError);

            var controls = 0;

            // 1. halve a tile material's UVs.
            var halved = CloneModel(tileModel!);
            TestAssert.True(ScaleMaterialUvs(halved, tileMaterial, 0.5, map),
                "negative control 1 setup: tile material \"" + tileMaterial + "\" not found in the parsed mesh");
            TestAssert.False(TryValidateDensities(map, slots, halved, out _, out var halvedError),
                "negative control 1: halving a tile material's UVs was accepted by the density check");
            TestAssert.True(halvedError.Contains("density", StringComparison.OrdinalIgnoreCase),
                "negative control 1: rejection should mention the density, got \"" + halvedError + "\"");
            controls++;

            // 2. double a tile material's UVs.
            var doubled = CloneModel(tileModel!);
            TestAssert.True(ScaleMaterialUvs(doubled, tileMaterial, 2.0, map),
                "negative control 2 setup: tile material \"" + tileMaterial + "\" not found in the parsed mesh");
            TestAssert.False(TryValidateDensities(map, slots, doubled, out _, out var doubledError),
                "negative control 2: doubling a tile material's UVs was accepted by the density check");
            TestAssert.True(doubledError.Contains("density", StringComparison.OrdinalIgnoreCase),
                "negative control 2: rejection should mention the density, got \"" + doubledError + "\"");
            controls++;

            // 3. stretch one patch-material face to the full 0..1 square (UV escape).
            var stretched = CloneModel(patchModel!);
            TestAssert.True(StretchFirstFaceToUnitSquare(stretched, patchMaterial),
                "negative control 3 setup: patch material \"" + patchMaterial + "\" triangle not found");
            TestAssert.False(TryValidateUvContainment(map, stretched, out _, out _, out var stretchError),
                "negative control 3: a 0..1-stretched patch face was accepted by the canvas containment check");
            TestAssert.True(stretchError.Contains("outside", StringComparison.OrdinalIgnoreCase)
                    || stretchError.Contains("canvas", StringComparison.OrdinalIgnoreCase),
                "negative control 3: rejection should mention leaving the canvas, got \"" + stretchError + "\"");
            controls++;

            // 4. a tile UV on the atlas cell border (auto-atlas would bleed the neighbour material).
            var escaped = CloneModel(tileModel!);
            TestAssert.True(PushFirstUvToCanvasBorder(escaped, tileMaterial),
                "negative control 4 setup: tile material \"" + tileMaterial + "\" UV not found");
            TestAssert.False(TryValidateUvContainment(map, escaped, out _, out _, out var escapeError),
                "negative control 4: a UV on the atlas cell border was accepted (2-texel guard hole)");
            controls++;

            // 5. drop a used material from the map.
            var mapWithoutMaterial = map.CloneWithout(tileMaterial);
            TestAssert.True(mapWithoutMaterial.Materials.Count == map.Materials.Count - 1,
                "negative control 5 setup: the material \"" + tileMaterial + "\" was not in the map");
            TestAssert.False(TryValidateMaterialCoverage(mapWithoutMaterial, models, out var dropError),
                "negative control 5: dropping a used material from the map was accepted by the coverage check");
            TestAssert.True(dropError.Contains(tileMaterial, StringComparison.Ordinal),
                "negative control 5: rejection should name the dropped material, got \"" + dropError + "\"");
            controls++;

            Console.WriteLine("  NEGATIVE CONTROLS (tampered meshes): PASS " + controls + "/" + controls);
        }

        // ==================================================================
        // ART-07 data model (parsed from the shipped files)
        // ==================================================================

        /// <summary>Parsed art/material_texture_map.json (ART07 §4 records + policy block).</summary>
        private sealed class TilingMap
        {
            public JsonElement Root;
            public JsonElement UvMapping;
            public double HardStdCap;
            public Dictionary<string, TilingMaterial> Materials { get; } = new(StringComparer.Ordinal);

            public TilingMap CloneWithout(string material)
            {
                var clone = new TilingMap { Root = Root, UvMapping = UvMapping, HardStdCap = HardStdCap };
                foreach (var pair in Materials)
                {
                    if (!string.Equals(pair.Key, material, StringComparison.Ordinal))
                    {
                        clone.Materials[pair.Key] = pair.Value;
                    }
                }

                return clone;
            }
        }

        /// <summary>One ART07 §4 material policy record (map materials[&lt;name&gt;]).</summary>
        private sealed class TilingMaterial
        {
            public string Name = string.Empty;
            public string Mode = string.Empty;
            public string Wrap = string.Empty;
            public string PatchClass = string.Empty;
            public double? TileMeters;
            public bool HasTileMetersBand;
            public double[] TileMetersBand = Array.Empty<double>();
            public int Width;
            public int Height;
            public int[] CanvasPixelRect = Array.Empty<int>();
            public double[] CanvasUvRect = Array.Empty<double>();
            public int[] CanvasSizePx = Array.Empty<int>();
            public string CanvasMeaning = string.Empty;
            public Dictionary<string, string[]> ProjectionAxes = new(StringComparer.Ordinal);
            public double[] ProjectionWorldCenter = Array.Empty<double>();
            public double[] ProjectionUvCenter = Array.Empty<double>();
            public string ProjectionNote = string.Empty;
            public double PxPerM;
            public double CoverageU;
            public double CoverageV;
            public double[] CanvasMeters = Array.Empty<double>();
            public string WrapEvidence = string.Empty;
            public string UvReason = string.Empty;
            public bool HasPatchRecord;
            public bool HasArt07CanvasFlag;
            public bool PatchIsArt07Canvas;
            public int[] PatchPixelRect = Array.Empty<int>();
            public double[] PatchUvRect = Array.Empty<double>();
            public double PatchStd;
            public double PatchStdMax;

            public bool IsTile => string.Equals(Mode, "tile", StringComparison.Ordinal);

            public TilingMaterial Clone()
            {
                var clone = (TilingMaterial)MemberwiseClone();
                clone.TileMetersBand = (double[])TileMetersBand.Clone();
                clone.CanvasPixelRect = (int[])CanvasPixelRect.Clone();
                clone.CanvasUvRect = (double[])CanvasUvRect.Clone();
                clone.CanvasSizePx = (int[])CanvasSizePx.Clone();
                clone.CanvasMeters = (double[])CanvasMeters.Clone();
                clone.ProjectionAxes = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (var pair in ProjectionAxes)
                {
                    clone.ProjectionAxes[pair.Key] = (string[])pair.Value.Clone();
                }

                clone.ProjectionWorldCenter = (double[])ProjectionWorldCenter.Clone();
                clone.ProjectionUvCenter = (double[])ProjectionUvCenter.Clone();
                clone.PatchPixelRect = (int[])PatchPixelRect.Clone();
                clone.PatchUvRect = (double[])PatchUvRect.Clone();
                return clone;
            }
        }

        /// <summary>Parsed art/material_slots.json (ART07 §4 per-model world_tiled mapping).</summary>
        private sealed class SlotsContract
        {
            public JsonElement Root;
            public Dictionary<string, Dictionary<string, string>> ConstructionFactionMaterials { get; } =
                new(StringComparer.Ordinal);
        }

        /// <summary>A decoded shipped .timbermesh with its uv0 geometry.</summary>
        private sealed class ShippedModel
        {
            public string Stem = string.Empty;
            public string FilePath = string.Empty;
            public byte[] Payload = Array.Empty<byte>();
            public string PayloadText = string.Empty;
            public string DecodedName = string.Empty;
            public List<MeshNode> Nodes { get; } = new();
            public List<string> Materials { get; } = new();
        }

        private sealed class MeshNode
        {
            public string Name = string.Empty;
            public int VertexCount;
            public List<(double X, double Y, double Z)> Positions { get; set; } = new();
            public List<(double U, double V)> Uvs { get; set; } = new();
            public List<MeshPart> Parts { get; } = new();
        }

        private sealed class MeshPart
        {
            public List<int> Indices { get; set; } = new();
            public string Material = string.Empty;
        }

        /// <summary>Per-material world-projected texel-density accumulator (ART07 §5 C15).</summary>
        private sealed class MaterialDensity
        {
            public string Material = string.Empty;
            public int TextureWidth;
            public int TextureHeight;
            public double SumUvArea;
            public double SumProjectedAreaM2;
            public double SumWorldAreaM2;
            public double AmbiguousWorldAreaM2;
            public int TriangleCount;
            public int AmbiguousTriangles;
            public int DegenerateTriangles;
            public int LargeTriangles;
            public int LargeTrianglesOutsideTolerance;
            public double MaxTriangleRelativeError;
            public double ExpectedPxPerM;

            public double MeasuredPxPerM => SumProjectedAreaM2 > 0.0
                ? Math.Sqrt(SumUvArea * TextureWidth * TextureHeight / SumProjectedAreaM2)
                : double.NaN;
        }

        private sealed class ModelDensityReport
        {
            public Dictionary<string, MaterialDensity> Materials { get; } = new(StringComparer.Ordinal);

            public int Triangles => Materials.Values.Sum(density => density.TriangleCount);
            public int AmbiguousTriangles => Materials.Values.Sum(density => density.AmbiguousTriangles);
            public int DegenerateTriangles => Materials.Values.Sum(density => density.DegenerateTriangles);
            public int LargeTriangles => Materials.Values.Sum(density => density.LargeTriangles);
            public int LargeTrianglesOutsideTolerance =>
                Materials.Values.Sum(density => density.LargeTrianglesOutsideTolerance);
            public double TotalWorldAreaM2 => Materials.Values.Sum(density => density.SumWorldAreaM2);
            public double AmbiguousWorldAreaM2 => Materials.Values.Sum(density => density.AmbiguousWorldAreaM2);
        }

        // ==================================================================
        // loading: art/material_texture_map.json
        // ==================================================================

        private static TilingMap LoadTilingMap()
        {
            var root = LoadJsonFile(MapRelativePath, "the ART-07 UV material map");
            const string context = MapRelativePath;
            var map = new TilingMap { Root = root };
            TestAssert.Equal(MapSchema, StringMember(root, "schema", context),
                context + " schema id (ART07 §4 keeps the /v1 schema)");

            var thresholds = Member(root, "thresholds", context);
            map.HardStdCap = NumberMember(thresholds, "hard_std_cap", context + " thresholds");
            TestAssert.True(map.HardStdCap > 0.0,
                context + ": thresholds.hard_std_cap must be a positive std cap");

            map.UvMapping = Member(root, "uv_mapping",
                context + " (the ART07 §4 world-UV policy block is required)");

            var materials = Member(root, "materials", context);
            foreach (var property in ObjectEntries(materials, context + " materials"))
            {
                map.Materials[property.Name] = ParseTilingMaterial(property.Name, property.Value,
                    context + " materials[\"" + property.Name + "\"]");
            }

            TestAssert.True(map.Materials.Count >= 8,
                context + ": expected a policy entry for every used vanilla material, found only "
                + map.Materials.Count);
            return map;
        }

        private static TilingMaterial ParseTilingMaterial(string materialName, JsonElement entry, string context)
        {
            var material = new TilingMaterial { Name = materialName };
            material.Mode = StringMember(entry, "mode", context);
            material.Wrap = StringMember(entry, "wrap", context);
            material.PatchClass = StringMember(entry, "patch_class", context);

            var textureSize = IntArray(Member(entry, "texture_size", context), 2, context + " texture_size");
            material.Width = textureSize[0];
            material.Height = textureSize[1];

            if (entry.TryGetProperty("tile_meters", out var tileMetersElement)
                && tileMetersElement.ValueKind != JsonValueKind.Null)
            {
                material.TileMeters = NumberValue(tileMetersElement, context + " tile_meters");
            }

            if (entry.TryGetProperty("tile_meters_band", out var tileMetersBandElement)
                && tileMetersBandElement.ValueKind == JsonValueKind.Array)
            {
                material.HasTileMetersBand = true;
                material.TileMetersBand = DoubleArray(tileMetersBandElement, 2,
                    context + " tile_meters_band");
            }

            var canvas = Member(entry, "uv_canvas", context);
            material.CanvasPixelRect = IntArray(Member(canvas, "pixel_rect", context + " uv_canvas"), 4,
                context + " uv_canvas.pixel_rect");
            material.CanvasUvRect = DoubleArray(Member(canvas, "uv_rect", context + " uv_canvas"), 4,
                context + " uv_canvas.uv_rect");
            material.CanvasSizePx = IntArray(Member(canvas, "size_px", context + " uv_canvas"), 2,
                context + " uv_canvas.size_px");
            material.CanvasMeaning = StringMember(canvas, "meaning", context + " uv_canvas");

            var projection = Member(entry, "projection", context);
            material.ProjectionAxes = ParseProjectionAxes(
                Member(projection, "axes", context + " projection"),
                context + " projection.axes");
            material.ProjectionWorldCenter = DoubleArray(
                Member(projection, "world_center", context + " projection"), 3,
                context + " projection.world_center");
            material.ProjectionUvCenter = DoubleArray(
                Member(projection, "uv_center", context + " projection"), 2,
                context + " projection.uv_center");
            material.ProjectionNote = StringMember(projection, "note", context + " projection");

            material.PxPerM = NumberMember(entry, "px_per_m", context);
            material.CoverageU = NumberMember(entry, "coverage_u_m", context);
            material.CoverageV = NumberMember(entry, "coverage_v_m", context);
            material.CanvasMeters = DoubleArray(Member(entry, "canvas_meters", context), 2,
                context + " canvas_meters");
            material.WrapEvidence = StringMember(entry, "wrap_evidence", context);

            if (entry.TryGetProperty("uv_reason", out var uvReasonElement)
                && uvReasonElement.ValueKind == JsonValueKind.String)
            {
                material.UvReason = uvReasonElement.GetString()!;
            }

            // The legacy ART-05/06 probe record is kept for every material (ART07 §4 bullet:
            // patch mode -> the canvas itself; tile mode -> the small uniform probe window).
            var patch = Member(entry, "patch", context);
            material.HasPatchRecord = true;
            material.PatchPixelRect = IntArray(Member(patch, "pixel_rect", context + " patch"), 4,
                context + " patch.pixel_rect");
            material.PatchUvRect = DoubleArray(Member(patch, "uv_rect", context + " patch"), 4,
                context + " patch.uv_rect");
            material.PatchStd = NumberMember(patch, "std", context + " patch");
            material.PatchStdMax = NumberMember(patch, "std_max", context + " patch");
            if (patch.TryGetProperty("art07_canvas", out var art07CanvasElement))
            {
                material.HasArt07CanvasFlag = true;
                material.PatchIsArt07Canvas = art07CanvasElement.ValueKind == JsonValueKind.True;
            }

            return material;
        }

        private static Dictionary<string, string[]> ParseProjectionAxes(JsonElement element, string context)
        {
            var axes = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var property in ObjectEntries(element, context))
            {
                var values = StringArray(property.Value, context + "[\"" + property.Name + "\"]",
                    requireNonEmpty: true);
                TestAssert.Equal(2, values.Length,
                    context + "[\"" + property.Name + "\"] must be a 2-axis pair");
                axes[property.Name] = values;
            }

            return axes;
        }

        // ==================================================================
        // loading: art/material_slots.json
        // ==================================================================

        private static SlotsContract LoadSlotsContract()
        {
            var root = LoadJsonFile(SlotsRelativePath, "art/material_slots.json");
            const string context = SlotsRelativePath;
            var slots = new SlotsContract { Root = root };

            JsonElement factionMaterials;
            if (!root.TryGetProperty("construction_faction_materials", out factionMaterials))
            {
                var models = Member(root, "models", context);
                var construction = Member(models, ConstructionStageModelKey, context + " models");
                factionMaterials = Member(construction, "construction_faction_materials",
                    context + " models[\"" + ConstructionStageModelKey + "\"]");
            }

            foreach (var faction in Factions)
            {
                slots.ConstructionFactionMaterials[faction] = ReadSlotMaterialMap(
                    Member(factionMaterials, faction, context + " construction_faction_materials"),
                    context + " construction_faction_materials[\"" + faction + "\"]");
            }

            return slots;
        }

        private static Dictionary<string, string> ReadSlotMaterialMap(JsonElement element, string context)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in ObjectEntries(element, context))
            {
                result[property.Name] = StringValue(property.Value,
                    context + "[\"" + property.Name + "\"]");
            }

            return result;
        }

        // ==================================================================
        // loading: shipped .timbermesh files
        // ==================================================================

        private static Dictionary<string, ShippedModel> LoadShippedModels()
        {
            var models = new Dictionary<string, ShippedModel>(StringComparer.Ordinal);
            foreach (var stem in ExpectedShippedStems())
            {
                var path = ShippedMeshPath(stem);
                var label = stem + ".timbermesh";
                TestAssert.True(File.Exists(path),
                    "expected the shipped model " + label + " at " + path
                    + " (all " + ExpectedShippedModelCount + " BoxFarm models must ship)");

                var compressed = File.ReadAllBytes(path);
                TestAssert.True(compressed.Length > 0, label + " is empty (" + path + ")");
                TestAssert.True(compressed.Length >= 2, label + " is too short to carry a zlib header");
                TestAssert.True((compressed[0] & 0x0F) == 8,
                    label + " is not a zlib stream (CMF compression method must be 8, got 0x"
                    + (compressed[0] & 0x0F).ToString("X2") + ")");
                TestAssert.True((((compressed[0] << 8) | compressed[1]) % 31) == 0,
                    label + " has an invalid zlib header (FCHECK mismatch)");

                var payload = DecompressZlib(compressed);
                TestAssert.True(payload.Length > 0, label + " decompressed to 0 bytes");

                var raw = ParseTimbermesh(payload, label);
                var model = new ShippedModel
                {
                    Stem = stem,
                    FilePath = path,
                    Payload = payload,
                    PayloadText = Encoding.UTF8.GetString(payload),
                    DecodedName = raw.Name,
                };

                foreach (var rawNode in raw.Nodes)
                {
                    var node = new MeshNode { Name = rawNode.Name, VertexCount = rawNode.VertexCount };
                    var positionProperty = rawNode.Properties.FirstOrDefault(property =>
                        string.Equals(property.Name, "position", StringComparison.Ordinal));
                    var uvProperty = rawNode.Properties.FirstOrDefault(property =>
                        string.Equals(property.Name, "uv0", StringComparison.Ordinal));
                    if (positionProperty is not null)
                    {
                        node.Positions = DecodePositions(positionProperty,
                            label + ": node \"" + rawNode.Name + "\" position");
                    }

                    if (uvProperty is not null)
                    {
                        node.Uvs = DecodeUvs(uvProperty, label + ": node \"" + rawNode.Name + "\" uv0");
                    }

                    foreach (var rawMesh in rawNode.Meshes)
                    {
                        node.Parts.Add(new MeshPart
                        {
                            Material = rawMesh.Material,
                            Indices = rawMesh.Indices,
                        });
                    }

                    model.Nodes.Add(node);
                }

                CollectShippedMaterials(model);
                ValidateShippedGeometry(model);
                models[stem] = model;
            }

            return models;
        }

        private static void CollectShippedMaterials(ShippedModel model)
        {
            foreach (var part in model.Nodes.SelectMany(node => node.Parts))
            {
                if (part.Material.Length > 0
                    && !model.Materials.Contains(part.Material, StringComparer.Ordinal))
                {
                    model.Materials.Add(part.Material);
                }
            }

            model.Materials.Sort(StringComparer.Ordinal);
        }

        /// <summary>
        /// Structural sanity of one decoded model: every mesh part carries a material, indices are
        /// a multiple of 3 and inside the node's vertex arrays, and a node with mesh data carries
        /// both position and uv0 (a mesh without uv0 cannot sample its material at all).
        /// </summary>
        private static void ValidateShippedGeometry(ShippedModel model)
        {
            var label = model.Stem + ".timbermesh";
            foreach (var node in model.Nodes)
            {
                var meshParts = node.Parts.Where(part => part.Indices.Count > 0).ToList();
                if (meshParts.Count > 0)
                {
                    TestAssert.True(node.Positions.Count > 0,
                        label + ": node \"" + node.Name
                        + "\" carries mesh data but no position vertex property");
                    TestAssert.True(node.Uvs.Count > 0,
                        label + ": node \"" + node.Name
                        + "\" carries mesh data but no uv0 vertex property (cannot sample a material)");
                    TestAssert.Equal(node.Positions.Count, node.Uvs.Count,
                        label + ": node \"" + node.Name
                        + "\" has a different number of position and uv0 vertices");
                }

                foreach (var part in meshParts)
                {
                    TestAssert.True(part.Material.Length > 0,
                        label + ": node \"" + node.Name + "\" has a mesh without a material name");
                    TestAssert.Equal(0, part.Indices.Count % 3,
                        label + ": node \"" + node.Name + "\" mesh \"" + part.Material
                        + "\" index count is not a multiple of 3");
                    foreach (var index in part.Indices)
                    {
                        TestAssert.True(index >= 0 && index < node.Positions.Count,
                            label + ": node \"" + node.Name + "\" mesh \"" + part.Material
                            + "\" index " + index + " is outside the node's " + node.Positions.Count
                            + " position vertices");
                        TestAssert.True(index < node.Uvs.Count,
                            label + ": node \"" + node.Name + "\" mesh \"" + part.Material
                            + "\" index " + index + " is outside the node's " + node.Uvs.Count
                            + " uv0 vertices");
                    }
                }
            }
        }

        private static List<(double X, double Y, double Z)> DecodePositions(
            RawTimbermeshProperty property, string context)
        {
            TestAssert.Equal(3, property.Dimension, context + " vertex property must have dimension 3");
            TestAssert.Equal(0, property.Data.Length % 12, context + " data length is not a multiple of 12 bytes");
            var result = new List<(double X, double Y, double Z)>(property.Data.Length / 12);
            for (var offset = 0; offset < property.Data.Length; offset += 12)
            {
                var x = BitConverter.ToSingle(property.Data, offset);
                var y = BitConverter.ToSingle(property.Data, offset + 4);
                var z = BitConverter.ToSingle(property.Data, offset + 8);
                TestAssert.True(float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z),
                    context + " has a non-finite position at vertex " + (offset / 12));
                result.Add((x, y, z));
            }

            return result;
        }

        private static List<(double U, double V)> DecodeUvs(RawTimbermeshProperty property, string context)
        {
            TestAssert.Equal(2, property.Dimension, context + " vertex property must have dimension 2");
            TestAssert.Equal(0, property.Data.Length % 8, context + " data length is not a multiple of 8 bytes");
            var result = new List<(double U, double V)>(property.Data.Length / 8);
            for (var offset = 0; offset < property.Data.Length; offset += 8)
            {
                var u = BitConverter.ToSingle(property.Data, offset);
                var v = BitConverter.ToSingle(property.Data, offset + 4);
                TestAssert.True(float.IsFinite(u) && float.IsFinite(v),
                    context + " has a non-finite uv0 value at vertex " + (offset / 8));
                result.Add((u, v));
            }

            return result;
        }

        // ------------------------------------------------------------------ timbermesh protobuf
        //
        // A .timbermesh is zlib(protobuf Model) with this vendored schema (see
        // art/tools/timbermesh_blender_plugin/model_pb2.py):
        //   Model { version=1:int32, name=2:string, nodes=3:repeated Node }
        //   Node  { parent=1:int32, name=2:string, position=3:Vector3Float,
        //           vertexCount=6:int32, vertexProperties=7:repeated VertexProperty,
        //           meshes=8:repeated Mesh }
        //   VertexProperty { name=1:string, scalarType=2:enum, scalarTypeDimension=3:int32,
        //                    data=4:bytes }
        //   Mesh { indices=1:repeated int32 (packed or not), material=2:string }
        // The same minimal wire-format reader as ArtModelContractTests / V2MaterialPatchContractTests
        // (kept local so the ART-07 card has no dependency on the other contract cards); no
        // game/Unity/protobuf assembly is referenced.

        private sealed class RawTimbermeshModel
        {
            public int Version;
            public string Name = string.Empty;
            public List<RawTimbermeshNode> Nodes { get; } = new();
        }

        private sealed class RawTimbermeshNode
        {
            public string Name = string.Empty;
            public int VertexCount;
            public List<RawTimbermeshProperty> Properties { get; } = new();
            public List<RawTimbermeshMesh> Meshes { get; } = new();
        }

        private sealed class RawTimbermeshProperty
        {
            public string Name = string.Empty;
            public int Dimension;
            public byte[] Data = Array.Empty<byte>();
        }

        private sealed class RawTimbermeshMesh
        {
            public List<int> Indices { get; } = new();
            public string Material = string.Empty;
        }

        private sealed class ProtobufReader
        {
            private readonly byte[] _data;
            private int _offset;

            public ProtobufReader(byte[] data)
            {
                _data = data;
            }

            public bool AtEnd => _offset >= _data.Length;

            public ulong ReadVarint()
            {
                ulong value = 0;
                var shift = 0;
                while (true)
                {
                    if (_offset >= _data.Length)
                    {
                        throw new InvalidDataException("truncated varint");
                    }

                    var current = _data[_offset++];
                    value |= (ulong)(current & 0x7F) << shift;
                    if ((current & 0x80) == 0)
                    {
                        return value;
                    }

                    shift += 7;
                    if (shift > 63)
                    {
                        throw new InvalidDataException("varint longer than 10 bytes");
                    }
                }
            }

            public int ReadInt32()
                => unchecked((int)ReadVarint());

            public byte[] ReadBytes()
            {
                var length = ReadInt32();
                if (length < 0 || _offset + length > _data.Length)
                {
                    throw new InvalidDataException("truncated length-delimited field");
                }

                var result = new byte[length];
                Buffer.BlockCopy(_data, _offset, result, 0, length);
                _offset += length;
                return result;
            }

            public List<int> ReadPackedVarints()
            {
                var nested = new ProtobufReader(ReadBytes());
                var values = new List<int>();
                while (!nested.AtEnd)
                {
                    values.Add(nested.ReadInt32());
                }

                return values;
            }

            public void Skip(int wireType)
            {
                switch (wireType)
                {
                    case 0:
                        ReadVarint();
                        break;
                    case 1:
                        if (_offset + 8 > _data.Length)
                        {
                            throw new InvalidDataException("truncated fixed64 field");
                        }

                        _offset += 8;
                        break;
                    case 2:
                        ReadBytes();
                        break;
                    case 5:
                        if (_offset + 4 > _data.Length)
                        {
                            throw new InvalidDataException("truncated fixed32 field");
                        }

                        _offset += 4;
                        break;
                    default:
                        throw new InvalidDataException("unsupported wire type " + wireType);
                }
            }
        }

        private static RawTimbermeshModel ParseTimbermesh(byte[] payload, string label)
        {
            try
            {
                return ParseTimbermeshCore(payload);
            }
            catch (Exception exception) when (exception is not TestFailureException)
            {
                throw new TestFailureException(
                    label + ": decompressed payload is not a decodable timbermesh protobuf ("
                    + exception.GetType().Name + ": " + exception.Message + ")");
            }
        }

        private static RawTimbermeshModel ParseTimbermeshCore(byte[] payload)
        {
            var model = new RawTimbermeshModel();
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        model.Version = checked((int)reader.ReadVarint());
                        break;
                    case 2:
                        model.Name = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    case 3:
                        model.Nodes.Add(ParseTimbermeshNode(reader.ReadBytes()));
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            return model;
        }

        private static RawTimbermeshNode ParseTimbermeshNode(byte[] payload)
        {
            var node = new RawTimbermeshNode();
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        _ = reader.ReadInt32(); // parent (-1 for the root node)
                        break;
                    case 2:
                        node.Name = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    case 3:
                        _ = reader.ReadBytes(); // node translation Vector3Float (not needed: every
                        // density/area metric below only uses vertex position differences)
                        break;
                    case 6:
                        node.VertexCount = checked((int)reader.ReadVarint());
                        break;
                    case 7:
                        node.Properties.Add(ParseTimbermeshProperty(reader.ReadBytes()));
                        break;
                    case 8:
                        node.Meshes.Add(ParseTimbermeshMesh(reader.ReadBytes()));
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            return node;
        }

        private static RawTimbermeshProperty ParseTimbermeshProperty(byte[] payload)
        {
            var property = new RawTimbermeshProperty();
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        property.Name = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    case 3:
                        property.Dimension = checked((int)reader.ReadVarint());
                        break;
                    case 4:
                        property.Data = reader.ReadBytes();
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            return property;
        }

        private static RawTimbermeshMesh ParseTimbermeshMesh(byte[] payload)
        {
            var mesh = new RawTimbermeshMesh();
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        if (wire == 2)
                        {
                            mesh.Indices.AddRange(reader.ReadPackedVarints());
                        }
                        else
                        {
                            mesh.Indices.Add(reader.ReadInt32());
                        }

                        break;
                    case 2:
                        mesh.Material = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            return mesh;
        }

        // ==================================================================
        // validators (shared by the shipped-data tests and the negative controls)
        // ==================================================================

        /// <summary>
        /// Reproduces the ART07 §2 density formula from the §4 record:
        /// <c>px_per_m = floor1(min(nominal, usable_w / coverage_u, usable_h / coverage_v))</c>
        /// with <c>usable = canvas_size - 2 * guard</c> and <c>nominal = texture_w / tile_meters</c>
        /// for tile materials. Used to prove the map value is not hand-written.
        /// </summary>
        private static double RecomputePxPerM(TilingMaterial material)
        {
            var usableWidth = material.CanvasSizePx[0] - 2 * UvGuardTexels;
            var usableHeight = material.CanvasSizePx[1] - 2 * UvGuardTexels;
            var fitDensity = double.PositiveInfinity;
            if (material.CoverageU > 0.0)
            {
                fitDensity = Math.Min(fitDensity, usableWidth / material.CoverageU);
            }

            if (material.CoverageV > 0.0)
            {
                fitDensity = Math.Min(fitDensity, usableHeight / material.CoverageV);
            }

            var nominalDensity = double.PositiveInfinity;
            if (material.TileMeters is double tileMeters && tileMeters > 0.0)
            {
                nominalDensity = material.Width / tileMeters;
            }

            var density = Math.Min(nominalDensity, fitDensity);
            return Math.Floor(density * 10.0 + 1e-9) / 10.0;
        }

        /// <summary>
        /// Semantic validator of one ART07 §4 policy record (used for every shipped material and
        /// for the in-memory negative controls). Returns false and a human-readable reason for the
        /// first violated rule.
        /// </summary>
        private static bool TryValidateTilingMaterial(TilingMaterial material, double hardStdCap, out string error)
        {
            error = string.Empty;

            if (material.Mode != "tile" && material.Mode != "patch")
            {
                return Reject("mode \"" + material.Mode + "\" must be \"tile\" or \"patch\"", out error);
            }

            if (!KnownPatchClasses.Contains(material.PatchClass, StringComparer.Ordinal))
            {
                return Reject("patch_class \"" + material.PatchClass + "\" is not in the frozen vocabulary",
                    out error);
            }

            var expectedWrap = material.IsTile ? "repeat" : "clamp";
            if (!string.Equals(material.Wrap, expectedWrap, StringComparison.Ordinal))
            {
                return Reject("wrap \"" + material.Wrap + "\" must be \"" + expectedWrap
                    + "\" for a " + material.Mode + " material (ART07 §4)", out error);
            }

            if (material.Width < 128 || material.Height < 128
                || material.Width > 8192 || material.Height > 8192)
            {
                return Reject("texture_size " + material.Width + "x" + material.Height
                    + " is not a real 1024²-class texture", out error);
            }

            // ---- uv_canvas ----
            if (material.CanvasPixelRect.Length != 4 || material.CanvasSizePx.Length != 2
                || material.CanvasUvRect.Length != 4)
            {
                return Reject("uv_canvas must carry pixel_rect(4), uv_rect(4) and size_px(2)", out error);
            }

            var x0 = material.CanvasPixelRect[0];
            var y0 = material.CanvasPixelRect[1];
            var x1 = material.CanvasPixelRect[2];
            var y1 = material.CanvasPixelRect[3];
            if (x0 < 0 || y0 < 0 || x0 >= x1 || y0 >= y1
                || x1 > material.Width || y1 > material.Height)
            {
                return Reject("uv_canvas.pixel_rect [" + string.Join(", ", material.CanvasPixelRect)
                    + "] is not a non-empty rect inside the " + material.Width + "x" + material.Height
                    + " texture", out error);
            }

            if (material.CanvasSizePx[0] != x1 - x0 || material.CanvasSizePx[1] != y1 - y0)
            {
                return Reject("uv_canvas.size_px must equal the pixel_rect size", out error);
            }

            var u0 = material.CanvasUvRect[0];
            var v0 = material.CanvasUvRect[1];
            var u1 = material.CanvasUvRect[2];
            var v1 = material.CanvasUvRect[3];
            if (material.CanvasUvRect.Any(value => double.IsNaN(value) || double.IsInfinity(value))
                || u0 < 0.0 || u0 >= u1 || u1 > 1.0 || v0 < 0.0 || v0 >= v1 || v1 > 1.0)
            {
                return Reject("uv_canvas.uv_rect must satisfy 0 <= u0 < u1 <= 1 and 0 <= v0 < v1 <= 1",
                    out error);
            }

            var expectedCanvasUv = new[]
            {
                x0 / (double)material.Width,
                1.0 - y1 / (double)material.Height,
                x1 / (double)material.Width,
                1.0 - y0 / (double)material.Height,
            };
            if (!UvRectsEqual(material.CanvasUvRect, expectedCanvasUv, UvRectTolerance))
            {
                return Reject("uv_canvas.uv_rect does not match pixel_rect / texture_size under the "
                    + "frozen flipped-v convention (top-left pixel origin, v flipped)", out error);
            }

            if (material.CanvasMeaning.Trim().Length == 0)
            {
                return Reject("uv_canvas.meaning must explain what the canvas is", out error);
            }

            // ---- projection (ART07 §2/§4 frozen table) ----
            if (!TryValidateProjectionAxes(material.ProjectionAxes, out var projectionError))
            {
                return Reject(projectionError, out error);
            }

            if (material.ProjectionWorldCenter.Length != 3
                || !AreClose(material.ProjectionWorldCenter, ExpectedWorldCenter, 1e-9))
            {
                return Reject("projection.world_center must be the frozen [0, 0, 0.5]", out error);
            }

            var canvasCenterU = (material.CanvasUvRect[0] + material.CanvasUvRect[2]) * 0.5;
            var canvasCenterV = (material.CanvasUvRect[1] + material.CanvasUvRect[3]) * 0.5;
            if (material.ProjectionUvCenter.Length != 2
                || Math.Abs(material.ProjectionUvCenter[0] - canvasCenterU) > UvRectTolerance
                || Math.Abs(material.ProjectionUvCenter[1] - canvasCenterV) > UvRectTolerance)
            {
                return Reject("projection.uv_center must be the centre of the material canvas", out error);
            }

            if (material.ProjectionNote.Trim().Length == 0)
            {
                return Reject("projection.note must document the mapping formula", out error);
            }

            if (!double.IsFinite(material.CoverageU) || !double.IsFinite(material.CoverageV)
                || material.CoverageU <= 0.0 || material.CoverageV <= 0.0)
            {
                return Reject("coverage_u_m/coverage_v_m must be positive finite world spans", out error);
            }

            // ---- mode-specific canvas shape (checked before the density formula so the rejection
            //      message names the structural problem, not the derived density mismatch) ----
            if (material.IsTile)
            {
                if (material.TileMeters is null)
                {
                    return Reject("a tile material must declare tile_meters", out error);
                }

                if (!TileMetersBandByPatchClass.TryGetValue(material.PatchClass, out var band))
                {
                    return Reject("patch_class \"" + material.PatchClass
                        + "\" has no REQ tile_meters band", out error);
                }

                if (material.TileMeters.Value < band.Min - 1e-9
                    || material.TileMeters.Value > band.Max + 1e-9)
                {
                    return Reject("tile_meters " + material.TileMeters + " is outside the REQ class band ["
                        + band.Min + ", " + band.Max + "] of patch class \"" + material.PatchClass + "\"",
                        out error);
                }

                if (material.CanvasPixelRect[0] != 0 || material.CanvasPixelRect[1] != 0
                    || material.CanvasPixelRect[2] != material.Width
                    || material.CanvasPixelRect[3] != material.Height)
                {
                    return Reject("a tile material canvas must be the whole " + material.Width + "x"
                        + material.Height + " texture (full-bleed tileable surface texture)", out error);
                }
            }
            else
            {
                if (material.TileMeters is not null)
                {
                    return Reject("a patch material must record tile_meters: null", out error);
                }

                if (material.CanvasPixelRect[0] == 0 && material.CanvasPixelRect[1] == 0
                    && material.CanvasPixelRect[2] == material.Width
                    && material.CanvasPixelRect[3] == material.Height)
                {
                    return Reject("a patch material canvas must be a curated region of the atlas sheet, "
                        + "not the whole texture", out error);
                }
            }

            // ---- px_per_m (ART07 §2 formula + §5 REQ bands) ----
            if (!double.IsFinite(material.PxPerM) || material.PxPerM <= 0.0)
            {
                return Reject("px_per_m must be a positive finite number", out error);
            }

            var recomputedDensity = RecomputePxPerM(material);
            if (Math.Abs(recomputedDensity - material.PxPerM) > 1e-6)
            {
                return Reject("px_per_m " + material.PxPerM + " does not reproduce from usable canvas / "
                    + "coverage (expected " + recomputedDensity + ")", out error);
            }

            if (material.IsTile)
            {
                if (material.PxPerM < TileDensityBandMin || material.PxPerM > TileDensityBandMax)
                {
                    return Reject("tile px_per_m " + material.PxPerM + " is outside the REQ "
                        + TileDensityBandMin + "-" + TileDensityBandMax + " px/m band", out error);
                }
            }
            else if (material.PxPerM <= RetiredPatchTexelsPerGrid)
            {
                return Reject("patch px_per_m " + material.PxPerM + " is not clearly above the retired "
                    + "ART-06 " + RetiredPatchTexelsPerGrid + " px/m density", out error);
            }

            // ---- canvas_meters covers the required world span (ART07 §3) ----
            if (material.CanvasMeters.Length != 2)
            {
                return Reject("canvas_meters must have 2 numbers", out error);
            }

            var coverage = new[] { material.CoverageU, material.CoverageV };
            for (var axis = 0; axis < 2; axis++)
            {
                var expectedMeters = material.CanvasSizePx[axis] / material.PxPerM;
                if (Math.Abs(material.CanvasMeters[axis] - expectedMeters) > 1e-3)
                {
                    return Reject("canvas_meters[" + axis + "] " + material.CanvasMeters[axis]
                        + " must equal canvas_size / px_per_m (" + expectedMeters + ")", out error);
                }

                if (material.CanvasMeters[axis] < coverage[axis] - 1e-6)
                {
                    return Reject("canvas_meters[" + axis + "] " + material.CanvasMeters[axis]
                        + " does not cover the required world span coverage_" + (axis == 0 ? "u" : "v")
                        + "_m " + coverage[axis], out error);
                }
            }

            // ---- legacy ART-05/06 patch record is kept for C12/C13 ----
            if (!material.HasPatchRecord)
            {
                return Reject("the legacy \"patch\" record must be kept so the C12/C13 texture checks "
                    + "keep working", out error);
            }

            if (material.PatchPixelRect.Length != 4 || material.PatchUvRect.Length != 4)
            {
                return Reject("patch record must carry a 4-int pixel_rect and a 4-number uv_rect", out error);
            }

            var px0 = material.PatchPixelRect[0];
            var py0 = material.PatchPixelRect[1];
            var px1 = material.PatchPixelRect[2];
            var py1 = material.PatchPixelRect[3];
            if (px0 < 0 || py0 < 0 || px0 >= px1 || py0 >= py1
                || px1 > material.Width || py1 > material.Height)
            {
                return Reject("patch.pixel_rect [" + string.Join(", ", material.PatchPixelRect)
                    + "] is not a non-empty rect inside the " + material.Width + "x" + material.Height
                    + " texture", out error);
            }

            var expectedPatchUv = new[]
            {
                px0 / (double)material.Width,
                1.0 - py1 / (double)material.Height,
                px1 / (double)material.Width,
                1.0 - py0 / (double)material.Height,
            };
            if (!UvRectsEqual(material.PatchUvRect, expectedPatchUv, UvRectTolerance))
            {
                return Reject("patch.uv_rect does not match patch.pixel_rect / texture_size under the "
                    + "frozen flipped-v convention", out error);
            }

            if (!double.IsFinite(material.PatchStd) || material.PatchStd < 0.0)
            {
                return Reject("patch.std must be a recorded value >= 0", out error);
            }

            if (!double.IsFinite(material.PatchStdMax) || material.PatchStdMax <= 0.0)
            {
                return Reject("patch.std_max must be a threshold > 0", out error);
            }

            if (material.PatchStd > material.PatchStdMax)
            {
                return Reject("patch.std " + material.PatchStd + " exceeds patch.std_max "
                    + material.PatchStdMax, out error);
            }

            if (material.PatchStdMax > hardStdCap + 1e-9)
            {
                return Reject("patch.std_max " + material.PatchStdMax
                    + " exceeds thresholds.hard_std_cap " + hardStdCap, out error);
            }

            if (!material.IsTile)
            {
                // ART07 §4: for patch materials the legacy record IS the curated canvas.
                if (!SameIntRect(material.PatchPixelRect, material.CanvasPixelRect))
                {
                    return Reject("a patch material's \"patch\" record must BE the curated uv_canvas "
                        + "(patch.pixel_rect [" + string.Join(", ", material.PatchPixelRect)
                        + "] != uv_canvas.pixel_rect [" + string.Join(", ", material.CanvasPixelRect) + "])",
                        out error);
                }

                if (!UvRectsEqual(material.PatchUvRect, material.CanvasUvRect, UvRectTolerance))
                {
                    return Reject("a patch material's \"patch\" uv_rect must equal uv_canvas.uv_rect",
                        out error);
                }

                if (material.HasArt07CanvasFlag && !material.PatchIsArt07Canvas)
                {
                    return Reject("patch.art07_canvas must be true on the curated canvas record", out error);
                }
            }

            return true;
        }

        /// <summary>
        /// ART07 §1/§5 C7/C11 containment: every indexed uv0 value must stay inside the material
        /// canvas — tile: [guard, 1-guard] on each axis; patch: the curated uv_canvas.uv_rect.
        /// </summary>
        private static bool TryValidateUvContainment(TilingMap map, ShippedModel model,
            out int checkedUvs, out int checkedMeshes, out string error)
        {
            checkedUvs = 0;
            checkedMeshes = 0;
            error = string.Empty;
            var label = model.Stem + ".timbermesh";

            foreach (var node in model.Nodes)
            {
                foreach (var part in node.Parts)
                {
                    if (part.Indices.Count == 0)
                    {
                        continue;
                    }

                    checkedMeshes++;
                    if (!map.Materials.TryGetValue(part.Material, out var material))
                    {
                        return Reject(label + ": material \"" + part.Material
                            + "\" has no art/material_texture_map.json entry", out error);
                    }

                    double uMin, uMax, vMin, vMax;
                    string kind;
                    if (material.IsTile)
                    {
                        var guardU = UvGuardTexels / (double)material.Width;
                        var guardV = UvGuardTexels / (double)material.Height;
                        uMin = guardU;
                        uMax = 1.0 - guardU;
                        vMin = guardV;
                        vMax = 1.0 - guardV;
                        kind = "tile canvas guard";
                    }
                    else
                    {
                        uMin = material.CanvasUvRect[0];
                        vMin = material.CanvasUvRect[1];
                        uMax = material.CanvasUvRect[2];
                        vMax = material.CanvasUvRect[3];
                        kind = "patch uv_canvas";
                    }

                    foreach (var index in part.Indices)
                    {
                        if (index < 0 || index >= node.Uvs.Count)
                        {
                            return Reject(label + ": mesh index " + index + " is outside the node's "
                                + node.Uvs.Count + " uv0 vertices", out error);
                        }

                        var (u, v) = node.Uvs[index];
                        if (u < uMin - CanvasContainmentTolerance || u > uMax + CanvasContainmentTolerance
                            || v < vMin - CanvasContainmentTolerance || v > vMax + CanvasContainmentTolerance)
                        {
                            return Reject(label + ": material \"" + part.Material + "\" has uv0 (" + u
                                + ", " + v + ") outside its " + kind + " [" + uMin + ", " + vMin + "]..["
                                + uMax + ", " + vMax + "]; the runtime AutoAtlaser would sample a "
                                + "neighbouring material (ART07 §1)", out error);
                        }

                        checkedUvs++;
                    }
                }
            }

            if (checkedMeshes == 0)
            {
                return Reject(label + ": no material mesh with indices was decoded", out error);
            }

            return true;
        }

        /// <summary>
        /// Every material used by a shipped mesh must have an ART07 map entry and every map entry
        /// must be used by at least one shipped mesh (no stale/unmapped materials).
        /// </summary>
        private static bool TryValidateMaterialCoverage(TilingMap map,
            Dictionary<string, ShippedModel> models, out string error)
        {
            error = string.Empty;
            var used = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var stem in ExpectedShippedStems())
            {
                if (!models.TryGetValue(stem, out var model))
                {
                    return Reject("missing parsed shipped model " + stem, out error);
                }

                foreach (var material in model.Materials)
                {
                    used.Add(material);
                    if (!map.Materials.ContainsKey(material))
                    {
                        return Reject(stem + ".timbermesh: uses material \"" + material
                            + "\" but art/material_texture_map.json has no ART07 policy entry", out error);
                    }
                }
            }

            foreach (var material in map.Materials.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!used.Contains(material))
                {
                    return Reject("art/material_texture_map.json declares ART07 material \"" + material
                        + "\" but no shipped model uses it (stale map entry)", out error);
                }
            }

            return true;
        }

        /// <summary>
        /// Expected px/m for every material of a shipped model. Finished models use the material's
        /// own map px_per_m. The ConstructionStage0 geometry is baked ONCE from the Folktails
        /// construction material set (ART07 §3) and the IronTeeth file reuses that UV set with its
        /// own materials, so the expected density there is the Folktails source material's density
        /// (e.g. RoofPlanks.IronTeeth inherits BaseWood_Brown 145.3 px/m). The mapping comes from
        /// art/material_slots.json construction_faction_materials, not from hard-coded names.
        /// </summary>
        private static Dictionary<string, double> ResolveExpectedPxPerM(ShippedModel model, TilingMap map,
            SlotsContract slots)
        {
            var expected = new Dictionary<string, double>(StringComparer.Ordinal);
            var isConstructionStage = model.Stem.Contains(ConstructionStemMarker, StringComparison.Ordinal);
            var faction = FactionOfModelStem(model.Stem);
            foreach (var material in model.Materials)
            {
                if (!map.Materials.TryGetValue(material, out var record))
                {
                    expected[material] = double.NaN;
                    continue;
                }

                var density = record.PxPerM;
                if (isConstructionStage
                    && slots.ConstructionFactionMaterials.TryGetValue(faction, out var factionMaterials))
                {
                    string? slot = null;
                    foreach (var pair in factionMaterials)
                    {
                        if (string.Equals(pair.Value, material, StringComparison.Ordinal))
                        {
                            slot = pair.Key;
                            break;
                        }
                    }

                    if (slot is not null
                        && slots.ConstructionFactionMaterials.TryGetValue("Folktails", out var folktailsMaterials)
                        && folktailsMaterials.TryGetValue(slot, out var sourceMaterial)
                        && map.Materials.TryGetValue(sourceMaterial, out var sourceRecord))
                    {
                        density = sourceRecord.PxPerM;
                    }
                }

                expected[material] = density;
            }

            return expected;
        }

        /// <summary>
        /// ART07 §5 C15 world-UV density measurement. Per triangle:
        ///  * dominant axis of the triangle normal (game space; the exporter axis conversion is an
        ///    isometry, so the dominant axis and the projected area are invariant);
        ///  * triangles with two near-equal dominant |components| (bevels / 45° planes) are
        ///    "ambiguous plane": skipped and counted;
        ///  * world area projected onto the dominant-axis plane = |cross|·axis / 2;
        ///  * uv area from the triangle's uv0 values;
        ///  * per material: measured px/m = sqrt(sum(uv_area) * W * H / sum(projected_area)).
        /// </summary>
        private static ModelDensityReport MeasureDensities(ShippedModel model, TilingMap map,
            SlotsContract slots)
        {
            var report = new ModelDensityReport();
            var expected = ResolveExpectedPxPerM(model, map, slots);
            foreach (var node in model.Nodes)
            {
                foreach (var part in node.Parts)
                {
                    if (part.Indices.Count < 3
                        || !map.Materials.TryGetValue(part.Material, out var materialRecord))
                    {
                        continue;
                    }

                    if (!report.Materials.TryGetValue(part.Material, out var density))
                    {
                        density = new MaterialDensity
                        {
                            Material = part.Material,
                            TextureWidth = materialRecord.Width,
                            TextureHeight = materialRecord.Height,
                            ExpectedPxPerM = expected.TryGetValue(part.Material, out var expectedDensity)
                                ? expectedDensity
                                : double.NaN,
                        };
                        report.Materials[part.Material] = density;
                    }

                    for (var offset = 0; offset + 2 < part.Indices.Count; offset += 3)
                    {
                        var i0 = part.Indices[offset];
                        var i1 = part.Indices[offset + 1];
                        var i2 = part.Indices[offset + 2];
                        var p0 = node.Positions[i0];
                        var p1 = node.Positions[i1];
                        var p2 = node.Positions[i2];
                        var e1X = p1.X - p0.X;
                        var e1Y = p1.Y - p0.Y;
                        var e1Z = p1.Z - p0.Z;
                        var e2X = p2.X - p0.X;
                        var e2Y = p2.Y - p0.Y;
                        var e2Z = p2.Z - p0.Z;
                        var nX = e1Y * e2Z - e1Z * e2Y;
                        var nY = e1Z * e2X - e1X * e2Z;
                        var nZ = e1X * e2Y - e1Y * e2X;
                        var normalLength = Math.Sqrt(nX * nX + nY * nY + nZ * nZ);
                        if (normalLength <= 1e-12)
                        {
                            density.DegenerateTriangles++;
                            continue;
                        }

                        // Frozen projection tie-break (ART07 §5 C15 / validate_models.py):
                        // unit Newell normal, |component| rounded to 4 decimals, dominant axis with
                        // X > Y > Z priority; proj_area = world_area * dominant rounded component.
                        var unitAbs = new[]
                        {
                            Math.Abs(nX) / normalLength,
                            Math.Abs(nY) / normalLength,
                            Math.Abs(nZ) / normalLength,
                        };
                        var roundedAbs = new[]
                        {
                            Math.Round(unitAbs[0], 4),
                            Math.Round(unitAbs[1], 4),
                            Math.Round(unitAbs[2], 4),
                        };
                        var dominant = 0;
                        if (roundedAbs[1] > roundedAbs[dominant])
                        {
                            dominant = 1;
                        }

                        if (roundedAbs[2] > roundedAbs[dominant])
                        {
                            dominant = 2;
                        }

                        var sortedAbs = new[] { roundedAbs[0], roundedAbs[1], roundedAbs[2] }
                            .OrderByDescending(value => value)
                            .ToArray();
                        var isAmbiguous = sortedAbs[0] - sortedAbs[1] <= AmbiguousNormalEpsilon;

                        var worldAreaM2 = 0.5 * normalLength;
                        var projectedAreaM2 = worldAreaM2 * roundedAbs[dominant];
                        var (u0, v0) = node.Uvs[i0];
                        var (u1, v1) = node.Uvs[i1];
                        var (u2, v2) = node.Uvs[i2];
                        var uvArea = 0.5 * Math.Abs((u1 - u0) * (v2 - v0) - (u2 - u0) * (v1 - v0));
                        density.SumUvArea += uvArea;
                        density.SumProjectedAreaM2 += projectedAreaM2;
                        density.SumWorldAreaM2 += worldAreaM2;
                        density.TriangleCount++;
                        if (isAmbiguous)
                        {
                            density.AmbiguousTriangles++;
                            density.AmbiguousWorldAreaM2 += worldAreaM2;
                        }

                        if (worldAreaM2 < MinLargeTriangleWorldAreaM2
                            || projectedAreaM2 <= 0.0
                            || !(density.ExpectedPxPerM > 0.0))
                        {
                            continue;
                        }

                        density.LargeTriangles++;
                        var triangleDensity = Math.Sqrt(
                            uvArea * density.TextureWidth * density.TextureHeight / projectedAreaM2);
                        var relativeError = Math.Abs(triangleDensity - density.ExpectedPxPerM)
                            / density.ExpectedPxPerM;
                        density.MaxTriangleRelativeError =
                            Math.Max(density.MaxTriangleRelativeError, relativeError);
                        if (relativeError > PerTriangleDensityTolerance)
                        {
                            density.LargeTrianglesOutsideTolerance++;
                        }
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// Density validator shared by the shipped-data test and the negative controls: aggregate
        /// ±2 % against the resolved policy, REQ 300-600 px/m for tile materials, ±25 % on every
        /// large triangle, and a sane ambiguous-plane budget.
        /// </summary>
        private static bool TryValidateDensities(TilingMap map, SlotsContract slots, ShippedModel model,
            out ModelDensityReport report, out string error)
        {
            report = MeasureDensities(model, map, slots);
            error = string.Empty;
            var label = model.Stem + ".timbermesh";
            if (report.Materials.Count == 0)
            {
                return Reject(label + ": no material mesh with triangles was decoded", out error);
            }

            foreach (var material in model.Materials)
            {
                if (!map.Materials.ContainsKey(material))
                {
                    return Reject(label + ": material \"" + material
                        + "\" has no art/material_texture_map.json entry", out error);
                }

                if (!report.Materials.ContainsKey(material))
                {
                    return Reject(label + ": material \"" + material
                        + "\" has no non-degenerate triangle to measure", out error);
                }
            }

            var analysedTotal = report.Triangles + report.DegenerateTriangles;
            if (analysedTotal == 0)
            {
                return Reject(label + ": no triangle at all was analysed", out error);
            }

            if (report.AmbiguousTriangles > MaxAmbiguousTriangleFraction * report.Triangles)
            {
                return Reject(label + ": " + report.AmbiguousTriangles + "/" + report.Triangles
                    + " triangles are ambiguous-plane (45° bevels: two near-equal dominant normal "
                    + "components), over the " + (MaxAmbiguousTriangleFraction * 100.0) + " % budget",
                    out error);
            }

            var ambiguousAreaFraction = report.TotalWorldAreaM2 > 0.0
                ? report.AmbiguousWorldAreaM2 / report.TotalWorldAreaM2
                : 1.0;
            if (ambiguousAreaFraction > MaxAmbiguousAreaFraction)
            {
                return Reject(label + ": ambiguous-plane triangles cover "
                    + (ambiguousAreaFraction * 100.0).ToString("F3")
                    + " % of the world area, over the " + (MaxAmbiguousAreaFraction * 100.0)
                    + " % budget", out error);
            }

            foreach (var pair in report.Materials.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var density = pair.Value;
                var materialRecord = map.Materials[pair.Key];
                if (!double.IsFinite(density.MeasuredPxPerM))
                {
                    return Reject(label + ": material \"" + pair.Key
                        + "\" has no positive projected world area (cannot measure a density)", out error);
                }

                if (!double.IsFinite(density.ExpectedPxPerM) || density.ExpectedPxPerM <= 0.0)
                {
                    return Reject(label + ": material \"" + pair.Key
                        + "\" has no expected px_per_m in the ART07 map", out error);
                }

                var relativeError = Math.Abs(density.MeasuredPxPerM - density.ExpectedPxPerM)
                    / density.ExpectedPxPerM;
                if (relativeError > AggregateDensityTolerance)
                {
                    return Reject(label + ": material \"" + pair.Key + "\" measured world-UV density "
                        + density.MeasuredPxPerM + " px/m deviates "
                        + (relativeError * 100.0).ToString("F2") + " % from the ART07 policy "
                        + density.ExpectedPxPerM + " px/m (tolerance "
                        + (AggregateDensityTolerance * 100.0) + " %)", out error);
                }

                // Tile band only for materials that use their OWN policy density: the shared
                // ConstructionStage0 UV set legitimately inherits the Folktails source density.
                if (materialRecord.IsTile
                    && Math.Abs(density.ExpectedPxPerM - materialRecord.PxPerM) < 1e-9
                    && (density.MeasuredPxPerM < TileDensityBandMin
                        || density.MeasuredPxPerM > TileDensityBandMax))
                {
                    return Reject(label + ": tile material \"" + pair.Key + "\" measured "
                        + density.MeasuredPxPerM + " px/m outside the REQ target band ["
                        + TileDensityBandMin + ", " + TileDensityBandMax + "]", out error);
                }

                if (density.LargeTrianglesOutsideTolerance > 0)
                {
                    return Reject(label + ": material \"" + pair.Key + "\" has "
                        + density.LargeTrianglesOutsideTolerance + "/" + density.LargeTriangles
                        + " large triangle(s) outside the ±"
                        + (PerTriangleDensityTolerance * 100.0) + " % per-triangle density tolerance "
                        + "(worst " + (density.MaxTriangleRelativeError * 100.0).ToString("F2") + " %)",
                        out error);
                }
            }

            return true;
        }

        private static bool TryValidateProjectionAxes(Dictionary<string, string[]> actual, out string error)
        {
            error = string.Empty;
            foreach (var expected in ExpectedProjectionAxes)
            {
                if (!actual.TryGetValue(expected.Key, out var values))
                {
                    return Reject("projection.axes is missing \"" + expected.Key + "\"", out error);
                }

                if (values.Length != 2 || !values.SequenceEqual(expected.Value, StringComparer.Ordinal))
                {
                    return Reject("projection.axes[\"" + expected.Key + "\"] must be ["
                        + string.Join(", ", expected.Value) + "], got ["
                        + string.Join(", ", values) + "]", out error);
                }
            }

            if (actual.Count != ExpectedProjectionAxes.Count)
            {
                return Reject("projection.axes carries " + actual.Count + " keys; the frozen table has "
                    + ExpectedProjectionAxes.Count, out error);
            }

            return true;
        }

        private static bool Reject(string message, out string error)
        {
            error = message;
            return false;
        }

        // ==================================================================
        // negative-control helpers (in-memory only)
        // ==================================================================

        /// <summary>Synthetic, fully consistent tile policy record (positive control).</summary>
        private static TilingMaterial GoodTileMaterial()
        {
            const int textureSize = 1024;
            const int left = 468;
            const int top = 4;
            const int right = 788;
            const int bottom = 324;
            return new TilingMaterial
            {
                Name = "Plaster_White.Folktails",
                Mode = "tile",
                Wrap = "repeat",
                PatchClass = "plaster",
                TileMeters = 2.6,
                HasTileMetersBand = true,
                TileMetersBand = new[] { 2.4, 2.8 },
                Width = textureSize,
                Height = textureSize,
                CanvasPixelRect = new[] { 0, 0, textureSize, textureSize },
                CanvasUvRect = new[] { 0.0, 0.0, 1.0, 1.0 },
                CanvasSizePx = new[] { textureSize, textureSize },
                CanvasMeaning = "synthetic positive-control full-bleed canvas",
                ProjectionAxes = CloneExpectedProjectionAxes(),
                ProjectionWorldCenter = new[] { 0.0, 0.0, 0.5 },
                ProjectionUvCenter = new[] { 0.5, 0.5 },
                ProjectionNote = "synthetic positive control",
                PxPerM = 340.0,
                CoverageU = 3.0,
                CoverageV = 3.0,
                CanvasMeters = new[] { 3.0118, 3.0118 },
                WrapEvidence = "synthetic positive control",
                UvReason = "synthetic positive control",
                HasPatchRecord = true,
                PatchPixelRect = new[] { left, top, right, bottom },
                PatchUvRect = new[]
                {
                    left / (double)textureSize,
                    1.0 - bottom / (double)textureSize,
                    right / (double)textureSize,
                    1.0 - top / (double)textureSize,
                },
                PatchStd = 9.97,
                PatchStdMax = 10.0,
            };
        }

        /// <summary>Synthetic, fully consistent patch policy record (positive control).</summary>
        private static TilingMaterial GoodPatchMaterial()
        {
            const int textureSize = 1024;
            const int x0 = 4;
            const int y0 = 4;
            const int x1 = 512;
            const int y1 = 444;
            var uv = new[]
            {
                x0 / (double)textureSize,
                1.0 - y1 / (double)textureSize,
                x1 / (double)textureSize,
                1.0 - y0 / (double)textureSize,
            };
            return new TilingMaterial
            {
                Name = "BaseWood_Brown.Folktails",
                Mode = "patch",
                Wrap = "clamp",
                PatchClass = "wood",
                TileMeters = null,
                Width = textureSize,
                Height = textureSize,
                CanvasPixelRect = new[] { x0, y0, x1, y1 },
                CanvasUvRect = uv,
                CanvasSizePx = new[] { x1 - x0, y1 - y0 },
                CanvasMeaning = "synthetic positive-control curated canvas",
                ProjectionAxes = CloneExpectedProjectionAxes(),
                ProjectionWorldCenter = new[] { 0.0, 0.0, 0.5 },
                ProjectionUvCenter = new[] { (uv[0] + uv[2]) * 0.5, (uv[1] + uv[3]) * 0.5 },
                ProjectionNote = "synthetic positive control",
                PxPerM = 145.3,
                CoverageU = 3.0,
                CoverageV = 3.0,
                CanvasMeters = new[] { 3.4962, 3.0282 },
                WrapEvidence = "synthetic positive control",
                UvReason = "synthetic positive control",
                HasPatchRecord = true,
                HasArt07CanvasFlag = true,
                PatchIsArt07Canvas = true,
                PatchPixelRect = new[] { x0, y0, x1, y1 },
                PatchUvRect = uv,
                PatchStd = 11.73,
                PatchStdMax = 30.0,
            };
        }

        private static Dictionary<string, string[]> CloneExpectedProjectionAxes()
        {
            var clone = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var pair in ExpectedProjectionAxes)
            {
                clone[pair.Key] = (string[])pair.Value.Clone();
            }

            return clone;
        }

        private static ShippedModel CloneModel(ShippedModel source)
        {
            var clone = new ShippedModel
            {
                Stem = source.Stem,
                FilePath = source.FilePath,
                Payload = source.Payload,
                PayloadText = source.PayloadText,
                DecodedName = source.DecodedName,
            };
            clone.Materials.AddRange(source.Materials);
            foreach (var node in source.Nodes)
            {
                var cloneNode = new MeshNode
                {
                    Name = node.Name,
                    VertexCount = node.VertexCount,
                    Positions = new List<(double X, double Y, double Z)>(node.Positions),
                    Uvs = new List<(double U, double V)>(node.Uvs),
                };
                foreach (var part in node.Parts)
                {
                    cloneNode.Parts.Add(new MeshPart
                    {
                        Material = part.Material,
                        Indices = new List<int>(part.Indices),
                    });
                }

                clone.Nodes.Add(cloneNode);
            }

            return clone;
        }

        /// <summary>Scales the UVs of one material around the canvas centre (in-memory tamper).</summary>
        private static bool ScaleMaterialUvs(ShippedModel model, string material, double factor, TilingMap map)
        {
            var centerU = 0.5;
            var centerV = 0.5;
            if (map.Materials.TryGetValue(material, out var record) && record.ProjectionUvCenter.Length == 2)
            {
                centerU = record.ProjectionUvCenter[0];
                centerV = record.ProjectionUvCenter[1];
            }

            var touched = new HashSet<int>();
            var found = false;
            foreach (var node in model.Nodes)
            {
                foreach (var part in node.Parts)
                {
                    if (!string.Equals(part.Material, material, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var index in part.Indices)
                    {
                        if (!touched.Add(index))
                        {
                            continue;
                        }

                        var (u, v) = node.Uvs[index];
                        node.Uvs[index] = (centerU + (u - centerU) * factor,
                            centerV + (v - centerV) * factor);
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>Stretches the first triangle of a material to the full 0..1 square (UV escape).</summary>
        private static bool StretchFirstFaceToUnitSquare(ShippedModel model, string material)
        {
            foreach (var node in model.Nodes)
            {
                foreach (var part in node.Parts)
                {
                    if (!string.Equals(part.Material, material, StringComparison.Ordinal)
                        || part.Indices.Count < 3)
                    {
                        continue;
                    }

                    node.Uvs[part.Indices[0]] = (0.0, 0.0);
                    node.Uvs[part.Indices[1]] = (1.0, 0.0);
                    node.Uvs[part.Indices[2]] = (0.0, 1.0);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Pushes one shipped tile UV onto the atlas cell border (guard-hole control).</summary>
        private static bool PushFirstUvToCanvasBorder(ShippedModel model, string material)
        {
            foreach (var node in model.Nodes)
            {
                foreach (var part in node.Parts)
                {
                    if (!string.Equals(part.Material, material, StringComparison.Ordinal)
                        || part.Indices.Count == 0)
                    {
                        continue;
                    }

                    var index = part.Indices[0];
                    var (_, v) = node.Uvs[index];
                    node.Uvs[index] = (0.0, v);
                    return true;
                }
            }

            return false;
        }

        // ==================================================================
        // shipped model naming / paths
        // ==================================================================

        private static List<string> ExpectedShippedStems()
        {
            var stems = new List<string>(ExpectedShippedModelCount);
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    stems.Add("BoxFarm." + tier + "." + faction + ".Model");
                    stems.Add("BoxFarm." + tier + "." + faction + ".ConstructionStage0.Model");
                }
            }

            return stems;
        }

        private static List<string> ModelGroupKeys()
        {
            var keys = Tiers
                .SelectMany(tier => Factions.Select(faction => tier + "." + faction))
                .ToList();
            keys.Add(ConstructionStageModelKey);
            return keys;
        }

        private static string FactionOfModelStem(string stem)
        {
            var parts = stem.Split('.');
            TestAssert.True(parts.Length >= 4 && parts[0] == "BoxFarm"
                    && Tiers.Contains(parts[1], StringComparer.Ordinal)
                    && Factions.Contains(parts[2], StringComparer.Ordinal)
                    && parts[parts.Length - 1] == "Model",
                "unexpected shipped model stem \"" + stem
                + "\" (expected BoxFarm.<Tier>.<Faction>[.ConstructionStage0].Model)");
            return parts[2];
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2MaterialTilingContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Key, RepoRootMetadataKey, StringComparison.Ordinal));
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Value))
            {
                throw new TestFailureException(
                    "assembly metadata \"" + RepoRootMetadataKey + "\" is missing: HDBF.Tests.csproj must "
                    + "declare <AssemblyMetadata Include=\"" + RepoRootMetadataKey + "\" .../> so the L1 art "
                    + "contract tests can find art/ and data/ (the build output lives outside the repository)");
            }

            return metadata.Value!;
        }

        private static string DataRoot()
        {
            var dataRoot = Path.Combine(RepoRoot(), "data");
            if (!Directory.Exists(dataRoot))
            {
                throw new TestFailureException(
                    "shipped data/ directory not found under \"" + RepoRoot() + "\" (looked for "
                    + dataRoot + ")");
            }

            return dataRoot;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string ShippedDataFolder()
            => Path.Combine(DataRoot(), BoxFarmDataFolder.Replace('/', Path.DirectorySeparatorChar));

        private static string ShippedMeshPath(string stem)
            => Path.Combine(ShippedDataFolder(), stem + ".timbermesh");

        // ==================================================================
        // generic helpers
        // ==================================================================

        private static bool UvRectsEqual(double[] left, double[] right, double tolerance)
            => left.Length == 4 && right.Length == 4
               && Math.Abs(left[0] - right[0]) <= tolerance
               && Math.Abs(left[1] - right[1]) <= tolerance
               && Math.Abs(left[2] - right[2]) <= tolerance
               && Math.Abs(left[3] - right[3]) <= tolerance;

        private static bool SameIntRect(int[] left, int[] right)
            => left.Length == 4 && right.Length == 4
               && left[0] == right[0] && left[1] == right[1]
               && left[2] == right[2] && left[3] == right[3];

        private static bool AreClose(double[] left, double[] right, double tolerance)
            => left.Length == right.Length
               && left.Zip(right, (a, b) => Math.Abs(a - b) <= tolerance).All(match => match);

        private static int CountOccurrences(string text, string token)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static byte[] DecompressZlib(byte[] compressed)
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        // ------------------------------------------------------------------ JSON helpers

        private static JsonElement LoadJsonFile(string relativePath, string what)
        {
            var path = RepoPath(relativePath);
            TestAssert.True(File.Exists(path), "expected " + what + " at " + path);
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            return document.RootElement.Clone();
        }

        private static IEnumerable<JsonProperty> ObjectEntries(JsonElement element, string context)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new TestFailureException(context + ": expected a JSON object, got " + element.ValueKind);
            }

            return element.EnumerateObject();
        }

        private static JsonElement Member(JsonElement element, string name, string context)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new TestFailureException(context + ": expected a JSON object while reading \""
                    + name + "\", got " + element.ValueKind);
            }

            if (!element.TryGetProperty(name, out var value))
            {
                throw new TestFailureException(context + ": missing JSON member \"" + name + "\"");
            }

            return value;
        }

        private static string StringValue(JsonElement element, string context)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new TestFailureException(context + ": expected a JSON string, got " + element.ValueKind);
            }

            return element.GetString()!;
        }

        private static string StringMember(JsonElement element, string name, string context)
            => StringValue(Member(element, name, context), context + " \"" + name + "\"");

        private static bool BoolMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            throw new TestFailureException(context + " \"" + name + "\": expected a JSON boolean, got "
                + value.ValueKind);
        }

        private static double NumberValue(JsonElement element, string context)
        {
            if (element.ValueKind != JsonValueKind.Number)
            {
                throw new TestFailureException(context + ": expected a JSON number, got " + element.ValueKind);
            }

            var value = element.GetDouble();
            if (!double.IsFinite(value))
            {
                throw new TestFailureException(context + ": JSON number is not finite");
            }

            return value;
        }

        private static double NumberMember(JsonElement element, string name, string context)
            => NumberValue(Member(element, name, context), context + " \"" + name + "\"");

        private static int IntValue(JsonElement element, string context)
        {
            var value = NumberValue(element, context);
            if (Math.Abs(value - Math.Round(value)) > 1e-9)
            {
                throw new TestFailureException(context + ": expected an integer, got " + value);
            }

            return checked((int)Math.Round(value));
        }

        private static int[] IntArray(JsonElement element, int expectedLength, string context)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new TestFailureException(context + ": expected an array of " + expectedLength
                    + " integers, got " + element.ValueKind);
            }

            TestAssert.Equal(expectedLength, element.GetArrayLength(),
                context + ": expected " + expectedLength + " integers");
            var values = new int[expectedLength];
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                values[index] = IntValue(item, context + "[" + index + "]");
                index++;
            }

            return values;
        }

        private static double[] DoubleArray(JsonElement element, int expectedLength, string context)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new TestFailureException(context + ": expected an array of " + expectedLength
                    + " numbers, got " + element.ValueKind);
            }

            TestAssert.Equal(expectedLength, element.GetArrayLength(),
                context + ": expected " + expectedLength + " numbers");
            var values = new double[expectedLength];
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                values[index] = NumberValue(item, context + "[" + index + "]");
                index++;
            }

            return values;
        }

        private static string[] StringArray(JsonElement element, string context, bool requireNonEmpty)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new TestFailureException(context + ": expected an array of strings, got "
                    + element.ValueKind);
            }

            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                var text = StringValue(item, context + "[" + values.Count + "]");
                if (text.Length == 0)
                {
                    throw new TestFailureException(context + ": array items must be non-empty strings");
                }

                values.Add(text);
            }

            if (requireNonEmpty)
            {
                TestAssert.True(values.Count > 0, context + ": array must not be empty");
            }

            return values.ToArray();
        }
    }
}
