using System;
using System.Globalization;
using HDBF.Core.Json;

namespace HDBF.Core.Config
{
    /// <summary>
    /// Low-level, total JSON readers of the configuration codec (REQ-V2-SAFE-06). Every method
    /// either returns a usable value or the documented fallback and reports the reason through
    /// the caller's <c>warn</c> callback; none of them throws for player input.
    /// </summary>
    internal static class HdbfConfigReaders
    {
        /// <summary>Reads an integer property, clamped to [min,max]; fallback when absent/wrong type.</summary>
        public static int ReadInt(JsonValue? parent, string key, int fallback, int min, int max,
            string path, Action<string>? warn)
        {
            if (parent is null || !parent.TryGetProperty(key, out var node))
            {
                return fallback;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw)
                || Math.Floor(raw) != raw)
            {
                warn?.Invoke("config.invalid: " + path + " is not an integer; default "
                             + fallback.ToString(CultureInfo.InvariantCulture) + " used");
                return fallback;
            }

            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return (int)raw;
        }

        /// <summary>Reads an optional integer override, clamped to [min,max]; null when absent.</summary>
        public static int? ReadNullableInt(JsonValue parent, string key, int min, int max,
            string path, Action<string>? warn)
        {
            if (!parent.TryGetProperty(key, out var node) || node.Kind == JsonValueKind.Null)
            {
                return null;
            }

            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw)
                || Math.Floor(raw) != raw)
            {
                warn?.Invoke("config.invalid: " + path + " is not an integer; tier default kept");
                return null;
            }

            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return (int)raw;
        }

        /// <summary>Reads a float property, clamped to [min,max]; fallback when absent/wrong type.</summary>
        public static float ReadFloat(JsonValue? parent, string key, float fallback, float min, float max,
            string path, Action<string>? warn)
        {
            if (parent is null || !parent.TryGetProperty(key, out var node))
            {
                return fallback;
            }

            return ReadFiniteFloat(node, key, fallback, min, max, path, warn, "default "
                + JsonTextWriter.FormatFloat(fallback) + " used");
        }

        /// <summary>Reads an optional float override, clamped to [min,max]; null when absent.</summary>
        public static float? ReadNullableFloat(JsonValue parent, string key, float min, float max,
            string path, Action<string>? warn)
        {
            if (!parent.TryGetProperty(key, out var node) || node.Kind == JsonValueKind.Null)
            {
                return null;
            }

            var value = ReadFiniteFloat(node, key, float.NaN, min, max, path, warn, "tier default kept");
            return float.IsNaN(value) ? null : value;
        }

        /// <summary>Reads a boolean property; fallback when absent/wrong type.</summary>
        public static bool ReadBool(JsonValue? parent, string key, bool fallback, string path,
            Action<string>? warn)
        {
            if (parent is null || !parent.TryGetProperty(key, out var node))
            {
                return fallback;
            }

            if (!node.TryGetBoolean(out var value))
            {
                warn?.Invoke("config.invalid: " + path + " is not a boolean; default "
                             + (fallback ? "true" : "false") + " used");
                return fallback;
            }

            return value;
        }

        /// <summary>Reads one named optional object section; a wrong type warns and means "absent".</summary>
        public static JsonValue? GetObject(JsonValue parent, string key, string path, Action<string>? warn)
        {
            if (!parent.TryGetProperty(key, out var node))
            {
                return null;
            }

            if (node.Kind != JsonValueKind.Object)
            {
                warn?.Invoke("config.invalid: " + path + " is not an object; section defaults used");
                return null;
            }

            return node;
        }

        /// <summary>Formats the one clamp message shape the L1 suite and support logs key on.</summary>
        public static string ClampMessage(string path, double raw, double min, double max, double clamped)
        {
            return "config.clamp: " + path + "=" + raw.ToString("G9", CultureInfo.InvariantCulture)
                   + " outside [" + min.ToString("G9", CultureInfo.InvariantCulture) + ", "
                   + max.ToString("G9", CultureInfo.InvariantCulture) + "]; clamped to "
                   + clamped.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>Clamps one optional resolved tier integer override, reporting every clamp.</summary>
        public static int ResolveInt(int? value, int fallback, int min, int max, string path,
            Action<string>? warn)
        {
            if (!value.HasValue)
            {
                return fallback;
            }

            var raw = value.Value;
            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return raw;
        }

        /// <summary>Clamps one optional resolved tier float override, reporting every clamp.</summary>
        public static float ResolveFloat(float? value, float fallback, float min, float max, string path,
            Action<string>? warn)
        {
            if (!value.HasValue)
            {
                return fallback;
            }

            var raw = value.Value;
            if (float.IsNaN(raw) || float.IsInfinity(raw))
            {
                warn?.Invoke("config.invalid: " + path + " is not a finite number; default "
                             + JsonTextWriter.FormatFloat(fallback) + " used");
                return fallback;
            }

            if (raw < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (raw > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return raw;
        }

        private static float ReadFiniteFloat(JsonValue node, string key, float fallback, float min,
            float max, string path, Action<string>? warn, string fallbackReason)
        {
            if (!node.TryGetDouble(out var raw) || double.IsNaN(raw) || double.IsInfinity(raw))
            {
                warn?.Invoke("config.invalid: " + path + " is not a number; " + fallbackReason);
                return fallback;
            }

            var numeric = (float)raw;
            if (float.IsNaN(numeric) || float.IsInfinity(numeric))
            {
                warn?.Invoke("config.invalid: " + path + " is out of float range; " + fallbackReason);
                return fallback;
            }

            if (numeric < min)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, min));
                return min;
            }

            if (numeric > max)
            {
                warn?.Invoke(ClampMessage(path, raw, min, max, max));
                return max;
            }

            return numeric;
        }
    }
}
