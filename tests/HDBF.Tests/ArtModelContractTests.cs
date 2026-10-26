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
    /// <summary>
    /// ART-03 L1 data contract: every BoxFarm blueprint must point at the custom, shipped
    /// <c>.timbermesh</c> art, and the shipped files must really be readable meshes.
    ///
    /// This layer reads the REAL files under <c>data/</c> (the repository root is baked into the
    /// test assembly as <c>AssemblyMetadata("HdbfRepoRoot", ...)</c> because the build output lives
    /// outside the repo, see HDBF.Tests.csproj). No Timberborn or Unity assembly is referenced and
    /// none is loaded (REQ-TEST-03): <c>System.Text.Json</c> parses the blueprints and
    /// <c>System.IO.Compression.ZLibStream</c> decompresses the mesh payload so the test can prove
    /// the asset is a real zlib-compressed timbermesh containing the expected model name, without
    /// the game's protobuf types.
    ///
    /// ART-04 (user instruction 2026-10-25) changes the marker contract: the roof is one empty flat
    /// platform, the six <c>#Slot#Crop_0..5</c> marker nodes are gone, and the model keeps exactly
    /// the three rear port empties <c>#Port#Power</c>, <c>#Port#Pipe</c>, <c>#Port#Logistics</c>.
    /// Geometry (flat empty platform, bounds, materials, UV patches) is enforced by
    /// <c>art/validate_timbermesh.py</c>; this layer pins what the data container and the shipped
    /// documentation can prove.
    ///
    /// Why this exists: v0.1.0 pointed the eight blueprints at vanilla models (GathererFlag, ...)
    /// and carried their animation slot specs. A regression back to a vanilla model, back to a
    /// missing model file, or back to a slot spec that a custom model does not provide makes the
    /// building unplaceable or invisible in game, and would otherwise only show up in a smoke test.
    /// </summary>
    public static class ArtModelContractTests
    {
        private const string RepoRootMetadataKey = "HdbfRepoRoot";
        private const string BoxFarmDataFolder = "Buildings/Food/BoxFarm";
        private const string CustomModelFolder = "Buildings/Food/BoxFarm";

        private static readonly string[] Tiers = { "Basic", "Intermediate", "Advanced", "Top" };

        private static readonly string[] Factions = { "Folktails", "IronTeeth" };

        /// <summary>
        /// ART-04: every custom model must still ship exactly these three rear port marker empties
        /// (the blueprint wiring and the <c>HdbfBoxFarmSpec</c> port flags depend on them).
        /// </summary>
        private static readonly string[] RequiredPortMarkers =
        {
            "#Port#Power", "#Port#Pipe", "#Port#Logistics",
        };

        /// <summary>
        /// ART-04: the removed crop trough markers. No custom model may contain any <c>#Slot#</c>
        /// node name any more — the roof is one empty flat platform with nothing above it.
        /// </summary>
        private const string ForbiddenSlotMarker = "#Slot#";

        /// <summary>
        /// ART-04 machine-readable art contract lines that data/ART_PLACEHOLDER.md must carry.
        /// They are the text-level contract the docs workstream and the data validator agree on.
        /// </summary>
        private static readonly string[] ArtContractLines =
        {
            "ART_CONTRACT: roof=empty-flat-platform",
            "ART_CONTRACT: slots=none",
            "ART_CONTRACT: ports=#Port#Power,#Port#Pipe,#Port#Logistics",
            "ART_CONTRACT: material=flat-patch",
            "ART_CONTRACT: ground-crops-hidden=BUILD-05",
        };

        /// <summary>
        /// The eight vanilla model paths the v0.1.0 placeholders used. None of them may survive in
        /// any model field of a shipped blueprint: the custom art is faction-scoped and the vanilla
        /// paths also drag in animation slot keywords the new meshes do not have.
        /// </summary>
        private static readonly string[] VanillaPlaceholderModels =
        {
            "Buildings/Food/GathererFlag/GathererFlag.Folktails.Model",
            "Buildings/Food/GathererFlag/GathererFlag.IronTeeth.Model",
            "Buildings/Food/EfficientFarmHouse/EfficientFarmHouse.Folktails.Model",
            "Buildings/Food/FarmHouse/FarmHouse.IronTeeth.Model",
            "Buildings/Food/Gristmill/Gristmill.Folktails.Model",
            "Buildings/Food/HydroponicGarden/HydroponicGarden.IronTeeth.Model",
            "Buildings/Storage/LargeWarehouse/LargeWarehouse.Folktails.Model",
            "Buildings/Food/FoodFactory/FoodFactory.IronTeeth.Model",
        };

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // ------------------------------------------------------------ blueprint model paths

        /// <summary>
        /// Each of the eight blueprints must reference the faction-specific custom model for both
        /// the finished and the construction-stage prefab:
        /// <c>Buildings/Food/BoxFarm/BoxFarm.&lt;Tier&gt;.&lt;Faction&gt;.Model</c> and the matching
        /// <c>...ConstructionStage0.Model</c>.
        /// </summary>
        public static void TestBlueprintModelPathsPointAtCustomModels()
        {
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var blueprint = LoadBlueprint(tier, faction);

                    TestAssert.Equal(
                        FinishedModelPath(tier, faction),
                        ModelField(blueprint, tier, faction, construction: false),
                        BlueprintLabel(tier, faction) + " Children.#Finished.TimbermeshSpec.Model");

                    TestAssert.Equal(
                        ConstructionModelPath(tier, faction),
                        ModelField(blueprint, tier, faction, construction: true),
                        BlueprintLabel(tier, faction)
                        + " Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model");
                }
            }
        }

        // ------------------------------------------------------------ shipped mesh readability

        /// <summary>
        /// Every model path a blueprint references must resolve to a real, non-empty
        /// <c>data/&lt;path&gt;.timbermesh</c>. The file must carry a valid zlib header
        /// (CMF/FLG: compression method 8 and the FCHECK modulus-31 check), decompress to a
        /// non-empty protobuf payload (that is what the game's TimbermeshRepository loads) and the
        /// payload must contain the model's own name, which proves the copied asset belongs to the
        /// blueprint instead of being the wrong tier/faction file under the right name.
        /// </summary>
        public static void TestEveryReferencedModelIsAShippedReadableTimbermesh()
        {
            var dataRoot = DataRoot();

            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var blueprint = LoadBlueprint(tier, faction);
                    var references = new[]
                    {
                        ("finished", ModelField(blueprint, tier, faction, construction: false)),
                        ("construction-stage0", ModelField(blueprint, tier, faction, construction: true)),
                    };

                    foreach (var (kind, modelPath) in references)
                    {
                        var label = BlueprintLabel(tier, faction) + " " + kind + " model \"" + modelPath + "\"";
                        var meshPath = Path.Combine(
                            dataRoot,
                            (modelPath + ".timbermesh").Replace('/', Path.DirectorySeparatorChar));
                        TestAssert.True(File.Exists(meshPath), label + ": missing shipped mesh " + meshPath);

                        var compressed = File.ReadAllBytes(meshPath);
                        TestAssert.True(compressed.Length > 0, label + ": shipped mesh is empty (" + meshPath + ")");
                        TestAssert.True(compressed.Length >= 2, label + ": mesh is too short to carry a zlib header");
                        TestAssert.True((compressed[0] & 0x0F) == 8,
                            label + ": not a zlib stream (CMF compression method must be 8, got 0x"
                            + ((compressed[0] & 0x0F).ToString("X2")) + ")");
                        TestAssert.True((((compressed[0] << 8) | compressed[1]) % 31) == 0,
                            label + ": invalid zlib header (FCHECK mismatch: 0x"
                            + compressed[0].ToString("X2") + compressed[1].ToString("X2") + ")");

                        var payload = DecompressZlib(compressed);
                        TestAssert.True(payload.Length > 0, label + ": mesh decompressed to 0 bytes");

                        var stem = FileStem(modelPath);
                        var ascii = Encoding.ASCII.GetString(payload);
                        TestAssert.True(ascii.Contains(stem, StringComparison.Ordinal),
                            label + ": decompressed mesh payload does not contain \"" + stem
                            + "\" (wrong asset copied, or the file is not a timbermesh)");
                    }
                }
            }
        }

        // ------------------------------------------------------------ ART-04 marker contract

        /// <summary>
        /// ART-04: the decompressed payload of every shipped custom model must contain each of the
        /// three rear port marker empties exactly once and must contain no <c>#Slot#</c> marker node
        /// at all. This replaces the ART-03 "6 crop slots + 3 ports" expectation: the six
        /// <c>#Slot#Crop_0..5</c> nodes were removed when the roof became one empty flat platform,
        /// so an old mesh copied under a new name fails here instead of shipping silently.
        /// </summary>
        public static void TestCustomModelPayloadsCarryOnlyTheThreePortMarkers()
        {
            var dataRoot = DataRoot();

            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var blueprint = LoadBlueprint(tier, faction);
                    var references = new[]
                    {
                        ("finished", ModelField(blueprint, tier, faction, construction: false)),
                        ("construction-stage0", ModelField(blueprint, tier, faction, construction: true)),
                    };

                    foreach (var (kind, modelPath) in references)
                    {
                        var label = BlueprintLabel(tier, faction) + " " + kind + " model \"" + modelPath + "\"";
                        var payload = ShippedMeshPayload(dataRoot, modelPath, label);
                        var text = Encoding.UTF8.GetString(payload);

                        foreach (var marker in RequiredPortMarkers)
                        {
                            TestAssert.Equal(1, CountOccurrences(text, marker),
                                label + ": the decompressed payload must contain the port marker node \""
                                + marker + "\" exactly once (ART-04 keeps the three rear ports)");
                        }

                        TestAssert.Equal(0, CountOccurrences(text, ForbiddenSlotMarker),
                            label + ": the decompressed payload still contains \"" + ForbiddenSlotMarker
                            + "\" marker node(s); ART-04 removed the six crop slot markers #Slot#Crop_0..5 "
                            + "(the roof is one empty flat platform with nothing above it)");
                    }
                }
            }
        }

        // ------------------------------------------------------------ placeholder removal

        /// <summary>
        /// No model field of any shipped blueprint may still name a vanilla placeholder model.
        /// Checked recursively so a model reference nested in a child prefab cannot slip through,
        /// and matched with Ordinal <c>Contains</c> so appending a suffix to a placeholder path also
        /// fails.
        /// </summary>
        public static void TestNoBlueprintStillReferencesAVanillaPlaceholderModel()
        {
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var blueprint = LoadBlueprint(tier, faction);
                    var models = new List<string>();
                    CollectModelFields(blueprint, models);

                    TestAssert.True(models.Count >= 2,
                        BlueprintLabel(tier, faction) + ": expected at least the finished and construction model fields, found "
                        + models.Count);

                    foreach (var model in models)
                    {
                        foreach (var placeholder in VanillaPlaceholderModels)
                        {
                            TestAssert.False(model.Contains(placeholder, StringComparison.Ordinal),
                                BlueprintLabel(tier, faction) + " still references the vanilla placeholder "
                                + "\"" + placeholder + "\" (found \"" + model + "\")");
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------ animation slot specs

        /// <summary>
        /// The custom meshes ship no worker animation slot transforms, so the blueprints must not
        /// declare them at the root. A declared keyword without a matching transform throws
        /// <c>InvalidOperationException: There are no "&lt;keyword&gt;" slots in &lt;prefab&gt;</c>
        /// when the building is placed (smoke-test round 4, see data/ART_PLACEHOLDER.md).
        /// The root-only scope matches how the specs were removed: a copy nested in
        /// Children.#Unfinished makes placement throw as well.
        /// </summary>
        public static void TestBlueprintsShipNoAnimationSlotSpecsOnCustomModels()
        {
            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var blueprint = LoadBlueprint(tier, faction);

                    TestAssert.False(blueprint.TryGetProperty("TransformSlotInitializerSpec", out _),
                        BlueprintLabel(tier, faction)
                        + " must not declare TransformSlotInitializerSpec: the custom models have no slot transforms");
                    TestAssert.False(blueprint.TryGetProperty("WorkplaceSlotManagerSpec", out _),
                        BlueprintLabel(tier, faction)
                        + " must not declare WorkplaceSlotManagerSpec: the custom models have no slot transforms");
                }
            }
        }

        // ------------------------------------------------------------ tier icons

        /// <summary>
        /// The four tier icons must be the production renders, not the ~1 KB generated placeholder:
        /// a real 128x128 PNG (IHDR width/height parsed from the bytes) larger than 5000 bytes, with
        /// the sibling <c>.png.meta.json</c> still marked <c>{"isSprite": true}</c> for the game's
        /// ModSpriteConverter.
        /// </summary>
        public static void TestTierIconsAreProductionRenders()
        {
            var dataRoot = DataRoot();

            foreach (var tier in Tiers)
            {
                var pngPath = Path.Combine(dataRoot, "Icons", "BoxFarm" + tier + ".png");
                TestAssert.True(File.Exists(pngPath), tier + " icon is missing: " + pngPath);

                var bytes = File.ReadAllBytes(pngPath);
                TestAssert.True(bytes.Length > 5000,
                    tier + " icon is still a placeholder-sized file (" + bytes.Length
                    + " bytes at " + pngPath + "); expected the production render (> 5000 bytes)");
                TestAssert.True(bytes.Length >= 24, tier + " icon is too short to be a PNG: " + pngPath);

                for (var index = 0; index < PngSignature.Length; index++)
                {
                    TestAssert.Equal((int)PngSignature[index], (int)bytes[index],
                        tier + " icon is not a PNG (signature byte " + index + ")");
                }

                TestAssert.Equal(13, ReadBigEndianInt32(bytes, 8),
                    tier + " icon first chunk must be a 13-byte IHDR");
                TestAssert.Equal("IHDR", Encoding.ASCII.GetString(bytes, 12, 4),
                    tier + " icon first chunk type");
                TestAssert.Equal(128, ReadBigEndianInt32(bytes, 16), tier + " icon IHDR width");
                TestAssert.Equal(128, ReadBigEndianInt32(bytes, 20), tier + " icon IHDR height");

                var metaPath = pngPath + ".meta.json";
                TestAssert.True(File.Exists(metaPath), tier + " icon sprite metadata is missing: " + metaPath);
                var meta = LoadJson(metaPath, tier + " icon sprite metadata");
                var isSprite = Member(meta, "isSprite", metaPath);
                TestAssert.True(isSprite.ValueKind == JsonValueKind.True,
                    tier + " icon metadata must keep isSprite == true (found " + isSprite.ValueKind + ")");
            }
        }

        // ------------------------------------------------------------ art documentation

        /// <summary>
        /// The shipped art document must document the actual custom model paths, not the retired
        /// placeholder ones: all eight finished paths and all eight construction-stage paths.
        /// </summary>
        public static void TestArtDocDocumentsCustomModelPaths()
        {
            var docPath = Path.Combine(DataRoot(), "ART_PLACEHOLDER.md");
            TestAssert.True(File.Exists(docPath), "shipped art document is missing: " + docPath);
            var text = File.ReadAllText(docPath, Encoding.UTF8);

            foreach (var tier in Tiers)
            {
                foreach (var faction in Factions)
                {
                    var finished = FinishedModelPath(tier, faction);
                    TestAssert.True(text.Contains(finished, StringComparison.Ordinal),
                        "data/ART_PLACEHOLDER.md must document the finished model path \"" + finished + "\"");

                    var construction = ConstructionModelPath(tier, faction);
                    TestAssert.True(text.Contains(construction, StringComparison.Ordinal),
                        "data/ART_PLACEHOLDER.md must document the construction-stage model path \""
                        + construction + "\"");
                }
            }
        }

        // ------------------------------------------------------------ ART-04 art contract tags

        /// <summary>
        /// ART-04: data/ART_PLACEHOLDER.md must carry the five machine-readable ART_CONTRACT lines
        /// verbatim, so the shipped art document says the same thing as the data validator and the
        /// workstream doc (empty flat roof, no slots, the three ports, flat-patch materials, ground
        /// crops hidden by BUILD-05) instead of drifting back to the removed crop-trough contract.
        /// A line must be its own line (trailing CR/whitespace tolerated, extra wording not).
        /// </summary>
        public static void TestArtDocCarriesTheArt04ContractTags()
        {
            var docPath = Path.Combine(DataRoot(), "ART_PLACEHOLDER.md");
            TestAssert.True(File.Exists(docPath), "shipped art document is missing: " + docPath);

            var lines = File.ReadAllLines(docPath, Encoding.UTF8)
                .Select(line => line.TrimEnd('\r', ' ', '\t'))
                .ToList();

            foreach (var required in ArtContractLines)
            {
                TestAssert.True(lines.Contains(required, StringComparer.Ordinal),
                    "data/ART_PLACEHOLDER.md must contain the ART-04 machine-readable contract line \""
                    + required + "\" as its own line (found no exact line)");
            }
        }

        // ------------------------------------------------------------ helpers

        private static string BlueprintLabel(string tier, string faction)
            => "BoxFarm." + tier + "." + faction + ".blueprint.json";

        private static string FinishedModelPath(string tier, string faction)
            => CustomModelFolder + "/BoxFarm." + tier + "." + faction + ".Model";

        private static string ConstructionModelPath(string tier, string faction)
            => CustomModelFolder + "/BoxFarm." + tier + "." + faction + ".ConstructionStage0.Model";

        /// <summary>Repository root baked into this assembly by HDBF.Tests.csproj.</summary>
        private static string RepoRoot()
        {
            var metadata = typeof(ArtModelContractTests).Assembly
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

        private static JsonElement LoadBlueprint(string tier, string faction)
            => LoadJson(
                Path.Combine(DataRoot(), BoxFarmDataFolder.Replace('/', Path.DirectorySeparatorChar),
                    BlueprintLabel(tier, faction)),
                BlueprintLabel(tier, faction));

        private static JsonElement LoadJson(string path, string what)
        {
            TestAssert.True(File.Exists(path), "expected " + what + " at " + path);
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
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

        private static string StringMember(JsonElement element, string name, string context)
        {
            var value = Member(element, name, context);
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new TestFailureException(context + ": \"" + name + "\" must be a JSON string, got " + value.ValueKind);
            }

            return value.GetString()!;
        }

        /// <summary>
        /// Reads one of the two model references of a blueprint:
        /// <c>Children.#Finished.TimbermeshSpec.Model</c> or
        /// <c>Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model</c>.
        /// </summary>
        private static string ModelField(JsonElement blueprint, string tier, string faction, bool construction)
        {
            var context = BlueprintLabel(tier, faction);
            var children = Member(blueprint, "Children", context);
            JsonElement prefab;
            if (construction)
            {
                var unfinished = Member(children, "#Unfinished", context);
                prefab = Member(Member(unfinished, "Children", context), "ConstructionStage0", context);
            }
            else
            {
                prefab = Member(children, "#Finished", context);
            }

            return StringMember(Member(prefab, "TimbermeshSpec", context), "Model", context);
        }

        /// <summary>Recursively collects every string value of a JSON property named "Model".</summary>
        private static void CollectModelFields(JsonElement element, List<string> models)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "Model", StringComparison.Ordinal)
                            && property.Value.ValueKind == JsonValueKind.String)
                        {
                            models.Add(property.Value.GetString()!);
                        }

                        CollectModelFields(property.Value, models);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        CollectModelFields(item, models);
                    }

                    break;
            }
        }

        private static byte[] DecompressZlib(byte[] compressed)
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>Reads <c>data/&lt;modelPath&gt;.timbermesh</c> and returns its decompressed payload.</summary>
        private static byte[] ShippedMeshPayload(string dataRoot, string modelPath, string label)
        {
            var meshPath = Path.Combine(
                dataRoot, (modelPath + ".timbermesh").Replace('/', Path.DirectorySeparatorChar));
            TestAssert.True(File.Exists(meshPath), label + ": missing shipped mesh " + meshPath);
            return DecompressZlib(File.ReadAllBytes(meshPath));
        }

        /// <summary>Counts non-overlapping ordinal occurrences (ART-04: each port marker exactly once).</summary>
        private static int CountOccurrences(string haystack, string needle)
        {
            if (needle.Length == 0)
            {
                return 0;
            }

            var count = 0;
            var index = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>File name of a blueprint model path, e.g. "BoxFarm.Basic.Folktails.Model".</summary>
        private static string FileStem(string modelPath)
        {
            var separator = modelPath.LastIndexOf('/');
            return separator < 0 ? modelPath : modelPath.Substring(separator + 1);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
            => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}
