# HDBF.UI — v2 indoor farm entity panel

This assembly owns everything the player sees for a v2 indoor box farm: the right-hand entity
panel fragment, its card/row read model, the vanilla controls it composes and the localization keys
it formats. It renders only the `HDBF.Core.Indoor.IndoorFarmSnapshot` DTO
(docs/v2/INTERFACES.md section 9); no simulation state is read from a game component.

Dependency rule (docs/v2/ARCHITECTURE.md section 1): `HDBF.UI` references **HDBF.Core only**.
The game-context configurator binds the gameplay implementations to the Core query/command
interfaces; this assembly never compiles against `HDBF.Gameplay`.

## Panel look: the original controls, not an invented skin

The panel is authored in code with UIElements, but every visual is a shipped vanilla control or
class (read-only reference: `%USERPROFILE%\.hdbf\ref\UI\Views`, an unpack of the installed game):

| Panel part | Vanilla source used |
|---|---|
| cards | `.entity-sub-panel` + `.bg-sub-box--green/blue/red-striped` (NineSliceVisualElement) |
| section titles | `.game-text-heading .text--bold` inside a `.entity-panel__row` |
| compact subtitle row | `.entity-panel__row` + `.entity-panel__text .text--bold` (tier · faction; the building name is NOT repeated — the vanilla `EntityPanel` header already shows icon + name + district) |
| rows | `.entity-panel__row`, `.inventory-row__image` (24 px), `.entity-panel__text`, `.text--bold` |
| progress bars | `Timberborn.CoreUI.ProgressBar` (clones `Core/ProgressBar.uxml`: frame/background/fill/content) with `.progress-bar--green/red` and `need-view__image--marker` for C_min; used for the tank, coverage C and output occupancy |
| warnings | `StatusListElement.uxml` structure: `.status-list-element` + `.status-list-element__icon` + `.status-list-element__text` + a 3 px accent strip |
| buttons | the shipped `Game/EntityPanel/DebugButton.uxml` (a `cui:NineSliceButton` with `button-game`) loaded through `VisualElementLoader`, so the gold nine-slice frame/hover/click sound are the original ones |
| worker roster | the shipped `Game/EntityPanel/WorkerView.uxml` chips (`.worker-view`, `.worker-view__name`), read-only; a vacant chip shows the vanilla empty-beaver ("vacant") avatar and **never** the warning triangle (a free slot is normal state, not a problem) |
| mode switch | `RadioToggleFactory` + `Core/RadioButton.uxml` (`.radio-button`, `.selected` radio sprite) — normal / dense / fallow |
| crop dropdown | `Timberborn.DropdownSystem.Dropdown` (clones `Core/Dropdown.uxml`) + `DropdownItemsSetter`. Both vanilla text surfaces are `.dropdown-item__text` Labels (grey): the closed selection is re-painted after every `SetItems`/`UpdateSelectedValue`, and the open popup list — owned by the singleton `DropdownListDrawer`, outside the Dropdown subtree — is re-painted light when the `Showed` event fires. The fixed 28 px popup rows keep vanilla nowrap/ellipsis; the panel rows (rotation sequence, ports, values) wrap |
| scroll | `.scroll--green-decorated` on the card scroller, scroller hidden so the cards span the full 325 px width |
| tooltips | the vanilla `ITooltipRegistrar` (hover texts for rows, actions and the dropdown) |

The panel width is the vanilla entity-panel width (`.entity-panel { width: 325px }`); the fragment
root is transparent — only the section cards own a dark background.

### Colours (requirement D)

* Vanilla ships no `.text--white` class; the closest is `.text--default` (rgb 255). The panel
  therefore paints every text element it creates — and every vanilla label it adopts (the crop
  dropdown selection after each rebuild, the open dropdown popup when it is shown) — through one
  helper, `HdbfPanelControls.ApplyLightText`, with the single warm-white constant
  `HdbfIndoorPanelStyle.LightTextRgba` (`#F2EBDD`).
