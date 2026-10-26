# workstream: tech-data — tech-tree unlock gate + top-tier on-grid power

Task: shared task `task-2` (+ follow-up `task-5`). Game version: `1.1.2.8-ffc2de0-sw` (see `docs/API_FACTS.md`).
Status: **data + validator done and verified offline; needs a running-game check** (details in §7).

Short version:

* **A — tech unlock.** Timberborn 1.1.2.8 has **no research-node blueprints and no
  prerequisite system in blueprint data at all** (proof in §2). The real mechanism is
  `BuildingSpec.ScienceCost` + `BuildingToolLocker`/`BuildingUnlockingService`: a building with
  `ScienceCost > 0` is locked in the build menu until the player pays that exact amount of science
  points. The four tiers already carried the chapter-7 prices **200 / 600 / 1000 / 3000** for both
  factions, so the start-lock requirement already held; this workstream added **hard validator
  checks** that lock the mechanism and prices down. No `data/Research/` files are shipped because
  the engine has no such blueprint type (inventing one would be an unknown-spec load error).
  The chapter-7 **prerequisite chain cannot be expressed in data**; the Lead accepted it as a
  **documented deviation** (§2.4, conclusion to copy into REQ_TRACEABILITY in §10.5).
* **B — top-tier on-grid power.** Both `BoxFarm.Top.*` blueprints now carry the vanilla mechanical
  consumer composition (`MechanicalBuildingSpec`, `MechanicalNodeSpec.PowerInput = 150`,
  `TransputProviderSpec`, `MechanicalConnectorTargetSpec`) with three footprint-local connection
  faces on the **rear row only** (`(0,0,1) Left+Top`, `(1,0,1) Top`, `(2,0,1) Right+Top`), i.e.
  inside the 3×1×2 footprint, away from the front entrance/worker access and with no roof
  connection. The other three tiers stay mechanical-spec-free, and the off-grid fallback fields
  (`HdbfBoxFarmSpec.RequiresPower/PowerDemand`) are untouched and now cross-checked against
  `PowerInput`.
* **C — harvest output inventory (follow-up task-5).** All eight blueprints carry
  `SimpleOutputInventorySpec { Capacity: 50, IgnorableCapacity: false }` at the blueprint root, so
  farm-loop's harvest has a real `SimpleOutputInventory` to write into and vanilla hauling can move
  the goods out; the vanilla field-farm marker specs are explicitly not added and are now rejected
  by the validator (§10).

Validator gate: `RESULT: OK - data/ is valid.` exit 0 (command in §6). Nine negative controls for
task-2 plus seven for task-5 each exited 1 with the expected new error and every file was
byte-restored (SHA-256 verified, §6.3/§10.4).

No new localization keys are needed (§9).

---

## 1. What the requirement asked for (read-only source)

`高密度盒子农场模组需求文档.md`:

* ch. 7 (S1–S5): tier prices **200 / 600 / 1000 / 3000**; prerequisites “vanilla farming start”,
  “lumber/planks industry”, “metal processing”, “power network + late storage/logistics”;
  REQ-TECH-01 different tree stages, REQ-TECH-02 visible but greyed when prerequisite missing,
  REQ-TECH-04 build-menu order Basic→Top, REQ-TECH-06 same unlock/cost for both factions.
* ch. 6.5 / 8.3: Top runs on-grid at **150 power**, off-grid fallback = 15 metal / 8 game days,
  `G_power = 0.6`; both paths must stay viable (REQ-BAL-13/14, REQ-COMPAT-14).
* ch. 11.1 / 11.2: 3×2×1 footprint, no walkable roof / no roof connection points (REQ-IMPL-05),
  entrance and worker positions outside the footprint must not be blocked (REQ-IMPL-06),
  `HdbfBoxFarmSpec` keeps the tier data (REQ-IMPL-01/02/03).

---

## 2. Task A evidence: the real 1.1.2.8 unlock mechanism

### 2.1 There are no research / tech-tree blueprints

| Evidence | Result |
|---|---|
| Official 1.1.2.8 blueprint dump `Timberborn_Data\StreamingAssets\Modding\Blueprints.zip` (1067 entries) | top-level folders: `BlockObjectToolGroups, BonusTypes, Buildings, Characters, Configurations, ConstructionBases, CustomCursors, Decals, Environment, Factions, Fireworks, GoodCollections, GoodGroups, Goods, GoodVisualization, IlluminationColors, KeyBindingGroups, KeyBindings, MapEditor, MaterialCollections, Models, NaturalResources, NeedCollections, NeedGroups, Needs, NewGameModes, Recipes, RemoveYieldStrategies, Sounds, TemplateCollections, ToolGroups, Tutorials, WellbeingTiers, WorkerOutfits, WorkerTypes, YieldRemovalChanceBonuses` — **no Research/TechTree/Tech folder** |
| Entry-name search in that zip for `Research|Tech|Unlock|Science` | only `BlockObjectToolGroup.Science`, `Recipe.SciencePoints*`, `Buildings/Science/*`, `Tutorials[...]Science*`, `KeyBinding.*Unlock*` — no node blueprints |
| `docs/api/game-type-index.txt` (all 660 game assemblies) for `ResearchNode|TechTree|Prereq|Requirement` | no such type; `Timberborn.ScienceSystem` contains only `BuildingUnlockingService`, `ScienceService`, `ScienceNeedingBuilding(+Spec)`, `UnlockableOnceSpec`, `BuildingUnlockedEvent` |
| `%USERPROFILE%\.hdbf\ref\Blueprints` (external reference copy used by the validator) | same set; consistent with the zip |

