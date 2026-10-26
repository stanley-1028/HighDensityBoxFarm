# BUG-07 (P1) — 掃描可達性查詢失敗被記成 `compat.failed`、且每格一條含完整 stack 的 Warn（round-28 日誌洗版）

Status: **程式層修復完成、L1 全綠（504/504）、獨立驗證 PASS；實機未驗證**（本卡禁止部署、禁止啟動
Timberborn、禁止執行 `build.ps1`，也不碰 data/藍圖/UI/美術）。下一輪實機回合由 Lead 建置/部署後確認。
卡日期：2026-10-25。

誠實一句話：成因由 round-28 實機日誌（`docs/in-game/round28-Player.log`）＋現行原始碼定位；修法
只做程式層 + game-free L1（新增 12 條測試，全量 504 條 PASS、0 FAIL），並由獨立 verifier 以另一個
workroot 重跑 gate 與行為探針。**沒有**聲稱「實機已驗證」——實機效果要等下一個部署後的實機回合，
判準見 §6。

---

## 1. 實機證據（round 28，2026-10-25）

日誌：`docs/in-game/round28-Player.log`。關鍵行：

```
[HDBF/Warn] compat.failed: plot reachability query failed; cell excluded from this scan;
  entityId=eed460d8-…; exception=System.InvalidOperationException: There's no cached flow field at 1187834;
  stack=at Timberborn.Navigation.FlowFieldCache.GetFlowFieldAtNode …
  at HDBF.Gameplay.Components.HdbfBoxFarmController.IsReachable (UnityEngine.Vector3Int cell)
  … suppressed=2
```

判讀：

* 這是**掃描階段** `IsReachable(cell)` 內呼叫 `Accessible.IsReachableByTerrain` / `FindRoadPath`
  （經 `NavigationService` 的 reachable-area 測試）時，對沒有 flow field 的格丟例外。任何格都可能
  發生，27×27 的 Advanced 掃描（729 格）尤其多。
* 分類錯：`compat.failed`（`HdbfLogKeys.CompatFailed`）是給**外部模組適配器**失敗用的
  （`src/HDBF.Integration/**` 的 adapter 偵測/初始化與 `CompatDetector`），可達性過濾器是
  **可回收的 per-cell 條件**，不是兼容性事件。玩家看到 `compat.failed` 會以為兼容性壞了。
* 內容錯：舊碼走 `_log.Error(..., exception, entityId)`，`ThrottledLog.ComposeError` 會附上
  exception type + 最多 20 行 stack（`src/HDBF.Core/Logging/ThrottledLog.cs:120-159`）。30 秒節流
  只讓輸出變稀疏（`suppressed=2` 表示真正的失敗次數被折疊成一個計數），下一個窗口還是再噴一條
  含 stack 的線；而且 key 不帶 entityId，所有建築共用同一個 `compat.failed` 節流槽，一棟農場的
  失敗會壓掉別棟的可見性。
* 判定語意本身**正確**：查詢失敗 → 該格不可達 → 排除於候選。本卡明確不動這個語意。

**補充實機證據（同期 round-29，修復前 DLL；不是本修正的驗證）**：2026-10-25 15:57–16:13 同期
工作流跑了另一個實機回合 `docs/in-game/round29-*`，它部署的是 **15:48–15:49 的修復前 DLL**（verifier
以字串差分確認：deployed `HDBF.Gameplay.dll` 仍含舊字串 `plot reachability query failed` 且無
`WarnIfScanQueriesFailed`；deployed `HDBF.Core.dll` 無 `gameplay.scan-reachability`）。round-29 日誌
恰好再次重現同一簽名：**13 條 `compat.failed`、13 條 `plot reachability query failed`、13 個
`HdbfBoxFarmController.IsReachable` frame、0 條新 key**。這是修復前行為的第二次實機重現（佐證根因與
規模），但**不是**本修正的成果驗證；本卡的下一個實機回合記為 **round 30**（見 §6）。

