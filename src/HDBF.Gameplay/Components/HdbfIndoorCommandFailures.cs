// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-GAMEPLAY-02
// Purpose: Stable failure keys of the v2 indoor commands (machine readable, no raw game
//          strings). V2-GAMEPLAY-02 adds the specific water/material/worker/entrance
//          reasons required by REQ-V2-UI-02 and completes the refill/crop-policy surface.
// Requirements: REQ-V2-UI-02, REQ-V2-READ-04, REQ-V2-SAFE-07, REQ-V2-WATER-04
// ============================================================================
namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Failure keys returned by the v2 controller/query command surface. They follow the existing
    /// <c>HDBF.Error.*</c> naming scheme; the command service logs them under <c>ui.command-failed</c>.
    /// The UI keeps its own whitelist of keys it can localize; an unknown key degrades to the
    /// generic action-failed sentence instead of printing a raw key (REQ-V2-READ-04).
    /// </summary>
    public static class HdbfIndoorCommandFailures
    {
        /// <summary>The farm is not awake/active, e.g. a construction site or a deleted entity.</summary>
        public const string NotActive = "HDBF.Error.FarmNotActive";

        /// <summary>Manual refill is not available at all (kept for the frozen v1 UI contract).</summary>
        public const string RefillUnavailable = "HDBF.Error.RefillUnavailable";

        /// <summary>The fixed crop does not belong to the building faction's crop table.</summary>
        public const string CropNotAvailable = "HDBF.Error.CropNotAvailable";

        /// <summary>The tier has no material maintenance cycle to reset (basic or on-grid top).</summary>
        public const string MaintenanceUnavailable = "HDBF.Error.MaintenanceUnavailable";

        /// <summary>The requested mode value is not a valid indoor mode.</summary>
        public const string InvalidMode = "HDBF.Error.InvalidMode";

        /// <summary>The requested mode switch is still on its one-game-day cooldown.</summary>
        public const string ModeCooldown = "HDBF.Mode.SwitchCooldown";

        /// <summary>No worker is on duty, so nothing can be fetched or built (REQ-V2-LABOR-01).</summary>
        public const string NoWorkers = "HDBF.Error.NoWorkers";

        /// <summary>The entrance is blocked, so a worker cannot leave for a manual refill trip.</summary>
        public const string EntranceBlocked = "HDBF.Error.EntranceBlocked";

        /// <summary>
        /// No real water source is reachable within 12 cells (water body, water pump output or a
        /// water-tank inventory). Never refill from nothing (section 19.6).
        /// </summary>
        public const string NoWaterSource = "HDBF.Error.NoWaterSource";

        /// <summary>The nearest water source exists but a walk path to it does not (no teleport).</summary>
        public const string WaterSourceUnreachable = "HDBF.Error.WaterSourceUnreachable";

        /// <summary>The nearby water is contaminated (badwater), so it cannot fill the tank.</summary>
        public const string WaterContaminated = "HDBF.Error.WaterContaminated";

        /// <summary>The remaining action budget of the day cannot pay the 2-action refill trip.</summary>
        public const string NoActionsLeft = "HDBF.Error.NoActionsLeft";

        /// <summary>A manual refill trip is already queued for the next daily tick.</summary>
        public const string RefillPending = "HDBF.Error.RefillPending";

        /// <summary>The output inventory is full; the action cannot run without losing the harvest.</summary>
        public const string OutputFull = "HDBF.Error.OutputFull";
    }
}
