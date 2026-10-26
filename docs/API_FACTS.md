# Timberborn 1.1.2.8 — verified modding API facts

> Ground truth for this mod. Every statement below was read from the installed game
> assemblies (`C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`)
> with `tools/ApiIndex` (Mono.Cecil metadata + IL), not from documentation.
> If code and this file disagree, re-run ApiIndex and fix whichever is wrong — do not guess.

Game version: `1.1.2.8-ffc2de0-sw` (`Timberborn_Data/StreamingAssets/Version.txt`). Unity 6000.5.11f1.

## 1. Mod loading

| Fact | Detail |
|---|---|
| Manifest file | `<ModDir>/manifest.json` (game constant `ManifestLoader.ManifestFileName`) |
| Manifest shape | `{ "Name", "Version", "Id", "MinimumGameVersion", "Description", "RequiredMods": [], "OptionalMods": [] }` — see `Timberborn.Modding.ModManifest` |
| Assembly loading | `ModManagerScene.ModCodeStarter.LoadAssemblies` loads **every `*.dll` recursively** under the mod directory with `Assembly.Load(File.ReadAllBytes(path))` |
| Entry point | `Timberborn.ModManagerScene.IModStarter` → `void StartMod(IModEnvironment)`; every implementation in the loaded mod assemblies is instantiated (parameterless ctor) and called |
| Environment | `IModEnvironment.ModPath` (mod root), `IModEnvironment.OriginPath` |
| Asset files | Mod-local `*.blueprint.json`, `*.timbermesh`, sprites (`*.png` + `*.png.meta.json`), `Localizations/*.csv` are converted by `Timberborn.ModdingAssets` (`ModBlueprintConverter`, `ModTimbermeshConverter`, `ModSpriteConverter`, `ModTextAssetConverter`) |
| Asset bundles | only needed if a blueprint references a custom Unity prefab (we do not in v0.1) |

## 2. Dependency injection (Bindito)

| Fact | Detail |
|---|---|
| Discovery | `Bindito.Core.Internal.ContainerDefinition.FindConfigurators()` enumerates **`AppDomain.CurrentDomain.GetAssemblies()`** and keeps every non-abstract `IConfigurator` |
| Registration | `InstallAll(contextName)` instantiates configurators whose `[Context("...")]` matches; no manual registration needed for mod assemblies |
| Base class | `Bindito.Core.Configurator` with `protected override void Configure()`; helpers `Bind<T>()`, `MultiBind<T>()`, `Bind<T>().AsSingleton()` / `.AsTransient()` |
| Context names | game systems use `[Context("Game")]` (world simulation) and `[Context("MainMenu")]`; a building component/config belongs in `"Game"` |
| Singletons | `Bind<X>().AsSingleton()`; consumption via constructor injection |
| Loading order | `ILoadableSingleton.Load()` after container creation; `IPostLoadableSingleton.PostLoad()` after save load; `IUnloadableSingleton.Unload()` on unload |

## 3. Blueprints and specs

| Fact | Detail |
|---|---|
| JSON files | one component-set per file, e.g. `Buildings/Food/BoxFarm/BoxFarm.Basic.blueprint.json`; spec keys are spec type names, values are serialized member values |
| Blueprint object | `Timberborn.BlueprintSystem.Blueprint` = `Name`, `Specs` (`ImmutableArray<ComponentSpec>`), `Children` (`ImmutableArray<Blueprint>`) |
| Spec base type | `Timberborn.BlueprintSystem.ComponentSpec` — it is a **C# record**, so custom specs must be `public record X : ComponentSpec` |
| Serialization marker | properties need `[Timberborn.BlueprintSystem.Serialize]`; `AdvancedDeserializer.GetSerializedProperties` reflects `AllInstanceFlag` properties and keeps those carrying the attribute |
| Spec discovery | `SpecTypeCache.Create()` scans **all loaded assemblies** for non-abstract `ComponentSpec` subclasses → a mod assembly's spec types are automatically deserializable |
| Aliases | `[SpecAlias("OtherName")]` allows alternate JSON keys |
| Collections | a building becomes visible/buildable when its blueprint is listed in a `TemplateCollectionSpec` whose `CollectionId` matches an existing collection, e.g. `Buildings.Folktails` / `Buildings.IronTeeth`; mods append with `<CollectionId>.blueprint.json` + `"Blueprints#append": ["path/without.blueprint.json"]` |
| Children | nested blueprints under `"Children"` are named objects (`"#Finished"`, `"#Unfinished"`, `"ConstructionStage0"`, ...); nested blueprint references use `"#nested"` suffix + `BlueprintPath` |

