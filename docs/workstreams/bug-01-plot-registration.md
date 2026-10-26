# BUG-01 (P0) — plot registration / irrigation z semantics

Status: **offline fix complete; in-game verification still pending (no claim of "0 % fixed in game")**.
Card date: 2026-10-25. Workspace: `Documents\Timberborn\Mods\HighDensityBoxFarm` (source only,
no deploy, no game launch, no `build.ps1`).

Honest one-line summary: the z semantics are now **proven from the installed IL** (the plant cell
and the `SoilIsMoist` sample cell are the same cell — the terrain column ceiling), the coverage is
**resampled immediately after every registration change** instead of only on the daily tick, and an
empty / reachability-starved registry now writes a **stable `HDBF/Warn` line**. Whether the 0 %
panel symptom is gone in a real save is **not yet verified** — it needs one unlocked desktop run
with the log lines listed at the end.

---

## 1. IL evidence (real assemblies, no guessing)

Dump tool: `tools\ApiIndex` (`dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1`;
`ApiIndex.dll il <Managed> <out> <typePattern> [methodPattern]`).
Source assemblies: `C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`.
Verbatim method bodies are archived in **`docs/api/plot-z-semantics-il.txt`** (line references below
are to that file; the dump itself is reproducible with the commands in §5).

### Q1: which cell does `PlantingAreaValidator.CanPlant` expect?

**Answer: the terrain column ceiling — the first air cell above the solid voxel, i.e. the same cell
the Fields tool paints (`topSolidZ + 1`), not the solid voxel and not a cell higher.**

1. The Fields tool's coordinate source is
   `Timberborn.TerrainQueryingSystem.TerrainAreaService+.<InMapLeveledCoordinates>d__4::MoveNext`
   (`plot-z-semantics-il.txt:336`):
   * `startHeight = pickedTerrainCoordinates.z + 1`;
   * for each input block it yields `new Vector3Int(block.x, block.y, startHeight)` **only when
     `ITerrainService::OnGround(block.Above())` is true**.
2. The same coordinates are handed to the validator and to the planting map:
   `PlantingSelectionService::HighlightMarkableArea` calls
   `PlantingAreaValidator::CanPlant(coordinates, name)` (`:436`), and
   `PlantingSelectionService+.<>c__DisplayClass14_0::<MarkArea>b__1` calls
   `PlantingService::SetPlantingCoordinates(coordinates, name)` (`:486`) with the **same**
   coordinates.
3. `PlantingAreaValidator::CanPlant` (`:501`) is
   `IsSamePlantable(coordinates, name) || SpawnValidationService::IsUnobstructed(coordinates, name)`.
   `IsUnobstructed` (`:536`) builds `new Placement(coordinates)` for the crop
   `BlockObjectSpec` and calls `BlockValidator::BlocksValid` (`:562`).
   `BlockValidator::BlockConflictsWithTerrain` (`:581`) rejects a block whose own coordinate is
   terrain (`ITerrainService::Underground`), while `MatterBelowValidator` requires the crop to
   stand on ground — so a crop is only valid when the coordinate is the air cell directly above
   solid terrain.
4. `ITerrainService::OnGround(coordinates)` is
   `IsTerrainVoxel(coordinates.Below()) && !IsTerrainVoxel(coordinates)` (`:32`), the predicate
   step 1 already requires.

### Q2: which `coordinates.z` does `SoilIsMoist` expect?

**Answer: exactly the same cell z — the terrain column ceiling (`TerrainColumn.Ceiling`).**

1. `Timberborn.TerrainSystem.TerrainService::GetTerrainHeight` (`:18`) returns
   `coordinates.z + relativeHeight` from `TerrainMap::TryGetRelativeHeight` (`:62`). For a voxel
   inside a solid run the relative height is the distance to the top of that run
   (`TerrainMap::GetDistanceToTerrainTop`, `:126`), so probing with
   `new Vector3Int(x, y, 0)` on a normal column returns `topSolidZ + 1`.
2. `ColumnTerrainMap::GetColumn(index, height)` (`:234`) only returns a column while
   `height >= Floor && height < Ceiling`, i.e. **`Ceiling` is exclusive**: the solid voxels are
   `Floor..Ceiling-1` and `Ceiling` is the first air cell. Therefore
   `GetTerrainHeight(z=0) == TerrainColumn.Ceiling == the plant cell z`.
