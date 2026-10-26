# HDBF.UI — entity panel for the box farms

This assembly owns everything the player sees for a box farm: the right-hand entity panel
fragment, its status/warning rows and the localization keys the UI formats.

Dependency rule (chapter 9.1): `HDBF.UI` references **HDBF.Core only**, never
`HDBF.Gameplay`. Data reaches the UI exclusively through
`HDBF.Core.Abstractions.IBoxFarmQuery` / `BoxFarmSnapshot`, which gameplay binds in its own
`[Context("Game")]` configurator (`Bind<IBoxFarmQuery>().ToExisting<HdbfFarmQueryService>()`).

## What is actually wired (verified game extension point)

The panel is **not** a stub and does **not** need any Unity prefab/UXML asset. It uses the same
code-only extension point as the vanilla UI assemblies:

| Piece | Game API (verified with `tools/ApiIndex` against Timberborn 1.1.2.8) |
|---|---|
| Configuration | `[Bindito.Core.Context("Game")] HdbfUiConfigurator : Bindito.Core.Configurator` |
| Fragment contract | `Timberborn.EntityPanelSystem.IEntityPanelFragment` (`InitializeFragment`, `ShowFragment`, `ClearFragment`, `UpdateFragment`) |
| Panel contribution | `MultiBind<EntityPanelModule>().ToProvider<HdbfEntityPanelModuleProvider>().AsSingleton()` |
| Module building | `new EntityPanelModule.Builder().AddMiddleFragment(fragment, 1).Build()` — exactly the pattern of `Timberborn.FieldsUI.FieldsUIConfigurator+EntityPanelModuleProvider::Get` |
| Localization | `Timberborn.Localization.ILoc.T(key[, arg…])` |
| Selected entity id | `Timberborn.EntitySystem.EntityComponent.EntityId` (`System.Guid`), found through `BaseComponent.AllComponents` |

`HdbfFarmPanelFragment.InitializeFragment()` builds its `VisualElement` tree in C# (labels and
containers only). There is **no** `.uxml`, `.uss`, sprite or GameObject path in this assembly.
The fragment is invisible (`display: none`) for every entity that is not a readable box farm, so
it adds no rows to other buildings.

Public data types (game-free, testable without Unity): `Panels/HdbfFarmPanelData` (all formatted
panel fields + the ordered warning rows, built by `HdbfFarmPanelDataFactory.Create`) and
`Panels/HdbfFarmStatusRow` (localized text + P0..P4 severity).

## Behaviour

* `ShowFragment(entity)` resolves the entity's `Guid`, queries `IBoxFarmQuery` and renders.
* `UpdateFragment()` re-queries at most **4 times per second** (250 ms, matching the default
  `perf.uiRefreshHz = 4`, REQ-UI-12) and only re-formats when a value fingerprint changed.
* `ClearFragment()` hides the rows and drops the query target.
* A missing snapshot / throwing query hides the fragment; the UI never propagates an exception
  into the panel (REQ-SAFE-01).
* Rows (chapter 14.1): tier name, P0–P4 status rows (max 3 + "N more warnings"), workers,
  effective plots (`min(P_max, W × 38)` in the tooltip), irrigation coverage (floored percent),
  fatigue average + max (ceiled percent), maintenance countdown (or "no material upkeep" for the
  basic tier), tank water days (floor, 1 decimal), power state, and the three port states.
* Hovering the fragment shows the multiplier breakdown
  (`HDBF.Tooltip.MultiplierBreakdown`, five × two-decimal multipliers).
* All formatting follows chapter 14.5: integral percentages, one decimal for days, two for
  multipliers, `—` for unknown/NaN/Infinity, invariant culture, no thousands separators.

## Interaction (write side)

All writes go through `HDBF.Core.Abstractions.IBoxFarmCommand`, which gameplay implements and
binds to the same service that answers `IBoxFarmQuery`; the panel never sees a game type. The
contract returns `true` only when the action fully applied, and on `false` a no-placeholder
failure key from `HdbfCommandFailures` (`HDBF.Core.Abstractions`) which the UI localizes.

* **Mode row** (REQ-UI-06): three buttons (Normal / Dense / Fallow). The active mode is the
  disabled segment. Dense is two-step — the first click arms "Confirm Dense" and shows the hint,
  the second click executes; every button carries a localized cost tooltip (yield ×1.35,
  water ×1.5, fatigue ×1.5 for Dense; "production stops" for Fallow).
* **Maintenance button** (REQ-UI-07): calls `TryPerformMaintenance`; disabled for the
  irrigation-only Basic tier with the "no material upkeep" reason; its countdown row shows the
  same reason. While the farm is shut down (P0) the button is bold, i.e. the primary action.
  A rejected call (missing materials once gameplay wires inventory deduction) shows the localized
  failure key instead of a generic error.
