# HDBF architecture and decisions

This document records the shipped architecture and the deliberate deviations from the
requirement blueprint. Each deviation states what changed, why, and how the original
requirement's *intent* is still satisfied.

## 1. Layering

```
            ┌──────────────────────────────┐
            │  data/  (manifest, blueprints,│
            │  localizations, config, icons)│
            └───────────┬──────────────────┘
                        │ referenced by string id only
   ┌────────────────────┼─────────────────────┐
   │                    │                     │
┌──▼───────┐     ┌──────▼──────┐      ┌───────▼──────┐
│HDBF.UI   │     │HDBF.Gameplay│      │HDBF.Integr.  │
│(snapshot │     │(spec,       │      │(adapters,    │
│ + warns) │     │ components) │      │ detection)   │
└──┬───────┘     └──────┬──────┘      └───────┬──────┘
   │                    │                     │
   │              ┌─────▼──────┐              │
   │              │HDBF.Persist│              │
   │              └─────┬──────┘              │
   │                    │                     │
   └────────────────┐   │   ┌─────────────────┘
                 ┌──▼───▼───▼──┐
                 │ HDBF.Core   │   ← no game, no Unity, no third-party types
                 │ formulas,   │
                 │ config,     │
                 │ save codec, │
                 │ compat core │
                 └─────────────┘
```

Rules enforced by review (matching REQ-ARCH-01/02/05):

* `HDBF.Core` references only the .NET standard library. It is the only place balance math
  exists; `BalanceFormulas.PlotYield` is the sole multiplication point (REQ-BAL-28,
  REQ-IMPL-32).
* `HDBF.Integration` is the only assembly whose *strings* mention third-party mod types; even
  there, no assembly reference exists — everything goes through `ExternalProxy` reflection
  (REQ-COMPAT-02/05).
* `HDBF.UI` may not reference `HDBF.Gameplay`; it consumes `HDBF.Core.Abstractions.BoxFarmSnapshot`.
* No `static` mutable state except the two caches that have `Reset()`: `ThrottledLog` counters
  and `ExternalProxy` reflection cache (REQ-ARCH-06).

## 2. Runtime composition

1. Game loads `HDBF.*.dll` from the mod directory (`ModCodeStarter`), then instantiates every
   `IModStarter` → `HdbfModStarter.StartMod` logs version/tier summary and never throws.
2. Bindito discovers `Configurator` subclasses across loaded assemblies. `HdbfGameConfigurator`
   (`[Context("Game")]`) binds:
   * `HdbfSystemRegistrar` (singleton, loads `HDBF_Config.json`, resolves tier profiles, owns the
     throttled log);
   * `HdbfFarmQueryService` (singleton, produces snapshots for UI);
   * `HdbfFarmCommandService` (singleton, `IBoxFarmCommand`: mode/maintenance/ports/plot edits, and
     the maintenance material transaction against the closest district inventory);
   * `HdbfBoxFarmController` (transient component) and
     `MultiBind<TemplateModule>()` with `AddDecorator<HdbfBoxFarmSpec, HdbfBoxFarmController>()`,
     so an entity gets the controller exactly when its blueprint carries our spec;
   * `HdbfFarmWorkplaceBehavior` plus the vanilla hauling/emptying decorators
     (`AutoEmptiable`, `Emptiable`, `HaulCandidate`, `SimpleOutputInventoryHaulBehaviorProvider`,
     `EmptyOutputWorkplaceBehavior`) on the same spec, so the finished farm has workers that can
     plant/harvest and an output inventory that vanilla haulers empty.
   * `HdbfIntegrationConfigurator` binds `ICompatRegistry` (single adapter registry, five adapters,
     isolated failures).
3. Blueprint JSON carries `HdbfBoxFarmSpec`; the four tiers differ only by that spec's values
   (REQ-IMPL-01). `SpecTypeCache` scans all loaded assemblies, so the spec type is discovered
   without registration.

## 3. Decisions

### ADR-001 — save through native entity persistence instead of a standalone `HDBF` node

**Requirement:** chapter 12 asks for an independent `HDBF` root node with `HDBF.Version`,
`HDBF.Buildings`, `HDBF.Compat`, checksum, backup and a migration chain.

**Decision:** each farm saves its `HdbfBuildingRecord` through `IEntitySaver`/`IEntityLoader`
(native per-entity persistence). Versioning, damage repair and idempotent migration live in
`HDBF.Core.Save.HdbfSaveCodec` and are unit-tested; no second serialization system is added.

**Why:**
* The game's save is already transactional and versioned. A parallel node would need its own
  write ordering, checksum and migration inside a save system we do not control — exactly the
  duplicate-risk surface chapter 12 tries to reduce.
