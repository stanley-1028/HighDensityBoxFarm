using System.Reflection;
using System.Text.RegularExpressions;

namespace HDBF.Tools.Validate;

/// <summary>
/// Entry point. Usage (from the mod root):
///
///   dotnet run --project tools\validate -c Release -m:1 -- data
///   dotnet run --project tools\validate -c Release -m:1 -- data --managed "C:\...\Timberborn_Data\Managed"
///
/// Exit codes: 0 = data/ is clean, 1 = problems (listed), 2 = cannot even start (bad paths).
/// </summary>
internal static class Program
{
    /// <summary>
    /// The only five assemblies that ship as the HDBF mod (build.ps1 stages exactly these).
    /// --assemblies points at a build-output root that also contains tool/test projects, so
    /// discovery accepts this explicit set by each assembly's own AssemblyName — never by folder
    /// name and never by "whatever happens to be in the folder".
    /// </summary>
    private static readonly string[] ExpectedModAssemblies =
    {
        "HDBF.Core",
        "HDBF.Persistence",
        "HDBF.Gameplay",
        "HDBF.Integration",
        "HDBF.UI",
    };

    private static int Main(string[] args)
    {
        string? dataArgument = null;
        string? managedArgument = null;
        string? modArgument = null;
        string? refArgument = null;
        string? assembliesArgument = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                case "--managed":
                    managedArgument = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--mod":
                    modArgument = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--ref":
                    refArgument = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--assemblies":
                    assembliesArgument = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--":
                    // `dotnet run -m:1 -- data` forwards the separator itself as an app argument.
                    break;
                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal))
                    {
                        // `dotnet run -m:1 -- data` also forwards the MSBuild switch itself.
                        break;
                    }

                    if (dataArgument is null)
                    {
                        dataArgument = args[i];
                    }
                    else
                    {
                        Console.Error.WriteLine($"unexpected argument: {args[i]}");
                        PrintUsage();
                        return 2;
                    }

                    break;
            }
        }

        var dataDir = Path.GetFullPath(dataArgument ?? "data");
        if (!Directory.Exists(dataDir))
        {
            Console.Error.WriteLine($"data directory not found: {dataDir}");
            return 2;
        }

        var modRoot = Path.GetFullPath(modArgument ?? GuessModRoot(dataDir));

        // Vanilla reference blueprints live outside the mod folder on purpose: the game
        // recursively converts every *.blueprint.json below a mod directory, so a copy inside
        // the mod would register the entire vanilla blueprint set a second time.
        var refRoot = Path.GetFullPath(refArgument ?? Path.Combine(modRoot, ".ref"));

        var managedDir = ResolveManagedDir(modRoot, managedArgument);
        if (managedDir is null || !Directory.Exists(managedDir))
        {
            Console.Error.WriteLine(
                "could not find the Timberborn managed assemblies. "
                + "Pass --managed <game>\\Timberborn_Data\\Managed or set TIMBERBORN_MANAGED_DIR. "
                + $"Tried: {managedDir ?? "(none)"}");
            return 2;
        }

        var report = new Report();
        var assemblyDiscovery = DiscoverModAssemblies(modRoot, assembliesArgument);
        var modAssemblies = assemblyDiscovery.Assemblies;
        var metadata = GameMetadata.Load(managedDir, modAssemblies, report);

        Console.WriteLine("HDBF data validator");
        Console.WriteLine($"  data dir        : {dataDir}");
        Console.WriteLine($"  mod root        : {modRoot}");
        Console.WriteLine($"  reference dir   : {refRoot}");
        Console.WriteLine($"  managed dir     : {managedDir}");
        Console.WriteLine($"  game assemblies : {metadata.GameAssemblyCount}");
        Console.WriteLine($"  spec types      : {metadata.SpecTypeCount} (game + mod ComponentSpec subclasses)");
        Console.WriteLine($"  mod assemblies  : {metadata.ModAssemblyCount} "
                          + $"({string.Join(", ", modAssemblies.Select(Path.GetFileName))})");
        Console.WriteLine($"  mod assemblies  : {DescribeAssemblyCheck(assemblyDiscovery, metadata.ModAssemblyCount)}");
        ReportAssemblyDiscoveryProblems(assemblyDiscovery, report);

        if (!metadata.IsKnownSpecName("HdbfBoxFarmSpec"))
        {
            report.Error("metadata",
                "HdbfBoxFarmSpec was not found in any built mod assembly. "
                + "Run 'dotnet build src\\HDBF.Gameplay\\HDBF.Gameplay.csproj -c Release -m:1' first.");
        }

        new DataChecks(dataDir, modRoot, refRoot, report).Validate();

        var blueprintValidator = new BlueprintValidator(metadata, report);
        var blueprintFiles = Directory
            .GetFiles(dataDir, "*.blueprint.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"  blueprints      : {blueprintFiles.Count}");

        foreach (var file in blueprintFiles)
        {
            var relative = Path.GetRelativePath(dataDir, file).Replace('\\', '/');
            blueprintValidator.ValidateFile(file, relative);
        }

        new Localizations(report).Validate(dataDir, blueprintValidator.UsedLocKeys, modRoot);
        new ConfigValidator(report).Validate(dataDir);

        return report.Finish(Console.Out);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage: HdbfValidate [dataDir] [--managed <Timberborn_Data\\Managed>] [--mod <modRoot>] "
                          + "[--ref <vanillaReferenceDir>] [--assemblies <buildOutputRoot>]");
        Console.WriteLine();
        Console.WriteLine("Validates the shippable mod data folder against the installed Timberborn metadata.");
        Console.WriteLine("Defaults: dataDir = 'data', modRoot = parent of dataDir, refRoot = <modRoot>\\.ref,");
        Console.WriteLine("          assemblies = <modRoot>\\src\\*\\bin (or %USERPROFILE%\\.hdbf\\work\\bin with --assemblies).");
    }

    private static string GuessModRoot(string dataDir)
    {
        var parent = Directory.GetParent(dataDir);
        if (parent is not null
            && string.Equals(Path.GetFileName(dataDir), "data", StringComparison.OrdinalIgnoreCase))
        {
            return parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? ResolveManagedDir(string modRoot, string? explicitManagedDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitManagedDir))
        {
            return Path.GetFullPath(explicitManagedDir);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("TIMBERBORN_MANAGED_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var propsPath = Path.Combine(modRoot, "Directory.Build.props");
        if (File.Exists(propsPath))
        {
            var match = Regex.Match(File.ReadAllText(propsPath),
                @"<TimberbornDir[^>]*>([^<]+)</TimberbornDir>", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var candidate = Path.Combine(match.Groups[1].Value.Trim(), "Timberborn_Data", "Managed");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var steam = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "Timberborn", "Timberborn_Data", "Managed");
        return steam;
    }

    private static ModAssemblyDiscovery DiscoverModAssemblies(string modRoot, string? buildOutputRoot)
    {
        var discovery = new ModAssemblyDiscovery();

        string? searchRoot;
        if (!string.IsNullOrWhiteSpace(buildOutputRoot))
        {
            // New layout: build outputs live outside the workspace (%USERPROFILE%\.hdbf\work\bin),
            // one folder per project: <binRoot>/<Project>/<Configuration>/<tfm>/<AssemblyName>.dll.
            // The root also contains tool/test projects (ApiIndex, HDBF.Tests, validate), so the
            // whitelist below — not the folder name — decides what counts.
            searchRoot = Path.GetFullPath(buildOutputRoot);
            discovery.SearchRoots.Add(searchRoot);
            if (!Directory.Exists(searchRoot))
            {
                discovery.Notes.Add($"assemblies root not found: {searchRoot}");
                searchRoot = null;
            }
        }
        else
        {
            // Legacy fallback: <modRoot>\src\<Project>\bin\<Configuration>\<tfm>\<Project>.dll.
            searchRoot = Path.Combine(modRoot, "src");
            discovery.SearchRoots.Add(searchRoot);
            if (!Directory.Exists(searchRoot))
            {
                discovery.Notes.Add($"default assemblies root not found: {searchRoot} (build the mod first)");
                searchRoot = null;
            }
        }

        if (searchRoot is not null)
        {
            var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var expectedName in ExpectedModAssemblies)
            {
                candidates[expectedName] = new List<string>();
            }

            foreach (var dll in EnumerateDlls(searchRoot))
            {
                var fileName = Path.GetFileNameWithoutExtension(dll);
                var looksExpected = ExpectedModAssemblies.Contains(fileName, StringComparer.Ordinal);

                string actualName;
                try
                {
                    // Metadata only: the assembly is never loaded for execution. This is also what
                    // rejects a renamed/copied file — the assembly's own identity is what counts.
                    actualName = AssemblyName.GetAssemblyName(dll).Name ?? string.Empty;
                }
                catch (Exception ex)
                {
                    if (looksExpected)
                    {
                        discovery.Notes.Add($"could not read {dll}: {ex.GetType().Name}; expected assembly {fileName}");
                    }

                    continue;
                }

                if (!ExpectedModAssemblies.Contains(actualName, StringComparer.Ordinal))
                {
                    if (looksExpected)
                    {
                        discovery.Notes.Add($"rejected {dll}: AssemblyName is '{actualName}', expected '{fileName}'");
                    }

                    continue;
                }

                if (looksExpected && !string.Equals(fileName, actualName, StringComparison.Ordinal))
                {
                    discovery.Notes.Add(
                        $"name mismatch: {dll} is named {fileName}.dll but its AssemblyName is '{actualName}'; "
                        + $"only the assembly identity counts, so it is not a {fileName} candidate");
                }

                candidates[actualName].Add(dll);
            }

            foreach (var expectedName in ExpectedModAssemblies)
            {
                var selected = SelectPreferredAssembly(searchRoot, expectedName, candidates[expectedName]);
                if (selected is null)
                {
                    discovery.Missing.Add(expectedName);
                }
                else
                {
                    discovery.Assemblies.Add(selected);
                }
            }
        }
        else
        {
            discovery.Missing.AddRange(ExpectedModAssemblies);
        }

        discovery.Assemblies.Sort(StringComparer.OrdinalIgnoreCase);
        return discovery;
    }

    /// <summary>Recursive *.dll scan that skips unreadable directories instead of aborting.</summary>
    private static IEnumerable<string> EnumerateDlls(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                pending.Enqueue(subdirectory);
            }
        }
    }

    /// <summary>
    /// Picks one path per expected assembly. Most output folders contain copies of the same
    /// dependency assembly (HDBF.Core.dll sits in every project's output), so prefer the project's
    /// own folder, then Release over Debug, then netstandard2.1 (the TFM that ships) over any other
    /// TFM, then a stable path order.
    /// </summary>
    private static string? SelectPreferredAssembly(string searchRoot, string expectedName, List<string> candidates)
    {
        return candidates
            .OrderBy(path => ProjectFolderRank(searchRoot, expectedName, path))
            .ThenBy(path => ConfigurationRank(searchRoot, path))
            .ThenBy(path => FrameworkRank(searchRoot, path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int ProjectFolderRank(string searchRoot, string expectedName, string path)
    {
        var segments = RelativeSegments(searchRoot, path);
        return segments.Length > 0 && string.Equals(segments[0], expectedName, StringComparison.Ordinal)
            ? 0
            : 1;
    }

    private static int ConfigurationRank(string searchRoot, string path)
    {
        var segments = RelativeSegments(searchRoot, path);
        if (segments.Any(s => string.Equals(s, "Release", StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        return segments.Any(s => string.Equals(s, "Debug", StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
    }

    private static int FrameworkRank(string searchRoot, string path)
    {
        var segments = RelativeSegments(searchRoot, path);
        if (segments.Any(s => string.Equals(s, "netstandard2.1", StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        return segments.Any(s => string.Equals(s, "net9.0", StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
    }

    private static string[] RelativeSegments(string root, string path) =>
        Path.GetRelativePath(root, path).Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// One auditable assertion line: the five expected HDBF assemblies must be discovered AND
    /// readable. Anything else fails the run through ReportAssemblyDiscoveryProblems below.
    /// </summary>
    private static string DescribeAssemblyCheck(ModAssemblyDiscovery discovery, int loadedCount)
    {
        var expected = ExpectedModAssemblies.Length;
        var ok = discovery.Missing.Count == 0
                 && discovery.Assemblies.Count == expected
                 && loadedCount == expected;

        if (ok)
        {
            return $"{loadedCount}/{expected} OK (expected {expected})";
        }

        var detail = discovery.Missing.Count > 0
            ? $"missing: {string.Join(", ", discovery.Missing)}"
            : $"{discovery.Assemblies.Count} discovered, {loadedCount} readable";
        return $"{loadedCount}/{expected} FAIL (expected {expected}; {detail})";
    }

    /// <summary>
    /// Missing assemblies are a hard error (exit code 1), never a silent downgrade to 4: the
    /// report lists which assembly is missing and which root(s)/layout were searched for it.
    /// </summary>
    private static void ReportAssemblyDiscoveryProblems(ModAssemblyDiscovery discovery, Report report)
    {
        if (discovery.Missing.Count == 0)
        {
            return;
        }

        report.Error("assemblies",
            $"expected {ExpectedModAssemblies.Length} HDBF mod assemblies, found {discovery.Assemblies.Count}; "
            + $"missing: {string.Join(", ", discovery.Missing.Select(name => name + ".dll"))}");

        foreach (var missing in discovery.Missing)
        {
            report.Error("assemblies",
                $"missing mod assembly: {missing}.dll (no *.dll with AssemblyName '{missing}' was found)");
        }

        foreach (var root in discovery.SearchRoots)
        {
            report.Error("assemblies",
                $"searched root: {root} (recursively, *.dll; accepted AssemblyName: {string.Join(", ", ExpectedModAssemblies)})");
            report.Error("assemblies",
                $"expected layout: {root}\\<AssemblyName>\\<Release|Debug>\\<tfm>\\<AssemblyName>.dll");
        }

        foreach (var note in discovery.Notes)
        {
            report.Error("assemblies", note);
        }
    }

    private sealed class ModAssemblyDiscovery
    {
        public List<string> Assemblies { get; } = new();

        public List<string> Missing { get; } = new();

        public List<string> SearchRoots { get; } = new();

        public List<string> Notes { get; } = new();
    }
}
