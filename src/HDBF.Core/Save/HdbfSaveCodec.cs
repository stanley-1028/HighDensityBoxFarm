using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HDBF.Core.Balance;

namespace HDBF.Core.Save
{
    /// <summary>
    /// Persisted state of one box farm. Arrays are parallel and equal length, which keeps the
    /// save compact (chapter 12.7) and makes damaged-record repair mechanical.
    ///
    /// Only non-reconstructible state belongs here (REQ-SAVE-02): coverage, power availability
    /// and per-worker assignments are recomputed on load and must never be persisted.
    /// </summary>
    public sealed class HdbfBuildingRecord
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public long EntityId { get; set; }

        public HdbfTier Tier { get; set; }

        public HdbfMode Mode { get; set; } = HdbfMode.Normal;

        public int[] PlotIds { get; set; } = Array.Empty<int>();

        public string[] CropIds { get; set; } = Array.Empty<string>();

        public float[] Fatigue { get; set; } = Array.Empty<float>();

        public int[] ExcludedPlotIds { get; set; } = Array.Empty<int>();

        public float DueDay { get; set; }

        public int CyclesMissed { get; set; }

        public float TankWater { get; set; }

        public bool PortPower { get; set; }

        public bool PortPipe { get; set; }

        public bool PortLogistics { get; set; }

        public Dictionary<string, string> Adapters { get; set; } = new();