* **Port toggles** (REQ-UI-07): one `Toggle` per port plus a state label (Connected / Not
  Connected / Disabled / Not Applicable) so the state is never colour-only. Hidden (tier has no
  such port) and Fault disable the toggle. A rejected switch is rolled back by the next snapshot
  read — the panel never keeps optimistic state.
* **Result line**: the last success/failure message is rendered under the action button; keys are
  whitelisted (`FormatCommandFailure`), so an unexpected/null key degrades to the generic
  "action failed" text and a raw localization key can never reach the player. **World-space plot
  actions** (click exclude/restore, Shift+drag box select) are executed by gameplay; their last
  rejection reason arrives through `BoxFarmSnapshot.PlotActionFailureKey` and is shown in the same
  line when no newer panel-local message is pending.
* **Managed-plot highlight button** (REQ-UI-05, chapter 14.3): a focusable button in the action row
  calls `IBoxFarmCommand.TrySetPlotHighlight` with the inverse of the authoritative
  `BoxFarmSnapshot.IsPlotHighlightActive`. While active it is bold, and a legend (four colour-coded
  rows whose state is also written as text) plus the Shift+drag hint are shown; the panel never
  flips state optimistically. `ClearFragment()` and a building switch close the overlay through the
  same command, so a closed panel can never leave the world tinted. The world overlay,
  click/box-select input and frame-budgeted rendering live in gameplay, not in this assembly.
* **Safety**: every command callback is try/catch-guarded and logs through `IHdbfLog`
  (`ui.command-rejected` / `ui.command-exception` / `ui.render`, 30 s throttled), so a broken
  gameplay implementation can never break the entity panel (REQ-SAFE-01).
* **Keyboard**: `Button` and `Toggle` are UIElements focusable controls and handle
  `NavigationSubmit`, so Tab/Shift+Tab/Enter work with the game's default focus ring; no custom
  USS is required (chapter 14.3/14.5).

## Warning data contract (with HDBF.Gameplay)

`BoxFarmSnapshot.Warnings` is `IReadOnlyList<FarmWarning>` with
`{ CodeKey, Severity, NumberArg0, NumberArg1 }` (no string payload). This UI implements the
contract that `HDBF.Gameplay` actually writes:

| Warning key | Args as produced by gameplay | UI formatting |
|---|---|---|
| `HDBF.Warning.Shutdown` | — | no args |
| `HDBF.Warning.NoWater` | `coverage × 100`, `C_min × 100` | two percent points |
| `HDBF.Warning.IrrigationLow` | `coverage × 100`, `C_min × 100` | two percent points |
| `HDBF.Warning.NoPower` | `offGridEfficiency × 100` (60) | one percent point |
| `HDBF.Warning.AdapterFailed` / `AdapterDisabled` | adapter **count** | integer count |
| `HDBF.Warning.MaintenanceOverdue` | missed cycles | integer |
| `HDBF.Warning.FatigueHigh` | `F_max × 100` | one percent point |
| `HDBF.Warning.TankLow` | tank days left | 1 decimal |
| `HDBF.Warning.EntranceBlocked` | — | no args |
| unknown key | — | `HDBF.Warning.Generic` (never a raw key) |

Rows are sorted by `FarmWarningSeverity` (Critical = P0 … Info = P4) and then by key, so the
panel order is deterministic. Gameplay already applies the chapter-14.2 hysteresis, so the UI
does no debouncing of its own except the 4 Hz refresh cap.

## Localization

