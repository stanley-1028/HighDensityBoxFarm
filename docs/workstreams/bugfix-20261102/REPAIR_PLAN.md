# HDBF v2.5.4 維修計畫（BUG_REPAIR_20261102 實作卡）

> 依據：`docs/BUG_REPAIR_20261102.md`（玩家回饋 + 潛在 bug 調查，唯讀調查已完成）。
> 本輪目標：**修 P0 工作包（第 1 批）**＋同檔案範圍內的低風險 P1 bug；離線 L1／validator／44 gates 全綠。
> 遊戲實機驗證（fixture 回合）本輪**不做**（環境不可互動），相關項目標 `pending`。

## 1. 範圍（本輪要做／不做）

| WP | 內容 | 對應 bug | 本輪 |
|---|---|---|---|
| WP-03 | 休耕→常規不再清空成熟作物；零產量不清槽 | L-01 | ✅ 已完成（Lead，含 L1） |
| WP-01 | 維護「追平付費」：一次點擊付 k 期、dueDay += k×cycle；面板/tooltip 一致；未到期禁用（L-08） | RE-01, L-08 | ✅ |
| WP-02 | 自動維護開關（每棟持久化、預設 config true、付料與手動相同） | RE-01, RE-02, RE-07 | ✅ |
| WP-04 | 暫停殘留 reconcile（1 Hz singleton）＋撤出強化（UnreserveSlotAndExit／EntererAdded／eject-summary）＋拆除前撤出（L-13） | RE-04, L-13 | ✅ |
| WP-05 | 管道語義：面板顯示原因碼（NoPipeTank/NoPipeNetwork/TankDry/AdapterUnavailable/BindingFailed）＋文案；L-06 管口不被日 tick 覆寫 | RE-05, L-02, L-06 | ✅（真實連通判定 TryGetGraph 留 pending） |
| WP-06 | 閒置原因狀態列（Core 純函式）＋缺水三條路徑說明＋補水 headroom 修正（L-05）＋0 工人面板顯示「—」（L-10 UI 部分） | L-03, L-05, L-10, RE-01 UX | ✅（世界圖示多狀態 L-10/L-11 留 pending） |
| WP-07 | 崩潰唯讀診斷 probe | RE-03 | ⏸ pending（G-08 掃描禁止 source 含 Harmony 字串，需另案設計） |
| WP-09 | 維護材料可用性＝單一庫存可付整筆 | L-07 | ✅ |
| WP-12 / WP-13 / WP-14 | 作物清單、自訂陣營、物流/效能/backward 等 | L-04/09/12/14/15/16/18/19, RE-06 | ⏸ pending（非本輪 P0；見 bug 文件） |
| L-17 | 三語誤導/重複字串修正 | L-17 | ✅（字串層） |
| L-11 | 世界圖示與面板即時同步 | L-11 | ⏸ pending（需實機驗證 icon 行為） |

## 2. 檔案所有權（避免衝突；**只改自己名下的檔案**）

- **Lead**：`src/HDBF.Core/Indoor/IndoorGrowthMath.cs`、`IndoorLaborMath.Scheduling.cs`、`tests/HDBF.Tests/V2FallowTransitionTests.cs`、
  `src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs`、`HdbfBoxFarmController.Pause.cs`、`HdbfBoxFarmController.Pause.Eject.cs`、
  新增 `HdbfPauseReconcileService.cs`、`src/HDBF.Gameplay/HdbfGameConfigurator.cs`（只加 pause singleton 與 probe 註冊）、
  `tests/HDBF.Tests/V2PauseContractTests.cs`、`docs/**`、`data/manifest.json`、`README.md`。
- **T1 core-persistence**：`src/HDBF.Core/Indoor/*` 其餘檔（見 §3）＋`src/HDBF.Persistence/Indoor/IndoorEntitySaveBridge.cs`＋
  `data/HDBF_Config.json`＋`tools/validate/ConfigValidator.cs`＋Core/Persistence/Config 相關 tests。