3. `Timberborn.Planting.PlantingSoilValidator::Validate` (`:605`) passes the planting coordinate
   itself: `_soilMoistureService.SoilIsMoist(plantingSpot.Coordinates)` — the game already treats the
   plant cell as the moisture sample cell.
4. `SoilMoistureService::SoilIsMoist` (`:635`):
   * `index2D = MapIndexService::CellToIndex(VectorExtensions::XY(coordinates))` (`:665`);
   * `IThreadSafeColumnTerrainMap::TryGetIndexAtCeiling(index2D, coordinates.z, out index3D)`;
   * if it returns false → `ldc.i4.0; ret` (reported as dry);
   * `ThreadSafeColumnTerrainMap::TryGetIndexAtCeiling` (`:292`) succeeds only when some
     `ReadOnlyTerrainColumn.Ceiling == coordinates.z`.

### Conclusion applied to the code

`plant cell z == terrain column ceiling == moisture sample cell z` in this game build. The current
code's `surface = TerrainService.GetTerrainHeight(new Vector3Int(x, y, 0))` was **already the right
z for both APIs**. What the card changes is (a) making that fact explicit and test-guarded in
`HDBF.Core.Plots.PlotCellHeights` instead of an inline assumption, (b) adding the game's own
`OnGround(plantCell)` predicate as a defensive check, and (c) the two behavioural P0 fixes below.

The `CellIsField` root cause found by the Lead in round 6 (fresh maps have no planting coordinate,
so `CellIsField` is always false → empty registry → 0 % coverage) is consistent with this evidence:
`CellIsField` reads the field map at the planting coordinate z, which is exactly the surface cell.

---

## 2. What changed

### New pure Core logic (unit-testable, no game/Unity reference — REQ-TEST-03)

| File | Purpose |
| --- | --- |
| `src/HDBF.Core/Plots/PlotCellCodec.cs` | Plot-id bit layout/en/de-code moved to Core; `IsEncodable` range guard. |
| `src/HDBF.Core/Plots/PlotCellHeights.cs` | The z seam + the full IL evidence chain in the type comment. `ResolvePlantCellZ(columnCeiling)`, `ResolveMoistureSampleZ(plantZ)`, pinned `PlantingToMoistureCellOffset = 0`. |
| `src/HDBF.Core/Plots/PlotScanStats.cs` | Production per-scan counters, `ShouldWarnAboutRegistration` / `WarningKey` / `Describe` (stable message). |
| `src/HDBF.Core/Balance/IrrigationCoverage.cs` | Edge-safe coverage sample (`Compute(registered, moist, tankFed)`): 0 registered → 0 with no division, untrusted counters clamped. |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` | New stable keys `plot.scan-empty` (:43) and `plot.scan-reachability` (:50). |

### Gameplay changes

* `src/HDBF.Gameplay/Components/HdbfPlotCellCodec.cs:20-32` — now a thin `Vector3Int` adapter over
  `PlotCellCodec`; behaviour (bits, negative coordinates, z range) unchanged, z documented as the
  plant cell.
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs`
  * `:22` production `_scanStats`; `:64` `ResetScanCounters()` on every scan start (so the empty
    registry warning does not depend on `FarmDiagnostics`); `:362-375` also resets the temporary
    diagnostics counters unconditionally (the diagnostics file itself is untouched);
  * `:123-124` plant cell from `PlotCellHeights.ResolvePlantCellZ(GetTerrainHeight(x,y,0))` with the
    IL evidence in the comment;
  * `:136-142` defensive `ITerrainService.OnGround(cell)` check (the exact predicate the Fields tool
    requires, verified IL);
  * `:286` **coverage resampled immediately after `ApplyPlotArrays`** via the existing
    `SampleMoistPlots` path (never per frame);
  * `:292` `WarnIfRegistrationLooksBroken()` — one `HDBF/Warn` per completed scan when
    `registered == 0` or reachability rejects > accepted;
  * `:348-354` the old `if (_coverage > 0f)` coverage clamp (dead before the first day tick) is
    replaced by unconditional `ClampCoverageToRegisteredPlots()`.
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Simulation.cs`
  * `:82-85` daily coverage snapshot now uses `IrrigationCoverage.Compute` (same numbers as before,
    clamped);
  * `:112-145` `SampleMoistPlots` converts plot cell → sample cell through
    `PlotCellHeights.ResolveMoistureSampleZ` with the `PlantingSoilValidator` / `SoilIsMoist` IL
    comment;
  * `:167-190` new `RefreshCoverageFromRegisteredPlots` (0 registered → coverage 0, no sampling, no
    division; otherwise `SampleMoistPlots` + clamped snapshot);
  * `:193-210` new `ClampCoverageToRegisteredPlots` for non-sampling registration changes.
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:128` — player plot exclusion also
  resamples coverage immediately (same stale-0 % class).

