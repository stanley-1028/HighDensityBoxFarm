// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Entity panel fragment of the v2 indoor box farm: lifecycle, the 4 Hz refresh gate,
//          the value fingerprint (dirty check) and the read-model refresh. Rendering lives in
//          .Render.cs, element construction in .Build.cs and command callbacks in .Commands.cs.
// Requirements: REQ-V2-UI-01/02/03/04, REQ-V2-READ-04, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.UI.Localization;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Entity panel fragment for the four BoxFarm tiers (chapter 19.11). The whole fragment is
    /// authored in code with UIElements (no UXML/USS asset) and is added through the vanilla
    /// <see cref="EntityPanelModule"/> multi-binding.
    ///
    /// Behaviour:
    /// * read-only rows render strictly from the Core <see cref="IndoorFarmSnapshot"/>; UI never
    ///   reads a game component value and never keeps simulation state;
    /// * actions (refill, maintenance, mode, crop strategy) go through the frozen Core command
    ///   contract or the v2 indoor command bridge; every rejection renders its specific reason
    ///   (REQ-V2-UI-02);
    /// * the snapshot is re-queried at most four times per second (REQ-V2-UI-01) and the panel is
    ///   re-rendered only when the displayed-value fingerprint changed (dirty marker: no churn
    ///   when nothing changed);
    /// * it hides itself for every entity that is not a live farm and never throws into the
    ///   entity panel: broken queries/rendering log a throttled error instead (REQ-V2-SAFE-01).
    /// </summary>
    public sealed partial class HdbfFarmPanelFragment : IEntityPanelFragment
    {
        /// <summary>Quarter-second refresh cap (4 Hz), matching the chapter 19.13 UI budget.</summary>
        private const float RefreshIntervalSeconds = 0.25f;

        /// <summary>Maximum warning rows shown at once (four v2 categories + spare).</summary>
        private const int MaxStatusRows = 5;

        /// <summary>Maximum concrete problem rows shown at once.</summary>
        private const int MaxProblemRows = 6;

        private const string LogKeyCommandRejected = "ui.command-rejected";
        private const string LogKeyCommandFailed = "ui.command-exception";
        private const string LogKeyRenderFailed = "ui.render";

        private readonly IBoxFarmCommand _command;
        private readonly HdbfIndoorCommandBridge _indoor;
        private readonly ILoc _loc;
        private readonly IHdbfLog _log;

        private BaseComponent? _entity;
        private Guid _entityId;
        private long _lastFingerprint;
        private bool _hasSnapshot;
        private float _nextRefreshTime;
        private string _actionMessage = string.Empty;
        private string _cropSignature = string.Empty;
        private bool _suppressCropChange;

        /// <summary>
        /// Creates the fragment. The v2 read/command members are resolved once through
        /// <see cref="HdbfIndoorCommandBridge"/> because UI references HDBF.Core only.
        /// </summary>
        /// <param name="query">Core read contract instance (gameplay-owned singleton).</param>
        /// <param name="command">Core write contract for mode/maintenance.</param>
        /// <param name="loc">Game localization service.</param>
        /// <param name="log">Throttled mod log.</param>
        public HdbfFarmPanelFragment(IBoxFarmQuery query, IBoxFarmCommand command, ILoc loc, IHdbfLog log)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _indoor = new HdbfIndoorCommandBridge(query, command);
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>True once the fragment rendered a farm snapshot (diagnostics/tests).</summary>
        public bool HasData => _hasSnapshot;

        /// <inheritdoc />
        public VisualElement InitializeFragment()
        {
            return BuildRoot();
        }

        /// <inheritdoc />
        public void ShowFragment(BaseComponent entity)
        {
            _entity = entity;
            _entityId = ResolveEntityId(entity);
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _nextRefreshTime = 0f;
            _actionMessage = string.Empty;
            _cropSignature = string.Empty;
            Refresh(force: true);
        }

        /// <inheritdoc />
        public void UpdateFragment()
        {
            if (_root is null)
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = now + RefreshIntervalSeconds;
            Refresh(force: false);
        }

        /// <inheritdoc />
        public void ClearFragment()
        {
            _entity = null;
            _entityId = Guid.Empty;
            _hasSnapshot = false;
            _lastFingerprint = 0L;
            _actionMessage = string.Empty;
            _cropSignature = string.Empty;
            if (_root is not null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------ refresh / read model

        private void Refresh(bool force)
        {
            if (_root is null)
            {
                return;
            }

            try
            {
                if (_entity is null || _entityId == Guid.Empty)
                {
                    Hide();
                    return;
                }

                // REQ-V2-UI-01/-04: the fragment reads only the Core DTO exposed by the registered
                // gameplay query service; it never reaches into a controller or a game object.
                var snapshot = _indoor.GetIndoorSnapshot(_entityId);
                if (snapshot is null)
                {
                    Hide();
                    return;
                }

                var fingerprint = ComputeFingerprint(snapshot);
                if (!force && _hasSnapshot && fingerprint == _lastFingerprint)
                {
                    return;
                }

                _lastFingerprint = fingerprint;
                _hasSnapshot = true;
                Render(HdbfIndoorPanelFactory.Create(snapshot, _loc.T));
            }
            catch (Exception exception)
            {
                Hide();
                _log.Error(HdbfLogCategory.Error, LogKeyRenderFailed,
                    "indoor box farm panel failed to render; hiding the fragment", exception,
                    _entityId.ToString());
            }
        }

        private void Hide()
        {
            _hasSnapshot = false;
            if (_root is not null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private static Guid ResolveEntityId(BaseComponent entity)
        {
            try
            {
                var components = entity.AllComponents;
                for (var index = 0; index < components.Count; index++)
                {
                    if (components[index] is EntityComponent entityComponent)
                    {
                        return entityComponent.EntityId;
                    }
                }
            }
            catch (Exception)
            {
                // A half-destroyed entity is simply "not a farm" for the panel.
            }

            return Guid.Empty;
        }

        /// <summary>
        /// Cheap fingerprint over every displayed snapshot value. It is the dirty check that keeps
        /// the panel from re-formatting identical data: a collision only costs one skipped refresh,
        /// an unchanged fingerprint never touches a label (REQ-V2-UI-01).
        /// </summary>
        private static long ComputeFingerprint(IndoorFarmSnapshot snapshot)
        {
            unchecked
            {
                var hash = 17L;
                hash = hash * 31 + (int)snapshot.Tier;
                hash = hash * 31 + (int)snapshot.Faction;
                hash = hash * 31 + snapshot.Workers;
                hash = hash * 31 + snapshot.MaxWorkers;
                hash = hash * 31 + (int)snapshot.Mode;
                hash = hash * 31 + (snapshot.ModeTransitioning ? 1 : 0);
                hash = hash * 31 + snapshot.Capacity;
                hash = hash * 31 + snapshot.ConfiguredSlots;
                hash = hash * 31 + snapshot.EffectiveSlots;
                hash = hash * 31 + snapshot.EmptySlots;
                hash = hash * 31 + snapshot.GrowingSlots;
                hash = hash * 31 + snapshot.MatureSlots;
                hash = hash * 31 + snapshot.BacklogActions;
                hash = hash * 31 + Quantize(snapshot.Tank);
                hash = hash * 31 + Quantize(snapshot.TankCapacity);
                hash = hash * 31 + Quantize(snapshot.TankDaysRemaining);
                hash = hash * 31 + Quantize(snapshot.Coverage);
                hash = hash * 31 + Quantize(snapshot.MinCoverage);
                hash = hash * 31 + (int)snapshot.WaterSupply;
                hash = hash * 31 + Quantize(snapshot.MaintenanceDueDay);
                hash = hash * 31 + snapshot.MaintenanceCyclesMissed;
                hash = hash * 31 + (snapshot.MaintenanceMaterialMissing ? 1 : 0);
                hash = hash * 31 + (int)snapshot.Power;
                hash = hash * 31 + (snapshot.PowerConnected ? 1 : 0);
                hash = hash * 31 + (int)snapshot.CropPolicy;
                hash = hash * 31 + (snapshot.FixedCropId is null ? 0 : snapshot.FixedCropId.GetHashCode());
                hash = hash * 31 + (snapshot.RotationSequence is null ? 0 : snapshot.RotationSequence.GetHashCode());
                hash = hash * 31 + snapshot.OutputOccupied;
                hash = hash * 31 + (snapshot.OutputPortConnected ? 1 : 0);
                hash = hash * 31 + (snapshot.PipePort ? 1 : 0);
                hash = hash * 31 + (snapshot.LogisticsPort ? 1 : 0);
                hash = hash * 31 + (snapshot.PowerPort ? 1 : 0);
                hash = hash * 31 + (int)snapshot.Warnings;
                hash = hash * 31 + (snapshot.OutputInventoryPortState is null
                    ? 0
                    : snapshot.OutputInventoryPortState.GetHashCode());
                return hash;
            }
        }

        private static long Quantize(float value)
        {
            if (float.IsNaN(value))
            {
                return long.MinValue;
            }

            if (float.IsPositiveInfinity(value))
            {
                return long.MaxValue;
            }

            return (long)(value * 1000f);
        }
    }
}