* Entity persistence travels with the entity: deleting a building removes its record, which
  removes a whole class of orphaned-record bugs.
* The `HDBF`-node design in the document predates knowledge that Timberborn 1.1.2.8 exposes
  per-entity persistence with typed `PropertyKey`/`ListKey` and per-key defaulting.

**How the intent is kept:** schema version is stamped per record; missing/corrupt fields are
repaired with conservative defaults (never free production); a damaged record is isolated to
one building; migration is pure and idempotent; all of it is covered by L1 tests
(REQ-SAVE-05..09 map to `HdbfSaveCodec` tests + `HdbfEntitySaveBridge` tolerant load).

**Residual risk:** cross-building integrity (e.g. a global checksum) is not implemented. The
game's own save integrity covers file-level corruption; per-record validation covers data-level
corruption.

### ADR-002 — assembly graph is `Core ← Persistence ← Gameplay`, not `Gameplay → Core`

**Requirement:** chapter 9.1 draws `HDBF.Gameplay → HDBF.Core` only and forbids
`Gameplay → Persistence` implementation types.

**Decision:** Gameplay references Persistence (which brings Core transitively); Integration and
UI reference Core directly. Every project declares exactly one `ProjectReference`.

**Why:**
* A Timberborn component must implement `IPersistentEntity.Save/Load` itself; the mapping code
  that turns `IEntitySaver` into our record has to be callable from Gameplay. Putting that
  mapping in Core is impossible (Core must stay game-free), and putting it in Gameplay would
  make HDBF.Persistence an empty project.
* The build environment rejects any project with two project references (silent NuGet restore
  failure — docs/API_FACTS.md §6), so a diamond graph is not buildable here at all.

**How the intent is kept:** Gameplay still uses only Core *contracts* for state
(`HdbfBuildingRecord`, `HdbfSaveCodec`, `TierProfile`, `BalanceFormulas`); Persistence exposes a
single static bridge with no gameplay types. There is no cycle, and UI/Integration cannot reach
persistence or gameplay internals.

### ADR-003 — placeholder art with a frozen port contract

**Requirement:** chapter 13 specifies four models, LOD0-2, PBR atlas, ports.

**Decision:** v0.1.0 ships no `.timbermesh` assets. Blueprints reference existing vanilla model
paths per tier so the building is visible and testable in game, and `data/ART_PLACEHOLDER.md`
freezes the contract the art must satisfy (footprint, pivot, entrance, port coordinates, naming).

**Why:** `.timbermesh`/prefab authoring needs the game's Unity toolchain, which this delivery
environment does not have. Shipping fake binary assets would be worse than an explicit,
documented placeholder.

**How the intent is kept:** the blueprint structure (`Children`, `TimbermeshSpec.Model`,
`CollidersSpec`) is already the final one; swapping art is a path replacement plus collider
update, no code or schema change. Port markers (chapter 13.6) are documented so modeling can
start immediately.

**Update (2026-10-25, user-requested art pass):** the placeholder phase is over. Investigation
found the official **Timbermesh Blender plugin** (v1.2.0, MIT) and a portable Blender 4.2.1, so
real `.timbermesh` models can be authored and exported **without Unity**. `art/` now holds a
deterministic Blender generator for the four tiers × two factions (3×2×1 envelope, 6 roof crop
troughs, 3 port markers, LOD0–LOD2 + 2048 PBR atlas) with an offline validation report. Because
custom materials must be shipped as Unity AssetBundles and the Unity 6000.5.11f1 installer is not
reachable from this network (China CDN mirror lacks the build; local Unity licence is China-only),
the first in-game art pass uses **vanilla material names** (wood/metal/glass/fabric) on our own
geometry; the custom PBR atlas stays prepared for a later Unity-enabled pass. Ground crops on
managed plots are now hidden by code (VERIFICATION §23), matching the "crops live inside the box"
design.

**Outcome (2026-10-25, VERIFICATION §24):** the eight blueprints now point at our own 16
`.timbermesh` files; all four tiers were observed in game (round 30–33) with 3×2×1 footprints and
six roof troughs, and the release gate (`build.ps1`) validates the custom model assets (5/5
assemblies, 510 L1 tests, 55 release checks, 1,645 KB deployed). Remaining art debt: custom PBR
material AssetBundle and construction-stage appearance.

### ADR-004 — zero Harmony patches in v0.1.0

**Requirement:** REQ-IMPL-15 (0 patches preferred, ≤2 if unavoidable).

**Decision:** no patch assembly is referenced or shipped. All behaviour is attached through
custom spec + component binding, which the game supports first-class.

**Why:** every gameplay hook we need (per-building tick, persistence, workplace worker count,
soil moisture sampling, planting service) is reachable through public components/services.