- **T2 gameplay-maintenance**：`src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs`、`.Tick.cs`、`.IndoorSnapshot.cs`、
  `.ManualRefill.cs`、`HdbfFarmCommandService.cs`、`HdbfMaintenanceTransaction.cs`、`IndoorMaintenanceMaterialSource.cs`、
  `HdbfIndoorCommandFailures.cs`＋`tests/HDBF.Tests/V2MaintenancePowerTests.cs`（Gameplay 部分）、`V2GameplayContractTests.cs`、`V2GameplayWiringTests.cs`。
- **T3 integration-pipes**：`src/HDBF.Integration/Adapters/TimberPipesWaterSource.cs`、`TimberPipesWaterBinding*.cs`、`IndoorAdapterHost.cs`＋
  `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Context.cs`（僅 L-06 UpdatePorts）＋`tests/HDBF.Tests/V2AdapterHostContractTests.cs`。
- **T4 ui-localization**：`src/HDBF.UI/**`、`data/Localizations/{enUS,zhCN,zhTW}.csv`、`src/HDBF.Gameplay/Components/HdbfBoxFarmController.IndoorSnapshot.cs` 的
  **UI 取用面（不修改，只讀）**、`tests/HDBF.Tests/V2PanelContractTests.cs`、`V2PanelVisualContractTests.cs`、`V2LocalizationTests.cs`、
  `V2UiLiveWaterContractTests.cs`、`V2WarningIconContractTests.cs`、`V2NfrReleaseGateMatrixTests.cs`。

> 若需要修改不在自己名單的檔案，先用 `send_message` 問 Lead，不要直接改。

## 3. Core 契約（T1 產出；T2/T3/T4 依此寫碼）

### 3.1 `IndoorMaintenanceMath.cs`（新增，純函式）
```csharp
// 0 = 未到期；>=1 = 這次點擊要追平的期數
public static int CatchUpCycles(float dueDay, float today, float cycleDays);
// 飽和乘法；參數非正回 0
public static int CatchUpMaterialAmount(int amountPerCycle, int cycles);
// 追平帳單：tier/record 無材料維護或未到期 -> false
public static bool TryGetCatchUpBill(TierProfile? tier, IndoorFarmRecord? record, float today,
    out string? itemId, out int totalAmount, out int cycles, out float cycleDays);
// 以「本次需付的总材料」判斷缺料（面板/日 tick 都用這個）
public static bool IsMaterialMissingForCycles(IMaintenanceMaterialSource? materials, TierProfile tier,
    IndoorFarmRecord record, int cycles);
```
`IsMaterialMissing(...)` 保留原簽名（轉呼叫 cycles=1）。

### 3.2 `IndoorFarmRecord.cs`
```csharp
public bool AutoMaintenance { get; set; } = true;      // 持久化
public bool AutoMaintenanceSpecified { get; set; }     // runtime-only；不進存檔
```

### 3.3 `IndoorSaveCodec.cs`
`maintenance` 物件新增 `auto`（bool，放 `cyclesMissed` 後）。解碼：有 `auto` → 設值＋`AutoMaintenanceSpecified = true`；
缺 `auto` → 預設 true＋`Specified = false`＋note `maintenance.auto-default`。
`IndoorEntitySaveBridge.cs` 的 `Clone` 必須複製這兩個欄位（否則預寫驗證副本會把 false 洗回 true）。

### 3.4 `HdbfConfig.FeatureFlags`
`public bool AutoMaintenance { get; init; } = true;`；`HdbfConfigCodec.Sections.ReadFeatures` 讀 `autoMaintenance`；
`data/HDBF_Config.json` 加 `"autoMaintenance": true`；`tools/validate/ConfigValidator.cs` 的 required feature 清單加 `("autoMaintenance", true)`；
`tests/HDBF.Tests/ConfigCodecTests.cs` 加預設值斷言。

