using System;
using System.Collections.Generic;

namespace HDBF.Core.Abstractions
{
    /// <summary>Severity ordering matches the UI P0..P4 scheme (chapter 14.2).</summary>
    public enum FarmWarningSeverity
    {
        Critical = 0,   // P0: shutdown / no water
        High = 1,       // P1: irrigation low / no power / adapter failed
        Medium = 2,     // P2: maintenance overdue / fatigue high
        Low = 3,        // P3: adapter disabled
        Info = 4,       // P4: tank low
    }

    /// <summary>
    /// One player-visible warning. Carries a localization key plus at most two numeric
    /// arguments so the UI can format without knowing game types (REQ-UI-01..03).
    /// </summary>
    public sealed record FarmWarning
    {
        public string CodeKey { get; init; } = string.Empty;

        public FarmWarningSeverity Severity { get; init; }

        public float NumberArg0 { get; init; }

        public float NumberArg1 { get; init; }
    }

    /// <summary>The five balance multipliers, exposed for the UI hover breakdown (REQ-UI-02).</summary>
    public sealed record FarmMultipliers
    {
        public float Irrigation { get; init; } = 1f;

        public float Fatigue { get; init; } = 1f;

        public float Maintenance { get; init; } = 1f;

        public float Power { get; init; } = 1f;

        public float Mode { get; init; } = 1f;

        /// <summary>Product of all layers; the figure the player actually feels.</summary>
        public float Total => Irrigation * Fatigue * Maintenance * Power * Mode;
    }

    /// <summary>
    /// Immutable read-only view of one box farm, the only thing UI and diagnostics may see
    /// (IBoxFarmQuery in chapter 9.2). Rebuilt on demand; never stored in a save.
    /// </summary>
    public sealed record BoxFarmSnapshot
    {
        public long EntityId { get; init; }

        /// <summary>
        /// Runtime entity id (<c>EntityComponent.EntityId</c>), the lookup key of
        /// <see cref="IBoxFarmQuery"/> and of <see cref="IBoxFarmLocator"/>. Required by the
        /// cross-building warning rows so a member can be selected in the world; snapshot builders
        /// that predate this field return <see cref="Guid.Empty"/> and are treated as "not locatable".
        /// </summary>
        public Guid RuntimeEntityId { get; init; }

        public HdbfTier Tier { get; init; }

        public HdbfMode Mode { get; init; }

        public int RegisteredPlots { get; init; }

        public int EffectivePlots { get; init; }

        /// <summary>
        /// Sentinel returned in <see cref="TillablePlots"/> while no plot scan has completed yet.
        /// The panel renders "—" for it instead of a misleading 0 (chapter 14.1: 数据未就绪显示 "—").
        /// </summary>
        public const int UnknownTillablePlots = -1;

        /// <summary>
        /// Candidates accepted by the most recent completed plot scan (chapter 11.3): cells inside
        /// the work range that are plantable and reachable, with player-excluded cells and cells
        /// already claimed by another box farm removed. This is the panel's "可开垦格" figure; it
        /// deliberately is not derived from <see cref="EffectivePlots"/> or the plot capacity, so a
        /// player can see how many usable cells actually exist. <see cref="UnknownTillablePlots"/>
        /// while a scan is still running or before the first one completed.
        /// </summary>
        public int TillablePlots { get; init; } = UnknownTillablePlots;

        public int ActiveWorkers { get; init; }

        public int MaxWorkers { get; init; }

        public float IrrigationCoverage { get; init; }

        public float MinIrrigationCoverage { get; init; }

        public float FatigueAverage { get; init; }

        public float FatigueMax { get; init; }

        /// <summary>Negative once maintenance is overdue.</summary>
        public float MaintenanceDaysLeft { get; init; }

        public int CyclesMissed { get; init; }

        public float TankWater { get; init; }

        public float TankDaysLeft { get; init; }

        public bool TierNeedsPower { get; init; }

        public bool PowerConnected { get; init; }

        public HdbfPortState PipePort { get; init; }

        public HdbfPortState PowerPort { get; init; }

        public HdbfPortState LogisticsPort { get; init; }

        public bool IsShutdown { get; init; }

        public bool IsEntranceBlocked { get; init; }

        /// <summary>
        /// True while this farm's managed-plot overlay is the active highlight session
        /// (REQ-UI-05). Session-wide state owned by gameplay; at most one farm is active at a
        /// time, so the panel can render its toggle/legend without keeping local UI state.
        /// </summary>
        public bool IsPlotHighlightActive { get; init; }

        /// <summary>
        /// Last rejection reason of a world-space plot action (click exclude/restore or box
        /// select), as a no-placeholder localization key from <see cref="HdbfCommandFailures"/>.
        /// <c>null</c> when the last action succeeded or none ran. The panel renders it in the
        /// action line (chapter 14.3: "注册格不足必须提示"), so a world click can explain itself
        /// without UI guessing game state; it never carries a raw game string (REQ-UI-08).
        /// </summary>
        public string? PlotActionFailureKey { get; init; }

        public FarmMultipliers Multipliers { get; init; } = new();

        public IReadOnlyList<FarmWarning> Warnings { get; init; } = Array.Empty<FarmWarning>();
    }
}
