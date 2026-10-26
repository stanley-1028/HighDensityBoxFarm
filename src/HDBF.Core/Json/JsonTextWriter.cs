using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HDBF.Core.Json
{
    /// <summary>
    /// Minimal deterministic JSON writer used by Core diagnostics and configuration output.
    ///
    /// HDBF.Core must not depend on Newtonsoft.Json or System.Text.Json (REQ-ARCH-01: no
    /// game/third-party references in Core, and no NuGet packages at all on this build machine),
    /// so the handful of documents Core emits are written by hand. Json.Net is used only by the
    /// game-facing assemblies when they talk to Timberborn's persistence layer.
    ///
    /// Output rules: two-space indentation, property order exactly as written, invariant number
    /// formatting with round-trippable float text, sorted nothing (callers control order).
    /// </summary>
    internal sealed class JsonTextWriter
    {
        private sealed class Container
        {
            public bool HasElements;
            public char ClosingChar;
            public int Depth;
        }

        private readonly StringBuilder _builder = new StringBuilder(256);
        private readonly Stack<Container> _containers = new Stack<Container>();
        private bool _valuePending;

        /// <summary>Starts an object value.</summary>
        public JsonTextWriter StartObject()
        {
            BeforeValue();
            _builder.Append('{');
            Push('}');
            return this;
        }

        /// <summary>Closes the current object.</summary>
        public JsonTextWriter EndObject()
        {
            Pop('}');
            return this;
        }

        /// <summary>Starts an array value.</summary>
        public JsonTextWriter StartArray()
        {
            BeforeValue();
            _builder.Append('[');
            Push(']');
            return this;
        }

        /// <summary>Closes the current array.</summary>
        public JsonTextWriter EndArray()
        {
            Pop(']');
            return this;
        }

        /// <summary>Writes a property name; the next value call supplies the property value.</summary>
        public JsonTextWriter Property(string name)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            BeforeValue();
            AppendQuoted(name);
            _builder.Append(": ");
            _valuePending = true;
            return this;
        }

        /// <summary>Writes a string value; null becomes JSON <c>null</c>.</summary>
        public JsonTextWriter StringValue(string? value)
        {
            BeforeValue();
            if (value is null)
            {
                _builder.Append("null");
            }
            else
            {
                AppendQuoted(value);
            }

            return this;
        }

        /// <summary>Writes an integer value.</summary>
        public JsonTextWriter IntegerValue(int value)
        {
            BeforeValue();
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>Writes a float using the round-trippable invariant format; NaN/Infinity become 0.</summary>
        public JsonTextWriter FloatValue(float value)
        {
            BeforeValue();
            _builder.Append(FormatFloat(value));
            return this;
        }

        /// <summary>Writes a boolean value.</summary>
        public JsonTextWriter BooleanValue(bool value)
        {
            BeforeValue();
            _builder.Append(value ? "true" : "false");
            return this;
        }

        /// <summary>Writes an explicit JSON null.</summary>
        public JsonTextWriter NullValue()
        {
            BeforeValue();
            _builder.Append("null");
            return this;
        }

        /// <summary>Writes a JSON array of strings, inline for compact report fields.</summary>
        public JsonTextWriter StringArray(IReadOnlyList<string>? values)
        {
            if (values is null || values.Count == 0)
            {
                return StartArray().EndArray();
            }

            BeforeValue();
            _builder.Append('[');
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    _builder.Append(", ");
                }

                AppendQuoted(values[i] ?? string.Empty);
            }

            _builder.Append(']');
            return this;
        }

        /// <summary>Round-trippable invariant float text shared with the config writer.</summary>
        public static string FormatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "0";
            }

            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override string ToString() => _builder.ToString();

        private void Push(char closingChar)
        {
            var depth = _containers.Count + 1;
            _containers.Push(new Container { ClosingChar = closingChar, Depth = depth });
        }

        private void Pop(char expectedClosingChar)
        {
            if (_containers.Count == 0)
            {
                throw new InvalidOperationException("unbalanced JSON container close");
            }

            var container = _containers.Pop();
            if (container.ClosingChar != expectedClosingChar)
            {
                throw new InvalidOperationException("mismatched JSON container close");
            }

            if (container.HasElements)
            {
                _builder.AppendLine();
                AppendIndent(container.Depth - 1);
            }

            _builder.Append(expectedClosingChar);
        }

        private void BeforeValue()
        {
            if (_valuePending)
            {
                _valuePending = false;
                return;
            }

            if (_containers.Count == 0)
            {
                return;
            }

            var container = _containers.Peek();
            if (container.HasElements)
            {
                _builder.Append(',');
            }

            _builder.AppendLine();
            AppendIndent(container.Depth);
            container.HasElements = true;
        }

        private void AppendIndent(int depth)
        {
            _builder.Append(' ', depth * 2);
        }

        private void AppendQuoted(string value)
        {
            _builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"':
                        _builder.Append("\\\"");
                        break;
                    case '\\':
                        _builder.Append("\\\\");
                        break;
                    case '\n':
                        _builder.Append("\\n");
                        break;
                    case '\r':
                        _builder.Append("\\r");
                        break;
                    case '\t':
                        _builder.Append("\\t");
                        break;
                    case '\b':
                        _builder.Append("\\b");
                        break;
                    case '\f':
                        _builder.Append("\\f");
                        break;
                    default:
                        if (c < ' ')
                        {
                            _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _builder.Append(c);
                        }

                        break;
                }
            }

            _builder.Append('"');
        }
    }
}
