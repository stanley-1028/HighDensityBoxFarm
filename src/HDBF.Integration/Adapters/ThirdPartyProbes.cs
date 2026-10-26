using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Compat;

namespace HDBF.Integration.Adapters
{
    // =============================================================================================
    // VERIFIED 2026-10-25 against the real Workshop assemblies (game 1.1.2.8).
    //
    // The data source is HDBF.Core.Compat.CompatAnchorTable: it owns the immutable strings and is
    // unit-tested (tests/HDBF.Tests/CompatAnchorTableTests.cs), while this file adapts them to the
    // runtime probe. No third-party namespace, type or member is referenced at compile time
    // (REQ-COMPAT-05); `tools/dev/ModAnchorCheck` re-proves every anchor against the installed
    // DLL metadata.
    //
    // Anchor model (chapter 8.2): assembly simple name + type full name + member name + signature.
    // Detection uses the assembly tokens and type anchors only; Initialize additionally requires
    // the member signature to match for the three A-level adapters, so an external mod that renames
    // a member degrades the adapter instead of advertising an unbound port (chapter 8.3).
    //
    // Matching is tolerant on purpose (R-04 "external mod API may change"):
    // * assembly names are compared by a normalised substring token (CompatMatching);
    // * type anchors are resolved by assembly hint, then globally by full name, then by simple name,
    //   so a namespace rename does not break detection;
    // * a miss is null / Disabled, never an exception (REQ-COMPAT-09/12).
    // =============================================================================================

    /// <summary>
    /// One verified third-party type (and optionally the exact member signature) the adapter can
    /// bind to; immutable probe data built from <see cref="CompatAnchorSpec"/>.
    /// </summary>
    internal readonly struct ApiAnchor
    {
        internal ApiAnchor(string assemblyName, string typeName, string memberName,
            IReadOnlyList<string> parameterTypeNames, string returnTypeName)
        {
            AssemblyName = assemblyName;
            TypeName = typeName;
            MemberName = memberName;
            ParameterTypeNames = parameterTypeNames;
            ReturnTypeName = returnTypeName;
        }

        /// <summary>Simple assembly name the type is expected to live in.</summary>
        internal string AssemblyName { get; }

        /// <summary>Full type name candidate; resolution falls back to a global scan (R-04).</summary>
        internal string TypeName { get; }

        /// <summary>Member the anchor pins, or empty for a type-only anchor.</summary>
        internal string MemberName { get; }

        /// <summary>Expected parameter full names (ByRef stripped for <c>out</c>/<c>ref</c>).</summary>
        internal IReadOnlyList<string> ParameterTypeNames { get; }

        /// <summary>Expected return full name, or empty for a type-only anchor.</summary>
        internal string ReturnTypeName { get; }

        /// <summary>True when only the type is required (no member signature is pinned).</summary>
        internal bool IsTypeOnly => string.IsNullOrEmpty(MemberName);
    }

    /// <summary>
    /// Everything the detector and one adapter need to know about a known optional mod: adapter id,
    /// real manifest identity, detection strings and verified anchors. Pure data.
    /// </summary>
    internal sealed class ThirdPartyProbe
    {
        internal ThirdPartyProbe(string adapterId, string modId, string modName, string displayNameKey,
            bool requiresApiBinding, string[] modNameAliases, string[] assemblyNameTokens, ApiAnchor[] apiAnchors)
        {
            AdapterId = adapterId;
            ModId = modId;
            ModName = modName;
            DisplayNameKey = displayNameKey;
            RequiresApiBinding = requiresApiBinding;
            ModNameAliases = modNameAliases;
            AssemblyNameTokens = assemblyNameTokens;
            ApiAnchors = apiAnchors;
        }

        /// <summary>Stable adapter id, part of the save contract (REQ-COMPAT-10).</summary>
        internal string AdapterId { get; }

        /// <summary>Manifest id of the external mod, for diagnosis and the compatibility report.</summary>
        internal string ModId { get; }

        /// <summary>Mod name written to the compatibility report's <c>detectedMods</c>.</summary>
        internal string ModName { get; }

        /// <summary>Localization key for the compatibility panel.</summary>
        internal string DisplayNameKey { get; }