### 3.1 Blueprint fields observed in a working 1.1.2.8 example mod (BerryJam JamStove) and vanilla EfficientFarmHouse

| Spec | Fields |
|---|---|
| `BuildingSpec` | `SelectionSoundName`, `LoopingSoundName`, `BuildingCost[{Id,Amount}]`, `ScienceCost`, `PlaceFinished`, `FinishableWithBeaversOnSite`, `DrawRangeBoundsOnIt` |
| `BuildingModelSpec` | `FinishedModelName`, `UnfinishedModelName`, `FinishedUncoveredModelName`, `UndergroundModelName`, `ConstructionModeModel`, `UndergroundModelDepth` |
| `BlockObjectSpec` | `Size{X,Y,Z}`, `Blocks[]` (flattened, length = X*Y*Z, each `{MatterBelow, Occupations, Stackable, OccupyAllBelow, Underground}`), `Entrance{HasEntrance, Coordinates{X,Y,Z}}`, `BaseZ`, `Overridable`, `Flippable` |
| `PlaceableBlockObjectSpec` | `ToolGroupId` (existing groups: `Food`, `Wood`, `Power`, `Paths`, `Storage`, `Water`, `Landscaping`, `Science`, `Decoration`, `Housing`, `DistrictManagement`, `Wellbeing`, `Metal`, `Monuments`, `Automation`), `ToolOrder`, `ToolShape`, `Layout`, `CustomPivot{HasCustomPivot, Coordinates}`, `CanBeAttachedToTerrainSide`, `DevModeTool` |
| `BuildingAccessibleSpec` | `LocalAccess{X,Y,Z}`, `ForceOneFinalAccess` |
| `WorkplaceSpec` | `MaxWorkers`, `DefaultWorkers`, `DefaultWorkerType`, `DisallowOtherWorkerTypes`, `WorkerTypeUnlockCosts[]` |
| `LabeledEntitySpec` | `DisplayNameLocKey`, `DescriptionLocKey`, `FlavorDescriptionLocKey`, `Icon` (sprite path) |
| `TemplateSpec` | `TemplateName`, `BackwardCompatibleTemplateNames[]`, `RequiredFeatureToggle`, `DisablingFeatureToggle` |
| `TimbermeshSpec` (in child) | `Model` = path to `.timbermesh` asset **without extension**, folder-relative, e.g. `Buildings/Food/EfficientFarmHouse/EfficientFarmHouse.Folktails.Model` |
| `CollidersSpec` (in child) | `BoxColliders[{Center{X,Y,Z},Size{X,Y,Z}}]`, `SphereColliders[]`, `CapsuleColliders[]` |
| `ConstructionSiteProgressVisualizerSpec` | `ProgressThresholds[]` |
| `TransformSlotInitializerSpec` | `Slots[{SlotKeyword, Animation, Inanimate, RandomizeYRotation, WaterSlot}]`. `SlotKeyword` must name a transform that exists in the referenced model (vanilla: `GathererFlag` → `Seat`, `FarmHouse`/`EfficientFarmHouse`/`Gristmill`/`HydroponicGarden`/`FoodFactory` → `Entrance`, `LargeWarehouse` → none). If a declared keyword has no transform the game throws `InvalidOperationException: There are no "<keyword>" slots in <prefab>` **when the entity is instantiated** (i.e. when the construction site is placed). A model without slots must omit the spec (6 vanilla workplaces, e.g. 10-worker `Mine`, do exactly that and stay fully functional). Put this spec on the blueprint **root only**, never inside `Children.*` — child specs are instantiated on the child prefab too. |
| `SimpleOutputInventorySpec` | `Capacity`, `IgnorableCapacity` |
| `FarmHouseSpec` / `YieldRemovingBuildingSpec` | (vanilla agri building markers) |
| `PlanterBuildingSpec` | `PlantableResourceGroup` |
| `NeedCollection` etc. | not needed |

