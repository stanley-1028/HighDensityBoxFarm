using System;
using System.Collections.Generic;

namespace HDBF.Core.Plots
{
    /// <summary>
    /// What the gameplay layer must do for the crop visual sitting on one managed plot
    /// (BUILD-05, user design decision of 2026-10-25: the crop itself is grown and harvested by
    /// real vanilla <c>Crop</c>/<c>Growable</c> entities, but its model must not be visible from
    /// the ground — only the building's internal slots stay visible).
    ///
    /// The action is a *renderer-only* instruction: <see cref="Hide"/> disables the model
    /// renderers of the crop entity, <see cref="Restore"/> enables them again. It must never stop
    /// growth, yield or harvesting, and it must never touch a crop that is not on a registered
    /// plot of this farm (ADR-009's isolation rule, same class of constraint as BUILD-02/BUILD-04).
    /// </summary>
    public enum ManagedCropVisualAction
    {
        /// <summary>Nothing to do: the visual already matches the desired state.</summary>
        None = 0,

        /// <summary>Disable the crop model's renderers (visual hidden, simulation untouched).</summary>
        Hide = 1,

        /// <summary>Enable the crop model's renderers again (restore the vanilla visible crop).</summary>
        Restore = 2,
    }

    /// <summary>
    /// Game-free decision + cache state machine of BUILD-05 ("托管格上的農作物視覺完全隱藏"):
    /// given the farm's registered plot ids, the per-plot crop observation and the
    /// <c>ui.hideManagedCrops</c> switch, it says whether the gameplay layer must hide, restore or
    /// leave one crop visual alone. All mutable state lives in this instance, the type owns no
    /// static/global state, and it never throws for any input.
    ///
    /// Why a state machine instead of one pure boolean function: the controller scans the known
    /// plot cells at a low, configurable frequency (workers plant crops without telling the mod,
    /// so discovery must poll — chapter 11.3/10.5 forbid a per-frame full-map walk), and the
    /// cache is what keeps a 600-plot farm from re-toggling the same renderers every sweep:
    /// * the hidden set is keyed by plot id with the crop <b>entity id</b> as value, so a harvested
    ///   crop whose plot is replanted is a different identity and gets hidden again (a plain
    ///   "plot id only" set would leave the new crop visible forever);
    /// * a crop that disappeared (harvested/removed → <c>hasCrop=false</c>) produces
    ///   <see cref="ManagedCropVisualAction.Restore"/> exactly once, so the cache is cleaned even
    ///   when the entity is already gone (the gameplay restore is then a safe no-op);
    /// * changing the registered plot set returns the hidden plots that are no longer managed, so
    ///   the controller restores them immediately instead of waiting for a sweep
    ///   (cancel registration / player exclusion / building demolition).
    ///
    /// The sharded refresh cursor (<see cref="BeginRefreshCycle"/> /
    /// <see cref="TakeRefreshShard"/>) is here as well: the "known plot cells" walk must be split
    /// into per-frame slices of at most <c>perf.cropVisibilityCellsPerFrame</c> cells, never one
    /// synchronous 600-cell pass (chapter 10.5/14.6 frame budget).
    /// </summary>
    public sealed class ManagedCropVisibilityState
    {
        /// <summary>
        /// Hidden plot id → identity of the crop entity whose renderers were disabled. The
        /// identity is the vanilla <c>EntityComponent.EntityId</c>; <see cref="Guid.Empty"/> is
        /// allowed as a fallback identity for a crop without a readable entity id and simply means
        /// "replacement detection is weaker for this plot", never "hide everything".
        /// </summary>
        private readonly Dictionary<int, Guid> _hidden = new Dictionary<int, Guid>();

        /// <summary>Registered plot ids as a set, for the O(1) "is this plot managed" test.</summary>
        private readonly HashSet<int> _registered = new HashSet<int>();

        /// <summary>Registered plot ids in registration order; the refresh walk visits them all.</summary>
        private int[] _order = Array.Empty<int>();

        private int _cursor;

