# HDBF v2.5.4 → 2.5.5 維修計畫（BUG_REPAIR_20261102 第二實作輪）

> 依據：`docs/BUG_REPAIR_20261102.md` §4/§6/§10 的 **pending** 清單（唯讀調查 + 第一實作輪已完成 WP-01/02/03/04/05(文案)/09）。
> 本輪目標：只修 **離線可證明（offline-safe）** 的 pending bug；需要實機/外部模組的項目維持 pending、不假裝完成。
> 驗收：5 組件 0 warning/0 error、L1 `FAILED 0`、`tools/validate RESULT: OK`、44 gates PASSED。

## 1. 本輪範圍（只做這些；其餘 pending）

| 編號 | 問題（文件原文） | 本輪 |
|---|---|---|
| **L-04**（P0 風險） | 存檔「驗證有修復就整筆拒寫」→ 未知作物/舊狀態可能靜默不存檔、進度回捲 | ✅ 本輪修 |
| **L-12**（P1） | 面板固定作物清單/警告/名稱吃 `FallbackCropCatalog`＋硬編碼 10 種 → 作物模組新作物顯示無效/英文 id | ✅ 本輪修 |
| **L-15**（P2） | UI 讀 `TierCatalog.Get`（編譯期）而模擬用 config 覆寫後 `TierProfile` → 面板與實際行為不一致 | ✅ 本輪修 |
| **L-09**（P2） | 物流端口只回 adapter 探測結果，面板卻寫「已連接」→ 玩家以為輸送帶會搬作物 | ✅ 本輪修（語義：只說偵測到模組，不承諾連接） |
| L-11 / WP-10 | 世界圖示讀日 tick 快取、面板讀即時 | ⏸ pending（需實機驗證 vanilla icon 行為） |
| WP-05 真實連通 / L-02(b)(c) | `PipeRegistry.TryGetGraph` 真實判定、3-strike 時間窗 | ⏸ pending（需實機/第三方程 API） |
| WP-07 / L-20 | `indoor.behavior-probe` 唯讀診斷 | ⏸ pending（G-08 掃 source 禁 Harmony 字串，需另案設計） |
| WP-13 / RE-06 | 自訂陣營（Ember-pelts）支援 | ⏸ pending（需外部主模組） |
| L-14 / L-16 / L-18 / L-19 | 效能量測、backward 清單、死旗標、ADR 漂移 | ⏸ pending（非本輪；設計決策/量測） |

## 2. 檔案所有權（避免衝突；只改自己名下）

- **T1 `save-repair`（L-04）**
  - `src/HDBF.Core/Indoor/IndoorRepairResult.cs`
  - `src/HDBF.Core/Indoor/IndoorSaveValidator.cs`
  - `src/HDBF.Core/Indoor/IndoorLogKeys.cs`（只加 `save.*` 常數）
  - `src/HDBF.Persistence/Indoor/IndoorEntitySaveBridge.cs`、`IndoorEntitySaveBridge.Support.cs`、`IndoorSaveKeys.cs`
  - `tests/HDBF.Tests/V2SaveValidatorTests.cs`、`V2SaveRepairTests.cs`、`V2PersistenceBridgeContractTests.cs`、新增 `V2SaveWriteStrategyTests.cs`
- **T2 `panel-live`（L-12 / L-15 / L-09）**
  - `src/HDBF.Core/Indoor/IndoorFarmSnapshot.cs`、`CropDefinition.cs`、新增 `IndoorCropChoice.cs`（如需）
  - `src/HDBF.Gameplay/Components/HdbfBoxFarmController.IndoorSnapshot.cs`、`GameCropCatalog.cs`、`GameCropCatalog.SpecReader.cs`
  - `src/HDBF.UI/**`（面板、format、warnings、loc keys）
  - `data/Localizations/{enUS,zhCN,zhTW}.csv`
  - `tests/HDBF.Tests/V2PanelContractTests.cs`、`V2PanelVisualContractTests.cs`、`V2LocalizationTests.cs`、`V2CropCatalogTests.cs`、`V2CropReportTests.cs`、新增測試檔可自由
- **Lead**：`docs/**`、`README.md`、`data/manifest.json`、`docs/VERIFICATION.md`、最終 build gate、整合 review、`V2Nfr*` 若衝突由 Lead 裁決。

> 需要改別人名下的檔案 → 先 `send_message` 問 Lead，不要直接改。

## 3. 契約

### 3.1 L-04（Core + Persistence）
1. `IndoorRepairResult` 要能區分「**修好但可寫**」與「**根本不可寫**」：
   - 既有 `Repaired`（有任何修復）語意不變；
   - 新增 `CannotWrite`（record-missing / tier-missing / tier-profile-invalid / capacity-invalid / entity-id-mismatch / validator-exception → true，`Fail(...)` 觸發）。
2. `IndoorEntitySaveBridge.Save`：
   - `CannotWrite` → 維持舊行為：拒寫 + `save.validation-failed`，保留上一份良好快照；
   - `Repaired && !CannotWrite` → **寫入修復後的 candidate**（不再整筆拒寫）＋ `save.write-repaired`（含 `Describe(validation)` 與 notes）＋ 先把「上一份 Payload」寫到新 key `PreviousPayload`（僅在有舊 payload 時；此為最後良好快照）；
   - 乾淨 → 照舊。
   - Keep：deep-copy validation、encode round-trip、永不拋例外（REQ-V2-SAFE-01）。
