# HDBF.UI — v2 indoor farm entity panel

This assembly owns everything the player sees for a v2 indoor box farm: the right-hand entity
panel fragment, its warning rows and the localization keys the UI formats. It renders only the
`HDBF.Core.Indoor.IndoorFarmSnapshot` DTO (docs/v2/INTERFACES.md section 9); no simulation state
is read from a game component.

Dependency rule (docs/v2/ARCHITECTURE.md section 1): `HDBF.UI` references **HDBF.Core only**.
The game-context configurator binds the gameplay implementations to the Core query/command
interfaces; this assembly never compiles against `HDBF.Gameplay`.

## Panel blocks (chapter 19.11)

| Block | Rows / controls |
|---|---|
| Status | up to five warning rows (water shortage, maintenance overdue, power cut, blocked entrance, more) plus the concrete "why nothing happens" problem rows |
| Header | tier, faction, `W workers/max`, mode buttons (normal / dense / fallow), one-day mode transition sentence |
| Slots | configured/capacity, `P_eff` (tooltip carries the formula), empty, growing, ready to harvest, action backlog |
| Water | tank units + capacity + days (`2.4/10.8 units (about 2.0 days)`), coverage `C`, `C_min`, supply kind (pipe/manual/none), shortage derate, **Refill water** button |
| Maintenance | due day, overdue cycles, missing material + count, **Perform maintenance** button |
| Power | grid / off-grid / power cut / not required (top tier focus) |
| Crop strategy | rotation (default) / fixed crop via dropdown, current rotation sequence |
| Output | output stock, output port state, pipe/logistics/power port states |
| Result line | localized success or the specific failure reason of the last action |

There is **no** world-cell surface in this assembly: no rescan, no managed-cell legend, no
free-select, no world-cell icon toggle. The panel always shows values as text; colour is never
the only information carrier (the warning rows carry a text marker as well).

Empty or not-yet-known data renders as `—` through `HDBF.Core.Formatting.PanelNumbers`; the
fragment never shows a raw localization key or a `NaN`/`Infinity`.

## Data path

* `ShowFragment(entity)` resolves the `EntityComponent` runtime id and asks the Core query
  contract for the `IndoorFarmSnapshot` DTO.
* The gameplay singleton bound to `IBoxFarmQuery` exposes the v2 read model
  (`GetIndoorSnapshot`); the singleton bound to `IBoxFarmCommand` exposes the Core write actions.
  The two panel-only commands (manual refill, crop strategy) are additional public members of the
  command singleton: because this assembly may only reference `HDBF.Core`, the
  `HdbfIndoorCommandBridge` resolves exactly those two members **once** by name and caches the
  `MethodInfo`; every call is isolated and degrades to "no data" / generic failure. All payloads
  are Core DTOs (`IndoorFarmSnapshot`, `CropPolicy`); no gameplay implementation type is named.
* `UpdateFragment()` re-queries at most **4 times per second** (`RefreshIntervalSeconds = 0.25f`,
  REQ-V2-UI-01) and re-renders only when a value fingerprint changed, so an idle panel does not
  re-format or re-allocate its labels.
* Actions: mode switch and maintenance go through the Core `IBoxFarmCommand`; crop strategy and
  manual refill go through the two panel-only members on the same command singleton. Every rejection shows
  the specific localized reason returned by gameplay (REQ-V2-UI-02: no worker, blocked entrance,
  no/unreachable/contaminated water, empty action budget, pending trip, full output, missing
  material, invalid crop); rejected actions keep the authoritative snapshot values, never an
  optimistic local state.
* A missing snapshot, a throwing query or a rendering failure hides the fragment and logs a
  throttled `ui.*` error instead of propagating an exception into the entity panel
  (REQ-V2-SAFE-01).

## Game-free half (linked into the L1 suite)

`Panels/HdbfIndoor*.cs` contains the map from the DTO to localized strings:

| File | Responsibility |
|---|---|
| `HdbfIndoorPanelData.cs` | read model (rows, warnings, problems, crop choices) |
| `HdbfIndoorPanelFactory.cs` | snapshot -> read model, block by block |
| `HdbfIndoorPanelFormat.cs` | template formatting, enum -> key map, material lookup, failure whitelist |
| `HdbfIndoorPanelWarnings.cs` | four v2 warning rows, concrete problem rows, dropdown choices |
| `HdbfIndoorCommandBridge.cs` | one-time member resolution of the gameplay query singleton |

These files reference `HDBF.Core` and `HdbfUiLocKeys` only, so `V2PanelContractTests` compiles
them into the L1 runner and drives the real mapping (every snapshot field, every failure reason,
the `—` rule, the four warning categories). The game-dependent fragment partials
(`HdbfFarmPanelFragment*.cs`) are not linked and are checked at the source level instead.

## Localization

`Localization/HdbfUiLocKeys.cs` holds one compile-time constant per key the panel formats; every
constant must exist verbatim in `data/Localizations/enUS.csv`, `zhCN.csv` and `zhTW.csv` (the
three files carry identical key sets). `V2LocalizationTests` pins the parity, the placeholder
match, the complete v2 terminology set (slots, tank, refill, crop policy, rotation, fixed crop,
coverage, `P_eff`, backlog, maintenance, grid/off-grid/power cut) and the removal of every v1
field term from the shipped texts.

## Known gaps / deviations (honest list)

1. **Reflection bridge for the two panel-only commands.** The v2 read model and the
   mode/maintenance/port commands are compile-time Core interfaces; only the refill and crop-policy
   commands live on the gameplay implementation, while this assembly may not reference
   `HDBF.Gameplay`. The bridge is the documented seam for those two members; it resolves them once,
   never per frame, and fails safe.
2. **Maintenance material is derived from the tier profile, not carried by the DTO.** The snapshot
   only has a "material missing" flag, so the panel derives `Plank x8` / `metal block x15` from
   `TierCatalog` + the power state. Adding the material id/count to the Core DTO would remove the
   derivation.
3. **World-icon keys are the v2 sentences.** The four icon classes map straight from
   `IndoorWarningFlags` through `HDBF.Core.Indoor.IndoorWarningMap`; the hover tooltip reuses the
   shipped `HDBF.Warning.*` sentences (no v1 warning/aggregation framework remains).
4. **Manual refill is a queued request.** The button validates a real source and the daily action
   budget, then the next daily tick performs the trip and spends the two farm actions.
5. **UI assembly cannot run the game itself.** The panel is compile-verified and its game-free
   mapping is covered by L1; the in-game look/feel still needs TC-V2-05/06 verification.