So the `data/Research/**` folder listed in the ownership table is **not applicable** to 1.1.2.8;
no research blueprint is written (an unknown spec name would make the game reject the blueprint —
the validator classifies unknown top-level keys as `[spec] unknown spec`).

### 2.2 The real gate: `BuildingSpec.ScienceCost`

`Timberborn.ScienceSystem.BuildingUnlockingService` (ApiIndex `dump` / `il`):

```
Unlocked(BuildingSpec spec):
    if spec.ScienceCost == 0 -> true
    else -> _unlockedBuildings.Contains(BuildingService.GetTemplateName(spec))
Unlock(spec):
    if (!Unlockable(spec)) throw ArgumentException("Can't unlock <template>, not enough science points!")
    ScienceService.SubtractPoints(spec.ScienceCost)
    UnlockIgnoringCost(spec)        // adds template name, posts BuildingUnlockedEvent
Unlockable(spec): ScienceService.SciencePoints >= spec.ScienceCost
Load(): _unlockedBuildings <- saved ListKey<string> "UnlockedBuildings" (+ UnlockableOnce player data)
```

`Timberborn.BuildingTools.BuildingToolLocker : IToolLocker` is what turns this into menu UX:
`ShouldLock(tool)` and `TryToUnlock(tool)` show the unlock confirmation/insufficient-points dialog
(`CantUnlockLocKey`, `UnlockPromptLocKey`) before calling the service.

Consequences (all proven by the IL above):

1. On a **new game** the unlocked list is empty, so every building with `ScienceCost > 0` is
   **locked**, i.e. the four tiers cannot be built from the start as long as their costs are
   positive — this is exactly the state of the shipped blueprints (200/600/1000/3000 both factions).
2. `BuildingSpec` has exactly 8 serialized fields
   (`SelectionSoundName, LoopingSoundName, BuildingCost, ScienceCost, PlaceFinished,
   FinishableWithBeaversOnSite, DrawRangeBoundsOnIt`), **none of them a prerequisite**;
   `BuildingUnlockingService.Unlockable` only compares science points. Vanilla uses the same flat
   per-building pricing everywhere (e.g. `Gristmill.Folktails` 180, `ChargingStation.IronTeeth`
   200) — there is no tree ordering data to copy.
3. Wrong specs to avoid for this feature (now rejected by the validator): `ScienceNeedingBuildingSpec`
   makes a *running* building consume science per hour, and `UnlockableOnceSpec` turns the unlock
   into a one-per-player profile flag shared by later saves.

### 2.3 What changed for task A

The eight blueprints already had `ScienceCost` 200 / 600 / 1000 / 3000, so no JSON value had to
change; the deliverable is the **hard gate in `tools/validate/DataChecks.cs`** (`CheckTechGating`):

* every tier/faction blueprint must have an integer `BuildingSpec.ScienceCost`;
* it must be `> 0` (a `0` would make the tier buildable from the start) — error text names the
  `BuildingUnlockingService` rule;
* it must equal the chapter-7 price for that tier (200/600/1000/3000);
* both factions must agree (REQ-TECH-06);
* the four prices must be strictly ascending Basic→Top;
* `ScienceNeedingBuildingSpec` / `UnlockableOnceSpec` must not appear (they change the mechanism);
* the gated blueprint must be registered in the faction `TemplateCollection` so the locked tool is
  visible in the build menu (REQ-TECH-02/04 — vanilla shows locked tools with a lock icon, it does
  not hide them).

### 2.4 The prerequisite gap (needs a decision, not fakeable in data)

Chapter 7 wants: Basic only after “built a vanilla farmhouse and harvested once”, Intermediate
after lumber/planks, Advanced after metal, Top after power. With 1.1.2.8 there is **no blueprint
field, spec, collection flag or event that expresses “unlock A only if building/bought B”**.
`TemplateSpec.RequiredFeatureToggle` is a game-feature toggle (e.g. Badtides), not a tech
prerequisite. Therefore the requirement's prerequisite ordering is currently satisfied only by
*price* (200 is reachable early, 3000 is a late-game sink), not by an enforced condition.