Not touched: `data/`, `docs/VERIFICATION.md`, `docs/REQ_TRACEABILITY.md`,
`HdbfBoxFarmController.Diagnostics.cs` (still present, `FarmDiagnostics = true`, unchanged),
`build.ps1`, any deployed folder, any save, the game. No Harmony patch added, no static mutable
state added, no per-tier balance branch added.

---

## 3. New L1 tests (`tests/HDBF.Tests/PlotRegistrationHeightTests.cs`, 22 tests)

* plot-id codec bijection: map boundaries (`:29`), full-range sample sweep (`:39`), negative
  coordinate injectivity 65×65 (`:55`), sign-bit decode (`:73`), out-of-range guard (`:82`),
  documented save bit layout locked (`:92`);
* plant-cell == moisture-sample-cell: column ceiling identity (`:120`), identity/offset pin
  (`:135`), stored plot id → decoded cell → sample z end-to-end (`:152`);
* coverage boundaries: 0 registered without division (`:174`), all dry (`:190`), all moist (`:198`),
  partial (`:205`), moist + tank-fed (`:213`), untrusted counter clamping (`:223`), excluded plot
  not counted (`:236`);
* empty/starved registry visibility: counter reset (`:278`), empty warning + message fields
  (`:307`), accepted-but-zero-registered (`:327`), reachability starvation (`:336`), healthy scan
  no warning (`:353`), stable log keys (`:370`).

Run output (real, this machine):

```
HDBF L1 test run - 2026-10-25T03:20:06.0723103Z
...
PASSED 283  FAILED 0
```

(= 261 pre-existing tests + 22 new.)

---

## 4. Build / test evidence

```
$w = Join-Path $env:TEMP 'hdbf-card-work'

dotnet build HDBF.sln -c Release -m:1 -p:HdbfWorkRoot=$w
  -> HDBF.Core / HDBF.Persistence / HDBF.Gameplay / HDBF.Integration / HDBF.UI
     -> C:\Users\user\AppData\Local\Temp\dsh-XfwQbO\hdbf-card-work\bin\...
     已成功生成。0 个警告 0 个错误
  (also HDBF.Tests, ApiIndex, validate built green; all outputs outside the workspace)

dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
  -> PASSED 283  FAILED 0
```

Targeted card command (`HDBF.Gameplay` + `HDBF.Tests`, both `-m:1`) was also run green with
0 warnings / 0 errors. `dotnet --version` = `9.0.305`. Nothing was deployed and `build.ps1` was not
executed (the Lead runs the five-assembly gate + validate + deploy).

---

## 5. Remaining assumptions that only an in-game run can settle

1. **The z conclusion is IL-level, not observed in a live map.** The math says
   `GetTerrainHeight(x,y,0)` = column ceiling = Fields-tool plant cell; if a specific map had the
   probe column's lowest run not starting at z=0 (floating terrain at the map bottom), the probe
   could return 0 and the new `OnGround` check would safely reject that cell (no bad plot
   registered, possible missed plot). This is a miss-tolerance, not a false-positive path.
2. **The in-game 0 % symptom's primary root cause is assumed to be the already-applied
   `CellIsField` → `CanPlant` fix** (round 6). This card proves the z alignment and makes the
   registry visible *if it is still empty*, but cannot confirm in game that `CanPlant` accepts the
   scan cells on the test map.
3. **`SoilMoistureSimulator.MoistureLevels` fixture still needs to be visible to
   `SoilMoistureService`**: the service copies the simulator levels into
   `_threadSafeMoistureLevels` during `InitializeDesertIntensity` on `Load`, and only propagates
   subsequent changes flagged by `MoistureLevelsChangedLastTick`. The fixture rewrite must happen
   before/at load (as in the previous recipe) or a tick must follow with the changed flags set.