---

## 2. 根因（修復前程式路徑）

呼叫鏈：`Tick`（`HdbfBoxFarmController.Scan.cs:44`）→ `_scanner.Step` →
`TryAcceptPlot`（`:165`）→ `IsReachable(cell)`（修復前 `:359`）→ 導航查詢丟
`InvalidOperationException: There's no cached flow field` → catch 直接
`_log.Error(HdbfLogCategory.Warn, HdbfLogKeys.CompatFailed, "plot reachability query failed; …",
exception, _entityId)`（修復前 `Scan.cs:404-406`）。

三個錯誤疊加：

| # | 錯誤 | 後果 |
|---|---|---|
| 1 | 分類：用 `compat.failed` | 掃描路徑事件被誤報成外部模組適配器失敗 |
| 2 | 內容：`_log.Error` 附 exception + stack | 每條線都帶完整 stack，玩家/支援難讀 |
| 3 | 粒度/節流：每格呼叫一次，key 無 entityId | 失敗次數被 30 s 節流折疊成 `suppressed=n`；無 entityId 區分建築；每個窗口後仍會再噴一條含 stack |

同類問題（本卡一併修）：**retention 路徑的 crop probe catch**（修復前 `Scan.cs:345-356`，其中
`:350`）已用 `_retentionProbeWarned` 做「每掃描一次」，但仍走
`_log.Error(HdbfLogCategory.Warn, LogKeyScan, …, exception, _entityId)`：generic key + 完整 stack，
同屬「可回收 per-cell probe 卻附 stack」的類別。

判定語意不受影響：`IsReachable` 失敗仍回 `false`，`TryAcceptPlot` 仍
`_scanStats.ReachabilityRejects++; return false;`（現行 `Scan.cs:254-258`，未改）。

---

## 3. 修法（file:line 為修復後現行位置）

### 3.1 新 log key（Core）

`src/HDBF.Core/Logging/HdbfLogKeys.cs`

| 行 | 內容 |
|---|---|
| `:59-71` | 新 `ScanReachability = "gameplay.scan-reachability"`：每完成一次掃描最多一條、per-building 節流、無 stack、永不使用 `compat.failed`；message 契約見 §3.2 |
| `:73-80` | 新 `ScanRetentionProbe = "gameplay.scan-retention-probe"`：retention crop probe 的同類修正用 key |

兩個 key 都**不是** config 鍵（`HDBF_Config.json` 零改動）；log key 只是 throttle identity。

### 3.2 Core 可測的計數 + 訊息組裝（`src/HDBF.Core/Plots/PlotScanStats.cs`）

| 行 | 內容 |
|---|---|
| `:65-74` | `ReachFailures`（本掃描查詢失敗次數，private set） |
| `:76-89` | `FirstReachFailureX/Y/Z`、`FirstReachFailureException`（只留第一格 + 例外型別，永不 stack） |
| `:92` | `HasReachFailures` |
| `:99-110` | `RecordReachabilityFailure(x, y, z, exceptionTypeName)`：累加；僅第一次記下位置/型別 |
| `:118-121` | `ReachabilityFailureKey()`：`ReachFailures > 0` 時回 `HdbfLogKeys.ScanReachability`，否則 `null` |
| `:130-143` | `DescribeReachabilityFailures(entityId)`：單行穩定訊息（欄位順序固定、`InvariantCulture`） |
| `:222,233-237` | `Reset()` 一併清除新狀態 |

固定訊息格式（下一輪驗收 grep 的契約）：

```
plot reachability queries failed; failed cells are excluded from this scan
  ; entityId=<guid> scanned=<n> reachFailures=<n> accepted=<n>
  firstFailureCell=(<x>,<y>,<z>) exception=<type full name>
```

### 3.3 Gameplay 呼叫端（`src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs`）

