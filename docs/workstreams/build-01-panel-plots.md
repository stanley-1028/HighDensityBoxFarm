# BUILD-01 — panel plot numbers: 已注册格 / 有效格 / 可开垦格 (chapter 11.3, REQ-UI-02)

Status: **offline implementation complete; Core/Gameplay/UI/Tests compile 0 warning / 0 error; L1 green.
Not verified in game** (this card forbids deploy, game launch and `build.ps1`; the Lead owns the release
gate and the in-game round).

Card date: 2026-10-25. Workspace: `Documents\Timberborn\Mods\HighDensityBoxFarm` (source only).
Requirement source read read-only: `...\high-density-box-farm\高密度盒子农场模组需求文档.md`,
chapters 11.3, 14.1, 14.4, 14.5.

Honest one-line summary: the snapshot now carries the last scan's accepted candidate count
(`TillablePlots`, `-1` = unknown), gameplay fills it from `OnPlotsScanned(candidates.Count)`, and the
entity panel shows **registered / effective / tillable** side by side, so an empty registry can no
longer hide behind the worker-derived effective count. The effective row keeps its formula inputs in
the hover tooltip (`min(capacity, W × 38)`); the work-range area (121/225/361/729) appears only in the
tillable tooltip. Three new keys exist identically in enUS/zhCN/zhTW, and all chapter-14.5 number
formatting was moved into game-free Core functions so L1 can assert it.

## 0. Concurrent-card note (important for the Lead)

The Host started **BUG-05** (plot retention on rescan) in the same workspace while this card was
running; BUG-05 also edits `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` (and adds
`HDBF.Core/Plots/PlotRetention.cs`, extra diagnostics and `Bug05PlotRetentionTests.cs`). This card's
edits in the shared controller files are at different statements and survived; the final evidence
below was taken on the combined tree whose last source write was 12:02:12 (BUG-05 session was still
marked *running* at 12:13 but had stopped writing). No BUG-05 file was modified by this card.
BUG-05's retention rule makes a previously registered crop-occupied cell an accepted scan candidate
again, so `TillablePlots = candidates.Count` stays consistent with chapter 11.3: the count is what the
scan accepted, and excluded/claimed cells are still rejected inside `TryAcceptPlot`.

## 1. Data layer (Core + Gameplay)

