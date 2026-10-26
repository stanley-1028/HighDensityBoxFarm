// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01 / V2-UI-01
// Purpose: Session singleton behind IBoxFarmQuery. It keeps the entity -> live indoor farm
//          table and builds the Core IndoorFarmSnapshot on demand; it owns no world-plot
//          registry and no v1 warning evaluator (section 19.2).
// Requirements: REQ-V2-UI-01/02, REQ-V2-CORE-03, REQ-V2-SAFE-01/07
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session singleton that answers UI/diagnostic queries for live indoor farms. It keeps only
    /// the entity -> snapshot-source table; snapshots are rebuilt on demand and never stored in a
    /// save. All calls are main-thread only, matching the game model.
    ///
    /// Contract: every public entry point isolates a broken farm so UI and the other farms keep
    /// rendering (REQ-V2-SAFE-01/07).
    /// </summary>
    public sealed class HdbfFarmQueryService : IBoxFarmQuery
    {
        private const string LogKeySnapshot = "ui.snapshot";

        private readonly IHdbfLog _log;
        private readonly Dictionary<Guid, IHdbfIndoorFarmSource> _sources =
            new Dictionary<Guid, IHdbfIndoorFarmSource>();

        /// <summary>Creates the query singleton with the shared throttled log.</summary>
        /// <param name="log">Throttled mod log sink; required.</param>
        public HdbfFarmQueryService(IHdbfLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Number of live indoor farms; diagnostics and reduced-mode policy.</summary>
        public int RegisteredFarmCount => _sources.Count;

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

        /// <summary>Looks up the live snapshot source of one farm (commands/world icons).</summary>
        /// <param name="entityId">Runtime entity id; empty is rejected.</param>
        /// <param name="source">Registered source when true.</param>
        /// <returns>True when the entity is a live indoor farm.</returns>
        public bool TryGetSource(Guid entityId, out IHdbfIndoorFarmSource source)
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
        public void RegisterSource(IHdbfIndoorFarmSource? source)
        {
            if (source is null || source.EntityId == Guid.Empty)
            {
                return;
            }

            _sources[source.EntityId] = source;
        }

        /// <summary>Removes one farm (demolition/construction toggle); idempotent.</summary>
        /// <param name="entityId">Runtime entity id; empty is ignored.</param>
        public void UnregisterSource(Guid entityId)
        {
            if (entityId != Guid.Empty)
            {
                _sources.Remove(entityId);
            }
        }
    }
}
