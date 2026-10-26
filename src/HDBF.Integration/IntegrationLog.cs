using System;
using HDBF.Core;
using HDBF.Core.Logging;
using UnityEngine;

namespace HDBF.Integration
{
    /// <summary>
    /// The integration layer's route from <see cref="IHdbfLog"/> to the Unity player log.
    ///
    /// HDBF.Integration is the game-facing assembly, so it (and only it) may reference
    /// <c>UnityEngine.Debug</c>; Core still sees only <see cref="IHdbfLog"/> (REQ-ARCH-01).
    /// Messages carry the four fixed chapter 10.4 categories as a prefix so a player log can be
    /// filtered with the documented <c>HDBF/Info</c>, <c>HDBF/Warn</c>, <c>HDBF/Error</c> and
    /// <c>HDBF/Compat</c> tags (REQ-SAFE-14).
    ///
    /// Every write is guarded: logging must never be the reason a game loop step fails.
    /// </summary>
    internal static class IntegrationLog
    {
        /// <summary>Creates the throttled log used by adapters and the registry.</summary>
        internal static IHdbfLog Create()
        {
            return new ThrottledLog(Write);
        }

        internal static string Format(HdbfLogCategory category, string text)
        {
            return Prefix(category) + ": " + text;
        }

        private static string Prefix(HdbfLogCategory category)
        {
            switch (category)
            {
                case HdbfLogCategory.Warn:
                    return "HDBF/Warn";
                case HdbfLogCategory.Error:
                    return "HDBF/Error";
                case HdbfLogCategory.Compat:
                    return "HDBF/Compat";
                default:
                    return "HDBF/Info";
            }
        }

        private static void Write(HdbfLogCategory category, string text)
        {
            try
            {
                var line = Format(category, text);
                switch (category)
                {
                    case HdbfLogCategory.Error:
                        Debug.LogError(line);
                        break;
                    case HdbfLogCategory.Warn:
                    case HdbfLogCategory.Compat:
                        Debug.LogWarning(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }
            catch (Exception)
            {
                // A logging failure (e.g. outside the Unity player loop) is swallowed on purpose.
            }
        }
    }
}
