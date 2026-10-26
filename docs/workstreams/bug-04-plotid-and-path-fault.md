# BUG-04 (P0) — `plotId` 漏賦值 + farm plan 逐格路徑容錯

Status: **程式層修復完成、L1 全綠；實機未驗證**（本卡禁止部署、禁止啟動 Timberborn、
禁止 `build.ps1`）。下一回合（round 10）由 Lead 建置/部署後實機確認。
卡日期：2026-10-25。只改程式與測試。

誠實一句話：兩個成因都用 round-9 實機日誌 + 現行原始碼定位，並在程式層結構性修掉；
新增 L1 回歸測試全綠（`PASSED 292 FAILED 0`）。**沒有**聲稱「實機已修好」——那要等
round 10 的 `diag.scan.end registeredNow=60` / `diag.day coverage=1.000` 與否。

---

## 1. 實機證據（round 9，2026-10-25 11:14–11:23）

日誌檔：`docs/in-game/round9-Player.log`（12,903 B，Lead 已存）。原文：

`docs/in-game/round9-Player.log` 內 `diag.scan.end` 行（每次日 tick 掃描都會重複一次；本輪
日誌共 4 次掃描完成、3 個日 tick）：

```
[HDBF/Info] diag.scan.end...: scan.end entityId=a14b9d44-... scanned=121 accepted=102
  candidates=102 selected=1 granted=1 registeredNow=1
  rejects[terrain=0,field=19,block=0,blocker=0,reach=0,excluded=0,claimed=0]
```

同檔 `diag.scan.probe` 行（獨立 probe 證明 102 格真的可種）：

```
[HDBF/Info] diag.scan.probe...: scan.probe entityId=a14b9d44-... origin=(130, 140, 3)
  side=11 crop=Carrot contains=121 canPlant=102 samples=(125,135,h=3) ...
```

同檔 3 條 `farm loop fault (path length)` 行（其中 1 條帶 `suppressed=1`）：

```
[HDBF/Error] gameplay.farm: farm loop fault (path length); entityId=a14b9d44-...
  exception=System.InvalidOperationException: There's no cached flow field at 1187869;
  stack=... FlowFieldCache.GetFlowFieldAtNode ... FindTerrainPathCached ... Accessible.FindTerrainPath
  ... at HDBF.Gameplay.Components.HdbfBoxFarmController.ResolvePathLength (System.Int32 plotId)
```

同檔 3 條 `diag.day` 行（day=2.18 / 3.18 / 4.18；註冊表一直是 1 格，永遠乾燥）：

```
[HDBF/Info] diag.day...: day.tick ... day=2.18 registered=1 moist=0 dry=1 tankFed=0
  tank=0.00 coverage=0.000 workers=2 shutdown=False
```

判讀（與卡一致）：

1. `accepted=102` 但 `selected=1`。`PlotSelection.Select` 依 `PlotId` 去重
   （`src/HDBF.Core/Plots/PlotSelection.cs:56-67`），102 個候選只產生 1 個 id，表示
   **102 格全部拿到同一個 plotId = 0**。該 id decode 回 `(0,0,0)`，`SoilIsMoist` 必然
   乾燥 → `coverage=0.000`。
2. `ResolvePathLength` 對沒有 flow field 快取的格呼叫 `Accessible.FindTerrainPath` 會丟
   `InvalidOperationException`，舊碼把它當成 farm loop fault（`HDBF/Error` + 故障計數）；
   每次例外都 `_farmFaults++`，累積 5 次就停用整個作物循環。就算沒到門檻，round-9 唯一的
   註冊候選永遠不可達 → `_farmPlan` 恆空 → 同樣沒有派工。
3. 本卡修掉的兩個成因在**現行原始碼仍在**（BUG-01 只動了 z 語意 Core 化、覆蓋率即時重算、
   空註冊表可見化；`Scan.cs` 的 out `plotId` 與 `Farming.cs` 的 path catch 都不在 BUG-01
   的改動範圍）。round-9 部署包是 BUG-01 之前的版本，但問題程式碼相同。

### 成因 1：`out plotId` 從未賦值（pre-fix 行號＝本卡開始時的檔案）

```csharp
// src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs（pre-fix）
85:  public bool TryAcceptPlot(Vector3Int horizontalCell, int distanceSquared, out int plotId)
87:      plotId = 0;                       // 初始 0，之後從未改寫
...
173:     var id = HdbfPlotCellCodec.Encode(cell);   // 只用於 excluded/claimed 過濾
...
197:     return true;                      // ← 沒有 plotId = id;
```

