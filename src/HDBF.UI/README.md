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
| header card | `EntityPanel.uxml` structure: `.entity-panel__header bg-sub-box--purple-striped` + `.entity-panel__avatar` + `.entity-panel__heading-text` / `.entity-panel__sub-text` (NineSliceVisualElement) |
| cards | `.entity-sub-panel` + `.bg-sub-box--green/blue/red-striped` (NineSliceVisualElement) |
| section titles | `.game-text-heading .text--bold` inside a `.entity-panel__row` |
| rows | `.entity-panel__row`, `.inventory-row__image` (24 px), `.entity-panel__text`, `.text--bold` |
| progress bars | `Timberborn.CoreUI.ProgressBar` (clones `Core/ProgressBar.uxml`: frame/background/fill/content) with `.progress-bar--green/red` and `.need-view__image--marker` for C_min |
| warnings | `StatusListElement.uxml` structure: `.status-list-element` + `.status-list-element__icon` + `.status-list-element__text` |
| buttons | the shipped `Game/EntityPanel/DebugButton.uxml` (a `cui:NineSliceButton` with `button-game`) loaded through `VisualElementLoader`, so the gold nine-slice frame/hover/click sound are the original ones |
| worker roster | the shipped `Game/EntityPanel/WorkerView.uxml` chips (`.worker-view`, `.worker-view__name`, `.worker-view__vacant-icon`), disabled = read-only |
| mode switch | `RadioToggleFactory` + `Core/RadioButton.uxml` (`.radio-button`, `.selected` radio sprite) |
| crop dropdown | `Timberborn.DropdownSystem.Dropdown` (clones `Core/Dropdown.uxml`: `NineSliceButton` selection + arrows) + `DropdownItemsSetter` |
| scroll | `.scroll--green-decorated` on the card scroller |
| tooltips | the vanilla `ITooltipRegistrar` (hover texts for rows, actions and the dropdown) |

The panel width is the vanilla entity-panel width (`.entity-panel { width: 325px }`); the fragment
does not set a background, a pixel width or a colour of its own.

## Panel cards (chapter 19.11 order)

| Card | Rows / controls |
|---|---|
| Header | tier icon (vanilla building badge = `Icons/BoxFarm*`), localized name, tier · faction subtitle, mode badge |
| Workers & efficiency | `W/W_max`, irrigation growth efficiency `G_irr` (bar + %), read-only worker roster (`Vacancy` warning triangles), mode radio group, 1-day transition sentence |
| Slots | capacity, configured (bar), `P_eff` (formula tooltip), empty, growing, ready, action backlog |
| Water | live tank units (bar + days), live coverage `C` (bar + C_min marker), `C_min`, source, water/low-water warnings, specific derate sentence |
| Maintenance | next due day, overdue cycles, material-missing warning with material + count, stalled warning |
| Power | grid / off-grid / power cut / not required (+ warning row) |
| Crop strategy | one row: label + current policy text + vanilla dropdown; rotation (localized crop names) or fixed crop row |
| Output | occupied/blueprint capacity (bar), output port, pipe/logistics/power port states, output-full warning |
| Actions | Refill water / Perform maintenance vanilla buttons, inline disabled reason under each, one-line action result |

Every row is "icon + localized label + localized value" (or a progress bar carrying the value+unit).
Warnings carry an icon **and** a text marker (`▲`/`!`/`•`), so colour is never the only signal.

## Data path

* `ShowFragment(entity)` resolves the `EntityComponent` runtime id and asks the Core query
  contract for the `IndoorFarmSnapshot` DTO; the header avatar is read **once** from the vanilla
  `EntityBadgeService` (the building badge already carries the tier icon — the only game-object
  read, and it is not simulation state).
* The gameplay singleton bound to `IBoxFarmQuery` exposes the v2 read model; the singleton bound
  to `IBoxFarmCommand` exposes the Core write actions. The two panel-only commands (manual refill,
  crop strategy) are additional public members resolved once by `HdbfIndoorCommandBridge` (UI may
  only reference HDBF.Core).
* The snapshot's water block is computed **live** by gameplay: `demand = P_eff × WaterPerSlotPerDay
  × modeWaterFactor`, `C = IndoorWaterMath.Coverage(tank, demand)`. A loaded full tank therefore
  shows `C 100 %` before the first daily tick instead of the stale `C 0 %`.
* `UpdateFragment()` re-queries at most **4 times per second** and rebuilds the cards only when a
  value fingerprint changed (the fingerprint includes a language probe, so a language switch
  re-localizes too). An idle panel does no formatting/allocation work.
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
| `Panels/HdbfIndoorPanelData.cs` | read model: header + ordered cards + rows + worker chips + crop choices + action availability |
| `Panels/HdbfIndoorPanelFactory*.cs` | snapshot -> cards/rows (header, worker roster, seven cards, actions) |
| `Panels/HdbfIndoorPanelWarnings.cs` | warning rows per card (live water rule), crop dropdown choices, severity markers |
| `Panels/HdbfIndoorPanelFormat.cs` | template formatting, enum -> key map, material lookup, failure whitelist |
| `Panels/HdbfIndoorPanelStyle.cs` | the vanilla USS class names / icon sprite keys / tier icon assets the renderer uses |
| `Panels/HdbfIndoorPanelLimits.cs` | safe bar ratios + the output capacity mirrored from the blueprints |
| `Panels/HdbfIndoorCommandBridge.cs` | one-time member resolution of the gameplay query/command singleton |

These files reference `HDBF.Core` and `HdbfUiLocKeys` only, so `V2PanelContractTests` compiles them
into the L1 runner and drives the real mapping (card order, every row, live-coverage rule,
no-braces rule, failure reasons, output capacity vs blueprints, the 4 Hz/fingerprint gate).

## Localization

`Localization/HdbfUiLocKeys.cs` holds one compile-time constant per key the panel formats; every
constant must exist verbatim in `data/Localizations/enUS.csv`, `zhCN.csv` and `zhTW.csv` (identical
185-key sets). `V2LocalizationTests` pins the parity, the placeholder match, the row-label lengths,
the no-empty-brace rule, the complete v2 terminology set and the removal of every v1 plot term.

## Known gaps / deviations (honest list)

1. **Output capacity is not in the snapshot DTO.** `HDBF.Panel.OutputStock` shows occupied/capacity,
   where capacity is `HdbfIndoorPanelLimits.OutputCapacity` (50) mirrored from the eight shipped
   `SimpleOutputInventorySpec`s; `TestOutputCapacityMatchesEveryShippedBlueprint` fails if the data
   drifts. Exposing it in the DTO would remove the constant.
2. **Worker names are not in the snapshot.** The roster chips show `—` for staffed positions plus a
   localized note; the roster is read-only (the game assigns workers).
3. **Header avatar is the one game-object read.** It comes from the vanilla `EntityBadgeService`
   (the badge of the selected building), because mod sprites cannot be loaded through Unity
   `Resources`; it is read once per selection and every failure degrades to the vanilla frame.
4. **The generated panel uses the shipped game UXML templates** (`DebugButton`, `WorkerView`,
   `Core/Dropdown`, `Core/RadioButton`, `Core/ProgressBar`) and the shipped USS classes; these are
   game assets, not copied into the mod package.
5. **In-game look/feel is compile-verified only.** The L1 suite cannot load UIElements, and this
   round was not allowed to launch the game; TC-V2-05/06 still needs a screenshot round.
