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
    /// ART-03/v2 L1 data contract: every BoxFarm blueprint must point at the custom, shipped
    /// <c>.timbermesh</c> art, and the shipped files must really be readable meshes.
    ///
    /// This layer reads the REAL files under <c>data/</c> (the repository root is baked into the
    /// test assembly as <c>AssemblyMetadata("HdbfRepoRoot", ...)</c> because the build output lives
    /// outside the repo, see HDBF.Tests.csproj). No Timberborn or Unity assembly is referenced and
    /// none is loaded (REQ-TEST-03): <c>System.Text.Json</c> parses the blueprints,
    /// <c>System.IO.Compression.ZLibStream</c> decompresses the mesh payload and a small protobuf
    /// wire-format reader decodes the timbermesh Model/Node/Mesh messages, so the test can prove
    /// the asset is a real zlib-compressed timbermesh containing the expected model name, the three
    /// port markers and a strict 3x3x1 envelope ("loadable"), without the game's protobuf types.
    ///
    /// v2 (2026-10-26): the four tiers are all strict <b>3.00 x 1.00 x 3.00</b> indoor farms with
    /// ONE flat empty roof platform whose walking surface is the model apex at Y = 1.00 (nothing
    /// above it), exactly the three port empties <c>#Port#Power/#Port#Pipe/#Port#Logistics</c> and
    /// no <c>#Slot#</c> node anywhere. Geometry (mesh topology, materials, flat-patch UV, LOD
    /// budgets) is enforced by <c>art/validate_models.py</c> / <c>art/validate_timbermesh.py</c>;
    /// this layer pins what the data container and the shipped documentation can prove.
    ///
    /// Why this exists: v0.1.0 pointed the eight blueprints at vanilla models (GathererFlag, ...)
    /// and carried their animation slot specs. A regression back to a vanilla model, back to a
    /// missing model file, back to a 3x2 or slot-carrying mesh, or back to a slot spec that a
    /// custom model does not provide makes the building unplaceable/invisible/incorrect in game,
    /// and would otherwise only show up in a smoke test.
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

        /// <summary>
        /// v2 machine-readable art contract lines that data/ART_PLACEHOLDER.md must carry
        /// on top of the ART-04 lines. They pin the 3x3x1 envelope and the flat-roof rule
        /// at the text level (the geometry itself is checked by
        /// <see cref="TestShippedModelsAreStrict3x3x1FlatRoofTimbermeshes"/>).
        /// </summary>
        private static readonly string[] V2ArtContractLines =
        {
            "ART_CONTRACT: envelope=3.00x1.00x3.00",
            "ART_CONTRACT: roof-top=1.00",
            "ART_CONTRACT: slot-markers=0",
        };

        /// <summary>
        /// v2 frozen marker positions in the game's block-corner model space (export = contract
        /// + (1.5, 0, 1.5) for the 3x3 footprint). A shipped mesh whose port markers moved fails
        /// the pipeline/blueprint port wiring.
        /// </summary>
        private static readonly Dictionary<string, (double X, double Y, double Z)> ExpectedPortPositions = new()
        {
            ["#Port#Power"] = (2.50, 0.85, 3.00),
            ["#Port#Pipe"] = (0.00, 0.40, 1.50),
            ["#Port#Logistics"] = (0.50, 0.35, 3.00),
        };

        private const int V2Lod0TriangleBudget = 6000;

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

        // ------------------------------------------------------------ v2 3x3x1 envelope

        /// <summary>
        /// v2 (REQ-V2-BLD-01/03/06, REQ-V2-ART-01/02): every shipped custom model must
        /// decode as a REAL timbermesh (zlib + protobuf wire format, not just a string
        /// container), keep exactly the three #Port# empties and no #Slot# node, and measure
        /// a strict 3.00 x 1.00 x 3.00 block-corner envelope whose flat roof walking surface
        /// is the model apex at Y = 1.00 with nothing above it (30 cm clearance / stackable).
        /// This pins the container + envelope half of the v2 contract in L1; the fine
        /// geometry/material/UV rules remain in art/validate_timbermesh.py.
        /// </summary>
        public static void TestShippedModelsAreStrict3x3x1FlatRoofTimbermeshes()
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
                        var model = ParseTimbermesh(payload, label);
                        var stem = FileStem(modelPath);

                        TestAssert.Equal(stem, model.Name,
                            label + ": decoded Model.name must equal the file stem");
                        TestAssert.Equal(0, model.Version,
                            label + ": decoded Model.version must be 0");
                        TestAssert.Equal(4, model.Nodes.Count,
                            label + ": v2 model must have exactly 4 nodes (1 body + 3 #Port#)");

                        var portNames = new List<string>();
                        var totalTriangles = 0;
                        var bodyMeshes = 0;
                        foreach (var node in model.Nodes)
                        {
                            if (node.Name.StartsWith("#Port#", StringComparison.Ordinal))
                            {
                                portNames.Add(node.Name);
                            }
                            else if (node.Name.Contains("#Slot#", StringComparison.Ordinal))
                            {
                                throw new TestFailureException(
                                    label + ": decoded model still contains a #Slot# node \"" + node.Name + "\"");
                            }

                            foreach (var mesh in node.Meshes)
                            {
                                bodyMeshes++;
                                totalTriangles += mesh.Indices.Count / 3;
                            }
                        }

                        TestAssert.Equal(3, portNames.Count,
                            label + ": decoded model must carry exactly the three #Port# empties");
                        TestAssert.True(bodyMeshes >= 1,
                            label + ": decoded model carries no mesh at all (not loadable)");
                        TestAssert.True(totalTriangles > 0,
                            label + ": decoded model has no triangles");
                        TestAssert.True(totalTriangles <= V2Lod0TriangleBudget,
                            label + ": decoded model has " + totalTriangles + " triangles, over the "
                            + V2Lod0TriangleBudget + " LOD0 budget");

                        // frozen marker positions (game/block-corner space)
                        foreach (var pair in ExpectedPortPositions)
                        {
                            var node = model.Nodes.Find(n => string.Equals(n.Name, pair.Key, StringComparison.Ordinal));
                            TestAssert.NotNull(node, label + ": missing port marker \"" + pair.Key + "\"");
                            TestAssert.True(node!.HasPosition,
                                label + ": port marker \"" + pair.Key + "\" has no position");
                            TestAssert.Equal(pair.Value.X, node.PositionX, 0.02,
                                label + ": " + pair.Key + ".X");
                            TestAssert.Equal(pair.Value.Y, node.PositionY, 0.02,
                                label + ": " + pair.Key + ".Y");
                            TestAssert.Equal(pair.Value.Z, node.PositionZ, 0.02,
                                label + ": " + pair.Key + ".Z");
                            TestAssert.Equal(0, node.VertexCount,
                                label + ": " + pair.Key + " must be an empty marker (vertexCount)");
                        }

                        var body = model.Nodes.Find(n =>
                            string.Equals(n.Name, stem, StringComparison.Ordinal));
                        TestAssert.NotNull(body, label + ": no root body node named \"" + stem + "\"");
                        TestAssert.True(body!.VertexCount > 0,
                            label + ": the body node carries no vertices");

                        // strict 3x3x1 block-corner envelope; flat roof = apex at Y = 1.00
                        var envelope = MeasureEnvelope(model);
                        TestAssert.Equal(3.00, envelope.MaxX - envelope.MinX, 0.02, label + ": envelope X size");
                        TestAssert.Equal(1.00, envelope.MaxY - envelope.MinY, 0.02, label + ": envelope Y size");
                        TestAssert.Equal(3.00, envelope.MaxZ - envelope.MinZ, 0.02, label + ": envelope Z size");
                        TestAssert.Equal(0.00, envelope.MinX, 0.02, label + ": envelope min X");
                        TestAssert.Equal(0.00, envelope.MinY, 0.02, label + ": envelope min Y");
                        TestAssert.Equal(0.00, envelope.MinZ, 0.02, label + ": envelope min Z");
                        TestAssert.Equal(1.00, envelope.MaxY, 0.005,
                            label + ": the flat roof walking surface must be the model apex at Y = 1.00");
                        TestAssert.Equal(0, envelope.VerticesAboveTop,
                            label + ": " + envelope.VerticesAboveTop
                            + " vertex/vertices exist above the roof walking surface Y = 1.00");
                        TestAssert.True(envelope.VerticesAtTop >= 8,
                            label + ": no flat roof top face found at Y = 1.00");
                    }
                }
            }
        }

        /// <summary>
        /// v2: data/ART_PLACEHOLDER.md must carry the three additional machine-readable
        /// contract lines (envelope / roof-top / slot-markers) verbatim as their own lines.
        /// </summary>
        public static void TestArtDocCarriesTheV2ContractTags()
        {
            var docPath = Path.Combine(DataRoot(), "ART_PLACEHOLDER.md");
            TestAssert.True(File.Exists(docPath), "shipped art document is missing: " + docPath);

            var lines = File.ReadAllLines(docPath, Encoding.UTF8)
                .Select(line => line.TrimEnd('\r', ' ', '\t'))
                .ToList();

            foreach (var required in V2ArtContractLines)
            {
                TestAssert.True(lines.Contains(required, StringComparer.Ordinal),
                    "data/ART_PLACEHOLDER.md must contain the v2 machine-readable contract line \""
                    + required + "\" as its own line (found no exact line)");
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

        // --- minimal timbermesh protobuf reader -------------------------------------------------
        //
        // A .timbermesh is zlib(protobuf Model) with this vendored schema (art/tools/
        // timbermesh_blender_plugin/model_pb2.py):
        //   Model { version=1:int32, name=2:string, nodes=3:repeated Node }
        //   Node  { parent=1:int32, name=2:string, position=3:Vector3Float,
        //           vertexCount=6:int32, vertexProperties=7:repeated VertexProperty,
        //           meshes=8:repeated Mesh }
        //   Vector3Float { x=1:float, y=2:float, z=3:float }
        //   VertexProperty { name=1:string, scalarType=2:enum, scalarTypeDimension=3:int32,
        //                    data=4:bytes }
        //   Mesh { indices=1:repeated int32 (packed), material=2:string }
        // Reading it here proves the payload is a loadable model (not merely a string
        // container), which is what the game's TimbermeshRepository does.

        private sealed class TimbermeshModel
        {
            public int Version;
            public string Name = string.Empty;
            public List<TimbermeshNode> Nodes { get; } = new();
        }

        private sealed class TimbermeshNode
        {
            public string Name = string.Empty;
            public bool HasPosition;
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public int VertexCount;
            public List<TimbermeshProperty> Properties { get; } = new();
            public List<TimbermeshMesh> Meshes { get; } = new();
        }

        private sealed class TimbermeshProperty
        {
            public string Name = string.Empty;
            public int ScalarTypeDimension;
            public byte[] Data = Array.Empty<byte>();
        }

        private sealed class TimbermeshMesh
        {
            public List<int> Indices { get; } = new();
            public string Material = string.Empty;
        }

        private sealed class Envelope
        {
            public Envelope(double minX, double minY, double minZ, double maxX, double maxY,
                double maxZ, int verticesAtTop, int verticesAboveTop)
            {
                MinX = minX;
                MinY = minY;
                MinZ = minZ;
                MaxX = maxX;
                MaxY = maxY;
                MaxZ = maxZ;
                VerticesAtTop = verticesAtTop;
                VerticesAboveTop = verticesAboveTop;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MinZ { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public double MaxZ { get; }
            public int VerticesAtTop { get; }
            public int VerticesAboveTop { get; }
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
            {
                // proto int32 negative values (e.g. Node.parent == -1) are encoded as a
                // 10-byte varint of the two's-complement ulong; decode without overflow.
                return unchecked((int)ReadVarint());
            }

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

            public float ReadFloat()
            {
                if (_offset + 4 > _data.Length)
                {
                    throw new InvalidDataException("truncated fixed32 field");
                }

                var value = BitConverter.ToSingle(_data, _offset);
                _offset += 4;
                return value;
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
            catch (Exception ex) when (ex is not TestFailureException)
            {
                throw new TestFailureException(
                    label + ": decompressed payload is not a decodable timbermesh protobuf ("
                    + ex.GetType().Name + ": " + ex.Message + ")");
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
                        model.Version = checked((int)reader.ReadVarint());
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
                        _ = reader.ReadInt32(); // parent (-1 for the root node)
                        break;
                    case 2:
                        node.Name = Encoding.UTF8.GetString(reader.ReadBytes());
                        break;
                    case 3:
                        ParseVector3(reader.ReadBytes(), node);
                        break;
                    case 6:
                        node.VertexCount = checked((int)reader.ReadVarint());
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

        private static void ParseVector3(byte[] payload, TimbermeshNode node)
        {
            var reader = new ProtobufReader(payload);
            while (!reader.AtEnd)
            {
                var tag = reader.ReadVarint();
                var field = (int)(tag >> 3);
                var wire = (int)(tag & 0x07);
                switch (field)
                {
                    case 1:
                        node.PositionX = reader.ReadFloat();
                        node.HasPosition = true;
                        break;
                    case 2:
                        node.PositionY = reader.ReadFloat();
                        node.HasPosition = true;
                        break;
                    case 3:
                        node.PositionZ = reader.ReadFloat();
                        node.HasPosition = true;
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }
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
                        property.ScalarTypeDimension = checked((int)reader.ReadVarint());
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

        private static Envelope MeasureEnvelope(TimbermeshModel model)
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var minZ = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            var maxZ = double.NegativeInfinity;
            var verticesAtTop = 0;
            var verticesAboveTop = 0;
            var vertexCount = 0;

            foreach (var node in model.Nodes)
            {
                foreach (var property in node.Properties)
                {
                    if (!string.Equals(property.Name, "position", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (property.ScalarTypeDimension != 3)
                    {
                        throw new TestFailureException(
                            "timbermesh position property has dimension "
                            + property.ScalarTypeDimension + ", expected 3");
                    }

                    if (property.Data.Length % 12 != 0)
                    {
                        throw new TestFailureException(
                            "timbermesh position data length " + property.Data.Length
                            + " is not a multiple of 12 bytes");
                    }

                    for (var offset = 0; offset < property.Data.Length; offset += 12)
                    {
                        var x = (double)BitConverter.ToSingle(property.Data, offset) + node.PositionX;
                        var y = (double)BitConverter.ToSingle(property.Data, offset + 4) + node.PositionY;
                        var z = (double)BitConverter.ToSingle(property.Data, offset + 8) + node.PositionZ;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        minZ = Math.Min(minZ, z);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        maxZ = Math.Max(maxZ, z);
                        vertexCount++;
                        if (Math.Abs(y - 1.0) <= 0.01)
                        {
                            verticesAtTop++;
                        }

                        if (y > 1.0 + 1e-4)
                        {
                            verticesAboveTop++;
                        }
                    }
                }
            }

            if (vertexCount == 0)
            {
                throw new TestFailureException("timbermesh contains no vertex position data");
            }

            return new Envelope(minX, minY, minZ, maxX, maxY, maxZ, verticesAtTop,
                verticesAboveTop);
        }

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
