// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: World-space warning icon coordinator (section 19.11): exactly four v2 icon classes
//          (water shortage, maintenance overdue, power cut, blocked entrance), rendered through
//          the vanilla floating-status-icon pipeline with distance culling and blink, at most
//          once per second. Registration/deactivation helpers plus the private handle state
//          live in the .Evaluate/.Update partials.
// Requirements: REQ-V2-BAL-03, REQ-V2-WATER-03, REQ-V2-COMPAT-02, REQ-V2-SAFE-01/02
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.BaseComponentSystem;
using Timberborn.CameraSystem;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.StatusSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Owns the v2 world-space warning icons: only the four classes of section 19.11 keep an icon
    /// (water shortage, maintenance overdue, power cut, blocked entrance). The service reuses the
    /// **vanilla** floating-status-icon pipeline instead of drawing anything itself:
    ///
    /// * every vanilla building already carries <see cref="StatusSubject"/>, <see cref="StatusIconCycler"/>
    ///   and <c>BuildingStatusIconOffsetter</c> (decorated on <c>BuildingSpec</c> by
    ///   <c>Timberborn.BuildingStatuses.BuildingStatusesConfigurator</c>, IL evidence in the
    ///   BUILD-04 report);
    /// * this service registers one <see cref="StatusToggle"/> per v2 icon class on the farm's
    ///   subject and only ever keeps one active, so the vanilla cycler shows exactly one icon
    ///   (camera facing, sprite material, scaling and screen-space collision are all vanilla);
    /// * "hide" (distance/off-screen/blink) goes through the vanilla
    ///   <see cref="StatusVisibilityToggle"/>, so the status itself stays active for the vanilla
    ///   status aggregation while only the floating icon disappears.
    ///
    /// The icon class comes straight from the daily <see cref="IndoorWarningFlags"/> through the
    /// pure <see cref="IndoorWarningMap"/>; the v1 warning evaluator/aggregation chain was deleted
    /// with the world-plot chain (section 19.2).
    ///
    /// Performance (section 19.13): one pass at most once per second (the v2 icon budget), only
    /// farms inside the camera viewport and within <c>ui.worldIconMaxDistance</c> cells of the
    /// camera focus; at most <c>ui.worldIconFarmsPerTick</c> farms per pass (round-robin). No
    /// per-frame work in <c>Update</c>, no Find/GetComponent in the update path.
    ///
    /// Failure policy: an icon problem only disables icons for that farm; the panel and the farm
    /// simulation are untouched (REQ-V2-SAFE-01/02).
    /// </summary>
    public sealed partial class HdbfWorldIconService : IUpdatableSingleton, IUnloadableSingleton
    {
        /// <summary>Vanilla status sprite names (IL literals from vanilla status classes).</summary>
        private const string SpriteWater = "BuildingNeedsWater";

        private const string SpriteMaintenance = "UnspecifiedRecipe";
        private const string SpriteNoPower = "NoPower";
        private const string SpriteEntranceBlocked = "UnconnectedBuilding";

        /// <summary>Vanilla status icons fade by screen-space size, not by a mod-side scale.</summary>
        private const float FloatingIconDelayHours = 0f;

        /// <summary>v2 world icons refresh at most once per second (section 19.11/19.13).</summary>
        private const float MinIconIntervalSeconds = IndoorIconPolicy.DefaultIconIntervalSeconds;

        /// <summary>Localization key of the world-icon hover tooltip.</summary>
        private const string TooltipWorldIcon = "HDBF.Tooltip.WorldIcon";

        private readonly IBoxFarmQuery _query;
        private readonly IHdbfGameConfigProvider _configProvider;
        private readonly IHdbfLog _log;
        private readonly ILoc _loc;
        private readonly CameraService _cameraService;

        private readonly Dictionary<Guid, IconHandle> _handles = new Dictionary<Guid, IconHandle>();
        private readonly List<Guid> _order = new List<Guid>();

        private Camera? _camera;
        private double _nextIconPass;
        private int _cursor;

        /// <summary>Creates the coordinator from the session singletons.</summary>
        /// <param name="query">Farm read contract (snapshot provider).</param>
        /// <param name="configProvider">Resolved config (icon distance/interval/blink switches).</param>
        /// <param name="log">Throttled mod log.</param>
        /// <param name="loc">Game localization for the icon tooltip.</param>
        /// <param name="cameraService">Camera service (focus + viewport culling).</param>
        public HdbfWorldIconService(
            IBoxFarmQuery query,
            IHdbfGameConfigProvider configProvider,
            IHdbfLog log,
            ILoc loc,
            CameraService cameraService)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        }

        /// <summary>Number of farms with registered icon handles; diagnostics/tests only.</summary>
        public int RegisteredFarmCount => _handles.Count;

        /// <summary>
        /// Called by the farm controller when it becomes finished. Resolves the vanilla status
        /// components and registers one toggle per v2 icon class. Returns false (and logs) when the
        /// entity lacks the vanilla pipeline; the farm stays fully functional without icons.
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        /// <param name="entity">Finished farm component (status pipeline owner).</param>
        /// <returns>True when the farm is tracked for icons.</returns>
        public bool Register(Guid entityId, BaseComponent entity)
        {
            if (entityId == Guid.Empty || entity is null)
            {
                return false;
            }

            if (_handles.ContainsKey(entityId))
            {
                // Re-entering the finished state (construction-mode toggle) reuses the toggles that
                // are already registered on the StatusSubject; registering them again would leave
                // duplicate status entries in the vanilla status aggregator.
                _handles[entityId].Active = true;
                return true;
            }

            try
            {
                StatusSubject? subject = null;
                StatusIconCycler? cycler = null;
                try
                {
                    subject = entity.GetComponent<StatusSubject>();
                    cycler = entity.GetComponent<StatusIconCycler>();
                }
                catch (Exception exception)
                {
                    _log.Error(HdbfLogCategory.Warn,
                        HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                        "vanilla status components could not be resolved; this farm gets no head-top "
                        + "icons (panel unaffected)", exception, entityId.ToString());
                }

                if (subject is null || cycler is null)
                {
                    _log.Write(HdbfLogCategory.Warn,
                        HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                        "vanilla StatusSubject/StatusIconCycler missing; head-top warning icons "
                        + "disabled for this farm (panel unaffected), entityId=" + entityId);
                }

                var toggles = new StatusToggle?[IndoorWarningMap.MaxWorldIconKind + 1];
                if (subject is not null)
                {
                    RegisterToggle(entityId, subject, toggles, IndoorWarningIconKind.Water,
                        SpriteWater, isPriority: true);
                    RegisterToggle(entityId, subject, toggles, IndoorWarningIconKind.Maintenance,
                        SpriteMaintenance, isPriority: true);
                    RegisterToggle(entityId, subject, toggles, IndoorWarningIconKind.NoPower,
                        SpriteNoPower, isPriority: false);
                    RegisterToggle(entityId, subject, toggles, IndoorWarningIconKind.EntranceBlocked,
                        SpriteEntranceBlocked, isPriority: false);
                }

                var visibility = subject is not null && cycler is not null
                    ? TryGetVisibilityToggle(entityId, cycler)
                    : null;
                var handle = new IconHandle(entityId, entity, subject, cycler, visibility, toggles);
                _handles[entityId] = handle;
                _order.Add(entityId);
                return true;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                    "world warning icon registration failed; icons disabled for this farm",
                    exception, entityId.ToString());
                return false;
            }
        }

        /// <summary>
        /// Called when the farm leaves the finished state (construction-mode toggle) or is
        /// demolished. Deactivates every toggle so no stale status remains in the vanilla
        /// aggregation, but keeps the registered toggles for a later re-activation (a
        /// <see cref="StatusSubject"/> offers no "unregister status" API, so creating new toggles
        /// on every finished-state toggle would duplicate them).
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        public void Deactivate(Guid entityId)
        {
            if (entityId == Guid.Empty || !_handles.TryGetValue(entityId, out var handle))
            {
                return;
            }

            handle.Active = false;
            handle.CullingHidden = true;
            handle.BlinkHidden = false;
            TryDeactivateToggles(handle);
        }

        /// <summary>
        /// Drops all references to a farm. Called from <c>DeleteEntity</c>: the entity tree is going
        /// away, so keeping its components would leak them. Idempotent.
        /// </summary>
        /// <param name="entityId">Runtime entity id of the farm.</param>
        public void Forget(Guid entityId)
        {
            if (entityId == Guid.Empty || !_handles.TryGetValue(entityId, out var handle))
            {
                return;
            }

            Deactivate(entityId);
            _handles.Remove(entityId);
            _order.Remove(entityId);
            if (handle.Visibility is null)
            {
                return;
            }

            try
            {
                handle.Visibility.Show();
            }
            catch (Exception)
            {
                // The entity tree is being destroyed; nothing to repair.
            }
        }

        private StatusVisibilityToggle? TryGetVisibilityToggle(Guid entityId, StatusIconCycler cycler)
        {
            try
            {
                return cycler.GetStatusVisibilityToggle();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconsUnavailable + ":" + entityId,
                    "vanilla status visibility toggle could not be created; icons cannot be "
                    + "hidden by distance/blink for this farm", exception, entityId.ToString());
                return null;
            }
        }

        private void TryDeactivateToggles(IconHandle handle)
        {
            try
            {
                for (var kind = 1; kind < handle.Toggles.Length; kind++)
                {
                    var toggle = handle.Toggles[kind];
                    if (toggle is not null && toggle.IsActive)
                    {
                        toggle.Deactivate();
                    }
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconsUnavailable + ":" + handle.EntityId,
                    "world warning icon deactivation failed", exception, handle.EntityId.ToString());
            }
        }
    }
}
