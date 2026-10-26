// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: The runtime/persisted state of one indoor farm (INTERFACES section 4)
//          plus the safe factory used by the save migrator and by tests.
//          Does not encode/decode or validate; that is the persistence card.
// Requirements: REQ-V2-CORE-01, REQ-V2-SAVE-04, REQ-V2-SAVE-06
// ============================================================================
using HDBF.Core.Balance;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Runtime and save state of one indoor farm (INTERFACES section 4). Invariants:
    /// <c>Slots.Length == TierProfile.SlotCapacity</c>; tank in [0, TankCapacity]; all floats
    /// finite; enums whitelisted; <see cref="FixedCropId"/> empty or a faction crop.
    /// </summary>
    public sealed class IndoorFarmRecord
    {
        /// <summary>Current save schema version of an indoor farm record.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>Schema version the record was written with; used by the migration chain.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Building entity id, diagnostics only; may be empty.</summary>
        public string EntityId { get; set; } = string.Empty;

        /// <summary>Tier of the building; fixes the slot capacity and the balance profile.</summary>
        public HdbfTier Tier { get; set; }

        /// <summary>Faction of the building; fixes the crop table.</summary>
        public HdbfFaction Faction { get; set; }

        /// <summary>Operating mode (normal/dense/fallow).</summary>
        public IndoorMode Mode { get; set; }

        /// <summary>Crop selection policy (rotation/fixed).</summary>
        public CropPolicy CropPolicy { get; set; }

        /// <summary>Crop planted while <see cref="CropPolicy"/> is Fixed; empty string otherwise.</summary>
        public string FixedCropId { get; set; } = string.Empty;

        /// <summary>Slot array; its length must equal the tier slot capacity.</summary>
        public IndoorSlot[] Slots { get; set; } = new IndoorSlot[0];

        /// <summary>Water currently held by the internal tank, in water units.</summary>
        public float Tank { get; set; }

        /// <summary>Game day on which the next material maintenance is due (0 = none/basic tier).</summary>
        public float MaintenanceDueDay { get; set; }

        /// <summary>Material maintenance cycles missed; 1 derates, 2 or more stalls farm actions.</summary>
        public int MaintenanceCyclesMissed { get; set; }

        /// <summary>True while the top tier has grid power; false when off-grid or power is lost.</summary>
        public bool PowerConnected { get; set; }

        /// <summary>Power contract of the building (not required/grid/off-grid).</summary>
        public PowerConnection PowerMode { get; set; }

        /// <summary>True when the water pipe port exists on this tier (all tiers in v2).</summary>
        public bool PipePort { get; set; }

        /// <summary>True when the logistics output port exists (intermediate tier and above).</summary>
        public bool LogisticsPort { get; set; }

        /// <summary>True when the power port exists (top tier only).</summary>
        public bool PowerPort { get; set; }

        /// <summary>Last reported Timber Pipes adapter state: None/Ok/Degraded/Disabled/Failed.</summary>
        public string TimberPipesAdapter { get; set; } = IndoorConstants.AdapterStateNone;

        /// <summary>
        /// Creates a safe default record: empty slots, a full tank, default maintenance for the
        /// tier and per-tier port defaults (REQ-V2-SAVE-06 recovery path). The caller (save
        /// migrator) may overwrite individual fields afterwards.
        /// </summary>
        /// <param name="tier">Tier of the building; decides the slot capacity and default tank.</param>
        /// <param name="faction">Faction of the building; decides the crop table.</param>
        /// <returns>A valid empty record for the requested tier/faction.</returns>
        public static IndoorFarmRecord Create(HdbfTier tier, HdbfFaction faction)
        {
            var profile = TierCatalog.Get(tier);
            var capacity = profile.SlotCapacity;
            var slots = new IndoorSlot[capacity];
            for (var index = 0; index < capacity; index++)
            {
                slots[index] = new IndoorSlot();
            }

            return new IndoorFarmRecord
            {
                SchemaVersion = CurrentSchemaVersion,
                EntityId = string.Empty,
                Tier = tier,
                Faction = faction,
                Mode = IndoorMode.Normal,
                CropPolicy = CropPolicy.Rotation,
                FixedCropId = string.Empty,
                Slots = slots,
                Tank = profile.TankCapacity,
                MaintenanceDueDay = MaintenanceDefaults.DefaultCycleDays(profile),
                MaintenanceCyclesMissed = 0,
                PowerConnected = false,
                PowerMode = profile.NeedsPower ? PowerConnection.Grid : PowerConnection.NotRequired,
                PipePort = true,
                LogisticsPort = (int)tier >= (int)HdbfTier.Intermediate,
                PowerPort = profile.NeedsPower,
                TimberPipesAdapter = IndoorConstants.AdapterStateNone,
            };
        }
    }
}
