using System;
using System.Collections.Generic;

namespace HDBF.Core.Balance
{
    /// <summary>
    /// Immutable per-tier balance data. Every value that differs between the four tiers lives
    /// here; gameplay code must never branch on <see cref="HdbfTier"/> (REQ-IMPL-01,
    /// REQ-ARCH-07). Values follow requirement document chapters 5 and 6 and are all
    /// "初版可调" — they may be overridden from JSON at runtime (REQ-IMPL-03).
    /// </summary>
    public sealed record TierProfile
    {
        public HdbfTier Tier { get; init; }

        public string BlueprintId { get; init; } = string.Empty;

        public int PlotCapacity { get; init; }

        public int MaxWorkers { get; init; }

        /// <summary>Square work-range edge length in cells (11/15/19/27).</summary>
        public int WorkRange { get; init; }

        public float MinIrrigationCoverage { get; init; }

        public float WaterPerPlotPerDay { get; init; }

        public int TankCapacityDays { get; init; }

        /// <summary>Multiplier applied to the base fatigue gain per harvest (1.00/1.10/1.25/1.50).</summary>
        public float SoilFatigueFactor { get; init; }

        public int ScienceCost { get; init; }

        public int BuildWood { get; init; }

        public int BuildPlank { get; init; }

        public int BuildMetal { get; init; }

        public float BuildWorkdays { get; init; }

        public HdbfMaintenanceKind MaintenanceKind { get; init; }

        /// <summary>Maintenance period in game days (0 for the irrigation-only basic tier).</summary>
        public float MaintenanceCycleDays { get; init; }

        /// <summary>Plank/metal upkeep per cycle. Unused for the irrigation-only basic tier.</summary>
        public int MaintenanceMaterialCount { get; init; }

        /// <summary>True when the tier can attach to a power network (top tier only).</summary>
        public bool NeedsPower { get; init; }

        public int PowerDemand { get; init; }

        /// <summary>Maintenance material per cycle when running off-grid (top tier: 15 metal / 8 days).</summary>
        public int OffGridMaintenanceMaterial { get; init; }

        public float OffGridMaintenanceCycleDays { get; init; }

        /// <summary>Tank capacity in water units at full effective plot count (3.6/10.8/33.6/76.8).</summary>
        public float TankCapacity => WaterPerPlotPerDay * PlotCapacity * TankCapacityDays;
    }

    /// <summary>
    /// Authoritative tier table for v0.1.0 (document chapters 5 and 6). JSON overrides are
    /// layered on top by the config loader; this type stays the compiled-in fallback so a broken
    /// config can never change balance silently (chapter 10.3).
    /// </summary>
    public static class TierCatalog
    {
        public const int PlotsPerFarmer = 38;
        public const int PlotActionsPerFarmerDay = 13;
        public const float WorkCostPerPlotDay = 0.08f;
        public const float PathDistanceDivisor = 20f;

        public const float DenseYieldMultiplier = 1.35f;
        public const float DenseWaterMultiplier = 1.50f;
        public const float DenseFatigueMultiplier = 1.50f;
        public const float DenseWorkMultiplier = 1.20f;
        public const float FallowWaterMultiplier = 0.30f;
        public const float FallowWorkMultiplier = 0.20f;

        public const float FatiguePerHarvest = 0.08f;
        public const float FatiguePenalty = 0.60f;
        public const float RotationFactor = 0.40f;
        public const float RotationRecovery = 0.05f;
        public const float FallowRecoveryPerDay = 0.03f;

        public const float OffGridEfficiency = 0.60f;

        public const int MaxWorkRangeCells = 27;
        public const int MinWorkRangeCells = 11;
        public const int MaxPlotCapacity = 600;
        public const int MinPlotCapacity = 60;

