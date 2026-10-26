using System;
using System.Collections.Generic;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Save;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;

namespace HDBF.Persistence
{
    /// <summary>
    /// Maps a <see cref="HdbfBuildingRecord"/> onto Timberborn's per-entity persistence.
    ///
    /// Design: the game already serializes components transactionally as part of the world save,
    /// so HDBF writes one component (<see cref="ComponentKeyName"/>) on its own building entity
    /// instead of adding a second save pipeline (chapter 12, REQ-SAVE-01/03). That keeps entity
    /// identity and load order in the hands of the game, and confines HDBF damage to HDBF keys.
    ///
    /// Guarantees (verified against the installed 1.1.2.8 assemblies, see docs/API_FACTS.md):
    /// * <see cref="IObjectSaver"/> offers <c>Set</c> overloads only for primitive properties and
    ///   primitive lists ??there is no dictionary writer, so adapter status is written as two
    ///   parallel string lists;
    /// * <c>IObjectLoader.Get</c> / <c>GetArray</c> throw <c>ArgumentOutOfRangeException</c> when a
    ///   key is absent (verified in <c>SerializedObject::Get</c> IL), so every read is guarded by
    ///   <c>Has</c> and a per-field try/catch rather than relying on a default value;
    /// * no exception escapes <see cref="Save"/> or <see cref="Load"/> (REQ-SAFE-01,
    ///   REQ-SAFE-08, REQ-SAFE-09, REQ-SAFE-13).
    ///
    /// Repair and migration live in <see cref="HdbfSaveCodec"/> so they stay game-free and
    /// unit-testable; this class only translates between game keys and the raw record.
    /// </summary>
    public static class HdbfEntitySaveBridge
    {
        /// <summary>Stable component key for every HDBF building (REQ-COMPAT-10 style naming).</summary>
        public const string ComponentKeyName = "HDBF.BoxFarm";

        /// <summary>
        /// Writes one record into the entity saver under stable keys.
        ///
        /// The write is defensive: null collections are replaced by empty ones, non-finite floats
        /// are sanitised to a safe default so the JSON writer can never see NaN/Infinity, and a
        /// failure of the underlying saver is swallowed so one component can never abort the whole
        /// world save (REQ-SAVE-01, REQ-SAFE-13). A partially written record is not a problem:
        /// <see cref="Load"/> treats every missing key as its documented default (chapter 12.7).
        /// </summary>
        public static void Save(HdbfBuildingRecord? record, IEntitySaver? saver)
        {
            if (record is null || saver is null)
            {
                return;
            }

            try
            {
                var component = saver.GetComponent(HdbfSaveKeys.Component);
                if (component is null)
                {
                    return;
                }

                component.Set(HdbfSaveKeys.SchemaVersion, record.SchemaVersion);
                component.Set(HdbfSaveKeys.EntityId, record.EntityId.ToString(CultureInfo.InvariantCulture));
                component.Set(HdbfSaveKeys.Tier, (int)record.Tier);
                component.Set(HdbfSaveKeys.Mode, (int)record.Mode);
                component.Set(HdbfSaveKeys.DueDay, SafeFloat(record.DueDay));
                component.Set(HdbfSaveKeys.CyclesMissed, record.CyclesMissed);
                component.Set(HdbfSaveKeys.TankWater, SafeFloat(record.TankWater));

                var plotIds = record.PlotIds ?? Array.Empty<int>();
                component.Set(HdbfSaveKeys.PlotIds, plotIds);
                component.Set(HdbfSaveKeys.CropIds, Align(record.CropIds, plotIds.Length, string.Empty));
                component.Set(HdbfSaveKeys.Fatigue, Align(record.Fatigue, plotIds.Length, 0f));
                component.Set(HdbfSaveKeys.ExcludedPlotIds, record.ExcludedPlotIds ?? Array.Empty<int>());

                component.Set(HdbfSaveKeys.PortPower, record.PortPower);
                component.Set(HdbfSaveKeys.PortPipe, record.PortPipe);
                component.Set(HdbfSaveKeys.PortLogistics, record.PortLogistics);

                var adapterKeys = new List<string>();
                var adapterValues = new List<string>();
                var adapters = record.Adapters;
                if (adapters is not null)
                {
                    foreach (var pair in adapters)
                    {
                        if (string.IsNullOrEmpty(pair.Key))
                        {
                            continue;
                        }

                        adapterKeys.Add(pair.Key);
                        adapterValues.Add(pair.Value ?? string.Empty);
                    }
                }

                component.Set(HdbfSaveKeys.AdapterKeys, adapterKeys);
                component.Set(HdbfSaveKeys.AdapterValues, adapterValues);
            }
            catch (Exception)
            {
                // Swallowed on purpose: REQ-SAVE-01 says a save must never be aborted by one
                // component, and REQ-SAFE-01 says damage must be confined. The missing keys are
                // recreated from defaults on the next load and reported there through `warn`.
            }
        }