`Localization/HdbfUiLocKeys.cs` holds one compile-time constant per key the UI formats. The
constants must exist verbatim in `data/Localizations/enUS.csv`, `zhCN.csv` and `zhTW.csv` (the
three files carry identical key sets); `tools/validate` reads the UI constants and fails when a
constant is missing from enUS/zhCN, and the build pipeline fails when any of the three files is
missing. Lowercase `zhTW` text still uses English/Traditional Chinese as authored — only the key
set is enforced there.
Chapter 14.4 defines 30 keys; the UI additionally formats `HDBF.Panel.IrrigationThreshold`,
`HDBF.Panel.FatigueMax`, `HDBF.Panel.TankWater`, `HDBF.Panel.MaintenanceNone`, the power keys,
the port-state labels, `HDBF.Warning.NoWater`, `HDBF.Warning.Shutdown`,
`HDBF.Warning.TankLow`, `HDBF.Warning.EntranceBlocked`, `HDBF.Warning.MoreWarnings`,
`HDBF.Warning.Generic`, `HDBF.Panel.Status`, `HDBF.Panel.Mode`, `HDBF.Panel.Ports`,
`HDBF.Panel.Unknown`, `HDBF.Tooltip.MultiplierBreakdown` and `HDBF.Tooltip.EffectivePlots`.
The interactive panel adds the action keys (`HDBF.Action.ConfirmDense`, `DenseConfirmHint`,
`MaintenanceDone`, `ModeChanged`, `PortUpdated`), the tooltip keys (`HDBF.Tooltip.ModeNormal`,
`ModeDense`, `ModeFallow`, `CurrentMode`, `Maintenance`, `MaintenanceShutdown`, `PortToggle`,
`PortPower`), the port failure key `HDBF.Port.PowerManaged`, and the command failure keys
`HDBF.Error.ActionFailed`, `ErrorDenseDisabled`, `ErrorMaterialsMissing`, `ErrorMissingMod`,
`ErrorNotBoxFarm`, `ErrorPlotMinimum`, `ErrorPlotNotRegistered` (plus the reused
`HDBF.Mode.SwitchCooldown` and `HDBF.Port.NotApplicable`). Their string values are duplicated in
`HdbfCommandFailures` (Core cannot reference UI); both must stay in sync.
It also declares `HDBF.Error.LoadFailed` for a future degraded-load row (currently the fragment
hides itself when the query fails, which is safer than showing a permanent error), and
`HDBF.Panel.WorkRange` is reserved for the "details" view that v0.1.0 does not have.

## Known gaps (honest list for v0.1.0)

1. **Managed-plot interaction is implemented but not verified in game.** The panel highlight
   button/legend and the `TrySetPlotHighlight` command chain are implemented; the world-space
   overlay, click exclude/restore, Shift+drag box select, Esc handling and the frame-budgeted
   renderer live in `HDBF.Gameplay` (`HdbfPlotHighlightService`/`HdbfPlotHighlightRenderer`,
   REQ-UI-05). They compile and their pure rules are covered by L1 tests, but no one has run them
   in a running Timberborn build yet — the honest status is "compiles, needs in-game check"
   (see `docs/workstreams/build-02-plot-highlight.md`). The optional red **Unreachable** state is
   defined in the contract and legend but is not populated by the current scan (chapter 11.3
   diagnostic; the scan drops unreachable candidates).
2. **Adapter warnings show a count, not an adapter id**, because `FarmWarning` has no string
   field. Adding `StringArg` to `HDBF.Core.Abstractions.FarmWarning` would let the UI render
   "Adapter Failed: TimberPipes"; that is a Core contract change, not a UI-only change.
3. **Cross-building aggregation rows and world-space icons are implemented, not yet verified in
   game.** The panel renders the cached on-screen warning groups ("另有 N 座建築…", expandable,
   click a member to locate it via the vanilla selection service), and P0/P1 overhead icons reuse
   the vanilla `StatusSubject`/`StatusIconCycler` pipeline (40-cell culling, 1 Hz P0 blink,
   `ui.*` switches). Both compile and their Core rules are covered by L1 tests
   (`docs/workstreams/build-04-warning-icons.md`), but the sprite choice, the camera-focus distance
   origin and the blink/culling behaviour still need the in-game check. The panel itself stays
   UI-only: it reads `IBoxFarmWarningQuery` and calls `IBoxFarmLocator`, both Core contracts.
4. **The highlight visual is a gameplay renderer, not a UXML/USS asset.** Managed-plot tinting is
   drawn by `HDBF.Gameplay`'s `HdbfPlotHighlightRenderer` (vanilla marker tile mesh/material on
   `Layers.UILayer`, one instanced draw per state, four total) — see the build-02 workstream report
   for the IL evidence, the frame budget and the remaining in-game checks (overlay visibility, tint
   alpha, Esc layering, drag behaviour).
5. **Default label styling.** Because no USS asset is authored, rows use the game's default
   label style. A designer can restyle later with a UXML/USS asset (and change
   `InitializeFragment` to load it) without touching the data/formatting code; the current code
   is deliberately asset-free so the mod cannot break on a missing bundle.
6. **Tooltip instead of an expandable breakdown.** Requirement 14.3 asks for a hover breakdown;
   it is implemented as the fragment tooltip string, not a collapsible sub-panel.
7. **`HdbfPortState.Fault` is rendered as "Disabled"** (the state machine chapter text says
   Fault implies a retry then Hidden); gameplay currently only emits Hidden/Available/Connected,
   so the mapping is future-proof but untested in game.
8. **The refresh rate is fixed at 4 Hz** (`RefreshIntervalSeconds`). `HDBF.Core.Config.HdbfConfig`
   is read by gameplay's config provider and there is no read-only config contract exposed to the
   UI, so `perf.uiRefreshHz` is not wired into the panel yet. Any value between 1 and 10 would be
   safe; the current constant matches the documented default.
