// ============================================================================
// HDBF v2 | Layer: Core/Data | Owner: V2-CONFIG-02 (L-16 blueprint history contract)
// Purpose: Pure L-16 contract of the published TemplateName/BlockObjectSpec.Size history of
//          the BoxFarm blueprints: the shipped data must stay inside the recorded history, a
//          renamed blueprint must list its old names in BackwardCompatibleTemplateNames, and a
//          footprint change must first be recorded (and migration-reviewed) in the history file
//          tools/validate/boxfarm-history.json. No game/Unity reference; used by the data
//          validator (source-linked) and by the offline L1 contract.
// Requirements: REQ-V2-BLD-01, REQ-V2-EXT-04, REQ-V2-SAVE-06, L-16 (BUG_REPAIR_20261102)
// ============================================================================
using System;
using System.Collections.Generic;

namespace HDBF.Core.Data
{
    /// <summary>
    /// One <c>BlockObjectSpec.Size</c> triple (X = width, Y = depth, Z = height; API_FACTS.md). A
    /// value type so the published-history table can be compared without allocation.
    /// </summary>
    public readonly struct BlueprintBlockSize : IEquatable<BlueprintBlockSize>
    {
        /// <summary>Creates one size triple.</summary>
        /// <param name="x">Width (blocks along X).</param>
        /// <param name="y">Depth (blocks along Y).</param>
        /// <param name="z">Height (blocks along Z).</param>
        public BlueprintBlockSize(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Width in blocks.</summary>
        public int X { get; }

        /// <summary>Depth in blocks.</summary>
        public int Y { get; }

        /// <summary>Height in blocks.</summary>
        public int Z { get; }

        /// <inheritdoc />
        public bool Equals(BlueprintBlockSize other) => X == other.X && Y == other.Y && Z == other.Z;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is BlueprintBlockSize other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Z;
                return hash;
            }
        }

        /// <summary>Game-log friendly form, for example <c>3x3x1</c>.</summary>
        /// <returns>The three axes joined by 'x'.</returns>
        public override string ToString() => X + "x" + Y + "x" + Z;
    }

    /// <summary>One shipped release of a blueprint, as recorded in the L-16 history file.</summary>
    public sealed class BlueprintPublishedRelease
    {
        /// <summary>Creates one published release entry.</summary>
        /// <param name="version">Release label that published this state (e.g. "2.0.0").</param>
        /// <param name="size">The <c>BlockObjectSpec.Size</c> that release shipped.</param>
        /// <param name="note">Short human note/evidence of the release (may be empty).</param>
        public BlueprintPublishedRelease(string version, BlueprintBlockSize size, string? note = null)
        {
            Version = version ?? string.Empty;
            Size = size;
            Note = note ?? string.Empty;
        }

        /// <summary>Release label of the entry (free text, e.g. a mod version).</summary>
        public string Version { get; }

        /// <summary>Footprint the release shipped.</summary>
        public BlueprintBlockSize Size { get; }

        /// <summary>Evidence/notes of the release; never null.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// Recorded history of one blueprint: every published TemplateName and every published
    /// footprint, oldest release first (the L-16 gate treats the last entry as the current one).
    /// </summary>
    public sealed class BlueprintHistoryExpectation
    {
        /// <summary>Creates one blueprint history entry.</summary>
        /// <param name="blueprintPath">Blueprint path relative to the mod root data directory.</param>
        /// <param name="publishedTemplateNames">All TemplateName values ever shipped for the blueprint.</param>
        /// <param name="releases">Published footprint entries, oldest first.</param>
        public BlueprintHistoryExpectation(string blueprintPath,
            IReadOnlyList<string>? publishedTemplateNames,
            IReadOnlyList<BlueprintPublishedRelease>? releases)
        {
            BlueprintPath = blueprintPath ?? string.Empty;
            PublishedTemplateNames = publishedTemplateNames ?? Array.Empty<string>();
            Releases = releases ?? Array.Empty<BlueprintPublishedRelease>();
        }

        /// <summary>Blueprint path relative to the mod <c>data</c> directory.</summary>
        public string BlueprintPath { get; }

        /// <summary>Every TemplateName value ever shipped for this blueprint.</summary>
        public IReadOnlyList<string> PublishedTemplateNames { get; }

        /// <summary>Published footprints, oldest first.</summary>
        public IReadOnlyList<BlueprintPublishedRelease> Releases { get; }
    }

    /// <summary>
    /// The three L-16 gates over one blueprint and its recorded history. Pure functions only, so the
    /// data validator and the offline L1 contract run exactly the same rules.
    /// </summary>
    public static class BlueprintHistoryContract
    {
        /// <summary>
        /// Evaluates the L-16 contract of one blueprint (a) current TemplateName is in the recorded
        /// history, (b) every historical name other than the current one is listed in
        /// <c>BackwardCompatibleTemplateNames</c>, (c) the current footprint equals the last published
        /// footprint, so any footprint change forces the history file update and a migration review.
        /// </summary>
        /// <param name="history">Recorded history of the blueprint; null is a violation.</param>
        /// <param name="currentTemplateName">TemplateName of the shipped blueprint.</param>
        /// <param name="backwardTemplateNames">Shipped BackwardCompatibleTemplateNames; null = empty.</param>
        /// <param name="currentSize">BlockObjectSpec.Size of the shipped blueprint.</param>
        /// <returns>Concrete problems; an empty list means the blueprint passes.</returns>
        public static IReadOnlyList<string> Evaluate(BlueprintHistoryExpectation? history,
            string? currentTemplateName, IReadOnlyList<string>? backwardTemplateNames,
            BlueprintBlockSize currentSize)
        {
            var problems = new List<string>();
            if (history is null)
            {
                problems.Add("has no entry in tools/validate/boxfarm-history.json");
                return problems;
            }

            var published = history.PublishedTemplateNames;
            if (published is null)
            {
                problems.Add("its history entry lists no published TemplateName");
                return problems;
            }

            if (published.Count == 0)
            {
                problems.Add("its history entry lists no published TemplateName");
            }
            else if (string.IsNullOrEmpty(currentTemplateName) || !Contains(published, currentTemplateName!))
            {
                problems.Add("current TemplateName '" + (currentTemplateName ?? "<null>")
                             + "' is not in the recorded published names (" + Join(published) + ")");
            }

            // (b) every historical (old) name must be readable by old saves through the backlog list.
            for (var index = 0; index < published.Count; index++)
            {
                var oldName = published[index];
                if (string.IsNullOrEmpty(oldName)
                    || string.Equals(oldName, currentTemplateName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (backwardTemplateNames is null || !Contains(backwardTemplateNames, oldName))
                {
                    problems.Add("historical TemplateName '" + oldName
                                 + "' is missing from BackwardCompatibleTemplateNames (old saves would be"
                                 + " deleted as not backward compatible)");
                }
            }

            var releases = history.Releases;
            if (releases is null || releases.Count == 0)
            {
                problems.Add("its history entry lists no published footprint");
                return problems;
            }

            // (c) footprint changes are a save-migration red line: record first, then ship.
            var last = releases[releases.Count - 1];
            if (!last.Size.Equals(currentSize))
            {
                problems.Add("current BlockObjectSpec.Size " + currentSize + " differs from the last published"
                             + " footprint " + last.Size + " (" + last.Version + "); update"
                             + " tools/validate/boxfarm-history.json and perform the save-migration review"
                             + " before shipping this footprint");
            }

            return problems;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Join(IReadOnlyList<string> values)
        {
            var text = string.Empty;
            for (var index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    text += ", ";
                }

                text += values[index];
            }

            return text;
        }
    }
}