If the requirement must be enforced literally, options (all C# gameplay, outside this
workstream's write scope, for the Lead to choose):

* **A (smallest):** a component on the four buildings that subscribes to `BuildingUnlockedEvent` /
  the science service and refuses `BuildingToolLocker.TryToUnlock` until a vanilla prerequisite is
  detected (e.g. a finished `FarmHouse.Folktails/IronTeeth` + a harvested field for Basic,
  `BuildingUnlockingService.Unlocked(plank/metal/power buildings)` for the others). The unlock
  dialog would need its own message (“missing condition”), matching REQ-TECH-02.
* **B (data + code):** introduce a mod spec (e.g. `HdbfTechGateSpec` with prerequisite template
  names) read by gameplay on load; the validator can then check the chain in data. This is the only
  route that keeps the prerequisite table in JSON, but it is new C# + new spec and still needs a
  tool-locker to affect the menu.
* **C (documented deviation):** keep the per-building price gate only and record REQ-TECH-02's
  greyed-prerequisite text as a known deviation. Needs the Lead's decision.

No option was implemented here because `src/**` is another workstream's scope and the requirement's
own ch. 7 note says node names/mechanism follow the current game version.

---

## 3. Task B evidence: mechanical / on-grid power

### 3.1 Spec shapes (read from the installed assemblies)

ApiIndex dump sources (regenerate with `tools/ApiIndex`; see §6.1):

| Spec | Shape | Notes |
|---|---|---|
| `Timberborn.MechanicalSystem.MechanicalBuildingSpec` | empty marker record | decorator adds `MechanicalBuilding` (state/efficiency provider, `IFinishedPausable` — see §3.4) |
| `Timberborn.MechanicalSystem.MechanicalNodeSpec` | `PowerOutput:int`, `PowerInput:int`, `IsShaft:bool` | `PowerInput > 0` ⇒ `MechanicalNode.IsConsumer = true`; also builds `MechanicalNodeActuals.PowerInput/PowerOutput` |
| `Timberborn.MechanicalSystem.TransputProviderSpec` | `Transputs: TransputSpec[]`, `IgnoreRotation:bool` | decorators: `TransputProvider` + `MechanicalNode` |
| `Timberborn.MechanicalSystem.TransputSpec` | `Coordinates:{X,Y,Z}`, `Directions: Directions3D` (flags string), `ReverseRotation:bool` | coordinates are **footprint-local blocks** |
| `Timberborn.MechanicalConnectorSystem.MechanicalConnectorTargetSpec` | empty marker record | adds the vanilla connector visuals (`MechanicalConnectors`/`MechanicalConnectorActivator`); prefab comes from the global `Configurations/MechanicalConnectorFactory.blueprint.json` (`Models/MechanicalConnector/MechanicalConnector`) |

Vanilla consumer composition (all 42 powered consumers, e.g. `Gristmill.Folktails` PowerInput 60,
`BotPartFactory.Folktails` 150): **`MechanicalBuildingSpec` + `MechanicalNodeSpec` +
`TransputProviderSpec` + `MechanicalConnectorTargetSpec`** on the blueprint root. Shaft/clutch/meter
buildings are the only ones with a node spec but no `MechanicalBuildingSpec`, so they are not a model
for a consumer.

### 3.2 Transput coordinate/direction semantics (decompiled, not guessed)

`Timberborn.Coordinates.Direction3D` + `Direction3DExtensions.ToOffset` (IL):

| Direction | Offset | Meaning in 1.1.2.8 |
|---|---|---|
| `Left` / `Right` | `(-1,0,0)` / `(1,0,0)` | ±X faces |
| `Up` / `Down` | `(0,1,0)` / `(0,-1,0)` | ±Y (vertical) |
| `Bottom` / `Top` | `(0,0,-1)` / `(0,0,1)` | ∓Z; `Bottom` = front face (the HDBF entrance side), `Top` = rear face |

`Timberborn.MechanicalSystem.Transput` (IL):

```
get_Coordinates() = blockObject.TransformCoordinates(spec.Coordinates)   // local -> world
get_Direction()   = blockObject.TransformDirection(spec.Direction)      // local -> world
get_Target()      = Coordinates + ToOffset(Direction)
Faces(other)      = other.Target == this.Coordinates
                    && this.Target == other.Coordinates
                    && other.Direction == Across(this.Direction)
```

So `TransputSpec.Coordinates` is a **block cell inside the building's own footprint** (0-based,
same axes as `BlockObjectSpec.Size`), `Directions` names the outward face(s) of that cell, and a
neighbouring power shaft/consumer whose transput faces back connects automatically. A transput whose
target cell is still inside the footprint can never connect and is rejected by the validator.

### 3.3 Vanilla reference geometry used

| Vanilla blueprint | Footprint | Power | Connection faces |
|---|---|---|---|
| `PowerWheel.Folktails` (same 3×1×2 footprint, entrance `(1,-1,0)`) | 3×1×2 | output 50 | one transput `(1,0,0)` `Up` |
| `Gristmill.Folktails` | 3×2×3 | input 60 | six transputs on the front row `z=0` (`Bottom`/`Left`/`Right`/`Up`/`Down`) |
| `BotPartFactory.Folktails` | 3×3×2 | **input 150** | full rear row `z=1` `Top` + front row `Bottom` + side/vertical edges |
| `PaperMill.Folktails` / `OilPress.IronTeeth` | 3×2×2 / 2×2×3 | input 80 / 50 | front row `z=0` + side/vertical edges |

