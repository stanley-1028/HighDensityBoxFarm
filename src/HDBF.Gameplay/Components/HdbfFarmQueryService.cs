using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session singleton that answers UI/diagnostic queries and owns swappable session state:
    /// the farm lookup by entity id (chapter 9.2 <c>IBoxFarmQuery</c>) and the plot claim table
    /// (REQ-BLD-04: one managed plot belongs to exactly one box farm, first registration wins).
    ///
    /// Contract:
    /// * every public entry point is isolated: a broken farm can never stop UI or the other
    ///   farms from rendering (chapter 10.1, REQ-SAFE-02);
    /// * snapshots are built on demand and never stored in a save (chapter 9.2);
    /// * all members are main-thread only, matching the game's simulation model, so the shared
    ///   tables need no locking.
    /// </summary>
    public sealed class HdbfFarmQueryService : IBoxFarmQuery, IHdbfFarmQueryService, IHdbfFarmRegistry
    {
        private const string LogKeySnapshot = "gameplay.snapshot";

        /// <summary>Default value returned with a failed lookup; never null for UI consumers.</summary>
        private static readonly BoxFarmSnapshot EmptySnapshot = new BoxFarmSnapshot();

        private readonly IHdbfLog _log;

        private readonly Dictionary<Guid, IHdbfBoxFarmSnapshotSource> _sources =
            new Dictionary<Guid, IHdbfBoxFarmSnapshotSource>();

        private readonly Dictionary<Guid, HdbWarningEvaluator> _evaluators =
            new Dictionary<Guid, HdbWarningEvaluator>();

        /// <summary>plotId → owning farm entity id.</summary>
        private readonly Dictionary<int, Guid> _plotOwners = new Dictionary<int, Guid>();

        /// <summary>farm entity id → plots it currently owns (kept for O(1) release).</summary>
        private readonly Dictionary<Guid, HashSet<int>> _plotsByFarm =
            new Dictionary<Guid, HashSet<int>>();

        public HdbfFarmQueryService(IHdbfLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public int RegisteredFarmCount => _sources.Count;

        /// <inheritdoc />
        public bool TryGetSnapshot(Guid entityId, out BoxFarmSnapshot snapshot)
        {
            if (entityId != Guid.Empty && _sources.TryGetValue(entityId, out var source))
            {
                try
                {
                    snapshot = BuildSnapshot(source);
                    return true;
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Error, LogKeySnapshot,
                        "farm snapshot failed; using empty snapshot", exception, entityId.ToString());
                }
            }

            snapshot = EmptySnapshot;
            return false;
        }

        /// <inheritdoc />
        public IReadOnlyList<BoxFarmSnapshot> AllSnapshots()
        {
            if (_sources.Count == 0)
            {
                return Array.Empty<BoxFarmSnapshot>();
            }

            var snapshots = new List<BoxFarmSnapshot>(_sources.Count);
            foreach (var pair in _sources)
            {
                try
                {
                    snapshots.Add(BuildSnapshot(pair.Value));
                }
                catch (Exception exception)
                {
                    // One damaged farm must not hide the others (REQ-SAFE-08).
                    _log.Error(HdbfLogCategory.Error, LogKeySnapshot,
                        "farm snapshot failed and was skipped", exception, pair.Key.ToString());
                }
            }

            return snapshots;
        }

        /// <summary>
        /// Resolves the live snapshot source for one farm. Used by the command service (the
        /// write-side panel contract) which needs the concrete controller, not just a snapshot,
        /// and by the ADR-009 growth-rate provider.
        /// </summary>
        public bool TryGetSource(Guid entityId, out IHdbfBoxFarmSnapshotSource source)
        {
            if (entityId != Guid.Empty && _sources.TryGetValue(entityId, out var found))
            {
                source = found;
                return true;
            }

            source = null!;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetPlotOwner(int plotId, out Guid ownerEntityId)
        {
            return _plotOwners.TryGetValue(plotId, out ownerEntityId);
        }

        /// <inheritdoc />
        public void RegisterSource(IHdbfBoxFarmSnapshotSource source)
        {
            if (source is null || source.EntityId == Guid.Empty)
            {
                return;
            }

            _sources[source.EntityId] = source;
            if (!_evaluators.ContainsKey(source.EntityId))
            {
                _evaluators[source.EntityId] = new HdbWarningEvaluator();
            }
        }

        /// <inheritdoc />
        public void UnregisterSource(Guid entityId)
        {
            if (entityId == Guid.Empty)
            {
                return;
            }

            _sources.Remove(entityId);
            if (_evaluators.TryGetValue(entityId, out var evaluator))
            {
                evaluator.Reset();
                _evaluators.Remove(entityId);
            }

            ReleasePlots(entityId);
        }

        /// <inheritdoc />
        public bool IsPlotClaimedByOther(int plotId, Guid ownerEntityId)
        {
            return _plotOwners.TryGetValue(plotId, out var owner) && owner != ownerEntityId;
        }

        /// <inheritdoc />
        public int[] ApplyPlotSelection(Guid ownerEntityId, IReadOnlyList<int> desiredPlotIds)
        {
            if (ownerEntityId == Guid.Empty || desiredPlotIds is null || desiredPlotIds.Count == 0)
            {
                ReleasePlots(ownerEntityId);
                return Array.Empty<int>();
            }

            if (!_plotsByFarm.TryGetValue(ownerEntityId, out var owned))
            {
                owned = new HashSet<int>();
                _plotsByFarm[ownerEntityId] = owned;
            }

            var granted = new List<int>(desiredPlotIds.Count);
            var grantedSet = new HashSet<int>();
            for (var i = 0; i < desiredPlotIds.Count; i++)
            {
                var plotId = desiredPlotIds[i];
                if (!_plotOwners.TryGetValue(plotId, out var currentOwner) || currentOwner == ownerEntityId)
                {
                    _plotOwners[plotId] = ownerEntityId;
                    if (grantedSet.Add(plotId))
                    {
                        granted.Add(plotId);
                    }
                }
            }

            // Drop claims the farm no longer uses (rescan shrank the set, plot was excluded).
            if (owned.Count > 0)
            {
                var stale = new List<int>();
                foreach (var plotId in owned)
                {
                    if (!grantedSet.Contains(plotId))
                    {
                        stale.Add(plotId);
                    }
                }

                for (var i = 0; i < stale.Count; i++)
                {
                    _plotOwners.Remove(stale[i]);
                }
            }

            owned.Clear();
            for (var i = 0; i < granted.Count; i++)
            {
                owned.Add(granted[i]);
            }

            return granted.ToArray();
        }

        /// <inheritdoc />
        public void ReleasePlots(Guid ownerEntityId)
        {
            if (!_plotsByFarm.TryGetValue(ownerEntityId, out var owned))
            {
                return;
            }

            foreach (var plotId in owned)
            {
                if (_plotOwners.TryGetValue(plotId, out var currentOwner) && currentOwner == ownerEntityId)
                {
                    _plotOwners.Remove(plotId);
                }
            }

            owned.Clear();
        }

        private BoxFarmSnapshot BuildSnapshot(IHdbfBoxFarmSnapshotSource source)
        {
            var snapshot = source.CreateSnapshot();
            if (!_evaluators.TryGetValue(source.EntityId, out var evaluator))
            {
                evaluator = new HdbWarningEvaluator();
                _evaluators[source.EntityId] = evaluator;
            }

            // Wall-clock gate: the panel (≤4 Hz) and the world-icon pass (2 Hz) can both build
            // snapshots for the same farm, so the 2 s hysteresis must not depend on sample count
            // (chapter 14.2 / 14.6).
            var warnings = evaluator.Evaluate(source.CreateWarningInputs(snapshot),
                Time.unscaledTime);
            return warnings.Length == 0
                ? snapshot
                : snapshot with { Warnings = warnings };
        }
    }
}
