// ============================================================================
// HDBF v2 | Layer: Gameplay/Specs | Owner: V2-GAMEPLAY-01
// Purpose: Blueprint data of every indoor box farm tier. JSON member names stay the v1
//          names for save/blueprint compatibility; their v2 meaning is documented below
//          (PlotCapacity = indoor slot capacity, WaterPerPlotPerDay = water per slot/day).
// Requirements: REQ-V2-CORE-01, REQ-V2-BLD-06, REQ-V2-EXT-01
// ============================================================================
using Timberborn.BlueprintSystem;

namespace HDBF.Gameplay.Specs
{
    /// <summary>
    /// Every per-tier difference of the four box farms, expressed as blueprint data (section
    /// 19.3). One spec type and one controller serve all tiers, so balancing a tier never changes
    /// code.
    ///
    /// v2 semantics: the building has no world plots. <see cref="PlotCapacity"/> is the number of
    /// abstract indoor growing slots (60/150/300/600) and <see cref="WaterPerPlotPerDay"/> is the
    /// tank demand per slot and day. The JSON member names are intentionally not renamed: existing
    /// v1 blueprints and saves keep loading, and the four shipped v2 blueprints already carry these
    /// keys. Renaming is deferred to the data migration card that can rewrite both together.
    /// </summary>
    public record HdbfBoxFarmSpec : ComponentSpec
    {
        /// <summary>Tier name; must match the last segment of the blueprint id ("Basic", ...).</summary>
        [Serialize]
        public string Tier { get; set; } = "Basic";

        /// <summary>
        /// v2 indoor slot capacity (60/150/300/600). JSON name kept from v1; it no longer means
        /// world plots and is never registered as a plot count.
        /// </summary>
        [Serialize]
        public int PlotCapacity { get; set; } = 60;

        /// <summary>Slots one on-duty farmer can tend (38 by default, section 19.7).</summary>
        [Serialize]
        public int PlotsPerFarmer { get; set; } = 38;

        /// <summary>【作废 in v2】Work range is no longer read; kept for v1 blueprint compatibility.</summary>
        [Serialize]
        public int WorkRangeX { get; set; } = 11;

        /// <summary>【作废 in v2】Work range is no longer read; kept for v1 blueprint compatibility.</summary>
        [Serialize]
        public int WorkRangeZ { get; set; } = 11;

        /// <summary>Minimum tank coverage C_min (0.50/0.60/0.75/0.80, section 19.6).</summary>
        [Serialize]
        public float MinIrrigationCoverage { get; set; } = 0.5f;

        /// <summary>
        /// v2 water demand per indoor slot and day (0.020/0.024/0.028/0.032). JSON name kept from
        /// v1; it no longer refers to world plots.
        /// </summary>
        [Serialize]
        public float WaterPerPlotPerDay { get; set; } = 0.02f;

        /// <summary>Internal tank capacity expressed in full-load days (3/3/4/4).</summary>
        [Serialize]
        public int TankCapacityDays { get; set; } = 3;

        /// <summary>Multiplier on fatigue gained per harvest (1.00/1.10/1.25/1.50).</summary>
        [Serialize]
        public float SoilFatigueFactor { get; set; } = 1f;

        /// <summary>Maintenance payment, <see cref="HDBF.Core.HdbfMaintenanceKind"/> as int.</summary>
        [Serialize]
        public int MaintenanceKind { get; set; }

        /// <summary>Maintenance period in game days (0 for the irrigation-only basic tier).</summary>
        [Serialize]
        public float MaintenanceCycleDays { get; set; }

        /// <summary>Plank/metal upkeep per maintenance cycle.</summary>
        [Serialize]
        public int MaintenanceMaterialAmount { get; set; }

        /// <summary>True when the tier must connect to power to run at full efficiency.</summary>
        [Serialize]
        public bool RequiresPower { get; set; }

        /// <summary>Power consumed while connected (150 for the top tier).</summary>
        [Serialize]
        public int PowerDemand { get; set; }

        /// <summary>True when the tier exposes a water pipe port (all tiers in v2).</summary>
        [Serialize]
        public bool HasPipePort { get; set; } = true;

        /// <summary>True when the tier exposes a logistics output port (Intermediate and above).</summary>
        [Serialize]
        public bool HasLogisticsPort { get; set; }

        /// <summary>True when the tier exposes a power port (Top only).</summary>
        [Serialize]
        public bool HasPowerPort { get; set; }
    }
}