All consumer transputs use `ReverseRotation: false` and `IgnoreRotation: false`; `Top` (rear face)
is an established vanilla consumer face (BotPartFactory, housing, pumps).

### 3.4 Off-grid fallback is not broken by the mechanical specs

* `MechanicalBuilding` only exposes `ActiveAndPowered`, `CanUse`, `Efficiency`; it does **not**
  pause or block the building on its own. `PausableBuilding` uses `IFinishedPausable` only to decide
  whether the player may pause a finished building (`IsPausable`), and pausing is user-initiated.
* `MechanicalSystemUIConfigurator` decorates any `MechanicalNode` with `NoPowerStatus` (vanilla
  status icon/loc keys) plus marker/illuminator visuals — a warning, not a production stop.
* The HDBF production decision stays in `HdbfBoxFarmSpec`/gameplay (`RequiresPower`, `PowerDemand`,
  `MaintenanceKind = 3`, `MaintenanceCycleDays = 8`, `MaintenanceMaterialAmount = 15` are unchanged),
  so the off-grid “15 metal / 8 days, `G_power = 0.6`” path remains available; the on-grid path can
  read `MechanicalNodeActuals.PowerInput`/`MechanicalNode.ActiveAndPowered`.

### 3.5 Chosen geometry (both `BoxFarm.Top.*`)

Added to the blueprint root (after `HdbfBoxFarmSpec`):

```json
"MechanicalBuildingSpec": {},
"MechanicalNodeSpec": { "PowerOutput": 0, "PowerInput": 150, "IsShaft": false },
"TransputProviderSpec": {
  "Transputs": [
    { "Coordinates": { "X": 0, "Y": 0, "Z": 1 }, "Directions": "Left, Top",  "ReverseRotation": false },
    { "Coordinates": { "X": 1, "Y": 0, "Z": 1 }, "Directions": "Top",        "ReverseRotation": false },
    { "Coordinates": { "X": 2, "Y": 0, "Z": 1 }, "Directions": "Right, Top", "ReverseRotation": false }
  ],
  "IgnoreRotation": false
},
"MechanicalConnectorTargetSpec": {},
```

| Coordinate | Faces | Target cells (footprint-local) | Purpose |
|---|---|---|---|
| `(0,0,1)` | `Left`, `Top` | `(-1,0,1)`, `(0,0,2)` | rear-left corner: side + rear shafts |
| `(1,0,1)` | `Top` | `(1,0,2)` | rear-centre main socket |
| `(2,0,1)` | `Right`, `Top` | `(3,0,1)`, `(2,0,2)` | rear-right corner: side + rear shafts |

Rationale:

* all three cells are inside the 3×1×2 footprint (`x 0..2`, `y 0`, `z 0..1`);
* the whole socket set is on the **rear row (z=1)** — the HDBF entrance `(1,-1,0)` and
  `BuildingAccessibleSpec.LocalAccess (1.5,0,0.5)` are on the front row (z=0), so no power
  shaft/connectable cell enters the worker approach; deliberately narrower than vanilla
  (vanilla consumers also expose front-face sockets);
* no `Up`/`Down` face: REQ-IMPL-05 / ch. 11.2 #4 forbid walkable roofs and roof connection points,
  and a vertical shaft standing on the roof would create a worker-reachable structure above the
  footprint;
* `ReverseRotation: false`, `IgnoreRotation: false`, `PowerOutput: 0`, `IsShaft: false` — the vanilla
  consumer conventions; `MechanicalConnectorTargetSpec` gives the vanilla connector stubs as the only
  visible “this building takes power” cue until real art exists (`data/ART_PLACEHOLDER.md` models are
  placeholders);
* only the two Top blueprints change; Basic/Intermediate/Advanced stay free of mechanical specs, and
  their `RequiresPower=false` / `PowerDemand=0` expectations are still enforced.

### 3.6 Validator checks added (task B)

`CheckMechanicalPower` in `tools/validate/DataChecks.cs`:

* Basic/Intermediate/Advanced: none of `MechanicalBuildingSpec`, `MechanicalNodeSpec`,
  `TransputProviderSpec`, `MechanicalConnectorTargetSpec` may appear;
* Top: all four must exist (`MechanicalBuildingSpec`/`MechanicalConnectorTargetSpec` as objects);
* `MechanicalNodeSpec.PowerOutput == 0`, `.PowerInput == 150`, `.IsShaft == false`;
* `HdbfBoxFarmSpec.PowerDemand` must equal `MechanicalNodeSpec.PowerInput` (REQ-IMPL-21 — keeps the
  custom off-grid field and the engine field in sync);
* `TransputProviderSpec.IgnoreRotation == false`; `Transputs` non-empty; every
  `Coordinates` inside the `BlockObjectSpec.Size` footprint; every direction token is one of
  `Top/Left/Right`; every direction target lies outside the footprint; at least one rear (`Top`)
  face; every `ReverseRotation == false`;
