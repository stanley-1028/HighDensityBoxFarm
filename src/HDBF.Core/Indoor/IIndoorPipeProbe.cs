// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-03
// Purpose: Read-only pipe probe DTO + interface (WP-05 / RE-05 / L-02): the adapter
//          reports why the pipe port is (not) usable without drawing water or
//          tripping the adapter health. Core has no Timber Pipes reference.
// Requirements: REQ-V2-COMPAT-01, REQ-V2-WATER-02, REQ-V2-READ-04
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Reason code of the read-only pipe probe (WP-05). The distinct values are what lets the panel
    /// stop showing the misleading single "Not connected" string; numeric values are part of the
    /// panel contract and must not be renumbered.
    /// </summary>
    public enum PipeProbeReason
    {
        /// <summary>No probe ran (no pipe source / old adapter): the panel shows "—".</summary>
        Unknown = 0,

        /// <summary>A finished, watered pipe tank is reachable: automatic refill can draw water.</summary>
        Connected = 1,

        /// <summary>No finished water tank has a pipe graph: the pipe is not in a network.</summary>
        NoPipeNetwork = 2,

        /// <summary>A network exists but holds no water-capable pipe tank: nothing to draw from.</summary>
        NoPipeTank = 3,

        /// <summary>All reachable pipe tanks are empty: the network is dry.</summary>
        TankDry = 4,

        /// <summary>The Timber Pipes adapter is missing, disabled or failed: no probe possible.</summary>
        AdapterUnavailable = 5,

        /// <summary>The adapter binding failed (reflection/version drift): probe cannot be trusted.</summary>
        BindingFailed = 6,

        /// <summary>The farm's pipe port is switched off by the player (or the tier has none).</summary>
        PortOff = 7,

        /// <summary>
        /// A pipe graph at the farm's own pipe port is absent, but the settlement-wide pipe census
        /// still holds a network-connected water tank (WP-05/L-02). Automatic refill can therefore
        /// draw water - just not from a pipe the farm itself sits on - so the panel labels it
        /// "connected (settlement-wide supply)" instead of pretending the farm is on the network.
        /// </summary>
        GlobalSupply = 8,
    }

    /// <summary>
    /// Grid position of the asking farm's pipe port, used only by the read-only probe (WP-05/L-02).
    /// A box farm is not a Timber Pipes endpoint, so a graph at this position is what distinguishes
    /// "the farm itself sits on the network" from "the settlement supplies water elsewhere".
    /// Core value type on purpose: no Unity/game type crosses the layer boundary.
    /// </summary>
    /// <param name="X">Grid x coordinate of the farm's pipe port.</param>
    /// <param name="Y">Grid y coordinate of the farm's pipe port.</param>
    /// <param name="Z">Grid z coordinate of the farm's pipe port.</param>
    public readonly record struct PipeProbeAnchor(int X, int Y, int Z);

    /// <summary>
    /// Optional companion of <see cref="IIndoorPipeProbe"/> (WP-05): the caller tells the probe
    /// where its farm stands before asking, so the probe can be honest about settlement-wide
    /// supply. Implementations must stay observation-only - no water draw, no failure strike, no
    /// exception into the panel/render path - and <c>null</c> must clear the anchor so the next
    /// probe falls back to the settlement census. A separate interface keeps the frozen one-method
    /// <see cref="IIndoorPipeProbe"/> contract unchanged, and a host that does not implement it
    /// simply keeps the census semantics.
    /// </summary>
    public interface IIndoorPipeProbeAnchor
    {
        /// <summary>Sets the farm pipe-port position used by the following probes; null clears it.</summary>
        /// <param name="anchor">Grid position of the farm, or null when the caller has none.</param>
        void SetProbeAnchor(PipeProbeAnchor? anchor);
    }

    /// <summary>
    /// Immutable read-only result of one pipe probe (WP-05, REQ-V2-COMPAT-01): the reason plus the
    /// counts the panel/diagnostic line shows. Counts are 0 when unknown; <see cref="Detail"/> may be
    /// empty but never null in a probed value.
    /// </summary>
    /// <param name="Reason">Reason code the panel maps to a dedicated text.</param>
    /// <param name="TankCount">Finished pipe tanks found (any good).</param>
    /// <param name="WaterTankCount">Finished pipe tanks that accept the water good.</param>
    /// <param name="Detail">Short diagnostic detail; empty when there is nothing to add.</param>
    public readonly record struct PipeProbe(PipeProbeReason Reason, int TankCount, int WaterTankCount,
        string Detail)
    {
        /// <summary>
        /// The default "no probe ran" value. A static expression-bodied property (never a mutable
        /// static field) so the v2 no-static-state gate stays green and no caller can mutate it.
        /// </summary>
        public static PipeProbe Unknown => new PipeProbe(PipeProbeReason.Unknown, 0, 0, string.Empty);
    }

    /// <summary>
    /// Read-only pipe probe contract of the water adapter (WP-05 / WP-07). Implementations must be
    /// observation-only: never draw water, never consume a health strike and never throw into the
    /// panel/render path (the caller treats an exception as <see cref="PipeProbe.Unknown"/>).
    /// </summary>
    public interface IIndoorPipeProbe
    {
        /// <summary>Probes the current pipe connectivity without changing any state.</summary>
        /// <returns>Immutable probe result; never null.</returns>
        PipeProbe ProbePipe();
    }
}
