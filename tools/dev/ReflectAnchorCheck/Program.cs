using System.Reflection;
using HDBF.Core.Compat;
using HDBF.Integration.Reflection;

namespace HDBF.Tools.ReflectAnchorCheck;

/// <summary>
/// Offline runtime-path verifier for the compatibility anchors.
///
/// It loads the 660 installed game assemblies plus the six assemblies of the five target mods into
/// an ordinary .NET process and then calls the production reflection gateway
/// (<see cref="ExternalProxy"/>). Success means the exact code the game will execute can resolve
/// every anchor type and member signature against the real third-party DLLs; it does NOT mean the
/// mods were loaded by Timberborn (that remains the Lead's in-game matrix step).
///
/// Usage:
///   ReflectAnchorCheck [managedDir] [workshopContentRoot]
/// Exit codes: 0 = all anchors resolved, 1 = any type/member miss, 2 = arguments/paths invalid.
/// </summary>
internal static class Program
{
    private const string DefaultManagedDir =
        @"C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed";

    private const string DefaultWorkshopRoot =
        @"C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090";

    private static readonly string[] ModDllRelativePaths =
    {
        @"3801726769\version-1.1\TimberPipes.dll",
        @"3766893705\version-1.1\PowerLines.dll",
        @"3815376360\version-1.1\ConveyorBelt.dll",
        @"3815376360\version-1.1\ConveyorBelt.Algorithm.dll",
        @"3723216060\version-1.1.0.2\Scripts\More Mines.dll",
        @"3682012904\Version-1.1\hoomanstairs.dll",
    };

    private static int Main(string[] args)
    {
        var managedDir = args.Length > 0 ? args[0] : DefaultManagedDir;
        var workshopRoot = args.Length > 1 ? args[1] : DefaultWorkshopRoot;

        if (!Directory.Exists(managedDir))
        {
            Console.Error.WriteLine("managed directory not found: " + managedDir);
            return 2;
        }

        if (!Directory.Exists(workshopRoot))
        {
            Console.Error.WriteLine("workshop content root not found: " + workshopRoot);
            return 2;
        }

        Console.WriteLine("HDBF runtime anchor probe (production ExternalProxy path)");
        Console.WriteLine("managed dir: " + managedDir);
        Console.WriteLine("workshop:    " + workshopRoot);

        var gameLoaded = 0;
        var gameFailed = 0;
        foreach (var dll in Directory.GetFiles(managedDir, "*.dll"))
        {
            try
            {
                Assembly.LoadFrom(dll);
                gameLoaded++;
            }
            catch (Exception)
            {
                gameFailed++;
            }
        }

        Console.WriteLine($"game assemblies loaded: {gameLoaded} (failed: {gameFailed})");

        var modLoaded = 0;
        foreach (var relative in ModDllRelativePaths)
        {
            var path = Path.Combine(workshopRoot, relative);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("missing mod dll: " + path);
                return 2;
            }

            try
            {
                Assembly.LoadFrom(path);
                modLoaded++;
                Console.WriteLine("  loaded mod dll: " + Path.GetFileName(path));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"  failed to load {path}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"mod assemblies loaded: {modLoaded}/{ModDllRelativePaths.Length}");

        // Detection strings must resolve against the same loaded assembly set the game would scan.
        var loadedNames = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = assembly.GetName().Name;
                if (!string.IsNullOrEmpty(name))
                {
                    loadedNames.Add(CompatMatching.Normalize(name));
                }
            }
            catch (Exception)
            {
                // An unreadable assembly cannot participate in detection; the scan tolerates it.
            }
        }

        var pass = 0;
        var fail = 0;
        var missing = 0;
        var detectionFail = 0;

        foreach (var adapter in CompatAnchorTable.All)
        {
            var detected = false;
            foreach (var normalized in loadedNames)
            {
                if (CompatMatching.MatchesAssemblyName(normalized, adapter.AssemblyNameTokens))
                {
                    detected = true;
                    break;
                }
            }

            if (!detected)
            {
                detectionFail++;
                Console.WriteLine($"FAIL    detection: {adapter.AdapterId} did not match any loaded assembly");
            }
            else
            {
                Console.WriteLine($"PASS    detection: {adapter.AdapterId}");
            }

            foreach (var anchor in adapter.ApiAnchors)
            {
                Type? type;
                try
                {
                    type = ExternalProxy.FindType(anchor.AssemblyName, anchor.TypeName) ??
                           ExternalProxy.FindType(null, anchor.TypeName);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"FAIL    {anchor.TypeName}: type lookup threw {exception.GetType().Name}");
                    fail++;
                    continue;
                }

                if (type is null)
                {
                    missing++;
                    Console.WriteLine($"MISSING {anchor.TypeName}");
                    continue;
                }

                if (anchor.IsTypeOnly)
                {
                    pass++;
                    Console.WriteLine($"PASS    {anchor.TypeName}");
                    continue;
                }

                MethodInfo? method;
                try
                {
                    method = ExternalProxy.FindMethodBySignature(type, anchor.MemberName,
                        anchor.ParameterTypeNames, anchor.ReturnTypeName);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"FAIL    {anchor.SignatureText}: lookup threw {exception.GetType().Name}");
                    fail++;
                    continue;
                }

                if (method is null)
                {
                    fail++;
                    Console.WriteLine($"FAIL    {anchor.SignatureText}");
                    continue;
                }

                pass++;
                Console.WriteLine($"PASS    {anchor.SignatureText}");
                Console.WriteLine($"        resolved: {method}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"SUMMARY pass={pass} fail={fail} missing={missing} detectionFail={detectionFail}");
        Console.WriteLine(fail == 0 && missing == 0 && detectionFail == 0
            ? "RESULT: OK - production reflection resolves every verified anchor."
            : "RESULT: FAILED");

        return fail == 0 && missing == 0 && detectionFail == 0 ? 0 : 1;
    }
}