| File | Lines | Change |
|---|---|---|
| `src/HDBF.Core/Abstractions/BoxFarmSnapshot.cs` | `:65-78` | new `const int UnknownTillablePlots = -1` and `int TillablePlots { get; init; } = UnknownTillablePlots`. Documented as "accepted candidates of the last completed scan; not derived from EffectivePlots/capacity". Existing fields (`RegisteredPlots`, `EffectivePlots`, …) untouched — no semantic change. |
| `src/HDBF.Core/Formatting/PanelNumbers.cs` | **new**, `:22-89` / `:102-187` | `PanelPlotFigures` (registered, capacity, effective, workers, plots-per-farmer, tillable, work-range side/area) with `FromSnapshot(snapshot, profile)`; `PanelNumbers` formatting rules of chapter 14.5 (`Percent`, `Days`, `DaysRounded`, `Multiplier`, `PlotRatio`, `PlotCountOrUnknown`, `RoundedCount`, `Unknown = "—"`). Pure Core, no game/Unity/localization reference. |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs` | `:123-129` | instance field `_tillablePlots = BoxFarmSnapshot.UnknownTillablePlots` (no static mutable state). |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` | `:82-85` | `StartScan()` resets `_tillablePlots` to `UnknownTillablePlots` — a running scan means the previous count is stale, so the panel renders "—" until completion (chapter 14.1). |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` | `:451-455` | `OnPlotsScanned(IReadOnlyList<PlotCandidate> candidates, …)` sets `_tillablePlots = candidates.Count`. Excluded/claimed/terrain/blocked cells never enter `candidates` (`TryAcceptPlot` returns false for them), so this is the "可开垦格" count required by the card. Set before the capacity cut, so it is not capped at `PlotCapacity`. |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Snapshot.cs` | `:52` | `CreateSnapshot()` copies `TillablePlots = _tillablePlots` (the only `BoxFarmSnapshot` construction point; `HdbfFarmQueryService`'s `EmptySnapshot` therefore defaults to the unknown sentinel, and `snapshot with { Warnings = … }` copies the field automatically). |

Scan path in one line: `StartScan` → unknown; scan runs frame-budgeted; `OnPlotsScanned` → accepted
candidate count; `CreateSnapshot` → panel.

## 2. UI — the three numbers in the core data area

Displayed rows, in panel order (after 工人):

| Row | Display | Data source | Notes |
|---|---|---|---|
| 已注册格 | `Registered Plots {0}/{1}` / `已注册格 {0}/{1}` | `snapshot.RegisteredPlots` / tier `PlotCapacity` | `0/60` is shown as `0/60`; never a capacity-derived stand-in (chapter 11.3 risk note). |
| 有效托管格 | `Effective Plots {0}/{1}` (unchanged key/text) | `snapshot.EffectivePlots` / capacity | Hover keeps `HDBF.Tooltip.EffectivePlots` = `min(capacity, W × 38)`, so the formula inputs are visible (chapter 14.1 / REQ-UI-02). |
| 可开垦格 | `Tillable Plots {0}` / `可开垦格 {0}` | `snapshot.TillablePlots` | `-1` (scan not completed / no scan yet) → `—`; a completed scan with 0 accepted cells → `0` (known zero, not unknown). |

| File | Lines | Change |
|---|---|---|
| `src/HDBF.UI/Localization/HdbfUiLocKeys.cs` | `:25-33`, `:108-111` | new constants `PanelRegisteredPlots`, `PanelTillablePlots`, `TooltipTillablePlots` (chapter-14.4 naming: `HDBF.<Panel\|Tooltip>.<PascalCase>`). |
| `src/HDBF.UI/Panels/HdbfFarmPanelData.cs` | `:44-57` | new localized fields `RegisteredPlots`, `TillablePlots`, `TillablePlotsTooltip`; existing records unchanged. |
| `src/HDBF.UI/Panels/HdbfFarmPanelData.cs` | `:125-147` | one `PanelPlotFigures.FromSnapshot(...)` feeds the three rows; registered label gets `(figures.RegisteredPlots, figures.PlotCapacity)`; tillable gets `figures.TillableText` ("—" or the number); tillable tooltip gets `(workRange, workRange, workRangeCells)`. |
| `src/HDBF.UI/Panels/HdbfFarmPanelData.cs` | `:283-310`, `:440-458` | all chapter-14.5 formatting now delegates to `PanelNumbers` (identical rounding semantics: irrigation floor, fatigue ceil, days 1 decimal floor/ceil, maintenance 1 decimal round, multipliers 2 decimals, counts 0 decimals/no group separators). Warning number args use `PanelNumbers.RoundedCount`. |
| `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` | `:66-68`, `:131-135` | new `Label` fields; core rows created in the order workers → registered → effective → tillable → irrigation…. |
| `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` | `:391-395` | rows rendered from `data.RegisteredPlots` / `data.EffectivePlots` / `data.TillablePlots`. |
| `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` | `:408-414` | tillable hover carries `data.TillablePlotsTooltip` (work-range area detail, chapter 14.1: area never on the main panel). |
| `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` | `:640-644` | refresh fingerprint now includes `snapshot.TillablePlots` (it already included `RegisteredPlots`), so a finished scan re-renders within the 4 Hz cap without extra query work. |

Work-range area per tier (tooltip only): Basic 11×11 = **121**, Intermediate 15×15 = **225**,
Advanced 19×19 = **361**, Top 27×27 = **729** (locked by L1 test, chapter 11.3).

## 3. Localization (chapter 14.4)

Three new keys, identical placeholder sets in all three shipped files (key count 83 → 86 each,
key sets identical):

| Key | enUS | zhCN | zhTW | Placeholders |
|---|---|---|---|---|
| `HDBF.Panel.RegisteredPlots` | `Registered Plots {0}/{1}` | `已注册格 {0}/{1}` | `已註冊格 {0}/{1}` | `{0}`,`{1}` |
| `HDBF.Panel.TillablePlots` | `Tillable Plots {0}` | `可开垦格 {0}` | `可開墾格 {0}` | `{0}` |
| `HDBF.Tooltip.TillablePlots` | `Tillable plots found by the last scan in the {0}×{1} work range ({2} cells); excluded and other farms' plots are not counted.` | `最近一次扫描在 {0}×{1} 工作范围（{2} 格）内可开垦的格数；不含玩家排除格与其他农场的格。` | `最近一次掃描在 {0}×{1} 工作範圍（{2} 格）內可開墾的格數；不含玩家排除格與其他農場的格。` | `{0}`,`{1}`,`{2}` |

* File rows: `data/Localizations/enUS.csv:11,13,37`, `zhCN.csv:11,13,37`, `zhTW.csv:11,13,37`.
* Label length limits (chapter 14.4): registered = 12 Chinese chars / 24 English chars (limit 12/24);
  tillable = 8 / 18 (limit 12/24). Tooltip is a description-style key (exempt from the label limit)
  and carries the work-range area as a detail figure.
* `tools/validate/Localizations.cs:17-56` — required-key list extended (added, nothing removed):
  `HDBF.Panel.RegisteredPlots` (`:32`), `HDBF.Panel.TillablePlots` (`:34`),
  `HDBF.Tooltip.TillablePlots` (`:55`). The validator's existing `ValidateUiKeyConstants` pass also
  cross-checks the three new `HdbfUiLocKeys` constants against enUS/zhCN automatically.

Reproducible localization check (run from the workspace root; all `Import-Csv` rows parsed as
exactly 3 fields):

```
keys: enUS=86 zhCN=86 zhTW=86
key sets identical across 3 languages: OK
placeholders HDBF.Panel.RegisteredPlots : {0,1} OK
placeholders HDBF.Panel.TillablePlots   : {0} OK
placeholders HDBF.Tooltip.TillablePlots : {0,1,2} OK
RegisteredPlots  en=24 zhCN=12 zhTW=12
TillablePlots    en=18 zhCN=8  zhTW=8
label limits OK
no raw HDBF.* literal outside HdbfUiLocKeys.cs: OK
no bin/obj inside workspace: OK
```

## 4. Tests (`tests/HDBF.Tests`, no game assembly — REQ-TEST-03)

New file `tests/HDBF.Tests/PanelPlotsContractTests.cs` — 20 tests:

* **data layer**: `TestSnapshotTillableDefaultsToUnknown` (`:35`), `TestSnapshotCarriesTheScanCandidateCount` (`:46`);
* **three figures**: `TestRegisteredZeroIsShownAsZeroNotCapacity` (`:56`, the card's core assertion: 0 → `0/60`, effective still 60, never 60/60 for registered), `TestRegisteredRatioUsesRegisteredFirstAndCapacitySecond` (`:71`), `TestTillableUnknownRendersTheDash` (`:79`), `TestTillableKnownRendersItsNumber` (`:90`, includes known `0` is not "—"), `TestWorkRangeAreasMatchChapter113` (`:106`, 121/225/361/729), `TestWorkRangeAreaOnlyAppearsInTheTooltipKey` (`:127`, panel templates ≤2 placeholders, tooltip has {0},{1},{2});
* **chapter-14.5 formats**: `TestPercentFormattingUsesZeroDecimalsAndConservativeRounding` (`:147`), `TestDaysFormattingUsesOneDecimal` (`:159`), `TestMultiplierFormattingUsesTwoDecimals` (`:169`), `TestPlotCountsAreInvariantIntegersWithoutGroupSeparators` (`:177`), `TestUnknownIsOnlyTheNegativeSentinel` (`:187`), `TestRoundedCountIsSafeForWarningArguments` (`:195`);
* **localization contract**: `TestNewKeysExistInAllThreeLanguagesWithIdenticalSets` (`:204`), `TestNewKeysKeepPlaceholdersIdenticalAcrossLanguages` (`:222`), `TestPanelLabelLengthLimitsAreRespected` (`:235`), `TestPanelTemplatesRenderTheThreeFigures` (`:251`, end-to-end text: `Registered Plots 0/60`, `已注册格 0/60`, `Tillable Plots —`, `可开垦格 121`), `TestUiKeyConstantsMatchTheShippedKeys` (`:279`), `TestRegisteredAndEffectiveKeysStayUnchanged` (`:293`).

Test-infrastructure change (`tests/HDBF.Tests/HDBF.Tests.csproj:37-56`): the pure constant table
`src/HDBF.UI/Localization/HdbfUiLocKeys.cs` is `<Compile Link>`-ed into the L1 assembly (it references
no Timberborn type), and the three shipped CSVs are embedded as resources
(`HDBF.Tests.Localizations.{enUS,zhCN,zhTW}.csv`), so the key-set/placeholder/label-length tests run
against exactly the shipped data from any working directory. `HdbfFarmPanelDataFactory` itself is
still not loadable offline (Timberborn.Localization) — its testable number rules are what moved to
Core, exactly as the card allowed.

Observed (real, this machine):

```
# card-only run, before BUG-05's 13 tests landed (307 pre-existing + 20 new)
HDBF L1 test run - 2026-10-25T04:01:28Z
PASSED 327  FAILED 0

# final combined tree incl. BUG-05 (307 + 20 + 13)
HDBF L1 test run - 2026-10-25T04:11:33Z
PASSED 340  FAILED 0
```

## 5. Build evidence (sandbox, outputs outside the workspace)

```
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj        -c Release -m:1 -p:HdbfWorkRoot=$w  -> 0 warnings, 0 errors
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w -> 0 warnings, 0 errors
dotnet build src\HDBF.UI\HDBF.UI.csproj             -c Release -m:1 -p:HdbfWorkRoot=$w -> 0 warnings, 0 errors
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj     -c Release -m:1 -p:HdbfWorkRoot=$w -> 0 warnings, 0 errors
dotnet build tools\validate\validate.csproj         -c Release -m:1 -p:HdbfWorkRoot=$w -> 0 warnings, 0 errors
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll" -> PASSED 340 FAILED 0
```

Final evidence timestamp: 2026-10-25 12:13 +08:00, on a tree whose latest source write before the run
was BUG-05's 12:02:12. `tools/validate` was **compiled** (the required-key list changed) but **not
run** (the Lead's release gate runs it with the five-assembly set). No deploy, no game launch, no
`build.ps1`.

## 6. Files changed / not changed

Changed:

* `src/HDBF.Core/Abstractions/BoxFarmSnapshot.cs`, `src/HDBF.Core/Formatting/PanelNumbers.cs` (new)
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs`, `…Scan.cs` (shared statement area with
  BUG-05, see §0), `…Snapshot.cs`
* `src/HDBF.UI/Localization/HdbfUiLocKeys.cs`, `src/HDBF.UI/Panels/HdbfFarmPanelData.cs`,
  `…HdbfFarmPanelFragment.cs`
* `data/Localizations/{enUS,zhCN,zhTW}.csv`, `tools/validate/Localizations.cs`
* `tests/HDBF.Tests/{HDBF.Tests.csproj,PanelPlotsContractTests.cs}`
* this report

Not changed: `data/Buildings/**`, `data/Research/**`, `data/TemplateCollections/**`,
`data/HDBF_Config.json` (no balance/spec/cost/research change), `docs/VERIFICATION.md`,
`docs/REQ_TRACEABILITY.md`, other workstream reports, `src/HDBF.UI/README.md` (outside this card's
code/data/tests scope), managed-plot highlight rendering, world warning icons, dense/fallow logic
(other cards), deployed folder `Mods\HDBF`, saves, the game.

## 7. In-game pending checklist (one unlocked run; the Lead's gate)

Panel to inspect: **core data area, the three plot rows**. Expected values per scenario:

1. **Fresh placement, first scan not finished yet** (a few frames): 已注册格 `0/60` (or the prior
   saved registry), 有效格 `60/60` (Basic, 2 farmers), 可开垦格 **`—`**. The dash must turn into a
   number within the first scan (11×11 = at most 121 cells, ≤ `perf.scanBudgetPerFrame` per frame).
2. **Scan completed on flat grass**: 已注册格 `60/60`, 有效格 `60/60`, 可开垦格 ≥ 60 (typically up to
   121 minus water/steep/blocked cells). Hover the tillable row → `… 11×11 work range (121 cells)…`;
   the number 121 must **not** appear in the main list.
3. **Reduce farmers to 1** (Intermediate, capacity 150): 有效格 `38/150`, hover shows
   `min(150, 1 × 38)`; 已注册格 unchanged (e.g. 150/150); 可开垦格 unchanged. This is the case the
   chapter-11.3 risk note wants visible: registered plots beyond `P_eff` are reserves, not output.
4. **Registry actually empty / blocked map** (place where few or no cells are plantable; or after a
   terrain/blocking change): 已注册格 must show a visibly **small or 0 numerator** (`0/60`), while
   有效格 still shows the worker-derived value — the old panel showed only `60/60` here. If a scan
   completed and accepted nothing, 可开垦格 shows `0` (known), not `—`.
5. **Workers 0**: 有效格 `0/60`; 已注册格/可开垦格 unchanged — three numbers explain the situation.
6. **Language**: switch zhCN / zhTW / enUS — the three rows read 已注册格/有效托管格/可开垦格 or
   Registered/Effective/Tillable, never a raw `HDBF.*` key; the tillable tooltip is localized too.
7. **BUG-05 interaction (next round)**: with crops on the plots, the daily rescan must keep
   已注册格 at 60 (BUG-05's invariant) and 可开垦格 should stay ≥ 60 because retained registered
   cells count as accepted candidates; if 可开垦格 < 已注册格 after a completed scan, capture
   `diag.scan.end`/`kept/added/dropped` for the next round.
8. Refresh: the tillable value updates on scan completion within ≤250 ms (fingerprint includes it);
   no per-frame formatting work is added (4 Hz cap unchanged).

In-game values are **not claimed** here: the three rows and their text were verified offline only.

## 8. Requirement traceability (this card's scope)

| Requirement | Status |
|---|---|
| chapter 11.3 risk note — 面板同时显示已注册格/有效格/可开垦格 | implemented + offline-verified (data + formatting + templates in L1); in-game display unverified |
| chapter 14.1 — 数据未就绪显示 "—"；工作范围面积只在详情/tooltip | implemented: `-1` sentinel → "—"; area only in `HDBF.Tooltip.TillablePlots`; L1 pins placeholder structure and areas |
| chapter 14.1 / REQ-UI-02 — 有效格同时显示公式与输入项 (W × 38) | preserved: existing effective tooltip unchanged; L1 keeps the effective key semantics; live hover unverified |
| chapter 14.4 — key naming, ASCII PascalCase, ≤12/≤24 label limits, identical key sets | implemented; key sets/lengths/placeholders covered by L1 over the shipped CSVs and validator list |
| chapter 14.5 — number formats (0 decimals for plots/percent, 1 for days, 2 for multipliers, "—" for unknown, no group separators) | implemented in Core `PanelNumbers`; L1-verified; panel rendering/font behaviour unverified |
| REQ-ARCH-04 — UI only through Core contracts | UI reads `BoxFarmSnapshot` + Core `PanelNumbers`; no gameplay type in UI |
| REQ-TEST-03 — game-free L1 | new tests reference no Timberborn/Unity assembly; CSVs embedded |

## 9. Open risks / honesty notes

* **Not in game.** "Compiles + L1 green" is not "the panel looks right in game": UIElements text
  wrapping, tooltip hover behaviour, Chinese glyph width and the 420 px panel layout were not observed
  in this card.
* **Tooltip `W × 38` is the display constant** `TierCatalog.PlotsPerFarmer` (38). Gameplay computes
  `EffectivePlots` with `_spec.PlotsPerFarmer`, which a JSON override could change; then the hover
  formula would still read 38. This mismatch predates this card (the UI tooltip already used
  `TierCatalog.PlotsPerFarmer`) and was deliberately not changed here.
* **BUG-05 concurrent execution** (§0): the final evidence covers the combined tree. If BUG-05 makes
  further edits after this report, re-run the four builds + L1; the lines this card touched in
  `Scan.cs` are `:82-85` and `:451-455` (grep `_tillablePlots`).
* The `tools/validate` required-key list now includes the three new keys; the release gate must run it
  with the five assemblies (`build.ps1 -Install`) before any deploy claim.
