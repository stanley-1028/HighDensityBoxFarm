using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HDBF.Core.Compat;
using HDBF.Core.Logging;
using HDBF.Integration.Adapters;
using HDBF.Integration.Reflection;

namespace HDBF.Integration.Detection
{
    /// <summary>
    /// Detects the five optional mods once per session and caches the result per adapter id
    /// (chapter 8.2, REQ-COMPAT-06).
    ///
    /// Detection sources, in priority order:
    /// 1. the mod-manager list when the host supplies one (optional <c>modListProvider</c>);
    /// 2. an <c>AppDomain.CurrentDomain.GetAssemblies()</c> scan by assembly simple name;
    /// 3. the same scan by candidate type full name, then by simple type name.
    ///
    /// Hard requirements implemented here:
    /// * one session-wide scan: repeated <see cref="TryDetect"/> calls are cache hits;
    /// * total scan budget of <see cref="DetectionBudgetMs"/> (2 s, chapter 18.8
    ///   <c>compat.detectTimeoutSeconds</c>); an over-budget probe reports <c>detect-timeout</c>
    ///   and is treated as "not installed";
    /// * nothing throws: every reflection/metadata access is guarded (REQ-SAFE-01);
    /// * <see cref="Reset"/> / <see cref="Refresh"/> clear the cache for the manual "re-detect"
    ///   action of chapter 8.2, without forcing a running adapter to unload.
    /// </summary>
    public sealed class CompatDetector
    {
        /// <summary>Session detection budget in milliseconds (chapter 18.8, REQ-COMPAT-06).</summary>
        public const int DetectionBudgetMs = 2000;

        private sealed class AssemblyInfo
        {
            internal AssemblyInfo(Assembly assembly, string name, string normalizedName)
            {
                Assembly = assembly;
                Name = name;
                NormalizedName = normalizedName;
            }

            internal Assembly Assembly { get; }

            internal string Name { get; }

            internal string NormalizedName { get; }
        }

        private readonly IHdbfLog? _log;
        private readonly Func<IReadOnlyList<string>>? _modListProvider;
        private readonly Func<string, DetectionResult?>? _probeOverride;
        private readonly Dictionary<string, DetectionResult> _cache =
            new Dictionary<string, DetectionResult>(StringComparer.Ordinal);
        private readonly Stopwatch _sessionScan = new Stopwatch();

        private List<AssemblyInfo>? _assemblies;
        private IReadOnlyList<string>? _modListNames;
        private bool _modListRead;
        private string _lastDetectionSource = "Unknown";

        /// <summary>Creates the detector with an optional diagnostics sink.</summary>
        public CompatDetector(IHdbfLog? log = null)
            : this(log, null, null)
        {
        }

        /// <summary>
        /// Full constructor used by the registry (optional mod list) and by tests (probe override
        /// that simulates an installed / broken external mod without touching the filesystem).
        /// </summary>
        internal CompatDetector(IHdbfLog? log, Func<IReadOnlyList<string>>? modListProvider,
            Func<string, DetectionResult?>? probeOverride)
        {
            _log = log;
            _modListProvider = modListProvider;
            _probeOverride = probeOverride;
        }

        /// <summary>Number of distinct mods found during this session's detection.</summary>
        public int DetectedModCount => DetectedMods.Count;

        /// <summary>
        /// Mods found so far, in the fixed chapter 8.1 adapter order. Only probes that already ran
        /// are represented, so the property is meaningful after <see cref="Adapters.CompatAdapterRegistry"/>
        /// has registered its adapters.
        /// </summary>
        public IReadOnlyList<string> DetectedMods
        {
            get
            {
                var found = new List<string>(ThirdPartyProbes.All.Length);
                lock (_cache)
                {
                    for (var i = 0; i < ThirdPartyProbes.All.Length; i++)
                    {
                        var probe = ThirdPartyProbes.All[i];
                        if (_cache.TryGetValue(probe.AdapterId, out var result) && result.Detected &&
                            !string.IsNullOrEmpty(result.DetectedMod) && !found.Contains(result.DetectedMod!))
                        {
                            found.Add(result.DetectedMod!);
                        }
                    }
                }

                return found;
            }
        }