```csharp
// src/HDBF.Gameplay/Components/HdbfPlotScanner.cs（pre-fix）
101: if (host.TryAcceptPlot(horizontal, distance, out var plotId))
103:     _candidates.Add(new PlotCandidate(plotId, distance));   // 102 個候選全吃到 0
```

鏈路：`accepted=102` → `candidates=102` 但 id 全是 0 → `PlotSelection.Select` 去重成
`selected=1` → `granted=1` → `ApplyPlotArrays([0])` → `registeredNow=1` → 取樣
`(0,0,0)` 乾燥 → `coverage=0.000`（round-9 日誌的 `diag.scan.end` / `diag.day` 行）。

### 成因 2：逐格 path 例外被升級成「整份 farm plan 故障」

```csharp
// src/HDBF.Gameplay/Components/HdbfBoxFarmController.Farming.cs（pre-fix）
624: private int ResolvePathLength(int plotId)
...
656:     catch (Exception exception)
658:         ReportFarmFault(exception, "path length");   // ← 逐格 filter 失敗被當成 plan 故障
659:         return -1;

// ReportFarmFault（同檔，pre-fix :275-288）
277:     _farmFaults++;
278:     _log.Error(HdbfLogCategory.Error, LogKeyFarm,
279:         "farm loop fault (" + context + "); ...", exception, ...);
281:     if (_farmFaults >= FarmFaultsBeforeDisable && !_farmDisabled)   // 5 次
283:         _farmDisabled = true;                                        // 整個作物循環停用
```

壞處不只「多幾行 Error」：(a) 每份 plan（每日 tick、每次 plot 集合變更都會重建）都會重打
同樣的 path 查詢；(b) 沒有 flow field 的格全部被當成農場故障，累積 5 次就永久
`_farmDisabled = true`，之後不再派工。round-9 的 farm fault 行被 `ThrottledLog` 用同一個
`gameplay.farm` key 節流（只留下 3 條 emission + 1 次 suppressed），但**故障計數不受
throttle 影響，照樣每次例外累加**；round-9 只有 1 格註冊，每份 plan 最多 1 次 path 例外，
是否真的累到 5 次無法從日誌斷定——但就算未停用，該唯一候選永遠不可達，`_farmPlan` 恆空，
同樣沒有派工。這才是「沒有作物循環」的完整解釋。

---

## 2. 修法

### 2.1 結構性修掉 `out plotId`：id 只能由 accepted cell 推導（型別上不可能漏）

不再「把 `plotId = id;` 補上」，而是把「accepted cell → plotId」的責任整個移出 host：

`src/HDBF.Gameplay/Components/HdbfPlotScanner.cs:19-42` 的新介面：

```csharp
public interface IHdbfPlotScanHost
{
    // 主機只負責 game-side 的 cell 解析（plant cell z 來自 TerrainService）。
    Vector3Int ResolvePlotCell(Vector3Int horizontalCell);

    // 接受/拒絕只看 bool；沒有 out 參數可漏。
    bool TryAcceptPlot(Vector3Int plotCell, int distanceSquared);

    void OnPlotsScanned(IReadOnlyList<PlotCandidate> candidates, int deferredFrames);
}
```

`HdbfPlotScanner.Step`（`:104-143`）改成：

```csharp
var plotCell = host.ResolvePlotCell(horizontal);          // :124
if (!host.TryAcceptPlot(plotCell, distance)) continue;    // :125-128
_candidates.Add(new PlotCandidate(                          // :132
    HdbfPlotCellCodec.Encode(plotCell), distance));
```

* **編碼只有一份實作**：scanner 呼叫的 `HdbfPlotCellCodec.Encode(Vector3Int)` 是
  BUG-01 已建好的薄 adapter，內部直接 delegate 到 `HDBF.Core.Plots.PlotCellCodec`
  （bit layout 唯一實作在 Core）；本次沒有新增第二份 bit layout、也沒有改 adapter。
* **「接受但沒有 id」不可表示**：host 回 `true` 之後 scanner 一定拿同一個 `plotCell`
  編碼；candidate 與 accepted cell 不可能不同步。舊介面的 `out int` 正是允許
  「`true` + out 仍是初始 0」的縫。
