// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-SAVE-01
// Purpose: The single stable key table of the entity-scoped indoor save: component names, scalar
//          PropertyKey<T> fields, slot parallel-array ListKey<T> fields and the published v1 keys.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-02, REQ-V2-SAVE-04, REQ-V2-SAVE-06, REQ-V2-EXT-03
// ============================================================================
// Key names are a persistence contract: a released key can never be renamed, only added to
// (REQ-V2-EXT-03). Only int/float/bool/string primitives exist in the game serializer, so enums
// are stored as int and the slot flag byte is stored as int (docs/API_FACTS.md section 6).
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace HDBF.Persistence.Indoor
{
    /// <summary>
    /// Every component/property/list key of the schema-v2 indoor farm save (INTERFACES section 8,
    /// section 19.10). The keyed bridge writes one documented property per scalar field and six
    /// parallel lists for the 60/150/300/600 slots, so no slot object is ever serialized on its
    /// own. The v1 keys stay declared here because the migration reader of the old
    /// <c>HDBF.BoxFarm</c> component reads them verbatim (section 19.10 / REQ-V2-SAVE-02) and
    /// v1 keys were published, so renaming one would orphan every existing save.
    /// </summary>
    public static class IndoorSaveKeys
    {
        // ------------------------------------------------------------ component names

        /// <summary>Component key of the v2 indoor record; never rename after release.</summary>
        public const string ComponentKeyName = "HDBF.IndoorFarm";

        /// <summary>Legacy schema-v2 text payload property written by the pre-keyed bridge.</summary>
        public const string PayloadKeyName = "Payload";

        /// <summary>Component key of the v1 entity record, read only by the migration path.</summary>
        public const string LegacyComponentKeyName = "HDBF.BoxFarm";

        /// <summary>V2 component handle; the entity saver/loader is always accessed through it.</summary>
        public static readonly ComponentKey Component = new ComponentKey(ComponentKeyName);

        /// <summary>V1 component handle; the migration reader never writes through it.</summary>
        public static readonly ComponentKey LegacyComponent = new ComponentKey(LegacyComponentKeyName);

        /// <summary>Payload-string fallback key kept readable for records written before the keyed format.</summary>
        public static readonly PropertyKey<string> Payload = new PropertyKey<string>(PayloadKeyName);

        // ------------------------------------------------------------ record scalars (v2)

        /// <summary>Schema version of the record; its presence marks a keyed v2 node.</summary>
        public static readonly PropertyKey<int> SchemaVersion = new PropertyKey<int>("SchemaVersion");

        /// <summary>Building entity id as invariant text (the serializer has no Int64).</summary>
        public static readonly PropertyKey<string> EntityId = new PropertyKey<string>("EntityId");

        /// <summary>Tier enum value; the resolved tier profile is authoritative on load.</summary>
        public static readonly PropertyKey<int> Tier = new PropertyKey<int>("Tier");

        /// <summary>Faction enum value; the building blueprint is authoritative on load.</summary>
        public static readonly PropertyKey<int> Faction = new PropertyKey<int>("Faction");

        /// <summary>Operating mode enum value (Normal/Dense/Fallow).</summary>
        public static readonly PropertyKey<int> Mode = new PropertyKey<int>("Mode");

        /// <summary>Crop policy enum value (Rotation/Fixed).</summary>
        public static readonly PropertyKey<int> CropPolicy = new PropertyKey<int>("CropPolicy");

        /// <summary>Fixed crop id; empty for the rotation policy.</summary>
        public static readonly PropertyKey<string> FixedCropId = new PropertyKey<string>("FixedCropId");

        /// <summary>Water units currently held by the internal tank.</summary>
        public static readonly PropertyKey<float> Tank = new PropertyKey<float>("Tank");

        /// <summary>Game day on which the next material maintenance is due.</summary>
        public static readonly PropertyKey<float> MaintenanceDueDay = new PropertyKey<float>("MaintenanceDueDay");

        /// <summary>Missed material maintenance cycles; two or more stall farm actions.</summary>
        public static readonly PropertyKey<int> MaintenanceCyclesMissed =
            new PropertyKey<int>("MaintenanceCyclesMissed");

        /// <summary>True while the power tier is attached to a working grid.</summary>
        public static readonly PropertyKey<bool> PowerConnected = new PropertyKey<bool>("PowerConnected");

        /// <summary>Power contract enum value (NotRequired/Grid/OffGrid).</summary>
        public static readonly PropertyKey<int> PowerMode = new PropertyKey<int>("PowerMode");

        /// <summary>True when the water pipe port exists on this tier.</summary>
        public static readonly PropertyKey<bool> PortPipe = new PropertyKey<bool>("PortPipe");

        /// <summary>True when the power port exists (top tier only).</summary>
        public static readonly PropertyKey<bool> PortPower = new PropertyKey<bool>("PortPower");

        /// <summary>True when the logistics output port exists (intermediate tier and above).</summary>
        public static readonly PropertyKey<bool> PortLogistics = new PropertyKey<bool>("PortLogistics");

        /// <summary>Last reported Timber Pipes adapter state string (None/Ok/Degraded/Disabled/Failed).</summary>
        public static readonly PropertyKey<string> TimberPipesAdapter =
            new PropertyKey<string>("TimberPipesAdapter");

        // ------------------------------------------------------------ slot parallel arrays (v2)

        /// <summary>Slot crop ids in slot order; empty string marks an empty slot.</summary>
        public static readonly ListKey<string> SlotCropIds = new ListKey<string>("SlotCropIds");

        /// <summary>Slot growth progress in game days, one entry per slot.</summary>
        public static readonly ListKey<float> SlotGrowth = new ListKey<float>("SlotGrowth");

        /// <summary>Slot soil fatigue in [0,1], one entry per slot.</summary>
        public static readonly ListKey<float> SlotFatigue = new ListKey<float>("SlotFatigue");

        /// <summary>Previous slot crop ids (rotation basis), one entry per slot; empty = none.</summary>
        public static readonly ListKey<string> SlotLastCropIds = new ListKey<string>("SlotLastCropIds");

        /// <summary>Slot stage enum values as int (Empty/Growing/Mature), one entry per slot.</summary>
        public static readonly ListKey<int> SlotStages = new ListKey<int>("SlotStages");

        /// <summary>Slot flag bits as int (only bit 0 is persisted today), one entry per slot.</summary>
        public static readonly ListKey<int> SlotFlags = new ListKey<int>("SlotFlags");

        // ------------------------------------------------------------ published v1 keys (read-only)

        /// <summary>v1 operating mode (int); published, never rename.</summary>
        public static readonly PropertyKey<int> LegacyMode = new PropertyKey<int>("Mode");

        /// <summary>v1 maintenance due day; published, never rename.</summary>
        public static readonly PropertyKey<float> LegacyDueDay = new PropertyKey<float>("DueDay");

        /// <summary>v1 missed maintenance cycles; published, never rename.</summary>
        public static readonly PropertyKey<int> LegacyCyclesMissed = new PropertyKey<int>("CyclesMissed");

        /// <summary>v1 internal tank water; published, never rename.</summary>
        public static readonly PropertyKey<float> LegacyTankWater = new PropertyKey<float>("TankWater");

        /// <summary>v1 registered plot ids, in registration order; published, never rename.</summary>
        public static readonly ListKey<int> LegacyPlotIds = new ListKey<int>("PlotIds");

        /// <summary>v1 crop id per registered plot; published, never rename.</summary>
        public static readonly ListKey<string> LegacyCropIds = new ListKey<string>("CropIds");

        /// <summary>v1 per-plot fatigue; published, never rename.</summary>
        public static readonly ListKey<float> LegacyFatigue = new ListKey<float>("Fatigue");

        /// <summary>v1 power port/connection flag; published, never rename.</summary>
        public static readonly PropertyKey<bool> LegacyPortPower = new PropertyKey<bool>("PortPower");

        /// <summary>v1 water pipe port flag; published, never rename.</summary>
        public static readonly PropertyKey<bool> LegacyPortPipe = new PropertyKey<bool>("PortPipe");

        /// <summary>v1 logistics output port flag; published, never rename.</summary>
        public static readonly PropertyKey<bool> LegacyPortLogistics = new PropertyKey<bool>("PortLogistics");
    }
}
