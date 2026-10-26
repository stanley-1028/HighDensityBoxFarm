// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Immutable summary of one save-repair pass (INTERFACES section 8) plus the
//          bounded note accumulator the validator/slot repair fill. No logging and no
//          repair logic here: the HDBF.Persistence bridge logs the notes (save.* keys).
// Requirements: REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-CORE-02, REQ-V2-READ-02
// ============================================================================
// Namespace: HDBF.Core.Indoor (frozen INTERFACES section 8 contract; no .Save sub-namespace).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// What one <see cref="IndoorSaveValidator.ValidateAndRepair"/> pass changed
    /// (INTERFACES section 8, REQ-V2-SAVE-06). A clean record returns
    /// <see cref="IndoorRepairResult.Clean"/>; every repair is counted and, when it can be
    /// attributed to a field or slot index, described by one entry of <see cref="Notes"/> so the
    /// persistence bridge can write a locatable <c>save.*</c> log line.
    /// </summary>
    public sealed class IndoorRepairResult
    {
        /// <summary>Shared result of a pass that found nothing to repair (immutable, thread-safe).</summary>
        public static IndoorRepairResult Clean { get; } = new IndoorRepairResult(false, 0, 0, 0, Array.Empty<string>());

        private readonly ReadOnlyCollection<string> _notes;

        /// <summary>
        /// Creates a repair summary. Counters below zero are treated as zero; a null note list is
        /// treated as empty, so callers (and tests) can synthesize a summary safely.
        /// </summary>
        /// <param name="repaired">True when any counter is non-zero.</param>
        /// <param name="slotsTrimmed">Slot array entries dropped because the array was too long.</param>
        /// <param name="slotsPadded">Empty slots appended because the array was too short.</param>
        /// <param name="valuesClamped">Individual field/slot values repaired, clamped or cleared.</param>
        /// <param name="notes">Optional locatable repair notes, e.g. <c>slot[3].growth-clamped</c>.</param>
        public IndoorRepairResult(bool repaired, int slotsTrimmed, int slotsPadded, int valuesClamped,
            IReadOnlyList<string>? notes)
        {
            SlotsTrimmed = slotsTrimmed > 0 ? slotsTrimmed : 0;
            SlotsPadded = slotsPadded > 0 ? slotsPadded : 0;
            ValuesClamped = valuesClamped > 0 ? valuesClamped : 0;

            var copy = new List<string>(notes is null ? 0 : notes.Count);
            if (notes is not null)
            {
                for (var index = 0; index < notes.Count; index++)
                {
                    var note = notes[index];
                    if (!string.IsNullOrEmpty(note))
                    {
                        copy.Add(Shorten(note!));
                    }
                }
            }

            _notes = new ReadOnlyCollection<string>(copy);
            Repaired = repaired || SlotsTrimmed > 0 || SlotsPadded > 0 || ValuesClamped > 0;
        }

        /// <summary>True when the record was changed by the repair pass and must not be written as-is.</summary>
        public bool Repaired { get; }

        /// <summary>Number of trailing slot entries dropped because the array exceeded the capacity.</summary>
        public int SlotsTrimmed { get; }

        /// <summary>Number of empty slots appended because the array was shorter than the capacity.</summary>
        public int SlotsPadded { get; }

        /// <summary>Number of individual values repaired (clamped, cleared, whitelisted or filled).</summary>
        public int ValuesClamped { get; }

        /// <summary>Locatable repair notes, in repair order (bounded by <see cref="IndoorSaveLimits.MaxRepairNotes"/>).</summary>
        public IReadOnlyList<string> Notes => _notes;

        /// <summary>One-line invariant summary for <c>save.validation-failed</c> / diagnostics.</summary>
        /// <returns>Deterministic text, e.g. <c>repaired=True trimmed=300 padded=0 clamped=4 notes=2</c>.</returns>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "repaired={0} trimmed={1} padded={2} clamped={3} notes={4}",
                Repaired, SlotsTrimmed, SlotsPadded, ValuesClamped, _notes.Count);
        }

        private static string Shorten(string value)
        {
            return value.Length <= IndoorSaveLimits.MaxDiagnosticLength
                ? value
                : value.Substring(0, IndoorSaveLimits.MaxDiagnosticLength);
        }
    }

    /// <summary>
    /// Mutable repair collector used while a validator pass runs. Kept in this file because it is
    /// the only writer of <see cref="IndoorRepairResult"/>; the bounded note list enforces
    /// REQ-V2-SAFE-03 (a corrupted 600-slot record may not allocate an unbounded report).
    /// </summary>
    internal sealed class IndoorRepairAccumulator
    {
        private readonly List<string> _notes = new List<string>(4);
        private int _suppressedNotes;
        private bool _invalid;

        /// <summary>Slot entries dropped because the array exceeded the tier capacity.</summary>
        public int SlotsTrimmed { get; private set; }

        /// <summary>Empty slots appended because the array was shorter than the tier capacity.</summary>
        public int SlotsPadded { get; private set; }

        /// <summary>Individual values repaired.</summary>
        public int ValuesClamped { get; private set; }

        /// <summary>True when the record was changed.</summary>
        public bool Repaired => _invalid || SlotsTrimmed > 0 || SlotsPadded > 0 || ValuesClamped > 0;

        /// <summary>
        /// Marks the record as NOT writable although no value could be repaired (missing record or
        /// missing tier profile), so the caller keeps the previous good snapshot (REQ-V2-SAVE-04).
        /// </summary>
        public void Fail(string note)
        {
            _invalid = true;
            Note(note);
        }

        /// <summary>Records that <paramref name="count"/> trailing slot entries were dropped.</summary>
        public void AddTrimmed(int count)
        {
            if (count > 0) SlotsTrimmed += count;
        }

        /// <summary>Records that <paramref name="count"/> empty slots were appended.</summary>
        public void AddPadded(int count)
        {
            if (count > 0) SlotsPadded += count;
        }

        /// <summary>Records one clamped/cleared/filled value and its locatable note.</summary>
        public void Clamp(string note)
        {
            ValuesClamped++;
            Note(note);
        }

        /// <summary>Adds one locatable note; notes beyond <see cref="IndoorSaveLimits.MaxRepairNotes"/> are counted only.</summary>
        public void Note(string note)
        {
            if (string.IsNullOrEmpty(note)) return;
            if (_notes.Count < IndoorSaveLimits.MaxRepairNotes)
            {
                _notes.Add(note!);
            }
            else
            {
                _suppressedNotes++;
            }
        }

        /// <summary>
        /// Builds the immutable summary. When the record was repaired, the owning entity id is
        /// prepended so a single log line identifies the building (REQ-V2-SAVE-06).
        /// </summary>
        public IndoorRepairResult Build(string? entityId)
        {
            var notes = new List<string>(_notes.Count + 2);
            if (Repaired && !string.IsNullOrEmpty(entityId))
            {
                notes.Add("entity:" + entityId);
            }

            notes.AddRange(_notes);
            if (_suppressedNotes > 0)
            {
                notes.Add("+" + _suppressedNotes.ToString(CultureInfo.InvariantCulture) + " more");
            }

            return new IndoorRepairResult(Repaired, SlotsTrimmed, SlotsPadded, ValuesClamped, notes);
        }
    }
}