* 為什麼拆成 `ResolvePlotCell` + `TryAcceptPlot` 兩步而不是一步回傳 cell：plant cell 的 z
  必須經 `TerrainService.GetTerrainHeight`（game 資料，只能在 host）；plot id 編碼必須
  在 Core（單一實作，只能在 scanner）。兩者分屬不同層，拆開後各自單一來源；scanner 先拿
  cell 再驗證，驗證通過即可編碼，host 端不再有機會產生 id。
* host 內部仍然會用 `HdbfPlotCellCodec.Encode(cell)` 算 id（
  `HdbfBoxFarmController.Scan.cs:186`）——但只用於 **excluded / claimed by other farm**
  這兩個既有過濾器（它們的資料結構本來就是 plot id），不是 candidate 的 id 來源；
  沒有第二份 layout。

既有語意不變：`TryAcceptPlot` 內 `_diagScanned/_scanStats.Scanned`、各 reject
計數、`_diagAccepted/_scanStats.Accepted` 都照舊；`ResolvePlotCell` 只多了「非地形 column
回傳水平 cell 讓 `TryAcceptPlot` 的 `Contains` 先拒絕」的防護，避免對地圖外 column 呼叫
`GetTerrainHeight`（與舊碼順序等價）。

### 2.2 逐格路徑容錯（不可達語意抽到 Core）

新檔 `src/HDBF.Core/Farming/FarmPathProbe.cs`（Core 純函式，無 game/Unity 依賴）：

| 成員 | 語意 |
| --- | --- |
| `FarmPathProbe.Unreachable = -1`（`:36`） | 「本期不可達」sentinel，永遠不是合法路徑長 |
| `IsReachable(int)`（`:39`） | `>= 0` 才可派工 |
| `FromQuery(bool found, float distance)`（`:50`） | game 回報的 float cell 數 → 向上取整；false/負/NaN/Inf → `-1` |
| `Measure(Func<int>? query, out Exception? failure)`（`:73`） | 執行逐格查詢；任何例外 → 回 `-1` 並回報 exception，**不往外丟** |

Gameplay（`src/HDBF.Gameplay/Components/HdbfBoxFarmController.Farming.cs`）：

* `ResolvePathLength`（`:642-660`）＝ cache 查詢 + `FarmPathProbe.Measure(() =>
  QueryPathLength(plotId), out var failure)`；失敗回 `-1`，並把 **`-1` 也 memoize**
  （`_farmPathByPlot[plotId] = pathLength`），所以同一份 plan 內每格最多查一次，逐幀
  slice 不會重試壞格。
* `QueryPathLength`（`:668-692`）是純 game-side 查詢（`FindRoadPath` →`FindTerrainPath`
  →`FindPathUnlimitedRange`），成功走 `FarmPathProbe.FromQuery`；查不到回
  `FarmPathProbe.Unreachable`。例外由 `ResolvePathLength` 的 `Measure` 接住。
* 不再呼叫 `ReportFarmFault`、不再 `_farmFaults++`、不再觸發 `_farmDisabled`：單格 path
  失敗不會中止 plan，其他格照常量測（`StepFarmPathMeasurements :335-373`，只有
  `FarmPathProbe.IsReachable` 的才進 `_farmPlan`）。
* 新增玩家可見 **`HDBF/Warn`**：`ReportPathUnreachable(plotId, failure)`（`:702-718`）：
  * key = `HdbfLogKeys.FarmPathUnreachable + "." + _entityId`（per-building throttle；新
    穩定 key `gameplay.path-unreachable`，見 `src/HDBF.Core/Logging/HdbfLogKeys.cs:59-68`），
    30 s 窗口內每棟建築最多一行，suppressed 次數依既有 `ThrottledLog` 規則附在下一行；
  * 訊息含 `entityId`、`plotId`、`cell=(x,y,z)`、`exception=<型別>: <message>`，
    **不含 stack、不是 `HDBF/Error`、不是 `farm loop fault`**。
* 其他逐格 path 呼叫檢查：scan 的 `IsReachable`（`Scan.cs:222-272`）本來就有 per-cell
  `catch` 回 `false`（屬於註冊過濾，不是 farm plan，未改）；farm plan 內逐格 path 呼叫
  只有 `ResolvePathLength` 一處。`ApplyDenseGrowthBoost` 等逐格 catch 非 path、維持原樣。
* plan 級別的 `ReportFarmFault` 仍保留給真的 plan 故障（work dispatch、planting
  resource、harvest accounting 等），語意未被放寬。

---

## 3. 改動清單（file:line，本卡結束時的檔案行號）

