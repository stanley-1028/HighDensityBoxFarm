// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Enumerations that a persisted indoor farm record may carry (INTERFACES
//          section 1). Values are part of the save schema and must not be renumbered.
// Requirements: REQ-V2-CORE-01, REQ-V2-SAVE-04, REQ-V2-READ-02
// ============================================================================
using System;

namespace HDBF.Core.Indoor
{
    /// <summary>Player-selectable operating mode of an indoor farm (section 19.5, section 19.8).</summary>
    public enum IndoorMode
    {
        /// <summary>Normal operation: yield 1.00, water 1.00, fatigue 1.00, work 1.00.</summary>
        Normal = 0,

        /// <summary>Dense: yield x1.35, water x1.50, fatigue x1.50, work x1.20.</summary>
        Dense = 1,

        /// <summary>Fallow: no planting/harvesting, yield 0, water x0.30, fatigue recovers daily.</summary>
        Fallow = 2,
    }

    /// <summary>How the farm chooses which crop to plant next (section 19.5, section 19.16).</summary>
    public enum CropPolicy
    {
        /// <summary>Automatic rotation through the faction crop table; never repeats the last crop when avoidable.</summary>
        Rotation = 0,

        /// <summary>Always plant the configured fixed crop; never rotates.</summary>
        Fixed = 1,
    }

    /// <summary>State machine of one indoor slot (INTERFACES section 3).</summary>
    public enum SlotStage
    {
        /// <summary>No crop; the slot may be planted.</summary>
        Empty = 0,

        /// <summary>A crop is growing; progress advances while G_growth &gt; 0.</summary>
        Growing = 1,

        /// <summary>Growth completed; the slot waits for a farmer to harvest it.</summary>
        Mature = 2,
    }

    /// <summary>Where the tank water of a farm is coming from (section 19.6).</summary>
    public enum WaterSupplyKind
    {
        /// <summary>No water source is available; the tank only drains.</summary>
        None = 0,

        /// <summary>Automatic refill through the Timber Pipes adapter port.</summary>
        Pipe = 1,

        /// <summary>Manual worker refill from a nearby water body/pump (2 actions per trip).</summary>
        Manual = 2,
    }

    /// <summary>Power contract of a tier / building (section 19.3, section 19.8).</summary>
    public enum PowerConnection
    {
        /// <summary>The tier never needs power (basic/intermediate/advanced).</summary>
        NotRequired = 0,

        /// <summary>Top tier attached to a power grid; power loss derates growth to 0.60.</summary>
        Grid = 1,

        /// <summary>Top tier deliberately off-grid: material maintenance instead of the 150 power demand.</summary>
        OffGrid = 2,
    }

    /// <summary>
    /// Per-day problem flags of an indoor farm (INTERFACES section 1). Rendered by the panel and by
    /// the world warning icons; deliberately a flags enum so several problems coexist.
    /// </summary>
    [Flags]
    public enum IndoorWarningFlags
    {
        /// <summary>No problem.</summary>
        None = 0,

        /// <summary>The tank is empty / the pipe or manual water source failed.</summary>
        NoWater = 1,

        /// <summary>Coverage C is above zero but below the tier minimum C_min.</summary>
        LowWater = 2,

        /// <summary>Material maintenance is overdue (G_maint derated).</summary>
        MaintenanceDue = 4,

        /// <summary>Two or more maintenance cycles were missed; farm actions are stalled.</summary>
        MaintenanceStalled = 8,

        /// <summary>The top tier expects grid power but the grid is down (G_power 0.60).</summary>
        NoPower = 16,

        /// <summary>The entrance is blocked; farm actions are paused until it clears.</summary>
        EntranceBlocked = 32,

        /// <summary>The output inventory is full; mature slots wait and the farm reports a backlog.</summary>
        OutputFull = 64,

        /// <summary>No worker is on duty; crops keep growing but nothing is planted/harvested.</summary>
        NoWorkers = 128,

        /// <summary>Maintenance material is missing from the nearest stockpile.</summary>
        MissingMaterial = 256,
    }
}
