// ============================================================================
// HDBF v2 | Layer: Persistence/Indoor | Owner: V2-GAMEPLAY-01
// Purpose: Best-effort v1 "HDBF.BoxFarm" entity read feeding IndoorSaveMigrator. Kept in
//          a partial beside the v2 bridge so the v2 path stays short and readable.
// Requirements: REQ-V2-SAVE-02, REQ-V2-SAVE-05, REQ-V2-SAFE-01
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace HDBF.Persistence.Indoor
{
    public static partial class IndoorEntitySaveBridge
    {
        /// <summary>The v1 component key shipped by the former v1 entity bridge.</summary>
        public const string LegacyComponentKeyName = "HDBF.BoxFarm";

        private static readonly ComponentKey LegacyComponent = new ComponentKey(LegacyComponentKeyName);
        private static readonly PropertyKey<int> LegacyMode = new PropertyKey<int>("Mode");
        private static readonly PropertyKey<float> LegacyDueDay = new PropertyKey<float>("DueDay");
        private static readonly PropertyKey<int> LegacyCyclesMissed = new PropertyKey<int>("CyclesMissed");
        private static readonly PropertyKey<float> LegacyTankWater = new PropertyKey<float>("TankWater");
        private static readonly ListKey<int> LegacyPlotIds = new ListKey<int>("PlotIds");
        private static readonly ListKey<string> LegacyCropIds = new ListKey<string>("CropIds");
        private static readonly ListKey<float> LegacyFatigue = new ListKey<float>("Fatigue");
        private static readonly PropertyKey<bool> LegacyPortPower = new PropertyKey<bool>("PortPower");
        private static readonly PropertyKey<bool> LegacyPortPipe = new PropertyKey<bool>("PortPipe");
        private static readonly PropertyKey<bool> LegacyPortLogistics = new PropertyKey<bool>("PortLogistics");

        /// <summary>
        /// Reads the old component and maps it through <see cref="IndoorSaveMigrator"/> when present.
        /// A missing key is a fresh/new building and is not reported as damage; a present but
        /// unreadable v1 node degrades to the safe default (REQ-V2-SAVE-05).
        /// </summary>
        private static IndoorFarmRecord? TryLoadV1(IEntityLoader loader, TierProfile tier, HdbfFaction faction,
            ICropCatalog? catalog, IHdbfLog? log, ref bool degraded)
        {
            try
            {
                IObjectLoader? component = null;
                if (!loader.TryGetComponent(LegacyComponent, out component) || component is null)
                {
                    return null;
                }

                var legacy = new IndoorLegacyV1Record
                {
                    EntityId = string.Empty,
                    Tier = tier.Tier,
                    Faction = faction,
                    Mode = ReadLegacyMode(component),
                    PlotIds = ReadIntList(component, LegacyPlotIds),
                    Crops = ReadStringList(component, LegacyCropIds),
                    Fatigue = ReadFloatList(component, LegacyFatigue),
                    Tank = ReadFloat(component, LegacyTankWater, 0f),
                    MaintenanceDueDay = ReadFloat(component, LegacyDueDay, 0f),
                    MaintenanceCyclesMissed = ReadInt(component, LegacyCyclesMissed, 0),
                    PowerConnected = ReadBool(component, LegacyPortPower),
                    PipePort = ReadBool(component, LegacyPortPipe),
                    LogisticsPort = ReadBool(component, LegacyPortLogistics),
                    PowerPort = tier.NeedsPower,
                    TimberPipesAdapter = string.Empty,
                };

                var migrator = new IndoorSaveMigrator(log);
                if (!migrator.TryMigrateV1(legacy, tier, out var migrated))
                {
                    degraded = true;
                    return null;
                }

                var validation = IndoorSaveValidator.ValidateAndRepair(migrated, tier,
                    catalog ?? FallbackCropCatalog.Default, migrated.EntityId);
                if (validation is not null && validation.Repaired)
                {
                    degraded = true;
                }

                migrated.Faction = faction;
                degraded = true;
                return migrated;
            }
            catch (Exception exception)
            {
                degraded = true;
                Warn(log, IndoorLogKeys.MigratedFailed, "v1 indoor record read failed; safe defaults kept", exception);
                return null;
            }
        }

        private static int ReadInt(IObjectLoader loader, PropertyKey<int> key, int fallback)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static float ReadFloat(IObjectLoader loader, PropertyKey<float> key, float fallback)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static bool ReadBool(IObjectLoader loader, PropertyKey<bool> key)
        {
            try
            {
                return loader.Has(key) && loader.Get(key);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int[] ReadIntList(IObjectLoader loader, ListKey<int> key)
        {
            try
            {
                if (!loader.Has(key))
                {
                    return Array.Empty<int>();
                }

                var values = loader.Get(key);
                return values is null || values.Count == 0 ? Array.Empty<int>() : Copy(values);
            }
            catch (Exception)
            {
                return Array.Empty<int>();
            }
        }

        private static string[] ReadStringList(IObjectLoader loader, ListKey<string> key)
        {
            try
            {
                if (!loader.Has(key))
                {
                    return Array.Empty<string>();
                }

                var values = loader.Get(key);
                if (values is null || values.Count == 0)
                {
                    return Array.Empty<string>();
                }

                var copy = new string[values.Count];
                for (var index = 0; index < copy.Length; index++)
                {
                    copy[index] = values[index] ?? string.Empty;
                }

                return copy;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static float[] ReadFloatList(IObjectLoader loader, ListKey<float> key)
        {
            try
            {
                if (!loader.Has(key))
                {
                    return Array.Empty<float>();
                }

                var values = loader.Get(key);
                if (values is null || values.Count == 0)
                {
                    return Array.Empty<float>();
                }

                var copy = new float[values.Count];
                for (var index = 0; index < copy.Length; index++)
                {
                    copy[index] = values[index];
                }

                return copy;
            }
            catch (Exception)
            {
                return Array.Empty<float>();
            }
        }

        private static int[] Copy(List<int> values)
        {
            var copy = new int[values.Count];
            for (var index = 0; index < copy.Length; index++)
            {
                copy[index] = values[index];
            }

            return copy;
        }

        private static HdbfMode ReadLegacyMode(IObjectLoader loader)
        {
            switch (ReadInt(loader, LegacyMode, 0))
            {
                case 1: return HdbfMode.Dense;
                case 2: return HdbfMode.Fallow;
                default: return HdbfMode.Normal;
            }
        }
    }
}
