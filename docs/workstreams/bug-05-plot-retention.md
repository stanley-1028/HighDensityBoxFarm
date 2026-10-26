# BUG-05 (P0) — 每日重掃把「作物佔格的已註冊格」當成不可種植格丟棄，註冊表歸零

Status: **程式層修復完成、L1 全綠；實機未驗證**（本卡禁止部署、禁止啟動 Timberborn、
禁止 `build.ps1`）。下一回合（round 12）由 Lead 建置/部署後實機確認。
卡日期：2026-10-25。只改程式與測試。

誠實一句話：成因用 round-11 實機日誌（`docs/in-game/round11-Player.log`）＋現行原始碼定位，
並在程式層結構性修掉；新增 15 條 L1 回歸／契約測試，全量 L1 **PASSED 342 FAILED 0**。
**沒有**聲稱「實機已修好」——那要等 round 12 的 `diag.scan.end` 顯示
`registeredNow=60`、穩態 `kept=60 added=0 dropped=0`、載入首掃 `coverageAfterScan=1.000`、
`plot.scan-empty` 消失，以及 `harvested plot ...` 與產物入庫出現。

---

## 1. 實機證據（round 11，2026-10-25 11:48–11:54）

日誌：`docs/in-game/round11-Player.log`（13,934 B，120 行；Lead 已存）。每次掃描完成的
`diag.scan.end` 依時間序（行號 = 該檔行號）：

```
:83  [HDBF/Info] diag.scan.begin...: origin=(130, 139, 3) side=11 entrance=True ...
:86  diag.scan.end: scanned=121 accepted=103 candidates=103 selected=60 granted=60
       registeredNow=60 rejects[terrain=0,field=18,block=0,blocker=0,reach=0,excluded=0,claimed=0]
:87  diag.scan.probe: canPlant=103 ...
:88  diag.farm.plan: candidates=60 queued=60 accessible=1 entrance=(130,139,3)
       reachSelf=1 pathSelf=throw:InvalidOperationException
:90  diag.day: day=2.18 registered=60 moist=40 dry=20 tankFed=0 tank=0.00 coverage=0.667 ...

:92  diag.scan.end: scanned=121 accepted=62 candidates=62 selected=60 granted=60
       registeredNow=60 rejects[terrain=0,field=55,block=4,...]
:93  diag.scan.probe: canPlant=66 ...
:94  diag.farm.plan: candidates=41 queued=41 ...
:95  diag.day: day=3.18 registered=60 moist=35 dry=25 coverage=0.583 ...

:97  diag.scan.end: scanned=121 accepted=25 candidates=25 selected=25 granted=25
       registeredNow=25 rejects[terrain=0,field=79,block=17,...]
:99  diag.farm.plan: candidates=7 queued=7 ...
:100 diag.day: day=4.18 registered=25 moist=13 dry=12 coverage=0.520 ...

:102 diag.scan.end: scanned=121 accepted=0 candidates=0 selected=0 granted=0
       registeredNow=0 rejects[terrain=0,field=87,block=34,...]
:104 [HDBF/Warn] plot.scan-empty: no usable managed plots were registered; ...
       entityId=d90c9962-... registered=0 scanned=121 accepted=0
       rejects[terrain=0,field=87,block=34,blocker=0,reach=0,excluded=0,claimed=0]
:105 diag.farm.plan: candidates=0 queued=0 ...
:106 diag.day: day=5.18 registered=0 moist=0 dry=0 coverage=0.000 ...
:108 / :116 之後每次重掃都是 scanned=121 accepted=0 registeredNow=0
:109 / :117 diag.scan.probe: canPlant=37（作物長大後更多格被佔）
```

判讀（與卡一致）：

1. **第一段（:86）證明 BUG-04 的 plotId/路徑容錯修復在實機生效**：`registeredNow=60`、
   `farm.plan candidates=60`、`pathSelf=throw` 已被 BUG-04 的 fallback 接住；隨後
   `canPlant 103→66→42→34`、`block 0→4→17→34`、`farm-plan candidates 60→41→7` 代表
   **P1 播種循環真的把作物種到託管格上**（間接證據）。
2. **但每日重掃把「已有作物的已註冊格」當成不可種植格過濾掉**：`field`（CanPlant 拒絕）
   `18→55→79→87`、`block`（AnyObjectAt 拒絕）`0→4→17→34`，`accepted 103→62→25→0`，
   `registeredNow 60→60→25→0`。day 3 的 `registeredNow=60` 只是「候選 62 ≥ 容量 60」時
   用別的空格補滿（plot id 已悄悄換人），day 4 掉到 25、day 5 歸零。
3. **歸零即不可收穫**：`:104` `plot.scan-empty`、`:105` `farm.plan candidates=0`、
   `:106` coverage 0.000，`diag.scan.probe canPlant=37` 證明地圖上仍有可種格，但農場已沒有
   任何託管格可派工——播種/收穫都不可能。
4. **`diag.day moist=40 dry=20`（day 2）是正常水分衰減**，不是 bug（全濕 fixture 經過 1–2 個
   遊戲日自然衰減）；但掃描剛完成時的即時覆蓋率在 round 11 沒有日誌可證明 — 本卡補
   `coverageAfterScan=`（見 §3.3）。

---

## 2. 根因：重掃對「已註冊格」重跑「新格」資格判斷

