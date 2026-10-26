using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Logging;
using HDBF.Core.Plots;
using HDBF.Core.Save;
using HDBF.Gameplay.Specs;
using HDBF.Persistence;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.EntitySystem;
using Timberborn.MechanicalSystem;
using Timberborn.Navigation;
using Timberborn.Planting;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SoilMoistureSystem;
using Timberborn.TemplateSystem;
using Timberborn.TerrainSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorkSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// The single runtime component of all four box-farm tiers (REQ-ARCH-07): every difference
    /// between tiers comes from <see cref="HdbfBoxFarmSpec"/> plus the config-resolved
    /// <see cref="TierProfile"/>; there is no per-tier code path anywhere in this class.
    ///
    /// Responsibilities (chapter 11):
    /// * bind the spec/profile on <c>Awake</c> and seed the persisted record
    ///   (<see cref="HdbfSaveCodec.CreateDefault"/>), with the internal tank full on first placement;
    /// * maintain the managed-plot registry with an event-driven, budgeted, incremental scan
    ///   (chapter 11.3, REQ-IMPL-09), honouring first-come-first-served plot ownership
    ///   (REQ-BLD-04) and player exclusions (REQ-IMPL-10);
    /// * run exactly one simulation step per game day in the fixed order of REQ-IMPL-22:
    ///   tank draw -> coverage snapshot -> fatigue/fallow -> maintenance expiry -> dirty flag;
    /// * persist through <see cref="HdbfEntitySaveBridge"/> (chapter 12) and expose a read-only
    ///   snapshot for UI (chapter 9.2);
    /// * never let an exception escape into the game loop (REQ-SAFE-02).
    ///
    /// Performance shape: <c>Tick</c> is the component's frame hook, but it does nothing unless a
    /// budgeted scan is active (at most <c>perf.scanBudgetPerFrame</c> cells, default 128); the
    /// simulation itself runs from a one-day <see cref="ITimeTrigger"/>, not from <c>Tick</c>
    /// (REQ-SAFE-17, table 10-5).
    /// </summary>
    public partial class HdbfBoxFarmController : TickableComponent, IAwakableComponent, IFinishedStateListener,
        IPersistentEntity, IPostLoadableEntity, IDeletableEntity, IHdbfBoxFarmSnapshotSource,
        IHdbfPlotScanHost
    {
        private const string LogKeyEntity = "gameplay.entity";
        private const string LogKeySave = "gameplay.save";
        private const string LogKeyTick = "gameplay.tick";
        private const string LogKeyScan = "gameplay.scan";
        private const string LogKeyMode = "gameplay.mode";
        private const string LogKeyPort = "gameplay.port";

        private const float DayTriggerDelayDays = 1f;

        /// <summary>
        /// Cap on the expensive path-query fallback inside one scan. The cheap reachable-area test
        /// runs for every candidate; only when it says "different area" do we ask the building's
        /// Accessible component for a real path, and only for this many cells, so a pathological
        /// map cannot blow the scan budget (table 10-5).
        /// </summary>
        private const int MaxReachabilityFallbacksPerScan = 32;

        private readonly ITimeTriggerFactory _timeTriggerFactory;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly ISoilMoistureService _soilMoistureService;
        private readonly IBlockService _blockService;
        private readonly ITerrainService _terrainService;
        private readonly PlantingService _plantingService;
        private readonly PlantingAreaValidator _plantingAreaValidator;
        private readonly INavigationService _navigationService;
        private readonly TemplateService _templateService;
        private readonly IHdbfFarmRegistry _registry;
        private readonly IHdbfGameConfigProvider _configProvider;
        private readonly IHdbfLog _log;
        private readonly HdbfPlotHighlightSession _plotHighlightState;
        private readonly HdbfWorldIconService _worldIcons;

        private HdbfBoxFarmSpec _spec = null!;
        private TierProfile _profile = TierCatalog.All[0];
        private BlockObject _blockObject = null!;
        private EntityComponent _entityComponent = null!;
        private Workplace? _workplace;
        private MechanicalBuilding? _mechanicalBuilding;
        private BuildingAccessible? _buildingAccessible;
        private Accessible? _accessible;
        private HdbfBuildingRecord _record = null!;
        private ITimeTrigger? _dayTrigger;
        private HdbfPlotScanner _scanner = null!;

        private readonly HashSet<int> _excluded = new HashSet<int>();
        private readonly Dictionary<int, int> _oldIndexByPlot = new Dictionary<int, int>();

        private bool _awake;
        private bool _activated;
        private bool _saveLoaded;
        private Guid _entityId;
        private long _saveEntityId;

        // Cached snapshot inputs, refreshed once per day tick (never per frame, REQ-IMPL-27).
        private int _irrigatedCount;
        private int _tankFedPlots;
        private float _coverage;
        private int _lastMoistCount;
        private float _maintenanceMultiplier = 1f;
        private bool _isShutdown;
        private float _cycleDays;
        private int _cyclesMissed;
        private bool _powerConnected;
        private float _powerMultiplier = 1f;
        private bool _maintenanceInitialized;
        private bool _saveDirty;
        private float _lastModeChangeDay = float.NaN;
        private int _reachabilityFallbackBudget;

        /// <summary>
        /// Accepted candidates of the most recent completed plot scan, the panel's "可开垦格"
        /// figure (chapter 11.3). <see cref="BoxFarmSnapshot.UnknownTillablePlots"/> until the first
        /// scan completes and again while a new scan is running (unknown must not read as 0).
        /// </summary>
        private int _tillablePlots = BoxFarmSnapshot.UnknownTillablePlots;

        public HdbfBoxFarmController(
            ITimeTriggerFactory timeTriggerFactory,
            IDayNightCycle dayNightCycle,
            ISoilMoistureService soilMoistureService,
            IBlockService blockService,
            ITerrainService terrainService,
            PlantingService plantingService,
            PlantingAreaValidator plantingAreaValidator,
            INavigationService navigationService,
            TemplateService templateService,
            IHdbfFarmRegistry registry,
            IHdbfGameConfigProvider configProvider,
            IHdbfLog log,
            HdbfPlotHighlightSession plotHighlightState,
            HdbfWorldIconService worldIcons)
        {
            _timeTriggerFactory = timeTriggerFactory;
            _dayNightCycle = dayNightCycle;
            _soilMoistureService = soilMoistureService;
            _blockService = blockService;
            _terrainService = terrainService;
            _plantingService = plantingService;
            _plantingAreaValidator = plantingAreaValidator;
            _navigationService = navigationService;
            _templateService = templateService;
            _registry = registry;
            _configProvider = configProvider;
            _log = log;
            _plotHighlightState = plotHighlightState ?? throw new ArgumentNullException(nameof(plotHighlightState));
            _worldIcons = worldIcons ?? throw new ArgumentNullException(nameof(worldIcons));
        }

        // ------------------------------------------------------------------ identity / snapshot

        /// <inheritdoc />
        public Guid EntityId => _entityId;

        /// <inheritdoc />
        public long SaveEntityId => _saveEntityId;

        /// <summary>True when gameplay changed persisted state since the last save. Save itself is driven
        /// by the game's world-save transaction; the flag is exposed for diagnostics and lets a
        /// future autosave integration reason about the cost (chapter 12.7).
        /// </summary>
        public bool SaveDirty => _saveDirty;

        // ------------------------------------------------------------------ lifecycle

        /// <inheritdoc />
        public void Awake()
        {
            try
            {
                _spec = GetComponent<HdbfBoxFarmSpec>();
                _blockObject = GetComponent<BlockObject>();
                _entityComponent = HasComponent<EntityComponent>()
                    ? GetComponent<EntityComponent>()
                    : null!;
                _workplace = HasComponent<Workplace>() ? GetComponent<Workplace>() : null;
                _mechanicalBuilding = HasComponent<MechanicalBuilding>()
                    ? GetComponent<MechanicalBuilding>()
                    : null;
                // A building exposes reachability through BuildingAccessible, which owns the
                // navigation Accessible component; it may be initialised a little later, so the
                // Accessible reference is resolved lazily by the scan.
                _buildingAccessible = HasComponent<BuildingAccessible>()
                    ? GetComponent<BuildingAccessible>()
                    : null;

                ResolveTierProfile();

                var today = _dayNightCycle.PartialDayNumber;
                _record = HdbfSaveCodec.CreateDefault(0L, _profile.Tier, _profile, today);
                _record.TankWater = _profile.TankCapacity;
                _scanner = new HdbfPlotScanner(Math.Max(_profile.WorkRange, 1));
                _dayTrigger = _timeTriggerFactory.Create(OnDayTick, DayTriggerDelayDays);

                // Nothing may run before the building is finished: no ticks, no simulation, no scan
                // (same lifecycle as the vanilla FarmHouse component).
                DisableComponent();
                _awake = true;
            }
            catch (Exception exception)
            {
                _awake = false;
                DisableComponent();
                _log.Error(HdbfLogCategory.Error, LogKeyEntity,
                    "box farm failed to initialize; component disabled", exception);
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
            // Loaded finished buildings never receive OnEnterFinishedState again.
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

            // BUILD-05: make the crop models this farm hid visible again even when Deactivate
            // threw above; the entity tree is going away, so a stale hidden renderer would leak.
            RestoreAllCropVisuals("delete");

            // The entity tree is going away: drop every icon reference so the service does not
            // keep destroyed BaseComponent objects alive (temporary finished-state exits keep
            // them, see HdbfWorldIconService.Deactivate). Runs even if Deactivate threw.
            ForgetWorldIcons();
        }

        // ------------------------------------------------------------------ persistence

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
                _record.EntityId = _saveEntityId;
                HdbfEntitySaveBridge.Save(_record, entitySaver);
                _saveDirty = false;
            }
            catch (Exception exception)
            {
                // One building must never abort the world save (REQ-SAVE-01, REQ-SAFE-13).
                _log.Error(HdbfLogCategory.Error, LogKeySave,
                    "HDBF record save failed; keeping the previous state", exception,
                    _entityId.ToString());
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
                var today = _dayNightCycle.PartialDayNumber;
                _record = HdbfEntitySaveBridge.Load(entityLoader, _saveEntityId, _profile.Tier, _profile,
                    today, out var degraded,
                    warning => _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.SaveDegraded, warning));

                _saveLoaded = true;
                _maintenanceInitialized = true;
                _excluded.Clear();
                var excludedIds = _record.ExcludedPlotIds;
                for (var i = 0; i < excludedIds.Length; i++)
                {
                    _excluded.Add(excludedIds[i]);
                }

                if (degraded)
                {
                    _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.SaveRecordReset,
                        "box farm record was repaired; using sanitized defaults, entityId=" + _entityId);
                }
            }
            catch (Exception exception)
            {
                // Keep the safe defaults created in Awake; the building stays playable (REQ-SAFE-08).
                _log.Error(HdbfLogCategory.Error, LogKeySave,
                    "HDBF record load failed; building keeps safe defaults", exception,
                    _entityId.ToString());
            }
        }
    }
}

