using System;
using System.Collections.Generic;
using HDBF.Core.Abstractions;

namespace HDBF.Core.Warnings
{
    /// <summary>
    /// Canonical localization keys of the player-visible farm warnings (chapter 14.2/14.4).
    ///
    /// Core owns the strings because three layers must agree on them:
    /// * gameplay emits <see cref="FarmWarning.CodeKey"/> values,
    /// * UI formats them into status rows,
    /// * and the world-icon/aggregation rules map a key to its priority icon.
    /// UI keeps its own compile-time constants (the localization validator cross-checks those);
    /// <c>WarningIconContractTests</c> pins that both spellings are identical.
    /// </summary>
    public static class HdbfWarningKeys
    {
        public const string Shutdown = "HDBF.Warning.Shutdown";
        public const string NoWater = "HDBF.Warning.NoWater";
        public const string IrrigationLow = "HDBF.Warning.IrrigationLow";
        public const string NoPower = "HDBF.Warning.NoPower";
        public const string AdapterFailed = "HDBF.Warning.AdapterFailed";
        public const string MaintenanceOverdue = "HDBF.Warning.MaintenanceOverdue";
        public const string FatigueHigh = "HDBF.Warning.FatigueHigh";
        public const string AdapterDisabled = "HDBF.Warning.AdapterDisabled";
        public const string TankLow = "HDBF.Warning.TankLow";
    }

    /// <summary>
    /// The warning semantics that get a world-space icon (chapter 14.2: only P0/P1).
    /// The enum is deliberately game-free: gameplay maps a kind to the vanilla
    /// <c>Sprites/StatusIcons/*</c> sprite name, Core never knows about sprites.
    /// </summary>
    public enum FarmWarningIconKind
    {
        /// <summary>No world icon (P2..P4, unknown key, or no warning).</summary>
        None = 0,

        /// <summary>P0: maintenance shutdown.</summary>
        Shutdown = 1,

        /// <summary>P0: coverage below 0.25 × C_min, growth stopped.</summary>
        NoWater = 2,

        /// <summary>P1: coverage below C_min.</summary>
        IrrigationLow = 3,

        /// <summary>P1: top tier on the off-grid fallback.</summary>
        NoPower = 4,

        /// <summary>P1: a compatibility adapter failed.</summary>
        AdapterFailed = 5,
    }

    /// <summary>Pure rules shared by the panel, the aggregation cache and the world icons.</summary>
    public static class FarmWarningCatalog
    {
        /// <summary>Highest kind value; used to size the per-farm icon slot table.</summary>
        public const int MaxIconKind = (int)FarmWarningIconKind.AdapterFailed;

        /// <summary>Chapter 14.2: only P0 (Critical) and P1 (High) get a world icon.</summary>
        public static bool IsWorldIconSeverity(FarmWarningSeverity severity) =>
            severity == FarmWarningSeverity.Critical || severity == FarmWarningSeverity.High;

        /// <summary>Maps a warning localization key to its icon semantics; unknown keys map to None.</summary>
        public static FarmWarningIconKind IconKindOf(string? codeKey)
        {
            switch (codeKey)
            {
                case HdbfWarningKeys.Shutdown:
                    return FarmWarningIconKind.Shutdown;
                case HdbfWarningKeys.NoWater:
                    return FarmWarningIconKind.NoWater;
                case HdbfWarningKeys.IrrigationLow:
                    return FarmWarningIconKind.IrrigationLow;
                case HdbfWarningKeys.NoPower:
                    return FarmWarningIconKind.NoPower;
                case HdbfWarningKeys.AdapterFailed:
                    return FarmWarningIconKind.AdapterFailed;
                default:
                    return FarmWarningIconKind.None;
            }
        }