        /// <summary>
        /// The <c>ui.hideManagedCrops</c> switch as last observed by the controller. Default
        /// <c>true</c> (BUILD-05 ships the hiding on; <c>false</c> restores every crop visual).
        /// </summary>
        public bool HideEnabled { get; private set; } = true;

        /// <summary>Number of registered plots the next refresh cycle will visit (0 before scan).</summary>
        public int RegisteredCount => _order.Length;

        /// <summary>Number of crop visuals this farm currently hides (the observability figure).</summary>
        public int HiddenCount => _hidden.Count;

        /// <summary>Current position of the sharded refresh walk (0..RegisteredCount-1).</summary>
        public int RefreshCursor => _cursor;

        /// <summary>
        /// Applies the configuration switch. Turning it off must restore every hidden crop, so
        /// the returned plot ids are the ones whose renderers the caller has to enable again;
        /// turning it on (or an unchanged value) returns an empty array.
        /// </summary>
        public int[] SetHideEnabled(bool enabled)
        {
            if (enabled == HideEnabled)
            {
                return Array.Empty<int>();
            }

            HideEnabled = enabled;
            return enabled ? Array.Empty<int>() : DrainHidden();
        }

        /// <summary>
        /// Replaces the registered plot set after a scan, a player exclusion or a save load.
        /// Order is preserved (the refresh walk visits plots in registration order) and the
        /// returned array contains every hidden plot that is no longer registered, so the caller
        /// can restore those crop visuals immediately (registered-plot cancellation path).
        /// When the switch is off nothing can be cached as hidden (the switch-off path drains the
        /// set), so the result is then empty by invariant. Never returns <c>null</c>; a null list
        /// is treated as an empty registration.
        /// </summary>
        public int[] SetRegisteredPlots(IReadOnlyList<int>? plotIds)
        {
            _registered.Clear();
            _cursor = 0;

            var count = plotIds?.Count ?? 0;
            if (count <= 0)
            {
                _order = Array.Empty<int>();
                return HideEnabled ? DrainHidden() : Array.Empty<int>();
            }

            var order = new int[count];
            var written = 0;
            for (var i = 0; i < count; i++)
            {
                var plotId = plotIds![i];
                order[written++] = plotId;
                _registered.Add(plotId);
            }

            if (written != order.Length)
            {
                Array.Resize(ref order, written);
            }

            _order = order;
            if (!HideEnabled || _hidden.Count == 0)
            {
                return Array.Empty<int>();
            }

            return DrainHidden(plotId => !_registered.Contains(plotId));
        }

        /// <summary>
        /// Decisions for one observed managed-plot cell. The controller calls this for every plot
        /// of the current refresh shard, and once more on an explicit trigger (a crop was just
        /// planted/harvested). Idempotent: repeating the same observation returns
        /// <see cref="ManagedCropVisualAction.None"/> after the first hide/restore.
        /// </summary>
        /// <param name="plotId">Registered plot id of the cell.</param>
        /// <param name="hasCrop">True when a vanilla crop component was found on the cell.</param>
        /// <param name="cropEntityId">
        /// Identity of that crop entity (entity id). Only compared with the cached identity; it is
        /// never interpreted, so any stable per-entity value works.
        /// </param>
        public ManagedCropVisualAction Observe(int plotId, bool hasCrop, Guid cropEntityId)
        {
            var wasHidden = _hidden.TryGetValue(plotId, out var previousIdentity);

            // Any of "switch off", "no longer managed", "no crop there" means the plot must not
            // keep a hidden visual; a previous hide is restored exactly once and the cache entry
            // is dropped (a harvested crop is already gone, the restore is then a safe no-op).
            if (!HideEnabled || !_registered.Contains(plotId) || !hasCrop)
            {
                if (!wasHidden)
                {
                    return ManagedCropVisualAction.None;
                }

                _hidden.Remove(plotId);
                return ManagedCropVisualAction.Restore;
            }

            // Same crop as the one already hidden: nothing to re-apply (this is the cache that
            // keeps a 600-plot sweep idempotent and cheap).
            if (wasHidden && previousIdentity == cropEntityId)
            {
                return ManagedCropVisualAction.None;
            }

            _hidden[plotId] = cropEntityId;
            return ManagedCropVisualAction.Hide;
        }