### 3.2 `BlockObjectSpec.Blocks` — ordering, `MatterBelow`/`Occupations` semantics, vanilla 1-high layout

Proven by decompiling `Timberborn.BlockSystem.dll` (ILDasm) and by calling its pure functions from a
net9.0 probe that references the installed assemblies directly (`Blocks.GetAllCoordinates`,
`Blocks.From`, `BlockObjectSpec.GetBlocks(placement)`, `BlockObjectSpec.BlockSpecFromCoordinates`):

| Fact | Detail |
|---|---|
| JSON authoring order | index `n` maps to `(x = n % Size.X, y = (n / Size.X) % Size.Y, z = n / (Size.X * Size.Y))` — row-major, x fastest inside a row, then y, then z. `Blocks.From` enumerates coordinates x-outer/z-fastest but fetches each spec with the row-major formula, and `BlockObjectSpec.GetBlocks(placement)` pairs coordinate and spec the same way; the probe confirmed both paths return the same spec for every coordinate, so the row-major mapping above is what the game actually uses. |
| `MatterBelow` | `Ground = 0`, `GroundOrStackable = 1`, `Air = 2`, `Any = 3`, `Stackable = 4`. `MatterBelowValidator` accepts `GroundOrStackable` only when the cell sits directly on terrain (or on a finished stackable block / underground case); `Any` always passes. Blocks that may overhang must be `Any`. |
| `Occupations` flags | `None = 0`, `Floor = 1`, `Bottom = 2`, `Top = 4`, `Corners = 8`, `Path = 16`, `Middle = 32`, `All = -1`. `Block.IsFoundationBlock` = `IsSolidMatter(MatterBelow)` = Ground/GroundOrStackable/Stackable; `BlockObjectAccessGenerator.GetBottomAccesses` only emits construction-site accesses below foundation blocks. |
| Vanilla 1-high layout | Grid coordinates are **X = width, Y = depth, Z = height** (IL: `TerrainService.GetTerrainHeight` uses `coordinates.z` as the column height and `VectorExtensions.XY()` returns the horizontal pair). A one-block-high building therefore has `Size.Z = 1`, and `Size.Y` is its depth. Vanilla keeps the facade depth row (y = 0, the row in front of which `Entrance.Coordinates.Y = -1` sits) solid: `MatterBelow: GroundOrStackable` + `Occupations: "Bottom, Top, Corners, Path, Middle"`; the depth rows behind it (y >= 1) are `MatterBelow: Any` + `Occupations: "Floor, Bottom, Corners, Path, Middle"` (the 4 counter buildings omit `Corners` on the solid row). Examples (Size = X x Y x Z = width x depth x height): `GathererFlag`, `LumberjackFlag`, `SmallTank` (1x1x2); `PowerWheel`, `DistrictCrossing` (3x1x2); `Fermenter` (3x1x3); `EfficientFarmHouse`, `FarmHouse`, `BuildersHut` (multi-storey). No vanilla building has a fully solid footprint. |
| HDBF consequence | The 3 wide x 2 deep x 1 high BoxFarm blueprints ship `Size { X: 3, Y: 2, Z: 1 }`: `Blocks[0..2]` = the y=0 facade depth row (solid, `GroundOrStackable`), `Blocks[3..5]` = the y=1 row behind it (`Any` + `Floor`), all on the only height layer z=0. `tools/validate` enforces exactly this shape and rejects the pre-ART-04 `{ X: 3, Y: 1, Z: 2 }` value (that one means 3 wide x 1 deep x 2 HIGH, not the 3x2x1 footprint); smoke-test round 3 had all four tiers unplaceable with an all-solid footprint. |

Localisation in a mod: `Localizations/enUS.csv` with header `ID,Text,Comment`; keys are arbitrary
strings used by `*LocKey`/`DisplayNameLocKey` fields.

## 4. Components on entities