`TryAcceptPlot`（pre-fix 與現行新格路徑相同，現行行號 `HdbfBoxFarmController.Scan.cs:191-265`）
對掃描到的**每一格**依序做：

| 步驟 | 程式位置（現行行號） | 對「已註冊且長著作物」的格子 |
|---|---|---|
| 1. `TryGetScanCropTemplate` | `Scan.cs:197` | 通過（作物模板存在） |
| 2. `_terrainService.OnGround(cell)` | `Scan.cs:209` | 通過（IL：OnGround 只看地形，`IsTerrainVoxel(Below) && !IsTerrainVoxel(cell)`） |
| 3. `_plantingAreaValidator.CanPlant(cell, scanCrop)` | `Scan.cs:216` | **false** → `field` reject（作物佔格，Fields 驗證器不給種） |
| 4. `_terrainService.Contains(cell)` | `Scan.cs:223` | 通過 |
| 5. `_blockService.AnyObjectAt(cell)` | `Scan.cs:231` | **true** → `block` reject（作物本身就是 block object） |
| 6. `_plantingService.TryGetPlantingBlocker(cell, ...)` | `Scan.cs:239` | 可能也 true（作物/植披佔用） |

也就是說：**一個已經註冊、長著作物的格子，用「新格」規則重掃時永遠不會被接受**。它每天
必須重新以「空格」身分被發現；一旦作物蓋住它，它就從 candidate 集合消失：

```
TryAcceptPlot 每格只看「現在能不能種」→ 作物佔格的已註冊格 → false
→ HdbfPlotScanner 不產生 candidate
→ PlotSelection.Select 只保留「仍是 candidate」的 previous id（PlotSelection.cs:71-82）
→ 該已註冊格被 dropped（ApplyPlotArrays 重寫 _record.PlotIds，Scan.cs:498-545）
```

作物越長越多 → 候選越少 → 註冊表 `60 → 60（換人補滿）→ 25 → 0`。歸零後
`plot.scan-empty` 出現、`farm.plan candidates=0`，播種/收穫全部停止。這是候選**資格判斷**
的設計錯誤，不是選擇器或註冊表問題（`PlotSelection` 的「previous 優先保留」語意本來就
存在，只是 previous 根本沒進 candidate）。

---

## 3. 修法

### 3.1 Core：一條可測的保留規則 `PlotRetention`

新檔 `src/HDBF.Core/Plots/PlotRetention.cs`（純 Core，無 game/Unity 依賴）：

| 成員 | 行 | 語意 |
|---|---|---|
| `enum PlotDropReason` | `:11` | `None` / `TerrainMissing` / `OutOfRange` / `ExcludedByPlayer` / `ClaimedByOtherFarm` / `OccupiedByNonCrop`——**這 6 個值就是已註冊格唯一合法的移除理由集合** |
| `readonly struct PlotRetentionState` | `:49` | 5 個 disqualifier flag；**default（全 false）= 作物佔格 = 必須保留**（這正是 round-11 的狀態，文件明講） |
| `readonly struct PlotRetentionObservation` | `:76` | 一次重掃看到的單格：plotId + state |
| `PlotRetention.Evaluate` | `:115` | 依文件化優先序（terrain > range > excluded > other-farm > non-crop）回傳 drop reason |
| `PlotRetention.ShouldKeep` | `:146` | `Evaluate(...) == None`；gameplay 與 L1 共用同一條規則 |
| `PlotRetention.Retain` | `:163` | 批次形式：輸入 `previousIds` + 觀測到的 state，輸出保留集合；**保持 previous 順序**、**容量上限不超**、重複 id 去重；`previousIds` 中沒被觀測到的 id（超出工作範圍）視為 `OutOfRange` 丟棄（`:196`、`:204`） |

### 3.2 Gameplay：重掃走「保留路徑」

檔案：`src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs`。

1. **一次掃描一次的 HashSet 快取**：`_registeredWhenScanBegan`（`:33`）在 `StartScan()`
   （`:87`）呼叫 `SnapshotRegisteredPlots()`（`:112-125`）時填入，之後每格只做一次
   `HashSet.Contains`（`:186`），沒有逐格線性搜尋 `_record.PlotIds`。
2. **已註冊格改走保留路徑**：`TryAcceptPlot` 在算出 plot id 後（`:174`）先判斷
   `_registeredWhenScanBegan.Contains(id)`（`:186-189`），是則直接 `TryAcceptRetainedPlot`
   （`:286`），**在 crop-template / OnGround / CanPlant / AnyObjectAt / planting-blocker
   全部之前 return**——這些判定正是「作物佔格」會踩到的。