### 3.5 `IndoorLogKeys.cs`（新增常數，全部 indoor./water. 前綴）
`MaintenanceAuto="indoor.maintenance-auto"`、`MaintenanceAutoMissing="indoor.maintenance-auto-missing"`、
`PausedReconcile="indoor.paused.reconcile"`、`PausedResidualEnterer="indoor.paused.residual-enterer"`、
`PausedEjectSummary="indoor.paused.eject-summary"`、`PausedEjectFailed="indoor.paused.eject-failed"`、
`PausedForceRemove="indoor.paused.force-remove"`、`PausedAlreadyBlocked="indoor.paused.already-blocked"`、
`PipeProbe="water.pipe-probe"`。

### 3.6 `IndoorFarmSnapshot.cs`（用 `init` 屬性，**不改既有 positional 參數**，tests 才不會爆）
```csharp
public bool Paused { get; init; }
public bool AutoMaintenance { get; init; } = true;
public int MaintenanceCyclesToCatchUp { get; init; }
public int MaintenanceCatchUpMaterialAmount { get; init; }
public IndoorIdleReason IdleReason { get; init; } = IndoorIdleReason.None;
public bool ManualRefillSourceAvailable { get; init; }
public PipeProbe PipeProbeState { get; init; } = PipeProbe.Unknown;
```

### 3.7 新檔 `IndoorIdleReason.cs`（v2 header；純函式）
```csharp
public enum IndoorIdleReason { None=0, Paused=1, NoWorkers=2, EntranceBlocked=3, OutputFull=4,
    WaterShortage=5, MaintenanceStalled=6, MaintenanceDue=7, Fallow=8 }
public static class IndoorIdleReasonMath {
    // 優先序：Paused > NoWorkers > EntranceBlocked > OutputFull > WaterShortage >
    //          MaintenanceStalled > MaintenanceDue > Fallow > None
    public static IndoorIdleReason Resolve(IndoorFarmSnapshot? snapshot);
    // 回傳 "HDBF.IdleReason.<EnumName>"（None -> "HDBF.IdleReason.None"）
    public static string LocalizationKey(IndoorIdleReason reason);
}
```

### 3.8 新檔 `IIndoorPipeProbe.cs`（v2 header；Core/Indoor）
```csharp
public enum PipeProbeReason { Unknown=0, Connected=1, NoPipeNetwork=2, NoPipeTank=3, TankDry=4,
    AdapterUnavailable=5, BindingFailed=6, PortOff=7 }
public readonly record struct PipeProbe(PipeProbeReason Reason, int TankCount, int WaterTankCount, string Detail);
public interface IIndoorPipeProbe { PipeProbe ProbePipe(); }
```
（`PipeProbe.Unknown` 以 static expression-bodied property 提供，不得用 static mutable field。）

### 3.9 `IndoorWaterMath.cs` ＋ `IndoorConstants.cs`（L-05）
- `IndoorConstants.ManualRefillMinHeadroom = 1f;`
- `public static float ManualRefillDrawAmount(float tank, float tankCapacity);` = min(1/3 cap, headroom)；headroom < 1 單位 → 0。
- `IndoorDayContracts.ManualRefillOutcome` 加 `TankFull = 6`。
- `IndoorDayTick` 手動補水分支改用 `ManualRefillDrawAmount`；`TankFull` 不扣動作、不加水、不掛 NoWater。
- 維護缺料警告改用 `IsMaterialMissingForCycles(... cycles)`。

### 3.10 T1 必跑測試
`dotnet run --project tests/HDBF.Tests/HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=... -p:TimberbornDir=...`
→ 目標 `PASSED >= 616, FAILED 0`。新增：CatchUp matrix、save round-trip（auto true/false/缺欄位）、
`ManualRefillDrawAmount`/`TankFull`、`V2IdleReasonTests`。不要改 Lead 名下的兩個 Core 檔與 `V2FallowTransitionTests.cs`。