* both Top factions must have the same `PowerInput` and the same transput signature (REQ-TECH-06).

Blueprint/`spec` validation of the new keys themselves is generic already: `BlueprintValidator`
verifies each spec name exists in game metadata, each field exists on the spec, and every
`Directions` token is a real `Directions3D` member.

---

## 4. Files changed

| File | Change |
|---|---|
| `data/Buildings/Food/BoxFarm/BoxFarm.Basic.*`, `BoxFarm.Intermediate.*`, `BoxFarm.Advanced.*` (6 files) | task-5: + `SimpleOutputInventorySpec { Capacity: 50, IgnorableCapacity: false }` at the blueprint root |
| `data/Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.blueprint.json` / `.Top.IronTeeth.blueprint.json` | task-2: + `MechanicalBuildingSpec`, `MechanicalNodeSpec` (`PowerInput: 150`), `TransputProviderSpec` (3 rear-row transputs), `MechanicalConnectorTargetSpec`; task-5: + `SimpleOutputInventorySpec` (Capacity 50) |
| `tools/validate/DataChecks.cs` | task-2: `CheckTechGating`, `CheckMechanicalPower`, `CheckTopMechanicalSpecs`, helpers `ReadTemplateCollectionEntries`, `AssertEmptyMarkerSpec`, `TryGetInt`, `_collectionEntries` cache; task-5: `CheckOutputInventory`, `ForbiddenFarmSpecs`, `SimpleOutputInventorySpec` in `RootOnlySpecs`; all wired into `Validate()` |
| `docs/workstreams/tech-data.md` | this report |

Not changed (deliberately): `data/Research/**` (no such blueprint type), `data/Localizations/**`
(UI workstream), `src/**`, `tests/**`, `build.ps1`, `data/ART_PLACEHOLDER.md` (models unchanged);
Basic/Intermediate/Advanced stay free of mechanical and tech-extra specs. No
`FarmHouseSpec`/`PlanterBuildingSpec`/`YieldRemovingBuildingSpec` was added (now validator-enforced).

---

## 5. Chapter mapping

| Requirement | Where satisfied |
|---|---|
| REQ-TECH-01/03 (four separate gates, one-shot cost) | 4 separate `ScienceCost` values, engine subtracts the whole cost once (`BuildingUnlockingService.Unlock`); validator enforces 200/600/1000/3000 |
| REQ-TECH-02 (locked, visible, greyed) | `ScienceCost > 0` ⇒ `BuildingToolLocker.ShouldLock` shows the locked tool + unlock dialog; validator requires the template to stay in the faction collection |
| REQ-TECH-04 (build-menu order) | `PlaceableBlockObjectSpec.ToolGroupId = Food`, `ToolOrder` 95/96/97/98 (unchanged, validator-enforced) |
| REQ-TECH-05 (no 0/negative cost) | validator `ScienceCost > 0` check (+ existing equality check) |
| REQ-TECH-06 (faction parity) | validator cross-faction cost equality + identical Top power geometry |
| ch. 6.5 / 8.3 / REQ-COMPAT-14 (150 power on-grid) | `MechanicalNodeSpec.PowerInput = 150` on both Top tiers |
| REQ-BAL-13/14 (off-grid path preserved) | `RequiresPower/PowerDemand/Maintenance*` untouched; PowerDemand ↔ PowerInput cross-check |
| REQ-IMPL-05 (no roof connection / worker above footprint) | no `Up`/`Down` transput faces; validator rejects vertical directions |
| REQ-IMPL-06 (entrance/worker access not blocked) | power faces only on the rear row; validator rejects front (`Bottom`) faces |
| REQ-IMPL-21 / ch. 11.1 (data-only, spec table) | all tier differences stay in JSON; no C# touched |
| ch. 6.8 / 11.5, REQ-BAL-22/23 (harvest output is real, then hauled) | task-5: `SimpleOutputInventorySpec` (Capacity 50) on all 8 blueprints gives every tier a native `SimpleOutputInventory` + hauling path; validator-enforced (§10) |

Chapter 7's **prerequisite conditions** are the only requirement not satisfiable in data — see §2.4/§8.

---

## 6. Validation: commands, result, negative controls

### 6.1 Fact-gathering commands (rerunnable)

```powershell
# metadata of the installed game (writes outside the workspace)
dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1
$managed = "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed"
dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll dump $managed $out "Timberborn.ScienceSystem" "Timberborn.BuildingTools.BuildingToolLocker"
dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll il   $managed $out2 "Timberborn.ScienceSystem.BuildingUnlockingService" "Unlocked|Unlock|Unlockable|Load$"
dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll dump $managed $out3 "Timberborn.MechanicalSystem.MechanicalNodeSpec" "Timberborn.MechanicalSystem.TransputProviderSpec" "Timberborn.MechanicalSystem.TransputSpec" "Timberborn.MechanicalConnectorSystem.MechanicalConnectorTargetSpec"
dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll il   $managed $out4 "Timberborn.MechanicalSystem.Transput" "get_Coordinates|get_Target|Faces"
dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll il   $managed $out5 "Timberborn.Coordinates.Direction3DExtensions" "ToOffset|cctor"
```