| 行 | 內容 |
|---|---|
| `:405-417` | `IsReachable` catch 改為**只計數**：`_scanStats.RecordReachabilityFailure(cell.x, cell.y, cell.z, exception.GetType().FullName); return false;`。不再呼叫 `_log`、不再帶 stack、不再用 `compat.failed`。判定語意不變（回 `false`） |
| `:473-478` | `OnPlotsScanned`（每次掃描完成）在既有 `WarnIfRegistrationLooksBroken()` 之後呼叫 `WarnIfScanQueriesFailed()` |
| `:585-614` | 新 `WarnIfScanQueriesFailed()`：`key = ScanReachability + "." + _entityId`（per-building 30 s 節流）→ `_log.Write(HdbfLogCategory.Warn, key, DescribeReachabilityFailures(...))`；無失敗則直接 return，**每次掃描最多一條** |
| `:345-361` | retention crop probe catch 改用 `HdbfLogKeys.ScanRetentionProbe + "." + _entityId` + `_log.Write`（type + message，無 stack），仍以 `_retentionProbeWarned` 維持每掃描最多一條 |
| `:551-555` | `ResetScanCounters()` 沿用 `_scanStats.Reset()`（新狀態一併歸零）+ `_retentionProbeWarned = false` |

節流方式沿用既有機制（與 `FarmPathUnreachable`/`CropVisibility` 相同的 `key + "." + entityId` pattern），
沒有新增 static 可變狀態、沒有新增 config 鍵。

### 3.4 掃描路徑逐格 catch 全面檢視（卡要求第 2 點）

| catch | 位置 | 修復前 | 處置 |
|---|---|---|---|
| `IsReachable` 導航查詢 | `Scan.cs:405-417` | `Warn` + `compat.failed` + 完整 stack、每格一次 | **修**：計數 + 每掃描一條精簡摘要 |
| `IsOccupiedByNonCrop` crop probe（retention） | `Scan.cs:345-361` | `Warn` + generic `gameplay.scan` + stack、每掃描一次 | **修**：專用 key、無 stack、每掃描一條 |
| scan abort（`Tick`） | `Scan.cs:57-62` | `Error` + stack | 不改：**每次 abort** 一條，非逐格；stack 對真正掃描中止有診斷價值（REQ-SAFE-15） |
| `OnPlotsScanned` 外層 | `Scan.cs:486-491` | `Error` + stack | 不改：每次掃描失敗一條，非逐格 |
| `WarnIfRegistrationLooksBroken` 自身 | `Scan.cs:577-582` | `Warn` + stack | 不改：只有診斷格式化本身丟例外才會走，每次掃描最多一次 |
| `ResolvePathLength` / `ReportPathUnreachable`（farm loop，非掃描） | `Scan.cs` 外，`Farming.cs:740-756` | 已是 BUG-04 好樣板 | 不改：per-building throttled key、type+message、無 stack |
| `Diagnostics.cs`（dev-only） | `HdbfBoxFarmController.Diagnostics.cs` | 只有 `-DevDiagnostics` 編譯 | 不改（release 編譯掉；保留給 Lead 取證） |

`Diagnostics.cs` 的 `diag.scan.end` 未新增 reachFailures 欄位：本卡不依賴 dev-only 診斷，release 的
`gameplay.scan-reachability` 行本身就是驗收證據；不動 Diagnostics.cs 也避免與其他卡範圍重疊。

---

## 4. 測試（L1，game-free）

新檔：`tests/HDBF.Tests/Bug07ScanReachabilityLoggingTests.cs`（12 條，`TestRunner` 反射自動發現；
未改 `TestRunner`/`HDBF.Tests.csproj`；只引用 `System` + `HDBF.Core`，不載入任何 Timberborn/Unity
組件）。分組：

