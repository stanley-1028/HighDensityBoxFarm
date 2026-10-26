// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: The single indoor-farm component of the four tiers: lifecycle, DI, schema-v2
//          save/load through IndoorEntitySaveBridge and the once-per-day trigger. All slot
//          formulas live in HDBF.Core.Indoor; this file only wires game objects and services.
// Requirements: REQ-V2-CORE-01..05, REQ-V2-SAVE-01/04/06, REQ-V2-SAFE-01/04, REQ-V2-MAINT-03
// ============================================================================
using System;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using HDBF.Persistence.Indoor;
using HDBF.Gameplay.Specs;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.MechanicalSystem;
using Timberborn.SimpleOutputBuildings;
using Timberborn.TimeSystem;
using Timberborn.WorkSystem;
using Timberborn.WorldPersistence;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Runtime component of every indoor box farm (section 19.1). One component serves all four
    /// tiers and both factions: the tier comes from <see cref="HdbfBoxFarmSpec"/> plus the
    /// config-resolved <see cref="TierProfile"/>, and the faction from the blueprint/template name.
    ///
    /// Responsibilities:
    /// * lifecycle: finished-state activation, post-load activation, idempotent deactivation/delete;
    /// * persistence: <see cref="IPersistentEntity.Save"/>/<see cref="Load"/> through
    ///   <see cref="IndoorEntitySaveBridge"/> with failure-safe fallbacks;
    /// * the daily trigger: exactly one <see cref="ITimeTrigger"/> callback per game day; there is
    ///   no frame hook and no frame simulation in this component;
    /// * delegation: the actual fixed-order simulation lives in <see cref="IndoorDayTick"/> and the
    ///   command/snapshot partials.
    /// </summary>
    public partial class HdbfBoxFarmController : BaseComponent, IAwakableComponent, IFinishedStateListener,
        IPersistentEntity, IPostLoadableEntity, IDeletableEntity, IHdbfIndoorFarmSource
    {
        private const string LogKeyEntity = "indoor.entity";
        private const float DayTriggerDelayDays = 1f;

        private readonly ITimeTriggerFactory _timeTriggerFactory;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly IBlockService _blockService;
        private readonly IIndoorAdapterHost _adapterHost;
        private readonly ICropCatalog _cropCatalog;
        private readonly HdbfFarmQueryService _farms;
        private readonly IHdbfGameConfigProvider _configProvider;
        private readonly IHdbfLog _log;
        private readonly HdbfWorldIconService _worldIcons;
        private readonly DistrictCenterRegistry _districtCenters;
        private readonly HdbfManualWaterSource _manualWater;

        private HdbfBoxFarmSpec _spec = null!;
        private TierProfile _profile = TierCatalog.Get(HdbfTier.Basic);
        private BlockObject? _blockObject;
        private EntityComponent? _entityComponent;
        private Workplace? _workplace;
        private MechanicalBuilding? _mechanicalBuilding;
        private MechanicalNode? _mechanicalNode;
        private SimpleOutputInventory? _outputInventory;
        private IndoorInventorySink? _outputSink;
        private IndoorMaintenanceMaterialSource? _materials;
        private IndoorFarmRecord _record = null!;
        private ITimeTrigger? _dayTrigger;
        private HdbfFaction _faction = HdbfFaction.Folktails;

        private Guid _entityId;
        private long _saveEntityId;
        private bool _awake;
        private bool _activated;
        private bool _saveLoaded;
        private bool _saveDirty;
        private bool _maintenanceInitialized;
        private bool _entranceBlocked;
        private bool _lastStalled;
        private float _lastCoverage;
        private float _lastModeChangeDay = float.NaN;
        private IndoorWarningFlags _lastWarnings;
        private WaterSupplyKind _waterSupply = WaterSupplyKind.None;
        private bool _manualSourceAvailable;
        private IWaterSource? _pendingManualSource;
        private bool _manualRefillRequested;
        private float _actionsUsedToday;
        private int _actionsDayIndex = -1;
        private int _lastBacklogActions;
        private IndoorMode _modeTransitionFrom = IndoorMode.Normal;
        private string _rotationSequence = string.Empty;
        private string? _lastCommandFailureKey;

        /// <summary>Creates one controller per building entity; every dependency is a DI singleton.</summary>
        public HdbfBoxFarmController(ITimeTriggerFactory timeTriggerFactory, IDayNightCycle dayNightCycle,
            IBlockService blockService, IIndoorAdapterHost adapterHost, ICropCatalog cropCatalog,
            HdbfFarmQueryService farms, IHdbfGameConfigProvider configProvider, IHdbfLog log,
            HdbfWorldIconService worldIcons, DistrictCenterRegistry districtCenters,
            HdbfManualWaterSource manualWater)
        {
            _timeTriggerFactory = timeTriggerFactory ?? throw new ArgumentNullException(nameof(timeTriggerFactory));
            _dayNightCycle = dayNightCycle ?? throw new ArgumentNullException(nameof(dayNightCycle));
            _blockService = blockService ?? throw new ArgumentNullException(nameof(blockService));
            _adapterHost = adapterHost ?? throw new ArgumentNullException(nameof(adapterHost));
            _cropCatalog = cropCatalog ?? throw new ArgumentNullException(nameof(cropCatalog));
            _farms = farms ?? throw new ArgumentNullException(nameof(farms));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _worldIcons = worldIcons ?? throw new ArgumentNullException(nameof(worldIcons));
            _districtCenters = districtCenters ?? throw new ArgumentNullException(nameof(districtCenters));
            _manualWater = manualWater ?? throw new ArgumentNullException(nameof(manualWater));
        }

        /// <inheritdoc />
        public Guid EntityId => _entityId;

        /// <inheritdoc />
        public long SaveEntityId => _saveEntityId;

        /// <summary>True while gameplay state changed since the last successful entity save.</summary>
        public bool SaveDirty => _saveDirty;

        /// <inheritdoc />
        public void Awake()
        {
            try
            {
                if (!HasComponent<HdbfBoxFarmSpec>())
                {
                    _log.Write(HdbfLogCategory.Error, LogKeyEntity,
                        "box farm entity has no HdbfBoxFarmSpec; controller disabled");
                    return;
                }

                _spec = GetComponent<HdbfBoxFarmSpec>();
                _blockObject = HasComponent<BlockObject>() ? GetComponent<BlockObject>() : null;
                _entityComponent = HasComponent<EntityComponent>() ? GetComponent<EntityComponent>() : null;
                _workplace = HasComponent<Workplace>() ? GetComponent<Workplace>() : null;
                _mechanicalBuilding = HasComponent<MechanicalBuilding>()
                    ? GetComponent<MechanicalBuilding>()
                    : null;
                // The mechanical node is what tells us whether the top tier is actually wired to a
                // grid at all (PowerMode Grid vs off-grid fallback, V2-GAMEPLAY-02 power refresh).
                _mechanicalNode = _mechanicalBuilding is not null && HasComponent<MechanicalNode>()
                    ? GetComponent<MechanicalNode>()
                    : null;
                _outputInventory = HasComponent<SimpleOutputInventory>()
                    ? GetComponent<SimpleOutputInventory>()
                    : null;
                if (_outputInventory is not null)
                {
                    _outputSink = new IndoorInventorySink(_outputInventory);
                }

                ResolveTierProfile();
                _faction = GameCropCatalog.ResolveFaction(BlueprintId());
                _record = IndoorFarmRecord.Create(_profile.Tier, _faction);
                _materials = new IndoorMaintenanceMaterialSource(_districtCenters, _log);

                // Nothing runs before the building is finished: no trigger, no tick, no save.
                DisableComponent();
                _awake = true;
            }
            catch (Exception exception)
            {
                _awake = false;
                DisableComponent();
                _log.Error(HdbfLogCategory.Error, LogKeyEntity,
                    "box farm controller failed to initialize; component disabled", exception);
            }
        }

        /// <inheritdoc />
        public void OnEnterFinishedState()
        {
            Activate();
        }

        /// <inheritdoc />
        public void OnExitFinishedState()
        {
            Deactivate();
        }

        /// <inheritdoc />
        public void PostLoadEntity()
        {
            // Loaded finished buildings do not receive OnEnterFinishedState again.
            Activate();
        }

        /// <inheritdoc />
        public void DeleteEntity()
        {
            try
            {
                Deactivate();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKeyEntity, "box farm delete failed", exception,
                    _entityId.ToString());
            }

            try
            {
                _worldIcons.Forget(_entityId);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, LogKeyEntity,
                    "world icon cleanup failed during deletion", exception, _entityId.ToString());
            }
        }

        /// <inheritdoc />
        public void Save(IEntitySaver entitySaver)
        {
            if (!_awake && _record is null)
            {
                return;
            }

            try
            {
                SyncEntityIdentity();
                if (_record is null)
                {
                    return;
                }

                _record.EntityId = _saveEntityId.ToString(CultureInfo.InvariantCulture);
                var saved = IndoorEntitySaveBridge.Save(_record, _profile, _cropCatalog,
                    _record.EntityId, entitySaver, _log);
                _saveDirty = !saved;
            }
            catch (Exception exception)
            {
                // One building must never abort the world save (REQ-V2-SAVE-01/SAFE-01).
                _saveDirty = true;
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.SaveWrite,
                    "indoor record save failed; previous state kept", exception, _entityId.ToString());
            }
        }

        /// <inheritdoc />
        public void Load(IEntityLoader entityLoader)
        {
            if (!_awake)
            {
                return;
            }

            try
            {
                SyncEntityIdentity();
                var loaded = IndoorEntitySaveBridge.Load(entityLoader, _profile, _faction, _cropCatalog,
                    _log, out var degraded);
                if (loaded is not null)
                {
                    _record = loaded;
                }

                if (_record is null)
                {
                    _record = IndoorFarmRecord.Create(_profile.Tier, _faction);
                }

                _record.EntityId = _saveEntityId.ToString(CultureInfo.InvariantCulture);
                _saveLoaded = loaded is not null;
                // A refill request is transient gameplay convenience state; never restore it from a
                // save, otherwise a loaded farm would spend actions the player never asked for.
                _manualRefillRequested = false;
                _pendingManualSource = null;
                ResolveMaintenanceAfterLoad();
                if (degraded)
                {
                    _log.Write(HdbfLogCategory.Warn, IndoorLogKeys.Migrated,
                        "indoor record loaded in a repaired/degraded state, entityId=" + _entityId);
                }
            }
            catch (Exception exception)
            {
                // Keep the safe default created in Awake; the building stays playable (REQ-V2-SAFE-06).
                _log.Error(HdbfLogCategory.Error, IndoorLogKeys.MigratedFailed,
                    "indoor record load failed; safe default state kept", exception, _entityId.ToString());
            }
        }
    }
}
