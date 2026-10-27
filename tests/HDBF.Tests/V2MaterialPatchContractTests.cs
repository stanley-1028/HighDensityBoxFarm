using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HDBF.Tests
{
    /// <summary>
    /// v2.3 L1 data contract for the per-material texture patch map.
    ///
    /// Background (user-visible bug, 2026-10-27): the 16 shipped BoxFarm
    /// <c>.timbermesh</c> files sampled a single fabricated 2048x2048 ART-01 atlas,
    /// while every vanilla material actually resolves its own 1024x1024 <c>_D</c>
    /// texture at runtime. In game that produced checkerboard/UV-mismatch surfaces
    /// and yellow/black hazard stripes on the Advanced/Top large shells
    /// (<c>PaintedMetal</c>). The Lead replaced the flat-patch system with
    /// <c>art/material_texture_map.json</c> (one entry per used vanilla material:
    /// its REAL texture + the pixel/UV patch inside that texture, with recorded
    /// mean RGB / std / std_max thresholds), and the regenerated models map every
    /// face of a material into that material's own patch.
    ///
    /// What this L1 layer pins (all cheap, deterministic, offline: JSON + zlib +
    /// protobuf byte reading only, no Blender, no game/Unity assemblies):
    ///  * <c>art/material_texture_map.json</c> exists, has the frozen schema id and
    ///    every declared field (thresholds, large_shell_policy, texture roots,
    ///    per-material texture/sha256/tint/patch record);
    ///  * every material used by any of the 16 shipped models has a map entry, and
    ///    every map material is faction-scoped;
    ///  * no large shell uses a <c>PaintedMetal.*</c> hazard material (the direct
    ///    regression guard for the yellow/black stripes) and the retired
    ///    MATERIAL_MAP mapping no longer targets PaintedMetal;
    ///  * the shipped mesh material names stay inside the faction
    ///    MATERIAL_WHITELIST and the 16 files exist / are listed in
    ///    art/material_slots.json;
    ///  * patch UV rects are inside [0,1], non-degenerate, and every used
    ///    material's texture is a real per-material 1024x1024-class texture, not the
    ///    old fake 2048 atlas;
    ///  * the shipped meshes' real uv0 vertex data (decompressed from the
    ///    <c>.timbermesh</c> protobuf) lies inside the mapped patch for the mesh's
    ///    own material — this is the ground-truth guard that the UVs the game
    ///    receives no longer sample a fake atlas;
    ///  * negative controls prove the private validators reject a UV outside its
    ///    patch, a std above std_max, a pixel rect beyond the texture size, a
    ///    forbidden PaintedMetal large-shell material, a fake-atlas texture name
    ///    and a missing map entry.
    ///
    /// The sha256 check is portability-safe: it is skipped with a console note when
    /// the recorded vanilla texture extraction roots are not present on the machine.
    /// </summary>
    public static class V2MaterialPatchContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string MapRelativePath = "art/material_texture_map.json";
        private const string SlotsRelativePath = "art/material_slots.json";
        private const string CommonPyRelativePath = "art/timbermesh_common.py";
        private const string BoxFarmDataFolder = "Buildings/Food/BoxFarm";

        private const string ExpectedSchema = "hdbf.material_texture_map/v1";
        private const string ExpectedGeneratedBy = "art/material_texture_analysis.py";

        private const int ExpectedShippedModelCount = 16;

        /// <summary>Tolerance the recorded uv_rect may differ from pixel_rect / texture_size.</summary>
        private const double UvRectConsistencyTolerance = 1e-5;

        /// <summary>Tolerance when checking the shipped uv0 vertex data against a patch rect (the
        /// generator keeps a &gt;= 4-texel inset, so real data has a visible margin).</summary>
        private const double MeshUvInsidePatchTolerance = 1e-3;

        private static readonly string[] Tiers = { "Basic", "Intermediate", "Advanced", "Top" };

        private static readonly string[] Factions = { "Folktails", "IronTeeth" };

        /// <summary>Large-shell hazard materials: PaintedMetal carries the yellow/black stripe
        /// texture that caused the user-visible bug, so it must never be used by a shipped model.</summary>
        private static readonly string[] ForbiddenLargeShellMaterials =
        {
            "PaintedMetal.Folktails", "PaintedMetal.IronTeeth",
        };

        /// <summary>Frozen patch-class vocabulary of art/material_texture_map.json (the classes
        /// art/material_texture_analysis.py assigns to its ground-truth material windows).</summary>
        private static readonly string[] KnownPatchClasses =
        {
            "wood", "metal", "glass", "plaster", "details", "moss", "roof_planks", "forged_planks",
        };

        /// <summary>The retired fabricated ART-01 atlas files. No map entry may reference them.</summary>
        private static readonly string[] FakeAtlasTextureFiles =
        {
            "atlas_albedo.png", "atlas_normal.png", "atlas_mask.png",
        };

        private const string ConstructionStageModelKey = "ConstructionStage0";

        /// <summary>
        /// Frozen std budgets per patch class (art/material_texture_analysis.py STD_MAX_BY_CLASS) and
        /// the reviewed global hard cap. A permissive regression (higher budgets/cap) fails L1.
        /// </summary>
        private static readonly Dictionary<string, double> ExpectedStdBudgets = new(StringComparer.Ordinal)
        {
            ["wood"] = 18.0,
            ["metal"] = 14.0,
            ["glass"] = 12.0,
            ["plaster"] = 10.0,
            ["details"] = 26.0,
            ["moss"] = 16.0,
            ["roof_planks"] = 16.0,
            ["forged_planks"] = 27.0,
        };

        private const double ExpectedHardStdcap = 30.0;

        /// <summary>
        /// Expected vanilla material set of every shipped FINISHED model (ground truth for the
        /// ART-05 material re-mapping; no PaintedMetal.*). Pinned against both the shipped mesh
        /// payload and the art/material_slots.json per-model mapping.
        /// </summary>
        private static readonly Dictionary<string, string[]> ExpectedFinishedMaterialSets =
            new(StringComparer.Ordinal)
            {
                ["BoxFarm.Basic.Folktails.Model"] =
                    new[] { "BaseWood_Brown.Folktails", "Moss.Folktails" },
                ["BoxFarm.Basic.IronTeeth.Model"] =
                    new[] { "BaseWood_DarkBrown.IronTeeth", "RoofPlanks.IronTeeth" },
                ["BoxFarm.Intermediate.Folktails.Model"] =
                    new[] { "BaseWood_LightBrown.Folktails", "Plaster_White.Folktails", "WindowsAtlas.Folktails" },
                ["BoxFarm.Intermediate.IronTeeth.Model"] =
                    new[] { "BaseWood_Grey.IronTeeth", "Plaster_Orange.IronTeeth", "WindowsAtlas.IronTeeth" },
                ["BoxFarm.Advanced.Folktails.Model"] =
                    new[] { "BaseWood_White.Folktails", "BaseMetal.Folktails", "WindowsAtlas.Folktails", "Details.Folktails" },
                ["BoxFarm.Advanced.IronTeeth.Model"] =
                    new[] { "IrregularPlanks_MetalForged.IronTeeth", "BaseMetal.IronTeeth", "WindowsAtlas.IronTeeth", "Details.IronTeeth" },
                ["BoxFarm.Top.Folktails.Model"] =
                    new[] { "IrregularPlanks_Brown.Folktails", "Details.Folktails", "WindowsAtlas.Folktails", "BaseMetal.Folktails" },
                ["BoxFarm.Top.IronTeeth.Model"] =
                    new[] { "BaseWood_DarkBrown.IronTeeth", "Details.IronTeeth", "WindowsAtlas.IronTeeth", "IrregularPlanks_MetalForged.IronTeeth" },
            };

        /// <summary>
        /// The ConstructionStage0 source geometry bakes ONE uv0 set that is shared by both factions,
        /// so the shared window must be a region both factions' material canvases can sample. ART-07
        /// (§3 "Construction stage") replaced the old small patch window with the Folktails source
        /// material's ART-07 canvas: BaseWood_Brown.Folktails' curated wood canvas is inside the
        /// RoofPlanks.IronTeeth full-bleed tile texture, and BaseMetal.Folktails / BaseMetal.IronTeeth
        /// share the identical metal canvas. The expected window is therefore derived from the map
        /// (the Folktails canvas), not hard-coded.
        /// group -> (Folktails source material, IronTeeth material).
        /// </summary>
        private static readonly Dictionary<string, (string Folktails, string IronTeeth)>
            SharedConstructionGroups = new(StringComparer.Ordinal)
            {
                ["construction_wood"] = ("BaseWood_Brown.Folktails", "RoofPlanks.IronTeeth"),
                ["construction_metal"] = ("BaseMetal.Folktails", "BaseMetal.IronTeeth"),
            };

        private static readonly Lazy<JsonElement> LazyMapRoot = new(LoadMapRoot);

        private static readonly Lazy<Dictionary<string, PatchRecord>> LazyMapMaterials = new(LoadMapMaterials);

        private static readonly Lazy<JsonElement> LazySlotsRoot = new(LoadSlotsRoot);

        private static readonly Lazy<Dictionary<string, ParsedMesh>> LazyShippedMeshes = new(LoadShippedMeshes);

        private static readonly Lazy<Dictionary<string, HashSet<string>>> LazyWhitelist = new(LoadMaterialWhitelist);

        private static JsonElement MapRoot => LazyMapRoot.Value;

        private static Dictionary<string, PatchRecord> MapMaterials => LazyMapMaterials.Value;

        private static JsonElement SlotsRoot => LazySlotsRoot.Value;

        private static Dictionary<string, ParsedMesh> ShippedMeshes => LazyShippedMeshes.Value;

        private static Dictionary<string, HashSet<string>> MaterialWhitelist => LazyWhitelist.Value;

        // ------------------------------------------------------------ 1. map schema

        /// <summary>
        /// art/material_texture_map.json exists, parses, carries the frozen schema id and every
        /// contract field, and every used-material entry is a consistent patch record: a real
        /// texture filename + size + sha256 + tint, a pixel/UV patch inside that texture, recorded
        /// mean RGB / std, std &lt;= std_max &lt;= hard_std_cap, a patch class from the frozen
        /// vocabulary, a non-empty reason and the reject-tag list. The large_shell_policy must
        /// carry the two PaintedMetal hazard materials. The unused_materials section must record
        /// them as hazard references (with a hazard pixel rect) instead of pretending they are used.
        /// </summary>
        public static void TestMaterialTextureMapExistsAndIsWellFormed()
        {
            var mapPath = RepoPath(MapRelativePath);
            TestAssert.True(File.Exists(mapPath),
                "the per-material texture map is missing: " + mapPath
                + " (the flat-patch system must be replaced by art/material_texture_map.json)");

            var mapRoot = MapRoot;
            const string context = "art/material_texture_map.json";

            TestAssert.Equal(ExpectedSchema, StringMember(mapRoot, "schema", context),
                context + " schema id (a regression to the old fake flat-patch atlas must fail L1)");
            TestAssert.Equal(ExpectedGeneratedBy, StringMember(mapRoot, "generated_by", context),
                context + " generated_by");
            TestAssert.True(StringMember(mapRoot, "material_source_provenance", context).Trim().Length > 0,
                context + ": material_source_provenance must explain where the real material/tint data came from");
            TestAssert.True(StringMember(mapRoot, "uv_convention", context).Trim().Length > 0,
                context + ": uv_convention must document the pixel-rect -> uv-rect convention");

            var textureRoots = Member(mapRoot, "texture_roots", context);
            var rootNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in ObjectEntries(textureRoots, context + " texture_roots"))
            {
                TestAssert.True(root.Name.Length > 0, context + ": texture_roots contains an empty root name");
                var rootPath = StringValue(root.Value, context + " texture_roots[\"" + root.Name + "\"]");
                TestAssert.True(rootPath.Trim().Length > 0,
                    context + ": texture_roots[\"" + root.Name + "\"] must record the extraction path");
                rootNames.Add(root.Name);
            }

            TestAssert.True(rootNames.Contains("vanilla-tex-all") && rootNames.Contains("vanilla-tex-extra"),
                context + ": texture_roots must record both \"vanilla-tex-all\" and \"vanilla-tex-extra\"; found "
                + string.Join(", ", rootNames.OrderBy(name => name, StringComparer.Ordinal)));

            var thresholds = Member(mapRoot, "thresholds", context);
            var stdMaxDefault = NumberMember(thresholds, "std_max_default", context + " thresholds");
            TestAssert.True(stdMaxDefault > 0.0, context + ": thresholds.std_max_default must be > 0");
            var byClass = Member(thresholds, "std_max_by_class", context + " thresholds");
            var classBudgets = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in ObjectEntries(byClass, context + " thresholds.std_max_by_class"))
            {
                var classMax = NumberValue(entry.Value, context + " thresholds.std_max_by_class[\"" + entry.Name + "\"]");
                TestAssert.True(classMax > 0.0,
                    context + ": thresholds.std_max_by_class[\"" + entry.Name + "\"] must be > 0");
                classBudgets[entry.Name] = classMax;
            }

            TestAssert.Equal(ExpectedStdBudgets.Count, classBudgets.Count,
                context + ": thresholds.std_max_by_class must carry the reviewed class budgets ("
                + string.Join(", ", ExpectedStdBudgets.Keys.OrderBy(name => name, StringComparer.Ordinal)) + ")");
            foreach (var budget in ExpectedStdBudgets)
            {
                TestAssert.True(classBudgets.ContainsKey(budget.Key),
                    context + ": thresholds.std_max_by_class is missing the reviewed \"" + budget.Key + "\" budget");
                TestAssert.Equal(budget.Value, classBudgets[budget.Key], 1e-9,
                    context + ": std budget for patch class \"" + budget.Key
                    + "\" must stay at the reviewed value (a looser budget would let hazard/banner windows pass)");
            }

            var hardStdCap = NumberMember(thresholds, "hard_std_cap", context + " thresholds");
            // The reviewed hard cap in art/material_texture_analysis.py is 30.0 (hazard/banner
            // windows score ~45..60 and must stay far outside it); a permissive regression fails.
            TestAssert.Equal(ExpectedHardStdcap, hardStdCap, 1e-9,
                context + ": thresholds.hard_std_cap must stay at the reviewed 30.0 cap");
            TestAssert.True(stdMaxDefault <= hardStdCap,
                context + ": thresholds.std_max_default (" + stdMaxDefault + ") must not exceed hard_std_cap (" + hardStdCap + ")");

            var policy = Member(mapRoot, "large_shell_policy", context);
            var forbidden = ReadForbiddenMaterials(mapRoot);
            foreach (var hazard in ForbiddenLargeShellMaterials)
            {
                TestAssert.True(forbidden.Contains(hazard, StringComparer.Ordinal),
                    context + ": large_shell_policy.forbidden_materials must contain the hazard material \""
                    + hazard + "\" (the yellow/black stripe regression guard)");
            }

            TestAssert.True(StringMember(policy, "reason", context + " large_shell_policy").Trim().Length > 0,
                context + ": large_shell_policy.reason must be non-empty");

            var materials = MapMaterials;
            TestAssert.True(materials.Count >= 8,
                context + ": expected a materials entry for every used vanilla material, found only "
                + materials.Count);

            foreach (var pair in materials.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var entryContext = context + " materials[\"" + pair.Key + "\"]";
                var record = pair.Value;
                if (!IsPatchAcceptable(record, out var error))
                {
                    TestAssert.True(false, entryContext + ": " + error);
                }

                TestAssert.True(rootNames.Contains(record.TextureSource),
                    entryContext + ": texture_source \"" + record.TextureSource
                    + "\" is not a key of texture_roots ("
                    + string.Join(", ", rootNames.OrderBy(name => name, StringComparer.Ordinal)) + ")");
                TestAssert.True(record.StdMax <= hardStdCap,
                    entryContext + ": patch.std_max (" + record.StdMax + ") exceeds thresholds.hard_std_cap ("
                    + hardStdCap + ")");
                TestAssert.True(classBudgets.ContainsKey(record.PatchClass),
                    entryContext + ": patch_class \"" + record.PatchClass
                    + "\" has no std_max_by_class budget (classes: "
                    + string.Join(", ", classBudgets.Keys.OrderBy(name => name, StringComparer.Ordinal)) + ")");
                // ART-07 §4: for PATCH materials the legacy patch record is the large curated canvas,
                // whose std budget is the ART-05 hard cap (the canvas deliberately contains plank
                // seams/wear); for TILE materials it stays the small ART-05 probe window and keeps
                // the reviewed per-class budget.
                var expectedStdMax = ExpectedPatchStdMax(record, classBudgets, hardStdCap);
                TestAssert.Equal(expectedStdMax, record.StdMax, 1e-6,
                    entryContext + ": patch.std_max must be the ART-07 canvas budget (" + hardStdCap
                    + ") for patch-mode canvases and min(class budget, hard_std_cap) for the tile probe window");
            }

            var sharedPatches = Member(mapRoot, "construction_shared_patches", context);
            // The map additionally records one patch record per REAL texture (texture_patches): every
            // texture used by a material entry must be described there, with a consistent pixel/UV
            // window and statistics under its class budget.
            var texturePatches = Member(mapRoot, "texture_patches", context);
            var texturePatchEntries = ObjectEntries(texturePatches, context + " texture_patches").ToList();
            TestAssert.True(texturePatchEntries.Count >= 1, context + ": texture_patches must not be empty");
            foreach (var materialPair in materials.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                TestAssert.True(texturePatchEntries.Any(entry =>
                        string.Equals(entry.Name, materialPair.Value.Texture, StringComparison.Ordinal)),
                    context + " materials[\"" + materialPair.Key + "\"]: texture \""
                    + materialPair.Value.Texture + "\" has no texture_patches record");
            }

            foreach (var property in texturePatchEntries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                var textureContext = context + " texture_patches[\"" + property.Name + "\"]";
                var entry = property.Value;
                TestAssert.Equal(property.Name, StringMember(entry, "texture", textureContext),
                    textureContext + " texture");
                TestAssert.True(IsBarePngFileName(property.Name),
                    textureContext + ": texture_patches keys must be bare *.png filenames");
                var textureSize = IntArray(Member(entry, "texture_size", textureContext), 2,
                    textureContext + " texture_size");
                TestAssert.True(textureSize[0] >= 128 && textureSize[1] >= 128,
                    textureContext + ": texture_size " + textureSize[0] + "x" + textureSize[1]
                    + " must be a real texture (>=128x128)");
                TestAssert.True(IsHexSha256(StringMember(entry, "texture_sha256", textureContext)),
                    textureContext + ": texture_sha256 must be a 64-character hex digest");
                TestAssert.True(rootNames.Contains(StringMember(entry, "texture_source", textureContext)),
                    textureContext + ": texture_source must be a key of texture_roots");
                var patchClass = StringMember(entry, "patch_class", textureContext);
                TestAssert.True(KnownPatchClasses.Contains(patchClass, StringComparer.Ordinal),
                    textureContext + ": patch_class \"" + patchClass + "\" is not in the frozen vocabulary");

                var texturePatch = Member(entry, "patch", textureContext);
                var pixelRect = IntArray(Member(texturePatch, "pixel_rect", textureContext), 4,
                    textureContext + " patch.pixel_rect");
                TestAssert.True(pixelRect[0] >= 0 && pixelRect[1] >= 0
                    && pixelRect[2] > pixelRect[0] && pixelRect[3] > pixelRect[1]
                    && pixelRect[2] <= textureSize[0] && pixelRect[3] <= textureSize[1],
                    textureContext + ": patch pixel_rect must be a non-empty rect inside texture_size");
                var patchSize = IntArray(Member(texturePatch, "size_px", textureContext), 2,
                    textureContext + " patch.size_px");
                TestAssert.Equal(pixelRect[2] - pixelRect[0], patchSize[0],
                    textureContext + " patch.size_px width");
                TestAssert.Equal(pixelRect[3] - pixelRect[1], patchSize[1],
                    textureContext + " patch.size_px height");
                var patchUv = DoubleArray(Member(texturePatch, "uv_rect", textureContext), 4,
                    textureContext + " patch.uv_rect");
                TestAssert.True(0.0 <= patchUv[0] && patchUv[0] < patchUv[2] && patchUv[2] <= 1.0
                    && 0.0 <= patchUv[1] && patchUv[1] < patchUv[3] && patchUv[3] <= 1.0,
                    textureContext + ": patch uv_rect must lie inside [0,1] and be non-degenerate");
                var expectedUv = new[]
                {
                    pixelRect[0] / (double)textureSize[0],
                    1.0 - pixelRect[3] / (double)textureSize[1],
                    pixelRect[2] / (double)textureSize[0],
                    1.0 - pixelRect[1] / (double)textureSize[1],
                };
                TestAssert.True(UvRectsEqual(patchUv, expectedUv, UvRectConsistencyTolerance),
                    textureContext + ": patch uv_rect does not match pixel_rect / texture_size under the "
                    + "frozen flipped-v convention");
                var patchStd = NumberMember(texturePatch, "std", textureContext + " patch");
                var patchStdMax = NumberMember(texturePatch, "std_max", textureContext + " patch");
                TestAssert.True(patchStd >= 0.0 && patchStdMax > 0.0 && patchStd <= patchStdMax,
                    textureContext + ": patch std must be >= 0 and <= std_max");
                var textureOwner = FindTextureOwner(materials, property.Name);
                TestAssert.NotNull(textureOwner,
                    textureContext + ": no used material samples this texture, so its patch record cannot "
                    + "be identified as probe window (tile) or ART-07 canvas (patch)");
                TestAssert.Equal(ExpectedPatchStdMax(textureOwner!, classBudgets, hardStdCap),
                    patchStdMax, 1e-6,
                    textureContext + ": patch.std_max must be the ART-07 canvas budget (" + hardStdCap
                    + ") for a patch-mode canvas and min(class budget, hard_std_cap) for a tile probe window");
                var patchMean = DoubleArray(Member(texturePatch, "mean_rgb", textureContext), 3,
                    textureContext + " patch.mean_rgb");
                TestAssert.True(patchMean.All(value => value >= 0.0 && value <= 255.0),
                    textureContext + ": patch.mean_rgb must be recorded values in [0,255]");
                var patchByChannel = DoubleArray(Member(texturePatch, "std_by_channel", textureContext), 3,
                    textureContext + " patch.std_by_channel");
                TestAssert.True(patchByChannel.All(value => value >= 0.0),
                    textureContext + ": patch.std_by_channel values must be >= 0");
                TestAssert.True(StringMember(texturePatch, "reason", textureContext + " patch").Trim().Length > 0,
                    textureContext + ": patch.reason must be non-empty");
                _ = StringArray(Member(texturePatch, "reject_tags", textureContext),
                    textureContext + " patch.reject_tags", requireNonEmpty: false);

                // A texture shared by the two construction factions must carry the joint UV window.
                // ART-07 §3: the window is the Folktails source material's canvas; a patch-mode
                // texture records that canvas as its own patch window, while a full-bleed tile
                // texture (e.g. RoofPlanks) keeps its small ART-05 probe record and only has to
                // CONTAIN the shared window.
                if (entry.TryGetProperty("shared_uv_group", out var sharedGroupElement)
                    && sharedGroupElement.ValueKind == JsonValueKind.String)
                {
                    var sharedGroupName = sharedGroupElement.GetString()!;
                    if (sharedGroupName.Length > 0)
                    {
                        TestAssert.True(SharedConstructionGroups.ContainsKey(sharedGroupName),
                            textureContext + ": unknown shared_uv_group \"" + sharedGroupName + "\"");
                        var sharedGroup = Member(sharedPatches, sharedGroupName,
                            context + " construction_shared_patches");
                        var groupUv = DoubleArray(Member(sharedGroup, "uv_rect",
                            context + " construction_shared_patches[\"" + sharedGroupName + "\"]"), 4,
                            context + " construction_shared_patches[\"" + sharedGroupName + "\"] uv_rect");
                        var sourceMaterial = SharedConstructionGroups[sharedGroupName].Folktails;
                        TestAssert.True(materials.ContainsKey(sourceMaterial),
                            textureContext + ": missing the map entry of the Folktails construction "
                            + "source material \"" + sourceMaterial + "\"");
                        TestAssert.True(UvRectsEqual(groupUv, materials[sourceMaterial].UvCanvasUvRect, 1e-9),
                            textureContext + ": the shared construction window must be the ART-07 canvas of "
                            + "the Folktails source material \"" + sourceMaterial + "\" (ART-07 §3)");
                        var sharedTextureOwner = FindTextureOwner(materials, property.Name);
                        TestAssert.NotNull(sharedTextureOwner,
                            textureContext + ": no used material samples this shared texture");
                        if (string.Equals(sharedTextureOwner!.Mode, "patch", StringComparison.Ordinal))
                        {
                            TestAssert.True(UvRectsEqual(patchUv, groupUv, 1e-9),
                                textureContext + ": a patch-mode shared texture's patch window must equal "
                                + "the shared construction window");
                        }

                        TestAssert.True(UvRectInsideOrEqual(sharedTextureOwner.UvCanvasUvRect, groupUv, 1e-9),
                            textureContext + ": the shared construction window must lie inside the ART-07 "
                            + "canvas of \"" + sharedTextureOwner.Material + "\" so the one baked uv0 set "
                            + "samples this faction's texture correctly");
                    }
                }
            }

            var unused = ReadUnusedMaterials(mapRoot);
            TestAssert.True(unused.Count > 0,
                context + ": unused_materials must record the whitelisted-but-unused materials (e.g. the PaintedMetal hazards)");
            foreach (var pair in unused.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var unusedContext = context + " unused_materials[\"" + pair.Key + "\"]";
                TestAssert.True(IsFactionScopedMaterialName(pair.Key),
                    unusedContext + ": unused material names must be faction-scoped (.Folktails/.IronTeeth)");
                var texture = StringMember(pair.Value, "texture", unusedContext);
                TestAssert.True(IsBarePngFileName(texture),
                    unusedContext + ": texture must be a bare *.png filename, got \"" + texture + "\"");
                TestAssert.True(StringMember(pair.Value, "reason", unusedContext).Trim().Length > 0,
                    unusedContext + ": reason must be non-empty");
                TestAssert.True(StringMember(pair.Value, "policy", unusedContext).Trim().Length > 0,
                    unusedContext + ": policy must be non-empty");
            }

            foreach (var hazard in ForbiddenLargeShellMaterials)
            {
                TestAssert.True(unused.ContainsKey(hazard),
                    context + ": unused_materials must record \"" + hazard
                    + "\" as a hazard reference (PaintedMetal is whitelisted but must never be used)");
                var hazardContext = context + " unused_materials[\"" + hazard + "\"]";
                var hazardEntry = unused[hazard];
                var hazardRect = ReadPixelRect(Member(hazardEntry, "hazard_reference_pixel_rect", hazardContext),
                    hazardContext + " hazard_reference_pixel_rect");
                TestAssert.True(hazardRect[0] >= 0 && hazardRect[1] >= 0
                    && hazardRect[2] > hazardRect[0] && hazardRect[3] > hazardRect[1],
                    hazardContext + ": hazard_reference_pixel_rect must be a valid non-empty pixel rect (x0<x1, y0<y1)");

                // The recorded hazard window must be provably too varied for the frozen cap: that is
                // what makes PaintedMetal unusable on a large shell (yellow/black stripe regression).
                var hazardTextureSize = IntArray(Member(hazardEntry, "texture_size", hazardContext), 2,
                    hazardContext + " texture_size");
                TestAssert.True(hazardTextureSize[0] >= 128 && hazardTextureSize[1] >= 128,
                    hazardContext + ": texture_size must be a real texture size");
                var hazardUv = DoubleArray(Member(hazardEntry, "hazard_reference_uv_rect", hazardContext), 4,
                    hazardContext + " hazard_reference_uv_rect");
                var expectedHazardUv = new[]
                {
                    hazardRect[0] / (double)hazardTextureSize[0],
                    1.0 - hazardRect[3] / (double)hazardTextureSize[1],
                    hazardRect[2] / (double)hazardTextureSize[0],
                    1.0 - hazardRect[1] / (double)hazardTextureSize[1],
                };
                TestAssert.True(UvRectsEqual(hazardUv, expectedHazardUv, UvRectConsistencyTolerance),
                    hazardContext + ": hazard_reference_uv_rect must match hazard_reference_pixel_rect / "
                    + "texture_size under the frozen flipped-v convention");

                var hazardStats = Member(hazardEntry, "hazard_reference_stats", hazardContext);
                var hazardStd = NumberMember(hazardStats, "std", hazardContext + " hazard_reference_stats");
                TestAssert.True(hazardStd > hardStdCap,
                    hazardContext + ": the recorded hazard window std (" + hazardStd
                    + ") must exceed thresholds.hard_std_cap (" + hardStdCap
                    + "), i.e. the warning-stripe texture is provably rejected by the patch threshold");
                var hazardMean = DoubleArray(Member(hazardStats, "mean_rgb", hazardContext + " hazard_reference_stats"),
                    3, hazardContext + " hazard_reference_stats.mean_rgb");
                TestAssert.True(hazardMean.All(value => value >= 0.0 && value <= 255.0),
                    hazardContext + ": hazard_reference_stats.mean_rgb must be recorded values in [0,255]");
                var hazardByChannel = DoubleArray(
                    Member(hazardStats, "std_by_channel", hazardContext + " hazard_reference_stats"), 3,
                    hazardContext + " hazard_reference_stats.std_by_channel");
                TestAssert.True(hazardByChannel.All(value => value >= 0.0),
                    hazardContext + ": hazard_reference_stats.std_by_channel values must be >= 0");
            }

            // The shared ConstructionStage0 geometry bakes ONE uv0 set for both factions. ART-07 §3:
            // the shared window is the ART-07 canvas of the Folktails source material (e.g.
            // BaseWood_Brown.Folktails), which both factions' material canvases can sample.
            var sharedKeys = ObjectEntries(sharedPatches, context + " construction_shared_patches")
                .Select(property => property.Name)
                .ToList();
            foreach (var groupName in SharedConstructionGroups.Keys)
            {
                TestAssert.True(sharedKeys.Contains(groupName, StringComparer.Ordinal),
                    context + ": construction_shared_patches must record the joint UV group \"" + groupName
                    + "\"; found " + string.Join(", ", sharedKeys));

                var groupContext = context + " construction_shared_patches[\"" + groupName + "\"]";
                var group = Member(sharedPatches, groupName, context + " construction_shared_patches");
                TestAssert.Equal(groupName, StringMember(group, "group", groupContext),
                    groupContext + " group id");

                var sourceMaterial = SharedConstructionGroups[groupName].Folktails;
                TestAssert.True(materials.ContainsKey(sourceMaterial),
                    groupContext + ": missing the map entry of the Folktails construction source material \""
                    + sourceMaterial + "\"");

                var textures = StringArray(Member(group, "textures", groupContext),
                    groupContext + " textures", requireNonEmpty: true);
                TestAssert.True(textures.Length >= 2,
                    groupContext + ": a shared construction group must cover both factions' textures");
                TestAssert.Equal(textures.Length, textures.Distinct(StringComparer.Ordinal).Count(),
                    groupContext + ": textures must be distinct");
                foreach (var texture in textures)
                {
                    TestAssert.True(IsBarePngFileName(texture),
                        groupContext + ": texture \"" + texture + "\" must be a bare *.png filename");
                }

                var sharedUv = DoubleArray(Member(group, "uv_rect", groupContext), 4, groupContext + " uv_rect");
                TestAssert.True(0.0 <= sharedUv[0] && sharedUv[0] < sharedUv[2] && sharedUv[2] <= 1.0
                    && 0.0 <= sharedUv[1] && sharedUv[1] < sharedUv[3] && sharedUv[3] <= 1.0,
                    groupContext + ": shared uv_rect must lie inside [0,1] and be non-degenerate");
                TestAssert.True(UvRectsEqual(sharedUv, materials[sourceMaterial].UvCanvasUvRect, 1e-9),
                    groupContext + ": the shared uv_rect must be the ART-07 canvas of the Folktails source "
                    + "material \"" + sourceMaterial + "\"; found [" + string.Join(", ", sharedUv)
                    + "] vs canvas [" + string.Join(", ", materials[sourceMaterial].UvCanvasUvRect) + "]");

                var sharedPixel = IntArray(Member(group, "pixel_rect", groupContext), 4,
                    groupContext + " pixel_rect");
                TestAssert.True(sharedPixel[0] >= 0 && sharedPixel[1] >= 0
                    && sharedPixel[2] > sharedPixel[0] && sharedPixel[3] > sharedPixel[1],
                    groupContext + ": pixel_rect must be a valid non-empty pixel rect");
                TestAssert.Equal(string.Join(",", sharedPixel),
                    string.Join(",", materials[sourceMaterial].UvCanvasPixelRect),
                    groupContext + ": the shared pixel_rect must be the ART-07 canvas pixel rect of \""
                    + sourceMaterial + "\"");
                var sharedSize = IntArray(Member(group, "size_px", groupContext), 2, groupContext + " size_px");
                TestAssert.Equal(sharedPixel[2] - sharedPixel[0], sharedSize[0], groupContext + " size_px width");
                TestAssert.Equal(sharedPixel[3] - sharedPixel[1], sharedSize[1], groupContext + " size_px height");
                TestAssert.True(StringMember(group, "reason", groupContext).Trim().Length > 0,
                    groupContext + ": reason must explain the joint sampling across factions");

                var textureUvRects = Member(group, "texture_uv_rect", groupContext);
                var perTexture = Member(group, "per_texture", groupContext);
                foreach (var texture in textures)
                {
                    var textureContext = groupContext + " textures[\"" + texture + "\"]";
                    var textureUv = DoubleArray(Member(textureUvRects, texture, groupContext + " texture_uv_rect"),
                        4, textureContext + " texture_uv_rect");
                    TestAssert.True(UvRectsEqual(textureUv, sharedUv, 1e-9),
                        textureContext + ": the per-texture uv_rect must be the IDENTICAL shared uv_rect "
                        + "(one baked uv0 set is used for both factions)");

                    var owner = materials.Values.FirstOrDefault(record =>
                        string.Equals(record.Texture, texture, StringComparison.Ordinal));
                    TestAssert.NotNull(owner,
                        groupContext + ": shared texture \"" + texture
                        + "\" has no per-material record to derive its real texture size");
                    var expectedUv = new[]
                    {
                        sharedPixel[0] / (double)owner!.Width,
                        1.0 - sharedPixel[3] / (double)owner.Height,
                        sharedPixel[2] / (double)owner.Width,
                        1.0 - sharedPixel[1] / (double)owner.Height,
                    };
                    TestAssert.True(UvRectsEqual(textureUv, expectedUv, UvRectConsistencyTolerance),
                        textureContext + ": texture_uv_rect does not match pixel_rect / texture_size under "
                        + "the frozen flipped-v convention");

                    var statsContext = groupContext + " per_texture[\"" + texture + "\"]";
                    var stats = Member(perTexture, texture, groupContext + " per_texture");
                    var std = NumberMember(stats, "std", statsContext);
                    var stdMax = NumberMember(stats, "std_max", statsContext);
                    TestAssert.True(std >= 0.0 && stdMax > 0.0 && std <= stdMax,
                        statsContext + ": recorded std must be >= 0 and <= std_max");
                    var stdByChannel = DoubleArray(Member(stats, "std_by_channel", statsContext), 3,
                        statsContext + " std_by_channel");
                    TestAssert.True(stdByChannel.All(value => value >= 0.0),
                        statsContext + ": std_by_channel values must be >= 0");
                    var meanRgb = DoubleArray(Member(stats, "mean_rgb", statsContext), 3,
                        statsContext + " mean_rgb");
                    TestAssert.True(meanRgb.All(value => value >= 0.0 && value <= 255.0),
                        statsContext + ": mean_rgb values must be in [0,255]");
                }
            }

            foreach (var pair in SharedConstructionGroups)
            {
                AssertSharedConstructionUvPair(materials, pair.Value.Folktails, pair.Value.IronTeeth, pair.Key);
            }

            Console.WriteLine("  material_texture_map: " + materials.Count + " used + " + unused.Count
                + " unused materials, " + rootNames.Count + " texture roots, "
                + SharedConstructionGroups.Count + " shared construction UV group(s)");
        }

        // ------------------------------------------------------------ 2. per-model coverage

        /// <summary>
        /// Every vanilla material actually named by any of the 16 shipped <c>.timbermesh</c> mesh
        /// payloads must have an entry in art/material_texture_map.json (ground truth from the
        /// shipped bytes). Cross-checked against the regenerated art/material_slots.json: each
        /// model group must carry the new per-model "material_texture_patches" slot -> vanilla
        /// material mapping (a missing/hazard material there fails), the finished models' mappings
        /// must cover exactly the materials their shipped mesh uses, the new "material_texture_map"
        /// summary must be present, and the retired fake-2048 "atlas" block must be gone.
        /// Every material name recorded in the map must be faction-scoped.
        /// </summary>
        public static void TestEveryShippedModelMaterialHasATexturePatch()
        {
            var map = MapMaterials;
            foreach (var pair in map.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                TestAssert.True(IsFactionScopedMaterialName(pair.Key),
                    "art/material_texture_map.json materials key \"" + pair.Key
                    + "\" must be a faction-scoped vanilla material name ending in .Folktails/.IronTeeth");
            }

            // (a) ground truth: every material used by a shipped mesh has a map entry.
            foreach (var stem in ExpectedShippedStems())
            {
                var mesh = ShippedMeshes[stem];
                TestAssert.True(mesh.Materials.Count >= 1,
                    stem + ": shipped mesh names no material at all (cannot be lit in game)");
                foreach (var material in mesh.Materials)
                {
                    TestAssert.True(HasMaterialEntry(map, material),
                        stem + ": uses vanilla material \"" + material
                        + "\" but art/material_texture_map.json has no texture patch entry for it "
                        + "(a missing map entry must fail L1)");
                }
            }

            // (b) art/material_slots.json must now carry the map summary and per-model mappings,
            //     and must no longer carry the old fake 2048 atlas block.
            var slots = SlotsRoot;
            const string slotsContext = "art/material_slots.json";
            var summary = Member(slots, "material_texture_map", slotsContext);
            TestAssert.True(summary.ValueKind == JsonValueKind.Object && summary.EnumerateObject().Any(),
                slotsContext + ": the new \"material_texture_map\" summary object is required "
                + "(it carries the map schema/thresholds/per-material patch data used by the shipped models)");

            TestAssert.False(slots.TryGetProperty("atlas", out _),
                slotsContext + ": the old fake-2048 \"atlas\" block must be removed; the shipped models now "
                + "sample their own per-material textures, not a fabricated ART-01 atlas");

            var slotsRaw = slots.GetRawText();
            foreach (var fake in FakeAtlasTextureFiles)
            {
                TestAssert.False(slotsRaw.Contains(fake, StringComparison.Ordinal),
                    slotsContext + ": must not reference the retired fake atlas texture \"" + fake + "\"");
            }

            var slotsMaterialPatches = Member(slots, "material_texture_patches", slotsContext);
            TestAssert.True(slotsMaterialPatches.ValueKind == JsonValueKind.Array
                && slotsMaterialPatches.GetArrayLength() >= 8,
                slotsContext + ": the top-level \"material_texture_patches\" summary array must list the "
                + "used shipped materials; found " + slotsMaterialPatches.ValueKind + " with "
                + (slotsMaterialPatches.ValueKind == JsonValueKind.Array
                    ? slotsMaterialPatches.GetArrayLength()
                    : 0) + " entries");
            foreach (var patchSummary in slotsMaterialPatches.EnumerateArray())
            {
                var summaryMaterial = StringMember(patchSummary, "material",
                    slotsContext + " material_texture_patches[]");
                var summaryContext = slotsContext + " material_texture_patches[\"" + summaryMaterial + "\"]";
                TestAssert.True(HasMaterialEntry(map, summaryMaterial),
                    summaryContext + ": summary material \"" + summaryMaterial
                    + "\" has no art/material_texture_map.json entry");
                var summaryRecord = map[summaryMaterial];
                TestAssert.Equal(summaryRecord.Texture, StringMember(patchSummary, "texture", summaryContext),
                    summaryContext + ": texture must match the material_texture_map entry for \""
                    + summaryMaterial + "\"");
                var summaryUv = DoubleArray(Member(patchSummary, "uv_rect", summaryContext), 4,
                    summaryContext + " uv_rect");
                TestAssert.True(UvRectsEqual(summaryUv, summaryRecord.UvRect, 1e-9),
                    summaryContext + ": uv_rect must match the material_texture_map patch window for \""
                    + summaryMaterial + "\"");
                TestAssert.Equal(summaryRecord.PatchClass,
                    StringMember(patchSummary, "patch_class", summaryContext),
                    summaryContext + ": patch_class must match the material_texture_map entry");
            }

            var expectedConstructionMaterials = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Folktails"] = new[] { "BaseMetal.Folktails", "BaseWood_Brown.Folktails" },
                ["IronTeeth"] = new[] { "BaseMetal.IronTeeth", "RoofPlanks.IronTeeth" },
            };
            var slotsFactionMaterials = Member(slots, "construction_faction_materials", slotsContext);
            TestAssert.True(slotsFactionMaterials.ValueKind == JsonValueKind.Object,
                slotsContext + ": \"construction_faction_materials\" must be an object");
            foreach (var faction in Factions)
            {
                var factionMap = ReadSlotMaterialMap(
                    Member(slotsFactionMaterials, faction, slotsContext + " construction_faction_materials"),
                    slotsContext + " construction_faction_materials[\"" + faction + "\"]");
                TestAssert.Equal(SortedJoin(expectedConstructionMaterials[faction]), SortedJoin(factionMap.Values),
                    slotsContext + " construction_faction_materials[\"" + faction
                    + "\"] must carry the frozen construction material mapping");
            }

            var models = Member(slots, "models", slotsContext);
            foreach (var modelKey in ModelGroupKeys())
            {
                var groupContext = slotsContext + " models[\"" + modelKey + "\"]";
                var group = Member(models, modelKey, slotsContext + " models");
                TestAssert.True(group.ValueKind == JsonValueKind.Object,
                    groupContext + ": expected a model object, got " + group.ValueKind);

                var patchesElement = Member(group, "material_texture_patches", groupContext);
                var mapped = ReadSlotMaterialMap(patchesElement, groupContext + " material_texture_patches");
                TestAssert.True(mapped.Count >= 1 && mapped.Count <= 4,
                    groupContext + ": material_texture_patches must map 1..4 material slots, found "
                    + mapped.Count);

                var faction = modelKey == ConstructionStageModelKey ? null : FactionOfModelGroupKey(modelKey);
                foreach (var mapping in mapped.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    TestAssert.True(mapping.Key.StartsWith("M_HDBF_", StringComparison.Ordinal),
                        groupContext + ": material_texture_patches keys must be the M_HDBF_* slot names, got \""
                        + mapping.Key + "\"");
                    TestAssert.True(IsFactionScopedMaterialName(mapping.Value),
                        groupContext + " slot \"" + mapping.Key + "\": mapped material \"" + mapping.Value
                        + "\" must be faction-scoped");
                    if (faction is not null)
                    {
                        TestAssert.True(mapping.Value.EndsWith("." + faction, StringComparison.Ordinal),
                            groupContext + " slot \"" + mapping.Key + "\": mapped material \"" + mapping.Value
                            + "\" must belong to the model faction " + faction);
                    }

                    TestAssert.True(HasMaterialEntry(map, mapping.Value),
                        groupContext + " slot \"" + mapping.Key + "\": mapped material \"" + mapping.Value
                        + "\" has no entry in art/material_texture_map.json");
                }

                if (modelKey == ConstructionStageModelKey)
                {
                    var groupFactionMaterials = Member(group, "construction_faction_materials", groupContext);
                    TestAssert.True(groupFactionMaterials.ValueKind == JsonValueKind.Object,
                        groupContext + ": ConstructionStage0 must record construction_faction_materials");
                    foreach (var constructionFaction in Factions)
                    {
                        var factionMap = ReadSlotMaterialMap(
                            Member(groupFactionMaterials, constructionFaction,
                                groupContext + " construction_faction_materials"),
                            groupContext + " construction_faction_materials[\"" + constructionFaction + "\"]");
                        TestAssert.Equal(SortedJoin(expectedConstructionMaterials[constructionFaction]),
                            SortedJoin(factionMap.Values),
                            groupContext + " construction_faction_materials[\"" + constructionFaction
                            + "\"] must carry the frozen construction material mapping");
                    }
                }
                else
                {
                    // The shipped finished mesh and the per-model mapping are both pinned to the frozen
                    // ART-05 material set (a re-mapping or a PaintedMetal regression fails here).
                    var stem = "BoxFarm." + modelKey + ".Model";
                    var expectedMaterials = ExpectedFinishedMaterialSets[stem];
                    var meshMaterials = ShippedMeshes[stem].Materials;
                    TestAssert.Equal(SortedJoin(expectedMaterials), SortedJoin(meshMaterials),
                        stem + ": shipped finished material set changed from the frozen ART-05 mapping");
                    TestAssert.Equal(SortedJoin(expectedMaterials), SortedJoin(mapped.Values),
                        groupContext + " material_texture_patches values must equal the frozen shipped "
                        + "finished material set");
                }
            }
        }

        // ------------------------------------------------------------ 3. hazard ban

        /// <summary>
        /// Direct regression guard for the user-visible yellow/black hazard stripes: none of the
        /// 16 shipped models may use any large_shell_policy.forbidden_materials entry
        /// (<c>PaintedMetal.*</c>), the map's used-material section must not list them, the retired
        /// MATERIAL_MAP in art/timbermesh_common.py must no longer target PaintedMetal, Advanced/Top
        /// large shells must map their metal slot onto BaseMetal.*, and the ConstructionStage0 models
        /// must use exactly base wood + base metal (never a painted/metal-forged hazard material).
        /// </summary>
        public static void TestNoHazardStripeMaterialIsUsedOnLargeShells()
        {
            var mapRoot = MapRoot;
            var forbidden = ReadForbiddenMaterials(mapRoot);
            foreach (var hazard in ForbiddenLargeShellMaterials)
            {
                TestAssert.True(forbidden.Contains(hazard, StringComparer.Ordinal),
                    "art/material_texture_map.json large_shell_policy.forbidden_materials must contain \""
                    + hazard + "\" (the yellow/black hazard stripe material)");
            }

            var map = MapMaterials;

            // (a) ground truth: no shipped mesh may name a forbidden material.
            foreach (var stem in ExpectedShippedStems())
            {
                var mesh = ShippedMeshes[stem];
                foreach (var material in mesh.Materials)
                {
                    TestAssert.True(IsMaterialAllowed(material, forbidden),
                        stem + ": uses the forbidden large-shell hazard material \"" + material
                        + "\" (PaintedMetal carries the yellow/black stripe texture reproduced by the "
                        + "checkerboard/hazard-stripe bug)");
                }
            }

            // (b) the map's used-material section must not record PaintedMetal as used.
            foreach (var hazard in ForbiddenLargeShellMaterials)
            {
                TestAssert.False(map.ContainsKey(hazard),
                    "art/material_texture_map.json \"materials\" records \"" + hazard
                    + "\" as a used material; PaintedMetal must live in \"unused_materials\" only");
            }

            // (c) the shared generator mapping must not target PaintedMetal any more.
            var materialMapSource = MaterialMapSourceText();
            TestAssert.False(materialMapSource.Contains("\"PaintedMetal.", StringComparison.Ordinal),
                "art/timbermesh_common.py MATERIAL_MAP still maps a material slot onto a PaintedMetal.* "
                + "hazard material; Advanced/Top must map onto BaseMetal.*/Details.* instead (this is the "
                + "yellow/black stripe regression)");

            // (d) positive control: the re-mapped Advanced/Top large shells must carry a hazard-free
            //     replacement family on their metal slot (ART-05: BaseMetal / Details, never
            //     PaintedMetal, which is the yellow/black warning-stripe texture).
            var models = Member(SlotsRoot, "models", "art/material_slots.json");
            foreach (var tier in new[] { "Advanced", "Top" })
            {
                foreach (var faction in Factions)
                {
                    var modelKey = tier + "." + faction;
                    var groupContext = "art/material_slots.json models[\"" + modelKey + "\"]";
                    var group = Member(models, modelKey, "art/material_slots.json models");
                    var patchesElement = Member(group, "material_texture_patches", groupContext);
                    var mapped = ReadSlotMaterialMap(patchesElement, groupContext + " material_texture_patches");
                    TestAssert.True(mapped.ContainsKey("M_HDBF_Metal"),
                        groupContext + ": the large-shell metal slot \"M_HDBF_Metal\" must be mapped in "
                        + "material_texture_patches");
                    var metalMaterial = mapped["M_HDBF_Metal"];
                    TestAssert.True(metalMaterial.StartsWith("BaseMetal.", StringComparison.Ordinal)
                        || metalMaterial.StartsWith("Details.", StringComparison.Ordinal),
                        groupContext + ": the large-shell metal slot must map onto a hazard-free replacement "
                        + "family (BaseMetal.* or Details.*), got \"" + metalMaterial + "\"");
                }
            }

            // (e) positive control: ConstructionStage0 ships base wood + base metal per faction.
            //     IronTeeth construction wood is RoofPlanks.IronTeeth (not BaseWood_DarkBrown) so the
            //     single baked shared uv0 set is inside BOTH factions' patches (see
            //     construction_shared_patches in art/material_texture_map.json).
            var constructionExpectations = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Folktails"] = new[] { "BaseMetal.Folktails", "BaseWood_Brown.Folktails" },
                ["IronTeeth"] = new[] { "BaseMetal.IronTeeth", "RoofPlanks.IronTeeth" },
            };
            foreach (var stem in ExpectedShippedStems())
            {
                if (!stem.Contains(".ConstructionStage0.", StringComparison.Ordinal))
                {
                    continue;
                }

                var faction = FactionOfModelStem(stem);
                var expected = constructionExpectations[faction].OrderBy(name => name, StringComparer.Ordinal);
                var actual = ShippedMeshes[stem].Materials.OrderBy(name => name, StringComparer.Ordinal);
                TestAssert.Equal(string.Join(", ", expected), string.Join(", ", actual),
                    stem + ": construction-stage mesh must use exactly base wood + base metal for " + faction
                    + " (no painted/hazard material)");
            }
        }

        // ------------------------------------------------------------ 4. shipped meshes

        /// <summary>
        /// All 16 shipped <c>data/Buildings/Food/BoxFarm/*.timbermesh</c> files exist, are
        /// non-empty, are listed by art/material_slots.json's models map, and every vanilla
        /// material name stored in their Mesh messages stays inside the faction
        /// MATERIAL_WHITELIST parsed from art/timbermesh_common.py (the faction must also match
        /// the material suffix). The whitelist parse itself is required to yield two non-empty
        /// faction sets, so a drifted/broken contract file fails instead of silently weakening
        /// the check.
        /// </summary>
        public static void TestShippedTimbermeshMaterialsStayInWhitelist()
        {
            var dataFolder = ShippedDataFolder();
            var actualFiles = Directory.GetFiles(dataFolder, "*.timbermesh")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var expectedFiles = ExpectedShippedStems().OrderBy(name => name, StringComparer.Ordinal).ToList();
            TestAssert.Equal(string.Join(", ", expectedFiles), string.Join(", ", actualFiles),
                "data/Buildings/Food/BoxFarm must ship exactly the " + ExpectedShippedModelCount
                + " BoxFarm .timbermesh models");

            var meshes = ShippedMeshes;
            TestAssert.Equal(ExpectedShippedModelCount, meshes.Count,
                "expected all " + ExpectedShippedModelCount + " shipped models to be readable");

            var whitelist = MaterialWhitelist;
            foreach (var faction in Factions)
            {
                TestAssert.True(whitelist.ContainsKey(faction) && whitelist[faction].Count >= 5,
                    "art/timbermesh_common.py MATERIAL_WHITELIST must yield a non-empty " + faction
                    + " whitelist (parsed " + (whitelist.TryGetValue(faction, out var parsed) ? parsed.Count : 0)
                    + " names)");
                foreach (var name in whitelist[faction])
                {
                    TestAssert.True(name.EndsWith("." + faction, StringComparison.Ordinal),
                        "art/timbermesh_common.py MATERIAL_WHITELIST[" + faction + "] contains \"" + name
                        + "\" which is not a " + faction + " material");
                }
            }

            foreach (var pair in meshes.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var stem = pair.Key;
                var faction = FactionOfModelStem(stem);
                foreach (var material in pair.Value.Materials)
                {
                    TestAssert.True(material.EndsWith("." + faction, StringComparison.Ordinal),
                        stem + ": material \"" + material + "\" is not faction-scoped for " + faction
                        + " (a cross-faction material would resolve to the wrong MaterialCollection)");
                    TestAssert.True(whitelist[faction].Contains(material),
                        stem + ": material \"" + material + "\" is not in the " + faction
                        + " MATERIAL_WHITELIST of art/timbermesh_common.py");
                }
            }

            // Each shipped file must be represented by the art/material_slots.json models map.
            var models = Member(SlotsRoot, "models", "art/material_slots.json");
            foreach (var stem in ExpectedShippedStems())
            {
                var groupKey = ModelGroupKeyForStem(stem);
                var group = Member(models, groupKey, "art/material_slots.json models");
                TestAssert.True(group.ValueKind == JsonValueKind.Object && group.EnumerateObject().Any(),
                    "art/material_slots.json models[\"" + groupKey + "\"] is empty; the shipped model "
                    + stem + " is not listed by the art metadata");
            }
        }

        // ------------------------------------------------------------ 5. UV / texture sanity

        /// <summary>
        /// The per-material patch sits in the material's OWN texture, so for every material used by
        /// a shipped model: the patch uv_rect is inside [0,1] and non-degenerate, the texture is a
        /// real 1024x1024-class vanilla _D texture (each dimension in [512..2048]) and is NOT the
        /// retired 2048x2048 fake atlas or one of its atlas_*.png files. Every shipped model uses
        /// 1..4 distinct materials. At least one map texture must differ from 2048x2048: a regression
        /// to the single fabricated atlas must fail here even if the material names look clean.
        /// </summary>
        public static void TestPatchUvSpaceIsSelfConsistent()
        {
            var map = MapMaterials;
            var usedMaterials = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var stem in ExpectedShippedStems())
            {
                var materials = ShippedMeshes[stem].Materials;
                TestAssert.True(materials.Count >= 1 && materials.Count <= 4,
                    stem + ": shipped model uses " + materials.Count
                    + " distinct materials, expected 1..4 (more than four slots is unshippable)");
                foreach (var material in materials)
                {
                    usedMaterials.Add(material);
                }
            }

            foreach (var material in usedMaterials)
            {
                TestAssert.True(HasMaterialEntry(map, material),
                    "material \"" + material + "\" has no texture-patch entry (see "
                    + "TestEveryShippedModelMaterialHasATexturePatch)");
                var record = map[material];
                var u0 = record.UvRect[0];
                var v0 = record.UvRect[1];
                var u1 = record.UvRect[2];
                var v1 = record.UvRect[3];

                TestAssert.True(0.0 <= u0 && u0 < u1 && u1 <= 1.0,
                    material + ": patch uv_rect U range [" + u0 + ", " + u1 + "] must lie strictly inside [0,1]");
                TestAssert.True(0.0 <= v0 && v0 < v1 && v1 <= 1.0,
                    material + ": patch uv_rect V range [" + v0 + ", " + v1 + "] must lie strictly inside [0,1]");
                TestAssert.True(u1 - u0 > 0.05 && v1 - v0 > 0.05,
                    material + ": patch is degenerate (" + (u1 - u0) + " x " + (v1 - v0)
                    + " UV); every face of the material must fit a real patch inside its own texture");

                TestAssert.True(record.Width >= 512 && record.Width <= 2048
                    && record.Height >= 512 && record.Height <= 2048,
                    material + ": texture_size " + record.Width + "x" + record.Height
                    + " is not a 1024x1024-class vanilla texture (allowed range 512..2048 per dimension)");
                TestAssert.False(record.Width == 2048 && record.Height == 2048,
                    material + ": still records the retired fake 2048x2048 atlas as its texture; every "
                    + "material must sample its own real vanilla _D texture");

                TestAssert.False(IsFakeAtlasTexture(record.Texture),
                    material + ": references the retired fabricated atlas texture \"" + record.Texture
                    + "\"; the map must reference a real vanilla _D texture");
            }

            TestAssert.True(map.Values.Any(record => record.Width != 2048 || record.Height != 2048),
                "every material_texture_map entry is 2048x2048; that is the old fabricated atlas, not the "
                + "per-material 1024x1024-class vanilla _D textures");
            foreach (var pair in map.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                TestAssert.False(IsFakeAtlasTexture(pair.Value.Texture),
                    "art/material_texture_map.json materials[\"" + pair.Key
                    + "\"] still references the fake atlas texture \"" + pair.Value.Texture + "\"");
            }
        }

        // ------------------------------------------------------------ 6. negative controls

        /// <summary>
        /// Pure in-memory negative controls that prove the same private validators the shipped-data
        /// tests use have teeth: a UV outside its patch, a patch with std &gt; std_max, a pixel rect
        /// beyond the texture size, a forbidden PaintedMetal material on a large shell, a reference
        /// to the fake 2048 atlas texture and a missing map entry must all be REJECTED. A matching
        /// positive control proves the validators are not simply always false.
        /// </summary>
        public static void TestMaterialPatchValidatorsRejectBadRecords()
        {
            var good = GoodPatchRecord();
            TestAssert.True(IsPatchAcceptable(good, out var goodError),
                "positive control: a well-formed patch record was rejected (" + goodError + ")");
            TestAssert.True(IsUvInsidePatch(good, 0.2, 0.8),
                "positive control: a UV inside the patch was rejected");
            TestAssert.True(IsMaterialAllowed("BaseMetal.Folktails", ForbiddenLargeShellMaterials),
                "positive control: BaseMetal.Folktails was rejected on a large shell");

            var controls = 0;

            // 1. UV outside the patch.
            var outsideU = good.UvRect[2] + 0.05;
            var outsideV = (good.UvRect[1] + good.UvRect[3]) / 2.0;
            TestAssert.False(IsUvInsidePatch(good, outsideU, outsideV),
                "negative control 1: a UV outside its material patch was accepted (fake-atlas regression)");
            controls++;

            // 2. std above the recorded std_max threshold.
            var highVariance = GoodPatchRecord();
            highVariance.Std = highVariance.StdMax + 1.0;
            TestAssert.False(IsPatchAcceptable(highVariance, out var varianceError),
                "negative control 2: a patch with std > std_max was accepted");
            TestAssert.True(varianceError.Contains("std", StringComparison.OrdinalIgnoreCase),
                "negative control 2: the rejection message should mention std, got \"" + varianceError + "\"");
            controls++;

            // 3. pixel rect outside the texture.
            var outOfBounds = GoodPatchRecord();
            outOfBounds.PixelRect = new[] { 900, 900, 1200, 1200 };
            outOfBounds.SizePx = new[] { 300, 300 };
            TestAssert.False(IsPatchAcceptable(outOfBounds, out var boundsError),
                "negative control 3: a patch whose pixel_rect exceeds texture_size was accepted");
            TestAssert.True(boundsError.Contains("pixel", StringComparison.OrdinalIgnoreCase)
                || boundsError.Contains("texture", StringComparison.OrdinalIgnoreCase),
                "negative control 3: the rejection message should mention the pixel/texture bounds, got \""
                + boundsError + "\"");
            controls++;

            // 4. forbidden hazard material on a large shell.
            TestAssert.False(IsMaterialAllowed("PaintedMetal.Folktails", ForbiddenLargeShellMaterials),
                "negative control 4: PaintedMetal.Folktails was allowed on a large shell (yellow/black stripe regression)");
            TestAssert.False(IsMaterialAllowed("PaintedMetal.IronTeeth", ForbiddenLargeShellMaterials),
                "negative control 4: PaintedMetal.IronTeeth was allowed on a large shell (yellow/black stripe regression)");
            controls++;

            // 5. fake 2048 atlas texture reference.
            var fakeAtlas = GoodPatchRecord();
            fakeAtlas.Texture = "atlas_albedo.png";
            TestAssert.False(IsPatchAcceptable(fakeAtlas, out var atlasError),
                "negative control 5: a patch referencing the old fake atlas_albedo.png was accepted");
            TestAssert.True(IsFakeAtlasTexture("atlas_albedo.png"),
                "negative control 5: the fake-atlas detector missed atlas_albedo.png");
            TestAssert.False(IsFakeAtlasTexture("BaseWood_1K_D_Brown.png"),
                "negative control 5: a real vanilla _D texture was wrongly flagged as the fake atlas");
            controls++;

            // 6. missing material_texture_map entry.
            var smallMap = new Dictionary<string, PatchRecord>(StringComparer.Ordinal) { [good.Material] = good };
            TestAssert.True(HasMaterialEntry(smallMap, good.Material),
                "negative control 6: an existing map entry was reported missing");
            TestAssert.False(HasMaterialEntry(smallMap, "PaintedMetal.Folktails"),
                "negative control 6: a material without a map entry was reported as covered");
            controls++;

            // 7. uv_rect inconsistent with pixel_rect / texture_size.
            var mismatchedUv = GoodPatchRecord();
            mismatchedUv.UvRect[0] += 0.01;
            TestAssert.False(IsPatchAcceptable(mismatchedUv, out var uvError),
                "negative control 7: a uv_rect inconsistent with pixel_rect/texture_size was accepted");
            TestAssert.True(uvError.Contains("uv", StringComparison.OrdinalIgnoreCase),
                "negative control 7: the rejection message should mention the uv_rect, got \"" + uvError + "\"");
            controls++;

            Console.WriteLine("  NEGATIVE CONTROLS: PASS " + controls + "/" + controls);
        }

        // ------------------------------------------------------------ 7. real texture sha256

        /// <summary>
        /// Optional integrity check: when the vanilla texture extraction roots recorded in the map
        /// exist on this machine, the sha256 of every recorded real _D texture must match the map.
        /// When the roots are absent (CI / clean checkout) the check SKIPs with a console note and
        /// never fails portability. Hashes are cached per file path.
        /// </summary>
        public static void TestMaterialTextureFilesMatchSha256()
        {
            var rootEntries = ReadTextureRoots(MapRoot);
            var availableRoots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in rootEntries.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var resolved = ResolveRootPath(pair.Value);
                if (Directory.Exists(resolved))
                {
                    availableRoots[pair.Key] = resolved;
                }
            }

            if (availableRoots.Count == 0)
            {
                Console.WriteLine("  SKIP TestMaterialTextureFilesMatchSha256: vanilla texture roots are not "
                    + "present on this machine (" + string.Join(", ", rootEntries.Values) + ")");
                return;
            }

            var map = MapMaterials;
            var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var verified = 0;
            var missing = new List<string>();
            foreach (var pair in map.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var record = pair.Value;
                if (!availableRoots.TryGetValue(record.TextureSource, out var rootPath))
                {
                    continue;
                }

                var texturePath = Path.Combine(rootPath, record.Texture);
                if (!File.Exists(texturePath))
                {
                    missing.Add(pair.Key + " -> " + texturePath);
                    continue;
                }

                if (!hashCache.TryGetValue(texturePath, out var actualSha))
                {
                    actualSha = ComputeSha256(texturePath);
                    hashCache[texturePath] = actualSha;
                }

                TestAssert.Equal(record.Sha256.ToLowerInvariant(), actualSha,
                    "material \"" + pair.Key + "\": recorded texture_sha256 does not match the real file "
                    + texturePath + " (the map must describe the actual vanilla _D texture)");
                verified++;
            }

            if (verified == 0)
            {
                Console.WriteLine("  SKIP TestMaterialTextureFilesMatchSha256: the recorded texture roots exist "
                    + "but contain none of the referenced textures yet (" + string.Join(", ", availableRoots.Values) + ")");
                return;
            }

            TestAssert.Equal(0, missing.Count,
                "art/material_texture_map.json references textures missing from their recorded texture root: "
                + string.Join("; ", missing.Take(5)) + (missing.Count > 5 ? " ..." : ""));
            Console.WriteLine("  sha256 verified: " + verified + " texture(s) from " + availableRoots.Count
                + " available root(s)");
        }

        // ------------------------------------------------------------ 8. shipped uv0 ground truth

        /// <summary>
        /// STRONGEST guard for the user-visible bug: decompress every shipped <c>.timbermesh</c>,
        /// decode its protobuf, read the node uv0 vertex property together with each Mesh's
        /// material and index list, and require every indexed UV to sample that material's own
        /// texture canvas from art/material_texture_map.json (ART-07 §1: tile material -> inside the
        /// 2-texel guard of the full texture; patch material -> inside the curated uv_canvas).
        /// If the models regress to the old fake atlas UVs (or the generator/map disagree), this
        /// fails even though all material names and map entries look correct. It also pins the
        /// shared ConstructionStage0 invariant: the shared window is the Folktails source material's
        /// ART-07 canvas, which both factions' material canvases can sample, otherwise the one baked
        /// uv0 set necessarily samples outside one faction's texture canvas.
        /// </summary>
        public static void TestShippedMeshUvsStayInsideTheirMaterialPatch()
        {
            var map = MapMaterials;
            var totalMeshes = 0;
            var totalIndexedUvs = 0;

            foreach (var pair in SharedConstructionGroups)
            {
                AssertSharedConstructionUvPair(map, pair.Value.Folktails, pair.Value.IronTeeth, pair.Key);
            }

            foreach (var stem in ExpectedShippedStems())
            {
                var mesh = ShippedMeshes[stem];
                var label = stem + ".timbermesh";
                foreach (var node in mesh.Model.Nodes)
                {
                    if (node.Meshes.Count == 0)
                    {
                        continue;
                    }

                    var hasUv0 = TryReadUv0(node, label, out var uvs);
                    foreach (var part in node.Meshes)
                    {
                        if (part.Indices.Count == 0)
                        {
                            continue;
                        }

                        TestAssert.True(hasUv0,
                            label + ": node \"" + node.Name + "\" carries mesh data but no uv0 vertex "
                            + "property (cannot prove the model samples its material patch)");
                        TestAssert.True(part.Material.Length > 0,
                            label + ": node \"" + node.Name + "\" has a mesh without a material name");
                        TestAssert.True(HasMaterialEntry(map, part.Material),
                            label + ": mesh material \"" + part.Material
                            + "\" has no entry in art/material_texture_map.json (see "
                            + "TestEveryShippedModelMaterialHasATexturePatch)");

                        var record = map[part.Material];
                        foreach (var index in part.Indices)
                        {
                            TestAssert.True(index >= 0 && index < uvs.Count,
                                label + ": " + part.Material + " mesh index " + index
                                + " is outside the node's " + uvs.Count + " uv0 vertices");
                            var (u, v) = uvs[index];
                            if (!IsUvInsideMaterialCanvas(record, u, v, MeshUvInsidePatchTolerance))
                            {
                                TestAssert.True(false,
                                    label + ": material \"" + part.Material + "\" has uv0 (" + u + ", " + v
                                    + ") outside its art/material_texture_map.json "
                                    + (record.Mode == "tile" ? "tile canvas guard" : "patch uv_canvas")
                                    + "; the shipped model regressed to UVs that leave the material's own "
                                    + "texture canvas (auto-atlas bleed / fake-atlas checkerboard regression)");
                            }

                            totalIndexedUvs++;
                        }

                        totalMeshes++;
                    }
                }
            }

            TestAssert.True(totalMeshes >= ExpectedShippedModelCount,
                "only " + totalMeshes + " material meshes were found across the shipped models; expected at least "
                + ExpectedShippedModelCount);
            TestAssert.True(totalIndexedUvs >= 10000,
                "only " + totalIndexedUvs + " indexed UVs were checked across the shipped models; the UV ground-truth "
                + "guard did not run over the real geometry");
            Console.WriteLine("  shipped uv0 inside material patch: " + totalIndexedUvs
                + " indexed UVs across " + totalMeshes + " material meshes");
        }

        // ------------------------------------------------------------ validators (also used by tests)

        /// <summary>Returns true when the UV pair lies inside the patch uv rect (with tolerance).</summary>
        private static bool IsUvInsidePatch(PatchRecord record, double u, double v, double tolerance = 1e-6)
        {
            if (record.UvRect.Length != 4 || double.IsNaN(u) || double.IsNaN(v)
                || double.IsInfinity(u) || double.IsInfinity(v))
            {
                return false;
            }

            var u0 = record.UvRect[0];
            var v0 = record.UvRect[1];
            var u1 = record.UvRect[2];
            var v1 = record.UvRect[3];
            return u >= u0 - tolerance && u <= u1 + tolerance
                && v >= v0 - tolerance && v <= v1 + tolerance;
        }

        /// <summary>
        /// ART-07 §1/§4 containment in the material's own texture canvas:
        /// tile material -&gt; inside the 2-texel guard of the whole texture;
        /// patch material -&gt; inside the curated uv_canvas.uv_rect.
        /// </summary>
        private static bool IsUvInsideMaterialCanvas(PatchRecord record, double u, double v, double tolerance)
        {
            if (double.IsNaN(u) || double.IsNaN(v) || double.IsInfinity(u) || double.IsInfinity(v))
            {
                return false;
            }

            double uMin, uMax, vMin, vMax;
            if (string.Equals(record.Mode, "tile", StringComparison.Ordinal))
            {
                if (record.Width <= 0 || record.Height <= 0)
                {
                    return false;
                }

                var guardU = 2.0 / record.Width;
                var guardV = 2.0 / record.Height;
                uMin = guardU;
                uMax = 1.0 - guardU;
                vMin = guardV;
                vMax = 1.0 - guardV;
            }
            else
            {
                if (record.UvCanvasUvRect.Length != 4)
                {
                    return false;
                }

                uMin = record.UvCanvasUvRect[0];
                vMin = record.UvCanvasUvRect[1];
                uMax = record.UvCanvasUvRect[2];
                vMax = record.UvCanvasUvRect[3];
            }

            return u >= uMin - tolerance && u <= uMax + tolerance
                && v >= vMin - tolerance && v <= vMax + tolerance;
        }

        /// <summary>Returns true when every corner of <paramref name="inner"/> is inside
        /// <paramref name="outer"/> (with tolerance).</summary>
        private static bool UvRectInsideOrEqual(double[] outer, double[] inner, double tolerance)
            => outer.Length == 4 && inner.Length == 4
               && inner[0] >= outer[0] - tolerance && inner[1] >= outer[1] - tolerance
               && inner[2] <= outer[2] + tolerance && inner[3] <= outer[3] + tolerance;

        /// <summary>Finds the map record of the used material that samples the given texture.</summary>
        private static PatchRecord? FindTextureOwner(Dictionary<string, PatchRecord> materials, string texture)
            => materials.Values.FirstOrDefault(record =>
                string.Equals(record.Texture, texture, StringComparison.Ordinal));

        /// <summary>
        /// ART07 §4/§5 std_max rule for the legacy patch record: a patch-mode record is the large
        /// curated ART-07 canvas whose budget is the ART-05 hard cap; a tile-mode record is the
        /// small ART-05 probe window which keeps the reviewed per-class budget.
        /// </summary>
        private static double ExpectedPatchStdMax(PatchRecord record,
            IReadOnlyDictionary<string, double> classBudgets, double hardStdCap)
            => string.Equals(record.Mode, "patch", StringComparison.Ordinal)
                ? hardStdCap
                : Math.Min(classBudgets[record.PatchClass], hardStdCap);

        /// <summary>
        /// Returns true when the material is not banned by the forbidden large-shell list. Supports
        /// exact names and <c>Family.*</c> wildcard entries.
        /// </summary>
        private static bool IsMaterialAllowed(string material, IReadOnlyCollection<string> forbidden)
        {
            if (string.IsNullOrWhiteSpace(material))
            {
                return false;
            }

            foreach (var entry in forbidden)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var token = entry.Trim();
                if (token.EndsWith(".*", StringComparison.Ordinal))
                {
                    var family = token.Substring(0, token.Length - 2);
                    if (material.Equals(family, StringComparison.Ordinal)
                        || material.StartsWith(family + ".", StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                else if (material.Equals(token, StringComparison.Ordinal))
                {
                    return false;
                }
                else if (token.IndexOf('.') < 0
                         && material.StartsWith(token + ".", StringComparison.Ordinal))
                {
                    // A bare family name (e.g. "PaintedMetal") bans every faction variant.
                    return false;
                }
            }

            return true;
        }

        /// <summary>Returns true when the given file name is the retired fabricated 2048 atlas.</summary>
        private static bool IsFakeAtlasTexture(string textureFileName)
        {
            var name = Path.GetFileName(textureFileName ?? string.Empty);
            if (name.Length == 0)
            {
                return false;
            }

            var stem = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4)
                : name;
            return stem.StartsWith("atlas_albedo", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("atlas_normal", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("atlas_mask", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("atlas", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasMaterialEntry(Dictionary<string, PatchRecord> map, string material)
            => map.ContainsKey(material);

        /// <summary>Deterministic comma-joined sorted view of a material-name sequence.</summary>
        private static string SortedJoin(IEnumerable<string> values)
            => string.Join(", ", values.OrderBy(value => value, StringComparer.Ordinal));

        /// <summary>Returns true when two 4-value UV rects are equal within the given tolerance.</summary>
        private static bool UvRectsEqual(double[] left, double[] right, double tolerance)
            => left.Length == 4 && right.Length == 4
               && Math.Abs(left[0] - right[0]) <= tolerance
               && Math.Abs(left[1] - right[1]) <= tolerance
               && Math.Abs(left[2] - right[2]) <= tolerance
               && Math.Abs(left[3] - right[3]) <= tolerance;

        /// <summary>
        /// The one-baked-UV-set invariant of the shared ConstructionStage0 geometry (ART-07 §3):
        /// the source (Folktails) material's ART-07 canvas is the shared window, and every faction's
        /// material canvas must contain that window (BaseMetal.Folktails / BaseMetal.IronTeeth share
        /// the identical canvas; the RoofPlanks.IronTeeth full-bleed tile texture contains the
        /// BaseWood_Brown wood canvas). Both construction materials must still keep a valid patch
        /// record so C12/C13 keep working.
        /// </summary>
        private static void AssertSharedConstructionUvPair(
            Dictionary<string, PatchRecord> materials,
            string folktailsMaterial,
            string ironTeethMaterial,
            string groupName)
        {
            TestAssert.True(materials.ContainsKey(folktailsMaterial),
                groupName + ": missing material_texture_map entry for the Folktails construction material \""
                + folktailsMaterial + "\"");
            TestAssert.True(materials.ContainsKey(ironTeethMaterial),
                groupName + ": missing material_texture_map entry for the IronTeeth construction material \""
                + ironTeethMaterial + "\"");
            var folktails = materials[folktailsMaterial];
            var ironTeeth = materials[ironTeethMaterial];
            TestAssert.Equal(4, folktails.UvCanvasUvRect.Length,
                groupName + ": the Folktails construction source material must carry an ART-07 uv_canvas");
            TestAssert.Equal(4, ironTeeth.UvCanvasUvRect.Length,
                groupName + ": the IronTeeth construction material must carry an ART-07 uv_canvas");
            TestAssert.True(UvRectInsideOrEqual(ironTeeth.UvCanvasUvRect, folktails.UvCanvasUvRect, 1e-9),
                groupName + ": the shared window (the " + folktailsMaterial + " canvas ["
                + string.Join(", ", folktails.UvCanvasUvRect) + "]) must lie inside the "
                + ironTeethMaterial + " canvas [" + string.Join(", ", ironTeeth.UvCanvasUvRect)
                + "]; otherwise the single baked uv0 set samples outside one faction's texture canvas");
            TestAssert.True(folktails.Std <= folktails.StdMax,
                groupName + ": " + folktailsMaterial + " patch record must stay within its std budget");
            TestAssert.True(ironTeeth.Std <= ironTeeth.StdMax,
                groupName + ": " + ironTeethMaterial + " patch record must stay within its std budget");
        }

        /// <summary>
        /// Semantic patch validator shared by the shipped-data tests and the negative controls.
        /// Returns false and a human-readable reason for the first violated rule.
        /// </summary>
        private static bool IsPatchAcceptable(PatchRecord record, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(record.Material))
            {
                return Reject("material name is empty", out error);
            }

            if (!IsBarePngFileName(record.Texture))
            {
                return Reject("texture \"" + record.Texture + "\" must be a bare *.png filename of a real "
                    + "vanilla _D texture (no directories)", out error);
            }

            if (IsFakeAtlasTexture(record.Texture))
            {
                return Reject("texture \"" + record.Texture + "\" is the retired fabricated 2048 atlas; every "
                    + "material must reference its own real vanilla _D texture", out error);
            }

            if (string.IsNullOrWhiteSpace(record.TextureSource) || string.IsNullOrWhiteSpace(record.TexturePath))
            {
                return Reject("texture_source/texture_path is empty", out error);
            }

            if (record.Width < 128 || record.Height < 128)
            {
                return Reject("texture_size " + record.Width + "x" + record.Height
                    + " is smaller than 128x128", out error);
            }

            if (!IsHexSha256(record.Sha256))
            {
                return Reject("texture_sha256 \"" + record.Sha256 + "\" is not a 64-character hex digest", out error);
            }

            if (record.ColorTint.Length != 3
                || record.ColorTint.Any(value => double.IsNaN(value) || value < 0.0 || value > 1.0))
            {
                return Reject("material_color_tint must be 3 floats in [0,1]", out error);
            }

            if (!KnownPatchClasses.Contains(record.PatchClass, StringComparer.Ordinal))
            {
                return Reject("patch_class \"" + record.PatchClass + "\" is not one of "
                    + string.Join("|", KnownPatchClasses), out error);
            }

            if (record.PixelRect.Length != 4)
            {
                return Reject("patch.pixel_rect must have 4 integers", out error);
            }

            var x0 = record.PixelRect[0];
            var y0 = record.PixelRect[1];
            var x1 = record.PixelRect[2];
            var y1 = record.PixelRect[3];
            if (x0 < 0 || y0 < 0 || x0 >= x1 || y0 >= y1 || x1 > record.Width || y1 > record.Height)
            {
                return Reject("patch.pixel_rect [" + x0 + ", " + y0 + ", " + x1 + ", " + y1
                    + "] is not a non-empty rect inside the " + record.Width + "x" + record.Height
                    + " texture", out error);
            }

            if (record.SizePx.Length != 2 || record.SizePx[0] != x1 - x0 || record.SizePx[1] != y1 - y0)
            {
                return Reject("patch.size_px [" + string.Join(", ", record.SizePx)
                    + "] must equal the pixel_rect size [" + (x1 - x0) + ", " + (y1 - y0) + "]", out error);
            }

            if (record.UvRect.Length != 4)
            {
                return Reject("patch.uv_rect must have 4 numbers", out error);
            }

            var u0 = record.UvRect[0];
            var v0 = record.UvRect[1];
            var u1 = record.UvRect[2];
            var v1 = record.UvRect[3];
            if (record.UvRect.Any(value => double.IsNaN(value) || double.IsInfinity(value))
                || u0 < 0.0 || u0 >= u1 || u1 > 1.0 || v0 < 0.0 || v0 >= v1 || v1 > 1.0)
            {
                return Reject("patch.uv_rect [" + string.Join(", ", record.UvRect)
                    + "] must satisfy 0 <= u0 < u1 <= 1 and 0 <= v0 < v1 <= 1", out error);
            }

            // The frozen convention is atlas_layout.px_rect_to_uv: top-left pixel origin and a
            // FLIPPED v (v0 = 1 - y1/H, v1 = 1 - y0/H).
            var expectedU0 = x0 / (double)record.Width;
            var expectedU1 = x1 / (double)record.Width;
            var expectedV0 = 1.0 - y1 / (double)record.Height;
            var expectedV1 = 1.0 - y0 / (double)record.Height;
            if (Math.Abs(u0 - expectedU0) > UvRectConsistencyTolerance
                || Math.Abs(u1 - expectedU1) > UvRectConsistencyTolerance
                || Math.Abs(v0 - expectedV0) > UvRectConsistencyTolerance
                || Math.Abs(v1 - expectedV1) > UvRectConsistencyTolerance)
            {
                return Reject("patch.uv_rect [" + string.Join(", ", record.UvRect)
                    + "] does not match pixel_rect / texture_size under the frozen uv_rect convention "
                    + "(top-left pixel origin, v flipped): expected [" + expectedU0 + ", " + expectedV0
                    + ", " + expectedU1 + ", " + expectedV1 + "]", out error);
            }

            if (record.MeanRgb.Length != 3
                || record.MeanRgb.Any(value => double.IsNaN(value) || value < 0.0 || value > 255.0))
            {
                return Reject("patch.mean_rgb must be 3 recorded values in [0,255]", out error);
            }

            if (record.MeanRgbTinted.Length != 3
                || record.MeanRgbTinted.Any(value => double.IsNaN(value) || value < 0.0 || value > 255.0))
            {
                return Reject("patch.mean_rgb_tinted must be 3 values in [0,255]", out error);
            }

            if (double.IsNaN(record.Std) || record.Std < 0.0)
            {
                return Reject("patch.std must be a recorded value >= 0", out error);
            }

            if (record.StdByChannel.Length != 3
                || record.StdByChannel.Any(value => double.IsNaN(value) || value < 0.0))
            {
                return Reject("patch.std_by_channel must be 3 recorded values >= 0", out error);
            }

            if (double.IsNaN(record.StdMax) || record.StdMax <= 0.0)
            {
                return Reject("patch.std_max must be a threshold > 0", out error);
            }

            if (record.Std > record.StdMax)
            {
                return Reject("patch.std (" + record.Std + ") exceeds patch.std_max (" + record.StdMax
                    + "); this patch's real texture is too varied to be a flat large-surface patch", out error);
            }

            if (string.IsNullOrWhiteSpace(record.Reason))
            {
                return Reject("patch.reason must be non-empty (why this patch was chosen)", out error);
            }

            if (record.RejectTags is null)
            {
                return Reject("patch.reject_tags must be an array (may be empty)", out error);
            }

            return true;
        }

        private static bool Reject(string message, out string error)
        {
            error = message;
            return false;
        }

        /// <summary>A synthetic, fully consistent patch record used for the positive/negative controls.</summary>
        private static PatchRecord GoodPatchRecord()
        {
            const int textureSize = 1024;
            const int left = 64;
            const int top = 64;
            const int right = 320;
            const int bottom = 320;
            return new PatchRecord
            {
                Material = "BaseWood_Brown.Folktails",
                Texture = "BaseWood_1K_D_Brown.png",
                TextureSource = "vanilla-tex-all",
                TexturePath = Path.Combine("C:\\vanilla-tex-all", "BaseWood_1K_D_Brown.png"),
                Width = textureSize,
                Height = textureSize,
                Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                ColorTint = new[] { 0.9, 0.8, 0.7 },
                PatchClass = "wood",
                PixelRect = new[] { left, top, right, bottom },
                SizePx = new[] { right - left, bottom - top },
                UvRect = new[]
                {
                    left / (double)textureSize,
                    1.0 - bottom / (double)textureSize,
                    right / (double)textureSize,
                    1.0 - top / (double)textureSize,
                },
                MeanRgb = new[] { 120.0, 90.0, 70.0 },
                MeanRgbTinted = new[] { 128.0, 96.0, 80.0 },
                Std = 10.0,
                StdByChannel = new[] { 8.0, 9.0, 10.0 },
                StdMax = 18.0,
                Mode = "patch",
                UvCanvasPixelRect = new[] { left, top, right, bottom },
                UvCanvasUvRect = new[]
                {
                    left / (double)textureSize,
                    1.0 - bottom / (double)textureSize,
                    right / (double)textureSize,
                    1.0 - top / (double)textureSize,
                },
                Reason = "flat mid-tone plank region used as the wood patch",
                RejectTags = new[] { "seam" },
            };
        }

        // ------------------------------------------------------------ map / slots loading

        private static JsonElement LoadMapRoot()
            => LoadJsonFile(MapRelativePath, "the per-material texture patch map");

        private static Dictionary<string, PatchRecord> LoadMapMaterials()
        {
            var mapRoot = MapRoot;
            const string context = "art/material_texture_map.json";
            var materialsElement = Member(mapRoot, "materials", context);
            var records = new Dictionary<string, PatchRecord>(StringComparer.Ordinal);
            foreach (var property in ObjectEntries(materialsElement, context + " materials"))
            {
                records[property.Name] = ParsePatchRecord(
                    property.Name, property.Value, context + " materials[\"" + property.Name + "\"]");
            }

            TestAssert.True(records.Count >= 8,
                context + ": expected a per-material entry for every used vanilla material, found only "
                + records.Count);
            return records;
        }

        private static JsonElement LoadSlotsRoot()
            => LoadJsonFile(SlotsRelativePath, "art/material_slots.json");

        private static string[] ReadForbiddenMaterials(JsonElement mapRoot)
        {
            const string context = "art/material_texture_map.json";
            var policy = Member(mapRoot, "large_shell_policy", context);
            var forbidden = Member(policy, "forbidden_materials", context + " large_shell_policy");
            var values = StringArray(forbidden, context + " large_shell_policy.forbidden_materials", requireNonEmpty: true);
            TestAssert.True(values.Length >= 2,
                context + ": large_shell_policy.forbidden_materials must ban both PaintedMetal faction variants");
            return values;
        }

        private static Dictionary<string, JsonElement> ReadUnusedMaterials(JsonElement mapRoot)
        {
            const string context = "art/material_texture_map.json";
            var unusedElement = Member(mapRoot, "unused_materials", context);
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in ObjectEntries(unusedElement, context + " unused_materials"))
            {
                result[property.Name] = property.Value.Clone();
            }

            return result;
        }

        private static Dictionary<string, string> ReadTextureRoots(JsonElement mapRoot)
        {
            const string context = "art/material_texture_map.json";
            var rootsElement = Member(mapRoot, "texture_roots", context);
            var roots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in ObjectEntries(rootsElement, context + " texture_roots"))
            {
                roots[property.Name] = StringValue(property.Value,
                    context + " texture_roots[\"" + property.Name + "\"]");
            }

            return roots;
        }

        private static PatchRecord ParsePatchRecord(string materialName, JsonElement entry, string context)
        {
            var record = new PatchRecord { Material = materialName };
            record.Texture = StringMember(entry, "texture", context);
            record.TextureSource = StringMember(entry, "texture_source", context);
            record.TexturePath = StringMember(entry, "texture_path", context);

            var textureSize = IntArray(Member(entry, "texture_size", context), 2, context + " texture_size");
            record.Width = textureSize[0];
            record.Height = textureSize[1];
            record.Sha256 = StringMember(entry, "texture_sha256", context);
            record.ColorTint = DoubleArray(Member(entry, "material_color_tint", context), 3,
                context + " material_color_tint");
            record.PatchClass = StringMember(entry, "patch_class", context);

            // ART-07 §4 additions (optional on the texture_patches records, required on the
            // per-material records; the mode-aware UV containment below only needs the materials).
            if (entry.TryGetProperty("mode", out var modeElement)
                && modeElement.ValueKind == JsonValueKind.String)
            {
                record.Mode = modeElement.GetString()!;
            }

            if (entry.TryGetProperty("uv_canvas", out var uvCanvasElement)
                && uvCanvasElement.ValueKind == JsonValueKind.Object)
            {
                record.UvCanvasPixelRect = IntArray(
                    Member(uvCanvasElement, "pixel_rect", context + " uv_canvas"), 4,
                    context + " uv_canvas.pixel_rect");
                record.UvCanvasUvRect = DoubleArray(
                    Member(uvCanvasElement, "uv_rect", context + " uv_canvas"), 4,
                    context + " uv_canvas.uv_rect");
            }

            var patch = Member(entry, "patch", context);
            record.PixelRect = IntArray(Member(patch, "pixel_rect", context), 4, context + " patch.pixel_rect");
            record.SizePx = IntArray(Member(patch, "size_px", context), 2, context + " patch.size_px");
            record.UvRect = DoubleArray(Member(patch, "uv_rect", context), 4, context + " patch.uv_rect");
            record.MeanRgb = DoubleArray(Member(patch, "mean_rgb", context), 3, context + " patch.mean_rgb");
            record.MeanRgbTinted = DoubleArray(Member(patch, "mean_rgb_tinted", context), 3,
                context + " patch.mean_rgb_tinted");
            record.Std = NumberMember(patch, "std", context + " patch");
            record.StdByChannel = DoubleArray(Member(patch, "std_by_channel", context), 3,
                context + " patch.std_by_channel");
            record.StdMax = NumberMember(patch, "std_max", context + " patch");
            record.Reason = StringMember(patch, "reason", context + " patch");
            record.RejectTags = StringArray(Member(patch, "reject_tags", context),
                context + " patch.reject_tags", requireNonEmpty: false);
            return record;
        }

        /// <summary>Reads a "material_texture_patches" object into slot name -> vanilla material name.</summary>
        private static Dictionary<string, string> ReadSlotMaterialMap(JsonElement element, string context)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in ObjectEntries(element, context))
            {
                result[property.Name] = StringValue(property.Value, context + "[\"" + property.Name + "\"]");
            }

            return result;
        }

        // ------------------------------------------------------------ shipped model loading

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

        private static string ModelGroupKeyForStem(string stem)
        {
            var parts = stem.Split('.');
            ValidateModelStem(stem, parts);
            return stem.Contains(".ConstructionStage0.", StringComparison.Ordinal)
                ? ConstructionStageModelKey
                : parts[1] + "." + parts[2];
        }

        private static string FactionOfModelStem(string stem)
        {
            var parts = stem.Split('.');
            ValidateModelStem(stem, parts);
            return parts[2];
        }

        private static string FactionOfModelGroupKey(string modelKey)
        {
            var parts = modelKey.Split('.');
            TestAssert.True(parts.Length == 2 && Factions.Contains(parts[1], StringComparer.Ordinal),
                "art/material_slots.json model key \"" + modelKey + "\" is not a <Tier>.<Faction> key");
            return parts[1];
        }

        private static void ValidateModelStem(string stem, string[] parts)
        {
            TestAssert.True(parts.Length >= 4 && parts[0] == "BoxFarm"
                && Tiers.Contains(parts[1], StringComparer.Ordinal)
                && Factions.Contains(parts[2], StringComparer.Ordinal)
                && parts[parts.Length - 1] == "Model",
                "unexpected shipped model stem \"" + stem + "\" (expected BoxFarm.<Tier>.<Faction>[.ConstructionStage0].Model)");
        }

        private static string ShippedDataFolder()
            => Path.Combine(DataRoot(), BoxFarmDataFolder.Replace('/', Path.DirectorySeparatorChar));

        private static string ShippedMeshPath(string stem)
            => Path.Combine(ShippedDataFolder(), stem + ".timbermesh");

        private static Dictionary<string, ParsedMesh> LoadShippedMeshes()
        {
            var meshes = new Dictionary<string, ParsedMesh>(StringComparer.Ordinal);
            foreach (var stem in ExpectedShippedStems())
            {
                var path = ShippedMeshPath(stem);
                TestAssert.True(File.Exists(path),
                    "expected the shipped model " + stem + ".timbermesh at " + path
                    + " (all " + ExpectedShippedModelCount + " BoxFarm models must ship)");

                var compressed = File.ReadAllBytes(path);
                TestAssert.True(compressed.Length > 0, stem + ".timbermesh is empty (" + path + ")");
                TestAssert.True(compressed.Length >= 2, stem + ".timbermesh is too short to carry a zlib header");
                TestAssert.True((compressed[0] & 0x0F) == 8,
                    stem + ".timbermesh is not a zlib stream (CMF compression method must be 8, got 0x"
                    + (compressed[0] & 0x0F).ToString("X2") + ")");
                TestAssert.True((((compressed[0] << 8) | compressed[1]) % 31) == 0,
                    stem + ".timbermesh has an invalid zlib header (FCHECK mismatch)");

                var payload = DecompressZlib(compressed);
                TestAssert.True(payload.Length > 0, stem + ".timbermesh decompressed to 0 bytes");

                var model = ParseTimbermesh(payload, stem);
                var materials = new List<string>();
                foreach (var node in model.Nodes)
                {
                    foreach (var mesh in node.Meshes)
                    {
                        if (mesh.Material.Length > 0
                            && !materials.Contains(mesh.Material, StringComparer.Ordinal))
                        {
                            materials.Add(mesh.Material);
                        }
                    }
                }

                materials.Sort(StringComparer.Ordinal);
                meshes[stem] = new ParsedMesh
                {
                    Stem = stem,
                    FilePath = path,
                    Payload = payload,
                    Model = model,
                    Materials = materials,
                };
            }

            return meshes;
        }

        // ------------------------------------------------------------ whitelist / mapping source

        private static Dictionary<string, HashSet<string>> LoadMaterialWhitelist()
        {
            var path = RepoPath(CommonPyRelativePath);
            TestAssert.True(File.Exists(path), "expected the shared material contract at " + path);
            var text = File.ReadAllText(path, Encoding.UTF8);

            var block = Regex.Match(text, @"MATERIAL_WHITELIST\s*=\s*\{(?<body>.*?)\n\}",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            TestAssert.True(block.Success,
                "art/timbermesh_common.py: could not locate the \"MATERIAL_WHITELIST = { ... }\" dict literal; "
                + "the faction whitelist is the contract the shipped material names are checked against");

            var body = block.Groups["body"].Value;
            var whitelist = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var faction in Factions)
            {
                var section = Regex.Match(body,
                    "\"" + faction + "\"\\s*:\\s*\\[(?<list>.*?)\\]",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant);
                TestAssert.True(section.Success,
                    "art/timbermesh_common.py: MATERIAL_WHITELIST has no \"" + faction + "\" list");

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in Regex.Matches(section.Groups["list"].Value,
                    "\"(?<name>[A-Za-z0-9_]+\\.(?:Folktails|IronTeeth))\"",
                    RegexOptions.CultureInvariant))
                {
                    names.Add(match.Groups["name"].Value);
                }

                TestAssert.True(names.Count >= 5,
                    "art/timbermesh_common.py: parsed only " + names.Count + " " + faction
                    + " MATERIAL_WHITELIST material names; the regex/contract drifted");
                whitelist[faction] = names;
            }

            return whitelist;
        }

        /// <summary>
        /// The MATERIAL_MAP source span of art/timbermesh_common.py (explicit tier/faction mappings
        /// plus the ConstructionStage0 loop). Used to prove no slot is mapped onto PaintedMetal any
        /// more. The span ends at the first stable marker after the mapping block.
        /// </summary>
        private static string MaterialMapSourceText()
        {
            var path = RepoPath(CommonPyRelativePath);
            TestAssert.True(File.Exists(path), "expected the shared material contract at " + path);
            var text = File.ReadAllText(path, Encoding.UTF8);
            var start = text.IndexOf("MATERIAL_MAP = {", StringComparison.Ordinal);
            TestAssert.True(start >= 0,
                "art/timbermesh_common.py: could not locate the MATERIAL_MAP mapping literal");
            var end = text.Length;
            foreach (var marker in new[]
                     {
                         "assert all(len(m) <= 4",
                         "# empties: connection ports only",
                         "def contract_to_export",
                     })
            {
                var index = text.IndexOf(marker, start, StringComparison.Ordinal);
                if (index > start && index < end)
                {
                    end = index;
                }
            }

            return text.Substring(start, end - start);
        }

        // ------------------------------------------------------------ timbermesh protobuf reader

        private sealed class ParsedMesh
        {
            public string Stem = string.Empty;
            public string FilePath = string.Empty;
            public byte[] Payload = Array.Empty<byte>();
            public TimbermeshModel Model = new();
            public List<string> Materials = new();
        }

        private sealed class PatchRecord
        {
            public string Material = string.Empty;
            public string Texture = string.Empty;
            public string TextureSource = string.Empty;
            public string TexturePath = string.Empty;
            public int Width;
            public int Height;
            public string Sha256 = string.Empty;
            public double[] ColorTint = Array.Empty<double>();
            public string PatchClass = string.Empty;
            public int[] PixelRect = Array.Empty<int>();
            public int[] SizePx = Array.Empty<int>();
            public double[] UvRect = Array.Empty<double>();
            public double[] MeanRgb = Array.Empty<double>();
            public double[] MeanRgbTinted = Array.Empty<double>();
            public double Std;
            public double[] StdByChannel = Array.Empty<double>();
            public double StdMax;
            public string Reason = string.Empty;
            public string[] RejectTags = Array.Empty<string>();

            // ART-07 §4: "tile" | "patch" plus the actual material canvas (materials records only).
            public string Mode = string.Empty;
            public int[] UvCanvasPixelRect = Array.Empty<int>();
            public double[] UvCanvasUvRect = Array.Empty<double>();
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
        // This is the same minimal reader approach as ArtModelContractTests (kept local to this
        // L1 contract so the two cards stay independent); no game/Unity/protobuf assembly is used.
        private sealed class TimbermeshModel
        {
            public string Name = string.Empty;
            public List<TimbermeshNode> Nodes { get; } = new();
        }

        private sealed class TimbermeshNode
        {
            public string Name = string.Empty;
            public List<TimbermeshProperty> Properties { get; } = new();
            public List<TimbermeshMesh> Meshes { get; } = new();
        }

        private sealed class TimbermeshProperty
        {
            public string Name = string.Empty;
            public int Dimension;
            public byte[] Data = Array.Empty<byte>();
        }

        private sealed class TimbermeshMesh
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

        private static TimbermeshModel ParseTimbermesh(byte[] payload, string label)
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

        private static TimbermeshModel ParseTimbermeshCore(byte[] payload)
        {
            var model = new TimbermeshModel();
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        _ = reader.ReadInt32(); // Model.version
                        break;
                    case 2:
                        model.Name = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    case 3:
                        model.Nodes.Add(ParseNode(reader.ReadBytes()));
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            return model;
        }

        private static TimbermeshNode ParseNode(byte[] payload)
        {
            var node = new TimbermeshNode();
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        _ = reader.ReadInt32(); // parent
                        break;
                    case 2:
                        node.Name = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    case 7:
                        node.Properties.Add(ParseProperty(reader.ReadBytes()));
                        break;
                    case 8:
                        node.Meshes.Add(ParseMesh(reader.ReadBytes()));
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            return node;
        }

        private static TimbermeshProperty ParseProperty(byte[] payload)
        {
            var property = new TimbermeshProperty();
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

        private static TimbermeshMesh ParseMesh(byte[] payload)
        {
            var mesh = new TimbermeshMesh();
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

        /// <summary>Reads the node's uv0 vertex property into a UV-pair list.</summary>
        private static bool TryReadUv0(TimbermeshNode node, string label, out List<(double U, double V)> uvs)
        {
            uvs = new List<(double U, double V)>();
            var property = node.Properties.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "uv0", StringComparison.Ordinal));
            if (property is null)
            {
                return false;
            }

            TestAssert.Equal(2, property.Dimension,
                label + ": node \"" + node.Name + "\" uv0 vertex property must have dimension 2");
            TestAssert.True(property.Data.Length % 8 == 0,
                label + ": node \"" + node.Name + "\" uv0 data length " + property.Data.Length
                + " is not a multiple of 8 bytes");
            for (var offset = 0; offset < property.Data.Length; offset += 8)
            {
                var u = BitConverter.ToSingle(property.Data, offset);
                var v = BitConverter.ToSingle(property.Data, offset + 4);
                if (float.IsNaN(u) || float.IsNaN(v) || float.IsInfinity(u) || float.IsInfinity(v))
                {
                    TestAssert.True(false,
                        label + ": node \"" + node.Name + "\" has a non-finite uv0 value at vertex "
                        + (offset / 8));
                }

                uvs.Add((u, v));
            }

            return true;
        }

        // ------------------------------------------------------------ json / file helpers

        private static string RepoRoot()
        {
            var metadata = typeof(V2MaterialPatchContractTests).Assembly
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
                    "shipped data/ directory not found under \"" + RepoRoot() + "\" (looked for " + dataRoot + ")");
            }

            return dataRoot;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

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
                throw new TestFailureException(
                    context + ": expected a JSON object while reading \"" + name + "\", got " + element.ValueKind);
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

        private static double NumberValue(JsonElement element, string context)
        {
            if (element.ValueKind != JsonValueKind.Number)
            {
                throw new TestFailureException(context + ": expected a JSON number, got " + element.ValueKind);
            }

            var value = element.GetDouble();
            if (double.IsNaN(value) || double.IsInfinity(value))
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
                throw new TestFailureException(
                    context + ": expected an array of " + expectedLength + " integers, got " + element.ValueKind);
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
                throw new TestFailureException(
                    context + ": expected an array of " + expectedLength + " numbers, got " + element.ValueKind);
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
                throw new TestFailureException(context + ": expected an array of strings, got " + element.ValueKind);
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

        /// <summary>Reads a pixel rect given either as [x0,y0,x1,y1] or as an object with x0/y0/x1/y1.</summary>
        private static int[] ReadPixelRect(JsonElement element, string context)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return IntArray(element, 4, context);
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                var values = new int[4];
                var names = new[] { "x0", "y0", "x1", "y1" };
                for (var index = 0; index < names.Length; index++)
                {
                    var value = Member(element, names[index], context);
                    values[index] = IntValue(value, context + " \"" + names[index] + "\"");
                }

                return values;
            }

            throw new TestFailureException(
                context + ": expected a pixel rect array or object, got " + element.ValueKind);
        }

        private static bool IsBarePngFileName(string name)
            => !string.IsNullOrWhiteSpace(name)
               && name.IndexOf('/') < 0
               && name.IndexOf('\\') < 0
               && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        private static bool IsFactionScopedMaterialName(string name)
            => !string.IsNullOrWhiteSpace(name)
               && (name.EndsWith(".Folktails", StringComparison.Ordinal)
                   || name.EndsWith(".IronTeeth", StringComparison.Ordinal));

        private static bool IsHexSha256(string value)
            => value.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F'));

        private static string ResolveRootPath(string recordedPath)
        {
            if (string.IsNullOrWhiteSpace(recordedPath))
            {
                return recordedPath;
            }

            var normalized = recordedPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(RepoRoot(), normalized));
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private static byte[] DecompressZlib(byte[] compressed)
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
    }
}