## 4. Gameplay 契約（T2）
1. `TryGetMaintenancePlan(out itemId, out amountPerCycle, out cycleDays, out cycles)`：`cycles = CatchUpCycles(...)`，
   未到期（0）→ false（失敗鍵 `HdbfIndoorCommandFailures.MaintenanceNotDue`）。
2. `TryApplyMaintenance(cycleDays, cycles, out failureKey)`：`nextDue = currentDue + cycleDays * cycles`；`cyclesMissed = 0`。
3. `HdbfFarmCommandService.TryPerformMaintenance`：plan → `_maintenance.TryConsumeNearest(..., amountPerCycle*cycles)` →
   apply；缺料失敗鍵不變。
4. `TrySetAutoMaintenance(Guid, bool, out failureKey)`：controller 寫 `record.AutoMaintenance=true／false`＋
   `AutoMaintenanceSpecified=true`＋`_saveDirty=true`＋log `indoor.auto-maintenance`。失敗鍵 `NotActive`/`Unknown`。
5. `RunDailyTick` Phase 0（在 `UpdatePower/UpdatePorts` 之後、context/`IndoorDayTick.Run` 之前）：
   - `if (!_record.AutoMaintenanceSpecified) _record.AutoMaintenance = SafeConfigAutoMaintenance();`
   - 若 `AutoMaintenance` 且 `record.MaintenanceDueDay <= today`：用 `TryGetCatchUpBill` 取帳單（未到期/免材料不動作）；
     `_materials.TryConsume(itemId,total)` 成功 → dueDay 前進、cycles=0、`_saveDirty`、log `indoor.maintenance-auto`；
     失敗 → log `indoor.maintenance-auto-missing`（needed/available）。
6. `IndoorMaintenanceMaterialSource.Available` 改成「**單一庫存最大 unreserved 量**」（與交易一致，L-07）。
7. `HdbfBoxFarmController.ManualRefill.cs`：立即補水只抽 `ManualRefillDrawAmount`；<=0 → `RefillTankFull`、不扣動作。
8. `HdbfBoxFarmController.IndoorSnapshot.cs`：填 §3.6 全部新欄位（`Paused=_paused`；catch-up 用 `today=ReadCurrentDay()`；
   `PipeProbeState = (_adapterHost as IIndoorPipeProbe)?.ProbePipe() ?? PipeProbe.Unknown`）。
9. `HdbfIndoorCommandFailures` 新增 `MaintenanceNotDue`/`RefillTankFull`（值＝`HDBF.Error.MaintenanceNotDue`/`HDBF.Error.RefillTankFull`）。
10. T2 必跑 tests 目標同上；不要改 T3/T4/Lead 的檔案。

## 5. Integration 契約（T3）
1. `TimberPipesWaterSource` 實作 `IIndoorPipeProbe.ProbePipe()`：health 非 Ok/Degraded → `AdapterUnavailable`；
   binding 失敗/異常 → `BindingFailed`；無任何 finished+graph 水塔 → `NoPipeNetwork`；有 graph 水塔但無合格加水塔 → `NoPipeTank`；
   合格水塔皆乾 → `TankDry`；否則 `Connected`。只讀，不抽水、不計三振。
2. `IndoorAdapterHost` 實作 `IIndoorPipeProbe`，委派給 source（source 為 null 時回 `AdapterUnavailable`）。
3. L-06：`HdbfBoxFarmController.Context.cs` 的 `UpdatePorts` **不再寫 `record.PipePort`/`record.LogisticsPort`**（玩家啟用選擇）；
   只保留/刷新 `PowerPort`（由 profile 決定），並在 adapter 健康狀態改變時寫一次 `indoor.port` log。`ReadPipeWater` 仍用 `record.PipePort`。
4. 測試：`V2AdapterHostContractTests` 增 `IIndoorPipeProbe`/四態契約；修掉因 UpdatePorts 行為改變而失敗的測試（只改自己名下）。