        /// <summary>True when this plot's crop visual is currently in the hidden set.</summary>
        public bool IsHidden(int plotId)
        {
            return _hidden.ContainsKey(plotId);
        }

        /// <summary>
        /// Reads the cached crop identity of a hidden plot; false when nothing is hidden there.
        /// The controller uses it to decide whether a restore may still touch the entity it hid.
        /// </summary>
        public bool TryGetHiddenIdentity(int plotId, out Guid cropEntityId)
        {
            return _hidden.TryGetValue(plotId, out cropEntityId);
        }

        /// <summary>
        /// Drops one cached hide <b>without</b> asking for a restore. Used when the gameplay layer
        /// could not actually disable the renderers after <see cref="Observe"/> had cached the
        /// decision (e.g. the crop was destroyed mid-toggle): without this the cache would report
        /// "already hidden" and the next sweep would never retry, leaving a visible crop on a
        /// managed plot. Returns true when an entry was present.
        /// </summary>
        public bool Forget(int plotId)
        {
            return _hidden.Remove(plotId);
        }

        /// <summary>
        /// Drops every cached hide and returns the plot ids the caller must restore. One-shot by
        /// design: used by the switch-off path, the save-unload/deactivate path, and by tests.
        /// Never returns <c>null</c>.
        /// </summary>
        public int[] RestoreAll()
        {
            return DrainHidden();
        }

        /// <summary>
        /// Restarts the sharded refresh walk at the first registered plot. The caller decides
        /// *when* a cycle runs (low frequency, configurable interval); this type only bounds the
        /// per-frame slice, which is why the same state object can be tested without any clock.
        /// </summary>
        public void BeginRefreshCycle()
        {
            _cursor = 0;
        }

        /// <summary>
        /// Copies at most <paramref name="budget"/> registered plot ids into
        /// <paramref name="buffer"/>, advancing the refresh cursor. <paramref name="cycleCompleted"/>
        /// is true when the walk reached the end of the registered set and wrapped to the start
        /// (the caller then waits one interval before the next cycle). Returns 0 for an empty
        /// registration, an empty buffer, a non-positive budget or no registered plots; never
        /// throws and never allocates.
        /// </summary>
        public int TakeRefreshShard(int[]? buffer, int budget, out bool cycleCompleted)
        {
            cycleCompleted = false;
            if (buffer is null || buffer.Length == 0 || budget <= 0 || _order.Length == 0)
            {
                return 0;
            }

            var remaining = _order.Length - _cursor;
            var take = budget < remaining ? budget : remaining;
            if (take > buffer.Length)
            {
                take = buffer.Length;
            }

            for (var i = 0; i < take; i++)
            {
                buffer[i] = _order[_cursor + i];
            }

            _cursor += take;
            if (_cursor >= _order.Length)
            {
                _cursor = 0;
                cycleCompleted = true;
            }

            return take;
        }

        /// <summary>
        /// Removes hidden entries (all of them, or only those matching <paramref name="filter"/>)
        /// and returns the plot ids in ascending order for deterministic restore batches.
        /// </summary>
        private int[] DrainHidden(Func<int, bool>? filter = null)
        {
            if (_hidden.Count == 0)
            {
                return Array.Empty<int>();
            }

            var drained = new List<int>(_hidden.Count);
            foreach (var pair in _hidden)
            {
                if (filter is null || filter(pair.Key))
                {
                    drained.Add(pair.Key);
                }
            }

            for (var i = 0; i < drained.Count; i++)
            {
                _hidden.Remove(drained[i]);
            }

            if (drained.Count == 0)
            {
                return Array.Empty<int>();
            }

            drained.Sort();
            return drained.ToArray();
        }
    }
}
