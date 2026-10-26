// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-INTEG-01
// Purpose: The v2 indoor adaptor host contract (INTERFACES section 6). It is the only
//          interface Gameplay depends on for pipe/logistics availability; it names no
//          game type and no third-party type, so Core stays reference-free and a missing
//          or broken external mod can only degrade to manual refill (REQ-V2-COMPAT-02/03).
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Implemented by <c>HDBF.Integration.Adapters.IndoorAdapterHost</c> and consumed by
    /// <c>HDBF.Gameplay</c> through DI; the frozen v2 contract of INTERFACES.md section 6.
    ///
    /// Contract rules (REQ-V2-COMPAT-02/03, REQ-V2-SAFE-05):
    /// * every member is a safe snapshot: a missing mod, a broken external API or a reflection
    ///   failure must answer <c>null</c>/<c>false</c>/<see cref="HdbfAdapterHealth.Disabled"/>,
    ///   never throw into the simulation;
    /// * <see cref="PipeWaterSource"/> is non-null only while the pipe adaptor can still serve
    ///   water; <c>null</c> means "fall back to manual refill", not an error;
    /// * adaptor health reuses the v1 <see cref="HDBF.Core.Compat.CompatStateMachine"/> values so
    ///   the panel and the compatibility report observe exactly one state source (no second
    ///   state machine is allowed).
    /// </summary>
    public interface IIndoorAdapterHost
    {
        // REQ-V2-COMPAT-02: every member below answers null/false/Disabled instead of throwing,
        // so a missing or broken external mod can only degrade the refill path, never the farm.

        /// <summary>
        /// Pipe water source, or <c>null</c> when the Timber Pipes mod is missing, its API cannot
        /// be reflected, or the adaptor was disabled after repeated failures. Gameplay then
        /// degrades to manual refill (REQ-V2-COMPAT-02).
        /// </summary>
        IWaterSource? PipeWaterSource { get; }

        /// <summary>
        /// True when a usable pipe network is present and the pipe adaptor is serving. It is a
        /// capability probe for the panel; it never blocks farm operation when false.
        /// </summary>
        bool IsPipeConnected { get; }

        /// <summary>
        /// True when the logistics (Conveyor Belts V2) adaptor is active. Vanilla output storage is
        /// used when false (REQ-V2-COMPAT-02).
        /// </summary>
        bool IsLogisticsConnected { get; }

        /// <summary>
        /// Health snapshot of the Timber Pipes adaptor, reusing the v1 three-strike state machine:
        /// <c>Unknown/Ok/Degraded/Disabled/Failed</c> (REQ-V2-COMPAT-01).
        /// </summary>
        HdbfAdapterHealth PipeHealth { get; }
    }
}