* No row/state uses a hard-coded dark colour, and `HdbfPanelControls.CreateLabel` is the only
  place `this panel` creates a `Label`; the vanilla templates keep their own labels, which the
  helper repaints where they would otherwise stay grey.
* Warnings stay recognisable without relying on colour: icon + `▲/!` text marker + a 3 px accent
  strip (the strip's colour is the only raw background colour the panel sets).

### Two UIElements traps fixed in this round

* `VisualElement.AddToClassList("a b")` adds **one** class named `"a b"`, which no USS selector
  matches. Every multi-class constant is now applied one name at a time through
  `HdbfPanelControls.AddClasses` (`HdbfIndoorPanelStyle.Split`). This was why the old panel showed
  dark default text and a column-laid-out duplicate header.
* An unstyled `NineSliceVisualElement` draws nothing, but a transparent fragment lets the
  **tan entity-panel background** show through wherever cards do not cover it (it looked like a
  big brown/beige block behind the panel). There is no second header any more, cards are
  contiguous and full width, and the scroller reserves no gutter, so the tan background is not
  visible (requirement C).

## Panel cards (chapter 19.11 order)

The vanilla `EntityPanel` header (icon + name + district) comes first. The fragment then renders:

| Card | Rows / controls |
|---|---|
| Workers & efficiency | compact “tier · faction” subtitle row, crew `W/W_max`, growth efficiency `G_irr` (bar + %), read-only worker roster (vacant = vanilla empty avatar, no ⚠), vanilla mode RadioToggle group, 1-day transition sentence |
| Slots | capacity, configured (bar), `P_eff` (formula tooltip), empty, growing, ready, action backlog |
| Water | live tank units (bar + days), live coverage `C` (bar + C_min marker), `C_min`, supply source, water/low-water warnings, specific derate sentence |
| Maintenance | next due day, overdue cycles, upkeep-material requirement (8 plank/10 days, 6 metal/10 days, off-grid top 15 metal/8 days), missing-material detail warning, stalled warning |
| Power | grid / off-grid / power cut / not required (+ warning row) |
| Crop strategy | current policy row + vanilla dropdown; rotation (localized crop names) or fixed crop row |
| Output | occupied/blueprint capacity (ProgressBar), output port row, pipe port row, logistics port row, power port row, output-full warning |
| Actions | Refill water / Perform maintenance vanilla buttons, inline disabled reason under each, one-line action result |

Every value row is “icon + localized label + localized value”; labels and values wrap and shrink, so
no number, port state or rotation sequence is truncated or overlapped. The card scroller height is
re-measured at the 4 Hz gate, so the action card is reachable on every window size.

## Data path

* `ShowFragment(entity)` resolves the `EntityComponent` runtime id and asks the Core query
  contract for the `IndoorFarmSnapshot` DTO. No game object is read any more (the header badge is
  the vanilla panel's own job), so the fragment is a pure DTO renderer.
* The gameplay singleton bound to `IBoxFarmQuery` exposes the v2 read model; the singleton bound
  to `IBoxFarmCommand` exposes the Core write actions. The two panel-only commands (manual refill,
  crop strategy) are additional public members resolved once by `HdbfIndoorCommandBridge` (UI may
  only reference HDBF.Core).
* The snapshot's water block is computed **live** by gameplay: `demand = P_eff × WaterPerSlotPerDay
  × modeWaterFactor`, `C = IndoorWaterMath.Coverage(tank, demand)`. A loaded full tank therefore
  shows `C 100 %` before the first daily tick instead of the stale `C 0 %`.
* `UpdateFragment()` re-queries at most **4 times per second** and rebuilds the cards only when a
  value fingerprint changed (the fingerprint includes a language probe, so a language switch
  re-localizes too). An idle panel does no formatting or label write work; the only idle cost is the snapshot query itself (4 Hz, bridge invoke buffer reused; gameplay still builds a fresh DTO per query, see known gaps).
* Actions: mode + maintenance go through the Core `IBoxFarmCommand`; crop strategy + manual refill
  go through the two panel-only members. Every rejection renders the specific localized reason
  (no worker, blocked entrance, no/unreachable/contaminated water, empty action budget, pending
  trip, full output, missing material, invalid crop) and the authoritative snapshot re-syncs the
  controls — never an optimistic state.
* A missing snapshot, a throwing query, a broken card render or a missing game UI asset hides the
  fragment and logs a throttled `ui.*` error instead of propagating into the entity panel.

## Game-free half (linked into the L1 suite)

| File | Responsibility |
|---|---|
| `Panels/HdbfIndoorPanelData.cs` | read model: compact subtitle/meta line + ordered cards + rows + worker chips + crop choices + action availability |
| `Panels/HdbfIndoorPanelFactory*.cs` | snapshot -> cards/rows (`Cards.cs` workers/slots, `Cards.Water.cs` water, `Cards.Maintenance.cs` maintenance/material, `Cards.Output.cs` power/crops/output) |
| `Panels/HdbfIndoorPanelWarnings.cs` | warning rows per card (live water rule), crop dropdown choices, severity markers |
| `Panels/HdbfIndoorPanelFormat.cs` | template formatting, enum -> key map, material lookup, failure whitelist |
| `Panels/HdbfIndoorPanelStyle.cs` | the vanilla USS class names / icon sprite keys / tier icon assets / the single light-text + warning-accent colours |
| `Panels/HdbfIndoorPanelLimits.cs` | safe bar ratios + the output capacity mirrored from the blueprints |
| `Panels/HdbfIndoorCommandBridge.cs` | one-time member resolution of the gameplay query/command singleton |

These files reference `HDBF.Core` and `HdbfUiLocKeys` only, so `V2PanelContractTests` and
`V2PanelVisualContractTests` compile them into the L1 runner and drive the real mapping (card
order, every row, live-coverage rule, no-braces rule, failure reasons, output capacity vs
blueprints, port-row separation, light-text palette, the 4 Hz/fingerprint gate).

## Localization

`Localization/HdbfUiLocKeys.cs` holds one compile-time constant per key the panel formats; every
constant must exist verbatim in `data/Localizations/enUS.csv`, `zhCN.csv` and `zhTW.csv` (identical
187-key sets). `V2LocalizationTests` pins the parity, the placeholder match, the row-label lengths,
the no-empty-brace rule, the complete v2 terminology set and the removal of every v1 plot term.

## Known gaps / deviations (honest list)

1. **Output capacity is not in the snapshot DTO.** `HDBF.Panel.OutputStock` shows occupied/capacity,
   where capacity is `HdbfIndoorPanelLimits.OutputCapacity` (50) mirrored from the eight shipped
   `SimpleOutputInventorySpec`s; `TestOutputCapacityMatchesEveryShippedBlueprint` fails if the data
   drifts. Exposing it in the DTO would remove the constant.
2. **Worker names are not in the snapshot.** The roster chips show `—` for staffed positions plus a
   localized note; the roster is read-only (the game assigns workers).
3. **The vanilla workplace fragment already shows the crew roster.** The card keeps its own
   read-only roster because chapter 19.11 asks for a worker/efficiency block; the two lists stay
   consistent because both read the same authoritative assignment.
4. **The generated panel uses the shipped game UXML templates** (`DebugButton`, `WorkerView`,
   `Core/Dropdown`, `Core/RadioButton`, `Core/ProgressBar`) and the shipped USS classes; these are
   game assets, not copied into the mod package.
5. **In-game look/feel is compile-verified only.** The L1 suite cannot load UIElements, and this
   round was not allowed to launch the game; TC-V2-05/06 still needs a screenshot round.
6. **The mode radio labels are localized once at panel build time.** A language switch re-renders
   the cards, but the already built radio buttons keep their original language until the panel is
   reopened; the vanilla `RadioToggleFactory` has no re-localize entry point.
7. **The 4 Hz snapshot query still allocates on the gameplay side.** UI reuses one invoke buffer,
   but `IBoxFarmQuery.GetIndoorSnapshot` builds a fresh immutable `IndoorFarmSnapshot` per call
   (4/s, never per frame). A gameplay-side cached DTO would remove that last bounded allocation.