9. **Cooldown and material shortage cannot pre-disable the buttons.** `BoxFarmSnapshot` carries
   neither the mode cooldown nor the required maintenance material, so those two reasons only
   arrive after a click and are rendered in the result line. Disabled states do exist for every
   fact the snapshot exposes: active mode, irrigation-only maintenance tier, Hidden/Fault ports.
   Pre-disabling those two would need new read-model fields (a `BoxFarmSnapshot` change), which is
   deliberately out of scope for this UI round.
10. **A disabled UIElements control receives no pointer events**, so its `tooltip` may not show.
    Every disabled control therefore repeats its reason as visible text: the port state label,
    the maintenance countdown row ("no material upkeep"), or the current-mode segment.
11. **Per-port adapter health is not in the snapshot.** A "compatible mod missing" port failure is
    reported by `IBoxFarmCommand` (`HDBF.Error.MissingMod` / `HDBF.Port.PowerManaged`) and shown
    in the result line instead of pre-disabling the specific toggle.
12. **The power port toggle is forward-looking.** Gameplay v0.1 owns the power port through the
    mechanical network, so a switch request is rejected with `HDBF.Port.PowerManaged`. The toggle
    is still rendered because REQ-UI-07 asks for three switchable ports; if gameplay later allows
    an explicit disconnect, no UI change is needed.
13. **Language switch does not force an immediate re-render.** `ILoc` exposes no language-change
    event and no `ILocalizationService` is injected (kept out to avoid a new DI dependency), so an
    already-open panel re-localizes on the next value/fingerprint change or selection change.

## What remains Unity work

Nothing is required for the panel to appear and update. Remaining art/UX work, all optional and
outside this assembly:

* restyle rows and the mode/maintenance/port controls with a UXML/USS asset if the default game
  styling is not good enough (the fragment deliberately stays asset-free);
* optionally author a custom P0–P4 warning sprite set (24×24 monochrome outline, chapter 14.5) and
  point `HdbfWorldIconService` at it; the shipped build reuses the closest vanilla
  `Sprites/StatusIcons/*` sprites (see `docs/workstreams/build-04-warning-icons.md` §10.2);
* in-game verification and visual tuning of the managed-plot overlay (tint alpha, drag threshold,
  Esc layering) — the overlay code itself ships in `HDBF.Gameplay`; see
  `docs/workstreams/build-02-plot-highlight.md` for the exact steps.

## How to check manually in game

1. Build the mod package (`powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install`),
   install it, enable it in the Mods screen. Gameplay must bind `IBoxFarmCommand` first (see the
   UI report); otherwise the Game container cannot construct the fragment.
2. Build any BoxFarm tier. Select it: the panel should show the tier name and rows only for an
   HDBF building (other buildings get no extra rows).
3. Empty the tank / disconnect power / block the entrance and confirm the warning rows change
   in P0→P4 order (max three + "N more warnings").
4. Mode row: the active mode is greyed out. Hover Dense → cost tooltip. Click Dense once →
   button becomes "Confirm Dense" and the result line shows the confirm hint; click again →
   mode switches and the active segment moves. Repeat within one game day → the result line shows
   the cooldown reason.
5. Maintenance button: Basic tier is disabled with "No material upkeep" (also in the countdown
   row). On a tier with material upkeep, force a shutdown (miss two cycles) → the button becomes
   bold; click it → countdown resets and the result line confirms.
6. Port rows: a tier without a port shows "Not Applicable" and the toggle is disabled. Toggle pipe
   off/on → state text and checkbox follow. Toggle the power port on the Top tier → the result line
   explains that the output is controlled by the power network (gameplay v0.1).
7. Keyboard: Tab to the mode/maintenance/highlight buttons and the port toggles, Enter activates,
   the game's focus ring follows. Tab order matches the visual order.
8. Managed-plot highlight (REQ-UI-05, needs in-game check): click "Show Managed Plots" → the four
   legend rows appear and the managed cells are tinted green (irrigated) / yellow (dry) / gray
   (excluded); the button turns bold. Click a managed cell → it turns gray and Registered Plots
   drops by one; click a gray cell → it is restored and reappears after the next rescan. Clicking
   the world must not select another building while the overlay is open. Shift+drag a rectangle →
   the registered cells inside it are excluded in one batch (a request that would leave zero
   registered plots is refused and the reason appears in the result line). Esc closes the overlay;
   a second Esc closes the panel. Closing the panel or selecting another building clears the
   overlay. With the Top tier (600 plots) opening the overlay must not visibly hitch; the overlay
   should be four draw calls (Profiler/Frame Debugger) and finish within three frames.
9. Open the same panel in Chinese: all rows, buttons, state labels, tooltips and result messages
   must be Chinese with no `HDBF.` text visible (switch language while the panel is closed, then
   select the building — see known gap 13).
