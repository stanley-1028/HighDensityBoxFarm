# HDBF internal interface contract (frozen for v0.1.0)

This file is the contract between the five assemblies. Signatures here are binding:
other agents compile against them. If an implementation needs a different signature,
message the Lead first — do not silently rename or reorder parameters.

Namespace root: `HDBF`. Assemblies and their single allowed dependency:

```
HDBF.Core          (no dependencies except .NET standard)
HDBF.Persistence   -> HDBF.Core
HDBF.Gameplay      -> HDBF.Persistence (Core flows transitively)
HDBF.Integration   -> HDBF.Core
HDBF.UI            -> HDBF.Core
```

## Already implemented (do not change signatures)

`HDBF.Core` (files in `src/HDBF.Core/`):

* `HDBF.Core.HdbfTier`, `HdbfMode`, `HdbfPortKind`, `HdbfPortState`,
  `HdbfMaintenanceKind`, `HdbfAdapterHealth`, `HdbfLogCategory`, `HdbfPlotId`
* `HDBF.Core.Balance.TierProfile` (record) and `TierCatalog` (constant + default table)
* `HDBF.Core.Balance.BalanceFormulas` — every balance formula (chapters 6, 11)

## To be implemented by the Core agent (`src/HDBF.Core`)

### Config — `HDBF.Core.Config`

```csharp
public sealed record TierOverride          // all nullable: null = keep default
{
    public int? PlotCapacity { get; init; }
    public int? MaxWorkers { get; init; }
    public int? WorkRange { get; init; }
    public float? MinIrrigationCoverage { get; init; }
    public float? WaterPerPlotPerDay { get; init; }
    public int? TankCapacityDays { get; init; }
    public float? SoilFatigueFactor { get; init; }
    public int? ScienceCost { get; init; }
}

public sealed record FeatureFlags          // chapter 9.3 table
{
    public bool DensePlanting { get; init; } = true;
    public bool Rotation { get; init; } = true;
    public bool Maintenance { get; init; } = true;
    public bool PowerConnection { get; init; } = true;
    public bool PipeConnection { get; init; } = true;
    public bool LogisticsOutput { get; init; } = false;
    public bool SoilFatigue { get; init; } = true;
    public bool GrowthMultiplierPatch { get; init; } = true;  // ADR-009: on; off = pre-patch D-2
}

public sealed record PerfSettings          // chapter 18.7/18.8 defaults
{
    public int ScanBudgetPerFrame { get; init; } = 128;
    public float ScanIntervalSeconds { get; init; } = 2f;
    public float IrrigationSampleInterval { get; init; } = 0.5f;
    public int UiRefreshHz { get; init; } = 4;
    public int ReducedBuildingThreshold { get; init; } = 100;
    public float SteadyFrameBudgetMs { get; init; } = 0.5f;
    public float WarnFrameMs { get; init; } = 2f;
    public float PauseFrameMs { get; init; } = 5f;
}

public sealed record HdbfConfig
{
    public int ConfigSchemaVersion { get; init; }
    public IReadOnlyList<TierOverride> TierOverrides { get; init; }   // index == HdbfTier
    public FeatureFlags Features { get; init; }
    public PerfSettings Perf { get; init; }
    public float DenseYieldMultiplier { get; init; }
    public float DenseWaterMultiplier { get; init; }
    public float DenseFatigueMultiplier { get; init; }
    public float DenseWorkMultiplier { get; init; }
    public float FallowRecoveryPerDay { get; init; }
    public float FatiguePenalty { get; init; }
    public float RotationFactor { get; init; }
    public float RotationRecovery { get; init; }
    public static HdbfConfig Default { get; }
}

public static class HdbfConfigCodec
{
    // Strict but forgiving: unknown keys ignored, wrong types/defaults used, every
    // clamp logged. Never throws for bad player input (REQ-SAFE-10).
    public static HdbfConfig Parse(string json, Action<string>? warn = null);
    public static string Serialize(HdbfConfig config);
    // Applies overrides on top of TierCatalog and clamps to chapter 18 ranges (REQ-BAL-18).
    public static IReadOnlyList<TierProfile> ResolveTiers(HdbfConfig config, Action<string>? warn = null);
}
```