| 組 | 測試 | 釘住的行為 |
|---|---|---|
| 計數/第一格 | `TestReachabilityFailuresAccumulateAndKeepTheFirstCell` | 200/3 次失敗全累加；第一格與第一例外型別保留 |
| 單一摘要 | `TestManyFailuresAssembleExactlyOneSummaryLine`、`TestSummaryCarriesTheMandatedFields` | 200 次失敗 → **一個**單行摘要；欄位 entityId/scanned/reachFailures/accepted/firstFailureCell/exception 齊全；只有一個 firstFailureCell |
| 無 stack/分類 | `TestSummaryHasNoStackAndNoCompatClassification` | 訊息不含 `stack`、`at HDBF`、`compat.` |
| 無失敗/重置 | `TestNoFailureHasNoSummaryKey`、`TestResetClearsTheReachabilityFailureState` | 無失敗 → key 為 null；`Reset()` 清空計數/first/型別 |
| key 契約 | `TestFailureKeyIsTheNewGameplayKey`、`TestScanLogKeysAreStableAndNotCompatKeys` | 新 key 字串 `gameplay.scan-reachability` / `gameplay.scan-retention-probe` 固定、非 `compat.failed`、與既有 `plot.scan-reachability` 區分 |
| 座標格式 | `TestSummaryUsesInvariantCoordinatesForNegativeCells` | 負座標 `(-1024,1023,0)` 不因 culture/千分位走鐘 |
| 語意分離 | `TestReachFailureCounterIsSeparateFromTheRejectCounters` | `reachFailures` 與 registration 警告的 `reach=` reject 計數分離 |
| 節流輸出 | `TestOneThrottledSummaryLineReachesTheLogForManyCellFailures` | 200 次 per-cell 失敗 → 真實 `ThrottledLog` sink（key 帶 entityId）**只收到 1 條**，內容含 `reachFailures=200`、無 `stack=` |
| 生產碼反回歸 | `TestProductionScanSourceKeepsTheFixWired` | 用 csproj 已嵌入的 `Scan.cs` 文字釘住：無 `HdbfLogKeys.CompatFailed`、`IsReachable` 內無 `_log.`、scan end 只呼叫 `WarnIfScanQueriesFailed()` 一次、`!IsReachable` → `ReachabilityRejects++` → `return false;` 順序保留、retention catch 用 `ScanRetentionProbe` 且無 `_log.Error(` |

---

## 5. 建置與測試實際輸出（本機沙箱，2026-10-25 16:05–16:07 Asia/Macau）

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

輸出（本卡最終 gate）：

```
Core     : 0 個警告 / 0 個錯誤
Gameplay : 0 個警告 / 0 個錯誤
Tests    : 0 個警告 / 0 個錯誤
PASSED 504  FAILED 0        (exit 0)
```

`504 = 本卡前基線 492 + 本卡新增 12`；既有 492 條（含 BUG-04/05/06、REQ 契約、release gate）全綠，
沒有既有測試被改寫或停用。build 產物全在 `$env:TEMP`（沙箱導向
`C:\Users\user\AppData\Local\Temp\dsh-ZX1J3T\hdbf-card-work`），工作區沒有新增 `bin/obj`，無沙箱拒寫。

---

## 6. 下一輪實機預期（round 30；待驗證，不是已驗證）

> **命名/證據註記（獨立 verifier 回報後補記）**：同期工作流已用掉一個實機回合，產物是
> `docs/in-game/round29-*`（15:57–16:13），但它部署的是 **15:48–15:49 的修復前 DLL**（差分證據見
> §1 補充段落），日誌是 bug 的修復前重現，**不是**本修正的驗證。因此本卡的下一個實機回合改名為
> **round 30**，避免與已存在的 round-29 撞名，也避免把修復前日誌誤當驗收證據。

部署本卡 Core + Gameplay 後，用與 round-28 相同的 fixture（含 Advanced 27×27 農場，地圖上存在
沒有 flow field 的格）跑一輪。日誌應滿足：

1. **`gameplay.scan-reachability.` 每棟建築最多一條**：grep
   `\[HDBF/Warn\] gameplay\.scan-reachability\.<entityId>:`。每次掃描完成最多一行；30 秒窗口內重複的
   掃描會被節流成 ` suppressed=<n>` 計數，不會逐格出現。訊息含 `scanned=…`、`reachFailures=…`、
   `accepted=…`、`firstFailureCell=(x,y,z)`、`exception=System.InvalidOperationException`。
