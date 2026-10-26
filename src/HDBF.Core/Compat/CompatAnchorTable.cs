using System;
using System.Collections.Generic;

namespace HDBF.Core.Compat
{
    /// <summary>
    /// One verified reflection anchor of an optional mod: an assembly + a type + (optionally) the
    /// exact member signature HDBF needs. Chapter 8.2 requires <c>ExternalProxy</c> to cache
    /// <c>Type</c>/<c>MethodInfo</c> lookups by "type full name + member name + signature", so the
    /// anchor carries all three parts as strings (REQ-COMPAT-05: no compile-time reference to a
    /// third-party assembly).
    ///
    /// Names are metadata facts, not suggestions: every value in <see cref="CompatAnchorTable"/>
    /// was read from the real Workshop assembly listed in
    /// <c>docs/workstreams/compat-01-adapters.md</c>. An anchor whose type or member cannot be
    /// resolved at runtime must degrade the adapter, never be guessed around (R-04).
    /// </summary>
    public sealed class CompatAnchorSpec
    {
        /// <summary>Creates one anchor; empty <paramref name="memberName"/> means "type only".</summary>
        public CompatAnchorSpec(string assemblyName, string typeName, string memberName,
            string[]? parameterTypeNames, string? returnTypeName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                throw new ArgumentException("anchor assembly name is required", nameof(assemblyName));
            }

            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("anchor type name is required", nameof(typeName));
            }

            AssemblyName = assemblyName;
            TypeName = typeName;
            MemberName = memberName ?? string.Empty;

            var parameters = parameterTypeNames ?? Array.Empty<string>();
            var copy = new string[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (string.IsNullOrEmpty(parameters[i]))
                {
                    throw new ArgumentException("anchor parameter type name is empty", nameof(parameterTypeNames));
                }

                copy[i] = parameters[i];
            }

