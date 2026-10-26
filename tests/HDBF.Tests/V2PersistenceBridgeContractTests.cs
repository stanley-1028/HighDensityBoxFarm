// ============================================================================
// HDBF v2 | Layer: L1 tests | Owner: Lead (replacement for a flaky card test that
//          mirrored the keyed bridge instead of testing the shipped sources)
// Purpose: Deterministic contract for the schema-v2 persistence bridge: published key
//          names, validate-before-write, tolerant load, legacy v1 read, no world-plot
//          or vanilla-farming API, header/size gates, and a real Core payload round-trip.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-02, REQ-V2-SAVE-04, REQ-V2-SAVE-06,
//               REQ-V2-MAINT-04, REQ-V2-SAFE-01, REQ-V2-READ-02
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace HDBF.Tests
{
    /// <summary>
    /// Source-contract + Core behaviour tests of the shipped indoor save bridge. Reads the real
    /// <c>src/HDBF.Persistence/Indoor</c> files (the bridge references Timberborn and cannot be
    /// loaded into this game-free assembly) and exercises the real Core codec/validator/migrator.
    /// Every test builds its own records: no shared mutable fixture, so ordering cannot matter.
    /// </summary>
    public static class V2PersistenceBridgeContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string BridgeDirectory = "src/HDBF.Persistence/Indoor";

        private static readonly string[] KeyNames =
        {
            "HDBF.IndoorFarm", "Payload", "HDBF.BoxFarm", "SchemaVersion", "EntityId", "Tier",
            "Faction", "Mode", "CropPolicy", "FixedCropId", "Tank", "MaintenanceDueDay",
            "MaintenanceCyclesMissed", "PowerConnected", "PowerMode", "PortPipe", "PortPower",
            "PortLogistics", "TimberPipesAdapter", "SlotCropIds", "SlotGrowth", "SlotFatigue",
            "SlotLastCropIds", "SlotStages", "SlotFlags", "PlotIds", "CropIds", "Fatigue",
            "DueDay", "TankWater",
        };

        private static readonly string[] ForbiddenTokens =
        {
            "NaturalResourceFactory", "PlantingService", "ITerrainService", "SetField",
            "PlotRegistry", "PlantingAreaValidator", "HarmonyLib", "[HarmonyPatch]", "Transpiler",
            "NaturalResourceFactory.PlantNew",
        };

        /// <summary>Every published v2/v1 key name must stay declared in one table.</summary>
        public static void TestKeyTableDeclaresEveryPublishedKey()
        {
            var keys = File.ReadAllText(Path.Combine(BridgeDirectory, "IndoorSaveKeys.cs"));
            for (var index = 0; index < KeyNames.Length; index++)
            {
                TestAssert.True(keys.Contains("\"" + KeyNames[index] + "\"", StringComparison.Ordinal),
                    "IndoorSaveKeys.cs is missing the published key '" + KeyNames[index] + "'");
            }

            Console.WriteLine("[v2-save] key table: " + KeyNames.Length + " published v2/v1 key names present");
        }

        /// <summary>REQ-V2-SAVE-04: validation runs on a copy before anything is written.</summary>
        public static void TestSaveValidatesBeforeWriteAndNeverThrows()
        {
            var source = ReadBridge("IndoorEntitySaveBridge.cs");
            var save = Slice(source, "public static bool Save(", "public static IndoorFarmRecord? Load(");
            var validate = save.IndexOf("ValidateAndRepair", StringComparison.Ordinal);
            var write = save.IndexOf("component.Set(", StringComparison.Ordinal);
            TestAssert.True(validate >= 0, "the save path must validate before writing (REQ-V2-SAVE-04)");
            TestAssert.True(write >= 0, "the save path must write through the entity component Set()");
            TestAssert.True(validate < write, "validation must run before the write");
            TestAssert.True(save.Contains("catch (Exception", StringComparison.Ordinal),
                "the save path must catch every exception");
            TestAssert.True(save.Contains("return false", StringComparison.Ordinal),
                "a failed save must return false without throwing");
            Console.WriteLine("[v2-save] save: validate-before-write + guarded failure path");
        }

        /// <summary>REQ-V2-SAVE-06: load is tolerant and reports a repair/degrade summary.</summary>
        public static void TestLoadIsTolerantAndReportsDegraded()
        {
            var source = ReadBridge("IndoorEntitySaveBridge.cs");
            var load = Slice(source, "public static IndoorFarmRecord? Load(", "public static bool Save(");
            if (load.Length == 0)
            {
                // Load may be documented before Save in this file; slice the other way round.
                load = Slice(source, "public static IndoorFarmRecord? Load(", "private static");
            }

            TestAssert.True(load.Contains("TryDecode", StringComparison.Ordinal),
                "load must use the tolerant codec (REQ-V2-SAVE-06)");
            TestAssert.True(load.Contains("ValidateAndRepair", StringComparison.Ordinal),
                "load must repair/validate the decoded record");
            TestAssert.True(load.Contains("degraded", StringComparison.Ordinal),
                "load must report whether the record was repaired");
            TestAssert.True(load.Contains("catch (Exception", StringComparison.Ordinal),
                "load must never throw into the game's load callback");
        }

        /// <summary>REQ-V2-SAVE-02: the v1 component/keys stay readable and feed the migrator.</summary>
        public static void TestLegacyV1PathFeedsTheMigrator()
        {
            var legacy = ReadBridge("IndoorEntitySaveBridge.Legacy.cs");
            TestAssert.True(legacy.Contains("LegacyComponentKeyName", StringComparison.Ordinal),
                "the legacy reader must target the published HDBF.BoxFarm component");
            TestAssert.True(legacy.Contains("IndoorSaveMigrator", StringComparison.Ordinal),
                "the legacy reader must delegate mapping to IndoorSaveMigrator");
            TestAssert.True(legacy.Contains("TryMigrateV1", StringComparison.Ordinal),
                "the legacy reader must call the v1 migration entry point");
            TestAssert.True(legacy.Contains("catch (Exception", StringComparison.Ordinal),
                "a broken v1 record must degrade, never throw");
        }

        /// <summary>REQ-V2-CORE-03 / SAFE-05: no world plot, vanilla farming or Harmony API.</summary>
        public static void TestBridgeUsesNoWorldPlotOrVanillaFarmingApi()
        {
            var files = Directory.GetFiles(BridgeDirectory, "*.cs");
            for (var index = 0; index < files.Length; index++)
            {
                var text = File.ReadAllText(files[index]);
                for (var token = 0; token < ForbiddenTokens.Length; token++)
                {
                    TestAssert.False(text.Contains(ForbiddenTokens[token], StringComparison.Ordinal),
                        Path.GetFileName(files[index]) + " must not use " + ForbiddenTokens[token]);
                }
            }

            Console.WriteLine("[v2-save] bridge: no world-plot/vanilla-farming/Harmony API");
        }

        /// <summary>REQ-V2-READ-02 / MAINT-04: header template, size gate, no static mutable state.</summary>
        public static void TestBridgeFilesKeepHeaderSizeAndStaticGate()
        {
            var files = Directory.GetFiles(BridgeDirectory, "*.cs");
            TestAssert.True(files.Length > 0, "no persistence bridge sources found");
            for (var index = 0; index < files.Length; index++)
            {
                var name = Path.GetFileName(files[index]);
                var lines = File.ReadAllLines(files[index]);
                TestAssert.True(lines.Length <= 300, name + " exceeds the 300 line soft gate");
                var head = string.Join("\n", lines.Take(12));
                TestAssert.True(head.Contains("HDBF v2 |", StringComparison.Ordinal), name + ": header template missing");
                TestAssert.True(head.Contains("Purpose:", StringComparison.Ordinal), name + ": Purpose line missing");
                TestAssert.True(head.Contains("Requirements:", StringComparison.Ordinal),
                    name + ": Requirements line missing");

                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex].Trim();
                    if (!line.StartsWith("private static ", StringComparison.Ordinal)
                        && !line.StartsWith("public static ", StringComparison.Ordinal)
                        && !line.StartsWith("internal static ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.Contains(" class ", StringComparison.Ordinal)
                        || line.Contains("partial class", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var equals = line.IndexOf(" = ", StringComparison.Ordinal);
                    if (equals < 0)
                    {
                        continue; // method signature, not a field initializer
                    }

                    var paren = line.IndexOf('(');
                    if (paren >= 0 && paren < equals)
                    {
                        continue; // method signature with a default parameter
                    }

                    TestAssert.True(line.Contains(" const ", StringComparison.Ordinal)
                            || line.Contains(" readonly ", StringComparison.Ordinal),
                        name + ":" + (lineIndex + 1) + " has static mutable state (" + line + ")");
                }
            }

            Console.WriteLine("[v2-save] bridge: " + files.Length + " files, headers + <=300 lines + no static mutable state");
        }

        /// <summary>REQ-V2-SAVE-01: real Core codec round-trip is byte-stable and validate-clean.</summary>
        public static void TestCorePayloadRoundTripIsByteStable()
        {
            var record = FreshRecord(HdbfTier.Intermediate, HdbfFaction.IronTeeth, planted: 40, growth: 1.25f);
            var profile = TierCatalog.Get(HdbfTier.Intermediate);
            var catalog = FallbackCropCatalog.Default;

            var repair = IndoorSaveValidator.ValidateAndRepair(record, profile, catalog, null);
            TestAssert.False(repair.Repaired, "a clean record must not need repair");

            var first = IndoorSaveCodec.Encode(record);
            TestAssert.True(IndoorSaveCodec.TryDecode(first, out var decoded, out var error),
                "clean payload must decode: " + error);
            TestAssert.Equal(string.Empty, error ?? string.Empty);
            TestAssert.Equal(first, IndoorSaveCodec.Encode(decoded), "decode -> encode must be byte stable");
            TestAssert.Equal(record.Slots.Length, decoded.Slots.Length, "slot count must survive the round-trip");
            TestAssert.Equal(record.Tank, decoded.Tank, 1e-4f, "tank must survive the round-trip");

            Console.WriteLine("[v2-save] payload round-trip: byte-stable, " + first.Length + " chars");
        }

        /// <summary>REQ-V2-SAVE-04: non-finite values are repaired, and the repaired text is stable.</summary>
        public static void TestCoreValidatorRepairsNonFiniteValues()
        {
            var record = FreshRecord(HdbfTier.Basic, HdbfFaction.Folktails, planted: 10, growth: 0.5f);
            record.Tank = float.NaN;
            record.Slots[0].Fatigue = float.PositiveInfinity;
            record.Slots[1].GrowthDays = -5f;

            var repair = IndoorSaveValidator.ValidateAndRepair(
                record, TierCatalog.Get(HdbfTier.Basic), FallbackCropCatalog.Default, null);
            TestAssert.True(repair.Repaired, "NaN/Inf/negative values must be reported as repaired");
            TestAssert.True(float.IsFinite(record.Tank), "tank must be finite after repair");
            TestAssert.True(record.Tank >= 0f && record.Tank <= TierCatalog.Get(HdbfTier.Basic).TankCapacity,
                "tank must be clamped into [0, capacity]");
            TestAssert.True(float.IsFinite(record.Slots[0].Fatigue) && record.Slots[0].Fatigue <= 1f,
                "fatigue must be finite and clamped to <=1");
            TestAssert.True(record.Slots[1].GrowthDays >= 0f, "growth days must not stay negative");

            var text = IndoorSaveCodec.Encode(record);
            TestAssert.True(IndoorSaveCodec.TryDecode(text, out var decoded, out _),
                "the repaired record must still encode/decode");
            TestAssert.Equal(text, IndoorSaveCodec.Encode(decoded), "repaired payload stays stable");
        }

        /// <summary>REQ-V2-SAVE-02: v1 plots map to slots and scalars are preserved.</summary>
        public static void TestV1MigrationMapsPlotsAndPreservesScalars()
        {
            var legacy = new IndoorLegacyV1Record
            {
                EntityId = "42",
                Tier = HdbfTier.Advanced,
                Faction = HdbfFaction.IronTeeth,
                Mode = HdbfMode.Dense,
                PlotIds = new[] { 10, 11, 12 },
                Crops = new[] { "Canola", "Cassava", "Corn" },
                Fatigue = new[] { 0.2f, 0.5f, 0.1f },
                Tank = 12.5f,
                MaintenanceDueDay = 17f,
                MaintenanceCyclesMissed = 1,
                PowerConnected = false,
                PipePort = true,
                LogisticsPort = true,
                PowerPort = false,
                TimberPipesAdapter = "Degraded",
            };

            var profile = TierCatalog.Get(HdbfTier.Advanced);
            TestAssert.True(IndoorSaveMigrator.Default.TryMigrateV1(legacy, profile, out var migrated),
                "a valid v1 record must migrate");
            TestAssert.Equal(profile.SlotCapacity, migrated.Slots.Length, "migrated slots must match the tier capacity");
            TestAssert.Equal("Canola", migrated.Slots[0].CropId);
            TestAssert.Equal("Corn", migrated.Slots[2].CropId);
            TestAssert.Equal(0.2f, migrated.Slots[0].Fatigue, 1e-4f);
            TestAssert.Equal(12.5f, migrated.Tank, 1e-4f, "tank must be preserved by v1 -> v2 migration");
            TestAssert.Equal(17f, migrated.MaintenanceDueDay, 1e-4f, "maintenance due day must be preserved");
            TestAssert.Equal(1, migrated.MaintenanceCyclesMissed);
            TestAssert.Equal(IndoorMode.Dense, migrated.Mode);
            TestAssert.Equal("Degraded", migrated.TimberPipesAdapter);

            Console.WriteLine("[v2-save] v1 -> v2: 3 plots mapped into " + profile.SlotCapacity + " slots, scalars preserved");
        }

        /// <summary>
        /// V2-SAVE-01 keyed bridge: only the four game serializer primitives, validate a copy
        /// before the one complete write, and the total save/load boundaries (no throw, guarded
        /// reads, SafeDefault recovery). Text contract only: the keyed partials reference
        /// Timberborn, so they are pinned as shipped source like the payload bridge above.
        /// </summary>
        public static void TestKeyedBridgeFollowsThePrimitiveAndFailSafeContract()
        {
            var keyedFiles = new[]
            {
                "IndoorSaveKeys.cs", "IndoorEntitySaveBridge.Keyed.cs",
                "IndoorEntitySaveBridge.KeyedFields.cs", "IndoorEntitySaveBridge.KeyedReaders.cs",
                "IndoorEntitySaveBridge.KeyedSupport.cs",
            };
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "int", "float", "bool", "string" };
            var declarations = 0;
            for (var fileIndex = 0; fileIndex < keyedFiles.Length; fileIndex++)
            {
                var text = ReadBridge(keyedFiles[fileIndex]);
                foreach (var marker in new[] { "new PropertyKey<", "new ListKey<" })
                {
                    var index = 0;
                    while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
                    {
                        var typeStart = index + marker.Length;
                        var typeEnd = text.IndexOf('>', typeStart);
                        TestAssert.True(typeEnd > typeStart, keyedFiles[fileIndex] + ": malformed key declaration");
                        var type = text.Substring(typeStart, typeEnd - typeStart).Trim();
                        TestAssert.True(allowed.Contains(type),
                            keyedFiles[fileIndex] + " persists unsupported primitive '" + type
                            + "' (only int/float/bool/string, docs/API_FACTS.md section 6)");
                        declarations++;
                        index = typeEnd;
                    }
                }

                TestAssert.False(text.Contains("PropertyKey<long>", StringComparison.Ordinal),
                    keyedFiles[fileIndex] + ": Int64 is not serializable (EntityId must be text)");
            }

            TestAssert.True(declarations >= 32, "the keyed surface declares too few keys: " + declarations);

            var keyed = ReadBridge("IndoorEntitySaveBridge.Keyed.cs");
            var save = Slice(keyed, "public static bool Save(", "public static IndoorFarmRecord? Load(");
            var clone = save.IndexOf("Clone(record)", StringComparison.Ordinal);
            var validate = save.IndexOf("ValidateAndRepair", StringComparison.Ordinal);
            var write = save.IndexOf("WriteRecord(component, candidate)", StringComparison.Ordinal);
            TestAssert.True(clone >= 0 && validate > clone,
                "REQ-V2-SAVE-04: the keyed write path must validate a copy, not the live record");
            TestAssert.True(write > validate, "the complete record must be written only after validation");
            TestAssert.True(save.Contains("validation.Repaired", StringComparison.Ordinal),
                "a candidate that needed repair must be refused");
            TestAssert.True(save.Contains("catch (Exception", StringComparison.Ordinal)
                            && save.Contains("return false", StringComparison.Ordinal),
                "the keyed save boundary must catch and return false (REQ-V2-SAFE-01)");

            var load = Slice(keyed, "public static IndoorFarmRecord? Load(", "private static bool TryReadKeyed");
            foreach (var token in new[]
                     {
                         "TryReadKeyed(", "HasLegacyRecord(loader)",
                         "IndoorSaveMigrator.RepairDefaults", "IndoorLogKeys.MigratedFailed",
                         "catch (Exception", "return null",
                     })
            {
                TestAssert.True(load.Contains(token, StringComparison.Ordinal),
                    "the keyed load must keep its guarded recovery contract: " + token);
            }

            var allKeyed = new System.Text.StringBuilder();
            for (var index = 0; index < keyedFiles.Length; index++)
            {
                allKeyed.Append(ReadBridge(keyedFiles[index])).Append('\n');
            }

            TestAssert.False(allKeyed.ToString().Contains("throw new", StringComparison.Ordinal),
                "the keyed bridge must not throw at a game boundary (REQ-V2-SAVE-04/SAFE-01)");
            var readers = ReadBridge("IndoorEntitySaveBridge.KeyedReaders.cs");
            var guardCount = 0;
            var probe = 0;
            while ((probe = readers.IndexOf("catch (Exception)", probe, StringComparison.Ordinal)) >= 0)
            {
                guardCount++;
                probe += "catch (Exception)".Length;
            }

            TestAssert.True(guardCount >= 7,
                "each scalar/list reader needs its own catch guard, found " + guardCount);
            Console.WriteLine("[v2-save] keyed bridge: " + declarations
                              + " primitive key declarations, validate-before-write, guarded readers");
        }

        // ------------------------------------------------------------------ helpers

        private static IndoorFarmRecord FreshRecord(HdbfTier tier, HdbfFaction faction, int planted, float growth)
        {
            var record = IndoorFarmRecord.Create(tier, faction);
            var crops = FallbackCropCatalog.Default.ForFaction(faction);
            var crop = crops.Count > 0 ? crops[0].Id : string.Empty;
            for (var index = 0; index < record.Slots.Length; index++)
            {
                var slot = record.Slots[index];
                if (index < planted)
                {
                    slot.CropId = crop;
                    slot.GrowthDays = growth;
                    slot.Stage = SlotStage.Growing;
                    slot.Fatigue = (index % 5) * 0.1f;
                    slot.LastCropId = index % 2 == 0 ? string.Empty : crop;
                }
            }

            return record;
        }

        private static string ReadBridge(string fileName)
        {
            var path = Path.Combine(RepoRoot(), BridgeDirectory, fileName);
            TestAssert.True(File.Exists(path), "persistence bridge source missing: " + path);
            return File.ReadAllText(path);
        }

        private static string Slice(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        private static string RepoRoot()
        {
            var attribute = typeof(V2PersistenceBridgeContractTests).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
                .OfType<AssemblyMetadataAttribute>()
                .FirstOrDefault(entry => string.Equals(entry.Key, RepoRootKey, StringComparison.Ordinal));
            TestAssert.True(attribute is not null && !string.IsNullOrEmpty(attribute.Value),
                "assembly metadata \"" + RepoRootKey + "\" is missing");
            return attribute!.Value!;
        }
    }
}