3. **保留路徑只保留/只丟允許的理由**（`:286-337`）：

   | 檢查 | 行 | 結果 |
   |---|---|---|
   | `!Contains(cell) || !OnGround(cell)` | `:292` | `TerrainMissing` → 丟（地形消失/高度改變，儲存格不再是地表格） |
   | `_excluded.Contains(plotId)` | `:293` | `ExcludedByPlayer` → 丟（玩家排除，REQ-IMPL-10） |
   | `_registry.IsPlotClaimedByOther(plotId, _entityId)` | `:294-295` | `ClaimedByOtherFarm` → 丟（他場先佔，REQ-BLD-04） |
   | `IsOccupiedByNonCrop(cell)` | `:296-297`、`:346-371` | `OccupiedByNonCrop` → 丟（道路/建築等非作物物件；見下） |
   | 以上皆否 | `:307` | `PlotRetention.ShouldKeep(state)` → 接受 |

   * **作物佔格必須保留**：`IsOccupiedByNonCrop` 的判定是
     `AnyObjectAt(cell) && GetFirstObjectWithComponentAt<Crop>(cell) == null`——
     `AnyObjectAt` 對作物為 true，但作物有 `Crop` component（同一個 probe 在
     `Farming.cs:456`/`:530` 用於收穫分類），因此**本農場作物在格上時不丟**；
     只有找不到 Crop component 的 block object（道路/建築/堆場）才丟（`:332-333`，
     計入 `block` reject）。round-11 的 `accepted=25 registered=25` / `farm.plan candidates=7`
     已間接證明 `Crop` probe 在實機找得到成長中的作物（farm plan 以同一個 probe 分類）。
   * **不確定時寧可保留**：probe 丟例外時回 `false`（保留），並每次掃描最多記一條
     `HDBF/Warn`（`_retentionProbeWarned` `:39`、`:361-370`，在下一次掃描的
     `ResetScanCounters` `:548-551` 重置），絕不因 probe 失敗整批丟棄。
   * **超出工作範圍**：不需額外程式——scanner 只會造訪範圍內格子（`HdbfPlotScanner.cs:117-133`），
     範圍外的已註冊 id 不會成為 candidate，`PlotSelection` 自然不保留（= `OutOfRange`）。
   * **可達性刻意不重檢**：作物不是導航障礙，註冊時已通過的可達性不會因長出作物而改變；
     對已註冊格重跑導航過濾正是這張卡要修掉的「脆弱過濾器整批清空」風險。真的被圍死的格
     由 BUG-04 的 farm plan 逐格容錯跳過（`FarmPathProbe`，`Farming.cs:643-661`），身份與
     水分取樣仍保留。此點與 `docs/NEXT_ROUND.md:95-96` 的「only ... / unreachable / ... may
     drop」不衝突：那只是**允許**的移除理由清單，本卡實作的是其子集（見 §8 誠實偏離）。
4. **容量上限與「已註冊格優先」語意不變**：`PlotSelection.Select` 本來就會優先保留 previous
   （`PlotSelection.cs:71-82`），候選集合修好後自動生效；容量仍由
   `take = min(capacity, distinct candidates)` 上限（`PlotSelection.cs:67`）。`ApplyPlotArrays`
   依 id 保留 crop/fatigue（`Scan.cs:498-545`，REQ-IMPL-07：重掃不換身份）。

### 3.3 可觀測性：`diag.scan.end` 新增 `kept=/added=/dropped=/coverageAfterScan=`

* `PlotScanStats`（`src/HDBF.Core/Plots/PlotScanStats.cs`）新增 production 計數
  `Kept`（`:66`）/`Added`（`:69`）/`Dropped`（`:72`）與
  `RecordSelection(previousIds, selectedIds)`（`:81-119`）：
  * `kept = |previous ∩ selected|`、`added = |selected| - kept`、`dropped = |previous| - kept`；
  * 以 HashSet 去重，`kept + added = |selected distinct|`、`kept + dropped = |previous distinct|`；
  * `Reset()`（`:136`）同時清零，與其他 per-scan counter 同步。
* **診斷片段的欄名/順序/數字格式住在 Core**：`DescribeSelectionDelta()`（`:128-135`）回傳
  固定 `kept=<n> added=<n> dropped=<n>`（invariant 數字），由 L1 直接釘住字串，避免
  round-12 的 grep 契約漂移（review 指出的文化格式風險一併消除）。
* `OnPlotsScanned` 在 `PlotSelection.Select` 之後、`ApplyPlotArrays` 之前呼叫
  `_scanStats.RecordSelection(previousIds, selected)`（`Scan.cs:461-463`），所以計數與
  selection 用**同一份** previous/selected 陣列。
* `DiagScanEnd`（`HdbfBoxFarmController.Diagnostics.cs:65-101`）在既有
  `registeredNow=` 之後追加（`:81-85`）：

  ```
  ... registeredNow=60 kept=60 added=0 dropped=0 coverageAfterScan=1.000 rejects[...]
  ```

  `coverageAfterScan` 讀的是 `RefreshCoverageFromRegisteredPlots()`（`Scan.cs:472`，BUG-01 的
  掃描後即時重算）剛寫入的 `_coverage`，並用 `InvariantCulture` 固定小數點
  （`Diagnostics.cs:83-84`），所以下一輪可直接證明「掃描完成時 60/60 且覆蓋率 1.000」——
  round 11 缺的正是這一行。

---

## 4. 改動清單（file:line，本卡結束時的檔案行號）

