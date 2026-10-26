// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-UI-01
// Purpose: Session singleton behind IBoxFarmQuery/IHdbfFarmQueryService. Looks up live
//          v2 indoor controllers, exposes the Core IndoorFarmSnapshot read model, forwards the
//          two v2 panel commands and rebuilds the retained warning framework as the four v2
//          warning categories. No plot registry and no world-cell ownership.
// Requirements: REQ-V2-UI-01/02/03, REQ-V2-BAL-03, REQ-V2-SAFE-01/07, REQ-V2-CORE-03
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.Core.Warnings;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session singleton that answers UI/diagnostic queries for live indoor farms. It keeps only
    /// the entity → source table, one warning evaluator and one entrance-blocking gate per farm;
    /// snapshots are rebuilt on demand and never stored in a save.
    ///
    /// Contract: every public entry point isolates a broken farm so UI and the other farms keep
    /// rendering (REQ-V2-SAFE-01/07). All calls are main-thread only, matching the game model.
    /// </summary>
    public sealed class HdbfFarmQueryService : IBoxFarmQuery, IHdbfFarmQueryService
    {
        private const string LogKeySnapshot = "ui.snapshot";

        private static readonly BoxFarmSnapshot EmptySnapshot = new BoxFarmSnapshot();

        private readonly IHdbfLog _log;
        private readonly Dictionary<Guid, IHdbfBoxFarmSnapshotSource> _sources =
            new Dictionary<Guid, IHdbfBoxFarmSnapshotSource>();
        private readonly Dictionary<Guid, HdbWarningEvaluator> _evaluators =
            new Dictionary<Guid, HdbWarningEvaluator>();
        private readonly Dictionary<Guid, WarningHysteresis> _entranceGates =
            new Dictionary<Guid, WarningHysteresis>();

        /// <summary>Creates the query singleton with the shared throttled log.</summary>
        public HdbfFarmQueryService(IHdbfLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Number of live indoor farms; diagnostics and reduced-mode policy.</summary>
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
                        "indoor farm snapshot failed; using empty snapshot", exception, entityId.ToString());
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
                    // One damaged farm must not hide the others (REQ-V2-SAFE-01).
                    _log.Error(HdbfLogCategory.Error, LogKeySnapshot,
                        "indoor farm snapshot failed and was skipped", exception, pair.Key.ToString());
                }
            }

            return snapshots;
        }

        /// <inheritdoc />
        public IndoorFarmSnapshot? GetIndoorSnapshot(Guid entityId)
        {
            if (entityId == Guid.Empty || !_sources.TryGetValue(entityId, out var source))
            {
                return null;
            }

            try
            {
                return source.CreateIndoorSnapshot();
            }
            catch (Exception exception)
            {
                // Missing data renders as "not available" (the panel hides); never a thrown panel.
                _log.Error(HdbfLogCategory.Error, LogKeySnapshot,
                    "indoor panel snapshot failed; panel hides its rows", exception, entityId.ToString());
                return null;
            }
        }

        /// <inheritdoc />
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

        /// <summary>Registers one finished indoor farm; replaces any earlier source for the entity.</summary>
        /// <param name="source">Live controller source; null/empty id is ignored.</param>
        public void RegisterSource(IHdbfBoxFarmSnapshotSource? source)
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

            if (!_entranceGates.ContainsKey(source.EntityId))
            {
                _entranceGates[source.EntityId] = new WarningHysteresis(1);
            }
        }

        /// <summary>Removes one farm (demolition/construction toggle); resets its warning gates.</summary>
        /// <param name="entityId">Runtime entity id; empty is ignored.</param>
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

            if (_entranceGates.TryGetValue(entityId, out var gate))
            {
                gate.Reset();
                _entranceGates.Remove(entityId);
            }
        }

        private BoxFarmSnapshot BuildSnapshot(IHdbfBoxFarmSnapshotSource source)
        {
            var snapshot = source.CreateSnapshot();
            if (!_evaluators.TryGetValue(source.EntityId, out var evaluator))
            {
                evaluator = new HdbWarningEvaluator();
                _evaluators[source.EntityId] = evaluator;
            }

            // Wall-clock gate, shared by the panel (≤4 Hz) and the world-icon pass (2 Hz).
            var evaluated = evaluator.Evaluate(source.CreateWarningInputs(snapshot), Time.unscaledTime);
            var indoor = SnapshotOf(source);
            if (!_entranceGates.TryGetValue(source.EntityId, out var gate))
            {
                gate = new WarningHysteresis(1);
                _entranceGates[source.EntityId] = gate;
            }

            var warnings = HdbfIndoorWarningMapper.MapToV2(evaluated, indoor, gate,
                HdbfWarningRules.HysteresisSeconds, Time.unscaledTime);
            return warnings.Length == 0 ? snapshot : snapshot with { Warnings = warnings };
        }

        private IndoorFarmSnapshot? SnapshotOf(IHdbfBoxFarmSnapshotSource source)
        {
            try
            {
                return source.CreateIndoorSnapshot();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeySnapshot,
                    "indoor snapshot unavailable for the warning pass", exception, source.EntityId.ToString());
                return null;
            }
        }
    }
}

