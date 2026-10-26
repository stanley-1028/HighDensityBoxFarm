// ============================================================================
// HDBF v2 | Layer: dev tooling | Owner: Lead
// Purpose: Crafts the schema-v2 IndoorFarmRecord payload embedded into an in-game fixture
//          save by tools/dev/fixtures/craft-farm-fixture.py (verification fixture only; the
//          tool is not shipped and is not part of HDBF.sln).
// Requirements: REQ-V2-TEST-02 (in-game evidence), REQ-V2-SAVE-01 (payload format)
// ============================================================================
using System;
using System.Globalization;
using System.IO;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;

namespace CraftIndoorFixture
{
    /// <summary>
    /// Produces one validated schema-v2 payload. Example:
    /// <c>dotnet run --project tools/dev/CraftIndoorFixture -- --tier Basic --crop Carrot --growth 3.9 --planted 60 --tank full --out payload.txt</c>
    /// </summary>
    public static class Program
    {
        private static int Main(string[] args)
        {
            var tier = HdbfTier.Basic;
            var faction = HdbfFaction.Folktails;
            var mode = IndoorMode.Normal;
            var cropId = "Carrot";
            var growth = 3.9f;
            var planted = 60;
            var tank = -1f;
            string outPath = "payload.txt";

            for (var i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--tier": tier = ParseTier(args[++i]); break;
                    case "--faction": faction = ParseFaction(args[++i]); break;
                    case "--mode": mode = ParseMode(args[++i]); break;
                    case "--crop": cropId = args[++i]; break;
                    case "--growth": growth = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--planted": planted = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--tank":
                        var value = args[++i];
                        tank = string.Equals(value, "full", StringComparison.OrdinalIgnoreCase) ? -1f
                            : float.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--out": outPath = args[++i]; break;
                    default:
                        Console.Error.WriteLine("unknown argument: " + args[i]);
                        return 2;
                }
            }

            var profile = TierCatalog.Get(tier);
            var record = IndoorFarmRecord.Create(tier, faction);
            record.Mode = mode;
            if (tank >= 0f)
            {
                record.Tank = Math.Min(tank, profile.TankCapacity);
            }

            var count = Math.Min(planted, record.Slots.Length);
            for (var index = 0; index < count; index++)
            {
                var slot = record.Slots[index];
                slot.CropId = cropId;
                slot.GrowthDays = Math.Min(growth, CropTime(faction, cropId));
                slot.Stage = SlotStage.Growing;
                slot.Fatigue = 0f;
                slot.LastCropId = DifferentCrop(faction, cropId);
            }

            var repair = IndoorSaveValidator.ValidateAndRepair(
                record, profile, FallbackCropCatalog.Default, null);
            var text = IndoorSaveCodec.Encode(record);
            File.WriteAllText(outPath, text);

            Console.WriteLine(
                "payload written: " + outPath + " bytes=" + text.Length
                + " tier=" + tier + " faction=" + faction + " mode=" + mode
                + " capacity=" + record.Slots.Length + " planted=" + count
                + " tank=" + record.Tank.ToString("F3", CultureInfo.InvariantCulture)
                + " repaired=" + repair.Repaired);
            return 0;
        }

        private static float CropTime(HdbfFaction faction, string cropId)
        {
            return FallbackCropCatalog.Default.TryGet(faction, cropId, out var crop)
                ? crop.GrowthTimeInDays
                : 4f;
        }

        private static string DifferentCrop(HdbfFaction faction, string cropId)
        {
            var crops = FallbackCropCatalog.Default.ForFaction(faction);
            for (var i = 0; i < crops.Count; i++)
            {
                if (!string.Equals(crops[i].Id, cropId, StringComparison.Ordinal))
                {
                    return crops[i].Id;
                }
            }

            return string.Empty;
        }

        private static HdbfTier ParseTier(string value)
        {
            if (string.Equals(value, "Intermediate", StringComparison.OrdinalIgnoreCase)) return HdbfTier.Intermediate;
            if (string.Equals(value, "Advanced", StringComparison.OrdinalIgnoreCase)) return HdbfTier.Advanced;
            if (string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase)) return HdbfTier.Top;
            return HdbfTier.Basic;
        }

        private static HdbfFaction ParseFaction(string value)
        {
            return string.Equals(value, "IronTeeth", StringComparison.OrdinalIgnoreCase)
                ? HdbfFaction.IronTeeth
                : HdbfFaction.Folktails;
        }

        private static IndoorMode ParseMode(string value)
        {
            if (string.Equals(value, "Dense", StringComparison.OrdinalIgnoreCase)) return IndoorMode.Dense;
            if (string.Equals(value, "Fallow", StringComparison.OrdinalIgnoreCase)) return IndoorMode.Fallow;
            return IndoorMode.Normal;
        }
    }
}