        /// <summary>
        /// True when this (severity, key) pair may be shown above the building. Both conditions
        /// must hold: a P0/P1 key mapped to an icon kind and a P0/P1 severity. A malformed pairing
        /// (e.g. a P2 entry carrying the NoPower key) must not produce a world icon.
        /// </summary>
        public static bool HasWorldIcon(FarmWarningSeverity severity, string? codeKey) =>
            IsWorldIconSeverity(severity) && IconKindOf(codeKey) != FarmWarningIconKind.None;

        /// <summary>
        /// Chapter 14.2 ordering: severity first, then the localization key. Null entries sort last
        /// so a defensive list containing null cannot break the comparer.
        /// </summary>
        public static int Compare(FarmWarning? left, FarmWarning? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            var bySeverity = left.Severity.CompareTo(right.Severity);
            return bySeverity != 0
                ? bySeverity
                : string.CompareOrdinal(left.CodeKey, right.CodeKey);
        }
    }

    /// <summary>
    /// One farm's warning set as published to the aggregation cache. Snapshot data is copied into
    /// an immutable subject so the cache never holds a live game object or a mutable list
    /// (chapter 9.2: UI reads immutable read models only).
    /// </summary>
    public sealed record FarmWarningSubject
    {
        public Guid EntityId { get; init; }

        public HdbfTier Tier { get; init; }

        public IReadOnlyList<FarmWarning> Warnings { get; init; } = Array.Empty<FarmWarning>();

        public static FarmWarningSubject FromSnapshot(BoxFarmSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return new FarmWarningSubject();
            }

            // Prefer the runtime id: the long save id cannot address an entity for the locate
            // action. Old snapshots default to Guid.Empty; the aggregation still works, but the
            // UI row is not locatable (TryLocate returns false).
            var runtimeId = snapshot.RuntimeEntityId;
            return new FarmWarningSubject
            {
                EntityId = runtimeId != Guid.Empty ? runtimeId : ToEntityGuid(snapshot.EntityId),
                Tier = snapshot.Tier,
                Warnings = CopyWarnings(snapshot.Warnings),
            };
        }

        public static FarmWarningSubject FromWarnings(Guid entityId, HdbfTier tier,
            IReadOnlyList<FarmWarning>? warnings)
        {
            return new FarmWarningSubject
            {
                EntityId = entityId,
                Tier = tier,
                Warnings = CopyWarnings(warnings),
            };
        }

        /// <summary>
        /// <see cref="BoxFarmSnapshot.EntityId"/> is the runtime Guid stored as a long by the
        /// controller; this is the same reversible mapping the game-side bridge uses for the save
        /// id. Aggregation only needs a stable key, not a lookup back to an entity.
        /// </summary>
        public static Guid ToEntityGuid(long entityId)
        {
            if (entityId == 0L)
            {
                return Guid.Empty;
            }

            var bytes = new byte[16];
            BitConverter.GetBytes(entityId).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        private static IReadOnlyList<FarmWarning> CopyWarnings(IReadOnlyList<FarmWarning>? warnings)
        {
            if (warnings is null || warnings.Count == 0)
            {
                return Array.Empty<FarmWarning>();
            }

            var copy = new List<FarmWarning>(warnings.Count);
            for (var i = 0; i < warnings.Count; i++)
            {
                if (warnings[i] is not null)
                {
                    copy.Add(warnings[i]);
                }
            }

            return copy.ToArray();
        }
    }

    /// <summary>
    /// The per-farm aggregation result of chapter 14.2/14.6: highest priority warning, how many
    /// warnings sit at that priority ("影響條數"), and whether that highest warning is eligible for
    /// the world icon. Pure and allocation-light so it can be recomputed on the 0.5 s icon pass.
    /// </summary>
    public sealed record FarmWarningSummary
    {
        public Guid EntityId { get; init; }

        public HdbfTier Tier { get; init; }

        public string HighestWarningKey { get; init; } = string.Empty;

        /// <summary>Severity of the highest warning; <c>Info</c> when there is no warning.</summary>
        public FarmWarningSeverity HighestSeverity { get; init; } = FarmWarningSeverity.Info;