## 6. UI 契約（T4）
1. `HdbfUiLocKeys.cs`＋三語 CSV 同步新增（key 集合必須一致、無空字串）：
   `HDBF.Panel.IdleReasonLabel`、`HDBF.IdleReason.{None,Paused,NoWorkers,EntranceBlocked,OutputFull,WaterShortage,MaintenanceStalled,MaintenanceDue,Fallow}`、
   `HDBF.Panel.AutoMaintenanceLabel`、`HDBF.Panel.AutoMaintenanceOn`、`HDBF.Panel.AutoMaintenanceOff`、
   `HDBF.Tooltip.AutoMaintenance`、`HDBF.Panel.MaintenanceCatchUpValue`、`HDBF.Error.MaintenanceNotDue`、`HDBF.Error.RefillTankFull`、
   `HDBF.Port.NoPipeTank`、`HDBF.Port.NoPipeNetwork`、`HDBF.Port.TankDry`、`HDBF.Port.AdapterUnavailable`、`HDBF.Port.BindingFailed`、
   `HDBF.Panel.WaterPaths`、`HDBF.Tooltip.IdleReason`。
   並修正 L-17：`HDBF.Error.RefillUnavailable` 文案、`HDBF.Warning.AdapterFailed`/`HDBF.Warning.Shutdown` 重複、`HDBF.Panel.WaterShortage`
   （「減產」→「生長變慢」）、`HDBF.Tooltip.Maintenance`／`MaintenanceShutdown` 與追平行為一致；`HDBF.Port.Pipe` 改「管道管網（自動補水）」
   ＋`HDBF.Tooltip.PipeWater` 說明「只從接上管網的原版液體水塔抽水，接在農場外殼無效」。
2. `HdbfIndoorPanelData`：加 `IdleReasonText`、`IdleReasonTooltip`、`AutoMaintenance`、`AutoMaintenanceTooltip`、`WaterPathText`。
3. `HdbfIndoorPanelFactory`：第一張卡最上方加狀態列（`IdleReason`）；維護卡加「自動維護 On/Off」＋追平提示
   （`MaintenanceCyclesToCatchUp>0` 顯示 `MaintenanceCatchUpValue`）；`CanPerformMaintenance` = 未缺料 **且 cycles>0**；
   未到期時 reason＝`MaintenanceNotDue`；水卡加三條補水路徑列、`Workers<=0` 時 coverage/efficiency 顯示「—」；
   Output 卡的 pipe 列用 `PipeProbeState` 原因碼（`Connected`→Connected，其餘→對應原因鍵；`PortOff`→Disabled）。
4. `HdbfIndoorCommandBridge`：反射加 `TrySetAutoMaintenance(Guid,bool,out string)`；fragment 加 checkbox 事件並在成功/失敗後 `Refresh(force:true)`。
5. 測試：`V2PanelContractTests`/`V2PanelVisualContractTests`/`V2LocalizationTests` 更新（key parity、新控制項、原因文字）；
   `V2NfrReleaseGateMatrixTests` 若有 193 keys 硬編碼需改為動態。

## 7. 驗收（Lead 最終執行）
- `build.ps1`（完整）：5 組件 0 warning/0 error、L1 `FAILED 0`（≥616）、`tools/validate RESULT: OK`、44 gates PASSED。
- `git status` 只含本輪相關檔案；`git diff` 自查無 debug/cheat/Harmony 字串。
- 文件：`docs/BUG_REPAIR_20261102.md`（狀態）、`docs/v2/CHANGELOG.md`（[2.5.4]）、`docs/VERIFICATION.md`（離線回合 + pending 清單）、
  `docs/REQ_TRACEABILITY.md`（變更記錄）、`README.md`/`data/manifest.json` 版本。
- 每項未實機驗證者一律標 `pending`，不得寫成已完成。
