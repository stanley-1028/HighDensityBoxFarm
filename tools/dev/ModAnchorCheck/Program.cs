using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using HDBF.Core.Compat;

namespace HDBF.Tools.ModAnchorCheck;

/// <summary>
/// Offline verifier for the five optional-mod anchors.
///
/// It never copies a third-party DLL into this repository and never loads it into a runtime: it
/// reads the metadata straight out of the installed Steam Workshop folder with
/// System.Reflection.Metadata. That is the same ground-truth method as tools/ApiIndex, but it is
/// data-driven from <see cref="CompatAnchorTable"/> so the table cannot silently drift away from
/// the DLLs HDBF claims to support.
///
/// Usage:
///   ModAnchorCheck check [searchRoot ...]
///       Verify every anchor; prints PASS/FAIL/MISSING plus the DLL path and manifest identity.
///       Exit code 0 = all anchors verified, 1 = any mismatch, 2 = usage/no DLLs.
///   ModAnchorCheck list
///       Print the anchor table as tab-separated rows (for docs / diffing).
/// </summary>
internal static class Program
{
    private const string WorkshopContentRoot =
        @"C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090";

    /// <summary>
    /// The five verified version folders from docs/workstreams/compat-01-adapters.md. The Workshop
    /// root also contains unrelated/older copies of some mods (e.g. an older "Conveyor Belts"
    /// 3761295060 next to "Conveyor Belts v2" 3815376360), so the default is deliberately scoped
    /// to the exact version folder the matrix installs; pass roots explicitly to override.
    /// </summary>
    private static readonly string[] DefaultRoots =
    {
        Path.Combine(WorkshopContentRoot, "3801726769", "version-1.1"),
        Path.Combine(WorkshopContentRoot, "3766893705", "version-1.1"),
        Path.Combine(WorkshopContentRoot, "3815376360", "version-1.1"),
        Path.Combine(WorkshopContentRoot, "3723216060", "version-1.1.0.2"),
        Path.Combine(WorkshopContentRoot, "3682012904", "Version-1.1"),
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "check" && args[0] != "list"))
        {
            Console.Error.WriteLine("usage: ModAnchorCheck check [searchRoot ...]");
            Console.Error.WriteLine("       ModAnchorCheck list");
            return 2;
        }

        if (args[0] == "list")
        {
            WriteTable();
            return 0;
        }

        var roots = args.Length > 1 ? args.Skip(1).ToArray() : DefaultRoots;
        var assemblies = AssemblyIndex.Build(roots);
        if (assemblies.Count == 0)
        {
            Console.Error.WriteLine("no *.dll found under: " + string.Join(", ", roots));
            return 2;
        }

        Console.WriteLine("HDBF third-party anchor verification");
        Console.WriteLine("game version fact: 1.1.2.8 (docs/API_FACTS.md)");
        Console.WriteLine("search roots: " + string.Join(" ; ", roots));
        foreach (var assembly in assemblies.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  assembly {assembly.Name,-24} version={assembly.Version,-10} path={assembly.Path}");
            var manifest = ManifestSummary(assembly.Path);
            if (!string.IsNullOrEmpty(manifest))
            {
                Console.WriteLine($"    {manifest}");
            }
        }

        var pass = 0;
        var fail = 0;
        var missing = 0;

        foreach (var adapter in CompatAnchorTable.All)
        {
            Console.WriteLine();
            Console.WriteLine($"## {adapter.AdapterId}  (mod {adapter.ModId} / {adapter.ModName})");
            foreach (var anchor in adapter.ApiAnchors)
            {
                var result = VerifyAnchor(assemblies, anchor);
                switch (result.Status)
                {
                    case AnchorStatus.Pass:
                        pass++;
                        Console.WriteLine($"PASS   {adapter.AdapterId} | {anchor.SignatureText} | dll={result.Path}");
                        break;
                    case AnchorStatus.Fail:
                        fail++;
                        Console.WriteLine($"FAIL   {adapter.AdapterId} | {anchor.SignatureText}");
                        Console.WriteLine($"       expected: {anchor.SignatureText}");
                        Console.WriteLine($"       actual:   {result.Detail}");
                        Console.WriteLine($"       dll:      {result.Path}");
                        break;
                    default:
                        missing++;
                        Console.WriteLine($"MISSING {adapter.AdapterId} | {anchor.SignatureText}");
                        Console.WriteLine($"        {result.Detail}");
                        break;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"SUMMARY: {pass + fail + missing} anchors checked, {pass} PASS, {fail} FAIL, {missing} MISSING");
        Console.WriteLine(fail == 0 && missing == 0 ? "RESULT: OK - every anchor matches the installed DLL metadata." : "RESULT: FAILED");

        return fail == 0 && missing == 0 ? 0 : 1;
    }

    private static void WriteTable()
    {
        Console.WriteLine("adapter\tassembly\ttype\tmember\tparameters\treturn");
        foreach (var adapter in CompatAnchorTable.All)
        {
            foreach (var anchor in adapter.ApiAnchors)
            {
                Console.WriteLine(string.Join('\t',
                    adapter.AdapterId,
                    anchor.AssemblyName,
                    anchor.TypeName,
                    anchor.MemberName.Length == 0 ? "-" : anchor.MemberName,
                    anchor.ParameterTypeNames.Count == 0 ? "-" : string.Join(", ", anchor.ParameterTypeNames),
                    anchor.ReturnTypeName.Length == 0 ? "-" : anchor.ReturnTypeName));
            }
        }
    }

    private static AnchorResult VerifyAnchor(AssemblyIndex assemblies, CompatAnchorSpec anchor)
    {
        if (!assemblies.TryGet(anchor.AssemblyName, out var assembly))
        {
            return AnchorResult.Missing($"assembly '{anchor.AssemblyName}' was not found under the search roots");
        }

        try
        {
            using var stream = File.OpenRead(assembly.Path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var type = FindType(reader, anchor.TypeName);
            if (type is null)
            {
                return AnchorResult.Missing($"type '{anchor.TypeName}' not found in {assembly.Path}");
            }

            if (anchor.IsTypeOnly)
            {
                return AnchorResult.Pass(anchor.TypeName, assembly.Path);
            }

            var candidates = new List<string>();
            foreach (var handle in type.Value.GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                var name = reader.GetString(method.Name);
                if (!string.Equals(name, anchor.MemberName, StringComparison.Ordinal))
                {
                    continue;
                }

                var decoded = Decode(reader, method);
                candidates.Add(decoded.ActualSignature);
                if (!ParametersMatch(decoded.ParameterTypeNames, anchor.ParameterTypeNames) ||
                    !string.Equals(decoded.ReturnTypeName, anchor.ReturnTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(decoded.ActualSignature, anchor.SignatureText, StringComparison.Ordinal))
                {
                    return AnchorResult.Pass(decoded.ActualSignature, assembly.Path);
                }

                // The out/ref display can differ from SignatureText (which records element types);
                // a metadata-identical signature is what matters, so accept it and report the
                // rendered form with out/ref markers.
                return AnchorResult.Pass(decoded.ActualSignature, assembly.Path);
            }

            var detail = candidates.Count == 0
                ? $"member '{anchor.MemberName}' not found on {anchor.TypeName}"
                : $"member '{anchor.MemberName}' exists but no overload matches; candidates: {string.Join(" ; ", candidates)}";
            return AnchorResult.Fail(detail, assembly.Path);
        }
        catch (Exception exception)
        {
            return AnchorResult.Missing($"could not read {assembly.Path}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? ManifestSummary(string dllPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(dllPath)!);
        for (var depth = 0; depth < 4 && directory is not null; depth++, directory = directory.Parent)
        {
            var manifestPath = Path.Combine(directory.FullName, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = document.RootElement;
                return "manifest Name=\"" + ReadString(root, "Name") + "\" Id=\"" + ReadString(root, "Id") +
                       "\" Version=\"" + ReadString(root, "Version") + "\"";
            }
            catch (Exception)
            {
                return "manifest.json present but unreadable: " + manifestPath;
            }
        }

        return null;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static TypeDefinition? FindType(MetadataReader reader, string fullName)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (string.Equals(TypeName(reader, type), fullName, StringComparison.Ordinal))
            {
                return type;
            }
        }

        return null;
    }

    private static string TypeName(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return TypeName(reader, reader.GetTypeDefinition(declaring)) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static DecodedMethod Decode(MetadataReader reader, MethodDefinition method)
    {
        var provider = new SignatureProvider();
        var signature = method.DecodeSignature(provider, null);
        var parameters = new List<string>();
        var display = new StringBuilder();
        display.Append(reader.GetString(method.Name)).Append('(');

        var parameterHandles = method.GetParameters();
        var outFlags = new bool[signature.ParameterTypes.Length + 1];
        foreach (var parameterHandle in parameterHandles)
        {
            var parameter = reader.GetParameter(parameterHandle);
            var sequence = parameter.SequenceNumber;
            if (sequence >= 0 && sequence < outFlags.Length)
            {
                outFlags[sequence] = (parameter.Attributes & ParameterAttributes.Out) != 0;
            }
        }

        for (var i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var rendered = signature.ParameterTypes[i];
            var element = rendered.EndsWith("&", StringComparison.Ordinal)
                ? rendered.Substring(0, rendered.Length - 1)
                : rendered;
            parameters.Add(element);

            if (i > 0)
            {
                display.Append(", ");
            }

            if (outFlags[i + 1])
            {
                display.Append("out ");
            }
            else if (rendered.EndsWith("&", StringComparison.Ordinal))
            {
                display.Append("ref ");
            }

            display.Append(element);
        }

        display.Append(") -> ").Append(signature.ReturnType);
        return new DecodedMethod(parameters, signature.ReturnType, display.ToString());
    }

    private static bool ParametersMatch(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < actual.Count; i++)
        {
            if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private enum AnchorStatus
    {
        Pass,
        Fail,
        Missing,
    }

    private readonly struct AnchorResult
    {
        private AnchorResult(AnchorStatus status, string actualSignature, string detail, string path)
        {
            Status = status;
            ActualSignature = actualSignature;
            Detail = detail;
            Path = path;
        }

        public AnchorStatus Status { get; }

        public string ActualSignature { get; }

        public string Detail { get; }

        public string Path { get; }

        public static AnchorResult Pass(string actualSignature, string path) =>
            new(AnchorStatus.Pass, actualSignature, string.Empty, path);

        public static AnchorResult Fail(string detail, string path) =>
            new(AnchorStatus.Fail, string.Empty, detail, path);

        public static AnchorResult Missing(string detail) =>
            new(AnchorStatus.Missing, string.Empty, detail, string.Empty);
    }

    private readonly struct DecodedMethod
    {
        public DecodedMethod(IReadOnlyList<string> parameterTypeNames, string returnTypeName, string actualSignature)
        {
            ParameterTypeNames = parameterTypeNames;
            ReturnTypeName = returnTypeName;
            ActualSignature = actualSignature;
        }

        public IReadOnlyList<string> ParameterTypeNames { get; }

        public string ReturnTypeName { get; }

        public string ActualSignature { get; }
    }

    private sealed class AssemblyEntry
    {
        public AssemblyEntry(string name, string version, string path)
        {
            Name = name;
            Version = version;
            Path = path;
        }

        public string Name { get; }

        public string Version { get; }

        public string Path { get; }
    }

    private sealed class AssemblyIndex
    {
        private readonly Dictionary<string, AssemblyEntry> _byName;

        private AssemblyIndex(Dictionary<string, AssemblyEntry> byName)
        {
            _byName = byName;
        }

        public int Count => _byName.Count;

        public IEnumerable<AssemblyEntry> Values => _byName.Values;

        public bool TryGet(string name, out AssemblyEntry entry) => _byName.TryGetValue(name, out entry!);

        public static AssemblyIndex Build(string[] roots)
        {
            var byName = new Dictionary<string, AssemblyEntry>(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    Console.Error.WriteLine("warning: search root does not exist: " + root);
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(root, "*.dll", options))
                {
                    try
                    {
                        using var stream = File.OpenRead(path);
                        using var peReader = new PEReader(stream);
                        if (!peReader.HasMetadata)
                        {
                            continue;
                        }

                        var reader = peReader.GetMetadataReader();
                        if (!reader.IsAssembly)
                        {
                            continue;
                        }

                        var definition = reader.GetAssemblyDefinition();
                        var name = reader.GetString(definition.Name);
                        if (byName.TryGetValue(name, out var existing))
                        {
                            if (!string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.Error.WriteLine(
                                    $"warning: assembly '{name}' exists in more than one root; using {existing.Path}, ignoring {path}");
                            }

                            continue;
                        }

                        byName[name] = new AssemblyEntry(name, definition.Version.ToString(), path);
                    }
                    catch (Exception)
                    {
                        // Not a managed assembly / unreadable file: irrelevant to the anchor check.
                    }
                }
            }

            return new AssemblyIndex(byName);
        }
    }

    private sealed class SignatureProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) =>
            elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                _ => typeCode.ToString(),
            };

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil)
            {
                return GetTypeFromDefinition(reader, declaring, rawTypeKind) + "+" + name;
            }

            var ns = reader.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) +
                       "+" + name;
            }

            var ns = reader.GetString(reference.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
            TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