| Fact | Detail |
|---|---|
| Base class | `Timberborn.BaseComponentSystem.BaseComponent` (fields `GameObject`, `Transform`, `Enabled`; generic `GetComponent<T>()`, `HasComponent<T>()`) |
| Tick | `Timberborn.TickSystem.TickableComponent` (virtual `Tick()`), `Timberborn.TickSystem.ITickableSingleton` for services |
| Spec access from a component | `GetComponent<Timberborn.BlueprintSystem.ComponentSpec subclass>()` — e.g. `Building.Awake` does `Spec = GetComponent<BuildingSpec>()`; specs are resolved like components |
| Instantiation | components are created by `TemplateInstantiator` from the blueprint; first the spec types, then **decorators** |
| Registering a custom component | `[Context("Game")] public class XConfigurator : Configurator { protected override void Configure() { Bind<MyComponent>().AsTransient(); MultiBind<TemplateModule>().ToProvider(ProvideTemplateModule); } static TemplateModule ProvideTemplateModule() { var b = new TemplateModule.Builder(); b.AddDecorator<MySpec, MyComponent>(); return b.Build(); } }` — verified against `WaterBuildingsConfigurator.ProvideTemplateModule` IL |
| Decorator rule | `AddDecorator<TSubject,TDecorator>()` adds component `TDecorator` to the entity whenever `TSubject` is present (subject may be a spec type or another component type); duplicates are skipped unless a dedicated decorator is used |
| Saving | `Timberborn.WorldPersistence.IPersistentEntity` with `Save(IEntitySaver)`, `Load(IEntityLoader)`; keys are `ComponentKey`, `PropertyKey<T>`, `ListKey<T>` |
| Lifecycle interfaces | `IAwakableComponent.Awake()`, `IInitializableEntity.InitializeEntity()`, `IPostLoadableEntity.PostLoadEntity()`, `IFinishedStateListener.OnEnterFinishedState()` |

## 5. Simulation services (constructor-injectable singletons)

| Service | Use |
|---|---|
| `Timberborn.TimeSystem.IDayNightCycle` (`DayNightCycle`) | `DayNumber`, `DayProgress`, `PartialDayNumber`, `IsDaytime`, `FixedDeltaTimeInHours` |
| `Timberborn.TimeSystem.ITimeTriggerFactory` / `ITimeTrigger` | `Create(Action, delayInDays)`; `DaysLeft`, `Progress`, `Finished`, `Reset/Resume/Pause` |
| `Timberborn.SoilMoistureSystem.ISoilMoistureService` | `bool SoilIsMoist(Vector3Int)`, `float SoilMoisture(int index)` |
| `Timberborn.BlockSystem.IBlockService` | `AnyObjectAt`, `GetBottomObjectAt`, `Contains`, `GetObjectsAt`, … |
| `Timberborn.TerrainSystem.ITerrainService` | `GetTerrainHeight`, `CellIsField`, `CellIsCutout`, `OnGround`, `Contains` |
| `Timberborn.WorkSystem.Workplace` (component) | `AssignedWorkers`, `NumberOfAssignedWorkers`, `MaxWorkers`, `DesiredWorkers`, `Understaffed` |
| `Timberborn.Planting.PlantingService` | `IsResourceAt`, `GetResourceAt`, `SetPlantingCoordinates`, `UnsetPlantingCoordinates`, `GetSpotAt`, `PlantingCoordinates` |
| `Timberborn.InventorySystem` / `SimpleOutputBuildings.SimpleOutputInventory` | output storage / hauling |
| `Timberborn.SingletonSystem.EventBus` | subscribe to published events (`Subscribe<T>`) |
| `Timberborn.BlueprintSystem.ISpecService` | `GetBlueprint(path)`, `GetSingleSpec<T>()`, `GetSpecs<T>()` |

Always confirm a member's exact signature with
`ApiIndex dump <managedDir> <out> <typePattern>` before writing the call — the table above is a
navigation aid, not a substitute.

## 6. Persistence / DI corrections (verified later in the build, supersedes any earlier assumption)

These were proven with ApiIndex IL plus the compiler while implementing `HDBF.Persistence`
and `HDBF.Integration`; the earlier "loaders return defaults" assumption was **wrong**.