| 檔案 | 改動 |
| --- | --- |
| `src/HDBF.Core/Farming/FarmPathProbe.cs`（新） | `:29` 型別；`:36` `Unreachable`；`:39` `IsReachable`；`:50` `FromQuery`；`:73` `Measure` |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` | `:59-68` 新穩定 key `FarmPathUnreachable = "gameplay.path-unreachable"`（per-building 由呼叫端接 entityId） |
| `src/HDBF.Gameplay/Components/HdbfPlotScanner.cs` | `:19-42` 新 `IHdbfPlotScanHost`（`ResolvePlotCell` :28、`bool TryAcceptPlot` :36、不再有 out id）；`:104-143` `Step` 先解析再接受、`_candidates.Add(... Encode(plotCell) ...)` :132 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` | `:84-115` `ResolvePlotCell`（game-side plant cell z）；`:117-209` `TryAcceptPlot(Vector3Int, int)` 只回 bool（`:186` 仍用 Core id 做 excluded/claimed 過濾）；`:287-319` `OnPlotsScanned` 不變（`:291` Select、`:293` ApplyPlotArrays、`:301` diag） |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Farming.cs` | `:335-373` 量測 slice 只收可達格；`:642-660` `ResolvePathLength`（memoize + Measure，回 -1）；`:668-692` `QueryPathLength`（純查詢）；`:702-718` `ReportPathUnreachable`（per-building 節流 Warn，`:707` key） |
| `tests/HDBF.Tests/Bug04PlotIdAndPathTests.cs`（新） | 9 條新 L1 測試（見 §4）；`:276-319` game-free `FakeScanHost`（只實作 resolve/accept，不產生 id） |
| `tests/HDBF.Tests/HDBF.Tests.csproj` | `:31-35` 以 `<Compile Link>` 把真實的 `HdbfPlotScanner.cs` / `HdbfPlotCellCodec.cs` 編進 L1（與 `FarmWorkPlanner.cs` 同模式，無 game/Unity 參考） |
| `tests/HDBF.Tests/Stubs/UnityEngineVector3IntStub.cs` | 形狀改成 Unity 的 lowercase `x/y/z` properties（linked scanner/codec 需要讀座標） |
| `docs/workstreams/bug-04-plotid-and-path-fault.md`（新） | 本報告 |

**未動（依卡）**：`data/`、任何平衡數值、`HdbfBoxFarmController.Diagnostics.cs`
（仍在、`FarmDiagnostics = true`）、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、
`docs/workstreams/bug-01-plot-registration.md`、`PlotRegistrationHeightTests.cs`、UI/面板、
兼容適配器、灌溉取樣公式。0 Harmony patch、無新增 static 可變狀態、無 tier 平衡分支。

---

## 4. 新測試（9 條）與實際輸出

`tests/HDBF.Tests/Bug04PlotIdAndPathTests.cs`：

| # | 測試（:line） | 釘住的行為 |
| --- | --- | --- |
| 1 | `TestScannerBuildsOneCandidatePerAcceptedCellWithDistinctIds`（:49） | 真實 scanner 走 11×11：121 掃描 / 102 接受 → 102 candidates、102 個**不同** id、每個 id 等於其 accepted cell 的 Core 編碼；`Select(candidate, 60)` = 60（舊 bug 是 1） |
| 2 | `TestScannerIdsStayDistinctForNegativeCoordinates`（:111） | 掃描原點負座標（x∈-8..2、y∈-9..1）：121 格 → 121 個不同 id，decode 回正確負座標與 z |
| 3 | `TestCodecGivesDistinctIdsTo121ScanCellsIncludingBoundaries`（:142） | 純 Core：121 個 cell（含負座標、z=0/1/1023 邊界）encode/decode 往返一致、id 無碰撞；`Select(...,60)=60`、`Select(...,121)=121` |
| 4 | `TestRepeatedCandidatesForOneCellCollapseToASingleSlot`（:167） | 同一 cell 重複 3 個候選 → Select 仍只取 1 格 |
| 5 | `TestPathQueryFailureBecomesUnreachableInsteadOfThrowing`（:190） | `Measure` 內丟 `InvalidOperationException("There's no cached flow field at 1187869")` → 回 `-1`、例外不逸出、`failure` 型別可回報 |
| 6 | `TestMeasuredPathQueryKeepsItsLengthAndInvalidValuesAreUnreachable`（:205） | 成功值保留；負值/null query 一律 `-1` 且不算故障 |
| 7 | `TestPathQueryDistanceConversionRoundsUpAndRejectsInvalidValues`（:218） | float cell 數向上取整（4.2→5）、false/負/NaN/Inf → `-1` |
| 8 | `TestOneUnreachablePlotDoesNotAbortTheDailyPlan`（:234） | 1 個不可達 + 1 個可達：不可達排最後且被跳過，可達格仍被 `FindNext` 派工並帶真實 path cost → **單格失敗不中止計畫** |
| 9 | `TestFarmPathUnreachableLogKeyIsStable`（:264） | 新日誌契約 `gameplay.path-unreachable` 固定不漂移 |

實際輸出（本機，沙箱 `$env:TEMP\dsh-DPmqoe\hdbf-card-work`）：

```
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj              -c Release -m:1 -p:HdbfWorkRoot=$w
  -> 已成功生成。  0 个警告  0 个错误
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj      -c Release -m:1 -p:HdbfWorkRoot=$w
  -> 已成功生成。  0 个警告  0 个错误
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj          -c Release -m:1 -p:HdbfWorkRoot=$w
  -> 已成功生成。  0 个警告  0 个错误