4. **The immediate-resample path samples up to `PlotCapacity` plots (≤600) once per scan** — a
   one-off cost at scan completion, not per frame; no measurable frame-budget concern expected, but
   it is unmeasured in game.
5. **Throttled warning cadence**: one scan-complete warning per key per 30 s window; a farm that
   stays broken logs once per window, not once per scan. Suppressed repeats are counted by the
   existing `ThrottledLog`.

---

## 6. Lead in-game verification steps (one unlocked run)

1. `build.ps1 -Install` as usual (all 5 assemblies 0/0, L1 green, `tools/validate RESULT: OK`).
2. Launch with the existing recipe; place/load the box farm on a flat grassy area (or load the
   all-moist fixture save where `SoilMoistureSimulator.MoistureLevels = 16`).
3. **Before the first game day** (within the first seconds after the building finishes loading):
   * panel 灌溉覆蓋率 must now show a non-zero value when the registered plots are moist — this is
     the new immediate resample; previously it stayed 0 % until the first daily tick. If it still
     shows 0 %, check the `diag.day` line value first (below) to separate UI binding from sampling.
   * grep `%TEMP%\hdbf-round\Player.log` / `docs/in-game/<tag>-Player.log` for:
     * `[HDBF/Info] diag.scan.begin` — `origin=`/`side=`/`entrance=`/`cell=` (is the scan centred
       on the entrance and does `DescribeCell` show `surface=h` with `contains=True`?);
     * `[HDBF/Info] diag.scan.probe` — **`canPlant` must be > 0** (ideally ≈ available cells); if
       `canPlant=0`, `PlantingAreaValidator` still rejects every cell and the problem is step 3/4;
     * `[HDBF/Info] diag.scan.end` — `registeredNow` must be > 0 (up to the tier capacity);
       `rejects[field=…]` is the bucket to read if `accepted` is 0;
     * **`[HDBF/Warn] plot.scan-empty: …` / `plot.scan-reachability: …`** (new): each line carries
       `entityId=`, `registered=`, `scanned=`, `accepted=`, `rejects[terrain,field,block,blocker,
       reach,excluded,claimed]`;
     * `[HDBF/Info] diag.day` — after one game day: `registered=`, `moist=`, `dry=`, `tankFed=`,
       **`coverage=`**; for the all-moist fixture expect `moist == registered` and `coverage=1.000`.
4. Expected outcomes on the all-moist fixture: `diag.scan.probe canPlant>0`, `diag.scan.end
   registeredNow>0`, no `plot.scan-empty` warning, `diag.day moist=registered coverage=1.000`, panel
   100 % shortly after the scan completes (before the first day tick).
5. If instead `plot.scan-reachability` appears: reachability is the remaining starvation cause —
   record `reach=`/`accepted=`/`entityId=` and check the entrance/doorstep path (injected entity
   may have no reachable path; this is the round-2 suspect (a)).
6. If `plot.scan-empty` appears with `field=scanTotal`: no cell passed the field filter even though
   the probe should show `canPlant`; compare `diag.scan.probe canPlant` and, if it is also 0, the
   crop-template/validator path (first suspect: `TryGetScanCropTemplate` returning no *Farmhouse*
   `PlantableSpec`) is the next target.
7. If `moist=0` while the fixture is all-moist **and** `registered>0`: the z assumption would be
   wrong in game despite the IL — then record the decoded plot cell z and `GetTerrainHeight` for
   that column in the next diagnostic round (this card's evidence predicts they are equal).

Report back: the four lines above (`diag.scan.probe`, `diag.scan.end`, any new `plot.scan-*` Warn,
`diag.day`) plus whether the panel showed non-zero coverage before the first day tick.

---

## 7. Explicitly not claimed / not done

* Not claimed: "the in-game 0 % is fixed". In-game verification is still pending
  (desktop locked per round-7 notes; this card also forbids launching the game).
* Not done: deploy, `build.ps1`, five-assembly release gate, `tools/validate`, save edits, data/ or
  balance changes, `docs/VERIFICATION.md` / `docs/REQ_TRACEABILITY.md` updates (Lead owns those),
  removal of the temporary diagnostics file (kept unchanged for the next in-game round).