| 檔案 | 改動 |
|---|---|
| `src/HDBF.Core/Plots/PlotRetention.cs`（**新**） | `:11` `PlotDropReason`；`:49` `PlotRetentionState`；`:76` `PlotRetentionObservation`；`:115` `Evaluate`；`:146` `ShouldKeep`；`:163` `Retain`（順序/容量/去重/未觀測=OutOfRange，`:196`、`:204`） |
| `src/HDBF.Core/Plots/PlotScanStats.cs` | `:66-72` `Kept/Added/Dropped`；`:81-119` `RecordSelection`；`:128-135` `DescribeSelectionDelta`（diag 片段契約）；`:136` `Reset` 同步清零 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` | `:33` `_registeredWhenScanBegan`；`:39` `_retentionProbeWarned`；`:87` StartScan 快照呼叫；`:112-125` `SnapshotRegisteredPlots`；`:161-189` `TryAcceptPlot` 保留分支；`:286-337` `TryAcceptRetainedPlot`；`:346-371` `IsOccupiedByNonCrop`；`:461-463` `RecordSelection`；`:548-551` 每掃描重置 warn flag |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Diagnostics.cs` | `:81-85` `kept=/added=/dropped=/coverageAfterScan=`（delta 由 Core 產生、coverage invariant；僅新增診斷欄位，檔案保留、`FarmDiagnostics = true` 不變） |
| `tests/HDBF.Tests/Bug05PlotRetentionTests.cs`（**新**） | 15 條 L1 測試（見 §5）；game-free `RetentionScanHost`（`:524`）以真實 linked `HdbfPlotScanner` + 真實 `PlotSelection` 驗證整合；`:398` production source contract |
| `tests/HDBF.Tests/HDBF.Tests.csproj` | `:45-58` 把 controller 的 `Scan.cs`/`Diagnostics.cs` 以 `EmbeddedResource` 嵌入 L1，給 anti-revert source contract 測試讀取（不編譯、不引用 game assembly） |

**未動（依卡）**：`data/` 任何平衡數值、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、
`docs/workstreams/bug-04-plotid-and-path-fault.md`、`tests/HDBF.Tests/Bug04PlotIdAndPathTests.cs`、
`tests/HDBF.Tests/PlotRegistrationHeightTests.cs`、灌溉公式、UI、兼容適配器、D-2 補丁。
0 Harmony patch；沒有新增 static 可變狀態（新狀態全部是 instance 欄位；`PlotRetention` 是
無狀態 static 純函式）；沒有依 tier 分支寫平衡數值。

---

## 5. 新測試（15 條）與實際輸出

`tests/HDBF.Tests/Bug05PlotRetentionTests.cs`（新檔，未覆蓋他人測試檔）：

| # | 測試（:line） | 釘住的行為 |
|---|---|---|
| 1 | `TestRetentionKeepsRegisteredPlotOccupiedByItsOwnCrop`（`:47`） | default state（作物佔格）→ `DropReason.None` / `ShouldKeep == true`（這是 round-11 的狀態） |
| 2 | `TestRetentionDropsPlotTakenOverByRoadOrBuilding`（`:56`） | `OccupiedByNonCrop` → 丟（不可無限保留） |
| 3 | `TestRetentionDropsTerrainMissingOutOfRangeExcludedAndClaimedPlots`（`:65`） | 地形消失/超範圍/玩家排除/他場佔用各自對應 drop reason |
| 4 | `TestRetentionPrecedenceIsDeterministic`（`:78`） | 多個 flag 同時成立時的 drop reason 優先序固定（診斷契約） |
| 5 | `TestRetainKeepsAll60CropOccupiedRegisteredPlots`（`:120`） | **核心不變量**：60 個已註冊格 + 全部長出作物 → `Retain` 仍回 60、順序不變 |
| 6 | `TestRetainDropsRoadOccupiedAndUnobservedPlots`（`:133`） | 被道路佔用的丟、掃描沒看到的（超範圍）丟，其餘 58 保留 |
| 7 | `TestRetainRespectsCapacityAndPreviousOrder`（`:164`） | 容量 50 → 只留前 50 且保持 previous 順序；容量 0 → 空；`null` previous → 空 |
| 8 | `TestRetainIgnoresCellsThatWereNeverRegistered`（`:184`） | 新格不會被「保留」；retention 只涵蓋 previous |
| 9 | `TestRetainBatchAgreesWithPerCellRuleForEveryReason`（`:197`） | 批次 `Retain` 與逐格 `ShouldKeep` 對每一種 state 都一致（防兩份規則漂移） |
| 10 | `TestScanStatsRecordSelectionReportsKeptAddedDropped`（`:227`） | `kept=60/added=0/dropped=0`、換格時 `58/2/2`、`Reset` 清零（diag 契約） |
| 11 | `TestScanStatsDescribeSelectionDeltaPinsTheScanEndFragment`（`:261`） | diag 片段字串**逐字**釘住 `kept=60 added=0 dropped=0` / `kept=58 added=2 dropped=2`（round-12 grep 契約） |
| 12 | `TestRescanWithAllCropsGrownStillSelectsTheSame60`（`:292`） | **round-11 重演**：真實 scanner + 真實 selector，60 已註冊格全部被作物佔用、其餘 61 格不可種 → 60 candidates → selected 仍是同樣 60 且同序；同一 fixture 用「舊規則」（無 retention）→ 0 candidates / 0 selected = round-11 `plot.scan-empty` 簽名 |
| 13 | `TestRescanDropsRoadPlotAndBackfillsFromNewCells`（`:325`） | 1 格被道路佔用 → 丟；空出的容量由 1 個全新可種格補上 → `kept=59 added=1 dropped=1` |
| 14 | `TestRescanDropsOutOfRangeRegistrationAndKeepsTheRest`（`:356`） | 1 個已註冊 id 落在工作範圍外（掃不到）→ 不復活；其餘 59 保留，`dropped=1` |
| 15 | `TestProductionRetentionSourceContractIsStillWired`（`:398`） | **anti-revert 契約**（controller 無法編進 L1）：嵌入的 production `Scan.cs`/`Diagnostics.cs` 必須仍有「已註冊分支 → 在 CanPlant/AnyObjectAt/blocker 之前」、`PlotRetention.ShouldKeep`、`GetFirstObjectWithComponentAt<Crop>`、`RecordSelection` 在 `ApplyPlotArrays` 之前，以及 diag 四個欄位；**刪掉真正的修復會讓此測試紅** |

