// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GATE-02/03
// Purpose: NFR-GATE-02/GATE-03 (REQ-V2-READ-02/READ-03, MAINT-01/02/05, CORE-03/04):
//          source contracts of the five assemblies - section-19.18.1-B file headers,
//          XML documentation on the v2 public surface, no unregistered TODO/FIXME or
//          debug entry point, registered log keys only, architecture dependency
//          direction (no cycle, Core reference-free, removed v1 plot/Harmony paths)
//          and the 300-line soft gate. Read-only src file IO through HdbfRepoRoot.
// Requirements: REQ-V2-READ-02, REQ-V2-READ-03, REQ-V2-MAINT-01, REQ-V2-MAINT-02,
//               REQ-V2-MAINT-05, REQ-V2-CORE-03, REQ-V2-CORE-04, REQ-V2-SAFE-05,
//               REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace HDBF.Tests
{
    public static class V2NfrCodeContractTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string SrcRoot = "src";

        private static readonly string[] Projects =
        {
            "HDBF.Core", "HDBF.Persistence", "HDBF.Gameplay", "HDBF.UI", "HDBF.Integration",
        };

        private static readonly string[] AllowedLogPrefixes =
        {
            "indoor.", "water.", "save.", "ui.", "compat.", "config.", "gameplay.",
        };

        private static readonly string[] ForbiddenDebugStrings =
        {
            "Cheat", "GodMode", "UnlockAll", "DebugPanel", "FarmDiagnostics",
        };

        /// <summary>
        /// Legacy v1 source files that predate section 19.18.1-B (no HDBF v2 header by design).
        /// Every src file that is neither v2-marked nor one of the v2 directories/files below must
        /// match one of these prefixes, otherwise it is treated as a new v2 file that forgot its
        /// header (NFR-GATE-02). The reason is part of the gate contract, not a comment.
        /// </summary>
        private static readonly (string Prefix, string Reason)[] LegacySourcePolicy =
        {
            ("src/HDBF.Core/Abstractions/", "v1 command/query/enums contract (pre-v2); v2 additions live in Core/Indoor"),
            ("src/HDBF.Core/Balance/", "v1 balance formula table reused by v2; unchanged platform file"),
            ("src/HDBF.Core/Compat/", "v1 compatibility contract; the v2 IIndoorAdapterHost.cs carries a v2 header and is enforced"),
            ("src/HDBF.Core/Config/", "v1 config layer; v2 schema-2 edits live in the v2-marked .Sections/.Tiers partials"),
            ("src/HDBF.Core/Farming/", "v1 path probe; v2 farms have no world path"),
            ("src/HDBF.Core/Formatting/", "v1 panel number formatting used by the v2 read model"),
            ("src/HDBF.Core/Formulas/", "v1 build-cost probe; unchanged platform helper"),
            ("src/HDBF.Core/Json/", "v1 dependency-free JSON reader/writer (no NuGet); not touched by v2 cards"),
            ("src/HDBF.Core/Logging/", "v1 logging contract (IHdbfLog/ThrottledLog) consumed by v2"),
            ("src/HDBF.Gameplay/HdbfModStarter.cs", "v1 mod bootstrap; v2 DI lives in HdbfGameConfigurator.cs"),
            ("src/HDBF.Gameplay/Compat/", "v1 PolySharp-style IsExternalInit shim"),
            ("src/HDBF.Gameplay/Components/HdbfSystemRegistrar.cs", "v1 DI/log/config registrar; v2 registrations were added through HdbfGameConfigurator.cs"),
            ("src/HDBF.Integration/HdbfIntegrationConfigurator.cs", "v1 integration DI bootstrap"),
            ("src/HDBF.Integration/IntegrationLog.cs", "v1 Unity player-log sink behind IHdbfLog"),
            ("src/HDBF.Integration/TestHooks.cs", "v1 test seam (release-gates testSeams.playerFacingEntryPoints=false); not reachable from gameplay/UI"),
            ("src/HDBF.Integration/Adapters/", "v1 five-adapter platform; v2 adapter files carry v2 headers and are enforced"),
            ("src/HDBF.Integration/Detection/", "v1 session mod detector (chapter 8.2); v2 delegates through IIndoorAdapterHost"),
            ("src/HDBF.Integration/Reflection/", "v1 cached reflection gateway (chapter 8.2)"),
            ("src/HDBF.UI/HdbfUiConfigurator.cs", "v1 UI DI bootstrap; v2 panels carry v2 headers"),
            ("src/HDBF.UI/Compat/", "v1 PolySharp-style IsExternalInit shim"),
            ("src/HDBF.UI/Panels/HdbfEntityPanelModuleProvider.cs", "v1 entity-panel module provider; v2 fragment partials carry v2 headers"),
        };

        /// <summary>
        /// v1 log keys that are outside the v2 prefix set (§19.18.1-B) but still exist in the
        /// untouched v1 platform files. Both are registered v1 diagnostics keys; no v2 call site
        /// emits them, so they cannot pollute a v2 player log. Any additional out-of-set key fails.
        /// </summary>
        private static readonly Dictionary<string, string> LegacyLogKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "port.fault", "v1 key registered in docs/INTERFACES.md (v1 contract); dead constant, no call site" },
            { "startup", "v1 HdbfSystemRegistrar bootstrap throttle key; v1-only, no prefix by design" },
        };

        /// <summary>
        /// Legacy v1 implementation files above the v2 300-line soft gate (§19.18.2-C). They are
        /// not v2 implementation files: the v2 gate applies to v2-scope files, but the oversized
        /// legacy list is pinned here with a written reason so a new oversized file cannot slip in
        /// without a decision (REQ-V2-MAINT-02).
        /// </summary>
        private static readonly Dictionary<string, string> LegacyLargeFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {
                "src/HDBF.Core/Balance/BalanceFormulas.cs",
                "v1 formula table (chapters 5-6): one pure static function per requirement entry; splitting it would break the single-implementation contract, v2 formulas live in Core/Indoor"
            },
            {
                "src/HDBF.Core/Compat/CompatAnchorTable.cs",
                "v1 verified reflection anchor data table (chapter 8.2): data plus tiny spec type, no control flow to split"
            },
            {
                "src/HDBF.Integration/Detection/CompatDetector.cs",
                "v1 one-shot session detector (chapter 8.2) used by the v1 adapters; its cache/probe/timeout branches form one contract"
            },
            {
                "src/HDBF.Integration/Reflection/ExternalProxy.cs",
                "v1 single cached reflection gateway (chapter 8.2); the cache and lookup halves are one frozen INTERFACES.md type"
            },
            {
                "src/HDBF.Integration/Adapters/ReflectionCompatAdapter.cs",
                "v1 shared behaviour of the five v1 adapters (chapter 8.2); retained v1 type, not a v2 controller"
            },
        };

        private static readonly Regex PublicTypePattern = new Regex(
            @"^\s*public\s+(?:(?:sealed|abstract|static|partial|readonly|ref)\s+)*(class|record|struct|enum|interface)\s+([A-Za-z_]\w*)",
            RegexOptions.Compiled);

        private static readonly Regex TodoPattern = new Regex(@"\b(TODO|FIXME)\b", RegexOptions.Compiled);

        /// <summary>Static field declarations that keep mutable state across buildings/sessions.</summary>
        private static readonly Regex StaticMutableFieldPattern = new Regex(
            @"^\s*(?:(?:private|internal|public|protected)\s+)?static\s+(?!readonly\b)(?!const\b)(?<type>[A-Za-z_][\w<>,\[\]\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*;",
            RegexOptions.Compiled);

        /// <summary>Initialized static fields; "=&gt;" expression-bodied properties are excluded by the caller.</summary>
        private static readonly Regex StaticMutableInitializedFieldPattern = new Regex(
            @"^\s*(?:(?:private|internal|public|protected)\s+)?static\s+(?!readonly\b)(?!const\b)(?<type>[A-Za-z_][\w<>,\[\]\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*=(?!\s*>)(?!=)",
            RegexOptions.Compiled);

        private static readonly Regex ProblemIdPattern = new Regex(@"\b[A-Z]{2,6}-\d+\b", RegexOptions.Compiled);

        private static readonly Regex LogKeyConstantPattern = new Regex(
            @"const\s+string\s+LogKey\w*\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex LogKeysClassConstantPattern = new Regex(
            @"public\s+const\s+string\s+\w+\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex DirectLogKeyLiteralPattern = new Regex(
            @"\.(?:Write|Error)\(\s*HdbfLogCategory\.\w+\s*,\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex ProjectReferencePattern = new Regex(
            @"<ProjectReference\s+Include\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex CompileReferencePattern = new Regex(
            @"<Reference\s+Include\s*=|PackageReference\s+Include\s*=", RegexOptions.Compiled);

        // ------------------------------------------------------------------ NFR-GATE-02: headers

        public static void TestEveryV2SourceFileCarriesTheHeaderTemplate()
        {
            var required = 0;
            var selfConsistent = 0;
            var legacy = 0;
            var missing = new List<string>();

            foreach (var file in SourceFiles())
            {
                var header = HeaderBlock(file.Lines);
                var marked = header.Contains("HDBF v2 |", StringComparison.Ordinal);
                var isRequired = IsRequiredV2File(file.Relative);

                if (marked)
                {
                    selfConsistent++;
                    RequireHeaderField(file, header, "Purpose:", missing);
                    RequireHeaderField(file, header, "Requirements:", missing);
                    RequireHeaderField(file, header, "Layer:", missing);
                    RequireHeaderField(file, header, "Owner:", missing);
                }

                if (isRequired)
                {
                    required++;
                    if (!marked)
                    {
                        missing.Add(file.Relative + ": missing the section-19.18.1-B header block (HDBF v2 | Layer | Owner | Purpose | Requirements)");
                    }
                    else
                    {
                        RequireHeaderField(file, header, "Purpose:", missing);
                        RequireHeaderField(file, header, "Requirements:", missing);
                        // The template is a fixed header: line 1 is the banner, line 2 carries
                        // "HDBF v2 | Layer | Owner" and line 3 starts "Purpose:".
                        TestAssert.True(file.Lines.Length >= 3
                                        && file.Lines[1].Contains("HDBF v2 |", StringComparison.Ordinal)
                                        && file.Lines[2].Contains("Purpose:", StringComparison.Ordinal),
                            file.Relative + ": the v2 header must start with the banner/HDBF v2/Purpose lines of section 19.18.1-B (got '"
                            + (file.Lines.Length > 2 ? file.Lines[1].Trim() + "' / '" + file.Lines[2].Trim() : "short file") + "')");
                    }
                }

                if (!marked && !isRequired)
                {
                    if (LegacyReason(file.Relative) is null)
                    {
                        missing.Add(file.Relative
                                   + ": neither a v2-marked file nor in the legacy policy; a new source file must carry the v2 header template");
                    }
                    else
                    {
                        legacy++;
                    }
                }
            }

            missing.Sort(StringComparer.Ordinal);
            TestAssert.Equal(0, missing.Count,
                "section 19.18.1-B header template violations (" + missing.Count + "): "
                + string.Join(" | ", missing));
            TestAssert.True(required >= 60,
                "only " + required + " mandatory v2 files found; the v2 directories/files are not being scanned");
            TestAssert.True(selfConsistent >= 80,
                "only " + selfConsistent + " v2-marked files found; expected the full v2 source surface");

            Console.WriteLine("[nfr-gate-02] header template: " + required + " mandatory v2 files, "
                              + selfConsistent + " v2-marked files, " + legacy + " documented v1 legacy files");
        }

        // ------------------------------------------------------------------ NFR-GATE-02: XML docs

        public static void TestV2PublicTypesCarryXmlDocumentation()
        {
            var files = SourceFiles();
            var documentedTypes = CollectDocumentedTypeNames(files);
            var checkedTypes = 0;
            var failures = new List<string>();

            foreach (var file in files)
            {
                if (!IsV2SurfaceFile(file))
                {
                    continue;
                }

                for (var index = 0; index < file.Lines.Length; index++)
                {
                    var match = PublicTypePattern.Match(file.Lines[index]);
                    if (!match.Success)
                    {
                        continue;
                    }

                    checkedTypes++;
                    var typeName = match.Groups[2].Value;
                    if (HasXmlDocAbove(file.Lines, index))
                    {
                        continue;
                    }

                    if (file.Lines[index].Contains("partial", StringComparison.Ordinal)
                        && documentedTypes.Contains(typeName))
                    {
                        // A partial declaration documents the type on its primary part.
                        continue;
                    }

                    failures.Add(file.Relative + ":" + (index + 1) + " public " + match.Groups[1].Value
                                 + " " + typeName + " has no /// <summary> comment (REQ-V2-READ-02)");
                }
            }

            TestAssert.Equal(0, failures.Count,
                "undocumented v2 public types (" + failures.Count + "): " + string.Join(" | ", failures));
            TestAssert.True(checkedTypes >= 60,
                "only " + checkedTypes + " public v2 types were inspected; the scan looks empty");

            Console.WriteLine("[nfr-gate-02] XML documentation: " + checkedTypes
                              + " public v2 types checked, 0 undocumented");
        }

        // ------------------------------------------------------------------ NFR-GATE-02/03: maintainability

        public static void TestDailyTickOrderAndCoreFormulasArePinned()
        {
            // ARCHITECTURE section 3 fixes the daily order; the phases are labelled in code so a
            // future edit cannot silently reorder growth before water or maintenance before actions.
            var tick = RepoRead("src/HDBF.Core/Indoor/IndoorDayTick.cs");
            var previous = -1;
            for (var phase = 1; phase <= 7; phase++)
            {
                var marker = "// Phase " + phase + ":";
                var position = tick.IndexOf(marker, StringComparison.Ordinal);
                TestAssert.True(position >= 0,
                    "IndoorDayTick.cs must keep the fixed day-tick phase " + phase
                    + " of ARCHITECTURE section 3 (REQ-V2-MAINT-03)");
                TestAssert.True(position > previous,
                    "day-tick phase " + phase + " must run after phase " + (phase - 1));
                previous = position;
            }

            foreach (var module in new[]
                     {
                         "IndoorWaterMath", "IndoorGrowthMath", "IndoorLaborMath",
                         "IndoorMaintenanceMath", "IndoorPowerMath", "IndoorMultipliers",
                     })
            {
                TestAssert.True(File.Exists(RepoPath("src/HDBF.Core/Indoor/" + module + ".cs")),
                    "the " + module + " formulas must stay in HDBF.Core pure modules (REQ-V2-MAINT-03)");
            }

            Console.WriteLine("[nfr-gate-02] day tick: 7 fixed phases in order; formulas live in Core pure modules");
        }

        public static void TestNoStaticMutableStateInV2Sources()
        {
            var violations = new List<string>();
            foreach (var file in SourceFiles())
            {
                if (!IsV2SurfaceFile(file))
                {
                    continue;
                }

                for (var index = 0; index < file.Lines.Length; index++)
                {
                    var line = file.Lines[index];
                    if (line.Contains("=>", StringComparison.Ordinal))
                    {
                        // Expression-bodied members are state-free static helpers, not fields.
                        continue;
                    }

                    if (StaticMutableFieldPattern.IsMatch(line)
                        || StaticMutableInitializedFieldPattern.IsMatch(line))
                    {
                        violations.Add(file.Relative + ":" + (index + 1) + ": " + line.Trim());
                    }
                }
            }

            violations.Sort(StringComparer.Ordinal);
            TestAssert.Equal(0, violations.Count,
                "static mutable state in v2 sources (REQ-V2-MAINT-04): " + string.Join(" | ", violations));
            Console.WriteLine("[nfr-gate-02] no static mutable fields in the v2 source surface");
        }

        // ------------------------------------------------------------------ NFR-GATE-02: TODOs/debug

        public static void TestNoUnregisteredTodosOrDebugEntryPoints()
        {
            var files = SourceFiles();
            var unregistered = new List<string>();
            var registered = 0;
            var debugHits = new List<string>();
            var testSeamViolations = new List<string>();

            foreach (var file in files)
            {
                for (var index = 0; index < file.Lines.Length; index++)
                {
                    var line = file.Lines[index];
                    if (TodoPattern.IsMatch(line))
                    {
                        if (line.Contains("REQ-V2-", StringComparison.Ordinal) || ProblemIdPattern.IsMatch(line))
                        {
                            registered++;
                        }
                        else
                        {
                            unregistered.Add(file.Relative + ":" + (index + 1) + ": " + line.Trim());
                        }
                    }

                    foreach (var token in ForbiddenDebugStrings)
                    {
                        if (line.Contains(token, StringComparison.Ordinal))
                        {
                            debugHits.Add(file.Relative + ":" + (index + 1) + ": " + token);
                        }
                    }
                }

                if (file.Text.Contains("TestHooks", StringComparison.Ordinal)
                    && !file.Relative.StartsWith("src/HDBF.Integration/", StringComparison.Ordinal))
                {
                    testSeamViolations.Add(file.Relative);
                }
            }

            TestAssert.Equal(0, unregistered.Count,
                "unregistered TODO/FIXME markers (" + unregistered.Count + "): " + string.Join(" | ", unregistered));
            TestAssert.Equal(0, debugHits.Count,
                "debug/cheat entry points in src (" + debugHits.Count + "): " + string.Join(" | ", debugHits));
            TestAssert.Equal(0, testSeamViolations.Count,
                "the TestHooks test seam must stay inside HDBF.Integration (not reachable from gameplay/UI): "
                + string.Join(", ", testSeamViolations));

            Console.WriteLine("[nfr-gate-02] debug surface: 0 forbidden strings, 0 unregistered TODO/FIXME, "
                              + registered + " registered marker line(s); TestHooks confined to HDBF.Integration");
        }

        // ------------------------------------------------------------------ NFR-GATE-02: log keys

        public static void TestLogKeysUseOnlyRegisteredPrefixes()
        {
            var files = SourceFiles();
            var allKeys = new HashSet<string>(StringComparer.Ordinal);
            var unknown = new List<string>();
            var families = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var isKeysClass = file.Relative.EndsWith("LogKeys.cs", StringComparison.Ordinal);
                if (isKeysClass)
                {
                    foreach (Match match in LogKeysClassConstantPattern.Matches(file.Text))
                    {
                        AddLogKey(file.Relative, match.Groups[1].Value, allKeys, unknown);
                    }
                }

                foreach (Match match in LogKeyConstantPattern.Matches(file.Text))
                {
                    AddLogKey(file.Relative, match.Groups[1].Value, allKeys, unknown);
                }

                foreach (Match match in DirectLogKeyLiteralPattern.Matches(file.Text))
                {
                    AddLogKey(file.Relative, match.Groups[1].Value, allKeys, unknown);
                }

                // The v1 plot-scan diagnostics namespace was deleted with the world-plot chain
                // (section 19.2); it must never come back under any spelling.
                TestAssert.False(file.Text.Contains("\"diag.", StringComparison.Ordinal)
                                 || file.Text.Contains("\"scan.", StringComparison.Ordinal)
                                 || file.Text.Contains("'diag.", StringComparison.Ordinal)
                                 || file.Text.Contains("'scan.", StringComparison.Ordinal),
                    file.Relative + " reintroduces a diag./scan. log key (section 19.18.1-B)");
            }

            foreach (var key in allKeys)
            {
                if (HasAllowedPrefix(key))
                {
                    var dot = key.IndexOf('.');
                    families.Add(dot > 0 ? key.Substring(0, dot) : key);
                }
            }

            unknown.Sort(StringComparer.Ordinal);
            TestAssert.Equal(0, unknown.Count,
                "log key(s) outside the registered v2 prefixes (indoor./water./save./ui./compat./config./gameplay.): "
                + string.Join(" | ", unknown));
            TestAssert.True(allKeys.Count >= 40, "only " + allKeys.Count + " log keys found; the scan looks incomplete");

            // Every v2 key family of section 19.18.1-B must actually be declared by the v2 registry.
            var v2Registry = RepoRead("src/HDBF.Core/Indoor/IndoorLogKeys.cs");
            foreach (var family in new[] { "indoor", "water", "save", "ui", "compat" })
            {
                TestAssert.True(v2Registry.Contains("\"" + family + ".", StringComparison.Ordinal),
                    "IndoorLogKeys.cs does not declare any " + family + ".* key");
            }

            Console.WriteLine("[nfr-gate-02] log keys: " + allKeys.Count + " keys, families "
                              + string.Join("/", families) + ", 0 outside the registered prefixes, "
                              + LegacyLogKeys.Count + " pinned v1 legacy key(s), 0 diag./scan.");
        }

        // ------------------------------------------------------------------ NFR-GATE-03: architecture

        public static void TestArchitectureDependencyDirectionsAndNoCycles()
        {
            var references = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var project in Projects)
            {
                references[project] = ProjectReferencesOf("src/" + project + "/" + project + ".csproj");
            }

            references["HDBF.Tests"] = ProjectReferencesOf("tests/HDBF.Tests/HDBF.Tests.csproj");

            AssertSameSet("HDBF.Core", references["HDBF.Core"], Array.Empty<string>());
            AssertSameSet("HDBF.Persistence", references["HDBF.Persistence"], new[] { "HDBF.Core" });
            AssertSameSet("HDBF.Gameplay", references["HDBF.Gameplay"], new[] { "HDBF.Persistence" });
            AssertSameSet("HDBF.UI", references["HDBF.UI"], new[] { "HDBF.Core" });
            AssertSameSet("HDBF.Integration", references["HDBF.Integration"], new[] { "HDBF.Core" });
            AssertSameSet("HDBF.Tests", references["HDBF.Tests"], new[] { "HDBF.Core" });

            // GATE-03: direction "Core <- Persistence <- Gameplay" plus UI/Integration -> Core.
            foreach (var project in new[] { "HDBF.UI", "HDBF.Integration" })
            {
                TestAssert.False(Array.IndexOf(references[project], "HDBF.Gameplay") >= 0
                                 || Array.IndexOf(references[project], "HDBF.Persistence") >= 0,
                    project + " may only depend on HDBF.Core (section 19.18.2-A)");
            }

            AssertNoCycles(references);

            // Core must stay reference-free: no project/assembly/package reference at all, and no
            // using directive of a game assembly in any Core source file (REQ-V2-MAINT-01/CORE-04).
            var coreProject = RepoRead("src/HDBF.Core/HDBF.Core.csproj");
            TestAssert.False(CompileReferencePattern.IsMatch(coreProject),
                "HDBF.Core.csproj must not reference Unity/Timberborn or any package");
            foreach (var file in SourceFiles())
            {
                if (!file.Relative.StartsWith("src/HDBF.Core/", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var line in file.Lines)
                {
                    var trimmed = line.TrimStart();
                    TestAssert.False(trimmed.StartsWith("using UnityEngine", StringComparison.Ordinal)
                                     || trimmed.StartsWith("using Unity.", StringComparison.Ordinal)
                                     || trimmed.StartsWith("using Timberborn", StringComparison.Ordinal),
                        file.Relative + " puts a game assembly using directive into HDBF.Core (REQ-V2-CORE-04)");
                }
            }

            // Deleted v1 chains: the world-plot scanner, the planting dispatch and the Harmony patch
            // layer are gone (REQ-V2-CORE-03, section 19.2).
            var removed = new List<string>();
            RemovedPath("src/HDBF.Core/Plots", removed);
            RemovedPath("src/HDBF.Integration/Harmony", removed);
            RemovedPath("src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs", removed);
            if (Directory.GetFiles(RepoPath("src"), "HdbfPlotScanner*.cs", SearchOption.AllDirectories).Length > 0)
            {
                removed.Add("src/**/HdbfPlotScanner*.cs");
            }

            TestAssert.Equal(0, removed.Count,
                "v1 world-plot/Harmony source paths still exist (section 19.2): " + string.Join(", ", removed));

            var removedApiTokens = new[]
            {
                "PlantingService", "NaturalResourceFactory", "SetField(", "PlotRegistry", "HDBF.Core.Plots",
            };
            foreach (var file in SourceFiles())
            {
                if (file.Relative.StartsWith("src/HDBF.Core/Balance/", StringComparison.Ordinal)
                    || file.Relative.StartsWith("src/HDBF.Core/Json/", StringComparison.Ordinal))
                {
                    // v1 pure formula/JSON platform files keep their own vocabulary and never call
                    // the game API; they are not part of the gameplay removal contract.
                    continue;
                }

                foreach (var token in removedApiTokens)
                {
                    TestAssert.False(file.Text.Contains(token, StringComparison.Ordinal),
                        file.Relative + " still references the removed v1 world-plot/planting API '" + token
                        + "' (REQ-V2-CORE-03)");
                }
            }

            var graph = new List<string>();
            foreach (var pair in references)
            {
                graph.Add(pair.Key + " -> " + (pair.Value.Length == 0 ? "(none)" : string.Join(",", pair.Value)));
            }

            Console.WriteLine("[nfr-gate-03] dependencies: " + string.Join("; ", graph));
            Console.WriteLine("[nfr-gate-03] Core reference-free; removed v1 Plots/Harmony paths absent");
        }

        // ------------------------------------------------------------------ NFR-GATE-03: size gate

        public static void TestSourceFileSizeGate()
        {
            var oversizedV2 = new List<string>();
            var oversizedV2WithReason = new List<string>();
            var oversizedLegacy = new List<string>();
            var largestV2 = string.Empty;
            var largestV2Lines = 0;

            foreach (var file in SourceFiles())
            {
                var lines = file.Lines.Length;
                var v2Scope = IsV2SurfaceFile(file);
                if (v2Scope && lines > largestV2Lines)
                {
                    largestV2Lines = lines;
                    largestV2 = file.Relative;
                }

                if (lines <= 300)
                {
                    continue;
                }

                if (v2Scope)
                {
                    // REQ-V2-MAINT-02: an implementation file above 300 lines needs a written reason
                    // in the first 40 lines, otherwise it must be split.
                    var head = string.Join("\n", file.Lines, 0, Math.Min(40, file.Lines.Length));
                    if (head.Contains("REQ-V2-MAINT-02", StringComparison.Ordinal))
                    {
                        oversizedV2WithReason.Add(file.Relative + " (" + lines + " lines, reason documented)");
                    }
                    else
                    {
                        oversizedV2.Add(file.Relative + " (" + lines + " lines)");
                    }
                }
                else if (!LegacyLargeFiles.ContainsKey(file.Relative))
                {
                    oversizedLegacy.Add(file.Relative + " (" + lines + " lines)");
                }
            }

            TestAssert.Equal(0, oversizedV2.Count,
                "v2 files above the 300-line soft gate without a REQ-V2-MAINT-02 reason (section 19.18.2-C): "
                + string.Join(", ", oversizedV2));
            TestAssert.Equal(0, oversizedLegacy.Count,
                "oversized legacy file(s) with no written decision; add a reason to LegacyLargeFiles: "
                + string.Join(", ", oversizedLegacy));

            var legacyList = new List<string>(LegacyLargeFiles.Keys);
            legacyList.Sort(StringComparer.Ordinal);
            Console.WriteLine("[nfr-gate-03] size gate: largest v2 file " + largestV2 + " = " + largestV2Lines
                              + " lines; documented oversized v1 legacy files: " + string.Join(", ", legacyList));
            if (oversizedV2WithReason.Count > 0)
            {
                Console.WriteLine("[nfr-gate-03] v2 files above 300 lines with a written reason: "
                                  + string.Join(", ", oversizedV2WithReason));
            }
        }

        // ------------------------------------------------------------------ helpers: source scan

        private sealed class SourceFile
        {
            public SourceFile(string relative, string text)
            {
                Relative = relative;
                Text = text;
                var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
                if (normalized.EndsWith("\n", StringComparison.Ordinal))
                {
                    // A trailing newline terminates the last line; it does not add an empty one.
                    normalized = normalized.Substring(0, normalized.Length - 1);
                }

                Lines = normalized.Split('\n');
            }

            public string Relative { get; }

            public string Text { get; }

            public string[] Lines { get; }
        }

        private static List<SourceFile> SourceFiles()
        {
            var root = RepoPath(SrcRoot);
            TestAssert.True(Directory.Exists(root), "missing source root: " + root);
            var result = new List<SourceFile>();
            foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
                result.Add(new SourceFile(relative, File.ReadAllText(path, Encoding.UTF8)));
            }

            result.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
            TestAssert.True(result.Count >= 120,
                "only " + result.Count + " source files found; the src scan looks incomplete");
            return result;
        }

        private static bool IsRequiredV2File(string relative)
        {
            var path = relative.Replace('\\', '/');
            if (path.StartsWith("src/HDBF.Core/Indoor/", StringComparison.Ordinal)
                || path.StartsWith("src/HDBF.Persistence/Indoor/", StringComparison.Ordinal))
            {
                return true;
            }

            return path.StartsWith("src/HDBF.Gameplay/", StringComparison.Ordinal)
                   && IsGameplayIndoorFile(path.Substring("src/HDBF.Gameplay/".Length));
        }

        private static bool IsGameplayIndoorFile(string relativeToGameplay)
        {
            return relativeToGameplay.StartsWith("HdbfGameConfigurator.cs", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfBoxFarmController", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/GameCropCatalog", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfManualWaterSource", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfFarm", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfGameplayContracts.cs", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfIndoorCommandFailures.cs", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfMaintenanceTransaction.cs", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/HdbfWorldIconService", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Components/Indoor", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Farming/IndoorFarmWorkplaceBehavior.cs", StringComparison.Ordinal)
                   || relativeToGameplay.StartsWith("Specs/HdbfBoxFarmSpec.cs", StringComparison.Ordinal);
        }

        private static bool IsV2SurfaceFile(SourceFile file)
        {
            return IsRequiredV2File(file.Relative)
                   || HeaderBlock(file.Lines).Contains("HDBF v2 |", StringComparison.Ordinal);
        }

        private static string HeaderBlock(string[] lines)
        {
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    break;
                }

                builder.AppendLine(trimmed);
            }

            return builder.ToString();
        }

        private static void RequireHeaderField(SourceFile file, string header, string field, List<string> missing)
        {
            if (!header.Contains(field, StringComparison.Ordinal))
            {
                missing.Add(file.Relative + ": header is missing the '" + field + "' field of section 19.18.1-B");
                return;
            }

            // The field must be followed by real content, not an empty skeleton.
            var start = header.IndexOf(field, StringComparison.Ordinal) + field.Length;
            var end = header.IndexOf('\n', start);
            var value = end < 0 ? header.Substring(start) : header.Substring(start, end - start);
            if (value.Trim().Length < 2)
            {
                missing.Add(file.Relative + ": header field '" + field + "' has no value");
            }
        }

        private static string? LegacyReason(string relative)
        {
            var path = relative.Replace('\\', '/');
            foreach (var entry in LegacySourcePolicy)
            {
                if (path.StartsWith(entry.Prefix, StringComparison.Ordinal))
                {
                    return entry.Reason;
                }
            }

            return null;
        }

        private static HashSet<string> CollectDocumentedTypeNames(List<SourceFile> files)
        {
            var documented = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                for (var index = 0; index < file.Lines.Length; index++)
                {
                    var match = PublicTypePattern.Match(file.Lines[index]);
                    if (!match.Success || !HasXmlDocAbove(file.Lines, index))
                    {
                        continue;
                    }

                    documented.Add(match.Groups[2].Value);
                }
            }

            return documented;
        }

        private static bool HasXmlDocAbove(string[] lines, int index)
        {
            var from = Math.Max(0, index - 3);
            for (var probe = index - 1; probe >= from; probe--)
            {
                if (lines[probe].Contains("///", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddLogKey(string relative, string key, HashSet<string> allKeys, List<string> unknown)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            allKeys.Add(key);
            if (HasAllowedPrefix(key) || LegacyLogKeys.ContainsKey(key))
            {
                return;
            }

            unknown.Add(relative + ": \"" + key + "\"");
        }

        private static bool HasAllowedPrefix(string key)
        {
            foreach (var prefix in AllowedLogPrefixes)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ helpers: architecture

        private static string[] ProjectReferencesOf(string relativeProjectPath)
        {
            var text = RepoRead(relativeProjectPath);
            var result = new List<string>();
            foreach (Match match in ProjectReferencePattern.Matches(text))
            {
                var included = match.Groups[1].Value.Replace('\\', '/');
                result.Add(Path.GetFileNameWithoutExtension(included));
            }

            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private static void AssertSameSet(string project, string[] actual, string[] expected)
        {
            var actualSorted = new List<string>(actual);
            actualSorted.Sort(StringComparer.Ordinal);
            var expectedSorted = new List<string>(expected);
            expectedSorted.Sort(StringComparer.Ordinal);
            TestAssert.Equal(expectedSorted.Count, actualSorted.Count,
                project + " project-reference count (section 19.18.2-A): expected "
                + string.Join(",", expectedSorted) + ", got " + string.Join(",", actualSorted));
            for (var index = 0; index < expectedSorted.Count; index++)
            {
                TestAssert.Equal(expectedSorted[index], actualSorted[index],
                    project + " project reference #" + index);
            }
        }

        private static void AssertNoCycles(Dictionary<string, string[]> references)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in references.Keys)
            {
                Visit(project, references, visiting, visited);
            }
        }

        private static void Visit(string project, Dictionary<string, string[]> references,
            HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(project))
            {
                return;
            }

            TestAssert.True(visiting.Add(project),
                "circular project reference detected at " + project + " (section 19.18.2-A)");
            if (references.TryGetValue(project, out var children))
            {
                foreach (var child in children)
                {
                    Visit(child, references, visiting, visited);
                }
            }

            visiting.Remove(project);
            visited.Add(project);
        }

        private static void RemovedPath(string relative, List<string> removed)
        {
            var path = RepoPath(relative);
            if (File.Exists(path) || Directory.Exists(path))
            {
                removed.Add(relative);
            }
        }

        // ------------------------------------------------------------------ helpers: repo IO

        private static string RepoRead(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath), Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2NfrCodeContractTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach (var attribute in metadata)
            {
                if (string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(attribute.Value))
                {
                    return attribute.Value!;
                }
            }

            throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
        }
    }
}
