# UI workstream report — interactive entity panel + write contract + localization (task-3)

Status: **implemented and compile-verified offline; in-game behaviour not yet executed.**
Owner: `ui-panel`. Date: 2026-10-24. Requirement source: chapter 14 (all), REQ-UI-05/06/07/08/09.

| Evidence type | Result |
|---|---|
| `dotnet build src\HDBF.Core\HDBF.Core.csproj -c Release -m:1` | **0 warnings, 0 errors** |
| `dotnet build src\HDBF.UI\HDBF.UI.csproj -c Release -m:1` | **0 warnings, 0 errors** |
| Localization consistency script (see §6) | **OK** — 83 keys in each of enUS/zhCN/zhTW, identical sets; 68 UI constants all present; enUS/zhCN placeholders identical; no raw `HDBF.*` literal outside `HdbfUiLocKeys.cs` |
| Assembly reference graph (reflection on the built DLLs) | `HDBF.UI` references **HDBF.Core only** (no `HDBF.Gameplay`); `HDBF.Core` references `netstandard` only |
| In-game run | **not performed** (Lead owns full build/deploy; see §7 checklist) |

Files changed (only owned paths):

* `src/HDBF.Core/Abstractions/IBoxFarmCommand.cs` — **new**, the only Core file touched.
* `src/HDBF.UI/Localization/HdbfUiLocKeys.cs` — 23 new UI key constants.
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs` — rewritten: interactive controls, command isolation, result line.
* `src/HDBF.UI/Panels/HdbfFarmPanelData.cs` — raw mode/port/maintenance state + localization/formating helpers.
* `src/HDBF.UI/README.md` — behaviour, key inventory, known-gap list, manual checklist updated.
* `data/Localizations/enUS.csv`, `zhCN.csv`, `zhTW.csv` — 20 new keys each; 1 key text changed (identical key sets).
* `docs/workstreams/ui-panel.md` — this report.

Not touched: `src/HDBF.Gameplay/**`, other `src/HDBF.Core/**` files, `data/Buildings/**`, `data/Research/**`,
`tests/**`, `tools/**`, `build.ps1`, `Directory.Build.*`, `docs/INTERFACES.md`, other workstream reports.
No `.csproj` change was needed (HDBF.UI still references HDBF.Core only; `IHdbfLog`/UIElements/
Localization were already referenced).

---

## 1. Write-side contract (exact, frozen)

File: `src/HDBF.Core/Abstractions/IBoxFarmCommand.cs`, namespace `HDBF.Core.Abstractions`.
Pure .NET (enums + strings): gameplay can be unit tested against a fake without Unity.

```csharp
public interface IBoxFarmCommand
{
    bool TrySetMode(Guid entityId, HdbfMode mode, out string? failureKey);
    bool TryPerformMaintenance(Guid entityId, out string? failureKey);
    bool TrySetPort(Guid entityId, HdbfPortKind port, bool connected, out string? failureKey);
    bool TryExcludePlot(Guid entityId, int plotId, out string? failureKey);
    bool TryIncludePlot(Guid entityId, int plotId, out string? failureKey);
}

public static class HdbfCommandFailures
{
    public const string NotABoxFarm          = "HDBF.Error.NotBoxFarm";
    public const string MissingMaterials     = "HDBF.Error.MaterialsMissing";
    public const string ModeCooldown         = "HDBF.Mode.SwitchCooldown";
    public const string DenseDisabled        = "HDBF.Error.DenseDisabled";
    public const string MissingMod           = "HDBF.Error.MissingMod";
    public const string PortNotAvailable     = "HDBF.Port.NotApplicable";
    public const string PortManagedByNetwork = "HDBF.Port.PowerManaged";
    public const string PlotMinimum          = "HDBF.Error.PlotMinimum";
    public const string PlotNotRegistered    = "HDBF.Error.PlotNotRegistered";
    public const string Unknown              = "HDBF.Error.ActionFailed";
}
```

Contract rules written into the XML docs (binding for gameplay):

1. `entityId` is the **runtime** entity id (`EntityComponent.EntityId`, the same Guid
   `IBoxFarmQuery` uses), never the save-file id.
2. `true` = the action applied completely; `failureKey` is ignored.
3. `false` = **nothing changed** ("整单回滚", chapter 14.3) and `failureKey` must be non-null and one
   of `HdbfCommandFailures` (fixed sentence, **no `{0}` placeholder** — the UI receives only the key).
4. Main thread only; player input, missing materials, cooldown, wrong entity must return `false`,
   never throw.
5. Plot calls never remove the last registered plot and never interrupt an in-flight harvest
   (implementation obligation of REQ-UI-05, documented on the interface).

**Deviation from the task text, deliberate and announced to the Lead before implementation
finished:** the task sketch had `out string? failureKey` only on `TryPerformMaintenance`; I added it
to all five methods because chapter 14.3 requires a *specific* failure reason for every action
(缺材料 / 缺模组 / 冷却中 / 注册格不足), including mode cooldown and port-mod-missing. Method names,
order and the Guid-first argument stay exactly as specified.

## 2. Wiring required from the Lead (gameplay side, not done here)

1. `HdbfFarmQueryService` (it already owns the `Guid → IHdbfBoxFarmSnapshotSource` table) implements
   `IBoxFarmCommand`: look the source up, cast to `HdbfBoxFarmController` (same assembly), delegate to
   the controller's existing `TrySetMode` / `TryPerformMaintenance` / `TryExcludePlot` /
   `TryIncludePlot` / `TrySetPortConnected`, and map every rejection to a `HdbfCommandFailures` key.
   The controller currently returns plain `bool`, so cooldown vs dense-disabled must be
   distinguished (add an out-reason or a small state query in gameplay) to give a useful message.
2. `HdbfGameConfigurator.Configure()`: add
   `Bind<IBoxFarmCommand>().ToExisting<HdbfFarmQueryService>();` (single `Bind`; a duplicate binding
   throws `Bindito.Core.BinditoException`).
3. **Mandatory before the mod is playable:** `HdbfFarmPanelFragment` now constructor-injects
   `IBoxFarmCommand`. Without the binding, the Game container cannot construct the fragment and the
   entity panel fails. Compilation of HDBF.UI does not prove the binding exists.
4. The maintenance path is fully functional only after inventory deduction is wired (v0.1 gameplay
   gap): until then `TryPerformMaintenance` returns true and the UI shows the success message; once
   materials are required it must return `false` + `HdbfCommandFailures.MissingMaterials`.
5. Please add the interface to `docs/INTERFACES.md` (Lead-owned file).

## 3. Panel interaction implementation (chapter 14.1/14.3/14.5)

`HdbfFarmPanelFragment` (code-only UIElements, no UXML/USS/prefab; asset-free by design):

* **Mode row** (REQ-UI-06): `Normal` / `Dense Planting` / `Fallow` `Button`s. The active mode is the
  disabled segment; the other two stay operable. Every button carries a localized cost tooltip
  (`HDBF.Tooltip.ModeNormal|ModeDense|ModeFallow`, Dense = yield ×1.35 / water ×1.5 / fatigue ×1.5,
  Fallow = production stops) and the active one gets `HDBF.Tooltip.CurrentMode`.
  **Dense is two-step**: first click arms the button (text becomes `HDBF.Action.ConfirmDense`, result
  line shows `HDBF.Action.DenseConfirmHint`) and changes nothing; a second Dense click or clicking
  another mode executes. Switching while on cooldown shows `HDBF.Mode.SwitchCooldown` returned by
  the command (no local guessing).
* **Maintenance button** (REQ-UI-07): calls `TryPerformMaintenance`. Disabled with
  `HDBF.Panel.MaintenanceNone` ("no material upkeep") on the irrigation-only Basic tier; the
  countdown row already shows the same reason, so the disabled reason is visible text, not only a
  tooltip. While shutdown is active (P0) the button is **bold**, i.e. rendered as the primary action.
  Success → `HDBF.Action.MaintenanceDone`; failure → the localized `failureKey`
  (`HDBF.Error.MaterialsMissing` once inventory is wired).
* **Port toggles** (REQ-UI-07, chapter 8.3): one `Toggle` per port plus a **state label**
  (`Connected` / `Not Connected` / `Disabled` / `Not Applicable`) so state is never colour-only
  (chapter 14.5 triple encoding). `Hidden` (tier has no port) and `Fault` disable the toggle.
  Pipe/logistics toggle calls `TrySetPort(...)`; power is also a toggle but gameplay v0.1 rejects it
  with `HDBF.Port.PowerManaged` (the mechanical network owns it). A rejected switch is **rolled back**
  on the immediate re-read of the authoritative snapshot — no optimistic UI.
* **Result line** under the action button: last localized success/failure text. The failure key is
  whitelisted in `HdbfFarmPanelDataFactory.FormatCommandFailure`; an unexpected/null key degrades to
  `HDBF.Error.ActionFailed`, so a raw key can never reach the player (REQ-UI-08/09).
* **Keyboard**: `Button`/`Toggle` are UIElements focusable controls and register
  `NavigationSubmit`, so Tab/Shift+Tab/Enter work with the game's default focus ring; the fragment
  sets no custom focus styling (no USS asset). Verified from the game assembly IL:
  `Button::.ctor` sets `focusable = true`, `BaseField<T>::.ctor` sets `focusable`/`delegatesFocus`
  and `BaseBoolField` registers `NavigationSubmit`.
* **Refresh/perf unchanged** (REQ-UI-12): quarter-second cap, dirty fingerprint (mode, ports,
  shutdown already included), same-values no re-format. Command clicks trigger one forced refresh.
* **Safety hardened** (REQ-SAFE-01): the whole refresh (query + format + render) is now inside one
  try/catch that hides the fragment and logs `ui.render`; every command callback catches, shows
  `HDBF.Error.ActionFailed`, and logs `ui.command-exception` / `ui.command-rejected` through the
  injected `IHdbfLog` (30 s throttle per key). Before this change only the query call was guarded.
* Non-HDBF entities still hide the fragment (`display: none`), and a missing/throwing snapshot hides
  it — no rows leak onto other buildings.

Pure helpers added to `HdbfFarmPanelDataFactory` (game-free, unit-testable): `IsPortToggleEnabled`,
`IsPortConnected`, `ModeTooltip`, `PortTooltip`, `MaintenanceTooltip`, `FormatCommandFailure`.
`HdbfFarmPanelData` gained raw `ModeValue`, `IsShutdown`, `CanPerformMaintenance`,
`PipePortState`/`PowerPortState`/`LogisticsPortState` (raw `HdbfPortState` values); existing fields
and the warning-row contract are unchanged.

## 4. Disabled states that are honestly possible — and the ones that are not

`BoxFarmSnapshot` has no mode-cooldown field, no maintenance-material field and no per-port adapter
health, and this workstream was not allowed to extend `BoxFarmSnapshot.cs`. Therefore:

* **Disabled now** (from snapshot facts): active mode segment; maintenance on the irrigation-only
  tier; ports in `Hidden` and `Fault`.
* **Reason shown after the click instead** (needs new read-model fields to pre-disable): mode
  cooldown, missing maintenance material, missing compatibility mod for a port. The contract returns
  the exact key, and the result line renders it.

This is recorded as known gaps 9/11 in `src/HDBF.UI/README.md`, not silently dropped.

## 5. Localization (REQ-UI-08/09)

Three files carry **identical key sets: 83 keys each** (was 63 keys after the previous baseline;
all three files get the same 20 new keys). Placeholders in enUS/zhCN are identical (validator rule).
Chinese labels are within the chapter-14.4 limits (≤12 chars); English button/tooltip strings are
within 24 chars except the canonical `HDBF.Action.Maintenance` = "Perform Maintenance" (the
requirement's own table text, kept verbatim). `zhTW` rows were authored in Traditional Chinese
(the baseline file carried English text; key set is what the validator/build enforces).

New keys (present in enUS + zhCN + zhTW):

| Key | enUS | zhCN | Purpose |
|---|---|---|---|
| `HDBF.Action.ConfirmDense` | Confirm Dense | 确认密植 | armed Dense button label |
| `HDBF.Action.DenseConfirmHint` | Click Dense again to confirm | 再次点击密植以确认 | two-step hint in result line |
| `HDBF.Action.MaintenanceDone` | Maintenance performed. | 维护完成 | success feedback |
| `HDBF.Action.ModeChanged` | Mode changed. | 模式已切换 | success feedback |
| `HDBF.Action.PortUpdated` | Port updated. | 端口已更新 | success feedback |
| `HDBF.Error.ActionFailed` | The action could not be completed. | 操作未能完成 | command failure fallback |
| `HDBF.Error.DenseDisabled` | Dense planting is disabled by the mod configuration. | 模组配置已禁用密植 | dense feature off |
| `HDBF.Error.MaterialsMissing` | Not enough maintenance materials. | 维护材料不足 | maintenance failure |
| `HDBF.Error.MissingMod` | The required compatibility mod is not detected. | 未检测到所需兼容模组 | port failure |
| `HDBF.Error.NotBoxFarm` | This building is not a High-Density Box Farm. | 该建筑不是高密度盒子农场 | stale entity id |
| `HDBF.Error.PlotMinimum` | At least one managed plot must remain. | 至少保留一个托管格 | plot exclusion limit |
| `HDBF.Error.PlotNotRegistered` | That plot is not managed by this farm. | 该托管格不属于此农场 | plot include failure |
| `HDBF.Port.PowerManaged` | The power port follows the mechanical power network. | 动力接口由电网自动管理 | power port failure |
| `HDBF.Tooltip.CurrentMode` | This is the current mode. | 当前模式 | active mode tooltip |
| `HDBF.Tooltip.Maintenance` | Consumes upkeep material and resets the maintenance timer. | 消耗维护材料并重置维护倒计时 | maintenance tooltip |
| `HDBF.Tooltip.MaintenanceShutdown` | The farm is shut down; maintenance restores full output. | 农场已停摆，执行维护可恢复全部产出 | shutdown tooltip |
| `HDBF.Tooltip.ModeDense` | Dense planting: yield ×1.35; water ×1.5; fatigue ×1.5. Click again to confirm. | 密植：产量 ×1.35，水耗 ×1.5，疲劳 ×1.5，再次点击确认 | Dense cost preview |
| `HDBF.Tooltip.ModeFallow` | Fallow: production stops while the soil recovers. | 休耕：停止产出，让土壤恢复 | Fallow cost preview |
| `HDBF.Tooltip.ModeNormal` | Normal planting: no yield, water or fatigue change. | 常规种植：产量、水耗与疲劳不变 | Normal tooltip |
| `HDBF.Tooltip.PortPower` | The power port connects through the mechanical power network. | 动力接口通过机械电网自动连接 | power port tooltip |
| `HDBF.Tooltip.PortToggle` | Toggle {0}. | 切换{0} | pipe/logistics tooltip |

Changed existing key: `HDBF.Error.MaterialsMissing` lost its `{0}` placeholder
(enUS "Missing materials: {0}" → "Not enough maintenance materials."; zhCN "材料不足：{0}" →
"维护材料不足") because the command contract carries no numeric payload. No other existing text was
modified. All 23 new UI constants are cross-checked against the CSVs; the duplicated values between
`HdbfCommandFailures` (Core) and `HdbfUiLocKeys` (UI) are intentional and documented in both files.

## 6. Reproducible checks

```powershell
cd C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm
dotnet build src\HDBF.Core\HDBF.Core.csproj -c Release -m:1
dotnet build src\HDBF.UI\HDBF.UI.csproj -c Release -m:1
# localization: 83-key identity across 3 files, UI constants present, placeholders match, no raw keys
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.hdbf\work\check-loc.ps1"
# assembly graph: HDBF.UI must reference HDBF.Core only, HDBF.Core must be game-free
$m = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom("$env:USERPROFILE\.hdbf\work\bin\HDBF.UI\Release\netstandard2.1\HDBF.UI.dll")
$m.GetReferencedAssemblies().Name
[System.Reflection.Assembly]::ReflectionOnlyLoadFrom("$env:USERPROFILE\.hdbf\work\bin\HDBF.Core\Release\netstandard2.1\HDBF.Core.dll").GetReferencedAssemblies().Name
```

Observed result: UI references `Bindito.Core, HDBF.Core, netstandard, Timberborn.BaseComponentSystem,
Timberborn.Common, Timberborn.EntityPanelSystem, Timberborn.EntitySystem, Timberborn.Localization,
UnityEngine.CoreModule, UnityEngine.TextRenderingModule, UnityEngine.UIElementsModule`; Core
references `netstandard` only.

The check script lives outside the workspace on purpose (the workspace must stay build-artifact-free);
it exits 0 with `LOCALIZATION CHECK: OK`, otherwise prints every failure with `exit 1`.

## 7. In-game verification steps (must be run by the Lead after wiring)

Precondition: gameplay binds `IBoxFarmCommand` (`Bind<IBoxFarmCommand>().ToExisting<HdbfFarmQueryService>();`)
and `build.ps1 -Install` deploys all five assemblies; the mod is enabled.

1. Load a save and select a box farm: no `BinditoException`/missing-binding error in `Player.log`,
   the panel appears with the interactive rows. Select a vanilla building: no extra rows.
2. Mode: click Dense once → button reads "Confirm Dense", nothing changes yet; click again → mode
   switches and the active segment moves; set Dense again within the same game day → cooldown reason
   appears in the result line. Tooltips show the three cost previews.
3. Maintenance: Basic tier → button disabled + "no material upkeep" visible in the countdown row.
   Intermediate/Advanced/Top → after two missed cycles (shutdown, P0 red row, bold button) click
   maintenance → countdown resets and "Maintenance performed." appears. Once inventory deduction is
   wired, remove the material and confirm "Not enough maintenance materials." appears and nothing
   is deducted/reset.
4. Ports: a tier without a port shows disabled toggle + "Not Applicable". Toggle pipe/logistics →
   checkbox and state text follow; disconnect the port → state rolls back to the snapshot.
   Power toggle on Top → "The power port follows the mechanical power network." (v0.1 gameplay).
5. Keyboard: Tab through mode buttons → port toggles → maintenance; Enter activates; focus ring is
   the game default; visual order matches Tab order.
6. Chinese: close the panel, switch to zhCN, select the building → every row/button/state/tooltip is
   Chinese with no `HDBF.` text. (A language switch while the panel is open re-localizes on the next
   fingerprint change — known gap 13.)
7. Non-HDBF and deleted entities: select another building while a box farm is selected, then delete
   a box farm while selected → panel hides, no exception in `Player.log`.

## 8. Requirement traceability (UI scope only)

| Requirement | Status |
|---|---|
| REQ-UI-05 managed-plot interaction | **partial**: contract (`TryExcludePlot`/`TryIncludePlot`) ships; highlight/click/box-select UI is a rendering feature not implemented here (known gap 1/4) |
| REQ-UI-06 mode switching | **implemented (offline-verified)**: 3 modes, cost preview, Dense two-step confirm, cooldown reason via contract; cooldown cannot pre-disable (gap 9) |
| REQ-UI-07 maintenance + ports | **implemented (offline-verified)**: disabled states + tooltips, per-action failure reason, rollback on reject; material deduction/inventory is the Lead's gameplay wiring |
| REQ-UI-08 key discipline | **implemented**: all visible text through `HDBF.*` constants; no raw key literal outside `HdbfUiLocKeys.cs`; whitelisted failure keys |
| REQ-UI-09 en/zh consistency | **verified offline** for enUS/zhCN (identical 83-key sets, identical placeholders) and zhTW key set; missing-key English fallback/throttled log is ILoc/gameplay behaviour, not exercised here |
| REQ-UI-12 UI perf | **preserved**: 4 Hz cap, dirty fingerprint, no per-frame work in click handlers |
| REQ-UI-10/11 accessibility/format | **preserved**: game-default focus ring and control styling (no USS), text+state label alongside the checkbox, formatting code untouched |

## 9. Open risks / notes for other workstreams

* **Highest risk: a full build+deploy without the gameplay binding** makes the entity panel throw at
  container creation. Compile is green either way; only an in-game/`Player.log` check proves it.
* `verify`: the pure helpers (`FormatCommandFailure`, `IsPortToggleEnabled`, `ModeTooltip`,
  `PortTooltip`, `MaintenanceTooltip`) and a fake `IBoxFarmCommand`/`IBoxFarmQuery` can be covered by
  L1 tests without Unity; `HDBF.UI` itself cannot load outside the game (UIElements/game references).
* `tech-data`: no data/blueprint change was needed; the new keys are additive and the four tier
  name/description keys are untouched.
* `Lead`: when wiring the command service, map the controller rejections to the documented keys
  (see §2.4); otherwise the UI still compiles but shows the generic "action failed" text.