| Fact | Consequence |
|---|---|
| `IObjectLoader.Get(PropertyKey<T>)` and `Get(ListKey<T>)` **throw `ArgumentOutOfRangeException`** when the key is absent (`SerializedObject.Get`/`GetArray` IL: `"Property not found: '<name>'"`) | Always guard with `Has(...)` or `HasList(...)` before `Get`; wrap per-field in try/catch for corrupt values |
| `IObjectLoader.Get<T>` generic overload is constrained to `System.Enum` (CS0314) | Read int/float/bool/string through the dedicated non-generic overloads |
| `IEntityLoader.TryGetComponent` takes **`out IObjectLoader`** | `TryGetComponent(key, out var comp)` |
| `Timberborn.Persistence` primitive serialization has **no Int64** support (`PropertyKey<long>` serializes to null) | Persist `EntityId` as invariant string; keep the logical field name `EntityId` |
| `IObjectSaver` has no dictionary setter | Adapter state map is persisted as two parallel `ListKey<string>` lists |
| `Bindito.Core.Internal.ConstructorRetriever` scans `Public\|NonPublic\|Instance` constructors and fails with "No eligible constructor found" if a DI-bound type exposes more than one | Every DI-bound type must have exactly **one** constructor |
| Same-instance interface binding | `Bind<Concrete>().AsSingleton(); Bind<IInterface>().ToExisting<Concrete>();` (matches vanilla `NavigationConfigurator`) |
| `ApiIndex` prints `out` parameters as `ref` | Tool cosmetic bug, fixed in `tools/ApiIndex/Program.cs`; trust the C# compiler over a raw dump for `out` vs `ref` |

## 7. Known build-environment quirks (not game facts)
1. `msbuild`/`dotnet build` must be invoked with **`-m:1`**. With parallel node reuse this machine
   fails `_GetProjectReferenceTargetFrameworkProperties` / NuGet restore silently (exit 1, zero
   messages).
2. **Every `.csproj` may declare at most one `ProjectReference`**; two or more makes restore fail
   the same silent way. The assembly graph is therefore a chain:
   `HDBF.Core ← HDBF.Persistence ← HDBF.Gameplay`, with `HDBF.Integration` and `HDBF.UI` referencing
   `HDBF.Core` directly.
3. NuGet has no network access; only the local package cache is usable. HDBF ships with **zero
   NuGet dependencies** (tests use a built-in runner, not xUnit).

## 8. Faction-scoped materials (verified in game, 2026-10-24)

Timberborn's `MaterialRepository` only contains the materials of the **running faction**
(`BaseWood_Grey.Folktails` vs `BaseWood_Grey.IronTeeth`). A blueprint whose
`Children.*.TimbermeshSpec.Model` points at another faction's vanilla model makes
`PrefabOptimizationChain.ProcessPrefab` throw
`ArgumentException: Material <name> not found in repository` while the build menu creates the
tool previews, which aborts the whole build-menu load (not just that building).

Consequences for mod authors:

* ship **one blueprint per faction** when borrowing vanilla models or materials
  (`BoxFarm.<Tier>.Folktails` / `BoxFarm.<Tier>.IronTeeth`), exactly like vanilla's
  `FarmHouse.IronTeeth` vs `FarmHouse.Folktails`;
* `TemplateSpec.TemplateName` must be unique across all loaded blueprints, so the faction belongs
  in the template name, not only in the file name;
* each faction's `TemplateCollectionSpec` may only append its own blueprints;
* balance data can stay identical between the two (REQ-TECH-06 allows appearance-only differences).

## 9. Command-line autostart (verification aid)

Read from `Timberborn.MainMenuScene.AutoStarter` IL on 1.1.2.8 (not from documentation):