### Logging — `HDBF.Core.Logging`

```csharp
public interface IHdbfLog
{
    void Write(HdbfLogCategory category, string key, string message);
    void Error(HdbfLogCategory category, string key, string message, Exception? exception, string? entityId = null);
    int SuppressedCount(string key);
    void Reset();
}

public sealed class ThrottledLog : IHdbfLog      // 30 s throttle per key, counts suppressed (REQ-SAFE-14)
{
    public ThrottledLog(Action<HdbfLogCategory, string>? sink, Func<double>? clock = null);
}

public static class HdbfLogKeys   // stable log keys, e.g. "config.invalid", "compat.missing"
{
    public const string ConfigInvalid = "config.invalid";
    public const string ConfigMissing = "config.missing";
    public const string SaveDegraded = "save.degraded";
    public const string SaveRecordReset = "save.record-reset";
    public const string CompatMissing = "compat.missing";
    public const string CompatFailed = "compat.failed";
    public const string PortFault = "port.fault";
    public const string PlotScanBudget = "plot.scan-budget";
    public const string PerfReduced = "perf.reduced";
}
```

### Compatibility — `HDBF.Core.Compat`

```csharp
public interface ICompatAdapter
{
    string Id { get; }                       // "HDBF.Adapter.TimberPipes"
    string DisplayNameKey { get; }           // "HDBF.Compat.Adapter.TimberPipes"
    HdbfAdapterHealth Health { get; }
    int FailureCount { get; }
    bool Detect();
    bool Initialize();
    void Shutdown();
    void Tick(float gameHours);
}

public interface ICompatRegistry                // consumed by Gameplay/UI via DI
{
    IReadOnlyList<string> EnabledAdapters { get; }
    IReadOnlyList<string> FailedAdapters { get; }
    IReadOnlyList<string> DetectedMods { get; }
    bool IsAdapterActive(string adapterId);
    bool IsPortAvailable(HdbfPortKind port);
    void Register(ICompatAdapter adapter);      // registration + detect + init, isolated per adapter
    void RefreshDetection();
    void ReportHarmonyPatchCount(int count);    // ADR-009: harmonyPatchCount of the chapter 10.4 report
}

public sealed class PatchFailureStateMachine      // pure, per-patch (ADR-009 / REQ-COMPAT-18)
{
    public const int FailureThreshold = 3;
    public PatchFailureStateMachine(string patchId, string targetMethod, int threshold = FailureThreshold);
    public string PatchId { get; }
    public string TargetMethod { get; }
    public int FailureCount { get; }
    public bool IsUnloaded { get; }
    public bool RecordFailure();                // true exactly when the budget is exhausted
    public void Reset();
}

// HDBF.Core.Growth — the only seam the HDBF.Integration patch uses to read farm state.
public interface IGrowthMultiplierSource
{
    bool IsPatchEnabled { get; }                // features.growthMultiplierPatch
    bool TryGetGrowthRate(int cellX, int cellY, int cellZ, out float growthRate);
}

public static class BalanceFormulas          // ADR-009 additions (single multiplication point)
{
    public const float MinimumGrowthRate = 0.05f;
    public static float GrowthMultiplier(float gIrr, float gFat, float gMaint, float gPower);
    public static float GrowthRateMultiplier(float growthMultiplier);   // clamp to [0.05, 1]
    public static float GrowthTimeScale(float growthRateMultiplier);    // 1 / rate, max 20
}

public sealed class CompatStateMachine          // pure, per-adapter, unit-testable
{
    public HdbfAdapterHealth Health { get; }
    public int FailureCount { get; }
    public bool Detect();                       // Unknown -> Detected
    public bool InitializeSucceeded();          // Detected -> Active
    public void Fail(string reason);            // counts failures, >=3 -> Failed (REQ-COMPAT-07)
    public void Shutdown();                     // Active -> Detected
    public bool CanRetry { get; }
}

public sealed class CompatReport                // chapter 10.4 fields, serialisable by hand
{
    public int ReportVersion { get; init; }
    public string ModVersion { get; init; }
    public string GameVersion { get; init; }
    public int SchemaVersion { get; init; }
    public int ConfigSchemaVersion { get; init; }
    public IReadOnlyList<string> DetectedMods { get; init; }
    public IReadOnlyList<string> EnabledAdapters { get; init; }
    public IReadOnlyList<string> FailedAdapters { get; init; }
    public string DetectionSource { get; init; }
    public int DetectionDurationMs { get; init; }
    public int HarmonyPatchCount { get; init; }
    public string PerformanceMode { get; init; }
    public string? LastError { get; init; }
    public string ToJson();
}
```