        /// <summary>
        /// Reads best-effort; missing/corrupt fields fall back to defaults instead of throwing.
        ///
        /// Read order follows chapter 12: obtain the component, read every field independently,
        /// assemble a raw record, then run <see cref="HdbfSaveCodec.TrySanitize"/> (which also runs
        /// the idempotent migration chain). If a field is absent it is silently completed with the
        /// documented default (table 10-2); if a field exists but cannot be read, the value falls
        /// back and the load is marked degraded; if the record is unusable a default record is
        /// returned with <paramref name="degraded"/> set (REQ-SAVE-08, REQ-SAFE-08).
        /// </summary>
        /// <param name="loader">Entity loader supplied by the game.</param>
        /// <param name="entityId">Entity id known by the caller, used when the save has none or an unreadable one.</param>
        /// <param name="tier">Tier implied by the blueprint, used when the save has none or an invalid one.</param>
        /// <param name="profile">Resolved tier profile, used by repair and migration.</param>
        /// <param name="currentDay">Current game day, used to rebuild maintenance deadlines.</param>
        /// <param name="degraded">True when the returned record is not exactly what was saved.</param>
        /// <param name="warn">Optional diagnostics sink; a stable short reason string is passed.</param>
        public static HdbfBuildingRecord Load(IEntityLoader? loader, long entityId, HdbfTier tier,
            TierProfile? profile, float currentDay, out bool degraded, Action<string>? warn = null)
        {
            degraded = false;
            var fallback = SafeDefault(entityId, tier, profile, currentDay, out var fallbackDegraded);
            if (fallbackDegraded)
            {
                degraded = true;
                Warn(warn, "profile-invalid");
            }

            if (profile is null)
            {
                // Without a tier profile nothing can be repaired or migrated; the bare default is
                // already the most conservative usable record (REQ-SAFE-08).
                return fallback;
            }

            if (loader is null)
            {
                degraded = true;
                Warn(warn, "loader-missing");
                return fallback;
            }

            try
            {
                IObjectLoader? component = null;
                try
                {
                    if (!loader.TryGetComponent(HdbfSaveKeys.Component, out component) || component is null)
                    {
                        // No HDBF component on this entity: either a freshly placed building or a
                        // save written before HDBF existed. This is the documented "node missing"
                        // case of chapter 12.5, not damage, so it is not reported as degraded.
                        return fallback;
                    }
                }
                catch (Exception exception)
                {
                    degraded = true;
                    Warn(warn, "component-unreadable:" + exception.GetType().Name);
                    return fallback;
                }

                var failures = new List<string>(4);
                var raw = new HdbfBuildingRecord
                {
                    // Missing fields are completed with the documented tier defaults (table 10-2),
                    // not with CLR zero, so e.g. a missing DueDay still means "one full cycle from
                    // now" for maintenance tiers (REQ-SAVE-04/12).
                    SchemaVersion = ReadInt(component, HdbfSaveKeys.SchemaVersion, "schemaVersion", failures, 0),
                    EntityId = ReadEntityId(component, entityId, failures),
                    Tier = ReadTier(component, tier, failures),
                    Mode = ReadMode(component, fallback.Mode, failures),
                    DueDay = ReadFloat(component, HdbfSaveKeys.DueDay, "dueDay", failures, fallback.DueDay),
                    CyclesMissed = ReadInt(component, HdbfSaveKeys.CyclesMissed, "cyclesMissed", failures,
                        fallback.CyclesMissed),
                    TankWater = ReadFloat(component, HdbfSaveKeys.TankWater, "tankWater", failures,
                        fallback.TankWater),
                    PlotIds = ReadIntList(component, HdbfSaveKeys.PlotIds, "plotIds", failures),
                    CropIds = ReadStringList(component, HdbfSaveKeys.CropIds, "cropIds", failures),
                    Fatigue = ReadFloatList(component, HdbfSaveKeys.Fatigue, "fatigue", failures),
                    ExcludedPlotIds = ReadIntList(component, HdbfSaveKeys.ExcludedPlotIds, "excludedPlotIds", failures),
                    PortPower = ReadBool(component, HdbfSaveKeys.PortPower, "portPower", failures),
                    PortPipe = ReadBool(component, HdbfSaveKeys.PortPipe, "portPipe", failures),
                    PortLogistics = ReadBool(component, HdbfSaveKeys.PortLogistics, "portLogistics", failures),
                    Adapters = ReadAdapters(component, failures),
                };

                if (failures.Count > 0)
                {
                    // REQ-SAFE-13: a failed field must be visible, never silent.
                    degraded = true;
                    Warn(warn, "fields-fallback:" + string.Join(",", failures.ToArray()));
                }

                try
                {
                    if (!HdbfSaveCodec.TrySanitize(raw, profile, currentDay, out var repaired, out var reason))
                    {
                        degraded = true;
                        Warn(warn, "sanitized:" + (reason ?? "repaired"));
                    }

                    return repaired;
                }
                catch (Exception exception)
                {
                    degraded = true;
                    Warn(warn, "sanitize-failed:" + exception.GetType().Name);
                    return fallback;
                }
            }
            catch (Exception exception)
            {
                // Last line of defence: Load must never throw into the game's load loop
                // (REQ-SAVE-01/09, REQ-SAFE-08).
                degraded = true;
                Warn(warn, "load-failed:" + exception.GetType().Name);
                return fallback;
            }
        }