            ParameterTypeNames = copy;
            ReturnTypeName = returnTypeName ?? string.Empty;
        }

        /// <summary>Simple name of the third-party assembly that declares the type.</summary>
        public string AssemblyName { get; }

        /// <summary>Full type name, e.g. <c>TimberPipes.Services.PipeRegistry</c>.</summary>
        public string TypeName { get; }

        /// <summary>Member name (<c>get_X</c> for a property getter), or empty for a type-only anchor.</summary>
        public string MemberName { get; }

        /// <summary>
        /// Parameter types as full names, with <c>ByRef</c> already stripped (an <c>out</c>/
        /// <c>ref</c> parameter is recorded by its element type). Empty for a parameterless member.
        /// </summary>
        public IReadOnlyList<string> ParameterTypeNames { get; }

        /// <summary>Full return type name, or empty for a type-only anchor.</summary>
        public string ReturnTypeName { get; }

        /// <summary>True when the anchor only asserts that the type exists (no member signature).</summary>
        public bool IsTypeOnly => MemberName.Length == 0;

        /// <summary>Human-readable signature used by diagnostics, tests and the anchor checker.</summary>
        public string SignatureText
        {
            get
            {
                if (IsTypeOnly)
                {
                    return TypeName;
                }

                var parameters = ParameterTypeNames.Count == 0
                    ? string.Empty
                    : string.Join(", ", ToArray(ParameterTypeNames));
                return TypeName + "." + MemberName + "(" + parameters + ") -> " + ReturnTypeName;
            }
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var copy = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }

    /// <summary>
    /// Everything the detector and one adapter need to know about a known optional mod: stable
    /// adapter id, real manifest identity, tolerant detection strings and verified anchors.
    /// Pure data: no game or third-party types (chapter 8.2 soft dependency).
    /// </summary>
    public sealed class CompatAdapterSpec
    {
        /// <summary>Creates the immutable probe description of one optional mod.</summary>
        public CompatAdapterSpec(string adapterId, string modId, string modName, string displayNameKey,
            bool requiresApiBinding, string[] modNameAliases, string[] assemblyNameTokens,
            CompatAnchorSpec[] anchors)
        {
            if (string.IsNullOrEmpty(adapterId))
            {
                throw new ArgumentException("adapter id is required", nameof(adapterId));
            }

            if (string.IsNullOrEmpty(modId))
            {
                throw new ArgumentException("mod id is required", nameof(modId));
            }

            if (string.IsNullOrEmpty(modName))
            {
                throw new ArgumentException("mod name is required", nameof(modName));
            }

            if (string.IsNullOrEmpty(displayNameKey))
            {
                throw new ArgumentException("display name key is required", nameof(displayNameKey));
            }

            if (modNameAliases is null || modNameAliases.Length == 0)
            {
                throw new ArgumentException("at least one mod-name alias is required", nameof(modNameAliases));
            }

            if (assemblyNameTokens is null || assemblyNameTokens.Length == 0)
            {
                throw new ArgumentException("at least one assembly-name token is required", nameof(assemblyNameTokens));
            }

            if (anchors is null || anchors.Length == 0)
            {
                throw new ArgumentException("at least one API anchor is required", nameof(anchors));
            }

            AdapterId = adapterId;
            ModId = modId;
            ModName = modName;
            DisplayNameKey = displayNameKey;
            RequiresApiBinding = requiresApiBinding;
            ModNameAliases = Copy(modNameAliases);
            AssemblyNameTokens = Copy(assemblyNameTokens);
            ApiAnchors = Copy(anchors);
        }

        /// <summary>Stable adapter id, part of the save/report contract (REQ-COMPAT-10).</summary>
        public string AdapterId { get; }

        /// <summary>Manifest <c>Id</c> of the external mod (verified from its manifest.json).</summary>
        public string ModId { get; }

        /// <summary>Mod name written to the compatibility report's <c>detectedMods</c>.</summary>
        public string ModName { get; }

        /// <summary>Localization key for the compatibility panel.</summary>
        public string DisplayNameKey { get; }

        /// <summary>
        /// True when the adapter may only report Active if at least one anchor resolved *and*
        /// its member signature matched (chapter 8.3: never advertise an unbound capability).
        /// </summary>
        public bool RequiresApiBinding { get; }

        /// <summary>Mod-manager display-name/id aliases (normalised comparison).</summary>
        public IReadOnlyList<string> ModNameAliases { get; }

        /// <summary>Normalised assembly-name tokens; any substring hit means "probably installed".</summary>
        public IReadOnlyList<string> AssemblyNameTokens { get; }

        /// <summary>Verified API anchors; any fully matching anchor satisfies <see cref="RequiresApiBinding"/>.</summary>
        public IReadOnlyList<CompatAnchorSpec> ApiAnchors { get; }

        /// <summary>Number of anchors that also pin a member signature (diagnostics/tests).</summary>
        public int MemberAnchorCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < ApiAnchors.Count; i++)
                {
                    if (!ApiAnchors[i].IsTypeOnly)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private static string[] Copy(string[] source)
        {
            var copy = new string[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static CompatAnchorSpec[] Copy(CompatAnchorSpec[] source)
        {
            var copy = new CompatAnchorSpec[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    /// <summary>
    /// The five soft dependencies in the fixed chapter 8.1 / table 8-1 order, with the anchor
    /// names verified against the real Workshop assemblies on 2026-10-25 (game 1.1.2.8).
    ///
    /// Why the table lives in Core: the requirement demands a session-wide detection contract and
    /// L1 proof of the detection strings, while the actual reflection must stay in
    /// <c>HDBF.Integration</c> (REQ-ARCH-02). Core therefore owns only immutable *data* and pure
    /// matching helpers (<see cref="CompatMatching"/>); it never references a third-party type.
    /// Full evidence (DLL path + member signature + mod version) is in
    /// <c>docs/workstreams/compat-01-adapters.md</c>; <c>tools/dev/ModAnchorCheck</c> re-proves it
    /// against the installed Workshop folders.
    /// </summary>
    public static class CompatAnchorTable
    {
        /// <summary>Adapter id of Timber Pipes (chapter 8.1 order: 1).</summary>
        public const string TimberPipesId = "HDBF.Adapter.TimberPipes";

        /// <summary>Adapter id of Power Lines (chapter 8.1 order: 2).</summary>
        public const string PowerLinesId = "HDBF.Adapter.PowerLines";

        /// <summary>Adapter id of Conveyor Belts V2 (chapter 8.1 order: 3).</summary>
        public const string ConveyorBeltsV2Id = "HDBF.Adapter.ConveyorBeltsV2";

        /// <summary>Adapter id of More Mines (chapter 8.1 order: 4).</summary>
        public const string MoreMinesId = "HDBF.Adapter.MoreMines";

        /// <summary>Adapter id of Hooman Stairs (chapter 8.1 order: 5).</summary>
        public const string HoomanStairsId = "HDBF.Adapter.HoomanStairs";

        /// <summary>
        /// Timber Pipes 11.4.0 (Workshop 3801726769, assembly <c>TimberPipes</c>): pipe registry,
        /// fluid simulator, pipe graph and tank are the public surface HDBF samples (A level).
        /// </summary>
        public static readonly CompatAdapterSpec TimberPipes = new CompatAdapterSpec(
            TimberPipesId,
            "TimberPipes",
            "Timber Pipes",
            "HDBF.Compat.Adapter.TimberPipes",
            requiresApiBinding: true,
            modNameAliases: new[] { "Timber Pipes", "TimberPipes" },
            assemblyNameTokens: new[] { "timberpipes" },
            anchors: new[]
            {
                new CompatAnchorSpec("TimberPipes", "TimberPipes.Services.PipeRegistry", "TryGetGraph",
                    new[] { "UnityEngine.Vector3Int", "TimberPipes.Models.PipeGraph" }, "System.Boolean"),
                new CompatAnchorSpec("TimberPipes", "TimberPipes.Services.PipeFluidSimulator", "Tick",
                    null, "System.Void"),
                new CompatAnchorSpec("TimberPipes", "TimberPipes.Models.PipeGraph", "RefreshContamination",
                    null, "System.Void"),
                new CompatAnchorSpec("TimberPipes", "TimberPipes.Components.PipeTank", "TakesGood",
                    new[] { "System.String" }, "System.Boolean"),
            });

        /// <summary>
        /// Power Lines 11.1.1 (Workshop 3766893705, assembly <c>PowerLines</c>): connection service,
        /// graph service, line component and settings spec (A level).
        /// </summary>
        public static readonly CompatAdapterSpec PowerLines = new CompatAdapterSpec(
            PowerLinesId,
            "PowerLines",
            "Power Lines",
            "HDBF.Compat.Adapter.PowerLines",
            requiresApiBinding: true,
            modNameAliases: new[] { "Power Lines", "PowerLines" },
            assemblyNameTokens: new[] { "powerlines" },
            anchors: new[]
            {
                new CompatAnchorSpec("PowerLines", "PowerLines.Services.PowerLineConnectionService", "CanConnect",
                    new[] { "PowerLines.Components.PowerLineComponent", "PowerLines.Components.PowerLineComponent" },
                    "System.String"),
                new CompatAnchorSpec("PowerLines", "PowerLines.Services.PowerLineGraphService", "OnNodeAddedToGraph",
                    new[] { "Timberborn.MechanicalSystem.MechanicalNode" }, "System.Void"),
                new CompatAnchorSpec("PowerLines", "PowerLines.Components.PowerLineComponent", "ShortestDistanceTo",
                    new[] { "PowerLines.Components.PowerLineComponent" }, "System.Single"),
                new CompatAnchorSpec("PowerLines", "PowerLines.Specs.PowerLineSettingsSpec", "get_DefaultMaxConnections",
                    null, "System.Int32"),
            });

        /// <summary>
        /// Conveyor Belts v2 11.4.0 (Workshop 3815376360, assembly <c>ConveyorBelt</c> plus
        /// <c>ConveyorBelt.Algorithm</c>): belt registry/simulator/inventory and the algorithm
        /// simulation step (A level).
        /// </summary>
        public static readonly CompatAdapterSpec ConveyorBeltsV2 = new CompatAdapterSpec(
            ConveyorBeltsV2Id,
            "ConveyorBelt",
            "Conveyor Belts v2",
            "HDBF.Compat.Adapter.ConveyorBeltsV2",
            requiresApiBinding: true,
            modNameAliases: new[] { "Conveyor Belts v2", "ConveyorBelt", "ConveyorBeltsV2" },
            assemblyNameTokens: new[] { "conveyorbelt" },
            anchors: new[]
            {
                new CompatAnchorSpec("ConveyorBelt", "ConveyorBelt.Services.BeltRegistry", "Register",
                    new[] { "ConveyorBelt.Components.BeltCarrier" }, "System.Void"),
                new CompatAnchorSpec("ConveyorBelt", "ConveyorBelt.Services.BeltSimulator", "Tick",
                    null, "System.Void"),
                new CompatAnchorSpec("ConveyorBelt", "ConveyorBelt.Components.BeltInventory", "ClearContents",
                    null, "System.Void"),
                new CompatAnchorSpec("ConveyorBelt.Algorithm", "ConveyorBelt.Algorithm.BeltSimulation", "Tick",
                    new[] { "System.Single" }, "System.Void"),
            });

        /// <summary>
        /// More Mines 1.1.20 (Workshop 3723216060, assembly <c>More Mines</c>): read-only adapter,
        /// the evidence anchors are the public spec/service surface (B level: detection alone activates).
        /// </summary>
        public static readonly CompatAdapterSpec MoreMines = new CompatAdapterSpec(
            MoreMinesId,
            "TonWolfe.MoreMines",
            "More Mines [ModContest1]",
            "HDBF.Compat.Adapter.MoreMines",
            requiresApiBinding: false,
            modNameAliases: new[] { "More Mines", "TonWolfe.MoreMines" },
            assemblyNameTokens: new[] { "moremines" },
            anchors: new[]
            {
                new CompatAnchorSpec("More Mines", "TonWolfe.DeepMines.TWOreGroupSpec", "get_GroupId",
                    null, "System.String"),
                new CompatAnchorSpec("More Mines", "TonWolfe.DeepMines.TWTunnelSpec", "get_ExplosionPrefabPath",
                    null, "System.String"),
                new CompatAnchorSpec("More Mines", "TonWolfe.DeepMines.TWOreUpdateService", "HasViewBlockingObject",
                    new[] { "UnityEngine.Vector3Int" }, "System.Boolean"),
                new CompatAnchorSpec("More Mines", "TonWolfe.DeepMines.TWOre", "IsHidden",
                    null, "System.Boolean"),
            });

        /// <summary>
        /// Hooman Stairs 2.0.2 (Workshop 3682012904, assembly <c>hoomanstairs</c>): read-only
        /// placement/pathing hints (B level: detection alone activates).
        /// </summary>
        public static readonly CompatAdapterSpec HoomanStairs = new CompatAdapterSpec(
            HoomanStairsId,
            "Calloatti.HoomanStairs",
            "Hooman Stairs",
            "HDBF.Compat.Adapter.HoomanStairs",
            requiresApiBinding: false,
            modNameAliases: new[] { "Hooman Stairs", "Calloatti.HoomanStairs" },
            assemblyNameTokens: new[] { "hoomanstairs" },
            anchors: new[]
            {
                new CompatAnchorSpec("hoomanstairs", "Calloatti.HoomanStairs.HoomanStairsRegistry", "RemoveNode",
                    new[] { "System.Int32" }, "System.Void"),
                new CompatAnchorSpec("hoomanstairs", "Calloatti.HoomanStairs.HoomanStairsManager", "RefreshAllBuildings",
                    null, "System.Void"),
                new CompatAnchorSpec("hoomanstairs", "Calloatti.HoomanStairs.HoomanStairsPathfinder", string.Empty,
                    null, null),
            });

        /// <summary>
        /// Fixed registration order (table 8-1): pipes, power, logistics, then the read-only
        /// B-level adapters. The order is part of the save/report contract (REQ-COMPAT-10).
        ///
        /// Declared after the individual specs on purpose: C# runs static field initializers in
        /// textual order, so this array must not be initialised before its elements exist.
        /// </summary>
        public static readonly CompatAdapterSpec[] All =
        {
            TimberPipes,
            PowerLines,
            ConveyorBeltsV2,
            MoreMines,
            HoomanStairs,
        };

        /// <summary>Finds the adapter spec with the exact stable id; null when unknown or empty.</summary>
        public static CompatAdapterSpec? ById(string? adapterId)
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
    }
}