dotnet build HDBF.sln                                     -c Release -m:1 -p:HdbfWorkRoot=$w
  -> 已成功生成。  0 个警告  0 个错误      （額外確認其他專案未受影響）

dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
  -> HDBF L1 test run - 2026-10-25T03:29:13.1008316Z   （本卡改動完成後的全量）
     PASSED 292  FAILED 0                               （283 基線 + 本卡 9）

  -> HDBF L1 test run - 2026-10-25T03:32:55.7441935Z   （同工作區另一張卡的
     PASSED 307  FAILED 0                                 CompatAnchorTableTests.cs（15 條）
                                                          併入後重跑；307 = 292 + 15）
```

基線是本卡開始前實測的 `PASSED 283 FAILED 0`；本卡新增 9 條 → 292，無回歸。
最後一次重跑是 **307 全綠**：其中 15 條是同一工作區另一張卡在 11:32 併入的
`tests/HDBF.Tests/CompatAnchorTableTests.cs`（Compatibility 卡，非本卡改動；本卡沒有
編輯該檔）。`dotnet --version` = `9.0.305`。

測試策略說明：`HdbfPlotScanner.cs`、`HdbfPlotCellCodec.cs` 以 linked source 編進
`HDBF.Tests`（沿用 `FarmWorkPlanner.cs` 的既有模式與 stub），所以測的是**真實 production
scanner** 的 candidate 建構路徑，不是重寫一份；stub 只提供 `UnityEngine.Vector3Int`
建構子與 `x/y/z`，無任何 Timberborn/Unity 組件被參考或載入（REQ-TEST-03）。若有人把
「host 回傳 id」的舊形狀加回來，測試組會編譯失敗或 distinct-id 測試直接紅。

---

## 5. 不變量與 accepted/selected/granted 語意（本卡 task 3）

`OnPlotsScanned`（`HdbfBoxFarmController.Scan.cs:287-319`）的資料流與計數定義：

| 計數 | 定義 | 現況保證 |
| --- | --- | --- |
| `accepted` | `TryAcceptPlot` 回 `true` 的格數（`_diagAccepted`/`_scanStats.Accepted`） | scanner 只在 `true` 後 append candidate → `candidates == accepted`（1:1，無其他 append 路徑；`Scan.cs:208-209` 與 `HdbfPlotScanner.cs:125-132`） |
| `candidates` | scanner 交給 Select 的 `PlotCandidate.Count` | 每格 id 由 accepted cell 決定且不同格不同 id（新測試 #1/#2/#3） |
| `selected` | `PlotSelection.Select(candidates, capacity, _record.PlotIds)`（`:291`） | `≤ min(capacity, distinct accepted)`；無重複 cell 且舊註冊 ⊆ 候選時 `== min(capacity, accepted)`；舊 id 會優先保留但數量不變 |
| `granted` | `IHdbfFarmRegistry.ApplyPlotSelection` 實際授予（`:292`） | `≤ selected`；只有「同格已被其他農場先註冊」才會更少（scan 的 `claimed` 過濾通常先擋掉；防禦性保留） |
| `registeredNow` | `ApplyPlotArrays(granted)` 後 `_record.PlotIds.Length`（`:293`、`:325`；snapshot/daily/diag 全部讀同一個 `PlotIds.Length`） | `== granted.Length`；`PlotCount => PlotIds.Length`，`ApplyPlotArrays` 同步對齊 `CropIds`/`Fatigue`，不存在第二個會漂移的 count |

**預期不變量**：掃描完成後（無其他農場佔用、無重複 cell）
`registeredNow == min(capacity, accepted)`，且不得是 1/0。
本卡 fixture 的實際值：Basic `PlotCapacity = 60`（`TierCatalog.Get(HdbfTier.Basic)`，
測試 #1 也釘住 60）、`accepted = 102` → **`registeredNow` 應為 60**。
round-9 的 `selected=1 granted=1 registeredNow=1` 正是舊 bug 的簽名，不是合法狀態。

---

## 6. 下一回合（round 10）實機驗證的預期值（Lead 執行）

先 `build.ps1 -Install` + 五程序集/L1/validate 門禁，再跑既有 fixture：
`Invoke-GameRound.ps1 -Action Start -Tag round10` → `SpeedUp -Seconds 180` → `Stop`。
grep `docs/in-game/round10-Player.log`：

1. **必須**：`diag.scan.end` 出現
   `scanned=121 accepted=102 candidates=102 selected=60 granted=60 registeredNow=60
   rejects[terrain=0,field=19,block=0,blocker=0,reach=0,excluded=0,claimed=0]`
   （`field=19` 之外的 reject 分佈可能因 fixture 佈局微調，重點是 `selected`/`granted`/
   `registeredNow` 三者皆 **60**；若 `capacity` 被 config 覆蓋則為其值）。
2. **必須**：`diag.day` 出現 `registered=60 moist=60 dry=0 tankFed=0 ... coverage=1.000`
   （全濕 fixture；`moist`/`coverage` 由 BUG-01 的即時重算 + z 語意保證；本卡只負責
   讓註冊格是 60 個「真格」）。
3. **必須消失**：任何 `[HDBF/Error] gameplay.farm: farm loop fault (path length)`；
   `_farmFaults` 不再因 path 查詢增加（`FarmFaultsBeforeDisable` 不會被 path 觸發）。
4. **允許且預期可能出現**（不是回歸）：`[HDBF/Warn] gameplay.path-unreachable.<entityId>:
   farm path query failed; plot treated as unreachable for this plan; entityId=...,
   plotId=..., cell=(x,y,z), exception=System.InvalidOperationException: There's no cached
   flow field ...`。注入 fixture 的假路網仍可能沒有 flow field；此 Warn 每棟建築每 30 s
   最多一行，代表「該格本期不派工、其餘照常」，farm plan 不會被中止。若 round-10 只有這行
   Warn 而沒有 Error farm fault，且 `registeredNow=60 / coverage=1.000`，即本卡兩個成因
   都已在實機環境下失效。
5. 若 `registeredNow` 仍是 1：部署包沒更新（核對部署的 `HDBF.Gameplay.dll` 時間戳/hash，
   或 `diag.scan.end` 是否還是舊行為）；若是 2–59：看同一行 `selected`/`granted` 與
   `rejects[claimed=...]`，那是另一農場先佔或舊存檔 claim，屬預期 first-come-first-served。
6. 面板灌溉覆蓋率應在掃描完成後即非 0（BUG-01 的即時重算，與本卡獨立）。

---

## 7. 明確不聲稱 / 未做

* 不聲稱「實機已修好」：本卡只做「依 round-9 日誌定位 → 程式層結構性修掉 → L1 證明」；
  round 10 才會有實機證據。
* 未部署、未啟動 Timberborn、未跑 `build.ps1`、未動存檔/`data/`/平衡數值/UI/適配器/
  `docs/VERIFICATION.md`/`docs/REQ_TRACEABILITY.md`/BUG-01 報告。
* 未移除 `HdbfBoxFarmController.Diagnostics.cs`（Lead 的實機取證管道，保持 `true`）。
* round 10 若 path-unreachable Warn 仍存在，代表「注入 fixture 沒有真實路網」這個
  **原本就存在**的環境限制還在，不是本卡的回歸；派工→播種→收穫→入庫的作物循環仍是
  後續回合（`docs/VERIFICATION.md` §16.4 step 4）。
* 本卡進行中有另一張卡（Compatibility，`src/HDBF.Core/Compat/*`、
  `src/HDBF.Integration/*`、`tests/HDBF.Tests/CompatAnchorTableTests.cs` 等）在同一工作區
  並行改動；那些檔案不是本卡改的，本卡沒有編輯它們（唯一交會點是
  `HdbfLogKeys.cs`：本卡新增 `FarmPathUnreachable`，另一卡新增 `CompatEnabled`，兩者並存、
  編譯與 L1 全綠）。
