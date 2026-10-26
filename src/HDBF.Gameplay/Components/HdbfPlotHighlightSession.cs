using System;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session-wide managed-plot overlay state (REQ-UI-05, chapter 14.3). Kept as a game-context
    /// singleton with no static mutability so the panel, the command service and the world-space
    /// overlay all observe the same instance:
    ///
    /// * at most one farm is highlighted at a time (opening another overlay replaces the first);
    /// * the controller reads <see cref="IsActiveFor"/> to fill
    ///   <c>BoxFarmSnapshot.IsPlotHighlightActive</c>, so the panel toggle never guesses;
    /// * <see cref="FailureKey"/> is the last rejected world action (click/box-select), surfaced
    ///   through <c>BoxFarmSnapshot.PlotActionFailureKey</c> so the panel can explain "注册格不足"
    ///   without the UI knowing any game state.
    ///
    /// All members are main-thread only, matching the game's simulation model.
    /// </summary>
    public sealed class HdbfPlotHighlightSession
    {
        private Guid _activeEntityId;
        private string? _failureKey;

        /// <summary>Runtime entity id of the farm whose overlay is open; <see cref="Guid.Empty"/> when closed.</summary>
        public Guid ActiveEntityId => _activeEntityId;

        /// <summary>True while any farm's overlay is open.</summary>
        public bool HasActive => _activeEntityId != Guid.Empty;

        /// <summary>Last world-action rejection key (see <c>HdbfCommandFailures</c>), or null.</summary>
        public string? FailureKey => _failureKey;

        /// <summary>True when the given farm owns the open overlay.</summary>
        public bool IsActiveFor(Guid entityId) => entityId != Guid.Empty && entityId == _activeEntityId;

        /// <summary>Starts (or moves) the overlay session to one farm and clears the last failure.</summary>
        public void Activate(Guid entityId)
        {
            _activeEntityId = entityId;
            _failureKey = null;
        }

        /// <summary>Closes the overlay session; safe to call when already closed.</summary>
        public void Clear()
        {
            _activeEntityId = Guid.Empty;
            _failureKey = null;
        }

        /// <summary>Records a rejected world action so the panel can show the reason (chapter 14.3).</summary>
        public void ReportFailure(string? failureKey)
        {
            _failureKey = string.IsNullOrEmpty(failureKey) ? null : failureKey;
        }

        /// <summary>Clears the last world-action rejection after a successful action.</summary>
        public void ReportSuccess()
        {
            _failureKey = null;
        }
    }
}