        private static readonly TierProfile[] Profiles =
        {
            new()
            {
                Tier = HdbfTier.Basic,
                BlueprintId = "HDBF.BoxFarm.Basic",
                PlotCapacity = 60,
                MaxWorkers = 2,
                WorkRange = 11,
                MinIrrigationCoverage = 0.50f,
                WaterPerPlotPerDay = 0.020f,
                TankCapacityDays = 3,
                SoilFatigueFactor = 1.00f,
                ScienceCost = 200,
                BuildWood = 40,
                BuildPlank = 0,
                BuildMetal = 0,
                BuildWorkdays = 6f,
                MaintenanceKind = HdbfMaintenanceKind.IrrigationOnly,
                MaintenanceCycleDays = 0f,
                MaintenanceMaterialCount = 0,
                NeedsPower = false,
                PowerDemand = 0,
                OffGridMaintenanceMaterial = 0,
                OffGridMaintenanceCycleDays = 0f,
            },
            new()
            {
                Tier = HdbfTier.Intermediate,
                BlueprintId = "HDBF.BoxFarm.Intermediate",
                PlotCapacity = 150,
                MaxWorkers = 4,
                WorkRange = 15,
                MinIrrigationCoverage = 0.60f,
                WaterPerPlotPerDay = 0.024f,
                TankCapacityDays = 3,
                SoilFatigueFactor = 1.10f,
                ScienceCost = 600,
                BuildWood = 20,
                BuildPlank = 40,
                BuildMetal = 0,
                BuildWorkdays = 10f,
                MaintenanceKind = HdbfMaintenanceKind.Plank,
                MaintenanceCycleDays = 10f,
                MaintenanceMaterialCount = 8,
                NeedsPower = false,
                PowerDemand = 0,
                OffGridMaintenanceMaterial = 0,
                OffGridMaintenanceCycleDays = 0f,
            },
            new()
            {
                Tier = HdbfTier.Advanced,
                BlueprintId = "HDBF.BoxFarm.Advanced",
                PlotCapacity = 300,
                MaxWorkers = 8,
                WorkRange = 19,
                MinIrrigationCoverage = 0.75f,
                WaterPerPlotPerDay = 0.028f,
                TankCapacityDays = 4,
                SoilFatigueFactor = 1.25f,
                ScienceCost = 1000,
                BuildWood = 0,
                BuildPlank = 50,
                BuildMetal = 30,
                BuildWorkdays = 16f,
                MaintenanceKind = HdbfMaintenanceKind.Metal,
                MaintenanceCycleDays = 10f,
                MaintenanceMaterialCount = 6,
                NeedsPower = false,
                PowerDemand = 0,
                OffGridMaintenanceMaterial = 0,
                OffGridMaintenanceCycleDays = 0f,
            },
            new()
            {
                Tier = HdbfTier.Top,
                BlueprintId = "HDBF.BoxFarm.Top",
                PlotCapacity = 600,
                MaxWorkers = 16,
                WorkRange = 27,
                MinIrrigationCoverage = 0.80f,
                WaterPerPlotPerDay = 0.032f,
                TankCapacityDays = 4,
                SoilFatigueFactor = 1.50f,
                ScienceCost = 3000,
                BuildWood = 0,
                BuildPlank = 50,
                BuildMetal = 100,
                BuildWorkdays = 28f,
                MaintenanceKind = HdbfMaintenanceKind.PowerOrOffGridMetal,
                MaintenanceCycleDays = 0f,
                MaintenanceMaterialCount = 0,
                NeedsPower = true,
                PowerDemand = 150,
                OffGridMaintenanceMaterial = 15,
                OffGridMaintenanceCycleDays = 8f,
            },
        };

        public static IReadOnlyList<TierProfile> All => Profiles;

        public static TierProfile Get(HdbfTier tier)
        {
            var index = (int)tier;
            if (index < 0 || index >= Profiles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "unknown HDBF tier");
            }

            return Profiles[index];
        }

        /// <summary>
        /// Maps a blueprint id to its tier. Accepts the canonical id ("HDBF.BoxFarm.Top"), the
        /// shipped template name ("BoxFarm.Top") and the faction-scoped template name the game
        /// actually reports at runtime ("BoxFarm.Top.Folktails" / "BoxFarm.Top.IronTeeth").
        /// Returns false instead of guessing when the id is unknown (chapter 9.4).
        /// </summary>
        public static bool TryGetByBlueprintId(string? blueprintId, out TierProfile profile)
        {
            if (!string.IsNullOrEmpty(blueprintId))
            {
                var id = NormalizeBlueprintId(blueprintId);
                for (var i = 0; i < Profiles.Length; i++)
                {
                    if (string.Equals(Profiles[i].BlueprintId, id, StringComparison.Ordinal))
                    {
                        profile = Profiles[i];
                        return true;
                    }
                }

                // Game template names: "BoxFarm.<Tier>" and "BoxFarm.<Tier>.<Faction>".
                var parts = id.Split('.');
                for (var index = 0; index + 1 < parts.Length; index++)
                {
                    if (!string.Equals(parts[index], "BoxFarm", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    for (var tierIndex = 0; tierIndex < Profiles.Length; tierIndex++)
                    {
                        if (string.Equals(Profiles[tierIndex].Tier.ToString(), parts[index + 1],
                                StringComparison.OrdinalIgnoreCase))
                        {
                            profile = Profiles[tierIndex];
                            return true;
                        }
                    }
                }
            }

            profile = Profiles[0];
            return false;
        }

        /// <summary>
        /// Strips the "(Clone)" / whitespace decorations Unity/game logging can add, so template
        /// names keep resolving in log-driven diagnostics.
        /// </summary>
        private static string NormalizeBlueprintId(string blueprintId)
        {
            var id = blueprintId.Trim();
            if (id.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                id = id.Substring(0, id.Length - "(Clone)".Length).TrimEnd();
            }

            return id;
        }
    }
}
