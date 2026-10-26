// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-02
// Purpose: Internal text/number primitives of the schema-v2 save: deterministic float
//          text, JSON string escaping, tolerant field readers and the fixed-order field
//          writers used by IndoorSaveCodec. No save policy and no enum names here.
// Requirements: REQ-V2-SAVE-01, REQ-V2-SAVE-06, REQ-V2-EXT-03
// ============================================================================
using System;
using System.Globalization;
using System.Text;
using HDBF.Core.Json;

namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Encoding primitives of the schema-v2 save text (REQ-V2-SAVE-01). Everything is pure and
    /// deterministic: the same value always produces the same text, independent of culture,
    /// machine and insertion order.
    /// </summary>
    internal static class IndoorSaveFormat
    {
        /// <summary>
        /// Save precision of every persisted float: three fraction digits, trailing zeros trimmed
        /// (section 19.10 "浮点按存档精度"). Precision loss is at most 5e-4, far below the simulation
        /// interest of growth days, fatigue and water units, and it keeps the 600-slot payload
        /// inside the section-19.10 size band without a binary format.
        /// </summary>
        private const string FloatFormat = "0.###";

        private const string TrueText = "true";
        private const string FalseText = "false";

        // ---------------------------------------------------------------- writing

        /// <summary>Formats a persisted float; NaN/Infinity become <c>0</c> (the validator repairs them before write).</summary>
        internal static string FormatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "0";
            }

            var text = value.ToString(FloatFormat, CultureInfo.InvariantCulture);
            return text == "-0" ? "0" : text;
        }

        /// <summary>Appends one JSON string literal; null is written as <c>null</c>.</summary>
        internal static void AppendQuoted(StringBuilder builder, string? value)
        {
            if (builder is null)
            {
                return;
            }

            if (value is null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        /// <summary>Starts one fixed-order object member; <paramref name="first"/> decides the comma.</summary>
        internal static void StartField(StringBuilder builder, string name, bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }

            AppendQuoted(builder, name);
            builder.Append(':');
        }

        /// <summary>Appends one string member.</summary>
        internal static void AppendString(StringBuilder builder, string name, string? value, bool first)
        {
            StartField(builder, name, first);
            AppendQuoted(builder, value);
        }

        /// <summary>Appends one integer member.</summary>
        internal static void AppendInt(StringBuilder builder, string name, int value, bool first)
        {
            StartField(builder, name, first);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Appends one float member using <see cref="FormatFloat"/>.</summary>
        internal static void AppendFloat(StringBuilder builder, string name, float value, bool first)
        {
            StartField(builder, name, first);
            builder.Append(FormatFloat(value));
        }

        /// <summary>Appends one boolean member.</summary>
        internal static void AppendBool(StringBuilder builder, string name, bool value, bool first)
        {
            StartField(builder, name, first);
            builder.Append(value ? TrueText : FalseText);
        }

        // ---------------------------------------------------------------- reading

        /// <summary>True when the member exists and its parent is a JSON object.</summary>
        internal static bool TryGetMember(JsonValue? parent, string name, out JsonValue value)
        {
            if (parent is not null && parent.Kind == JsonValueKind.Object && parent.TryGetProperty(name, out value))
            {
                return true;
            }

            value = JsonValue.Null;
            return false;
        }

        /// <summary>Reads a string member; false for a missing member or a wrong-typed value.</summary>
        internal static bool TryReadString(JsonValue? parent, string name, out string value)
        {
            if (TryGetMember(parent, name, out var member) && member.TryGetString(out value))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        /// <summary>Reads a boolean member; false for a missing member or a wrong-typed value.</summary>
        internal static bool TryReadBool(JsonValue? parent, string name, out bool value)
        {
            if (TryGetMember(parent, name, out var member) && member.TryGetBoolean(out value))
            {
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>Reads a numeric member as double; false for a missing member or a wrong-typed value.</summary>
        internal static bool TryReadDouble(JsonValue? parent, string name, out double value)
        {
            if (TryGetMember(parent, name, out var member) && member.TryGetDouble(out value))
            {
                return true;
            }

            value = 0d;
            return false;
        }

        /// <summary>Reads a numeric member as float; the cast may saturate to Infinity, the validator clamps it.</summary>
        internal static bool TryReadFloat(JsonValue? parent, string name, out float value)
        {
            if (!TryReadDouble(parent, name, out var number))
            {
                value = 0f;
                return false;
            }

            value = (float)number;
            return true;
        }

        /// <summary>Reads a numeric member as int; fractional values truncate and out-of-range values saturate.</summary>
        internal static bool TryReadInt(JsonValue? parent, string name, out int value)
        {
            if (!TryReadDouble(parent, name, out var number))
            {
                value = 0;
                return false;
            }

            value = ToInt(number);
            return true;
        }

        /// <summary>Truncating, saturating double-to-int conversion for untrusted JSON numbers.</summary>
        internal static int ToInt(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (value <= int.MinValue)
            {
                return int.MinValue;
            }

            return (int)value;
        }
    }
}
