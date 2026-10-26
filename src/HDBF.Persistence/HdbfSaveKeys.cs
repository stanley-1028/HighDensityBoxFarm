using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace HDBF.Persistence
{
    /// <summary>
    /// The single, stable table of serialization keys used by <see cref="HdbfEntitySaveBridge"/>.
    ///
    /// Field names are an external contract: a published save that is read by a newer build must
    /// keep every key name unchanged (REQ-SAVE-04). Anything that needs a new field must add a key
    /// here and migrate, never rename an existing one (chapter 12.3).
    ///
    /// The type arguments are the verified Timberborn persistence primitives only:
    /// <c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>. <c>long</c> is deliberately not used
    /// because <c>Timberborn.SerializationSystem.PrimitiveTypeSerialization</c> does not handle
    /// <c>Int64</c> (verified with ApiIndex: 32-bit ints, floats, bools, strings, chars, enums,
    /// Unity vectors/colors/quaternions and Guid only), so the entity id travels as an invariant
    /// string instead. See API_FACTS.md and the agent report for the exact evidence.
    /// </summary>
    internal static class HdbfSaveKeys
    {
        /// <summary>One component per HDBF building; never a separate save file node (REQ-SAVE-01/03).</summary>
        internal const string ComponentName = HdbfEntitySaveBridge.ComponentKeyName;

        internal const string SchemaVersionName = "SchemaVersion";
        internal const string EntityIdName = "EntityId";
        internal const string TierName = "Tier";
        internal const string ModeName = "Mode";
        internal const string DueDayName = "DueDay";
        internal const string CyclesMissedName = "CyclesMissed";
        internal const string TankWaterName = "TankWater";
        internal const string PlotIdsName = "PlotIds";
        internal const string CropIdsName = "CropIds";
        internal const string FatigueName = "Fatigue";
        internal const string ExcludedPlotIdsName = "ExcludedPlotIds";
        internal const string PortPowerName = "PortPower";
        internal const string PortPipeName = "PortPipe";
        internal const string PortLogisticsName = "PortLogistics";
        internal const string AdapterKeysName = "AdapterKeys";
        internal const string AdapterValuesName = "AdapterValues";

        internal static readonly ComponentKey Component = new ComponentKey(ComponentName);

        internal static readonly PropertyKey<int> SchemaVersion = new PropertyKey<int>(SchemaVersionName);

        internal static readonly PropertyKey<string> EntityId = new PropertyKey<string>(EntityIdName);

        internal static readonly PropertyKey<int> Tier = new PropertyKey<int>(TierName);

        internal static readonly PropertyKey<int> Mode = new PropertyKey<int>(ModeName);

        internal static readonly PropertyKey<float> DueDay = new PropertyKey<float>(DueDayName);

        internal static readonly PropertyKey<int> CyclesMissed = new PropertyKey<int>(CyclesMissedName);

        internal static readonly PropertyKey<float> TankWater = new PropertyKey<float>(TankWaterName);

        internal static readonly ListKey<int> PlotIds = new ListKey<int>(PlotIdsName);

        internal static readonly ListKey<string> CropIds = new ListKey<string>(CropIdsName);

        internal static readonly ListKey<float> Fatigue = new ListKey<float>(FatigueName);

        internal static readonly ListKey<int> ExcludedPlotIds = new ListKey<int>(ExcludedPlotIdsName);

        internal static readonly PropertyKey<bool> PortPower = new PropertyKey<bool>(PortPowerName);

        internal static readonly PropertyKey<bool> PortPipe = new PropertyKey<bool>(PortPipeName);

        internal static readonly PropertyKey<bool> PortLogistics = new PropertyKey<bool>(PortLogisticsName);

        /// <summary>
        /// Adapter status is a map in the logical schema (chapter 12.2) but saved as two parallel
        /// string lists because <c>IObjectSaver</c> has no dictionary member (verified API dump).
        /// </summary>
        internal static readonly ListKey<string> AdapterKeys = new ListKey<string>(AdapterKeysName);

        internal static readonly ListKey<string> AdapterValues = new ListKey<string>(AdapterValuesName);
    }
}
