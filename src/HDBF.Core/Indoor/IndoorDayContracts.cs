// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-GAMEPLAY-02
// Purpose: Input/result DTOs of one daily indoor tick (INTERFACES section 5) moved out of
//          IndoorDayTick so both stay within the 300-line readability gate. Backward
//          compatible: every member added by this card is optional and defaults to "none".
// Requirements: REQ-V2-MAINT-03, REQ-V2-WATER-02, REQ-V2-WATER-04, REQ-V2-LABOR-01,
//               REQ-V2-BAL-01
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>Inputs of one daily indoor farm tick (INTERFACES section 5).</summary>
    public sealed class IndoorDayContext
    {
        /// <summary>On-duty workers of the building; source of the daily action budget.</summary>
        public int ActiveWorkers { get; init; }

        /// <summary>Current game day used by the maintenance cycle countdown.</summary>
        public float CurrentDay { get; init; }

        /// <summary>True when the front entrance is blocked; pauses farm actions only.</summary>
        public bool EntranceBlocked { get; init; }

        /// <summary>Output inventory sink; may be null when the port/inventory is missing.</summary>
        public IInventorySink? Output { get; init; }

        /// <summary>Pipe water source of the automatic refill path; may be null (no source).</summary>
        public IWaterSource? Water { get; init; }

        /// <summary>
        /// Real water source found by the manual refill probe (water body, water pump output or a
        /// water tank inventory within the manual radius). When null the manual path falls back to
        /// <see cref="Water"/>, which keeps the pre-existing Core tests and callers valid.
        /// </summary>
        public IWaterSource? ManualWater { get; init; }

        /// <summary>Nearest maintenance material stockpile; may be null.</summary>
        public IMaintenanceMaterialSource? Materials { get; init; }

        /// <summary>True when the pipe port is connected and water is expected from it.</summary>
        public bool HasPipeWater { get; init; }

        /// <summary>True when the player requested a manual refill trip for this day.</summary>
        public bool ManualRefillRequested { get; init; }

        /// <summary>
        /// Farm actions already consumed by gameplay before this tick (for example a queued worker
        /// job). They are deducted from the daily action budget so a day can never overspend
        /// (REQ-V2-LABOR-01). Negative values are treated as 0; the default keeps old callers intact.
        /// </summary>
        public int ReservedActions { get; init; }

        /// <summary>
        /// Previous operating mode while the one-game-day mode transition runs
        /// (REQ-V2-BAL-03/section 19.8: settle the transition day at the lower yield). Null means
        /// "no transition this day".
        /// </summary>
        public IndoorMode? PreviousMode { get; init; }
    }

    /// <summary>Outcome of the optional manual water refill path of one day (section 19.6).</summary>
    public enum ManualRefillOutcome
    {
        /// <summary>No manual refill was requested this day.</summary>
        NotRequested = 0,

        /// <summary>A worker fetched water and the tank was refilled; 2 farm actions were spent.</summary>
        Completed = 1,

        /// <summary>The farm has no on-duty worker who could fetch water.</summary>
        NoWorkers = 2,

        /// <summary>No usable (reachable, uncontaminated) water source was available.</summary>
        NoSource = 3,

        /// <summary>The remaining daily action budget is smaller than the 2-action refill cost.</summary>
        NoBudget = 4,

        /// <summary>The entrance is blocked, so a worker cannot leave/return for the trip.</summary>
        EntranceBlocked = 5,
    }

    /// <summary>Outcome of one daily indoor farm tick (INTERFACES section 5).</summary>
    public readonly struct IndoorDayResult
    {
        /// <summary>Tank coverage C after the daily settlement.</summary>
        public float Coverage { get; init; }

        /// <summary>G_growth = G_irr x G_maint x G_power used by the growth step.</summary>
        public float GrowthMultiplier { get; init; }

        /// <summary>Panel yield multiplier: G_mode x average G_fat of the slots holding a crop.</summary>
        public float YieldMultiplier { get; init; }

        /// <summary>Farm actions performed this day (plant/harvest only, refill excluded).</summary>
        public IndoorActionStats Actions { get; init; }

        /// <summary>All problems detected this day.</summary>
        public IndoorWarningFlags Warnings { get; init; }

        /// <summary>True when the tick changed persisted state (the save should be marked dirty).</summary>
        public bool Changed { get; init; }

        /// <summary>True when maintenance stalls farm actions (crops keep growing).</summary>
        public bool Stalled { get; init; }

        /// <summary>Farm actions consumed by the manual refill trip (0 or 2, section 19.6).</summary>
        public int RefillActionsUsed { get; init; }

        /// <summary>Manual refill outcome of this day; drives the command/panel failure reason.</summary>
        public ManualRefillOutcome ManualRefill { get; init; }

        /// <summary>
        /// Yield scale applied while the mode transition is active (1 = no transition). Harvests
        /// settle at the lower of the old/new mode yield (section 19.8).
        /// </summary>
        public float TransitionYieldScale { get; init; }
    }

    /// <summary>Per-day action counters of <see cref="IndoorLaborMath.RunActions"/> (INTERFACES section 5).</summary>
    public struct IndoorActionStats
    {
        /// <summary>Mature slots harvested this day.</summary>
        public int Harvested;

        /// <summary>Empty slots planted this day.</summary>
        public int Planted;

        /// <summary>Actions consumed from the daily budget.</summary>
        public int ActionsUsed;

        /// <summary>Mature slots that could not be harvested because the output was full.</summary>
        public int BlockedByOutput;

        /// <summary>True when output-full blocked at least one harvest (panel backlog).</summary>
        public bool Backlog;
    }
}