        private static long ReadEntityId(IObjectLoader loader, long entityId, List<string> failures)
        {
            var text = ReadString(loader, HdbfSaveKeys.EntityId, "entityId", failures);
            if (string.IsNullOrEmpty(text))
            {
                return entityId;
            }

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : entityId;
        }

        private static HdbfTier ReadTier(IObjectLoader loader, HdbfTier tier, List<string> failures)
        {
            var value = ReadInt(loader, HdbfSaveKeys.Tier, "tier", failures, (int)tier);
            return Enum.IsDefined(typeof(HdbfTier), value) ? (HdbfTier)value : tier;
        }

        private static HdbfMode ReadMode(IObjectLoader loader, HdbfMode fallbackMode, List<string> failures)
        {
            var value = ReadInt(loader, HdbfSaveKeys.Mode, "mode", failures, (int)fallbackMode);
            return Enum.IsDefined(typeof(HdbfMode), value) ? (HdbfMode)value : fallbackMode;
        }

        private static Dictionary<string, string> ReadAdapters(IObjectLoader loader, List<string> failures)
        {
            var keys = ReadStringList(loader, HdbfSaveKeys.AdapterKeys, "adapterKeys", failures);
            var values = ReadStringList(loader, HdbfSaveKeys.AdapterValues, "adapterValues", failures);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var count = keys.Length < values.Length ? keys.Length : values.Length;
            if (keys.Length != values.Length)
            {
                // REQ-SAVE-10: keep every pair we can pair, report the damage, never throw.
                failures.Add("adapterLengthMismatch");
            }

            for (var i = 0; i < count; i++)
            {
                var key = keys[i];
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                result[key] = values[i] ?? string.Empty;
            }

            return result;
        }

