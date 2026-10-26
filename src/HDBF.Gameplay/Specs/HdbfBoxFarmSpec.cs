using Timberborn.BlueprintSystem;

namespace HDBF.Gameplay.Specs
{
    /// <summary>
    /// Every per-tier difference of the four box farms, expressed as blueprint data
    /// (chapter 11.1 / REQ-IMPL-01). One spec type and one controller serve all tiers
    /// (REQ-ARCH-07): balancing a tier never changes code.
    ///
    /// <see cref="ComponentSpec"/> is a C# record in Timberborn 1.1.2.8, so a custom spec must
    /// be declared as a record; serializable members are plain <c>get; set;</c> properties
    /// marked with <see cref="Serialize"/> (verified against
    /// <c>BlueprintDeserializer.GetSpecTypes</c> and the shipped specs, docs/API_FACTS.md §3).
    ///
    /// JSON keys are the member names, e.g. <c>"Tier": "Basic", "PlotCapacity": 60</c>.
    /// </summary>
    public record HdbfBoxFarmSpec : ComponentSpec
    {
        /// <summary>Tier name; must match the last segment of the blueprint id ("Basic", ...).</summary>
        [Serialize]
        public string Tier { get; set; } = "Basic";

        /// <summary>Maximum number of managed plots (dense planting slots), 60/150/300/600.</summary>
        [Serialize]
        public int PlotCapacity { get; set; } = 60;

        /// <summary>Plots one on-duty farmer can tend (38 by default, chapter 6.2).</summary>
        [Serialize]
        public int PlotsPerFarmer { get; set; } = 38;

        /// <summary>Horizontal work-range edge on the X axis (11/15/19/27 cells).</summary>
        [Serialize]
        public int WorkRangeX { get; set; } = 11;

        /// <summary>Horizontal work-range edge on the Y axis (Timberborn's second horizontal axis).</summary>
        [Serialize]
        public int WorkRangeZ { get; set; } = 11;

        /// <summary>Minimum irrigation coverage C_min (0.50/0.60/0.75/0.80, chapter 6.3).</summary>
        [Serialize]
        public float MinIrrigationCoverage { get; set; } = 0.5f;

        /// <summary>Water units consumed per plot per game day (chapter 6.3).</summary>
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

        /// <summary>True when the tier exposes a water pipe port (all tiers in v0.1).</summary>
        [Serialize]
        public bool HasPipePort { get; set; } = true;

        /// <summary>True when the tier exposes a logistics output port.</summary>
        [Serialize]
        public bool HasLogisticsPort { get; set; }

        /// <summary>True when the tier exposes a power port (top tier only).</summary>
        [Serialize]
        public bool HasPowerPort { get; set; }
    }
}