| Fact | Detail |
|---|---|
| `-saveName <name>` | Only this key turns autostart on (`AutoStartingInStandalone` = `Application.isEditor == false && Has("saveName")`). |
| `-settlementName <name>` | Second half of the save reference; autostart builds `new SaveReference(saveName, new SettlementReference(settlementName, Path.Combine(UserDataFolder.Folder, "Saves")))` and calls `GameSceneLoader.StartSaveGameInstantly(...)`. |
| Batch mode | `Timberborn.exe -batchmode -nographics -logFile <path>` initialises Unity with `NullGfxDevice`, scans the Mods folder (a folder without `manifest.json` logs `No manifest file found in ...` and is ignored), then exits 0. It performs the mod-folder scan but not a world load, so it is only a *deploy layout* check, not gameplay verification. |
| Windowed autostart | `Timberborn.exe -saveName "<file>" -settlementName "<folder>"` is the intended quiet way to load straight into a settlement. On the delivery machine this exited cleanly right after the Mods scan without loading a world (the interactive desktop session was not available to the harness); treat it as "needs a user-attended launch" until proven otherwise. |
| Safety | Point `-settlementName` at a **copy** of a save folder (e.g. `Saves\HDBFTest`), never at the real settlement; the game may write autosaves there. |

## 10. P1 farming integration facts (verified by farm-loop from IL, 1.1.2.8)

| Fact | Detail |
|---|---|
| Plot ownership | `FarmHouse.Validate(PlantingSpot)` only checks `spot.PlantingBlocker == null`; vanilla farms find work by *range* (`PlanterBuildingSpec` → `PlanterBuilding.InRangePlantingCoordinates`, `FarmHouse` → `BuildingTerrainRange` radius `NavigationDistance.ResourceBuildings` = 20), not by an ownership list. A 27×27 top tier cannot reuse that range filter. |
| Task steps are worker-side | `PlantingConfigurator`: `AddDecorator<Worker, Planter>`, `AddDecorator<Worker, PlantBehavior>`; `FieldsConfigurator`: `AddDecorator<Worker, HarvestStarter>`; `YieldingConfigurator`: `AddDecorator<Worker, YielderRemover/YieldRemoverBehavior>`. A custom `WorkplaceBehavior` on the building can therefore drive the vanilla plant/harvest/carry steps for arbitrary managed plots with **zero patches**. |
| Dispatch point | `WorkerRootBehavior.WorkAtWorkplace` iterates `Workplace.WorkplaceBehaviors` and calls `Decide(agent)`; returning `Decision.TransferNow(behavior, in decision)` runs the vanilla worker behavior. |

## 11. Third-party mod APIs used by HDBF.Integration (verified 2026-10-25)

> Scope: the five optional mods listed by chapter 8.1. These facts were read from the **installed
> Steam Workshop DLL metadata** (not documentation), game version `1.1.2.8`:
> * `tools/dev/ModAnchorCheck` reads assembly identity + type/member signatures with
>   `System.Reflection.Metadata` (19/19 anchors PASS);
> * `tools/dev/ReflectAnchorCheck` loads the 660 game assemblies + the six mod assemblies into a
>   .NET process and exercises the production `ExternalProxy` path (19/19 anchors + 5/5 detection
>   strings PASS).
>
> They are **not** verified by launching Timberborn. Full anchor table, file/line changes and the
> in-game matrix steps are in `docs/workstreams/compat-01-adapters.md`.

### 11.1 Verified mod identities and DLLs