        /// <summary>
        /// Reads one <c>int</c> property, returning its default when the key is absent and falling
        /// back to the default on any read failure (REQ-SAFE-08). A failure is recorded by name so
        /// the caller can report it without aborting the rest of the record.
        ///
        /// The typed overloads exist because <c>IObjectLoader.Get&lt;T&gt;</c> is constrained to
        /// <c>System.Enum</c> in this game version (verified by compiler: CS0314); primitive
        /// properties must use the dedicated non-generic <c>Get(PropertyKey&lt;int&gt;)</c> overloads.
        /// </summary>
        private static int ReadInt(IObjectLoader loader, PropertyKey<int> key, string field, List<string> failures,
            int fallback)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return fallback;
            }
        }

        private static float ReadFloat(IObjectLoader loader, PropertyKey<float> key, string field, List<string> failures,
            float fallback)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) : fallback;
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return fallback;
            }
        }

        private static bool ReadBool(IObjectLoader loader, PropertyKey<bool> key, string field, List<string> failures)
        {
            try
            {
                return loader.Has(key) && loader.Get(key);
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return false;
            }
        }

        private static string ReadString(IObjectLoader loader, PropertyKey<string> key, string field, List<string> failures)
        {
            try
            {
                return loader.Has(key) ? loader.Get(key) ?? string.Empty : string.Empty;
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads one primitive list. Absent keys are the documented "omitted default" case
        /// (chapter 12.7) and yield an empty array; unreadable keys yield an empty array plus a
        /// recorded failure (REQ-SAFE-08).
        /// </summary>
        private static int[] ReadIntList(IObjectLoader loader, ListKey<int> key, string field, List<string> failures)
        {
            try
            {
                if (!loader.Has(key))
                {
                    return Array.Empty<int>();
                }

                var values = loader.Get(key);
                if (values is null || values.Count == 0)
                {
                    return Array.Empty<int>();
                }

                var result = new int[values.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = values[i];
                }

                return result;
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return Array.Empty<int>();
            }
        }

        private static float[] ReadFloatList(IObjectLoader loader, ListKey<float> key, string field, List<string> failures)
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

                var result = new float[values.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = values[i];
                }

                return result;
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return Array.Empty<float>();
            }
        }

        private static string[] ReadStringList(IObjectLoader loader, ListKey<string> key, string field, List<string> failures)
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

                var result = new string[values.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = values[i] ?? string.Empty;
                }

                return result;
            }
            catch (Exception exception)
            {
                failures.Add(field + ":" + exception.GetType().Name);
                return Array.Empty<string>();
            }
        }

        private static HdbfBuildingRecord SafeDefault(long entityId, HdbfTier tier, TierProfile? profile,
            float currentDay, out bool degraded)
        {
            degraded = false;
            try
            {
                if (profile is null)
                {
                    degraded = true;
                    return BareDefault(entityId, tier);
                }

                return HdbfSaveCodec.CreateDefault(entityId, tier, profile, currentDay);
            }
            catch (Exception)
            {
                degraded = true;
                return BareDefault(entityId, tier);
            }
        }

        /// <summary>
        /// The last-resort record, used only when even the codec cannot produce a default. It is
        /// still structurally valid so gameplay can run the building in its safe state.
        /// </summary>
        private static HdbfBuildingRecord BareDefault(long entityId, HdbfTier tier)
        {
            return new HdbfBuildingRecord
            {
                SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion,
                EntityId = entityId,
                Tier = Enum.IsDefined(typeof(HdbfTier), tier) ? tier : HdbfTier.Basic,
                Mode = HdbfMode.Normal,
                PlotIds = Array.Empty<int>(),
                CropIds = Array.Empty<string>(),
                Fatigue = Array.Empty<float>(),
                ExcludedPlotIds = Array.Empty<int>(),
                DueDay = 0f,
                CyclesMissed = 0,
                TankWater = 0f,
                PortPower = false,
                PortPipe = false,
                PortLogistics = false,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal),
            };
        }

        private static T[] Align<T>(T[]? source, int length, T fill)
        {
            var result = new T[length];
            if (source is null)
            {
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = fill;
                }

                return result;
            }

            var count = source.Length < length ? source.Length : length;
            for (var i = 0; i < count; i++)
            {
                result[i] = source[i];
            }

            for (var i = count; i < result.Length; i++)
            {
                result[i] = fill;
            }

            return result;
        }

        /// <summary>
        /// The JSON writer must never see NaN/Infinity: those values cannot be represented and a
        /// failed numeric conversion would take down the surrounding save entry. Saves are written
        /// from repaired records, so this is a belt-and-braces guard (REQ-SAVE-13).
        /// </summary>
        private static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static void Warn(Action<string>? warn, string reason)
        {
            if (warn is null)
            {
                return;
            }

            try
            {
                warn(reason);
            }
            catch (Exception)
            {
                // Diagnostics must never break a load (REQ-SAFE-01).
            }
        }
    }
}

