// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-UI-01
// Purpose: The gameplay-side read contracts shared by the controller and the retained
//          query/command/warning services. The v1 plot-ownership registry is gone; these
//          types only describe the v2 indoor snapshot source, config lookup and warning inputs.
// Requirements: REQ-V2-CORE-01, REQ-V2-WATER-03, REQ-V2-UI-01/02, REQ-V2-CORE-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Indoor;
using HDBF.Core.Warnings;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Read/command source published by one live indoor farm to
    /// <see cref="HdbfFarmQueryService"/>. UI never sees this type; UI consumes the Core
    /// <c>IndoorFarmSnapshot</c> DTO through the query service seam only.
    /// </summary>
    public interface IHdbfBoxFarmSnapshotSource
    {
        /// <summary>Runtime entity id used as the UI lookup key.</summary>
        Guid EntityId { get; }

        /// <summary>Save-file identity of the building.</summary>
        long SaveEntityId { get; }

        /// <summary>
        /// Builds the Core v2 panel DTO (INTERFACES section 9). Must not throw; the caller
        /// isolates it per farm (REQ-V2-SAFE-01/07).
        /// </summary>
        /// <returns>Fresh immutable snapshot of this farm.</returns>
        IndoorFarmSnapshot CreateIndoorSnapshot();

        /// <summary>Builds a fresh immutable legacy snapshot. Must not throw; the caller isolates it.</summary>
        BoxFarmSnapshot CreateSnapshot();

        /// <summary>Builds the warning evaluation input for an already-created snapshot.</summary>
        HdbfWarningInputs CreateWarningInputs(BoxFarmSnapshot snapshot);

        /// <summary>
        /// v1 ADR-009 compatibility member. v2 has no world plots, so implementations always
        /// return false and leave <paramref name="growthRate"/> at 1; the member is kept because
        /// the frozen interface signature and the still-shipped patch bridge both name it.
        /// </summary>
        /// <param name="plotId">Legacy world plot id; ignored in v2.</param>
        /// <param name="growthRate">Always 1.</param>
        /// <returns>Always false in v2.</returns>
        bool TryGetPlotGrowthRate(int plotId, out float growthRate);
    }

    /// <summary>
    /// Resolved balance data for gameplay. Implemented by <see cref="HdbfSystemRegistrar"/>:
    /// loads <c>HDBF_Config.json</c> once and exposes the config-resolved tier table.
    /// </summary>
    public interface IHdbfGameConfigProvider
    {
        /// <summary>Fully resolved configuration (defaults when the file is absent or broken).</summary>
        HdbfConfig Config { get; }

        /// <summary>Config-resolved tier table, four entries indexed by <see cref="HdbfTier"/>.</summary>
        IReadOnlyList<TierProfile> Profiles { get; }

        /// <summary>
        /// Looks a profile up by blueprint id. Returns false when the blueprint is unknown; the
        /// <paramref name="profile"/> is then the tier fallback (never throws).
        /// </summary>
        bool TryGetTierProfile(string? blueprintId, HdbfTier fallbackTier, out TierProfile profile);
    }

    /// <summary>
    /// Inputs for one warning evaluation pass. The fields keep their v1 names/semantics for the
    /// retained warning/icon framework; the controller fills them from the v2 record (coverage,
    /// maintained cycles, tank days, no world plot counts).
    /// </summary>
    public readonly struct HdbfWarningInputs
    {
        /// <summary>P0: maintenance stall is active for this farm.</summary>
        public bool IsShutdown { get; init; }

        /// <summary>True when the farm has slots at all; suppresses coverage warnings on an empty farm.</summary>
        public bool HasRegisteredPlots { get; init; }

        /// <summary>Latest daily irrigation coverage C (0..1).</summary>
        public float IrrigationCoverage { get; init; }

        /// <summary>Tier minimum coverage C_min (0..1); 0 disables irrigation warnings.</summary>
        public float MinIrrigationCoverage { get; init; }

        /// <summary>True for the tier that needs power.</summary>
        public bool RequiresPower { get; init; }

        /// <summary>True when the farm is on the power network right now.</summary>
        public bool PowerConnected { get; init; }

        /// <summary>Consecutive missed maintenance cycles (0 = on schedule).</summary>
        public int CyclesMissed { get; init; }

        /// <summary>Highest slot fatigue (0..1).</summary>
        public float FatigueMax { get; init; }

        /// <summary>Days the tank can still supply at the current draw.</summary>
        public float TankDaysLeft { get; init; }

        /// <summary>Effective slots the tank currently supplies (the P4 reserve reference).</summary>
        public int TankFedPlots { get; init; }

        /// <summary>Full-load tank capacity in days.</summary>
        public int TankCapacityDays { get; init; }

        /// <summary>Adapters that reached the failure threshold.</summary>
        public int AdapterFailedCount { get; init; }

        /// <summary>Adapters disabled because their mod is missing.</summary>
        public int AdapterDisabledCount { get; init; }

        /// <summary>Wall-clock seconds a warning must persist before it toggles.</summary>
        public float HysteresisSeconds { get; init; }
    }

    /// <summary>
    /// Maps the output of the retained v1 warning evaluator plus the v2 indoor flags onto the four
    /// warning categories of chapter 19.11 (water shortage, maintenance overdue, power cut and
    /// blocked entrance). The v1-only categories (adapter health, fatigue, low tank) are dropped
    /// because the v2 panel and world icons only keep those four.
    ///
    /// Two categories reuse the frozen Core icon slots: maintenance is emitted under
    /// <see cref="HdbfWarningKeys.Shutdown"/> and a blocked entrance under
    /// <see cref="HdbfWarningKeys.AdapterFailed"/>, because <c>FarmWarningIconKind</c> is a frozen
    /// Core enum that HDBF.UI-01 may not extend. The user-visible texts of those keys are the v2
    /// sentences (see data/Localizations).
    /// </summary>
    internal static class HdbfIndoorWarningMapper
    {
        /// <summary>Projects one evaluated warning list onto the v2 four-category set.</summary>
        /// <param name="evaluated">Hysteresis-filtered output of <see cref="HdbWarningEvaluator"/>.</param>
        /// <param name="indoor">Current Core indoor snapshot (null when unavailable).</param>
        /// <param name="entranceGate">Per-farm hysteresis gate for the blocked entrance.</param>
        /// <param name="hysteresisSeconds">Wall-clock persistence required before a toggle.</param>
        /// <param name="nowSeconds">Unscaled wall clock.</param>
        /// <returns>Warnings sorted P0 → P4 and then by localization key.</returns>
        public static FarmWarning[] MapToV2(FarmWarning[]? evaluated, IndoorFarmSnapshot? indoor,
            WarningHysteresis entranceGate, float hysteresisSeconds, double nowSeconds)
        {
            var warnings = new List<FarmWarning>(4);
            FarmWarningSeverity? maintenance = null;
            if (evaluated is not null)
            {
                for (var index = 0; index < evaluated.Length; index++)
                {
                    var warning = evaluated[index];
                    if (warning is null)
                    {
                        continue;
                    }

                    switch (warning.CodeKey)
                    {
                        case HdbfWarningKeys.NoWater:
                        case HdbfWarningKeys.IrrigationLow:
                        case HdbfWarningKeys.NoPower:
                            warnings.Add(warning);
                            break;
                        case HdbfWarningKeys.Shutdown:
                            maintenance = FarmWarningSeverity.Critical;
                            break;
                        case HdbfWarningKeys.MaintenanceOverdue:
                            if (maintenance is null)
                            {
                                maintenance = FarmWarningSeverity.High;
                            }

                            break;
                    }
                }
            }

            if (maintenance.HasValue)
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.Shutdown,
                    Severity = maintenance.Value,
                });
            }

            var blocked = indoor is not null
                          && (indoor.Warnings & IndoorWarningFlags.EntranceBlocked) != 0;
            if (entranceGate.Observe(0, blocked, nowSeconds, hysteresisSeconds))
            {
                warnings.Add(new FarmWarning
                {
                    CodeKey = HdbfWarningKeys.AdapterFailed,
                    Severity = FarmWarningSeverity.High,
                });
            }

            warnings.Sort((left, right) => FarmWarningCatalog.Compare(left, right));
            return warnings.ToArray();
        }
    }
}
