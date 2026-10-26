using System.Text.Json;
using Mono.Cecil;

namespace HDBF.Tools.Validate;

/// <summary>
/// Validates `.blueprint.json` documents against the metadata of the installed game plus the
/// mod's own built assemblies:
///
/// * every top-level key is either a real public <c>ComponentSpec</c> subclass, a mod spec
///   (e.g. <c>HdbfBoxFarmSpec</c>) or the reserved <c>Children</c> block;
/// * every field inside a spec exists on that spec type;
/// * array item types are validated recursively (blocks, costs, colliders, ...);
/// * enum values are real enum member names (flags are split on commas);
/// * modifier suffixes (<c>#append</c>, <c>#remove</c>, <c>#replace</c>) are resolved to the
///   underlying property;
/// * every localization key referenced by the JSON is collected for the CSV cross-check.
/// </summary>
internal sealed class BlueprintValidator
{
    private static readonly HashSet<string> ReservedChildKeys =
        new(StringComparer.Ordinal) { "Children", "BlueprintPath", "Modification" };

    private static readonly HashSet<string> UnityVectorKeys =
        new(StringComparer.OrdinalIgnoreCase) { "X", "Y", "Z", "W" };

    private readonly GameMetadata _metadata;
    private readonly Report _report;
    private readonly SortedSet<string> _usedLocKeys = new(StringComparer.Ordinal);

    public BlueprintValidator(GameMetadata metadata, Report report)
    {
        _metadata = metadata;
        _report = report;
    }

    public IReadOnlyCollection<string> UsedLocKeys => _usedLocKeys;

    public void ValidateFile(string filePath, string relativePath)
    {
        JsonDocument document;
        try
        {
            var text = File.ReadAllText(filePath);
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException ex)
        {
            _report.Error("blueprint", $"{relativePath}: invalid JSON: {ex.Message}");
            return;
        }

        using (document)
        {
            CollectLocKeys(document.RootElement, relativePath);
            ValidateBlueprint(document.RootElement, relativePath, string.Empty, nested: false);
        }
    }

    private void ValidateBlueprint(JsonElement blueprint, string relativePath, string location, bool nested)
    {
        if (blueprint.ValueKind != JsonValueKind.Object)
        {
            _report.Error("blueprint", $"{At(relativePath, location)}: a blueprint must be a JSON object");
            return;
        }

        foreach (var property in blueprint.EnumerateObject())
        {
            var name = property.Name;

            if (name == "Children")
            {
                ValidateChildren(property.Value, relativePath, Join(location, "Children"));
                continue;
            }

            if (ReservedChildKeys.Contains(name))
            {
                if (!nested)
                {
                    _report.Error("blueprint",
                        $"{At(relativePath, location)}: '{name}' is only valid in a child blueprint");
                }
                else if (name == "BlueprintPath")
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        _report.Error("blueprint", $"{At(relativePath, location)}.BlueprintPath must be a string");
                    }
                }
                else if (name == "Modification")
                {
                    ValidateBlueprint(property.Value, relativePath, Join(location, "Modification"), nested: true);
                }

                continue;
            }

            if (!_metadata.IsKnownSpecName(name))
            {
                _report.Error("spec", $"{At(relativePath, location)}: unknown spec '{name}' "
                                       + "(not a ComponentSpec subclass in the installed game or a built mod assembly)");
                continue;
            }