### Save model + codec — `HDBF.Core.Save`

```csharp
public sealed class HdbfBuildingRecord        // one building, aligned arrays (chapter 12.2)
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; }
    public long EntityId { get; set; }
    public HdbfTier Tier { get; set; }
    public HdbfMode Mode { get; set; }
    public int[] PlotIds { get; set; }
    public string[] CropIds { get; set; }      // same length as PlotIds
    public float[] Fatigue { get; set; }       // same length as PlotIds
    public int[] ExcludedPlotIds { get; set; }
    public float DueDay { get; set; }
    public int CyclesMissed { get; set; }
    public float TankWater { get; set; }
    public bool PortPower { get; set; }
    public bool PortPipe { get; set; }
    public bool PortLogistics { get; set; }
    public Dictionary<string, string> Adapters { get; set; }
}

public static class HdbfSaveCodec
{
    public static HdbfBuildingRecord CreateDefault(long entityId, HdbfTier tier, TierProfile profile, float currentDay);
    // Validates + repairs one record: truncates arrays to equal length, clamps fatigue 0..1,
    // tank >= 0, unknown mode -> Normal, missing tier -> false. Never throws (REQ-SAVE-08, REQ-SAFE-08).
    public static bool TrySanitize(HdbfBuildingRecord record, TierProfile profile, float currentDay,
        out HdbfBuildingRecord repaired, out string? reason);
    // Idempotent, per-record migration chain. from == to returns an equivalent copy (REQ-SAVE-05/06).
    public static HdbfBuildingRecord Migrate(HdbfBuildingRecord record, TierProfile profile, float currentDay);
    // Canonical text form used for tests and diffing (stable key order, 3 decimals).
    public static string Canonicalize(HdbfBuildingRecord record);
}
```

### Plot selection — `HDBF.Core.Plots`

```csharp
public readonly struct PlotCandidate
{
    public PlotCandidate(int plotId, int distance);
    public int PlotId { get; }
    public int Distance { get; }
}

public static class PlotSelection
{
    // Stable: distance ascending, then plotId ascending. Take min(capacity, candidates).
    // Existing ids already registered must be kept when still present (REQ-IMPL-07).
    public static int[] Select(IReadOnlyList<PlotCandidate> candidates, int capacity,
        IReadOnlyList<int> previouslyRegistered);
}
```

## To be implemented by the Persistence agent (`src/HDBF.Persistence`)

Game-facing mapping between `HdbfBuildingRecord` and Timberborn's entity persistence.
Declared now so Gameplay compiles against it:

```csharp
public static class HdbfEntitySaveBridge
{
    // Writes one record into the entity saver under stable keys.
    public static void Save(HdbfBuildingRecord record, Timberborn.WorldPersistence.IEntitySaver saver);

    // Reads best-effort; missing/corrupt fields fall back to defaults instead of throwing.
    public static HdbfBuildingRecord Load(Timberborn.WorldPersistence.IEntityLoader loader, long entityId,
        HdbfTier tier, TierProfile profile, float currentDay, out bool degraded, Action<string>? warn = null);
}
```

## To be implemented by the Gameplay agent (`src/HDBF.Gameplay`)

Public surface other assemblies may use:

```csharp
namespace HDBF.Gameplay.Specs
{
    public record HdbfBoxFarmSpec : ComponentSpec
    {
        [Serialize] public string Tier { get; set; } = "Basic";
        [Serialize] public int PlotCapacity { get; set; } = 60;
        [Serialize] public int PlotsPerFarmer { get; set; } = 38;
        [Serialize] public int WorkRangeX { get; set; } = 11;
        [Serialize] public int WorkRangeZ { get; set; } = 11;
        [Serialize] public float MinIrrigationCoverage { get; set; } = 0.5f;
        [Serialize] public float WaterPerPlotPerDay { get; set; } = 0.02f;
        [Serialize] public int TankCapacityDays { get; set; } = 3;
        [Serialize] public float SoilFatigueFactor { get; set; } = 1f;
        [Serialize] public int MaintenanceKind { get; set; }   // 0 irrigation-only,1 plank,2 metal,3 power
        [Serialize] public float MaintenanceCycleDays { get; set; }
        [Serialize] public int MaintenanceMaterialAmount { get; set; }
        [Serialize] public bool RequiresPower { get; set; }
        [Serialize] public int PowerDemand { get; set; }
        [Serialize] public bool HasPipePort { get; set; } = true;
        [Serialize] public bool HasLogisticsPort { get; set; }
        [Serialize] public bool HasPowerPort { get; set; }
    }
}

namespace HDBF.Gameplay.Components
{
    // Single controller shared by all four tiers (REQ-ARCH-07). Also implements the read-only
    // snapshot consumed by HDBF.UI through HDBF.Core.
    public class HdbfBoxFarmController : BaseComponent { }

    public interface IHdbfFarmQueryService
    {
        bool TryGetSnapshot(Guid entityId, out HDBF.Core.Abstractions.BoxFarmSnapshot snapshot);
        IReadOnlyList<HDBF.Core.Abstractions.BoxFarmSnapshot> AllSnapshots();
        bool TryGetSource(Guid entityId, out IHdbfBoxFarmSnapshotSource source);   // ADR-009 lookup
    }

    public interface IHdbfBoxFarmSnapshotSource
    {
        Guid EntityId { get; }
        long SaveEntityId { get; }
        HDBF.Core.Abstractions.BoxFarmSnapshot CreateSnapshot();
        HdbfWarningInputs CreateWarningInputs(HDBF.Core.Abstractions.BoxFarmSnapshot snapshot);
        // ADR-009: G_irr*G_fat*G_maint*G_power for one registered plot, already clamped to
        // [BalanceFormulas.MinimumGrowthRate, 1]; false = plot not managed by this farm.
        bool TryGetPlotGrowthRate(int plotId, out float growthRate);
    }

    public interface IHdbfFarmRegistry
    {
        // ... existing members ...
        bool TryGetPlotOwner(int plotId, out Guid ownerEntityId);                  // ADR-009 cell lookup
    }

    // Gameplay implementation of the Core growth seam consumed by the HDBF.Integration patch:
    // cell -> plotId -> owning farm -> per-plot rate. Bound in HdbfGameConfigurator.
    public sealed class HdbfGrowthRateProvider : HDBF.Core.Growth.IGrowthMultiplierSource { }

    public sealed class HdbfSystemRegistrar : ILoadableSingleton { /* binds config/log, exposes IHdbfLog + registry */ }
}

namespace HDBF.Gameplay
{
    public class HdbfModStarter : Timberborn.ModManagerScene.IModStarter
    {
        public void StartMod(Timberborn.ModManagerScene.IModEnvironment environment);
    }

    [Bindito.Core.Context("Game")]
    public class HdbfGameConfigurator : Bindito.Core.Configurator { }

    public static class HdbfLocKeys { /* all HDBF.* localization keys used by code/blueprints */ }
}
```