        /// <summary>Number of registered plots; arrays are guaranteed equal length after sanitising.</summary>
        public int PlotCount => PlotIds.Length;
    }

    /// <summary>
    /// Repairs, migrates and canonicalises building records.
    ///
    /// Design rules (chapters 10.2, 12.3, 12.4):
    /// * repair is conservative: clamp and truncate, never invent production;
    /// * migration is idempotent and pure (returns a copy);
    /// * nothing here throws for bad data — the caller decides whether to log.
    /// </summary>
    public static class HdbfSaveCodec
    {
        private static readonly string[] EmptyCrops = Array.Empty<string>();

        public static HdbfBuildingRecord CreateDefault(long entityId, HdbfTier tier, TierProfile profile, float currentDay)
        {
            return new HdbfBuildingRecord
            {
                SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion,
                EntityId = entityId,
                Tier = tier,
                Mode = HdbfMode.Normal,
                PlotIds = Array.Empty<int>(),
                CropIds = Array.Empty<string>(),
                Fatigue = Array.Empty<float>(),
                ExcludedPlotIds = Array.Empty<int>(),
                // Single source for the maintenance cycle (chapter 6.5): the top tier has no
                // on-grid cycle but a defined off-grid one, so the safe default must come from
                // MaintenanceDefaults, never from a bare profile.MaintenanceCycleDays (BUG-06:
                // the top tier used to schedule its first cycle at currentDay + 0 = today).
                DueDay = MaintenanceDefaults.ResolveDueDay(profile, currentDay, 0f),
                CyclesMissed = 0,
                TankWater = 0f,
                PortPower = false,
                PortPipe = false,
                PortLogistics = false,
                Adapters = new Dictionary<string, string>(StringComparer.Ordinal),
            };
        }

        /// <summary>
        /// Validates one record and returns a repaired copy. Returns false only when the record
        /// was unusable and had to be rebuilt from defaults; the repaired value is always usable
        /// (REQ-SAFE-08: isolate the damaged building, keep the rest of the save intact).
        /// </summary>
        public static bool TrySanitize(HdbfBuildingRecord? record, TierProfile profile, float currentDay,
            out HdbfBuildingRecord repaired, out string? reason)
        {
            reason = null;
            if (record is null)
            {
                repaired = CreateDefault(0, profile.Tier, profile, currentDay);
                reason = "record-missing";
                return false;
            }

            var degraded = false;
            var copy = new HdbfBuildingRecord
            {
                SchemaVersion = record.SchemaVersion,
                EntityId = record.EntityId,
                Tier = record.Tier,
                Mode = record.Mode,
                PlotIds = record.PlotIds ?? Array.Empty<int>(),
                CropIds = record.CropIds ?? Array.Empty<string>(),
                Fatigue = record.Fatigue ?? Array.Empty<float>(),
                DueDay = record.DueDay,
                CyclesMissed = record.CyclesMissed,
                TankWater = record.TankWater,
                PortPower = record.PortPower,
                PortPipe = record.PortPipe,
                PortLogistics = record.PortLogistics,
                ExcludedPlotIds = record.ExcludedPlotIds ?? Array.Empty<int>(),
                Adapters = record.Adapters ?? new Dictionary<string, string>(StringComparer.Ordinal),
            };

            // Unknown/negative stamps are repaired to the prototype v0 stamp so the migration
            // chain below can fill v1 defaults (a pre-v1 record has no maintenance date yet).
            // Only stamps from the future are clamped straight to the current schema: those are
            // opened read-only by the caller (chapter 9.4) and must not be "migrated" here.
            if (copy.SchemaVersion > HdbfBuildingRecord.CurrentSchemaVersion + 1)
            {
                degraded = true;
                reason ??= "schema-out-of-range";
                copy.SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion;
            }
            else if (copy.SchemaVersion < 0)
            {
                degraded = true;
                reason ??= "schema-out-of-range";
                copy.SchemaVersion = 0;
            }

            if (!Enum.IsDefined(typeof(HdbfTier), copy.Tier))
            {
                degraded = true;
                reason ??= "tier-invalid";
                copy.Tier = profile.Tier;
            }

            if (!Enum.IsDefined(typeof(HdbfMode), copy.Mode))
            {
                degraded = true;
                reason ??= "mode-invalid";
                copy.Mode = HdbfMode.Normal;
            }

            // A maintenance date is unusable when it is non-finite, or when a material-maintenance
            // tier carries a non-positive value ("never scheduled" after a missing field, or a
            // damaged record). v1+ records are rescheduled from the single-source default; v0
            // records keep the value so Migrate can fill the v1 default exactly once without
            // degrading a structurally complete v0 record (REQ-SAVE-05/06, BUG-06).
            var dueDayNotFinite = float.IsNaN(copy.DueDay) || float.IsInfinity(copy.DueDay);
            var dueDayReschedulable = copy.SchemaVersion >= HdbfBuildingRecord.CurrentSchemaVersion
                && !MaintenanceDefaults.IsUsableDueDay(profile, copy.DueDay);
            if (dueDayNotFinite || dueDayReschedulable)
            {
                degraded = true;
                reason ??= "due-day-invalid";
                copy.DueDay = MaintenanceDefaults.ResolveDueDay(profile, currentDay, copy.DueDay);
                copy.CyclesMissed = 0;
            }

            if (copy.CyclesMissed < 0)
            {
                degraded = true;
                reason ??= "cycles-missed-invalid";
                copy.CyclesMissed = 0;
            }

            if (float.IsNaN(copy.TankWater) || copy.TankWater < 0f || float.IsInfinity(copy.TankWater))
            {
                degraded = true;
                reason ??= "tank-invalid";
                copy.TankWater = 0f;
            }

            if (copy.TankWater > profile.TankCapacity)
            {
                degraded = true;
                reason ??= "tank-over-capacity";
                copy.TankWater = profile.TankCapacity;
            }

            var ids = copy.PlotIds;
            var crops = copy.CropIds;
            var fatigue = copy.Fatigue;

            var count = ids.Length;
            if (crops.Length != count)
            {
                degraded = true;
                reason ??= "crops-length-mismatch";
            }

            if (fatigue.Length != count)
            {
                degraded = true;
                reason ??= "fatigue-length-mismatch";
            }

            // The resolved tier capacity is the hard bound for registered plots (chapter 10.5).
            // Using the profile instead of the global maximum keeps config-driven capacities
            // (chapter 18) consistent with what the save may contain.
            var maxPlots = profile.PlotCapacity > 0 ? profile.PlotCapacity : TierCatalog.MaxPlotCapacity;
            if (count > maxPlots)
            {
                degraded = true;
                reason ??= "plot-count-over-capacity";
                count = maxPlots;
            }

            var cleanIds = new List<int>(count);
            var cleanCrops = new List<string>(count);
            var cleanFatigue = new List<float>(count);
            var seen = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var plotId = ids[i];
                if (!seen.Add(plotId))
                {
                    // A cell may hold at most one registration slot: drop the duplicate instead
                    // of double-counting it in coverage and yield.
                    degraded = true;
                    reason ??= "duplicate-plot-id";
                    continue;
                }

                cleanIds.Add(plotId);
                cleanCrops.Add(i < crops.Length && !string.IsNullOrEmpty(crops[i]) ? crops[i] : string.Empty);

                var f = i < fatigue.Length ? fatigue[i] : 0f;
                if (float.IsNaN(f) || float.IsInfinity(f) || f < 0f || f > 1f)
                {
                    degraded = true;
                    reason ??= "fatigue-out-of-range";
                    f = ClampFatigue(f);
                }

                cleanFatigue.Add(f);
            }

            copy.PlotIds = cleanIds.ToArray();
            copy.CropIds = cleanCrops.ToArray();
            copy.Fatigue = cleanFatigue.ToArray();

            var excluded = copy.ExcludedPlotIds;
            var cleanExcluded = new List<int>(excluded.Length);
            for (var i = 0; i < excluded.Length; i++)
            {
                if (!seen.Contains(excluded[i]) && !cleanExcluded.Contains(excluded[i]))
                {
                    cleanExcluded.Add(excluded[i]);
                }
            }

            if (cleanExcluded.Count != excluded.Length)
            {
                degraded = true;
                reason ??= "excluded-plot-invalid";
            }

            copy.ExcludedPlotIds = cleanExcluded.ToArray();

            var adapters = copy.Adapters;
            var cleanAdapters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in adapters)
            {
                if (!string.IsNullOrEmpty(pair.Key))
                {
                    cleanAdapters[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            copy.Adapters = cleanAdapters;

            repaired = Migrate(copy, profile, currentDay);
            return !degraded;
        }

        private static float ClampFatigue(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        /// <summary>
        /// Idempotent migration chain. v1 is the first shipped schema, so this only guarantees the
        /// version stamp; the structure exists so a v2 migration can be added without touching
        /// call sites (REQ-SAVE-05, REQ-SAVE-06).
        /// </summary>
        public static HdbfBuildingRecord Migrate(HdbfBuildingRecord record, TierProfile profile, float currentDay)
        {
            var copy = Clone(record);
            if (copy.SchemaVersion < HdbfBuildingRecord.CurrentSchemaVersion)
            {
                // v0 prototype saves stored an integer mode and had no fatigue array; the
                // sanitiser above already normalised those shapes (mode/array lengths). A v0
                // record also had no maintenance date: fill the v1 safe default from the same
                // single-source resolver as CreateDefault (BUG-06), and only when it is really
                // missing so an existing v0 due date is never moved.
                var resolvedDueDay = MaintenanceDefaults.ResolveDueDay(profile, currentDay, copy.DueDay);
                if (resolvedDueDay != copy.DueDay)
                {
                    copy.DueDay = resolvedDueDay;
                    copy.CyclesMissed = 0;
                }

                copy.SchemaVersion = HdbfBuildingRecord.CurrentSchemaVersion;
            }

            if (copy.SchemaVersion > HdbfBuildingRecord.CurrentSchemaVersion)
            {
                // Newer save than this build understands: keep the stamp, the caller opens
                // it read-only (chapter 9.4).
                copy.SchemaVersion = record.SchemaVersion;
            }

            return copy;
        }

        /// <summary>Deep copy; arrays and the adapter map are duplicated so callers cannot alias.</summary>
        public static HdbfBuildingRecord Clone(HdbfBuildingRecord record)
        {
            var copy = new HdbfBuildingRecord
            {
                SchemaVersion = record.SchemaVersion,
                EntityId = record.EntityId,
                Tier = record.Tier,
                Mode = record.Mode,
                PlotIds = (int[])record.PlotIds.Clone(),
                CropIds = (string[])record.CropIds.Clone(),
                Fatigue = (float[])record.Fatigue.Clone(),
                ExcludedPlotIds = (int[])record.ExcludedPlotIds.Clone(),
                DueDay = record.DueDay,
                CyclesMissed = record.CyclesMissed,
                TankWater = record.TankWater,
                PortPower = record.PortPower,
                PortPipe = record.PortPipe,
                PortLogistics = record.PortLogistics,
                Adapters = new Dictionary<string, string>(record.Adapters, StringComparer.Ordinal),
            };
            return copy;
        }

        /// <summary>
        /// Canonical text form with stable key order and 3 decimal places. Used for save diffing,
        /// corruption tests and checksum-style equality checks (chapter 12.4 rules).
        /// </summary>
        public static string Canonicalize(HdbfBuildingRecord record)
        {
            var sb = new StringBuilder(256);
            sb.Append("schema=").Append(record.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(";entity=").Append(record.EntityId.ToString(CultureInfo.InvariantCulture));
            sb.Append(";tier=").Append(((int)record.Tier).ToString(CultureInfo.InvariantCulture));
            sb.Append(";mode=").Append(((int)record.Mode).ToString(CultureInfo.InvariantCulture));
            sb.Append(";ids=");
            AppendInts(sb, record.PlotIds);
            sb.Append(";crops=");
            for (var i = 0; i < record.CropIds.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(record.CropIds[i]);
            }

            sb.Append(";fatigue=");
            for (var i = 0; i < record.Fatigue.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(record.Fatigue[i].ToString("F3", CultureInfo.InvariantCulture));
            }

            sb.Append(";excluded=");
            AppendInts(sb, record.ExcludedPlotIds);
            sb.Append(";due=").Append(record.DueDay.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(";missed=").Append(record.CyclesMissed.ToString(CultureInfo.InvariantCulture));
            sb.Append(";tank=").Append(record.TankWater.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(";ports=")
                .Append(record.PortPower ? '1' : '0')
                .Append(record.PortPipe ? '1' : '0')
                .Append(record.PortLogistics ? '1' : '0');

            sb.Append(";adapters=");
            var keys = new List<string>(record.Adapters.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(keys[i]).Append('=').Append(record.Adapters[keys[i]]);
            }

            return sb.ToString();
        }

        private static void AppendInts(StringBuilder sb, int[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Empty crop array constant reused by callers that clear crops.</summary>
        public static string[] NoCrops() => (string[])EmptyCrops.Clone();
    }
}