2. **不含 stack**：該行不得出現 `stack=` 或 `at HDBF.Gameplay.Components.HdbfBoxFarmController.IsReachable`
   之類的 frame 文字；`suppressed` 只出現在行尾。
3. **`compat.failed` 不再由掃描產生**：不得再出現
   `compat.failed: plot reachability query failed`（注意：Integration adapter 若真的載入失敗仍可能合法
   出現 `compat.failed:<adapter>`，那是別的事件；驗收看的是掃描訊息文字/`IsReachable` frame）。
4. **可達性語意不變**：被例外打到的格子仍不進候選/註冊；同一 fixture 的 `accepted`/`registeredNow`/
   coverage 量級應與 round-28 相同（本卡只改日誌，不改判定）。
5. **既有聚合警告可能仍在**：若查詢失敗造成 `ReachabilityRejects > Accepted`，
   `plot.scan-reachability`（既有 starved-registry 警告，`HdbfLogKeys.PlotScanReachability`）仍可能
   出現一條——它本來就不含 stack、不是 compat，屬正常聚合訊號，不要與新 key 混淆。
6. **retention probe 同類修正**：只有在 `IsOccupiedByNonCrop` 真的丟例外時才會看到
   `gameplay.scan-retention-probe.<entityId>`（每掃描最多一條、無 stack）；正常回合不應出現。

驗收一句話：round-30 日誌中「Advanced 農場的掃描失敗」應呈現為**至多一條精簡
`gameplay.scan-reachability` Warn（無 stack）**，且掃描路徑不再出現 `compat.failed`。

---

## 7. 改動檔案清單

修改：

