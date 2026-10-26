using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace HDBF.Tools.ApiIndex;

/// <summary>
/// Offline metadata dumper for the installed Timberborn assemblies.
///
/// The requirements document says every technical name in it is a suggestion and that the
/// real game source wins. This tool is how we establish that ground truth without an IDE,
/// without running the game, and without network access: it reads assembly metadata with
/// Mono.Cecil and emits searchable text indexes.
///
/// Usage:
///   ApiIndex index  &lt;managedDir&gt; &lt;outFile.txt&gt;
///   ApiIndex dump   &lt;managedDir&gt; &lt;outFile.txt&gt; &lt;typeNamePattern&gt; [morePatterns...]
///   ApiIndex spec   &lt;managedDir&gt; &lt;outFile.txt&gt; &lt;jsonFieldNamesFile&gt;
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: ApiIndex <index|dump|spec> <managedDir> <outFile> [patterns...]");
            return 2;
        }

        var mode = args[0];
        var managedDir = args[1];
        var outFile = args[2];

        if (!Directory.Exists(managedDir))
        {
            Console.Error.WriteLine($"managed dir not found: {managedDir}");
            return 2;
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var assemblies = Directory.GetFiles(managedDir, "*.dll")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => TryRead(p, resolver))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        using var writer = new StreamWriter(outFile, false, new UTF8Encoding(false));
        switch (mode)
        {
            case "index":
                WriteIndex(assemblies, writer);
                break;
            case "dump":
                WriteDump(assemblies, writer, args.Skip(3).ToArray());
                break;
            case "spec":
                WriteSpecCheck(assemblies, writer, args[3]);
                break;
            case "il":
                WriteIl(assemblies, writer, args[3], args.Length > 4 ? args[4] : ".*");
                break;
            case "referrers":
                WriteReferrers(assemblies, writer, args[3]);
                break;
            default:
                Console.Error.WriteLine($"unknown mode: {mode}");
                return 2;
        }

        Console.WriteLine($"wrote {outFile} ({new FileInfo(outFile).Length:N0} bytes) from {assemblies.Count} assemblies");
        return 0;
    }

    private static AssemblyDefinition? TryRead(string path, IAssemblyResolver resolver)
    {
        try
        {
            return AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"skip {Path.GetFileName(path)}: {ex.GetType().Name}");
            return null;
        }
    }

    private static void WriteIndex(List<AssemblyDefinition> assemblies, TextWriter writer)
    {
        writer.WriteLine("# Timberborn assembly type index");
        writer.WriteLine("# columns: assembly | full type name | kind | base type | type attributes");
        foreach (var asm in assemblies)
        {
            foreach (var type in AllTypes(asm.MainModule))
            {
                var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
                var bt = type.BaseType is null ? "" : TypeName(type.BaseType);
                writer.WriteLine($"{asm.Name.Name}|{TypeName(type)}|{kind}|{bt}|{type.Attributes}");
            }
        }
    }

    private static void WriteDump(List<AssemblyDefinition> assemblies, TextWriter writer, string[] patterns)
    {
        var regexes = patterns.Select(p => new Regex(p, RegexOptions.Compiled)).ToArray();
        writer.WriteLine($"# member dump for {string.Join(", ", patterns)}");
        foreach (var asm in assemblies)
        {
            foreach (var type in AllTypes(asm.MainModule))
            {
                var full = TypeName(type);
                if (!regexes.Any(r => r.IsMatch(full)))
                {
                    continue;
                }

                writer.WriteLine();
                writer.WriteLine($"=== {full}   [{asm.Name.Name}]");
                writer.WriteLine($"  kind: {(type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class")}   base: {(type.BaseType is null ? "-" : TypeName(type.BaseType))}");
                if (type.Interfaces.Count > 0)
                {
                    writer.WriteLine("  interfaces: " + string.Join(", ", type.Interfaces.Select(i => TypeName(i.InterfaceType))));
                }

                foreach (var attr in type.CustomAttributes)
                {
                    writer.WriteLine($"  [type attr] {TypeName(attr.AttributeType)}");
                }

                foreach (var field in type.Fields)
                {
                    if (field.Name.StartsWith("k__BackingField", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    writer.WriteLine($"  field {(field.IsStatic ? "static " : "")}{TypeName(field.FieldType)} {field.Name}");
                }

                foreach (var prop in type.Properties)
                {
                    var accessors = string.Join("/", new[] { prop.GetMethod is not null ? "get" : null, prop.SetMethod is not null ? "set" : null }.Where(s => s is not null));
                    writer.WriteLine($"  prop {TypeName(prop.PropertyType)} {prop.Name} {{{accessors}}}");
                }

                foreach (var evt in type.Events)
                {
                    writer.WriteLine($"  event {TypeName(evt.EventType)} {evt.Name}");
                }

                foreach (var method in type.Methods)
                {
                    var parameters = string.Join(", ", method.Parameters.Select(p =>
                        $"{(p.IsOut ? "out " : p.ParameterType is ByReferenceType ? "ref " : "")}{TypeName(p.ParameterType)} {p.Name}"));
                    writer.WriteLine($"  method {(method.IsPublic ? "public " : method.IsPrivate ? "private " : "")}{(method.IsStatic ? "static " : "")}{TypeName(method.ReturnType)} {method.Name}({parameters})");
                }

                foreach (var nested in type.NestedTypes)
                {
                    writer.WriteLine($"  nested {TypeName(nested)}");
                }
            }
        }
    }

    /// <summary>
    /// Checks that every JSON field name we plan to ship in a blueprint spec actually exists as a
    /// public serialized member of some game blueprint spec type. Field names are matched
    /// case-insensitively against public instance properties/fields.
    /// </summary>
    private static void WriteSpecCheck(List<AssemblyDefinition> assemblies, TextWriter writer, string namesFile)
    {
        var wanted = File.ReadAllLines(namesFile)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        var memberIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in assemblies)
        {
            foreach (var type in AllTypes(asm.MainModule))
            {
                if (!type.IsPublic && !type.IsNestedPublic)
                {
                    continue;
                }

                foreach (var prop in type.Properties.Where(p => p.GetMethod is not null))
                {
                    Add(memberIndex, prop.Name, $"{TypeName(type)}.{prop.Name} [{asm.Name.Name}]");
                }

                foreach (var field in type.Fields.Where(f => f.IsPublic && !f.IsStatic))
                {
                    Add(memberIndex, field.Name, $"{TypeName(type)}.{field.Name} [{asm.Name.Name}]");
                }
            }
        }

        foreach (var name in wanted)
        {
            if (memberIndex.TryGetValue(name, out var hits))
            {
                writer.WriteLine($"OK   {name}  ->  {string.Join(" ; ", hits.Take(6))}{(hits.Count > 6 ? $" ...(+{hits.Count - 6})" : "")}");
            }
            else
            {
                writer.WriteLine($"MISS {name}");
            }
        }
    }

    private static void Add(Dictionary<string, List<string>> index, string key, string value)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<string>();
            index[key] = list;
        }

        list.Add(value);
    }

    /// <summary>
    /// Dumps IL for matching types/methods so binding conventions can be read straight from the
    /// game instead of guessed from documentation that may not match this build.
    /// </summary>
    private static void WriteIl(List<AssemblyDefinition> assemblies, TextWriter writer, string typePattern, string methodPattern)
    {
        var typeRegex = new Regex(typePattern, RegexOptions.Compiled);
        var methodRegex = new Regex(methodPattern, RegexOptions.Compiled);
        foreach (var asm in assemblies)
        {
            foreach (var type in AllTypes(asm.MainModule))
            {
                var full = TypeName(type);
                if (!typeRegex.IsMatch(full))
                {
                    continue;
                }

                foreach (var method in type.Methods)
                {
                    if (!methodRegex.IsMatch(method.Name))
                    {
                        continue;
                    }

                    var generics = method.HasGenericParameters
                        ? "<" + string.Join(", ", method.GenericParameters.Select(p => p.Name)) + ">"
                        : "";
                    writer.WriteLine();
                    writer.WriteLine($"=== {full}::{method.Name}{generics}   [{asm.Name.Name}]");
                    if (!method.HasBody)
                    {
                        writer.WriteLine("  (no body)");
                        continue;
                    }

                    foreach (var instruction in method.Body.Instructions)
                    {
                        writer.WriteLine($"  {instruction}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds every method whose IL mentions a matching type/member name. Used to answer
    /// "where does the game discover mod configurators / mod starters / blueprint sources".
    /// </summary>
    private static void WriteReferrers(List<AssemblyDefinition> assemblies, TextWriter writer, string namePattern)
    {
        var regex = new Regex(namePattern, RegexOptions.Compiled);
        foreach (var asm in assemblies)
        {
            foreach (var type in AllTypes(asm.MainModule))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    var hit = false;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var operand = instruction.Operand?.ToString();
                        if (operand is null)
                        {
                            continue;
                        }

                        if (regex.IsMatch(operand))
                        {
                            hit = true;
                            break;
                        }
                    }

                    if (hit)
                    {
                        writer.WriteLine($"{TypeName(type)}::{method.Name}   [{asm.Name.Name}]");
                    }
                }
            }
        }
    }

    internal static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            yield return type;
            foreach (var nested in Flatten(type))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes)
        {
            yield return nested;
            foreach (var deeper in Flatten(nested))
            {
                yield return deeper;
            }
        }
    }

    internal static string TypeName(TypeReference reference)
    {
        if (reference is GenericInstanceType generic)
        {
            return $"{generic.ElementType.Name.Split('`')[0]}<{string.Join(", ", generic.GenericArguments.Select(TypeName))}>";
        }

        if (reference is ArrayType array)
        {
            return $"{TypeName(array.ElementType)}[]";
        }

        if (reference is ByReferenceType byRef)
        {
            return $"ref {TypeName(byRef.ElementType)}";
        }

        if (reference is GenericParameter gp)
        {
            return gp.Name;
        }

        var decl = reference.DeclaringType is not null ? TypeName(reference.DeclaringType) + "+" : reference.Namespace;
        return decl is null or "" ? reference.Name : $"{decl}.{reference.Name}";
    }
}