3. 新 log key：`SaveWriteRepaired = "save.write-repaired"`、`SavePreviousSnapshot = "save.previous-snapshot"`（只在真的保留時寫）。
4. 測試（L1 可跑的部分＝Core 行為 + 真實 bridge 原始碼契約）：
   - `IndoorRepairResult.CannotWrite` 對每個 `Fail` 情境成立、`Clean` 為 false；
   - 有修復（如 slot 補齊/未知作物清空）但非 `Fail` → `CannotWrite == false`、`Repaired == true`；
   - bridge 原始碼必須含 `save.write-repaired`、`PreviousPayload`、且不再用單一 `validation.Repaired` 當拒寫條件；
   - 既有 V2Save* 測試全綠。

### 3.2 L-12 / L-15 / L-09（Core snapshot + Gameplay + UI）
1. **L-15**：`IndoorFarmSnapshot` 加 `TierProfile? Profile { get; init; }`（config 覆寫後的真實 profile）；controller 填 `_profile`；UI 的 `FindProfile` 優先 `snapshot.Profile`，null 才退回 `TierCatalog.Get`（既有測試 snapshot 不帶 Profile → 行為不變）。
2. **L-12**：
   - `CropDefinition` 加 `DisplayNameLocKey`（init property，可空；不得改 positional record 參數以免爆編譯）；
   - `GameCropCatalog` 從 blueprint 的 `LabeledEntitySpec.DisplayNameLocKey` 讀取（try/catch，缺 spec 時留空＝退回內建表/raw id）；
   - `IndoorFarmSnapshot` 加 `IReadOnlyList<IndoorCropChoice> CropChoices { get; init; }`（Core 型別：`Id` + `DisplayNameLocKey`）；
   - controller 快取並填入 live `_cropCatalog.ForFaction(_faction)` 的作物清單（不逐幀配置；catalog 變更時重建）；
   - UI：下拉選項、固定作物顯示名、invalid-crop 警告全部改用 snapshot 的 live 清單/名稱；清單為空（舊測試/未知）才退回 `FallbackCropCatalog` + 內建 10 種名稱；
   - 一致性：命令層（`Commands.CropPolicy.cs`）用 live catalog 接受該作物 → 面板不得再判它無效。
3. **L-09**：Output 卡的物流端口列不再顯示「已連接」：
   - 無 port → Not applicable；有 port → 新鍵 `HDBF.Port.LogisticsDetected`（「已偵測到物流模組（HDBF 尚未接出貨物）」/en/zhCN/zhTW）；
   - 新增 tooltip `HDBF.Tooltip.LogisticsPort`：明說 HDBF 目前不把產物交給輸送帶，請用原版搬運工/倉庫；
   - 三語 key 集必須一致、無空字串（G-09 會驗）。
4. 測試：panel 用 snapshot.Profile 覆寫值（維護材料/週期）而非編譯期 TierCatalog；live CropChoices 覆蓋 fallback；未知作物但 live 有 → 不顯示 invalid 警告且顯示本地化名；logistics 列文字/tooltip 契約；三語 parity。

## 4. 驗收（Lead 最終執行）
- `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-work"` → 5 組件 0/0、L1 `PASSED >= 655, FAILED 0`、`tools/validate RESULT: OK`、44 gates PASSED。
- `git status` 只含本輪相關檔案；`git diff` 自查無 debug/Harmony/cheat 字串。
- 文件：`docs/BUG_REPAIR_20261102.md` §10/§11 狀態、`docs/v2/CHANGELOG.md` [2.5.5]、`docs/VERIFICATION.md` §38、`docs/REQ_TRACEABILITY.md` Round-25、`docs/v2/ADRS.md` ADR-V2-006、`README.md`/`data/manifest.json` 版本 2.5.5（未發佈/未實機驗證者一律標 pending）。

## 5. 執行結果（2026-11-03 收輪）

| 項目 | 結果 |
|---|---|
| T1 `save-repair`（L-04） | ✅ 完成：`task-1` completed；`build.ps1 -WorkRoot "$env:TEMP\hdbf-work-t1"` → 5×0/0、L1 **672/672**、validate OK、44 gates |
| T2 `panel-live`（L-12/L-15/L-09） | ✅ 完成：`task-2` completed；`build.ps1 -WorkRoot "$env:TEMP\hdbf-work-t2"` → 5×0/0、L1 **672/672**、validate OK、44 gates |
| Lead 整合 | ✅ `build.ps1 -WorkRoot "$env:TEMP\hdbf-work"` → 5×0/0、L1 **672/672**、validate OK、44 gates；文件同步（§11、CHANGELOG [2.5.5]、VERIFICATION §38、ADR-V2-006、REQ_TRACEABILITY Round-25、README/manifest 2.5.5） |
| 未執行 | 未 `-Install`、未開遊戲、未跑實機 fixture、未上傳 Steam Workshop、未 push GitHub |

**實作偏差（已寫入文件）**：`entityId-mismatch` 維持 Clamp（不列 `CannotWrite`）；`PreviousPayload` 語意為
「修復前回滾副本」而非 last-good snapshot（`IEntitySaver` write-only）；keyed bridge 未統一（非 live 路徑）；
`HdbfUiLocKeys.cs` 剛好 300 行，下一批字串需先拆 partial。

**已知設計點（留後續，不在本輪擴張）**：

* L-09 目前是**三態**（Not applicable / 偵測到模組 / Not connected），永不宣稱 Connected；`Output port` 與
  `Logistics port` 兩列共用同一誠實語意（保留兩列是為了既有「實體端口各自一列」的 visual contract），
  是否合併留後續決定。
* `DisplayNameLocKey` 只讀 `LabeledEntitySpec`；只有 `SimpleLabeledEntitySpec` 的極端 mod 作物會退回 raw id
  （面板仍可選、不再被判無效）。
* `features.logisticsOutput` 等 config 死旗標未動（L-18 不在本輪）；面板文案對所有 tier 一律誠實。