        /// <summary>Number of warnings at <see cref="HighestSeverity"/> (the "影響條數").</summary>
        public int HighestSeverityCount { get; init; }

        /// <summary>Total number of warnings on the farm (P0..P4).</summary>
        public int TotalCount { get; init; }

        /// <summary>Icon semantics of <see cref="HighestWarningKey"/> (None for P2..P4).</summary>
        public FarmWarningIconKind IconKind { get; init; }

        /// <summary>True only for a P0/P1 warning with a mapped icon (chapter 14.2).</summary>
        public bool HasWorldIcon { get; init; }

        /// <summary>Chapter 14.5: only P0 may blink; P1 icons are static.</summary>
        public bool BlinkAllowed { get; init; }

        public bool IsEmpty => TotalCount == 0;

        public static FarmWarningSummary FromSubject(FarmWarningSubject? subject)
        {
            if (subject is null || subject.Warnings is null || subject.Warnings.Count == 0)
            {
                return new FarmWarningSummary
                {
                    EntityId = subject?.EntityId ?? Guid.Empty,
                    Tier = subject?.Tier ?? HdbfTier.Basic,
                };
            }

            FarmWarning? highest = null;
            for (var i = 0; i < subject.Warnings.Count; i++)
            {
                var warning = subject.Warnings[i];
                if (warning is null)
                {
                    continue;
                }

                if (highest is null || FarmWarningCatalog.Compare(warning, highest) < 0)
                {
                    highest = warning;
                }
            }

            if (highest is null)
            {
                return new FarmWarningSummary
                {
                    EntityId = subject.EntityId,
                    Tier = subject.Tier,
                };
            }

            var countAtHighest = 0;
            for (var i = 0; i < subject.Warnings.Count; i++)
            {
                var warning = subject.Warnings[i];
                if (warning is not null && warning.Severity == highest.Severity)
                {
                    countAtHighest++;
                }
            }

            var iconKind = FarmWarningCatalog.IconKindOf(highest.CodeKey);
            var hasIcon = FarmWarningCatalog.IsWorldIconSeverity(highest.Severity)
                          && iconKind != FarmWarningIconKind.None;

            return new FarmWarningSummary
            {
                EntityId = subject.EntityId,
                Tier = subject.Tier,
                HighestWarningKey = highest.CodeKey,
                HighestSeverity = highest.Severity,
                HighestSeverityCount = countAtHighest,
                TotalCount = subject.Warnings.Count,
                IconKind = iconKind,
                HasWorldIcon = hasIcon,
                BlinkAllowed = hasIcon && highest.Severity == FarmWarningSeverity.Critical,
            };
        }

        public static FarmWarningSummary FromSnapshot(BoxFarmSnapshot snapshot) =>
            FromSubject(FarmWarningSubject.FromSnapshot(snapshot));
    }

    /// <summary>One building that currently reports a given warning key.</summary>
    public sealed record FarmWarningMember
    {
        public Guid EntityId { get; init; }

        public HdbfTier Tier { get; init; }

        public FarmWarningSeverity Severity { get; init; }
    }

    /// <summary>
    /// Chapter 14.2 cross-building aggregation: "3 buildings are low on irrigation" becomes one
    /// row with three locatable members. <see cref="Members"/> is sorted by entity id so the row
    /// order is stable across passes (chapter 14.6 "排序稳定").
    /// </summary>
    public sealed record FarmWarningGroup
    {
        public string CodeKey { get; init; } = string.Empty;

        /// <summary>Highest severity among the members (a group is keyed by kind, not severity).</summary>
        public FarmWarningSeverity Severity { get; init; }

        public IReadOnlyList<FarmWarningMember> Members { get; init; } = Array.Empty<FarmWarningMember>();

        public int Count => Members.Count;
    }

