using Mono.Cecil;

namespace HDBF.Tools.Validate;

/// <summary>One bindable member of a blueprint/spec type.</summary>
internal sealed record MemberType(string Name, TypeReference Type);

/// <summary>
/// Read-only view of the game + mod type metadata, loaded with Mono.Cecil.
///
/// The validator never loads the game assemblies for execution: it only reads metadata, so it
/// works on a machine where the game is installed but not running, and a corrupt mod assembly
/// can never break validation with a runtime exception.
/// </summary>
internal sealed class GameMetadata
{
    private const string ComponentSpecFullName = "Timberborn.BlueprintSystem.ComponentSpec";

    private readonly Dictionary<string, TypeDefinition> _typesByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeDefinition> _specsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, MemberType>> _memberCache =
        new(StringComparer.Ordinal);

    private GameMetadata(string managedDir)
    {
        ManagedDir = managedDir;
    }

    public string ManagedDir { get; }

    public int GameAssemblyCount { get; private set; }

    public int ModAssemblyCount { get; private set; }

    public int SpecTypeCount => _specsByName.Count;

    /// <summary>True when name is a public non-abstract ComponentSpec subclass (or its alias).</summary>
    public bool IsKnownSpecName(string name) => _specsByName.ContainsKey(name);

    public bool TryGetSpec(string name, out TypeDefinition definition) =>
        _specsByName.TryGetValue(name, out definition!);

    public IReadOnlyCollection<string> SpecNames => _specsByName.Keys;

    public static GameMetadata Load(
        string managedDir,
        IReadOnlyList<string> modAssemblyPaths,
        Report report)
    {
        var metadata = new GameMetadata(managedDir);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);

        foreach (var directory in modAssemblyPaths
                     .Select(Path.GetDirectoryName)
                     .Where(d => !string.IsNullOrEmpty(d))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            resolver.AddSearchDirectory(directory!);
        }

        var gameAssemblies = new List<AssemblyDefinition>();
        foreach (var path in Directory.GetFiles(managedDir, "*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var assembly = TryRead(path, resolver, report, gameAssembly: true);
            if (assembly is not null)
            {
                gameAssemblies.Add(assembly);
            }
        }

        metadata.GameAssemblyCount = gameAssemblies.Count;

        var modAssemblies = new List<AssemblyDefinition>();
        foreach (var path in modAssemblyPaths)
        {
            var assembly = TryRead(path, resolver, report, gameAssembly: false);
            if (assembly is not null)
            {
                modAssemblies.Add(assembly);
            }
        }

        metadata.ModAssemblyCount = modAssemblies.Count;

        var allTypes = new List<TypeDefinition>();
        foreach (var assembly in gameAssemblies.Concat(modAssemblies))
        {
            CollectTypes(assembly.MainModule, allTypes);
        }

        foreach (var type in allTypes)
        {
            // First definition wins: game assemblies are read first, then mod assemblies.
            metadata._typesByFullName.TryAdd(type.FullName, type);
        }

        foreach (var type in allTypes)
        {
            if (!IsComponentSpec(type, metadata._typesByFullName))
            {
                continue;
            }

            metadata._specsByName.TryAdd(type.Name, type);

            foreach (var alias in ReadSpecAliases(type))
            {
                metadata._specsByName.TryAdd(alias, type);
            }
        }

        return metadata;
    }

    /// <summary>Serialized/bindable members of a blueprint or plain serializable type.</summary>
    public IReadOnlyDictionary<string, MemberType> GetMembers(TypeDefinition type)
    {
        if (_memberCache.TryGetValue(type.FullName, out var cached))
        {
            return cached;
        }

        var properties = type.Properties
            .Where(p => p.GetMethod is { IsPublic: true, IsStatic: false }
                        && p.SetMethod is { IsPublic: true, IsStatic: false })
            .ToList();

        var serialized = properties.Where(HasSerializeAttribute).ToList();
        var selected = serialized.Count > 0 ? serialized : properties;

        var members = new Dictionary<string, MemberType>(StringComparer.Ordinal);
        foreach (var property in selected)
        {
            members[property.Name] = new MemberType(property.Name, property.PropertyType);
        }

        // Public fields are used by a few plain structs (and by Unity types); only add them when
        // the type exposes no bindable properties at all, so record properties always win.
        if (members.Count == 0)
        {
            foreach (var field in type.Fields.Where(f => f.IsPublic && !f.IsStatic))
            {
                members[field.Name] = new MemberType(field.Name, field.FieldType);
            }
        }

        _memberCache[type.FullName] = members;
        return members;
    }

