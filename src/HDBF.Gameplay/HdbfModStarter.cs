using System;
using System.IO;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace HDBF.Gameplay
{
    /// <summary>
    /// Mod entry point (docs/API_FACTS.md §1): the game instantiates every
    /// <see cref="IModStarter"/> with a parameterless constructor and calls
    /// <see cref="StartMod"/> once with the mod environment. The game-context DI container is
    /// created later, so this method only records the mod path (and version) for the
    /// <see cref="Components.HdbfSystemRegistrar"/> and never throws (REQ-SAFE-02).
    /// </summary>
    public class HdbfModStarter : IModStarter
    {
        /// <inheritdoc />
        public void StartMod(IModEnvironment modEnvironment)
        {
            try
            {
                var modPath = modEnvironment != null ? modEnvironment.ModPath : null;
                HdbStartupContext.Initialize(modPath);
                Debug.Log("[HDBF/Info] start HDBF version=" + HdbStartupContext.ModVersion
                          + " modPath=" + (modPath ?? "<unknown>"));
            }
            catch (Exception exception)
            {
                // Never let the mod entry point break game startup (REQ-SAFE-01).
                try
                {
                    Debug.LogError("[HDBF/Error] HDBF StartMod failed: "
                                   + exception.GetType().Name + ": " + exception.Message);
                }
                catch
                {
                    // Unity logging itself is unavailable: nothing else we can safely do.
                }
            }
        }
    }

    /// <summary>
    /// Startup hand-off between <see cref="HdbfModStarter"/> (runs before dependency injection
    /// exists) and <see cref="Components.HdbfSystemRegistrar"/> (runs inside the game container).
    ///
    /// <see cref="ModEnvironment"/> is not available from the container, and mod assemblies are
    /// loaded from raw bytes so <c>Assembly.Location</c> is empty; this write-once static is the
    /// smallest possible seam. It holds no simulation state and is written exactly once during
    /// <c>StartMod</c>, which keeps chapter 16.2 red line 6 (no global mutable game state) intact.
    /// </summary>
    internal static class HdbStartupContext
    {
        private static string? _modPath;
        private static string? _version;

        /// <summary>Absolute mod root as supplied by the game; null before <c>StartMod</c>.</summary>
        public static string? ModPath => _modPath;

        /// <summary>Manifest version, or "unknown" when the manifest cannot be read.</summary>
        public static string ModVersion => _version ?? "unknown";

        /// <summary>Records the environment; idempotent so a hot reload cannot corrupt it.</summary>
        public static void Initialize(string? modPath)
        {
            if (_modPath != null || string.IsNullOrEmpty(modPath))
            {
                return;
            }

            _modPath = modPath;
            _version = TryReadVersion(modPath!);
        }

        private static string? TryReadVersion(string modPath)
        {
            try
            {
                var manifestPath = Path.Combine(modPath, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                // Minimal targeted read instead of a JSON dependency: the manifest is a flat object
                // and we only need the quoted Version string (docs/API_FACTS.md §1).
                var text = File.ReadAllText(manifestPath);
                var key = text.IndexOf("\"Version\"", StringComparison.Ordinal);
                if (key < 0)
                {
                    return null;
                }

                var colon = text.IndexOf(':', key);
                if (colon < 0)
                {
                    return null;
                }

                var firstQuote = text.IndexOf('"', colon + 1);
                if (firstQuote < 0)
                {
                    return null;
                }

                var secondQuote = text.IndexOf('"', firstQuote + 1);
                if (secondQuote <= firstQuote)
                {
                    return null;
                }

                return text.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }
            catch
            {
                return null;
            }
        }
    }
}