    /// <summary>
    /// Pure aggregator over the cached per-farm warning subjects (chapter 14.2/14.6).
    ///
    /// Determinism: every farm contributes at most one member per key, members are ordered by
    /// entity id, groups by severity then descending count then key. The result is immutable, so
    /// the game-side cache can hand the same list to every panel refresh within a rebuild window.
    /// </summary>
    public static class FarmWarningAggregator
    {
        public static IReadOnlyList<FarmWarningGroup> BuildGroups(
            IReadOnlyList<FarmWarningSubject>? subjects)
        {
            if (subjects is null || subjects.Count == 0)
            {
                return Array.Empty<FarmWarningGroup>();
            }

            var byKey = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);
            for (var i = 0; i < subjects.Count; i++)
            {
                var subject = subjects[i];
                if (subject is null || subject.Warnings is null)
                {
                    continue;
                }

                // A farm must not be counted twice for the same key even if a malformed input
                // repeats it; the evaluator emits one warning per kind, but the cache is defensive.
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                for (var w = 0; w < subject.Warnings.Count; w++)
                {
                    var warning = subject.Warnings[w];
                    if (warning is null || string.IsNullOrEmpty(warning.CodeKey))
                    {
                        continue;
                    }

                    if (!seenKeys.Add(warning.CodeKey))
                    {
                        continue;
                    }

                    if (!byKey.TryGetValue(warning.CodeKey, out var builder))
                    {
                        builder = new GroupBuilder(warning.CodeKey);
                        byKey[warning.CodeKey] = builder;
                    }

                    builder.Add(new FarmWarningMember
                    {
                        EntityId = subject.EntityId,
                        Tier = subject.Tier,
                        Severity = warning.Severity,
                    });
                }
            }

            if (byKey.Count == 0)
            {
                return Array.Empty<FarmWarningGroup>();
            }

            var groups = new List<FarmWarningGroup>(byKey.Count);
            foreach (var builder in byKey.Values)
            {
                groups.Add(builder.Build());
            }

            groups.Sort(CompareGroups);
            return groups;
        }

        /// <summary>Returns the group for one warning key, or <c>null</c> when no farm reports it.</summary>
        public static FarmWarningGroup? FindGroup(IReadOnlyList<FarmWarningGroup>? groups, string? codeKey)
        {
            if (groups is null || groups.Count == 0 || string.IsNullOrEmpty(codeKey))
            {
                return null;
            }

            for (var i = 0; i < groups.Count; i++)
            {
                if (string.Equals(groups[i].CodeKey, codeKey, StringComparison.Ordinal))
                {
                    return groups[i];
                }
            }

            return null;
        }

        private static int CompareGroups(FarmWarningGroup left, FarmWarningGroup right)
        {
            var bySeverity = left.Severity.CompareTo(right.Severity);
            if (bySeverity != 0)
            {
                return bySeverity;
            }

            var byCount = right.Count.CompareTo(left.Count);
            if (byCount != 0)
            {
                return byCount;
            }

            return string.CompareOrdinal(left.CodeKey, right.CodeKey);
        }

        private sealed class GroupBuilder
        {
            private readonly string _codeKey;
            private readonly List<FarmWarningMember> _members = new List<FarmWarningMember>();
            private FarmWarningSeverity _severity = FarmWarningSeverity.Info;

            public GroupBuilder(string codeKey)
            {
                _codeKey = codeKey;
            }

            public void Add(FarmWarningMember member)
            {
                // First member defines the seed; Min keeps the highest severity for the group.
                if (_members.Count == 0 || member.Severity < _severity)
                {
                    _severity = member.Severity;
                }

                _members.Add(member);
            }

            public FarmWarningGroup Build()
            {
                _members.Sort(CompareMembers);
                return new FarmWarningGroup
                {
                    CodeKey = _codeKey,
                    Severity = _severity,
                    Members = _members.ToArray(),
                };
            }

            private static int CompareMembers(FarmWarningMember left, FarmWarningMember right) =>
                left.EntityId.CompareTo(right.EntityId);
        }
    }
}