### 6.2 Main gate (the command from the task description)

```powershell
dotnet build tools\validate\validate.csproj -c Release -m:1
dotnet run --project tools\validate\validate.csproj -c Release -m:1 -- data `
  --managed "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed" `
  --ref "$env:USERPROFILE\.hdbf\ref" --assemblies "$env:USERPROFILE\.hdbf\work\bin"
```

Observed (final state):

```
  game assemblies : 660
  spec types      : 506 (game + mod ComponentSpec subclasses)
  mod assemblies  : 7 (...)
  blueprints      : 10
WARNINGS: none
RESULT: OK - data/ is valid.          (exit 0)
```

### 6.3 Negative controls (each injected, observed, then restored)

Backup + SHA-256 restore: all mutated files were copied to a temp folder before injection and
compared by SHA-256 after restore — `restoreOk=True` for every batch.

| # | Injected change | Expected new error | Exit | Observed |
|---|---|---|---|---|
| C1 | `Basic.Folktails` `ScienceCost` 200 → **150** | `[tech] ... must be the chapter-7 Basic price 200 (found 150)` | 1 | ✅ (+ the pre-existing `[blueprint]` equality error) |
| C2 | `Basic.IronTeeth` `ScienceCost` 200 → **0** | `[tech] ... must be > 0 ... buildable from the start (BuildingUnlockingService treats ScienceCost == 0 as always unlocked)` | 1 | ✅ |
| C3 | `Top.Folktails` `PowerInput` 150 → **120** | `[mechanical] MechanicalNodeSpec.PowerInput must be 150 (found 120)` + PowerDemand↔PowerInput + faction-parity errors | 1 | ✅ (3 matching errors) |
| C4 | `Top.IronTeeth` key `TransputProviderSpec` renamed | `[mechanical] ... missing TransputProviderSpec ...` | 1 | ✅ (+ `[spec] unknown spec`) |
| C5 | inject `MechanicalNodeSpec` into `Advanced.Folktails` | `[mechanical] 'MechanicalNodeSpec' is only allowed on the Top tier...` | 1 | ✅ (only error) |
| C6 | `Top.Folktails` transput #2 `X` 2 → **3** | `[mechanical] ... Coordinates (3,0,1) is outside the 3x1x2 footprint` | 1 | ✅ |
| C7 | `Top.Folktails` middle `Directions` `Top` → **`Bottom`** | `[mechanical] ... 'Bottom' points at the front face, where the HDBF entrance and worker access are` | 1 | ✅ (+ faction geometry parity error) |
| C8 | `Top.IronTeeth`: all `Top` tokens replaced by `Left`/`Right` | `[mechanical] ... needs at least one rear ('Top') power connection face` | 1 | ✅ |
| C9 | `Top.Folktails` middle transput `Z` 1 → **0** (faces into the footprint) | `[mechanical] ... faces 'Top' towards (1,0,1), which is still inside the footprint` | 1 | ✅ |

Also verified while red: the run fails with `RESULT: FAILED` exit 1, and after restore the run is
`RESULT: OK` exit 0 (`restoreOk=True`, SHA-256 identical). The follow-up task-5 controls (output
inventory spec, plus re-confirmation that the mechanical and ScienceCost gates still fire) are
tabulated in §10.4.

---

## 7. What still needs a running game (not proven offline)

These are serialization/behaviour questions a headless validator cannot answer; run them in a
creative/test world after `build.ps1 -Install`:

1. **Blueprint load**: no `InvalidOperationException`/`ArgumentException` at world load or when the
   build-menu previews are created for both factions (new specs deserialize; `MechanicalConnector`
   prefab resolves).
2. **Placement**: Top places on flat ground, entrance/worker access works, the three rear connector
   stubs are visible; no collision with the placeholder LargeWarehouse/FoodFactory model.
3. **On-grid**: place a power source + shafts behind the building (rear face) → the building shows
   power and `MechanicalNodeActuals.PowerInput` reaches 150; game panel (once gameplay/UI reads it)
   reports on-grid `G_power = 1.0`. Also check a side connection (rear corner `Left`/`Right` faces).
4. **Off-grid**: no grid at all → building still works with the off-grid fallback (15 metal / 8 days,
   `G_power = 0.6`); the vanilla `NoPowerStatus` icon is expected and must not hard-stop production.
5. **Unlock flow** (task A): new game → the four tiers appear locked in the Food menu; clicking each
   charges exactly 200/600/1000/3000 science and unlocks it; a save/reload keeps them unlocked; the
   four tools are ordered Basic→Top; no earlier tier can be built at game start.
6. **Save/load**: place a Top building, connect, save, reload → mechanical node/transputs rebuild
   (they are not persisted in the custom record; vanilla rebuilds them), connector state stable.
