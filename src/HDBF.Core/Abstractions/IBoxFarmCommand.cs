using System;

namespace HDBF.Core.Abstractions
{
    /// <summary>
    /// Write-side companion of <see cref="IBoxFarmQuery"/> (chapter 9.2, REQ-UI-06/07).
    ///
    /// UI and diagnostics are read-only through <see cref="IBoxFarmQuery"/>; every player action
    /// goes through this contract instead, so the entity panel never needs a game type. Gameplay
    /// implements it as a <c>[Context("Game")]</c> singleton and binds it to the existing gameplay
    /// service (see docs/workstreams/ui-panel.md for the one-line wiring).
    ///
    /// Contract rules (binding for both sides):
    /// * every method validates synchronously on the game's main thread and either applies the
    ///   change completely or changes nothing ("整单回滚", REQ-UI-07);
    /// * no method throws for player input, a missing entity, a cooldown or missing materials;
    /// * on <c>false</c> the implementation must return a non-null <c>failureKey</c> from
    ///   <see cref="HdbfCommandFailures"/> so the UI can show a specific, localized reason
    ///   (chapter 14.3: 缺材料 / 缺模组 / 冷却中 / 注册格不足); the key is ignored on success;
    /// * failure keys are fixed sentences and contain no <c>{0}</c> placeholder, because the UI
    ///   only receives the key (no numeric payload);
    /// * the identifier is the runtime entity id (<c>EntityComponent.EntityId</c>), the same key
    ///   <see cref="IBoxFarmQuery"/> uses, never the save-file id.
    ///
    /// The interface is pure .NET (enums + strings), so gameplay can be unit tested outside Unity
    /// with a fake implementation.
    /// </summary>
    public interface IBoxFarmCommand
    {
        /// <summary>
        /// Switches the operating mode. Fails while the one-game-day mode cooldown is running,
        /// when dense planting is disabled by configuration, or for an unknown entity.
        /// </summary>
        bool TrySetMode(Guid entityId, HdbfMode mode, out string? failureKey);

        /// <summary>
        /// Performs one maintenance cycle: validates and consumes the tier's upkeep material,
        /// resets the countdown and clears the shutdown state. Fails with a specific reason when
        /// materials are missing or the tier has no material upkeep.
        /// </summary>
        bool TryPerformMaintenance(Guid entityId, out string? failureKey);

        /// <summary>
        /// Requests a port switch (chapter 8.3). Fails when the tier does not expose the port, when
        /// the optional compatibility mod is missing, or when the port is not player-switchable.
        /// </summary>
        bool TrySetPort(Guid entityId, HdbfPortKind port, bool connected, out string? failureKey);

        /// <summary>
        /// Opens or closes the managed-plot overlay for one farm (REQ-UI-05, chapter 14.3).
        /// Opening validates that the entity is a live box farm and replaces any other open overlay
        /// (at most one farm is highlighted at a time); closing always succeeds. The overlay owns
        /// world-space click/box-select handling, so the panel only needs this toggle plus the
        /// authoritative <c>BoxFarmSnapshot.IsPlotHighlightActive</c> flag.
        /// </summary>
        bool TrySetPlotHighlight(Guid entityId, bool active, out string? failureKey);

        /// <summary>
        /// Excludes one registered plot from management (REQ-UI-05). The last registered plot can
        /// never be excluded; an in-flight harvest is not interrupted.
        /// </summary>
        bool TryExcludePlot(Guid entityId, int plotId, out string? failureKey);

        /// <summary>Restores a previously excluded plot; the next scan may register it again.</summary>
        bool TryIncludePlot(Guid entityId, int plotId, out string? failureKey);
    }

    /// <summary>
    /// Stable failure keys returned by <see cref="IBoxFarmCommand"/>. They are localization keys
    /// (chapter 14.4, category <c>Error</c>/<c>Mode</c>/<c>Port</c>) and must exist in every
    /// shipped <c>data/Localizations/*.csv</c>; the matching constants live in
    /// <c>HDBF.UI.Localization.HdbfUiLocKeys</c> so the UI can whitelist what it displays.
    ///
    /// Keeping them here (instead of inline strings in gameplay) is what lets the UI render a
    /// localized reason without knowing which implementation answered.
    /// </summary>
    public static class HdbfCommandFailures
    {
        /// <summary>The requested entity id is not a live box farm (demolished, wrong building).</summary>
        public const string NotABoxFarm = "HDBF.Error.NotBoxFarm";

        /// <summary>Maintenance cannot be paid: the tier's upkeep material is missing (REQ-UI-07).</summary>
        public const string MissingMaterials = "HDBF.Error.MaterialsMissing";

        /// <summary>Mode switch is still on its one-game-day cooldown (REQ-UI-06).</summary>
        public const string ModeCooldown = "HDBF.Mode.SwitchCooldown";

        /// <summary>Dense planting is turned off by the mod configuration (chapter 9.3).</summary>
        public const string DenseDisabled = "HDBF.Error.DenseDisabled";

        /// <summary>The optional compatibility mod required by the port is not detected.</summary>
        public const string MissingMod = "HDBF.Error.MissingMod";

        /// <summary>This tier does not expose the requested port (Hidden state, chapter 8.3).</summary>
        public const string PortNotAvailable = "HDBF.Port.NotApplicable";

        /// <summary>The port is owned by the physical network (the power port) and cannot be switched here.</summary>
        public const string PortManagedByNetwork = "HDBF.Port.PowerManaged";

        /// <summary>At least one managed plot must stay registered (REQ-UI-05).</summary>
        public const string PlotMinimum = "HDBF.Error.PlotMinimum";

        /// <summary>The plot is not currently registered by this farm.</summary>
        public const string PlotNotRegistered = "HDBF.Error.PlotNotRegistered";

        /// <summary>Generic fallback for an implementation-specific rejection with no better reason.</summary>
        public const string Unknown = "HDBF.Error.ActionFailed";
    }
}