            ValidateSpec(name, property.Value, relativePath, Join(location, name));
        }
    }

    private void ValidateChildren(JsonElement children, string relativePath, string location)
    {
        if (children.ValueKind != JsonValueKind.Object)
        {
            _report.Error("blueprint", $"{At(relativePath, location)} must be an object");
            return;
        }

        foreach (var child in children.EnumerateObject())
        {
            ValidateBlueprint(child.Value, relativePath, Join(location, child.Name), nested: true);
        }
    }

    private void ValidateSpec(string specName, JsonElement value, string relativePath, string location)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            _report.Error("spec", $"{At(relativePath, location)} must be an object");
            return;
        }

        if (!_metadata.TryGetSpec(specName, out var definition))
        {
            return;
        }

        var members = _metadata.GetMembers(definition);

        foreach (var property in value.EnumerateObject())
        {
            var baseName = StripModifier(property.Name);

            if (!members.TryGetValue(baseName, out var member))
            {
                _report.Error("spec",
                    $"{At(relativePath, location)}: '{property.Name}' is not a serialized member of {specName}");
                continue;
            }

            ValidateValue(member.Type, property.Value, relativePath, Join(location, property.Name), depth: 0);
        }
    }

    private void ValidateValue(TypeReference type, JsonElement value, string relativePath, string location, int depth)
    {
        if (depth > 12 || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (GameMetadata.IsCollection(type, out var elementType))
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                // Asset-reference arrays and similar opaque wrappers are serialized as strings or
                // objects by custom deserializers; only police arrays when the JSON really is one.
                return;
            }

            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateValue(elementType, item, relativePath, location + "[" + index + "]", depth + 1);
                index++;
            }

            return;
        }

        if (GameMetadata.TryGetPrimitiveKind(type, out var kind))
        {
            if (kind == PrimitiveKind.Boolean)
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    _report.Error("spec", $"{At(relativePath, location)}: expected a boolean but found {value.ValueKind}");
                }

                return;
            }

            var expected = kind switch
            {
                PrimitiveKind.String => JsonValueKind.String,
                PrimitiveKind.Number => JsonValueKind.Number,
                _ => JsonValueKind.Undefined,
            };

            if (expected != JsonValueKind.Undefined && value.ValueKind != expected)
            {
                _report.Error("spec",
                    $"{At(relativePath, location)}: expected {expected} but found {value.ValueKind} ({type.Name})");
            }

            return;
        }

        if (GameMetadata.IsUnityVector(type))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var axis in value.EnumerateObject())
            {
                if (!UnityVectorKeys.Contains(axis.Name))
                {
                    _report.Error("spec", $"{At(relativePath, location)}: unexpected vector component '{axis.Name}'");
                }
                else if (axis.Value.ValueKind != JsonValueKind.Number)
                {
                    _report.Error("spec", $"{At(relativePath, location)}.{axis.Name}: expected a number");
                }
            }

            return;
        }

        var definition = _metadata.Resolve(type);
        if (definition is null)
        {
            // Type lives outside the indexed assemblies (or failed to load); do not guess.
            return;
        }

        if (definition.IsEnum)
        {
            ValidateEnum(definition, value, relativePath, location);
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            // Most non-primitive wrappers (asset ids, custom serializers) are serialized as strings.
            return;
        }

        var members = _metadata.GetMembers(definition);
        if (members.Count == 0)
        {
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            var baseName = StripModifier(property.Name);
            if (!members.TryGetValue(baseName, out var member))
            {
                _report.Error("spec",
                    $"{At(relativePath, location)}: '{property.Name}' is not a serialized member of {definition.Name}");
                continue;
            }

            ValidateValue(member.Type, property.Value, relativePath, Join(location, property.Name), depth + 1);
        }
    }

    private void ValidateEnum(TypeDefinition definition, JsonElement value, string relativePath, string location)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            _report.Error("spec", $"{At(relativePath, location)}: expected an enum name or number");
            return;
        }

        var names = _metadata.ReadEnumNames(definition);
        var text = value.GetString() ?? string.Empty;

        foreach (var token in text.Split(','))
        {
            var name = token.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var known = names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                _report.Error("spec",
                    $"{At(relativePath, location)}: '{name}' is not a member of enum {definition.FullName}");
            }
        }
    }

    private static string StripModifier(string name)
    {
        var hash = name.IndexOf('#');
        return hash < 0 ? name : name[..hash];
    }

    private static string Join(string location, string segment) =>
        location.Length == 0 ? segment : location + "." + segment;

    private static string At(string relativePath, string location) =>
        location.Length == 0 ? relativePath : relativePath + ": " + location;

    private void CollectLocKeys(JsonElement element, string relativePath)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString() ?? string.Empty;
                        if (value.Length > 0 && property.Name.EndsWith("LocKey", StringComparison.Ordinal))
                        {
                            AddLocKey(value, relativePath, property.Name);
                        }
                        else if (value.StartsWith("HDBF.", StringComparison.Ordinal))
                        {
                            AddLocKey(value, relativePath, property.Name);
                        }
                    }

                    CollectLocKeys(property.Value, relativePath);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectLocKeys(item, relativePath);
                }

                break;

            case JsonValueKind.String:
            {
                var value = element.GetString() ?? string.Empty;
                if (value.StartsWith("HDBF.", StringComparison.Ordinal))
                {
                    AddLocKey(value, relativePath, "(value)");
                }

                break;
            }
        }
    }

    private void AddLocKey(string key, string relativePath, string source)
    {
        if (key.Length == 0)
        {
            _report.Error("localization", $"{relativePath}: {source} is an empty localization key");
            return;
        }

        _usedLocKeys.Add(key);
    }
}