測試策略：`HdbfPlotScanner` 仍是 BUG-04 的 `<Compile Link>` 模式（`tests/HDBF.Tests/HDBF.Tests.csproj:33`），
測到的是**真實 production scanner** 的 candidate 建構；`PlotSelection` 是 Core 真實實作；
host double 只提供 game side（resolve/accept）並把格子狀態交給**同一條**
`PlotRetention.ShouldKeep` 規則。controller itself 不可進 L1，所以用 `EmbeddedResource` +
source contract 測試防止「刪掉修復仍全綠」（§5.2 有實證）。

### 5.1 實際輸出（本機，沙箱 temp work root；最終驗收用 `$env:TEMP\hdbf-bug05-final` 從零建置）

```
$ dotnet --version
9.0.305

$ dotnet build src\HDBF.Core\HDBF.Core.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
    0 个警告
    0 个错误
$ dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
    0 个警告
    0 个错误
$ dotnet build tests\HDBF.Tests\HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
    0 个警告
    0 个错误

$ dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
HDBF L1 test run - 2026-10-25T04:22:52.4260804Z      # 最終驗收：全新 work root 從零建置後重跑
PASSED 342  FAILED 0
```

基線說明（誠實）：本卡開始時的基線是 **307**（`PASSED 307 FAILED 0`，11:58 實測）。
本卡進行中同一工作區有另一張卡 **BUILD-01（面板 已註冊格/有效格/可開墾格）** 並行加入
`tests/HDBF.Tests/PanelPlotsContractTests.cs`（20 條）→ 327；再加上本卡 15 條 = **342**。
`342 = 307 + 20 + 15`，**既有 307 條全部仍綠**（任何一條紅都會出現在 `FAILED` 清單而不是
被吃掉）。

獨立驗證（`bug05-verifier` teammate，自己的 temp work root，不共用輸出）：在 13 條版本上
Core / Gameplay / Tests 各 `0 warnings, 0 errors`、L1 `PASSED 340 FAILED 0` 連跑兩次，
並獨立核對 340 = 307 + 20 + 13 的來源。review 處置後**再次獨立重驗**目前樹：三個專案
`0 warnings / 0 errors`、L1 `PASSED 342 FAILED 0`、獨立重數 15 條 BUG-05 測試；
並且（a）確認兩個新測試真的被 discovery 執行、（b）確認 `HDBF.Tests.dll` 內嵌的
production `Scan.cs`/`Diagnostics.cs` 與工作區檔案**逐位元組一致**（不是讀到舊副本）、
（c）在 scratch copy 重現 mutation proof：刪 retained branch → build 0 error、`PASSED 341
FAILED 1`（唯一失敗就是 source contract）、（d）受保護檔 29/29 不變、workspace 無 `bin/obj`。

### 5.2 anti-revert 實證 + L1 不能覆蓋的部分

**anti-revert scratch copy 實證**（review 指出的 MAJOR proof gap）：
複製整棵工作區到 `%TEMP%`（不污染 workspace），只刪掉 `Scan.cs` 的 retained branch：

```
scratch copy: retained branch deleted (chars 26728 -> 26561)
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj ... -> 0 个警告 / 0 个错误
HDBF L1 test run - ... 
PASSED 341  FAILED 1
  FAIL Bug05PlotRetentionTests.TestProductionRetentionSourceContractIsStillWired:
       TestFailureException: Scan.cs must still branch on the per-scan registration snapshot (BUG-05 retained path)
```

（review 先前已在另一個 scratch copy 證明：只刪 branch 而沒有 source contract 時，舊的
13 條測試仍是 `PASSED 340 FAILED 0`——所以 #15 是必要的防線，不是裝飾。）

L1 仍**不能**覆蓋、交由 round 12 實機證明：

* game-service probe 的**實際回傳**：`OnGround`/`Contains`、`AnyObjectAt`、
  `GetFirstObjectWithComponentAt<Crop>`（本卡用來判定「作物而非道路/建築」的關鍵）、
  `IsPlotClaimedByOther`、`_excluded` 的真實接線、存檔載入流程。
* controller 本身（partial 含 Timberborn 型別）不可執行；source contract 只能釘住原始碼
  標記與順序，不能釘住 runtime 行為。
* round-12 日誌本身：`registeredNow/kept/added/dropped/coverageAfterScan/plot.scan-empty/
  harvested plot` 與產物入庫。
* 效能/frame budget 的實測值（本卡只做複雜度論證：新格路徑不變；已註冊格路徑用
  `HashSet.Contains` O(1) + 最多一次 `AnyObjectAt`+`GetFirstObjectWithComponentAt<Crop>`，
  且**跳過**了原本較貴的 `CanPlant` 與導航查詢，所以每掃描成本反而低於 pre-fix）。

