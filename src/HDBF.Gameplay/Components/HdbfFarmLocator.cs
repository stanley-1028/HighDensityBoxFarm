using System;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using Timberborn.BaseComponentSystem;
using Timberborn.SelectionSystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Resolves a <see cref="IBoxFarmLocator"/> request with the vanilla selection service
    /// (chapter 14.2: click to expand the locatable building list).
    ///
    /// Verified vanilla API (1.1.2.8): <c>Timberborn.SelectionSystem.EntitySelectionService</c>
    /// exposes <c>Select(BaseComponent)</c> and <c>SelectAndFocusOn(BaseComponent)</c>; the latter
    /// is the same path the alert panel uses to jump to a building, so the mod does not move the
    /// camera itself. All game logging is throttled through the HDBF log; failures return false
    /// instead of throwing into the panel.
    /// </summary>
    public sealed class HdbfFarmLocator : IBoxFarmLocator
    {
        private const string LogKey = "ui.locate";

        private readonly HdbfFarmQueryService _query;
        private readonly EntitySelectionService _selection;
        private readonly IHdbfLog _log;

        public HdbfFarmLocator(HdbfFarmQueryService query, EntitySelectionService selection,
            IHdbfLog log)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public bool TryLocate(Guid entityId)
        {
            if (entityId == Guid.Empty)
            {
                return false;
            }

            try
            {
                if (!_query.TryGetSource(entityId, out var source) || source is not BaseComponent entity)
                {
                    return false;
                }

                _selection.SelectAndFocusOn(entity);
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKey,
                    "locating a box farm from the warning aggregate row failed; the panel stays open",
                    exception, entityId.ToString());
                return false;
            }
        }
    }
}