7. **Faction parity**: repeat 1–6 for the other faction.
8. **Output inventory / hauling (task-5)**: in the entity panel, every tier shows the vanilla output
   inventory (capacity 50, "not ignorable"); after farm-loop harvests into it the goods are offered
   to haulers and leave the building; the stockpile never exceeds 50 and hauling does not stall when
   the building is full; save/reload keeps the stored goods (vanilla inventory persistence).

---

## 8. Open questions for the Lead

1. **Chapter-7 prerequisites** (§2.4) — **resolved by Lead: documented deviation (option C).**
   `ScienceCost` is the only native gate in 1.1.2.8; the prerequisite chain remains a
   documentation-level deviation and the Lead records it in `docs/REQ_TRACEABILITY.md`.
   Copy-ready conclusion text is in §10.5. Only if this is revisited later: options A/B need new
   gameplay code plus new localization keys for the “missing prerequisite” dialog (UI teammate owns
   the CSVs).
2. **Front-face power sockets**: HDBF deliberately allows rear/side only, while vanilla consumers also
   expose the front face. Confirm that “player must route power behind the Top building” is the
   intended UX (it keeps the entrance clear, but it is stricter than vanilla).
3. **`MechanicalConnectorTargetSpec` visuals**: expected to show three small vanilla connector stubs
   on the rear row; if the placeholder model makes them look wrong the spec can be dropped without
   losing function (only visuals), but that removes the only in-world “power port” cue until art
   exists. Needs the in-game check in §7.2.

---

## 9. Localization

**No new localization keys.** The added specs carry no `*LocKey` fields; the locked-tool prompt,
insufficient-science dialog and the vanilla `NoPowerStatus` icon all use vanilla keys
(`BuildingToolLocker.CantUnlockLocKey/UnlockPromptLocKey`, `NoPowerStatus.NoPowerLocKey`).
The task-5 `SimpleOutputInventorySpec` is loc-key-free too (the vanilla inventory panel/hauling
statuses use vanilla keys), so no CSV change is needed from the UI workstream for either task.

---

## 10. Follow-up (task-5): harvest output inventory + chapter-7 prerequisite conclusion

Follow-up shared task `task-5`: give farm-loop's P1 harvest a real vanilla output inventory to
deposit into, add the matching validator gate, and record the chapter-7 prerequisite conclusion for
`REQ_TRACEABILITY`.

### 10.1 Evidence: the spec is sufficient and the values are the vanilla farmhouse ones

`Timberborn.SimpleOutputBuildings.SimpleOutputInventorySpec` (ApiIndex `dump`, installed 1.1.2.8):

| Field | Type | Vanilla farmhouse value |
|---|---|---|
| `Capacity` | `int` | 50 |
| `IgnorableCapacity` | `bool` | false |

`SimpleOutputBuildingsTemplateModuleProvider.Get` IL shows the spec is enough to get the whole
pipeline (no extra specs needed):

```
AddDecorator<SimpleOutputInventorySpec, SimpleOutputInventory>()
AddDedicatedDecorator<SimpleOutputInventory, Inventory>(SimpleOutputInventoryInitializer)   // capacity from the spec
AddDecorator<SimpleOutputInventory, InventoryNeedBehavior>()
```

with `SimpleOutputInventoryHaulBehaviorProvider : IHaulBehaviorProvider` making the stored goods
haulable. Vanilla values for reference: `EfficientFarmHouse.Folktails` / `FarmHouse.IronTeeth` /
`AquaticFarmhouse` / `TappersShack` all use `Capacity 50, IgnorableCapacity false`; gathering flags
use 20; `BuildersHut`/`DistrictCenter` use 20/true. All **15** vanilla usages declare the spec at
the **blueprint root** — none is nested under `Children` (checked by walking every vanilla children
tree), which is why the validator now treats it as root-only.

### 10.2 What was added

All eight faction blueprints (`Basic`/`Intermediate`/`Advanced`/`Top` × `Folktails`/`IronTeeth`) now
carry at the blueprint root, directly after `HdbfBoxFarmSpec`:

```json
"SimpleOutputInventorySpec": {
  "Capacity": 50,
  "IgnorableCapacity": false
},
```

Deliberately **not** added (and now rejected by the validator): `FarmHouseSpec`,
`PlanterBuildingSpec`, `YieldRemovingBuildingSpec` — farm-loop's route drives the vanilla
worker-side planting/harvest behaviors directly and does not want the building to become a vanilla
field farm or own a plantable group.

### 10.3 Validator checks added (`CheckOutputInventory`)

* every one of the 8 blueprints must have `SimpleOutputInventorySpec` as an object at the root
  (missing ⇒ error naming the consequence: harvest output has no `SimpleOutputInventory`);
* `Capacity` must be an integer within **\[20,100\]**, and all eight values must be identical (one
  balance number, no per-tier divergence);
* `IgnorableCapacity` must be `false`;
* `ForbiddenFarmSpecs` (`FarmHouseSpec`, `PlanterBuildingSpec`, `YieldRemovingBuildingSpec`) must
  not appear on the HDBF blueprints;
* `SimpleOutputInventorySpec` is in `RootOnlySpecs`, so a copy nested in `Children.*` is an error
  (vanilla is root-only 15/15; a child copy would put the inventory on the child prefab).

