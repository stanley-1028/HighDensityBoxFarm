# Next automated rounds — prioritized backlog

This file is the handoff between automatic continuation rounds. Read it first, then pick the
highest-priority item that is not blocked. Status keywords: **offline-safe** (can be done and
verified without a game session), **blocked-on-game** (needs one user-attended Timberborn run),
**blocked-on-unlock** (the Windows desktop is locked, so synthetic input cannot reach the game).

## Round-7 status (2026-10-25 morning, Lead) — read before the old lists below

**Baseline is green and current**: `build.ps1 -Install` exit 0, 5 assemblies 0 warning/0 error,
L1 `PASSED 261 FAILED 0`, `tools/validate RESULT: OK` (but "mod assemblies: 7" — see card below),
stage 308 KB / 29 files, deploy SHA-256 identical. Sources unchanged since 2026-10-25 00:20.

**User decisions taken this round (authoritative, do not re-ask):**
1. Nightly automated in-game verification is authorized (mute audio, read-only save copy, kill
   the game afterwards). A failed attempt must be recorded honestly, never faked.
2. Save-size conflict (ch. 12.7 = 1.0–1.4 MB/100 top farms vs ch. 15.3 = ≤256 KB):
   **ch. 12.7 is authoritative**, ch. 15.3's 256 KB row is superseded — see ADR-008.
3. One Postfix patch is approved to make G_irr/G_fat/G_maint/G_power actually slow vanilla crop
   growth (deviation D-2), guarded by a feature switch and only kept if in-game verified — ADR-009.
4. The five third-party mods may be used for the compatibility matrix; they are already
   downloaded as Steam Workshop items (see the card list below) — no network download needed.
5. Art stays as faction-correct vanilla placeholder models + frozen contract (ADR-003 unchanged).
6. Scope priority: P0 plot-registration/irrigation fix + P1 crop loop in-game proof + remaining
   offline bugs + UI gaps; compat matrix and performance as far as time allows.

**Task board cards (created this round, run by the Host; Lead verifies):**
- BUG-01 (`fc603511-…`, max) plot registration / irrigation z semantics — running.
- BUG-02 (`6accb357-…`, max) `tools/validate` assembly scope 7→5 — running.

**P0 root cause already found (round 6, needs in-game confirmation)**: the scan used
`TerrainService.CellIsField` to test plantability; on a fresh map that is always false, so the
managed-plot registry stayed empty (0% coverage, invisible because the panel only showed the
capacity-derived "effective plots"). The deployed build now uses
`PlantingAreaValidator.CanPlant(cell, crop)` and carries temporary always-on diagnostics
(`HdbfBoxFarmController.Diagnostics.cs`, `FarmDiagnostics = true`; remove before release).

**In-game automation status (2026-10-25 11:00–11:35) — SOLVED, no synthetic input needed**: the
first obstacle was the first-load "Mods" dialog, which needs a mouse click. Synthetic input does
not work from this harness (mouse_event/SendInput/keybd_event report success but no window receives
them — not even a WinForms window created by the same process; `OpenInputDesktop` fails with access
denied even though the session is `WTSActive` and `WTSINFOEX.SessionFlags=1`/unlocked; cause
unresolved and recorded in VERIFICATION §16.2). The real fix came from the installed IL:
`Timberborn.ModManagerSceneUI.ModManagerScenePanel.cctor` sets `SkipModManagerKey = "skipModManager"`
and `ShouldSkipModManager() = CommandLineArguments.Has(key)`; with that argument `Awake()` calls
`LoadModsAndStartGame()` directly. Launch line:

```
Timberborn.exe -skipModManager -saveName "<name>" -settlementName HDBFTest -logFile <log>
```