        /// <summary>True when the adapter may only report Active if a fully verified anchor matched (type + member signature).</summary>
        internal bool RequiresApiBinding { get; }

        /// <summary>Mod-manager display-name/id aliases (normalised comparison).</summary>
        internal string[] ModNameAliases { get; }

        /// <summary>Normalised assembly-name tokens; any substring hit means "probably installed".</summary>
        internal string[] AssemblyNameTokens { get; }

        /// <summary>Verified API anchors; any fully matching anchor satisfies <see cref="RequiresApiBinding"/>.</summary>
        internal ApiAnchor[] ApiAnchors { get; }
    }

    /// <summary>
    /// Fixed registry of the five known soft dependencies, in the order required by chapter 8.1 /
    /// REQ-COMPAT-01. Also owns the adapter-id to port mapping so the registry never has to switch
    /// on third-party types.
    /// </summary>
    internal static class ThirdPartyProbes
    {
        internal const string TimberPipesId = CompatAnchorTable.TimberPipesId;
        internal const string PowerLinesId = CompatAnchorTable.PowerLinesId;
        internal const string ConveyorBeltsV2Id = CompatAnchorTable.ConveyorBeltsV2Id;
        internal const string MoreMinesId = CompatAnchorTable.MoreMinesId;
        internal const string HoomanStairsId = CompatAnchorTable.HoomanStairsId;

        internal static readonly ThirdPartyProbe TimberPipes = From(CompatAnchorTable.TimberPipes);

        internal static readonly ThirdPartyProbe PowerLines = From(CompatAnchorTable.PowerLines);

        internal static readonly ThirdPartyProbe ConveyorBeltsV2 = From(CompatAnchorTable.ConveyorBeltsV2);

        internal static readonly ThirdPartyProbe MoreMines = From(CompatAnchorTable.MoreMines);

        internal static readonly ThirdPartyProbe HoomanStairs = From(CompatAnchorTable.HoomanStairs);

        /// <summary>
        /// Registration order is fixed by chapter 8.1: pipes, power, logistics, then the read-only
        /// B-level adapters. Declared after the probes so static initialisation order is correct.
        /// </summary>
        internal static readonly ThirdPartyProbe[] All =
        {
            TimberPipes,
            PowerLines,
            ConveyorBeltsV2,
            MoreMines,
            HoomanStairs,
        };

        internal static ThirdPartyProbe? ByAdapterId(string? adapterId)
        {
            if (string.IsNullOrEmpty(adapterId))
            {
                return null;
            }

            for (var i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].AdapterId, adapterId, StringComparison.Ordinal))
                {
                    return All[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The single port an adapter can provide; null means "no port" (read-only adapters).
        /// Keeps <c>IsPortAvailable</c> free of type names (chapter 8.3).
        /// </summary>
        internal static HdbfPortKind? PortFor(string? adapterId)
        {
            if (string.Equals(adapterId, TimberPipesId, StringComparison.Ordinal))
            {
                return HdbfPortKind.Pipe;
            }

            if (string.Equals(adapterId, PowerLinesId, StringComparison.Ordinal))
            {
                return HdbfPortKind.Power;
            }

            if (string.Equals(adapterId, ConveyorBeltsV2Id, StringComparison.Ordinal))
            {
                return HdbfPortKind.Logistics;
            }

            return null;
        }

        private static ThirdPartyProbe From(CompatAdapterSpec spec)
        {
            var aliases = new string[spec.ModNameAliases.Count];
            for (var i = 0; i < aliases.Length; i++)
            {
                aliases[i] = spec.ModNameAliases[i];
            }

            var tokens = new string[spec.AssemblyNameTokens.Count];
            for (var i = 0; i < tokens.Length; i++)
            {
                tokens[i] = spec.AssemblyNameTokens[i];
            }

            var anchors = new ApiAnchor[spec.ApiAnchors.Count];
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = spec.ApiAnchors[i];
                anchors[i] = new ApiAnchor(anchor.AssemblyName, anchor.TypeName, anchor.MemberName,
                    anchor.ParameterTypeNames, anchor.ReturnTypeName);
            }

            return new ThirdPartyProbe(spec.AdapterId, spec.ModId, spec.ModName, spec.DisplayNameKey,
                spec.RequiresApiBinding, aliases, tokens, anchors);
        }
    }
}