`HDBF.Core.Abstractions.BoxFarmSnapshot` (owned by the Core agent, consumed by UI and
Gameplay) — plain immutable record with: `EntityId`, `Tier`, `Mode`, `RegisteredPlots`,
`EffectivePlots`, `ActiveWorkers`, `MaxWorkers`, `IrrigationCoverage`,
`MinIrrigationCoverage`, `FatigueAverage`, `FatigueMax`, `MaintenanceDaysLeft`,
`CyclesMissed`, `TankWater`, `TankDaysLeft`, `TierNeedsPower`, `PowerConnected`,
`Ports` (`HdbfPortState` x3), `IsShutdown`, `IsEntranceBlocked`, `Warnings`
(`IReadOnlyList<FarmWarning>` where `FarmWarning` = `{ CodeKey, Severity, NumberArg }`),
`Multipliers` (`irr/fat/maint/power/mode`).

## To be implemented by the Integration agent (`src/HDBF.Integration`)

```csharp
namespace HDBF.Integration.Detection
{
    public sealed class CompatDetector          // assembly scan + session cache (REQ-COMPAT-06)
    {
        public bool TryDetect(string adapterId, out DetectionResult result);
        public int DetectedModCount { get; }
        public IReadOnlyList<string> DetectedMods { get; }
    }

    public readonly struct DetectionResult
    {
        public bool Detected { get; }
        public string? DetectedMod { get; }
        public string? ModVersion { get; }
        public long DetectedAtTicks { get; }
        public string? FailureReason { get; }
    }
}

namespace HDBF.Integration.Reflection
{
    public static class ExternalProxy          // cached Type/MethodInfo lookups, null on failure
    {
        public static Type? FindType(string assemblyName, string typeName);
        public static MethodInfo? FindMethod(Type? type, string methodName, params Type[] args);
        public static MethodInfo? FindMethodBySignature(Type? type, string memberName,
            IReadOnlyList<string> parameterTypeNames, string? returnTypeName);
        public static void ClearCache();
    }
}

namespace HDBF.Integration.Adapters
{
    public sealed class TimberPipesAdapter : ICompatAdapter { }
    public sealed class PowerLinesAdapter : ICompatAdapter { }
    public sealed class ConveyorBeltsV2Adapter : ICompatAdapter { }
    public sealed class MoreMinesAdapter : ICompatAdapter { }
    public sealed class HoomanStairsAdapter : ICompatAdapter { }
    public sealed class CompatAdapterRegistry : ICompatRegistry { }
}

// ADR-009 / deviation D-2 — the single approved Harmony Postfix (REQ-COMPAT-16/17/18).
// Harmony is reached through reflection only: no HarmonyLib type appears in this assembly's
// metadata and no 0Harmony.dll is bundled/shipped.
namespace HDBF.Patches
{
    public static class HdbfPatchCatalog        // whitelist: exactly one Postfix entry
    {
        public const string HarmonyInstanceId = "hdbf.highdensityboxfarm";
        public const string GrowthPatchId = "HDBF.Patches.BoxFarmGrowthPatch";
        public const string GrowthPatchTargetType = "Timberborn.Growing.Growable";
        public const string GrowthPatchTargetMethod = "ResumeGrowing";
        public const string GrowthPatchKind = "Postfix";
    }

    public static class BoxFarmGrowthPatch      // Postfix(Growable __instance, ITimeTrigger ____timeTrigger)
    {
        public static void Postfix(Growable __instance, ITimeTrigger ____timeTrigger);
    }
}

namespace HDBF.Integration
{
    public sealed class HarmonyBootstrap : ILoadableSingleton, IUnloadableSingleton
    {
        // Load(): feature switch check -> HdbfHarmonyInstaller.TryInstall (reflection);
        // Unload(): unpatch + bridge shutdown + CompatReport harmonyPatchCount back to 0.
    }
}
```

## Conventions

* Nullable reference types are enabled everywhere; avoid `!` unless the invariant is local.
* No `static` mutable state outside `ThrottledLog`/`ExternalProxy` caches, and both expose `Reset()`.
* Every public method that touches game data catches and logs instead of throwing (chapter 10).
* Tests assert exact numbers from chapters 5/6; use `Assert.Equal(expected, actual, tolerance)`.