### 10.4 Task-5 negative controls (each injected, observed, restored)

Backups + SHA-256 restore as in §6.3 (`restoreOk=True`), and the validator is rebuilt once before
the batch (`dotnet build tools\validate\validate.csproj -c Release -m:1`, 0 warnings/0 errors):

| # | Injected change (file) | Expected new error | Exit | Observed |
|---|---|---|---|---|
| T5-C1 | remove the whole spec from `Intermediate.Folktails` | `[inventory] ... missing SimpleOutputInventorySpec at the blueprint root; harvest output would have no SimpleOutputInventory to write into ...` | 1 | ✅ |
| T5-C2 | `Intermediate.IronTeeth` `Capacity` 50 → **150** | `[inventory] ... Capacity must be within [20,100] (found 150)` | 1 | ✅ |
| T5-C3 | `Intermediate.IronTeeth` `Capacity` 50 → **60** | `[inventory] ... Capacity (60) must match ...Basic.Folktails... (50); all four tiers share the same output inventory ...` | 1 | ✅ |
| T5-C4 | `Basic.Folktails` `IgnorableCapacity` false → **true** | `[inventory] ... IgnorableCapacity must be false (found true)` | 1 | ✅ |
| T5-C5 | inject `"FarmHouseSpec": {}` into `Basic.Folktails` | `[inventory] ... 'FarmHouseSpec' must not be added; ...` | 1 | ✅ |
| T5-C6 | re-confirm mechanical gate: `Top.Folktails` `PowerInput` 150 → **120** | `[mechanical] MechanicalNodeSpec.PowerInput must be 150 (found 120)` | 1 | ✅ |
| T5-C7 | re-confirm tech gate: `Basic.Folktails` `ScienceCost` 200 → **150** | `[tech] ... must be the chapter-7 Basic price 200 (found 150)` | 1 | ✅ |

Final full run after restore:

```
  game assemblies : 660
  spec types      : 506 (game + mod ComponentSpec subclasses)
  blueprints      : 10
WARNINGS: none
RESULT: OK - data/ is valid.          (exit 0)
```

### 10.5 Chapter-7 prerequisite conclusion (copy-ready for REQ_TRACEABILITY)

> Timberborn 1.1.2.8 ships no research-node blueprint type and exposes no blueprint field, spec,
> collection flag or event that expresses a prerequisite chain: the official
> `StreamingAssets/Modding/Blueprints.zip` (1067 entries) has no Research/Tech folder, the game
> assemblies contain no `ResearchNode`/`TechTree`/prerequisite type, and
> `BuildingUnlockingService.Unlockable` only compares science points. The only native unlock gate is
> `BuildingSpec.ScienceCost` read by `BuildingUnlockingService` + `BuildingToolLocker`: a building
> with `ScienceCost > 0` is locked in the build menu until the player pays exactly that many science
> points (one shot; saved in the `UnlockedBuildings` list; visible-but-locked tool with the vanilla
> unlock dialog). **Satisfied in data:** the four separate 200/600/1000/3000 gates, one-shot payment,
> no zero/negative cost, ascending build-menu order in the Food group, and faction parity
> (REQ-TECH-01/03/04/05/06). **Documented deviation:** REQ-TECH-02's per-stage prerequisite chain
> (vanilla farming start → lumber/planks → metal → power network) is **not data-expressible** in
> 1.1.2.8 and is accepted as a v0.1 documentation-level deviation; the escalating price (200/600/
> 1000/3000) is the only progression pressure. Enforcing the chain would require gameplay code (a
> custom tool-locker/component subscribing to `BuildingUnlockedEvent` and gating
> `BuildingToolLocker.TryToUnlock`) plus its own localization keys, which is out of scope for the
> data workstream.

No `data/Research/**` files exist or are needed; no localization keys were added for the prerequisite
deviation (the vanilla unlock dialog keys are reused).

### 10.6 Needs an in-game check (task-5 additions)

* every tier's entity panel shows the vanilla output inventory (capacity 50, not ignorable);
* a harvest deposits real goods into it, haulers pick them up and the stored amount never exceeds
  capacity; a full inventory does not throw or stall the crop loop;
* save/reload keeps the stored goods (native inventory persistence, not the HDBF record);
* both factions behave identically.

## 補記（block-axis 修正卡，2026-10-25）

本報告的 transput 軸向分析沿用舊假設「Y=高、Z=深」，與遊戲實際網格（X=寬、Y=深、Z=高）不同；
transput 方向 token 的實際語意是 `Up`=+Y（背面）、`Down`=−Y（正面）、`Top`/`Bottom`=±Z（垂直面）。
Top 的動力 transput 已改為 `(0,1,0)`/`(1,1,0)`/`(2,1,0)` 配 `Left, Up`/`Up`/`Right, Up`，
`BlockObjectSpec.Size` 改為 `{X:3,Y:2,Z:1}`。本文舊數值不再適用，推導與實機 A/B 見
`docs/workstreams/bug-08-block-axis-size.md`。