**Resolved risk (was the delivery's single unverified assumption):** the P1 crop loop does **not**
need a patch. Vanilla planting/harvest steps live on the *worker* (`Planter`/`PlantBehavior`,
`YielderRemover`/`YieldRemoverBehavior`) and are dispatched from the building's
`WorkplaceBehavior`; vanilla farmhouse tasks are range-driven, not ownership-driven, so a custom
behavior can point them at our managed plots without touching vanilla code. See
docs/API_FACTS.md §10 and docs/workstreams/farm-loop.md. One patch stays available as a
documented fallback if in-game verification contradicts this.

### ADR-006 — the source workspace is not the installed mod folder

**Requirement:** the blueprint assumes one project tree that is also the mod at build time.

**Decision:** the source tree lives in `Documents\Timberborn\Mods\HighDensityBoxFarm` (the DSH
workspace) but deliberately has no `manifest.json`; the game only loads
`Documents\Timberborn\Mods\HDBF`, produced by `build.ps1 -Install`. Build artefacts go to
`%USERPROFILE%\.hdbf\work`, vanilla references to `%USERPROFILE%\.hdbf\ref`.

**Why:** Timberborn recursively converts every `*.blueprint.json` / `.timbermesh` below a mod
folder and loads every `*.dll` below it. Sources, a 1067-file vanilla reference tree and bin/obj
would be re-registered as game data and duplicate assemblies — a guaranteed load failure, and
exactly the class of accident the game's own example mod avoids by having no source tree.
`build.ps1` fails the build if a manifest or a stray DLL appears in the workspace.

**How the intent is kept:** one command (`build.ps1 -Install`) still produces a normal
`Mods\<Folder>\manifest.json + dlls + data` package; the data folder under `data/` remains the
single source of truth and ships verbatim.

### ADR-007 — tech-tree prerequisites are documented as a deviation

**Requirement:** chapter 7 places the four tiers behind research nodes with prerequisites
(vanilla farming start → planks → metal → power).

**Decision:** only `BuildingSpec.ScienceCost` (200/600/1000/3000, both factions) gates the four
buildings now; there is no C# prerequisite gate in v0.1.0. The blueprint's research *node*
requirements cannot be expressed: 1.1.2.8 has no research-node blueprints, no prerequisite spec
and no tech tree — `BuildingUnlockingService` IL only compares `ScienceCost` with the science
points and the saved unlocked-template set (evidence: docs/workstreams/tech-data.md §A).

**Why:** faking research-node JSON would be an unknown-spec load error; a custom prerequisite
component would add unverifiable runtime state with no in-game test available. The native
mechanism already enforces "not buildable at start" and a one-shot cost, which is the load-bearing
part of the chapter.

**Residual risk:** the flavour prerequisites (must have farmed, must have planks/metal/power) are
not enforced. This is recorded in `docs/REQ_TRACEABILITY.md` instead of being silently dropped; a
future version can add a `BuildingToolLocker`-style gate once a running game is available for
verification.

### ADR-005 — config is advisory, code is the floor

**Requirement:** REQ-IMPL-03/REQ-BAL-18/REQ-TECH-05 (JSON-overridable, clamped, never zero/negative).

**Decision:** `HdbfConfigCodec.ResolveTiers` layers JSON over `TierCatalog`, clamps every value to
chapter 18 ranges, and falls back to the compiled default per field on any error. A missing or
corrupt config therefore degrades to the shipped balance, never to something cheaper.

### ADR-008 — save-size budget: chapter 12.7 wins over chapter 15.3's 256 KB row

**Requirement:** chapter 12.7 budgets 1.0–1.4 MB of JSON for 100 top-tier farms (10–14 KB each);
chapter 15.3's performance table says "100 buildings, save size ≤256 KB". The two cannot both hold
for 100 top-tier farms: the offline upper-bound measurement is **1.21 MB** (100 × Top/600,
`[budget]` lines in VERIFICATION §13.2), which sits inside the 12.7 band and 5× above the 15.3 row.

**Decision (user ruling 2026-10-25):** chapter 12.7 is authoritative. The 15.3 row is treated as
applying to the typical 20–40 building settlement (the same table's volume estimate in 12.7 gives
20–60 KB for 20–40 basic farms, i.e. it is the "typical settlement" budget), not to the 100 × 600
stress case. No schema compression, no rename of fields, no change to 12.2's field contract.
Acceptance: 100 × Top/600 ≤ 1.4 MB, and serialization stays inside the measured ~28 ms proxy.

**Why:** compressing keys or binarising the record would break the frozen 12.2 field contract that
the persistence bridge and the traceability matrix depend on, to satisfy a row that contradicts the
chapter's own per-building estimate. The honest fix is to scope the row, not weaken the schema.

### ADR-009 — one approved Postfix to make the four constraint layers bite

**Requirement:** REQ-CORE-03 / REQ-BAL-02 / REQ-BAL-28 require every managed plot's output to be
the product of `G_irr × G_fat × G_maint × G_power × G_mode`; deviation D-2 currently applies the
layers as dispatch gates plus the growth channel only (irrigation via real soil moisture, dense
mode as a growth multiplier), so fatigue/maintenance/power cannot slow vanilla growth.

**Decision (user approved 2026-10-25):** at most **one** Postfix patch is allowed for v0.1 to slow
vanilla growth on HDBF-managed plots by the four multipliers. Constraints: single Harmony instance
`hdbf.highdensityboxfarm`, Postfix only (no Transpiler), whole-patch `try/catch` with fallback to
vanilla behaviour, feature switch `features.growthMultiplierPatch` (default on, off restores D-2),
auto-unload after 3 failures (REQ-COMPAT-18), and **kept only if an in-game run shows the slower
growth and no vanilla-crop regression**; otherwise it is removed and D-2 stands. The patch may not
treat vanilla farmland: only cells registered by a `HdbfBoxFarmController`.

**Why:** this is the only way the requirement's "no free lunch" numbers become observable; the
zero-patch preference in REQ-IMPL-15 is a preference, not a prohibition (≤2 Postfix allowed).

**Implementation status (2026-10-25):** the single Postfix is implemented, was observed installing
in a real 1.1.2.8 session, and round 14 measured it actually rescaling managed crops: a top-tier
off-grid crop got `growthRate=0.198` (= `G_irr 0.663 × G_power 0.60 × G_maint 0.50`, maintenance
being wrongly overdue on a record-less entity) and the vanilla 10-day timer became 50.63 days; after
BUG-06 fixed the missing-maintenance default, the same scenario measures `growthRate=0.397`
(= 0.663 × 0.60) with a 25.21-day timer (VERIFICATION §20–§21). The shipped config therefore defaults
`features.growthMultiplierPatch = true` (validator clean), with the switch kept for rollback; the
remaining evidence gates (vanilla non-managed crop regression, full-cycle harvest at 50 days,
save/reload non-stacking) are tracked in VERIFICATION §20.2 and must be closed before release.

**Implementation (BUG-03, 2026-10-25):** the patch point proved by IL is
`Timberborn.Growing.Growable.ResumeGrowing` (private, parameterless): `Growable.Awake` creates an
`ITimeTrigger` with `GrowthTimeInDays`, `InitializeEntity` resumes it, and `TimeTrigger.Resume`
registers the absolute completion timestamp with `TimeTriggerService`; completion is wall-clock
scheduled, so a Postfix there can rewrite the duration before any growth accumulated. The Postfix
stretches the trigger's `_fullDelayInDays`/`_delayLeftInDays` by `1 / GrowthRateMultiplier(product)`
(≤20x, finite because REQ-IMPL-20 keeps crops growing during shutdown), only for cells owned by a
live `HdbfBoxFarmController` (cell→plot→farm lookup through `HDBF.Core.Growth.IGrowthMultiplierSource`,
implemented in Gameplay). Harmony is reached through reflection (`HDBF.Integration.HarmonyBootstrap`),
so no HarmonyLib type or bundled DLL is shipped; `features.growthMultiplierPatch=false` installs
nothing. Details, IL snippets and the pending in-game verification steps:
`docs/workstreams/bug-03-growth-multiplier-patch.md`.


## 4. Performance shape (chapter 10.5/11.8)

* No frame loop: one `ITimeTrigger` per building fires the daily update; irrigation coverage is
  sampled on a timer (config `perf.irrigationSampleInterval`), not per frame.
* Plot scanning is capped by `perf.scanBudgetPerFrame` cells per slice.
* Snapshots are rebuilt at most at `perf.uiRefreshHz`, and only for farms the UI asks about.
* 600 plots * 100 buildings = 60k records ≈ single-digit MB; each plot keeps an int id, a crop
  string and a float fatigue (the same shape as the save arrays).

## 5. Failure behaviour

| Failure | Behaviour | Evidence |
|---|---|---|
| Config missing/corrupt | compiled defaults, one throttled warning, one `.bak` attempt | `HdbfConfigCodec` tests |
| Save record corrupt | single building reset to safe defaults, rest untouched | `HdbfSaveCodec.TrySanitize` tests |
| Adapter missing | port unavailable, no red error, one `HDBF/Compat` log | adapter tests + `CompatStateMachine` tests |
| Adapter throws | failure counted, disabled after 3, other adapters unaffected | `CompatStateMachine` tests |
| Game API changed | integration layer returns null instead of throwing | `ExternalProxy` pattern |
| Building logic throws | caught in component, logged with entity id, building degraded | controller try/catch boundaries |
