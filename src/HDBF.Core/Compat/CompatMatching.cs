using System;
using System.Collections.Generic;
using System.Text;

namespace HDBF.Core.Compat
{
    /// <summary>
    /// Pure name-matching rules used by the soft-dependency detector (chapter 8.2,
    /// REQ-COMPAT-06) and by the L1 tests that pin the detection contract.
    ///
    /// Why this lives in Core: the strings in <see cref="CompatAnchorTable"/> are the *only*
    /// connection between HDBF and the five optional mods (REQ-COMPAT-05). Keeping the comparison
    /// rules dependency-free means the exact same code the game runs can be asserted by the L1
    /// suite without loading a game or third-party assembly (REQ-TEST-03).
    ///
    /// Matching is tolerant on purpose (risk R-04 "external mod API may change"):
    /// <list type="bullet">
    /// <item>assembly names are compared as normalised substrings, so <c>TimberPipes</c>,
    /// <c>Timberborn.TimberPipes</c> and <c>Timber_Pipes</c> all match;</item>
    /// <item>mod-manager names (manifest <c>Name</c>/<c>Id</c>) match in either direction after
    /// normalisation, so "Timber Pipes", "TimberPipes" and "TimberPipes Continued" are
    /// interchangeable inside the alias list.</item>
    /// </list>
    /// Every method returns false/null instead of throwing; the detector converts a miss into
    /// "not installed", never into an exception (REQ-COMPAT-07, REQ-SAFE-01).
    /// </summary>
    public static class CompatMatching
    {
        /// <summary>
        /// Lower-cases and strips every non-alphanumeric character so "Timber Pipes",
        /// "TimberPipes" and "timber_pipes" compare equal. Null/empty becomes the empty string.
        /// </summary>
        public static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value!.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// True when the (already normalised) assembly simple name contains any of the adapter's
        /// normalised assembly-name tokens. A null/empty name or token list is a miss, not an error.
        /// </summary>
        public static bool MatchesAssemblyName(string? normalizedAssemblyName, IReadOnlyList<string>? assemblyNameTokens)
        {
            if (string.IsNullOrEmpty(normalizedAssemblyName) || assemblyNameTokens is null)
            {
                return false;
            }

            var name = normalizedAssemblyName!;
            for (var i = 0; i < assemblyNameTokens.Count; i++)
            {
                var token = Normalize(assemblyNameTokens[i]);
                if (token.Length > 0 && name.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the first mod-manager entry that matches one of the adapter's display-name
        /// aliases (both sides normalised, either direction), or null when nothing matches.
        /// </summary>
        public static string? MatchModName(IReadOnlyList<string>? modList, IReadOnlyList<string>? aliases)
        {
            if (modList is null || aliases is null)
            {
                return null;
            }

            for (var i = 0; i < modList.Count; i++)
            {
                var entry = modList[i];
                var normalizedEntry = Normalize(entry);
                if (normalizedEntry.Length == 0)
                {
                    continue;
                }

                for (var a = 0; a < aliases.Count; a++)
                {
                    var alias = Normalize(aliases[a]);
                    if (alias.Length == 0)
                    {
                        continue;
                    }

                    if (normalizedEntry.IndexOf(alias, StringComparison.Ordinal) >= 0 ||
                        alias.IndexOf(normalizedEntry, StringComparison.Ordinal) >= 0)
                    {
                        return entry;
                    }
                }
            }

            return null;
        }
    }
}