---

## 6. 不變量 / 語意（round 12 判讀依據）

| 計數 | 定義（本卡後） | 保證 |
|---|---|---|
| `scanned` | scanner 造訪格數（Basic 11×11 = 121） | 不變 |
| `accepted` | `TryAcceptPlot` 回 true 的格數 = 保留路徑接受 + 新格接受 | 作物佔格的已註冊格會計入 accepted |
| `candidates` | scanner 交給 selector 的 `PlotCandidate.Count` | = accepted（1:1，無其他 append 路徑） |
| `kept` | `previous ∩ selected` | 60 個已註冊格 + 全部作物佔格 + 容量 60 → **60** |
| `added` | `selected` 中不在 previous 的 id 數 | 穩態 **0** |
| `dropped` | `previous` 中不在 selected 的 id 數 | 穩態 **0**（有道路/地形/排除/他場/超範圍才會 > 0，且同步反映在 rejects[]） |
| `selected` | `PlotSelection.Select(candidates, capacity, previousIds)` | ≤ `min(capacity, distinct candidates)`；previous 先保留、順序不變 |
| `granted` | `_registry.ApplyPlotSelection` 實際授予 | ≤ selected；只有他場在掃描後搶先註冊才會更少（既有 race，round-11 未觸發；可看 rejects[claimed] 與 kept/dropped 對照） |
| `registeredNow` | `_record.PlotIds.Length`（ApplyPlotArrays 後） | = granted.Length；穩態 **60** |
| `coverageAfterScan` | 掃描後 `RefreshCoverageFromRegisteredPlots()` 的 `_coverage` | 載入後第一次掃描、fixture 濕度仍全 16 時 **1.000**；之後自然衰減（round-11 day2 `0.667` 是水分衰減，不是 bug；tank 一直是 0.00，不會補回 1.000） |

**round-12 預期的兩個掃描階段（重要，見 §7）**：

```
# (1) 載入後第一次掃描：fixture 農場沒有 HDBF 存檔記錄（見 §7），是全新註冊 →
diag.scan.end: scanned=121 accepted=103 candidates=103 selected=60 granted=60
  registeredNow=60 kept=0 added=60 dropped=0 coverageAfterScan=1.000 rejects[...]
# (2) 之後每一次每日重掃：保留 60、其他空格照常可為新候選 →
diag.scan.end: scanned=121 accepted≈103 candidates≈103 selected=60 granted=60
  registeredNow=60 kept=60 added=0 dropped=0 coverageAfterScan≈0.6xx（自然衰減） rejects[...]
```

`accepted/candidates` 在穩態會回到 ~103（保留的 60 格 + 其他從未註冊的空格），**重點是
selected/granted/registeredNow 三者皆 60、kept=60 added=0 dropped=0**；`rejects[]` 的
`field/block` 數字不會再造成 dropped（retained 格根本不走那些 filter）。**不要**用單一
grep 要求同一行同時出現 `kept=60` 與 `coverageAfterScan=1.000`——依 fixture 事實那不可能
（第一掃 kept=0；之後 coverage 已衰減）。

---

## 7. 下一回合（round 12）實機驗證的預期值（Lead 執行）

先 `build.ps1 -Install`（五程序集/L1/validate 門禁），再跑既有 fixture：`Invoke-GameRound.ps1
-Action Start -Tag round12` → `SpeedUp -Seconds 240`（跑 6+ 遊戲日）→ `Stop`。grep
`docs/in-game/round12-Player.log`。

**先講清楚 fixture 事實（避免把正確修復誤判成失敗）**：`tools/dev/fixtures/craft-farm-fixture.py:71-84`
注入的農場 entity 只有 vanilla components（`PausableBuilding`/`Workplace`/`BlockObject`/
`WorkplaceWorkerType`/`Emptiable`），**沒有 HDBF 控制器的存檔記錄**；載入後
`HdbfEntitySaveBridge` 走空記錄 fallback（round-11 `tank=0.00` 佐證），所以
`_record.PlotIds` 初始為空，載入後第一次掃描一定是**全新註冊**：`kept=0 added=60`，
而此時 fixture 水分仍全 16 → `coverageAfterScan=1.000`。之後每日掃描才是
`kept=60 added=0 dropped=0`，但水分已自然衰減、tank 也不會補水 → `coverageAfterScan≈0.6xx`
是預期值。round-11 首掃 `accepted=103` 同樣是全新註冊的候選數。

驗證清單：

1. **必須**（載入第一掃）：
   `diag.scan.end ... registeredNow=60 kept=0 added=60 dropped=0 coverageAfterScan=1.000
   rejects[terrain=0,field=18,...]`（候選數 ~103，與 round-11 首掃一致）。
2. **必須**（之後每一次每日重掃）：
   `diag.scan.end ... registeredNow=60 kept=60 added=0 dropped=0`；
   `accepted/candidates` 回到 ~103（=60 retained + ~43 從未註冊的空格），這是正確行為。
   round-11 的簽名 `registeredNow=25/0`、`kept<60` 不得再出現。