* `src/HDBF.Core/Logging/HdbfLogKeys.cs` — 新增 `ScanReachability`、`ScanRetentionProbe`（§3.1）
* `src/HDBF.Core/Plots/PlotScanStats.cs` — 新增失敗累加/訊息組裝/重置（§3.2）
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` — `IsReachable` catch、retention catch、
  scan-end 摘要呼叫（§3.3/§3.4）

新增：

* `tests/HDBF.Tests/Bug07ScanReachabilityLoggingTests.cs` — 12 條 game-free L1（§4）
* `docs/workstreams/bug-07-scan-reachability-logging.md` — 本報告

刻意未改（硬規則）：`data/**`（含 `HDBF_Config.json`、藍圖）、`docs/VERIFICATION.md`、
`docs/REQ_TRACEABILITY.md`、`build.ps1`、`Directory.Build.*`、`src/HDBF.Integration/**`（D-2 Harmony
補丁零改動）、`src/HDBF.UI/**`、`HdbfBoxFarmController.Diagnostics.cs`、平衡數值、部署/實機。
沒有新增 static 可變狀態；沒有新增 config 鍵；沒有改 `PlotScanStats` 既有欄位/訊息格式。

---

## 8. 殘餘風險／範圍外

1. **不會讓查詢不再丟例外**：本卡只修「分類/粒度/內容」。地圖上「沒有 cached flow field」的格子仍會
   被判定為不可達（與現在一致）。若該例外其實代表導航初始化/BuildingAccessible 接線有更大的問題，
   需要另開卡（不是日誌 bug）。
2. **`Simulation.cs:154` 的每日 moisture sampling 失敗仍用 `compat.failed`**（`HdbfBoxFarmController.
   Simulation.cs`，非掃描路徑、非逐格）：屬同型分類問題但不在本卡「掃描路徑」範圍，未改，留給 Lead
   決定是否另開卡。
3. **掃描中途 abort**（`Tick` catch → `_scanner.Cancel()`）不會走到 `OnPlotsScanned`，因此該次不完整的
   掃描不會印 reachability 摘要；abort 本身已有一次 Error。下次完整掃描會重新計數。
4. **`OnPlotsScanned` 較早步驟丟例外**（例如 plot selection 失敗）時，同一次掃描的摘要可能來不及印；
   outer catch 會記一次 Error，counter 於下次 `StartScan` 重置。屬可接受的診斷降級（REQ-SAFE-02）。
5. **新 retention key 是額外新增**：卡只明文要求 `gameplay.scan-reachability`；`gameplay.scan-retention-probe`
   是同類問題修正所需（卡第 2 點），已在 §3.4 說明。若 Lead 希望完全不新增第二個 key，可改回
   `gameplay.scan` 分類，但會失去 per-building 節流與清楚分類，不建議。
6. **同期其他工作流**也在同一工作區寫檔（本工作區不是 git repo，無 commit 隔離）：verifier 觀測到
   BUG-07 窗口內另有 32 個非本卡寫入（`docs/in-game/round29-*`、`docs/VERIFICATION.md` 16:13:24 的
   round-29 整合、art-01 previews、`ARCHITECTURE.md`、`art/README` 等）。這些**不是本卡改的**（本卡
   未碰 `docs/VERIFICATION.md`／`docs/REQ_TRACEABILITY.md`／`data/**`／`build.ps1`／
   `Directory.Build.*`／`Integration/**`）；本卡 gate 於 2026-10-25 16:07 跑完時全綠，最終整合由
   Lead 負責。部署資料夾 `Mods\HDBF` 的 DLL 是 15:48–15:49（修復前），本卡沒有部署。

---

## 9. 驗證邊界（誠實聲明）

* 本卡**只做程式層與 L1**：沒有部署、沒有啟動 Timberborn、沒有跑 `build.ps1`、沒有實機日誌。
* 根因定位依據：卡上提供的 round-28 日誌片段 + 現行原始碼（`Scan.cs` 修復前路徑、`ThrottledLog.
  ComposeError` 的 stack 行為、`HdbfLogKeys` 的使用者）。修法效果由 Core 單元測試 + 生產碼 source
  contract 固定；**實機日誌效果待 round 30**。
* 同期 round-29 實機回合（15:57–16:13）跑的是**修復前**部署 DLL，是 bug 的第二次實機重現，
  **不可**當作本修正的驗證（見 §1 補充段落、§6 註記）。
* 沒有聲稱 `compat.failed` 已從整個模組消失：只保證**掃描路徑**不再使用它（`Scan.cs` 內已無
  `HdbfLogKeys.CompatFailed`，由 L1 source contract 固定）。

---

## 10. 獨立驗證（verifier，read-only；程式層，非實機）

由另一個 agent（fresh context，不共用本卡假設）全程只讀工作區、以**另一個 workroot**
（`%TEMP%\hdbf-bug07-verify\work`）獨立完成；verifier 未部署、未啟動遊戲、未跑 `build.ps1`、
未寫工作區。結果 **PASS（6/6 步）**：

1. **Gate PASS**：Core / Gameplay / Tests 三支 build 各 `0 warnings / 0 errors`、exit 0；
   `PASSED 504 FAILED 0`、exit 0——與 §5 完全一致（504 = 492 + 12）。
2. **反射逐條 PASS**：載入 verifier 自己 fresh build 的 `HDBF.Tests.dll`，反射呼叫
   `Bug07ScanReachabilityLoggingTests` 的 12 條 → **12/12 PASS**；並獨立數到 assembly 共 504 條
   測試方法（492+12）。
3. **行為探針 PASS（34 項 expectation）**：對 fresh build 的 `HDBF.Core.dll` 實跑 200 次
   `RecordReachabilityFailure` → `ReachFailures=200`、first cell `(118,42,3)`、
   `System.InvalidOperationException` 保留、`ReachabilityRejects` 不受影響（=37，計數分離）、
   `ReachabilityFailureKey()="gameplay.scan-reachability"`。實際 summary 字串（234 字元、無 CR/LF、
   無 `stack`、無 `compat`，case-insensitive）：

   ```
   plot reachability queries failed; failed cells are excluded from this scan;
   entityId=eed460d8-… scanned=729 reachFailures=200 accepted=12
   firstFailureCell=(118,42,3) exception=System.InvalidOperationException
   ```

   `Reset()` 後 `ReachFailures=0`、FirstException/Key=null；無失敗時 key=null；
   空 entityId 顯示 `entityId=<unknown>`。
4. **Source audit PASS（sha256 固定）**：`Scan.cs` 無 `HdbfLogKeys.CompatFailed`；`IsReachable` 內
   `RecordReachabilityFailure` 一次、零 `_log.`；`WarnIfScanQueriesFailed()` 在 `OnPlotsScanned`
   恰好一次（定義也一次）；`!IsReachable → ReachabilityRejects++ → return false` 順序保留；
   retention catch 用 `HdbfLogKeys.ScanRetentionProbe + "." + _entityId`、Warn-level `Write`、
   無 `_log.Error(`、失敗仍保留 plot；`HdbfLogKeys` 兩個新字串逐字相符（Scan 相關
   `gameplay.scan*` literal 只有這兩個）；`PlotScanStats` 零 `static`（無新增 static 可變狀態）；
   `data/HDBF_Config.json` sha256 未變、無 `scan-reachability`/`reachFailures`/`scan-retention` token。
5. **硬規則 audit PASS（1 個 (x)，非本卡）**：本卡歸屬的 changed set 恰為 §7 的 5 個檔案
   （hash/mtime 記錄：`HdbfLogKeys.cs` 15:59:19、`PlotScanStats.cs` 15:59:36、`Scan.cs` 15:59:50、
   新測試 16:06:33、本報告 16:09:26）；`src/**`/`tests/**` 其他檔在窗口內零改動；工作區無
   `bin/obj/dll/exe/pdb`；`data/**` 窗口內零改動；`REQ_TRACEABILITY.md`/`build.ps1`/
   `Directory.Build.*`/`src/HDBF.Integration/**` 皆為當日更早的基線、非本卡。**(x)** 同期
   round-29 整合工作流在 16:13:24 改了 `docs/VERIFICATION.md`——**非本卡所為**（本卡未碰該檔；另見
   §8.6 的同工作區並行說明）。
6. **報告 audit PASS**：本報告含根因、file:line、實際 504/0 輸出、round-30 驗收 grep、明示
   「實機未驗證」；所有「實機已驗證」字樣皆為否定語境，無 overclaim。

Verifier 另回報、已反映於本報告的注意事項：

* **round-29 是修復前重現，不是本修正的驗證**：差分證據與日誌統計見 §1 補充段落、§6 註記；
  下一輪已改名 round 30。
* **report 命名撞名風險**：原稿 §6 寫「round 29」，與已在用的 `docs/in-game/round29-*` 衝突，
  已改為 round 30。
* `Simulation.cs:154` 的每日 moisture sampling 失敗仍用 `compat.failed`（同型分類問題、非掃描
  路徑、非逐格）→ 列入 §8.2 殘餘風險，未改。
* `WarnIfScanQueriesFailed` 自身的診斷格式化 catch 若真的丟例外仍會是一條帶 stack 的 generic
  Error（與既有 `WarnIfRegistrationLooksBroken` 同 pattern、非逐格）→ 列入 §8.4，可接受。
* L1 限制：`HdbfBoxFarmController.Scan.cs` 引用 Timberborn 型別、無法編進 game-free 測試組件，
  所以 scan 接線由 embedded-source contract test 固定，而非真正執行；實機日誌行為仍需 round 30。

Verifier 證據檔：`%TEMP%\hdbf-bug07-verify\logs\{step1-gate,step4-source-audit,
step5-hardrule-audit,step6-report-audit}.log`、scratch probes 於 `...\scratch\`；verifier 全程
未寫工作區，事後 re-hash 本卡 5 檔皆未變。