Round 9 (11:14–11:23) therefore ran fully unattended: mod loaded, fixture autoloaded, farm entity
active, world time advanced to day 4.18, and the diagnostics produced
`diag.scan.begin/probe/end` + `diag.day` lines (`docs/in-game/round9-Player.log`). Results:
`canPlant=102/121` (the round-6 plantability fix is real in game) but
`selected=1 granted=1 registeredNow=1` — `TryAcceptPlot`'s `out plotId` was never assigned, so all
102 accepted cells collapsed to plotId 0; plus `ResolvePathLength` throws "no cached flow field"
and aborts the whole farm plan. Card BUG-04 (`c08612c8-…`, max) is fixing both. After it lands:
gate + deploy, then `Invoke-GameRound.ps1 -Action Start -Tag round10`, `SpeedUp -Seconds 180`,
`Stop`, and expect `registeredNow=60`, `diag.day coverage=1.000`, no farm-loop fault.
Procedure: `-Action Start` (Fixture→Prepare→Launch) → screenshot/`-Action ConfirmMods` not needed
anymore → `-Action SpeedUp -Seconds 180` → `-Action Stop`; read
`%TEMP%\hdbf-round\Player.log` and `docs/in-game/<tag>-Player.log`.

**Third-party mods for the compatibility matrix (already on disk, Steam Workshop):**
`C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\`:
Conveyor Belts v2 `3815376360\version-1.1` (ConveyorBelt v11.4.0, min 1.1.1);
More Mines `3723216060\version-1.1.0.2` (TonWolfe.MoreMines v1.1.20, min 1.1.0.2);
Hooman Stairs `3682012904\Version-1.1` (Calloatti.HoomanStairs v2.0.2, min 1.1.2.4);
Power Lines `3766893705\version-1.1` (PowerLines v11.1.1, min 1.1.1);
Timber Pipes `3801726769\version-1.1` (TimberPipes v11.4.0, min 1.1.1).
Install = copy the version folder into `Documents\Timberborn\Mods\<Folder>` (same layout the game
loads for HDBF); remove after the matrix run.

## Round 10–11 results (2026-10-25 11:39–11:53) — first unattended in-game world runs

Both rounds ran with zero human input (no synthetic mouse/keyboard needed): fixture → mute →
launch with `-skipModManager` → wait → focus → fast-forward → stop. Logs:
`docs/in-game/round10-Player.log`, `round11-Player.log` (+ `-summary.txt`).

* **Fixture upgraded**: the 110000 map has a real Path chain at y=139 x=130..136 and the district
  centre at (131,138); the farm now anchors at (129,140) so its entrance lands on the real road cell
  (130,139) — no injected fake Path (`tools/dev/fixtures/craft-farm-fixture.py`, `path=None`).
* **Verified in game**: after the BUG-04 build the first scan registers **60/60** plots
  (`registeredNow=60`), the daily rescans keep 60 for one day, and `plot.scan-empty` (new BUG-01
  visibility) works when the registry breaks. `coverage=0.667` on day 2 is legit moisture decay of
  the all-16 fixture, not a z bug (`dryCells` show `z==h`, `onGround=1`).
* **P1 planting actually happened**: next-day rescan shows plantable cells 103→62, `block` 4→34 and
  farm-plan candidates 60→41→7, i.e. crops were planted on managed plots. `Accessible.Find*Path`
  still throws "no cached flow field" even for the entrance-to-itself query (`pathSelf=throw`), but
  the BUG-04 fallback (`NavigationService.FindPathUnlimitedRange`) takes over and dispatch works.
* **New P0 (BUG-05 card running)**: the daily rescan treats already-planted managed cells as
  non-plantable and drops them, so the registry collapses 60→7→0 by day 5 and the farm can never
  harvest. Required rule: a re-scan must keep the farm's registered cells while they are in range
  (crop occupancy is not a reason to deregister); only lost terrain / out of range / unreachable /
  player-excluded / claimed-by-other farms may drop them.
* Harness lesson: a 300 s tool-call timeout kills the shell **and the game**; game/audio/log state
  now lives in `%USERPROFILE%\.hdbf\round` (stable path) instead of `%TEMP%`.

**Round 12 plan**: BUG-05 build + `build.ps1 -Install` + a longer run (8–10 game days). Expected:
`registeredNow=60` on every rescan, `kept=60 added=0 dropped=0`, `coverageAfterScan=1.000` right
after load, `harvested plot ...` lines from the farm loop, product in `SimpleOutputInventory`.
After that: save round-trip, maintenance materials, power link, performance, compat matrix.

## Round 12 results (2026-10-25 12:23–12:41) — P1 crop loop verified in game

`build.ps1 -Install` after BUG-05 + BUILD-01: 5 assemblies 0/0, L1 **342/342**, validate 5/5 OK,
338 KB deployed. Fully unattended run (no synthetic input), 16 game days. Log:
`docs/in-game/round12-Player.log` (+ summary, screenshot with the ripe wheat field).

* **BUG-05 verified**: first scan `registeredNow=60`, `coverageAfterScan=1.000`; every later rescan
  `selected=60 granted=60 registeredNow=60 kept=60 added=0 dropped=0` for 16 days, no
  `plot.scan-empty`. Coverage `0.667` from day 2 is moisture decay of the all-16 fixture.
* **P1 loop verified**: day 1–2 plant dispatch (`farm.plan candidates=60→39`), days 3–10 growing
  (`candidates=0`), day 11+ harvest (`harvested plot … crop Wheat rotation=True`, repeated with
  `suppressed=38/39`), products in the district inventory/hauling; day 13–16 constantly 40 harvest
  candidates. `Accessible.Find*Path` still throws for the injected building's entrance, but the
  BUG-04 fallback measures real paths and dispatch works.
* Remaining in-game gaps: real build-menu placement, maintenance material withdrawal (needs
  Intermediate+), top-tier power link, save round-trip, panel runtime (BUILD-01), performance L5,
  five-mod compatibility matrix, D-2 growth-multiplier patch.

### Open cards / next rounds

* **BUG-03 (running)**: one approved Postfix (ADR-009) so G_irr×G_fat×G_maint×G_power really slows
  vanilla growth on managed plots; needs gate + an in-game delta run (same fixture, Dense/Fallow and
  dry-plot cases) before it may stay.
* **BUILD-02** managed-plot highlight/box-select (REQ-UI-05) — todo.
* **BUILD-04** world-space P0/P1 warning icons + cross-building aggregation — todo.
* **BUILD-03** adapter anchors — delivered offline; still needs the in-game five-mod matrix
  (`tools/dev/CompatMatrix.ps1` is the prepared install/remove/log-check tool).
* Release blockers left: remove/guard the temporary diagnostics (`FarmDiagnostics`, REQ-BAL-27),
  in-game save round-trip (AC-06), maintenance transaction, top-tier power, performance L5,
  five-mod matrix, real placement/construction run.

## Round 13 results (2026-10-25 13:08–13:22) — Postfix installs in game; timing still unmeasured

Deploy = BUG-03 build: 5 assemblies 0/0, L1 **358/358**, validate 5/5 OK, 356 KB. Log:
`docs/in-game/round13-Player.log`. The single approved Postfix actually installed in the real
1.1.2.8 session: `compat.harmony-patch-installed … target=Timberborn.Growing.Growable.ResumeGrowing
harmonyInstance=hdbf.highdensityboxfarm`; farm loop kept running (plant dispatch day 1–2, registry
60 the whole time, no `[HDBF/Error]`). Not measured yet: the growth-time delta (needs a multiplier
< 1 condition), vanilla-crop regression, switch-off path, save/reload non-stacking.

**Conservative ruling applied**: `data/HDBF_Config.json` ships
`features.growthMultiplierPatch = false` (D-2 remains the released default) until the delta run in
VERIFICATION §19.3 passes; the patch code/switch/failure-unload/L1 tests stay. `tools/validate`
now emits one **intentional** warning about this departure from chapter 9.3 until the default flips
back to true.

**Next round priorities**
1. **Delta run for ADR-009** (VERIFICATION §19.3): top-tier off-grid fixture (`G_power=0.6`) vs
   basic baseline, plus a non-managed vanilla crop as the regression control; then flip the config
   default to true and clear the validator warning (or remove the patch if it cannot be shown).
2. **BUILD-02** managed-plot highlight/box-select (REQ-UI-05) and **BUILD-04** world-space warning
   icons — both still `todo` on the board (high reasoning cards already written).
3. **REL-01** (created, run last): collapse the temporary `FarmDiagnostics`, add release gates
   (no debug entries, patch inventory, compat-report fields) and `docs/OPS.md`.
4. In-game panel runtime check for BUILD-01 (registered/effective/tillable trio) and save
   round-trip (AC-06), maintenance materials (Intermediate+), top-tier power, 100-building perf,
   five-mod matrix (`tools/dev/CompatMatrix.ps1`).
5. Remember: the game advances ≈1 game day per 60 real seconds at 1× (synthetic input cannot press
   `>>>`), so a 12-day campaign = ~12 real minutes per round; plan fixture campaigns accordingly.

## Round 14 results (2026-10-25 13:23–13:31) — ADR-009 patch proven to rescale managed crops

Deploy = BUG-03 build + one temporary diagnostic line per rescaled crop
(`compat.harmony-patch-applied.N`; REL-01 removes it). Fixture switched to the top tier
(`craft-farm-fixture.py --tier BoxFarm.Top.Folktails`: 27×27 range, capacity 600, 2 workers,
off-grid `G_power=0.6`). Log: `docs/in-game/round14-Player.log`.

* Patch installed and **applied to real managed crops**: 12 lines, e.g.
  `cell=(131,142,3) growthRate=0.198 vanillaDays=10.00 scaledDays=50.63`.
* The rate decomposes exactly as the requirement formula: `G_irr 0.663 (C=0.598, C_min=0.80)
  × G_power 0.60 × G_maint 0.50 = 0.199`; at `growthRate ≥ 0.999` the bridge no-ops, so a healthy
  basic farm is unchanged (round 12/13 in-game behaviour).
* Shipped config is back to `features.growthMultiplierPatch = true` (validator `WARNINGS: none`);
  the switch remains as the rollback path.
* **Still open**: non-managed vanilla crop regression, a full 50-day harvest cycle, and
  save/reload non-stacking (VERIFICATION §20.2). Also noted: a record-less injected top farm starts
  with `maintenance.dueDay = 0` → `G_maint = 0.5` immediately; real saves carry the record, but the
  12.1 "missing field" default deserves a second look (VERIFICATION §20.3).

## Round-28..29 + art pass (2026-10-25 15:52–16:20)

* **BUILD-05 verified in game**: managed crops are hidden (`gameplay.crop-visibility hidden=76
  registered=300 hideFailures=0`), the ground shows only tilled soil, and harvest still works
  (`harvested plot … crop Wheat`, hidden count drops after harvest). Screenshot:
  `docs/in-game/round29-run.png`.
* **BUILD-04 world icon observed**: an Advanced farm (no vanilla mechanical component) with P1
  irrigation shortage shows a head icon although `ui.worldIcons` is on; the Top-tier icon seen in
  round 21 was the *vanilla* no-power icon (it stayed with `ui.worldIcons=false`). Tooltip/distance/
  blink/aggregation still need a user-attended session (no synthetic input).
* **BUG-07 opened+delivered**: scan-reachability exceptions were logged as `compat.failed` with full
  stacks; now one throttled `gameplay.scan-reachability` warning per scan.
* **Art pipeline decision (user 2026-10-25)**: real 3D art, crops inside the building. The official
  **Timbermesh Blender plugin** (v1.2.0 MIT) + portable Blender 4.2.1 exports `.timbermesh`
  **without Unity**; custom PBR materials still need Unity AssetBundles, but the Unity 6000.5.11f1
  installer is unreachable (China CDN mirror lacks it) and the local Unity licence is China-only
  (2022.3 max). First pass therefore uses **vanilla material names**; the 2048 PBR atlas is
  generated and kept for a later Unity-enabled upgrade.
* ART-01 delivered the deterministic Blender generator (`art/`): 8 tier/faction models + LOD0-2 +
  construction greybox + 3 atlas PNGs + validation PASS (bounds 3×1×2, tri budgets, 6 troughs,
  3 ports, 12/12 tier pairs distinguishable at 50%). ART-02 exported 16 `.timbermesh` (8 finished +
  8 construction, 16/16 PASS, 128/128 checks) via the official plugin (+ 4 icons); ART-03 wired the
  8 blueprints/validator/icons and raised L1 to 510. **Round 30–33 verified all four tiers in game**
  (`docs/in-game/round30-Basic-run.png`, `round31-Int-loaded.png`, `round32-Adv-run.png`,
  `round33-Top-run.png`): custom 3×2×1 models with six roof troughs render, top tier still shows the
  vanilla no-power icon/notification. Deployment is 1,645 KB at the same gate (510 L1, 55 checks).

### Next after ART-03

1. `build.ps1 -Install` gate (5 assemblies, L1, validate 5/5, release gates 55 checks, deploy SHA).
2. In-game: one fixture per tier (Basic/Intermediate/Advanced/Top) → screenshot the real model,
   confirm 3×2×1 placement, entrance, no caveats from `CollidersSpec`, construction stage model.
3. Remaining user-attended checks: panel trio/highlight/aggregation, save round-trip (AC-06),
   maintenance materials (Intermediate+), top-tier power, 100-building perf, TC-08 upgraded path.
4. Optional upgrade: Unity 6000.5.11f1 + official TimbermeshTools per user (custom PBR atlas /
   material AssetBundle) once an installer + global Unity account are available.

## Older backlog

## P0 — blocked-on-game (do not fake; wait for or request a run)

1. Run `docs/IN_GAME_VERIFICATION.md` with the user. Capture `Player.log`, screenshots, any
   `[HDBF/Error|Warn]` lines into `docs/in-game/`.
2. Fix whatever the run surfaces (placement/slot crash, task-handshake failure, panel binding
   error, material transaction, power port). Apply the max-effort debugging protocol:
   reproduce → IL evidence → minimal fix → regression test → re-run.
3. Update `docs/VERIFICATION.md` (new numbered round) and `docs/REQ_TRACEABILITY.md` statuses.

## P1 — offline-safe, valuable now

4. **DONE (round 5) — do not redo**: v0 migration ordering was fixed and regression-tested
   (`SaveRoundTripFaultTests.cs:132-183`; `TrySanitize` keeps the v0 stamp so `Migrate` fills
   `DueDay`). The old text below is kept only as history.
5. **CARD RUNNING (round 7)**: validator assembly scope — `tools/validate` still prints
   "mod assemblies: 7" because every directory under `--assemblies` is treated as `<dir>.dll`, so
   `ApiIndex.dll` / `HDBF.Tests.dll` are counted. Card BUG-02 tells it to accept only the five
   `HDBF.*` game assemblies and to FAIL when one is missing.
6. **DECIDED (round 7)**: save-size contradiction settled in favour of ch. 12.7 (1.0–1.4 MB/100
   top farms, measured upper bound 1.21 MB). Do not compress the schema for the 256 KB row; ADR-008
   records the ruling.
7. **Round-isolation audits**: re-run `build.ps1` and `tools/smoke` dry-run after any change;
   keep deploy == stage SHA-256 equality as the release gate.

## P2 — blocked-on-game or needs toolchain (document, don't fake)

8. Managed-plot highlight / box-select (REQ-UI-05 rendering layer): needs a world-space overlay;
   can be prototyped offline only as contract + data, not verified.
9. World-space P0/P1 warning icons + cross-building aggregation (chapter 14.2/14.6).
10. Art chapter 13: needs Unity/`.timbermesh` toolchain. Placeholder contract is frozen.
11. Third-party adapters: only verifiable with the five mods installed. Do not invent anchors.
12. Optional P2 decision point: one approved Postfix to make G_irr/G_fat/G_maint/G_power slow
    vanilla growth (currently deviation D-2). Only with user approval + in-game verification.

## Guardrails for every round

* Work in `C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm` (source workspace); deploy
  to `Documents\Timberborn\Mods\HDBF` only via `build.ps1 -Install`.
* Never copy source/reference data into a mod folder: the game recursively loads DLLs and
  blueprint JSON. `build.ps1` gates this.
* Build with `-m:1`; full gate before any "done" claim: 5 assemblies 0/0 → L1 tests green →
  `tools/validate` `RESULT: OK` → stage + deploy SHA equality.
* Update `docs/VERIFICATION.md` honestly: "compiles" is not "verified in game".
﻿
## Round-5 findings — update to the P0 plan (automation recipe that works)

The automated in-game path now works (details: VERIFICATION §14, artifacts in `docs/in-game/`):

1. Set `SteamAppId=1062090` / `SteamGameId=1062090` in the child environment (and keep
   `Timberborn\steam_appid.txt` = `1062090`). Launch `Timberborn.exe` directly.
2. The first load after a mod-folder change shows a **Mods dialog**: click its OK button
   (screen 2560×1440: ~ (1281,1098)) before the game continues.
3. For autoload, copy a save folder to `Saves\HDBFTest` and pass
   `-saveName "<file name without .timber>" -settlementName HDBFTest`; delete the copy afterwards.
4. Injected entities need the vanilla persisted component set
   (`PausableBuilding`, `Workplace`, `BlockObject`, `WorkplaceWorkerType`, `Emptiable`) and a
   `BlockObject.Coordinates` whose Z equals the terrain surface height (`TerrainMap.Voxels`
   column solid count). (153,110,4) on the 110000 map is valid; (20,20,4) is rejected.
5. Focus handling: DSH steals foreground between tool calls. Minimize its window and perform
   launch/click/screenshot inside a single pwsh call; keep audio muted via CoreAudio
   (`IAudioEndpointVolume.SetMute`) and restore it at the end.

Next concrete P0 step: craft a save with (a) a road path adjacent to the farm entrance and
(b) a water-adjacent flat spot, or place the farm with the build menu via clicks, then run
several game days at fast speed and capture `gameplay.farm` logs + a saved game to prove the
P1 worker→plant→harvest→inventory chain. Then walk the rest of `docs/IN_GAME_VERIFICATION.md`.

## Round-2 (in-game) findings — NEXT P0 debug plan

Verified in game: path-connected farm loads, HDBF panel renders correct worker/plots/maintenance/port
values, pipe-port toggle works (status feedback line), mode switch to Fallow logs
`gameplay.mode: mode changed to Fallow`, cooldown rejection shows "模式切换冷却中", game time advances.

**Open defect to fix first:** irrigation coverage is 0% even when every
`SoilMoistureSimulator.MoistureLevels` value is set to 16 in the test save. Suggested next steps:
1. Add temporary `HDBF/Info` diagnostics (gated by an env var or a `dev` config flag, removed before
   final packaging): after scan completion log registered plot count; in the daily tick log
   `registered/irrigated/coverage`, the first 3 decoded plot cells + `SoilIsMoist(cell)` results,
   entrance cell, day number, `_tankFedPlots`, and whether the day trigger fired.
2. Rebuild + deploy, rerun the same all-moist fixture save, and read the log to locate the layer:
   scan (registry empty?) vs sampling (`SoilIsMoist` false?) vs tick (trigger not firing?).
3. Likely suspects: (a) reachability filter rejects every candidate for the injected entity
   (`BuildingAccessible`/`Accessible` resolution), (b) `HdbfPlotCellCodec` encode/decode vs the
   `SoilIsMoist(Vector3Int)` expected height, (c) day trigger not re-armed after `PostLoadEntity`
   activation for a loaded building.
4. Also add the missing panel numbers from chapter 11.3: 已注册格 / 有效格 / 可开垦格 (currently the
   panel hides an empty registry behind "有效托管格 60/60").
5. After irrigation works, run 3+ game days at `>>>` and confirm: crops appear on managed plots,
   harvest enters the building's 50-capacity SimpleOutputInventory, haulers drain it, no
   `[HDBF/Error]`, then save/reload and compare persisted `PlotIds/CropIds/Fatigue/TankWater`.

Test fixture recipe (works, repeatable): `SteamAppId=1062090` env + `steam_appid.txt`, click the Mods
dialog OK, `-saveName`/`-settlementName` with a save **copy** under `Saves\HDBFTest`; inject entity with
`PausableBuilding/Workplace/BlockObject/WorkplaceWorkerType/Emptiable` and Z == terrain surface height;
path cells connect the entrance to the existing road; all-moist fixture by rewriting
`SoilMoistureSimulator.MoistureLevels` to 16s. Mute audio (`IAudioEndpointVolume.SetMute(true)`) and
restore it afterwards; a single pwsh call must keep the game focused (DSH steals foreground between calls).