3. **必須**：`[HDBF/Warn] plot.scan-empty` **消失**（出現即代表又歸零，見下方失敗判讀）。
4. **必須**：`diag.farm.plan` 的 `candidates/queued` 持續 > 0（有託管格可派工）；
   6+ 遊戲日內出現 `gameplay.farm: harvested plot <plotId> crop <Crop> rotation=...`
   且產物進入 `SimpleOutputInventory`（P1 作物循環）。
5. **允許**：`gameplay.path-unreachable.<entityId>` 的 per-building Warn（round-11 已知的
   注入 fixture 沒有真實 flow field；BUG-04 已把它降級為逐格跳過）、`farm.plan
   pathSelf=throw...` 診斷（不影響派工），以及 day2+ `coverageAfterScan` 自然衰減。
6. **不得**：`[HDBF/Error] gameplay.farm: farm loop fault ...`、`plot.scan-empty`、
   `registeredNow < 60` 且 `dropped > 0` 卻沒有任何 `rejects[]` 解釋。

失敗判讀（依新欄位）：

| 觀察 | 判讀 |
|---|---|
| `registeredNow` 仍是 25/0，且**沒有** `kept=/added=/dropped=` 欄位 | 部署包沒更新（舊 DLL）；核對部署的 `HDBF.Gameplay.dll` 時間戳/hash |
| 載入首掃 `kept=60`（而不是 `kept=0 added=60`） | 存檔裡其實有 HDBF 記錄（不是本 fixture）；不影響正確性判斷，但要用對應的預期值 |
| `kept=60 dropped=0` 但 `registeredNow < 60` | `granted < selected`：其他農場在掃描後搶先註冊；看 `rejects[claimed]`（既有 race，非本卡） |
| `kept < 60`、`dropped > 0`、`rejects[block>0]` | `IsOccupiedByNonCrop` 把作物誤判為非作物（`GetFirstObjectWithComponentAt<Crop>` 沒找到）；檢查是否有 `plot retention crop probe failed` Warn；這是 round-12 最需要盯的 in-game 風險 |
| `kept < 60`、`dropped > 0`、`rejects[terrain>0]` | 儲存格 z 不再是地表格（地形被改/填高）—合理移除 |
| `kept < 60`、`rejects[excluded/claimed]` | 玩家排除 / 他場佔用 —合理移除 |
| `plot.scan-empty` 再現 | 註冊表歸零；若同時 `accepted=0` 且 `kept` 欄位不存在 = 舊 DLL；若欄位存在但 dropped=60，看 rejects 分類 |
| `coverageAfterScan < 1.000`（載入後第一次掃描） | 水分 fixture 不是全 16，或 BUG-01 取樣問題；不是 BUG-05（看 `diag.day moist`） |
| day2+ `coverageAfterScan` 下降 | **正常**（自然水分衰減 + tank=0.00 不補水）；不是回歸 |
| 6+ 日仍沒有 `harvested plot` | 派工/收穫鏈問題（BUG-04/資料層 territory）：看 `diag.farm.plan candidates/queued`、`gameplay.path-unreachable`、`FarmWorkPlanner` 相關日誌；不是本卡回歸（但若 `registeredNow=60` 仍在，至少證明本卡目標達成） |

**開始狀態注意**：本卡只保留「已註冊 id」。若 round-12 直接載入一個已經被 round-11 洗成
`registeredNow=0` 的存檔（作物仍佔格、沒有 previous id 可保留），重掃不會復活它——那是
「沒有 previous 可保留」的資料狀態，不是本卡邏輯。round-12 必須用 round-11 同一個
**day-1 read-only fixture 存檔**（或全新放置），不要從洗爛後的存檔續跑。

---

## 8. 誠實偏離 / 審查發現處置 / 未做 / 不聲稱

### 8.1 adversarial review（`bug05-reviewer`）主要發現與處置

| 發現 | 嚴重度 | 處置 |
|---|---|---|
| L1 測不到 production fix：刪掉 `Scan.cs` retained branch 仍是全綠（review 在 scratch copy 實證） | MAJOR（proof） | 已加測試 #15（`EmbeddedResource` + source-contract 順序斷言），並用 scratch copy 實證：刪 branch → `PASSED 341 FAILED 1`（§5.2）。controller 無法編進 L1 是架構限制，仍列入 §5.2 的未覆蓋清單 |
| round-12 不可能在同一行同時出現 `kept=60 ... coverageAfterScan=1.000`（fixture 無 HDBF 記錄 → 首掃 kept=0；之後 coverage 自然衰減） | MAJOR（expectation） | 本報告 §6/§7 已改成兩階段預期，並明列 fixture 事實與「不要用單一 grep 誤判」 |
| retained 作物格計入 `_tillablePlots`（面板「可開墾格」語意） | MINOR（cross-card） | 不在本卡改（UI/build 屬 BUILD-01）。BUILD-01 報告 §0/§7.7 已明確接受「retained 已註冊格計入 accepted candidates」的語意；若 Lead 要嚴格「現在可種」語意，建議由 BUILD-01/後續卡改成 `candidates.Count - kept` 或「空的可接受格」計數 |
| 「可達性不重檢」與 `NEXT_ROUND:95-96` 字面不完全一致 | MINOR | 保留不重檢（理由見 §3.2 與 §8.2）；卡細則未要求重檢，且重檢會重引「脆弱過濾器整批清空」風險 |
| `coverageAfterScan` 文化格式可能輸出 `1,000` 破壞 grep | NIT | 已修：Core `DescribeSelectionDelta` invariant + `Diagnostics.cs:83-84` 用 `InvariantCulture` |
| `kept/added` 依 selected、`registeredNow` 依 granted，多農場多幀時可能 `registeredNow < kept+added` | NIT | 既有 claim race（round-11 未觸發）；§6/§7 已列判讀，未新增輸出欄位 |
| `PlotRetention.Retain`/`OutOfRange` 在 production 未被呼叫；`HdbfPlotScanner.cs:47-49` 舊註解 35×35=1225 | NIT | `Retain`/`OutOfRange` 是批次不變量/API 完整（刻意保留，L1 測）；scanner 舊註解屬既有 bug-04 文檔漂移，不在本卡範圍（未動 scanner） |