        /// <summary>Wall time spent in this session's detection, for report field <c>detectionDurationMs</c>.</summary>
        public int LastDetectionDurationMs
        {
            get
            {
                try
                {
                    var elapsed = _sessionScan.ElapsedMilliseconds;
                    return elapsed > int.MaxValue ? int.MaxValue : (int)elapsed;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>Source of the session detection: <c>ModList</c>, <c>AssemblyScan</c> or <c>Unknown</c>.</summary>
        public string LastDetectionSource => _lastDetectionSource;

        /// <summary>
        /// Probes one adapter id, returning the session-cached result when present. Returns false
        /// when the mod is absent, the id is unknown or any step failed; never throws.
        /// </summary>
        public bool TryDetect(string? adapterId, out DetectionResult result)
        {
            result = default;
            if (string.IsNullOrEmpty(adapterId))
            {
                return false;
            }

            try
            {
                lock (_cache)
                {
                    if (_cache.TryGetValue(adapterId!, out var cached))
                    {
                        result = cached;
                        return cached.Detected;
                    }
                }

                var probe = ThirdPartyProbes.ByAdapterId(adapterId);
                if (probe is null)
                {
                    result = new DetectionResult(false, null, null, DateTime.UtcNow.Ticks, "unknown-adapter");
                    Cache(adapterId!, result);
                    return false;
                }

                result = DetectCore(probe);
                Cache(adapterId!, result);
                return result.Detected;
            }
            catch (Exception exception)
            {
                // REQ-COMPAT-07: a detection failure is a miss, never an exception in the game loop.
                result = new DetectionResult(false, null, null, DateTime.UtcNow.Ticks,
                    "detect-exception:" + exception.GetType().Name);
                TryLog(exception, adapterId!);
                return false;
            }
        }

        /// <summary>
        /// Clears the session cache and assembly snapshot so the next probe re-scans the
        /// environment (manual "re-detect" of chapter 8.2). Also drops the reflection cache so a
        /// hot-reloaded external assembly is not probed through stale handles.
        /// </summary>
        public void Reset()
        {
            try
            {
                lock (_cache)
                {
                    _cache.Clear();
                }

                _assemblies = null;
                _modListNames = null;
                _modListRead = false;
                _sessionScan.Reset();
                _lastDetectionSource = "Unknown";
                ExternalProxy.ClearCache();
            }
            catch (Exception)
            {
                // Reset is called from UI/DI paths; it must never fail the caller.
            }
        }

        /// <summary>Alias for <see cref="Reset"/> used by the manual re-detect flow (chapter 8.2).</summary>
        public void Refresh()
        {
            Reset();
        }

        private void Cache(string adapterId, DetectionResult result)
        {
            lock (_cache)
            {
                _cache[adapterId] = result;
            }
        }

        private DetectionResult DetectCore(ThirdPartyProbe probe)
        {
            var overridden = Override(probe.AdapterId);
            if (overridden.HasValue)
            {
                return overridden.Value;
            }

            if (!EnsureSessionScan())
            {
                return NotInstalled("scan-failed");
            }

            // The scan itself is the detection source unless the optional mod list answers first
            // (chapter 10.4 detectionSource: ModList / AssemblyScan).
            if (string.Equals(_lastDetectionSource, "Unknown", StringComparison.Ordinal))
            {
                _lastDetectionSource = "AssemblyScan";
            }

            if (IsBudgetExhausted())
            {
                return NotInstalled("detect-timeout");
            }

            var modList = ModListNames();
            if (modList is not null)
            {
                var listed = CompatMatching.MatchModName(modList, probe.ModNameAliases);
                if (listed is not null)
                {
                    _lastDetectionSource = "ModList";
                    return new DetectionResult(true, probe.ModName, null, DateTime.UtcNow.Ticks, null);
                }
            }

            var assemblies = _assemblies;
            if (assemblies is null)
            {
                return NotInstalled("scan-failed");
            }

            for (var i = 0; i < assemblies.Count; i++)
            {
                if (CompatMatching.MatchesAssemblyName(assemblies[i].NormalizedName, probe.AssemblyNameTokens))
                {
                    _lastDetectionSource = "AssemblyScan";
                    return new DetectionResult(true, probe.ModName, TryReadVersion(assemblies[i].Assembly),
                        DateTime.UtcNow.Ticks, null);
                }
            }

            for (var i = 0; i < probe.ApiAnchors.Length; i++)
            {
                if (IsBudgetExhausted())
                {
                    return NotInstalled("detect-timeout");
                }

                var anchor = probe.ApiAnchors[i];
                var type = ResolveAnchor(anchor);
                if (type is not null)
                {
                    _lastDetectionSource = "AssemblyScan";
                    return new DetectionResult(true, probe.ModName, TryReadVersion(type.Assembly),
                        DateTime.UtcNow.Ticks, null);
                }
            }

            return NotInstalled("not-installed");
        }

        private DetectionResult? Override(string adapterId)
        {
            var probeOverride = _probeOverride;
            if (probeOverride is null)
            {
                return null;
            }

            try
            {
                return probeOverride(adapterId);
            }
            catch (Exception exception)
            {
                TryLog(exception, adapterId);
                return new DetectionResult(false, null, null, DateTime.UtcNow.Ticks, "probe-override-failed");
            }
        }

        private static DetectionResult NotInstalled(string reason)
        {
            return new DetectionResult(false, null, null, DateTime.UtcNow.Ticks, reason);
        }

        private bool EnsureSessionScan()
        {
            if (_sessionScan.IsRunning)
            {
                return true;
            }

            try
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                var snapshot = new List<AssemblyInfo>(loaded.Length);
                for (var i = 0; i < loaded.Length; i++)
                {
                    try
                    {
                        var assembly = loaded[i];
                        if (assembly.IsDynamic)
                        {
                            continue;
                        }

                        var name = assembly.GetName().Name;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        snapshot.Add(new AssemblyInfo(assembly, name!, CompatMatching.Normalize(name)));
                    }
                    catch (Exception)
                    {
                        // One unreadable assembly must not abort the scan (REQ-SAFE-01).
                    }
                }

                _assemblies = snapshot;
                _sessionScan.Start();
                return true;
            }
            catch (Exception exception)
            {
                _assemblies = null;
                TryLog(exception, "source:AssemblyScan");
                return false;
            }
        }

        private bool IsBudgetExhausted()
        {
            try
            {
                return _sessionScan.IsRunning && _sessionScan.ElapsedMilliseconds > DetectionBudgetMs;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private IReadOnlyList<string>? ModListNames()
        {
            if (_modListRead)
            {
                return _modListNames;
            }

            _modListRead = true;
            var provider = _modListProvider;
            if (provider is null)
            {
                return null;
            }

            try
            {
                var names = provider();
                if (names is not null && names.Count > 0)
                {
                    _modListNames = names;
                }
            }
            catch (Exception exception)
            {
                // A broken mod-list provider degrades to assembly scanning (REQ-COMPAT-06).
                TryLog(exception, "source:ModList");
                _modListNames = null;
            }

            return _modListNames;
        }

        private static Type? ResolveAnchor(ApiAnchor anchor)
        {
            // Prefer the assembly the anchor was verified in (fast, unambiguous), then fall back to
            // a global scan so a mod that renames its assembly or namespace still detects (R-04).
            var type = ExternalProxy.FindType(anchor.AssemblyName, anchor.TypeName) ??
                       ExternalProxy.FindType(null, anchor.TypeName);
            return type ?? ExternalProxy.FindTypeBySimpleName(SimpleName(anchor.TypeName));
        }

        private static string SimpleName(string fullName)
        {
            var dot = fullName.LastIndexOf('.');
            return dot >= 0 && dot < fullName.Length - 1 ? fullName.Substring(dot + 1) : fullName;
        }

        private static string? TryReadVersion(Assembly assembly)
        {
            try
            {
                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informational is not null && !string.IsNullOrEmpty(informational.InformationalVersion))
                {
                    return informational.InformationalVersion.Trim();
                }

                var version = assembly.GetName().Version;
                return version?.ToString();
            }
            catch (Exception)
            {
                // Version metadata is best-effort; a miss only leaves the report field null.
                return null;
            }
        }

        private void TryLog(Exception exception, string adapterId)
        {
            var log = _log;
            if (log is null)
            {
                return;
            }

            try
            {
                log.Error(HDBF.Core.HdbfLogCategory.Compat,
                    HdbfLogKeys.CompatFailed + ":" + adapterId,
                    "detection failed; treating the mod as missing",
                    exception,
                    adapterId);
            }
            catch (Exception)
            {
                // Logging must never break detection (REQ-SAFE-01).
            }
        }
    }
}
