namespace HDBF.Core.Compat
{
    /// <summary>
    /// One optional-mod adapter (chapter 8.2, REQ-IMPL-23).
    ///
    /// Implementations live in <c>HDBF.Integration</c> and are the only place allowed to touch
    /// third-party mod types, always through reflection (REQ-COMPAT-05). Core code only sees
    /// this interface, so a missing or broken adapter can never stop the farm from running
    /// (REQ-ARCH-08).
    ///
    /// Lifecycle: <see cref="Detect"/> is read-only, <see cref="Initialize"/> binds ports and
    /// must roll back on failure (REQ-COMPAT-08), <see cref="Shutdown"/> must be idempotent, and
    /// <see cref="Tick"/> is called on a coarse timer rather than every frame (REQ-SAFE-17).
    /// </summary>
    public interface ICompatAdapter
    {
        /// <summary>Stable adapter id, e.g. <c>HDBF.Adapter.TimberPipes</c> (REQ-COMPAT-10).</summary>
        string Id { get; }

        /// <summary>Localization key for the adapter name shown in the compatibility panel.</summary>
        string DisplayNameKey { get; }

        /// <summary>Current health; see <see cref="CompatStateMachine"/> for the exact mapping.</summary>
        HdbfAdapterHealth Health { get; }

        /// <summary>Failures observed this session; used for the three-strikes rule (REQ-COMPAT-07).</summary>
        int FailureCount { get; }

        /// <summary>Read-only probe: is the external mod present and compatible?</summary>
        bool Detect();

        /// <summary>Binds ports/events; returns false (after rolling back) when binding fails.</summary>
        bool Initialize();

        /// <summary>Unregisters everything; idempotent and safe to call when not initialized.</summary>
        void Shutdown();

        /// <summary>Coarse periodic update in game hours; must stay cheap (REQ-SAFE-17).</summary>
        void Tick(float gameHours);
    }
}
