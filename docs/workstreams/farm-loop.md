# farm-loop — P1 crop loop (planting / harvesting / real output)

Owner: `farm-loop` teammate. Task: `task-1`. Scope: `src/HDBF.Gameplay/**` (except the files the
Lead reserved). Requirement source (read-only): `高密度盒子农场模组需求文档.md` chapters 6.2, 6.7,
6.8, 11.3, 11.5, 15 (AC-03). Coordination rules: [`README.md`](README.md).

Status legend used below:

* **proven here** — read from the installed game (IL/metadata/blueprints) or asserted by a local probe.
* **compiles, needs in-game check** — code exists and builds against Timberborn 1.1.2.8, but the
  behaviour can only be confirmed with the game running.
* **deferred** — explicitly not done in this round, with the reason.

---

## 1. Verdict: route B — custom workplace behaviour + vanilla worker-side task steps, 0 patches

The central question — *"can vanilla planting/harvest tasks target plots that are not owned by a
vanilla farmhouse?"* — is answered by the IL:

**There is no farmhouse ownership of plots in 1.1.2.8.** A "field plot" is an entry in the global
`Timberborn.Planting.PlantingService` planting map (coordinate → crop resource). A building sees the
plots it may work only through *range-filtered components*:

| Fact | Evidence (installed assemblies, `tools/ApiIndex il`) |
|---|---|
| `FarmHouse.Validate(PlantingSpot spot)` only returns `spot.PlantingBlocker == null`; it does not test range, ownership or the building | `Timberborn.Fields.FarmHouse::Validate` IL (`il-farmhouse.txt`) |
| `InRangePlantingCoordinates` collects global planting coordinates that are inside the building's `BuildingTerrainRange` and whose resource is allowed by `PlanterBuilding.CanPlant(resource)` | `Timberborn.Planting.InRangePlantingCoordinates::UpdateCoordinates` / `::Awake` IL (`il-behaviors.txt`) |
| `BuildingTerrainRange` is *not* per-blueprint: it is the terrain nodes within `NavigationDistance.ResourceBuildings = 20` of the building's single access | `BuildingTerrainRange::UpdateRange` + `NavigationDistance::.ctor` IL (`il-range.txt`) |
| Vanilla harvesting is range-filtered the same way: `HarvestStarter.FindYielder` → `InRangeYielders.GetYielders(...)` → `YielderFinder` | `Timberborn.Fields.HarvestStarter::StartHarvesting/FindYielder` IL (`il-behaviors.txt`) |
| The actual task steps live on the **worker**, not the farmhouse: `Worker → Planter + PlantBehavior` (PlantingConfigurator), `Worker → HarvestStarter` (FieldsConfigurator), `Worker → YielderRemover + YieldRemoverBehavior` (YieldingConfigurator) | `Timberborn.Planting.PlantingConfigurator::ProvideTemplateModule`, `Timberborn.Fields.FieldsConfigurator::ProvideTemplateModule`, `Timberborn.Yielding.YieldingConfigurator+TemplateModuleProvider::Get` IL (`il-config.txt`, `il-yielding-tm2.txt`) |
| Vanilla plant step = walk + `NaturalResourceFactory.PlantNew(crop, cell)`; vanilla harvest step = walk + reserve + `YielderRemover.CompleteReservation` (goods into the worker's hands, delivered by the carrying root behaviour) | `PlantBehavior::ReserveCoordinates/Plant`, `PlantExecutor::Launch/SpawnResource`, `YielderRemover::CompleteReservation`, `YieldRemoverBehavior::Decide` IL (`il-behaviors.txt`, `il-harvest.txt`) |
| `WorkerRootBehavior.WorkAtWorkplace` iterates `workplace.WorkplaceBehaviors`, calls `Decide(agent)`; `Decision.ReleaseNow()` = "try the next behaviour", a decision with `ShouldReleaseNow == false` is transferred to the vanilla worker behaviour | `Timberborn.WorkSystem.WorkerRootBehavior::WorkAtWorkplace/Decide` IL (`il-workerroot.txt`) |
| Worker job behaviours are present on **every** worker (decorators keyed on `Worker`), so a mod-owned workplace behaviour can call them without touching vanilla code | same configurator IL above |

Therefore:

* **Route A (pure vanilla farmhouse components via `FarmHouseSpec`)** was rejected: the vanilla
  range is a fixed 20-distance radius while the top tier works a 27×27 square (corner ≈ 26 path
  units), so far plots would never be serviced; and vanilla dispatch cannot enforce the required
  `workers × 13` actions/day, harvest-before-seed ordering, real path-length ordering or
  `workCost = 0.08 × (1 + path/20)`.
* **Route C (Harmony patch)** was rejected: not needed. `REQ-IMPL-15` holds with **0 patches**.
* **Route B chosen**: HDBF owns the deterministic daily plan and picks the exact plot; the vanilla
  `PlantBehavior`/`PlantExecutor` and `YieldRemoverBehavior` execute it. No item is ever created by
  HDBF code — crops are spawned by the vanilla planting executor and harvested goods only come from
  the real crop's `Yielder` (`REQ-BAL-22/23`).

## 2. Design

```
daily tick (REQ-IMPL-22 order)                    worker idle (WorkerRootBehavior)
  ...maintenance/power...                              │
  OnFarmDayTick(today)                                 ▼
    ├ reset day plan/budget                        HdbfFarmWorkplaceBehavior.Decide(agent)
    └ dense growth boost (mode)                       ├ finalize previous harvest (fatigue)
                                                      ├ controller.TryTakeFarmWorkItem()
controller.Farming (plan)                             │     ├ budget: workers×13 actions,
  candidates = first P_eff registered plots  ◄────────┤     │   workdays = actions×0.08
    classify: mature/yielding → Harvest               │     ├ order: Harvest > Plant, real path,
              empty → Plant, growing → skip           │     │   plotId (FarmWorkPlanner)
  real path length measured 4/frame (Tick)            │     └ consume budget, claim plot
  sorted plan (harvest-first, path, plotId)           ├ Plant: Planter.Reserve(cell) →
                                                      │        PlantBehavior.StartPlanting(agent)
                                                      └ Harvest: crop.Yielder →
                                                               GoodReserver.ReserveCapacity(buildingInv)
                                                               YielderRemover.ReserveForRemoval →
                                                               YieldRemoverBehavior.Decide(agent)
```

* **Plots**: `HdbfBoxFarmController` already registers fields in range (chapter 11.3 scan); the farm
  loop only reads the first `P_eff = min(PlotCapacity, activeWorkers × 38)` ids of the aligned
  `PlotIds/CropIds/Fatigue` arrays (REQ-IMPL-08). No workers → no `Decide` dispatch at all.
* **Budget** (`Farming/FarmWorkPlanner.cs`, pure): `actions = workers × 13`; workday pool
  `= actions × 0.08`; every action costs `0.08 × (1 + path/20) × modeWork` (Dense ×1.2, Fallow ×0.2).
  At path 0 one farmer gets exactly 13 actions/day; at path 20 only 6 (commute penalty,
  REQ-BAL-04/REQ-IMPL-16).
* **Crop assignment**: `EnsurePlantingCrop` adopts an existing vanilla planting-map resource (the
  player's field crop) or picks a deterministic `PlantableSpec` of resource group `Farmhouse`
  (resolved at runtime, so the list is faction-correct). It writes it through the vanilla
  `PlantingService.SetPlantingCoordinates`, which is exactly what the vanilla field tool does.
* **Harvest accounting**: `NotifyPlotHarvested` applies
  `BalanceFormulas.ApplyHarvestFatigue(current, sameCrop, tierFatigueFactor × modeFatigue, …)` and
  stores the harvested crop. Because the save schema has no separate `LastCrop` field, the frozen
  `HdbfBuildingRecord.CropIds[]` is used as the **last harvested crop** (rotation is detected when
  the next harvested template differs, e.g. after the player changes the field crop). This keeps
  schema v1 untouched (no save migration, ADR-001 respected).
* **Modes** (chapter 6.7): Dense = +35 % *real growth speed* on managed crops
  (`Growable.IncreaseGrowthProgress((G_mode−1)/GrowthTimeInDays)` daily) so the yield multiplier
  produces more real harvests instead of spawned items; work ×1.2 and water/fatigue ×1.5 are already
  in `BalanceFormulas`; Fallow = no planting/harvest dispatch (yield 0) and the existing daily
  fallow fatigue recovery continues. Shutdown (`_isShutdown`) and fallow stop dispatch, crops keep
  growing (vanilla growth is not touched) — REQ-IMPL-20/REQ-BAL-05.
* **Output**: harvest reserves capacity in the building's vanilla `SimpleOutputInventory`/`Inventory`
  and vanilla carrying delivers the goods there; the vanilla haul-out chain (`Emptiable`,
  `HaulCandidate`, `SimpleOutputInventoryHaulBehaviorProvider`, `EmptyOutputWorkplaceBehavior`,
  `AutoEmptiable` — added as decorators) lets normal haulers take them to warehouses. HDBF never
  writes a global resource count (REQ-IMPL-17).
* **Safety**: every public entry point and every game-object call is try/catch; failures log
  `HDBF/Error`/`HDBF/Warn` with entityId and a stable key (`gameplay.farm`); after 5 faults the
  building's farm loop disables itself instead of throwing every frame (REQ-SAFE-01/02).

## 3. Files

New:

| File | Purpose |
|---|---|
| `src/HDBF.Gameplay/Farming/FarmWorkPlanner.cs` | pure daily budget + ordering + `workCost` (13 actions/farmer, harvest-first, real path length, deterministic tie-break) |
| `src/HDBF.Gameplay/Farming/HdbfFarmWorkplaceBehavior.cs` | `WorkplaceBehavior` that executes the chosen item through the vanilla worker behaviours; 0 Harmony patches |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Farming.cs` | daily plan, real-path measurement (4/frame), crop assignment, harvest fatigue/last-crop, dense growth boost, fault isolation |
| `docs/workstreams/farm-loop.md` | this report |

Edited (all owned by `farm-loop` except `HdbfGameConfigurator.cs`, which the Lead co-owns):

| File | Change |
|---|---|
| `Components/HdbfBoxFarmController.cs` | `TemplateService` ctor dependency + field (single constructor preserved) |
| `Components/HdbfBoxFarmController.Scan.cs` | `Tick()` also steps the path-measurement slice; `ApplyPlotArrays` invalidates the plan |
| `Components/HdbfBoxFarmController.Simulation.cs` | `OnFarmDayTick(today)` added as step 5 of the fixed daily order (after maintenance/power, before scan request) |
| `Components/HdbfBoxFarmController.Activation.cs` | `Deactivate()` invalidates the plan |
| `HdbfGameConfigurator.cs` | `Bind<HdbfFarmWorkplaceBehavior>().AsTransient()` + six `AddDecorator<HdbfBoxFarmSpec, T>()` lines (controller decorator and the Lead's `IBoxFarmCommand` lines untouched) |

Not touched: `Commands.cs`, `HdbfFarmQueryService.cs`, `HDBF.Gameplay.csproj`, data, UI, Core,
tests, tools, build files. The Lead added the five game-assembly references
(`BehaviorSystem`, `Yielding`, `Carrying`, `Emptying`, `Hauling`) to the csproj.

## 4. Build / verification performed on this machine

```
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1
  HDBF.Core -> ...\HDBF.Core.dll
  HDBF.Persistence -> ...\HDBF.Persistence.dll
  HDBF.Gameplay -> ...\HDBF.Gameplay.dll
  已成功生成。 0 个警告 0 个错误
```

Pure planner probe (scratch net9 console under `%USERPROFILE%\.hdbf\tmp\planner-probe`, not shipped,
not part of the workspace): **PLANNER PROBE PASSED** — 20/20 assertions:

* `workCost` 0.08 / 0.12 / 0.16 at path 0/10/20; dense ×1.2; fallow ×0.2;
* 1 farmer = 13 actions and 1.04 workdays; 3 farmers = 39 actions;
* path-0 consumption = exactly 13 actions; path-20 consumption = 6 actions (commute cost);
* order = harvest before plant, then path length, then plot id; claimed and unmeasured (-1) plots are
  never dispatched; measurement quota = 2×actions+16 capped at 512.

**Honest status: `compiles, needs in-game check`.** Nothing in this report was observed in a running
game; all runtime behaviour below needs the L3 pass.

## 5. What is proven here vs what needs the game

Proven here (IL/blueprint/probe):

* route B is sufficient and zero-patch (table in §1);
* the exact API signatures used compile against 1.1.2.8 assemblies;
* the daily budget/ordering/workCost arithmetic (probe);
* faction-correct crop list source (`TemplateService.GetAll<PlantableSpec>()`, filter
  `ResourceGroup == "Farmhouse"`; crops `Carrot`/`Potato`/`Wheat`/… exist in
  `%USERPROFILE%\.hdbf\ref\Blueprints\NaturalResources\Crops\*` with `PlantableSpec.ResourceGroup:
  "Farmhouse"`, `GrowableSpec`, `CropSpec`, `CuttableSpec.Yielder.ResourceGroup: "Farmhouse"`);
* the vanilla harvest goods can only come from `Yielder` (`YielderRemover.CompleteReservation` IL
  decreases the crop yield and puts the reserved amount in the carrier's hands).

Needs a running game (L3, see §7):

* that the six decorators actually attach in the template pipeline and
  `Workplace.ValidateWorkplaceBehaviors` sees `HdbfFarmWorkplaceBehavior` (collects
  `GetComponents<WorkplaceBehavior>()` in `PostLoadEntity`);
* that `PlantBehavior.StartPlanting` with a pre-reserved `Planter` walks to the chosen cell and the
  vanilla executor spawns the crop (no range filter on our path);
* that `YieldRemoverBehavior` walks to a far crop and the carrying root behaviour delivers the goods
  into the building's reserved inventory capacity;
* that vanilla haulers drain the box farm's `SimpleOutputInventory` (Emptiable/HaulCandidate chain);
* fatigue/last-crop/rotation numbers in the panel/save after real cycles;
* performance of the 4-path-queries/frame slice with 100 buildings (chapter 15.3).

## 6. DATA DEPENDENCY (tech-data — required for harvest)

All 8 faction blueprints need:

```json
"SimpleOutputInventorySpec": { "Capacity": 50, "IgnorableCapacity": false }
```

It creates `SimpleOutputInventory` → `Inventory` → `Inventories`; HDBF's harvest needs that receiving
inventory and the haul-out decorators target it. Without it the farm loop logs
`HDBF/Warn gameplay.farm "farm action failed (harvest prerequisites missing…)"` and does not
dispatch (deliberate fail-safe, not a crash). `FarmHouseSpec`, `PlanterBuildingSpec` and
`YieldRemovingBuildingSpec` are **not** needed and should not be added (fewer vanilla components =
fewer runtime assumptions). The Lead is routing this to `tech-data` including a validator check.

## 7. In-game verification plan (L3)

Preconditions: mod deployed (`HDBF` folder), data change from §6 present, new flat map, at least one
beaver available as farmer. Logs: Player.log, search `HDBF/`. All steps must run with the game clock
unpaused.

1. **Placement + worker gate.** Build the Basic box farm near flat terrain, use the vanilla field
   tool to paint ≥10 field cells inside its 11×11 range with Carrot. Assign 0 farmers for one game
   day. Expect: no `HDBF` harvest log, no new crop entities, no output. (REQ-BAL-05 / TC-33)
2. **Planting.** Assign 1 farmer (Farmer outfit is in the blueprint). Within one game day expect the
   farmer to walk to a field cell, a Carrot crop entity to appear, and, if it was empty, no global
   resources created. Check `HDBF/Info gameplay.farm` has no error.
3. **Harvest + output.** Wait for `GrowableSpec.GrowthTimeInDays` of the crop (Carrot = 4 days) and
   another game day. Expect: `HDBF/Info gameplay.farm "harvested plot <id> crop Carrot rotation=false
   fatigue=0.080"`, the building's internal inventory gains Carrots, then a vanilla hauler carries
   them to a warehouse; the total good count rises only by the harvested amount. (AC-03 / TC-01)
4. **Budget sanity.** With 1 farmer, no more than 13 plot actions/day; with 2 farmers no more than
   26. The `fatigue` of far plots rises only when actually harvested. (REQ-IMPL-16/17)
5. **No-worker repeat.** Remove all workers on a day with mature crops: no harvest that day, crops
   stay; reassign → harvesting resumes the next day. (TC-33)
6. **Modes.** Switch Dense for one day: in-progress managed crops' `GrowthProgress` advances faster by
   0.35/`GrowthTimeInDays` (and water/fatigue use ×1.5, work ×1.2). Switch Fallow: no plant/harvest
   logs, `fatigue` decreases 0.03/day. (TC-22)
7. **Shutdown.** Top tier off-grid overdue ≥2 maintenance cycles (`_isShutdown` in panel): no
   plant/harvest logs while shutdown, crops keep growing; perform maintenance → work resumes.
   (REQ-IMPL-20 / TC-20)
8. **Rotation + persistence.** Harvest Carrot twice (fatigue +0.08 each), then repaint the plot with
   Potato and harvest → `rotation=true` log, increment ×0.4 and extra −0.05. Save, reload, harvest
   again: rotation/fatigue continue from the persisted `CropIds/Fatigue` arrays (no save migration
   needed). (chapter 6.4 / AC-06)
9. **Demolish/rebuild.** Demolish, rebuild on the same spot, re-register the same fields: fatigue
   must not reset (it lives in the entity record; the rebuilt entity is a new record by design —
   document the observed behaviour and report it if the requirement wants cross-entity retention).
10. **Fault isolation.** Temporarily remove `SimpleOutputInventorySpec` from one blueprint: expect a
    `HDBF/Warn` and no dispatch, not a crash. Corrupt a saved record: expect the existing save-repair
    path plus no farm-loop `HDBF/Error`.
11. **Performance (L5).** 100 farms (mixed tiers, full plots): ≥45 fps P95, mod frame cost ≤0.5 ms;
    with the 4 path queries/frame slice the path measurement must not spike the daily tick.

## 8. Deferred / open decisions for the Lead

1. **G_irr / G_fat / G_maint / G_power are not applied to crop growth/output in P1.** They are
   computed and shown by the panel, but making them slow real growth would require pausing the
   vanilla `Growable` trigger, whose pause API is private (`PauseGrowing`) — i.e. a patch. Options:
   (a) accept P1 as "planting/harvesting loop only" and wire the multipliers in P2 with one approved
   Postfix, (b) realise `G < 1` by throttling dispatch (fewer harvested plots) instead of growth —
   no patch, but the UI numbers and actual output would diverge, (c) leave as is. **Decision needed.**
2. **Dense "yield ×1.35" is implemented as real growth ×1.35** (chapter 6.1 allows the growth-speed
   channel and it avoids item spawning). If the product owner insists on literal per-harvest yield,
   that needs an approved patch or a vanilla-yield multiplier mechanism we have not found.
3. **Fallow mode**: dispatch stops (yield 0) but vanilla crops keep growing; a strict "no growth in
   fallow" would need the same pause patch as (1).
4. **`CropIds` semantics**: frozen save schema has no separate `LastCrop`; the farm loop uses
   `CropIds[i]` as *last harvested crop* for rotation/fatigue. Confirm this interpretation for the
   save documentation (`SAVE-04` field semantics) and for the UI.
5. **Output inventory capacity 50**: when full, harvest actions fail with a `HDBF/Warn` until haulers
   drain. If the panel/balancing wants a bigger buffer, change the data spec (tech-data).
6. **Vanilla task reuse proof is static.** If the L3 run shows the decorators are not applied
   transitively for `HdbfBoxFarmSpec`-keyed decorators, fallback options are (i) move the decorators
   under `BuildingSpec` keying, or (ii) one approved Postfix. No patch was needed in code review.

## 9. API facts added by this workstream (for `docs/API_FACTS.md` / `INTERFACES.md` updates)

| Type | Verified member(s) used |
|---|---|
| `Timberborn.BehaviorSystem.Behavior` | abstract `Decision Decide(BehaviorAgent)`; `ComponentName` |
| `Timberborn.BehaviorSystem.Decision` | struct; `ReturnWhenFinished`, `ReleaseWhenFinished`, `ReturnNextTick`, `ReleaseNextTick`, `ReleaseNow`, `TransferNow(Behavior, in Decision)`, `ShouldReleaseNow` |
| `Timberborn.BehaviorSystem.ExecutorStatus` | `Success=0, Failure=1, Running=2` (walk/plant executors) |
| `Timberborn.WorkSystem.WorkplaceBehavior` | abstract job behaviour base; collected by `Workplace.ValidateWorkplaceBehaviors` via `GetComponents<WorkplaceBehavior>()` |
| `Timberborn.WorkSystem.WorkerRootBehavior.WorkAtWorkplace` | iterates `Workplace.WorkplaceBehaviors`, transfers non-release decisions |
| `Timberborn.WorkSystem.Worker` | `Workplace`, `WorkingSpeedMultiplier`, `Employed` |
| `Timberborn.WorkSystem.Workplace` | `NumberOfAssignedWorkers`, `WorkplaceBehaviors` |
| `Timberborn.Planting.Planter` | `Reserve(Vector3Int)`, `Unreserve()`, `PlantingCoordinates` |
| `Timberborn.Planting.PlantBehavior` | `StartPlanting(BehaviorAgent) → Decision`; uses `Planter` reservation if present, otherwise `PlantingSpotFinder` |
| `Timberborn.Planting.PlantExecutor` | `Launch(coords, resourceTemplate)`; spawns through `NaturalResourceFactory.PlantNew` |
| `Timberborn.Planting.PlantingService` | `GetResourceAt`, `SetPlantingCoordinates` (also calls `ITerrainService.SetField`), `IsResourceAt` |
| `Timberborn.Planting.PlantableSpec` | `ResourceGroup`, `TemplateName` (crop id) resolved from `TemplateService.GetAll<PlantableSpec>()` |
| `Timberborn.Fields.Crop` | `Yielder`; crops have `CropSpec → Crop`, `GrowableSpec → Growable`, `CuttableSpec.Yielder` |
| `Timberborn.Growing.Growable` | `IsGrown`, `GrowthInProgress`, `GrowthTimeInDays`, `IncreaseGrowthProgress(fractionOfFullDuration)` (`ITimeTrigger.FastForwardProgress`) |
| `Timberborn.Yielding.Yielder` | `Yield` (`GoodAmount`), `IsYielding`, `IsYieldRemoved`, `RemovalTimeInHours` |
| `Timberborn.Yielding.YielderRemover` | `ReserveForRemoval(Yielder, GoodAmount)`, `Unreserve()`, `HasReservedYielder`; `CompleteReservation` puts goods in `GoodCarrier` hands and decreases the crop yield |
| `Timberborn.Yielding.YieldRemoverBehavior` | `Decide(BehaviorAgent) → Decision` (walk to `IRemoveYieldStrategy.Reacher`, remove, complete) |
| `Timberborn.InventorySystem.GoodReserver` | `ReserveCapacity(Inventory, GoodAmount)`, `UnreserveCapacity()`, `HasReservedCapacity` |
| `Timberborn.InventorySystem.Inventory` | `HasUnreservedCapacity(GoodAmount)`, `Enabled` |
| `Timberborn.SimpleOutputBuildings.SimpleOutputInventory` | `Inventory` |
| `Timberborn.BlockSystem.IBlockService` | `GetFirstObjectWithComponentAt<Crop>/<Yielder>(Vector3Int)` |
| `Timberborn.Navigation.Accessible` | `FindRoadPath(Vector3, out float)`, `FindTerrainPath(Vector3, out float)` (real path length) |
| `Timberborn.Navigation.INavigationService` | `FindPathUnlimitedRange(start, end, corners, out distance)` fallback |
| `Timberborn.TemplateSystem.TemplateService` | `GetAll<PlantableSpec>()` |
| Decorator chain | `AddDecorator<HdbfBoxFarmSpec, …>` works exactly like vanilla `FarmHouseSpec`/`BuilderHubSpec`-keyed decorators; vanilla cascade example: `AddDecorator<Emptiable, HaulCandidate>` |

Raw dumps used for this report (machine-local, never shipped):
`%USERPROFILE%\.hdbf\api\{farm-api,planting,il-behaviors,il-farmhouse,il-worksystem,il-config,il-yielding-tm2,il-harvest,il-decorators2,il-emptying,il-growable,il-planting-service,il-workerroot,api3,more-api}.txt`.