    public TypeDefinition? Resolve(TypeReference reference)
    {
        if (reference is TypeDefinition definition)
        {
            return definition;
        }

        return _typesByFullName.TryGetValue(reference.FullName, out var resolved) ? resolved : null;
    }

    public static bool IsCollection(TypeReference type, out TypeReference element)
    {
        switch (type)
        {
            case ArrayType arrayType:
                element = arrayType.ElementType;
                return true;

            case GenericInstanceType generic when generic.GenericArguments.Count == 1:
            {
                var name = generic.ElementType.Name;
                var simple = name.Split('`')[0];
                if (simple is "ImmutableArray" or "List" or "IReadOnlyList" or "IList" or "IEnumerable"
                    or "IReadOnlyCollection" or "ICollection" or "HashSet")
                {
                    element = generic.GenericArguments[0];
                    return true;
                }

                break;
            }
        }

        element = type;
        return false;
    }

    public static bool TryGetPrimitiveKind(TypeReference type, out PrimitiveKind kind)
    {
        switch (type.FullName)
        {
            case "System.String":
                kind = PrimitiveKind.String;
                return true;
            case "System.Boolean":
                kind = PrimitiveKind.Boolean;
                return true;
            case "System.Int16":
            case "System.Int32":
            case "System.Int64":
            case "System.Byte":
            case "System.SByte":
            case "System.UInt16":
            case "System.UInt32":
            case "System.UInt64":
            case "System.Single":
            case "System.Double":
            case "System.Decimal":
                kind = PrimitiveKind.Number;
                return true;
            default:
                kind = PrimitiveKind.None;
                return false;
        }
    }

    /// <summary>Unity vector structs are deserialized from capital X/Y/Z/W keys by a custom deserializer.</summary>
    public static bool IsUnityVector(TypeReference type) =>
        type.FullName.StartsWith("UnityEngine.Vector", StringComparison.Ordinal)
        || type.FullName.StartsWith("UnityEngine.Quaternion", StringComparison.Ordinal);

    public IReadOnlyList<string> ReadEnumNames(TypeDefinition type)
    {
        return type.Fields
            .Where(f => f.IsStatic && f.HasConstant)
            .Select(f => f.Name)
            .ToList();
    }

    private static AssemblyDefinition? TryRead(string path, IAssemblyResolver resolver, Report report, bool gameAssembly)
    {
        try
        {
            return AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
        }
        catch (Exception ex)
        {
            if (gameAssembly)
            {
                report.Warn("metadata", $"could not read game assembly {Path.GetFileName(path)}: {ex.GetType().Name}");
            }
            else
            {
                report.Error("metadata", $"could not read mod assembly {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }
    }

    private static void CollectTypes(ModuleDefinition module, List<TypeDefinition> result)
    {
        foreach (var type in module.Types)
        {
            CollectType(type, result);
        }
    }

    private static void CollectType(TypeDefinition type, List<TypeDefinition> result)
    {
        result.Add(type);
        foreach (var nested in type.NestedTypes)
        {
            CollectType(nested, result);
        }
    }

    private static bool IsComponentSpec(TypeDefinition type, IReadOnlyDictionary<string, TypeDefinition> types)
    {
        // SpecTypeCache discovers every non-abstract ComponentSpec subclass regardless of
        // visibility: most vanilla specs (BuildingModelSpec, BuildingAccessibleSpec, ...) are
        // internal to their game assembly, so IsPublic must NOT be required here.
        if (type.IsAbstract || type.IsInterface || type.IsValueType)
        {
            return false;
        }

        var current = type.BaseType;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (string.Equals(current.FullName, ComponentSpecFullName, StringComparison.Ordinal))
            {
                return true;
            }

            if (!types.TryGetValue(current.FullName, out var baseDefinition))
            {
                return false;
            }

            current = baseDefinition.BaseType;
        }

        return false;
    }

    private static IEnumerable<string> ReadSpecAliases(TypeDefinition type)
    {
        foreach (var attribute in type.CustomAttributes)
        {
            if (!string.Equals(attribute.AttributeType.Name, "SpecAliasAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is string alias && alias.Length > 0)
                {
                    yield return alias;
                }
            }
        }
    }

    private static bool HasSerializeAttribute(PropertyDefinition property)
    {
        foreach (var attribute in property.CustomAttributes)
        {
            if (string.Equals(attribute.AttributeType.Name, "SerializeAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal enum PrimitiveKind
{
    None,
    String,
    Boolean,
    Number,
}
