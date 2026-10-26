using System;
using System.Collections.Generic;

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

        /// <summary>Property names of an object node, in document order; empty for non-objects.</summary>
        public IReadOnlyList<string> PropertyNames
        {
            get
            {
                if (_members is null || _members.Count == 0)
                {
                    return Array.Empty<string>();
                }

                var names = new List<string>(_members.Count);
                foreach (var pair in _members)
                {
                    names.Add(pair.Key);
                }

                return names;
            }
        }

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
}