Workshop content root: `C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\`.

| Mod (manifest Name) | Manifest Id / Version / MinimumGameVersion | Version folder | Assembly (`*.dll`) |
|---|---|---|---|
| Timber Pipes | `TimberPipes` / `11.4.0` / `1.1.1` | `3801726769\version-1.1` | `TimberPipes.dll` (`TimberPipes`, 1.0.0.0) |
| Power Lines | `PowerLines` / `11.1.1` / `1.1.1` | `3766893705\version-1.1` | `PowerLines.dll` (`PowerLines`, 1.0.0.0) |
| Conveyor Belts v2 | `ConveyorBelt` / `11.4.0` / `1.1.1` | `3815376360\version-1.1` | `ConveyorBelt.dll` + `ConveyorBelt.Algorithm.dll` |
| More Mines [ModContest1] | `TonWolfe.MoreMines` / `1.1.20` / `1.1.0.2` | `3723216060\version-1.1.0.2` | `Scripts\More Mines.dll` (assembly name `More Mines`) + `Scripts\SharpNoise\SharpNoise.dll` |
| Hooman Stairs | `Calloatti.HoomanStairs` / `2.0.2` / `1.1.2.4` | `3682012904\Version-1.1` | `hoomanstairs.dll` (assembly name `hoomanstairs`, lowercase) |

Key public types (all verified public except where noted):
`TimberPipes.Services.PipeRegistry`, `TimberPipes.Services.PipeFluidSimulator`,
`TimberPipes.Models.PipeGraph`, `TimberPipes.Components.PipeTank`,
`TimberPipes.Specs.PipeSimulationSpec`; `PowerLines.Services.PowerLineConnectionService`,
`PowerLines.Services.PowerLineGraphService`, `PowerLines.Services.PowerLinesService`,
`PowerLines.Components.PowerLineComponent`, `PowerLines.Specs.PowerLineSettingsSpec`;
`ConveyorBelt.Services.BeltRegistry`, `ConveyorBelt.Services.BeltSimulator`,
`ConveyorBelt.Components.BeltInventory`, `ConveyorBelt.Algorithm.BeltSimulation`;
`TonWolfe.DeepMines.TWOreGroupSpec`, `TonWolfe.DeepMines.TWTunnelSpec`,
`TonWolfe.DeepMines.TWOreUpdateService`, `TonWolfe.DeepMines.TWOre`
(`TonWolfe.DeepMines.TWDeepMinesConfigurator`, `TonWolfe.DeepMines.TWDeepOreDepositSpec` and
`TonWolfe.DeepMines.TWTailingsCollector` are **internal**);
`Calloatti.HoomanStairs.HoomanStairsRegistry`, `Calloatti.HoomanStairs.HoomanStairsManager`,
`Calloatti.HoomanStairs.HoomanStairsPathfinder`, `Calloatti.HoomanStairs.StairConnection`.

There is **no** `TimberPipes.TimberPipeNetwork` / `PowerLines.PowerLineNetwork` /
`ConveyorBelts.*` / `MoreMines.*` / `HoomanStairs.*` type in any of the assemblies: the pre-verify
anchor names in `ThirdPartyProbes.cs` were invented and have been replaced by the names above.

### 11.2 Game-side mod loading facts relevant to the matrix

| Fact | Detail / evidence |
|---|---|
| Workshop content is not auto-loaded | The game scans `Documents\Timberborn\Mods` (`ModRepository.GetMods` / `UserFolderModsProvider`); a Workshop folder must be copied there first. |
| A copied-in mod is enabled by default | `ModPlayerPrefsHelper.IsModEnabled(Mod...)` returns `true` when the PlayerPrefs key is absent (IL: `HasKey` → `false` → `ldc.i4.1; ret`). |
| `-skipModManager` still loads enabled mods | `ModManagerScenePanel.LoadModsAndStartGame` filters `ModRepository.Mods` by `Mod.IsEnabled`, then calls `ModCodeStarter.Start()` and `ModAssetBundleLoader.Load()`; `ShouldSkipModManager()` only decides between the panel and that direct call. |
| `RequiredMods` is **not** a load gate | IL referrers of `get_RequiredMods`: only `ModSorter.SortByDependencies` (load order) and `ModdingUI.ModWarningUpdater.ValidateRequiredMods` (UI warning). No loader rejects a mod with missing required mods. |
| `OptionalMods` is a load-order hint only | `ModSorter.SortByDependencies` concatenates `RequiredMods` + `OptionalMods` to build the dependency count; a missing optional entry has no effect and produces no warning. |
| Duplicate manifest Ids are a layout pitfall | `ModRepository.MarkDuplicatedIds` groups mods by identity and `Mod.MarkIdAsDuplicated` exists, so two installed folders with the same manifest Id can interfere. The matrix installs only its own `HDBFCompat.*` folders. |
| All enabled mod assemblies load before any mod starter | `ModCodeStarter.Start()` IL is exactly `LoadAssemblies(); StartMods();`. `LoadAssemblies()` iterates `ModRepository.EnabledMods` and loads every `*.dll` recursively with `Assembly.Load(File.ReadAllBytes(...))`; only after that does `StartMods()` instantiate `IModStarter`s. HDBF's detection therefore sees every enabled mod's assemblies without relying on manifest load-order hints, which is why `data/manifest.json` keeps `RequiredMods`/`OptionalMods` empty (adding object-form entries would also conflict with `tools/validate`'s current string-array schema check; see `docs/workstreams/compat-01-adapters.md` §8). |
| Declared dependencies of the five mods | Conveyor Belts v2 + Timber Pipes require `ModdableTimberborn`, `ModdableToolGroups`, `eMka.ModSettings`; Power Lines requires `TimberUi`, `Harmony`; More Mines requires `Harmony`, `eMka.ModSettings`, `ModdableTimberborn`, `TimberUi`, `MoreTunnels.Ladder`, `TonWolfe.TradeOffice`; Hooman Stairs requires `Harmony` (2.4.1+). On the matrix machine only Harmony + HDBF are currently installed, so those dependencies are missing: loading is not gated (row above), but the external mods may log their own errors — unverified until the in-game matrix runs. |

### 11.3 `ExternalProxy` signature cache

Chapter 8.2 requires `Type`/`MethodInfo` to be cached by "type full name + member name + signature".
`ExternalProxy.FindMethodBySignature(type, memberName, parameterTypeNames, returnTypeName)` does
exactly that without a compile-time reference: parameter type names are full-name strings, `ByRef`
parameters are recorded by their element type, and misses are cached as `null` (same
`ClearCache()` reset as the type/method caches). `ReflectionCompatAdapter.Initialize` only counts an
A-level anchor as bound when this lookup succeeds, so a renamed member degrades the adapter with
`api-anchor-signature-changed` instead of advertising an unbound port.

## 12. World-space status icons (verified 2026-10-25 by BUILD-04)

> Read from `Timberborn.StatusSystem`, `Timberborn.BuildingStatuses` and `Timberborn.CameraSystem`
> IL with ApiIndex. Curated excerpts: `docs/api/il-status-icons.txt`.

| Fact | Detail |
|---|---|
| Every building owns the pipeline | `BuildingStatusesConfigurator.ProvideTemplateModule` decorates `BuildingSpec` with `BuildingStatusIconOffsetter`, `StatusSubject` and `StatusIconCycler`; `StatusSystemConfigurator` adds `FacingCamera` to the cycler and `StatusSlotOccupier` to the `BlockObject`. A mod building with `BuildingSpec` therefore already has camera-facing, screen-slot-stacked floating icons. |
| Registering an icon | Public `StatusSubject.RegisterStatus(StatusToggle)`; public factories `StatusToggle.CreatePriorityStatusWithFloatingIcon(sprite, description, delayInHours)` / `CreateNormalStatusWithFloatingIcon(...)`. Only statuses with `ShowFloatingIcon` are considered by the cycler. |
| Sprite source | `StatusSpriteLoader.LoadSprite(name)` loads `Path.Combine("Sprites/StatusIcons", name)` through the public `IAssetLoader`. Vanilla literal names confirmed in IL: `NoPower`, `BuildingNeedsWater`, `UnspecifiedRecipe`, `UnconnectedBuilding`, … — a mod can reuse them without authoring a sprite. |
| Material/scale/facing | `StatusIconMaterials.SetMaterial` clones the renderer material and writes the sprite texture into `_BaseMap`; `StatusIconCycler.UpdateScale` divides the icon scale by the building's local scale; `ToggleStatusIcon` calls `FacingCamera.Enable`. All vanilla. |
| Visibility control | Public `StatusIconCycler.GetStatusVisibilityToggle()` returns a `StatusVisibilityToggle` with `Hide()`/`Show()`; the cycler reacts to it (`UpdateVisibility` → `ToggleRoot`). Hiding it hides the floating icon **without** deactivating the underlying status. |
| Icon update cadence | `StatusIconCyclerUpdater.UpdateSingleton` calls `UpdateStatusVisibility()` every frame and `IntervalUpdate()` every `UpdateInterval = 1.25 s`; the cycler shows one active status per building at a time. |
| Camera | `CameraService.Transform` is the camera transform, `CameraService.Target` the camera focus. `CameraServiceSpec.BaseDistance = 32` (blueprint `Configurations/CameraService.blueprint.json`), so camera-to-building distance changes with zoom; measure "distance in cells" from `Target` instead. |
| Locate a building | Public `EntitySelectionService.Select(BaseComponent)` and `SelectAndFocusOn(BaseComponent)` (the alert-panel jump path). |


