# HDBF v2.5.6 第三維修計畫（BUG_REPAIR_20261102 剩餘 pending 收尾）

> 依據：`docs/BUG_REPAIR_20261102.md` §4/§6/§10/§11 仍標 pending 的項目（第一輪 v2.5.4、第二輪 v2.5.5 已完成可離線者）。
> 本輪目標：**把「不需要實機、不需要外部模組」的 pending 全部落地**；需要實機會話或 Emberpelts 主模組者維持 pending。
> 驗收：5 組件 0 warning/0 error、L1 `FAILED 0`、`tools/validate RESULT: OK`、44 gates PASSED。

## 1. 範圍

| 項目 | 對應 bug | 本輪 |
|---|---|---|
| 3-strike 時間窗＋單農場失敗只降級 | L-02(c) | ✅ |
| 管道 probe 誠實區分「聚落級供水」＋農場 anchor probe | WP-05 / L-02(b) | ✅（抽水行為不變；per-farm 判定仍 pending 實機） |
| 世界圖示與面板共用 live 警告旗標、NoWorkers P1 | L-11 / WP-10 / L-10 | ✅（實機 pending） |
| 圖示 tooltip 固定本地化句子 | L-17 剩餘 | ✅（語言切換需下次註冊） |
| 水流量/水源探測/圖示 pass 幀節流 | L-14 離線部分 | ✅（100 棟量測 pending） |
| config 5 個死旗標接上消費者 | L-18 | ✅ |
| 藍圖模板名/佔格歷史契約＋validator 硬檢查 | L-16 | ✅ |
| ADR-V2-004 / REQ-V2-WATER-02 漂移修訂 | L-19 | ✅（文件） |
| 啟動唯讀行為補丁診斷 | WP-07 / RE-03 / L-20 | ✅（probe；玩家隔離流程仍為客服文件） |
| 一鍵為全部農場補水 | WP-08 剩餘 | ✅（不做自動多趟） |
| keyed bridge 與 L-04 寫入策略統一 | L-04 剩餘 | ✅ |
| 自訂陣營（Ember-pelts M0/M1） | WP-13 / RE-06 | ⏸ **受外部模組阻塞**（見 §4） |
| 實機 fixture A–K、100 棟 Profiler | §7.2 / L-14 | ⏸ 本環境無可互動遊戲會話 |

## 2. 檔案所有權（本輪實際）

- **T1 `compat-water`**：`src/HDBF.Core/Compat/**`、`Indoor/IIndoorPipeProbe.cs`、
  `Integration/Adapters/TimberPipes*.cs`、`IndoorAdapterHost.cs`、`ReflectionCompatAdapter.cs`（Lead 批准）、
  `Gameplay/Components/HdbfBoxFarmController.{WaterFlow,WaterSupply}.cs`、`HdbfManualWaterSource.Search.cs`、
  `Persistence/Indoor/IndoorEntitySaveBridge.Keyed.cs`＋測試。
- **T2 `state-icons`**：`Core/Indoor/{IndoorWarnings,IndoorFarmSnapshot}.cs`、
  `Gameplay/Components/HdbfWorldIconService*.cs`、`HdbfBoxFarmController.IndoorSnapshot.cs`、
  `UI/Panels/**`＋測試。
- **T3 `config-data`**：`Core/Indoor/{IndoorDayTick,IndoorGrowthMath,IndoorLaborMath*,IndoorDayContracts}.cs`、
  `Core/Data/BlueprintHistoryContract.cs`、`Gameplay/Components/HdbfBoxFarmController.*`（feature 接線）、
  `tools/validate/**`、藍圖歷史檔＋測試。
- **Lead**：loc keys（拆 partial）與三語 CSV、log keys、`tests/HDBF.Tests.csproj` linked Compile、
  `IBoxFarmCommand`/`HdbfFarmCommandService`/query/bridge/fragment（WP-08）、`HdbfBehaviorProbe`（WP-07）、
  ADR/需求/VERIFICATION/CHANGELOG/BUG_REPAIR/README/manifest 版本、最終 gate。

## 3. 驗收結果

- `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-work"`
  → EXIT 0：5 組件 **0 warning / 0 error**、L1 **PASSED 712 / FAILED 0**（基線 672，+40 條）、
  `tools/validate RESULT: OK`（10 blueprints）、**RELEASE GATES: PASSED (44 checks)**、staged 2,141 KB。
- `git status` 只含本輪相關檔案；source 掃描無 Harmony 禁用字樣；每個 `.cs` 檔 ≤300 行。
- 文件：`VERIFICATION.md` §39、`CHANGELOG.md` [2.5.6]、`REQ_TRACEABILITY.md` Round-26、
  `BUG_REPAIR_20261102.md` §12、`ADRS.md`（ADR-V2-004 修訂）、README/manifest 2.5.6。

## 4. 仍 pending 的阻塞理由（誠實記錄）

- **WP-13 / RE-06 Ember-pelts**：本機無 Emberpelts 主模組（`3346318229`），無法確認其
  `FactionSpec`/材質/作物/水源；`.timbermesh` 是 zlib 壓縮二進位，中立材質變體需 Unity/timbermesh
  工具鏈重新匯出；盲目改材質名稱會觸發 `MaterialRepository` 失敗，依 §3.7 證據會讓整個建造選單載入
  失敗（比現況更糟）。因此不實作、不猜測，等主模組與工具鏈。
- **WP-05 per-farm 真實連通/端點**：改變抽水平衡且需 Timber Pipes 實機 API 取證；本輪只做誠實標示與
  唯讀 probe。
- **實機 fixture §7.2 A–K、L-14 100 棟 Profiler**：本環境沒有可互動遊戲會話。

## 5. 已知殘留

- `maintenance=false` 期間面板 Next due 顯示舊 due day（本輪不改 UI）。
- port 旗標重新啟用後玩家需在面板重開端口（可逆性對「玩家選擇」部分成立）。
- 世界圖示 tooltip 語言切換要等下一次註冊（vanilla 無可寫描述 API）。
- `MissingMaterial` 無 live 來源，live 旗標沿用日 tick bit；`OutputFull` live 判定粒度與日 tick backlog 有差異。
