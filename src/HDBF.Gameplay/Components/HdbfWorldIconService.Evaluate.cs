// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-02
// Purpose: Per-farm icon evaluation, culling/blink visibility and camera resolution of the
//          world-warning icon coordinator, plus its private handle state. The icon class is
//          derived directly from the daily IndoorWarningFlags through the pure Core
//          IndoorWarningMap, so only four v2 visuals can ever appear (section 19.11).
// Requirements: REQ-V2-BAL-03, REQ-V2-UI-02, REQ-V2-SAFE-01, REQ-V2-READ-02
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
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
    public sealed partial class HdbfWorldIconService
    {
        private void Evaluate(IconHandle handle, double now, HDBF.Core.Config.UiSettings ui,
            bool iconsEnabled)
        {
            // L-14: the pass cost is dominated by the snapshot build, so a farm whose icons are off
            // (global switch or no vanilla status pipeline) never pays for one: settle the toggles
            // and leave instead of querying the farm.
            if (!iconsEnabled || handle.Subject is null)
            {
                DeactivateAll(handle);
                return;
            }

            var snapshot = _query.GetIndoorSnapshot(handle.EntityId);
            // L-11: the live-derived flag set wins over the once-per-day cache, so the icon follows
            // the panel within one pass of a refilled tank / hauled output instead of a whole day.
            var flags = IndoorWarningMap.EffectiveFlags(snapshot);
            var target = IndoorWarningMap.IconKindOf(flags);
            if (!handle.Applied || handle.AppliedKind != target)
            {
                // L-14: the toggles are only touched when the class actually changed; an unchanged
                // farm costs no status calls at all (the vanilla cycler keeps rendering the icon).
                ApplyTarget(handle, target);
                handle.Applied = true;
                handle.AppliedKind = target;
            }

            handle.TargetKind = target;
            var blink = IndoorIconPolicy.ShouldBlink(IndoorWarningMap.IsCritical(flags), ui.WorldIconBlink)
                        && target != IndoorWarningIconKind.None;
            handle.Blinking = blink;
            handle.BlinkHidden = blink && !IndoorIconPolicy.IsBlinkOn(now, IndoorIconPolicy.DefaultBlinkHz);
            ApplyVisibility(handle);
        }

        /// <summary>
        /// Deactivates every class of one farm and resets its transient hide state (icons switched
        /// off, or the entity has no vanilla status pipeline). No snapshot is needed for this.
        /// </summary>
        private void DeactivateAll(IconHandle handle)
        {
            handle.TargetKind = IndoorWarningIconKind.None;
            handle.Applied = false;
            handle.AppliedKind = IndoorWarningIconKind.None;
            for (var kind = 1; kind < handle.Toggles.Length; kind++)
            {
                var toggle = handle.Toggles[kind];
                if (toggle is not null && toggle.IsActive)
                {
                    toggle.Deactivate();
                }
            }

            handle.Blinking = false;
            handle.BlinkHidden = false;
            handle.VisibilityHidden = false;
        }

        /// <summary>
        /// Applies one icon class to the fixed toggle array: exactly the slot of the target is
        /// active, every other class is off (the vanilla cycler shows one camera-facing icon).
        /// </summary>
        private static void ApplyTarget(IconHandle handle, IndoorWarningIconKind target)
        {
            for (var kind = 1; kind < handle.Toggles.Length; kind++)
            {
                var toggle = handle.Toggles[kind];
                if (toggle is null)
                {
                    continue;
                }

                var wanted = kind == IndoorWarningMap.SlotOf(target);
                if (toggle.IsActive == wanted)
                {
                    continue;
                }

                if (wanted)
                {
                    toggle.Activate();
                }
                else
                {
                    toggle.Deactivate();
                }
            }
        }

        private void SetCullingHidden(IconHandle handle, bool hidden)
        {
            if (handle.CullingHidden == hidden)
            {
                return;
            }

            handle.CullingHidden = hidden;
            ApplyVisibility(handle);
        }

        private void ApplyVisibility(IconHandle handle)
        {
            if (handle.Visibility is null)
            {
                return;
            }

            var hidden = handle.CullingHidden || handle.BlinkHidden;
            if (handle.VisibilityHidden == hidden)
            {
                return;
            }

            try
            {
                if (hidden)
                {
                    handle.Visibility.Hide();
                }
                else
                {
                    handle.Visibility.Show();
                }

                handle.VisibilityHidden = hidden;
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn,
                    HdbfLogKeys.WorldIconsUnavailable + ":" + handle.EntityId,
                    "world warning icon visibility toggle failed", exception,
                    handle.EntityId.ToString());
            }
        }

        /// <summary>
        /// Resolves the game's Camera once. <c>CameraService.Transform</c> exists only after the
        /// camera singleton's Load(); the first pass retries, then the cached reference is used
        /// (no Find/GetComponent in the update path).
        /// </summary>
        private void EnsureCamera()
        {
            if (_camera is not null)
            {
                return;
            }

            try
            {
                var transform = _cameraService.Transform;
                if (transform is null)
                {
                    return;
                }

                _camera = transform.GetComponent<Camera>();
            }
            catch (Exception)
            {
                _camera = null;
            }
        }

        private sealed class IconHandle
        {
            public IconHandle(Guid entityId, BaseComponent entity, StatusSubject? subject,
                StatusIconCycler? cycler, StatusVisibilityToggle? visibility, StatusToggle?[] toggles)
            {
                EntityId = entityId;
                Entity = entity;
                Subject = subject;
                Cycler = cycler;
                Visibility = visibility;
                Toggles = toggles;
            }

            public Guid EntityId { get; }

            public BaseComponent Entity { get; }

            /// <summary>Null when the entity lacks the vanilla status pipeline (icons only).</summary>
            public StatusSubject? Subject { get; }

            /// <summary>Null when the entity lacks the vanilla status pipeline (icons only).</summary>
            public StatusIconCycler? Cycler { get; }

            /// <summary>Null when the entity lacks the vanilla status pipeline (icons only).</summary>
            public StatusVisibilityToggle? Visibility { get; }

            public StatusToggle?[] Toggles { get; }

            /// <summary>False between OnExitFinishedState and the next OnEnterFinishedState.</summary>
            public bool Active { get; set; } = true;

            /// <summary>Currently targeted v2 icon class (one of the four, or None).</summary>
            public IndoorWarningIconKind TargetKind { get; set; } = IndoorWarningIconKind.None;

            /// <summary>True once the toggles reflect <see cref="AppliedKind"/> (L-14 skip flag).</summary>
            public bool Applied { get; set; }

            /// <summary>Icon class last written to the toggles; None after a forced deactivation.</summary>
            public IndoorWarningIconKind AppliedKind { get; set; } = IndoorWarningIconKind.None;

            public bool CullingHidden { get; set; }

            public bool BlinkHidden { get; set; }

            public bool Blinking { get; set; }

            public bool VisibilityHidden { get; set; }
        }
    }
}
