using System;
using System.Collections.Generic;
using System.Globalization;

namespace HDBF.Core.Json
{
    /// <summary>Kind of a parsed JSON value (the subset HDBF needs).</summary>
    internal enum JsonValueKind
    {
        Null = 0,
        Object = 1,
        Array = 2,
        String = 3,
        Number = 4,
        Boolean = 5,
    }

    /// <summary>
    /// Immutable JSON value produced by <see cref="JsonParser"/>.
    ///
    /// Only what configuration and diagnostics need is modelled: null, object, array, string,
    /// number and boolean. Errors are never represented as exceptions here; parsing reports
    /// success/failure and the config codec decides the conservative fallback (chapter 10.3).
    /// </summary>
    internal sealed class JsonValue
    {
        private Dictionary<string, JsonValue>? _members;
        private List<JsonValue>? _items;
        private string? _text;
        private double _number;
        private bool _boolean;

        private JsonValue(JsonValueKind kind)
        {
            Kind = kind;
        }

        public JsonValueKind Kind { get; }

        public static JsonValue Null { get; } = new JsonValue(JsonValueKind.Null);

        public static JsonValue FromString(string value) => new JsonValue(JsonValueKind.String) { _text = value };

        public static JsonValue FromNumber(double value) => new JsonValue(JsonValueKind.Number) { _number = value };

        public static JsonValue FromBoolean(bool value) => new JsonValue(JsonValueKind.Boolean) { _boolean = value };

        public static JsonValue FromObject(Dictionary<string, JsonValue> members) =>
            new JsonValue(JsonValueKind.Object) { _members = members };

        public static JsonValue FromArray(List<JsonValue> items) => new JsonValue(JsonValueKind.Array) { _items = items };

        public IReadOnlyList<JsonValue> Items => _items ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();

        public int Count => _members?.Count ?? _items?.Count ?? 0;

        public bool TryGetProperty(string name, out JsonValue value)
        {
            if (_members is not null && _members.TryGetValue(name, out var found))
            {
                value = found;
                return true;
            }

            value = Null;
            return false;
        }

        public bool TryGetString(out string value)
        {
            if (Kind == JsonValueKind.String && _text is not null)
            {
                value = _text;
                return true;
            }

            value = string.Empty;
            return false;
        }

        public bool TryGetDouble(out double value)
        {
            if (Kind == JsonValueKind.Number)
            {
                value = _number;
                return true;
            }

            value = 0d;
            return false;
        }

        public bool TryGetBoolean(out bool value)
        {
            if (Kind == JsonValueKind.Boolean)
            {
                value = _boolean;
                return true;
            }

            value = false;
            return false;
        }
    }

    /// <summary>
    /// Hand-written non-throwing JSON parser (RFC 8259 subset) for untrusted player config.
    ///
    /// Why hand-written: HDBF.Core may not reference Newtonsoft/System.Text.Json (REQ-ARCH-01,
    /// no NuGet on the build machine), and the config loader needs a total function that returns
    /// defaults instead of throwing on garbage (chapter 10.3, REQ-SAFE-10).
    /// </summary>
    internal static class JsonParser
    {
        /// <summary>Nesting limit that keeps malicious input from exhausting the stack.</summary>
        private const int MaxDepth = 64;

        /// <summary>
        /// Parses <paramref name="text"/>; returns false for any malformed or over-deep input.
        /// Never throws.
        /// </summary>
        public static bool TryParse(string? text, out JsonValue? value)
        {
            value = null;
            if (text is null)
            {
                return false;
            }

            try
            {
                var parser = new Parser(text);
                var parsed = parser.ParseValue(0);
                parser.SkipWhitespace();
                if (parser.AtEnd && parsed is not null)
                {
                    value = parsed;
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                // Total function by contract: bad input is reported, never thrown (REQ-SAFE-10).
                value = null;
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text;
            }

            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    var c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }
            }

            public JsonValue? ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    return null;
                }

                SkipWhitespace();
                if (AtEnd)
                {
                    return null;
                }

                var c = _text[_index];
                switch (c)
                {
                    case '{':
                        return ParseObject(depth);
                    case '[':
                        return ParseArray(depth);
                    case '"':
                        return ParseString() is { } text ? JsonValue.FromString(text) : null;
                    case 't':
                        return ConsumeLiteral("true") ? JsonValue.FromBoolean(true) : null;
                    case 'f':
                        return ConsumeLiteral("false") ? JsonValue.FromBoolean(false) : null;
                    case 'n':
                        return ConsumeLiteral("null") ? JsonValue.Null : null;
                    default:
                        return ParseNumber();
                }
            }

            private JsonValue? ParseObject(int depth)
            {
                _index++; // '{'
                var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}')
                {
                    _index++;
                    return JsonValue.FromObject(members);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"')
                    {
                        return null;
                    }

                    var name = ParseString();
                    if (name is null)
                    {
                        return null;
                    }

                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':')
                    {
                        return null;
                    }

                    _index++;
                    var member = ParseValue(depth + 1);
                    if (member is null)
                    {
                        return null;
                    }

                    members[name] = member;

                    SkipWhitespace();
                    if (AtEnd)
                    {
                        return null;
                    }

                    if (_text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (_text[_index] == '}')
                    {
                        _index++;
                        return JsonValue.FromObject(members);
                    }

                    return null;
                }
            }

            private JsonValue? ParseArray(int depth)
            {
                _index++; // '['
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']')
                {
                    _index++;
                    return JsonValue.FromArray(items);
                }

                while (true)
                {
                    var item = ParseValue(depth + 1);
                    if (item is null)
                    {
                        return null;
                    }

                    items.Add(item);
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        return null;
                    }

                    if (_text[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    if (_text[_index] == ']')
                    {
                        _index++;
                        return JsonValue.FromArray(items);
                    }

                    return null;
                }
            }

            private string? ParseString()
            {
                _index++; // opening quote
                var builder = new System.Text.StringBuilder();
                while (_index < _text.Length)
                {
                    var c = _text[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        return null;
                    }

                    var escape = _text[_index++];
                    switch (escape)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                            {
                                return null;
                            }

                            if (!int.TryParse(_text.Substring(_index, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out var code))
                            {
                                return null;
                            }

                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            return null;
                    }
                }

                return null;
            }

            private JsonValue? ParseNumber()
            {
                var start = _index;
                if (!AtEnd && (_text[_index] == '-' || _text[_index] == '+'))
                {
                    _index++;
                }

                while (_index < _text.Length)
                {
                    var c = _text[_index];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }

                if (start == _index)
                {
                    return null;
                }

                var token = _text.Substring(start, _index - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return null;
                }

                return JsonValue.FromNumber(number);
            }

            private bool ConsumeLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length)
                {
                    return false;
                }

                if (string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    return false;
                }

                _index += literal.Length;
                return true;
            }
        }
    }
}
