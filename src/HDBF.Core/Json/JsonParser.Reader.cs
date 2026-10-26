using System;
using System.Collections.Generic;
using System.Globalization;

namespace HDBF.Core.Json
{
    internal static partial class JsonParser
    {
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