review 的 mechanism 檢查（inverse drops、`ApplyPlotArrays` identity、snapshot timing、
frame budget、0 Harmony/static/tier）全部通過，無 blocker。

### 8.2 刻意偏離與不聲稱

* **不聲稱「實機已修好」**：本卡只做「依 round-11 日誌定位 → 程式層修復 → L1 證明」；
  round 12 才會有實機證據。
* **刻意不重檢可達性（reachability）— 可達性不是保留格的移除理由**：`docs/NEXT_ROUND.md:95-96`
  與卡開頭的移除理由清單含「unreachable」，但卡的**細則**（「只要通過 地形存在/在範圍內/
  未被玩家排除/未被其他農場搶走 就直接接受，跳過 CanPlant/AnyObjectAt/planting blocker/
  crop-template」）沒有要求重檢；本卡選擇不重檢：作物不是導航障礙、註冊時已通過，重跑導航
  過濾正是「脆弱過濾器整批清空」的風險來源；被圍死的格由 BUG-04 farm plan 逐格跳過
  （身份與取樣保留）。若 Lead 要求嚴格「unreachable 即移除」，建議另開小卡並規定「只有
  明確 unreachable 才丟、查詢例外一律保留」。
* `PlotRetentionState.OutOfRange` flag 存在於 Core（批次/API 完整），但 production 的
  retained 路徑不會設它：超範圍是由「scanner 沒造訪 → 沒有 candidate」自然表達（L1 有測）。
* **未做**：部署、啟動 Timberborn、`build.ps1`、寫存檔、改 `data/` 平衡值、
  `docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、BUG-04 報告、UI、兼容適配器、D-2 補丁、
  灌溉公式。未刪除 `HdbfBoxFarmController.Diagnostics.cs`（Lead 的實機取證管道；
  只在 `diag.scan.end` 新增欄位）。
* **並行卡交互（已核對，無衝突）**：本卡進行中同一工作區有 **BUILD-01**（面板三數字）並行；
  BUILD-01 的 `docs/workstreams/build-01-panel-plots.md` §0/§7.7 已明確接受
  「retained 的已註冊作物格會計入 `TillablePlots = candidates.Count`」的語意（該數值不是
  本卡改的；本卡沒有編輯 UI/build-01 相關檔案）。BUILD-01 於 12:13 用同一棵樹重跑
  `PASSED 340 FAILED 0`；本卡之後又依 adversarial review 在 12:1x 改了 review 處置檔案
  （`PlotScanStats.cs`、`Diagnostics.cs`、`Bug05PlotRetentionTests.cs`、`HDBF.Tests.csproj`），
  追加 2 條契約測試，現為 `PASSED 342 FAILED 0`。
  受保護檔案在卡開始後的 SHA-256 比對：**29/29 不變**（`data/**` 24 檔 +
  `docs/VERIFICATION.md` + `docs/REQ_TRACEABILITY.md` + BUG-04 報告 + BUG-04/高度測試）。
* 本卡沒有新增 static 可變狀態、沒有 Harmony patch、沒有依 tier 分支寫平衡數值。

---

## 9. 驗收對照

| 卡驗收項 | 結果 |
|---|---|
| Core / Gameplay / Tests 編譯 0 error | PASS（各 0 warning / 0 error） |
| L1 全綠（307 + 新增） | PASS：`PASSED 342 FAILED 0` = 307 基線 + BUILD-01 的 20 + 本卡 15；原 307 全綠 |
| 重掃保留已註冊格（核心） | 已實作：`Scan.cs:186-189` → `TryAcceptRetainedPlot`（`:286-337`）跳過 4 個新格 filter；Core `PlotRetention` 單一規則；0 Harmony、無 static 可變狀態 |
| 可觀測性 kept/added/dropped/coverageAfterScan | 已實作：`Diagnostics.cs:81-85`（delta 片段由 Core `PlotScanStats.DescribeSelectionDelta` 產生）、`RecordSelection` 在 `ApplyPlotArrays` 前、`RefreshCoverageFromRegisteredPlots` 後 |
| 不變量 + 測試（60 已註冊 + 全作物重掃仍 60） | 15 條新 L1（§5）；核心整合測試 `:292` 直接重演 round-11；另有 anti-revert source contract（`:398`，實證見 §5.2） |
| 報告（根因/修法/file:line/測試輸出/round-12 預期） | 本檔 |
| 不寫「實機已修好」 | 本檔多處明示「待 round 12」 |
