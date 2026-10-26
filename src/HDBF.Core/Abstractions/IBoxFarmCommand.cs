using System;

namespace HDBF.Core.Abstractions
{
    /// <summary>
    /// Write-side companion of <see cref="IBoxFarmQuery"/> (chapter 9.2, REQ-V2-UI-02).
    ///
    /// UI and diagnostics are read-only through <see cref="IBoxFarmQuery"/>; every player action
    /// goes through this contract instead, so the entity panel never needs a game type. Gameplay
    /// implements it as a <c>[Context("Game")]</c> singleton and binds it to the existing gameplay
    /// service.
    ///
    /// v2 scope (section 19.11): mode switch, material maintenance and port switching. The v1
    /// plot/highlight commands were deleted with the world-plot chain (REQ-V2-CORE-03); the
    /// panel-only refill and crop-policy commands live on the gameplay implementation and are
    /// resolved through the UI command bridge.
    ///
    /// Contract rules (binding for both sides):
    /// * every method validates synchronously on the game's main thread and either applies the
    ///   change completely or changes nothing ("整单回滚", REQ-V2-SAFE-01);
    /// * no method throws for player input, a missing entity, a cooldown or missing materials;
    /// * on <c>false</c> the implementation must return a non-null <c>failureKey</c> from
    ///   <see cref="HdbfCommandFailures"/> so the UI can show a specific, localized reason
    ///   (REQ-V2-UI-02); the key is ignored on success;
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
        /// <param name="entityId">Runtime entity id of the farm.</param>
        /// <param name="mode">Requested operating mode.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the mode was applied.</returns>
        bool TrySetMode(Guid entityId, HdbfMode mode, out string? failureKey);

        /// <summary>
        /// Performs one maintenance cycle: validates and consumes the tier's upkeep material,
        /// resets the countdown and clears the shutdown state. Fails with a specific reason when
        /// materials are missing or the tier has no material upkeep.
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when maintenance was performed.</returns>
        bool TryPerformMaintenance(Guid entityId, out string? failureKey);

        /// <summary>
        /// Requests a port switch (chapter 8.3). Fails when the tier does not expose the port, when
        /// the optional compatibility mod is missing, or when the port is not player-switchable.
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        /// <param name="port">Pipe, power or logistics port.</param>
        /// <param name="connected">Requested port state.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when the port state was applied.</returns>
        bool TrySetPort(Guid entityId, HdbfPortKind port, bool connected, out string? failureKey);
    }

    /// <summary>
    /// Stable failure keys returned by <see cref="IBoxFarmCommand"/>. They are localization keys
    /// and must exist in every shipped <c>data/Localizations/*.csv</c>; the matching constants live
    /// in <c>HDBF.UI.Localization.HdbfUiLocKeys</c> so the UI can whitelist what it displays.
    ///
    /// Keeping them here (instead of inline strings in gameplay) is what lets the UI render a
    /// localized reason without knowing which implementation answered.
    /// </summary>
    public static class HdbfCommandFailures
    {
        /// <summary>The requested entity id is not a live box farm (demolished, wrong building).</summary>
        public const string NotABoxFarm = "HDBF.Error.NotBoxFarm";

        /// <summary>Maintenance cannot be paid: the tier's upkeep material is missing (REQ-V2-UI-02).</summary>
        public const string MissingMaterials = "HDBF.Error.MaterialsMissing";

        /// <summary>Mode switch is still on its one-game-day cooldown (REQ-V2-BAL-02).</summary>
        public const string ModeCooldown = "HDBF.Mode.SwitchCooldown";

        /// <summary>Dense planting is turned off by the mod configuration (section 19.8).</summary>
        public const string DenseDisabled = "HDBF.Error.DenseDisabled";

        /// <summary>The optional compatibility mod required by the port is not detected.</summary>
        public const string MissingMod = "HDBF.Error.MissingMod";

        /// <summary>This tier does not expose the requested port (Hidden state, chapter 8.3).</summary>
        public const string PortNotAvailable = "HDBF.Port.NotApplicable";

        /// <summary>The port is owned by the physical network (the power port) and cannot be switched here.</summary>
        public const string PortManagedByNetwork = "HDBF.Port.PowerManaged";

        /// <summary>Generic fallback for an implementation-specific rejection with no better reason.</summary>
        public const string Unknown = "HDBF.Error.ActionFailed";
    }
}
