using System;

namespace HDBF.Integration.Harmony
{
    /// <summary>
    /// One installed patch, owned by <see cref="HarmonyBootstrap"/>. Kept free of HarmonyLib types
    /// on purpose: the bootstrap loads it through an interface, so a missing Harmony mod cannot
    /// make the HDBF.Integration types unloadable (Bindito enumerates <c>Assembly.GetTypes()</c> of
    /// every mod assembly during container creation).
    /// </summary>
    internal interface IHdbfPatchSession
    {
        /// <summary>Target method this session patched, for logs and the compatibility report.</summary>
        string TargetMethod { get; }

        /// <summary>True while the patch is installed.</summary>
        bool IsInstalled { get; }

        /// <summary>Unpatches and releases the runtime bridge. Idempotent; never throws.</summary>
        void Shutdown();
    }
}
